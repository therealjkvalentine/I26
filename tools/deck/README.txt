I26 on the Steam Deck: Steam entry and controller layout
========================================================

add-to-steam.py
    python3 add-to-steam.py ~/i26/i26-0.11.0/i26.x86_64 [--install /path/to/Interstate76]

  Adds the Linux build to Steam as a non-Steam game (or, when an entry by that name is there, points it at this build and keeps
  its play time), writes a plain library tile (a flat colour with the name; no game art) and installs
  controller_neptune_i26.vdf so Steam offers it for the entry. Run it with Steam closed (it refuses otherwise;
  --force overrides); run it again after an update, it changes only its own entry. The old shortcuts.vdf is kept
  as shortcuts.vdf.i26bak. --dry-run writes nothing; --name, --steam-dir, --userid and --no-layout are there too.
  Python 3, standard library only. Run on a Deck by someone who has not yet: to be confirmed.

  Without --install, I26 looks for the game as usual (a game folder or a GOG installer next to it; docs/playing.md).

controller_neptune_i26.vdf
  The Steam Input layout, for the Deck's controls:
    sticks, triggers, A/B/X/Y, bumpers, D-pad, Start, View ... an Xbox pad, so I26's default pad map and the Controller screen apply
    left trackpad ............ radial menu: keys 1 to 5 (Fire Hardpoint 1 to 5)
    right trackpad ........... pointer for the menus, press = left click
    L4 ....................... Z (Emergency Brake)
    R4, L5, R5 ............... 6, 7, 8 (Use Special 1, 2, 3)
  If Game Mode does not apply it by itself: the entry's controller icon > Templates > "I26 - Steam Deck".
  The keys are I26's defaults; rebinding a key on the Controller screen means editing the layout too.
  Written from the format of other Steam Deck layouts, not exported from Steam; it passes a bracket check but has not been loaded by a
  Deck yet: to be confirmed. If Steam rejects it, make the layout by hand with the same table.
