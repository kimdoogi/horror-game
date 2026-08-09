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

> 🟢 **Current, 2026-08-09, tag `realtex3` — six zone wall/floor materials got a CC0
> photo-scan base layer (PolyHaven brick + concrete, ambientCG plaster + tile +
> diamond-plate; procedural grime/wet/grain kept layered on top). It caught, and
> survived, exactly the trap this section is about.** The raw swap looked better on a
> flat plane but measured **+6–8 pts more crushed in every one of the six zones** on a
> clean same-map A/B (procedural fallback vs CC0, identical scene + seed — so the deltas
> are the textures, not map drift), at an *unchanged* mean luminance. That is the §3.13
> signature of a real regression: a shadow-floor/contrast problem, not a brightness one —
> photo scans carry deep mortar/grout valleys and deeper baked AO than the old procedural
> surfaces, and under the dim flashlight those valleys fall below 2/255. It tipped B3 and
> B6 out of band. Recovered **in the texture generator, not the lighting**: a per-material
> `PhotoSpec.ao_floor` (lifts the baked-AO floor toward 1.0) + `shadow_lift` (lifts only
> the darkest albedo texels, below a `shadow_knee`, after grain and before the mean is
> re-landed — so no midtone moves and the relief in the normal/height maps is untouched);
> plus `Floor_Metal` rust 0.32 → 0.48 to give the metal zone diffuse midtone (§12 asks the
> stair for rust streaks). Final, all six zones on the identical map:
>
> ```
> shot                                     mean    p50    p90    p99  black%  legible%  blown%    sat
> realtex3_Zone_B1_B1_Concrete.png          7.7    3.9   18.7   47.2    34.3      30.4    0.00    7.2
> realtex3_Zone_B2_B2_Wood.png              7.5    4.0   18.2   47.6    35.2      30.2    0.00    5.4
> realtex3_Zone_B3_B3_Metal.png            7.6    3.9   19.2   42.5    34.3      29.8    0.00    8.9
> realtex3_Zone_B4_B4_Gravel.png            8.9    4.6   21.7   61.4    32.2      34.6    0.00    7.8
> realtex3_Zone_B5_B5_Tile.png              9.7    6.0   22.7   55.9    26.9      40.4    0.00    9.4
> realtex3_Zone_B6_B6_Carpet.png            7.2    3.9   19.9   37.5    38.9      32.8    0.00    5.2
> ```
>
> **Crushed is back in band on all six** (26.9–38.9; B6 recovered from 43.1 → 38.9),
> median and blown pass everywhere, and legible passes on five. **B3 금속 sits at 29.8,
> 0.2 under the floor** — the same perennially-marginal metal view this section's history
> shows riding that line (`land_main` 52.3 at one framing, `prodship` 30.2, here 29.8): the
> shared CC0 walls cost ~3 legible pts uniformly and B1/B2/B3 all landed within 0.6 of the
> floor, B3 the one that tipped under. There is no per-zone lever for it that does not
> touch the other five (the walls are shared; more wall-lift starts flattening the mortar
> detail the swap exists to add), so it is accepted, measured and named, as the cost of
> photoreal walls — not silently shipped. Detail preservation was verified against the
> pre-recovery renders (the shadow lift is sub-2/255 only; the bricks, mortar, plaster
> peel and rust bleed are all intact). Provenance in [ASSETS.md](ASSETS.md).

> 🔴 **This section's headline was "All five zone views are inside all four bands, for
> the first time." It is no longer true, and the regression is recorded here rather
> than deleted.** Re-measured 2026-08-01 with the identical command and viewpoints on
> the identical scene, tag `land_main`:
>
> ```
> shot                                     mean    p50    p90    p99  black%  legible%  blown%    sat
> land_main_Zone_A_B2_Wood.png              6.9    2.6   15.7   62.2    41.1      25.4    0.00    5.6
> land_main_Zone_B_B5_Tile.png              7.4    3.3   16.9   63.5    37.5      28.7    0.00    7.7
> land_main_Zone_C_B4_Gravel.png            7.3    3.1   17.3   69.2    39.5      26.2    0.00    7.2
> land_main_Zone_D_B1_Concrete.png          9.0    6.1   18.9   59.5    20.5      38.5    0.00   10.4
> land_main_Zone_E_B3_Metal.png            12.7    8.4   31.0   49.7    17.3      52.3    0.00   14.5
> ```
>
> | Shot | crushed % (10–40) | legible % (30–75) | median (3–16) |
> |---|--:|--:|--:|
> | Zone A · B2 · 나무 | 33.9 → **41.1 ✗** | 32.2 → **25.4 ✗** | 4.2 → 2.6 ✓ |
> | Zone B · B5 · 타일 | 31.6 → 37.5 ✓ | 33.2 → **28.7 ✗** | 4.7 → 3.3 ✓ |
> | Zone C · B4 · 자갈 | 30.9 → 39.5 ✓ | 34.3 → **26.2 ✗** | 5.1 → 3.1 ✓ |
> | Zone D · B1 · 콘크리트 | 14.5 → 20.5 ✓ | 47.8 → 38.5 ✓ | 7.4 → 6.1 ✓ |
> | Zone E · B3 · 금속 | 17.6 → 17.3 ✓ | 51.9 → 52.3 ✓ | 8.4 → 8.4 ✓ |
>
> **Every zone moved the same direction — darker and less legible — which says a global
> lighting or exposure change, not a per-zone one.** Nothing in the player-model or van
> pass was aimed at lighting, so the cause is upstream of both and is not yet found.
> `Map_FirstSketch_Solo` reproduces these figures exactly, so it is not scene-specific.
> Do not quote the table below as current.

> 🟢 **Found and closed, 2026-08-08 — and it was three causes stacked, which is why no
> single suspect ever fit.** (1) The 08-01 regens went through layout-only entry points
> that save the scene without re-running the atmosphere pass, stripping every decal and
> glow — the distributed mid-tone pool in all zones at once; the committed scene had
> been a bare 1-light layout since 08-05, so the shots were grading a building that was
> not the game. (2) `NightAtmosphere.AmbientGain = 0.62`, tuned that day against the
> torch-OFF lock with the torch-ON bands never re-measured. (3) The production texture
> pass then landed real baked AO, which eats the ambient term — invalidating even the
> corrected gain on the same day it was applied. Fixes, each measured on the artefact:
> the full `MapPipeline` is the only sanctioned regeneration path (the atmosphere pass
> restores 5,900+ decals and the glows); `AmbientGain = 1.15` (the 0.62-era level under
> materials that now occlude — derivation in `NightAtmosphere.cs`); working bulbs are
> chosen **per storey, spatially stratified around the ring, count falling with depth**
> (`ScatterSession.LightStratifiedBulbs` — per-bulb dice made one reroll move a storey
> from 16 working lights to 4, and a zone view in and out of this band with no code
> change); and the deep floors got the `ZoneIdentity` rows they never had (병동, 수몰층,
> 굴착층 — B6 measured 8.4 % legible with none). Measured, tag `prodship_`, all six
> zones, **all 18 measures in band for the first time since `land_main`**:
>
> ```
> shot                                     mean    p50    p90    p99  black%  legible%  blown%
> prodship_Zone_B1_B1_Concrete.png          8.8    6.4   17.8   45.5    20.1      42.7    0.00
> prodship_Zone_B2_B2_Wood.png              8.7    6.2   19.1   47.2    26.7      40.2    0.00
> prodship_Zone_B3_B3_Metal.png             7.2    4.4   17.9   41.7    32.7      30.2    0.00
> prodship_Zone_B4_B4_Gravel.png            9.8    6.5   22.5   54.6    24.1      44.0    0.00
> prodship_Zone_B5_B5_Tile.png             12.9   10.9   26.7   52.5    21.1      59.2    0.00
> prodship_Zone_B6_B6_Carpet.png           17.4   13.4   42.0   66.3    29.6      58.0    0.00
> ```
>
> Two cautions carried forward. B3 sits 0.2 above the legible floor — inside, with no
> margin. And these six viewpoints sample a seeded building: the stratified bulbs
> removed the worst of the variance (three consecutive regenerations had left B2's
> frame byte-identical while every constant around it swung ×2), but a future reroll
> can still move any single frame by a few points. Judge a regression by all six moving
> together, which is what this section's own history says a real one looks like.

