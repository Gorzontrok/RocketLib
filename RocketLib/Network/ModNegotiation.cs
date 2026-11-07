using System;
using System.Collections.Generic;
using System.IO;
using Networking;
using UnityEngine;

namespace RocketLib.Network
{
    public sealed class ModManifest
    {
        public string ModId { get; private set; }
        public string Version { get; private set; }
        public Dictionary<string, byte[]> ExtensionData { get; private set; }

        public ModManifest(string modId, string version)
        {
            ModId = modId;
            Version = version;
            ExtensionData = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        }
    }

    public sealed class PrefabManifestEntry
    {
        public string Id { get; private set; }
        public bool ComponentsKnown { get; private set; }
        public string[] ComponentNames { get; private set; }

        public PrefabManifestEntry(string id, bool componentsKnown, string[] componentNames)
        {
            Id = id;
            ComponentsKnown = componentsKnown;
            ComponentNames = componentNames == null ? new string[0] : (string[])componentNames.Clone();
        }
    }

    public static class ModNegotiation
    {
        private const int ExtensionWarningBytes = 64 * 1024;
        // Provisional warning; the actual RPC tolerance needs a two-peer runtime measurement.
        private const int ManifestWarningBytes = 256 * 1024;
        private static readonly Dictionary<string, ModManifest> localManifests = new Dictionary<string, ModManifest>(StringComparer.Ordinal);
        private static readonly Dictionary<PID, ModManifest[]> playerManifests = new Dictionary<PID, ModManifest[]>();
        private static readonly HashSet<PID> departedPlayers = new HashSet<PID>();
        private static readonly HashSet<PID> registeredPeers = new HashSet<PID>();
        private static bool dirty;
        private static bool completed;
        private static bool timeoutAnchored;
        private static bool timeoutStarted;
        private static float timeoutAt;

        public static event Action<PID, ModManifest[]> OnPlayerManifestReceived;
        public static event Action OnNegotiationComplete;
        public static event Action<PID> OnPlayerLeft;
        public static event Action OnSessionEnded;

        public static void RegisterMod(string modId, string version)
        {
            if (string.IsNullOrEmpty(modId) || version == null) throw new ArgumentException("Mod ID and version are required");
            ModManifest manifest;
            if (!localManifests.TryGetValue(modId, out manifest))
            {
                localManifests.Add(modId, new ModManifest(modId, version));
                ScheduleFlush();
            }
            else if (manifest.Version != version)
            {
                ModManifest replacement = new ModManifest(modId, version);
                foreach (KeyValuePair<string, byte[]> extension in manifest.ExtensionData)
                    replacement.ExtensionData.Add(extension.Key, extension.Value);
                localManifests[modId] = replacement;
                ScheduleFlush();
            }
        }

        public static void SetExtensionData(string modId, string key, byte[] data)
        {
            ModManifest manifest;
            if (!localManifests.TryGetValue(modId, out manifest)) throw new ArgumentException("Register the mod first", "modId");
            if (string.IsNullOrEmpty(key) || data == null) throw new ArgumentException("Extension key and data are required");
            byte[] previous;
            manifest.ExtensionData.TryGetValue(key, out previous);
            if (EqualBytes(previous, data)) return;
            bool immediate = key == "prefabs" && NewlyKnown(previous, data);
            byte[] copy = (byte[])data.Clone();
            manifest.ExtensionData[key] = copy;
            if (copy.Length > ExtensionWarningBytes)
                Warn("Extension " + modId + "/" + key + " exceeds 64 KiB: " + copy.Length + " bytes");
            if (immediate)
            {
                Announce(PID.TargetOthers);
                dirty = false;
            }
            else ScheduleFlush();
        }

        public static byte[] SerializeManifests(IEnumerable<ModManifest> manifests)
        {
            List<ModManifest> ordered = new List<ModManifest>(manifests);
            ordered.Sort(delegate(ModManifest a, ModManifest b) { return StringComparer.Ordinal.Compare(a.ModId, b.ModId); });
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(ordered.Count);
                foreach (ModManifest manifest in ordered)
                {
                    writer.Write(manifest.ModId);
                    writer.Write(manifest.Version);
                    List<string> keys = new List<string>(manifest.ExtensionData.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    writer.Write(keys.Count);
                    foreach (string key in keys)
                    {
                        byte[] data = manifest.ExtensionData[key];
                        writer.Write(key);
                        writer.Write(data.Length);
                        writer.Write(data);
                    }
                }
                return stream.ToArray();
            }
        }

