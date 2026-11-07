using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RocketLib.Network
{
    /// <summary>Record watermarks only. These values never drive character input or count bullets.</summary>
    public struct ShortInputSnapshot
    {
        public readonly ulong ConsumedEdges;
        public readonly ulong ReceivedEdges;
        public readonly ulong SelectedEdges;

        internal ShortInputSnapshot(ulong consumed, ulong received, ulong selected)
        {
            ConsumedEdges = consumed;
            ReceivedEdges = received;
            SelectedEdges = selected;
        }
    }

    // False baseline; odd edges are presses, even edges are releases. No timing is encoded.
    internal sealed class ShortInputLedger
    {
        private bool held;
        internal ulong Consumed;
        internal ulong Received;
        internal ulong Selected;

        internal void Consume(bool value)
        {
            if (value == held) return;
            Consumed = checked(Consumed + 1);
            held = value;
        }

        internal void Receive(ulong value) { if (value > Received) Received = value; }
        internal void Select(ulong value) { if (value > Selected) Selected = value; }
        internal ShortInputSnapshot Snapshot { get { return new ShortInputSnapshot(Consumed, Received, Selected); } }
    }

    /// <summary>
    /// Observes ordinary fire after Update's virtual CheckInput call. Requires continuous sync,
    /// stable owner and a unique full NID for each object lifetime. Timed replay requires its own capability.
    /// </summary>
    public static class ShortInputSync
    {
        internal const string Capability = "short-input-records";
        internal const string ExecutionCapability = "short-input-timed-execution";
        private static bool initialized;
        private static bool sampling;
        private static bool timedSampling;
        private static readonly Dictionary<NetworkObject, Track> tracks = new Dictionary<NetworkObject, Track>();
        private static readonly Dictionary<NID, NetworkObject> instances = new Dictionary<NID, NetworkObject>();
        private static readonly HashSet<NID> retired = new HashSet<NID>();

        private sealed class Track
        {
            internal NID Key;
            internal PID Owner;
            internal readonly ShortInputLedger Ledger = new ShortInputLedger();
            internal readonly TimedInputHistory History = new TimedInputHistory();
            internal readonly TimedInputReplay Replay = new TimedInputReplay();
            internal bool Checking;
            internal bool ApplyingSync;
        }

        // State owns its immutable metadata, so discarded states need no global side-table.
        internal sealed class RecordedState : State
        {
            internal readonly ShortInputLedger Ledger;
            internal readonly ulong Sequence;
            internal RecordedState(State original, object[] values, ShortInputLedger ledger, ulong sequence)
                : base(original.Key, original.TimeStamp, values)
            {
                Ledger = ledger;
                Sequence = sequence;
            }
        }

        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            ModNegotiation.OnSessionEnded += EndSession;
            ModNegotiation.OnPlayerLeft += PlayerLeft;
            ModNegotiation.SetExtensionData("RocketLib", Capability, new byte[] { 1 });
            ModNegotiation.SetExtensionData("RocketLib", ExecutionCapability, new byte[] { 1 });
        }

        public static bool TryGetExecutionSnapshot(NetworkObject obj, out ShortInputExecutionSnapshot snapshot)
        {
            ShortInputSnapshot records;
            snapshot = new ShortInputExecutionSnapshot();
            if (!TryGetSnapshot(obj, out records)) return false;
            snapshot = new ShortInputExecutionSnapshot(tracks[obj].Replay);
            return true;
        }

        public static bool TryGetSnapshot(NetworkObject obj, out ShortInputSnapshot snapshot)
        {
            Track track;
            snapshot = new ShortInputSnapshot();
            if (obj == null || !tracks.TryGetValue(obj, out track)) return false;
            if (!obj.Syncronize || obj.Nid != track.Key || obj.Owner != track.Owner)
            {
                Forget(obj);
                return false;
            }
            snapshot = track.Ledger.Snapshot;
            return true;
        }

        private static Track GetTrack(NetworkObject obj)
        {
            if (Connect.IsOffline)
            {
                Track offlineTrack;
                if (tracks.TryGetValue(obj, out offlineTrack)) offlineTrack.Replay.Cancel("offline");
                return null;
            }
            if (!initialized || !obj.Syncronize || obj.Nid == NID.NoID)
            { Forget(obj); return null; }
            Track track;
            if (tracks.TryGetValue(obj, out track))
            {
                if (track.Key == obj.Nid && track.Owner == obj.Owner) return track;
                Forget(obj);
            }
            // Reusing an NID or migrating its owner needs an explicit epoch protocol, not a reset to zero.
            if (retired.Contains(obj.Nid)) return null;
            NetworkObject previous;
            if (instances.TryGetValue(obj.Nid, out previous) && !ReferenceEquals(previous, obj))
            {
                Forget(previous);
                return null;
            }
            track = new Track { Key = obj.Nid, Owner = obj.Owner };
            tracks.Add(obj, track);
            instances.Add(track.Key, obj);
            return track;
        }

        internal static void RecordConsumed(TestVanDammeAnim character)
        {
            if (!character.IsHero) return;
            if (!character.Syncronize) { Forget(character); return; }
            Track track = GetTrack(character);
            if (track == null) return;
            if (character.IsMine)
            {
                track.Ledger.Consume(character.fire);
                track.History.Capture(track.Ledger.Consumed, Connect.Timef);
            }
            else
            {
                track.Replay.Complete(character.fire);
                track.Checking = false;
            }
        }

        internal static void BeginInput(TestVanDammeAnim character)
        {
            if (character.IsMine || !character.IsHero) return;
            Track track = GetTrack(character);
            if (track == null) return;
            if (!CanExecuteTimed && track.Replay.Active) track.Replay.Cancel("execution-capability-lost");
            track.Checking = true;
            track.Replay.Begin();
            // LateUpdate's ordinary setter must not change the previous replay input used by wasFire.
            if (track.Replay.Active) character.fire = track.Replay.FinalFire;
        }

        internal static void ApplyMirrorInput(TestVanDammeAnim character)
        {
            Track track;
            if (character.IsMine || !tracks.TryGetValue(character, out track) || !track.Checking || !track.Replay.Active) return;
            character.fire = track.Replay.Prepare(Connect.GetInterpTime(track.Owner));
        }

        private static void Forget(NetworkObject obj)
        {
            Track track;
            if (!tracks.TryGetValue(obj, out track)) return;
            track.Replay.Cancel("object-lifecycle-ended");
            retired.Add(track.Key);
            tracks.Remove(obj);
            instances.Remove(track.Key);
        }

        private static void PlayerLeft(PID owner)
        {
            List<NetworkObject> remove = new List<NetworkObject>();
            foreach (KeyValuePair<NetworkObject, Track> pair in tracks)
                if (pair.Value.Owner == owner) remove.Add(pair.Key);
            foreach (NetworkObject obj in remove) Forget(obj);
        }

        private static void EndSession()
        {
            tracks.Clear();
            instances.Clear();
            retired.Clear();
            sampling = false;
            timedSampling = false;
        }

        internal static bool SupportsAll(byte[][] capabilities)
        {
            if (capabilities.Length == 0) return false;
            foreach (byte[] value in capabilities)
                if (value == null || value.Length != 1 || value[0] != 1) return false;
            return true;
        }

        /// <summary>Capability status only; this does not promise gameplay consumption or packet delivery.</summary>
        public static bool CanTransmitRecords
        {
            get
            {
                if (!initialized || Connect.IsOffline || !Connect.LayerExists) return false;
                List<byte[]> capabilities = new List<byte[]>();
                foreach (PID peer in ModNegotiation.GetAllPlayers())
                    if (peer != PID.MyID)
                        capabilities.Add(ModNegotiation.GetExtensionData(peer, "RocketLib", Capability));
                return SupportsAll(capabilities.ToArray());
            }
        }

        public static bool CanExecuteTimed
        {
            get
            {
                if (!CanTransmitRecords) return false;
                foreach (PID peer in ModNegotiation.GetAllPlayers())
                {
                    if (peer == PID.MyID) continue;
                    byte[] value = ModNegotiation.GetExtensionData(peer, "RocketLib", ExecutionCapability);
                    if (value == null || value.Length != 1 || value[0] != 1) return false;
                }
                return true;
            }
        }

        private static void BeginSample() { sampling = CanTransmitRecords; timedSampling = sampling && CanExecuteTimed; }

        internal static byte[] Encode(ulong sequence)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 82, 83, 73, 1 }); // RSI, record-only protocol v1
                writer.Write(sequence);
                return stream.ToArray();
            }
        }

        internal static State ReadRecord(State state, int propertyCount, ShortInputLedger ledger)
        {
            if (state.Values.Length == propertyCount) return state;
            byte[] bytes = state.Values.Length == propertyCount + 1 ? state.Values[propertyCount] as byte[] : null;
            if (bytes != null && bytes.Length >= 4 && bytes[0] == 82 && bytes[1] == 83 && bytes[2] == 73 && bytes[3] == 2)
            {
                TimedInputFrame frame = TimedInputFrame.Decode(bytes, state.TimeStamp);
                object[] stripped = new object[propertyCount];
                Array.Copy(state.Values, stripped, propertyCount);
                if (ledger != null) ledger.Receive(frame.Latest);
                return new RecordedState(state, stripped, ledger, frame.Latest);
            }
            if (bytes == null || bytes.Length != 12 || bytes[0] != 82 || bytes[1] != 83 || bytes[2] != 73 || bytes[3] != 1)
                throw new InvalidDataException("Invalid short-input State extension or property count");
            ulong sequence;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                reader.ReadInt32();
                sequence = reader.ReadUInt64();
            }
            object[] values = new object[propertyCount];
            Array.Copy(state.Values, values, propertyCount);
            if (ledger != null) ledger.Receive(sequence);
            return new RecordedState(state, values, ledger, sequence);
        }

        internal static void ObserveSelection(ShortInputLedger ledger, State prev, State next, float lerp)
        {
            State selected = prev == null ? next : next == null ? prev : lerp > 0.5f ? next : prev;
            RecordedState record = selected as RecordedState;
            if (record != null && ReferenceEquals(record.Ledger, ledger)) ledger.Select(record.Sequence);
        }

        [HarmonyPatch(typeof(TestVanDammeAnim), "Update")]
        internal static class CapturePatch
        {
            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return WrapCheckInput(instructions, AccessTools.Method(typeof(TestVanDammeAnim), "CheckInput"),
                    AccessTools.Method(typeof(ShortInputSync), "RecordConsumed"));
            }
        }

        internal static IEnumerable<CodeInstruction> WrapCheckInput(IEnumerable<CodeInstruction> instructions, MethodInfo check, MethodInfo after)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                bool match = instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, check);
                if (match)
                {
                    CodeInstruction duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(instruction.labels);
                    duplicate.blocks.AddRange(instruction.blocks);
                    instruction.labels.Clear();
                    instruction.blocks.Clear();
                    yield return duplicate;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ShortInputSync), "BeginInput"));
                    yield return new CodeInstruction(OpCodes.Dup);
                    count++;
                }
                yield return instruction;
                if (match) yield return new CodeInstruction(OpCodes.Call, after);
            }
            if (count != 1) throw new InvalidOperationException("Expected one virtual CheckInput in Update, found " + count);
        }

        [HarmonyPatch(typeof(TestVanDammeAnim), "CheckInput")]
        internal static class ExecutionPatch
        {
            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                int count = 0;
                MethodInfo run = AccessTools.Method(typeof(NetworkObject), "RunSync");
                foreach (CodeInstruction instruction in instructions)
                {
                    bool match = instruction.Calls(run);
                    if (match)
                    {
                        CodeInstruction duplicate = new CodeInstruction(OpCodes.Dup);
                        duplicate.labels.AddRange(instruction.labels);
                        duplicate.blocks.AddRange(instruction.blocks);
                        instruction.labels.Clear();
                        instruction.blocks.Clear();
                        yield return duplicate;
                        count++;
                    }
                    yield return instruction;
                    if (match) yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ShortInputSync), "ApplyMirrorInput"));
                }
                if (count != 1) throw new InvalidOperationException("Expected one RunSync in CheckInput, found " + count);
            }
        }

        [HarmonyPatch(typeof(TestVanDammeAnim), "InputBits", MethodType.Setter)]
        private static class InputBitsPatch
        {
            private static void Prefix(TestVanDammeAnim __instance, ref short value)
            {
                Track track;
                if (__instance.IsMine || !tracks.TryGetValue(__instance, out track) || !track.ApplyingSync || !track.Replay.Active) return;
                if (!__instance.Syncronize || track.Key != __instance.Nid || track.Owner != __instance.Owner)
                { Forget(__instance); return; }
                if (!CanExecuteTimed) { track.Replay.Cancel("execution-capability-lost"); return; }
                value = (short)((value & ~16) | (track.Replay.FinalFire ? 16 : 0));
            }
        }

        [HarmonyPatch(typeof(NetworkObject), "RunSync")]
        private static class SyncApplicationPatch
        {
            private static void Prefix(NetworkObject __instance)
            {
                Track track;
                if (tracks.TryGetValue(__instance, out track)) track.ApplyingSync = true;
            }
            private static Exception Finalizer(NetworkObject __instance, Exception __exception)
            {
                Track track;
                if (tracks.TryGetValue(__instance, out track)) track.ApplyingSync = false;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(SyncController), "SerializeAllOwnedObjects")]
        private static class SamplingPatch
        {
            private static void Prefix() { BeginSample(); }
            private static Exception Finalizer(Exception __exception)
            {
                sampling = false;
                timedSampling = false;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(State), MethodType.Constructor, new Type[] { typeof(NID), typeof(NetworkObject) })]
        private static class StatePatch
        {
            private static void Postfix(NetworkObject netObj, ref object[] ___values, ref Type[] ___types)
            {
                TestVanDammeAnim character = netObj as TestVanDammeAnim;
                if (!sampling || character == null || !character.IsMine || !character.IsHero) return;
                Track track = GetTrack(character);
                if (track == null) return;
                int count = ___values.Length;
                Array.Resize(ref ___values, count + 1);
                Array.Resize(ref ___types, count + 1);
                ___values[count] = timedSampling && track.History.Valid ? track.History.Encode() : Encode(track.Ledger.Consumed);
                ___types[count] = typeof(byte[]);
            }
        }

        [HarmonyPatch(typeof(NetworkObject), "AddState")]
        private static class ReceivePatch
        {
            private static bool Prefix(NetworkObject __instance, ref State state)
            {
                TestVanDammeAnim character = __instance as TestVanDammeAnim;
                if (character == null) return true;
                if (state.Key != __instance.Nid) return false;
                // Parse the wire format even while the receiving character's hero setup is incomplete.
                Track track = character.IsHero ? GetTrack(character) : null;
                try
                {
                    int count = __instance.Props.Length;
                    byte[] bytes = state.Values.Length == count + 1 ? state.Values[count] as byte[] : null;
                    bool timed = bytes != null && bytes.Length >= 4 && bytes[3] == 2;
                    if (track != null && !character.IsMine)
                    {
                        if (timed && CanExecuteTimed) track.Replay.Receive(TimedInputFrame.Decode(bytes, state.TimeStamp));
                        else if (track.Replay.Active) track.Replay.Cancel("legacy-or-unnegotiated-state");
                    }
                    state = ReadRecord(state, count, track == null ? null : track.Ledger);
                }
                catch (InvalidDataException ex)
                {
                    if (RocketMain.Logger != null) RocketMain.Logger.Warning("[ShortInput] " + ex.Message);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(NetworkObject), "GetInterpolatedValues")]
        private static class SelectionPatch
        {
            private static void Postfix(NetworkObject __instance, State prev, State next, float lerp)
            {
                Track track;
                if (tracks.TryGetValue(__instance, out track)) ObserveSelection(track.Ledger, prev, next, lerp);
            }
        }

        [HarmonyPatch(typeof(NetworkObject), "OnDestroy")]
        private static class DestroyPatch
        {
            private static void Prefix(NetworkObject __instance) { Forget(__instance); }
        }

        [HarmonyPatch(typeof(NetworkObject), "SetOwner")]
        private static class OwnerPatch
        {
            private static void Prefix(NetworkObject __instance, PID newPID)
            { if (__instance.Owner != newPID) Forget(__instance); }
        }

        [HarmonyPatch(typeof(NetworkObject), "SetSyncingInternal")]
        private static class SyncPatch
        {
            private static void Prefix(NetworkObject __instance, bool enabled)
            { if (!enabled) Forget(__instance); }
        }
    }
}
