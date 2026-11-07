using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static RocketLib.CustomTriggers.CustomTriggerPatches;

namespace RocketLib.CustomTriggers
{
    /// <summary>
    /// Manages registration and lifecycle of custom triggers for the Broforce level editor.
    /// </summary>
    /// <remarks>
    /// This class handles trigger registration, serialization, and integration with the level editor.
    /// Modders should only need to call RegisterCustomTrigger to add new trigger types.
    /// </remarks>
    public class CustomTriggerManager
    {
        /// <summary>
        /// List of all registered custom triggers, sorted by tag, priority (descending), and action name.
        /// </summary>
        public static List<CustomTrigger> CustomTriggers = new List<CustomTrigger>();

        private static TriggerActionInfo currentAction;
        private static string currentActionName;

        /// <summary>
        /// Registers a new custom trigger type with the level editor.
        /// Call this during mod initialization, before any levels are loaded.
        /// </summary>
        /// <param name="customTriggerActionType">
        /// The Type of your CustomTriggerAction class (e.g., typeof(MyTriggerAction)).
        /// Must inherit from CustomTriggerAction or LevelStartTriggerAction.
        /// </param>
        /// <param name="customTriggerActionInfoType">
        /// The Type of your CustomTriggerActionInfo class (e.g., typeof(MyTriggerActionInfo)).
        /// Must inherit from CustomTriggerActionInfo or LevelStartTriggerActionInfo.
        /// </param>
        /// <param name="actionName">
        /// Display name for the trigger in the level editor (e.g., "My Mod - Spawn Item").
        /// Must be unique across all registered triggers.
        /// </param>
        /// <param name="tag">
        /// Category tag for grouping triggers in the menu (e.g., "Custom Bros", "My Mod").
        /// Triggers with the same tag are grouped together.
        /// </param>
        /// <param name="priority">
        /// Sort priority within the same tag. Higher values appear first. Default is 0.
        /// Use higher priority for frequently-used triggers.
        /// </param>
        /// <exception cref="Exception">Thrown if a trigger with the same actionName is already registered.</exception>
        public static void RegisterCustomTrigger(Type customTriggerActionType, Type customTriggerActionInfoType, string actionName, string tag, int priority = 0)
        {
            if (CustomTriggers.Any(t => t.ActionName == actionName))
            {
                throw new Exception($"Custom trigger with name '{actionName}' is already registered");
            }
            CustomTrigger customTrigger = new CustomTrigger(customTriggerActionType, customTriggerActionInfoType, actionName, tag, priority);
            CustomTriggers.Add(customTrigger);

            CustomTriggers = CustomTriggers
                .OrderBy(t => t.Tag)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.ActionName)
                .ToList();
        }

        public static void DisplayAddCustomTriggers(LevelEditorGUI __instance, ref TriggerInfo ___selectedTrigger, ref TriggerActionInfo ___selectedAction)
        {
            GUILayout.Space(10);
            GUILayout.Label("Modded Actions:");
            GUILayout.Space(7);

            string currentTag = null;
            foreach (CustomTrigger customTrigger in CustomTriggers)
            {
                if (currentTag != customTrigger.Tag)
                {
                    currentTag = customTrigger.Tag;
                    GUILayout.Space(5);
                    GUILayout.Label($"[{currentTag}]");
                    GUILayout.Space(2);
                }

                if (GUILayout.Button("Add New " + customTrigger.ActionName + " Action", new GUILayoutOption[0]))
                {
                    LevelEditorGUI_ShowTriggerMenu_Patch.PlayClickSound(__instance);
                    TriggerActionInfo customActionInfo = Activator.CreateInstance(customTrigger.CustomTriggerActionInfoType) as TriggerActionInfo;
                    customActionInfo.type = TriggerActionType.Weather;
                    ___selectedTrigger.actions.Add(customActionInfo);
                    ___selectedAction = customActionInfo;
                }
            }
        }

        public static string GetCustomActionType(TriggerActionInfo actionInfo)
        {
            if (currentAction == actionInfo)
            {
                return currentActionName;
            }

            foreach (CustomTrigger customTrigger in CustomTriggers)
            {
                if (actionInfo.GetType() == customTrigger.CustomTriggerActionInfoType)
                {
                    currentActionName = customTrigger.ActionName;
                    currentAction = actionInfo;
                    return currentActionName;
                }
            }

            return string.Empty;
        }

        private static CustomTriggerActionInfo ConvertToCustomInfo(WeatherActionInfo weatherInfo, out CustomTrigger customTrigger, out string problem)
        {
            customTrigger = null;
            problem = null;
            string triggerType = null;
            try
            {
                string[] parts = weatherInfo.name.Split('|');
                if (parts.Length < 3)
                {
                    throw new Exception("Invalid custom trigger encoding format");
                }

                string encodedJson = parts[1];
                string actualName = parts[2];

                string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedJson));

                JObject wrapper = JObject.Parse(json);
                triggerType = wrapper["TriggerType"]?.ToString();

                if (string.IsNullOrEmpty(triggerType))
                {
                    throw new Exception("TriggerType not found in JSON");
                }

                customTrigger = CustomTriggers.FirstOrDefault(t => t.ActionName == triggerType);
                if (customTrigger == null)
                {
                    problem = $"Level uses custom trigger '{triggerType}', which no installed mod provides. Its actions will do nothing.";
                    return null;
                }

                CustomTriggerActionInfo customInfo = Activator.CreateInstance(customTrigger.CustomTriggerActionInfoType) as CustomTriggerActionInfo;

                customInfo.type = weatherInfo.type;
                customInfo.timeOffset = weatherInfo.timeOffset;
                customInfo.onlyOnHardMode = weatherInfo.onlyOnHardMode;
                customInfo.name = actualName;

