# Auto Cackle & Chant

A mod for **Pathfinder: Wrath of the Righteous** that uses the Witch's **Cackle** and the
Shaman's **Chant** for you, so you no longer click the hex every round to keep
Fortune, Protective Luck, Evil Eye, Misfortune or Fury running.

The mod only removes the click. Cackle and Chant still extend the same hexes by one round
per use and still reach only targets within 30 feet in line of sight. In combat they still
cost the character's move action; out of combat the game charges no action for them, with
or without the mod.

## What it does

### In combat

A character uses Cackle/Chant automatically when all of these are true:

- the character is under your control and can act;
- the move action is available;
- one of the character's own hexes that Cackle/Chant can extend is on a target in range and
  has 2 rounds left or fewer (the number is a setting).

When no hex needs extending, nothing happens and the move action stays free.

By default the character receives the same command as when you click the ability, so it
plays the casting animation and the game spends the action and writes the combat log line
itself. A setting switches the animation off; the ability is then resolved at once.

#### Real-time combat

The command is placed at the head of the character's command queue. The game runs it as
soon as the current action is over (an attack round, a spell, a walk), so the automatic
use never interrupts what the character is doing, and the character then goes back to
attacking the same target. Because the use can wait a few seconds for that moment, in
real time a hex is extended from 2 rounds left even if the setting is lower. As with any
move-action ability, a full attack is not possible in the 3 seconds after it.

A character you ordered to stop is not given a command, which would cancel the order;
for it the ability is resolved at once, without animation.

#### Turn-based combat

The ability is used **when you end the turn**, with the move action you left unused:
the character plays the animation and then the turn ends. This way a hex you cast during
the turn is extended in the same turn, and the move action remains yours to spend on
something else. If you spend it (you move, or you make a full attack), the mod does
nothing that turn.

### Out of combat

Out of combat the game lets you use Cackle/Chant as often as you like at no cost, which
is how players build up minutes of Fortune between fights. The mod does the same: while
the party explores, a character keeps using the ability (about twice per second) until
the hexes in range reach 5 minutes (a setting, 1 to 10). It then waits until a hex has
dropped below 4 minutes (a second setting) before topping it up again, so the hexes stay
between the two values. A refill takes dozens of uses, so out of combat the ability is
always resolved without the animation.

This runs only during normal exploration: not in dialogues, cutscenes, while paused or on
the world map. Each use is written to the combat log, one line per use, so a refill is
dozens of lines; a setting hides them. Because nothing is spent,
this option amounts to keeping the hexes up permanently for targets that stay within
30 feet; turn it off in the settings if you do not want that.

## Settings

Open the Unity Mod Manager window (Ctrl+F10) and expand the mod.

| Setting | Default | Effect |
| --- | --- | --- |
| Real-time combat | on | Automatic use in real-time combat. |
| Turn-based combat | on | Automatic use at the end of the turn. |
| In combat: extend a hex when it has N rounds left or fewer | 2 | Lower values use the ability later; higher values keep a larger reserve of duration. |
| In combat: play the casting animation | on | Off resolves the ability at once, without animation and without waiting for the current action. |
| In combat: show the automatic use in the combat log | on | Turn off to hide the "uses Cackle" line. |
| Out of combat | on | Automatic use while exploring. |
| Out of combat: bring hexes up to N minutes | 5 | The duration the mod builds up between fights (1 to 10). |
| Out of combat: top up when a hex drops below N minutes | 4 | Hexes stay between this value and the one above (0.5 to 10, in half minutes). At or above the duration to build up, a hex is topped up every round. |
| Out of combat: show the automatic uses in the combat log | on | Turn off to hide the lines of a refill (one per use, dozens per refill). |
| Write every automatic use to the mod log | off | For troubleshooting. |
| Characters | all on | Switch the automation off for single party members. |

## Installation

Requires [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) 0.23.0 or later,
set up for Pathfinder: Wrath of the Righteous. Built and checked against game version 2.7.0.

1. Download `AutoCackle-<version>.zip` from
   [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1265).
2. Install it with Unity Mod Manager (Mods tab, drop the zip), or extract it so that the
   files are in `<game folder>\Mods\AutoCackle\`.
3. Start the game.

To uninstall, delete the `AutoCackle` folder. The mod adds nothing to save files, so saves
keep working without it.

## Compatibility

- The mod adds no blueprints and changes none. It patches four game methods:
  `UnitCombatCooldownsController.TickOnUnit` (postfix), `UnitTicksController.TickOnUnit`
  (postfix), `TurnController.ForceToEnd` (prefix) and `TurnController.Tick` (postfix).
- The list of hexes to watch is read from the Cackle and Chant blueprints at run time, so
  hexes that other mods add to those abilities are handled too.
- Other mods that give a character the vanilla Cackle or Chant ability work as well
  (the abilities are recognised by blueprint id).

## Building from source

Requirements: the .NET SDK (the project is built with version 9), the game, and Unity Mod
Manager installed into the game (the build references the game's assemblies and the mod
manager's `0Harmony.dll`).

```powershell
.\build.ps1            # builds and creates dist\AutoCackle-<version>.zip
.\build.ps1 -Install   # also copies the mod into the game's Mods folder
```

If the game is not in the default Steam folder, copy `GamePath.props.example` to
`GamePath.props` and set `WrathPath`.

The version number is taken from `Info.json`.

## How it works

- `src/CackleLogic.cs` decides whether to use the ability. In combat it creates the
  command the action bar creates (`UnitUseAbility.CreateCastCommand`); without the
  animation, and out of combat, it resolves the ability through the game's cast rule
  (`RuleCastSpell`) and, in combat, spends the action with the game's `SpendAction`.
- `src/Patches.cs` holds the Harmony patches that call it: once per frame per unit in
  real-time combat and out of combat (both throttled to two evaluations per second per
  character), and when the turn is ended in turn-based combat, where the end of the turn
  is held back until the command is over.
- `src/Settings.cs` and `src/SettingsUI.cs` hold the settings and their panel.

## License

MIT. See [LICENSE](LICENSE).
