# Latest changes 1.0.0

- The Orb C.A.R.T. moves here from the OrbCart mod, which is now deprecated.
- The Orb C.A.R.T.'s handle screen now shows the total value of its orbs, and a new screen on the far end shows how full it is.
- New cart: the C.A.R.T. Plus, a wider and longer C.A.R.T. that can shrink itself and its contents to fit through tight doors.

# Hatter Carts Pack

Carts for R.E.P.O. Everything is sold in the shop. More carts are on the way.

This mod is practically vibe-coded, so there might be possible bugs that went untested. Feel free to let me know and I'll update it.

## Coming from the OrbCart mod?

The Orb C.A.R.T. is the OrbCart mod's cart, now part of this pack (only its screens changed). Uninstall OrbCart: if both are installed, this pack leaves its own Orb C.A.R.T. out so there aren't two. Orb C.A.R.T.s you already own carry over. The settings now live in `BepInEx/config/HatterCartsPack.cfg`, so set them again if you had changed them.

## Orb C.A.R.T.

A Pocket C.A.R.T.-sized cart built for enemy orbs, with a Hauler-style hatch on top that only accepts orbs. The screen by the handle shows the total value of the orbs inside, and a screen on the far end shows how full it is. Costs 3 times the Pocket C.A.R.T. Credits to Koalas_Weapons_and_Valuables for the idea.

Settings, in `BepInEx/config/HatterCartsPack.cfg` (also editable in-game with REPOConfig), section `[Orb C.A.R.T.]`:

| Setting   | Default  | What it does                                                                                                                                                                        |
| --------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Enabled   | true     | Adds the Orb C.A.R.T. to the shop. Needs a restart.                                                                                                                                 |
| Capacity  | 100000   | Total orb value ($) the cart holds. The far-end screen shows stored value as a % of this. The host's value is used and synced to everyone. Applies to carts spawned after a change. |
| TintColor | B88CFFFF | Body tint as a hex colour (RRGGBB or RRGGBBAA). Leave empty to keep the Pocket C.A.R.T.'s own colours. Needs a restart.                                                             |

## C.A.R.T. Plus

A C.A.R.T. 40% wider and longer and 10% taller than the normal one (the walls are just as thick), for 1.5 times the C.A.R.T.'s price. At the far end there's a shorter second handle with a shrink icon (four arrows pointing inwards): the stasis button, with a bar under it where the other end has its screen.

**Hold the stasis button** to shrink the cart and everything in it back to the size of a normal C.A.R.T., so it fits through tight doors:

- The cart stays small for as long as you hold the button, and you can still push and pull it.
- Everything in the basket shrinks with it: valuables, items, death heads and players, standing or tumbled (a shrunk player's camera sits lower too). Anything someone is holding stays as it is.
- Let go and the cart grows straight back.

Settings, section `[C.A.R.T. Plus]`: `Enabled` (adds it to the shop; needs a restart).

## Multiplayer

Everyone needs the mod. The host decides what fits in a cart and its capacity is synced from the host.

## How it works

Each cart is assembled at startup from the game's own carts (the Orb C.A.R.T. from the Pocket C.A.R.T. and the Hauler), then registered with REPOLib, so there is no asset bundle and the carts always match the current game models.

## Feedback

If you have any issues or feedback, contact me at the GitHub page: https://github.com/kt-kianheng/repo_mods
