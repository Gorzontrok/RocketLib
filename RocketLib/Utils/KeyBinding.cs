using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Rewired;
using UnityEngine;
using UnityModManagerNet;

namespace RocketLib
{
    [Flags]
    public enum ModifierKeys
    {
        None = 0,
        Ctrl = 1 << 0,
        Shift = 1 << 1,
        Alt = 1 << 2
    }

    public enum ControllerMappingType
    {
        Xbox,
        PlayStation,
        SwitchPro
    }

    [Serializable]
    public class KeyBinding : IEquatable<KeyBinding>
    {
        public string name;
        public KeyCode key;
        public int joystickDirection = 1;
        public float axisThreshold = 0.8f;
        public string joystickDisplayName;
        public ModifierKeys modifiers = ModifierKeys.None;
        public ControllerMappingType mappingType = ControllerMappingType.Xbox;

        // Rewired controller binding fields
        public int rewiredPlayerId = -1;
        public int rewiredInputId = -1;
        public bool rewiredIsAxis = false;

        [JsonIgnore, XmlIgnore]
        public bool IsRewiredBinding => rewiredPlayerId >= 0;

        [JsonIgnore, XmlIgnore]
        public bool isSettingKey;
        [JsonIgnore, XmlIgnore]
        internal static Rect toolTipRect;
        [JsonIgnore, XmlIgnore]
        private bool wasDown = false;
        // XInput / Steam Input ON button names (IDs 6-19)
        private static readonly Dictionary<int, string> XInputButtonNames = new Dictionary<int, string>
        {
            { 6, "A" }, { 7, "B" }, { 8, "X" }, { 9, "Y" },
            { 10, "LB" }, { 11, "RB" },
            { 12, "Back" }, { 13, "Start" },
            { 14, "LS" }, { 15, "RS" },
            { 16, "D-Pad Up" }, { 17, "D-Pad Right" }, { 18, "D-Pad Down" }, { 19, "D-Pad Left" }
        };

        private static readonly Dictionary<int, string> XInputPlayStationButtonNames = new Dictionary<int, string>
        {
            { 6, "Cross" }, { 7, "Circle" }, { 8, "Square" }, { 9, "Triangle" },
            { 10, "L1" }, { 11, "R1" },
            { 12, "Share" }, { 13, "Options" },
            { 14, "L3" }, { 15, "R3" },
            { 16, "D-Pad Up" }, { 17, "D-Pad Right" }, { 18, "D-Pad Down" }, { 19, "D-Pad Left" }
        };

        private static readonly Dictionary<int, string> XInputSwitchProButtonNames = new Dictionary<int, string>
        {
            { 6, "B" }, { 7, "A" }, { 8, "Y" }, { 9, "X" },
            { 10, "L" }, { 11, "R" },
            { 12, "-" }, { 13, "+" },
            { 14, "LS" }, { 15, "RS" },
            { 16, "D-Pad Up" }, { 17, "D-Pad Right" }, { 18, "D-Pad Down" }, { 19, "D-Pad Left" }
        };

        // Linux Steam Input OFF - Generic template button names (IDs 32+)
        private static readonly Dictionary<int, string> GenericXboxButtonNames = new Dictionary<int, string>
        {
            { 32, "A" }, { 33, "B" }, { 35, "X" }, { 36, "Y" },
            { 38, "LB" }, { 39, "RB" },
            { 42, "Back" }, { 43, "Start" },
            { 45, "LS" }, { 46, "RS" },
            { 160, "D-Pad Up" }, { 161, "D-Pad Right" }, { 162, "D-Pad Down" }, { 163, "D-Pad Left" }
        };

        private static readonly Dictionary<int, string> GenericPlayStationButtonNames = new Dictionary<int, string>
        {
            { 32, "Cross" }, { 33, "Circle" }, { 35, "Square" }, { 34, "Triangle" },
            { 36, "L1" }, { 37, "R1" },
            { 38, "L2" }, { 39, "R2" },
            { 40, "Share" }, { 41, "Options" },
            { 43, "L3" }, { 44, "R3" },
            { 160, "D-Pad Up" }, { 161, "D-Pad Right" }, { 162, "D-Pad Down" }, { 163, "D-Pad Left" }
        };

