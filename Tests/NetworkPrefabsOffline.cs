using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RocketLib.Network;
using UnityEngine;

public sealed class PID
{
    public static readonly PID NoID = new PID(0);
    public static readonly PID TargetOthers = new PID(1);
    public static readonly PID TargetServer = new PID(2);
    public static readonly PID MyID = new PID(5);
    public readonly int Value;
    public PID(int value) { Value = value; }
    public override bool Equals(object other) { return other is PID && ((PID)other).Value == Value; }
    public override int GetHashCode() { return Value; }
    public override string ToString() { return Value.ToString(); }
    public static bool operator ==(PID left, PID right) { return ReferenceEquals(left, right) || (!ReferenceEquals(left, null) && !ReferenceEquals(right, null) && left.Value == right.Value); }
    public static bool operator !=(PID left, PID right) { return !(left == right); }
}

public sealed class NID
{
    public readonly PID OwnerID;
    public readonly int Value;
    public NID(PID owner, int value) { OwnerID = owner; Value = value; }
    public override bool Equals(object other) { return other is NID && ((NID)other).OwnerID == OwnerID && ((NID)other).Value == Value; }
    public override int GetHashCode() { return OwnerID.GetHashCode() * 397 ^ Value; }
}

public sealed class IDWrapper { public bool haveRequestedSyncStateFromPlayer; }
public sealed class ConnectionLayer { public readonly Dictionary<PID, IDWrapper> PlayerIDPairs = new Dictionary<PID, IDWrapper>(); }
public static class Connect
{
    private static readonly ConnectionLayer layer = new ConnectionLayer();
    public static bool LayerExists;
        public static bool IsOffline;
        public static bool IsHost;
        public static ConnectionLayer Layer { get { if (!LayerExists) throw new InvalidOperationException("Layer accessed while absent"); return layer; } }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
    public static class Application { public static bool isPlaying; }
    public static class Time { public static float realtimeSinceStartup; }
    public class MonoBehaviour : Component { }
    public class Object
    {
        internal bool destroyed;
        public static void DontDestroyOnLoad(Object target) { }
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || (!ReferenceEquals(left, null) && left.destroyed);
            bool rightNull = ReferenceEquals(right, null) || (!ReferenceEquals(right, null) && right.destroyed);
            return leftNull ? rightNull : (!rightNull && ReferenceEquals(left, right));
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.destroyed) return;
            value.destroyed = true;
            GameObject gameObject = value as GameObject;
            if (!ReferenceEquals(gameObject, null))
                foreach (Component component in gameObject.Components)
                    if (component is NetworkObject) ((NetworkObject)component).OnDestroy();
        }
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object
        {
            GameObject source = original as GameObject;
            if (source == null)
            {
                Component component = original as Component;
                source = component == null ? null : component.gameObject;
            }
            if (source == null) return null;
            GameObject clone = source.Clone(position, rotation);
            GameObject requestedGameObject = original as GameObject;
            if (requestedGameObject != null) return (T)(Object)clone;
            Component requestedComponent = original as Component;
            return requestedComponent == null ? null : (T)(Object)clone.GetComponent(requestedComponent.GetType());
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject == null ? null : gameObject.transform; } }
    }
    public sealed class Transform : Component
    {
        public Vector3 position;
        public Quaternion rotation;
    }
    public sealed class GameObject : Object
    {
        private readonly List<Component> components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf;
        public IList<Component> Components { get { return components; } }
        public GameObject(string name)
        {
            transform = new Transform();
            transform.gameObject = this;
            components.Add(transform);
        }
        public T AddComponent<T>() where T : Component
        {
            T component = (T)Activator.CreateInstance(typeof(T));
            component.gameObject = this;
            components.Add(component);
            return component;
        }
        public Component GetComponent(Type type)
        {
            foreach (Component component in components)
                if (type.IsAssignableFrom(component.GetType())) return component;
            return null;
        }
        public T GetComponent<T>() where T : Component { return (T)GetComponent(typeof(T)); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            List<T> result = new List<T>();
            foreach (Component component in components)
                if (component is T) result.Add((T)component);
            return result.ToArray();
        }
        public void SetActive(bool active) { activeSelf = active; }
        internal GameObject Clone(Vector3 position, Quaternion rotation)
        {
            GameObject clone = new GameObject("clone");
            clone.transform.position = position;
            clone.transform.rotation = rotation;
            foreach (Component component in components)
            {
                if (component is Transform) continue;
                Component copy = clone.AddComponentOfType(component.GetType());
                NetworkObject sourceNetworkObject = component as NetworkObject;
                NetworkObject copiedNetworkObject = copy as NetworkObject;
                if (sourceNetworkObject != null && copiedNetworkObject != null)
                {
                    copiedNetworkObject.instantiationDataBytes = sourceNetworkObject.instantiationDataBytes;
                    copiedNetworkObject.SawTempDataAtClone = sourceNetworkObject.instantiationDataBytes != null;
                }
            }
            return clone;
        }
        private Component AddComponentOfType(Type type)
        {
            Component component = (Component)Activator.CreateInstance(type);
            component.gameObject = this;
            components.Add(component);
            return component;
        }
    }
}

public class NetworkObject : Component
{
    public byte[] instantiationDataBytes;
    public bool SawTempDataAtClone;
    public PID LastOwner;
    public int NetworkSetupCount;
    public void NetworkSetup(PID owner) { LastOwner = owner; NetworkSetupCount++; }
    public void OnDestroy() { }
}
public class MarkerBase : Component { }
public sealed class Marker : MarkerBase { }
public sealed class GameModeController { }
public sealed class MainMenu { }

