using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using RocketLib.UI;
using UnityEngine;
using UnityModManagerNet;

namespace RocketLib.Utils
{
    /// <summary>Connects ReferenceScanner to the installed libraries and the game's assemblies.</summary>
    public static class ModCompatibility
    {
        private const string RocketLibAssembly = "RocketLib";
        private const string BroMakerAssembly = "BroMakerLib";
        private const string BroMakerModId = "BroMaker";
        private const string RocketLibModId = "RocketLib";
        private const float NotificationDuration = 6f;
        private const int MaxListedItems = 3;
        private const int MaxListedSites = 3;
        private const string RichTextTag = "</?(?:b|i|size|color|material|quad)\\b[^>]*>";

        private static readonly string[] Libraries = { BroMakerAssembly, RocketLibAssembly };

        private static readonly string[] GameAssemblies =
        {
            "Assembly-CSharp.dll",
            "Assembly-CSharp-firstpass.dll",
            "UnityEngine.dll",
            "UnityEngine.CoreModule.dll"
        };

        /// <summary>Creates a scanner for the installed libraries and the game assemblies; the caller disposes it.</summary>
        public static ReferenceScanner CreateScanner()
        {
            string managed = Path.Combine(Application.dataPath, "Managed");
            string[] gamePaths = new string[GameAssemblies.Length];
            for (int i = 0; i < gamePaths.Length; i++)
            {
                gamePaths[i] = Path.Combine(managed, GameAssemblies[i]);
            }
            return new ReferenceScanner(new[] { InstalledPath(RocketLibAssembly), InstalledPath(BroMakerAssembly) }, gamePaths);
        }

