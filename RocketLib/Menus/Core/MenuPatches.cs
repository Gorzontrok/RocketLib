using System;
using HarmonyLib;
using Localisation;

namespace RocketLib.Menus.Core
{
    [HarmonyPatch(typeof(MainMenu), "Awake")]
    internal static class MainMenu_Awake_Patch
    {
        private static void Postfix(MainMenu __instance)
        {
            try
            {
                MenuRegistry.InjectMenuItems(__instance);
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in MainMenu_Awake_Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(MainMenu), "SetupItems")]
    internal static class MainMenu_SetupItems_Patch
    {
        private static void Postfix(MainMenu __instance)
        {
            try
            {
                MenuRegistry.InjectMenuItems(__instance);
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in MainMenu_SetupMenu_Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PauseMenu), "InstantiateItems")]
    internal static class PauseMenu_InstantiateItems_Patch
    {
        private static void Postfix(PauseMenu __instance)
        {
            try
            {
                MenuRegistry.InjectMenuItems(__instance);
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in PauseMenu_InstantiateItems_Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(OptionsMenu), "InstantiateItems")]
    internal static class OptionsMenu_SetupItems_Patch
    {
        private static void Prefix(OptionsMenu __instance)
        {
            try
            {
                MenuRegistry.InjectMenuItems(__instance);
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in OptionsMenu_InstantiateItems_Prefix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(InGameOptionsMenu), "InstantiateItems")]
    internal static class InGameOptionsMenu_InstantiateItems_Patch
    {
        private static void Prefix(InGameOptionsMenu __instance)
        {
            try
            {
                MenuRegistry.InjectMenuItems(__instance);
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in InGameOptionsMenu_InstantiateItems_Prefix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Menu), "InstantiateItems")]
    internal static class Menu_InstantiateItems_Patch
    {
        private static void Postfix(Menu __instance, ref MenuBarItem[] ___masterItems, ref MenuBarItemUI[] ___items)
        {
            try
            {
                if (!(__instance is MainMenu))
                {
                    return;
                }

                if (___masterItems == null || ___items == null)
                {
                    return;
                }

                for (int i = 0; i < ___masterItems.Length && i < ___items.Length; i++)
                {
                    var masterItem = ___masterItems[i];

                    if (!string.IsNullOrEmpty(masterItem.invokeMethod) && masterItem.invokeMethod.StartsWith("RocketLib_"))
                    {
                        var itemUI = ___items[i];
                        if (itemUI != null)
                        {
                            itemUI.text = masterItem.name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in Menu_InstantiateItems_Postfix: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Menu), "RunInput")]
    internal static class Menu_RunInput_Patch
    {
        private static bool Prefix(Menu __instance, ref bool ___accept, ref bool ___activatedThisFrame, ref MenuBarItem[] ___masterItems, ref int ___highlightIndex)
        {
            try
            {
                if (!___accept)
                {
                    return true;
                }

                bool acceptPrev = (bool)AccessTools.Field(typeof(Menu), "acceptPrev").GetValue(null);

                if (___activatedThisFrame || acceptPrev)
                {
                    return true;
                }

                var masterItems = ___masterItems;
                var highlightIndex = ___highlightIndex;

                if (masterItems == null || highlightIndex < 0 || highlightIndex >= masterItems.Length)
                {
                    return true;
                }

                var currentItem = masterItems[highlightIndex];

                if (!string.IsNullOrEmpty(currentItem.invokeMethod) && currentItem.invokeMethod.StartsWith("RocketLib_"))
                {
                    bool handled = MenuRegistry.InvokeMenuAction(__instance, currentItem.invokeMethod);

                    if (handled)
                    {
                        var playDrumSound = AccessTools.Method(typeof(Menu), "PlayDrumSound");
                        if (playDrumSound != null)
                        {
                            playDrumSound.Invoke(__instance, new object[] { 1 });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in Menu_RunInput_Prefix: {ex}");
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(MainMenu), "Start")]
    internal static class MainMenu_Start_Patch
    {
        private static bool Prefix(MainMenu __instance)
        {
            try
            {
                if (FlexMenu.HasReturnTargetOverride())
                {
                    var targetMenu = FlexMenu.GetReturnTarget();

                    FlexMenu.ClearReturnTarget();

                    if (targetMenu != null)
                    {
                        Traverse.Create(__instance).Method("InitializeMenu").GetValue();

                        targetMenu.gameObject.SetActive(true);
                        FlexMenu.activeMenu = targetMenu;

                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Error($"Error in MainMenu_Start_Prefix: {ex}");
            }

            return true;
        }
    }
}
