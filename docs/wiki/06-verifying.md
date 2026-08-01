# How to verify anything

> **Do not hand back something you did not run.** And do not trust a green tick you
> did not read the exit code of. Half the checks in this project have a failure mode
> where they look green while proving nothing.

The full inventory of checks is [`docs/TESTING.md`](../TESTING.md); the last real
output of each is [`docs/STATUS.md`](../STATUS.md). This page is the routing table
and the list of false greens.

Shell preamble for everything below:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
P=/Users/doogi/horror-game/unity/HorrorGame
cd /Users/doogi/horror-game
```

---

## 1. Which check answers which question

| The question you actually have | The check | Cost |
|---|---|---|
| Are the rules and every tuned number still correct? | `dotnet test core/HorrorGame.Core.Tests/…csproj` | **1 s** |
| Does everything compile, including the simulator? | `dotnet build core/HorrorGame.sln -c Release` | 5 s |
| Does Unity compile? | `$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log` then `grep -cE '^Assets/.*error CS' /tmp/u.log` | ~1 min |
| **Can the monster reach a player at all?** | PlayMode `-testFilter "MonsterChaseTests"` | ~1 min |
| Does the whole solo match loop run? | `-executeMethod HorrorGame.EditorTools.SoloPlaytest.VerifyBatch` | ~1 min |
| Is the navigation surface connected? | `-executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch -auditScene …` | ~1 min |
| Is the map legal under §12, and how does it grade? | `-executeMethod …SceneGen.MapSceneGenerator.ReportQualityMenu` | ~1 min |
| Will a stereo import have killed the 청음사? | `-executeMethod …AssetImportValidator.ValidateAllBatch` | ~1 min |
| Are the five floor surfaces still tellable apart? | `tools/audio/verify_audio.py` | ~1 min |
| Can you see the monster at 15 m? | `MonsterShot.StageBatch`, **no `-nographics`** | ~2 min |
| Do you have hands, and is the torch in one? | `FirstPersonHandsShot.Batch`, **no `-nographics`** — reads out each hand's viewport coordinate | ~3 min |
| Does a piece you put down land on the floor? | `DropShot.Batch`, **no `-nographics`** — first-person plus a lit side view, and a wall-facing drop | ~3 min |
| Can four people read §08's shop in a dark room? | `ShopShot.Batch`, **no `-nographics`** — ten states | ~2 min |
| Is the picture inside ART.md's luminance bands? | `SceneShot.Batch` then `tools/render/frame_stats.py` | ~3 min |
| Is the economy / match length sane? | `dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1` | ~10 s |
| Does a seed still replay exactly? | `… -- replay --seed 42 --times 3` | ~2 s |
| Do the Blender generators still work? | `tools/ci/run_blender_generators.sh` | ~10 s |
| Does it produce a shippable player? | `-executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine -buildPlatform windows-x64 -buildConfig release`, then read `dist/last-build-summary.txt` **and** check for `MONO-FALLBACK-DO-NOT-SHIP.txt` | ~2 min each |
| **Is the game fun?** | Two humans, two instances, Discord. §14 | unautomatable |

The last row is not a joke. §14 says questions 1 and 2 decide the project and
「직접 만져봐야 나온다」. Every automated gate above is green or explained; none of
them can answer it.

---

## 2. The exact commands, in the order worth running

```bash
dotnet test  core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj      # 451/451, 건너뜀 0
dotnet build core/HorrorGame.sln -c Release                               # 0 errors

$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log
grep -cE '^Assets/.*error CS' /tmp/u.log                                  # 0

$U -batchmode -projectPath $P -runTests -testPlatform PlayMode \
   -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SoloPlaytest.VerifyBatch -logFile /tmp/solo.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu -logFile /tmp/quality.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log

# NO -quit. The runner exits from its own callback.
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine -logFile /tmp/t2.log

tools/audio/.venv/bin/python tools/audio/verify_audio.py
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1

# WITHOUT -nographics, or every shot is black.
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
   -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag verify -logFile /tmp/shot.log
