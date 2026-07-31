# Art

How the game is supposed to look, how every asset in it is regenerated, which
settings actually decide the picture, and what is still wrong.

Companion documents: [ASSETS.md](ASSETS.md) for the asset pipeline and file
contracts, [BLOCKERS.md](BLOCKERS.md) for things that stop the game working,
[game-design.md](game-design.md) §03 §05 §07 §12 for the rules every value here is
derived from.

---

## 1. What the look has to do

The look is not decoration in this game. Three design sections make it load-bearing:

| Section | Demand on the picture |
|---|---|
| §03 | 어둠 = 목표의 잠금장치. Darkness gates the objective; the flashlight is the key. If a room is readable without the beam, the lock is open. |
| §05 | First person, 90° FOV, a 22° half-angle cone. Everything the player knows arrives through that cone or not at all. |
| §12 | 구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다. Five floor materials must be **told apart on sight**, not just in the audio mixer. |
| §07 | The night gets worse continuously across five tiers, and it has to be felt. |

So there are four targets, in priority order:

1. **The beam is the source of information.** Outside it, shape only — enough to know
   a corridor turns, not enough to read a clue.
2. **The five floors are distinguishable at a glance**, under the beam and at the
   edge of it.
3. **Depth.** A frame must have a near, a middle and a far. A flat black field behind
   a lit disc is the failure mode.
4. **You can tell where you are** from a still frame.

### Measured targets

Judging "is it too dark" by eye across iterations does not work — the eye adapts to
whatever it saw last, and every iteration looks like an improvement on the one
before. These are the numbers a first-person frame should land in, measured with
`tools/render/frame_stats.py`:

```bash
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'final_*.png'
```


| Measure | Target | Why |
|---|:--:|---|
| pixels below 2/255 ("crushed") | 10–40% | Below 10%, the dark is not dark. Above ~50%, the frame is a black rectangle and the player has been denied input rather than gated. |
| pixels in 8–235 ("legible") | 30–75% | Shape outside the beam. |
| median luminance | 3–16 | The unlit room. Not zero. |
| pixels above 250 ("blown") | < 0.5% | A clipped hotspot throws away the texture detail in the exact part of the frame the player is looking at. |

