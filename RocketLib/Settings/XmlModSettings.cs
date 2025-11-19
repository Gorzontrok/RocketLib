using UnityModManagerNet;

namespace RocketLib
{
    public abstract class XmlModSettings : UnityModManager.ModSettings
    {
        public virtual int SettingsVersion { get; set; } = 1;

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