public static class Registry
{
    private static readonly Dictionary<NID, GameObject> objects = new Dictionary<NID, GameObject>();
    private static int next;
    public static bool FailNextRegistration;
    public static NID AllocatePlayerUniqueID() { return new NID(PID.MyID, ++next); }
    public static bool RegisterGameObject(ref NID id, GameObject gameObject)
    {
        if (FailNextRegistration) { FailNextRegistration = false; return false; }
        if (objects.ContainsKey(id)) return false;
        objects.Add(id, gameObject);
        return true;
    }
    public static bool IsKeyInUse(NID id) { return objects.ContainsKey(id); }
    public static GameObject GetGameObject(NID id) { GameObject result; return objects.TryGetValue(id, out result) ? result : null; }
    public static void Reset() { objects.Clear(); next = 0; }
}

public sealed class InstantiationController
{
    public static readonly InstantiationController Instance = new InstantiationController();
    private readonly Dictionary<PID, List<GameObject>> instances = new Dictionary<PID, List<GameObject>>();
    public static T NetworkedInstantiate<T>(PID playerID, T prefab, Vector3 position, Quaternion rotation,
        object[] instantiationData, bool bufferThisRPC, bool executeImmediately) where T : UnityEngine.Object
    {
        return UnityEngine.Object.Instantiate(prefab, position, rotation);
    }
    public static void StoreTempData(byte[] data, NetworkObject target) { if (target != null) target.instantiationDataBytes = data; }
    public static void ClearTempData(NetworkObject target) { if (target != null) target.instantiationDataBytes = null; }
}
public static class HarmonyExtensions
{
    public static T GetFieldValue<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return (T)field.GetValue(target);
    }
}
public sealed class Logger : RocketLib.ILogger
{
    public readonly List<string> Warnings = new List<string>();
    public void Warning(string message) { Warnings.Add(message); }
}
namespace RocketLib
{
    public interface ILogger { void Warning(string message); }
    public static class RocketMain { public static ILogger Logger; }
}

namespace HarmonyLib
{
    public sealed class PatchRecord
    {
        public MethodBase Original;
        public MethodInfo Prefix;
    }
    public sealed class HarmonyMethod
    {
        public readonly MethodInfo Method;
        public HarmonyMethod(MethodInfo method) { Method = method; }
    }
    public sealed class Harmony
    {
        public static readonly List<PatchRecord> Patches = new List<PatchRecord>();
        public Harmony(string id) { }
        public void Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
        {
            Patches.Add(new PatchRecord { Original = original, Prefix = prefix == null ? null : prefix.Method });
        }
    }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class HarmonyArgument : Attribute { public HarmonyArgument(int index) { } }
}
namespace Networking
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AllowedRPCAttribute : Attribute { }
    public delegate void RpcSignature<T>(T a);
    public delegate void RpcSignature<T1, T2>(T1 a, T2 b);
    public delegate void RpcSignature<T1, T2, T3, T4, T5, T6, T7, T8>(T1 a, T2 b, T3 c, T4 d, T5 e, T6 f, T7 g, T8 h);
    public static class Networking
    {
        public sealed class Call
        {
            public PID Target;
            public bool Admin;
            public bool ExecuteImmediately;
            public bool IgnoreSessionID;
            public object[] Arguments;
        }
        public static readonly List<Call> Calls = new List<Call>();
        public static void Clear() { Calls.Clear(); }
        public static void AdminRPC<T>(PID target, bool ignoreSession, RpcSignature<T> method, T value)
        {
            Calls.Add(new Call { Target = target, Admin = true, IgnoreSessionID = ignoreSession, Arguments = new object[] { value } });
        }
        public static void AdminRPC<T1, T2>(PID target, bool ignoreSession, RpcSignature<T1, T2> method, T1 a, T2 b)
        {
            Calls.Add(new Call { Target = target, Admin = true, IgnoreSessionID = ignoreSession, Arguments = new object[] { a, b } });
        }
        public static void RPC<T1, T2, T3, T4, T5, T6, T7, T8>(PID target,
            RpcSignature<T1, T2, T3, T4, T5, T6, T7, T8> method,
            T1 a, T2 b, T3 c, T4 d, T5 e, T6 f, T7 g, T8 h, bool executeImmediately)
        {
            Calls.Add(new Call { Target = target, Arguments = new object[] { a, b, c, d, e, f, g, h }, ExecuteImmediately = executeImmediately });
        }
        public static void AdminRPC<T1, T2, T3, T4, T5, T6, T7, T8>(PID target, bool ignoreSession,
            RpcSignature<T1, T2, T3, T4, T5, T6, T7, T8> method,
            T1 a, T2 b, T3 c, T4 d, T5 e, T6 f, T7 g, T8 h)
        {
            Calls.Add(new Call { Target = target, Admin = true, IgnoreSessionID = ignoreSession, Arguments = new object[] { a, b, c, d, e, f, g, h } });
        }
    }
}

public static class TypeSerializer
{
    public static Type[] LastTypes;
    public static byte[] SerializeParameterList(Type[] types, object[] values)
    {
        LastTypes = (Type[])types.Clone();
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(types.Length);
            for (int i = 0; i < types.Length; i++) writer.Write(types[i].FullName);
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i] == null ? "<null>" : values[i].ToString();
                writer.Write(value);
            }
            return stream.ToArray();
        }
    }
}

