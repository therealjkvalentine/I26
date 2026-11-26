I26 HD Packs for Interstate '76  -  release {version}
====================================================

Optional texture packs for I26 (https://github.com/vahokif/I26), the open-source engine that plays Interstate '76 from your
own install. The 8x pack needs the 4x pack (on its own it does nothing).

{archives}

(Each archive also holds README.txt, ATTRIBUTION.txt, pack.json and FILES.sha256. The archives' SHA-256 hashes are in
SHA256SUMS-{version} beside them.)

WHAT CHANGED
{notes}

INSTALLING
  Extract each archive into I26's user folder, so that its folder (tex-world-4x, tex-world-8x) sits there:
    Windows  %APPDATA%\I26\
    Linux    ~/.local/share/I26/
    macOS    ~/Library/Application Support/I26/
  Start the game in the modern look; Options > Graphic Detail > Advanced shows the World Textures and Car & Dash Textures
  rows once a pack is found. F12 switches to the 1997 look, which always draws the originals.

HOW THESE WERE MADE
  Every step is a command or a spec in the I26 fork's repository: tools/hd-pack.py builds each pack from its recipe in
  tools/hd-recipes/ and your own install, and pack.json names the recipe, its commit, the models (pinned by SHA-256), the
  tools' and libraries' versions. docs/hd-textures.md says how to make or change them yourself.

CREDITS AND LICENCES
  ATTRIBUTION.txt in each archive names every model and font used. Where a model is under a NonCommercial licence the pack
  is offered non-commercially only: free, never sold, never behind a paywall, subscription or reward scheme.
  Source art: Interstate '76 (Activision, 1997). Its art belongs to its rights holders; these packs are derived from it and
  require an owned copy of the game.
