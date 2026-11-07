using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Networking;
using UnityEngine;

namespace RocketLib.Network
{
    internal static class CustomInstantiation
    {
        private sealed class BufferedSpawn
        {
            internal string PrefabId;
            internal NID Nid;
            internal PID Owner;
            internal GameObject GameObject;
            internal NetworkObject NetworkObject;
            internal byte[] InstantiationData;
        }

        private static readonly List<BufferedSpawn> ownedSpawns = new List<BufferedSpawn>();
        private static readonly List<BufferedSpawn> sceneOwnedSpawns = new List<BufferedSpawn>();
        private static bool routePatched;
        private static readonly Harmony routeHarmony = new Harmony("RocketLib.CustomInstantiationRoutes");

        internal static void EnsureGenericRoutePatches(GameObject prefab)
        {
            if (prefab == null || routePatched) return;

            MethodInfo target = typeof(InstantiationController).GetMethod("NetworkedInstantiate",
                BindingFlags.Public | BindingFlags.Static);
            // Keep the shared reference-type original body closed over the common constraint.
            if (target.IsGenericMethodDefinition) target = target.MakeGenericMethod(typeof(UnityEngine.Object));
            MethodInfo prefix = typeof(NetworkedInstantiatePatch).GetMethod("Prefix",
                BindingFlags.NonPublic | BindingFlags.Static);
            routeHarmony.Patch(target, prefix: new HarmonyMethod(prefix));
            routePatched = true;
        }

        internal static GameObject Spawn(string prefabId, PID owner, GameObject prefab, Vector3 position, Quaternion rotation,
            object[] instantiationData, bool buffer, bool executeImmediately)
        {
            byte[] data = SerializeInstantiationData(instantiationData);
            NID nid = Registry.AllocatePlayerUniqueID();
            NetworkObject prefabNetworkObject = prefab.GetComponent<NetworkObject>();
            if (prefabNetworkObject != null) InstantiationController.StoreTempData(data, prefabNetworkObject);

            GameObject instance;
            try { instance = UnityEngine.Object.Instantiate(prefab, position, rotation); }
            finally
            {
                if (prefabNetworkObject != null) InstantiationController.ClearTempData(prefabNetworkObject);
            }

            instance.SetActive(true);
            InvokeInstantiationHook(prefabId, instance);
            if (!RegisterAndInitialize(instance, prefabId, ref nid, owner, data, buffer))
                return instance;

            Networking.Networking.RPC<string, NID, PID, PID, Vector3, Quaternion, byte[], bool>(
                PID.TargetOthers,
                new RpcSignature<string, NID, PID, PID, Vector3, Quaternion, byte[], bool>(CustomInstantiateRPC),
                prefabId, nid, owner, PID.MyID, position, rotation, data, buffer, executeImmediately);

            return instance;
        }

        [AllowedRPC]
        public static void CustomInstantiateRPC(string prefabId, NID nid, PID owner, PID publisher,
            Vector3 position, Quaternion rotation, byte[] instantiationData, bool buffer)
        {
            if (Registry.IsKeyInUse(nid)) return;

            GameObject prefab = NetworkPrefabs.ResolvePrefab(prefabId, publisher);
            if (prefab == null) return;

            NetworkObject prefabNetworkObject = prefab.GetComponent<NetworkObject>();
            if (prefabNetworkObject != null) InstantiationController.StoreTempData(instantiationData, prefabNetworkObject);

            GameObject instance;
            try { instance = UnityEngine.Object.Instantiate(prefab, position, rotation); }
            finally
            {
                if (prefabNetworkObject != null) InstantiationController.ClearTempData(prefabNetworkObject);
            }

            instance.SetActive(true);
            InvokeInstantiationHook(prefabId, instance);
            RegisterAndInitialize(instance, prefabId, ref nid, owner, instantiationData, buffer);
        }

        internal static void ReplayOwnedSpawns(PID requestee)
        {
            Replay(ownedSpawns, requestee);
        }

        internal static void ReplaySceneOwnedSpawns(PID requestee)
        {
            Replay(sceneOwnedSpawns, requestee);
        }

        internal static void RemoveSpawn(GameObject instance)
        {
            RemoveSpawn(ownedSpawns, instance);
            RemoveSpawn(sceneOwnedSpawns, instance);
        }

        internal static void RemoveSpawn(NetworkObject instance)
        {
            RemoveSpawn(ownedSpawns, instance);
            RemoveSpawn(sceneOwnedSpawns, instance);
        }

        internal static void ClearBuffers()
        {
            ownedSpawns.Clear();
            sceneOwnedSpawns.Clear();
        }

        private static bool RegisterAndInitialize(GameObject instance, string prefabId, ref NID nid,
            PID owner, byte[] data, bool buffer)
        {
            if (!Registry.RegisterGameObject(ref nid, instance))
            {
                UnityEngine.Object.Destroy(instance);
                return false;
            }

            InstantiationController controller = InstantiationController.Instance;
            if (owner != PID.TargetServer)
            {
                Dictionary<PID, List<GameObject>> instances =
                    HarmonyExtensions.GetFieldValue<Dictionary<PID, List<GameObject>>>(controller, "instances");
                List<GameObject> ownedByPlayer;
                if (!instances.TryGetValue(owner, out ownedByPlayer))
                {
                    ownedByPlayer = new List<GameObject>();
                    instances.Add(owner, ownedByPlayer);
                }
                ownedByPlayer.Add(instance);
            }

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                if (buffer && owner == PID.MyID)
                    ownedSpawns.Add(MakeBufferedSpawn(prefabId, nid, owner, instance, networkObject, data));
                else if (buffer && owner == PID.TargetServer)
                    sceneOwnedSpawns.Add(MakeBufferedSpawn(prefabId, nid, owner, instance, networkObject, data));

                networkObject.NetworkSetup(owner);
            }
            return true;
        }

