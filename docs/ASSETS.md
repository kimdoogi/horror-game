# ASSETS — what exists, what it is for, and how to rebuild it

Everything here is still **built by the scripts in `tools/`**, but as of **2026-08-09** the
generators no longer start from nothing. Five third-party families now sit underneath
them, all landed that day:

1. the runner's eight animation clips are **Adobe Mixamo mocap**, retargeted onto the
   procedurally-built **17-bone** rig;
2. six zone wall/floor materials carry a **CC0 photo-scan base** under `gen_textures.py`'s
   own grime, §3.8c wet and §3.9 grain;
3. twelve `Dress_*` pieces are **CC0 PolyHaven scans** with their real PBR maps;
4. the runner's anatomy is a **CC0 human base mesh**, with the jacket lofted around it as
   a garment;
5. 101 of the 168 audio clips carry a **CC0 field recording** as their base layer.

In every case the generator still owns the result — the rig, the garment, the grime, the
loudness landing, the §12 contracts and every verification are ours; what is third-party
is the pose curves, the base albedo/normal/rough, the scan geometry, the body cage and
the contact body of a sound. §13 ships this on Steam and an asset of unclear provenance
is a legal problem rather than a mixing problem, so the provenance is recorded in full
below. Mixamo's licence (royalty-free, commercial games, no attribution) and CC0 1.0
(public-domain-equivalent, commercial use, no attribution) cover all five.

| Third-party source | Files | Licence | Used for |
|---|---|---|---|
| Adobe Mixamo (mocap) | `tools/blender/source/mixamo/{Running, Walking, Crouch Walking, Breathing Idle, Crouching Idle, Death, Pistol Idle, Pistol Walk}.fbx` | Mixamo General Terms — royalty-free, commercial games, no attribution | the runner's Run/Walk/CrouchWalk/Idle/Crouch/Death/GunWalk/GunIdle clips, retargeted by `gen_runner.py` |
| PolyHaven `brick_wall_10` | `tools/textures/cc0/polyhaven/brick_wall_10/{albedo.jpg, normal.png, rough.jpg, ao.jpg}` | CC0 1.0 (PolyHaven) | base albedo/normal/rough of `Wall_Brick_Painted`; procedural grime/grain layered on top by `gen_textures.py` |
| PolyHaven `concrete_wall_007` | `tools/textures/cc0/polyhaven/concrete_wall_007/{albedo, normal, rough, ao}` | CC0 1.0 (PolyHaven) | base of `Wall_Concrete_Bare` **and** `Floor_Concrete` (floor variant: offset, cooled, traffic + wet overlays) |
| ambientCG `PaintedPlaster016` | `tools/textures/cc0/ambientcg/PaintedPlaster016/{albedo, normal, rough, ao}` | CC0 1.0 (ambientCG) | base of `Wall_Plaster_Stained`; rising-damp band re-applied procedurally, keyed to the floor line |
| ambientCG `Tiles133B` | `tools/textures/cc0/ambientcg/Tiles133B/{albedo, normal, rough, ao}` | CC0 1.0 (ambientCG) | base of `Floor_Tile` (dirty white mosaic, dark grout) |
| ambientCG `DiamondPlate008A` | `tools/textures/cc0/ambientcg/DiamondPlate008A/{albedo, normal, rough, ao, metal}` | CC0 1.0 (ambientCG) | base of `Floor_Metal`; metalness scaled to a partial ceiling, light procedural rust |
| PolyHaven `barrel_03` | `tools/blender/source/props/barrel_03/` | CC0 1.0 (PolyHaven) | `Dress_BarrelUpright/Toppled/Cluster` geometry + `Dress_Barrel03` maps (`Assets/Models/Dressing/Textures/Barrel03/`) |
| PolyHaven `modular_industrial_pipes_01` | `tools/blender/source/props/modular_industrial_pipes_01/` | CC0 1.0 (PolyHaven) | `Dress_PipeRun_Wall / PipeRun_Ceiling / PipeValve_Cluster` segments + `Dress_PipeGalv01`/`Dress_PipeValve02` maps (albedo ×1.5, roughness ×0.7 at export — §03 beam visibility) |
| PolyHaven `old_military_crate` | `tools/blender/source/props/old_military_crate/` | CC0 1.0 (PolyHaven) | `Dress_CaseStack_Tall / CaseStack_Low` crates + `Dress_CrateMilitary` maps (albedo ×1.18) |
| PolyHaven `caged_hanging_light` | `tools/blender/source/props/caged_hanging_light/` | CC0 1.0 (PolyHaven) | `Dress_BulbCaged` housing (scan chains harvested off, kit chain re-hung; glass slot = `Dress_BulbDead` so the lit/dead swap survives) + `Dress_CagedLamp` maps |
| PolyHaven `worn_metal_rack` | `tools/blender/source/props/worn_metal_rack/` | CC0 1.0 (PolyHaven) | `Dress_ShelfStocked / ShelfToppled` bay (depth squashed 0.60→0.42 m per kit mount_depth) + `Dress_RackWorn` maps |
| PolyHaven `portable_generator` | `tools/blender/source/props/portable_generator/` | CC0 1.0 (PolyHaven) | `Dress_Generator` (new Bulk piece) + `Dress_GeneratorBody` maps |
| Blender Foundation "Human Base Meshes" v1.4.1 | `tools/blender/source/human/body_male_realistic.blend` (base cage of `GEO-body_male_realistic`, 10,590 quads; multires + eyeballs stripped, canonicalized to 1.700 m, feet on z=0; 519 KB) | CC0 1.0 | the runner's anatomy — head/neck/torso/legs welded under the generated garment, hands harvested as rigid glove shells, by `gen_runner.py` |
| USC HMH Foundation optical sound-effects collection — Red Library `R27-45`, `R19-07`, `R11-41`, `R10-22`, `R11-03`, `R19-19`, `R10-42` | `tools/audio/source/footsteps/{concrete,wood,metal,tile,gravel,earth,carpet}_01..06.wav` | CC0 1.0 (Internet Archive `usc-sound-effect-archive`) | the contact body under all 84 dry footstep clips; `R11-03` also supplies the 320–620 Hz gravel substrate that halves F-002 |
| USC Gold Library `G27-12`, `G53-16` | `tools/audio/source/ambience/{creak,drip}_01..08.wav` | CC0 1.0 | `sfx_creak_distant_01..05` (§06's 정지) and `sfx_water_drip_01..04` (§03's 「물이 있는 층」), plus the drips inside `amb_zone_c/f` |
| USC Red Library `R18-51`, `R09-24`, `R09-44` | `tools/audio/source/items/{hinge,doorbody,bolt}_*.wav` | CC0 1.0 | `door_open/close/lock_01..02` — §04 makes opening a door the Listener's own blindness |

