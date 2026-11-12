using System;
using HarmonyLib;
using Kingmaker.Controllers.Combat;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands.Base;
using TurnBased.Controllers;
using UnityEngine;

namespace AutoCackle
{
    /// <summary>
    /// Real-time combat: the game ticks this controller once per frame for every living unit
    /// in combat (not while paused), which is where action cooldowns are counted down.
    /// </summary>
    [HarmonyPatch(typeof(UnitCombatCooldownsController), "TickOnUnit")]
    internal static class RealTimePatch
    {
        private static void Postfix(UnitEntityData unit)
        {
            if (!Main.Enabled || !Main.Settings.RealTime || CombatController.IsInTurnBasedCombat())
            {
                return;
            }

            try
            {
                CackleLogic.RealTimeTick(unit);
            }
            catch (Exception exception)
            {
                Main.LogError(nameof(RealTimePatch), exception);
            }
        }
    }

    /// <summary>
    /// Out of combat: the game ticks this controller once per frame for every living, awake
    /// unit in the area, in or out of combat.
    /// </summary>
    [HarmonyPatch(typeof(UnitTicksController), "TickOnUnit")]
    internal static class OutOfCombatPatch
    {
        private static void Postfix(UnitEntityData unit)
        {
            if (!Main.Enabled || !Main.Settings.OutOfCombat)
            {
                return;
            }

            try
            {
                CackleLogic.OutOfCombatTick(unit);
            }
            catch (Exception exception)
            {
                Main.LogError(nameof(OutOfCombatPatch), exception);
            }
        }
    }

    /// <summary>
    /// Turn-based combat: Cackle/Chant is used when the player ends the turn, with the move
    /// action left unused. Acting last means a hex cast during the turn is extended too, and
    /// the player keeps the choice of spending the move action on something else.
    ///
    /// With the animation, the ability is given as a normal command and the end of the turn
    /// is held back until that command is over; then the turn is ended for real.
    /// </summary>
    internal static class TurnBasedHook
    {
        /// <summary>Longest the end of a turn is held back for the command, in seconds of play.</summary>
        private const float MaxWaitSeconds = 6f;

        private static TurnController pendingTurn;
        private static UnitCommand pendingCommand;
        private static float waitedSeconds;
        private static bool endingTurn;

        /// <summary>
        /// Called when the player asks to end the turn. Returns false to hold the end of the
        /// turn back while the Cackle/Chant command runs.
        /// </summary>
        internal static bool OnEndTurnRequested(TurnController turn)
        {
            if (endingTurn)
            {
                return true;
            }

            if (pendingCommand != null && ReferenceEquals(pendingTurn, turn))
            {
                // The player asked again while the command is still running.
                return false;
            }

            UnitEntityData unit = turn.Rider;
            AbilityData ability = CackleLogic.FindAbilityToUseInCombat(unit, Main.Settings.RoundsThreshold);
            if (ability == null)
            {
                return true;
            }

            // A command would replace whatever the character is still doing; in that case, and
            // when the animation is switched off, resolve the ability directly and end the turn.
            if (!Main.Settings.PlayAnimationInCombat || !unit.Commands.Empty)
            {
                CackleLogic.CastInCombat(unit, ability);
                return true;
            }

            UnitCommand command = CackleLogic.CreateCommand(unit, ability);
            unit.Commands.Run(command);
            pendingTurn = turn;
            pendingCommand = command;
            waitedSeconds = 0f;
            return false;
        }

        /// <summary>Called every frame of the current turn (not while the game is paused).</summary>
        internal static void OnTurnTick(TurnController turn)
        {
            if (pendingCommand == null)
            {
                return;
            }

            if (!ReferenceEquals(pendingTurn, turn))
            {
                // Another turn is running: the one that was waiting is gone.
                Clear();
                return;
            }

            waitedSeconds += Time.unscaledDeltaTime;
            bool over = pendingCommand.IsFinished || !turn.Rider.Commands.ContainsOrQueued(pendingCommand);
            if (!over && waitedSeconds < MaxWaitSeconds)
            {
                return;
            }

            Clear();

            // The game may already have ended the turn itself once the last action was spent.
            if (turn.CanEndTurn())
            {
                endingTurn = true;
                try
                {
                    turn.ForceToEnd(true);
                }
                finally
                {
                    endingTurn = false;
                }
            }
        }

        private static void Clear()
        {
            pendingTurn = null;
            pendingCommand = null;
            waitedSeconds = 0f;
        }
    }

    /// <summary>
    /// Every "end turn" input (button, key, gamepad) calls ForceToEnd(true); the prefix runs
    /// before the game marks all actions as used. ForceToEnd(false) delays the turn instead of
    /// ending it, so nothing must be spent then.
    /// </summary>
    [HarmonyPatch(typeof(TurnController), nameof(TurnController.ForceToEnd))]
    internal static class EndTurnPatch
    {
        private static bool Prefix(TurnController __instance, bool setCooldowns)
        {
            if (!setCooldowns || !Main.Enabled || !Main.Settings.TurnBased || !CombatController.IsInTurnBasedCombat())
            {
                return true;
            }

            try
            {
                return TurnBasedHook.OnEndTurnRequested(__instance);
            }
            catch (Exception exception)
            {
                Main.LogError(nameof(EndTurnPatch), exception);
                return true;
            }
        }
    }

    /// <summary>Follows the command given at the end of the turn and ends the turn when it is over.</summary>
    [HarmonyPatch(typeof(TurnController), "Tick")]
    internal static class TurnTickPatch
    {
        private static void Postfix(TurnController __instance)
        {
            try
            {
                TurnBasedHook.OnTurnTick(__instance);
            }
            catch (Exception exception)
            {
                Main.LogError(nameof(TurnTickPatch), exception);
            }
        }
    }
}