        private static readonly Dictionary<int, string> GenericSwitchProButtonNames = new Dictionary<int, string>
        {
            { 32, "A" }, { 33, "B" }, { 35, "X" }, { 34, "Y" },
            { 37, "L" }, { 38, "R" },
            { 39, "ZL" }, { 40, "ZR" },
            { 41, "-" }, { 42, "+" },
            { 44, "LS" }, { 45, "RS" },
            { 160, "D-Pad Up" }, { 161, "D-Pad Right" }, { 162, "D-Pad Down" }, { 163, "D-Pad Left" }
        };

        // Windows Native PlayStation button names (different D-Pad/stick click IDs)
        private static readonly Dictionary<int, string> NativePlayStationButtonNames = new Dictionary<int, string>
        {
            { 6, "Cross" }, { 7, "Circle" }, { 8, "Square" }, { 9, "Triangle" },
            { 10, "L1" }, { 11, "R1" },
            { 12, "Share" }, { 13, "Options" },
            { 16, "L3" }, { 17, "R3" },
            { 18, "D-Pad Up" }, { 19, "D-Pad Right" }, { 20, "D-Pad Down" }, { 21, "D-Pad Left" }
        };

        public static ControllerMappingType GetMappingTypeFromJoystickName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return ControllerMappingType.Xbox;

            string lower = name.ToLower();
            if (lower.Contains("dualsense") || lower.Contains("dualshock") ||
                lower.Contains("playstation") || lower.Contains("sony"))
                return ControllerMappingType.PlayStation;

            if (lower.Contains("pro controller") || lower.Contains("nintendo"))
                return ControllerMappingType.SwitchPro;

            return ControllerMappingType.Xbox;
        }

        public static string GetButtonDisplayName(int elementId, ControllerMappingType type, string rewiredName)
        {
            Dictionary<int, string> dict = null;

            // Check if this is XInput range (6-19) or generic range (32+)
            if (elementId >= 6 && elementId <= 21)
            {
                // XInput or Windows Native PlayStation range
                if (type == ControllerMappingType.PlayStation && elementId >= 16)
                {
                    // Could be native PS with different D-Pad/stick IDs
                    if (NativePlayStationButtonNames.TryGetValue(elementId, out string nativeName))
                        return nativeName;
                }

                switch (type)
                {
                    case ControllerMappingType.PlayStation:
                        dict = XInputPlayStationButtonNames;
                        break;
                    case ControllerMappingType.SwitchPro:
                        dict = XInputSwitchProButtonNames;
                        break;
                    default:
                        dict = XInputButtonNames;
                        break;
                }
            }
            else if (elementId >= 32)
            {
                // Generic template range (Linux Steam Input OFF)
                switch (type)
                {
                    case ControllerMappingType.PlayStation:
                        dict = GenericPlayStationButtonNames;
                        break;
                    case ControllerMappingType.SwitchPro:
                        dict = GenericSwitchProButtonNames;
                        break;
                    default:
                        dict = GenericXboxButtonNames;
                        break;
                }
            }

            if (dict != null && dict.TryGetValue(elementId, out string displayName))
                return displayName;

            // Fallback to Rewired's name or generic
            return !string.IsNullOrEmpty(rewiredName) ? rewiredName : $"Button {elementId}";
        }

