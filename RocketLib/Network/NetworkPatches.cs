using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Networking;

namespace RocketLib.Network
{
    public class NetworkPatches
    {
        // AOT cache miss fallback: modded types aren't cached, causing silent 0-byte serialization
        [HarmonyPatch(typeof(TypeSerializer), "SerializeObjectWithType")]
        static class TypeSerializer_SerializeObjectWithType_Patch
        {
            static bool Prefix(object obj, Type type, BinaryWriter writer)
            {
                if (obj == null) return true;

                var instance = TypeSerializer.Instance;
                if (instance == null) return true;

                var cache = _cacheField != null
                    ? _cacheField.GetValue(instance) as Dictionary<Type, MethodInfo>
                    : null;
                if (cache == null) return true;

                MethodInfo methodInfo;
                if (cache.TryGetValue(type, out methodInfo))
                    return true;

                try
                {
                    methodInfo = _serializeMethod.MakeGenericMethod(new Type[] { type });
                    methodInfo.Invoke(null, new object[] { obj, writer });
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Warning("[RPC] SerializeObjectWithType fallback failed for " + type.FullName + ": " + ex.Message);
                }

                return false;
            }
        }

        // GetMethod can't find private [AllowedRPC] methods on base classes for derived types
        [HarmonyPatch(typeof(RPCSecurity), "IsAllowed", new Type[] { typeof(Type), typeof(string) })]
        static class RPCSecurity_IsAllowed_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var getMethodCall = AccessTools.Method(typeof(Type), "GetMethod",
                    new Type[] { typeof(string), typeof(BindingFlags) });
                var replacement = AccessTools.Method(typeof(RPCInheritanceHelper),
                    nameof(RPCInheritanceHelper.FindMethodIncludingBasePrivate));

                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(new CodeMatch(i => i.Calls(getMethodCall)));
                if (!codeMatcher.IsValid)
                {
                    RocketMain.Logger.Warning("RPCSecurity_IsAllowed_Patch: Could not find Type.GetMethod call");
                    return instructions;
                }

                codeMatcher.Advance(-1);
                codeMatcher.RemoveInstruction();
                codeMatcher.Set(OpCodes.Call, replacement);

                return codeMatcher.InstructionEnumeration();
            }
        }

        // InvokeMember can't find private base class methods; also adds exception logging
        [HarmonyPatch(typeof(NonStaticRPCObject), "Execute")]
        static class NonStaticRPCObject_Execute_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var invokeMemberCall = AccessTools.Method(typeof(Type), "InvokeMember",
                    new Type[] { typeof(string), typeof(BindingFlags), typeof(Binder), typeof(object), typeof(object[]) });
                var replacement = AccessTools.Method(typeof(RPCInheritanceHelper),
                    nameof(RPCInheritanceHelper.FindAndInvoke));
                var getTypeCall = AccessTools.Method(typeof(object), "GetType");

                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(new CodeMatch(i => i.Calls(invokeMemberCall)));
                if (!codeMatcher.IsValid)
                {
                    RocketMain.Logger.Warning("NonStaticRPCObject_Execute_Patch: Could not find Type.InvokeMember call");
                    return instructions;
                }

                codeMatcher.MatchStartBackwards(new CodeMatch(i => i.Calls(getTypeCall)));
                if (!codeMatcher.IsValid)
                {
                    RocketMain.Logger.Warning("NonStaticRPCObject_Execute_Patch: Could not find GetType call before InvokeMember");
                    return instructions;
                }

                codeMatcher.RemoveInstructions(9);
                codeMatcher.Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(RPCObject), "methodName")),
                    new CodeInstruction(OpCodes.Ldloc_1),
                    new CodeInstruction(OpCodes.Call, replacement)
                );

                return codeMatcher.InstructionEnumeration();
            }

            static Exception Finalizer(Exception __exception, NonStaticRPCObject __instance)
            {
                if (__exception != null)
                {
                    var targetName = Registry.GetObject(__instance.targetID)?.GetType().Name ?? "NULL";
                    RocketMain.Logger.Exception($"[RPC] {__instance.methodName} on {targetName}", __exception);
                }
                return null;
            }
        }

        // Walks inheritance chain to find methods including private ones on base classes
        internal static class RPCInheritanceHelper
        {
            private const BindingFlags AllDeclared = BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            public static MethodInfo FindMethodIncludingBasePrivate(Type type, string methodName)
            {
                Type current = type;
                while (current != null)
                {
                    try
                    {
                        var method = current.GetMethod(methodName, AllDeclared);
                        if (method != null)
                            return method;
                    }
                    catch (AmbiguousMatchException)
                    {
                        foreach (var m in current.GetMethods(AllDeclared))
                        {
                            if (m.Name == methodName)
                                return m;
                        }
                    }
                    current = current.BaseType;
                }
                return null;
            }

            public static void FindAndInvoke(object target, string methodName, object[] parameters)
            {
                var method = FindMethodIncludingBasePrivate(target.GetType(), methodName);
                if (method != null)
                {
                    method.Invoke(target, parameters);
                }
            }
        }

        // Cached reflection for serialization patch
        private static readonly FieldInfo _cacheField = typeof(TypeSerializer)
            .GetField("cached_SerializeMethods", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo _serializeMethod = typeof(TypeSerializer)
            .GetMethod("Serialize", BindingFlags.Static | BindingFlags.Public);
    }
}
