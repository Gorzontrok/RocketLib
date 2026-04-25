using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using RocketLib.Utils;
using UnityModManagerNet;

namespace RocketLib.UMM
{
    // Fix UMM window disappearing
    [HarmonyPatch(typeof(UnityModManagerNet.UnityModManager.UI), "Start")]
    static class UnityModManager_UI_Start_Patch
    {
        public static void Postfix(UnityModManagerNet.UnityModManager.UI __instance)
        {
            RocketLibUtils.MakeObjectUnpausable(__instance.gameObject);
        }
    }

    // Fix RuntimeUnityEditor window disappearing
    [HarmonyPatch(typeof(Startup), "Update")]
    static class Startup_Update_Patch
    {
        public static void Prefix(Startup __instance)
        {
            if (!Main.Enabled)
                return;

            RocketLibUtils.MakeObjectUnpausable("RuntimeUnityEditor");
        }
    }

    // Strip rich-text tags from log file
    [HarmonyPatch(typeof(UnityModManager.Logger), "Write", new[] { typeof(string), typeof(bool) })]
    static class UnityModManager_Logger_Write_Patch
    {
        private static readonly Regex TagRegex = new Regex(
            @"</?(?:color|b|i|size|material|quad)(?:=[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly FieldInfo bufferField =
            AccessTools.Field(typeof(UnityModManager.Logger), "buffer");

        public static void Postfix(string str, bool onlyNative)
        {
            if (onlyNative || str == null || bufferField == null) return;
            if (str.IndexOf('<') < 0) return;

            var buffer = bufferField.GetValue(null) as List<string>;
            if (buffer == null || buffer.Count == 0) return;

            int last = buffer.Count - 1;
            if (!ReferenceEquals(buffer[last], str)) return;

            buffer[last] = TagRegex.Replace(str, string.Empty);
        }
    }
}
