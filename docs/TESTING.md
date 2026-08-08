# How to test this game

Every command here was run on this machine at commit `a3e268e` (2026-08-03) and the
output quoted under it is that run's real output. Where a figure comes from a log in
`/tmp`, the log is named. If a command does not reproduce for you, that is a bug in the
project or in this document — say so rather than working around it.

This document's whole job is that every claim in [STATUS.md](STATUS.md) can be
re-measured by someone with no memory of the session that produced it.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
P=/Users/doogi/horror-game/unity/HorrorGame
cd /Users/doogi/horror-game
```

> **One Unity process may hold the project lock.** Close the editor before any batch
> command below, and run them one at a time. `dotnet` does **not** take that lock, so
> the .NET half can run in parallel with somebody else's Unity work.

---

## Read this before trusting any green

Five ways this project has produced a false green, all of them real, all of them
recorded. They are the reason each section below says what to check rather than what to
run.

| Trap | What it looked like | The rule |
|---|---|---|
| `-quit` on a test run | exit 0, no results written, looks green | **Never `-quit` a `-runTests` run.** The runner is async and exits from its own callback |
| `-quit` on a build | a failed build reporting success | **Never `-quit` a build.** `BuildFromCommandLine` owns the exit code |
| Exit code instead of results | a run that died early has zero errors in its log | Read the XML / the report, not `$?` |
| A green suite over a broken build | Unity clean, 560 tests passing, `dotnet build` failing on 2 errors for four hours ([B-006](BLOCKERS.md#b-006), [B-013](BLOCKERS.md#b-013)) | §1 and §2 are different questions. Run both |
| A test that calls the method under the key | §08's pick-up key broken in the build with 575 tests green | Drive the input, and assert the input arrived |
| A `grep` that cannot match what it is counting | `m_Name: MonsterSpawn` found 1 spawn in a scene holding 8 — Unity escapes Korean as `\uXXXX` and then **double-quotes the whole value**, so every name but the pure-ASCII one was invisible | Count with a parser, not a prefix. `re.findall(r"m_Name:\s*(.+)")` and strip the quotes. A pattern that finds *some* of a thing looks exactly like a thing that is mostly absent |

And one more, which is this project's own signature failure: **a gate that describes a
different artefact than the one you are about to ship.** [B-009](BLOCKERS.md#b-009) was
three days of auditing a NavMesh that was not the one just baked;
[B-012](BLOCKERS.md#b-012) is the balance simulator measuring a building the game
deleted, right now. §5 below is how you check that the thing being measured is the thing
on disk.

---

## The one command to run constantly

```bash
dotnet test /Users/doogi/horror-game/core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

```
통과!  - 실패: 0, 통과: 512, 건너뜀: 0, 전체: 512, 기간: 419 ms - HorrorGame.Core.Tests.dll (net9.0)
```

**512 tests in under half a second, and Unity never opens.** 0 skipped, so the count is
not inflated by disabled cases. Every tuned number and every rule lives here — §05's
speed multipliers, §06's aggro and state machine, §07's threat curve, §12's map rules,
§02's finish condition.

This works because the rules core has no engine dependency: the same `.cs` files Unity
compiles are pulled into a .NET project by a glob, so there is one copy of the truth,
checked two ways. `FoundationTests.CoreSources_DoNotReferenceUnityEngine` fails the build
if anyone breaks that arrangement.

Run it before every commit. If it is green, the game's rules are intact.

> Older revisions of this file said **476**. That was the count on 2026-08-01, before the
> pivot. Re-read the number from the run rather than from here.

---

## The full sweep, in the order worth running

### 1 · Rules — 512 tests

```bash
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

Expect `실패: 0, 통과: 512, 건너뜀: 0`.

### 2 · Everything compiles outside Unity, including the simulator

```bash
dotnet clean core/HorrorGame.sln -c Release      # or the warning count below is a lie
dotnet build core/HorrorGame.sln -c Release
```

```
빌드했습니다.
    오류 0개
