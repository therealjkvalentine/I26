# Road surfaces on a bass shaker

Background for the bass-shaker signal's surfaces (`src/I76.Formats/Input/TactileSynth.cs`, `SurfaceProfile`): what driving
on pavement, gravel and bumpy dirt does below about 120 Hz, and what each surface wants in the shaker mix. The original game
has no shaker signal (docs/deviations.md, "Bass shaker"); this is the port's own design, from a playtest asking for a gravel
road pitched above off-road ground, more difference between roads and off-road, and a knock where a wheel meets a road or a
bridge. `tools/TactileRender` renders the options to WAV files.

## Where the vibration comes from

- **Speed turns wavelength into frequency.** A road feature of wavelength λ shakes the wheel at f = v / λ. PIARC sorts road
  texture by wavelength: microtexture under 0.5 mm, macrotexture 0.5 to 50 mm, megatexture 50 to 500 mm, unevenness
  (roughness) above 0.5 m. Megatexture is the band that causes in-car vibration and noise; unevenness drives the suspension
  and the dynamic tyre load. At 15 m/s megatexture lands at 30 to 300 Hz, unevenness under 30 Hz; at 30 m/s twice that.
- **The tyre filters the short waves.** The contact patch (about 10 to 20 cm long) envelops anything shorter than itself:
  the wheel centre follows a smoothed profile, a geometric low-pass. Macrotexture (the grain of the asphalt, single small
  stones) is heard as tyre noise far above 120 Hz, but reaches the body only as a weak, broadband, high hum. What reaches the
  seat below 120 Hz is megatexture and unevenness: stones and lumps bigger than a few centimetres, ruts, ripples, joints.
- **The suspension resonates.** A car's body bounces at 1 to 2 Hz and its wheels hop at 10 to 15 Hz (TRRL measurements over
  bridge joints and bumpy road). A sharp edge (a kerb, a deck joint, a pothole lip) excites the wheel hop: a short knock with
  energy from 10 to about 40 Hz, felt more as a jolt than a tone. The seat filters much of the hop out; the steering wheel and
  floor keep more of it.
- **Washboard is a tone.** Corrugated dirt and gravel roads ripple with a wavelength of about 0.5 to 1 m (0.3 to 0.6 m in
  some studies) and amplitudes up to about 5 cm: at 15 m/s a 15 to 30 Hz forcing, at 30 m/s 30 to 60 Hz, right in a shaker's
  best band. Random lumps (stones, ruts, holes a metre or more apart) are not a tone but a train of knocks whose rate rises
  with speed.
- **Measured gravel against asphalt.** Field measurements disagree on totals (one study finds a gravel road's peaks the
  highest of paved, concrete and gravel; another finds the gravel road lower in overall level than stone paving) but agree
  that roughness and speed raise the vibration dose roughly in proportion.

## What a shaker can play

Tactile transducers are rated from 5 to 200 Hz (ButtKicker, a large inertial type), 20 to 120 Hz (a sim-racing puck), or
30 to 100 Hz (small pucks, resonance about 33 Hz). Below about 20 Hz most amplifiers roll off, and above 80 to 100 Hz a shaker
turns into a buzz you hear more than feel; the port's crossover is 90 Hz by default. So the usable palette is about 20 to
90 Hz, with the most force from 25 to 60 Hz. Wheel hop (10 to 15 Hz) and body bounce cannot be reproduced as such: a knock is
played as a short sine at 25 to 30 Hz instead, which a shaker renders as a jolt. Sim-racing tactile setups (SimHub ShakeIt)
give each effect its own frequency so they read apart, a road-texture effect higher (60 to 90 Hz) than impacts.

## What each surface wants

