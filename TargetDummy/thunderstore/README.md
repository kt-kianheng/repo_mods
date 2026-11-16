# Target Dummy

Two training dummies for the shop, one on each side of the cosmetics gacha machine.

## Target Dummy (the Robe)

A Robe strung up on a wooden stand.

- **Can't be killed or moved.** It has no health to lose, can't be grabbed, and nothing can push it over. It rocks a little when hit, but only its looks move.
- **Shows every hit.** A number pops up for the damage it took: what a real Robe would lose, after its own damage resistance. A smaller line shows the knockback, torque, stun or freeze behind the hit. For a thrown object, it shows the impact force and speed instead (like the game, weapons and other items bumping into it don't count).
- **Catches every kind of damage**: guns, melee, explosions, grenades, staffs, thrown objects, and other mods' weapons that use the game's hurt zones
- **Running total.** A total over its head adds up a combo and resets after 3 seconds without a hit.

## Ally Dummy (the player droid)

A player droid on the same stand, treated as a **teammate**. It's for testing healing.

- **1000 health that never drops below 1.** A health bar over its head shows its current health.
- **Friendly fire hurts it** exactly like a player: the player damage of guns, melee, explosions and grenades. Red numbers show the damage, along with knockback, tumble and stun. If a hit would have downed a real player, it says so.
- **Healing heals it.** Green numbers show what actually went in, and say when it's already at full health.
- **Other mods see it as a teammate, unchanged.** It joins as a pseudo-player named "Ally Dummy". A mod that finds teammates with `SemiFunc.PlayerGetList()` (or by network ID) and heals or hurts them through their `PlayerHealth` treats it like any other player. For example, HatterWeaponsPack's Healing Wand beam and healing orb heal it, and full health means a free beam, just as for a real player. The game's own code never sees the pseudo-player, so trucks, spectating, the map and enemies are unaffected.
- **Limits:** mods that read `GameDirector.instance.PlayerList` directly won't see it, and mods that touch other parts of a player (grabbing, tumbling, camera, voice) find those empty. The base game's own heals (health packs and the heal orb heal only their user; the heal drone and health grab need a real player) don't reach it.
- `StartHealth` lets it start damaged. `ResetAfterSeconds` refills it to `StartHealth` after a quiet spell.
- Mods can also use `TargetDummy.AllyApi` directly (look it up by name with reflection).

## Both dummies

- **Multiplayer.** The host decides every hit and heal, and every player sees the same numbers and health. Every player needs the mod.

## Settings (`BepInEx/config/TargetDummy.cfg`)

| Section    | Setting                     | Default     | What it does                                                                       |
| ---------- | --------------------------- | ----------- | ---------------------------------------------------------------------------------- |
| Placement  | SideGap / ForwardOffset     | 1.3 / 0.3   | Ideal spot: gap from the machine's side, and how far forward                       |
| Placement  | PreferRightSide             | true        | Target Dummy on the right, Ally Dummy on the left (false swaps them)               |
| Placement  | AutoPlace                   | true        | Search nearby for the clear spot nearest the ideal one (floor, no walls, headroom) |
| Model      | TotalHeight / RobeHeight    | 2.6 / 1.9   | Size in metres                                                                     |
| Model      | CrossbarHeight              | 0.72        | Arm bar height as a fraction of the Robe                                           |
| Model      | RobeYaw                     | 0           | Turn the Robe if it faces the wrong way                                            |
| Model      | HideRobeParts               | (empty)     | Hide stray Robe pieces by name (names are listed in LogOutput.log)                 |
| Numbers    | ApplyRobeResistance         | true        | Show damage after the Robe's resistance                                            |
| Numbers    | ShowForce / ShowTotal       | true        | Extra lines                                                                        |
| Numbers    | NumberSize / NumberLifetime | 1 / 4.4     | Look of the numbers                                                                |
| Numbers    | Wobble                      | 1           | How much it rocks when hit                                                         |
| Model      | DebugLog                    | true        | Log every hit, heal and placement decision                                         |
| Ally Dummy | Enabled                     | true        | Spawn the Ally Dummy                                                               |
| Ally Dummy | MaxHealth / StartHealth     | 1000 / 1000 | Its health pool (never below 1)                                                    |
| Ally Dummy | ResetAfterSeconds           | 0           | Refill to StartHealth after this long untouched (0 = never)                        |
| Ally Dummy | TotalHeight / DroidHeight   | 2.5 / 1.6   | Size in metres                                                                     |
| Ally Dummy | CrossbarHeight / DroidYaw   | 0.62 / 0    | Arm bar height; turn the droid if it faces the wrong way                           |
| Ally Dummy | HideDroidParts              | (empty)     | Hide stray droid pieces by name                                                    |
| Ally Dummy | HealthBar                   | true        | Show the health bar                                                                |

## Credits

Stand modelled in Blender. The Robe and the droid are the game's own models, loaded at runtime; they aren't included in this mod.
