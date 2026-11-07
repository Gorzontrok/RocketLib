using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Localisation;
using UnityEngine;

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
                    MenuBarItem masterItem = ___masterItems[i];

                    if (!string.IsNullOrEmpty(masterItem.invokeMethod) && masterItem.invokeMethod.StartsWith("RocketLib_"))
                    {
                        MenuBarItemUI itemUI = ___items[i];
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
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo sendMessage = AccessTools.Method(typeof(Component), nameof(Component.SendMessage), new Type[] { typeof(string), typeof(object) });
            MethodInfo replacement = AccessTools.Method(typeof(Menu_RunInput_Patch), nameof(SendMenuMessage));

            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            bool found = false;
            foreach (CodeInstruction code in codes)
            {
                if (code.Calls(sendMessage))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                    found = true;
                }
            }

            if (!found)
            {
                RocketMain.Logger.Warning("Menu_RunInput_Patch: Could not find Component.SendMessage call");
            }
            return codes;
        }

        private static void SendMenuMessage(Component component, string methodName, object value)
        {
            Menu menu = component as Menu;
            if (menu != null && methodName != null && methodName.StartsWith("RocketLib_"))
            {
                try
                {
                    if (MenuRegistry.InvokeMenuAction(menu, methodName))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Error($"Error running menu action {methodName}: {ex}");
                    return;
                }
            }

            component.SendMessage(methodName, value);
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
                    FlexMenu targetMenu = FlexMenu.GetReturnTarget();

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
