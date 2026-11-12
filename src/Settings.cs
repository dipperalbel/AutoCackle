using System.Collections.Generic;
using UnityModManagerNet;

namespace AutoCackle
{
    /// <summary>Mod settings, saved by Unity Mod Manager as Settings.xml in the mod folder.</summary>
    public class Settings : UnityModManager.ModSettings
    {
        public const int MinRoundsThreshold = 1;
        public const int MaxRoundsThreshold = 10;
        public const int MinOutOfCombatMinutes = 1;
        public const int MaxOutOfCombatMinutes = 10;

        /// <summary>Use Cackle/Chant automatically in real-time combat.</summary>
        public bool RealTime = true;

        /// <summary>Use Cackle/Chant automatically at the end of a turn in turn-based combat.</summary>
        public bool TurnBased = true;

        /// <summary>A hex is extended when it has this many rounds left, or fewer.</summary>
        public int RoundsThreshold = 2;

        /// <summary>
        /// In combat, give the ability as a normal command so the character plays the casting
        /// animation. When off, the ability is resolved at once without animation.
        /// </summary>
        public bool PlayAnimationInCombat = true;

        /// <summary>Use Cackle/Chant automatically out of combat to build up hex duration.</summary>
        public bool OutOfCombat = true;

        /// <summary>Out of combat, hexes are brought up to this many minutes.</summary>
        public int OutOfCombatMinutes = 5;

        /// <summary>Show the automatic use of the ability in the combat log (in combat only).</summary>
        public bool ShowInCombatLog = true;

        /// <summary>Write every automatic use to the Unity Mod Manager log.</summary>
        public bool VerboseLog = false;

        /// <summary>Unique ids of the characters the player switched the automation off for.</summary>
        public List<string> DisabledCharacters = new List<string>();

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public bool IsDisabled(string characterId)
        {
            return DisabledCharacters.Contains(characterId);
        }

        public void SetDisabled(string characterId, bool disabled)
        {
            if (disabled)
            {
                if (!DisabledCharacters.Contains(characterId))
                {
                    DisabledCharacters.Add(characterId);
                }
            }
            else
            {
                DisabledCharacters.Remove(characterId);
            }
        }
    }
}