public static class Assert
{
    public static void True(bool value, string message) { if (!value) Fail(message); }
    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!object.Equals(expected, actual)) Fail(message + ": expected " + expected + ", got " + actual);
    }
    public static void Same(object expected, object actual, string message)
    {
        if (!ReferenceEquals(expected, actual)) Fail(message);
    }
    private static void Fail(string message)
    {
        Console.Error.WriteLine("FAIL: " + message);
        Environment.Exit(1);
    }
}

public static class NetworkPrefabsOffline
{
    private static int failures;
    private static readonly Logger logger = new Logger();
    private static readonly PID publisher = new PID(6);

    public static void Main()
    {
        RocketLib.RocketMain.Logger = logger;
        TestManifestAndRegistry();
        TestResolveAndFailures();
        TestRoutingAndReplay();
        TestReceiverAndLifecycle();
        Console.WriteLine("PASS: prefab manifest, registry, resolution failures/cache, routing, owner/publisher, replay, cleanup");
    }

    private static void TestManifestAndRegistry()
    {
        PrefabIdEntry[] entries = {
            new PrefabIdEntry("known", new string[] { "A.Component", "B.Component" }),
            new PrefabIdEntry("unknown", (string[])null)
        };
        byte[] phase2 = NetworkPrefabs.SerializeIdList(entries);
        byte[] phase1 = ModNegotiation.SerializePrefabEntries(new PrefabManifestEntry[] {
            new PrefabManifestEntry("known", true, new string[] { "A.Component", "B.Component" }),
            new PrefabManifestEntry("unknown", false, new string[0])
        });
        Assert.True(EqualBytes(phase1, phase2), "Phase 2 helpers reuse Phase 1 prefab wire format");
        PrefabIdEntry[] decoded = NetworkPrefabs.DeserializeIdList(phase2);
        Assert.Equal(2, decoded.Length, "manifest entry count");
        Assert.Equal("B.Component", decoded[0].ComponentNames[1], "manifest component names");

        GameObject instance = NewPrefab(true);
        Action<GameObject> oldHook = delegate { };
        Action<GameObject> newHook = delegate { };
        NetworkPrefabs.RegisterInstance("Mod:Item", instance, oldHook);
        Assert.True(HasClosedRoutePatch(typeof(UnityEngine.Object)), "instance registration installs the canonical Object route");
        Assert.Equal(1, HarmonyLib.Harmony.Patches.Count, "registration installs one shared reference route");
        Assert.Equal(0, CountClosedRoutePatches(typeof(GameObject)), "registration does not bind original body to GameObject");
        Assert.Equal(0, CountClosedRoutePatches(typeof(NetworkObject)), "registration does not bind original body to component type");
        Assert.True(!HasOpenRoutePatch(), "routing never patches the open generic definition");
        Assert.True(!NetworkPrefabs.IsRegistered("Mod:Item"), "instance-only registration does not promise a receiver factory");
        NetworkPrefabs.RegisterFactory("Mod:Item", delegate { return NewPrefab(true); });
        Assert.True(NetworkPrefabs.IsRegistered("Mod:Item"), "factory registration participates in IsRegistered");
        Assert.Same(oldHook, NetworkPrefabs.GetInstantiationHook("Mod:Item"), "factory registration without hook retains instance hook");
        GameObject second = NewPrefab(true);
        NetworkPrefabs.RegisterInstance("Mod:Item", second);
        Assert.Same(oldHook, NetworkPrefabs.GetInstantiationHook("Mod:Item"), "instance registration without hook retains prior hook");
        NetworkPrefabs.RegisterFactory("Mod:Item", delegate { return NewPrefab(true); }, newHook);
        Assert.Same(newHook, NetworkPrefabs.GetInstantiationHook("Mod:Item"), "provided exact hook replaces prior hook");
        Assert.Equal<string>(null, NetworkPrefabs.GetIdForPrefab(instance), "new instance registration removes previous reverse mapping");
        Assert.Equal("Mod:Item", NetworkPrefabs.GetIdForPrefab(second), "second instance registration maps its prefab");
        GameObject latest = NewPrefab(true);
        NetworkPrefabs.RegisterInstance("Mod:Item", latest);
        Assert.Equal<string>(null, NetworkPrefabs.GetIdForPrefab(instance), "last instance registration removes the old reverse mapping");
        Assert.Equal("Mod:Item", NetworkPrefabs.GetIdForPrefab(latest), "last instance registration wins");
        Assert.True(Array.IndexOf(NetworkPrefabs.GetRegisteredIds(), "Mod:Item") >= 0, "registered IDs include instances and factories");

        Action<GameObject> broadHook = delegate { };
        Action<GameObject> narrowHook = delegate { };
        NetworkPrefabs.RegisterResolver("Mod:", delegate(string id, PID source) { return NewPrefab(true); }, broadHook);
        NetworkPrefabs.RegisterResolver("Mod:Item", delegate(string id, PID source) { return NewPrefab(true); }, narrowHook);
        Assert.Same(newHook, NetworkPrefabs.GetInstantiationHook("Mod:Item"), "exact hook takes precedence over resolver hook");
        Assert.Same(narrowHook, NetworkPrefabs.GetInstantiationHook("Mod:Item:Child"), "longest resolver prefix wins when no exact hook exists");
        Assert.True(NetworkPrefabs.IsRegistered("Mod:Other"), "resolver prefix registers matching IDs");
    }

