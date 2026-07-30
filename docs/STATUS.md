# Project status

**Verified:** 2026-07-31 · **Unity:** 6000.3.21f1 · **dotnet:** 9.0.x (`DOTNET_ROOT=$HOME/.dotnet`)

This document records what was **personally observed to pass**, what exists but was
never executed, what is missing, and every known defect with a pointer to where it is
tracked. Every green claim below is followed by the command and its verbatim final
line. Nothing is marked verified on the strength of "it compiles" or "an agent said so".

**Headline:** the code is green — 448 core tests, 79 Unity tests, a clean asset
validation and a clean Unity compile. The audio audit fails, and that failure is
expected and tracked. The one thing a reader should not miss is **S-001**: §12's floor
material never reaches the runtime on a generated map, so the Listener's audio channel
— the mechanic §04 is built on — is silent, and no test in the project detects it.

---

## 1 · Verified green

Each of these was run to completion during this pass. Exit codes were captured.

### Core tests — 448 passed, 0 failed

```
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```
```
통과!  - 실패:     0, 통과:   448, 건너뜀:     0, 전체:   448, 기간: 322 ms - HorrorGame.Core.Tests.dll (net9.0)
```
`exit=0`

**No regression.** The baseline before this workflow was 387. It is now 448 — 61 tests
were added and none were removed, skipped or weakened. There are 0 skipped tests, so
the count is not inflated by disabled cases.

### Solution build (Release) — 0 warnings, 0 errors

```
dotnet build core/HorrorGame.sln -c Release
```
```
    경고 0개
    오류 0개
```
`exit=0` — builds `HorrorGame.Core` (netstandard2.1), `HorrorGame.Sim` (horrorsim), `HorrorGame.Core.Tests`.

### Unity compile — 0 errors

```
Unity -batchmode -quit -nographics -silent-crashes \
  -projectPath unity/HorrorGame -logFile /tmp/unity_verify.log
grep -E '^Assets/.*error CS' /tmp/unity_verify.log
```
```
(no output — 0 matching lines; 0 occurrences of "error CS" anywhere in the log)
```
`exit=0`, log ends `Exiting batchmode successfully now!`

This was checked for the failure mode where Unity exits 0 without compiling anything:

* the log shows real work — `DisplayProgressbar: Compiling Scripts` and
  `AssetDatabase: script compilation time: 0.665394s`, with `bee_backend … ScriptAssemblies`;
* all **16** `.asmdef` files produced a matching DLL in `Library/ScriptAssemblies`
  (set difference of asmdef names against built DLL names is empty), so no assembly
  failed silently and left a stale artifact behind.

**No cross-agent compile errors were found and none had to be fixed.** The seam damage
that was expected here is real, but it is behavioural rather than syntactic — see
S-001 and S-003.

### Unity tests — 79 passed, 0 failed

```
Unity -batchmode -nographics -silent-crashes -projectPath unity/HorrorGame \
  -logFile /tmp/unity_tests.log \
  -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine \
  -testSuites editmode,playmode -testRequireTests
```
```
[TestRunner] Summary
  editmode    52 passed     0 failed     0 skipped     0 inconclusive  in 0.1s  -> /Users/doogi/horror-game/dist/test-results/editmode-results.xml
  playmode    27 passed     0 failed     0 skipped     0 inconclusive  in 2.7s  -> /Users/doogi/horror-game/dist/test-results/playmode-results.xml
  total: 79 (79 passed, 0 failed, 0 skipped, 0 inconclusive) in 6.6s
```
`exit=0`

`-testRequireTests` was passed deliberately, so a suite that ran zero tests would have
been reported as a failure rather than as a quiet pass. Note `-quit` must **not** be
passed to this entry point; the run is asynchronous and the exit code comes from the
runner's own callback.

Counts confirmed independently of the log by parsing the NUnit XML: `editmode-results.xml`
has 52 `<test-case>` elements, `playmode-results.xml` has 27, both `result="Passed"`.
That cross-check only became possible after fixing S-002 below.

