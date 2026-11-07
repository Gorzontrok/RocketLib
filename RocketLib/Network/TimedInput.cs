using System;
using System.Collections.Generic;
using System.IO;

namespace RocketLib.Network
{
    /// <summary>Input observations, not shots. Times use the owner's network clock.</summary>
    public struct ShortInputExecutionSnapshot
    {
        public readonly ulong ReceivedEdges;
        public readonly ulong MirrorConsumedEdges;
        public readonly int PendingEdges;
        public readonly bool Active;
        public readonly bool Held;
        public readonly bool FinalFire;
        public readonly string Cancellation;
        public readonly float LastSourceTime;
        public readonly double LastDeliveryTime;

        internal ShortInputExecutionSnapshot(TimedInputReplay replay)
        {
            ReceivedEdges = replay.Received;
            MirrorConsumedEdges = replay.Consumed;
            PendingEdges = replay.PendingCount;
            Active = replay.Active;
            Held = replay.Held;
            FinalFire = replay.FinalFire;
            Cancellation = replay.Cancellation;
            LastSourceTime = replay.LastSourceTime;
            LastDeliveryTime = replay.LastDeliveryTime;
        }
    }

    internal struct TimedInputEdge
    {
        internal ulong Sequence;
        internal float Time;
        internal bool Held { get { return (Sequence & 1) != 0; } }
    }

    // RSI v2: magic/version, UInt64 latest, byte count, count Single timestamps, little endian.
    // Contiguous edges end at latest. RSI v1 stays record-only and byte-for-byte unchanged.
    internal sealed class TimedInputFrame
    {
        internal const int Capacity = 64;
        internal readonly ulong Latest;
        internal readonly float[] Times;
        internal ulong First { get { return Times.Length == 0 ? 0 : Latest - (ulong)Times.Length + 1; } }
        internal TimedInputFrame(ulong latest, float[] times) { Latest = latest; Times = times; }

        internal byte[] Encode()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 82, 83, 73, 2 });
                writer.Write(Latest);
                writer.Write((byte)Times.Length);
                foreach (float time in Times) writer.Write(time);
                return stream.ToArray();
            }
        }

        internal static TimedInputFrame Decode(byte[] bytes, float sampleTime)
        {
            if (bytes == null || bytes.Length < 13 || bytes[0] != 82 || bytes[1] != 83 || bytes[2] != 73 || bytes[3] != 2)
                throw new InvalidDataException("Invalid RSI v2 header");
            int count = bytes[12];
            if (count > Capacity || bytes.Length != 13 + 4 * count || !ValidTime(sampleTime))
                throw new InvalidDataException("Invalid RSI v2 length or sample time");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                reader.ReadInt32();
                ulong latest = reader.ReadUInt64();
                reader.ReadByte();
                if (latest < (ulong)count || ((latest == 0) != (count == 0)))
                    throw new InvalidDataException("Invalid RSI v2 sequence range");
                float[] times = new float[count];
                for (int i = 0; i < count; i++)
                {
                    times[i] = reader.ReadSingle();
                    if (!ValidTime(times[i]) || times[i] > sampleTime || (i > 0 && times[i] < times[i - 1]))
                        throw new InvalidDataException("Invalid RSI v2 edge time");
                }
                return new TimedInputFrame(latest, times);
            }
        }

        internal static bool ValidTime(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
    }

    internal sealed class TimedInputHistory
    {
        private readonly Queue<float> times = new Queue<float>();
        private ulong latest;
        private float lastTime;
        internal bool Valid = true;

        internal void Capture(ulong sequence, float time)
        {
            if (sequence == latest || !Valid) return;
            if (sequence != latest + 1 || !TimedInputFrame.ValidTime(time) || time < lastTime)
            { Valid = false; return; }
            latest = sequence;
            lastTime = time;
            if (times.Count == TimedInputFrame.Capacity) times.Dequeue();
            times.Enqueue(time);
        }

        internal byte[] Encode() { return new TimedInputFrame(latest, times.ToArray()).Encode(); }
    }

    internal sealed class TimedInputReplay
    {
        private readonly Queue<TimedInputEdge> pending = new Queue<TimedInputEdge>();
        private bool ordinaryFireConsumed;
        private bool prepared;
        private TimedInputEdge candidate;
        private double candidateShift;
        private double candidateTime;
        private double shift;
        private float receivedTime;
        internal ulong Received;
        internal ulong Consumed;
        internal bool Active;
        internal bool Held;
        internal bool FinalFire;
        internal string Cancellation;
        internal float LastSourceTime;
        internal double LastDeliveryTime;
        internal int PendingCount { get { return pending.Count; } }

        internal void Cancel(string reason)
        {
            if (Cancellation == null) Cancellation = reason;
            Active = false;
            prepared = false;
            Held = false;
            pending.Clear();
        }

        internal void Receive(TimedInputFrame frame)
        {
            if (Cancellation != null) return;
            if (ordinaryFireConsumed) { Cancel("ordinary-fire-already-consumed"); return; }
            if (frame.Latest < Received) return;
            if (frame.Latest == Received) { Active = true; return; }
            if (frame.First > Received + 1) { Cancel("timestamp-history-gap"); return; }
            int start = (int)(Received + 1 - frame.First);
            if (pending.Count + frame.Times.Length - start > TimedInputFrame.Capacity)
            { Cancel("timestamp-backlog-overflow"); return; }
            if (frame.Times[start] < receivedTime)
                throw new InvalidDataException("RSI v2 clock moved backwards across States");
            for (int i = start; i < frame.Times.Length; i++)
                pending.Enqueue(new TimedInputEdge { Sequence = frame.First + (ulong)i, Time = frame.Times[i] });
            Received = frame.Latest;
            receivedTime = frame.Times[frame.Times.Length - 1];
            Active = true;
        }

        internal void Begin() { prepared = false; }

        internal bool Prepare(double sourceNow)
        {
            if (!TimedInputFrame.ValidTime(sourceNow)) { Cancel("invalid-interpolation-clock"); return false; }
            if (pending.Count == 0 || sourceNow < pending.Peek().Time + shift) return Held;
            candidate = pending.Peek();
            candidateShift = System.Math.Max(shift, sourceNow - candidate.Time);
            candidateTime = sourceNow;
            prepared = true;
            return candidate.Held;
        }

        internal void Complete(bool finalFire)
        {
            FinalFire = finalFire;
            if (!Active) { ordinaryFireConsumed |= finalFire; return; }
            if (!prepared) return;
            pending.Dequeue();
            Consumed = candidate.Sequence;
            Held = candidate.Held;
            shift = candidateShift;
            LastSourceTime = candidate.Time;
            LastDeliveryTime = candidateTime;
            prepared = false;
        }
    }
}
