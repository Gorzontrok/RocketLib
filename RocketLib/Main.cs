using System;
using System.Reflection;
using HarmonyLib;
using RocketLib.Loggers;
using UnityEngine;
using UnityModManagerNet;

namespace RocketLib.UMM
{
    public class Main
    {
        public static bool enabled;
        public static Harmony harmony;
        public static UnityModManager.ModEntry mod;
        public static Settings Settings;

        internal static RLogger logger;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = ModUI.OnGui;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;
            modEntry.CustomRequirements = MakeUSAColorOnBroforce();

            Settings = Settings.Load<Settings>(modEntry);
            ScreenLogger.fontSize = Settings.FontSize;

            try
            {
                harmony = new Harmony(modEntry.Info.Id);
                var assembly = Assembly.GetExecutingAssembly();
                harmony.PatchAll(assembly);
            }
            catch (Exception ex)
            {
                logger.Exception("Error while applying RocketLib patches: ", ex);
            }

            logger = new RLogger();

            try
            {
                RocketLib.Main.logger = logger;

                RMain.Load(mod);
                RMain.showManagerLog = Settings.ShowManagerLog;
                RMain.showLogOnScreen = Settings.OnScreenLog;
                RMain.logTimer = Settings.LogTimer;

                // Load ScreenLogger
                if (Settings.OnScreenLog)
                {
                    ScreenLogger.Load();
                }
            }
            catch (Exception ex)
            {
                logger.Exception("Error while Loading RocketLib:", ex);
            }

            try
            {
                Mod.Load();
            }
            catch (Exception e)
            {
                logger.Exception(e);
            }

            // Initialize ModOptionsMenu to show in menus
            RocketLib.Menus.Vanilla.ModOptionsMenu.Initialize();

            RegisterTestMenus();

            return true;
        }

        static void RegisterTestMenus()
        {
            try
            {
                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.VanillaSubmenuExample>(
                    displayText: "TEST MAINMENU",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.MainMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "START",
                    priority: 100
                );

                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.VanillaSubmenuExample>(
                    displayText: "TEST OPTIONS MAINMENU",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.OptionsMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "CONFIGURE CONTROLS",
                    priority: 100
                );

                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.VanillaSubmenuExample>(
                    displayText: "TEST PAUSEMENU",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.PauseMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "RESUME GAME",
                    priority: 100
                );

                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.VanillaSubmenuExample>(
                    displayText: "TEST OPTIONS PAUSEMENU",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.InGameOptionsMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "BACK",
                    priority: 100
                );

                logger.Log("Test menus registered successfully");
            }
            catch (Exception ex)
            {
                logger.Error("Failed to register test menus: " + ex.ToString());
            }
        }

        static string MakeUSAColorOnBroforce()
        {
            string origCustomRequirements = "Broforce";
            string CustomRequirements = string.Empty;
            for (int i = 0; i < 3; i++)
            {
                if (i == 0)
                {
                    CustomRequirements = "<color=\"#1C59FE\">";
                }
                CustomRequirements += origCustomRequirements[i];
                if (i == 2)
                {
                    CustomRequirements += "</color>";
                }
            }
            for (int i = 3; i < origCustomRequirements.Length; i++)
            {
                if (i % 2 == 0)
                {
                    CustomRequirements += "<color=\"red\">" + origCustomRequirements[i] + "</color>";
                }
                else
                {
                    CustomRequirements += "<color=\"white\">" + origCustomRequirements[i] + "</color>";
                }
            }
            return CustomRequirements;
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.OnScreenLog = RMain.showLogOnScreen;
            Settings.ShowManagerLog = RMain.showManagerLog;
            Settings.LogTimer = RMain.logTimer;
            Settings.Save(modEntry);
        }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            if (!LevelEditorGUI.IsActive)
                ShowMouseController.ShowMouse = false;
            Cursor.lockState = CursorLockMode.None;

            Mod.Update();
        }
        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            return true;
        }
    }

    public class Settings : UnityModManager.ModSettings
    {
        // ScreenLogger Option
        public bool OnScreenLog = false;
        public bool ShowManagerLog = true;
        public float LogTimer = 3;
        public int FontSize = 13;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Mod.save.Save();
            Save(this, modEntry);
        }
    }

    
}

