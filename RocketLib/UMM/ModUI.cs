﻿using System;
using System.Collections.Generic;
using RocketLib.Loggers;
using RocketLib.Utils;
using UnityEngine;
using UnityModManagerNet;

namespace RocketLib.UMM
{
    internal static class ModUI
    {
        public const int KEYBIND_WINDOW_ID = -1;
        private static Settings settings
        {
            get { return Main.settings; }
        }

        private static readonly string[] _tabsName = new string[] { "<color=\"yellow\">Main</color>", "Screen Logger", "Scene Loader", "Log", "Key Bindings" };
        private static readonly Action[] _tabsAction = new Action[] { MainGUI, ScreenLoggerGUI, LoadSceneGUI, LogGUI, KeyBindings };

        private static readonly GUIStyle _logStyle = new GUIStyle();
        private static string _sceneStr = string.Empty;
        private static Vector2 _scrollViewVector = Vector2.zero;
        private static int _tabSelected = 0;
        private static readonly GUIStyle _testBtnStyle = new GUIStyle("button");
        private const string _changeKeyMessage = "Press Any Key";
        private static GUIStyle keybindModStyle;
        public static Dictionary<string, KeyBindingForPlayers> modKeyBindings;

        public static void Initialize()
        {
            _testBtnStyle.normal.textColor = Color.yellow;
            keybindModStyle = new GUIStyle();
            keybindModStyle.normal.background = (Texture2D)CreateTexture.WithColor(Color.gray.SetAlpha(0.5f));
        }

        public static void OnGui(UnityModManager.ModEntry modEntry)
        {
            _tabSelected = RGUI.Tab(_tabsName, _tabSelected, 10, 110);

            GUILayout.Space(30);
            Rect ToolTipRect = GUILayoutUtility.GetLastRect();
            _tabsAction[_tabSelected].Invoke();
            GUI.Label(ToolTipRect, GUI.tooltip);
        }

        public static void MainGUI()
        {
            GUILayout.BeginVertical("box");
            settings.ShowLogOnScreen = GUILayout.Toggle(settings.ShowLogOnScreen, "Enable OnScreenLog");
            GUILayout.EndVertical();
        }

        private static void ScreenLoggerGUI()
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Log on screen", GUILayout.Width(150)))
            {
                ScreenLogger.Instance.Clear();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Main.settings.ShowDebugLogs = GUILayout.Toggle(Main.settings.ShowDebugLogs, "Show Debug logs");
            settings.ShowManagerLog = GUILayout.Toggle(settings.ShowManagerLog, new GUIContent("Show Unity Mod Manager Log"));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Time before log disappear : " + settings.LogTimer.ToString(), GUILayout.ExpandWidth(false));
            settings.LogTimer = (int)GUILayout.HorizontalScrollbar(settings.LogTimer, 1f, 1f, 11f, GUILayout.MaxWidth(200));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Log Font Size : " + settings.FontSize.ToString(), GUILayout.ExpandWidth(false));
            settings.FontSize = (int)GUILayout.HorizontalScrollbar(settings.FontSize, 1f, 1f, 25f, GUILayout.MaxWidth(200));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private static void LoadSceneGUI()
        {
            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            _sceneStr = GUILayout.TextField(_sceneStr, GUILayout.Width(200));
            GUILayout.Space(10);
            if (GUILayout.Button("Load Scene", new GUILayoutOption[] { GUILayout.Width(150) }))
            {
                try
                {
                    Utility.SceneLoader.LoadScene(_sceneStr);
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Exception(ex);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            if (GUILayout.Button("Load Main Menu", new GUILayoutOption[] { GUILayout.Width(150) }))
            {
                try
                {
                    Utility.SceneLoader.LoadScene("MainMenu");
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Exception(ex);
                }
            }
            GUILayout.EndVertical();
        }

        private static void LogGUI()
        {
            GUILayout.BeginVertical("box");
            _scrollViewVector = GUILayout.BeginScrollView(_scrollViewVector, GUILayout.Height(250));
            foreach (string log in ScreenLogger.Instance.FullLogList)
            {
                _logStyle.normal.textColor = ScreenLogger.WhichColor(log);
                GUILayout.Label(log, _logStyle);
                GUILayout.Space(5);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static void KeyBindings()
        {
            try
            {
                int player = 0;
                GUILayout.BeginVertical("box", GUILayout.ExpandWidth(false));
                RGUI.LabelCenteredHorizontally(new GUIContent("RocketLib"), GUI.skin.label, RGUI.Unexpanded);
                if (modKeyBindings == null)
                {
                    AllModKeyBindings.TryGetAllKeyBindingsForMod("RocketLib", out modKeyBindings);
                }
                if (modKeyBindings != null)
                {
                    foreach (KeyValuePair<string, KeyBindingForPlayers> pair in modKeyBindings)
                    {
                        pair.Value.OnGUI(out player, true);
                        GUILayout.Space(30);
                    }
                }
                if (GUILayout.Button("Clear All", GUILayout.Width(100)))
                {
                    AllModKeyBindings.ClearKeyBindingsForMod("RocketLib");
                }
                GUILayout.EndVertical();
                GUILayout.Space(30);
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Exception(ex);
            }
        }
    }
}