The audio sources are 1930s–40s nitrate optical effects collected by a Hollywood sound
editor, donated to the USC HMH Foundation Moving Image Archive, transferred by USC
Cinema students in the 1970s, digitised at CalArts and uploaded by Archive.org's own
staff under CC0 1.0. Vendored **trimmed** (3.31 MB) with per-file source URLs, source
and output SHA-1s and extraction parameters in each category's `PROVENANCE.json`;
`tools/audio/fetch_sources.py` rebuilds the bank, caching the ~90 MB of originals
outside the tree. Delete `tools/audio/source/` and every clip still generates, fully
procedural, with a printed note per missing bank.

> ⚠️ **On trusting an archive.org licence tag.** `licenseurl` is set by the uploader and
> is not verified by anyone. The same CC0-filtered search that found this collection also
> returned a Skywalker Sound pack tagged CC0 whose entire description reads "I Own
> Nothing." Nothing was taken unless the uploader was Archive.org staff **and** the
> collection carried a written donation history. Freesound's CC0 pool was evaluated and
> rejected as a source: originals need an account and only lossy previews are publicly
> reachable, which is not a base layer.

The prop scans are vendored under `tools/blender/source/props/` (~87 MB with
`PROVENANCE.json`; `gen_dressing.py` loads only from there and fails loudly if it is
missing). Their 1024² albedo/normal/mask PNGs ship under
`Assets/Models/Dressing/Textures/` and are bound per material by
`DressingMaterials` when a manifest material row names them — absent fields fall back
to the flat-value path, so the 27 procedural material rows behave exactly as before.
Downloaded-but-skipped (NOT vendored): `metal_tool_chest` (red enamel, off-palette),
`Barrel_01` (redundant against barrel_03).

The CC0 scans are **vendored** under `tools/textures/cc0/` (curated 1024², ~12 MB) so the
generator rebuilds self-contained without a download; override the path with
`$HORROR_TEXTURE_CC0`. Each mapped material carries a `cc0_base` field in
`Textures.manifest.json` (null for the six still-procedural surfaces — `Floor_Wood`,
`Floor_Gravel`, `Ceiling_Concrete_Formed`, and the three trims, which had no honest CC0
match). `gen_textures.py` imports Pillow only lazily inside the photo path and falls back
to fully-procedural (with a printed `[cc0 … missing → procedural]` note) if a scan is
absent, so a checkout without the vendored scans still builds and passes.

Everything else is deterministic from a seed, so a clean rebuild is byte-identical and a
diff after regeneration means something actually changed. The Mixamo sources are
committed (the retarget cannot run without them) and the retarget itself is
deterministic, so `Runner.fbx` still rebuilds identically.

Counted on disk **2026-08-12 at `4ab204f`** with `find`/`stat`. The audio row is also
what `tools/ci/verify_audio.sh` measured the same day.

| | count | size | notes |
|---|--:|--:|---|
| `Assets/Audio/**.wav` | **168** | 93.1 MB | all 48 kHz, 16-bit PCM. Seven families: Footsteps 96, Ambience 22, Monster 15, UI 15, Items 10, Startle 6, Presence 4 |
| `Assets/Models/**.fbx` | **75** | 8.86 MB | MapKit 22, Dressing 39, Props 9, Player 2, Presence 2, Characters 1 |
| `Assets/Models/**.glb` | **1** | 9.87 MB | `Characters/Monster.glb` — preview only, do not import |
| manifests | **6** | — | `Audio/Monster/monster_audio.manifest.json`, `Models/Characters/Monster.clips.json`, `Models/{Dressing,MapKit,Presence,Props}/*.manifest.json` |