| Suite | Assembly under test | Tests |
|---|---|---|
| EditMode | `HorrorGame.Audio` | 26 |
| EditMode | `HorrorGame.UI` | 26 |
| PlayMode | `HorrorGame.Net` (+ `HorrorGame.Steam`) | 11 |
| PlayMode | `HorrorGame.Gameplay.Player` | 16 |

### Asset validation — clean

```
Unity -batchmode -quit -nographics -silent-crashes -projectPath unity/HorrorGame \
  -logFile /tmp/unity_assets.log \
  -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch
```
```
[AssetImport] Audio import settings: 166 inspected, 0 excluded by marker, 0 failing, 0 warnings.
[AssetImport] Model import settings: 47 inspected, 0 excluded by marker, 0 failing, 0 warnings.
```
`exit=0` — 0 failing and 0 warnings on both passes, and nothing suppressed by an exclusion marker.

### Simulator — all commands run

```
dotnet run --project core/HorrorGame.Sim -- validate
```
```
§12 map validation: passed
```
`exit=0` (full output: constants internally consistent · `BalanceOverrides` reproduces
`CarryLoad` exactly · `CachedWorldProbe` reproduces `MapGraphProbe` on every node pair
· §12 map validation passed)

Every other command the binary advertises was also executed:

| Command | Result | Final line |
|---|---|---|
| `replay --seed 12345 --times 5` | `exit=0` | `Seed 12345 replayed identically 5 times: PartialVictory, 1.66 min, 110 credits earned, 3 clues read.` |
| `sweep weight-mul-light --matches 200 --seed 7` | `exit=0` | 6 points, `0.85`→`0.95`, 200 matches each, seeds 7…206 identical across points |
| `sweep loot-value --matches 200 --seed 7` | `exit=0` | 6 points, `0.50`→`3.00`, 200 matches each |
| `run --matches 500 --seed 1` | `exit=0` | 500-match scorecard: `outcome_clear 319`, `outcome_wipe 108`, `outcome_survived 73` |

The sweeps confirm F-001 is still live: across the whole `WeightMulLight` band 0.85–0.95
the clear rate moves only 11.0 → 13.0 % and wipes 24.5 → 13.5 %, i.e. the constant the
finding is about barely moves the outcome it is supposed to control.

---

## 2 · Verified red — known and tracked

### Audio audit — FAIL

```
tools/audio/.venv/bin/python tools/audio/verify_audio.py
```
```
  clips: 166   loops checked: 18   blocking defects: 1   warnings: 2
  RESULT: FAIL
```
`exit=1`

**This is the expected state, not a regression.** All three defects are already
documented, and one of them is pinned by a passing test that asserts the bug still
exists (`AudioTests.OccludedAudibility_InvertsTheClarityTable_AsF002Reports`).

| Severity | Defect | Tracked in |
|---|---|---|
| 🔴 blocking | gravel (clarity 0.70) measures 32.4 dB **quieter** than concrete (clarity 0.50) at low-pass 600 Hz — the HUD and the ears disagree in exactly the through-wall case the Listener is used in | `docs/BALANCE-FINDINGS.md` **F-002**; `docs/ASSETS.md` §known defects |
| 🟡 warning | wood vs metal separate by only **1.396×** occluded at 25 m, under the 1.4× floor | `docs/BALANCE-FINDINGS.md` **F-003**; `docs/ASSETS.md` |
| 🟡 warning | `Items/flare_burn_loop.wav` — ~8 ms fade-notch at the loop seam lands on audible material (−9.7 dB below the clip's own 5th percentile) | `docs/ASSETS.md` §known defects |
| ℹ️ info | `Items/loot_sell_credit.wav` is mono although non-diegetic | `docs/ASSETS.md` §known defects |

The audit's own verdict is that the alphabet **is** supported in the room (worst pair
metal vs tile at 2.13×, need ≥1.4×) and does **not** hold at 25 m through a wall.
So §12's five-surface alphabet is a working design at close range and an open mix
question at range.

