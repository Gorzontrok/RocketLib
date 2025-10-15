using HarmonyLib;
using RocketLib.Utils;

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
}
