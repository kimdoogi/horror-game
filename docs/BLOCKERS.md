# Blockers

Things that stop the game working, as opposed to design questions. Balance
contradictions live in [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md). Art defects that
do not stop the game live in [ART.md](ART.md) §7.

Last verified: 2026-08-01 06:30, Unity 6000.3.21f1, macOS 24.3.0.

---

## B-008 · The player could not leave the entrance storey — a 계단 only the monster could use

**Status:** 🟢 **CLOSED**, opened and closed 2026-08-01 · found by playing, not by a gate

The owner reported the game was unplayable past the first floor. It was: measured with
the check that did not exist yet, a `CharacterController` with the player's dimensions
could stand in **8 019** places, all of them on B1, and reach **3 of 15** 후보 지점 and
**7 of 41** 전리품 spawns. Four of the building's five storeys had nothing on them a
person could occupy.

At the same moment `NavMeshAudit` reported **1830/1830 pairs complete, 100 %, 1 island,
monster reach 19/19**. Both numbers were correct. They are measurements of different
bodies:

| | climbs | stands | is wide |
|---|---|---|---|
| NavMesh agent (`ProjectSettings/NavMeshAreas.asset`) | `agentClimb` 0.75 m | 2.00 m | eroded region, no body |
| Player (`PlayerFeelHarnessMenu.BuildRig`) | `stepOffset` 0.40 m | 1.75 m | 0.60 m capsule |

### Two defects, and neither was a tall step

The first report of this pointed at a 0.645 m riser in `Stairwell_Metal.fbx`, sitting
between the two climb limits. **That measurement was an artefact of how it was taken.**
Re-measuring the shipped FBX in Blender and printing every horizontal face with its
area and extent, the 0.645 m gap is between `z=5.415` and `z=6.060` — a 16 cm pipe-collar
clip on the service riser and the ceiling joists above it, 5.4 m and 6.1 m in the air in
a shaft nobody can reach. It was produced by sorting *all* 134 distinct horizontal-face
heights in the whole piece, ceiling and trim and pipework included, and diffing
neighbours. No tread was ever involved: every riser on the shipped climb measured
0.2344 m. What actually blocked the player was:

**1. Every 계단 was floored over 0.015 m below its top landing.** `MapSceneBuilder`
pours a zone floor slab across each zone's whole rectangle and caps the storeys nothing
stands on, and a zone rectangle includes the cells a stairwell rises through.
`KeepOutOfNavMeshBake` already excluded both from the bake — its own comment says they
lie "0.16 m under the landing it seals off" — but they kept their `MeshCollider`. So the
monster walked down a stair that was, to a capsule, a lid. Probed in the shipped scene:

```
StairwellMetal_L1_15_30 (climbs -3.75 → 0.00 m)
  ray down the flight-B column: FloorTileConcrete_L0_16_29 at y = -0.015
  ray down the flight-A column: CeilingCap_L1_14_29       at y = -0.014
```

**2. The stair had no tread a 0.60 m capsule could stand on.** Eight treads a flight at
`STAIR_GOING` 0.30 m with a 25 mm nosing overhang leave 0.275 m of usable tread. A
capsule standing with its feet on a tread reaches `PLAYER_TREAD_REACH` = 0.293 m forward
at the height of the step in front of it. 0.275 < 0.293, so there was **no position on
any tread** where the player was not inside the next riser, and none on the shaft floor
where they were not inside the first one. `gen_mapkit.py` checked `STAIR_RISE <=
PLAYER_STEP_OFFSET` and passed — it had never been told the player has a width.

### The fix

- `MapSceneBuilder.SealsAShaft` — no zone floor slab and no ceiling cap is poured across
  the cells a 계단 rises through. A stairwell is supposed to be a hole between two
  storeys; the piece brings its own floor, walls and ceiling.
