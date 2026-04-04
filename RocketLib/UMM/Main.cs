using System;
using System.Reflection;
using HarmonyLib;
using RocketLib.Loggers;
using UnityEngine;
using UnityModManagerNet;

namespace RocketLib.UMM
{
    internal static class Main
    {
        public static bool Enabled;
        public static Harmony harmony;
        public static UnityModManager.ModEntry Mod;
        public static Settings settings;

        static RocketLib.Loggers.ILogger Logger
        {
            get => RocketMain.Logger;
        }

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = ModUI.OnGui;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;
            modEntry.CustomRequirements = MakeUSAColorOnBroforce();

            settings = Settings.Load<Settings>(modEntry);

            try
            {
                harmony = new Harmony(modEntry.Info.Id);
                var assembly = Assembly.GetExecutingAssembly();
                harmony.PatchAll(assembly);
            }
            catch (Exception ex)
            {
                Mod.Logger.LogException("Error while applying RocketLib patches: ", ex);
            }

            try
            {
                RocketMain.Load(Mod);
                // Load ScreenLogger
                if (settings.ShowLogOnScreen)
                {
                    ScreenLogger.Load();
                }
            }
            catch (Exception ex)
            {
                Mod.Logger.LogException("Error while Loading RocketLib:", ex);
            }

            try
            {
                UMM.Mod.Load();
            }
            catch (Exception e)
            {
                Mod.Logger.LogException(e);
            }

            // Initialize ModOptionsMenu to show in menus
            RocketLib.Menus.Vanilla.ModOptionsMenu.Initialize();

            if (settings.EnableDebugging)
                RegisterTestMenus();

            return true;
        }

        internal static void RegisterTestMenus()
        {
            try
            {
                // Vanilla-style submenu examples
                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.VanillaSubmenuExample>(
                    displayText: "TEST VANILLA SUBMENU",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.MainMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "START",
                    priority: 100
                );

                // FlexMenu examples
                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.BasicFlexMenuExample>(
                    displayText: "TEST BASIC FLEXMENU",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.MainMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "START",
                    priority: 99
                );

                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.GridLayoutExample>(
                    displayText: "TEST GRID LAYOUT",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.MainMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "START",
                    priority: 98
                );

                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.PaginatedGridExample>(
                    displayText: "TEST PAGINATED GRID",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.MainMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "START",
                    priority: 97
                );

                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.TransitionTestMenu>(
                    displayText: "TEST TRANSITIONS",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.MainMenu,
                    position: RocketLib.Menus.Core.PositionMode.After,
                    positionReference: "START",
                    priority: 96
                );

                // Mod options example
                RocketLib.Menus.Core.MenuRegistry.RegisterMenu<RocketLib.Menus.Tests.ModOptionsExample>(
                    displayText: "Test Mod Options",
                    targetMenu: RocketLib.Menus.Core.TargetMenu.ModOptions
                );

                Logger.Log("Test menus registered successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to register test menus: " + ex.ToString());
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
            settings.Save(modEntry);
        }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            if (!LevelEditorGUI.IsActive)
                ShowMouseController.ShowMouse = false;
            Cursor.lockState = CursorLockMode.None;

            UMM.Mod.Update();
        }
        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            return true;
        }
    }

    public class Settings : UnityModManager.ModSettings
    {
        // ScreenLogger Option
        public bool ShowLogOnScreen = false;
        public bool ShowManagerLog = true;
        public float LogTimer = 3;
        public int FontSize = 13;
        public bool ShowDebugLogs = false;

        // UI Options
        public bool ScaleUIWithWindowWidth = true;

        // Debug Options
        public bool EnableDebugging = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Mod.save.Save();
            Save(this, modEntry);
        }
    }
}