| Surface | Character | In the mix |
|---|---|---|
| Asphalt (and parking lot) | smooth: macrotexture only, the tyre swallows it; a faint broadband hum rising with speed | a quiet, narrow band (56 Hz, Q 1.6), no lumps: the absence of texture is what makes the road feel like a road |
| Dirt road (the "gravel road") | loose aggregate 1 to 4 cm and a graded but uneven bed: dense small impacts, the grit of stones under the tyre, occasional potholes | the bed higher than off-road (66 Hz, Q 1.2) with short 76 Hz grains every 0.45 m driven (so their rate is speed: 33 a second at 15 m/s) and a faint 30 Hz lump every 4 m |
| Dirt and sand (off-road) | soft, lumpy, long waves: ruts, tufts, ridges a metre or more apart; little fine texture | a low band (30 to 34 Hz, Q 0.8 to 0.9) and 24 to 26 Hz lumps every 2.5 m (dirt) or 3.5 m (sand) |
| Rough ground (id 5, the gravel rolling sound) | rocks and holes, the bumpiest | a low band (32 Hz), strong 25 Hz lumps every 1.4 m and a little 70 Hz grit |
| Planks (plank roads) | boards and beams: a hollow, resonant deck | a narrow 46 Hz band (Q 2.5) and 40 Hz knocks every 1.2 m (the beams) |
| Onto or off a road or bridge | an edge: the wheel hop's knock, per wheel, front axle then rear a wheelbase later | a 28 Hz, 150 ms decaying sine per wheel, by strength (asphalt or a deck against the ground 1, a dirt road against the ground 0.5, asphalt against a dirt road 0.35) and speed (full from 50 mph, 0.15 of it near walking pace) |

Level: the force model's road strengths (asphalt 15, dirt road 20, dirt 25, sand 35, rough 40, out of 40) went straight to
the bed, so sand was only 7 dB over asphalt. The port stretches them about the middle strength (30): the bed goes as
strength/40 · (strength/30)^(contrast - 1), with contrast 2, which puts sand 15 dB over asphalt, asphalt 6 dB under its old
level and rough ground 2.5 dB over its own. `lfe_offroad` scales the off-road surfaces on top of `lfe_road`.

The pitch is the main cue between a dirt road and the ground (the playtest's ask): the dirt road's band is about an octave
over sand's and its grains are higher still, while off-road energy sits at 24 to 34 Hz. Speed moves the rate of the lumps and
grains, not the band: a real surface's spectrum slides up with speed (f = v / λ), but a sliding band on a shaker reads as an
engine note, and the engine already has that role.

Options not taken (rendered for comparison, `tools/TactileRender`): expansion-joint knocks on asphalt every 12 m (the desert
roads of the game are asphalt, not jointed concrete), a washboard dirt road (a tone at v / 0.7 m: convincing but tiring over a
long drive, and the game's dirt roads are not drawn corrugated), heavier lumps at contrast 3.

## Randomness, cracks, the shoulder, the knock and the impacts (round 2)

- **Randomness.** Each mission draws a seed; the synth's draws come from it (a fixed seed in the tests and sweeps, so they
  repeat). On top of the lumps' jittered spacing and level: the bed's level wanders about ±2 dB and its band about ±4 % with a
  correlation time of about 1.6 s (an AR(1) per tick), and one lump in 14 is 1.6 times as strong, 15 % lower and 40 % longer
  (a rock, a pothole, a wide crack). `TactileTuning.Variation` 0 turns it off.
- **Filled cracks** on asphalt and parking lots: a 38 Hz, 70 ms tick every 8 to 20 m (14 m ± 43 %), each met again by the rear
  axle a wheelbase (2.8 m) on; 4 to 6 dB over the asphalt bed's own peaks (-22 against -26 dBFS at 30 m/s). With them the
  asphalt's RMS is about the dirt road's (it was 4 dB under), and sand stands 10 to 13.6 dB over asphalt instead of 13.6 to 15:
  the bed itself is as quiet as before, the cracks are separate ticks.
- **Rumble strip.** The game's roads have no shoulder; the heightfield's 5 m surface cells give the edge. When one side's two
  wheels are on asphalt and the other's off it for 0.3 s, the car runs along the edge: a buzz at the milled strip's strike rate
  (speed over 30.5 cm: 49 Hz at 15 m/s, 98 Hz at 30 m/s), folded an octave down while over 60 Hz so a shaker keeps it.
- **The knock** for a transducer that needs time to reach amplitude: 28 Hz held at full for 140 ms then down to -40 dB by
  350 ms, a 52 Hz thud (0.6 of it, 200 ms) and an 82 Hz click (0.45, 35 ms) for the edge, the click held up against the
  crossover's low-pass at its frequency (at most 6 dB, so a 60 Hz crossover does not eat it).
- **Impacts.** The level by damage on a log curve (the DLL's strengths are full from about 80 damage, so a landing, a ram and a
  head-on crash all felt the same): -12 dB at 8 rising 6 dB a decade to 0 dB at 800, an explosive hit 2 dB more. Each hit has
  its channel's tone held 60 ms, a 55 Hz part and an 80 Hz click. An impact-channel hit over 20 (the original's threshold for
  its cockpit jolt, `ScreenWobble`: 19 px easing out at 2·dt a frame, a 0.5 s time constant, under a pixel at 1.47 s) rings
  the cabin at 26 Hz, 0.55 of the hit, with the same 0.5 s time constant.

