using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RocketLib.Network;

public static partial class ShortInputRecordsOffline
{
    static partial void RunTimedCases(Harmony harmony)
    {
        foreach (string name in new[] { "InputBitsPatch", "SyncApplicationPatch" })
            harmony.CreateClassProcessor(sync.GetNestedType(name, All)).Patch();
        harmony.Patch(AccessTools.Method(typeof(ReplayCharacter), "CheckInput"),
            transpiler: new HarmonyMethod(sync.GetNestedType("ExecutionPatch", All), "Transpiler"));
        Case("v2-native-il-input-entry-and-full-override-return", TimedIL);
        Case("v2-source-times-actual-state-wire-short-press", TimedWire);
        Case("v2-two-presses-one-state-distinct-input-frames", TimedTwo);
        Case("v2-long-held-release-source-duration", TimedHeld);
        Case("v2-duplicate-out-of-order-and-cumulative-jump", TimedDuplicates);
        Case("v2-selected-is-not-mirror-consumed", TimedSelection);
        Case("v2-ordinary-bit-no-duplicate-other-input-unchanged", TimedOrdinary);
        Case("v2-native-and-derived-filters-not-bypassed", TimedFilters);
        Case("v2-normal-reselection-cancels-pending", TimedReselect);
        Case("v2-capability-missing-unknown-late-peer", TimedCapabilities);
        Case("v2-legacy-switch-cancels-no-reenable", TimedLegacy);
        Case("v2-prior-ordinary-fire-refuses-replay", TimedPriorOrdinary);
        Case("v2-history-gap-bounded-backlog-no-guessed-time", TimedLimits);
        Case("v2-malformed-time-refused", TimedMalformed);
        Case("v2-lifecycle-and-offline-cancel", TimedLifecycle);
    }

    private static void TimedCapability(byte[] value)
    {
        Capability(roster[1], new byte[] { 1 });
        IDictionary manifests = (IDictionary)AccessTools.Field(typeof(ModNegotiation), "playerManifests").GetValue(null);
        ModManifest manifest = ((ModManifest[])manifests[roster[1]])[0];
        if (value != null) manifest.ExtensionData.Add("short-input-timed-execution", value);
    }

    private static State TimedFrame(NID nid, float timestamp, short bits, ulong latest, params float[] times)
    {
        Type type = sync.Assembly.GetType("RocketLib.Network.TimedInputFrame", true);
        object frame = Activator.CreateInstance(type, All, null, new object[] { latest, times }, null);
        byte[] payload = (byte[])type.GetMethod("Encode", All).Invoke(frame, null);
        State state = new State(nid, timestamp, new object[] { bits, payload });
        Set(state, typeof(State), "types", new[] { typeof(short), typeof(byte[]) });
        return state;
    }

    private static ReplayCharacter Mirror(uint nid)
    {
        TimedCapability(new byte[] { 1 });
        return Character<ReplayCharacter>(nid, false);
    }

    private static ShortInputExecutionSnapshot Execution(NetworkObject value)
    {
        ShortInputExecutionSnapshot result;
        Check(ShortInputSync.TryGetExecutionSnapshot(value, out result), "execution snapshot exists");
        return result;
    }

    private static void Step(ProtocolCharacter value, double sourceTime)
    { interp = sourceTime; value.Drive(); }

    private static void TimedIL()
    {
        MethodInfo original = AccessTools.Method(typeof(TestVanDammeAnim), "CheckInput");
        List<CodeInstruction> code = ((IEnumerable<CodeInstruction>)InvokePatch("ExecutionPatch", "Transpiler", PatchProcessor.GetOriginalInstructions(original))).ToList();
        int run = code.FindIndex(i => i.Calls(AccessTools.Method(typeof(NetworkObject), "RunSync")));
        Check(run > 0 && code[run - 1].opcode == OpCodes.Dup && code[run + 1].Calls(sync.GetMethod("ApplyMirrorInput", All)),
            "actual native CheckInput injects immediately after virtual RunSync before later filters");
        Check(code.Count(i => i.Calls(sync.GetMethod("ApplyMirrorInput", All))) == 1, "one native execution entry, not one hook per derived override");
        ilEvidence.Add(new { method=original.ToString(), token=original.MetadataToken, originalBody=Convert.ToBase64String(original.GetMethodBody().GetILAsByteArray()), injection=code.Skip(run-1).Take(3).Select(i=>i.ToString()).ToArray(), limitation="transformed original IL, not full Unity CheckInput execution" });
    }

