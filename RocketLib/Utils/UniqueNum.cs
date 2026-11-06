using System;
using HarmonyLib;

namespace RocketLib.Utils
{
    /// <summary>
    /// This Player Num take damage from everyone and does damages to everyone except players.
    /// </summary>
    public static class UniqueNum
    {
        public const int NUM = -8;

        [HarmonyPatch(typeof(GameModeController), "DoesPlayerNumDamage", new Type[] { typeof(int), typeof(int) })]
        private static bool Patch(GameModeController __instance, bool __result, int fromNum, int toNum)
        {
            if (fromNum == NUM && toNum < 0)
            {
                __result = true;
            }
            else if (toNum == NUM && fromNum >= 0)
            {
                __result = true;
            }
            return !__result;
        }
    }
}
