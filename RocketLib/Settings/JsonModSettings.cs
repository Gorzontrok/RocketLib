using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;

namespace RocketLib.Settings
{
    /// <summary>
    /// Base class for JSON-based mod settings with built-in migration support for schema changes.
    /// Provides automatic versioning, migration, and backup on failure.
    /// </summary>
    /// <example>
    /// <code>
    /// public class Settings : JsonModSettings
    /// {
    ///     protected override int CurrentVersion => 2;
    ///     protected override string FileName => "MyModSettings.json";
    ///
    ///     public bool MyOption = true;
    ///     public string NewField = "default";
    ///
    ///     protected override void MigrateJson(JObject json, int fromVersion)
    ///     {
    ///         if (fromVersion &lt; 2)
    ///         {
    ///             json["NewField"] = "migrated_value";
    ///         }
    ///     }
    /// }
    ///
    /// // In your mod's Load method:
    /// settings = JsonModSettings.Load&lt;Settings&gt;(modEntry);
    /// </code>
    /// </example>
    /// <remarks>
    /// Settings are saved to the mod's ConfigPath directory (persists across Thunderstore updates).
    /// When loading settings with an older version, MigrateJson is called to transform the data,
    /// and the migrated settings are automatically saved. If migration fails, the original file
    /// is backed up and default settings are returned.
    /// </remarks>
    public class JsonModSettings
    {
        /// <summary>
        /// Version number stored in the settings file. Used to detect when migration is needed.
        /// </summary>
        public virtual int SettingsVersion { get; set; } = 1;

        /// <summary>
        /// The current schema version expected by the code. Override to increment when making breaking changes.
        /// When file version is less than this, MigrateJson is called.
        /// </summary>
        protected virtual int CurrentVersion => 1;

        /// <summary>
        /// The filename for the settings file. Override to use a custom name.
        /// </summary>
        protected virtual string FileName => "Settings.json";

        /// <summary>
        /// Gets the full path to the settings file.
        /// </summary>
        /// <param name="modEntry">The mod entry</param>
        /// <returns>Full path to the settings file in the mod's ConfigPath</returns>
        public virtual string GetPath(UnityModManager.ModEntry modEntry)
        {
            return Path.Combine(modEntry.ConfigPath, FileName);
        }

        /// <summary>
        /// Saves the current settings to disk.
        /// </summary>
        /// <param name="modEntry">The mod entry to save settings for</param>
        public virtual void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        /// <summary>
        /// Override to handle migration from older settings versions.
        /// Modify the JObject directly to transform old data to the new schema.
        /// </summary>
        /// <param name="json">The parsed JSON object to migrate</param>
        /// <param name="fromVersion">The version number found in the file</param>
        /// <remarks>
        /// Migration uses a direct-jump pattern: migrate from any old version to current in one pass.
        /// Use cascading if-statements (not else-if) to apply all necessary migrations.
        /// </remarks>
        protected virtual void MigrateJson(JObject json, int fromVersion)
        {
        }

        /// <summary>
        /// Saves settings data to the mod's ConfigPath.
        /// </summary>
        /// <typeparam name="T">The settings class type</typeparam>
        /// <param name="data">The settings object to save</param>
        /// <param name="modEntry">The mod entry to save settings for</param>
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

        /// <summary>
        /// Loads settings from the mod's ConfigPath, automatically migrating if the file version is older.
        /// </summary>
        /// <typeparam name="T">The settings class type, must inherit from JsonModSettings</typeparam>
        /// <param name="modEntry">The mod entry to load settings for</param>
        /// <returns>The loaded settings, migrated settings, or default settings if loading failed</returns>
        /// <remarks>
        /// If the file version is less than CurrentVersion, MigrateJson is called and the migrated
        /// settings are automatically saved. If migration throws an exception, the original file is
        /// backed up to {filename}.bak and default settings are returned.
        /// </remarks>
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
