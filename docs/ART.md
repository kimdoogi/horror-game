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

The current map lands at 16–39% crushed and 31–57% legible on the five zone views,
0.00% blown on all of them. Numbers for every shot are in §6.

---

## 2. Regenerating every asset

The order matters and is not obvious. Each step below overwrites state the previous
one wrote.

### 2.1 Textures → materials

```bash
python3 tools/textures/gen_textures.py                  # → unity/HorrorGame/Assets/Textures/**
python3 tools/textures/gen_textures.py --only Floor_Wood  # one, while iterating
```

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

Map: seed 1204, dressing seed 4703, §07 tier 0. §12 validation **PASS** on all 17
rules; 주자 테스트 **7/10**, inside §12's 5–7 band.

| Shot | median | crushed % | legible % | blown % |
|---|--:|--:|--:|--:|
| Zone A · B1 · 나무 | 3.3 | 38.8 | 30.9 | 0.00 |
| Zone B · B3 · 타일 | 7.2 | 16.6 | 47.7 | 0.00 |
| Zone C · B2 · 자갈 | 3.5 | 37.1 | 32.2 | 0.00 |
| Zone D · B1 · 콘크리트 | 9.2 | 15.6 | 56.6 | 0.00 |
| Zone E · B2 · 금속 | 8.4 | 19.3 | 52.3 | 0.00 |
| spawn0 | 15.6 | 9.8 | 72.0 | 0.00 |
| spawn1 | 70.3 | 2.1 | 93.9 | 0.15 |
| spawn2 | 47.4 | 10.9 | 84.5 | 0.00 |
| spawn3 | 6.1 | 30.0 | 39.3 | 0.39 |

For comparison, the `lit_*` baseline before this work had 0% crushed and 20–54%
legible — but with a median of 6 and a p90 of 10, i.e. the whole frame mushed into an
indistinguishable near-black with no beam visible in it.

---

## 7. What still needs work

Honest list, worst first.

### 7.1 The monster still cannot reach anybody — [B-001](BLOCKERS.md)

Not an art problem, but it is the largest problem and this work did not fix it. The
map pipeline reports it on every run:

```
NavMesh: before 5 complete, 61 partial, 0 unreachable, 0 markers off-mesh
      →  after  4 complete, 58 partial, 0 unreachable, 1 markers off-mesh
```

**5 of 62 sampled routes complete.** The dressing pass fails its own gate because it
made 5 into 4, which is true and beside the point: the baseline it is protecting is
already catastrophic. Everything §14 says decides the project — 추격이 재밌는가 — is
untestable until this is fixed.

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

### 7.5 Brick scale

Courses read at roughly 30–40 cm against a real 21.5 cm brick, which makes rooms feel
smaller and slightly toy-like. `ProceduralTextureMaterials.ReportKitUvScale` measures
the kit's UV scale; the wall entries in `Textures.manifest.json` need their
`world_size_metres` re-derived from it.

### 7.6 Thin geometry blows out at close range

A door panel or shelf edge seen on-edge close to the camera catches the beam and
clips to a white sliver — visible as a vertical bar in the Zone A frame. Smoothness on
the trim materials is too high for a near-field specular this narrow.

### 7.7 The overhead view is still nearly useless

It shows a roof. Judging §12's zones, loops and dead ends from above needs the top
storey culled, which nobody has written. `MapQualityReport` is the real layout gate
and it is thorough; the overhead shot should probably be deleted rather than fixed.

### 7.8 Everything is one colour

The grade is cold, saturation −34, and the practicals are the only warmth in the
building. It is coherent, and it is monotonous over a 25–35 minute match. §07 ramps
brightness and vignette across the night but not hue; there is room for the last tier
to go somewhere the first tier does not.
