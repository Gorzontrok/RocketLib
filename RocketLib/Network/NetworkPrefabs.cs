using System;
using System.Collections.Generic;
using System.IO;
using Networking;
using UnityEngine;

namespace RocketLib.Network
{
    public sealed class PrefabIdEntry
    {
        public string Id { get; private set; }
        public bool ComponentsKnown { get; private set; }
        public string[] ComponentNames { get; private set; }

        public PrefabIdEntry(string id)
            : this(id, true, new string[0])
        {
        }

        public PrefabIdEntry(string id, string[] componentNames)
            : this(id, componentNames != null, componentNames ?? new string[0])
        {
        }

        internal PrefabIdEntry(string id, bool componentsKnown, string[] componentNames)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Prefab ID is required", "id");
            Id = id;
            ComponentsKnown = componentsKnown;
            ComponentNames = componentNames == null ? new string[0] : (string[])componentNames.Clone();
        }
    }

    public static class NetworkPrefabs
    {
        private static readonly Dictionary<string, Func<GameObject>> factories = new Dictionary<string, Func<GameObject>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, GameObject> cachedPrefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private static readonly Dictionary<GameObject, string> instanceToId = new Dictionary<GameObject, string>();
        private static readonly Dictionary<string, Func<string, PID, GameObject>> resolvers = new Dictionary<string, Func<string, PID, GameObject>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Action<GameObject>> idHooks = new Dictionary<string, Action<GameObject>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Action<GameObject>> prefixHooks = new Dictionary<string, Action<GameObject>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> failedFactories = new Dictionary<string, string>(StringComparer.Ordinal);

        public static event Action<string, PID, string> OnResolveFailed;

        static NetworkPrefabs()
        {
            ModNegotiation.OnSessionEnded += ClearSessionState;
        }

        public static void RegisterFactory(string id, Func<GameObject> factory, Action<GameObject> onInstantiated = null)
        {
            RequireId(id, "id");
            if (factory == null) throw new ArgumentNullException("factory");

            factories[id] = factory;
            cachedPrefabs.Remove(id);
            failedFactories.Remove(id);
            if (onInstantiated != null) idHooks[id] = onInstantiated;
        }

        public static void RegisterInstance(string id, GameObject prefab, Action<GameObject> onInstantiated = null)
        {
            RequireId(id, "id");
            if (prefab == null) throw new ArgumentNullException("prefab");

            RemoveMappingsForId(id);
            instanceToId[prefab] = id;
            if (onInstantiated != null) idHooks[id] = onInstantiated;
            CustomInstantiation.EnsureGenericRoutePatches(prefab);
        }

        public static void RegisterResolver(string prefix, Func<string, PID, GameObject> resolver, Action<GameObject> onInstantiated = null)
        {
            RequireId(prefix, "prefix");
            if (resolver == null) throw new ArgumentNullException("resolver");

            resolvers[prefix] = resolver;
            if (onInstantiated != null) prefixHooks[prefix] = onInstantiated;
        }

        public static bool IsRegistered(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string prefix;
            return factories.ContainsKey(id) || FindResolver(id, out prefix) != null;
        }

        public static bool AllPlayersHavePrefab(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            foreach (PID player in ModNegotiation.GetAllPlayers())
            {
                if (player == PID.MyID) continue;

                string[] modIds = ModNegotiation.GetManifestModIds(player);
                if (modIds == null) return false;

                bool found = false;
                foreach (string modId in modIds)
                {
                    byte[] data = ModNegotiation.GetExtensionData(player, modId, "prefabs");
                    if (data == null) continue;

                    PrefabIdEntry[] entries;
                    if (!TryDeserializeIdList(data, out entries)) return false;
                    foreach (PrefabIdEntry entry in entries)
                    {
                        if (entry.Id == id)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }
                if (!found) return false;
            }
            return true;
        }

        public static string[] GetRegisteredIds()
        {
            HashSet<string> ids = new HashSet<string>(factories.Keys, StringComparer.Ordinal);
            foreach (string id in instanceToId.Values) ids.Add(id);
            List<string> ordered = new List<string>(ids);
            ordered.Sort(StringComparer.Ordinal);
            return ordered.ToArray();
        }

        public static byte[] SerializeIdList(IEnumerable<PrefabIdEntry> entries)
        {
            List<PrefabManifestEntry> manifestEntries = new List<PrefabManifestEntry>();
            foreach (PrefabIdEntry entry in entries)
                manifestEntries.Add(new PrefabManifestEntry(entry.Id, entry.ComponentsKnown, entry.ComponentNames));
            return ModNegotiation.SerializePrefabEntries(manifestEntries);
        }

        public static PrefabIdEntry[] DeserializeIdList(byte[] data)
        {
            PrefabManifestEntry[] entries = ModNegotiation.DeserializePrefabEntries(data);
            PrefabIdEntry[] result = new PrefabIdEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                result[i] = new PrefabIdEntry(entries[i].Id, entries[i].ComponentsKnown, entries[i].ComponentNames);
            return result;
        }

        public static PrefabIdEntry GetPrefabEntry(string id, PID publisher)
        {
            if (string.IsNullOrEmpty(id) || publisher == null) return null;

            string[] modIds = ModNegotiation.GetManifestModIds(publisher);
            if (modIds == null) return null;

            foreach (string modId in modIds)
            {
                byte[] data = ModNegotiation.GetExtensionData(publisher, modId, "prefabs");
                if (data == null) continue;

                PrefabIdEntry[] entries;
                if (!TryDeserializeIdList(data, out entries)) return null;
                foreach (PrefabIdEntry entry in entries)
                    if (entry.Id == id) return entry;
            }
            return null;
        }

        internal static string GetIdForPrefab(GameObject prefab)
        {
            if (prefab == null) return null;
            string id;
            return instanceToId.TryGetValue(prefab, out id) ? id : null;
        }

        internal static Action<GameObject> GetInstantiationHook(string id)
        {
            Action<GameObject> hook;
            if (idHooks.TryGetValue(id, out hook)) return hook;

            Func<string, PID, GameObject> ignored;
            string matchingPrefix;
            FindResolver(id, out matchingPrefix, out ignored);
            return matchingPrefix != null && prefixHooks.TryGetValue(matchingPrefix, out hook) ? hook : null;
        }

        internal static GameObject ResolvePrefab(string id, PID publisher)
        {
            if (string.IsNullOrEmpty(id)) return ResolveFailed(id, publisher, "No factory or resolver handles this ID");

            Func<GameObject> factory;
            Func<string, PID, GameObject> resolver;
            string reason;

            if (!factories.TryGetValue(id, out factory))
            {
                factory = null;
                resolver = FindResolver(id, out reason);
                if (resolver == null) return ResolveFailed(id, publisher, "No factory or resolver handles this ID");
            }
            else resolver = null;

            PrefabIdEntry entry = GetPrefabEntry(id, publisher);
            if (entry == null) return ResolveFailed(id, publisher, "Publisher manifest has no valid prefab entry");
            if (!entry.ComponentsKnown) return ResolveFailed(id, publisher, "Publisher manifest marks components as unknown");

            if (factory != null)
            {
                if (failedFactories.TryGetValue(id, out reason))
                    return ResolveFailed(id, publisher, "Factory previously threw: " + reason);

                GameObject cached;
                if (cachedPrefabs.TryGetValue(id, out cached))
                {
                    if (cached != null) return cached;
                    cachedPrefabs.Remove(id);
                }

                GameObject created;
                try { created = factory(); }
                catch (Exception exception)
                {
                    reason = exception.GetType().FullName + ": " + exception.Message;
                    failedFactories[id] = reason;
                    return ResolveFailed(id, publisher, "Factory threw: " + exception);
                }
                if (created == null) return ResolveFailed(id, publisher, "Factory returned null");

                cachedPrefabs[id] = created;
                return created;
            }

            GameObject resolved;
            try { resolved = resolver(id, publisher); }
            catch (Exception exception)
            {
                return ResolveFailed(id, publisher, "Resolver threw: " + exception);
            }
            return resolved == null ? ResolveFailed(id, publisher, "Resolver returned null") : resolved;
        }

        internal static void ClearSessionState()
        {
            foreach (GameObject prefab in cachedPrefabs.Values)
                if (prefab != null) UnityEngine.Object.Destroy(prefab);
            cachedPrefabs.Clear();
            failedFactories.Clear();
            CustomInstantiation.ClearBuffers();
        }

        internal static void ClearLevelFailureCache()
        {
            failedFactories.Clear();
        }

        private static GameObject ResolveFailed(string id, PID publisher, string reason)
        {
            if (RocketMain.Logger != null)
                RocketMain.Logger.Warning("[NetworkPrefabs] Resolve failed for " + id + " from " + publisher + ": " + reason);
            if (OnResolveFailed != null) OnResolveFailed(id, publisher, reason);
            return null;
        }

        private static Func<string, PID, GameObject> FindResolver(string id, out string prefix)
        {
            Func<string, PID, GameObject> resolver;
            return FindResolver(id, out prefix, out resolver) ? resolver : null;
        }

        private static bool TryDeserializeIdList(byte[] data, out PrefabIdEntry[] entries)
        {
            try
            {
                entries = DeserializeIdList(data);
                return true;
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (FormatException)
            {
            }

            entries = null;
            return false;
        }

        private static bool FindResolver(string id, out string prefix, out Func<string, PID, GameObject> resolver)
        {
            prefix = null;
            resolver = null;
            int longest = -1;
            if (id == null) return false;

            foreach (KeyValuePair<string, Func<string, PID, GameObject>> candidate in resolvers)
            {
                if (candidate.Key.Length > longest && id.StartsWith(candidate.Key, StringComparison.Ordinal))
                {
                    longest = candidate.Key.Length;
                    prefix = candidate.Key;
                    resolver = candidate.Value;
                }
            }
            return resolver != null;
        }

        private static void RemoveMappingsForId(string id)
        {
            List<GameObject> oldPrefabs = new List<GameObject>();
            foreach (KeyValuePair<GameObject, string> mapping in instanceToId)
                if (mapping.Value == id) oldPrefabs.Add(mapping.Key);
            foreach (GameObject prefab in oldPrefabs) instanceToId.Remove(prefab);
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A non-empty prefab ID is required", parameterName);
        }
    }
}