- `gen_mapkit.py` reprofiled: 6 treads a flight at 0.3125 m rise × **0.38 m** going, the
  nosing no longer overhangs the riser, approach 0.52 m. The depth budget is untouched —
  0.52 + 2.28 is the same 2.80 m of shaft that 0.40 + 2.40 was — so `STAIR_LANDING_D`
  stays at the 2.05 m B-001 was about. `design_checks` now holds the piece to the
  player's capsule as well as the agent's.
- **`stepOffset` was not touched.** §12's escape geometry is derived from what a player
  cannot climb, and a player who can step 0.65 m can climb crates, debris and the van.

Re-measured after the rebuild: every surface on the climb — shaft floor, six treads,
mid-landing, six treads, top landing — **max riser 0.3125 m**, against a 0.40 m step
offset with a 0.05 m margin held in both the Blender checks and the Unity gate.

### Why this is the important half

This is [B-001](#b-001) a second time and the project did not notice, because the gate
B-001 produced measures the antagonist and was read as measuring the level. Nothing in
the project had ever asked whether a *player* can traverse the building.

`HorrorGame.EditorTools.PlayerReachAudit.AuditBatch` now does, `MapSceneGenerator`
gates scene generation on it, and [TESTING.md §4c](TESTING.md) documents it beside §4b
with the same warning. It sweeps the real capsule with `Physics` casts and never
consults the NavMesh, because the premise is that the two disagree. It measures headroom
too: a beam at 1.60 m stops a 1.75 m player exactly as completely as a tall step and
reports nothing either.

Closing measurement, `Assets/Scenes/Map_FirstSketch.unity` at seed 1204:

```
[PlayerReach] PASS
  standing places  33658
  storeys          6/6      B0 231 · B1 9675 · B2 5280 · B3 9167 · B4 4923 · B5 4382
  후보 지점         15/15
  전리품 spawns     41/41
  계단              9/9 walked end to end
  tallest climb    0.313 m       tightest headroom 2.38 m
```

---

## B-007 · The map can no longer be regenerated — §12's 17th rule rejects the map that ships

**Status:** 🔴 **OPEN**, opened 2026-08-01 06:25 · reproduce with the command below

`66ce930` implemented 시야 차단 지점 간격 as `MapValidator`'s 17th rule. The rule is
right and the map was never fixed to satisfy it, so **the generator now refuses to
write the level the game ships**:

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -nographics -silent-crashes -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.GenerateFromCommandLine \
  -logFile /tmp/gen.log; echo "exit=$?"
```

```
exit=1
[SceneGen] §12 rejected the map at seed 1204, so nothing was written.
[FAIL] sight-break-spacing — 시야 차단 지점 간격 15~25m (질주 60m에 3~4번의 기회)
```

`MapSceneGenerator.Generate` gates on `MapQualityReport.Measure(map).Buildable`, which
is `MapValidator.Validate`'s verdict. The gate is doing its job. The consequence is
that `Assets/Scenes/Map_FirstSketch.unity` **is now a committed artefact its own
generator would reject** — it was written on 2026-08-01 while the checklist still had
16 rules.

### What still works, and what does not

The committed scene is intact and every measurement in
[STATUS.md §1](STATUS.md) was taken against it: NavMesh 1830/1830 with 1 island,
`MonsterChaseTests` 4/4, the solo loop, the standalone build. **Nothing regressed at
runtime.** What is blocked is the authoring loop — `HorrorGame ▸ Scene Gen ▸ Generate
First Map` now fails, so the map cannot be re-rolled, re-seeded or edited-and-rebuilt
until the geometry satisfies the rule.

### Why this is a blocker rather than a finding

It is the same shape as [B-005](#b-005): a gate that correctly refuses, protecting a
scene that is already past it. Anyone who regenerates the map to change one thing gets
an error and no scene, and the obvious reading — "the generator is broken" — is wrong.
The generator is fine; the map is.

### The fix is [F-007](BALANCE-FINDINGS.md#f-007), not a code change

Do **not** relax `SightBreakPointSpanMax` to make this go away. The rule and the
10/10 TooEasy grade are the same defect measured twice: 79 bends at a mean spacing of
4.1 m, none inside §12's 15–25 m band. Fixing the map's corner density closes this
blocker and moves the 주자 테스트 into the 5–7 band in one change. Until then this
entry and F-007 stay open together.

---

## B-006 · The core solution did not build — the simulator never compiled the file it depends on

**Status:** 🟢 **CLOSED** 2026-08-01 03:05 · verified by
`dotnet build core/HorrorGame.sln -c Release` → `오류 0개`

**`dotnet build core/HorrorGame.sln -c Release` failed on two errors.** This is the
second seam between the same four parallel passes that produced
[B-005](#b-005), and like that one it was invisible to the pass that caused it.

```
MapQualityReport.cs(29,13): error CS0246: 'RunnerCensus' 형식 또는 네임스페이스 이름을
  찾을 수 없습니다. [core/HorrorGame.Sim/HorrorGame.Sim.csproj]
MapQualityReport.cs(76,16): error CS0246: 'RunnerCensus' …
빌드하지 못했습니다.    오류 2개
```

The map pass added `Editor/SceneGen/RunnerCensus.cs` and made `MapQualityReport` hold
one. Inside Unity that just works — the assembly definition globs the folder. But
`HorrorGame.Sim.csproj` does **not** glob: it lists the engine-free map-authoring files
by name, deliberately, because that list is the project's statement of which files are
safe to compile outside Unity ([F-006](BALANCE-FINDINGS.md#f-006) is the whole reason
the simulator compiles the map at all). A new file in that folder is not picked up, and
nothing in the Unity project can notice.

So Unity compiled clean, all 560 tests passed, and the balance simulator — the tool the
night's headline number comes from — could not be built at all.

### The fix

`RunnerCensus.cs` added to the `<Compile Include>` list in
`core/HorrorGame.Sim/HorrorGame.Sim.csproj`, with a comment naming this incident so the
next person adding a file to `Editor/SceneGen/` knows the list is manual and why.

### Why it is filed here rather than shrugged off

The failure mode is the dangerous one: **a green Unity suite and a broken build of the
tool that measures the design.** `dotnet test core/HorrorGame.Core.Tests` also stays
green through it, because the test project does not reference the simulator. The only
command that catches it is the solution build, which is why
[TESTING.md §2](TESTING.md) puts it in the sweep and why it belongs before the
simulator run rather than after.

---

## B-001 · The monster could not reach the player

**Status:** 🟢 **CLOSED** 2026-07-31 · verified by
`MonsterChaseTests.MonsterClosesDistanceAndReachesAPlayerAcrossTheMap`

**The game has an antagonist.** The monster starts at its §12 spawn on B3 and
arrives at a player standing two storeys above it, on the real baked surface, in
27.52 s.

### The measurement that closes it

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath /Users/doogi/horror-game/unity/HorrorGame -runTests -testPlatform PlayMode \
  -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log
```

Exit code 0. `test-run result="Passed" total="4" passed="4" failed="0"`.

```
[ChaseTest]   t=20.00 s Alert at (25.3, 0.1, 51.9) path 37.2 m straight 21.1 m heading for (33.8, 0.2, 71.3)
[ChaseTest] §14 Q1 — can the monster reach a player at all?
[ChaseTest]   route            133.9 m of NavMesh path, monster spawn → (33.75, 0.18, 71.25)
[ChaseTest]   straight line    60.1 m
[ChaseTest]   chase entered    27.12 s
[ChaseTest]   reached          27.52 s
[ChaseTest]   closing speed    4.83 m/s of route, against §06's 4.8 m/s of ground speed
[ChaseTest]   worst 1 s rise   0.0 m of route (0 is a monster that never backtracked)
```

Against the state this bug was last written up in — `chase entered never`,
`NOT REACHED in 240.00 s`, `worst 1 s rise 0.0 m`, stalled at (26.8, −5.4, 36.5) for
220 consecutive seconds.

The climb is real and not an artifact of where the two were placed. From the scene:

```
MonsterSpawn   (36.25, -7.50, 11.25)      B3 저수조
PlayerSpawn_2  (33.75,  0.00, 71.25)      B1
```

7.5 m of vertical, two storey boundaries, 133.9 m of walked route, covered at
4.83 m/s — 0.6 % above §06's 4.8 m/s ground speed, which is one sub-step of
`FixedStep` rounding. `worst 1 s rise 0.0 m` says it never once turned round.

### Both halves of the fix were done

The write-up offered two candidate halves and said the first was the real fix. Both
landed, and it matters that both did.

**1 · The kit's stairs are walkable geometry and the links are gone.**
`tools/blender/gen_mapkit.py` `build_stairwell` no longer runs the dog-leg spine the
full depth of the mid-landing — the spine stops at the head of the flights and
continues below the landing as its support, so the landing is open and both flights
bake as one surface. `MapSceneBuilder.ForbidStairLinks` now *deletes* any
`NavMeshLink` it finds and `MapSceneBuilder.VerifyStairwellsAreWalkable` fails the
generation if a shaft bakes as more than one island. From the run that wrote the
current scene (`/tmp/mapgen.log`, 12:09):

```
[SceneGen] 7 계단 verified as single walkable surfaces, no NavMeshLink anywhere in
the map. §06's monster steps along NavMeshPath.corners, so every storey boundary it
crosses has to be geometry it can stand on.
```

`grep -c NavMeshLink Assets/Scenes/Map_FirstSketch.unity` → `0`.

This is the half that fixes the player too. A `NavMeshLink` is a gap with nothing to
step onto; a human being cannot use one at all. The stairs are now something both the
player and the monster walk up. `Shots/verify_spawn2.png` is a picture of one:
diamond-plate treads, a landing, headroom.

**2 · The probe no longer deadlocks on a duplicated path corner.**
`NavMeshWorldProbe.TryGetNextPathPoint` returned `_corners[1]` unconditionally.
It now returns the first corner further than `MinWaypointAdvanceSqr` (0.09 m²) from
the mover, and falls back to the last corner when every corner is coincident. Worth
keeping even with the links gone: a coincident corner is not unique to links, and one
of them used to freeze the antagonist for the rest of the match with no error of any
kind.

### The other three tests in the suite, all passing

**§06 어그로 해제 —**
`AggroReleaseSendsTheMonsterToTheLastSeenPositionNotThePlayer`, Passed:

```
[ChaseTest]   sighted from     (36.3, -7.4, 11.3), last seen at (34.8, -7.4, 16.7)
[ChaseTest]   hid at           (41.3, 0.4, 73.8), 62.7 m away, no line of sight
[ChaseTest]   separation       never below 57.4 m, against §06's 12 m
[ChaseTest]   sight regained   no
[ChaseTest]   released after   3.02 s, against §06's 3 s
[ChaseTest]   headed for       (34.8, -7.4, 16.7) — 0.0 m from the last sighting, 57.4 m
                               from where the player actually is
```

**§12 ① S자 통로 —** `AnSCorridorOfTwoTenMetreLegsBreaksAChase`, Passed:

```
[ChaseTest]   corridor         62.5 m of route, 3 corner(s), 2.5 m clear width
[ChaseTest]   aggro started at 10.0 m (§12's endorsed row)
[ChaseTest]   gap at corner 1  11.8 m, against §12's 14.4 m for a single corner to hold 3 s
[ChaseTest]   released         5.50 s after aggro, at 12.0 m
[ChaseTest]   caught           no
[ChaseTest]   longest cover    3.02 s unbroken, against §06's 3 s
[ChaseTest]   sight regained   0 time(s) mid-chase
[ChaseTest]   runner covered   30.9 m, monster 22.7 m
[ChaseTest]   monster speed    4.13 m/s of corridor, against §06's 4.8 m/s
[ChaseTest]   gap opened at    1.47 m/s while sprinting, against §06's 0.8 m/s (5.6 − 4.8)
```

**§12 단일 모퉁이는 실패한다 —** `ASingleCornerDoesNotBreakAChase`, Passed. Same
route length, same aggro distance, same runner, one corner instead of three:

```
[ChaseTest]   longest cover    1.98 s unbroken, against §06's 3 s
[ChaseTest]   sight regained   1 time(s) mid-chase
[ChaseTest]   released         NO
[ChaseTest]   caught           12.54 s
[ChaseTest]   runner covered   52.5 m, monster 60.1 m
[ChaseTest]   monster speed    4.80 m/s of corridor, against §06's 4.8 m/s
[ChaseTest]   gap opened at    0.80 m/s while sprinting, against §06's 0.8 m/s (5.6 − 4.8)
```

**4.80 against §06's 4.8, and 0.79→0.80 against §06's 0.8.** The design's most
load-bearing claim — "괴물이 달리기보다 0.3만 빠른 것이 핵심이다" — measured on real
geometry, correct to 1 %. And 1.98 s of cover from one corner against the 3.02 s two
10 m legs buy is §12's arithmetic reproduced.

### What this unblocks, and what it does not

Unblocked: §14 Q1 「추격이 재밌는가?」 can now be *played* on `Map_FirstSketch`. The
Runner role has a map to be a Runner on. `ObjectiveResolver`'s unreachable-site
fallback is exercisable.

**Not** unblocked: whether the chase is *fun* is still a person's judgement and no
test in this repo claims otherwise. §14 says questions 1 and 2 decide the project and
「직접 만져봐야 나온다」. Two instances, Discord, a human. That is the next thing.

---

## B-002 · The EditMode solo-match test fails on a broken Mirror package install

**Status:** 🟢 **not reproducing** as of 2026-08-01 · was 🟠 open, environment ·
never a code regression

`SoloMatchLoopTests.Solo_match_runs_the_whole_round_trip` **passes**, and EditMode is
green at 70 of 70:

```
total 70 passed 70 failed 0 result Passed
  Passed HorrorGame.Gameplay.MatchEditor.SoloMatchLoopTests.Solo_match_runs_the_whole_round_trip
```

Nothing was done to fix it. The package cache was rewritten at some point during the
map and art passes and the missing `.meta` stopped being reported, which is consistent
with this always having been an environment fault rather than a code one. **Treat it
as dormant, not closed** — B-004 says the Mirror package is an unofficial repack, and
until that is replaced this can return on any reimport. If it does, the fix in the
original write-up still applies:

> Reinstall `com.mirrornetworking.mirror`, or have this one test `LogAssert.Expect`
> this one message — but **not** by widening the test's log tolerance in general.

<details>
<summary>The failure as it was recorded on 2026-07-31</summary>

It failed on an unhandled log message rather than an assertion:

```
[TestRunner] Failed: HorrorGame.Gameplay.MatchEditor.SoloMatchLoopTests.Solo_match_runs_the_whole_round_trip
  Unhandled log message: '[Error] Asset Packages/com.mirrornetworking.mirror/Mirror/Assets
  has no meta file, but it's in an immutable folder. The asset will be ignored.'.
  Use UnityEngine.TestTools.LogAssert.Expect
  UnityEditor.AssetDatabase:Refresh ()
  HorrorGame.EditorTools.SoloPlaytest:BuildScene () (at SoloPlaytest.cs:150)
```

**The loop it is testing works.** The same code path run outside the test harness
passed end to end — see `SoloPlaytest.VerifyBatch` in [STATUS.md](STATUS.md) §1.4.
What failed was the harness's zero-tolerance for unexpected `Debug.LogError`, and the
error came from the package cache, not from the match.

</details>

---

## B-005 · Regenerating the map unregistered the scene 시작 loads

**Status:** 🟢 **CLOSED** 2026-08-01 · verified by
`UiFlowTests.Menu_ComesUp_AndStartReachesTheMatchScene`

**The main menu's 시작 button did nothing.** Found while integrating four parallel
passes; it is a seam between the one that grew the map and the one that built the
front end, and neither could see it alone.

`MapSceneGenerator.RegisterScenes()` rewrites Build Settings **wholesale** rather than
appending, and it named only `Bootstrap.unity` and `Map_FirstSketch.unity`. So
regenerating the map deleted `Map_FirstSketch_Solo.unity` — the assembled scene with a
player, a monster and a `MatchDirector` in it — from the build list.

`SceneManager.LoadSceneAsync` returns `null` for a scene outside that list instead of
throwing. `GameShell` therefore bounced silently back to the menu: no exception, no
error line, no compile failure. The only symptom was a button that appeared to be
unwired.

```
total 42 passed 41 failed 1 result Failed(Child)
  Failed HorrorGame.Tests.PlayMode.UI.UiFlowTests.Menu_ComesUp_AndStartReachesTheMatchScene
    Unhandled log message: '[Error] Scene 'Map_FirstSketch_Solo' couldn't be loaded
    because it has not been added to the active build profile or shared scene list'
```

### The fix

`SceneGenPaths.MatchScene` now names the scene in one place, `BootstrapSceneGenerator.
MatchScenePath` aliases it, and `MapSceneGenerator.RegisterScenes()` includes it when
it exists. The two writers can no longer disagree about which scene 시작 loads.

```
total 42 passed 42 failed 0 result Passed
```

The test was written by the front-end pass and is the reason this was caught at all —
it exists precisely to notice a menu button that does nothing, which no compile check
and no log line can see.

---

## B-003 · Two 개방 공간 are silently dropped from every map generation

**Status:** 🟠 open · the map is still valid without them, but the pipeline logs two
errors on every run and nobody has decided whether that is acceptable

`MapSketch` places two `HallOpen20x20` rooms under a corridor on the storey above and
then refuses to build them, at `LogError`, on every single generation:

```
[SceneGen] HallOpen20x20 at (4,13@L1) is 6.3 m tall on a 3.75 m storey, so its roof
rises into the storey above and leaves 7 place(s) up there under 2 m of headroom —
(4,20@L0), (5,20@L0), (6,20@L0), (7,20@L0), (8,20@L0), (9,20@L0). §06's monster is
2.30 m; it cannot path through any of them, so the room is not built. Move the 개방
공간 out from under the corridor, or move the corridor.
[SceneGen] HallOpen20x20 at (16,14@L1) is 6.3 m tall on a 3.75 m storey … 9 place(s)
… the room is not built.
```

`MapSketch.cs:1101`. The map still passes all 16 §12 checklist rules because other
rooms satisfy `open-adjacent-to-maze`, and the 주자 테스트 still grades 7/10 Balanced.
So the consequence is design intent lost quietly, not a broken build — but a
generator that prints two `LogError`s on the happy path means nobody can use "the log
is clean" as a gate. Either place the halls somewhere legal, or downgrade the message
and record the compromise.

---

## B-004 · The networking library is a stranger's repack, not Mirror

**Status:** 🔴 open · supply chain · must be resolved before anything ships

### What is installed

`Packages/manifest.json` pulls `com.mirrornetworking.mirror` 96.6.4 from OpenUPM. Its
own `package.json`, read from the package cache:

```
name               com.mirrornetworking.mirror
author             Chaoyang <960208781@qq.com>
                   https://github.com/960208781/UnityMirror.git
documentationUrl   https://github.com/MirrorNetworking/Mirror/blob/master/README.md
```

OpenUPM's registry entry names the same author and carries no `repository` field.

### Why this matters more than it looks

The package **id claims Mirror Networking** and the documentation URL points at the
official repository, so everything visible from `manifest.json` reads as official. The
code actually being compiled comes from an individual's fork.

This is the library that carries every byte of §13's P2P traffic between four players'
machines. On a game that is going to be sold, a networking layer of unverified
provenance is not a style question.

Nothing here says the repack is malicious. It says nobody has checked, the name implies
a provenance it does not have, and neither is acceptable at release.

### What the official route is

Verified against the upstream repository:

- `github.com/MirrorNetworking/Mirror` has **no `package.json`** at the root or under
  `Assets/Mirror`, so it cannot be installed as a UPM git dependency. Every
  UPM-installable "Mirror" is therefore somebody's repack.
- Official distribution is a `.unitypackage` from GitHub releases — currently
  **v96.11.1**, five versions ahead of the repack's 96.6.4 — or the Asset Store.

### The fix

Vendor the official `.unitypackage` into `Assets/`, and delete the OpenUPM dependency
and its scoped registry from `manifest.json`. Costs a larger repository and manual
updates; buys a dependency whose origin can be pointed at.

Do it as its own change, with the full suite run afterwards: it swaps the assembly the
whole `Assets/Scripts/Net/` layer compiles against, and `FizzySteamworks` is built on
top of it.

### Also worth checking at the same time

`com.mirror.steamworks.net` (FizzySteamworks) comes from `github.com/Chykary/FizzySteamworks`,
which IS the project's own repository — that one is fine. `com.rlabrecque.steamworks.net`
comes from `rlabrecque/Steamworks.NET`, also the real one. Mirror is the only dependency
whose publisher is not the project.

### Not urgent tonight, urgent before release

The playable build is single-player; Mirror is compiled in but not exercised. So this
blocks the four-player milestone and the store page, not tomorrow's playtest.

---

## B-009 · The NavMesh being audited is not the one for the geometry just built

**Status:** 🔴 open — blocks the descent map reaching disk · **Found** 2026-08-02

### The evidence, which is the whole finding

Three regenerations, three genuinely different sets of geometry, one identical
audit:

| run | geometry | audit |
|---|---|---|
| caps as authored | 29 caps, 14 colliding pairs | 8717 complete · 93.5% · 17 islands · monster 0/3 |
| caps 3 cells apart | fewer caps, **0** colliding pairs | 8717 complete · 93.5% · 17 islands · monster 0/3 |
| **no caps at all** | **zero** DeadEndCap pieces placed | 8717 complete · 93.5% · 17 islands · monster 0/3 |

Byte-identical, to the pair. The tile lists differ — the collision count was
measured going 14 → 0 outside Unity, and the third run places no caps at all —
so the audit cannot be reading the surface those tiles produce.

### What this rules out

Everything that has been suspected so far, and a lot of work went into each:

- dead-end caps colliding (they do, and it does not matter here)
- alcove spacing (changing it moved nothing)
- the radial layout's adjacency patterns

The marker names in the island dump ARE from the new map — `CandidateSite_B8
굴착층_21` and so on — so the MARKERS come from the current build. Only the
surface they are sampled against is suspect.

### Where to look first

`Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset` is a tracked
file and has been showing as modified since before this hunt started. If the
bake writes there and the audit samples a previously-loaded instance, or the
bake is skipped when the asset exists, every number above is explained at once.

### Why it matters beyond this map

`NavMeshAudit` is the gate that decides whether a level ships. If it can report
on a surface that is not the one just built, then every green audit in this
project's history is worth less than it looked, including the ones that closed
B-001. Whatever the cause, the fix has to include a check that the audit and the
build agree — a triangle count, a bake timestamp, anything that cannot be stale.


---

## B-010 · The middle of a radial storey has no piece, so it seals itself

**Status:** 🔴 open — the last thing between the descent map and a scene on disk ·
**Found** 2026-08-02 · **Supersedes the NavMesh half of** B-009

### What happens

`RadialStorey` draws the 3 × 3 middle as nine ordinary corridor cells. The kit
tiles a cell from its neighbour mask, so nine cells in a square become four L
corners, four T edges and a cross — nine 2.5 m passages, each walled on the sides
its own mask calls closed, meeting at their own walls. The audit finds it as a
sealed island:

```
[1] MonsterSpawn
```

That single line is why §06's creature reached 0 of 3 targets in every run of
this map. On B8 the same middle is §02's finish, so it is also the reason nobody
could win.

### What has already been tried, and measured

| attempt | complete | islands |
|---|---|---|
| filled 3 × 3 | 98.1% | 11 |
| a plus instead | **91.2%** | **18** |

The plus reads unambiguously to the tiler and should have been the fix. It was
worse: each of its four arms is a blind cell, each blind cell takes a
`DeadEndCap`, and four caps ringing the middle seal it more thoroughly than the
block did. Reverted, with the reasoning left in `RadialStorey` because the next
person will have the same idea.

### The fix

**The middle needs a PIECE.** Every version of this that stays inside the
corridor kit hits the same wall, because the corridor kit's whole contract is
"one cell, walls where the mask says" and a room is not that.

Add `ChamberOpen3x3` to `tools/blender/gen_mapkit.py`: 7.5 m square, corner
piers, openings mid-edge on all four sides, the same clear height as a corridor.
`build_junction_cross` is the closest model — it already has no walls at all, and
a chamber is that at three times the span. Then:

1. `MapKitPiece.ChamberOpen3x3` + its filename and height in `MapKitCatalogue`
2. `RadialStorey` places it with `Room()` rather than nine `Corridor()` calls
3. `MapSceneBuilder.VerifyRoomWalls` already checks that no passage crosses a
   room's wall anywhere but a doorway — which is exactly the check this needs,
   and it is why a room is safer here than a pattern of corridors

### The piece is done. The wiring is not, and here is the wall it hits

`Chamber_Open_3x3` is authored, exported and registered (commit d92a023). Wiring
`RadialStorey` to place it with `Room()` was tried and does not work yet, for a
reason that has to be solved first:

**`Room()` produces geometry and no graph.** The map graph is built from
`_corridor` cells; `_rooms` cells are excluded from it (`InsideRoom`). So a
chamber at the middle leaves its four dock cells with exactly one corridor
neighbour each — four dead ends — and §12 correctly refuses to hang the inner
gate's door on one:

```
A door was asked for at (12,10@L0), but no passage runs through that cell —
it is a junction, a bend or an end.
```

It also refuses to mark §02's finish inside the room, for the same reason and
just as correctly: a place nobody can stand in must not be counted.

Two failures in a row, each naming itself at GENERATION time. That is the whole
argument for the room — the same two defects inside the corridor kit showed up
as one coordinate in an audit island list three days later.

### What the wiring needs

`OpenRoom()` is the model, not `Room()`. §12 counts 개방 공간 as graph nodes, so
`OpenRoom` must already put cells in the graph while the hall piece covers them.
Whatever it does for `HallOpen20x20`, the chamber needs the same: cells in
`_corridor` for the graph, excluded from corridor TILING, covered by one piece.

Until then the map is back to a filled 3 x 3 of corridor cells — measured 98.1%
complete, 11 islands, §12 passing everything it still asks — with the middle
sealed. That is the best state so far and it is what is committed.

### Do not skip the last line

A room piece brings `VerifyRoomWalls` with it. That check is the reason to do
this properly rather than to keep rearranging cells: it fails loudly at generation
time instead of silently at bake time, which is the difference between this bug
taking an afternoon and taking three days.