```

**Do not skip this because §1 was green.** These two commands are `ci.yml`'s
`core tests (dotnet)` job, and §1 alone does not cover `HorrorGame.Sim`.

> **This has failed twice while everything else was green, and both times nobody
> noticed.** On 2026-08-01 the simulator's project did not compile a file
> `MapQualityReport` depends on ([B-006](BLOCKERS.md#b-006)). On 2026-08-03
> `ChamberDockProbe.cs` landed in `Editor/SceneGen/`, matched the glob that pulls the
> engine-free map sources into **both** headless projects, and did `using UnityEditor` —
> so `dotnet test` could not reach a single one of its 512 tests for three commits, one
> of which is titled *"and the suite is green"* ([B-013](BLOCKERS.md#b-013)).

> **`dotnet clean` first, or the warning count is meaningless.** An incremental build of
> an already-built solution recompiles nothing and therefore re-emits no warnings; it
> prints `경고 0개` and takes 1.5 s instead of 4.3 s. The **error** count is trustworthy
> either way.

### 3 · Unity compiles

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log
grep -cE '^Assets/.*error CS' /tmp/u.log
```

Prints `0`. Anything else is the error count — read `/tmp/u.log`. `-quit` is correct
*here*, and only here: this run has no async work to wait for.

### 4 · PlayMode — 113 tests, 112 green

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -runTests -testPlatform PlayMode \
   -testResults /tmp/playmode.xml -logFile /tmp/playmode.log
python3 -c "import xml.etree.ElementTree as ET,sys; r=ET.parse(sys.argv[1]).getroot(); \
  print(r.get('total'), r.get('passed'), r.get('failed'), r.get('skipped'), r.get('result'))" \
  /tmp/playmode.xml
```

From `/tmp/r7_all.xml`, 2026-08-03 08:37:

```
113 112 1 0 Failed(Child)
```

**The one red is expected and is tracked.**
`LobbyEntryWiringTests.HostingFromTheMenuReachesTheMazeWithARunnerStillAlive` fails on an
unhandled `[Error] [Race] §01 출발선이 완성되지 않았다 — 2석 중 1명에게 몸이 없다.` —
see [B-011](BLOCKERS.md#b-011) for why it is probably the test's own doing and why
"probably" is not good enough. **Anything other than exactly this one failure is a
regression.** List the failures rather than counting them:

```bash
python3 -c "
import xml.etree.ElementTree as ET,sys
for tc in ET.parse(sys.argv[1]).getroot().iter('test-case'):
    if tc.get('result')!='Passed': print(tc.get('fullname'), tc.get('result'))
" /tmp/playmode.xml
```

> **Never add `-quit`.** Unity's test runner is asynchronous and `-quit` shuts the editor
> down before results are written. The run then reports nothing, exits 0, and looks
> green.

The 113 by fixture, from that run:

| Fixture | n | What it is for |
|---|:--:|---|
| `Racing.RaceDirectorTests` | 17 | §02 — descent count, ranking, the finish |
| `PlayerRig.PlayerTests` | 16 | §05 movement |
| `Net.NetTests` | 11 | §13 host authority, replication, what a client may not learn |
| `PlayerRig.PlayerViewMotionTests` | 11 | §05 view motion |
| `Audio.AudioSceneTests` | 10 | the audio rig exists in the shipped scene |
| `PlayerRig.PlayerFirstPersonViewTests` | 7 | §05's 손 — but see [B-017](BLOCKERS.md#b-017) |
| `Ghosts.GhostSessionTests` | 6 | §09 elimination |
| `PlayerRig.PlayerStanceTests` | 6 | crouch / hop |
| `Presence.PresenceSessionTests` | 5 | §10 그늘 — **still in no scene and no prefab** |
| `Monster.MonsterChaseTests` | 4 | §06 — §14's Q1, below |
| `Net.LobbyEntryWiringTests` | 3 | the shipped 호스트 menu path — **1 red** |
| `Net.NetHumanRunnerTests` | 3 | a human's input crossing a real socket |
| `Net.NetRunnerTests` | 3 | other people's bodies |
| `Net.NetSocketTests` | 2 | two peers, real transport |
| `Interaction.InteractionPickupTests` | 2 | the real key, not `OnPressed` |
| `Racing.DescentPlaythroughTests` | 1 | **B1 → B8, the whole race** |
| `Interaction.InteractionDropTests` | 1 | the real key |
| `Match.MonsterKillTests` | 1 | standing in front of the creature kills you |
| `UI.UiFlowTests` | 1 | 시작 reaches the match scene |

Single fixtures with `-testFilter`:

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P -runTests -testPlatform PlayMode \
   -testFilter "DescentPlaythroughTests" -testResults /tmp/pt.xml -logFile /tmp/pt.log
```

