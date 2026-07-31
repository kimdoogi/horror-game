# How to test this game

Every command here was run on this machine and its real output is quoted. If one
does not reproduce, that is a bug — say so rather than working around it.

Put these two lines in your shell profile so the .NET commands work anywhere:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

---

## The one command to run constantly

```bash
dotnet test /Users/doogi/horror-game/core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

```
통과!  - 실패: 0, 통과: 448, 건너뜀: 0, 전체: 448, 기간: 325 ms
```

**448 tests in a third of a second, and Unity never opens.** Every tuned number and
every rule lives here: §05's speed multipliers, §06's aggro and state machine, §07's
threat curve, §08's economy, §03's clues and confusion pairs, §12's map rules.

This works because the rules core has no engine dependency. The same `.cs` files
Unity compiles are pulled into a .NET project by a glob, so there is one copy of the
truth, checked two ways. `FoundationTests.CoreSources_DoNotReferenceUnityEngine`
fails the build if anyone breaks that arrangement.

Run it before every commit. If it is green, the game's rules are intact.

---

## The full sweep, in the order worth running

### 1 · Rules — 448 tests

```bash
dotnet test /Users/doogi/horror-game/core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

### 2 · Everything compiles, including the simulator

```bash
dotnet build /Users/doogi/horror-game/core/HorrorGame.sln -c Release
```

### 3 · Unity compiles — 229 scripts and every package

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame -logFile /tmp/u.log; grep -cE '^Assets/.*error CS' /tmp/u.log
```

Prints `0`. Anything else is the error count — read `/tmp/u.log`.

> Only one Unity process may hold the project lock. Close the editor first, or
> expect the batch run to fail with a lock message.

### 4 · Unity tests — EditMode and PlayMode

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath /Users/doogi/horror-game/unity/HorrorGame -runTests -testPlatform PlayMode -testResults /tmp/playmode.xml -logFile /tmp/t.log
```

Swap `PlayMode` for `EditMode` for the other suite. To run one fixture, add
`-testFilter "MonsterChaseTests"`.

> **Never add `-quit` to a test run.** Unity's test runner is asynchronous, and
> `-quit` shuts the editor down before it writes results. The run then reports
> nothing, exits 0, and looks green — which is exactly how a failing EditMode test
> went unnoticed here for a while. This document told you to use `-quit`; that was
> wrong, and this is the correction.

Read the results properly rather than trusting the exit code:

```bash
python3 -c "import xml.etree.ElementTree as ET,sys; r=ET.parse('/tmp/playmode.xml').getroot(); print(r.get('total'),r.get('passed'),r.get('failed'))"
```

In the editor these are `Horror ▸ Test ▸ Run EditMode + PlayMode`.

The chase suite is the one to watch — it is §14's first verification question turned
into something a machine can answer:

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath /Users/doogi/horror-game/unity/HorrorGame -runTests -testPlatform PlayMode -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log
```

```
total=4 passed=4 failed=0 result=Passed
```

### 4b · NavMesh connectivity — the check that keeps the antagonist alive

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -nographics -projectPath /Users/doogi/horror-game/unity/HorrorGame -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log
```

```
complete 630 (100.0 %, need 98 %) · islands 1 · monster reach 19/19
```

The monster paths through the NavMesh. A fragmented surface produces no error — the
agent walks to the end of a partial path and stops, which reads as bad AI. This once
cost the game its antagonist entirely; see `docs/BLOCKERS.md` B-001.

Note it is necessary and not sufficient: the audit asks `CalculatePath`, while the
monster walks `NavMeshPath.corners` one at a time. Those are different questions, and
a `NavMeshLink` answers the first without the second. That is why the chase test in
§4 above exists — run both.

### 5 · Asset import settings — the check that keeps a role alive

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log
```

```
Audio import settings: 166 inspected, 0 failing, 0 warnings.
Model import settings: 47 inspected, 0 failing, 0 warnings.
```

This is not housekeeping. A positional clip imported as stereo will not be
spatialised by Unity, and §04's Listener localises the monster **by ear alone**. One
wrong checkbox silently deletes a role, and nothing else in the project would notice.

### 6 · Audio audit — §12's five-surface alphabet

```bash
/Users/doogi/horror-game/tools/audio/.venv/bin/python /Users/doogi/horror-game/tools/audio/verify_audio.py
```

Builds the full 5×5 spectral separation matrix across wood, tile, gravel, concrete
and metal, dry and at each occlusion step. §12 requires the Listener to tell zones
apart by floor material, so this is a **gameplay invariant**, not an audio nicety.
Re-run it after touching any generator.

Currently reports one blocking defect and two warnings — see
[BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) F-002 and F-003.

### 6b · The monster is visible at 15 m — the check that keeps §04 playable

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame -executeMethod HorrorGame.Gameplay.MonsterEditor.MonsterShot.StageBatch -shotTag stage -logFile /tmp/mon.log
```