    private static void TestResolveAndFailures()
    {
        InstallManifest(publisher, "Published", new PrefabIdEntry("Factory:One", new string[] { "N" }),
            new PrefabIdEntry("Factory:Throw", new string[0]), new PrefabIdEntry("Factory:Null", new string[0]),
            new PrefabIdEntry("Resolver:Item", new string[] { "R" }), new PrefabIdEntry("Resolver:Null", new string[0]));
        int created = 0;
        NetworkPrefabs.RegisterFactory("Factory:One", delegate { created++; return NewPrefab(true); });
        GameObject first = NetworkPrefabs.ResolvePrefab("Factory:One", publisher);
        GameObject second = NetworkPrefabs.ResolvePrefab("Factory:One", publisher);
        Assert.Same(first, second, "factory output cached while alive");
        Assert.Equal(1, created, "factory called once for a live cache");
        UnityEngine.Object.Destroy(first);
        GameObject recreated = NetworkPrefabs.ResolvePrefab("Factory:One", publisher);
        Assert.True(recreated != null && !ReferenceEquals(recreated, first), "destroyed Unity cache is recreated");
        Assert.Equal(2, created, "factory retries after Unity-null cache");

        PID malformedPublisher = new PID(7);
        InstallRawPrefabManifest(malformedPublisher, "Malformed", new byte[] { 1, 2 });
        NetworkPrefabs.RegisterFactory("Malformed:Item", delegate { return NewPrefab(true); });
        AssertFailure("Malformed:Item", malformedPublisher, "malformed remote prefab extension is reported instead of escaping resolve");
        Connect.LayerExists = true;
        Connect.Layer.PlayerIDPairs.Clear();
        Connect.Layer.PlayerIDPairs.Add(malformedPublisher, new IDWrapper());
        Assert.True(!NetworkPrefabs.AllPlayersHavePrefab("Malformed:Item"), "malformed remote prefab extension is incompatible instead of escaping query");
        Connect.Layer.PlayerIDPairs.Clear();
        Connect.LayerExists = false;

        GameObject resolverResult = NewPrefab(true);
        NetworkPrefabs.RegisterResolver("Priority:", delegate(string id, PID source) { return resolverResult; });
        GameObject factoryResult = NewPrefab(true);
        NetworkPrefabs.RegisterFactory("Priority:Item", delegate { return factoryResult; });
        PID priorityPublisher = new PID(13);
        InstallManifest(priorityPublisher, "PriorityMod", new PrefabIdEntry("Priority:Item", new string[0]));
        Assert.Same(factoryResult, NetworkPrefabs.ResolvePrefab("Priority:Item", priorityPublisher), "exact factory takes precedence over matching resolver");

        PID resolvedPublisher = null;
        NetworkPrefabs.RegisterResolver("Resolver:", delegate(string id, PID source) { resolvedPublisher = source; return NewPrefab(true); });
        Assert.True(NetworkPrefabs.ResolvePrefab("Resolver:Item", publisher) != null, "prefix resolver succeeds");
        Assert.Same(publisher, resolvedPublisher, "resolver receives RPC publisher PID");

        int throwCount = 0;
        NetworkPrefabs.RegisterFactory("Factory:Throw", delegate { throwCount++; throw new InvalidOperationException("expected"); });
        AssertFailure("Factory:Throw", publisher, "factory throw is reported");
        AssertFailure("Factory:Throw", publisher, "cached factory failure is reported");
        Assert.Equal(1, throwCount, "throwing factory is cached until level reset");
        NetworkPrefabs.ClearLevelFailureCache();
        AssertFailure("Factory:Throw", publisher, "level reset permits factory retry");
        Assert.Equal(2, throwCount, "factory retry after level reset");

        NetworkPrefabs.RegisterFactory("Factory:Null", delegate { return null; });
        AssertFailure("Factory:Null", publisher, "null factory result is reported");
        NetworkPrefabs.RegisterResolver("Resolver:Null", delegate(string id, PID source) { throw new InvalidOperationException("expected resolver"); });
        AssertFailure("Resolver:Null", publisher, "resolver throw is reported");
        NetworkPrefabs.RegisterResolver("Resolver:Null", delegate(string id, PID source) { return null; });
        AssertFailure("Resolver:Null", publisher, "null resolver result is reported");
        AssertFailure("No:Handler", publisher, "missing handler is reported");
        AssertFailure("Unpublished:Item", publisher, "missing publisher manifest entry is reported");

        PID unknownPublisher = new PID(8);
        InstallManifest(unknownPublisher, "Unknown", new PrefabIdEntry("Unknown:Components", (string[])null));
        NetworkPrefabs.RegisterFactory("Unknown:Components", delegate { return NewPrefab(true); });
        AssertFailure("Unknown:Components", unknownPublisher, "components-unknown publisher entry is reported");
        Assert.True(failures >= 9, "each failure path raises OnResolveFailed");
        Assert.True(logger.Warnings.Count >= failures, "each resolve failure is logged");
        bool publisherLogged = false;
        foreach (string warning in logger.Warnings)
            if (warning.IndexOf(publisher.ToString()) >= 0) publisherLogged = true;
        Assert.True(publisherLogged, "resolve failure log contains expected publisher PID");
    }

