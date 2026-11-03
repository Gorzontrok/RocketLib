using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Networking;

namespace RocketLib.Network
{
    public class NetworkPatches
    {
        // AOT cache miss fallback: uncached types outside SerializeValueType's list (all modded ones, some vanilla ones like Mook) serialize to 0 bytes, nulls included
        [HarmonyPatch(typeof(TypeSerializer), "SerializeObjectWithType")]
        private static class TypeSerializer_SerializeObjectWithType_Patch
        {
            private static bool Prefix(object obj, Type type, BinaryWriter writer)
            {
                var instance = TypeSerializer.Instance;
                if (instance == null)
                {
                    return true;
                }

                var cache = _cacheField != null
                    ? _cacheField.GetValue(instance) as Dictionary<Type, MethodInfo>
                    : null;
                if (cache == null)
                {
                    return true;
                }

                MethodInfo methodInfo;
                if (cache.TryGetValue(type, out methodInfo))
                {
                    return true;
                }

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
        private static class RPCSecurity_IsAllowed_Patch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
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

        // InvokeMember can't find private base class methods; the game's own catch swallows every failure
        [HarmonyPatch(typeof(NonStaticRPCObject), "Execute")]
        private static class NonStaticRPCObject_Execute_Patch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var logCaught = AccessTools.Method(typeof(NonStaticRPCObject_Execute_Patch), nameof(LogCaught));
                return RPCFailureLog.InsertIntoCatchBlock(ReplaceInvokeMember(instructions, generator), logCaught,
                    nameof(NonStaticRPCObject_Execute_Patch));
            }

            private static IEnumerable<CodeInstruction> ReplaceInvokeMember(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
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

            // Runs inside the game's catch handler: a throw here escapes Execute into the RPC caller
            private static void LogCaught(Exception exception, NonStaticRPCObject rpc)
            {
                try
                {
                    var targetName = Registry.GetObject(rpc.targetID)?.GetType().Name ?? "NULL";
                    RPCFailureLog.Log($"{rpc.methodName} on {targetName}", exception);
                }
                catch (Exception)
                {
                }
            }
        }

        // The game's own catch swallows every failure
        [HarmonyPatch(typeof(StaticRPCObject), "Execute")]
        private static class StaticRPCObject_Execute_Patch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var logCaught = AccessTools.Method(typeof(StaticRPCObject_Execute_Patch), nameof(LogCaught));
                return RPCFailureLog.InsertIntoCatchBlock(instructions, logCaught, nameof(StaticRPCObject_Execute_Patch));
            }

            // Runs inside the game's catch handler: a throw here escapes Execute into the RPC caller
            private static void LogCaught(Exception exception, StaticRPCObject rpc)
            {
                try
                {
                    RPCFailureLog.Log($"{rpc.classType?.FullName ?? "NULL"}.{rpc.methodName}", exception);
                }
                catch (Exception)
                {
                }
            }
        }

        internal static class RPCFailureLog
        {
            private const int RepeatIntervalMs = 60000;

            private static readonly Dictionary<string, Repeats> _repeats = new Dictionary<string, Repeats>();

            private class Repeats
            {
                public int LastLoggedAt;
                public int Skipped;
            }

            // The handler's first instruction carries the catch block; the exception is on the stack when it starts
            public static IEnumerable<CodeInstruction> InsertIntoCatchBlock(IEnumerable<CodeInstruction> instructions, MethodInfo logCaught, string patchName)
            {
                var codes = new List<CodeInstruction>(instructions);
                var handlerStart = codes.FindIndex(i => i.blocks.Any(b => b.blockType == ExceptionBlockType.BeginCatchBlock));
                if (handlerStart < 0)
                {
                    RocketMain.Logger.Warning(patchName + ": Could not find catch block");
                    return codes;
                }

                var duplicate = new CodeInstruction(OpCodes.Dup);
                codes[handlerStart].MoveBlocksTo(duplicate);
                codes.InsertRange(handlerStart, new[]
                {
                    duplicate,
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, logCaught)
                });

                return codes;
            }

            public static void Log(string target, Exception exception)
            {
                var invocation = exception as TargetInvocationException;
                if (invocation != null && invocation.InnerException != null)
                {
                    exception = invocation.InnerException;
                }

                var key = target + "|" + exception.GetType().FullName;
                var now = Environment.TickCount;
                Repeats repeats;
                if (_repeats.TryGetValue(key, out repeats))
                {
                    if (unchecked(now - repeats.LastLoggedAt) < RepeatIntervalMs)
                    {
                        repeats.Skipped++;
                        return;
                    }
                }
                else
                {
                    repeats = new Repeats();
                    _repeats[key] = repeats;
                }

                var skipped = repeats.Skipped > 0 ? $" ({repeats.Skipped} repeats since last logged)" : "";
                repeats.LastLoggedAt = now;
                repeats.Skipped = 0;
                RocketMain.Logger.Exception($"[RPC] {target}{skipped}", exception);
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
                        {
                            return method;
                        }
                    }
                    catch (AmbiguousMatchException)
                    {
                        foreach (var m in current.GetMethods(AllDeclared))
                        {
                            if (m.Name == methodName)
                            {
                                return m;
                            }
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
