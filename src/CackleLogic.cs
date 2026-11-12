using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Controllers.Combat;
using Kingmaker.Designers;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace AutoCackle
{
    /// <summary>
    /// Decides when a character should use Cackle (Witch) or Chant (Shaman) and uses it.
    /// The ability is resolved through the game's own cast rule, so its effect and its visual
    /// effect are the game's; the mod only removes the click.
    ///
    /// In combat the use costs the move action and keeps hexes from expiring; by default it is
    /// given as a normal command, so the character plays the animation. Out of combat the game
    /// charges no action for it, so the mod builds up a reserve of duration instead, resolving
    /// the ability directly because a refill takes dozens of uses.
    /// </summary>
    internal static class CackleLogic
    {
        private const float SecondsPerRound = 6f;

        /// <summary>Tolerance for comparing in-combat durations, which are multiples of a round.</summary>
        private const float DurationToleranceSeconds = 0.5f;

        /// <summary>Radius used if the ability blueprint has no area component (vanilla: 30 feet).</summary>
        private const int FallbackRadiusFeet = 30;

        private const int MaxActionDepth = 8;

        /// <summary>Lowest rounds threshold applied when a use is queued behind the current action.</summary>
        private const int MinRoundsWhenQueued = 2;

        /// <summary>Out of combat a refill starts when a hex falls to this share of the target duration.</summary>
        private const double RefillStartFraction = 0.8;

        /// <summary>Uses allowed in one refill beyond the number the target duration needs.</summary>
        private const int RefillExtraUses = 10;

        /// <summary>How often a character is evaluated (and, out of combat, how fast it refills).</summary>
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

        /// <summary>Pause after a refill that used more casts than the target duration can need.</summary>
        private static readonly TimeSpan RefillBlockDuration = TimeSpan.FromSeconds(60);

        // WitchHexCackleAbility and ShamanHexChantAbility.
        private static readonly HashSet<BlueprintGuid> AbilityGuids = new HashSet<BlueprintGuid>
        {
            BlueprintGuid.Parse("4bd01292a9bc4304f861a6a07f03b855"),
            BlueprintGuid.Parse("6cd07c80aabf2b248a11921090de9c17"),
        };

        private static readonly Dictionary<BlueprintGuid, List<BlueprintBuff>> ExtendedBuffsCache =
            new Dictionary<BlueprintGuid, List<BlueprintBuff>>();

        private static readonly Dictionary<string, UnitTracking> Tracking = new Dictionary<string, UnitTracking>();

        // The game records the last move-action ability here to forbid a full attack right after it.
        private static readonly FieldInfo LastMoveActionTimeField =
            AccessTools.Field(typeof(UnitCombatState), "m_LastUsageOfMoveActionTime");

        /// <summary>Per-character state kept between evaluations.</summary>
        private sealed class UnitTracking
        {
            public TimeSpan NextCheck;
            public UnitCommand PendingCommand;
            public bool Refilling;
            public int RefillUses;
            public TimeSpan RefillBlockedUntil;
        }

        internal static bool IsCackleAbility(BlueprintAbility blueprint)
        {
            return blueprint != null && AbilityGuids.Contains(blueprint.AssetGuid);
        }

        /// <summary>Returns the name of the unit's Cackle/Chant ability, or null if it has none.</summary>
        internal static string FindAbilityName(UnitEntityData unit)
        {
            foreach (Ability fact in unit.Abilities.RawFacts)
            {
                if (IsCackleAbility(fact.Blueprint))
                {
                    return fact.Blueprint.Name;
                }
            }

            return null;
        }

        /// <summary>
        /// Real-time combat: called every frame for every unit in combat; the evaluation runs at
        /// most twice per second per character.
        /// </summary>
        internal static void RealTimeTick(UnitEntityData unit)
        {
            if (!unit.IsPlayerFaction)
            {
                return;
            }

            UnitTracking tracking = GetTracking(unit);
            if (!IsCheckDue(tracking) || IsCommandPending(unit, tracking))
            {
                return;
            }

            // With the animation in real time a use can wait several seconds for the character to
            // finish an attack, so it is asked for at least two rounds before the hex expires.
            bool animated = Main.Settings.PlayAnimationInCombat && !unit.StopState;
            int rounds = animated
                ? Math.Max(Main.Settings.RoundsThreshold, MinRoundsWhenQueued)
                : Main.Settings.RoundsThreshold;

            AbilityData ability = FindAbilityToUseInCombat(unit, rounds);
            if (ability == null)
            {
                return;
            }

            if (animated)
            {
                QueueCommand(unit, ability, tracking);
            }
            else
            {
                // A character told to stop stays stopped: a command would cancel that order.
                Cast(unit, ability, true);
            }
        }

        /// <summary>
        /// In combat: returns the unit's Cackle/Chant ability if the unit may use it automatically
        /// now, has the action for it, and has a hex in range with no more than
        /// <paramref name="rounds"/> rounds left; otherwise null.
        /// </summary>
        internal static AbilityData FindAbilityToUseInCombat(UnitEntityData unit, int rounds)
        {
            if (unit == null || !unit.IsInCombat || !CanUseAutomatically(unit))
            {
                return null;
            }

            TimeSpan threshold = TimeSpan.FromSeconds(rounds * SecondsPerRound + DurationToleranceSeconds);
            AbilityData ability = FindUsefulAbility(unit, threshold);
            if (ability == null || unit.CombatState.HasCooldownForCommand(ability.RuntimeActionType))
            {
                return null;
            }

            return ability;
        }

        /// <summary>
        /// Creates the same command the action bar creates when the ability is clicked, so the
        /// game plays the animation, spends the action and writes the log line itself.
        /// </summary>
        internal static UnitCommand CreateCommand(UnitEntityData unit, AbilityData ability)
        {
            UnitCommand command = UnitUseAbility.CreateCastCommand(ability, new TargetWrapper(unit));

            // A command given by the player resets the target the character was told to attack;
            // this one must leave it alone.
            command.DoNotUpdateManualTarget();
            if (command is UnitUseAbility useAbility)
            {
                useAbility.DisableLog = !Main.Settings.ShowInCombatLog;
            }

            return command;
        }

        /// <summary>Resolves the ability at once, without command or animation, and pays the action.</summary>
        internal static bool CastInCombat(UnitEntityData unit, AbilityData ability)
        {
            return Cast(unit, ability, true);
        }

        /// <summary>
        /// Real time: puts the command at the head of the unit's command queue. The game runs a
        /// queued command as soon as the current action (an attack round, a spell, a walk) is
        /// over, so the automatic use never interrupts what the character is doing.
        /// </summary>
        private static void QueueCommand(UnitEntityData unit, AbilityData ability, UnitTracking tracking)
        {
            UnitCommand command = CreateCommand(unit, ability);
            command.Init(unit);
            unit.Commands.Queue.AddFirst(command);
            tracking.PendingCommand = command;
            Main.Verbose($"{unit.CharacterName}: queued {ability.Name}.");
        }

        /// <summary>
        /// True while a command queued earlier is still waiting or running. It stops being
        /// pending when it finishes, is interrupted, or is dropped from the queue because the
        /// player or the AI gave another command; the next evaluation then queues a new one.
        /// </summary>
        private static bool IsCommandPending(UnitEntityData unit, UnitTracking tracking)
        {
            UnitCommand command = tracking.PendingCommand;
            if (command == null)
            {
                return false;
            }

            if (command.IsFinished || !unit.Commands.ContainsOrQueued(command))
            {
                tracking.PendingCommand = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Out of combat: called every frame for every awake unit in the area. Runs only during
        /// normal exploration (not in dialogues, cutscenes, pause or on the map) and only while
        /// the whole party is out of combat.
        /// </summary>
        internal static void OutOfCombatTick(UnitEntityData unit)
        {
            if (!unit.IsPlayerFaction || unit.IsInCombat)
            {
                return;
            }

            UnitTracking tracking = GetTracking(unit);
            if (!IsCheckDue(tracking))
            {
                return;
            }

            if (Game.Instance.CurrentMode != GameModeType.Default || Game.Instance.Player.IsInCombat)
            {
                return;
            }

            TryRefill(unit, tracking);
        }

        /// <summary>
        /// Out of combat: brings the unit's hexes in range up to the configured duration, one use
        /// per evaluation, then waits until one of them has dropped noticeably before refilling.
        /// The wait avoids using the ability every round forever just to add back one round.
        /// </summary>
        private static void TryRefill(UnitEntityData unit, UnitTracking tracking)
        {
            if (!CanUseAutomatically(unit))
            {
                return;
            }

            TimeSpan now = Game.Instance.TimeController.GameTime;
            if (IsWithin(now, tracking.RefillBlockedUntil, RefillBlockDuration))
            {
                return;
            }

            TimeSpan target = TimeSpan.FromMinutes(Main.Settings.OutOfCombatMinutes);
            TimeSpan limit = tracking.Refilling
                ? target
                : TimeSpan.FromSeconds(target.TotalSeconds * RefillStartFraction);

            AbilityData ability = FindUsefulAbility(unit, limit);
            if (ability == null)
            {
                tracking.Refilling = false;
                tracking.RefillUses = 0;
                return;
            }

            // Each use adds one round. If far more uses than the target needs did not get the
            // hexes there, something is not being extended; stop instead of casting endlessly.
            int maxUses = (int)Math.Ceiling(target.TotalSeconds / SecondsPerRound) + RefillExtraUses;
            if (tracking.RefillUses >= maxUses)
            {
                tracking.Refilling = false;
                tracking.RefillUses = 0;
                tracking.RefillBlockedUntil = now + RefillBlockDuration;
                Main.Verbose($"{unit.CharacterName}: refill stopped after {maxUses} uses without reaching the target duration.");
                return;
            }

            tracking.Refilling = true;
            if (Cast(unit, ability, false))
            {
                tracking.RefillUses++;
            }
        }

        private static bool CanUseAutomatically(UnitEntityData unit)
        {
            return unit.IsPlayerFaction
                && unit.IsDirectlyControllable
                && unit.Descriptor.State.CanAct
                && !Main.Settings.IsDisabled(unit.UniqueId);
        }

        /// <summary>
        /// Returns the unit's Cackle/Chant ability if using it now would extend a hex with no
        /// more than <paramref name="maxTimeLeft"/> remaining; otherwise null.
        /// </summary>
        private static AbilityData FindUsefulAbility(UnitEntityData unit, TimeSpan maxTimeLeft)
        {
            foreach (Ability fact in unit.Abilities.RawFacts)
            {
                if (!IsCackleAbility(fact.Blueprint))
                {
                    continue;
                }

                AbilityData ability = fact.Data;
                if (ability != null && ability.IsAvailable && HasHexToExtend(unit, ability, maxTimeLeft))
                {
                    return ability;
                }
            }

            return null;
        }

        /// <summary>
        /// True if using the ability now would extend one of the caster's hexes that has no more
        /// than <paramref name="maxTimeLeft"/> remaining. Mirrors the ability's own targeting:
        /// units in line of sight within its radius, and for each one the buff it would extend.
        /// </summary>
        private static bool HasHexToExtend(UnitEntityData caster, AbilityData ability, TimeSpan maxTimeLeft)
        {
            List<BlueprintBuff> extendedBuffs = GetExtendedBuffs(ability.Blueprint);
            if (extendedBuffs.Count == 0)
            {
                return false;
            }

            AbilityTargetsAround area = ability.Blueprint.GetComponent<AbilityTargetsAround>();
            Feet radius = area != null ? area.AoERadius : FallbackRadiusFeet.Feet();

            foreach (UnitEntityData target in GameHelper.GetTargetsAround(caster.EyePosition, radius, true, false))
            {
                foreach (BlueprintBuff blueprint in extendedBuffs)
                {
                    Buff buff = target.Buffs.GetBuff(blueprint);
                    if (buff == null || buff.IsPermanent || buff.TimeLeft > maxTimeLeft)
                    {
                        continue;
                    }

                    if (ReferenceEquals(buff.Context?.MaybeCaster, caster))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Reads from the ability blueprint the buffs it extends, so hexes added to Cackle/Chant
        /// by other mods are picked up without a hard-coded list.
        /// </summary>
        private static List<BlueprintBuff> GetExtendedBuffs(BlueprintAbility blueprint)
        {
            if (ExtendedBuffsCache.TryGetValue(blueprint.AssetGuid, out List<BlueprintBuff> cached))
            {
                return cached;
            }

            List<BlueprintBuff> buffs = new List<BlueprintBuff>();
            AbilityEffectRunAction effect = blueprint.GetComponent<AbilityEffectRunAction>();
            if (effect != null)
            {
                CollectExtendedBuffs(effect.Actions, buffs, 0);
            }

            ExtendedBuffsCache[blueprint.AssetGuid] = buffs;
            Main.Verbose($"{blueprint.name} extends {buffs.Count} buff(s).");
            return buffs;
        }

        private static void CollectExtendedBuffs(ActionList list, List<BlueprintBuff> buffs, int depth)
        {
            if (list?.Actions == null || depth > MaxActionDepth)
            {
                return;
            }

            foreach (GameAction action in list.Actions)
            {
                if (action == null)
                {
                    continue;
                }

                if (action is ContextActionReduceBuffDuration change)
                {
                    BlueprintBuff buff = change.TargetBuff;
                    if (change.Increase && change.ToTarget && buff != null && !buffs.Contains(buff))
                    {
                        buffs.Add(buff);
                    }

                    continue;
                }

                // Any other action may nest action lists (the vanilla blueprints use Conditional).
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (FieldInfo field in action.GetType().GetFields(flags))
                {
                    if (field.FieldType == typeof(ActionList))
                    {
                        CollectExtendedBuffs(field.GetValue(action) as ActionList, buffs, depth + 1);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the ability the way the game's own use-ability command does, without the
        /// command and its animation. In combat it then pays the action; out of combat the game
        /// charges nothing for an ability, and the many uses of a refill are kept out of the log.
        /// </summary>
        private static bool Cast(UnitEntityData unit, AbilityData ability, bool inCombat)
        {
            RuleCastSpell rule = new RuleCastSpell(ability, new TargetWrapper(unit));
            rule.Context.CommandType = ability.RuntimeActionType;
            if (!inCombat || !Main.Settings.ShowInCombatLog)
            {
                rule.Context.DisableLog = true;
                rule.DisableBattleLogSelf = true;
            }

            Rulebook.Trigger(rule);
            if (!rule.Success)
            {
                Main.Verbose($"{unit.CharacterName}: {ability.Name} was not cast (the cast rule failed).");
                return false;
            }

            if (rule.ShouldSpendResource)
            {
                ability.Spend();
            }

            if (inCombat)
            {
                UnitCommand.CommandType actionType = ability.RuntimeActionType;
                unit.SpendAction(actionType, false, 0f);
                if (actionType == UnitCommand.CommandType.Move)
                {
                    LastMoveActionTimeField?.SetValue(unit.CombatState, Game.Instance.TimeController.GameTime);
                }
            }

            Main.Verbose($"{unit.CharacterName}: used {ability.Name} automatically ({(inCombat ? "in combat" : "out of combat")}).");
            return true;
        }

        private static UnitTracking GetTracking(UnitEntityData unit)
        {
            string id = unit.UniqueId;
            if (!Tracking.TryGetValue(id, out UnitTracking tracking))
            {
                tracking = new UnitTracking();
                Tracking[id] = tracking;
            }

            return tracking;
        }

        /// <summary>Returns true, and schedules the next evaluation, if the character is due for one.</summary>
        private static bool IsCheckDue(UnitTracking tracking)
        {
            TimeSpan now = Game.Instance.TimeController.GameTime;
            if (IsWithin(now, tracking.NextCheck, CheckInterval))
            {
                return false;
            }

            tracking.NextCheck = now + CheckInterval;
            return true;
        }

        /// <summary>
        /// True while <paramref name="now"/> has not reached <paramref name="until"/>. A deadline
        /// further away than <paramref name="maxWait"/> is stale: game time went back because an
        /// earlier save was loaded.
        /// </summary>
        private static bool IsWithin(TimeSpan now, TimeSpan until, TimeSpan maxWait)
        {
            return now < until && until - now <= maxWait;
        }
    }
}