    private static void TestRoutingAndReplay()
    {
        GameObject vanillaPrefab = NewPrefab(true);
        object[] args = { PID.MyID, vanillaPrefab, new Vector3(1, 2, 3), new Quaternion(0, 0, 0, 1), null, false, false, null };
        Assert.True(InvokePrefix(args), "unregistered prefab returns to vanilla path");
        Assert.Equal<object>(null, args[7], "unregistered prefab leaves result unset");
        UnityEngine.Object untouched = new GameObject("unchanged result");
        object[] vanillaComponentArgs = { PID.MyID, vanillaPrefab.GetComponent<NetworkObject>(), new Vector3(), new Quaternion(), null, false, false, untouched };
        Assert.True(InvokePrefix(vanillaComponentArgs), "unregistered component returns to vanilla path");
        Assert.Same(untouched, vanillaComponentArgs[7], "unregistered component preserves existing result");

        GameObject customPrefab = NewPrefab(true);
        bool hookRanAfterActivation = false;
        NetworkPrefabs.RegisterInstance("Route:GameObject", customPrefab, delegate(GameObject instance) { hookRanAfterActivation = instance.activeSelf; });
        object[] gameObjectArgs = { PID.MyID, customPrefab, new Vector3(1, 2, 3), new Quaternion(0, 0, 0, 1), new object[] { "payload", null }, true, true, null };
        Assert.True(!InvokePrefix(gameObjectArgs), "registered prefab skips vanilla method");
        GameObject spawned = (GameObject)gameObjectArgs[7];
        Assert.True(spawned != null && spawned.activeSelf, "custom route returns active GameObject");
        Assert.True(hookRanAfterActivation, "post-instantiate hook runs after activation");
        Assert.True(spawned.GetComponent<NetworkObject>().SawTempDataAtClone, "temp instantiation data is set before cloning");
        Assert.Equal(typeof(object), TypeSerializer.LastTypes[1], "null instantiation argument keeps vanilla object type tag");
        Assert.Equal<byte[]>(null, customPrefab.GetComponent<NetworkObject>().instantiationDataBytes, "temp data is cleared after cloning");
        Assert.Equal(1, spawned.GetComponent<NetworkObject>().NetworkSetupCount, "sender calls NetworkSetup");
        Assert.Equal(1, Networking.Networking.Calls.Count, "custom route sends one RPC after initialization");
        Networking.Networking.Call live = Networking.Networking.Calls[0];
        Assert.True(live.Target == PID.TargetOthers && !live.Admin, "live RPC targets other peers");
        Assert.True(live.ExecuteImmediately, "executeImmediately is forwarded as the RPC tail flag");
        Assert.True((bool)live.Arguments[7], "bufferThisRPC is carried as an RPC argument");

        GameObject componentPrefab = NewPrefab(true);
        componentPrefab.AddComponent<MarkerBase>();
        Marker sourceMarker = componentPrefab.AddComponent<Marker>();
        NetworkPrefabs.RegisterInstance("Route:Marker", componentPrefab);
        Assert.Equal(1, CountClosedRoutePatches(typeof(UnityEngine.Object)), "mixed registrations retain one canonical route");
        Assert.Equal(1, HarmonyLib.Harmony.Patches.Count, "mixed component types never replace the shared original body");
        Assert.Equal(0, CountClosedRoutePatches(typeof(Marker)), "derived marker never becomes the original body type");
        Assert.Equal(0, CountClosedRoutePatches(typeof(MarkerBase)), "marker base never becomes the original body type");
        object[] componentArgs = { PID.MyID, sourceMarker, new Vector3(), new Quaternion(), null, false, false, null };
        Assert.True(!InvokePrefix(componentArgs), "component prefab routes through custom path");
        Assert.True(componentArgs[7] is Marker, "component-typed instantiate returns matching component");
        Marker clonedMarker = (Marker)componentArgs[7];
        Assert.True(!ReferenceEquals(sourceMarker, clonedMarker), "component result is from the new clone");
        Assert.Same(clonedMarker, clonedMarker.gameObject.GetComponent<Marker>(), "component result belongs to spawned object");

        MarkerBase baseTypedSource = sourceMarker;
        object[] baseArgs = { PID.MyID, baseTypedSource, new Vector3(), new Quaternion(), null, false, false, null };
        Assert.True(!InvokePrefix(baseArgs), "base-typed derived source routes through the same prefix");
        Assert.Equal(typeof(Marker), baseArgs[7].GetType(), "base-typed source returns the corresponding derived component");
        Assert.True(!ReferenceEquals(baseTypedSource, baseArgs[7]), "base-typed result is a fresh component");

        NetworkObject sourceNetworkObject = componentPrefab.GetComponent<NetworkObject>();
        object[] networkArgs = { PID.MyID, sourceNetworkObject, new Vector3(), new Quaternion(), new object[] { "component", 101 }, true, false, null };
        Assert.True(!InvokePrefix(networkArgs), "NetworkObject source uses custom route");
        Assert.Equal(typeof(NetworkObject), networkArgs[7].GetType(), "NetworkObject source returns NetworkObject rather than GameObject");
        NetworkObject clonedNetworkObject = (NetworkObject)networkArgs[7];
        Networking.Networking.Call componentCall = Networking.Networking.Calls[Networking.Networking.Calls.Count - 1];
        Assert.Same(clonedNetworkObject.gameObject, Registry.GetGameObject((NID)componentCall.Arguments[1]), "component result is on the registered object");
        Assert.True(clonedNetworkObject.SawTempDataAtClone, "component result clone retains temporary instantiation payload");
        Assert.True(!ReferenceEquals(sourceNetworkObject, clonedNetworkObject), "NetworkObject result is a fresh component");

        object[] repeatedGameObjectArgs = { PID.MyID, componentPrefab, new Vector3(), new Quaternion(), null, false, false, null };
        Assert.True(!InvokePrefix(repeatedGameObjectArgs), "GameObject source still routes after component calls");
        Assert.Equal(typeof(GameObject), repeatedGameObjectArgs[7].GetType(), "same prefix preserves GameObject result shape");
        TestCanonicalNativeFallback();

        Registry.FailNextRegistration = true;
        Networking.Networking.Clear();
        object[] failedArgs = { PID.MyID, customPrefab, new Vector3(), new Quaternion(), null, false, false, null };
        Assert.True(!InvokePrefix(failedArgs), "registered prefab remains on custom route when registration fails");
        Assert.True(!ReferenceEquals(null, failedArgs[7]), "failed registry insertion preserves the local result reference");
        Assert.True((UnityEngine.Object)failedArgs[7] == null, "failed registry insertion still destroys the local object");
        Assert.Equal(0, Networking.Networking.Calls.Count, "failed registry insertion does not send RPC");

        Networking.Networking.Clear();
        CustomInstantiation.ClearBuffers();
        CustomInstantiation.Spawn("Route:GameObject", PID.MyID, customPrefab, new Vector3(), new Quaternion(), null, true, false);
        GameObject sceneSpawn = CustomInstantiation.Spawn("Route:GameObject", PID.TargetServer, customPrefab, new Vector3(), new Quaternion(), null, true, false);
        PID requestee = new PID(9);
        Networking.Networking.Clear();
        CustomInstantiation.ReplayOwnedSpawns(requestee);
        Assert.Equal(1, Networking.Networking.Calls.Count, "owned replay remains separate from scene-owned replay");
        Assert.True(Networking.Networking.Calls[0].Admin && Networking.Networking.Calls[0].Target == requestee, "owned replay uses AdminRPC requestee target");
        Assert.True(!Networking.Networking.Calls[0].IgnoreSessionID, "owned replay remains scoped to the current session");
        Assert.True((PID)Networking.Networking.Calls[0].Arguments[2] == PID.MyID, "owned replay keeps object owner");
        Assert.True((PID)Networking.Networking.Calls[0].Arguments[3] == PID.MyID, "owned replay publishes from local client");
        Networking.Networking.Clear();
        CustomInstantiation.ReplaySceneOwnedSpawns(requestee);
        Assert.Equal(1, Networking.Networking.Calls.Count, "scene-owned replay has its own buffer");
        Assert.True(!Networking.Networking.Calls[0].IgnoreSessionID, "scene-owned replay remains scoped to the current session");
        Assert.True((PID)Networking.Networking.Calls[0].Arguments[2] == PID.TargetServer, "scene-owned replay retains target-server owner");
        Assert.True((PID)Networking.Networking.Calls[0].Arguments[3] == PID.MyID, "scene-owned replay still publishes from local client");

        InvokePatch(typeof(DestroyCustomSpawnBufferPatch), sceneSpawn);
        Networking.Networking.Clear();
        CustomInstantiation.ReplaySceneOwnedSpawns(requestee);
        Assert.Equal(0, Networking.Networking.Calls.Count, "destroy removal prunes matching buffered spawn");

        CustomInstantiation.ClearBuffers();
        GameObject stale = CustomInstantiation.Spawn("Route:GameObject", PID.MyID, customPrefab, new Vector3(), new Quaternion(), null, true, false);
        UnityEngine.Object.Destroy(stale);
        Networking.Networking.Clear();
        CustomInstantiation.ReplayOwnedSpawns(requestee);
        Assert.Equal(0, Networking.Networking.Calls.Count, "destroyed Unity entries are stripped before replay");
        CustomInstantiation.ClearBuffers();

        GameObject objectDestroy = CustomInstantiation.Spawn("Route:GameObject", PID.MyID, customPrefab, new Vector3(), new Quaternion(), null, true, false);
        InvokePatch(typeof(DestroyCustomSpawnObjectBufferPatch), objectDestroy.GetComponent<NetworkObject>());
        Networking.Networking.Clear();
        CustomInstantiation.ReplayOwnedSpawns(requestee);
        Assert.Equal(0, Networking.Networking.Calls.Count, "NetworkObject destruction postfix removes owned buffered spawn");
        CustomInstantiation.ClearBuffers();

        GameObject noNetworkObject = NewPrefab(false);
        NetworkPrefabs.RegisterInstance("Route:NoNetworkObject", noNetworkObject);
        CustomInstantiation.Spawn("Route:NoNetworkObject", PID.MyID, noNetworkObject, new Vector3(), new Quaternion(), null, true, false);
        Networking.Networking.Clear();
        CustomInstantiation.ReplayOwnedSpawns(requestee);
        Assert.Equal(0, Networking.Networking.Calls.Count, "object without NetworkObject is not placed in replay buffer");
        CustomInstantiation.ClearBuffers();
    }