        public static string GetAxisDisplayName(int axisIndex, int direction, ControllerMappingType type, bool isGenericTemplate = false)
        {
            string dirName;
            switch (axisIndex)
            {
                case 0: // Left Stick X
                    dirName = direction > 0 ? "Right" : "Left";
                    return $"Left Stick {dirName}";
                case 1: // Left Stick Y
                    // Y-axis convention: generic template (+Y=Down), XInput/Steam Input (+Y=Up)
                    dirName = (isGenericTemplate ? direction > 0 : direction < 0) ? "Down" : "Up";
                    return $"Left Stick {dirName}";
                case 2: // Right Stick X
                    dirName = direction > 0 ? "Right" : "Left";
                    return $"Right Stick {dirName}";
                case 3:
                    if (type == ControllerMappingType.PlayStation && isGenericTemplate)
                    {
                        dirName = direction > 0 ? "Right" : "Left";
                        return $"Right Stick {dirName}";
                    }
                    dirName = (isGenericTemplate ? direction > 0 : direction < 0) ? "Down" : "Up";
                    return $"Right Stick {dirName}";
                case 4:
                    if (type == ControllerMappingType.PlayStation && isGenericTemplate)
                    {
                        dirName = direction > 0 ? "Down" : "Up";
                        return $"Right Stick {dirName}";
                    }
                    if (isGenericTemplate)
                        return type == ControllerMappingType.SwitchPro ? "ZR" : "RT";
                    if (type == ControllerMappingType.PlayStation)
                        return "L2";
                    return type == ControllerMappingType.SwitchPro ? "ZL" : "LT";
                case 5:
                    if (isGenericTemplate)
                        return type == ControllerMappingType.SwitchPro ? "ZL" : "LT";
                    if (type == ControllerMappingType.PlayStation)
                        return "R2";
                    return type == ControllerMappingType.SwitchPro ? "ZR" : "RT";
                default:
                    return $"Axis {axisIndex} {(direction > 0 ? "(+)" : "(-)")}";
            }
        }

        public KeyBinding()
        {
            this.name = string.Empty;
            AssignKey(KeyCode.None);
        }

        public KeyBinding(string name)
        {
            this.name = name;
            AssignKey(KeyCode.None);
        }

        public virtual bool Equals(KeyBinding other)
        {
            if (other == null)
                return false;

            // Rewired binding comparison
            if (this.IsRewiredBinding || other.IsRewiredBinding)
            {
                return this.rewiredPlayerId == other.rewiredPlayerId &&
                       this.rewiredInputId == other.rewiredInputId &&
                       this.rewiredIsAxis == other.rewiredIsAxis &&
                       this.joystickDirection == other.joystickDirection;
            }

            return this.key == other.key && this.modifiers == other.modifiers;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            KeyBinding keyBindingObj = obj as KeyBinding;
            if (keyBindingObj == null)
                return false;
            else
                return Equals(keyBindingObj);
        }

        public static bool operator ==(KeyBinding keyBinding1, KeyBinding keyBinding2)
        {
            if (((object)keyBinding1) == null || ((object)keyBinding2) == null)
                return object.Equals(keyBinding1, keyBinding2);

            return keyBinding1.Equals(keyBinding2);
        }

        public static bool operator !=(KeyBinding keyBinding1, KeyBinding keyBinding2)
        {
            if (((object)keyBinding1) == null || ((object)keyBinding2) == null)
                return !object.Equals(keyBinding1, keyBinding2);

            return !(keyBinding1.Equals(keyBinding2));
        }

        public override int GetHashCode()
        {
            if (IsRewiredBinding)
            {
                return rewiredPlayerId.GetHashCode() ^ rewiredInputId.GetHashCode() ^ rewiredIsAxis.GetHashCode() ^ joystickDirection.GetHashCode();
            }
            return this.key.GetHashCode() ^ this.modifiers.GetHashCode();
        }

        /// <summary>
        /// Checks whether a keybinding has been assigned.
        /// </summary>
        /// <returns>True if a keybinding is assigned, otherwise false.</returns>
        public bool HasKeyAssigned()
        {
            return this.key != KeyCode.None || this.IsRewiredBinding;
        }

        private Joystick GetRewiredJoystick()
        {
            if (rewiredPlayerId < 0 || rewiredPlayerId > 3 || !ReInput.isReady)
                return null;

            var player = ReInput.players.GetPlayer(rewiredPlayerId);
            if (player == null || player.controllers.joystickCount == 0)
                return null;

            return player.controllers.Joysticks[0];
        }

