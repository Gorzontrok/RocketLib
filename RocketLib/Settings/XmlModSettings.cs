using UnityModManagerNet;

namespace RocketLib.Settings
{
    /// <summary>
    /// Base class for XML-based mod settings with automatic recovery from corrupted or incompatible files.
    /// Inherits from UnityModManager.ModSettings and adds automatic recovery when deserialization fails.
    /// </summary>
    /// <example>
    /// <code>
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
    /// When SettingsVersion is 0 after loading, it indicates deserialization failed and recovery is attempted
    /// using <see cref="SettingsRecovery.TryRecoverSettings{T}"/>.
    /// </remarks>
    public abstract class XmlModSettings : UnityModManager.ModSettings
    {
        /// <summary>
        /// Version number for tracking settings schema changes.
        /// A value of 0 after loading indicates deserialization failed.
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
            var settings = UnityModManager.ModSettings.Load<T>(modEntry);

            if (settings.SettingsVersion == 0)
            {
                var settingsPath = settings.GetPath(modEntry);
                settings = SettingsRecovery.TryRecoverSettings<T>(settingsPath, settings);

                if (settings.SettingsVersion == 0)
                    settings.SettingsVersion = 1;
            }

            return settings;
        }
    }
}