    private static void TestReceiverAndLifecycle()
    {
        PID owner = new PID(10);
        PID distinctPublisher = new PID(11);
        InstallManifest(owner, "OwnerMod", new PrefabIdEntry("Remote:Owned", new string[] { "Owner.Component" }));
        InstallManifest(distinctPublisher, "RemoteMod", new PrefabIdEntry("Remote:Owned", new string[] { "NetworkObject" }));
        Assert.Equal("NetworkObject", NetworkPrefabs.GetPrefabEntry("Remote:Owned", distinctPublisher).ComponentNames[0], "prefab manifest lookup is scoped to publisher, not owner");
        int factoryCalls = 0;
        GameObject latestFactoryPrefab = null;
        NetworkPrefabs.RegisterFactory("Remote:Owned", delegate { factoryCalls++; return latestFactoryPrefab = NewPrefab(true); });
        NID id = new NID(owner, 1001);
        CustomInstantiation.CustomInstantiateRPC("Remote:Owned", id, owner, distinctPublisher,
            new Vector3(4, 5, 6), new Quaternion(0, 0, 0, 1), new byte[] { 1 }, true);
        GameObject received = Registry.GetGameObject(id);
        Assert.True(received != null, "receiver registers under sender's NID");
        Assert.True(received.GetComponent<NetworkObject>().LastOwner == owner, "receiver NetworkSetup uses owner PID");
        Assert.True(InstantiationController.Instance.InstancesFor(owner).Contains(received), "owner bookkeeping uses owner rather than publisher");
        Assert.Equal(1, factoryCalls, "receiver resolves remote publisher's prefab");

        CustomInstantiation.CustomInstantiateRPC("Remote:Owned", id, owner, distinctPublisher,
            new Vector3(), new Quaternion(), null, false);
        Assert.Equal(1, factoryCalls, "duplicate NID exits before resolving or cloning");

        GameObject buffered = CustomInstantiation.Spawn("Route:GameObject", PID.MyID, NewPrefab(true), new Vector3(), new Quaternion(), null, true, false);
        CustomInstantiation.Spawn("Route:GameObject", PID.TargetServer, NewPrefab(true), new Vector3(), new Quaternion(), null, true, false);
        ModNegotiation.SessionEnded();
        Assert.True(latestFactoryPrefab == null, "session cleanup destroys cached factory prefab templates");
        Networking.Networking.Clear();
        CustomInstantiation.ReplayOwnedSpawns(owner);
        CustomInstantiation.ReplaySceneOwnedSpawns(owner);
        Assert.Equal(0, Networking.Networking.Calls.Count, "session end clears both replay buffers");
        Assert.True(buffered != null, "session cleanup does not destroy caller-owned object directly");

        InstallManifest(distinctPublisher, "RemoteMod", new PrefabIdEntry("Remote:Owned", new string[] { "NetworkObject" }));
        CustomInstantiation.ClearBuffers();
        CustomInstantiation.Spawn("Route:GameObject", PID.MyID, NewPrefab(true), new Vector3(), new Quaternion(), null, true, false);
        Registry.Reset();
        InvokePatch(typeof(ClearCustomSpawnBuffersPatch));
        Networking.Networking.Clear();
        CustomInstantiation.ReplayOwnedSpawns(owner);
        Assert.Equal(0, Networking.Networking.Calls.Count, "registry reset cleanup leaves no replay entries");

        Connect.LayerExists = false;
        Assert.True(NetworkPrefabs.AllPlayersHavePrefab("anything"), "no-peer compatibility query succeeds without reading absent layer");
        Connect.LayerExists = true;
        Connect.Layer.PlayerIDPairs.Clear();
        PID unknown = new PID(12);
        Connect.Layer.PlayerIDPairs.Add(unknown, new IDWrapper());
        Assert.True(!NetworkPrefabs.AllPlayersHavePrefab("Remote:Owned"), "registered peer without manifest is unknown, not compatible");
        InstallManifest(unknown, "OtherMod", new PrefabIdEntry("Remote:Owned", new string[] { "Other.Component" }));
        Assert.True(NetworkPrefabs.AllPlayersHavePrefab("Remote:Owned"), "compatibility query scans all mods in every peer manifest");

        NetworkPrefabs.ClearSessionState();
        InstallManifest(distinctPublisher, "RemoteMod", new PrefabIdEntry("Remote:Owned", new string[] { "NetworkObject" }));
        Assert.True(NetworkPrefabs.ResolvePrefab("Remote:Owned", distinctPublisher) != null, "session cleanup clears cached prefab and later resolves anew");
        Assert.Equal(2, factoryCalls, "session cleanup invalidates successful factory cache");
        latestFactoryPrefab = NetworkPrefabs.ResolvePrefab("Remote:Owned", distinctPublisher);
        NetworkPrefabs.ClearSessionState();
        Assert.True(latestFactoryPrefab == null, "explicit session cleanup destroys cached factory prefab templates");

        InstallManifest(distinctPublisher, "RemoteMod", new PrefabIdEntry("Remote:Owned", new string[] { "NetworkObject" }));
        Assert.True(NetworkPrefabs.ResolvePrefab("Remote:Owned", distinctPublisher) != null, "session cleanup allows a fresh factory result");
        InvokePatch(typeof(ClearCustomPrefabSessionStateOnMainMenuPatch));
        InstallManifest(distinctPublisher, "RemoteMod", new PrefabIdEntry("Remote:Owned", new string[] { "NetworkObject" }));
        Assert.True(NetworkPrefabs.ResolvePrefab("Remote:Owned", distinctPublisher) != null, "main menu return permits a fresh factory result");
        Assert.Equal(4, factoryCalls, "main menu postfix clears successful factory cache");
        NetworkPrefabs.ClearSessionState();
    }