    private static void TimedWire()
    {
        TimedCapability(new byte[] { 1 });
        ProtocolCharacter source = Character<ProtocolCharacter>(201, true);
        now = 10f; source.Requested = true; source.Drive();
        now = 10.03f; source.Requested = false; source.Drive();
        now = 10.1f;
        State sent = Sample(source);
        byte[] bytes = Bytes(sent);
        byte[] payload = (byte[])sent.Values[1];
        Check(payload[3] == 2 && payload.Length == 21 && payload[12] == 2, "new negotiated payload contains two real consumed source timestamps");
        Check(BitConverter.ToSingle(payload,13)==10f && BitConverter.ToSingle(payload,17)==10.03f, "source hold is encoded, not inferred from sequence parity");
        NID key = source.Nid;
        Reset(); ReplayCharacter mirror = Mirror(201);
        mirror.AddState(Decode(bytes));
        Check(mirror.Nid == key && Snapshot(mirror).ReceivedEdges == 2 && Execution(mirror).MirrorConsumedEdges == 0, "actual UnityStream same full NID arrives without consuming");
        Step(mirror, 10.2);
        Check(mirror.fire && Execution(mirror).MirrorConsumedEdges == 1, "sampled false bit still delivers original short press");
        Step(mirror, 10.21);
        Check(mirror.fire && Execution(mirror).MirrorConsumedEdges == 1, "release waits for encoded source hold, not fixed one-frame pulse");
        Step(mirror, 10.24);
        Check(!mirror.fire && Execution(mirror).MirrorConsumedEdges == 2, "paired release consumed at a later full input return");
        wire.Add(new { kind="RSI-v2", sourceNid=key.ToString(), fullState=Convert.ToBase64String(bytes), payload=Convert.ToBase64String(payload), payloadBytes=payload.Length, framingBytes=5 });
    }

    private static void TimedTwo()
    {
        ReplayCharacter mirror=Mirror(202);
        mirror.AddState(Decode(Bytes(TimedFrame(mirror.Nid,2,0,4,1f,1.01f,1.02f,1.03f))));
        bool[] observed=new bool[4];
        for(int i=0;i<4;i++) { Step(mirror,2+i*0.02); observed[i]=mirror.fire; }
        Check(observed.SequenceEqual(new[]{true,false,true,false}), "two presses in a single false State produce ordered distinct true/false input frames");
        Check(Execution(mirror).MirrorConsumedEdges==4 && Execution(mirror).PendingEdges==0, "all four edges commit exactly once");
    }

    private static void TimedHeld()
    {
        ReplayCharacter mirror=Mirror(203);
        mirror.AddState(TimedFrame(mirror.Nid,1,16,1,1f));
        Step(mirror,2);
        Step(mirror,2.1);
        Check(mirror.fire && Execution(mirror).MirrorConsumedEdges==1, "live held is not compressed to a pulse");
        mirror.AddState(TimedFrame(mirror.Nid,1.6f,0,2,1f,1.5f));
        Step(mirror,2.49);
        Check(mirror.fire, "delayed start shifts a 500ms source hold intact");
        Step(mirror,2.5);
        Check(!mirror.fire && Execution(mirror).LastSourceTime==1.5f && Execution(mirror).LastDeliveryTime==2.5, "release respects encoded 500ms duration and independent clock mapping");
    }

    private static void TimedDuplicates()
    {
        ReplayCharacter mirror=Mirror(204);
        State latest=TimedFrame(mirror.Nid,4,0,4,1f,1.1f,1.2f,1.3f);
        mirror.AddState(latest); mirror.AddState(latest);
        mirror.AddState(TimedFrame(mirror.Nid,3,0,2,1f,1.1f));
        Check(Execution(mirror).PendingEdges==4 && Execution(mirror).ReceivedEdges==4, "duplicate and reordered older frames do not enqueue duplicate edges");
        for(int i=0;i<6;i++) Step(mirror,5+i*0.2);
        Check(Execution(mirror).MirrorConsumedEdges==4 && !mirror.fire, "cumulative jump executes each implied edge only with its received timestamp");
    }

