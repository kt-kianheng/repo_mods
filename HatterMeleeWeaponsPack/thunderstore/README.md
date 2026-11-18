# Hatter Melee Weapons Pack

Melee weapons for R.E.P.O.: a physics Nunchuck, a pull-to-cut Scythe and a rift-slashing Katana.

## Nunchuck

Two 50 cm hardwood handles joined by a 40 cm steel chain. You hold one handle. The other handle is real physics: gravity pulls it, and the chain keeps it within 40 cm of your hand, so it flops, swings and whips round however you move.

Pick it up by **either end**: aim at whichever handle you want and grab. One player holds it at a time.

**The faster the free handle is moving when it hits, the harder it hits.** Damage follows a logarithmic curve: it climbs quickly at first, and the top end takes a really fast swing.

| Hit speed      | Damage                 |
| -------------- | ---------------------- |
| under 8 m/s    | doesn't count          |
| 8 m/s          | 80                     |
| 12 m/s         | 117                    |
| 16 m/s         | 143, plus a 1.5 s stun |
| 20 m/s         | 163                    |
| 24 m/s         | 180                    |
| 30 m/s or more | 200 (the cap)          |

The free handle doesn't have to connect squarely: anything within 10 cm of it as it swings past counts as hit.

Only hits that count use the nunchuck's charge (durability), the same amount as a bat hit. Bumping things doesn't.

- **Knockback:** fast hits also push enemies back.
- **Friendly fire:** the free handle hurts other players it hits, never the one holding it.
- **Price:** the same as the baseball bat.

All of these numbers can be changed in `BepInEx/config/HatterMeleeWeaponsPack.cfg` (section `Nunchuck`): `MinDamage` and `SpeedForMinDamage` (where the curve starts), `MaxDamage` and `SpeedForMaxDamage` (where it tops out), `HitReach`, `KnockbackPerSpeed`, `StunSpeed`, `StunTime`, `HitCooldown`, `FriendlyFire`, `PriceMultiplier`, plus `FreeHandleMass` and `ChainPullOnHand` for how the chain feels.

## Scythe

A long, thin, crooked scythe: a 2 m shaft and a 1 m blade, with room to hook even the big enemies. You hold the end of the shaft; it reaches out towards your crosshair, and the blade at the far end sticks out to one side, its edge facing back towards you. It stays on that side however you move it; press **E** (Interact) while holding it to flip the blade to the other side.

**Only a pull cuts.** Reach past an enemy so the blade is behind them, then pull the scythe back towards you, or back away while it's hooked. Pushing it, swinging it sideways or bumping into things does nothing.

Scrolling it further out is smooth; scrolling it back in gives it a sharp tug towards you, with a sword swing sound. A cut that lands plays a sword slice.

Every cut does **150 damage**, as long as the pull is at least 2 m/s. The scythe has **12 uses** per charge: each cut that counts uses one battery bar. Recharge it like any other item.

- Each cut drags the enemy towards you a little.
- Cutting through another player hurts them, never the one holding the scythe.
- **Price:** the same as the baseball bat.

Settings are in the `Scythe` section: `Damage`, `MinPullSpeed`, `Uses`, `HookPull`, `HitCooldown`, `FriendlyFire`, `PriceMultiplier`, plus `BalancePoint` (how easy it is to hold level) and `BladeRoll` (only if the blade ends up pointing up or down instead of to the side).

## Katana

A razor-sharp katana. Swing it like a sword (it keeps the vanilla sword's scroll-out swing), or press **E** to charge it: the sharp half of the blade glows red. Then slash through the air to tear open a rift where you drew it. The rift builds up for 2 seconds and launches a red crescent wave that flies forward, cutting through every enemy in its path and marking each with a red X.

|            |                                                                                                                                                                                              |
| ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Blade hits | 120 damage (normal hits and the scroll-out swing)                                                                                                                                            |
| Charge     | Press E; the blade's edge glows red while it's charged                                                                                                                                       |
| Rift       | Slash 0.5–1 m quickly (1.5 m/s or faster) while charged; a shorter slash fizzles                                                                                                             |
| Wave       | Launches 2 s after the rift opens: 80 damage to every enemy it passes through, pushes them back, flies 20 m/s for 2 s, growing to twice its width; a longer slash makes a wider wave (2–8 m) |
| Chains     | Up to 3 rifts per charge, each 0.1–1 s after the last; each launches its own wave from the first rift's centre                                                                               |
| Energy     | 24 bars; a charge's first rift uses 2. The blade's normal hits wear it down like the sword                                                                                                   |
| Cooldown   | 2 s after the last rift                                                                                                                                                                      |
| Price      | 2x the Scythe                                                                                                                                                                                |

- Walls, floors and doors stop the wave. Players are never hurt by it unless you turn on `WaveFriendlyFire`.
- Pressing E again, letting go or putting the katana away ends the charge.

Settings are in the `Katana` section: `PriceVsScythe`, `BladeDamage`, `Energy`, `WaveEnergy`, `WaveCooldown`, `RiftMinLength`, `RiftMaxLength`, `RiftDrawSpeed`, `RiftDuration`, `ChainLength`, `ChainWindowStart`, `ChainWindowEnd`, `WaveDamage`, `WaveTravelSpeed`, `WaveLifetime`, `WaveWidthPerRiftMetre`, `WaveGrowth`, `WaveSpanMin`, `WaveSpanMax`, `WaveThickness`, `WaveHeight`, `WavePush`, `WaveStopsAtWalls`, `WaveFriendlyFire`, `EdgeRoll`, `DebugLog`.

## Multiplayer

Every player needs the mod. Each player's game swings its own copy of the chain from the held handle, and the host decides the damage.

## Works with

TargetDummy: its training dummies show the nunchuck's hits and the katana's waves.

## Credits

Scythe cut sound: "Sword Slice 2" by Dragon Studio (Pixabay). Scythe retract sound: "Sword Swing SFX" by oxidvideos (Pixabay). Katana slash sound: "sword slash and swing" by daviddumaisaudio (Pixabay).

## Feedback

If you have any issues or feedback, contact me at the GitHub page: https://github.com/kt-kianheng/repo_mods
