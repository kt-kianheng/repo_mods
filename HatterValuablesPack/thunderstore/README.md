# LATEST CHANGES 1.0.2

- Fixed issue where both compasses don't break properly when damaged too much.
- Fixed position of value for compasses

# Hatter Valuables Pack

New valuables for R.E.P.O.: an A-Bomb, an Enemy Compass and an Extraction Compass.

## A-Bomb

A 1940s-style atomic bomb on its cradle. It's a wide valuable worth **$40,000–60,000**, heavier than the piano.

- **Rare:** about 1 level in 10 has one. It doesn't count towards the level's valuable budget, so the level's other valuables are all still there.
- **Tough:** bumps and knocks never cost it any money.
- **Unstable:** 3 heavy hits (dropping it hard), or one long fall, arm it. Once armed, an alarm bell rings and smoke starts leaking out. **10 seconds later it explodes.** Nothing stops it.
- **The blast:** a blinding flash. Every player, enemy and valuable in the bomb's room and the rooms next to it is wiped out, the truck included. Players two rooms away see the flash from the bomb's direction.

Everything above can be changed in `BepInEx/config/HatterValuablesPack.cfg` (section `[A-Bomb]`): spawn chance, value, size, weight, how hard a hit must be, hits to arm, fuse time, kill and flash range in rooms.

## Enemy Compass and Extraction Compass

Two brass-and-glass compasses, medium valuables (about 43 cm across) worth **$2,500–5,000** each: a clear glass sphere in an hourglass-shaped frame, with a needle floating inside that points in 3D, up and down too. The red end of the needle points at the target.

- **Enemy Compass:** a red eye on top (or a little Robe, borrowed from the game). The needle points to the nearest enemy.
- **Extraction Compass:** a green dollar sign on top. The needle points to the nearest extraction point that isn't finished yet.
- The needle swings and settles like a real one, however you hold the compass. Within 25 m of its target it starts to shake, harder and faster the closer you get: at 2 m it's shaking wildly.
- Nothing to point to (no enemies left, or every extraction point done)? The needle wanders aimlessly.
- As fragile as the power crystal, so handle them gently. They spawn like any other medium valuable.

Settings: `[Enemy Compass]` and `[Extraction Compass]` (on/off, value), `[Compasses]` (scale, the emblem's size, the little Robe's facing, and `SpawnInTruckForTesting` to find one of each in the truck every level).

## Sound credits

- Alarm: "Alarm Clock" by freesound_community (Pixabay), one ring cut out and repeated.
- Explosion: "Bomb Explosion" by wings_of_freedom (Pixabay).

## Works with

- BepInExPack 5.4.2305
- REPOLib 4.3.0

Every player needs the mod.
