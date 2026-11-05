# Fonts of the text redraws

The faces `tools/text-redraws.py` letters an HD texture pack's small lettering with, each matched against the original's
painted letters on a sheet of free candidates (the same cap height, the original and today's pack beside each). They are
used only by the tool, on your own machine; nothing here is loaded by the game, and a pack made with them names them in its
`pack.json` and `ATTRIBUTION.txt`.

**Their licences are not the project's.** Each is under the SIL Open Font License 1.1, its `OFL.txt` (from
https://github.com/google/fonts, the font's folder) beside it; the font files are unchanged copies from the same place.

| Face | Folder | Copyright | Used for |
| --- | --- | --- | --- |
| Anton | `Anton/` | The Anton Project Authors (Vernon Adams) | the Piranha's licence plate (`pp11bkb1`: CHAMP, TEXAS) |
| Archivo Narrow (variable, set to Bold) | `ArchivoNarrow/` | The Archivo Narrow Project Authors (Omnibus-Type) | the speedometer's and tachometer's figures (`zdash101`, `zspeedo6`, `ztach6`) |
| League Gothic (variable) | `LeagueGothic/` | The League Gothic Project Authors (The League of Moveable Type) | the damage panel's maker's plate (`zsy_`: MR.DAMAGE) |
| Lobster | `Lobster/` | The Lobster Project Authors (Pablo Impallari), Reserved Font Name "Lobster" | the Piranha's chrome badge (`pp11bkb1`: 'rahna) |
| Mr Dafoe | `MrDafoe/` | Alejandro Paul (Sudtipos) | the maker's signature on the damage panel's plate (`zsy_`: by Ramone) |
| Russo One | `RussoOne/` | Jovanny Lemonad, Reserved Font Name "Russo" | the damage display's labels (`zsye`: ENGINE, SUSP., BRAKES, WHEEL) |

`fonts.json` names each face's file, author, licence and source for the tool.
