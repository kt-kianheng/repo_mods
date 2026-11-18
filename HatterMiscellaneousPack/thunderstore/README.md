# Latest changes 1.0.1

- When friends use it, they no longer just hold the monocle in front of them, the monocle will move closer to their face, appearing as though as they are actually using it.

# Hatter Miscellaneous Pack

Consumables and other odds and ends for R.E.P.O. Everything is sold in the shop.

## Pocket Enemy Compass and Pocket Extraction Compass

Little brass-and-glass compasses (a third the size of the compass valuables in Hatter Valuables Pack). The Pocket Enemy Compass costs $5K and the Pocket Extraction Compass $2K (fixed prices that don't rise from level to level). Their needles are stuck, so on their own they're useless.

**Hold one and press [E] to break it.** A small 3D arrowhead then appears at the top of your screen, under the haul, and points to its target from where you're looking, up and down included:

- **Pocket Enemy Compass** (red eye or a little Robe on top): a red arrow to the nearest enemy.
- **Pocket Extraction Compass** (green dollar sign on top): a green arrow to the nearest extraction point that isn't finished yet.

- The arrow lasts **5 minutes**, or until there's nothing left to point to (no enemies in the level, or every extraction point done). A small timer under the arrow counts down the time left, and the arrow blinks during its last 10 seconds.
- Only the player who broke the compass sees the arrow. With one of each running, the two arrows are stacked, extraction on top.
- It only works in a level: in the shop, or with nothing to point to, the compass pops back without breaking.

Settings, in `BepInEx/config/HatterMiscellaneousPack.cfg`, section `[Pocket Compasses]`: on/off and price for each, size, how long the arrow lasts, and the arrow's size and position.

## All-Seeing Monocle

A gold monocle with a little chain, in the style of the monocle cosmetic, sold for $8K. It handles like a drone: same size, weight and feel in your hand.

**Hold it and press [E] to look through it.** The monocle flies up to your eye until it fills your screen, and every valuable within 10 m is highlighted through the walls with the game's own yellow valuable highlight. Press [E] again to lower it.

- **Charges:** 10 per full battery, recharged at the charging station like any other battery item. Each charge gives 10 seconds of sight. A charge is used the moment you look, and again every 10 seconds after that, so stopping early still costs the whole charge.
- It switches off when the battery runs out, when you let go of it, or when you put it in your inventory.
- Only the player looking through it sees the valuables. It only works in a level.

Settings: `[All-Seeing Monocle]` (on/off, price, charges per battery, seconds per charge, size compared with a drone, zoom speed, sight range, the lens's size on screen, and which of the game's highlights to use).

## Feedback

If you have any issues or feedback, contact me at the GitHub page: https://github.com/kt-kianheng/repo_mods