                JToken dataToken = wrapper["Data"];
                if (dataToken != null)
                {
                    JsonConvert.PopulateObject(dataToken.ToString(), customInfo);
                }

                return customInfo;
            }
            catch (Exception ex)
            {
                customTrigger = null;
                problem = string.IsNullOrEmpty(triggerType)
                    ? $"Couldn't read a custom trigger action, so it will do nothing: {ex.Message}"
                    : $"Couldn't read the settings of custom trigger '{triggerType}', so its actions will do nothing: {ex.Message}";
                return null;
            }
        }

        public static WeatherActionInfo ConvertToWeatherInfo(CustomTriggerActionInfo customInfo)
        {
            try
            {
                WeatherActionInfo weatherInfo = new WeatherActionInfo();

                string triggerType = GetCustomActionType(customInfo);

                if (string.IsNullOrEmpty(triggerType))
                {
                    throw new Exception("Custom trigger type not registered");
                }

                var wrapper = new
                {
                    TriggerType = triggerType,
                    Data = customInfo
                };

                string json = JsonConvert.SerializeObject(wrapper, Formatting.None,
                    new JsonSerializerSettings
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    });

                string encodedJson = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

                weatherInfo.type = TriggerActionType.Weather;
                weatherInfo.timeOffset = customInfo.timeOffset;
                weatherInfo.onlyOnHardMode = customInfo.onlyOnHardMode;
                weatherInfo.name = $"CUSTOMTRIGGER|{encodedJson}|{customInfo.name ?? string.Empty}";

                return weatherInfo;
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Log($"Failed to convert CustomTriggerActionInfo to WeatherActionInfo: {ex}");
                throw;
            }
        }

        public static TriggerAction CreateCustomAction(TriggerActionInfo info)
        {
            try
            {
                CustomTriggerActionInfo customInfo = ConvertToCustomInfo(info as WeatherActionInfo, out CustomTrigger customTrigger, out string _);

                TriggerAction action;
                if (customInfo == null)
                {
                    action = new UnknownCustomTriggerAction();
                    action.Info = info;
                }
                else
                {
                    action = Activator.CreateInstance(customTrigger.CustomTriggerActionType) as TriggerAction;
                    action.Info = customInfo;
                }
                action.timeOffsetLeft = action.Info.timeOffset;
                action.AssignDeterministicIDs();

                return action;
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Log($"Failed to create custom trigger action: {ex}");
                throw;
            }
        }

        public static void ConvertAllCustomTriggersInMapData(MapData mapData, HashSet<string> reportedProblems = null)
        {
            if (reportedProblems == null)
            {
                reportedProblems = new HashSet<string>();
            }

            foreach (List<TriggerActionInfo> actions in GetActionLists(mapData))
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    if (actions[i] is WeatherActionInfo weatherAction && weatherAction.name != null && weatherAction.name.StartsWith("CUSTOMTRIGGER|"))
                    {
                        CustomTriggerActionInfo customInfo = ConvertToCustomInfo(weatherAction, out CustomTrigger _, out string problem);
                        if (customInfo != null)
                        {
                            actions[i] = customInfo;
                        }
                        else if (reportedProblems.Add(problem))
                        {
                            RocketMain.Logger.Warning(problem);
                        }
                    }
                }
            }
        }

        internal class EncodedAction
        {
            public List<TriggerActionInfo> Actions;
            public int Index;
            public CustomTriggerActionInfo Original;
        }

        internal static List<EncodedAction> EncodeCustomActions(IEnumerable<MapData> maps)
        {
            List<EncodedAction> encoded = new List<EncodedAction>();
            if (maps == null)
            {
                return encoded;
            }

            try
            {
                foreach (MapData mapData in maps)
                {
                    foreach (List<TriggerActionInfo> actions in GetActionLists(mapData))
                    {
                        for (int i = 0; i < actions.Count; i++)
                        {
                            if (actions[i] is CustomTriggerActionInfo customAction)
                            {
                                WeatherActionInfo weatherInfo = ConvertToWeatherInfo(customAction);
                                encoded.Add(new EncodedAction { Actions = actions, Index = i, Original = customAction });
                                actions[i] = weatherInfo;
                            }
                        }
                    }
                }
            }
            catch
            {
                RestoreCustomActions(encoded);
                throw;
            }

            return encoded;
        }

        internal static void RestoreCustomActions(List<EncodedAction> encoded)
        {
            if (encoded == null)
            {
                return;
            }

            for (int i = encoded.Count - 1; i >= 0; i--)
            {
                encoded[i].Actions[encoded[i].Index] = encoded[i].Original;
            }
        }

        private static IEnumerable<List<TriggerActionInfo>> GetActionLists(MapData mapData)
        {
            if (mapData == null)
            {
                yield break;
            }

            if (mapData.TriggerList != null)
            {
                foreach (TriggerInfo trigger in mapData.TriggerList)
                {
                    if (trigger != null && trigger.actions != null)
                    {
                        yield return trigger.actions;
                    }
                }
            }

            if (mapData.DoodadList != null)
            {
                foreach (DoodadInfo doodad in mapData.DoodadList)
                {
                    if (doodad != null && doodad.triggerInfo != null && doodad.triggerInfo.actions != null)
                    {
                        yield return doodad.triggerInfo.actions;
                    }
                }
            }
        }

        // A vanilla WeatherAction on this info would switch the weather and rain to None.
        private class UnknownCustomTriggerAction : TriggerAction
        {
            private TriggerActionInfo info;

            public override TriggerActionInfo Info
            {
                get { return info; }
                set { info = value; }
            }

            public override void Start()
            {
                state = TriggerActionState.Done;
            }

            public override void Update()
            {
            }
        }
    }
}