**Superseded — the reading when this section was written.** Measured
2026-08-01 on `Shots/final_*`, which is the same command and the same viewpoints as
the `map_*` figures it supersedes — only the tag differs:

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -silent-crashes -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag final
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'final_Zone_*.png'
```

```
shot                                     mean    p50    p90    p99  black%  legible%  blown%    sat
final_Zone_A_B2_Wood.png                  8.0    4.2   18.6   62.9    33.9      32.2    0.00    4.5
final_Zone_B_B5_Tile.png                  8.1    4.7   17.7   63.5    31.6      33.2    0.00    8.4
final_Zone_C_B4_Gravel.png                8.5    5.1   18.2   69.2    30.9      34.3    0.00    9.2
final_Zone_D_B1_Concrete.png             10.0    7.4   18.9   59.5    14.5      47.8    0.00   12.4
final_Zone_E_B3_Metal.png                12.6    8.4   31.0   49.7    17.6      51.9    0.00   13.9
```

Four misses to none. Zone by zone, against `map_*` on the same scene and seeds:

| Shot | crushed % (10–40) | legible % (30–75) | median (3–16) | blown % (<0.5) |
|---|--:|--:|--:|--:|
| Zone A · B2 · 나무 | **40.6 ✗ → 33.9** ✓ | **25.9 ✗ → 32.2** ✓ | **2.9 ✗ → 4.2** ✓ | 0.00 ✓ |
| Zone B · B5 · 타일 | 36.1 → **31.6** ✓ | **29.2 ✗ → 33.2** ✓ | 3.4 → **4.7** ✓ | 0.00 ✓ |
| Zone C · B4 · 자갈 | 38.0 → **30.9** ✓ | **26.6 ✗ → 34.3** ✓ | 3.3 → **5.1** ✓ | 0.00 ✓ |
| Zone D · B1 · 콘크리트 | 17.7 → **14.5** ✓ | 40.4 → **47.8** ✓ | 6.2 → **7.4** ✓ | 0.00 ✓ |
| Zone E · B3 · 금속 | 17.0 → **17.6** ✓ | 52.5 → **51.9** ✓ | 8.4 → **8.4** ✓ | 0.00 ✓ |
| spawn0 | 37.8 → **27.6** | 26.4 → **31.8** | 3.3 → **5.2** | 0.00 ✓ |
| spawn1 | 40.3 → **26.3** | 26.2 → **31.3** | 2.7 → **5.2** | 0.00 ✓ |
| spawn2 | 34.9 → **17.1** | 32.6 → **43.1** | 4.1 → **6.5** | 0.27 ✓ |
| spawn3 | 27.2 → **23.1** | 41.1 → **46.1** | 6.1 → **6.9** | 0.00 ✓ |

None of that came from the grade, and **none of it could have.** §3.13 is the
measurement that says so, and it is the most important thing on this page.

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

Writes five things, not one:

| Output | What | Contract |
|---|---|---|
| `<Material>/*.png` | albedo / normal / roughness / AO / metallic-smoothness, 1024² | `Textures.manifest.json` |
| `Detail/*.png` | five shared micro-normals, 512², §3.9 | same, `detail` per material |
| `Decals/*.png` | eight placed marks, 512², RGBA, §3.10 | `Decals.manifest.json` |
| `Glow/*.png` | two additive light sprites, 256², §3.11 | same, `glows` |

Then bind them. Two commands, and the second is not optional — a material that
silently loses its detail normal renders as a slightly softer floor and nothing
reports it:

```bash
Unity -batchmode -quit -nographics -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.TextureImport.ProceduralTextureMaterials.Build
Unity -batchmode -quit -nographics -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.Rendering.MaterialDetailPass.Batch
```

`MaterialDetailPass` also runs from `AtmosphereSetup.Configure`, so the map pipeline
cannot skip it. The decals and the glows are placed by the atmosphere pass — §2.3.

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
pass that knows what the air is supposed to look like — and, since this pass, because
it is the only one that can see what the other two left behind. The atmosphere pass
now does five things per `Map_` scene, in this order, and the order matters:

1. ambient, fog and skybox for the §07 tier;
2. shadow casting on every fitting (§3.8b);
3. **contact decals** (§3.10) — read off the baked NavMesh and the props;
4. **practical glows** (§3.11) — only for fittings that step 2 left switched on;
5. **zone identity** (§3.12) — re-skin before the scene is saved.

Nothing here needs a separate command and nothing here can be forgotten after a
regeneration, which for a decal or a glow would mean it simply is not there with
nothing in any log to say so.

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
night gets worse" as a picture rather than as a table. It takes one more flag:

```bash
… -executeMethod HorrorGame.EditorTools.Rendering.AtmosphereSetup.ShotBatch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag lit -atmoTier 0 -litZones
```

`-litZones` switches every fitting on **in memory only** and re-places the glows for
them. It is not a cheat for a prettier screenshot: it is the counter-example that says
whether a dark frame is a grading problem or a missing-light problem, and §3.13 is
what it found.

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

### 3.9 Grain, and where the texel density went — `Grain` in `gen_textures.py`

The scale correction in §3.8a did two things and only one of them was noticed. It
halved the world size of every tile, which is the fix. It also **doubled texel
density**, from 284 px/m to 410–683 px/m, and nothing in the set had anything up
there to resolve.

Measured rather than judged. `spectral_shares` reports, per material, the share of
its albedo's contrast energy below two cycles per *tile* — the blob that makes the
repeat legible — and above 25 cycles per *metre*, which is grain the near field can
see, 4 cm features and finer. Before:

```
material                       px   size   px/m   AOcon  blob% grain%
Floor_Concrete               1024   2.5m    410   0.037  15.0%   7.9%
Floor_Metal                  1024   2.0m    512   0.146   2.4%   9.7%
Wall_Plaster_Stained         1024   2.0m    512   0.210  54.4%   6.7%
Wall_Concrete_Bare           1024   2.5m    410   0.196   8.7%  36.8%
```

`Floor_Concrete` is the argument in one row: the softest texel density in the set,
the least grain, 15 % of its energy at tile scale, and an AO contrast of 0.037
against a failure threshold of 0.030. It was a photograph of clouds — and it is zone
D's floor and most corridors.

Why it matters at all: at one metre a 1920-wide frame at §05's 90° FOV resolves about
1220 px/m, and §05 puts the camera 1.63 m above a floor it looks at from about a
metre. Everything between 0.3 mm and 4 mm was simply absent from the surface a player
spends the match staring at.

Two layers, both authored in **millimetres of world** so the same call gives the same
physical grain on a 1.5 m gravel tile and a 2.5 m slab:

- **`Grain`, in the base maps** — 5–17 mm features, 0.2–2.0 mm of relief, added to the
  height field *in metres* with the height scale re-derived afterwards, so the normal
  stays a true slope and the reported `relief_mm` stays honest. Clipping into [0,1]
  instead would have quietly flattened the peaks of every deep surface in the set,
  gravel worst. It also modulates albedo and roughness a little: grain is where dirt
  collects and where a specular lobe breaks up.
- **`DETAILS`, five shared micro-normals** at 512², tiled every 22–45 cm — 1138 to
  2327 px/m, above what the eye resolves at one metre. Sand, aggregate, timber fibre,
  brushed steel, ceramic glaze; 1.6 MB for the set, shared by twelve materials. Pure
  grain on purpose: grain has no landmark, so a 30 cm repeat of it is invisible where
  a 30 cm repeat of anything recognisable would be unbearable.

After:

```
Floor_Concrete               1024   2.5m    410   0.105  13.8%  15.0%
Floor_Metal                  1024   2.0m    512   0.150   2.4%  10.2%
Wall_Brick_Painted           1024   1.8m    569   0.938   1.4%  20.3%
Wall_Concrete_Bare           1024   2.5m    410   0.214   7.7%  44.2%
```

Concrete's grain share doubled and its AO contrast went up 2.8×. Worst seam ratio
across the set moved 0.94 → 0.96 against a 1.10 limit, which is what putting more
energy into the high band costs and is still well inside.

This is the `_DetailNormalMap` that §7.9 called unreachable. It is reachable, from a
second pass over the generated materials — `Editor/Rendering/MaterialDetailPass.cs` —
which is a different file from the binder and only ever sets properties the binder
does not touch. The keyword is the part that fails silently: URP compiles the whole
detail block out unless `_DETAIL_MULX2` is on, so the map binds, the inspector shows
it, and the surface renders as though it were never assigned.

**Two things in the set are still wrong and are left as measured facts.**
`Wall_Plaster_Stained` carries 53.9 % of its albedo energy at tile scale — much of
that is the rising-damp gradient, which is *correct* authoring for a band that tiles
in one axis only, but not all of it. And `Trim_Skirting_Painted` runs at 4655 px/m —
1024² over a 0.2 m cross-section, 20× more texels than the eye can use, and a 256² away
from being fixed.

The soffit is the one surface with no detail normal, deliberately: nothing is ever
near a ceiling. §05 puts the eye at 1.63 m under a 3 m soffit, so the closest approach
is 1.4 m and it is straight up — and a ceiling is a large share of the screen in every
corridor, which made it the most expensive place in the game to sample two extra
textures for detail nobody can reach.

### 3.10 Decals — `ContactDecals.cs` and `DECALS` in `gen_textures.py`

A tiling material can say what a surface is made of. It cannot say what has happened
to it *here*, and everything in a used building that says it has been used is
registered to a position: the dirt where a crate stood for ten years, the tongue under
the joint that drips, the pale rectangle where the slab was cut and made good. Baked
into a tiling material any of those appears everywhere at once, which is worse than
not having it — it stops reading as history and starts reading as pattern.

Eight marks, generated at 512², 2.7 MB: contact dirt, water stain, traffic scuff,
screed patch, puddle, drip spatter, soot, rust bleed.

**One rule about the channels.** Every decal's RGB is painted across the whole texture
and only its alpha is shaped. A decal drawn as "colour where opaque, black where not"
bleeds that black into the colour channels at every mip level and then wears a dark
halo that gets worse with distance — in the one place a decal must not draw attention
to itself.

Placement runs last, inside the atmosphere pass, off geometry the earlier passes left
behind:

- the **baked NavMesh triangulation**, sampled area-weighted, for anywhere a person
  can stand. It is the right source and not a convenience: a wear mark taken from it
  is on a route by definition, and none of them land inside a wall, under a crate or
  on top of a stair nosing.
- **raycasts sideways** from those samples, for wall stains and for the grime line at
  the foot of a wall.
- **every point light**, for the burn halo on the soffit above it.

Which mark lands where is decided by reading the material actually under the sample,
which makes the mix a §12 zone cue as well as a plausibility check: water collects in
the voids of 자갈 and on 타일, a screed repair is cut into 콘크리트 and nowhere else, and
자갈 takes almost no polish, because ballast cannot be polished.

**The wall/floor junction is where this pays, and that is not what was expected.** The
map has **ten loose props in it.** Every crate, panel and conduit run a frame shows is
modelled into the kit piece, so "dirt under the thing that was standing there" had
almost nothing to attach to — five props settled, five were correctly rejected as not
standing on anything. The wall base has hundreds of metres of junction, it is where a
brush never reaches in any real building, and it is the line the eye uses to decide
whether a room has a floor or is a box with a texture on the bottom.

**Mesh decals, not URP decal projectors.** A projector needs `DecalRendererFeature` on
`HorrorGame_URP_Renderer.asset` and costs a screen-space pass every frame whether a
decal is visible or not. These are quads lifted 1.2 cm off their surface, merged into
one mesh per kind per storey, drawn through URP's ordinary transparent path and
therefore **lit by the flashlight** — which is the whole point. A stain the beam does
not find is not a clue.

**One bug worth writing down, because it will happen to the next person.** URP 12
added `_BlendModePreserveSpecular`; it defaults to **on**, and it silently redefines
what "alpha blend" means. The factors become `One / OneMinusSrcAlpha` and the shader
is expected to premultiply the colour by alpha itself, under `_ALPHAPREMULTIPLY_ON`.
Setting `SrcAlpha / OneMinusSrcAlpha` and turning that keyword off — the obvious thing
— produced neither: URP's material post-processor re-derived the factors from
`_Surface` on import and put `One` back, while the keyword stayed off. The result is
`dst·(1−a) + src·1`, which *adds* the decal's full colour everywhere its alpha is
zero. On screen that is a bright rectangle exactly the size of the quad, on every wall
in the building. It looks like a broken texture and it is a broken blend.

### 3.11 Practicals that read as sources — `PracticalGlow.cs`

§03 makes darkness the lock and light the key, and §04 sells zone lighting to the
정비공 as an ability with a material cost. Both assume a player can look down a
corridor and see *that there is a light there*. A point light cannot say that: it puts
a disc on the floor and leaves the source invisible, so the room reads as lit by
nothing and a fitting that could be switched on looks identical to one that could not.

URP 17 has no volumetric fog, so there is no setting for this (§7.10). The
alternatives are a light cookie — which URP wants as a *cubemap* for a point light,
and every fitting here is a point light — or geometry. Two pieces of geometry:

- a **filament halo**, three crossed quads at the bulb, driven at 1.35× the fitting's
  own colour so its core sits above the bloom threshold. Static crossed quads rather
  than a billboard because a billboard needs a component running every frame on 123
  objects, and for a radially symmetric sprite the two are indistinguishable.
- a **shaft**, two crossed quads hanging below wherever there is 1.6 m of clear drop,
  at 0.30×. First tried at 0.16×, where they were placed, counted, reported — and
  could not be found in the frame at all. Invisible is the other failure mode and it
  costs the same number of triangles.

Both are additive, and **the falloff is in RGB, not alpha**: an additive blend is
`src + dst` and never reads the alpha channel, so a sprite carrying its shape in alpha
renders as a solid rectangle of light. Batched by kind, storey and colour, so §12's
five zone tints cost five materials rather than 123.

Only fittings that are actually switched on get one. A dark bulb that glows is worse
than no bulb: §04's ability is "pay to turn this zone on", and it is meaningless if
the zone already looks lit.

### 3.12 Zone identity — `ZoneIdentity.cs`

§7.3 said: *"Five zones, five floors, and the floors genuinely do read now. Everything
above knee height does not: the same brick wall, the same square hall, the same single
central pillar."*

Each zone's rooms now carry their own walls, dado and soffit. Keyed on the §12 **floor
material** rather than on the zone letter, because letters come from the layout seed
and move with it while the five 바닥 재질 are a contract that does not. Corridors, which
live under `Map/Shared`, keep the base brick deliberately — a building where the
corridors also change colour has no places in it, only gradients.

| Zone | Walls | Why |
|---|---|---|
| 기록보관소 · 나무 · B2 | limewashed cream, chalky, ×1.22 | a dry paper store — and the darkest view in the building, so the one that most needed the light |
| 저수조 · 타일 · B5 | institutional glazed green, gloss kept | the only wall in the building that is wiped down, so it takes a specular streak |
| 저탄장 · 자갈 · B4 | damp limewash gone cold blue, ×1.10 | see below |
| 하역장 · 콘크리트 · B1 | **`Wall_Concrete_Bare`** — board-formed, snap-tie holes | the only zone that changes *material*. It was generated long ago and left unbound, with the note "awaiting a per-zone wall slot" |
| 기계실 · 금속 · B3 | oil-stained ochre, ×1.08, deeper relief | the warmest zone in the building, against 저수조's green two storeys down |

It costs 15 material assets and nothing at runtime: every variant points at the same
textures, so the batcher still batches and no extra texture memory is used at all.

**저탄장 had to be argued with three times.** A coal store is dark, and this view was
already the nearest in the building to the 40 % crushed ceiling. Darkened 12 % with
15 % more occlusion it measured 49.8 % crushed and 20.1 % legible; pulled back to 3 %
darker, 42.3 % and 23.7 % — still worse than leaving it alone. It is the one zone that
cannot afford to look like what it is, so its walls now go the other way: limewash,
cold and pale, 30.9 % and 34.3 %. Its identity is carried by 45 mm of ballast
underfoot, which is unmistakable at any brightness.

The test is the one the work was set: render one frame per zone, shuffle them, and try
to name them. All five are namable from the still — ballast under a corridor open to
the night sky; warm cream over plank; cold green over small tile; board-formed
concrete over a big slab; ochre over diamond plate.

### 3.13 **The building has one working light in it**

This is the most important measurement on this page and it is not an art finding.

```
Light components in Map_FirstSketch.unity      123
of which m_Enabled: 1                            1
```

122 of those are §04's zone lighting, which is *correctly* off until the 정비공 pays
for it. The caged bulbs that used to be lit belong to the dressing pass — and **the
dressing pass's output is not in the scene as saved**: there is no `Map/Dressing`
root and no reference to `Assets/Models/Dressing` anywhere in the file. §3.6 on this
page describes "16 fittings across 2469 m²"; there is one.

That is the whole of the luminance regression this document recorded as an art defect
in its previous edition. The three-storey map was measured with fittings in it and the
five-storey map was being measured without any.

**No grade can recover it, and that is arithmetic rather than opinion.** Tonemapping
and colour grading are multiplicative. Multiplying a wall lit by 0.005 of ambient does
not make it legible; it makes it a slightly less black wall. The counter-example is
one command — nothing is saved, the zone lights are switched on in memory only:

```bash
… -executeMethod HorrorGame.EditorTools.Rendering.AtmosphereSetup.ShotBatch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag lit -atmoTier 0 -litZones
```

```
                              crushed %   legible %
Zone A · 나무    unlit → lit    38.2 → 1.5   27.7 → 96.6
Zone D · 콘크리트 unlit → lit    14.5 → 0.8   47.5 → 96.0
Zone E · 금속    unlit → lit    17.6 → 0.1   51.9 → 99.4
```

So the band is bracketed rather than solved: **one** fitting gives 25–52 % legible and
three of five zones under the floor; **123** gives 96–99 % and a building with no dark
in it at all. The right answer is neither and it is not a grading decision — it is the
dressing pass's caged bulbs, at ART.md's own stated density of one in five. Until they
come back, every luminance number in this document is a number for a building with its
lights off.

It is also the first picture anyone has taken of what §04's ability actually buys, and
it is worth looking at: `Shots/d4lit_Zone_E_B3_Metal.png`.

### 3.14 The monster — a rim, two eyes, and less fog than the room

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

Map: `Assets/Scenes/Map_FirstSketch.unity` as saved, seed 1204, §07 tier 0 — and
with **no dressing in it at all**, which is §7.0 and is the single most important
qualifier on every number below. Rendered with

```bash
… -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag final
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'final_*.png'
```

`map_*` is the same command run on the same scene before any of §3.9–3.13, so the
two are a controlled comparison. The shot tags on this page, oldest first:
`real1` → `real8` (three storeys) → `map` (five storeys, before tonight) →
`final` (five storeys, after), plus `d4lit` for §3.13's counter-example.

The per-shot table is at the top of this page under **Measured targets** and is not
repeated here. In one line: **four band misses became none**, and §3.13 is why that
was possible at all and what it does not mean.

**spawn2 is outside the band by design and always was.** It stands at the surface by
the vehicle; a lit loading yard is not what a 10–40 % crushed band describes.

### Frame cost

```bash
… -executeMethod HorrorGame.EditorTools.Rendering.FrameCost.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -costTag final
```

1920×1080, MSAA 4×, §07 tier 0, on the M1 Pro. Median of 40 timed frames per
viewpoint after 12 warm-up frames, each frame followed by a one-pixel read-back so
the timer measures the render rather than the submission. This is the renderer's
share **in the editor** — no physics, animation, networking or UI — so read it as a
before/after against itself and not as a player frame rate.

Measured four times tonight on the five-storey map, which is why the attribution below
is a measurement rather than a guess:

| | typical | worst view |
|---|--:|--:|
| before any of §3.9–3.13 | 6.39 ms (156 fps) | Zone A, 9.32 ms (107 fps) |
| + grain, detail normals, decals, glows, zone skins | 8.05 ms (124 fps) | Zone A, 10.65 ms (94 fps) |
| − 93 decal quads (504 → 411) | 7.54 ms (133 fps) | Zone A, 10.44 ms (96 fps) |
| − the soffit's detail normal | **7.33 ms (136 fps)** | Zone A, **10.28 ms (97 fps)** |

**+14.7 % typical and +10.3 % on the worst view, against a +26 %/+14 % peak.** Both
cuts were made because they were measured, not because they were guessed:

- 93 fewer decal quads bought 0.51 ms, so decals cost about 5.5 µs each in frame. The
  93 came from the two marks with the worst value per quad — the drip spatter, at
  3.5 % coverage and 0.016 mean alpha, and half the wall-base grime. Comparing the two
  renders, the grime line is thinner and the frame is not worse.
- Clearing the ceiling's detail normal bought a further 0.21 ms, which is what two
  texture samples per pixel cost on a surface that fills the top third of every
  corridor. Nothing is ever near a soffit, so nothing was lost. `MaterialDetailPass`
  had to learn to *unbind* for this: a detail declaration that could only ever be
  added is a dial with one position.
- The remaining ~0.9 ms is the detail normals on the other eleven materials, and that
  is the price of §3.9. It is per-pixel and it is what the near field is made of.

Against the last figures this page published — 8.89 ms typical and 20.25 ms on the
worst view, measured on the three-storey map — the current build is faster on both,
so nothing here has spent the budget that table was worried about.

The dial, if it ever needs turning, is `SquareMetresPerWallBase` in `ContactDecals`
(11 → 22 tonight, and 30 would still leave the junction reading) and then
`CastShadowsFromEveryFitting` in `AtmosphereSetup`, which is the expensive one and is
the price of a mechanic rather than of a picture — see §3.8b.

---

## 7. What still needs work

Honest list, worst first. **§7.11 is closed** — it was missing from this page entirely
until 2026-08-01, it was the most visible defect in the game, and the interactables now
use the models `gen_props.py` had already generated. It is kept here rather than deleted
because of how it survived review: it looked like a plausible white box in every editor
screenshot on this page and rendered magenta in the player build.

### 7.0 The dressing pass's output is not in the saved map

**One of the 123 lights in `Map_FirstSketch.unity` is switched on**, there is no
`Map/Dressing` root in the scene and no reference to `Assets/Models/Dressing`
anywhere in it. Ten loose props in a 2469 m² building.

Everything this page said about the map being too dark was this. Everything it says
now about the map being in band was measured with the lights off, so the band will
have to be re-judged once they are back — probably downward, and that is the good
direction to have to move in.

It is also why §3.10's contact dirt found five props to settle and why §3.11 draws one
filament halo. Both passes are written against however many there turn out to be.

Not this area's to fix: `MapPipeline.Regenerate` runs layout → dressing → atmosphere
and would put it back, but running it re-rolls the §12 layout and re-bakes the NavMesh,
which is not a thing to do at 3 a.m. underneath four other passes' measurements.

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

### 7.3 ~~Every room is the same room~~ — half closed

The walls are done: §3.12 gives every zone its own dado, soffit and wall colour, and
하역장 its own wall *material*. Shuffled and unlabelled, all five zone frames are
namable.

What is left of this is the half that really is a map-kit problem, and it is worth
stating precisely so nobody re-solves the colour: **the same square hall and the same
single central pillar.** Every zone's 개방 공간 is a box with one column in it. Colour
separates places; silhouette is what makes them memorable, and no amount of paint
substitutes for one architectural feature per zone — a gantry, a tank, a stair that
crosses the room, a run of racking.

### 7.4 Sky is still visible from some lower-storey corridors

`BuildCeilingCaps` covers zone rects. Corridor cells outside a zone rect on B2/B3 are
still open to the sky where nothing is above them.

**Re-confirmed 2026-08-01 on `final_*`, and it is worse-framed than this said.** It is
not an edge-of-frame artefact any more: in `final_Zone_C_B4_Gravel.png` the sky is a
bright blue rectangle at the **vanishing point of the corridor**, dead centre, and it
is the brightest thing in a frame whose whole subject is a flashlight on gravel. Four
storeys underground, looking down a brick tunnel, at daylight. The store pass captured
the same thing independently — `docs/store/defects/S5_sky_visible_from_B3.png`.

**The reason recorded here for leaving it is now void.** It said verification was
impossible "with the NavMesh already broken"; the NavMesh is not broken — the audit
reads `complete 1830 (100.0 %) · islands 1 · monster reach 19/19`
([STATUS.md §1.5](STATUS.md)), and [B-001](BLOCKERS.md#b-001) has been closed since. So
the work is: extend the cap to tile cells, extend the stairwell guard so capping does
not seal a stairwell, regenerate, and re-run the NavMesh audit to prove the guard held.
That is a normal change with a normal gate on it, and nothing is blocking it.

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
building. It is coherent, and it is monotonous over a match of any length — 7.2 minutes
is the measured median ([F-006](BALANCE-FINDINGS.md#f-006)) and §01 asks for 25–35, and
the complaint gets worse in proportion to which of those two you believe. §07 ramps
brightness and vignette across the night but not hue; there is room for the last tier
to go somewhere the first tier does not.

### 7.9 ~~What surface variation cannot do from the texture side~~ — two of three closed

Two of the three were not blocked, they were unattempted, and the reason recorded here
for both — "that file belongs to another area" — was wrong in the same way twice. The
*materials* are a generated artefact; a second pass over them is not an edit to the
binder.

- **Detail normals** — done, §3.9. `Editor/Rendering/MaterialDetailPass.cs`.
- **Decals** — done, §3.10. `Editor/Rendering/ContactDecals.cs`. They did not need the
  URP decal renderer feature and they did not need the dressing pass's cooperation;
  they needed the *result* of the dressing pass, which is a different thing and is
  sitting in the scene by the time the atmosphere pass runs.
- **Stochastic or triplanar blending between clean and damaged** — still open, and now
  the only one. It is the proper answer to repetition. `detile` divides out each tile's
  low-frequency luminance envelope so the repeat has no blob to lock onto, and the
  decals break the plane up where they land, but a wall is still literally the same
  wall every 1.8 m and no texture-side trick changes that.

  What the measurement now says about where it would pay: `Wall_Plaster_Stained` at
  53.9 % blob share is the worst offender in the set, and it is the dado band on every
  wall in the building.

### 7.10 ~~Light shafts~~ — done, and they are the cheap version

§3.11. Two crossed additive quads per fitting where there is 1.6 m of clear drop, not
volumetrics — URP 17 still has none, and the light-cookie route still wants a cubemap
for a point light.

What that leaves open is honest: these are *static* geometry, so a shaft does not move
when the light does and does not disappear when something is put in front of it. Every
fitting in this building is fixed to a soffit, so neither has come up. A carried lamp
or a swinging bulb would need the same thing as a component instead, which is a
runtime script in a different assembly.

They are also invisible until somebody switches the lights on — §3.13.

### 7.11 Every object the player touches was an untextured white primitive — FIXED

**This was the worst-looking thing in the game and this page had never mentioned it.**
It was found by the store pass, which could not avoid it — three of the four trailer
beats worth cutting are *about* these objects — and filed under
`docs/store/defects/` rather than here, so the art register never saw it.

`Interactable.CreateProp` built every interactive object with
`GameObject.CreatePrimitive` and a self-lit material tinted by colour alone:

| Object | Section | Was | Is now |
|---|:--:|---|---|
| 단서 clue | §03 | Quad | `Clue_LedgerStand`, 1.15 m |
| 목표물 objective | §03 | Capsule | `Objective`, 0.60 × 0.41 × 0.40 m |
| 전리품 loot | §08 | Cube | the seven `Loot_*` models, 2.2 cm to 1.63 m |
| 금고 safe | §08 | Cube | `Safe_Closed`, swapping to `Safe_Open` when it pops |
| 차량 vehicle | §08 | Cube | `Vehicle`, 2.81 × 2.94 × 6.69 m |

The evidence captures are still under `docs/store/defects/`; they are now history.

**It was also worse in a build than in the editor, which is why it survived review.**
`CreatePrimitive` takes its material from `RenderPipelineAsset.defaultMaterial`, and
URP only answers that in the editor — in a player it returns null and Unity falls back
to the built-in `Default-Material`, whose `Standard` shader is not in a URP build's
shader set. Every one of these rendered as a plausible white box in every editor
screenshot on this page and as Unity's error magenta in the build the owner played.
Nothing spawned at runtime may resolve a shader by name for the same reason: a shader
no material asset references is stripped, and `Shader.Find` returns null with no error.

**What it took.** `tools/blender/gen_props.py -- --manifest-only` writes
`Props.manifest.json` with the values the Principled BSDF was authored with — FBX
carries no metallic and every one of the 21 prop materials was importing at metallic 0,
which is why the mirror-grade 은수저 was grey plastic. `PropMaterials` rebuilds them as
URP Lit assets and remaps the 25 FBX importers; `InteractablePropLibrary`, a
`ScriptableObject` under `Resources/`, is what puts the models in the build.

**One override of the generator, and it is an art decision.** Prop roughness is floored
at §7.6's own **0.40**. §05 holds the torch at the eye, so light and view arrive from
the same direction; loot lying flat on a floor is then seen at a grazing angle and a
near-mirror throws its whole lobe away from the camera. Measured under the game's beam
at 2 m: mirror-grade silver spoons (authored roughness 0.16) rendered as a black smudge
on lit gravel, which is the exact opposite of §08's "눈에 잘 보임 (유혹)". The real fix is
a reflection probe indoors; until there is one, a metal with nothing to reflect must not
be a mirror.

**And they light up when the crosshair is on them.** `InteractableHighlight` drives
`_EmissionColor` through a `MaterialPropertyBlock` — every prop material carries the
`_EMISSION` keyword with a black colour so the dial exists. The value is 0.10/0.073/0.035
and it is photographed, not guessed: four times that flattened the 금고 into a
featureless tan cut-out with its brass dial gone. §03 forbids a HUD marker, so the
object itself is the only place the cue can live.

**Photographed with `PropShot`**, which frames each prop under the real beam at 2 m and
5 m from its *surface*:

```bash
Unity -batchmode -quit -silent-crashes -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.Props.PropShot.Batch -shotTag props
```

Run it WITHOUT `-nographics`. It also stands up any model the current seed did not
place, so the 궤짝 is photographed on a seed that drew the 초상화.

### 7.12 ~~The 차량's body is a 90 % metal with an albedo of 0.11 — it renders as a hole~~ — MATERIAL FIXED

> **Closed as a material, not as a vehicle.** `Prop_VanBody` and `Prop_VanLower` now
> exist as URP Lit assets at metallic 0 and the van reads as painted steel instead of a
> hole. `Prop_Iron` was left alone, as this section asked. The mesh under the new paint
> is still two cuboids and four cylinders with no windscreen, grille or wheel arches —
> **see §7.13**, which is where the van's remaining problem lives.

§7.11 gave the 차량 a real 6.69 m model and left the last thing about it unfixed: its
body material is **`Prop_Iron` — albedo (0.112, 0.116, 0.122), metallic 0.90,
roughness 0.55**. A surface that metallic has almost no diffuse term, so it returns
essentially nothing to a point light; what little it does return is a specular lobe
that has nothing to reflect, because §7.9's argument applies here too — there is no
reflection probe indoors.

The result is measured rather than felt. `Shots/apron5/21_bay_from_ten_out.png` and
`41_the_van_rear.png` were taken after the van was given **five** of its own warm
sources — two headlamps, a rear work lamp standing 1.1 m clear of the doors, a bay fill
lamp 1.5 m above the roof and 2.4 m off its flank, and a roof beacon — and it is a
black silhouette in both. Its *shape* reads perfectly: cab, load box, wheels, loading
ramp. Its *surface* does not exist. The brightest thing on it is the cab glass.

This matters more than the other props on this page. §08 makes the 차량 the 안전 지대,
the 상점 and the 보급소 in one object and §01 sends the team back to it 2.94 times a
match; `SurfaceApron` now parks it in the 하역 베이 and lights it precisely so that it
is the thing a surfacing player sees first (STATUS §4.6).

**The fix is an asset decision and belongs in `tools/blender/gen_props.py`, not in the
runtime.** A works van is painted, not bare iron: it wants its own material — something
near `Prop_Paint`'s albedo (0.221, 0.172, 0.132) at **metallic 0** and roughness ~0.6,
assigned to the body while the chassis, wheel arches and ramp keep `Prop_Iron`.
Changing `Prop_Iron` itself is not the fix; it is shared with the 금고, the conduit and
every other iron thing in the game, all of which are meant to look like iron.

Do not fix it by tinting the material at runtime. §7.11's whole lesson is that the
material the player sees has to be an asset the build contains.

### 7.13 The two art passes landed their materials and did not land their meshes

**Both passes did what they set out to do, measured against the thing they were asked
to measure, and the result still does not look like a game somebody would pay for.**
That gap is the whole content of this section, and it is here because both passes are
otherwise reportable as successes: §7.12's van paint exists as an asset, the player is a
validated Humanoid, and `AssetImportValidator` passes 86 models with 0 failing.

Measured 2026-08-01. Reproduce with:

```bash
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -quit -silent-crashes -projectPath unity/HorrorGame \
   -executeMethod HorrorGame.Gameplay.PlayerEditor.FirstPersonHandsShot.Batch -shotTag land_hands
$U -batchmode -quit -silent-crashes -projectPath unity/HorrorGame \
   -executeMethod HorrorGame.Gameplay.PlayerEditor.PlayerBodyShot.Batch -shotTag land_body
$U -batchmode -quit -silent-crashes -projectPath unity/HorrorGame \
   -executeMethod HorrorGame.EditorTools.Props.PropShot.Batch -shotTag land_prop
$U -batchmode -quit -silent-crashes -projectPath unity/HorrorGame \
   -executeMethod HorrorGame.EditorTools.Playtest.GuidanceShot.Batch -shotTag land_guide
```

**Read `land_guide_van.png` and `land_guide_surface.png` first.** They are 1920×1080 and
brighter than the gameplay captures, so they are the two frames where the hands can
actually be judged. The 1280×720 `land_hands_*` set hides all of this.

#### The hands are the worst thing in the frame, and they are in every frame

`gen_player_ai.py` cuts them out of `monster_vessel_base.glb` — the flayed vessel the
file's own docstring calls *"something that used to be a person"*, with *"torn skin at
every joint"*. That decision is defensible on proportion and it is what produced the
surface:

| What the render shows | Where |
|---|---|
| Fingers fused into a paddle — no knuckles, no nails, no creases, no fingertips | both hands, every state |
| A stippled, lumpy displacement that reads as raw meat or a knitted mitten, not skin | both hands |
| **A hole at the right wrist** — the hand and forearm do not join; you can see through the seam to a dark void | `land_guide_van.png`, right hand |
| Forearms are bare, untextured, visibly faceted low-poly tubes — no sleeve, no cuff, no coverall | both arms |
| The first-person arms are bare skin; the third-person body wears a coverall. **The two do not match.** | `land_guide_*` vs `land_body_03m.png` |

`gen_player_ai.py` already has a guard that fails the harvest if the hand *"is a tube,
so it is a mitten"*. It passes, and the shipped hand still reads as a mitten. The guard
is measuring span, and the defect is surface and topology.

#### Nothing is held in any of §03's four carry states

All four render identical empty hands:

| State | What should be in frame | What is |
|---|---|---|
| empty hands, torch off | nothing | nothing ✓ |
| torch in hand, lit | the flashlight | a near-black stub intersecting the hand, no beam cone |
| §08 대형 전리품, both hands | the crate | nothing |
| §03 목표물, both hands | the objective | nothing |

The two-handed states are the ones §03 defines *by* what they cost you, and neither
shows the thing it costs you. `FirstPersonHandsShot`'s own table reports `torch -` for
both, so the tool knows; there is no held-prop renderer in first person to report.

#### 2026-08-01 · re-measured after the hand rebuild — three of the rows above are now stale

The table above was written against the harvested-from-the-vessel hand.
`gen_player_ai.py` now **builds** the hand instead of cutting one out, and
`FirstPersonHandsShot -shotTag night01` plus a flat Blender render of
`Player_Arms` out of `Player.fbx` say this much has changed:

| the old row | what the mesh does now |
|---|---|
| "Fingers fused into a paddle — no knuckles, no nails, no creases" | **fixed.** Five separate digits, knuckles and a thumb that opposes, visible in isolation at `arms_front` |
| "Forearms are bare, untextured, faceted tubes — no sleeve, no cuff" | **fixed.** Coverall sleeve, cuff and role band; `Player_Arms` is 3 376 polys |
| "§08 대형 전리품, both hands → nothing in frame" | **fixed.** The crate is in frame and held |
| "torch in hand → a near-black stub, no beam cone" | **still true.** `FirstPersonHandsShot` reports `torch -` in every state |

#### The hands were invisible for a different reason, and it was measurable

The rebuilt hand is good and nobody could see it. Mean luminance sampled off
`night01_*` at native brightness, no gain:

```
                        left arm   right arm   far floor   near floor
empty hands, torch off       3.9         3.5        52.7          8.3
§08 대형 전리품, both hands    3.7         2.9        52.9          8.0
§03 목표물, both hands        3.7         3.6        53.1          8.2
```

**3.5 of 255 against a floor at 52.7.** §05 asks for 손 and the renderer was drawing
them; a fifteenth of the floor's exposure is not a hand, it is a rumour of one. Every
render before this was read after a 3× brightness gain, which is why five rounds of
looking at the hands never found it — the crop that makes the mesh judgeable is the
crop that hides the defect.

`PlayerHandFill` is the fix: a point light parented to the eye, 0.62 m of range and
**0.055** intensity, which is where it stops lighting anything but the arms.

```
                     left arm   right arm   far floor   near floor
before                    3.9         3.5        52.7          8.3
after                    22.3        31.5        52.9          9.2
```

**The arms move 6~9×; the floor moves 0.2 of 255 and the corridor beyond it not at
all.** §03's lock is still shut — that was the whole constraint, because a lamp at the
camera that reached the corridor would open the objective for free.

Its intensity is not linear anywhere near the working point: 2.2 and 0.40 both land the
arms at 79~92, saturated, and only 0.055 lands them at 31.5. Anyone retuning this should
sweep by an order of magnitude, not by a factor of two.

**The rendering-layer restriction in that component does not currently do anything, and
the file says so.** URP honours a light's `renderingLayers`, but a *renderer's*
`renderingLayerMask` is only read when Rendering Layers are enabled on the renderer
asset, which they are not here. Measured: with the light on the arms' layer alone the
arms went back to 3.5 — the light matched nothing. The confinement that ships is the
0.62 m range. Enabling Rendering Layers on `HorrorGame_URP_Renderer.asset` would make
the mask real and is a project-wide render setting, so it was not flipped at 01:00.

#### What is still wrong with the arms, now that they can be seen

Read `fill06_20_loot.png`. The mesh is good and the framing is not:

- **The forearms are far too large in frame.** Each runs from a bottom corner to
  mid-height; the visible forearm is 4~5× the length of the hand against a human's
  1.4×. The eye sits directly above the shoulders, so the real arms foreshorten into
  long wedges. First-person arms in this genre are usually a separate asset posed for
  the camera rather than the body's own.
- **The hands do not touch what they are carrying.** In the 대형 전리품 state the crate
  is between them and both hands are clear of it. §03 defines that state by what it
  costs you; hands beside the object read as a bug rather than as a burden.
- **The coverall reads as pale hospital white**, not work canvas — the role band on the
  cuff is the only saturated thing on the arm.

None of these are the mesh. All three are pose and framing, which is
`gen_player_ai.py`'s four carry poses and the shoulder-to-eye offset in the rig.

#### The torch does not gate anything

§1 of this page: *"If a room is readable without the beam, the lock is open."*

| | mean | legible % |
|---|--:|--:|
| `land_hands_00_empty.png` (torch **off**) | 10.6 | 40.3 |
| `land_hands_10_torch.png` (torch **on**) | 11.1 | 40.6 |

**Switching §03's flashlight on changes the frame's mean luminance by 0.5 of 255.** The
corridor is equally readable either way. This is the §03 lock standing open, measured.

#### The van is a correctly painted blockout

`Prop_VanBody` and `Prop_VanLower` exist, import as URP Lit, and read as painted
dielectric steel rather than §7.12's black hole. That part worked. The mesh under the
paint is two cuboids and four cylinders:

- No windscreen, no grille, no headlights, no bumper — **the cab has no front at all**.
- No door lines, no handles, no mirrors, no wheel arches.
- Wheels are featureless black cylinders: no tread, no sidewall, no rim, no hub.
- The cab window is a flat pale-lavender rectangle with no frame, no glass and no
  reflection — the cheapest pixels in the shot.
- The specular response is a single soft untextured lobe. §7.9's point applies: **there
  is still no reflection probe indoors**, so "painted" can only read as "slightly
  shinier grey". The paint cannot finish the job the probe was deferred on.

Measured, the van's own frames are also outside every band on this page — `black% 0.5`
and `legible% 96.1` at 2 m against 10–40 and 30–75. The 하역 베이 it is now parked in is
lit like a car park, not like this game.

#### Two defects in the measuring tools themselves

- **`PlayerBodyShot` has never photographed 15 m.** Line 138 clamps the wanted distance
  to `stand.ClearMetres - 1.2`, and the corridor it picks is too short, so the run
  writes `land_body_15m.png` at **10 m** — the filename takes the wanted distance and
  the report table takes the actual. 15 m is `GameConstants.ObserverRange` and the whole
  reason the distance is in the list. Either find a longer run or rename the output.
- **A teammate's body contrast is an order of magnitude under the monster's floor.**
  0.0013 at 3 m, 0.0084 at 8 m, 0.0038 at 10 m, against the 0.015 §6b holds the creature
  to. The figure is legible in the picture because the coverall is bright, not because
  it separates from the wall — and the coverall being bright is its own problem: it is
  brighter than any wall in the building, so a teammate is self-lit.

### 7.14 The hands, the two empty carry states, and why the beam gated nothing

Answers §7.13. Everything below is a change to a **generator or to a renderer**, never a
tint written at runtime — §7.11's lesson holds.

#### The hands are built now, not harvested

`gen_player_ai.py` cut them out of `monster_vessel_base.glb` on the argument that a hand
is all concavity and a hull-and-tube assembly can only make bulges. That argument is
right about hulls and was applied to the wrong thing: **the gaps between fingers are not
concavities in one surface, they are the space between five separate surfaces**, and five
separate lofted solids have them for free. What genuinely defeated the creature was its
flank, which really is one surface with a hollow in it. A hand is not one surface.

`build_hand()` authors it from anthropometry scaled to `HAND_LENGTH`:

| | |
|---|---|
| palm | nine lofted superelliptical sections, wrist to interdigital web, on a **knuckle arch** — the four metacarpal heads are at four different distances out, which is the second thing after fused fingers that makes a built hand read as a mitten |
| web | the dorsal side is pulled back 11 mm at the commissure while the palmar side runs on, because fingers separate at the knuckle on the back of a hand and ~15 mm further out on the palm |
| digits | five, each nine sections on its **own curved centre line** with a rest curl of 9°/17°/12°, so a finger reads as three segments rather than as a taper |
| nails | five closed plates 0.9 mm proud, sized off the distal phalanx. What reads as a nail at 0.35 m is not the plate, it is the hard line round it — `shade_smooth`'s 44° crease |
| relief | four knuckles at 4.0 mm, a thenar at 4.7 mm and a hypothenar at 3.1 mm, applied as one-sided displacements so the silhouette does not balloon |
| thumb | built already opposed: `THUMB_BASE`→`THUMB_TIP`, and `DIGIT_SPLIT` lands its middle joint within 3 mm of a real MCP |
| wrist | a **capped solid**. `verify_hand` fails the build on any edge with other than two faces on it |

1,328 triangles per hand against the harvest's 1,400, and the whole model is 5,254
against a 7,200 cap.

**Two guards were added and one of them is the one §7.13 asked for.** The harvest's own
check — *"the hand is a tube, so it is a mitten"* — measured **span**, passed the whole
time, and shipped a paddle. `digit_clearance` measures the thing that was actually wrong:
closest approach between two digits' **surfaces**, distal of the knuckles, every point of
one against every point of the other. Currently `IM=3.0 MR=3.2 RL=4.3` mm. The other is
the closed-surface check above, which is what the hole at the right wrist needed.

**The grip solver was inverted, and that was forced by the hands being correct.** It used
to close the fist to anatomical limits and inscribe the largest cylinder in whatever
cavity was left. That works on a sculpt whose proximal phalanx is 55 mm — half again what
a 181 mm hand has — because an over-long finger curls into a wide arc with a hole in the
middle of it. On correct proportions a fist closed to the limit is a **fist**: the
fingertips reach the palm and the solver reported a handle 4 mm the wrong side of zero,
which is true. You cannot hold a torch in a clenched fist. So the handle is now placed
where a hand puts one — on the palm, under the metacarpal heads, sized by the hand at
`HANDLE_OVER_HAND` — and each digit's flexion is solved so its own fingertip lands on it.
Per digit, because the four fingers are 60–85 mm long and at a shared angle the short
ones never reach: `Index=86/101 Middle=83/98 Ring=80/94 Little=76/89` degrees, every one
landing within 0.05 mm of the handle's surface.

#### The sleeve is the coverall now, with a role-coloured cuff

§7.13's *"first-person arms are bare skin; the third-person body wears a coverall"* is
closed by `cuff_rings` growing a **cuff band**: two extra rings standing 2 mm proud of
the sleeve with a hard step at each end, painted `M_ROLE`. §04's colour was already on the
helmet, the collar, the vest, the deltoid caps and the bicep bands; this is the sixth
place and the only one the owner also sees.

#### §03's four carry states now hold what they cost you

`PlayerHeldProp` puts the actual model from `InteractablePropLibrary` on
`ObjectiveMount` — the 목표물 for §03's carry, `Loot_LargePiece_Chest` for §08's 대형
전리품 — and nothing for the other two. Never a primitive: §7.11.

It lives in `HorrorGame.Gameplay.Player` and the library lives in `Assembly-CSharp`,
which references every asmdef and is referenced by none, so the lookup crosses that
boundary through one static hook installed by `HeldPropModels`. The lower layer declares
the hole; the upper one fills it. Inverting it would put the component that reads
`PlayerLoadout` every `LateUpdate` in the one assembly the shot tools cannot reference.

#### The lock: the beam was never the problem, the ambient was

§7.13 measured torch-off 10.6 / 40.3 % against torch-on 11.1 / 40.6 %, and read it as the
flashlight failing. **Differencing the two frames shows a clean 44° cone reaching down
the corridor with a lit floor and a lit locker at the end.** The torch works exactly as
specified. It had nothing to reveal, because `land_hands_00_empty.png` — the **torch-off**
frame — shows brick courses, floor cracks, pipework and the far wall all readable.

The cause is in this file's own history. `NightAtmosphere.EarlyEvening`'s remarks record
that its ambient colours were raised ~2.6× in sRGB, about eight times in linear, to
compensate for removing a daytime skybox that had been carrying the room. That correction
was right in direction and roughly twice too far, and **nothing measured the torch-off
frame afterwards** — every band on §1 of this page is measured with the beam on, so a room
that is readable without one passes all four.

`NightAtmosphere.AmbientGain` is the fix: one number, applied in `ApplyEnvironment`, so
the two tiers and the ramp between them keep every note explaining how they were tuned.

**The shot tools now apply the atmosphere from the code rather than trusting the scene.**
Ambient and fog are scene state, so a review frame otherwise photographs whatever
`AtmosphereSetup` last baked — which is exactly how §1's five zone views drifted out of
band with nobody able to name a change.

#### The zone regression's cause, found and not fixed

§1 says *"every zone moved the same direction, which says a global lighting change"* and
that it was measured *"on the identical scene"*. It was not the identical scene.
`Map_FirstSketch.unity` was regenerated twice between the two measurements — `47bc2d8`
(*"Wrote … from seed 1204"*) and `9b75a08` — and **the atmosphere art pass was never
re-run afterwards.** Comparing the same file at `ba3e482` against HEAD:

| | `ba3e482` (the good `final_*` numbers) | HEAD (`land_main`) |
|---|--:|--:|
| Mesh / MeshRenderer / MeshFilter | **44** | **0** |
| Light | 123 | 123 |

The 44 are, by name, the whole output of `AtmosphereSetup.WriteEnvironment`: 42
`Decal_*` meshes from `ContactDecals`, plus `Glow_Point_*` and `Glow_Shaft_*` from
`PracticalGlow`. Corroborating: `CastShadowsFromEveryFitting` would set all 123 lights to
`LightShadows.Hard` and all 123 still read `m_Type: 0`, so the pass has definitively not
run on this scene. It also matches the signature — Zone C's p99 is 69.2 → 69.2 and Zone
D's p90 18.9 → 18.9, i.e. **the beam-lit region is identical** and only the dark tail
collapsed, which is lost mid-tone geometry and not a multiplier.

**Fixed for `Map_FirstSketch.unity` and not for the Solo scene.**
`ApplyEnvironmentToMapScenes` rewrites and re-saves *every* scene whose name starts with
`Map_`, and `Map_FirstSketch_Solo.unity` had another workflow's uncommitted changes in
it. So the pass was run in a throwaway worktree at HEAD and only the one scene copied
back. `Map_FirstSketch_Solo.unity` still has no decals, no glow and 123 shadowless
fittings; on a clean tree the whole thing is one command:

```bash
Unity -batchmode -quit -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.Rendering.AtmosphereSetup.Batch
```

**The shadowless fittings were the other half of the open lock.** All 123 point lights in
the map were authored `LightShadows.None` and `CastShadowsFromEveryFitting` had never run
on this scene, so an 18 m entrance light was lighting the corridor *through the walls*.
Ambient was never going to close a lock that a light shining through brickwork was
holding open.

#### `PlayerBodyShot` no longer lies about where it stood

The clamp was correct and the filename was not. It now names the file after the distance
**taken** and warns with `GameConstants.ObserverRange` and the run length that would be
needed. `land_body_15m.png` was 10 m; the file is now called what it is.

#### What it measures at, and the number that matters is not the frame mean

`FirstPersonHandsShot -shotTag h3`, `frame_stats.py`, against `land_hands` on the same
viewpoints:

| | mean | p50 | black % | legible % |
|---|--:|--:|--:|--:|
| before, torch **off** | 10.6 | 6.1 | 27.7 | 40.3 |
| before, torch **on** | 11.1 | 6.1 | 27.6 | 40.6 |
| after, torch **off** | 7.4 | 2.1 | 47.8 | 20.4 |
| after, torch **on** | 7.9 | 2.1 | 47.6 | 21.1 |

**The whole-frame mean still barely moves when the torch goes on, and that is not the
defect it looks like.** The beam covers **2.5 % of the frame** — `innerSpotAngle` is 0
on purpose, because `LightCone.QualityAt` models a falloff from the axis and §03 wants
aiming to matter — so it cannot move a whole-frame average however well it works. §7.13
read a 0.5 change as a broken flashlight; it is a beam doing its job to 2.5 % of the
pixels.

The measurement that answers ART.md §1's actual rule — *"if a room is readable without
the beam, the lock is open"* — is the **room outside the beam**, and it is the one that
moved:

| the frame, split by where the beam reaches | before | after |
|---|--:|--:|
| outside the beam, torch off — mean | 10.07 | **6.80** |
| outside the beam, torch off — legible % | 39.3 | **19.1** |
| outside the beam, torch off — crushed % | 28.3 | **48.7** |
| inside the beam — mean, off → on | 32.2 → 50.0 (1.55×) | 29.0 → 47.6 (**1.64×**) |
| inside the beam — legible %, off → on | 82.5 → 99.9 | 71.0 → **99.6** |

The room you have to read without the torch lost **half its legible pixels** and gained
72 % more crushed ones, and the beam got *better* at what it lights. That is the lock.

Reproduce both halves with:

```bash
Unity … -executeMethod HorrorGame.Gameplay.PlayerEditor.FirstPersonHandsShot.Batch -shotTag h3
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'h3_*.png'
```

#### 7.15 What this pass did NOT finish

Written here rather than left for the next render to discover.

- **The two-handed carry states still show empty hands.** `PlayerHeldProp` instantiates
  the right model on `ObjectiveMount` at the right scale — brightening
  `h4_20_loot.png` 7× shows its shadow — and it is **outside the camera frustum**. The
  offset is a typed guess; it should come from `gen_player_model.pose_metrics`'s
  `objective_reach` for the Carry clip. Shadow casting is forced Off until it is placed,
  so the component cannot make a frame worse than it found it. §7.13's row is still open.
- **The ghost is over-bright.** `g4_03m_dark.png` reports `brightest 236.8` at 3 m and
  `253.6` at 10 m: it clips. `EMISSION_SCALE` in `gen_ghost.py` came down 4× after the
  first render and has not come down far enough — the bands' *ratios* are right and their
  level is still about three times too high, so the vertical gradient, the drained role
  colour and the maw are all washing out into one white cut-out. One number, one render.
- **`PlayerBodyShot` still cannot reach 15 m.** The filename no longer lies about it, and
  the warning now names `GameConstants.ObserverRange` and the run length needed
  (16.2 m), but `ViewMotionShot.FindStandingSpot` scores toward a 14 m run and rejects
  any heading with under 5 m behind it. §04's 관측자 range has still never been
  photographed.
- **The 차량 is untouched** — §7.13's blockout list stands in full.
