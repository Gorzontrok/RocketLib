using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using RocketLib.Network;

// Runs in its own CLR process. Engine/connection properties below are deterministic fixtures,
// not Unity runtime evidence. Actual State, TypeSerializer, AddState, interpolation and patches run.
public static class ShortInputRecordsBootstrap
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string[] directories = args.Take(4).ToArray();
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
        {
            string filename = new AssemblyName(request.Name).Name + ".dll";
            foreach (string directory in directories)
            {
                string path = Path.Combine(directory, filename);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        try
        {
            return (int)Assembly.GetExecutingAssembly().GetType("ShortInputRecordsOffline")
                .GetMethod("Run").Invoke(null, new object[] { args[4] });
        }
        catch (Exception ex)
        {
            while (ex != null)
            {
                Console.WriteLine(ex.GetType().FullName + ": " + ex.Message);
                ex = ex.InnerException;
            }
            return 2;
        }
    }
}

public static partial class ShortInputRecordsOffline
{
    static partial void RunTimedCases(Harmony harmony);
    private static readonly List<object> cases = new List<object>();
    private static readonly List<object> assertions = new List<object>();
    private static readonly List<object> wire = new List<object>();
    private static readonly List<object> ilEvidence = new List<object>();
    private static readonly HashSet<NetworkObject> mine = new HashSet<NetworkObject>();
    private static readonly Type sync = typeof(ShortInputSync);
    private static readonly Type ledgerType = sync.Assembly.GetType("RocketLib.Network.ShortInputLedger", true);
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static PID[] roster;
    private static float now;
    private static double interp;
    private static bool offline;
    private static int failures;
    private static string caseId;
    private static Action<NetworkObject> runBaseSync;
    private delegate object Fallback(NetworkObject instance, object prev, object next, bool interpolate, float lerp);
    private static Fallback fallback;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string resultPath)
    {
        Harmony harmony = new Harmony("rocketlib.short-input.offline");
        string fatal = null;
        try
        {
            Utils.IsRunningInsideOfUnity = false; // skips Unity-only AOT warm-up; no serializer is replaced
            PatchBoundary(harmony, AccessTools.PropertyGetter(typeof(Connect), "IsOffline"), "Offline");
            PatchBoundary(harmony, AccessTools.PropertyGetter(typeof(Connect), "LayerExists"), "LayerExists");
            PatchBoundary(harmony, AccessTools.PropertyGetter(typeof(Connect), "IsRichardsEditor"), "Editor");
            PatchBoundary(harmony, AccessTools.PropertyGetter(typeof(Connect), "Timef"), "Clock");
            PatchBoundary(harmony, AccessTools.Method(typeof(Connect), "GetInterpTime"), "InterpolationClock");
            PatchBoundary(harmony, AccessTools.PropertyGetter(typeof(NetworkObject), "Nid"), "Identity");
            PatchBoundary(harmony, AccessTools.PropertyGetter(typeof(NetworkObject), "IsMine"), "Ownership");
            PatchBoundary(harmony, AccessTools.Method(typeof(ModNegotiation), "GetAllPlayers"), "Roster");
            foreach (Type type in sync.GetNestedTypes(All))
                if (new[] { "StatePatch", "ReceivePatch", "SelectionPatch", "OwnerPatch" }.Contains(type.Name))
                    harmony.CreateClassProcessor(type).Patch();
            fallback = CompileActualFallback();
            harmony.Patch(AccessTools.Method(typeof(NetworkObject), "GetInterpolatedValues"),
                prefix: new HarmonyMethod(typeof(ShortInputRecordsOffline), "ShortPropertiesOnly"));
            harmony.Patch(AccessTools.Method(typeof(ProtocolCharacter), "Drive"), transpiler: new HarmonyMethod(typeof(ShortInputRecordsOffline), "FixtureCapture"));
            runBaseSync = value => value.RunSync();

            Case("actual-il-and-virtual-filter", TestCapture);
            Case("two-consumed-presses-before-sampling", TestTwoPresses);
            Case("actual-state-wire-and-capability-gate", TestWire);
            Case("native-selection-skips-true", TestSkip);
            Case("single-fallback-threshold-and-repeat", TestThreshold);
            Case("new-instance-and-pending-cancellation", TestLifecycle);
            Case("nid-reuse-and-live-collision-refused", TestReuse);
            Case("owner-change-and-sync-disable", TestOwnerAndSync);
            Case("malformed-state-extension-refused", TestMalformed);
            Case("legacy-and-extended-states-can-coexist", TestMixed);
            Case("record-before-hero-ready", TestHeroSetup);
            Case("offline-and-no-consumed-request", TestOffline);
            Case("sampling-exception-does-not-leak-schema", TestSamplingFailure);
            RunTimedCases(harmony);
        }
        catch (Exception ex) { failures++; fatal = Unwrap(ex).ToString(); }
        finally { harmony.UnpatchAll("rocketlib.short-input.offline"); }
        object results = new
        {
            kind = "native-clr-offline-not-gameplay",
            passed = failures == 0,
            failures,
            assertionCount = assertions.Count,
            cases,
            assertions,
            wire,
            ilEvidence,
            fatal,
            assemblies = new[] { typeof(ShortInputSync).Assembly, typeof(State).Assembly, typeof(Harmony).Assembly }
                .Select(a => new { name = a.GetName().Name, path = a.Location, mvid = a.ManifestModule.ModuleVersionId.ToString() }).ToArray(),
            fixtures = new[] { "Connect offline/layer/editor/time/interpolation getters", "NetworkObject identity/ownership getters", "peer roster; actual ModManifest dictionaries", "uninitialized character with synthetic Unity pointer, managed reference hashing and one real InputBits property", "fixture overrides position update to avoid native transform writes", "GetInterpolatedValues loop fixture calls an executable copy of the actual game's final Int16 fallback IL; original ECall arithmetic method cannot be JIT-compiled outside Unity", "Unity AOT warm-up skipped", "lifecycle cleanup function invoked directly; no Unity destroy loop" },
            notTested = new[] { "full game Update/CheckInput patch generation requires Unity ECall runtime; original IL is transformed and checked instead", "game Update execution and Unity scheduling", "Steam transport or online delivery", "real peer negotiation callbacks", "actual Unity destruction/scene teardown", "timed replay in the full Unity game, source duration fidelity under network/frame jitter, or bullet equality" }
        };
        File.WriteAllText(resultPath, JsonConvert.SerializeObject(results, Formatting.Indented), new UTF8Encoding(false));
        Console.WriteLine("cases=" + cases.Count + " assertions=" + assertions.Count + " failures=" + failures);
        if (fatal != null) Console.WriteLine(fatal);
        return failures == 0 ? 0 : 1;
    }

    private static void Case(string id, Action body)
    {
        caseId = id;
        Reset();
        try { body(); cases.Add(new { id, passed = true }); }
        catch (Exception ex) { failures++; cases.Add(new { id, passed = false, error = Unwrap(ex).ToString() }); }
    }

    private static void Check(bool condition, string name)
    {
        assertions.Add(new { caseId, name, passed = condition });
        if (!condition) throw new InvalidOperationException(name);
    }

    private static Exception Unwrap(Exception ex)
    { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; return ex; }

    private static object Call(string name, params object[] args)
    { return sync.GetMethod(name, All).Invoke(null, args); }
    private static object LedgerCall(object ledger, string name, params object[] args)
    { return ledgerType.GetMethod(name, All).Invoke(ledger, args); }
    private static object Ledger() { return Activator.CreateInstance(ledgerType, true); }
    private static ulong Number(object ledger, string field) { return (ulong)ledgerType.GetField(field, All).GetValue(ledger); }
    private static void Set(object obj, Type declaring, string name, object value)
    {
        FieldInfo field = AccessTools.Field(declaring, name);
        if (field == null) throw new MissingFieldException(declaring.FullName, name);
        field.SetValue(obj, value);
    }
    private static object InvokePatch(string nested, string method, params object[] args)
    { return sync.GetNestedType(nested, All).GetMethod(method, All).Invoke(null, args); }

    private static void Reset()
    {
        Call("EndSession");
        Set(null, sync, "initialized", true);
        mine.Clear(); now = 100f; interp = 0; offline = false;
        roster = new[] { PID.MyID, new PID(8) };
        IDictionary manifests = (IDictionary)AccessTools.Field(typeof(ModNegotiation), "playerManifests").GetValue(null);
        manifests.Clear();
        Capability(roster[1], new byte[] { 1 });
    }

    private static void Capability(PID peer, byte[] value)
    {
        ModManifest manifest = new ModManifest("RocketLib", "offline-fixture");
        if (value != null) manifest.ExtensionData.Add("short-input-records", value);
        IDictionary manifests = (IDictionary)AccessTools.Field(typeof(ModNegotiation), "playerManifests").GetValue(null);
        manifests[peer] = new[] { manifest };
    }

    private static T Character<T>(uint nid, bool local) where T : ProtocolCharacter
    {
        T value = (T)FormatterServices.GetUninitializedObject(typeof(T));
        Set(value, typeof(UnityEngine.Object), "m_CachedPtr", new IntPtr(1));
        Set(value, typeof(NetworkObject), "nid", new NID(new SID(7), PID.MyID, nid));
        Set(value, typeof(NetworkObject), "owner", PID.MyID);
        Set(value, typeof(NetworkObject), "syncronize", true);
        Set(value, typeof(NetworkObject), "props", new[] { typeof(TestVanDammeAnim).GetProperty("InputBits") });
        Set(value, typeof(NetworkObject), "PropsToTryInterpolate", new[] { true });
        Set(value, typeof(NetworkObject), "states", new List<State>());
        Set(value, typeof(NetworkedUnit), "isHero", true);
        if (local) mine.Add(value);
        return value;
    }

    private static ShortInputSnapshot Snapshot(NetworkObject value)
    {
        ShortInputSnapshot result;
        Check(ShortInputSync.TryGetSnapshot(value, out result), "snapshot exists");
        return result;
    }

    private static State Sample(ProtocolCharacter source)
    {
        InvokePatch("SamplingPatch", "Prefix");
        try { return new State(source.Nid, source); }
        finally { InvokePatch("SamplingPatch", "Finalizer", new object[] { null }); }
    }

    private static State Frame(NID nid, float timestamp, short bits, ulong sequence)
    {
        State state = new State(nid, timestamp, new object[] { bits, Call("Encode", sequence) });
        Set(state, typeof(State), "types", new Type[] { typeof(short), typeof(byte[]) });
        return state;
    }

    private static byte[] Bytes(State state)
    { UnityStream stream = new UnityStream(); stream.Serialize<State>(state); return stream.ByteArray; }
    private static State Decode(byte[] bytes) { return (State)new UnityStream(bytes).DeserializeNext(); }

    private static void TestCapture()
    {
        MethodInfo update = AccessTools.Method(typeof(TestVanDammeAnim), "Update");
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(update);
        List<CodeInstruction> il = ((IEnumerable<CodeInstruction>)InvokePatch("CapturePatch", "Transpiler", original)).ToList();
        MethodInfo check = AccessTools.Method(typeof(TestVanDammeAnim), "CheckInput");
        MethodInfo record = sync.GetMethod("RecordConsumed", All);
        int index = il.FindIndex(i => i.Calls(check));
        Check(index > 0 && il[index - 1].opcode == OpCodes.Dup && il[index + 1].Calls(record), "actual Update wraps virtual CheckInput immediately after return");
        Check(il.Count(i => i.Calls(record)) == 1, "exactly one capture in transformed current game Update IL");
        ilEvidence.Add(new { method = update.ToString(), token = update.MetadataToken, originalBody = Convert.ToBase64String(update.GetMethodBody().GetILAsByteArray()), transformedCall = il.Skip(index - 1).Take(3).Select(i => i.ToString()).ToArray() });
        ProtocolCharacter normal = Character<ProtocolCharacter>(11, true);
        normal.Requested = true; normal.Drive(); normal.Drive();
        Check(Snapshot(normal).ConsumedEdges == 1 && normal.After == 16, "held counted once and character fire unchanged");
        normal.Requested = false; normal.Drive();
        Check(Snapshot(normal).ConsumedEdges == 2 && normal.After == 0, "normal release counted without delay");
        FilteredCharacter filtered = Character<FilteredCharacter>(12, true);
        filtered.Requested = true; filtered.Drive();
        Check(Snapshot(filtered).ConsumedEdges == 0 && filtered.After == 0, "derived CheckInput filter runs before actual capture");
        bool rejected = false;
        try { ((IEnumerable<CodeInstruction>)Call("WrapCheckInput", new CodeInstruction[0], check, record)).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "missing actual callsite fails patch instead of advertising coverage");
    }

    private static void TestTwoPresses()
    {
        ProtocolCharacter source = Character<ProtocolCharacter>(21, true);
        foreach (bool held in new[] { true, false, true, false }) { source.Requested = held; source.Drive(); }
        State state = Sample(source);
        object received = Ledger();
        State record = (State)Call("ReadRecord", state, 1, received);
        Call("ObserveSelection", received, record, null, 0f);
        Check(Snapshot(source).ConsumedEdges == 4 && Number(received, "Selected") == 4, "one sampled false bit retains two distinct press/release pairs");
        Check((short)record.Values[0] == 0 && source.InputBits == 0, "record observation does not manufacture held gameplay input");
    }

    private static void TestWire()
    {
        ProtocolCharacter source = Character<ProtocolCharacter>(31, true);
        source.Requested = true; source.Drive(); source.Requested = false; source.Drive();
        State extended = Sample(source);
        byte[] extendedBytes = Bytes(extended);
        State decoded = Decode(extendedBytes);
        Check(decoded.Key == source.Nid && decoded.TimeStamp == now, "actual UnityStream preserves NID and timestamp");
        Check(decoded.Values.Length == 2 && (short)decoded.Values[0] == 0, "actual State serializer carries original short plus record");
        object ledger = Ledger();
        State stripped = (State)Call("ReadRecord", decoded, 1, ledger);
        Check(stripped.Values.Length == 1 && Number(ledger, "Received") == 2, "extension stripped before original Props application");
        Capability(roster[1], null);
        Check(!ShortInputSync.CanTransmitRecords, "missing capability explicitly disables record transmission");
        State legacy = Sample(source);
        byte[] legacyBytes = Bytes(legacy);
        Check(legacy.Values.Length == 1 && legacy.Types.Length == 1, "missing capability leaves old State schema intact");
        Check(extendedBytes.Length - legacyBytes.Length == 17, "actual byte overhead is 12 payload + 5 type/length framing");
        Capability(roster[1], new byte[] { 2 });
        Check(Bytes(Sample(source)).SequenceEqual(legacyBytes), "unknown version produces byte-identical legacy packet");
        Capability(roster[1], new byte[] { 1 });
        roster = new[] { PID.MyID, new PID(8), new PID(9) };
        Check(!ShortInputSync.CanTransmitRecords && Sample(source).Values.Length == 1, "late peer with no manifest keeps whole batch legacy");
        Capability(roster[2], new byte[] { 1 });
        Check(ShortInputSync.CanTransmitRecords && Sample(source).Values.Length == 2, "all peers reporting v1 enables record extension");
        roster = new[] { PID.MyID };
        Check(!ShortInputSync.CanTransmitRecords, "empty remote roster does not claim network capability");
        wire.Add(new { legacyBytes = legacyBytes.Length, extendedBytes = extendedBytes.Length, payload = Convert.ToBase64String((byte[])extended.Values[1]), fullState = Convert.ToBase64String(extendedBytes) });
    }

    private static void TestSkip()
    {
        ProtocolCharacter mirror = Character<ProtocolCharacter>(41, false);
        mirror.AddState(Decode(Bytes(Frame(mirror.Nid, 0, 0, 0))));
        mirror.AddState(Decode(Bytes(Frame(mirror.Nid, 100, 16, 1))));
        mirror.AddState(Decode(Bytes(Frame(mirror.Nid, 200, 0, 2))));
        Check(Snapshot(mirror).ReceivedEdges == 2 && Snapshot(mirror).SelectedEdges == 0, "arrival distinguished from selection");
        interp = 250; runBaseSync(mirror);
        Check(Snapshot(mirror).SelectedEdges == 2 && mirror.InputBits == 0, "actual RunSync skips true but selected cumulative record retains press and release");
        runBaseSync(mirror); mirror.InputBits = 16; mirror.InputBits = 0;
        Check(Snapshot(mirror).SelectedEdges == 2 && Snapshot(mirror).ConsumedEdges == 0, "repeated selection and auxiliary setters do not consume or duplicate edges");
    }

    private static void TestThreshold()
    {
        ProtocolCharacter mirror = Character<ProtocolCharacter>(51, false);
        mirror.AddState(Frame(mirror.Nid, 0, 0, 0));
        mirror.AddState(Frame(mirror.Nid, 100, 16, 1));
        interp = 50; runBaseSync(mirror);
        Check(Snapshot(mirror).SelectedEdges == 0 && mirror.InputBits == 0, "exact Single lerp 0.5 chooses prev");
        interp = 50.001; runBaseSync(mirror);
        Check(Snapshot(mirror).SelectedEdges == 1 && mirror.InputBits == 16, "above 0.5 chooses next for bit and record");
        mirror.AddState(Frame(mirror.Nid, 200, 0, 2)); interp = 250; runBaseSync(mirror);
        Check(Snapshot(mirror).SelectedEdges == 2 && mirror.InputBits == 0, "subsequent release selected normally");
    }

    private static void TestLifecycle()
    {
        ProtocolCharacter old = Character<ProtocolCharacter>(61, true);
        old.Requested = true; old.Drive();
        object oldTrack = Call("GetTrack", old);
        object oldLedger = AccessTools.Field(oldTrack.GetType(), "Ledger").GetValue(oldTrack);
        State oldRecord = (State)Call("ReadRecord", Frame(old.Nid, 100, 16, 1), 1, oldLedger);
        Call("Forget", old);
        ShortInputSnapshot ignored;
        Check(!ShortInputSync.TryGetSnapshot(old, out ignored), "old pending/held record cancelled on lifecycle cleanup");
        ProtocolCharacter next = Character<ProtocolCharacter>(62, true);
        next.Requested = true; next.Drive();
        Check(Snapshot(next).ConsumedEdges == 1, "normal new NID starts independent first press");
        object newTrack = Call("GetTrack", next);
        object newLedger = AccessTools.Field(newTrack.GetType(), "Ledger").GetValue(newTrack);
        Call("ObserveSelection", newLedger, oldRecord, null, 0f);
        Check(Number(newLedger, "Selected") == 0, "old immutable State token cannot advance new instance ledger");
        next.AddState(Frame(old.Nid, 200, 0, 2));
        Check(((List<State>)AccessTools.Field(typeof(NetworkObject), "states").GetValue(next)).Count == 0, "wrong-NID State rejected at AddState boundary");
    }

    private static void TestReuse()
    {
        ProtocolCharacter old = Character<ProtocolCharacter>(71, true);
        old.Requested = true; old.Drive(); Call("Forget", old);
        ProtocolCharacter reused = Character<ProtocolCharacter>(71, true);
        reused.Requested = true; reused.Drive();
        ShortInputSnapshot ignored;
        Check(!ShortInputSync.TryGetSnapshot(reused, out ignored), "retired NID reuse refuses to reset sequence without an epoch");
        ProtocolCharacter first = Character<ProtocolCharacter>(72, true);
        first.Requested = true; first.Drive();
        ProtocolCharacter collision = Character<ProtocolCharacter>(72, true);
        collision.Requested = true; collision.Drive();
        Check(!ShortInputSync.TryGetSnapshot(first, out ignored) && !ShortInputSync.TryGetSnapshot(collision, out ignored), "two live instances with same NID cancel ambiguous recording");
        Call("EndSession");
        reused.Drive();
        Check(Snapshot(reused).ConsumedEdges == 1, "confirmed session end clears retired identities");
    }

    private static void TestOwnerAndSync()
    {
        ProtocolCharacter value = Character<ProtocolCharacter>(81, true);
        value.Requested = true; value.Drive(); value.SetOwner(new PID(9));
        ShortInputSnapshot ignored;
        Check(!ShortInputSync.TryGetSnapshot(value, out ignored), "actual SetOwner patch cancels old ledger");
        value.Drive();
        Check(!ShortInputSync.TryGetSnapshot(value, out ignored), "same-NID owner migration does not silently reuse zero baseline");
        ProtocolCharacter disabled = Character<ProtocolCharacter>(82, true);
        disabled.Requested = true; disabled.Drive();
        InvokePatch("SyncPatch", "Prefix", disabled, false);
        Set(disabled, typeof(NetworkObject), "syncronize", false);
        Check(!ShortInputSync.TryGetSnapshot(disabled, out ignored), "sync disable cancels recording");
        Set(disabled, typeof(NetworkObject), "syncronize", true); disabled.Drive();
        Check(!ShortInputSync.TryGetSnapshot(disabled, out ignored), "reenable on retired NID requires a future epoch design");
    }

    private static void TestMalformed()
    {
        ProtocolCharacter mirror = Character<ProtocolCharacter>(91, false);
        byte[] wrongVersion = (byte[])Call("Encode", 2UL); wrongVersion[3] = 2;
        foreach (object[] values in new[] { new object[] { (short)0, new byte[2] }, new object[] { (short)0, wrongVersion }, new object[] { (short)0, 2UL }, new object[] { (short)0, new byte[12], 7 } })
        {
            mirror.AddState(new State(mirror.Nid, 100, values));
            Check(((List<State>)AccessTools.Field(typeof(NetworkObject), "states").GetValue(mirror)).Count == 0, "invalid extension dropped before queue/apply");
        }
        mirror.AddState(Frame(mirror.Nid, 100, 0, 2));
        Check(Snapshot(mirror).ReceivedEdges == 2, "valid State remains usable after malformed input");
    }

    private static void TestMixed()
    {
        ProtocolCharacter mirror = Character<ProtocolCharacter>(101, false);
        mirror.AddState(new State(mirror.Nid, 0, new object[] { (short)0 }));
        mirror.AddState(Frame(mirror.Nid, 100, 0, 4));
        mirror.AddState(new State(mirror.Nid, 200, new object[] { (short)16 }));
        interp = 100; runBaseSync(mirror);
        Check(Snapshot(mirror).SelectedEdges == 4 && mirror.InputBits == 0, "legacy-to-record transition preserves Props length");
        interp = 250; runBaseSync(mirror);
        Check(Snapshot(mirror).SelectedEdges == 4 && mirror.InputBits == 16, "record-to-legacy transition preserves original held behavior and known watermark");
    }

    private static void TestOffline()
    {
        ProtocolCharacter source = Character<ProtocolCharacter>(111, true);
        // A requested pulse that never enters Drive/CheckInput cannot create a record.
        source.Requested = true; source.Requested = false; source.Drive();
        Check(Snapshot(source).ConsumedEdges == 0, "unconsumed timer pulse is outside network record contract");
        offline = true;
        source.Requested = true; source.Drive();
        Check(Snapshot(source).ConsumedEdges == 0 && !ShortInputSync.CanTransmitRecords && Sample(source).Values.Length == 1, "offline mode emits no network records");
    }

    private static void TestHeroSetup()
    {
        ProtocolCharacter mirror = Character<ProtocolCharacter>(112, false);
        Set(mirror, typeof(NetworkedUnit), "isHero", false);
        mirror.AddState(Frame(mirror.Nid, 100, 0, 2));
        List<State> states = (List<State>)AccessTools.Field(typeof(NetworkObject), "states").GetValue(mirror);
        Check(states.Count == 1 && states[0].Values.Length == 1, "wire extension stripped even before hero setup");
        ShortInputSnapshot ignored;
        Check(!ShortInputSync.TryGetSnapshot(mirror, out ignored), "ineligible object does not create recording ledger");
        Set(mirror, typeof(NetworkedUnit), "isHero", true);
        mirror.AddState(Frame(mirror.Nid, 200, 0, 4));
        interp = 250; runBaseSync(mirror);
        Check(Snapshot(mirror).SelectedEdges == 4 && mirror.InputBits == 0, "later cumulative State recovers records after hero setup");
    }

    private static void TestSamplingFailure()
    {
        ProtocolCharacter source = Character<ProtocolCharacter>(121, true);
        source.Drive();
        InvokePatch("SamplingPatch", "Prefix");
        Exception failure = new InvalidOperationException("fixture serialization failure");
        Check(ReferenceEquals(InvokePatch("SamplingPatch", "Finalizer", failure), failure), "sampling finalizer preserves original exception");
        Check(new State(source.Nid, source).Values.Length == 1, "extension scope cleared after failed sampling");
    }

    private static void PatchBoundary(Harmony harmony, MethodInfo method, string prefix)
    {
        if (method == null) throw new MissingMethodException(prefix);
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(ShortInputRecordsOffline), prefix));
    }
    private static bool Offline(ref bool __result) { __result = offline; return false; }
    private static bool LayerExists(ref bool __result) { __result = true; return false; }
    private static bool Editor(ref bool __result) { __result = false; return false; }
    private static bool Clock(ref float __result) { __result = now; return false; }
    private static bool InterpolationClock(ref double __result) { __result = interp; return false; }
    private static bool Identity(NetworkObject __instance, ref NID __result) { __result = (NID)AccessTools.Field(typeof(NetworkObject), "nid").GetValue(__instance); return false; }
    private static bool Ownership(NetworkObject __instance, ref bool __result) { __result = mine.Contains(__instance); return false; }
    private static bool Roster(ref PID[] __result) { __result = roster; return false; }
    private static Fallback CompileActualFallback()
    {
        MethodInfo original = AccessTools.Method(typeof(NetworkObject), "TryInterpolateValue");
        List<CodeInstruction> code = PatchProcessor.GetOriginalInstructions(original);
        int threshold = code.FindLastIndex(i => i.opcode == OpCodes.Ldc_R4 && Equals(i.operand, 0.5f));
        if (threshold < 1) throw new InvalidOperationException("Actual game fallback threshold not found");
        List<CodeInstruction> tail = code.Skip(threshold - 1).ToList();
        DynamicMethod method = new DynamicMethod("ActualInt16Fallback", typeof(object),
            new[] { typeof(NetworkObject), typeof(object), typeof(object), typeof(bool), typeof(float) }, true);
        ILGenerator generator = method.GetILGenerator();
        Dictionary<Label, Label> labels = new Dictionary<Label, Label>();
        foreach (CodeInstruction instruction in tail)
            foreach (Label label in instruction.labels) labels[label] = generator.DefineLabel();
        foreach (CodeInstruction instruction in tail)
        {
            foreach (Label label in instruction.labels) generator.MarkLabel(labels[label]);
            object operand = instruction.operand;
            if (operand == null) generator.Emit(instruction.opcode);
            else if (operand is Label) generator.Emit(instruction.opcode, labels[(Label)operand]);
            else if (operand is float) generator.Emit(instruction.opcode, (float)operand);
            else if (operand is byte) generator.Emit(instruction.opcode, (byte)operand);
            else if (operand is short) generator.Emit(instruction.opcode, (short)operand);
            else if (operand is int) generator.Emit(instruction.opcode, (int)operand);
            else throw new InvalidOperationException("Unsupported actual fallback IL: " + instruction);
        }
        ilEvidence.Add(new { method = original.ToString(), token = original.MetadataToken, originalBody = Convert.ToBase64String(original.GetMethodBody().GetILAsByteArray()), extractedTail = tail.Select(i => i.ToString()).ToArray() });
        return (Fallback)method.CreateDelegate(typeof(Fallback));
    }

    private static bool ShortPropertiesOnly(NetworkObject __instance, State prev, State next, float lerp, ref object[] __result)
    {
        if (next == null) __result = prev.Values;
        else if (prev == null) __result = next.Values;
        else
        {
            __result = new object[prev.Values.Length];
            for (int i = 0; i < __instance.Props.Length; i++)
            {
                if (!(prev.Values[i] is short) || !(next.Values[i] is short)) throw new InvalidOperationException("Int16-only fixture");
                __result[i] = fallback(__instance, prev.Values[i], next.Values[i], true, lerp);
            }
        }
        return false;
    }
    private static IEnumerable<CodeInstruction> FixtureCapture(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        MethodInfo target = code.Select(i => i.operand as MethodInfo).Single(m => m != null && m.Name == "CheckInput");
        return (IEnumerable<CodeInstruction>)Call("WrapCheckInput", code, target, sync.GetMethod("RecordConsumed", All));
    }
}

public class ProtocolCharacter : TestVanDammeAnim
{
    public bool Requested;
    public short After;
    public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
    public override bool Equals(object other) { return ReferenceEquals(this, other); }
    public override void SetPosition() { }
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected override void CheckInput() { InputBits = Requested ? (short)16 : (short)0; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Drive() { CheckInput(); After = InputBits; }
}
public sealed class FilteredCharacter : ProtocolCharacter
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected override void CheckInput() { base.CheckInput(); InputBits = 0; }
}
