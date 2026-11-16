# Changelog

## Unreleased

- New weapon: the Orb Cannon (2x the shotgun). Loads soul orbs and fires them: small = a shockwave cone, medium = a burst of 3 orbs (80 each), large = a big orb that hits like a rocket (300 + its blast). 3 s cooldown
- Ember Wand (and the Ember Staff's fireballs)
  - Burning: everything in the blast burns for 5 s, 30 per second to enemies, 10 to players (all configurable)
  - Leaves a 1.2 m patch of fire on the floor for 10 s; standing in it keeps you burning
- Ember Staff
  - The inferno can be cast on walls too: the pillar shoots out sideways from the wall
  - The inferno damages valuables in it (a light break every second, configurable)
  - When the inferno dies down it leaves fire on the floor for 10 s (one patch, or a trail under a wall inferno)
- Burning players see flames round the edges of their screen
- Ember Wand and Staff fireballs damage valuables in their blast (medium break by default, configurable)
- The fire patches no longer have a glowing ring round the edge
- Lightning Wand and Lightning Staff
  - With no enemy to strike, the straight bolt to the crosshair hits the first teammate in its way, for an enemy's damage (uses the battery bar)
- Rocket Launcher: the blast now damages players (teammates and you) the same as enemies
- Evolution Crystal: it's now removed from the world when used (it used to stay behind, dead)
- Fire and ice cancel each other out, with steam: icicles and the ice wall put out burning (enemies, teammates, dummies); any fire thaws frozen or slowed enemies (shatters the ice, ends the freeze, stun and slow) and sets them alight; the ice wall puts out patches of fire under it. Right after one takes hold, the other can't undo it for 1 s (`[Fire and Ice] GraceSeconds`; the ice wall ignores it)

## 1.0.0

Added 6 weapons:

- Rocket Launcher
  - 6 rockets per charge
  - 300 contact damage, 300(<2m)/150(<4m) splash damage,
  - 60 knockback, same as pulse grenade
  - 12s reload
  - 40 recoil knockback on user
  - Splash damage doesn't affect friendlies (can try to rocket jump with this!)
- Plasma Rifle
  - 12 shots per charge
  - 250 damage per bolt (teammates included)
  - 60 m/s plasma projectiles
- Ember Wand
  - Shoots a slow moving fireball
  - 2s charge-up,
  - 12 casts per charge
  - 2s reload
  - 120 direct + 30 splash (<0.8m), damages everyone
  - Costs the same as the gun.
- Frost Wand
  - A volley of 8 icicles that form one by one, 0.5s apart, each flying at the crosshair 1s after it forms
  - 40 damage each; every enemy hit is frozen in place for 2s, encased in ice
  - 12 casts per charge
  - 2s reload after the last icicle flies
  - Costs the same as the gun.
- Healing Wand
  - 2 casts per charge
  - 5s healing beam (20hp/s)
  - When charged by pointing to the sky, a 5s lime healing cylinder around you with green beams circling you (10/s to everyone inside, 2 m radius).
  - 5s reload
  - Costs the same as the gun.
- Lightning Wand
  - 12 casts per charge
  - 2s channel with a swelling yellow glow at the tip; targets are picked where you aim when it fires
  - A yellow lightning bolt with 4 charges that chains to up to 4 enemies, 0.2s apart, sharing the charges (4 / 2+2 / 2+1+1 / 1 each); it only jumps along a clear line of sight
  - Each enemy struck is electrocuted and stunned for 4s, then takes 50 damage per charge (at most 2 charges counted: 100)
  - 3s reload
  - Costs the same as the gun.
- Ember Staff (an upgrade of the Ember Wand; not in the shop)
  - Volley: 3 Ember Wand fireballs that grow at the head over 2s, then fly 0.2s apart
  - Inferno: point at the sky to ready it, aim at the floor (a ring shows where), cast: an 8s pillar of fire, 2 m in radius, to the ceiling, 100/s to enemies, 50/s to players
  - 24 battery bars per charge (volley 1, inferno 3), 2s reload
- Frost Staff (an upgrade of the Frost Wand; not in the shop)
  - Volley: 24 Frost Wand icicles forming 3 at a time, 0.25s apart, each set flying at the crosshair 1s after it forms
  - Ice wall: point at the sky to ready it, aim at the floor (a rectangle shows where), cast: a 5 m x 1 m wall of 8 ice blocks, about 3 m tall (+-30 cm each), rising from left to right, that blocks everything for 10s; enemies it rises on are encased in ice and frozen until it breaks, then slowed to half speed for 5s
  - 24 battery bars per charge (volley 1, wall 3), 2s reload after the volley
- Lightning Staff (an upgrade of the Lightning Wand; not in the shop)
  - 2s channel, then 3 lightning strikes 0.3s apart that home in on enemies on your screen (a different one first each, while there are enough) and chain like the wand's bolt
  - Damage lands on impact: 50 per charge, up to 4 charges per enemy per strike; the stun still applies
  - Orb: charge it by pointing at the sky, cast: a big (1 m radius), slow (1.5 m/s) orb of lightning that bounces off walls and strikes the nearest enemy every 1.5s for 30s
  - 24 battery bars per charge (strikes 1, orb 3), 3s reload
- Staves' charged spells: hold the staff up at the sky for 4s to charge (you chant an incantation; the charge flashes when ready, and lowering it sooner cancels), and shout the spell's name as you cast it
- All weapon stats are configurable in HatterWeaponsPack.cfg.
- Built against R.E.P.O. v0.5.0 and REPOLib 4.3.0.

Works with the TargetDummy mod (optional):

- Direct hits from every weapon register on its dummies
