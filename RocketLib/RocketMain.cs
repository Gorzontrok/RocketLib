using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;
using RocketLib.Loggers;
using RocketLib.Menus.Core;
using RocketLib.Menus.Tests;

namespace RocketLib
{
    public static class RocketMain
    {
        public const string NEWTONSOFT_ASSEMBLY_NAME = "Newtonsoft.Json.dll";

        /// <summary>
        /// Is RocketLib loaded
        /// </summary>
        public static bool Loaded { get; private set; } = false;

        internal static ILogger Logger;

        public static void Load(UnityModManager.ModEntry _mod)
        {
            if (Loaded)
            {
                Logger.Log("Cancel Load, already Started.");
                return;
            }
            Logger = new Logger();

            // Load Newtonsoft
            try
            {
                Assembly.LoadFile(Path.Combine(UMM.Main.Mod.Path, NEWTONSOFT_ASSEMBLY_NAME));
            }
            catch (Exception ex)
            {
                Logger.Exception("Error while loading Newtonsoft.Json", ex);
            }

            Loaded = true;

            // Uncomment to enable test menus:
            //RegisterTestMenus();
        }

        private static void RegisterTestMenus()
        {
            MenuRegistry.RegisterMenu<BasicFlexMenuExample>(
                displayText: "Basic Flex Menu Test",
                targetMenu: TargetMenu.MainMenu,
                positionReference: "OPTIONS"
            );

            MenuRegistry.RegisterMenu<VanillaSubmenuExample>(
                displayText: "Vanilla Submenu Test",
                targetMenu: TargetMenu.MainMenu,
                positionReference: "OPTIONS"
            );

            MenuRegistry.RegisterMenu<ModOptionsExample>(
                displayText: "Test Mod Options",
                targetMenu: TargetMenu.ModOptions
            );

            MenuRegistry.RegisterMenu<GridLayoutExample>(
                displayText: "Grid Layout Test",
                targetMenu: TargetMenu.MainMenu,
                positionReference: "OPTIONS"
            );

            MenuRegistry.RegisterMenu<PaginatedGridExample>(
                displayText: "Paginated Grid Test",
                targetMenu: TargetMenu.MainMenu,
                positionReference: "OPTIONS"
            );

            MenuRegistry.RegisterMenu<TransitionTestMenu>(
                displayText: "Transition Test",
                targetMenu: TargetMenu.MainMenu,
                positionReference: "OPTIONS"
            );

            RocketMain.Logger.Log("Test menus registered successfully");
        }
    }
}

