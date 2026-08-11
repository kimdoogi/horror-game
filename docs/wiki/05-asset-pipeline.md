# The asset pipeline — every shipped asset is built by a script

> Every sound, every mesh and every texture in `Assets/` is **produced by a script in
> `tools/`**. §13 ships this on Steam, and an asset of unclear provenance is a legal
> problem rather than a mixing problem — so provenance is recorded in full, per file,
> in [`docs/ASSETS.md`](../ASSETS.md).

> 🔴 **This page used to open "Nothing in this game is sampled, downloaded,
> photographed or licensed." That stopped being true on 2026-08-09.** Five third-party
> families now sit *underneath* the generators, all of them Mixamo-licensed or CC0:
> the runner's eight animation clips are **Mixamo mocap** retargeted onto a
> procedurally-built rig; six zone wall/floor materials carry a **CC0 photo-scan base**
> under `gen_textures.py`'s own grime; twelve `Dress_*` pieces are **CC0 PolyHaven
> scans**; the runner's anatomy is a **CC0 human base mesh**; and 101 of the 168 audio
> clips carry a **CC0 field recording** as their base layer. **The rule the sentence
> was protecting survived intact** — the generator still owns the result, every §12
> contract and every verification is ours, and nothing enters `Assets/` except through
> a script. What changed is that "generated" no longer implies "from nothing".
> `tools/audio/fetch_sources.py` rebuilds the sound bank, and deleting
> `tools/audio/source/` still leaves every clip generating, fully procedural, with a
> printed note per missing bank.

Contracts and per-file design rationale: [`docs/ASSETS.md`](../ASSETS.md).
The look and the render settings: [`docs/ART.md`](../ART.md).
This page is the operating manual and the traps.

On disk, counted 2026-08-12 at `4ab204f`:

```bash
find unity/HorrorGame/Assets/Models -name '*.fbx' | wc -l     #  75
find unity/HorrorGame/Assets/Audio  -name '*.wav' | wc -l     # 168
find unity/HorrorGame/Assets/Models -name '*.glb' | wc -l     #   1  (Monster.glb, preview only — do NOT import)
```

By folder: **Models** — Dressing 39, MapKit 22, Props 9, Player 2, Presence 2,
Characters 1. **Audio** — Footsteps 96, Ambience 22, Monster 15, UI 15, Items 10,
Startle 6, Presence 4.

> ASSETS.md's header table was re-counted on 2026-08-09 and says **74** FBX / 168 WAV
> / 2 GLB. Two of those three are already wrong again — one FBX and one GLB — which is
> the whole reason this page says **count, do not quote.** (The old warning here said
> that table "still says 47 FBX"; it has not said 47 for a fortnight.)

---

## 1. The three generators

| Domain | Tool | Writes | Verified by |
|---|---|---|---|
| Audio | `tools/audio/*.py` (Python + numpy/scipy) | `Assets/Audio/**/*.wav` | `tools/audio/verify_audio.py` |
| Models | `tools/blender/gen_*.py` (Blender headless) | `Assets/Models/**/*.fbx`, `*.glb`, manifests | `tools/ci/run_blender_generators.sh` |
| Textures | `tools/textures/gen_textures.py` (Python) | `Assets/Textures/**` + `Textures.manifest.json` | its own assertions + `ProceduralTextureMaterials.Build` |

All three are **deterministic from a seed**, so a clean rebuild reproduces the mesh
and sample data and a `git diff` after regeneration means something actually changed.
The one exception is documented in [CI.md §2.3](../CI.md): FBX *containers* embed a
`CreationTime`, so a regenerated FBX comes out at *identical length* differing in 53
header bytes. (CI.md measured that on `Crate.fbx`, which has since been deleted; the
phenomenon is a property of the FBX header, not of that mesh.) That is why no gate
does `git diff --exit-code` on models — the real signal is the `ASSET_REPORT` line's
vertex, triangle and bounding-box figures.

The Mixamo sources are committed and the retarget is deterministic, so `Runner.fbx`
still rebuilds identically even though its clips are mocap.

---

## 2. Audio — `tools/audio/`

