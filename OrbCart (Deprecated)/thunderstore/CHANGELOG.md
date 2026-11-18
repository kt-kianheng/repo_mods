# Changelog

## 1.0.7

- Price raised to 3x the Pocket C.A.R.T. (was 1.5x).
- Fixed the hatch sitting at the bottom of the cart for the host in multiplayer. The hatch box is now fixed to the cart: anything from the Hauler that could move it on its own is removed, and it is held in place every frame.

## 1.0.6

- Config file renamed to `BepInEx/config/OrbCart.cfg` (plugin GUID is now `OrbCart`).

## 1.0.5

- Config trimmed to two settings: `Capacity` and `TintColor` (leave the colour empty to keep the Pocket C.A.R.T.'s colours). Hatch layout, price and decals are now fixed; old settings are removed from the config file automatically.
- Inventory icon no longer has the orb badge; the hatch on top marks it as the Orb C.A.R.T.

## 1.0.4

- The hatch is now grabbable: grabbing it grabs the whole cart, like the cart's walls.
- Default `HatchOffset` is now -0.15, which puts the hatch flush with the top of the cart.

## 1.0.3

- The hatch now sits flush with the top of the cart walls instead of the top of the handle bar.

## 1.0.2

- Fixed the hatch sitting on top of the cart: its top is now flush with the top of the cart walls (the rim is measured from the cart body, not the taller in-cart volume).

## 1.0.1

- The hatch is now the cart's top face: it is fitted to the basket at load time, with a solid lid that orbs rest on until the hatch opens and pulls them in.
- New inventory icon: the Pocket C.A.R.T. icon with the closed hatch on top and an orb badge in the corner (drawn from the in-game orb's materials).
- Layout settings replaced: `HatchFill`, `HatchOffset`, `HatchStretch` (old `BoxScale` / `BoxHeight` are no longer used).

## 1.0.0

- Initial release: Orb C.A.R.T. with orb-only hatch, fill-percentage screen and configurable capacity (default $100,000).
- Built against R.E.P.O. v0.5.0 and REPOLib 4.3.0.
