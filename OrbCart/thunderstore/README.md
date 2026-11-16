# Latest update

Increased the price of the OrbCart to 'balance' it out. Apparently it's slightly broken.
Hosts used to have the hatch rendered at the base of the cart. It's fixed to render at the top instead.

# Orb C.A.R.T.

A Pocket C.A.R.T.-sized cart built for enemy orbs. Credits to Koalas_Weapons_and_Valuables for the mod idea. I wanted to include the orb cart but the mod came with other stuff that I didn't really want, hence the birth of this one.

This mod is practically vibe-coded, so there might be possible bugs that went untested. Feel free to let me know and I'll update it.

## Configuration

`BepInEx/config/OrbCart.cfg` (also editable in-game with REPOConfig).

| Section | Setting   | Default  | What it does                                                                                                                                                                |
| ------- | --------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| General | Capacity  | 100000   | Total orb value ($) the cart holds. The screen shows stored value as a % of this. The host's value is used and synced to everyone. Applies to carts spawned after a change. |
| Visuals | TintColor | B88CFFFF | Body tint as a hex colour (RRGGBB or RRGGBBAA). Leave empty to keep the Pocket C.A.R.T.'s own colours. Needs a restart.                                                     |

## Multiplayer

Everyone needs the mod. The host decides what fits and the capacity is synced from the host.

## How it works

The cart is assembled at startup from the game's own Pocket C.A.R.T. and Hauler parts, then registered with REPOLib, so there is no asset bundle and it always matches the current game models.