    private static void TimedSelection()
    {
        ReplayCharacter mirror=Mirror(205);
        mirror.AddState(TimedFrame(mirror.Nid,1,16,1,1f));
        mirror.AddState(TimedFrame(mirror.Nid,2,0,2,1f,1.1f));
        interp=3; mirror.RunSync();
        Check(Snapshot(mirror).SelectedEdges==2 && Execution(mirror).MirrorConsumedEdges==0 && !mirror.fire, "selection and LateUpdate do not execute a skipped press");
        Step(mirror,3);
        Check(Snapshot(mirror).SelectedEdges==2 && Execution(mirror).MirrorConsumedEdges==1 && mirror.fire, "complete CheckInput consumes press even though Selected already passed release");
    }

    private static void TimedOrdinary()
    {
        ReplayCharacter mirror=Mirror(206);
        mirror.AddState(TimedFrame(mirror.Nid,1,24,1,1f));
        interp=2; mirror.RunSync();
        Check(mirror.right && !mirror.fire, "ordinary direction is unchanged but fire is suppressed outside consumer");
        Step(mirror,2);
        Check(mirror.fire && mirror.right && !mirror.wasFire, "first true is a consumer edge, not a prior ordinary setter edge");
        mirror.RunSync(); Step(mirror,2.02);
        Check(mirror.fire && mirror.wasFire && Execution(mirror).MirrorConsumedEdges==1, "held and repeated ordinary true do not produce a second rising input");
    }

    private static void TimedFilters()
    {
        ReplayCharacter mirror=Mirror(207); mirror.FilterInput=true;
        mirror.AddState(TimedFrame(mirror.Nid,2,0,2,1f,1.1f));
        Step(mirror,3);
        Check(!mirror.fire && Execution(mirror).MirrorConsumedEdges==1 && !Execution(mirror).FinalFire, "native-style post-RunSync filter clears fire without replay retry or forced tail true");
        Reset(); TimedCapability(new byte[]{1});
        ReplayFilteredCharacter derived=Character<ReplayFilteredCharacter>(208,false);
        derived.AddState(TimedFrame(derived.Nid,2,0,2,1f,1.1f));
        Step(derived,3);
        Check(!derived.fire && Execution(derived).MirrorConsumedEdges==1, "derived InputBits setter filter runs before complete virtual-return commit and is not suppressed");
    }

    private static void TimedReselect()
    {
        ReplayCharacter old=Mirror(209); old.AddState(TimedFrame(old.Nid,2,0,4,1f,1.1f,1.2f,1.3f)); Step(old,3);
        Call("Forget",old);
        ShortInputExecutionSnapshot ignored;
        Check(!ShortInputSync.TryGetExecutionSnapshot(old,out ignored), "destroy/reselect removes old pending execution");
        ReplayCharacter next=Character<ReplayCharacter>(210,false);
        next.AddState(TimedFrame(next.Nid,4,0,2,3f,3.1f)); Step(next,5);
        Check(next.fire && Execution(next).MirrorConsumedEdges==1, "new full NID starts first press, not old pending");
    }

    private static void TimedCapabilities()
    {
        ProtocolCharacter source=Character<ProtocolCharacter>(211,true); source.Requested=true; source.Drive();
        Check(!ShortInputSync.CanExecuteTimed && ((byte[])Sample(source).Values[1])[3]==1, "v1 record peer never consents to execution");
        TimedCapability(new byte[]{2});
        Check(!ShortInputSync.CanExecuteTimed && ((byte[])Sample(source).Values[1])[3]==1, "unknown execution capability keeps byte-compatible RSI v1");
        TimedCapability(new byte[]{1});
        Check(ShortInputSync.CanExecuteTimed && ((byte[])Sample(source).Values[1])[3]==2, "exact execution capability uses new wire version");
        roster=new[]{PID.MyID,new PID(8),new PID(9)};
        Check(!ShortInputSync.CanExecuteTimed && Sample(source).Values.Length==1, "late peer without real manifest keeps original State schema");
    }