---

## 3 · Defects found during this pass

Two of these are new — found by inspection, not by any test, because no test covers
them. IDs are `S-00N` to avoid colliding with `BALANCE-FINDINGS.md`'s `F-00N`.
**They are tracked here and nowhere else** until someone pins them with a test.

### S-001 · 🔴 blocking — §12's floor material never reaches the runtime on a generated map

**Status:** open, not fixed, not pinned by any test.
**Sections:** §12 (바닥 재질) × §04 (청음사).

The scene generator, the audio layer and the player layer were built by agents who
could not see each other, and they chose three different ways to answer "what am I
standing on". The producer and one consumer agree; the two consumers that actually
play footsteps do not.

What the code actually does today:

| Component | Assembly | How it states/reads the surface |
|---|---|---|
| `MapSceneBuilder.Finish` (producer) | `HorrorGame.EditorTools.SceneGen` | writes it into `collider.sharedMaterial`, a `PhysicsMaterial` **named after the enum** (`new PhysicsMaterial(floor.ToString())` → `Wood`, `Tile`, `Gravel`, `Concrete`, `Metal`) |
| `NavMeshWorldProbe.ResolveFloor` | `Assembly-CSharp` | **reads exactly that** — physics-material name, then object name, then renderer material. ✅ correct decoder |
| `FloorSurfaces.Sample` | `HorrorGame.Audio` | raycasts for an `IFloorSurface` component, or uses `FloorSurfaces.Probe` if assigned |
| `PlayerFootsteps.ResolveSurface` | `HorrorGame.Gameplay.Player` | raycasts for an `IFloorMaterialSource` component |

The break:

1. `MapSceneBuilder` never attaches **either** component. Its only `AddComponent` calls
   are `Light`, `MeshCollider` and `NavMeshSurface` — and its asmdef references only
   `HorrorGame.Core`, `Unity.AI.Navigation` and `UnityEngine.UI`, so it *cannot*
   attach one without an asmdef change.
2. `FloorSurfaces.Probe` — the documented hand-off, "the layer that owns `MapGraph`
   assigns it once" — **is never assigned anywhere in the project.** The only matches
   for `FloorSurfaces.Probe` are its own declaration and its own doc comment.
3. `PlayerFootsteps` is likewise never given an `IWorldProbe`.

**Consequence.** On a generated map every consumer falls back to a raycast, finds no
tag component, and returns `FloorMaterial.None`. `FootstepAudio`, `PlayerFootsteps`,
`ZoneAmbienceDirector` and `ListenerAudioDriver` all treat `None` as "play nothing".
So §12's floor alphabet — the entire channel §04 gives the 청음사 and nothing else — is
**silent on generated geometry**. It works only on hand-built test geometry that was
tagged by hand, which is exactly what the feel harness does, which is why it looks fine
in the harness.

**Why no test caught it.** Nothing under `Assets/Tests/` mentions `Footstep`,
`FloorSurface` or `FloorMaterial`. The 26 EditMode audio tests cover the tuning maths
(occlusion curves, rolloff, clip banks) and never exercise surface *resolution*. The
`Gameplay/Monster` folder — which contains the one correct decoder — has no `.asmdef`,
falls into `Assembly-CSharp`, and has **no test coverage at all**.

**The fix is wiring, not design.** `NavMeshWorldProbe.SampleFloor` already returns the
right answer for generated geometry; its naming contract and `MapSceneBuilder`'s were
verified to match. Someone needs to assign `FloorSurfaces.Probe` from the live
`NavMeshWorldProbe` at session start (`MonsterAgent` already exposes it as `.Probe`),
and hand `PlayerFootsteps` the same probe. `HorrorGame.Audio` is `autoReferenced`, so
a component in `Assembly-CSharp` can see `FloorSurfaces` without any asmdef change.
This was **deliberately not done in this pass**: it is new runtime behaviour that no
test covers and that a headless batch run cannot verify, and shipping unverifiable code
under a heading that says "verified" would defeat the point of this document. It should
land with a test that generates a map and asserts a non-`None` surface under the player.