        public static ModManifest[] DeserializeManifests(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int count = ReadCount(reader, stream);
                ModManifest[] result = new ModManifest[count];
                for (int i = 0; i < count; i++)
                {
                    ModManifest manifest = new ModManifest(reader.ReadString(), reader.ReadString());
                    int extensionCount = ReadCount(reader, stream);
                    for (int j = 0; j < extensionCount; j++)
                    {
                        string key = reader.ReadString();
                        int length = ReadCount(reader, stream);
                        byte[] payload = reader.ReadBytes(length);
                        if (payload.Length != length) throw new EndOfStreamException();
                        manifest.ExtensionData.Add(key, payload);
                    }
                    result[i] = manifest;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing manifest data");
                return result;
            }
        }

        public static byte[] SerializePrefabEntries(IEnumerable<PrefabManifestEntry> entries)
        {
            List<PrefabManifestEntry> ordered = new List<PrefabManifestEntry>(entries);
            ordered.Sort(delegate(PrefabManifestEntry a, PrefabManifestEntry b) { return StringComparer.Ordinal.Compare(a.Id, b.Id); });
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(ordered.Count);
                foreach (PrefabManifestEntry entry in ordered)
                {
                    writer.Write(entry.Id);
                    writer.Write(entry.ComponentsKnown);
                    writer.Write(entry.ComponentNames.Length);
                    foreach (string name in entry.ComponentNames) writer.Write(name);
                }
                return stream.ToArray();
            }
        }

        public static PrefabManifestEntry[] DeserializePrefabEntries(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int count = ReadCount(reader, stream);
                PrefabManifestEntry[] entries = new PrefabManifestEntry[count];
                for (int i = 0; i < count; i++)
                {
                    string id = reader.ReadString();
                    bool known = reader.ReadBoolean();
                    int componentCount = ReadCount(reader, stream);
                    string[] names = new string[componentCount];
                    for (int j = 0; j < componentCount; j++) names[j] = reader.ReadString();
                    entries[i] = new PrefabManifestEntry(id, known, names);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing prefab data");
                return entries;
            }
        }