```

Expected exit codes: **0** everywhere except the full Unity suite (**6** — one
failure, [B-002](../BLOCKERS.md#b-002)) and `verify_audio.py` (**1** — one blocking
defect, [F-002](09-open-questions.md)).

Read PlayMode results properly rather than trusting the exit code:

```bash
python3 -c "import xml.etree.ElementTree as ET; r=ET.parse('/tmp/chase.xml').getroot(); print(r.get('total'), r.get('passed'), r.get('failed'), r.get('result'))"
```

---

## 3. The false greens — checks that pass while failing

### 3.1 `-quit` on a test run reports nothing and exits 0

Unity's test runner is **asynchronous** and exits from its own callback. `-quit`
shuts the editor down before results are written. The run then prints no summary,
writes no XML, exits 0, and looks green. A failing EditMode test went unnoticed here
for exactly this reason, and [TESTING.md's own §4 correction](../TESTING.md) records
it. **Never put `-quit` on a `-runTests` or `RunFromCommandLine` invocation.**

### 3.2 A held project lock makes an error count meaningless

Only one process may hold `unity/HorrorGame/Temp/UnityLockfile`. A second Unity exits
early — and a log from a run that died early contains zero errors, which is not the
same thing as a clean run. **Check the exit code first, always, and check for another
Unity before you start:**

```bash
ps aux | grep "[U]nity.app/Contents/MacOS/Unity"
```

Serialise and retry. This is not hypothetical: while this page was being written, a
concurrent `BuildPipelineRunner.BuildFromCommandLine` held the lock, and every Unity
command on this page was therefore attributed to STATUS.md rather than re-run.

### 3.3 The NavMesh audit passes while the monster is deadlocked

The most expensive false green in the project's history. [STATUS.md §1.5](../STATUS.md)
states it plainly:

```
[NavMeshAudit] PASS
  pairs 630 · complete 630 (100.0 %) · islands 1 · monster reach 19/19