**The two to know.**

`DescentPlaythroughTests.A_runner_can_descend_from_the_rim_of_B1_to_the_middle_of_B8` is
§01 end to end. It prints a table and the table is the evidence
(`/tmp/r6_all.log:2870`):

```
층   외곽→중심                        투하구
B1   PathComplete 49.8 m              ↓ B2  외곽 25.0 m
…                                     (eight legs, seven chutes)
B8   PathComplete 61.6 m              도착점
§02 Descended 7회 / 필요 7회 · 좌석 0의 층 B8 · 승자 0 · 완주 1명 · 경과 89초
```

`PathComplete` on every leg is the load-bearing word: a `PathPartial` is how a broken
NavMesh looks like bad AI rather than like an error.

`MonsterChaseTests` is §14's Q1 turned into something a machine can answer
(`/tmp/r6_all.log`):

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
  route 71.0 m of NavMesh path · reached 14.54 s · closing speed 4.81 m/s against §06's 4.8
[ChaseTest] §12 ① S자 통로 — released 5.50 s after aggro, at 12.0 m, caught no
[ChaseTest] §12 단일 모퉁이 — caught 12.54 s
  monster speed 4.80 m/s against §06's 4.8 · gap opened at 0.80 m/s against §06's 0.8
```

Those last two numbers are §06's central design claim —
「괴물이 달리기보다 0.3만 빠른 것이 핵심이다」 — measured on real geometry to 1 %. Watch
them: if either moves, a speed constant moved with it.

### 5 · Generating the map — and the three gates it runs

This is one command and it is the most important one in this document, because it is the
only one that produces the artefact everything else measures.

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.GenerateFromCommandLine \
   -logFile /tmp/gen.log; echo "exit=$?"
```

Expect **exit 0** and, in order in the log: §12's checklist, the 주자 테스트 grade, the
census, `[NavMeshAudit] PASS`, `[PlayerReach] PASS`, and a commit line with a stamp.
From `/tmp/r6_gen.log`, 2026-08-03:

```
§12 map validation — 하강 — 요양원 지하 8층: FAIL      ← 14 of 17 ok; see below
§12 주자 테스트: 10/10 (100%), TooEasy                 ← the project's largest problem
§12 실전 검증: 720/720 escapable (100%), against §12's 50%~70% band
시야 차단 지점 간격: 496 corners, mean 3.5 m, 0 inside §12's 15~25 m band
Scene contents: 1160 kit pieces, 8 props, 824 markers;
  graph has 720 places, 814 passages, 95 순환로, 152 막힌 길

[NavMeshAudit] PASS
  markers 220 · pairs 3482 · complete 3482 (100.0 %, need 98 %) · partial 0 · invalid 0
  islands 8  ← the surface is in pieces
  worst snap 0.25 m
  monster reach 212/212 markers reachable from a MonsterSpawn on the SAME storey,
                over 8 of 8 storeys (§06)

[PlayerReach] PASS
  runner reach   storeys 8/8 · starts 36/36 reach the finish · finish REACHED
  one-way routes 14/14 투하구 usable · no 계단
  chute-blind    0/36 starts reach the finish with the one-way routes deleted
  standing places 224236 · 후보 지점 24/24 · 전리품 152/152 · player spawns 36/36
  tallest climb 0.045 m · tightest headroom 2.53 m · worst reach gap 0.33 m
```

**Three things about that output that will mislead you if nobody says them.**