**No `-nographics`**, or every frame is black.

Stands the creature against a dark §12 corridor section with every light in the scene
switched off except §03's beam, at 8 / 12 / 15 / 20 m, and photographs each distance
twice — once with it and once without. Three numbers per frame, each gating:

| Measure | Floor | What it answers |
|---|:--:|---|
| `contrast` | 0.015 | Mean per-pixel luminance separation from the wall around it. ≈4 code values, which is where an observer who has not been told where to look picks a shape out of a dark field. |
| `coverage` | 0.40 | Fraction of the silhouette that differs from the empty frame at all. Separates a creature from a glint. |
| `peak` | 0.040 | 95th-percentile change inside the silhouette. ≈10 code values — one genuinely legible feature, which is what turns a smudge into a creature. |

The silhouette is ground truth, rendered as unlit white on black with fog and grading
off. Taking "the pixels that changed" as the silhouette is circular: a creature that
rendered at exactly the wall's luminance would have an empty footprint and score a
perfect coverage of nothing.

This is a **gameplay invariant**. §12 requires 1~2 관측 지점 per zone giving
"15m 거리에서 안전하게 괴물을 볼 수 있는 지점" and says without them 관측자는 죽으러
가야 한다; §12's 주자 table marks 10 m as the first distance an aggro pull reliably
survives a corner. Both roles need a creature that can be seen from further than
§03's 12 m beam reaches.

`-rimStrength`, `-rimPower`, `-rimFloor`, `-fogResponse` and `-eyeGlow` override the
shader without touching an asset, and the first and last take comma lists and are
crossed — a calibration sweep is one editor launch. `-ambientFill 0.22 -rimStrength 0
-fogResponse 1 -eyeGlow 0` reproduces the creature as it was before this pass, so a
before/after comparison is measured by one metric rather than quoted from an old log.

`MonsterShot.Batch` is the same measurement in the real map at 15 / 8 / 3 m.

### 7 · The balance simulator

```bash
dotnet run -c Release --project /Users/doogi/horror-game/core/HorrorGame.Sim -- run --matches 500 --seed 1
```

500 matches against the design's own targets. Also:

```bash
# a single match, verbose
dotnet run -c Release --project core/HorrorGame.Sim -- match --seed 42

# prove a seed reproduces — §13's whole diagnosis loop depends on this
dotnet run -c Release --project core/HorrorGame.Sim -- replay --seed 42 --times 3

# move one constant and watch what changes
dotnet run -c Release --project core/HorrorGame.Sim -- sweep weight-mul-light --matches 400 --seed 1
dotnet run -c Release --project core/HorrorGame.Sim -- sweep loot-value --matches 400 --seed 1
```

Read [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) F-006 before trusting any economy
number: matches currently resolve in 2.5 minutes against §01's 25–35 minute target,
so the late game the economy is meant to shape does not happen yet.

---

## Playing it

### First open

```bash
open -a "Unity Hub"
```

Add `/Users/doogi/horror-game/unity/HorrorGame`. First import takes several minutes —
it resolves Mirror, Steamworks.NET and FizzySteamworks and imports 213 assets.

### Generate the map

`HorrorGame ▸ Scene Gen ▸ Generate First Map`

Builds §12's 첫 맵 스케치 from the kit — zone B tiled hall, zone A wood, joined by an
S-corridor — then runs Core's `MapValidator` as a gate and `RunnerTest` as a grade.
**Generation fails if any §12 rule breaks**, so a bad map cannot reach you.

`HorrorGame ▸ Scene Gen ▸ Report Map Quality` prints the §12 checklist result and the
runner-test rate against §12's 5–7/10 target band.

> A map can pass all sixteen checklist rules and still grade 10/10 TooEasy. §12's
> checklist is necessary, not sufficient — pinned by
> `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`.