        /// <summary>
        /// Gets state of key
        /// </summary>
        /// <returns>True if key is pressed down</returns>
        public virtual bool IsDown()
        {
            // Rewired controller path
            if (IsRewiredBinding)
            {
                var joystick = GetRewiredJoystick();
                if (joystick == null) return false;

                if (rewiredIsAxis)
                {
                    float value = joystick.GetAxis(rewiredInputId);
                    return (joystickDirection > 0 && value >= axisThreshold) ||
                           (joystickDirection < 0 && value <= -axisThreshold);
                }
                return joystick.GetButtonById(rewiredInputId);
            }

            // Check if required modifiers are pressed
            if (modifiers != ModifierKeys.None)
            {
                ModifierKeys currentModifiers = ModifierKeys.None;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    currentModifiers |= ModifierKeys.Ctrl;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    currentModifiers |= ModifierKeys.Shift;
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                    currentModifiers |= ModifierKeys.Alt;

                if ((currentModifiers & modifiers) != modifiers)
                    return false;
            }

            return Input.GetKey(key);
        }

        /// <summary>
        /// Checks if key was just pressed
        /// </summary>
        /// <returns>True if key was pressed this frame</returns>
        public virtual bool PressedDown()
        {
            bool down = IsDown();
            if (!wasDown && down)
            {
                wasDown = down;
                return true;
            }
            else
            {
                wasDown = down;
                return false;
            }
        }

        /// <summary>
        /// Checks if key was just released
        /// </summary>
        /// <returns>True if key was released this frame</returns>
        public virtual bool Released()
        {
            bool down = IsDown();
            if (wasDown && !down)
            {
                wasDown = down;
                return true;
            }
            else
            {
                wasDown = down;
                return false;
            }
        }

        public virtual float GetAxis()
        {
            if (IsRewiredBinding && rewiredIsAxis)
            {
                var joystick = GetRewiredJoystick();
                if (joystick == null) return 0f;
                return joystick.GetAxis(rewiredInputId);
            }
            return -2f;
        }

        public virtual void AssignKey(KeyCode key)
        {
            if (key == KeyCode.Delete)
            {
                key = KeyCode.None;
            }
            this.key = key;
            this.isSettingKey = false;
            this.modifiers = ModifierKeys.None;
            this.joystickDirection = 1;
            this.joystickDisplayName = null;
            this.mappingType = ControllerMappingType.Xbox;
            this.rewiredPlayerId = -1;
            this.rewiredInputId = -1;
            this.rewiredIsAxis = false;
        }

        public virtual void AssignRewiredButton(int playerId, int elementId, ControllerMappingType type, string displayName)
        {
            ClearKey();
            this.rewiredPlayerId = playerId;
            this.rewiredInputId = elementId;
            this.rewiredIsAxis = false;
            this.mappingType = type;
            this.joystickDisplayName = displayName;
            this.isSettingKey = false;
        }

        public virtual void AssignRewiredAxis(int playerId, int axisIndex, int direction, ControllerMappingType type, string displayName)
        {
            ClearKey();
            this.rewiredPlayerId = playerId;
            this.rewiredInputId = axisIndex;
            this.rewiredIsAxis = true;
            this.joystickDirection = direction;
            this.mappingType = type;
            this.joystickDisplayName = displayName;
            this.isSettingKey = false;
        }

        public virtual void ClearKey()
        {
            this.key = KeyCode.None;
            this.isSettingKey = false;
            this.modifiers = ModifierKeys.None;
            this.joystickDirection = 1;
            this.joystickDisplayName = null;
            this.mappingType = ControllerMappingType.Xbox;
            this.rewiredPlayerId = -1;
            this.rewiredInputId = -1;
            this.rewiredIsAxis = false;
        }

        private string GetKeyDisplayString()
        {
            // Rewired binding
            if (IsRewiredBinding)
                return this.joystickDisplayName ?? "Controller";

            if (key == KeyCode.None)
                return "None";

            string displayString = "";
            if ((modifiers & ModifierKeys.Ctrl) != 0)
                displayString += "Ctrl+";
            if ((modifiers & ModifierKeys.Shift) != 0)
                displayString += "Shift+";
            if ((modifiers & ModifierKeys.Alt) != 0)
                displayString += "Alt+";
            displayString += key.ToString();

            return displayString;
        }

