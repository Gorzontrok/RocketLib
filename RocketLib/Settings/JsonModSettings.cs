using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;

namespace RocketLib.Settings
{
    public class JsonModSettings
    {
        public virtual int SettingsVersion { get; set; } = 1;

        protected virtual int CurrentVersion => 1;

        protected virtual string FileName => "Settings.json";

        public virtual string GetPath(UnityModManager.ModEntry modEntry)
        {
            return Path.Combine(modEntry.ConfigPath, FileName);
        }

        public virtual void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        protected virtual void MigrateJson(JObject json, int fromVersion)
        {
        }

        public static void Save<T>(T data, UnityModManager.ModEntry modEntry) where T : JsonModSettings, new()
        {
            Directory.CreateDirectory(modEntry.ConfigPath);
            var filepath = data.GetPath(modEntry);
            try
            {
                var settings = new JsonSerializerSettings()
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Formatting = Formatting.Indented
                };
                var json = JsonConvert.SerializeObject(data, settings);
                File.WriteAllText(filepath, json);
            }
            catch (Exception e)
            {
                modEntry.Logger.Error($"Can't save {filepath}.");
                modEntry.Logger.LogException(e);
            }
        }

        public static T Load<T>(UnityModManager.ModEntry modEntry) where T : JsonModSettings, new()
        {
            var t = new T();
            var filepath = t.GetPath(modEntry);

            if (!File.Exists(filepath))
                return t;

            try
            {
                var json = File.ReadAllText(filepath);
                var jObject = JObject.Parse(json);

                var fileVersion = jObject["SettingsVersion"]?.Value<int>() ?? 0;

                if (fileVersion < t.CurrentVersion)
                {
                    modEntry.Logger.Log($"Migrating settings from v{fileVersion} to v{t.CurrentVersion}");

                    try
                    {
                        t.MigrateJson(jObject, fileVersion);
                        jObject["SettingsVersion"] = t.CurrentVersion;
                    }
                    catch (Exception migrationEx)
                    {
                        modEntry.Logger.Error($"Migration failed: {migrationEx.Message}");
                        modEntry.Logger.LogException(migrationEx);

                        var backupPath = filepath + ".bak";
                        File.Copy(filepath, backupPath, true);
                        modEntry.Logger.Log($"Backed up old settings to {backupPath}");

                        return t;
                    }
                }

                var result = jObject.ToObject<T>();

                if (fileVersion < t.CurrentVersion && result != null)
                {
                    result.Save(modEntry);
                }

                return result ?? t;
            }
            catch (Exception e)
            {
                modEntry.Logger.Error($"Can't read {filepath}.");
                modEntry.Logger.LogException(e);
            }

            return t;
        }
    }
}