```

**This exact output was green while the monster was frozen 95 m from the player.**
The audit asks `NavMesh.CalculatePath`; the monster walks `NavMeshPath.corners` one
at a time. Those are different questions, and a `NavMeshLink` answers the first and
not the second. The audit is necessary and **not sufficient**. `MonsterChaseTests` is
the sufficient one. Full story: [the expensive bugs](07-expensive-bugs.md).

### 3.4 `-nographics` makes every render black

`-nographics` disables the graphics device. Any command that photographs anything —
`SceneShot.Batch`, `MonsterShot.Batch`, `MonsterShot.StageBatch`,
`AtmosphereSetup.ShotBatch`, `GuidanceShot.Batch` — must run **without** it. A black
PNG is a plausible-looking output for a horror game, which is what makes this one
expensive.

### 3.5 Blender exits 0 after a Python exception

Covered in [the asset pipeline §3](05-asset-pipeline.md). Grep for `ASSET_FAILED`,
for a traceback, and for the presence of an `ASSET_REPORT` line. The exit code is the
fourth and weakest signal.

### 3.6 A green §12 checklist does not mean a good map

`MapValidator` runs exactly **17** rules (count them:
`grep -c 'public const string Rule' unity/HorrorGame/Assets/Scripts/Core/Map/MapValidator.cs`
→ `17`). Core's own fixture map passes them all and grades **10/10 TooEasy** on the
주자 테스트 — pinned by `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`.
The checklist is necessary, not sufficient; the grade is the second half. §12 wants
5–7/10.

**The shipped map is worse than that: it now fails the checklist too.** 요양원 지하 5층
passes **16 of 17** and grades 10/10 TooEasy; the three-storey building it replaced
graded 7/10. The 17th rule, `sight-break-spacing`, landed in `66ce930` and is the
first one to measure corner *density* — which two passes of prose had already named as
the cause. The `RunnerCensus` line under the grade — `164/164 escapable (100%)` —
rules out an unlucky ten-point sample, and the line under *that* is the same geometry
the new rule fails on: 79 sight-breaking corners, mean nearest-neighbour spacing
**4.1 m** against §12's 15–25 m, **0 inside the band**. See
[F-007](09-open-questions.md#f-007).

> 🔴 **A failing checklist blocks map generation.** `MapSceneGenerator.Generate` gates
> on it, so `HorrorGame ▸ Scene Gen ▸ Generate First Map` now exits 1 and writes
> nothing — [B-007](../BLOCKERS.md#b-007). The committed scene predates the rule and
> still runs; re-rolling the map is what is blocked.

### 3.7 A passing suite with skipped tests

`dotnet test` prints `건너뜀: N`. If N is not 0, the total is inflated by disabled
cases. Read both numbers.

### 3.8 The chase numbers are measured at a tier a third of matches reach

`MonsterChaseTests` pins §07 to 심야 to measure against §06's 4.8 m/s. The simulator
says a real match reaches 심야 **33.6 %** of the time
([F-006](09-open-questions.md#f-006)) — it said 1.2 % until 2026-08-01, when the
five-storey map landed and the simulator was pointed at it. The chase numbers are
correct *for the tier they are measured at*, and that tier is now a third of the
population rather than a rounding error. They still are not the numbers of the median
match, which ends at 7.2 min in tier 1.

---

## 4. What each red thing currently means

| Red thing | Status | Do not "fix" it by |
|---|---|---|
| `SoloMatchLoopTests` fails on a Mirror package `.meta` | [B-002](../BLOCKERS.md#b-002) — environment, not a regression. The same code path passes outside the harness ([STATUS.md §1.4](../STATUS.md)) | widening the test's log tolerance in general — that hides the errors it exists to catch. Reinstall the package, or `LogAssert.Expect` this one message |
| `verify_audio.py` exit 1 | [F-002](09-open-questions.md) — an open design decision | changing the mix to make the number go away. `AudioTests.OccludedAudibility_InvertsTheClarityTable_AsF002Reports` fails if you do |
| Two `HallOpen20x20` `LogError`s on every map generation | [B-003](../BLOCKERS.md#b-003) — design intent lost quietly | ignoring it. A generator that logs errors on the happy path means nobody can use "the log is clean" as a gate |

---

## 5. Where the existing docs are already stale

Not a criticism — they are dated snapshots and they say so. But **re-measure before
quoting**, and prefer STATUS.md over the others. Known drift as of 2026-08-01 06:40:

| Claim | Where | Actually |
|---|---|---|
| "§12 validation PASS on all 17 rules" | ART.md §6 | there are now 17 rules, and the shipped map passes **16** of them — [B-007](../BLOCKERS.md#b-007) |
| "47 FBX" | ASSETS.md header | 86 on disk |
| "387 tests" | CI.md §2.1 | 451 |
| "`Assets/Tests/EditMode/` and `Assets/Tests/PlayMode/` are still empty" | CI.md §4.2, §5 | six test assemblies exist and run |
| "Nothing that needs the Unity editor has ever executed" | CI.md §5 | true of *CI*, not of this machine — STATUS.md quotes real editor runs |
| "`dist/` contains logs and test results and **no player executable**" | STATUS.md §5 | corrected — an IL2CPP macOS player is built and verified to reach a match ([STATUS.md §1.10](../STATUS.md)). Read `dist/last-build-summary.txt` |
| PlayMode is 27 tests, or 42, or 53, or 55 | anywhere | **64**; EditMode **100**; core **451**; 615 total ([STATUS.md §1.9](../STATUS.md)) |
| "16–39 % crushed, 31–57 % legible" | ART.md §1 | re-measured every art pass — [STATUS.md §4.3](../STATUS.md) is the current one |
| "matches finish in 2.5 min" / "0.6% inside the window" / "심야 1.2%" | anywhere | 7.2 min, 15.8%, 33.6% — the old figures were measured against a four-zone ring the game does not ship ([F-006](09-open-questions.md#f-006)) |
| "주자 테스트 7/10, Balanced" | anywhere | 10/10 TooEasy on the five-storey map ([F-007](09-open-questions.md#f-007)) |

**The one that cost most.** Every "2.5 min" above was not stale prose — it was a live
measurement of the wrong object, and it sat in `PlaytestGuidanceScreen`'s §14 overlay
where a playtester would read it as fact. A number in a string literal is a copy; grep
for it when the thing it copies moves.
| audio separation 2.13× / 1.98× / 1.396× / 32.4 dB | STATUS.md §2.2, CI.md §2.2, BALANCE-FINDINGS F-002/F-003 | **2.10× / 1.89× / 1.389× / 32.5 dB**, measured twice on 2026-07-31 23:38 with the pinned toolchain. 56 footstep WAVs were regenerated in commit `4fb93cd` |
| TESTING.md's suite command carries `-quit` | TESTING.md §4 vs its own warning box | the warning is right; the command above it is the one to use |

---

## 6. What is verified, what is built-but-unverified, and what is missing

Short form; [STATUS.md §5 and §6](../STATUS.md) are the authority.

- **Verified:** rules core, Unity compile, the chase, the solo loop, NavMesh
  connectivity, §12 validation and the 주자 grade, asset import settings.
- **Built, never exercised:** two-instance networking, the Steam upload path, three of
  the five roles in a real match, and the floor-material chain end-to-end (wired, not
  pinned — [STATUS.md §3a](../STATUS.md) defect 3.8).
- **Player builds — check `dist/last-build-summary.txt`, do not quote a doc.**
  STATUS.md §5 says `dist/` holds no player executable; that was true when it was
  written and is not true now. As of 2026-07-31 23:39 there is an IL2CPP macOS
  arm64 Release player and a **Mono** `dist/windows-x64/HorrorGame.exe` carrying
  `MONO-FALLBACK-DO-NOT-SHIP.txt`. That marker is the pipeline working as designed:
  macOS cannot cross-compile IL2CPP for Windows, §13's audience is Windows, and a
  Mono player ships plain managed assemblies that decompile in seconds — which hands
  out the host-only clue and objective logic §13 depends on. **Never ship a build
  that printed that warning**; pass `-buildRequireIl2cpp` to make it a hard failure,
  and produce the real one on a Windows machine or `.github/workflows/unity.yml`.
- **Missing:** every §14 verification question. Q1, Q2, Q4 and Q5 are askable today
  and unanswered. **Q3 cannot be asked at all** until F-006 is fixed.
