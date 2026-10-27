using System;
using System.Collections.Generic;
using RocketLib.Network;

public sealed class PID
{
    public static readonly PID NoID = new PID(0);
    public static readonly PID TargetOthers = new PID(1);
    public static readonly PID MyID = new PID(5);
    public readonly int Value;
    public PID(int value) { Value = value; }
    public override bool Equals(object other) { return other is PID && ((PID)other).Value == Value; }
    public override int GetHashCode() { return Value; }
    public static bool operator ==(PID a, PID b) { return ReferenceEquals(a, b) || (!ReferenceEquals(a, null) && !ReferenceEquals(b, null) && a.Value == b.Value); }
    public static bool operator !=(PID a, PID b) { return !(a == b); }
}
public sealed class IDWrapper
{
    public bool haveRequestedSyncStateFromPlayer;
}
public sealed class ConnectionLayer
{
    public readonly Dictionary<PID, IDWrapper> PlayerIDPairs = new Dictionary<PID, IDWrapper>();
}
public static class Connect
{
    public static bool LayerExists;
    public static bool IsOffline;
    public static ConnectionLayer Layer = new ConnectionLayer();
}
namespace UnityEngine
{
    public static class Application { public static bool isPlaying; }
    public static class Time { public static float realtimeSinceStartup; }
    public class Object { public static void DontDestroyOnLoad(Object target) { } }
    public class MonoBehaviour : Object { }
    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public T AddComponent<T>() { return (T)Activator.CreateInstance(typeof(T), true); }
    }
}
namespace Networking
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AllowedRPCAttribute : Attribute { }
    public delegate void RpcSignature<T>(T value);
    public delegate void RpcSignature<T1, T2>(T1 first, T2 second);
    public static class Networking
    {
        public static readonly List<PID> Targets = new List<PID>();
        public static readonly List<byte[]> Payloads = new List<byte[]>();
        public static void AdminRPC<T>(PID target, bool ignoreSessionID, RpcSignature<T> call, T value)
        {
            Assert.True(ignoreSessionID, "request ignores session ID");
            Targets.Add(target);
        }
        public static void AdminRPC<T1, T2>(PID target, bool ignoreSessionID, RpcSignature<T1, T2> call, T1 sender, T2 payload)
        {
            Assert.True(ignoreSessionID, "announce ignores session ID");
            Targets.Add(target);
            Payloads.Add((byte[])(object)payload);
        }
    }
}
namespace RocketLib
{
    public interface ILogger { void Warning(string message); }
    public static class RocketMain { public static ILogger Logger; }
}
public static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!Object.Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", got " + actual);
    }
}
public static class ModNegotiationOffline
{
    public static void Main()
    {
        PID remote = new PID(6);
        int received = 0, completed = 0, left = 0, ended = 0;
        bool clearedBeforeEnded = false;
        ModNegotiation.OnPlayerManifestReceived += delegate(PID pid, ModManifest[] manifests) { received++; };
        ModNegotiation.OnNegotiationComplete += delegate { completed++; };
        ModNegotiation.OnPlayerLeft += delegate(PID pid) { left++; Assert.True(!ModNegotiation.HasMod(pid, "Peer"), "leave clears before event"); };
        ModNegotiation.OnSessionEnded += delegate { ended++; clearedBeforeEnded = !ModNegotiation.HasMod(remote, "Peer"); };

        ModNegotiation.RegisterMod("Local", "1.0");
        Assert.True(ModNegotiation.HasMod(PID.MyID, "Local"), "local registration");
        Assert.Equal("1.0", ModNegotiation.GetModVersion(PID.MyID, "Local"), "local version");
        Assert.True(ModNegotiation.AllPlayersHaveMod("Local"), "zero peers");
        Assert.True(!ModNegotiation.HasMod(remote, "Peer"), "unknown peer");
        Assert.Equal<string>(null, ModNegotiation.GetModVersion(remote, "Peer"), "unknown version");
        Assert.Equal<byte[]>(null, ModNegotiation.GetExtensionData(remote, "Peer", "missing"), "unknown key");

        PrefabManifestEntry[] entries = { new PrefabManifestEntry("unknown", false, new string[0]), new PrefabManifestEntry("known", true, new string[] { "A.Component", "B.Component" }) };
        byte[] prefabs = ModNegotiation.SerializePrefabEntries(entries);
        PrefabManifestEntry[] decoded = ModNegotiation.DeserializePrefabEntries(prefabs);
        Assert.Equal(2, decoded.Length, "prefab count");
        Assert.True(decoded[0].ComponentsKnown, "sorted known entry");
        Assert.Equal("B.Component", decoded[0].ComponentNames[1], "component name");
        Assert.True(!decoded[1].ComponentsKnown, "unknown flag");

        ModManifest peer = new ModManifest("Peer", "2.0");
        peer.ExtensionData.Add("prefabs", prefabs);
        byte[] serialized = ModNegotiation.SerializeManifests(new ModManifest[] { peer });
        ModManifest[] roundTrip = ModNegotiation.DeserializeManifests(serialized);
        Assert.Equal("Peer", roundTrip[0].ModId, "mod ID");
        Assert.Equal("2.0", roundTrip[0].Version, "mod version");
        Assert.Equal(prefabs.Length, roundTrip[0].ExtensionData["prefabs"].Length, "extension length");

        Connect.LayerExists = true;
        Connect.Layer.PlayerIDPairs.Add(remote, new IDWrapper());
        Assert.True(!ModNegotiation.AllPlayersHaveMod("Peer"), "registered but unreported peer");
        UnityEngine.Time.realtimeSinceStartup = 1;
        ModNegotiation.PeerRegistered(PID.MyID);
        UnityEngine.Time.realtimeSinceStartup = 10;
        ModNegotiation.PeerRegistered(remote);
        UnityEngine.Time.realtimeSinceStartup = 12.9f;
        ModNegotiation.TickTimeout();
        Assert.Equal(0, completed, "timeout not early");
        ModNegotiation.AnnounceModListRPC(remote, serialized);
        Assert.Equal(1, received, "first manifest event");
        Assert.Equal(1, completed, "complete after manifest");
        Assert.True(ModNegotiation.HasMod(remote, "Peer"), "remote query");
        Assert.Equal("2.0", ModNegotiation.GetModVersion(remote, "Peer"), "remote version");
        Assert.Equal(prefabs.Length, ModNegotiation.GetExtensionData(remote, "Peer", "prefabs").Length, "remote extension");
        Assert.Equal<byte[]>(null, ModNegotiation.GetExtensionData(remote, "Peer", "missing"), "known peer missing key");
        Assert.True(!ModNegotiation.HasMod(remote, "Absent"), "known peer missing mod");
        Assert.Equal(1, ModNegotiation.GetAllPlayers().Length, "connected player enumeration");
        ModNegotiation.AnnounceModListRPC(remote, serialized);
        Assert.Equal(1, received, "identical manifest ignored");
        ModManifest changed = new ModManifest("Peer", "2.1");
        byte[] changedBytes = ModNegotiation.SerializeManifests(new ModManifest[] { changed });
        ModNegotiation.AnnounceModListRPC(remote, changedBytes);
        Assert.Equal(2, received, "changed manifest received");
        Assert.Equal(1, completed, "changed known manifest does not complete again");

        Networking.Networking.Targets.Clear();
        ModNegotiation.SetExtensionData("Local", "a", new byte[] { 1 });
        ModNegotiation.SetExtensionData("Local", "b", new byte[] { 2 });
        Assert.Equal(0, Networking.Networking.Targets.Count, "writes wait for flush");
        ModNegotiation.Flush();
        Assert.Equal(1, Networking.Networking.Targets.Count, "one coalesced flush");
        Assert.True(Networking.Networking.Targets[0] == PID.TargetOthers, "reannounce targets others");
        ModNegotiation.SetExtensionData("Local", "prefabs", ModNegotiation.SerializePrefabEntries(new PrefabManifestEntry[] { new PrefabManifestEntry("x", false, new string[0]) }));
        int beforeKnown = Networking.Networking.Targets.Count;
        ModNegotiation.SetExtensionData("Local", "prefabs", ModNegotiation.SerializePrefabEntries(new PrefabManifestEntry[] { new PrefabManifestEntry("x", true, new string[] { "C" }) }));
        Assert.Equal(beforeKnown + 1, Networking.Networking.Targets.Count, "known transition sends immediately");
        ModNegotiation.Flush();
        Assert.Equal(beforeKnown + 1, Networking.Networking.Targets.Count, "immediate send clears dirty flush");

        Connect.Layer.PlayerIDPairs.Remove(remote);
        ModNegotiation.PlayerLeft(remote);
        ModNegotiation.PlayerLeft(remote);
        Assert.Equal(1, left, "leave once");
        Assert.Equal(2, completed, "peer set change completes");
        ModNegotiation.SessionEnded();
        Assert.Equal(1, ended, "session ended once");
        Assert.True(clearedBeforeEnded, "session cleared before event");
        ModNegotiation.LevelStarted();
        ModNegotiation.LevelStarted();
        Assert.Equal(3, completed, "zero-peer completion once next session");
        ModNegotiation.SessionEnded();
        Connect.Layer.PlayerIDPairs.Add(remote, new IDWrapper());
        UnityEngine.Time.realtimeSinceStartup = 20;
        ModNegotiation.PeerRegistered(remote);
        UnityEngine.Time.realtimeSinceStartup = 22.9f;
        ModNegotiation.TickTimeout();
        Assert.Equal(3, completed, "timeout anchored to first peer");
        UnityEngine.Time.realtimeSinceStartup = 23;
        ModNegotiation.TickTimeout();
        Assert.Equal(4, completed, "three-second timeout");
        ModNegotiation.PeerRegistered(remote);
        UnityEngine.Time.realtimeSinceStartup = 40;
        ModNegotiation.TickTimeout();
        Assert.Equal(4, completed, "duplicate PID cannot restart timeout");
        Console.WriteLine("PASS: manifest/prefab round-trip, queries, events, coalescing, immediate publish, timeout, cleanup");
    }
}
