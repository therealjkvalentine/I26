# Bass-shaker band allocation

Which band each of the shaker's sources owns, and where the game's own sounds have their low-frequency energy, so sources
that play at once do not stack in one band and each is recognisable by its own frequency. The chart and both tables come
from `tools/TactileRender --sfx INSTALL DIR` (`lfe_bands.csv` from the synth's own constants, `sfx_bands.csv` from the
install's sounds) and `tools/TactileRender/plot_bands.py DIR`.

## The game's sounds below 200 Hz

Third-octave power of each sound, played at its rate (an engine loop at its idle rate, 8269 Hz, and at 5000 rpm, 14867 Hz):

| Sound | Where its low end is |
|---|---|
| engines (vvcre2, vnvcs1) | 63 to 200 Hz, little under 40; at 5000 rpm higher still |
| engines (vvch2, vvbo1) | broad: 25 to 125 at idle, 50 to 125 revved |
| machine guns (whmgun, wlmgun) | 63 to 125 |
| cannons (wmcan, whcan) | a peak at 50 and at 40 Hz |
| rocket, missile (wrock, wmiss) | 100 to 125 |
| howitzer | 80 to 125 |
| explosions (vexplode, xcar) | broad, 31.5 to 125, the peak at 40 |
| building collapse (xbuild) | 63 to 200 |
| collision, landing (vtcoll, vland) | 40 to 50 |
| skid (tskid1) | 63 to 125 |

So the game's sound lives mostly from 40 Hz up, heaviest from 50 to 125. Under 31.5 Hz most sounds are 10 to 25 dB down from
their peak (the explosions about 2 to 6 dB). In Stereo output that sound reaches a sub or shaker too, with its bass boosted
under the crossover; in 5.1 Godot sends it to the LFE at full level.

## The allocation

Under the sounds' energy where it can be, every event its own frequency, one or two parts each (fewer, cleaner):

| Source | Kind | Before (round 2) | Now | Why |
|---|---|---|---|---|
| explosion's roll | event | 25 Hz near, 18 far | **21 near, 17 far** | under everything; the explosion's sound peaks at 40 |
| cabin ring (impact over 20) | event, long | 26 | **23** | under the hits, so the ring reads as the cabin, not the hit going on |
| hits | event | explosive 25, ballistic 32, impact 30, each with a 55 Hz part and an 80 Hz click | **explosive 25, impact 27, ballistic 29, the tone alone** (held 60 ms) | one tone a channel, under the off-road beds' centres; the 55 and 80 Hz parts sat in the sounds' own band |
| off-road beds (sand, rough, dirt) | continuous noise | 30 / 32 / 34 Hz, Q 0.8 to 0.9 | unchanged | the playtest's liked feel; low, broad |
| off-road lumps | event, frequent | 24 to 26 Hz | **at their bed's centre (30 / 32 / 34)** | part of the bed's texture rather than a band of their own, and out of the hits' 25 to 29 |
| engine | continuous tone | 25 to 45 Hz by rpm | **36 to 50** | over the off-road beds' centres and the hits, under most engines' own sound |
| wheel knock | event | 28 Hz body, 52 Hz thud, 82 Hz click | **40 Hz body (held 140 ms, gone by 350), 80 Hz click (its octave, 35 ms)** | its own frequency between the ground and the asphalt hum; the click as the body's octave fuses with it; the thud dropped |
| shots | event | 30 to 60 Hz by class | **36 to 50**: mortar 36, cannon 38, rocket 42, dropper 44, machine gun 48, flame 50 | over the hits and the ground; each class its own; the machine gun under its 63 to 125 Hz sound |
| planks bed and lumps | continuous, events | 46 Hz bed, 40 Hz lumps | lumps **46** | the knock owns 40, and planks are where a car crosses onto a bridge |
| dirt road bed, grit, lumps | continuous, events | 66 Hz bed, 76 grit, 30 lumps | lumps **46** | the gravel's pitch is the playtest's; its lumps out of the hits' and the ground's band |
| asphalt bed | continuous | 56 Hz, narrow | unchanged | quiet; 4 to 7 dB under the dirt road |
| filled cracks (dev only, `lfe_cracks`) | event | 38 Hz | **46** | out of the engine's idle |
| rumble strip (dev only, `lfe_strip`) | continuous tone | speed / 30.5 cm folded under 60 | unchanged | only along a road's edge |

What still overlaps, and why it is left: the off-road beds are broad noise (sand's -3 dB edges 17 to 54 Hz) under every
event, but they are 10 to 20 dB under the events' peaks (the road trim of the headroom plan), and the events are tones at
their own frequencies inside them; the engine and the shots share 36 to 50, the one a steady tone that follows the rpm, the
others short knocks; the dirt road and asphalt beds and the grit sit in the sounds' 50 to 100 band, since their pitch is what
the playtest asked for (the gravel road above the ground).

The chart (`bands.png` from `plot_bands.py`, not kept in the repository) draws the first table's bands over the second's sounds.