### S-002 · ✅ fixed and verified — the test runner wrote unparseable result XML

**Status:** fixed in this pass. **File:**
`unity/HorrorGame/Assets/Scripts/Editor/BuildPipelineTestRunner/BuildPipelineTestRunner.cs`.

`WriteResultsXml` called `result.ToXml().ToString()`. `TNode` does not override
`ToString()`, so every run wrote a **32-byte file containing the literal text
`NUnit.Framework.Interfaces.TNode`** instead of the NUnit report.

This is the one failure mode a test report must not have: `dist/test-results/*.xml` is
what `.github/workflows/unity.yml` publishes, and an XML consumer parsing that file
finds zero tests and reports a **green** run. A suite that went fully red would have
been published as passing.

Fixed by using the serialiser, `result.ToXml().OuterXml`. Verified by re-running both
suites: the files are now 33,900 and 26,498 bytes, parse as well-formed XML with root
`<test-suite>`, and carry `total=52 passed=52 failed=0` and `total=27 passed=27 failed=0`
— matching the log exactly. The re-run also reconfirmed 79/79 passing and a clean
compile after the edit.

### S-003 · 🟡 open — two different components are both called `FloorSurfaceTag`

**Status:** open, not fixed. Compiles today; a trap rather than a break.

Two distinct `MonoBehaviour`s share the name, in different assemblies and namespaces:

* `HorrorGame.Audio.FloorSurfaceTag` — implements `IFloorSurface`, serialised field
  `floor`, and it owns the component-menu entry **"HorrorGame/Audio/Floor Surface Tag"**.
* `HorrorGame.Gameplay.Player.FloorSurfaceTag` — implements `IFloorMaterialSource`,
  serialised field `_material`, no menu entry.

They are not interchangeable and neither satisfies the other's consumer. Because the
Audio one owns the Add Component menu entry, a designer who tags geometry through the
inspector gets the component that `PlayerFootsteps` **cannot** read — a silent wrong
answer with no error anywhere. Deliberately left alone: collapsing them is the same
decision as S-001 (which floor abstraction is canonical), it spans three assemblies,
and `HorrorGame.Core` is `noEngineReferences: true` so the shared `MonoBehaviour`
cannot simply move there. Fix it with S-001, not before.

### S-004 · 🟢 minor — `VecInterop` is duplicated verbatim

`Gameplay/Monster/VecInterop.cs` and `Gameplay/Player/VecInterop.cs` are the same ~20
lines of `Vec3`↔`Vector3` conversion. Both are `internal` and live in different
assemblies (`Assembly-CSharp` and `HorrorGame.Gameplay.Player`), so there is no
ambiguity and no compile error — the duplication is *forced* by the assembly boundary
unless the helper moves to a shared engine-aware assembly. Not worth churn on its own;
worth folding in if S-001's fix introduces a shared runtime assembly anyway.

> Checked and dismissed: `MapKitPiece` appears twice but is not a duplicate — it is a
> `private sealed class` JSON DTO nested inside `AssetImportValidator`, unrelated to
> `MapKitCatalogue`'s public enum. No action.

---

## 4 · Built but unverified

These exist and compile. **No claim is made that they work.**

* **Monster subsystem** — `Gameplay/Monster/` (`MonsterAgent`, `MonsterBrain` driver,
  `NavMeshWorldProbe`, `MonsterFootsteps`, `MonsterAnimationDriver`, `MonsterDebugView`).
  No `.asmdef`, so it compiles into `Assembly-CSharp`, which **no test assembly
  references**. Zero coverage, including the `NavMeshWorldProbe` floor decoder that
  S-001 depends on.