    private static void TimedLegacy()
    {
        ReplayCharacter mirror=Mirror(212); mirror.AddState(TimedFrame(mirror.Nid,2,0,2,1f,1.1f)); Step(mirror,3);
        TimedCapability(null); Step(mirror,3.01);
        Check(!Execution(mirror).Active && Execution(mirror).PendingEdges==0, "capability loss cancels pending");
        mirror.AddState(Frame(mirror.Nid,4,16,3)); Step(mirror,5);
        Check(mirror.fire, "ordinary fire resumes without custom forced reset");
        TimedCapability(new byte[]{1}); mirror.AddState(TimedFrame(mirror.Nid,6,0,4,1f,1.1f,4f,4.1f));
        Check(!Execution(mirror).Active, "same NID does not replay old presses after capability recovery");
    }

    private static void TimedPriorOrdinary()
    {
        ReplayCharacter mirror=Character<ReplayCharacter>(213,false);
        mirror.AddState(Frame(mirror.Nid,1,16,1)); Step(mirror,2);
        TimedCapability(new byte[]{1}); mirror.AddState(TimedFrame(mirror.Nid,3,0,2,1f,1.1f));
        Check(!Execution(mirror).Active && Execution(mirror).Cancellation=="ordinary-fire-already-consumed", "late negotiation cannot execute the already ordinary-consumed press again");
    }

    private static void TimedLimits()
    {
        ReplayCharacter mirror=Mirror(214);
        mirror.AddState(TimedFrame(mirror.Nid,10,0,4,3f,4f));
        Check(!Execution(mirror).Active && Execution(mirror).Cancellation=="timestamp-history-gap", "missing timing range refuses guessed durations");
        Reset(); mirror=Mirror(215);
        mirror.AddState(TimedFrame(mirror.Nid,100,0,64,Enumerable.Range(1,64).Select(i=>(float)i).ToArray()));
        mirror.AddState(TimedFrame(mirror.Nid,101,16,65,Enumerable.Range(2,64).Select(i=>(float)i).ToArray()));
        Check(!Execution(mirror).Active && Execution(mirror).PendingEdges==0, "bounded pending overflow cancels instead of unbounded replay or dropped press success");
    }

    private static void TimedMalformed()
    {
        ReplayCharacter mirror=Mirror(216);
        foreach(float[] times in new[]{new[]{float.NaN},new[]{float.PositiveInfinity},new[]{-1f},new[]{2f},new[]{1f,0.5f}})
            mirror.AddState(TimedFrame(mirror.Nid,1,0,(ulong)times.Length,times));
        Check(((List<State>)AccessTools.Field(typeof(NetworkObject),"states").GetValue(mirror)).Count==0, "nonfinite future negative or decreasing edge time dropped before native State queue");
        Check(Execution(mirror).ReceivedEdges==0, "malformed payload cannot advance timed receive watermark");
    }

    private static void TimedLifecycle()
    {
        ReplayCharacter mirror=Mirror(217); mirror.AddState(TimedFrame(mirror.Nid,2,0,2,1f,1.1f)); Step(mirror,3);
        offline=true; Step(mirror,3.1);
        Check(!Execution(mirror).Active && Execution(mirror).PendingEdges==0, "offline cancels queued replay");
        Reset(); mirror=Mirror(218); mirror.AddState(TimedFrame(mirror.Nid,2,0,2,1f,1.1f));
        Call("PlayerLeft",mirror.Owner);
        ShortInputExecutionSnapshot ignored;
        Check(!ShortInputSync.TryGetExecutionSnapshot(mirror,out ignored), "peer leave removes exact owner's replay");
    }
}

public class ReplayCharacter : ProtocolCharacter
{
    public bool FilterInput;
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected override void CheckInput()
    {
        wasFire=fire;
        RunSync();
        if(FilterInput) fire=false;
    }
}
public sealed class ReplayFilteredCharacter : ReplayCharacter
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected override void CheckInput() { base.CheckInput(); InputBits=0; }
}
