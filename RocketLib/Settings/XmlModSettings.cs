using System;
using System.IO;
using System.Xml.Serialization;
using UnityModManagerNet;

namespace RocketLib.Settings
{
    /// <summary>
    /// Base class for XML-based mod settings with automatic recovery from corrupted or incompatible files.
    /// Inherits from UnityModManager.ModSettings and adds automatic recovery when deserialization fails.
    /// </summary>
    /// <example>
    ///     <code>
    /// public class Settings : XmlModSettings
    /// {
    ///     public override int SettingsVersion { get; set; } = 1;
    ///     public bool MyOption = true;
    ///     public int MyValue = 42;
    /// }
    /// 
    /// // In your mod's Load method:
    /// settings = XmlModSettings.Load&lt;Settings&gt;(modEntry);
    /// </code>
    /// </example>
    /// <remarks>
    /// Settings are saved to the mod's ConfigPath directory (persists across Thunderstore updates).
    /// When the settings file exists but can't be deserialized, recovery is attempted
    /// using <see cref="SettingsRecovery.TryRecoverSettings{T}" />.
    /// </remarks>
    public abstract class XmlModSettings : UnityModManager.ModSettings
    {
        /// <summary>
        /// Version number for tracking settings schema changes.
        /// </summary>
        public virtual int SettingsVersion { get; set; } = 1;

        /// <summary>
        /// Loads settings from the mod's ConfigPath, automatically recovering data if deserialization fails.
        /// </summary>
        /// <typeparam name="T">The settings class type, must inherit from XmlModSettings</typeparam>
        /// <param name="modEntry">The mod entry to load settings for</param>
        /// <returns>The loaded settings, or recovered/default settings if loading failed</returns>
        public static new T Load<T>(UnityModManager.ModEntry modEntry) where T : XmlModSettings, new()
        {
            T settings = new T();
            string settingsPath = settings.GetPath(modEntry);
            if (!File.Exists(settingsPath))
            {
                return settings;
            }

            try
            {
                using (FileStream stream = File.OpenRead(settingsPath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(T));
                    return (T)serializer.Deserialize(stream);
                }
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Warning($"Couldn't read {settingsPath} ({ex.Message}), recovering what it still holds and using defaults for the rest.");
                return SettingsRecovery.TryRecoverSettings<T>(settingsPath, settings);
            }
        }
    }
}