### Headroom plan

Peaks at the limiter's input at LFE 100 % (gain 1.5), measured by `tools/TactileRender --plan` (seed 76):

| Peak | What | Measured |
|---|---|---|
| 0 dBFS | the hardest impact: a head-on ram, a crash into a building; a blast at the car | head-on (783) -0.2, building (300) -2.7, blast at 5 m -2.1 |
| -6 | a typical hit, an axle's knock at speed, the rumble strip | rocket -8.3, landing (80) -6.1, ram (120) -5.1; axle knock 30 m/s -6.0; strip -7 to -9 |
| -12 | a wheel's knock, the heavy shots, the engine, light hits | wheel knock 30 m/s -12.0; cannon shot -12.1, mortar -11.2; engine -11.7; cannon round -12.0, MG round -15.5 |
| -6 or under | every road at its worst (full speed, the springs busy) | rough -8.5 (-6.7 busy), sand -7.2 (-5.1 busy), asphalt -18; big lumps now and then to about -5 |
| -18 to -24 | a filled crack, a road at 15 m/s | crack at 30 m/s -22.3, 15 m/s -26.7; sand at 15 m/s -17.5 |

The roads are 14 dB under their first-round level for this (`TactileTuning.RoadTrim`; `lfe_road` 200 gives 6 dB back), and
the shots 6 dB. The limiter (0.85·tanh(x/0.85), 1 dB of reduction from -5.7 dBFS) then takes the hardest impacts to about
-2.3 dBFS out and leaves the roads alone at LFE 100 %.

## Sources

- PIARC road dictionary, [megatexture](https://www.piarc.org/en/activities/Road-Dictionary-Terminology-Road-Transport/term-sheet/92983-en-megatexture) and [macrotexture](https://piarc.org/en/activities/Road-Dictionary-Terminology-Road-Transport/term-sheet/92982-en-macrotexture?ls=de); [Road texture](https://en.wikipedia.org/wiki/Road_texture) (Wikipedia); FHWA, [concrete pavement texture](https://www.fhwa.dot.gov/pavement/pubs/hif17011.pdf)
- [The Schwitters memorial problem: a car speeds across a triangular bump](https://arxiv.org/pdf/2609.02938) (contact patch about 10 cm envelops shorter irregularities; tyre corner about 10 Hz)
- TRRL, [Vertical accelerations measured in several types of car travelling over surface discontinuities](https://trid.trb.org/View/22565) (body heave about 1.75 Hz, wheel hop 10 to 15 Hz; the seat filters the hop)
- [Corrugation of an unpaved road surface under vehicle weight](https://arxiv.org/pdf/2007.05109); N. Taberlet et al., [washboard road](https://perso.ens-lyon.fr/nicolas.taberlet/washboard/), [arXiv cond-mat/0011510](https://arxiv.org/pdf/cond-mat/0011510)
- Vibration on paved, concrete and gravel roads: [LBTU conference 2022](https://iitf.lbtu.lv/conference/proceedings2022/Papers%5CTF238.pdf), [Komunikacie 2019](https://komunikacie.uniza.sk/pdfs/csl/2019/01/03.pdf)
- Shaker ranges: [ButtKicker LFE](https://www.parts-express.com/ButtKicker-BK-LFE-Low-Frequency-Effect-Transducer-300-9400), [Trak Racer haptic kits](https://trakracer.com/products/trak-racer-4ch-40w-low-frequency-class-d-amplifier-with-2-bass-shakers-and-rig-mounts); effect frequencies in practice: [SimHub ShakeIt discussion](https://overtake.gg/threads/simhub-shakeit-bass-shaker-v3-beta-discussion.167235/page-3)
