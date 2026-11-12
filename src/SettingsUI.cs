using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using UnityModManagerNet;

namespace AutoCackle
{
    /// <summary>Settings panel shown in the Unity Mod Manager window (Ctrl+F10).</summary>
    internal static class SettingsUI
    {
        private const float SliderWidth = 200f;
        private const float SectionSpacing = 10f;

        internal static void Draw(UnityModManager.ModEntry modEntry)
        {
            Settings settings = Main.Settings;

            settings.RealTime = GUILayout.Toggle(
                settings.RealTime,
                " Real-time combat: use Cackle/Chant as soon as the move action is available");
            settings.TurnBased = GUILayout.Toggle(
                settings.TurnBased,
                " Turn-based combat: use Cackle/Chant when you end the turn with a move action left");

            settings.RoundsThreshold = DrawSlider(
                $"In combat: extend a hex when it has {settings.RoundsThreshold} round(s) left or fewer",
                settings.RoundsThreshold,
                Settings.MinRoundsThreshold,
                Settings.MaxRoundsThreshold);

            settings.PlayAnimationInCombat = GUILayout.Toggle(
                settings.PlayAnimationInCombat,
                " In combat: play the casting animation, as when you use the ability yourself");
            settings.ShowInCombatLog = GUILayout.Toggle(
                settings.ShowInCombatLog,
                " In combat: show the automatic use in the combat log");

            GUILayout.Space(SectionSpacing);
            settings.OutOfCombat = GUILayout.Toggle(
                settings.OutOfCombat,
                " Out of combat: keep using Cackle/Chant to build up hex duration (costs no action)");
            settings.OutOfCombatMinutes = DrawSlider(
                $"Out of combat: bring hexes up to {settings.OutOfCombatMinutes} minute(s)",
                settings.OutOfCombatMinutes,
                Settings.MinOutOfCombatMinutes,
                Settings.MaxOutOfCombatMinutes);

            GUILayout.Space(SectionSpacing);
            settings.VerboseLog = GUILayout.Toggle(
                settings.VerboseLog,
                " Write every automatic use to the mod log (for troubleshooting)");

            GUILayout.Space(SectionSpacing);
            DrawCharacters(settings);
        }

        private static int DrawSlider(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            float slider = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(SliderWidth));
            GUILayout.EndHorizontal();
            return Mathf.Clamp(Mathf.RoundToInt(slider), min, max);
        }

        private static void DrawCharacters(Settings settings)
        {
            GUILayout.Label("Characters in the party with Cackle or Chant:");

            List<UnitEntityData> party;
            try
            {
                party = Game.Instance?.Player?.PartyAndPets;
            }
            catch (Exception)
            {
                // No game is loaded yet (main menu).
                party = null;
            }

            bool any = false;
            if (party != null)
            {
                foreach (UnitEntityData unit in party)
                {
                    string abilityName = unit != null ? CackleLogic.FindAbilityName(unit) : null;
                    if (abilityName == null)
                    {
                        continue;
                    }

                    any = true;
                    bool enabled = !settings.IsDisabled(unit.UniqueId);
                    bool toggled = GUILayout.Toggle(enabled, $" {unit.CharacterName} ({abilityName})");
                    if (toggled != enabled)
                    {
                        settings.SetDisabled(unit.UniqueId, !toggled);
                    }
                }
            }

            if (!any)
            {
                GUILayout.Label("  (none - load a save with a Witch or Shaman who has the hex)");
            }
        }
    }
}
