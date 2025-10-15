﻿using System;
using System.Collections.Generic;
using System.IO;

namespace RocketLib.UMM
{
    public class ModSave
    {
        public static string SavePath
        {
            get
            {
                return Path.Combine(Main.Mod.Path, "Keybindings.json");
            }
        }
        /// <summary>
        /// Military Salute
        /// </summary>
        public KeyBindingForPlayers gesture0;
        /// <summary>
        /// Waving
        /// </summary>
        public KeyBindingForPlayers gesture1;
        /// <summary>
        /// Point Direction
        /// </summary>
        public KeyBindingForPlayers gesture2;
        /// <summary>
        /// Thrust
        /// </summary>
        public KeyBindingForPlayers gesture3;
        /// <summary>
        /// Knee Drop
        /// </summary>
        public KeyBindingForPlayers gesture4;
        /// <summary>
        /// Shhh
        /// </summary>
        public KeyBindingForPlayers gesture5;
        /// <summary>
        /// Sing (Unused)
        /// </summary>
        public KeyBindingForPlayers gesture6;


        public void Save()
        {
            var json = AllModKeyBindings.ConvertToJson();
            File.WriteAllText(SavePath, json);
        }

        public static ModSave Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    AllModKeyBindings.ReadFromJson(File.ReadAllText(SavePath));
                    ModSave modsave = new ModSave();
                    Dictionary<string, KeyBindingForPlayers> modKeybindings;
                    AllModKeyBindings.TryGetAllKeyBindingsForMod(Main.Mod.Info.Id, out modKeybindings);
                    if (modKeybindings != null)
                    {
                        modKeybindings.TryGetValue("Gesture 0", out modsave.gesture0);
                        modKeybindings.TryGetValue("Gesture 1", out modsave.gesture1);
                        modKeybindings.TryGetValue("Gesture 2", out modsave.gesture2);
                        modKeybindings.TryGetValue("Gesture 3", out modsave.gesture3);
                        modKeybindings.TryGetValue("Gesture 4", out modsave.gesture4);
                        modKeybindings.TryGetValue("Gesture 5", out modsave.gesture5);
                        modKeybindings.TryGetValue("Gesture 6", out modsave.gesture6);
                    }
                    return modsave;
                }
            }
            // Likely failed to load due to a version change
            catch { }
            AllModKeyBindings.RecreateDictionary();
            return new ModSave();
        }

        public void Initialize()
        {
            try
            {
                if (gesture0 == null)
                    gesture0 = new KeyBindingForPlayers(Main.Mod.Info.Id, "Gesture 0");
                if (gesture1 == null)
                    gesture1 = new KeyBindingForPlayers(Main.Mod.Info.Id, "Gesture 1");
                if (gesture2 == null)
                    gesture2 = new KeyBindingForPlayers(Main.Mod.Info.Id, "Gesture 2");
                if (gesture3 == null)
                    gesture3 = new KeyBindingForPlayers(Main.Mod.Info.Id, "Gesture 3");
                if (gesture4 == null)
                    gesture4 = new KeyBindingForPlayers(Main.Mod.Info.Id, "Gesture 4");
                if (gesture5 == null)
                    gesture5 = new KeyBindingForPlayers(Main.Mod.Info.Id, "Gesture 5");
                if (gesture6 == null)
                    gesture6 = new KeyBindingForPlayers(Main.Mod.Info.Id, "Gesture 6");
            }
            catch (Exception e)
            {
                RocketMain.Logger.Exception(e);
            }
        }
    }
}