```bash
tools/audio/.venv/bin/python tools/audio/gen_footsteps.py      # → Audio/Footsteps, 96 clips — §12's surface alphabet
tools/audio/.venv/bin/python tools/audio/gen_ambience.py       # → Audio/Ambience,  22 beds and positional one-shots (§03, §07, §12)
tools/audio/.venv/bin/python tools/audio/gen_items.py          # → Audio/Items,     10 door and flashlight sounds (§03, §12)
tools/audio/.venv/bin/python tools/audio/gen_monster_audio.py  # → Audio/Monster,   15 clips + monster_audio.manifest.json (§06)
tools/audio/.venv/bin/python tools/audio/gen_ui.py             # → Audio/UI,        the non-diegetic set (§02, §09, §13)
tools/audio/.venv/bin/python tools/audio/gen_scares.py         # → Audio/Startle,    6 깜짝 stingers
tools/audio/.venv/bin/python tools/audio/gen_caught.py         # → Audio/UI,         exactly one file, caught_sent_home.wav
```

`gen_scares.py` and `gen_caught.py` are new since this list was last written, and
`Audio/Presence/`'s four clips come from **`tools/blender/gen_presence.py`**, not from
`tools/audio/` at all — which is why `verify_audio.py` warns that
`Audio/Presence/` "belongs to no known family".

`tools/audio/synth.py` is the shared DSP library, **not** a generator — it produces
no files. Each generator calls `synth.assert_usable` plus its own design assertions
and refuses to write silence, clipping, a DC offset or a broken loop.

Then always:

```bash
tools/audio/.venv/bin/python tools/audio/verify_audio.py
```

This is the cross-family audit — the checks no single generator can perform, because
they are properties of the *set*. Retune gravel and the check that breaks lives in
the wood file. Six sections: `[1] INVENTORY` and strays; `[2]` the **§12 material
separation matrix, now 8×8** (blocks 2a–2e: a headline over all 96 footstep clips,
one matrix per actor, a clip-level worst case, decay/noise axes, and a range model at
15 m and 25 m through a wall); `[3]` loop seamlessness; `[4]` levels and format;
`[5]` **channel policy** (every positional clip mono, every non-diegetic clip stereo);
and `[6]` **HUD versus ears** — `GameConstants.ListenerClarity*` against measured
audibility.

> 🔴 **The matrix was 5×5 when this page was written.** `FloorMaterial` now has eight
> members — wood, tile, gravel, concrete, metal, water, earth, carpet — and
> `GameConstants` carries a `ListenerClarity*` for each plus `…Unknown`. **The
> requirement outlived the role that justified it.** It was argued from §04's 청음사,
> whose single ability was reading the monster's 위치 · 거리 · 이동 방향 by ear; the
> 청음사 is deleted and *every one of the twenty runners hears footsteps now*, so
> `GameConstants` re-founds the table on §12 directly — 「소리 → 바닥 재질이
> 지도다」 — and `MapZone.ClarityOf` is asked by `MatchDirector`'s footsteps, by the
> monster's hearing and by `VoiceRules.MonsterHearingRangeMetres`. Fewer readers would
> have been an argument for deleting it; there are more.

### The audio audit fails today, on purpose, and that is not your bug

Measured 2026-08-12 at `4ab204f`, exit code **1**, on the pinned venv:

```
  §12 Listener alphabet: SUPPORTED — worst surface pair water vs gravel at 1.44x (need >= 1.4x)
  worst within a single actor: 1.41x
  at 25m through a wall it does NOT hold: worst pair metal vs gravel at 1.137x
  HUD vs ears: 4 inverted pair(s) — gravel/concrete, gravel/earth, water/wood, tile/concrete.
  clips: 164   loops checked: 16   blocking defects: 2   warnings: 5
  RESULT: FAIL
```

`clips: 164` against 168 on disk is not a miscount — the audit does not claim
`Audio/Presence/`'s four, which no `tools/audio/` generator writes.

Both blocking defects are [F-002](09-open-questions.md), an open design decision, and
both are gravel: the code says gravel gives the monster away more than concrete and
more than earth, and through a wall gravel measures 17.8 dB and 13.8 dB *quieter*.
CI handles it with a fingerprint baseline (`tools/ci/audio_baseline.json`) that must
name a finding id, so a *new* blocking defect still fails the build —
[CI.md §2.2](../CI.md) argues the design of that gate and it is worth reading before
touching the file.

**Adding materials made the margin worse, and that is the news here.** With five
surfaces the worst separated pair was metal vs tile at 2.10× against a 1.4×
requirement; with eight it is water vs gravel at **1.44×**, and within a single actor
**1.41×**. Both still pass. Neither has much room left, and the next surface anyone
adds is the one that breaks it.