    private static bool InvokePrefix(object[] args)
    {
        MethodInfo prefix = typeof(NetworkedInstantiatePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(!prefix.IsGenericMethod, "route prefix does not depend on a closed generic result binding");
        return (bool)prefix.Invoke(null, args);
    }

    private static void TestCanonicalNativeFallback()
    {
        GameObject source = NewPrefab(true);
        Marker marker = source.AddComponent<Marker>();
        MarkerBase baseTypedMarker = marker;
        UnityEngine.Object[] sources = { source, source.GetComponent<NetworkObject>(), baseTypedMarker };
        int callsBefore = Networking.Networking.Calls.Count;
        foreach (UnityEngine.Object original in sources)
        {
            object[] args = { PID.MyID, original, new Vector3(), new Quaternion(), null, false, false, null };
            Assert.True(InvokePrefix(args), "unregistered source still delegates to original after mixed registration");
            Assert.Equal<object>(null, args[7], "native delegation leaves result for original body");
            MethodInfo target = (MethodInfo)HarmonyLib.Harmony.Patches[0].Original;
            UnityEngine.Object result = (UnityEngine.Object)target.Invoke(null, new object[] { PID.MyID, original, new Vector3(), new Quaternion(), null, false, false });
            Assert.Equal(original.GetType(), result.GetType(), "canonical native-body fixture preserves runtime source shape");
            Assert.True(!ReferenceEquals(original, result), "canonical native-body fixture returns a fresh object");
        }
        Assert.Equal(callsBefore, Networking.Networking.Calls.Count, "native fallback does not send a custom RPC");
    }

    private static bool HasClosedRoutePatch(Type resultType)
    {
        foreach (HarmonyLib.PatchRecord patch in HarmonyLib.Harmony.Patches)
        {
            MethodInfo target = patch.Original as MethodInfo;
            if (target == null || target.DeclaringType != typeof(InstantiationController) ||
                target.Name != "NetworkedInstantiate" || !target.IsGenericMethod || target.IsGenericMethodDefinition ||
                target.GetGenericArguments()[0] != resultType) continue;
            return patch.Prefix != null && !patch.Prefix.IsGenericMethod &&
                patch.Prefix == typeof(NetworkedInstantiatePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
        }
        return false;
    }

    private static bool HasOpenRoutePatch()
    {
        foreach (HarmonyLib.PatchRecord patch in HarmonyLib.Harmony.Patches)
        {
            MethodInfo target = patch.Original as MethodInfo;
            if (target != null && target.DeclaringType == typeof(InstantiationController) &&
                target.Name == "NetworkedInstantiate" && target.IsGenericMethodDefinition) return true;
        }
        return false;
    }

    private static int CountClosedRoutePatches(Type resultType)
    {
        int count = 0;
        foreach (HarmonyLib.PatchRecord patch in HarmonyLib.Harmony.Patches)
        {
            MethodInfo target = patch.Original as MethodInfo;
            if (target != null && target.DeclaringType == typeof(InstantiationController) &&
                target.Name == "NetworkedInstantiate" && target.IsGenericMethod && !target.IsGenericMethodDefinition &&
                target.GetGenericArguments()[0] == resultType) count++;
        }
        return count;
    }

    private static void InvokePatch(Type patchType, params object[] args)
    {
        MethodInfo callback = patchType.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
        if (callback == null) callback = patchType.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
        callback.Invoke(null, args);
    }

    private static void InstallManifest(PID player, string modId, params PrefabIdEntry[] entries)
    {
        ModManifest manifest = new ModManifest(modId, "1");
        manifest.ExtensionData.Add("prefabs", NetworkPrefabs.SerializeIdList(entries));
        ModNegotiation.AnnounceModListRPC(player, ModNegotiation.SerializeManifests(new ModManifest[] { manifest }));
    }

    private static void InstallRawPrefabManifest(PID player, string modId, byte[] data)
    {
        ModManifest manifest = new ModManifest(modId, "1");
        manifest.ExtensionData.Add("prefabs", data);
        ModNegotiation.AnnounceModListRPC(player, ModNegotiation.SerializeManifests(new ModManifest[] { manifest }));
    }

    private static void AssertFailure(string id, PID from, string message)
    {
        int before = failures;
        NetworkPrefabs.ResolvePrefab(id, from);
        Assert.Equal(before + 1, failures, message);
    }

    private static void OnResolveFailed(string id, PID from, string reason) { failures++; }
    private static GameObject NewPrefab(bool withNetworkObject)
    {
        GameObject gameObject = new GameObject("prefab");
        if (withNetworkObject) gameObject.AddComponent<NetworkObject>();
        return gameObject;
    }
    private static bool EqualBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    static NetworkPrefabsOffline() { NetworkPrefabs.OnResolveFailed += OnResolveFailed; }
}

public static class InstantiationControllerTestAccess
{
    public static List<GameObject> InstancesFor(this InstantiationController controller, PID owner)
    {
        Dictionary<PID, List<GameObject>> instances = HarmonyExtensions.GetFieldValue<Dictionary<PID, List<GameObject>>>(controller, "instances");
        List<GameObject> values;
        return instances.TryGetValue(owner, out values) ? values : new List<GameObject>();
    }
}