**Three of the five zone views now miss the legible floor, and the five-storey map
is measurably darker than the three-storey one it replaced.** Measured 2026-08-01
on `Shots/map_*`, which is the same command and the same viewpoints as the
`real8_*` figures it supersedes — only the tag differs:

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -silent-crashes -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag map
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'map_Zone_*.png'
```

```
shot                                     mean    p50    p90    p99  black%  legible%  blown%    sat
map_Zone_A_B2_Wood.png                    6.9    2.9   15.8   62.8    40.6      25.9    0.00    5.6
map_Zone_B_B5_Tile.png                    7.5    3.4   17.0   63.7    36.1      29.2    0.00    7.9
map_Zone_C_B4_Gravel.png                  7.4    3.3   17.5   69.2    38.0      26.6    0.00    7.3
map_Zone_D_B1_Concrete.png                9.2    6.2   18.9   59.5    17.7      40.4    0.00   10.8
map_Zone_E_B3_Metal.png                  12.7    8.4   31.1   49.9    17.0      52.5    0.00   14.6
```

Four misses against the bands above, where the previous map had one:

| Band | Was (`real8_*`, 3 storeys) | Now (`map_*`, 5 storeys) | |
|---|---|---|:--:|
| crushed 10–40% | 10.4–37.4% | 17.0–**40.6%** | zone A over |
| legible 30–75% | 28.4–54.8% | **25.9**–52.5% | A, B, C under |
| median 3–16 | 3.9–9.1 | **2.9**–8.4 | zone A under |
| blown < 0.5% | 0.00% | 0.00% | ok |

The direction is consistent rather than noisy — every zone lost legible fraction and
every median fell. Zone A (wood, B2) is the worst on all three moving measures. The
grade was not retuned when the building grew from 74 places to 164; the same ambient
and fog now have to carry rooms that are further apart and more of which sit outside
any practical's falloff. This is an unclosed regression, not a settled state.

The older claim on this page — "16–39% crushed and 31–57% legible" — was stale by
two passes and was recorded as defect 3.7 in STATUS.md. Do not trust a range here
that does not name the shot tag it was measured from.

---

## 2. Regenerating every asset

The order matters and is not obvious. Each step below overwrites state the previous
one wrote.

### 2.1 Textures → materials

```bash
# There is no venv under tools/textures. The generator needs numpy and scipy and
# the audio toolkit's interpreter already has both — bare `python3` fails with
# ModuleNotFoundError: No module named 'numpy'.
V=tools/audio/.venv/bin/python
$V tools/textures/gen_textures.py                  # → unity/HorrorGame/Assets/Textures/**
$V tools/textures/gen_textures.py --only Floor_Wood  # one, while iterating
```

`--only` deliberately does **not** rewrite `Textures.manifest.json`; the manifest
is written only when every material was generated, so a filtered run cannot leave
the contract describing a set that was not produced. Run it unfiltered before
building materials in Unity.

Writes albedo / normal / roughness / AO / metallic-smoothness at 1024², plus
`Textures.manifest.json`, which is the contract the Unity side reads. Then bind them:

```bash
Unity -batchmode -quit -nographics -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.TextureImport.ProceduralTextureMaterials.Build
```

This writes the five contractual floor materials **in place** at
`Assets/Scenes/Generated/Materials/Floor_*.mat`. It must not create them anywhere
else and must never delete them: the generated scene references them by GUID, and a
fresh asset mints a fresh GUID and silently unbinds every floor in the map.

### 2.2 Geometry (Blender)

```bash
tools/ci/run_blender_generators.sh              # map kit, dressing, props, player, monster
tools/ci/run_blender_generators.sh gen_dressing # one, while iterating
```

Do not judge these by exit code. Blender `--background` exits 0 after an uncaught
Python exception; the script checks for an `ASSET_FAILED` marker, a traceback, and a
missing `ASSET_REPORT` line instead. `gen_mapkit_detail.py` is a library
`gen_mapkit.py` imports, not a generator, and is correctly absent from the list.

### 2.3 The map — one command, three passes

```bash
Unity -batchmode -quit -nographics -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.MapPipeline.RegenerateFromCommandLine \
  [-mapSeed 1204] [-dressSeed 4703] [-atmoTier 0]