**The venv is pinned and matters.** `tools/ci/requirements-audio.txt` holds
`numpy==2.0.2`, `scipy==1.13.1`, because F-003's figure sits ~0.01 from its 1.4×
threshold and a filter-design change of one sample can invent or hide a defect. The
local venv matches (Python 3.9.6, numpy 2.0.2, scipy 1.13.1).

**The quoted numbers drift every time the bank changes, and they have drifted
again.** `tools/ci/requirements-audio.txt`'s own header still states F-002 as "gravel
measures 32.4 dB quieter than concrete" and F-003 as "wood versus metal at 1.396x";
this page previously recorded a second generation of the same figures (`2.10x`,
`1.89x`, `1.389x`). **Neither set survives:** today's run names different worst pairs
in all three places, because the surface set grew from five to eight and 101 clips
gained a CC0 base layer. The findings' *direction* is unchanged — the alphabet holds
dry and fails through a wall — and that is the only part worth carrying forward.
**Re-run before you quote.**

---

## 3. Models — `tools/blender/`

```bash
BL=/Applications/Blender.app/Contents/MacOS/Blender
$BL --background --factory-startup --python tools/blender/gen_mapkit.py        # 22 pieces + MapKit.manifest.json (§12)
$BL --background --factory-startup --python tools/blender/gen_dressing.py      # 39 Dress_* pieces + Dressing.manifest.json
$BL --background --factory-startup --python tools/blender/gen_props.py         # 7 props + Props.manifest.json — pipes, shelving, debris, the 깜짝 kit
$BL --background --factory-startup --python tools/blender/gen_runner.py        # Assets/Models/Player/Runner.fbx — 17 bones, 8 clips (§05, §11)
$BL --background --factory-startup --python tools/blender/gen_monster_ai.py    # Assets/Models/Characters/Monster.fbx — 29 bones, 7 clips (§06)
$BL --background --factory-startup --python tools/blender/gen_presence.py      # Presence_Figure/Mote.fbx + Audio/Presence (§10)
$BL --background --factory-startup --python tools/blender/gen_gun.py           # Gun_Held.fbx, Gun_Pickup.fbx
```

Or the runner, with the checks applied for you:

```bash
tools/ci/run_blender_generators.sh              # its DEFAULT_GENERATORS list — six names
tools/ci/run_blender_generators.sh gen_props    # one, while iterating
```

> 🔴 **The old form of this section said "all five" and listed `gen_player_model.py`
> as writing `Player.fbx` with 26 bones and 9 clips. There is no `Player.fbx`** —
> `d61d02d` deleted the old co-op character rigs, and the body every one of the twenty
> runners wears is `Assets/Models/Player/Runner.fbx` from **`gen_runner.py`**: 17
> bones, 8 Mixamo-retargeted clips, plus `RunnerArms.fbx`, the 7-bone first-person
> viewmodel added at `b92ae78`. **`gen_runner.py` is not in the runner's
> `DEFAULT_GENERATORS`, and `gen_player_model.py` and `gen_ghost.py` still are** —
> which means CI regenerates two assets that were deleted and never touches the one
> that ships. `gen_player_ai.py` is orphaned the same way: its docstring says it writes
> "the player the game loads" to `Assets/Models/Characters/Player.fbx`, and no such
> file exists. That is exactly the rot `run_blender_generators.sh`'s own header essay
> says it exists to prevent. **[CI.md §2.3](../CI.md) has already filed the gap** —
> "three generators write assets and are in nobody's list: `gen_gun`, `gen_runner`,
> `gen_presence`" — so read it there rather than re-deriving it. It is a repository
> defect, not a doc defect.

Three files in that directory are **libraries, not generators**, and are correctly
absent from the runner's list: `blendkit.py` (shared mesh/rig), `gen_mapkit_detail.py`
and `monster_fit.py`. A fourth, `gen_monster_model.py`, became a library when the
sculpt was adopted — `gen_monster_ai.py` imports its seven §06 clip authors and its
procedural skin pipeline verbatim, and it refuses to write `Monster.fbx` unless run
with `-- --hull`.

### Trap 1 — Blender's exit code lies

> **`blender --background` exits 0 after an uncaught Python exception.**

Measured on Blender 5.2.0 and recorded in [CI.md §2.3](../CI.md) and in the header
comment of `tools/ci/run_blender_generators.sh`:

| Failure | Output | Exit code | Caught by |
|---|---|:--:|---|
| module-level `raise RuntimeError` | `Traceback (most recent call last)` | **0** | traceback grep |
| `SyntaxError` in the generator | `SyntaxError:`, **no** traceback header | **0** | the "wrote nothing" check |
| `blendkit.fail()` | `ASSET_FAILED <reason>` on stderr | 1 | marker grep, exit code |

So the runner applies four independent checks per generator and fails on any of them:
`ASSET_FAILED` present · a Python traceback present · **no `ASSET_REPORT` line** ·
non-zero exit. Rows two and three are why the first three exist. If you invoke
Blender by hand instead of through the script, **grep for `ASSET_FAILED` and for
`ASSET_REPORT`** — the exit code alone is worse than no signal, because it looks like
a pass.

### Trap 2 — `Assets/Models/Characters/` holds exactly one monster

`AssetImportValidator` grades anything in that folder against the *player's* humanoid
policy, so an extra monster variant dropped there reports as broken on every run —
two unadopted variants once cost four failures a run. A generator publishing a
variant writes to `artifacts/`, never to `Assets/Models/Characters/`. Verified
2026-08-12: the folder holds `Monster.fbx`, `Monster.glb` and `Monster.clips.json`,
and nothing else. Note that the *player* no longer lives there at all — it is
`Assets/Models/Player/`, so the humanoid policy this trap describes now grades a
folder with one non-humanoid in it.

The three monster material names are contracts, not labels: `MonsterSkin` binds the
eye glow to whatever matches `Monster_Eyes` (`MonsterSkin.EyeMaterialName`), and
`MonsterAcquireTell` fires §06's acquisition flare on whatever matches `Monster_Maw`.
A creature carrying only a hide disconnects both **without logging anything**
([ASSETS.md §3.1](../ASSETS.md)).

> 🔴 **The eye glow was argued from §04's 관측자** — "괴물의 시야를 본다 → 누가
> 표적인지" — and `MonsterSkin`'s doc comment still says so. The role is deleted; the
> two lenses are not. The reason that survives is the one the same comment gives
> first: two separated points read as a face **and as a facing** at a range where the
> body is a smudge, which is the only distance cue left when the creature is forty
> pixels tall. That is worth more to twenty runners than it was to one 관측자.
> The maw is a different signal and the two must never be confused — the maw firing
> means §06 has entered 추격 and you have about three seconds to find a corner; the
> eyes mean only that the creature exists, and they never change.

---

## 4. Textures — `tools/textures/`

```bash
python3 tools/textures/gen_textures.py                     # → Assets/Textures/** + Textures.manifest.json
python3 tools/textures/gen_textures.py --only Floor_Wood   # one, while iterating
```

Then bind them into materials, which is a separate Unity step:

```bash
Unity -batchmode -quit -nographics -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.TextureImport.ProceduralTextureMaterials.Build
```

> **`ProceduralTextureMaterials.Build` writes the five contractual floor materials
> in place at `Assets/Scenes/Generated/Materials/Floor_*.mat`. It must never delete
> and recreate them** — the generated scene references them by GUID, and a fresh
> asset mints a fresh GUID and silently unbinds every floor in the map
> ([ART.md §2.1](../ART.md)).

`ContractualFloors` in that file is still exactly five —
`Floor_Wood · Tile · Gravel · Concrete · Metal` — and a missing one throws rather than
warning. Everything else it builds goes to `Assets/Textures/Materials/`. **Eight
`Floor_*.mat` sit in the scene folder today**, so `Floor_Water`, `Floor_Earth` and
`Floor_Carpet` arrived by another route while `FloorMaterial` grew to eight members.
If you add a surface to `FloorMaterial`, adding it to `ContractualFloors` is the edit
that makes it fail loudly instead of quietly.

The generator's own load-bearing rule, asserted in code: **darkness comes from
lighting, never from painting the albedo black.** Every albedo is calibrated to a
mean inside `[ALBEDO_MIN_LINEAR, ALBEDO_MAX_LINEAR]` *linear* and the run fails if
one drifts out. A real basement wall reflects 20–40 %; painted black it reflects 2 %
and no lamp intensity recovers the shape of the room.

---

## 5. The trap that catches everyone: Unity does not re-run post-processors

> **Unity applies an `AssetPostprocessor` when an asset is imported. Regenerating the
> file on disk with the same path does not necessarily re-import it, and never
> re-applies the policy to assets already in the database.**

So after regenerating anything:

```
Horror ▸ Assets ▸ Reimport Audio And Models      →  AssetImportValidator.ReimportAll()
Horror ▸ Assets ▸ Validate All Asset Imports     →  AssetImportValidator.ValidateAllBatch()
```

`ReimportAll` calls `AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)`
for every governed path under both roots, inside a
`StartAssetEditing`/`StopAssetEditing` pair.

There is a second, subtler half. When you change the *policy* rather than the asset,
nothing on disk changed at all, so nothing reimports. That is what
`AssetImportPolicy.PolicyVersion` (currently `2`) is for: both post-processors return
it from `GetVersion()`, and bumping it makes Unity treat every governed asset as
stale. **Change a rule in `AssetImportPolicy` and bump `PolicyVersion` in the same
edit** — otherwise the new rule only reaches assets somebody happens to touch later,
which is a slower version of not having a policy.

### Why any of this matters

One wrong checkbox silently deletes a mechanic. Unity does not spatialise a stereo
`AudioClip`: a 2-channel clip on a 3D `AudioSource` plays at a fixed level with no
attenuation and no panning, and it does not error or warn. **A stereo footstep makes
the monster equally loud from everywhere** — and §12 makes 발소리 the map
(「소리 → 바닥 재질이 지도다」), so that one checkbox deletes the map's audio channel
for all twenty runners at once. §13's 근접 음성 fails the same way.

> 🔴 **This paragraph used to say "one wrong checkbox silently deletes a role", and
> named §04's 청음사, whose single ability was reading the monster's
> 위치 · 거리 · 이동 방향 from sound.** The role is deleted and the failure mode is
> strictly worse than it was: it used to cost one player in four their whole reason to
> exist, and it now costs every player the only positional information the game gives
> them for free. It also used to name §09's 유령 rattle — `GhostRattleCooldownSeconds`
> and `GhostRattleRange` are gone, and §09 has no constants at all now.

That is why the import check is a **gameplay invariant**:

```bash
Unity -batchmode -quit -nographics -silent-crashes -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log
```

Last recorded run, **2026-08-01** ([TESTING.md](../TESTING.md), "The full sweep"):
`166 audio inspected, 0 failing` / `86 models inspected, 0 failing`. On disk on
2026-08-12 there are **168 WAVs and 75 FBX**, so the record is eleven days and two
different sets old, in both directions — which is the whole reason this page says
**count, do not quote**. Do not subtract the two: TESTING.md records this tool
returning different verdicts eighteen minutes apart on an unchanged file, so a single
green from it is not yet a gate. **Re-run it rather than reasoning about it.**

---

## 6. Read before changing

| Before you | Read | Then run |
|---|---|---|
| touch a footstep generator | [ASSETS.md §2.1](../ASSETS.md), [F-002 and F-003](09-open-questions.md) | the generator, then `verify_audio.py` — the alphabet is a *set* property, and its worst pair is at 1.44× against a 1.4× floor |
| add a surface to `FloorMaterial` | §2 above | `gen_footsteps.py` (all eight, not `--only`), `verify_audio.py`, and add the name to `ProceduralTextureMaterials.ContractualFloors` |
| add a monster sound | [ASSETS.md §2.2](../ASSETS.md) | note that §06's 정지 is **deliberately silent** — "침묵이 가장 무서운 소리다". Do not add a clip there |
| change a map-kit dimension | [ASSETS.md §3.2](../ASSETS.md), [Where numbers live §3](03-where-numbers-live.md) | `run_blender_generators.sh`, then `MapPipeline` (not `MapSceneGenerator`) to regenerate, §12 validation, `MonsterChaseTests` |
| change the monster mesh or materials | [ASSETS.md §3.1](../ASSETS.md), [ART.md §3.14](../ART.md) | `MonsterShot.StageBatch` **without** `-nographics` |
| change the runner mesh or its clips | `gen_runner.py`'s own header essay | `gen_runner.py`, reimport, then `HorrorGame ▸ Play ▸ Build Solo Playtest Scene` — `SoloPlaytest.AnimationSlots` names all eight takes (Idle · Walk · Run · Crouch · CrouchWalk · GunIdle · GunWalk · Death) and audits that every one is wired, so a renamed take is reported rather than silently `{fileID: 0}` |
| change an import rule | §5 above | bump `AssetImportPolicy.PolicyVersion`, reimport, validate |
| change a texture | [ART.md §2.1](../ART.md) | `gen_textures.py`, then `ProceduralTextureMaterials.Build`, then review shots |