> 🔴 **Every row above was wrong before 2026-08-12** and each was wrong in the same
> direction — the document counted an older, smaller, more co-operative project. It
> said 74 fbx (75), **2 glb "preview copies of the two characters"** (there is one
> character and one glb — the player's preview went with `Player.fbx`), and **2
> manifests** (six). The audio count was right and its family breakdown omitted
> Startle and Presence entirely. See §2.6 and §2.7, which did not exist.

No generator reported a file that is not on disk. **The converse is no longer true**:
`Audio/Presence/` (4 clips) is audited by nothing — `verify_audio.py` reports it as a
`folder belongs to no known family` warning — and `gen_gun`, `gen_runner` and
`gen_presence` are not in `tools/ci/run_blender_generators.sh`'s list, so five of the
committed model files have no CI coverage at all.

---

## 1. The setting that silently breaks §12's floor alphabet

Read this before importing anything.

> **Every positional clip must be imported mono and played on a 3D AudioSource.**

Unity does not spatialise a stereo `AudioClip`. A 2-channel clip on a 3D
`AudioSource` plays at a fixed level with no distance attenuation and no panning —
it does not error, it does not warn, it just plays. The consequences:

* 🟢 **§12's floor alphabet stops working — for everyone.** This bullet used to be
  "§04's 청음사 stops working", one player in four. §04 now gives 귀 to all twenty, so
  if footsteps do not attenuate, the monster is equally loud from everywhere, *and* no
  runner can hear which gate the field is at (§11). Same defect, twenty times the blast
  radius.
* **§13's proximity voice stops working.** §13's design rests on one trick —
  "음성을 3D 오디오 소스로 재생하면 근접 음성이 자동으로 된다. 거리 계산 로직이
  필요 없다." A stereo voice clip means every other runner is at zero metres.
* **§06's 정지 stops being frightening.** The monster's silence only reads as *near*
  versus *far* if everything else localises; without attenuation a standing monster and
  a distant one are the same sound.

**136 positional clips ship mono** (140 mono files in total, of which the four
`Presence/pre_*` are mono but played 2D — see §2.6). There is nothing to fix in the
files; the job is to not break it at import.

> 🔴 **"There are no `.meta` files in the repo yet, so Unity has never imported these
> assets" was true once and is now badly false.** There are **168 `.wav.meta` files**
> under `Assets/Audio/` and **1,100 `.meta` files** under `Assets/` as of 2026-08-12.
> Unity has imported everything, the import settings below are *recorded in those
> files*, and the settings that matter are enforced by
> `Editor/AssetImportAudioPostprocessor.cs` and `AssetImportPolicy.cs` rather than by a
> reader remembering this table. Read the postprocessor to learn what is actually
> applied; the table below is what it is *trying* to apply and why.

### Unity import settings that matter

**Audio — positional clips** (`Footsteps/*` 96, `Monster/*` 15, `Items/*` 10,
`Startle/*` 6, `Ambience/sfx_*` 9 — 136 in all. The old list also named
`Ambience/amb_generator_hum_loop` and `UI/ghost_rattle_01..04`, none of which exist):

| setting | value | why |
|---|---|---|
| Force To Mono | **on** (already mono; keep the guard) | stereo silently disables spatialisation |
| `AudioSource.spatialBlend` | **1.0 (3D)** | the default of 0.0 is 2D and gives no attenuation |
| Load Type | Decompress On Load (short one-shots) / Streaming (long loops) | footsteps must not hitch |
| Compression | Vorbis, quality ~70 | ADPCM smears the high band gravel's identity lives in |
| Preload Audio Data | on for footsteps | first step must not be the one that is late |
| Loop | **on** for every `*_loop` clip, `monster_presence_bed`, `monster_breath_loop_*` | |

**Audio — non-diegetic clips** (`UI/*` all 15, `Ambience/amb_zone_*` all 7,
`amb_stairwell_metal_loop`, `amb_tension_t*` all 5, `Presence/pre_*` all 4 — 32 in
all, of which 28 are stereo and the four Presence clips are mono. The old list also
named `amb_surface_vehicle_loop`, `Items/shop_purchase_confirm` and
`Items/loot_sell_credit`, none of which exist, and excepted four ghost rattles that
do not exist either):

> The four `Presence/pre_*` clips are non-diegetic **because the 그늘 has no position**
> (§10). Every other rule in this file sorts a clip by where the sound is coming from;
> these are the one set with nowhere to come from, so they are 2D by construction rather
> than by taste. `pre_gathering_loop` and `pre_close_loop` are the pool filling,
> `pre_taken` is the toll, `pre_return` is the voice coming back.
> They ship, and — see STATUS.md §1.11 — nothing plays them yet.

| setting | value |
|---|---|
| Force To Mono | **off** — the UI and ambience beds are stereo on purpose |
| `AudioSource.spatialBlend` | **0.0 (2D)** |
| Loop | on for the **thirteen** ambience beds (7 zone + stairwell + 5 tension) and the two Presence loops |

**Models — all FBX:**

| setting | value | why |
|---|---|---|
| Scale Factor | 1, Convert Units **on** | measured heights are already correct in metres; `AssetImportPolicy.MetreScaleBand` fails an import outside 1–3 m for a character |
| Import Cameras / Lights | off | none present; keeps the hierarchy clean |
| Mesh Compression | off | these are 200–2500 triangle meshes; compression buys nothing and costs precision |
| Read/Write Enabled | off unless a script needs mesh data | |
| Generate Colliders | off | props need authored colliders, not per-triangle ones |

**Models — `Player/Runner.fbx` and `RunnerArms.fbx`:** Animation Type **Humanoid**.
The rig uses standard humanoid names (`Hips / Spine / Chest / UpperChest / Neck /
Head / Left…`) and maps cleanly to a Unity Avatar. The non-standard bones —
`HeadCameraAnchor` and `FlashlightMount`, plus `BackpackMount` — are attachment points
(§05's flashlight-as-pointer, and the first-person eye); expose them as extra
transforms or the avatar mapping loses them.

> 🔴 This paragraph said **`Player.fbx`**, a **26-bone** rig, and **four** non-standard
> bones including `ObjectiveMount`. There is no `Player.fbx`; the runner is a 17-bone
> rig (`gen_runner.py`); `PlayerRigBones.cs` declares three bones and tombstones
> `ObjectiveMount` as deleted with §03. `AssetImportPolicy.cs` has not been updated to
> match and still preserves four — worth fixing there, not here.

**Models — `Monster.fbx`:** Animation Type **Generic**, root node `Monster_Rig`.
Do **not** use Humanoid: the 29-bone rig has `Jaw`, `Crest1..3`, `LeftScapulaSpur`
and `LeftForearmExtra`, none of which exists in Unity's humanoid skeleton, and a
Humanoid avatar would silently drop their animation.

Both FBX files carry animation stacks named `Rig|Clip` (`Monster_Rig|Chase`, …), which
is how they appear in the importer's Animations tab: **7 clips for the monster, 8 for
the runner** (the old text said 9, from the deleted carry set). `Monster.glb` is for
eyeballing in a viewer and should **not** be imported into Unity — importing both
formats would give you two Avatars for the same character. There is no runner `.glb`.

---

## 2. Audio

### 2.1 Footsteps — 96 clips, 4.07 MB (`Assets/Audio/Footsteps/`)

`step_{surface}_{actor}_{01..04}.wav` — **8 surfaces × 3 actors × 4 variants**. All
mono, all positional.

🟢 **This family is a gameplay channel, not decoration — and the reason got bigger
when the role that justified it was deleted.** This section used to rest on §04's
청음사: one player in four whose single ability was reading the monster's
위치 · 거리 · 이동 방향 from sound. The five roles are gone. game-design §12 re-founds
the rule in one line — 「귀는 스무 명 전부가 가지고 있다」 — and the audience went from
one listener to twenty. §12 still calls it **아트 결정이 아니라 시스템 결정**, and in a
race it carries a second job the co-op game had no use for: a runner hears **which gate
the field is piling up at**, not only where the monster is.

**Eight surfaces, one per storey.** `Core/Map/FloorMaterial.cs` declares them and
`Editor/SceneGen/DescentMap.cs` spends exactly one per floor, so a footstep names a
storey:

| storey | floor | clip prefix |
|---|---|---|
| B1 하역장 | 콘크리트 | `step_concrete_` |
| B2 기록보관소 | 나무 | `step_wood_` |
| B3 기계실 | 금속 | `step_metal_` |
| B4 저탄장 | 자갈 | `step_gravel_` |
| B5 저수조 | 타일 | `step_tile_` |
| B6 병동 | 카펫 | `step_carpet_` |
| B7 수몰층 | 물 | `step_water_` |
| B8 굴착층 | 흙 | `step_earth_` |

> 🔴 The table here used to have **five** rows keyed to zones A–D plus 계단, with
> per-clip centroid and ring-time figures (wood 573 Hz / 122 ms, and so on). That was
> the co-op game's single-floor zone scheme. The measured spectral figures were not
> carried over rather than re-listed from memory: `tools/ci/verify_audio.sh --json`
> prints the current matrix, and on 2026-08-12 the **worst pair separates by 1.44×
> against a 1.4× requirement** — 0.04 of headroom across eight surfaces instead of
> five. Quote that file, not this one, when retuning.

The three actors exist for three different reasons:

* `player_walk` — 2.0 m/s (`GameConstants.WalkSpeed`). The quietest thing in the set,
  ~9 dB under a monster step: a walking runner is the one who can still hear.
* `player_run` — 4.5 m/s (`GameConstants.RunSpeed`). Loud enough that a runner beside
  you genuinely masks the floor — the noise cost of speed, now paid by everybody.
* `monster_step` — the clip the whole field tracks the monster by. §06 gives it
  footsteps in 순찰 / 경계 / 추격 / 수색 and **nothing** in 정지. Built to be
  identifiable in a single step: transposed down ~6.5 semitones, decays doubled, a
  body-weight sub-thump, a drag after the impact, and an off-beat second contact
  ~85 ms late that no human gait produces.

Four variants each because a machine-gun-identical step reads as a looping sound cue
rather than as a creature walking — and telling a creature from a cue is exactly what
§06's 정지 makes expensive to get wrong.

### 2.2 Monster — 15 clips, 5.81 MB (`Assets/Audio/Monster/`)

All mono, all positional. `monster_audio.manifest.json` maps clips to §06's state
machine and is the file the engine should read rather than hardcoding names.

| §06 state | clips | note |
|---|---|---|
| 순찰 Patrol | *(none)* | footsteps only — owned by the footstep family |
| 경계 Alert | `monster_growl_01..03` | the "something is coming" cue; power centroid under 400 Hz, closed mouth |
| 추격 Chase | `monster_roar_01..03` | §06's 발소리+포효; ≥1.4× the growl centroid — mouth open |
| 수색 Search | `monster_search_01..02` | 15 s of hunting the last known position |
| 정지 Standstill | **deliberately empty** | §06: 소리 없음. "침묵이 가장 무서운 소리다." **Do not add a clip here** — the Listener losing the monster is the mechanic |

| event / bed | clips | design |
|---|---|---|
| Grab | `monster_grab_01..02` | the kill |
| Stun | `monster_stun_01..02` | exactly 2.5 s, mirroring `GameConstants.MonsterStunSeconds`. Regenerate if that constant changes. 🔴 The constant was `FlashStunSeconds`, §04's 섬광수 flash; **the role is deleted and `MonsterAgent.Stun` currently has no caller — nothing in the race can stun anything.** The clips and the `Stunned` animation stay because `AssetImportValidator` fails the import if clip length and constant disagree, so deleting the constant unpins the clip from everything. Live finding: either the race grows a way to stun, or brain field + animator state + these two clips + the import rule go together |
| proximity bed | `monster_presence_bed` (25.6 s loop), `monster_breath_loop_01..02` | crossfade **by distance only, never by state** — a state-gated bed would leak the position §06's 정지 exists to hide |

### 2.3 Ambience — 22 clips, 72.2 MB (`Assets/Audio/Ambience/`)

**Thirteen stereo 2D beds and nine mono positional one-shots.** (The document said
twelve and nine, which totals 21; there are 22 on disk.)

| file(s) | role | section |
|---|---|---|
| `amb_zone_{a_wood,b_tile,c_gravel,d_concrete,e_carpet,f_water,g_earth}_loop`, `amb_stairwell_metal_loop` | storey identity — **eight beds, one per floor**, matching §12's eight-material alphabet. §12 makes floor material a gameplay channel, which only works if a runner already knows which storey they are standing on: in the dark, with no UI. 🔴 This row listed four zone loops; `AudioClipCatalog.cs` binds all seven `amb_zone_*` plus the stairwell | §12, §05 |
| `amb_tension_t{1..5}_{dusk,night,deepnight,dawn,predawn}_loop` | §07's five threat tiers *are* the clock. "안에서는 시간 감각이 없다" — underground the player cannot read a clock, so the bed is the readout. All built on one 45 Hz root so a crossfade is not a key change; a measured 3 dB per tier | §07 |
| `sfx_water_drip_01..04` | **mono, positional.** The wet layer of B7 수몰층, and the reason a flooded floor sounds like one before you see it | §12 |
| `sfx_creak_distant_01..05` | **mono, positional.** What remains when the footsteps stop, so §06's 정지 reads as *ominous* rather than as *the audio dropped out*. Also the false alarms: "어디 갔어? 방금 여기 있었는데" | §06 |

> 🔴 **Two rows were deleted from this table because the files do not exist.**
> `amb_surface_vehicle_loop` (§08's 지상 차량 — safe zone, shop, 보급소) and
> `amb_generator_hum_loop` (§03's battery generator) went with the economy and the
> round trip. game-design §01 puts it plainly: **지상은 없다.** There is no surface to
> return to, so there is no bed whose job is relief. Nothing replaced them — the
> descent has no safe room by design, and that is the point.
>
> The `sfx_water_drip` row also lost its old justification: it cited §03's worked clue
> 「그것은 물이 있는 층에 있다」, and there are no clues. **The clips survive on new
> grounds** — B7 is a flooded storey with its own floor material and its own bed, and
> dripping is what tells a runner they have arrived before their torch does.

### 2.4 Items — 10 clips, 640 KB (`Assets/Audio/Items/`)

All ten are mono and positional. **This is the entire family** — `ls` it and you get
exactly these:

| clips | role | section |
|---|---|---|
| `flashlight_on_01..02`, `flashlight_off_01..02` | §05's F key. The torch just turns on — there is nothing to manage | §05, §03 |
| `door_open_01..02`, `door_close_01..02` | the only thing in the game one runner can do to another. §04 gives everybody a door; closing one costs 1.1 s and the whole field behind you pays 4.5 s (§11). It is also loud, which is the trade | §04, §11, §12-B |
| `door_lock_01..02` | §12 puts a lockable door at the neck of a 순환로 | §12 |

> 🔴 **This table used to have sixteen rows and thirteen of them named files that do
> not exist.** Batteries, barricades, noise traps, the safe dial, the breaker, flares,
> chalk, rope, four `loot_pickup_*` weight classes, `loot_sell_credit`,
> `shop_purchase_confirm`, `detector_ping`, `muffler_equip` — every one deleted with
> §08's economy and §03's battery, and the header still claimed two of them were
> "shop UI". Ten clips remain and always did on disk; the document was describing a
> shopping game.
>
> Note what survived and why it changed meaning. The door clips were justified by
> **"opening a door is loud enough to blind the 청음사"**. There is no 청음사 — but
> there are twenty pairs of ears, so a door is now louder in consequence than it ever
> was: it tells the entire field where you are *and* where the gate is.

### 2.5 UI — 15 clips, 8.04 MB (`Assets/Audio/UI/`)

Stereo 2D. Everything peaks at or below −6 dBFS, deliberately underneath positional
world audio at −3, because UI that masks a footstep is a §12 gameplay bug rather than a
taste question.

| clips | role | section |
|---|---|---|
| `threat_night`, `threat_late_night`, `threat_pre_dawn`, `threat_before_sunrise` | §07's tier boundaries. **Four, not five** — 초저녁 is where the match starts, so there is no transition into it. Root drops 98→82→69→55 Hz; 심야 carries the 손전등 반경 −30% and 새벽 the 괴물이 더 빠르다 | §07 |
| `heartbeat_low/mid/high` | 60 / 90 / 120 BPM, all exactly 4.000 s with a shared +50 ms downbeat so an engine crossfade never lands mid-thump | §06 |
| `death_transition_01..02` | §09: being caught is not an ending, it is the ghost state. A severance — the shared soundscape gated to true silence, then a drone that belongs to nobody | §09 |
| `caught_sent_home` | §02's **탈락**, which has no rank. The screen shows two characters and no number, so this clip is the whole result — it must not resolve upward | §02, §09 |
| `escape_success` / `match_failure_wipe` | §02's two endings, built as opposites: finishing opens and releases; the wipe (nobody reaches B8 before §07 runs out) closes, and its last half-second is hard-gated to silence | §02 |
| `descend_basement` | one storey down, eight times. §01's rhythm — the maze is solved, the 투하구 is found, and the reward is another maze | §01, §12-C |
| `voice_activity_blip` / `voice_out_of_range` | §13's proximity voice, cut **at the sender** at `GameConstants.VoiceCutoffDistance` (30 m). The out-of-range cue is the channel losing carrier, so a player learns where 30 m is instead of guessing why nobody answered | §13 |

> 🔴 **Nine clips in the old table do not exist**, and one that does was missing.
> Deleted with their systems: `clue_read_success` / `clue_read_failed` and
> `objective_found` / `objective_pickup` (§03's clue chain and objective),
> `shop_open` / `shop_close` / `shop_denied` (§08), `surface_reached` (there is no
> surface — §01: **지상은 없다**), and `ghost_rattle_01..04` + `ghost_rattle_ready`.
> `caught_sent_home` was on disk and in no table.
>
> The ghost rattles are the interesting deletion. They were §09's *only channel*: a
> dead player could rattle an object to warn the living. game-design §11 closes that
> door on purpose — 「경주에서 죽은 사람이 산 사람을 도우면 그건 팀이다」. A ghost now
> watches and cannot act, so the header's "except the four ghost rattles" went with
> them and the family is stereo 2D throughout. §09's audio moved to `Audio/Presence/`
> (§2.6), which is a different idea entirely.

### 2.6 Presence — 4 clips, 1.70 MB (`Assets/Audio/Presence/`)

`pre_gathering_loop`, `pre_close_loop`, `pre_return`, `pre_taken`. Driven by
`Core/Presence/` (`PresenceField`, `PresenceStage`, `PresenceToll`).

**The odd one out on channel policy: mono files played 2D.** All four are 1-channel on
disk, and §1 imports them non-diegetic because §10's 그늘 has no position to come from.
Every other rule in this document sorts a clip by where the sound is — these have
nowhere. Mono is therefore not a spatialisation guard here, it is just half the bytes.

> ⚠️ **This family is audited by nothing.** `tools/audio/verify_audio.py` reports
> `[layout] Audio/Presence/ — folder belongs to no known family` as a warning and
> excludes the four clips from every measurement, which is why the audit counts
> **164** clips against 168 on disk. It is not in the §12 alphabet and it is not in the
> UI loudness policy, so nothing checks its levels against anything. Whoever owns
> `tools/audio/` should either add it as a family or state in the audit why it is
> exempt.

### 2.7 Startle — 6 clips, 0.68 MB (`Assets/Audio/Startle/`)

`stl_cabinet_slam_01..02`, `stl_glimpse_01`, `stl_pipe_vent_01`, `stl_skitter_01..02`.
Mono positional, paired with the four `Startle_*` meshes in §3.3 and placed by
`MapSceneBuilder.BuildStartles`.

The design constraint is the one §06 cares about: a startle may not be mistaken for
the monster. These are one-shots with no low-frequency body — the sub-thump belongs to
`monster_step` alone, so a slamming cabinet reads as *the building*, not as *the thing*.

---

## 3. Models

All FBX are single-mesh, unit-scaled, UV-mapped and materialled. No object carries a
non-unit transform, so nothing has a baked-scale hazard.

### 3.1 Characters — 1 FBX + 1 GLB, 12.66 MB total (`Assets/Models/Characters/`)

Measured 2026-08-12. The folder holds `Monster.fbx` (2.72 MB), `Monster.glb`
(9.87 MB, preview only — do not import), `Monster.clips.json`, and `Materials/`.

| file | clips | materials |
|---|--:|---|
| `Monster.fbx` | **7** — `Patrol`, `Chase`, `Alert`, `Search`, `Standstill`, `Stunned`, `Grab` (from `Monster.clips.json`, 30 fps) | `Monster_Hide`, `Monster_Eyes`, `Monster_Maw` |

> 🔴 **`Player.fbx` is not in this folder and has not been for some time.** The old
> table gave it 1,252 tris, 26 bones, 9 clips and a 1.750 m height, and paired
> `Player.glb` with `Monster.glb` as "preview copies of the two characters". **The
> player is now `Assets/Models/Player/Runner.fbx` + `RunnerArms.fbx` (§3.5), built by
> `gen_runner.py` on a 17-bone rig with Mixamo mocap**, and there is no player `.glb`.
> The tris/bones figures for the monster were also not carried over rather than
> re-copied: they were never re-measured after the sculpt was adopted and
> `gen_monster_ai.py` replaced `gen_monster_model.py` as the thing that writes this
> file. `Monster.clips.json` is the honest source for anything about the monster's
> animation, and it records per-clip `ground_speed_mps` measured from the
> weight-bearing foot, which is what a §07 tier playback rate is computed from.

**There is exactly one monster in this folder, and that is a rule.** Its three material
names are contracts, not labels: `MonsterSkin` puts §04's constant eye glow on whatever
matches `Monster_Eyes` and `MonsterAcquireTell` fires §06's acquisition flare on whatever
matches `Monster_Maw`, so a creature carrying only a hide disconnects both without
logging anything. Anything else dropped into `Assets/Models/Characters/` is graded
against the *player's* humanoid policy by `AssetImportValidator` and reported as broken
on every run — two unadopted monster variants once cost four failures a run there.
A generator publishing a variant writes it to `artifacts/`, not here.

🟢 **Why a first-person game still needs a body, re-founded.** §05 used to be quoted
here as "1인칭이어도 캐릭터 모델이 필요하다. **협동 게임에서는 다른 3명이 보여야
한다.**" There is no co-op party of three. The requirement did not weaken — it got
**seven times larger**: §11 puts up to **20** runners on one starting ring and the
whole design of §11 is that they crowd the same gate. Every one of them has to be
visible, animated and distinguishable in the dark. §16-1's "이 둘은 우회가 안 된다"
stands.

**Runner animations** (8): `Idle`, `Walk`, `Run`, `Crouch`, `CrouchWalk`, `Death`,
`GunIdle`, `GunWalk` — retargeted by `gen_runner.py` from the eight Mixamo sources
listed in the provenance table above.

> 🔴 **Three of the old nine were carry clips and they are gone.** `Carry`,
> `CarryHeavy` and `CarryIdle` covered §08's 궤짝 무게5 two-person carry and §04's
> 관측자 (whose ability needed 이동 정지 3초). No weight, no role, no clips. Two new
> ones arrived with the revolver: `GunIdle` and `GunWalk`.

**The rig's mount bones.** `Gameplay/Player/PlayerRigBones.cs` declares **three**
non-standard bones: `HeadCameraAnchor` (§05 puts the eye here), `FlashlightMount` (§05's
flashlight-as-pointer) and `BackpackMount`. `ObjectiveMount` was deleted with §03 and
the file says so in a comment where the constant used to be.

> ⚠️ **Two live inconsistencies, both outside this document's reach.**
> `Editor/AssetImportPolicy.cs` still lists **four** mount bones including
> `ObjectiveMount`, and `AssetImportModelPostprocessor.cs` still says "Four of them"
> in a comment — so the importer is preserving a bone nothing declares.
> And `BackpackMount` survives with no 가방 to hang on it: §08 deleted the bag, so
> unlike `FlashlightMount` this one has no current consumer.

**Monster animations** (7) map one-to-one onto §06's state machine plus two events:

| clip | §06 state |
|---|---|
| `Patrol` | 순찰 |
| `Alert` | 경계 |
| `Chase` | 추격 |
| `Search` | 수색 |
| `Standstill` | 정지 — moves, makes no sound; the silence is the weapon |
| `Grab` | the kill |
| `Stunned` | 🔴 was §04's 섬광수. The role is gone and `MonsterAgent.Stun` has **no caller** — see §2.2. The clip is pinned to `GameConstants.MonsterStunSeconds` by `AssetImportValidator`, which is currently the only thing holding it in the project |

**On the two heights.** This section used to close with "the monster is deliberately
0.59 m taller than the player", from `Player.fbx` at 1.750 m and `Monster.fbx` at
2.336 m. The player figure came from a file that no longer exists; the runner's body is
now built on a CC0 human base canonicalised to **1.700 m** (see the provenance table).
**Neither height was re-measured for this audit** — that needs Blender or the editor,
and this file does not print numbers nobody measured. The sanity rule itself is live
and enforced in code: `AssetImportPolicy.MetreScaleBand` fails an import outside the
1–3 m band, so a unit-scale error is caught whatever the exact figures are.

### 3.2 MapKit — 22 files, 1.94 MB, 69,580 tris (`Assets/Models/MapKit/`)

§12 opens with "맵은 아트가 아니라 시스템이다" and every dimension here is derived
from a numbered rule. `MapKit.manifest.json` carries the grid (2.5 m), storey height
(3.75 m), corridor clear section (2.2 × 3.0 m) and every footprint and dock point.

| piece(s) | §12 rule it implements |
|---|---|
| `Corridor_Straight_2m5 / _5m / _10m` | 직선 통로 최대 **20m** — no single piece can break the rule, and 10 m is the largest offered |
| `SCorridor_Unit_10m_x2` | ① S자 통로, 10 m × 2회 굽음 — 통과 4.2초 > 차단 3초. §12 calls this 가장 확실한 연속 차단 구조 |
| `Corridor_Corner_L`, `Junction_T`, `Junction_Cross_4Way` | ② 순환로 and ③ 분기; §12 requires 순환로 1+ per zone and 3+ overall — "트리 구조는 사형선고" |
| `DeadEnd_Cap` | 막힌 길 비율 20~25%, each with 보상 |
| `Hall_Open_20x20` | §12's 개방 공간, which must be adjacent to maze space — "두 성격의 공간이 인접해야 한다" |
| `FloorTile_{Wood,Tile,Gravel,Concrete,Metal}` | **five tiles for eight materials.** They are the geometry half of §12's alphabet and the footstep clips are the other half — but the kit was never extended past five. `MapKitCatalogue` maps the three newer materials onto existing meshes: Water→`FloorTile_Tile`, Earth→`FloorTile_Gravel`, Carpet→`FloorTile_Wood`. Only the *colour* differs (`MapSceneBuilder` assigns a per-material tint), so B6/B7/B8 are audibly distinct and visually borrowed. Live art gap |
| `FloorBoundary_Split` | "재질 경계를 명확히 할 것" — the boundary is authored, not implied |
| `Stairwell_Metal` | 계단 금속 울림. §12 makes a stairwell transit the clearest signal on the map |
| `ObservationPost_Gallery`, `ObservationPost_BarredWindow` | 🟢 was §04's 관측자 requirement — "없으면 관측자는 죽으러 가야 한다". The role is deleted; §12 keeps the rule (15 m sightline, safe, 구역당 1~2개) because **every runner now needs somewhere to look from before committing to a gate**, which is the same geometry for twenty people instead of one |
| `Doorway_Frame`, `Door_Panel_Lockable` | 🟢 was §04's 정비공 requirement. §04 now gives the door to **everyone** — 「문: 누구나 닫고 연다」 — so the cap of 구역당 1~2개 matters more, not less: twenty people with doors at a bottleneck is §11's whole design |
| `WallPanel_Electrical` | 전기 패널 구역당 1개. 🔴 The old justification — "§03 requires clue sites to have panel access so 정비공 can light them" — names two deleted systems. The piece is now **set dressing with a gameplay alibi**: §10's 그늘 still trades light against being seen. If nothing switches it, it is decoration and belongs in §3.4 |

### 3.3 Props — 9 files, 0.41 MB (`Assets/Models/Props/`)

Re-measured 2026-08-12. The §08 loot/safe/vehicle pieces this table once listed were
deleted with their systems; what remains:

| prop(s) | role | section |
|---|---|---|
| `Gun_Pickup` / `Gun_Held` | the 막힌 길 revolver — pickup form, and the held form `RunnerGun` mounts on the arm. **One shot, then the gun is gone**; it sends one runner back, it does not kill. `Core/Race/Gunplay.cs` cites §02 and §12's 관문 for it, not §08 | §02, §12 |
| `Startle_CabinetShell` / `Startle_CabinetLeaf`, `Startle_PipeStub`, `Startle_Skitterer` | the 깜짝 kit: hinged cabinet, steam stub, skitterer — placed by `MapSceneBuilder.BuildStartles`, judged unable to corrupt the race. Paired with the six `Audio/Startle/` clips (§2.7) | §06 |
| `Pipes`, `Shelving`, `Debris` | sightline blockers. §12 puts 시야 차단 지점 every 15~25 m so a 60 m 질주 has 3~4 chances to break aggro | §12, §06 |

### 3.4 Dressing — 39 pieces, 28,889 tris + 23 texture PNGs (`Assets/Models/Dressing/`)

The set-dressing kit `ScatterSession` scatters per cell (Bulk/Debris/Wall/Ceiling/
Corner/Sign groups, §12 palettes, KeepOut- and clear-band-gated). Generated by
`tools/blender/gen_dressing.py`; `Dressing.manifest.json` carries every measured
footprint, mount, palette and material row — C# reads it and never restates a number.

**Twelve of the 39 are built from CC0 PolyHaven scans as of 2026-08-09** (barrels ×3,
military crate stacks ×2, metal rack ×2, pipe runs ×3, caged bay light, and the new
`Dress_Generator`); their real PBR maps ship at 1024² under `Textures/` and bind via
the manifest's optional `albedo_map`/`normal_map`/`mask_map` fields. The other 27
pieces stay fully procedural (flat manifest values + the shared noise maps).
Contracts that survived the swap, by construction: piece names (ScatterSession's
string literals and `Dress_Bulb` prefix pickers), FLOOR/WALL/CEILING pivots, solid
floor footprints ≤ their procedural predecessors (§12 two-runner clear band), the
`Dress_BulbDead`→`Dress_BulbLit` lit-swap slots, per-piece tri budgets ≤ 2,600, and
byte-identical regeneration for every untouched piece.

### 3.5 Player — 2 files, 1.75 MB (`Assets/Models/Player/`)

`Runner.fbx` and `RunnerArms.fbx`, written by `tools/blender/gen_runner.py` on the
17-bone rig, anatomy from the CC0 human base and motion retargeted from the eight
Mixamo sources. `RunnerArms.fbx` is the **first-person viewmodel** — a dedicated pair
of arms rather than a third-person body seen from inside, which is what `b92ae78`
changed. No `.glb` preview ships for either.

This folder did not appear anywhere in this document before 2026-08-12, while §3.1
still described a `Player.fbx` that does not exist.

### 3.6 Presence — 2 files, 0.17 MB (`Assets/Models/Presence/`)

`Presence_Figure.fbx` and `Presence_Mote.fbx` + `Presence.manifest.json`, written by
`tools/blender/gen_presence.py`. §09's 유령 made visible; the four `Audio/Presence/`
clips (§2.6) are the audio half.

> ⚠️ **Neither `gen_runner.py` nor `gen_presence.py` nor `gen_gun.py` is in
> `tools/ci/run_blender_generators.sh`'s list**, so these four files plus the two
> `Gun_*` props are committed output that CI never regenerates. That is exactly the rot
> §2.3 of `docs/CI.md` describes, on five of the seventy-five models.

---

## 4. How to regenerate everything

Run from the repo root. Both toolchains are deterministic: a clean rebuild produces
byte-identical output, so `git diff` after a regeneration is a real change.

### 4.1 Audio

```sh
# 96 footstep clips — §12's eight-material alphabet (§12, §04 「귀」)
tools/audio/.venv/bin/python tools/audio/gen_footsteps.py

# 22 ambience beds and positional one-shots (§07, §12)
tools/audio/.venv/bin/python tools/audio/gen_ambience.py

# 10 item and interaction sounds (§05, §12-B)
tools/audio/.venv/bin/python tools/audio/gen_items.py

# 15 monster clips + monster_audio.manifest.json (§06)
tools/audio/.venv/bin/python tools/audio/gen_monster_audio.py

# 14 non-diegetic UI clips (§02, §07, §13)
tools/audio/.venv/bin/python tools/audio/gen_ui.py

# caught_sent_home — §02's 탈락, one clip, also into Audio/UI/ (§02, §09)
tools/audio/.venv/bin/python tools/audio/gen_caught.py

# 6 startle one-shots into Audio/Startle/ (§06)
tools/audio/.venv/bin/python tools/audio/gen_scares.py
```

**The four `Audio/Presence/` clips are not built by any of these.** They come from
`tools/blender/gen_presence.py` — a *Blender* script that writes both
`Models/Presence/*.fbx` and `Assets/Audio/Presence/*.wav` in one run. That is why §2.6's
family is unknown to `verify_audio.py` and absent from the audio generator list: it is
filed under the wrong toolchain, and it is in neither CI job's list either.

> 🔴 **Every count in this block was wrong and two generators were missing.** It read
> 60 / 21 / 43 / 15 / 27 — 166 clips across five scripts, describing the co-op game's
> item and UI families. The real totals are 96 / 22 / 10 / 15 / 14 / 1 / 6 = 164 across
> **seven** scripts, plus the 4 Presence clips from Blender = 168. `gen_scares.py` and
> `gen_caught.py` were not listed at all. Counts are `find | wc -l` per folder on
> 2026-08-12; the script-to-folder mapping is from each script's own `OUT_DIR`.

Each script verifies its own output with `synth.assert_usable` and its own
design-specific assertions, and fails loudly rather than writing something unusable.
`tools/audio/synth.py` is the shared DSP library; it is not a generator and produces
no files.

### 4.2 Models

```sh
BLENDER=/Applications/Blender.app/Contents/MacOS/Blender

# 22 MapKit pieces + MapKit.manifest.json (§12)
$BLENDER --background --factory-startup --python tools/blender/gen_mapkit.py

# 39 Dress_* pieces + Dressing.manifest.json (§12)
$BLENDER --background --factory-startup --python tools/blender/gen_dressing.py

# 9 props + Props.manifest.json (§02, §06, §12)
$BLENDER --background --factory-startup --python tools/blender/gen_props.py
# iterate on a subset: ... --python tools/blender/gen_props.py -- Pipes Shelving

# Runner.fbx + RunnerArms.fbx, 17-bone rig, 8 Mixamo clips (§05, §11)
$BLENDER --background --factory-startup --python tools/blender/gen_runner.py

# Monster.fbx + Monster.glb + Monster.clips.json, 7 clips (§06, §16-1)
$BLENDER --background --factory-startup --python tools/blender/gen_monster_ai.py

# §10's 그늘: Models/Presence/*.fbx AND Assets/Audio/Presence/*.wav
$BLENDER --background --factory-startup --python tools/blender/gen_presence.py

# Ghost.fbx + Ghost.glb — ⚠️ writes files the repo does NOT keep; see below
$BLENDER --background --factory-startup --python tools/blender/gen_ghost.py

# Gun_Pickup.fbx + Gun_Held.fbx (§02, §12)
$BLENDER --background --factory-startup --python tools/blender/gen_gun.py
```

> 🔴 **`gen_player_model.py` no longer writes the player.** This block told you to run
> it for "Player.fbx + Player.glb, 26 bones, 9 clips"; the player is now
> `gen_runner.py`'s `Runner.fbx` + `RunnerArms.fbx` on a 17-bone rig with 8 retargeted
> Mixamo clips, and no `.glb`. The MapKit and props counts were 21 and 24 against 22
> and 9 on disk, and `gen_dressing`, `gen_runner`, `gen_presence` and `gen_gun` — which
> between them write **52 of the 75 committed models** — were not listed at all.

`tools/blender/blendkit.py` is the shared mesh/rig library, not a generator. So, now,
is `tools/blender/gen_monster_model.py`: it built the monster out of convex hulls until
the committed sculpt replaced it, and it is kept because `gen_monster_ai.py` imports its
seven §06 clip authors and its procedural skin pipeline verbatim. It refuses to write
`Monster.fbx` unless run with `-- --hull`, which is only wanted for a before/after
comparison against the creature that ships.

### 4.3 Verify after any retune

```sh
tools/audio/.venv/bin/python tools/audio/verify_audio.py
tools/audio/.venv/bin/python tools/audio/verify_audio.py --json /tmp/audit.json
```

Exit code is non-zero when a blocking defect is present. This is the cross-family
audit — the checks no single generator can do:

1. inventory, strays and cross-family basename collisions
2. the **§12 8×8 material separation matrix**, at four strictnesses: per surface, per
   actor, per clip, and low-passed to stand in for distance and wall occlusion
3. loop seamlessness — click, level pulse, and fade-notch, per channel
4. levels and format — no clipping, nothing silent, no DC offset, 48 kHz throughout
5. **channel policy** — every positional clip mono, every non-diegetic clip stereo
6. **HUD versus ears** — `GameConstants.ListenerClarity*` against the measured
   A-weighted audibility of each surface, swept across occlusion. `ListenerAbility`
   hands the player an error radius from a hand-authored constant while their ears get
   the actual clip; nothing else in the repo compares the two.

### 4.4 Known open items

Recorded here so they are not rediscovered. **Re-derived 2026-08-12** from a
`tools/ci/verify_audio.sh` run at `4ab204f`; four of the seven items that used to be in
this list named files that do not exist.

* **Clarity is inverted against measured loudness on four pairs, not one.** Today's
  audit: `gravel/concrete`, `gravel/earth`, `water/wood`, `tile/concrete`. The first
  two are blocking and baselined as [F-002](BALANCE-FINDINGS.md#f-002); the other two
  are warnings. The mechanism is the same each time — the louder-ranked surface's
  identity lives in a high band a wall removes, so `GameConstants.ListenerClarity*` and
  the player's ears disagree exactly when it matters. `ListenerClarityGravel = 0.70`
  against `ListenerClarityConcrete = 0.50` is the original case.
  🔴 The old entry justified this with "`ListenerAbility` states that the role hears
  through walls". **`ListenerAbility` is deleted** — `GameConstants` has an explicit
  tombstone listing it with the other four abilities. The finding survives on stronger
  ground: the clarity table is now what the HUD shows *every* player, so an inversion
  is a lie told to twenty people rather than a mis-tuned role.
* **§12 separation has almost no margin, and less than it used to.** Measured
  2026-08-12: worst pair **water vs gravel at 1.44×** against a 1.4× floor, worst
  within a single actor **1.41×**, and occluded **metal vs gravel at 1.137×**. The old
  entry said 2.03× dry and 1.396× occluded for wood-vs-metal; three surfaces have been
  added since. Dry separation still passes, so this is a range limit rather than a
  generator bug, and §12 says the answer is in the Unity mix (occlusion filter
  strength, 3D rolloff) or in pushing surfaces apart in the **low** end.
* **`Audio/Presence/` is audited by nothing** — see §2.6. Four clips, no family, no
  channel policy, no level check.
* 🔴 **Deleted from this list because the files do not exist:**
  `Items/flare_burn_loop.wav` (loop-seam notch), `Items/loot_sell_credit.wav` (mono
  shop UI), the §08 구매 목록 backlog (응급킷 · 정비 자재 · 가방 · 건물 도면 · 미끼 ·
  감지기 · 소음기), and `HidingSpot_Locker`. The last is the interesting one:
  `Assets/Models/Props/` has **no `HidingSpot_Locker.fbx`**, yet
  `Editor/AssetImportPolicy.cs`, `tools/blender/gen_props.py` and
  `docs/wiki/10-glossary.md` all still describe it as shipping. §12's 은폐 지점 has no
  model.
* ⚠️ **`Ghost.fbx` is generated by CI and kept by nobody.** `tools/blender/gen_ghost.py`
  is in `run_blender_generators.sh`'s default list and declares
  `Assets/Models/Characters/Ghost.fbx` + `Ghost.glb` as its output. **Neither is in the
  repo** — not committed, not gitignored — while `Ghost.textures.json`,
  `GhostMaterials.cs`, `GhostShot.cs` and a set of `Ghost_*.mat` files are. Three of
  those materials are `Ghost_Role_Observer`, `Ghost_Role_Engineer` and
  `Ghost_Role_Runner`, named after §04 roles deleted two rounds ago, and §09's 유령
  itself was deleted from game-design on 2026-08-12. Decide: delete the generator and
  the materials, or reuse the model for the 1.75 s catch feedback, or say in writing
  that it is held for a second mode.
* **Still open, and still true:** there is no separate 질주 clip. `RunnerSprintSpeed`
  is 5.6 m/s (§04, §05) and the rig will play `Run`, authored slower, so expect
  foot-sliding until it is re-authored or speed-matched. This one got *worse* with the
  pivot — sprint used to belong to one role and now belongs to all twenty runners.