```

`MapPipeline` exists because the three passes were written independently, each one
saves the scene, and running them out of order silently discards work rather than
failing. Concretely: `MapSceneBuilder` used to write a placeholder ambience and leave
Unity's default **daytime** skybox in `RenderSettings`, so a map regenerated after
the atmosphere pass rendered under a bright procedural sky. Nothing errored. The sky
was simply the brightest thing in every frame, and every smooth surface mirrored it.

Order is **layout → dressing → atmosphere**, atmosphere last because it is the only
pass that knows what the air is supposed to look like.

The pipeline exits non-zero if §12 rejects the layout or the dressing scatter seals a
route. The atmosphere pass runs regardless of the dressing result — the dressing
gate is about NavMesh reachability, which has nothing to do with what the air looks
like, and bailing out would leave a scene with dressing in it and no environment.

### 2.4 Review shots

```bash
Unity -batchmode -quit -silent-crashes -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag final
```

**No `-nographics`.** That flag disables the graphics device and every shot comes out
black.

`AtmosphereSetup.ShotBatch` does the same thing with a chosen `-atmoTier` applied in
memory and nothing saved — the fast loop for grading, and the only way to review "the
night gets worse" as a picture rather than as a table.

---

## 3. The settings that decide the picture

### 3.1 Reflection intensity — `NightAtmosphere.ReflectionIntensity = 0.25`

The single largest error in the pre-existing frames, and the least visible in code.
Unity defaults `RenderSettings.reflectionIntensity` to 1. Ambient in this scene is
about 0.005 linear, but reflection intensity is a **separate term that does not read
the ambient colours at all**: a smooth surface mirrors the skybox cubemap at full
strength no matter how dark the diffuse ambient is.

§12 gives 타일 and 금속 the two lowest roughness values in the set on purpose, so the
beam streaks across them. At full reflection that inverted into two floors that glowed
without a beam anywhere near them — the metal floor was reliably the brightest object
in the game.

Not zero. Zero removes the sheen that makes a wet tile floor read as wet rather than
as flat paint, and that sheen is a §12 zone cue.

### 3.2 Contrast — `EarlyEvening.Contrast = 10` (was 26)

URP applies contrast in **ACEScc log space** around a mid-grey pivot of about 0.41,
so a value that reads like "a bit punchier" pushes an already-dark ambient almost to
zero. At +26 the walls of an unlit room measured a median luminance of **1 out of
255** — black, not dark, with 89% of the frame crushed and 8% legible. The night is
made out of the ambient values and the fog; this has to stay low enough to let them
survive.

### 3.3 Ambient — raised ~2.6× in sRGB, ~8× in linear

The previous values were tuned while the default daytime skybox was quietly
contributing most of the light in every frame. Once the night sky went in and
reflection came down, the same numbers rendered a black screen. The ambient had never
actually been carrying the room.

Trilight, not flat: equator (walls) brightest, ground (ceiling) darkest. A flat term
lights ceiling and floor identically, which is what makes an unlit corridor read as a
diagram.

### 3.4 Fog — exp², density `√(ln 2) / 25 ≈ 0.0333`

Solved, not eyeballed. Puts half-visibility at exactly 25 m —
`GameConstants.LineOfSightBreakSpacingMax`, the longest sight line §12 obliges a
legal map to provide, and also `FlashlightNoticeDistance`. At the distance §12's
escape arithmetic depends on, a shape is still half there: visible enough to aim a
taunt at, hazed enough that the corridor has depth.

Fog colour is kept **brighter than the ambient-lit walls**. That is what makes
distance read as haze rather than as a hole.

### 3.5 The flashlight — `FlashlightBeam.Intensity = 2.6`

There is now exactly one description of §03's beam, in
`Assets/Scripts/Rendering/FlashlightBeam.cs`, used by both the player rig and the
review rig. They disagreed before: the player ran it at 6 with hard shadows, and
`SceneShot` built its own at 3.5 with soft ones. Every screenshot the art was judged
against was a picture of a light that is not in the game — and the dimmer of the two,
so the beam looked better in review than in play.

At 6, a wall inside about two metres — which is most of a §12 corridor, whose clear
section is 2.2 m — clipped to pure white. 2.6 keeps a 1 m wall near the top of the
range without clipping and still lands visibly at the 12 m range.

Additional-light shadows must be on in the URP asset. A spot light that casts no
shadow passes through crates, doors and players, and §03's hiding is meaningless
without it.

### 3.6 Practical lights — range 5.5 m, intensity 1.1, tinted per zone

One fitting in five works: 16 across 2469 m². The range was one MapKit cell (2.5 m),
which was too literal a reading of §03's rule — hung at a 3 m ceiling it put a 0.5 m
disc on the floor directly underneath and was invisible from anywhere else. 5.5 m
reaches the floor and stays visible from down a corridor, and stays under half the
flashlight's range so a bulb never out-reaches the player's own beam.

They cast shadows now, which they did not — see §3.8b, and read that before
turning it off to buy back a frame.

They are tinted by §12 zone palette — tungsten in 나무 storage, faintly green
fluorescent in 타일 institutional, cold in the flooded 자갈 end, hard yellow in plant.
A warm glow at the end of a corridor is a different place from a green one before you
can see what either is attached to. Kept a few degrees apart, not a rainbow: the
building is one building.

### 3.7 Colour space

Linear, forced by `AtmosphereSetup`. Not a preference. A tonemapper is a curve
applied to *linear* radiance; Neutral or ACES in gamma space grades an
already-encoded image and returns the washed, milky look people mistake for "the
tonemapper is wrong". Every value above assumes linear and none of them mean anything
without it.

### 3.8a Texture scale — the kit unwraps at **0.5** UV units per metre

The largest error in the pre-existing frames, measured rather than judged: **every
surface in the game was rendering at exactly twice the size it was drawn at.**

`gen_mapkit.py` sets `UV_METRES_PER_TILE = 2.0`, and `uv_box_project` multiplies
each world coordinate by its reciprocal, so one metre of wall carries half a UV
unit. `ProceduralTextureMaterials.ApplyTiling` computes the material's tiling as
`KitUvUnitsPerMetre / world_size_metres` with `KitUvUnitsPerMetre = 1f`, on the
strength of a comment claiming the kit was unwrapped in metres.

Confirmed against the shipped FBXs rather than against either source. Importing
`Corridor_Straight_5m`, `Hall_Open_20x20` and `FloorTile_Concrete` and dividing
every UV edge length by its world edge length:

```
UVPM Corridor_Straight_5m.fbx  faces facing X (walls)          n= 1804  median=0.5000
UVPM Corridor_Straight_5m.fbx  faces facing Y (walls)          n= 1316  median=0.5000
UVPM Corridor_Straight_5m.fbx  faces facing Z (floor/ceiling)  n= 1376  median=0.5000
UVPM Hall_Open_20x20.fbx       faces facing Z (floor/ceiling)  n= 3390  median=0.5000
UVPM FloorTile_Concrete.fbx    faces facing Z (floor/ceiling)  n=  192  median=0.5000
```

0.5000 on every face of every orientation, 19 632 edges. So a brick drawn at
21.5 cm arrived 45 cm wide, a floor tile drawn at 25 cm arrived at 50 cm, and the
crack network drawn across a 2.5 m slab spanned five metres of floor. Nothing
else on this page communicates "this is a game asset" as loudly as masonry at
double scale, and it is why §7.5 below used to complain about brick courses.

**The correction lives in `tools/textures/gen_textures.py`**, in the constant
`KIT_UV_UNITS_PER_METRE`, because the engine-side file belongs to another area.
The manifest's `world_size_metres` is therefore emitted in the units that field
is actually *consumed* in — UV units per tile — and the truthful figure travels
beside it as `authored_metres_per_tile`. If `KitUvUnitsPerMetre` is ever corrected
to 0.5 in the engine, set the generator's constant to 1.0 in the same commit: the
two multiply, and getting both right doubles the error instead of fixing it.

Texel density doubled as a side effect, from 284 px/m to 569 px/m at a 1.8 m
tile. That is the right number rather than a bonus: a 1920-wide frame at §05's
90° FOV resolves about 21 px per degree, one centimetre two metres ahead subtends
0.29°, so the eye separates roughly 600 px/m at the distance a floor is read at.
The old 284 was visibly soft and nobody had a number to say so.

### 3.8b Every fitting casts a shadow

All 72 point lights in the map were authored with `LightShadows.None` — the caged
bulbs the dressing pass hangs, the entrance light, and §04's switchable zone
lights. `AtmosphereSetup.CastShadowsFromEveryFitting` turns them on.

This is not a quality setting here. A light that casts no shadow does not stop at
walls: it lights the far side of a partition, the inside of a crate and the
corridor behind a closed door, at full strength.

- §03 makes darkness the lock on the objective, and a shadowless fitting hands
  out light through the geometry that was keeping the room dark.
- §04 sells the 정비공 zone lighting as an ability with a material cost. A light
  at `ZoneLightRadius` that ignores walls lights the two zones either side of the
  one that was paid for, so the ability cannot be balanced at all.
- And it is most of what "looks like a real light" means: a bare filament in a
  wire cage throws the cage across the wall behind it.

Hard, not soft, and deliberately — a bare bulb is a point source and its shadows
genuinely are sharp. Costed in §6.

**It also made the building measurably darker, and that darkness is real.** Every
zone had been reading partly by light from the room next door. The AO and floor
albedo numbers below were re-tuned against the corrected lighting, not against
the leak.

### 3.8c Wet — §03's first worked example

§03's first example of a clue is 물이 있는 층, so wet is a gameplay-readable
surface state and not decoration. Three separable effects, applied in the order
they physically happen, in `gen_textures.wet`:

- **Darker.** Water removes the air/solid interface that was scattering light
  back out, so more of it enters the substrate. A damp patch is genuinely darker,
  not tinted.
- **Smoother.** A film fills the micro-roughness, which is what turns the beam's
  diffuse pool into a streak. At a grazing angle from 1.63 m that streak is the
  whole read.
- **Flat.** Standing water has a level top, so the height field is pulled up to
  the water line where it pools, and the puddle mirrors the room instead of the
  gravel under it.

Water levels are **quantiles of each surface's own height field**, not constants —
`water_line(height, fraction)`. The first version used constants and flooded
0.3 % of the gravel: packed ballast is a max over three layers of domes and its
5th percentile sits at 0.538, so "the bottom third" was nowhere near the bottom
third. Both floors measured wet and neither looked it.

Zone C is the flooded end and gets standing water in the voids between stones;
the concrete slab gets water in its cracks; the tiled floor fills its grout lines,
which is the most legible of the three because the beam then finds a lit lattice
rather than a lit patch; the timber floor wicks damp out of its joints and holds
none, because a lake on a wooden floor would read as the wrong zone.

### 3.8 The monster — a rim, two eyes, and less fog than the room

The creature was invisible past about 10 m and that was not atmosphere, it was two
broken systems: §04's 관측자 works at 15 m and §12's 주자 table endorses pulling aggro
from 10 m upward. Neither role can be played against something that is not in the
frame.

The cause is arithmetic rather than taste. The creature's albedo is calibrated to sit
just under the darkest wall in a §12 corridor; past the beam both are lit by the same
ambient and hazed by the same fog, so they land on the same luminance. Measured
against a dark wall at 15 m, the body came back **0.0128 out of 1.0** from the wall
behind it — three code values. The flat ambient fill that was supposed to fix this was
making it worse: it lifted the whole body *toward* the wall.

Three changes, in `Assets/Shaders/Monster/MonsterSkin.shader`:

- **A Fresnel rim** off the geometric normal, ramped up with distance by
  `MonsterBeamResolve` and zero inside 3 m. It puts light where the wall has none —
  on the outline — instead of raising the body. Exponent 4.5, not the 2.2 it was first
  authored at: a broad Fresnel's tail reaches the chest and face and lifts exactly the
  surfaces that should stay dark.
- **Two emissive lenses**, 7 cm, placed by ray-casting the head blade's own front face
  so a re-shaped head cannot bury them. Two separated points are what a person reads a
  face and a *facing* from at a range where the body is a smudge, and the facing is
  half of what §04's 관측자 is for.
- **`_FogResponse = 0.45`** — the creature takes 45% of the room's haze. URP hazes the
  creature and the wall behind it identically, so the haze lands on the difference
  between them and cancels it. Holding it back makes the creature fall as a *darker*
  shape against a lifting background, so distance now makes it clearer.

Albedo was not touched. A pale creature glowing in a dark corridor is a worse failure
than an invisible one, and the near field is measurably unchanged: at 3 m the creature
renders within 0.004 of what it did before.

Measured by `MonsterShot.StageBatch` — see [TESTING.md](TESTING.md) §6b for the three
gates and why each is needed. Against a dark wall with no backlight:

| Distance | contrast before → after | coverage before → after | peak before → after |
|---|:--:|:--:|:--:|
| 8 m | 0.0107 → **0.0193** | 0.68 → 0.74 | 0.036 → 0.060 |
| 12 m | 0.0099 → **0.0458** | 0.53 → 0.95 | 0.024 → 0.110 |
| 15 m | 0.0130 → **0.0459** | 0.63 → 0.95 | 0.021 → 0.104 |
| 20 m | 0.0121 → **0.0444** | 0.27 → 0.94 | 0.013 → 0.078 |

Every distance now reads as a darker silhouette than its background; before, every
distance past the beam read as slightly *brighter* and none of them read at all. 8 m
is the tightest — inside §03's beam the creature and the wall behind it are lit by the
same falling-off spot, which is the one case a rim cannot help much.

---

## 4. Ceilings

`MapSceneBuilder.BuildCeilingCaps` roofs the parts of a lower storey that nothing
stands on top of.

A storey's ceiling is normally the floor slab of the storey above, which works
wherever the footprints overlap. They do not always overlap: §12 asks for 4~6 zones
and 30~40 m diagonals and says nothing about stacking them, so the generator is free
to put a B3 zone where no B2 zone reaches — and it did. The result was 저수조, seven
and a half metres underground, with the **night sky visible above the walls**.

No validator could catch it. §12's checklist is entirely horizontal and the graph it
runs against has no notion of a ceiling.

The cap skips any cell with a kit piece one storey up, which is load-bearing rather
than tidy: a stairwell rising out of a storey lands in a corridor above, corridors are
tiles rather than zone rects, and without that clause the cap would be poured across
the top of the stairs and seal the only vertical route §03's clue chain has.

---

## 5. Reviewing shots

`SceneShot` picks its viewpoints from the scene's contents, so a regenerated map is
still framed sensibly. Three things about it are deliberate:

- **Zone views are pitched 14° down and aimed along the longest clear line.** Both
  were wrong before. A level camera at 1.63 m fills the frame with wall and shows
  almost no floor, so the reviewer was asked to tell §12's five 바닥 재질 apart in
  pictures that barely contained one; and a fixed 35° yaw put three of five cameras a
  metre from a wall.
- **Cameras are nudged out of geometry.** A zone's bounding-box centre is frequently a
  wall, a pillar or a stack of crates — the dressing puts 1074 pieces in this map and
  the middle of a room is where the big ones go.
- **The overhead view gets a survey light of its own** and is explicitly *not* a game
  frame. It is 60 m above a building lit by a 12 m torch; under the game's own
  lighting it renders as a black rectangle, which is correct and useless. Keeping the
  survey light on a separate switch is what stops the convenience from flattering the
  shots that are supposed to show the dark.

---

## 6. Where it stands

Map: `Assets/Scenes/Map_FirstSketch.unity` as saved, seed 1204, dressing seed
4703, §07 tier 0. Rendered with

```bash
… -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag real8
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'real8_*.png'
```

`real1` is the same command run before any of §3.8a–c, on the same scene, so the
two columns are a controlled comparison.

| Shot | crushed % (10–40) | legible % (30–75) | median (3–16) | blown % (<0.5) |
|---|--:|--:|--:|--:|
| Zone A · B1 · 나무 | 37.4 → **37.4** ✓ | 32.8 → **32.0** ✓ | 3.9 → **4.1** ✓ | 0.00 → **0.00** ✓ |
| Zone B · B3 · 타일 | 16.4 → **15.8** ✓ | 48.3 → **50.7** ✓ | 7.4 → **8.1** ✓ | 0.00 → **0.00** ✓ |
| Zone C · B2 · 자갈 | **43.8 ✗ → 34.4** ✓ | **29.2 ✗ → 28.4 ✗** | **2.4 ✗ → 3.9** ✓ | 0.00 → **0.00** ✓ |
| Zone D · B1 · 콘크리트 | **8.8 ✗ → 10.4** ✓ | 58.6 → **54.8** ✓ | 9.2 → **8.9** ✓ | 0.00 → **0.00** ✓ |
| Zone E · B2 · 금속 | 19.1 → **18.0** ✓ | 44.1 → **45.4** ✓ | 6.5 → **7.1** ✓ | 0.00 → **0.00** ✓ |
| spawn0 | 1.7 → **4.3** | 95.3 → **89.5** | 59.6 → **52.4** | 0.00 → **0.14** ✓ |
| spawn1 | 10.2 → **28.7** | 74.9 → **49.7** | 19.1 → **7.9** | 0.00 → **0.00** ✓ |
| spawn2 | 6.0 → **8.1** | 91.6 → **86.7** | 47.4 → **39.9** | 0.00 → **0.00** ✓ |
| spawn3 | 27.7 → **35.0** ✓ | 41.6 → **38.1** ✓ | 6.3 → **5.4** ✓ | **0.93 ✗ → 0.29** ✓ |

Two of the five zone views were out of band, in opposite directions, and both are
now in. Zone C's legible fraction is the one figure still outside: 28.4 % against
a 30 % floor. It moved 43.8 → 34.4 on crushed and did not recover on legible,
because the fittings shadow correctly now (§3.8b) and zone C had been reading
partly by light from the rooms next door. Its floor is already at 0.44 linear —
the last step before `ALBEDO_MAX_LINEAR` stops the run — and its baked AO is down
to 0.55 strength. The remaining 1.6 points are the room's light level rather than
the floor's paint, and the fittings belong to the dressing pass.

**spawn0 and spawn2 are outside the band by design and always were.** They stand
at the surface by the vehicle; a lit loading yard is not what a 10–40 % crushed
band describes. spawn1 moved a long way (74.9 → 49.7 legible) for the same reason
zone C did: it had been lit through walls.

### Frame cost

```bash
… -executeMethod HorrorGame.EditorTools.Rendering.FrameCost.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -costTag final
```

1920×1080, MSAA 4×, §07 tier 0, on the M1 Pro. Median of 40 timed frames per
viewpoint after 12 warm-up frames, each frame followed by a one-pixel read-back so
the timer measures the render rather than the submission. This is the renderer's
share **in the editor** — no physics, animation, networking or UI — so read it as
a before/after against itself and not as a player frame rate.

| | before (real1) | after (real8) |
|---|--:|--:|
| typical viewpoint | 8.48 ms (118 fps) | **8.89 ms (113 fps)** |
| worst viewpoint — Zone E, the open hall | 16.74 ms (60 fps) | **20.25 ms (49 fps)** |

+4.8 % typical, +21 % on the worst viewpoint, and effectively all of it is §3.8b:
72 fittings that now render shadow maps. The texture work costs nothing at
runtime, and the SSAO change moved intensity and radius rather than the sample
count. Zone E was already the outlier before any of this — an open 20 × 20 m hall
with a 6 m soffit and the most lights in frustum.

The dial, if it ever needs turning, is `CastShadowsFromEveryFitting` in
`AtmosphereSetup`: `LightShadows.None` there returns the cost and gives back light
that passes through walls. §04's zone-lighting ability cannot be balanced in that
state, so this is the price of a mechanic and not of a picture.

---

## 7. What still needs work

Honest list, worst first.

### 7.1 ~~The monster still cannot reach anybody~~ — closed

Stale. [B-001](BLOCKERS.md#b-001) is closed and `MonsterChaseTests` is 4/4 —
`<test-run result="Passed" total="4" passed="4" failed="0">`, re-run against the
scene this page's numbers were measured from. See [STATUS.md](STATUS.md) §1.3 for
the route, the 4.83 m/s and the 27.52 s.

Kept as a heading rather than deleted because the paragraph that used to be here
was quoted elsewhere as current for at least one pass after it stopped being true.

### 7.2 The graph and the geometry disagree about sight lines

§12's validator says *"Longest unbroken sight line is 20 m, inside §12's 20 m
limit"*. The geometry sampler in the same run says *"Corridor sight lines: 114
sampled, mean 15.0 m, longest **100.0 m**"*. Both are in the same log, four lines
apart. The validator checks the graph; nothing checks the built scene against it.
This is the same class of bug as B-001 and probably has the same root cause.

Related: *"14 corners, nearest-neighbour 2.5 m–7.5 m, mean 4.3 m, **0 inside the
band**"* against §12's 15–25 m 시야 차단 지점 spacing rule. The map passes the
checklist and violates the numeric rule the checklist was derived from.

### 7.3 Every room is the same room

Five zones, five floors, and the floors genuinely do read now. Everything above knee
height does not: the same brick wall, the same square hall, the same single central
pillar. The zone-tinted practicals help, but they are 16 lights in a 60×65 m
building. A still frame taken above the beam still cannot answer "where am I" without
looking down.

This is a map-kit and set-dressing problem, not a lighting one. It wants per-zone wall
treatments and at least one silhouette-level architectural feature per zone.

### 7.4 Sky is still visible from some lower-storey corridors

`BuildCeilingCaps` covers zone rects. Corridor cells outside a zone rect on B2/B3 are
still open to the sky where nothing is above them — visible at the right-hand edge of
the Zone E frame. Extending the cap to tile cells is easy; doing it without sealing a
stairwell needs the stairwell guard extended too, and with the NavMesh already broken
there is no way to verify the result, so it was left.

### 7.5 ~~Brick scale~~ — fixed, and it was worse than this said

Fixed in §3.8a. It was not a brick problem: **every** surface in the game was at
double scale, because the kit unwraps at 0.5 UV units per metre and the binder
assumed 1. A course now measures 7.5 cm and a brick 22.5 cm, against a real
21.5 cm brick plus a 10 mm joint.

Note for whoever reads this next: the number in the old paragraph was an eyeball
estimate and it was wrong by a factor of two in its own right. The measurement
that settled it is in §3.8a and took one headless Blender script.

### 7.6 ~~Thin geometry blows out at close range~~ — re-diagnosed, not a blowout

Re-measured on `real8_Zone_A_B1_Wood.png`, which still shows the vertical bar:
**16 pixels in the frame exceed 250, and `blown %` is 0.00.** Nothing is clipping.
The bar is a door leaf and its jamb seen edge-on from about fifteen centimetres,
because `SceneShot.ClearStandingSpot` parked the review camera inside a doorway —
a `Physics.CheckSphere` at 0.5 m clears an open door frame.

So it is a framing bug in the review rig, not a material one, and raising the trim
roughness to "fix" it would have dulled every door in the game to hide a camera
placement. The one real close-range clipping case — plaster at one metre in
`spawn3`, 0.93 % of the frame over 250 — was fixed by raising that material's
roughness floor to 0.40 and dropping its albedo to 0.30, and now measures 0.29 %.

### 7.7 The overhead view is still nearly useless

It shows a roof. Judging §12's zones, loops and dead ends from above needs the top
storey culled, which nobody has written. `MapQualityReport` is the real layout gate
and it is thorough; the overhead shot should probably be deleted rather than fixed.

### 7.8 Everything is one colour

The grade is cold, saturation −34, and the practicals are the only warmth in the
building. It is coherent, and it is monotonous over a 25–35 minute match. §07 ramps
brightness and vignette across the night but not hue; there is room for the last tier
to go somewhere the first tier does not.

### 7.9 What surface variation still cannot do from the texture side

Three of the things that most separate this from a shipped look need a shader, and
the materials are bound to URP's stock `Lit` by `ProceduralTextureMaterials`, which
is not this area's file:

- **Detail normals.** The binder sets `_BaseMap`, `_BumpMap`, `_OcclusionMap` and
  `_MetallicGlossMap` and nothing else, so `_DetailNormalMap` is unreachable. The
  near-field micro-detail that a detail map would give is currently carried by the
  base normal alone, which is why the tiling correction mattered so much — it is
  the only lever that made the base normal sharper.
- **Stochastic or triplanar blending between clean and damaged.** The proper
  answer to repetition. `detile` in `gen_textures.py` is what can be done from the
  texture side alone: it divides out each tile's low-frequency luminance envelope
  so the repeat has no blob to lock onto, and it does not stop a wall from being
  literally the same wall every 1.8 m.
- **Decals.** URP's decal renderer feature could be added to
  `HorrorGame_URP_Renderer.asset`, but a decal needs a projector placed where a
  prop meets a floor, and prop placement is the dressing pass. The contact
  darkening that exists instead comes from SSAO plus two things baked into
  materials that sit on the junction itself — the skirting's dirt line and the
  plaster's rising damp, both registered to the floor by §3.8a's box projection.

### 7.10 Light shafts

Not attempted. URP 17 has no volumetric fog, so a visible shaft through a grate
means an additive cone mesh placed per fitting — geometry, which is the dressing
pass's, not a setting. The cheap substitute would be a light cookie, and URP wants
a **cubemap** cookie for a point light; every fitting in this building is a point
light.
