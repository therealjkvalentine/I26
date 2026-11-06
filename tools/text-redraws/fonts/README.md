# Fonts of the text redraws

The faces `tools/text-redraws.py` letters an HD texture pack's small lettering with, each matched against the original's
painted letters on a sheet of free candidates (the same cap height, the original and today's pack beside each). They are
used only by the tool, on your own machine; nothing here is loaded by the game, and a pack made with them names them in its
`pack.json` and `ATTRIBUTION.txt`.

**Their licences are not the project's.** Each is under the SIL Open Font License 1.1, its `OFL.txt` beside it, except
Permanent Marker, under the Apache License 2.0, its `LICENSE.txt` beside it. The font files are unchanged copies from
https://github.com/google/fonts (the font's folder; D-DIN Condensed Bold from Datto's release, https://www.datto.com/fonts/d-din).

| Face | Folder | Copyright | Used for |
| --- | --- | --- | --- |
| Anton | `Anton/` | The Anton Project Authors (Vernon Adams) | the licence plates (the Piranha's CHAMP, dented too, and the AI cars' tails) |
| Bitter (variable, set to Bold) | `Bitter/` | The Bitter Project Authors (Huerta Tipografica) | the Palomino's badge (PALOMINO: the Mustang's spaced serif capitals) |
| Comfortaa (variable, set to Bold) | `Comfortaa/` | The Comfortaa Project Authors (Johan Aakerlund) | the Piranha's badge ('rahna: the 'Cuda's upright rounded lowercase) |
| Cinzel (variable, set to Bold) | `Cinzel/` | The Cinzel Project Authors (Natanael Gama) | the Cavera's badge (CAVERA) |
| D-DIN Condensed Bold | `DDINCondensed/` | Datto Inc., Reserved Font Name "D-DIN Condensed" | the police cars' doors (VALDEZ COUNTY SHERIFF, NEW MEXICO STATE POLICE, TEXAS HIGHWAY TROOPER) |
| Permanent Marker | `PermanentMarker/` | Font Diner (Apache 2.0) | the mission maps' hand lettering (a trial: T01, T02) |
| Archivo Narrow (variable, set to Bold) | `ArchivoNarrow/` | The Archivo Narrow Project Authors (Omnibus-Type) | the speedometer's and tachometer's figures (the 3D dashes `zdash101/201/301/501`, `zspeedo6`, `ztach6`) |
| League Gothic (variable) | `LeagueGothic/` | The League Gothic Project Authors (The League of Moveable Type) | the damage panel's maker's plate (`zsy_`: MR.DAMAGE) |
| Lobster | `Lobster/` | The Lobster Project Authors (Pablo Impallari), Reserved Font Name "Lobster" | (no longer used for 'rahna; kept for reference) |
| Mr Dafoe | `MrDafoe/` | Alejandro Paul (Sudtipos) | the maker's signature on the damage panel's plate (`zsy_`: by Ramone) |
| Arimo (variable, set to Bold) | `Arimo/` | The Arimo Project Authors (Steve Matteson) | Control Configuration's page in the HD menu art (`shellbg16`: the title, CONTROLS, and the buttons' words; the original's heavy grotesque) |
| Russo One | `RussoOne/` | Jovanny Lemonad, Reserved Font Name "Russo" | the damage display's labels (`zsye`), the DOVER and PALOMINO badges |

`fonts.json` names each face's file, author, licence and source for the tool.