### Two players on one PC — §14 step 2

`HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)`

§14 puts this before Steam on purpose: the prototype is meant to be verified with
Discord voice and local hosting first.

### Answering §14's five verification questions

§14 says questions 1 and 2 decide the project, and that they cannot be settled on
paper — "직접 만져봐야 나온다".

| # | Question | How to test it now |
|:--:|---|---|
| 1 | **추격이 재밌는가?** | Two instances. One takes aggro and runs for the S-corridor. Does breaking away feel earned? |
| 2 | **곁눈질 딜레마가 작동하는가?** | The Player Feel Harness shows live speed, the §05 directional multiplier and your margin over the monster. Turn the mouse and watch the margin fall. If you never agonise over looking back, §05's 65% is wrong. |
| 3 | **"지금 나갈까?" 갈등이 생기는가?** | Needs a full loop with loot and the shop. **Blocked by F-006** — a 2.5-minute match never creates the pressure. |
| 4 | **"6이었나 9였나" 대화가 나오는가?** | Two instances, one reads a clue and speaks it aloud. The confusion pairs are implemented and tested; whether they produce the argument is a human question. |
| 5 | **청음사가 방향·거리를 구별하는가?** | **Headphones required.** §05 makes 3D audio core. Section 6 above says the five surfaces separate 2.13× dry but only 1.396× at 25 m through a wall — so expect this to work close and fail far. That is F-003. |

Questions 1, 2, 4 and 5 are testable today. Question 3 is not, and F-006 explains why.

---

## Regenerating assets

Everything is generated by code — no samples, no downloads, no licensed content.

```bash
# sounds (166 WAVs)
tools/audio/.venv/bin/python tools/audio/gen_footsteps.py
tools/audio/.venv/bin/python tools/audio/gen_monster_audio.py
tools/audio/.venv/bin/python tools/audio/gen_ambience.py
tools/audio/.venv/bin/python tools/audio/gen_items.py
tools/audio/.venv/bin/python tools/audio/gen_ui.py

# models (47 FBX)
BL=/Applications/Blender.app/Contents/MacOS/Blender
$BL --background --factory-startup --python tools/blender/gen_monster_ai.py
$BL --background --factory-startup --python tools/blender/gen_player_model.py
$BL --background --factory-startup --python tools/blender/gen_mapkit.py
$BL --background --factory-startup --python tools/blender/gen_props.py
```

Each generator measures its own output and refuses to emit something unusable —
silence, clipping, a DC offset, an empty mesh, a model at the wrong unit scale, or
animations exported twice.

> **Blender's `--background` exits 0 even after a Python exception.** Never trust its
> exit code. Grep for `ASSET_FAILED`, which is what the generators emit on failure.

After regenerating, reimport so the post-processors run — Unity does not apply them
retroactively to assets already in the database:

`Horror ▸ Assets ▸ Reimport Audio And Models`, then `Horror ▸ Assets ▸ Validate All
Asset Imports`.

---

## Building

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath unity/HorrorGame -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine -buildTarget Win64 -logFile /tmp/b.log
```

Or `Horror ▸ Build ▸ Windows x64 — Release`. Output lands in `dist/`.

> **macOS cannot produce an IL2CPP Windows player — only Mono.** Steam's audience is
> mostly Windows, so a shipping build needs a Windows machine or the CI runner in
> `.github/workflows/unity.yml`. The build script warns loudly when it falls back;
> do not ship a build that printed that warning.

Steam depot upload is in `tools/steam/` — see [STEAM-RELEASE.md](STEAM-RELEASE.md).
It dry-runs without contacting Steam, and refuses to upload while the App ID is still
480.

---

## What to check when something breaks

| Symptom | Look here first |
|---|---|
| Rules behaving oddly | `dotnet test` — 448 tests name the section they defend |
| A monster ignoring the map | `NavMeshWorldProbe` must use path length, not straight-line distance. §12's S-corridor argument dies otherwise |
| The Listener useless | Section 5 — a positional clip imported as stereo |
| A map that plays badly | `HorrorGame ▸ Scene Gen ▸ Report Map Quality` |
| Balance feels wrong | The simulator, then [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) |
| Unity batch command fails | The editor holds the project lock. Close it |
| A number disagrees with the design | `GameConstants.cs` is the only authority. A literal anywhere else is a bug |