        private static bool TryDetectRewiredButton(float threshold, out int playerId, out int elementId, out ControllerMappingType type, out string displayName)
        {
            playerId = -1;
            elementId = -1;
            type = ControllerMappingType.Xbox;
            displayName = null;

            for (int p = 0; p < 4; p++)
            {
                var player = ReInput.players.GetPlayer(p);
                if (player == null || player.controllers.joystickCount == 0) continue;

                var joystick = player.controllers.Joysticks[0];
                foreach (var element in joystick.ElementIdentifiers)
                {
                    if (element.elementType != ControllerElementType.Button) continue;
                    if (joystick.GetButtonDownById(element.id))
                    {
                        playerId = p;
                        elementId = element.id;
                        type = GetMappingTypeFromJoystickName(joystick.name);
                        displayName = GetButtonDisplayName(element.id, type, element.name);
                        return true;
                    }
                }
            }
            return false;
        }

        // Track previous axis values during binding to detect transitions (edge detection)
        private static readonly KeyCode[] _allKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
        private static Dictionary<string, float> _bindingPrevAxisValues = new Dictionary<string, float>();
        private static bool _bindingInputInitialized = false;

        private static void InitializeBindingInputTracking()
        {
            _bindingPrevAxisValues.Clear();
            for (int p = 0; p < 4; p++)
            {
                var player = ReInput.players.GetPlayer(p);
                if (player == null || player.controllers.joystickCount == 0) continue;

                var joystick = player.controllers.Joysticks[0];

                // Track axes
                for (int i = 0; i < 10; i++)
                {
                    string key = $"{p}_axis_{i}";
                    _bindingPrevAxisValues[key] = joystick.GetAxis(i);
                }
            }
            _bindingInputInitialized = true;
        }

        private static void UpdateBindingInputTracking()
        {
            for (int p = 0; p < 4; p++)
            {
                var player = ReInput.players.GetPlayer(p);
                if (player == null || player.controllers.joystickCount == 0) continue;

                var joystick = player.controllers.Joysticks[0];

                // Update axes
                for (int i = 0; i < 10; i++)
                {
                    string key = $"{p}_axis_{i}";
                    _bindingPrevAxisValues[key] = joystick.GetAxis(i);
                }
            }
        }

        private static bool TryDetectRewiredAxis(float threshold, out int playerId, out int axisIndex, out int direction, out ControllerMappingType type, out string displayName)
        {
            playerId = -1;
            axisIndex = -1;
            direction = 1;
            type = ControllerMappingType.Xbox;
            displayName = null;

            if (!_bindingInputInitialized)
                return false;

            for (int p = 0; p < 4; p++)
            {
                var player = ReInput.players.GetPlayer(p);
                if (player == null || player.controllers.joystickCount == 0) continue;

                var joystick = player.controllers.Joysticks[0];
                for (int i = 0; i < 10; i++)
                {
                    float value = joystick.GetAxis(i);
                    string key = $"{p}_axis_{i}";
                    float prev = _bindingPrevAxisValues.ContainsKey(key) ? _bindingPrevAxisValues[key] : 0f;

                    // Edge detection: only trigger when crossing threshold
                    bool nowActive = Mathf.Abs(value) >= threshold;
                    bool wasActive = Mathf.Abs(prev) >= threshold;

                    if (nowActive && !wasActive)
                    {
                        playerId = p;
                        axisIndex = i;
                        direction = value > 0 ? 1 : -1;
                        type = GetMappingTypeFromJoystickName(joystick.name);

                        // Detect generic template by checking if button IDs are in 32+ range
                        bool isGenericTemplate = false;
                        foreach (var elem in joystick.ElementIdentifiers)
                        {
                            if (elem.elementType == ControllerElementType.Button && elem.id >= 32)
                            {
                                isGenericTemplate = true;
                                break;
                            }
                        }

                        displayName = GetAxisDisplayName(i, direction, type, isGenericTemplate);
                        return true;
                    }
                }
            }
            return false;
        }