        private static BufferedSpawn MakeBufferedSpawn(string prefabId, NID nid, PID owner, GameObject instance,
            NetworkObject networkObject, byte[] data)
        {
            return new BufferedSpawn
            {
                PrefabId = prefabId,
                Nid = nid,
                Owner = owner,
                GameObject = instance,
                NetworkObject = networkObject,
                InstantiationData = data
            };
        }

        private static byte[] SerializeInstantiationData(object[] data)
        {
            if (data == null) data = new object[0];

            List<Type> dataTypes = new List<Type>();
            foreach (object value in data) dataTypes.Add(value == null ? typeof(object) : value.GetType());
            return TypeSerializer.SerializeParameterList(dataTypes.ToArray(), data);
        }

        private static void Replay(List<BufferedSpawn> spawns, PID requestee)
        {
            for (int i = spawns.Count - 1; i >= 0; i--)
                if (spawns[i].GameObject == null || spawns[i].NetworkObject == null) spawns.RemoveAt(i);

            foreach (BufferedSpawn spawn in spawns)
            {
                Transform transform = spawn.NetworkObject.transform;
                Networking.Networking.AdminRPC<string, NID, PID, PID, Vector3, Quaternion, byte[], bool>(
                    requestee, false,
                    new RpcSignature<string, NID, PID, PID, Vector3, Quaternion, byte[], bool>(CustomInstantiateRPC),
                    spawn.PrefabId, spawn.Nid, spawn.Owner, PID.MyID,
                    transform.position, transform.rotation, spawn.InstantiationData, true);
            }
        }

        private static void RemoveSpawn(List<BufferedSpawn> spawns, GameObject instance)
        {
            for (int i = spawns.Count - 1; i >= 0; i--)
                if (ReferenceEquals(spawns[i].GameObject, instance)) spawns.RemoveAt(i);
        }

        private static void RemoveSpawn(List<BufferedSpawn> spawns, NetworkObject instance)
        {
            for (int i = spawns.Count - 1; i >= 0; i--)
                if (ReferenceEquals(spawns[i].NetworkObject, instance)) spawns.RemoveAt(i);
        }

        private static void InvokeInstantiationHook(string prefabId, GameObject instance)
        {
            Action<GameObject> hook = NetworkPrefabs.GetInstantiationHook(prefabId);
            if (hook != null) hook(instance);
        }
    }

    internal static class NetworkedInstantiatePatch
    {
        private static bool Prefix(PID playerID, UnityEngine.Object Prefab, Vector3 position, Quaternion rotation,
            object[] instantiationData, bool bufferThisRPC, bool executeImmediately, ref UnityEngine.Object __result)
        {
            GameObject prefab = GetGameObject(Prefab);
            string prefabId = NetworkPrefabs.GetIdForPrefab(prefab);
            if (prefabId == null) return true;

            GameObject instance = CustomInstantiation.Spawn(prefabId, playerID, prefab, position, rotation,
                instantiationData, bufferThisRPC, executeImmediately);
            // Use the source object's runtime component type instead of the closed patch target type.
            Component component = Prefab as Component;
            __result = component == null ? (UnityEngine.Object)instance : instance.GetComponent(component.GetType());
            return false;
        }

        private static GameObject GetGameObject(UnityEngine.Object prefab)
        {
            GameObject gameObject = prefab as GameObject;
            if (gameObject != null) return gameObject;
            Component component = prefab as Component;
            return component == null ? null : component.gameObject;
        }
    }

    [HarmonyPatch(typeof(InstantiationController), "SendInstantiatedPrefabs")]
    internal static class SendCustomOwnedSpawnsPatch
    {
        private static void Postfix([HarmonyArgument(0)] PID requestee)
        {
            CustomInstantiation.ReplayOwnedSpawns(requestee);
        }
    }

    [HarmonyPatch(typeof(InstantiationController), "SendSceneOwnedObjects")]
    internal static class SendCustomSceneOwnedSpawnsPatch
    {
        private static void Postfix([HarmonyArgument(0)] PID requestee)
        {
            CustomInstantiation.ReplaySceneOwnedSpawns(requestee);
        }
    }

    [HarmonyPatch(typeof(InstantiationController), "Destroy_Local")]
    internal static class DestroyCustomSpawnBufferPatch
    {
        private static void Postfix([HarmonyArgument(0)] GameObject instance)
        {
            CustomInstantiation.RemoveSpawn(instance);
        }
    }

    [HarmonyPatch(typeof(NetworkObject), "OnDestroy")]
    internal static class DestroyCustomSpawnObjectBufferPatch
    {
        private static void Postfix(NetworkObject __instance)
        {
            CustomInstantiation.RemoveSpawn(__instance);
        }
    }

    [HarmonyPatch(typeof(InstantiationController), "Clear")]
    internal static class ClearCustomSpawnBuffersPatch
    {
        private static void Postfix()
        {
            CustomInstantiation.ClearBuffers();
        }
    }

    [HarmonyPatch(typeof(Registry), "Reset")]
    internal static class ResetCustomSpawnBuffersPatch
    {
        private static void Postfix()
        {
            CustomInstantiation.ClearBuffers();
        }
    }

    [HarmonyPatch(typeof(GameModeController), "Start")]
    internal static class ClearCustomFactoryFailuresOnLevelStartPatch
    {
        private static void Prefix()
        {
            NetworkPrefabs.ClearLevelFailureCache();
        }
    }

    [HarmonyPatch(typeof(MainMenu), "Start")]
    internal static class ClearCustomPrefabSessionStateOnMainMenuPatch
    {
        private static void Postfix()
        {
            NetworkPrefabs.ClearSessionState();
        }
    }
}