- **`islands 8  ← the surface is in pieces` is wrong on the happy path.** A tower whose
  only vertical links are one-way falls is eight surfaces by construction. The audit
  judges a storey now, not a building; only the arrow text was left behind
  ([B-014](BLOCKERS.md#b-014)).
- **`§12 … : FAIL` is not the whole story either.** 14 of 17 rules pass. Of the three
  failures, `sight-break-spacing` is a genuine defect ([B-007](BLOCKERS.md#b-007)) and
  `zone-diagonal` and `open-adjacent-to-maze` are rules written for the deleted
  co-operative game. The generator writes the map anyway, through a named waiver, and
  says so every time:

  ```
  [SceneGen] §12 is failing a rule that is already recorded as a known defect, so the map
  was written anyway. This is not permission to ignore it — see docs/BLOCKERS.md B-007.
  ```

  If a **different** rule fails, the generator stops and writes nothing. That is correct.
  Do not add to `MapSceneGenerator.KnownFailingRules` to get past a new failure.
- **`chute-blind 0/36` is a good number.** It means deleting the one-way routes leaves
  nothing able to reach the finish — so the gate cannot pass by accident through some
  route nobody meant to leave in, and it fails the moment one 투하구 breaks.

#### 5a · Prove the scene on disk is the one that was audited

Every generation stamps the scene **and** the bake with the same string, and prints it:

```
[SceneGen] gen-20260803-080103-seed20260802: … BOTH written by this run — 7127 vertices,
232,304 bytes; the same stamp is on 'SceneGen_gen-20260803-080103-seed20260802' in the
scene and in …/NavMesh_Map_FirstSketch.asset.meta
```

Check it without Unity, which is the entire point:

```bash
grep -o 'SceneGen_gen-[0-9T-]*-seed[0-9]*' \
  unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity | head -1
grep -o 'gen-[0-9T-]*-seed[0-9]*' \
  unity/HorrorGame/Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta | head -1
```

Both print `gen-20260803-080103-seed20260802` at `a3e268e`. If they disagree, or if
either carries `-forced`, the measurements in STATUS.md do not describe what is on disk.
This exists because [B-009](BLOCKERS.md#b-009) was three days of auditing a stale
surface.

#### 5b · The report without writing anything

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu \
   -logFile /tmp/quality.log
```

Same checklist and grade, no disk writes. In the editor:
`HorrorGame ▸ Scene Gen ▸ Report Map Quality`.

#### 5c · The two audits on their own

Both run inside §5, and both can be pointed at any scene:

```bash
$U -batchmode -quit -nographics -projectPath $P \
   -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log

$U -batchmode -quit -nographics -projectPath $P \
   -executeMethod HorrorGame.EditorTools.PlayerReachAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/reach.log
```

**There are two bodies in this game and they are not the same size.** The NavMesh agent
climbs `agentClimb` 0.75 m and stands 2.00 m; the player's `CharacterController` climbs
`stepOffset` 0.40 m and stands 1.75 m. Every surface between those numbers is a route
only the antagonist can take, and the NavMesh audit cannot see one — Recast erodes a
walkable *region*, it carries no body. This once produced `1830/1830 pairs complete,
100 %, 1 island` for a building the player could not leave the ground floor of
([B-008](BLOCKERS.md#b-008)). **Run both. Neither substitutes for the other.**

`PlayerReachAudit` reads the capsule off `PlayerFeelHarnessMenu.BuildRig()` rather than
restating it, so it follows the controller if anyone retunes it, and it reports headroom,
which fails identically and just as silently: a beam at 1.60 m stops a 1.75 m capsule dead
and bakes a perfectly good agent surface underneath itself.

> **Do not fix a `PlayerReach` failure by raising `stepOffset`.** §12's escape geometry is
> derived from what a player *cannot* climb. Fix the geometry — `tools/blender/gen_mapkit.py`
> holds the kit to the player's capsule as well as the agent's and fails at export.

### 6 · Audio — §12's floor-material alphabet

```bash
tools/audio/.venv/bin/python tools/audio/verify_audio.py --json /tmp/audit.json; echo "exit=$?"
python3 tools/ci/check_audio_baseline.py --audit /tmp/audit.json; echo "exit=$?"
```

Measured 2026-08-03:

```
§12 Listener alphabet: SUPPORTED — worst surface pair water vs gravel at 1.50x (need >= 1.4x)
at 25m through a wall it does NOT hold: worst pair water vs tile at 1.377x
HUD vs ears: 5 inverted pair(s) — water/tile, gravel/concrete, gravel/earth, water/wood, gravel/carpet
clips: 205   loops checked: 21   blocking defects: 3   warnings: 6
RESULT: FAIL                                   ← verify_audio.py exits 1

RESULT: FAIL — 2 unbaselined blocking defect(s)  ← the CI gate
  [consistency] gravel vs earth · [consistency] gravel vs carpet
```

`verify_audio.py` exiting 1 is **expected**: F-002 is a known, baselined design
contradiction. What is *not* expected is the second command failing. It fails both ways
on purpose — a blocking defect absent from `tools/ci/audio_baseline.json` is a regression,
and a baselined defect that has stopped reproducing means the finding was fixed and the
write-up has to move in the same commit.

Right now it is red because Carpet, Water and Earth were added for B6/B7/B8 with clarity
constants but without the occlusion analysis, so F-002's shape now covers five pairs. See
[B-013](BLOCKERS.md#b-013). **This job is red on `main`.**

This is a gameplay invariant, not an audio nicety: §12 requires the Listener to tell zones
apart by floor material, and §04 localises the creature by ear. Re-run it after touching
any generator, and after touching `GameConstants` — the script parses the clarity table
out of it.

### 7 · Builds

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine \
   -buildPlatform mac -buildConfig development -logFile /tmp/build.log; echo "exit=$?"
```

`dist/last-build-summary.txt`, 2026-08-02T23:23:45Z:

```
exit code : 0
  macOS universal (Apple silicon + Intel)   Development Mono   OK   387.92 MB   17s
```

**Release does not build on this machine.** `/tmp/r5_build_release.log`, and identically
in the two runs before it:

```
exit code : 4
  macOS universal (Apple silicon + Intel)   Release   IL2CPP FAILED   125.27 MB   23s
.../libil2cpp/codegen/il2cpp-codegen.h:24:10: fatal error: 'cmath' file not found
```

The cause is this host, not Unity, and it takes two lines to prove:

```bash
printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp
clang++ -std=c++17 /tmp/p.cpp -o /tmp/p           # fatal error: 'cmath' file not found
ls /Library/Developer/CommandLineTools/usr/include/c++/v1 | wc -l            #  11
ls /Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1 | wc -l  # 185
```

The workaround still works (verified 2026-08-03 — the same compile exits 0 with it) and
was **not** used in any of the three failed Release runs:

```bash
export CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1
```

So try that before concluding anything. The real fix is
`sudo rm -rf /Library/Developer/CommandLineTools && sudo xcode-select --install`, which is
a system change and belongs to the owner. See [B-015](BLOCKERS.md#b-015).

> **Never pass `-quit` to a build.** `BuildFromCommandLine` owns the exit code and calls
> `EditorApplication.Exit` itself; `-quit` overrides a failure with 0.

Exit codes: `0` ok · `1` unexpected · `2` arguments · `3` scenes · `4` build failed ·
`5` IL2CPP required but unavailable · `7` scripts do not compile · `8` module missing.
Output is `dist/<platform>/`, wiped and rewritten each time;
`dist/<platform>/build-report.txt` holds every message off Unity's `BuildReport`, which
the raw log does not carry reliably.

| Target | Development | Release | Shippable on Steam |
|---|---|---|---|
| macOS arm64/universal | Mono, ~20 s | **IL2CPP — failing here** | not until §7's fix |
| Windows x64 | Mono | **Mono only, never IL2CPP** | **no** |

Windows IL2CPP cannot be produced on a Mac at all — IL2CPP transpiles to C++ and calls the
*target's* compiler. A Windows Release build made here falls back to Mono and the pipeline
drops `MONO-FALLBACK-DO-NOT-SHIP.txt` beside it. Steam's audience is mostly Windows, so
shipping needs a Windows machine or the runner in `.github/workflows/unity.yml`, which has
never run for want of a licence — [CI.md](CI.md), [STEAM-RELEASE.md](STEAM-RELEASE.md).

**Mirror's meta-file error is expected in every build** and does not fail it:
`Asset Packages/com.mirrornetworking.mirror/Mirror/Assets has no meta file, but it's in an
immutable folder.` The OpenUPM repack includes Mirror's submodule, whose Unity project root
legitimately has no `.meta`. It cannot be fixed from this repository — `Library/PackageCache`
is immutable and regenerated from the tarball on every resolve.
`BuildPipelineKnownDefects` matches on both the symptom and that exact package path, prints
it, counts it, and lists it under **known third-party defects**. Every other error still
fails the build. This is also why `BuildOptions.StrictMode` is not set: it fails a build
when *any* error was logged and blames the first scene, which made every scene here
unbuildable including the near-empty bootstrap menu.

### 8 · EditMode — not run since the pivot

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -runTests -testPlatform EditMode -testResults /tmp/editmode.xml -logFile /tmp/em.log
```

**No result to quote.** The newest EditMode run on this machine is `/tmp/editmode.xml`,
`start-time 2026-08-01 11:51:06Z`, 101/101 — against the four-player co-operative game.
(Every EditMode XML in `/tmp` is older than that one; every result file newer than it is
PlayMode.) Two of the five EditMode fixtures test things
the pivot deleted — `UiTests` (59 cases, the §08 shop) and `SoloMatchLoopTests` (the §01
co-op loop). The platform is either red or green-about-nothing and nobody knows which:
[B-016](BLOCKERS.md#b-016). **Until somebody runs it, no document here may quote a total
that includes an EditMode number.**

### 9 · The balance simulator — currently measuring the wrong building

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- map
```

> 🔴 **Do not quote any figure from this tool.** Measured 2026-08-03: it reports
> `254 places · 285 passages · footprint 50 m × 95 m`, while the game's map is
> **720 places, 814 passages, 57.5 m × 57.5 m**. `MapSceneGenerator` calls
> `DescentMap.Build`; `SimMap` calls `FirstMapSketch.Build` — the retired co-operative
> building. It also still simulates §03's clue chain and §08's economy, both deleted.
> This is [F-006](BALANCE-FINDINGS.md#f-006) recurring and is tracked as
> [B-012](BLOCKERS.md#b-012).

The commands, for when it is pointed at the right map again:

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1
dotnet run -c Release --project core/HorrorGame.Sim -- match --seed 42
dotnet run -c Release --project core/HorrorGame.Sim -- replay --seed 42 --times 3
dotnet run -c Release --project core/HorrorGame.Sim -- sweep loot-value --matches 400 --seed 1
```

**Read the first five lines of any run — they are the building — before quoting anything
under them.** That habit is the only thing that would have caught either occurrence of
F-006.

### 10 · Asset import settings — stale, and worth re-running

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log
```

Last recorded run, 2026-08-01: `166 audio inspected, 0 failing` / `86 models inspected,
0 failing`. There are now **209 WAVs and 91 FBX** on disk, so at least 43 clips have never
been through this check.

Not housekeeping. It enforces the Humanoid animation type, a valid `isHuman` Avatar, the
four non-humanoid mount bones (`AssetImportPolicy.PlayerMountBones` — Optimize Game
Objects strips exactly these and the Avatar does not protect them), the 1.750 m
`PlayerHeightMetres`, and the expected clip count. And a positional clip imported as
stereo is not spatialised by Unity at all — one wrong checkbox silently deletes the
Listener cue and nothing else in the project notices.

### 11 · The creature is visible at 15 m

```bash
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.Gameplay.MonsterEditor.MonsterShot.StageBatch \
   -shotTag stage -logFile /tmp/mon.log
```

**No `-nographics`**, or every frame is black. Last run 2026-08-01: 8 of 8 staged frames
pass (`contrast ≥ 0.015`, `coverage ≥ 0.40`, `peak ≥ 0.040`), 15 m contrast 0.0592.
Not re-run since the map changed, so treat it as describing the creature rather than the
game.

Three numbers per frame, each gating: `contrast` (mean per-pixel luminance separation from
the wall — ≈4 code values, where an observer who has not been told where to look picks a
shape out of a dark field), `coverage` (fraction of the silhouette that differs at all —
separates a creature from a glint), `peak` (95th-percentile change inside the silhouette —
one genuinely legible feature). The silhouette is ground truth, rendered unlit white on
black with fog and grading off; taking "the pixels that changed" as the silhouette would be
circular.

> **Judge every render at native brightness first.** A 3× gain in the viewer is exactly the
> crop that hides an exposure defect, and reading renders that way cost this project five
> review rounds.

---

## What is not tested at all

Stated here because a test inventory that only lists what exists is the most misleading
document a project can have.

- **A human playing.** Not one match, by anybody, ever.
- **Twenty humans.** `TwentyRunnersGetTwentyDistinctPlacesOnB1sRim` and
  `TheTwentiethRunnerIsAcceptedAndTheTwentyFirstIsRefused` stand up twenty sockets on
  `127.0.0.1` in one editor process. Latency, loss, NAT, Steam relay and twenty machines
  are untested.
- **How long a match takes.** §01 wants 12–20 minutes. The only number that exists is 89 s
  for a pathfinder that already knows the way.
- **§10's 그늘.** 25 core tests and 5 PlayMode tests pass, and `PresenceDirector` /
  `PresenceSubject` are in no scene and no prefab (checked by script GUID, not by name) and
  `MatchDirector` never mentions `Presence`. A green suite is not evidence the entity
  exists in a match.
- **Everything in [ART.md](ART.md) and `docs/store/`.** Every frame is of the five-storey
  co-operative building, and several contain the 차량 shop the pivot deleted.
- **근접 음성, on any machine that is not listening to a live microphone.** Three
  `VoiceSocketTests` — `AVoiceCrossesARealSocketAndArrivesAttenuatedByTheRule`,
  `AWallBetweenThemCostsTheRulesOcclusionAndNotTheEnginesRolloff` and
  `SpeakingIsReportedToTheCreatureEvenWithNobodyInRange` — drive a real socket with a real
  `VoiceCapture`, so they need the microphone to be producing *sound*, not merely to open.
  On 2026-08-08 the log says `[Voice] Microphone line at 16000 Hz` and all three fail with
  "the relay forwarded nothing", which is what silence looks like from the far end. **This
  is not a pass/fail signal about the voice code and must not be read as one in either
  direction** — green means somebody was making noise near the machine.

### PlayMode is 117/121 here, and 121/121 is on record. Both were measured

Two consecutive runs on the same tree, docs-only changes, nothing touching voice or
movement:

| run | failed |
|---|---|
| 1 | the 3 voice tests **+** `PlayerStanceTests.The_hop_cannot_mount_a_ledge_a_walk_cannot` |
| 2 | the 3 voice tests **+** `GunTests.Firing_hands_the_creature_a_sound_it_can_act_on` |

The three voice tests fail identically both times — see above, and treat them as
environment-gated rather than as a regression. **The fourth is a different test each run,
which makes it the more interesting one:** something in the PlayMode fixture is
order- or timing-dependent, and whichever test draws the short straw is the one that
fails. A suite with one floating failure reports a different set every time and will,
sooner or later, report an empty one — at which point the flake looks like a fix.
Neither has been chased down. The 121/121 on record was a real measurement of a run where
the straw fell elsewhere and the room was not silent.

**Update, same day, the flake showed its face.** Two later runs both failed
`GunTests.Firing_hands_the_creature_a_sound_it_can_act_on` with a message the earlier
sightings never captured:

> moving the rig beside the creature changed which creature is local to it.
> Expected: Monster @ MonsterSpawn_B1 하역장_45. But was: Monster @ MonsterSpawn_B2 기록보관소_135.

So the floating failure is not in any test's own subject — it is the harness's
"which creature is local to this rig" storey resolution flipping to the storey below,
sensitive to whatever position or scene state the previous test left behind (B1's floor
sits directly on B2's ceiling at a 3.75 m pitch; a rig or sample point landing in the
seam bin resolves down). Whoever chases it: reproduce by running the suite in order, not
the test alone — alone it passes, which is the signature of the whole class.

---

## Regenerating assets

Everything is generated by code — no samples, no downloads, no licensed content.

```bash
tools/audio/.venv/bin/python tools/audio/gen_footsteps.py
tools/audio/.venv/bin/python tools/audio/gen_monster_audio.py
tools/audio/.venv/bin/python tools/audio/gen_ambience.py
tools/audio/.venv/bin/python tools/audio/gen_items.py
tools/audio/.venv/bin/python tools/audio/gen_ui.py

BL=/Applications/Blender.app/Contents/MacOS/Blender
$BL --background --factory-startup --python tools/blender/gen_monster_ai.py
$BL --background --factory-startup --python tools/blender/gen_player_model.py
$BL --background --factory-startup --python tools/blender/gen_mapkit.py
$BL --background --factory-startup --python tools/blender/gen_props.py
```

Each generator measures its own output and refuses to emit something unusable — silence,
clipping, a DC offset, an empty mesh, a model at the wrong unit scale, animations exported
twice.

> **Blender's `--background` exits 0 even after a Python exception.** Never trust its exit
> code. Grep for `ASSET_FAILED`, which is what the generators emit on failure.
> `tools/ci/run_blender_generators.sh` does this for you and also fails when a generator
> writes nothing.

After regenerating, reimport so the post-processors run — Unity does not apply them
retroactively: `Horror ▸ Assets ▸ Reimport Audio And Models`, then §10 above.

---

## Playing it

### First open

`open -a "Unity Hub"`, add `/Users/doogi/horror-game/unity/HorrorGame`. First import takes
several minutes — it resolves Mirror, Steamworks.NET and FizzySteamworks.

### Alone

Open `Assets/Scenes/Map_FirstSketch_Solo.unity` and press Play. One runner, eight
creatures, a `MatchDirector`, the whole descent. The scene is assembled from the raw map
by:

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SoloPlaytest.BuildBatch -logFile /tmp/solo.log
```

which also reads the animation wiring back **out of the saved scene** rather than
asserting it about the object it just built:

```
[SoloPlaytest] §05 ANIMATION WIRING, read back from Assets/Scenes/Map_FirstSketch_Solo.unity
  — 1 PlayerAnimatorDriver block(s).
```

Expect one warning you cannot fix from here: `[Player] No renderer under this rig reads as
the owner's hands` — [B-017](BLOCKERS.md#b-017). In first person you will see no part of
yourself.

### Two players on one machine

`HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)`

Local hosting, Discord for voice, Steam offline on development App ID 480. §14 puts this
before Steam deliberately: 「직접 만져봐야 나온다」.

**This is still the single highest-value thing anyone can do with this project.** Every
automated gate above is green or explained; not one of them can say whether solving the
same maze eight times is worth doing. Watch for:

| Watch | What is known about it |
|---|---|
| Does the descent feel like a race? | nothing — unmeasured |
| Can you shake the creature? | **always.** 720/720 places escape it ([B-007](BLOCKERS.md#b-007)) |
| Does choosing a 투하구 matter? | the mapping is fixed, so it should reward map knowledge — unmeasured |
| How long does one descent take? | 89 s for a pathfinder; unknown for a person |
| Does the host's copy of you match what you did? | yes, on `127.0.0.1`, per `NetHumanRunnerTests` |

---

## What to check when something breaks

| Symptom | Look here first |
|---|---|
| Rules behaving oddly | `dotnet test` — 512 tests name the section they defend |
| A test suite that reports nothing and exits 0 | you passed `-quit` to `-runTests` |
| A build that "succeeded" but is broken | you passed `-quit` to the build |
| A creature that walks partway and stops | `PathPartial`. §5 — and `NavMeshWorldProbe` must use path length, not straight-line distance |
| A map that plays badly | §5b, then [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) F-007 |
| A number from the simulator that looks wrong | it is. [B-012](BLOCKERS.md#b-012) |
| Unity batch command fails immediately | the editor holds the project lock. Close it |
| `'cmath' file not found` | damaged Command Line Tools, not Unity. §7 |
| `Failed to process scene before export` | almost never the scene — `StrictMode` blaming the first scene for an unrelated logged error. §7 |
| A gate that is green for a scene you did not build | §5a. Check the stamp |
| A number disagrees with the design | `GameConstants.cs` is the only authority. A literal anywhere else is a bug |