        public static IEnumerator BindKey(KeyBinding keyBinding)
        {
            if (!ReInput.isReady)
            {
                RocketMain.Logger.Error("Cannot bind key: Rewired is not initialized.");
                yield break;
            }

            InputReader.IsBlocked = true;
            keyBinding.isSettingKey = true;
            yield return new WaitForSeconds(0.1f);

            // Initialize input tracking for edge detection
            InitializeBindingInputTracking();

            KeyCode[] keyCodes = _allKeyCodes;
            bool exit = false;

            while (!exit)
            {
                // Cancel on Escape
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    keyBinding.isSettingKey = false;
                    break;
                }

                // Clear binding on Delete
                if (Input.GetKeyDown(KeyCode.Delete))
                {
                    keyBinding.ClearKey();
                    exit = true;
                    break;
                }

                // Try Rewired controller button (check first - buttons take priority)
                if (TryDetectRewiredButton(keyBinding.axisThreshold, out int btnPlayerId, out int btnElementId, out ControllerMappingType btnType, out string btnDisplayName))
                {
                    keyBinding.AssignRewiredButton(btnPlayerId, btnElementId, btnType, btnDisplayName);
                    exit = true;
                    break;
                }

                // Try Rewired controller axis
                if (TryDetectRewiredAxis(keyBinding.axisThreshold, out int axisPlayerId, out int axisIndex, out int axisDirection, out ControllerMappingType axisType, out string axisDisplayName))
                {
                    keyBinding.AssignRewiredAxis(axisPlayerId, axisIndex, axisDirection, axisType, axisDisplayName);
                    exit = true;
                    break;
                }

                // Try keyboard (skip JoystickButton* KeyCodes, skip modifiers alone)
                foreach (KeyCode keyCode in keyCodes)
                {
                    if (!Input.GetKeyUp(keyCode)) continue;
                    if (keyCode.ToString().Contains("Joystick")) continue;

                    // Don't bind modifier keys alone
                    if (keyCode == KeyCode.LeftControl || keyCode == KeyCode.RightControl ||
                        keyCode == KeyCode.LeftShift || keyCode == KeyCode.RightShift ||
                        keyCode == KeyCode.LeftAlt || keyCode == KeyCode.RightAlt)
                    {
                        continue;
                    }

                    keyBinding.AssignKey(keyCode);

                    // Check and set modifier keys
                    keyBinding.modifiers = ModifierKeys.None;
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                        keyBinding.modifiers |= ModifierKeys.Ctrl;
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        keyBinding.modifiers |= ModifierKeys.Shift;
                    if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                        keyBinding.modifiers |= ModifierKeys.Alt;

                    exit = true;
                    break;
                }

                // Update input tracking for next frame
                UpdateBindingInputTracking();
                yield return null;
            }

            // Cleanup
            _bindingInputInitialized = false;
            InputReader.IsBlocked = false;
        }

        public virtual bool OnGUI(bool displayToolTip, bool displayName = false)
        {
            GUILayout.BeginHorizontal(RGUI.Unexpanded);
            if (displayName)
            {
                GUILayout.Label(name, RGUI.Unexpanded);
            }
            GUILayout.Space(10);
            bool result;
            string toolTip = displayToolTip ? "Press Delete to clear" : "";
            if (this.isSettingKey)
            {
                result = GUILayout.Button(new GUIContent("Press Any Key/Button", toolTip));
            }
            else
            {
                result = GUILayout.Button(new GUIContent(GetKeyDisplayString(), toolTip));
            }
            toolTipRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndHorizontal();
            if (result && !this.isSettingKey && !InputReader.IsBlocked)
            {
                UnityModManager.UI.Instance.StartCoroutine(BindKey(this));
            }
            else
            {
                result = false;
            }
            return result;
        }

        public virtual bool OnGUI(bool displayToolTip, bool displayName, bool includeNameInside)
        {
            GUILayout.BeginHorizontal(RGUI.Unexpanded);
            if (displayName)
            {
                GUILayout.Label(name, RGUI.Unexpanded);
                GUILayout.Space(10);
            }
            bool result;
            string toolTip = displayToolTip ? "Press Delete to clear" : "";
            string prefix = includeNameInside ? (name + ": ") : "";
            if (this.isSettingKey)
            {
                result = GUILayout.Button(new GUIContent(prefix + "Press Any Key/Button", toolTip));
            }
            else
            {
                result = GUILayout.Button(new GUIContent(prefix + GetKeyDisplayString(), toolTip));
            }
            toolTipRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndHorizontal();
            if (result && !this.isSettingKey && !InputReader.IsBlocked)
            {
                UnityModManager.UI.Instance.StartCoroutine(BindKey(this));
            }
            else
            {
                result = false;
            }
            return result;
        }
    }
}
