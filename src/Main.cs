using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace AutoCackle
{
    /// <summary>Unity Mod Manager entry point.</summary>
    public static class Main
    {
        private const int MaxLoggedErrors = 5;

        internal static bool Enabled;
        internal static Settings Settings;

        private static UnityModManager.ModEntry.ModLogger logger;
        private static Harmony harmony;
        private static int loggedErrors;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            logger = modEntry.Logger;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = SettingsUI.Draw;
            modEntry.OnSaveGUI = OnSaveGUI;

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Enabled = true;
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            return true;
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }

        internal static void Verbose(string message)
        {
            if (Settings.VerboseLog)
            {
                logger.Log(message);
            }
        }

        /// <summary>
        /// Logs an exception raised inside a game hook. The hooks run every frame, so only the
        /// first few errors are written to keep the log readable.
        /// </summary>
        internal static void LogError(string where, Exception exception)
        {
            if (loggedErrors >= MaxLoggedErrors)
            {
                return;
            }

            loggedErrors++;
            logger.Error($"Error in {where}: {exception}");
            if (loggedErrors == MaxLoggedErrors)
            {
                logger.Error("Further errors from this mod are not logged.");
            }
        }
    }
}