        /// <summary>Returns the three-part assembly version of the named library's file, or null when it isn't installed.</summary>
        public static Version InstalledVersion(string library)
        {
            string path = InstalledPath(library);
            if (path == null)
            {
                return null;
            }

            try
            {
                Version version = AssemblyName.GetAssemblyName(path).Version;
                return new Version(version.Major, version.Minor, System.Math.Max(version.Build, 0));
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void ReportStartup()
        {
            Stopwatch timer = Stopwatch.StartNew();
            int checkedMods = 0;
            using (ReferenceScanner scanner = CreateScanner())
            {
                foreach (UnityModManager.ModEntry mod in UnityModManager.modEntries)
                {
                    if (!mod.Active || string.IsNullOrEmpty(mod.Info.AssemblyName) || mod.Info.Id == RocketLibModId)
                    {
                        continue;
                    }

                    ReferenceScanResult result = scanner.Scan(Path.Combine(mod.Path, mod.Info.AssemblyName));
                    if (result.Checked)
                    {
                        checkedMods++;
                    }
                    string name = string.IsNullOrEmpty(mod.Info.DisplayName) ? mod.Info.Id : mod.Info.DisplayName;
                    ReportMod(StripRichText(name), mod.Info.AssemblyName, result);
                }
            }
            RocketMain.Logger.Log("Checked " + checkedMods + " mods for compatibility in " + timer.ElapsedMilliseconds + " ms.");
        }

        private static void ReportMod(string name, string dll, ReferenceScanResult result)
        {
            if (!result.Checked)
            {
                RocketMain.Logger.Log("Couldn't check '" + dll + "' of '" + name + "' (" + result.UncheckedReason + ").");
                return;
            }

            if (result.Unresolved.Count > 0)
            {
                RocketMain.Logger.Log("'" + name + "' has references that couldn't be followed: " + string.Join("; ", ToArray(result.Unresolved)));
            }

            if (result.Missing.Count == 0)
            {
                return;
            }

            List<string> libraries = new List<string>();
            foreach (MissingReference missing in result.Missing)
            {
                if (!libraries.Contains(missing.Library))
                {
                    libraries.Add(missing.Library);
                }
            }

            Version[] referenced = new Version[libraries.Count];
            Version[] installed = new Version[libraries.Count];
            for (int i = 0; i < libraries.Count; i++)
            {
                referenced[i] = result.ReferencedVersion(libraries[i]);
                installed[i] = InstalledVersion(libraries[i]);
            }

            List<string> builtAgainst = new List<string>();
            foreach (string library in Libraries)
            {
                Version version = result.ReferencedVersion(library);
                if (version != null)
                {
                    builtAgainst.Add(library + " " + version);
                }
            }

            RocketMain.Logger.Warning(WarningLine(name, dll, builtAgainst.ToArray(), result.Missing));
            string sentence = Sentence(name, libraries.ToArray(), referenced, installed, MissingList(result.Missing));
            Notification.Show(sentence, NotificationSeverity.Warning, NotificationDuration, name);
        }

        private static string StripRichText(string name)
        {
            return Regex.Replace(name, RichTextTag, string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        private static string PlayerName(string library)
        {
            return library == BroMakerAssembly ? BroMakerModId : library;
        }

        private static bool IsVersionUnknown(Version referenced, Version installed)
        {
            return referenced == null || installed == null || referenced == installed;
        }

        private static string Clause(string library, Version referenced, Version installed)
        {
            if (IsVersionUnknown(referenced, installed))
            {
                return PlayerName(library) + " that " + (installed != null ? installed.ToString() : "the installed version") + " doesn't have";
            }
            string built = PlayerName(library) + " " + referenced + " that " + installed;
            return referenced < installed ? built + " no longer has" : built + " doesn't have yet";
        }

        private static string Sentence(string mod, string[] libraries, Version[] referenced, Version[] installed, string list)
        {
            string[] clauses = new string[libraries.Length];
            List<string> toUpdate = new List<string>();
            for (int i = 0; i < libraries.Length; i++)
            {
                clauses[i] = Clause(libraries[i], referenced[i], installed[i]);
                if (!IsVersionUnknown(referenced[i], installed[i]) && referenced[i] > installed[i])
                {
                    toUpdate.Add(PlayerName(libraries[i]));
                }
            }

            string outcome = toUpdate.Count == libraries.Length
                ? "so some of its features may fail. Update " + string.Join(" and ", toUpdate.ToArray()) + " to fix it."
                : "so some of its features may fail until its author updates it.";
            return "'" + mod + "' uses parts of " + string.Join(" and ", clauses) + ", " + outcome + " Missing: " + list + ".";
        }

        private static string MissingList(IList<MissingReference> missing)
        {
            List<string> displays = new List<string>();
            Dictionary<string, List<string>> sites = new Dictionary<string, List<string>>();
            foreach (MissingReference reference in missing)
            {
                List<string> merged;
                if (!sites.TryGetValue(reference.Display, out merged))
                {
                    merged = new List<string>();
                    sites[reference.Display] = merged;
                    displays.Add(reference.Display);
                }
                foreach (string site in reference.UsedIn)
                {
                    if (merged.Count < MaxListedSites && !merged.Contains(site))
                    {
                        merged.Add(site);
                    }
                }
            }

            int listed = System.Math.Min(displays.Count, MaxListedItems);
            string[] items = new string[listed];
            for (int i = 0; i < listed; i++)
            {
                List<string> itemSites = sites[displays[i]];
                items[i] = itemSites.Count > 0 ? displays[i] + " in " + string.Join(", ", itemSites.ToArray()) : displays[i];
            }

            string list = string.Join("; ", items);
            return displays.Count > listed ? list + "; and " + (displays.Count - listed) + " more" : list;
        }

        private static string WarningLine(string mod, string dll, string[] builtAgainst, IList<MissingReference> missing)
        {
            string[] entries = new string[missing.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = missing[i].Library + ":" + missing[i].Target;
                if (missing[i].UsedIn.Count > 0)
                {
                    entries[i] += " [in " + string.Join("; ", ToArray(missing[i].UsedIn)) + "]";
                }
            }
            return "'" + mod + "' (" + dll + ", built against " + string.Join(", ", builtAgainst) + ") references: " + string.Join("; ", entries);
        }

        private static string[] ToArray(IList<string> strings)
        {
            return new List<string>(strings).ToArray();
        }

        private static string InstalledPath(string library)
        {
            UnityModManager.ModEntry mod = UnityModManager.FindMod(library == BroMakerAssembly ? BroMakerModId : library);
            if (mod == null || !mod.Enabled || mod.ErrorOnLoading)
            {
                return null;
            }

            string path = Path.Combine(mod.Path, mod.Info.AssemblyName);
            return File.Exists(path) ? path : null;
        }
    }
}