* **Scene generation** — `Editor/SceneGen/` (`MapSceneBuilder`, `BootstrapSceneGenerator`,
  `MapKitCatalogue`, `FirstMapSketch`). Never executed in this pass; no scene was
  generated and no NavMesh was baked. The generated-scene folders
  (`Assets/Scenes/Generated/{Materials,NavMesh}`) exist but their contents were not
  validated. **`MapSceneBuilder` is one of the two sides of S-001.**
* **Build pipeline** — `Editor/BuildPipeline*.cs` (~10 files, targets/backends/options/
  version stamping). Only the test-runner portion was exercised. **No player build of
  any kind was produced.** The Windows IL2CPP player in `.github/workflows/unity.yml`
  has never run here — that workflow needs a Unity licence that is not present, and it
  is written to skip when the secret is absent.
* **Steam integration** — `HorrorGame.Steam`, `.SteamworksBackend`, `.Editor` and
  `Net/SteamTransport`. Reachable from the 11 PlayMode Net tests only as a reference;
  nothing was run against a live Steam client.
* **Blender/asset generators** — `tools/blender/`, driven by
  `tools/ci/run_blender_generators.sh`. Not run. The 47 models were validated for
  *import settings* only; nothing re-generated or diffed them.
* **The audio↔rules agreement in the shipped mix.** `verify_audio.py` measures the
  WAVs offline. Whether the Unity mix (occlusion filter strength, 3D rolloff)
  preserves those margins at runtime is untested and is the open half of F-003.

## 5 · Missing

* **A test for the floor-material path** — the gap that lets S-001 exist.
* **Any coverage of `Assembly-CSharp`** — no test assembly references it, so the whole
  Monster subsystem is untestable as currently structured. It needs an `.asmdef`
  before it can be tested at all.
* **Content gaps**, per `docs/ASSETS.md`: shop items 응급킷 · 정비 자재 · 가방 ·
  건물 도면 · 미끼 have no model or sound; 감지기 and 소음기 have sounds
  (`detector_ping`, `muffler_equip`) but no models.
* **Animation gaps**, per `docs/ASSETS.md`: no animation for entering
  `HidingSpot_Locker`, and no separate 질주 clip — the Runner's 5.6 m/s sprint plays
  `Run`, authored slower, so expect foot-sliding.
* **Unity CI cannot run** — `unity.yml` is gated on a licence secret that does not
  exist, so the Unity suites and the player build are verified only on a developer
  machine. The dotnet/audio half (`ci.yml`) needs no licence and does run.
* **No commits.** `git log` reports *"your current branch 'main' does not have any
  commits yet"* and every path is untracked. Nothing described here is committed, so
  none of it is recoverable if the working tree is lost.

## 6 · Reproducing this document

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
UNITY=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity

dotnet test  core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj   # 448 passed
dotnet build core/HorrorGame.sln -c Release                            # 0 errors

$UNITY -batchmode -quit -nographics -silent-crashes \
  -projectPath unity/HorrorGame -logFile /tmp/unity_verify.log
grep -E '^Assets/.*error CS' /tmp/unity_verify.log                     # no output

# NOTE: no -quit here — the run is async and exits from its own callback.
$UNITY -batchmode -nographics -silent-crashes -projectPath unity/HorrorGame \
  -logFile /tmp/unity_tests.log \
  -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine \
  -testSuites editmode,playmode -testRequireTests                      # 79 passed

$UNITY -batchmode -quit -nographics -silent-crashes -projectPath unity/HorrorGame \
  -logFile /tmp/unity_assets.log \
  -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch

dotnet run --project core/HorrorGame.Sim -- validate
tools/audio/.venv/bin/python tools/audio/verify_audio.py               # exits 1, see §2
```

Only one Unity process may hold the project lock at a time — run the three Unity
invocations one after another, never concurrently.
