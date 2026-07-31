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
통과!  - 실패:     0, 통과:   451, 건너뜀:     0, 전체:   451, 기간: 363 ms
```

**451 tests in a third of a second, and Unity never opens.** Every tuned number and
every rule lives here: §05's speed multipliers, §06's aggro and state machine, §07's
threat curve, §08's economy, §03's clues and confusion pairs, §12's map rules.

This works because the rules core has no engine dependency. The same `.cs` files
Unity compiles are pulled into a .NET project by a glob, so there is one copy of the
truth, checked two ways. `FoundationTests.CoreSources_DoNotReferenceUnityEngine`
fails the build if anyone breaks that arrangement.

Run it before every commit. If it is green, the game's rules are intact.

---

## The full sweep, in the order worth running

### 1 · Rules — 451 tests

```bash
dotnet test /Users/doogi/horror-game/core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

### 2 · Everything compiles, including the simulator

```bash
dotnet build /Users/doogi/horror-game/core/HorrorGame.sln -c Release
```

```
    경고 11개
    오류 0개
```

**Do not skip this because §1 and §3 were green.** It is the only command in this
document that can see a break in `HorrorGame.Sim`. On 2026-08-01 it was failing on two
errors while `dotnet test` passed 448/448, Unity compiled 0 errors and the full Unity
suite passed 112/112 — the balance simulator would not build at all and nothing else
noticed, because nothing else references it. `HorrorGame.Sim.csproj` lists the
engine-free map-authoring sources **by name** rather than globbing them, deliberately,
so a new file in `Editor/SceneGen/` is not picked up automatically. See
[BLOCKERS.md B-006](BLOCKERS.md#b-006).

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

```
EditMode   total 71 passed 71 failed 0 result Passed
PlayMode   total 53 passed 53 failed 0 result Passed
```

**124 of 124 as of 2026-08-01 06:20**, and 575 of 575 with core's 451. Older
revisions of this file said EditMode 55 and PlayMode 27, then 70 and 42; all were
stale. Re-read the XML rather than trusting this line.

In the editor these are `Horror ▸ Test ▸ Run EditMode + PlayMode`.

Run **both** platforms. PlayMode is the only one that can catch a menu button that
does nothing — `UiFlowTests` found exactly that after a map regeneration dropped the
match scene from Build Settings, and nothing in EditMode or the compiler could see it
(see BLOCKERS.md B-005).

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
complete 1830 (100.0 %, need 98 %) · islands 1 · monster reach 19/19
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
Model import settings: 86 inspected, 0 failing, 0 warnings.
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
number: matches currently resolve in **7.2 minutes** against §01's 25–35 minute target,
so the late game the economy is meant to shape is reached by a minority of matches
rather than by the normal one. The economy does now run — 0.70 of one of everything
earned, 283 강화 손전등 bought per 500 matches against 23 — so §16-2 is measurable for
the first time.

> **This figure was 2.5 minutes until 2026-08-01, and that was not staleness.** The
> simulator built its own four-zone ring instead of reading the level, so every
> economy number ever taken from it described a building the game does not ship.
> `SimMap` now calls `FirstMapSketch.Build`. **Read the first five lines of the run
> output — they are the building — before quoting anything under them.**

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
runner-test rate against §12's 5–7/10 target band. `horrorsim map` prints the same
report headless, from the same sources, and the two agree exactly.

> 🔴 **This menu item currently fails and writes nothing — see
> [BLOCKERS.md B-007](BLOCKERS.md#b-007).** The checklist gained a 17th rule,
> `sight-break-spacing`, and 요양원 지하 5층 does not satisfy it, so the gate described
> above is doing exactly what it says and refusing the map the game ships. The
> committed `Map_FirstSketch.unity` was written while the checklist still had 16 rules
> and still runs fine; what is blocked is re-rolling or editing the map.
>
> Headless, the same verdict:
>
> ```
> §12 map validation: failed [sight-break-spacing]
> §12 rejects this map, so no measurement taken from it describes the shipped game.
> ```
>
> A map can pass every checklist rule and still grade 10/10 TooEasy — §12's checklist
> is necessary, not sufficient, pinned by
> `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`. 요양원 지하 5층 now
> fails the checklist **and** grades **10/10 TooEasy**, outside the 5–7 band; the
> three-storey building it replaced graded 7/10 Balanced. Read the two lines the report
> prints underneath the grade: `164/164 escapable` rules out an unlucky ten-point
> sample, and `79 corners … mean 4.1 m, 0 inside the band` is the cause — the same
> geometry the 17th rule fails on. See
> [BALANCE-FINDINGS F-007](BALANCE-FINDINGS.md#f-007).

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

## Building a standalone player

This produces a `.app` or `.exe` that runs on a machine with no Unity installed.
Every command below was run on this machine and its real output is quoted.

### The one command

```bash
cd /Users/doogi/horror-game
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -silent-crashes -projectPath unity/HorrorGame -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine -buildPlatform macos-arm64 -buildConfig development -logFile /tmp/b.log
echo "exit=$?"
```

```
exit=0
  macOS Apple silicon                        Development Mono   OK          259.83 MB  194s
```

Output is `dist/<platform>/`, wiped and rewritten each time.
`dist/last-build-summary.txt` holds the table above; `dist/<platform>/build-report.txt`
holds everything else — commit, backend, scene load order, sizes and every error.

> **Never pass `-quit`.** `BuildFromCommandLine` owns the exit code and calls
> `EditorApplication.Exit` itself. `-quit` overrides a failure with 0, which is how a
> broken build reports success. This is the same trap as the test runner above.

Exit codes: `0` ok · `1` unexpected · `2` arguments · `3` scenes · `4` build failed ·
`5` IL2CPP required but unavailable · `7` scripts do not compile · `8` module missing.

### Running it

```bash
open /Users/doogi/horror-game/dist/macos-arm64/HorrorGame.app
```

**It boots into the front end**, not into a match. Scene 0 is `Bootstrap`; 시작 loads
`Map_FirstSketch_Solo` through `GameShell.LoadMatchRoutine`, and
`UiFlowTests.Menu_ComesUp_AndStartReachesTheMatchScene` pins that path.

> Earlier revisions of this file said the player "boots straight into
> `Map_FirstSketch_Solo` … rather than opening the not-yet-wired bootstrap menu". That
> was true until the front end landed and took slot 0. The menu is wired now.

To get a player that starts a match with no clicking — useful in batch, and the only
way to check the match path when you cannot drive a GUI — put the solo scene first
before building:

```bash
$U -batchmode -quit -nographics -projectPath $P \
   -executeMethod HorrorGame.EditorTools.Playtest.StandaloneBuild.PrepareBatch -logFile /tmp/prep.log
```

```
[StandaloneBuild] Build scenes, in load order:
  [x] Assets/Scenes/Map_FirstSketch_Solo.unity
  [x] Assets/Scenes/Bootstrap.unity
  [x] Assets/Scenes/Map_FirstSketch.unity
```

That edits `ProjectSettings/EditorBuildSettings.asset`, which is tracked — restore it
with `git checkout --` when you are done, or the next build ships without its menu.

To confirm it actually reached a match rather than merely opening a window, read the
player's own log:

```bash
grep -E "\[Match\] seed|Exception" ~/Library/Logs/DefaultCompany/HorrorGame/Player.log
```

```
[Match] seed 20260731 · 4 clues (§03 needs 3) · planned round trips 4 · 5 zones · local role Runner
```

That line is `MatchDirector.BeginMatch` completing. No `Exception` line means §14's
guidance overlays came up with it — `PlaytestGuidanceScreen` builds its canvas in the
same frame and would throw here if it could not.

Two log lines are expected and harmless:

- `[Steam] Running offline on development App ID 480 (Spacewar).` — Steam is not
  running. Local hosting works, invites and in-game voice do not. §14 step 3 plays
  this way on purpose.
- `Failed to create agent because there is no valid NavMesh` — a load-order artifact
  that appears **only in a player**, never in the editor. The scene's NavMesh comes
  from a `NavMeshSurface` whose `OnEnable` calls `AddData()`, and the monster's native
  `NavMeshAgent` is enabled during the same scene load with no ordering guarantee
  between them. It has no lasting effect: `MonsterAgent` sets `updatePosition = false`
  and drives the transform from `MonsterBrain`, which paths through the *global*
  `NavMesh.CalculatePath` in `NavMeshWorldProbe` — and those succeed once the surface's
  data is in. The warning that would prove real breakage,
  `[Monster] NavMeshAgent is off the NavMesh at ...`, does not appear.

### Which backend this machine can produce

| Target | Development | Release | Shippable on Steam |
|---|---|---|---|
| macOS arm64 | Mono, ~3 min | **IL2CPP** — but see the caveat below | yes, for the macOS depot |
| Windows x64 | Mono | **Mono only, never IL2CPP** | no |

Development is always Mono by design: it links in seconds and a managed debugger
attaches. Release wants IL2CPP and gets it only when the host OS matches the target's
native toolchain.

**Windows IL2CPP cannot be produced on a Mac at all.** IL2CPP transpiles to C++ and
then calls the target's own compiler, which cannot cross-compile. A Windows Release
build made here falls back to Mono and the pipeline drops
`MONO-FALLBACK-DO-NOT-SHIP.txt` into the output folder. Steam's audience is mostly
Windows, so a shipping Windows build needs a Windows machine or a Windows CI runner —
see [STEAM-RELEASE.md](STEAM-RELEASE.md).

### macOS IL2CPP needs one environment variable on this machine

```bash
export CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -silent-crashes -projectPath unity/HorrorGame -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine -buildPlatform macos-arm64 -buildConfig release -logFile /tmp/b.log
```

Without it the build fails at exit 4 with:

```
[Error] Postprocess built player: Building Library/Bee/artifacts/.../pch-cpp.pch failed with output:
.../libil2cpp/codegen/il2cpp-codegen.h:24:10: fatal error: 'cmath' file not found
```

This is **not** a Unity fault and **not** the absence of Xcode. This machine's Command
Line Tools are damaged: `/Library/Developer/CommandLineTools/usr/include/c++/v1` holds
11 files where it should hold 185, and clang prefers that directory over the complete
copy inside the SDK. A two-line `.cpp` that includes `<cmath>` fails to compile with
plain `clang++`, no Unity involved — that is the whole bug.

The export points clang at the SDK's complete copy and is a workaround. The real fix is
to reinstall the tools, which is a system change to make deliberately:

```bash
sudo rm -rf /Library/Developer/CommandLineTools
sudo xcode-select --install
```

After that the export is unnecessary. Note that `Horror ▸ Build ▸ Report Build
Environment` reports `release backend IL2CPP` for macOS either way — it asks whether the
host OS matches the target, not whether the C++ toolchain is intact.

### Both platforms at once

```bash
export CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -silent-crashes -projectPath unity/HorrorGame -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine -buildPlatform windows-x64,macos-arm64 -buildConfig release -logFile /tmp/b.log
```

```
exit code : 0

  Windows x64                                Release     Mono   OK          144.09 MB  316s
  macOS Apple silicon                        Release     IL2CPP OK         1343.32 MB  44s
```

The macOS figure is the whole output folder. The shippable part —
`dist/macos-arm64/HorrorGame.app` — is 159 MB; the rest is
`HorrorGame_BackUpThisFolder_ButDontShipItWithYourGame`, IL2CPP's C++ intermediates,
which the name asks you not to ship.

Menu equivalents live under `Horror ▸ Build`.

### Mirror's meta-file error is expected

Every build prints this and it does not fail the build:

```
[Error] Prebuild Cleanup and Recompile: Asset Packages/com.mirrornetworking.mirror/Mirror/Assets
has no meta file, but it's in an immutable folder. The asset will be ignored.
```

`com.mirrornetworking.mirror@96.6.4` on OpenUPM repacks Mirror's git repository
*including its `Mirror` submodule*, and that submodule's Unity project root —
`Mirror/Assets` — legitimately has no `.meta`, because in the original repository
nothing sits above it to reference it. Inside a package Unity demands one for every
asset, so it logs an error. Everything below the folder has its own `.meta` and
compiles into the player: `Mirror.dll`, `Mirror.Components` and `Mirror.Transports`
are all in the build, which is what 70 EditMode tests sit on top of.

It cannot be fixed from this repository. Registry packages live in
`Library/PackageCache`, which Unity treats as immutable — writing the missing
`Assets.meta` there was tried, and Unity deleted the file and logged *"The following
asset(s) located in immutable packages were unexpectedly altered"*. The folder is
regenerated from the tarball on every resolve, so nothing written into it survives a
fresh clone. Fixing it for real means moving to a version that packs correctly, or
vendoring Mirror's 2,955 files into the repository — both deliberate decisions, and
neither belongs in a build fix.

`BuildPipelineKnownDefects` names it, matching on both the symptom *and* that exact
package path. Matching errors are printed, counted, and listed in `build-report.txt`
under **known third-party defects** — they simply do not fail the build. Every other
error still does, including a missing `.meta` in any other package. Delete the entry
when the dependency moves.

> **This is why `BuildOptions.StrictMode` is not set.** StrictMode fails a build when
> any error was logged, and reports it as
> `Failed to process scene before export: '<scene>'` — naming a scene that is not the
> problem and never naming the error that is. It made every scene in this project
> unbuildable, including the near-empty bootstrap menu, because the error being counted
> was Mirror's and was logged before any scene was touched. The pipeline enforces the
> same rule itself in `BuildPipelineRunner.ReportBuildMessages`, and can say what
> happened. Proof it still works: the first IL2CPP attempt above failed at exit 4 on the
> `'cmath'` error, reported as `1 this project's, 1 known third-party defect`.

Steam depot upload is in `tools/steam/` — see [STEAM-RELEASE.md](STEAM-RELEASE.md).
It dry-runs without contacting Steam, and refuses to upload while the App ID is still
480.

---

## What to check when something breaks

| Symptom | Look here first |
|---|---|
| Rules behaving oddly | `dotnet test` — 451 tests name the section they defend |
| A monster ignoring the map | `NavMeshWorldProbe` must use path length, not straight-line distance. §12's S-corridor argument dies otherwise |
| The Listener useless | Section 5 — a positional clip imported as stereo |
| A map that plays badly | `HorrorGame ▸ Scene Gen ▸ Report Map Quality` |
| Balance feels wrong | The simulator, then [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) |
| Unity batch command fails | The editor holds the project lock. Close it |
| `Failed to process scene before export` | Almost never the scene. It is `BuildOptions.StrictMode` failing on an unrelated logged error and blaming the first scene — see *Mirror's meta-file error is expected* above. Build one scene at a time to prove it: if the bootstrap menu fails too, the scene is innocent |
| A standalone build fails and says nothing | `dist/<platform>/build-report.txt` — the pipeline copies every message off Unity's `BuildReport`, which the raw log does not do reliably |
| `'cmath' file not found` during a Release build | Damaged Command Line Tools, not Unity. See *macOS IL2CPP needs one environment variable* above |
| A number disagrees with the design | `GameConstants.cs` is the only authority. A literal anywhere else is a bug |
