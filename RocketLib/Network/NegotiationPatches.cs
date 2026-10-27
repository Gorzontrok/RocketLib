using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Networking;

namespace RocketLib.Network
{
    [HarmonyPatch(typeof(Registry), "RequestNetworkObjectSync")]
    internal static class RequestNetworkObjectSyncPatch
    {
        private static void Prefix(PID requestee) { ModNegotiation.BeginHandshake(requestee); }
    }
    [HarmonyPatch(typeof(ConnectionLayer), "SetPIDPair")]
    internal static class SetPIDPairPatch
    {
        private static void Postfix(PID pid) { ModNegotiation.PeerRegistered(pid); }
    }
    [HarmonyPatch(typeof(GameModeController), "Start")]
    internal static class NegotiationLevelStartPatch
    {
        private static void Postfix() { ModNegotiation.LevelStarted(); }
    }
    [HarmonyPatch(typeof(Connect), "DeregisterPlayer")]
    internal static class NegotiationPlayerLeftPatch
    {
        private static void Postfix(PID pid) { ModNegotiation.PlayerLeft(pid); }
    }
    [HarmonyPatch(typeof(FuckNetLayer), "PlayerHasLeftLobby_Callback")]
    internal static class NegotiationLobbyLeftPatch
    {
        private static void Prefix(FuckNetID player, out PID __state)
        {
            __state = PID.NoID;
            if (player == null || !Connect.LayerExists) return;
            foreach (KeyValuePair<PID, IDWrapper> pair in Connect.Layer.PlayerIDPairs)
                if (pair.Value != null && pair.Value.UnderlyingID == player.UnderlyingID)
                {
                    __state = pair.Key;
                    return;
                }
        }
        private static void Postfix(PID __state) { ModNegotiation.PlayerLeft(__state); }
    }
    [HarmonyPatch(typeof(Connect), "Disconnect")]
    internal static class NegotiationDisconnectPatch
    {
        private static void Postfix() { ModNegotiation.SessionEnded(); }
    }
    [HarmonyPatch(typeof(StaticRPCObject), "Execute")]
    internal static class StaticRPCExecutePatch
    {
        private static Exception Finalizer(Exception __exception, StaticRPCObject __instance)
        {
            if (__exception != null && RocketMain.Logger != null)
                RocketMain.Logger.Exception("[RPC] " + __instance.TargetAsString(), __exception);
            return null;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                bool handler = false;
                foreach (ExceptionBlock block in instruction.blocks)
                    if (block.blockType == ExceptionBlockType.BeginCatchBlock) handler = true;
                if (handler)
                {
                    CodeInstruction duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    yield return duplicate;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StaticRPCExecutePatch), "LogCaught"));
                }
                yield return instruction;
            }
        }
        private static void LogCaught(Exception exception, StaticRPCObject rpc)
        {
            if (RocketMain.Logger != null) RocketMain.Logger.Exception("[RPC] " + rpc.TargetAsString(), exception);
        }
    }
}