        private static int ReadCount(BinaryReader reader, Stream stream)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > stream.Length - stream.Position) throw new InvalidDataException("Invalid count");
            return count;
        }

        private static bool NewlyKnown(byte[] previous, byte[] next)
        {
            Dictionary<string, bool> old = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (previous != null)
                foreach (PrefabManifestEntry entry in DeserializePrefabEntries(previous)) old[entry.Id] = entry.ComponentsKnown;
            foreach (PrefabManifestEntry entry in DeserializePrefabEntries(next))
            {
                bool wasKnown;
                if (entry.ComponentsKnown && old.TryGetValue(entry.Id, out wasKnown) && !wasKnown) return true;
            }
            return false;
        }

        private static bool EqualBytes(byte[] a, byte[] b)
        {
            if (a == b) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static ModManifest Find(PID player, string modId)
        {
            if (player == null || modId == null) return null;
            if (player == PID.MyID)
            {
                ModManifest local;
                return localManifests.TryGetValue(modId, out local) ? local : null;
            }
            ModManifest[] manifests;
            if (playerManifests.TryGetValue(player, out manifests))
                foreach (ModManifest manifest in manifests) if (manifest.ModId == modId) return manifest;
            return null;
        }

        public static bool HasMod(PID player, string modId) { return Find(player, modId) != null; }
        public static string GetModVersion(PID player, string modId)
        {
            ModManifest manifest = Find(player, modId);
            return manifest == null ? null : manifest.Version;
        }
        public static byte[] GetExtensionData(PID player, string modId, string key)
        {
            ModManifest manifest = Find(player, modId);
            byte[] data;
            return manifest != null && key != null && manifest.ExtensionData.TryGetValue(key, out data) ? (byte[])data.Clone() : null;
        }
        public static PID[] GetAllPlayers()
        {
            List<PID> players = new List<PID>();
            if (Connect.LayerExists)
                foreach (PID pid in Connect.Layer.PlayerIDPairs.Keys) players.Add(pid);
            return players.ToArray();
        }

        internal static string[] GetManifestModIds(PID player)
        {
            if (player == null) return null;

            ModManifest[] manifests;
            if (player == PID.MyID)
                manifests = new List<ModManifest>(localManifests.Values).ToArray();
            else if (!playerManifests.TryGetValue(player, out manifests))
                return null;

            string[] ids = new string[manifests.Length];
            for (int i = 0; i < manifests.Length; i++) ids[i] = manifests[i].ModId;
            return ids;
        }

        public static bool AllPlayersHaveMod(string modId)
        {
            foreach (PID pid in GetAllPlayers())
                if (pid != PID.MyID && !HasMod(pid, modId)) return false;
            return true;
        }

        [AllowedRPC]
        public static void AnnounceModListRPC(PID sender, byte[] serializedManifests)
        {
            if (sender == null || sender == PID.MyID || serializedManifests == null) return;
            ModManifest[] incoming;
            try { incoming = DeserializeManifests(serializedManifests); }
            catch (Exception ex) { Warn("Invalid manifest from " + sender + ": " + ex.Message); return; }
            ModManifest[] previous;
            bool existed = playerManifests.TryGetValue(sender, out previous);
            if (existed && EqualBytes(SerializeManifests(previous), SerializeManifests(incoming))) return;
            playerManifests[sender] = incoming;
            departedPlayers.Remove(sender);
            if (OnPlayerManifestReceived != null) OnPlayerManifestReceived(sender, incoming);
            if (!existed)
            {
                if (completed) FireComplete();
                else TryComplete();
            }
        }

        [AllowedRPC]
        public static void RequestModListRPC(PID requester)
        {
            if (requester != null && requester != PID.MyID) Announce(requester);
        }

        internal static void BeginHandshake(PID pid)
        {
            if (pid == null || pid == PID.MyID || Connect.IsOffline || !Connect.LayerExists) return;
            IDWrapper wrapper;
            if (!Connect.Layer.PlayerIDPairs.TryGetValue(pid, out wrapper) || wrapper == null || wrapper.haveRequestedSyncStateFromPlayer) return;
            Announce(pid);
            Networking.Networking.AdminRPC<PID>(pid, true, new RpcSignature<PID>(RequestModListRPC), PID.MyID);
        }

        private static void Announce(PID target)
        {
            if (!Connect.LayerExists || Connect.IsOffline) return;
            byte[] bytes = SerializeManifests(localManifests.Values);
            if (bytes.Length > ManifestWarningBytes) Warn("Manifest exceeds provisional 256 KiB RPC warning threshold: " + bytes.Length + " bytes; live limit not measured");
            Networking.Networking.AdminRPC<PID, byte[]>(target, true, new RpcSignature<PID, byte[]>(AnnounceModListRPC), PID.MyID, bytes);
        }

        private static void ScheduleFlush()
        {
            dirty = true;
            if (Application.isPlaying) NegotiationHost.EnsureInstance();
        }
        internal static void Flush()
        {
            if (!dirty) return;
            dirty = false;
            Announce(PID.TargetOthers);
        }
        internal static void PeerRegistered(PID pid)
        {
            if (pid == null || pid == PID.MyID || Connect.IsOffline || !Connect.LayerExists || !registeredPeers.Add(pid)) return;
            departedPlayers.Remove(pid);
            if (!timeoutAnchored)
            {
                timeoutAnchored = true;
                timeoutStarted = true;
                timeoutAt = Time.realtimeSinceStartup + 3f;
            }
            NegotiationHost.EnsureInstance();
            TryComplete();
        }
        internal static void TickTimeout()
        {
            if (timeoutStarted && Time.realtimeSinceStartup >= timeoutAt)
            {
                timeoutStarted = false;
                FireComplete();
            }
        }
        internal static void LevelStarted()
        {
            if (!completed && (Connect.IsOffline || NoRemotePeers())) FireComplete();
        }
        private static bool NoRemotePeers()
        {
            foreach (PID pid in GetAllPlayers()) if (pid != PID.MyID) return false;
            return true;
        }
        private static void TryComplete()
        {
            if (NoRemotePeers()) return;
            foreach (PID pid in GetAllPlayers())
                if (pid != PID.MyID && !playerManifests.ContainsKey(pid)) return;
            FireComplete();
        }
        private static void FireComplete()
        {
            completed = true;
            if (OnNegotiationComplete != null) OnNegotiationComplete();
        }
        internal static void PlayerLeft(PID pid)
        {
            if (pid == null || pid == PID.NoID || pid == PID.MyID || !departedPlayers.Add(pid)) return;
            registeredPeers.Remove(pid);
            bool hadManifest = playerManifests.Remove(pid);
            if (OnPlayerLeft != null) OnPlayerLeft(pid);
            if (hadManifest && (completed || NoRemotePeers() || AllRegisteredPeersReported())) FireComplete();
        }
        private static bool AllRegisteredPeersReported()
        {
            foreach (PID pid in GetAllPlayers())
                if (pid != PID.MyID && !playerManifests.ContainsKey(pid)) return false;
            return true;
        }
        internal static void SessionEnded()
        {
            playerManifests.Clear();
            departedPlayers.Clear();
            registeredPeers.Clear();
            timeoutAnchored = false;
            timeoutStarted = false;
            completed = false;
            dirty = false;
            if (OnSessionEnded != null) OnSessionEnded();
        }
        private static void Warn(string message)
        {
            if (RocketMain.Logger != null) RocketMain.Logger.Warning("[Negotiation] " + message);
        }
    }
}
