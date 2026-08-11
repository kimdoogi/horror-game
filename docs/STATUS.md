# Project status

Where 하강 actually stands at **commit `9f0f447`**, 2026-08-10.

> **If you are the owner deciding whether to put this on Steam, read the one-line answer
> and the table under it, then §2 and §5.** §1 is real and it is not the part that decides
> anything.

Every number on this page is quoted with the run it came from, and the command that
produced it is in [TESTING.md](TESTING.md). One caveat, added 08-12: the 08-11 disk
cleanup (`55520b0`) deleted `dist/test-results/` and `dist/logs/` along with 29 GB of
stale players. Numbers below that cite `playmode-results.xml` are therefore quoted from
**a run whose artefact no longer exists** — they are dated, they were real, and they are
not re-openable. Each such citation says so inline. Nothing is
carried forward from an earlier edition of this document: the previous edition described
commit `a3e268e`, seven days and forty commits ago, and most of what it said has since
stopped being true in one direction or the other.

**Three kinds of provenance appear below and they are not interchangeable:**

- **measured here** — re-derived while writing this page, on 2026-08-10, by the author of
  this page. All of these are `dotnet`, shell, or a read of a file on disk.
- **owner's gate, 2026-08-10** — measured by the owner on this machine between 00:03 and
  00:22 local (the logs' own headers say `2026-08-09T15:03Z`–`15:22Z`), in
  `/private/tmp/claude-501/-Users-doogi-horror-game/8448440f-eac2-4700-a1cd-d3fc1aa1367f/scratchpad/biggate/`.
  Everything Unity on this page is from that run. **The author of this page did not run
  Unity and cannot re-run it.**
  <br>*Precisely:* that gate ran against the working tree that was committed two minutes
  later as `b92ae78` and `9f0f447`. The tree is clean at HEAD and the artefacts the gate
  wrote — including the scene carrying `gen-20260810-000424-seed20260802` — are the ones
  committed, so the gate describes HEAD. It is not a post-commit run, and that distinction
  is the sort this project has been bitten by before.
- **carried, dated** — an older measurement quoted with the date it was taken, because
  nothing since has re-taken it. Treat the date as part of the number.

**Environment:** Unity 6000.3.21f1, macOS 24.3.0 (arm64), .NET 9 at `~/.dotnet` (not on
`PATH`). One Unity process may hold the project lock — close the editor before any batch
command, and run them one at a time. `dotnet` does **not** take that lock.

---

## 0 · What this game is now

**A 20-player competitive maze descent.** Twenty runners start scattered on the rim of
B1. Each storey is a concentric maze: rim → middle ring → inner ring → the middle, with
fewer gates at each step. The middle of every storey holds 투하구 — drop shafts — and
falling down one puts you on the **rim** of the storey below, so the maze is solved
eight times, not once. The middle of B8 is the finish. **One person wins.** A creature
patrols each storey; being caught sends you back to B1 rather than ending your race, and
there is no way to kill it or to kill each other. Design target for one match:
**12–20 minutes** (§01, `game-design.md:88`).

Since the previous edition the race has also gained a **총** (four are wired into the
map at match start), five **깜짝** haunted-house startles, and a **근접 음성** channel —
and the maze is no longer one building: `DescentRoster.txt` names **eight**, and the
lobby picks one from the seed.

Until 2026-08-02 this was a four-player co-operative looting game. The pivot
([DESCENT-PIVOT.md](DESCENT-PIVOT.md), design v1.0) deleted §04's five roles and §08's
economy. Step 7 of that pivot — the actual deletion rather than the gating — landed at
`e8c67ae` on 2026-08-03, and it took the balance simulator and most of the co-op test
surface with it. Where the residue still matters below, it says so.

---

## The one-line answer

**A macOS Release player now builds, the map passes its own gates for the first time, and
the art is photographs — but the Windows player is still an unshippable Mono fallback, the
build sitting on disk is 25 commits and one dirty working tree behind HEAD, CI's required
job has been red for six days, and no human has ever played a match of this game.**

The previous edition's one-line answer made three claims. Two of them are now false:

| The old claim | Status | What replaced it |
|---|---|---|
| "the creature is not a threat to anybody" | **disputed, not confirmed** | The 주자 테스트 still grades 10/10 TooEasy and 680/680 escapable — but the defect that was blamed for it, [B-007](BLOCKERS.md#b-007)'s sight-break-spacing, is **closed and the rule now passes**, and [F-013](BALANCE-FINDINGS.md#f-013) argues the 50–70 % band is a co-op instrument no §12-legal map can ever satisfy. On the instrument F-013 proposes instead, the map reads median 7.5 s inside a 3.4–20 s band — with 20 of 680 places now *over* the ceiling. See §2.1. |
| "there is no shippable build" | **false** | `dist/last-build-summary.txt`: macOS universal, Release, **IL2CPP OK, exit 0**, 2026-08-07. The toolchain fault that caused the old failure is gone from this host — measured here, §1.10. |
| "no human has ever played a match" | **still true, and it is still the most important sentence on this page** | There is no playtest record anywhere in `docs/`, no session note, no telemetry. Not one match. Not one person. |

---

## The distinction that decides this project

This repository has an unusually thick automated gate and an unusually thin human one.
An owner deciding whether to spend money needs both columns on the first screen, because
almost every impressive number in §1 is in the left one.

| Verified by machine | Never seen by a human |
|---|---|
| The map writes through its own gates with no override, and the scene on disk names the run that wrote it (§1.1) | Whether solving the same maze eight times is interesting |
| 8 of 8 storeys fully reachable by the creature, fully walkable by the player capsule (§1.2, §1.3) | How long a match actually takes. §01 wants 12–20 min; the only number in existence is a pathfinding robot's 171 s |
| A runner descends B1→B8 through seven chutes and finishes (§1.4) | Whether one winner and nineteen ranked finishers is a race or a queue |
| 357 engine-free rules tests, 0 skipped (§1.8) | Whether twenty people in one lobby works at all — twenty *sockets* in one process is the whole of the evidence |
| Twenty seats accepted, twenty-first refused, over real sockets (§1.7) | Whether the creature is frightening |
| Six of eight zones inside all four of ART.md's exposure bands (§1.11) | What B7 수몰층 and B8 굴착층 look like — **no frame of either has ever been taken** (§2.8) |
| A Release IL2CPP player builds at exit 0 (§1.10) | Whether that player runs. Nobody has launched it |

**The single highest-value action available on this project is still two people, two
instances, twenty minutes.** It has been the highest-value action for ten days. Every gate
above was built to make that session legible when it happens, and none of them substitutes
for it.

---

## 1 · What works, with the evidence

### 1.1 The map writes through its own gates, with no override

Owner's gate, 2026-08-10, `biggate/4_map.log`:

```
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.MapPipeline.RegenerateFromCommandLine \
  -logFile .../biggate/4_map.log
```

No `-forceWrite` in that command line — it is in the log's own `COMMAND LINE ARGUMENTS`
block, and `MapPipeline` refuses the flag outright anyway ([B-018](BLOCKERS.md#b-018)).

```
[SceneGen] gen-20260810-000424-seed20260802: Assets/Scenes/Map_FirstSketch.unity and
Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset were BOTH written by this
run — 6604 vertices, 215,604 bytes
```

**Half of the artefact-side check still works and half of it no longer does** (measured
here):

```bash
grep -c SceneGen_gen-20260810-000424-seed20260802 unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity   # 1
grep -c gen-20260810-000424-seed20260802 unity/HorrorGame/Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta   # 0
```

The scene carries its stamp and no `-forced` suffix. The bake's `.meta` **does not**, and
the log line above still claims it does. That is a regression in the check
[B-009](BLOCKERS.md#b-009) was closed on — see §2.7. What survives is the scene half,
which is the half that identifies which generation the level came from; what is lost is
the half that ties a *bake* to it.

The building it wrote, same log:

```
Scene contents: 1144 kit pieces, 0 props, 241 markers; graph has 680 places, 766
passages, 87 순환로, 152 막힌 길.
footprint 57.5 m × 57.5 m
B1 하역장=Concrete · B2 기록보관소=Wood · B3 기계실=Metal · B4 저탄장=Gravel
B5 저수조=Tile · B6 병동=Carpet · B7 수몰층=Water · B8 굴착층=Earth
```

`0 props` is the layout stage; the dressing pass runs after it. **680 places, not 720** —
the 2026-08-10 re-lay changed the geometry, so every per-place figure on this page moved
with it, and any figure quoting 720 is about a building that no longer exists.

### 1.2 The creature can reach everything on its own storey — 8 of 8

Owner's gate, `biggate/4_map.log`, the audit `MapSceneGenerator` runs before it commits
(and again in `biggate/5_nav.log`):

```
[NavMeshAudit] PASS
  markers          204
  pairs            2674
  complete         2674 (100.0 %, need 98 %)
  partial          0
  invalid          0
  islands          8  ← the surface is in pieces
  worst snap       0.25 m  (PlayerSpawn_14)
  monster reach    196/196 markers reachable from a MonsterSpawn on the SAME storey,
                   over 8 of 8 storeys (§06)
```

**Read the two qualifiers before quoting this.**

- **Eight islands is correct here and the log line is still wrong.** A tower whose only
  vertical links are one-way falls *is* eight surfaces by construction.
  `NavMeshAudit.Report` still prints `← the surface is in pieces` whenever `islands > 1`.
  Cosmetic, still unfixed after a week, and still exactly the kind of line somebody reads
  as a failure at 3 a.m. [B-014](BLOCKERS.md#b-014) records it as the one piece of that
  entry left open.
- **The question got easier when the game did.** This used to ask whether the creature
  could cross the whole building to a player. It cannot climb a chute, so what is measured
  now is per storey. That is the right question for this game, and it is a weaker one than
  the number it replaced.

### 1.3 A player can walk all of it, and the chutes are load-bearing — 20/20

Owner's gate, `biggate/4_map.log`, the gate that runs after the NavMesh audit (final of
two passes, taken on the dressed building):

```
[PlayerReach] PASS
  body             height 1.75 m · radius 0.30 m · stepOffset 0.40 m · slopeLimit 50°
  usable step      0.35 m (stepOffset less a 0.05 m margin)
  runner reach     storeys 8/8 · starts 20/20 reach the finish · finish REACHED
  finish (§02)     (31.25, -26.25, 31.25)
  starts (§01)     20 markers in 1 pocket
  one-way routes   14/14 투하구 usable  ·  no 계단
  chute-blind      0/20 starts reach the finish with the one-way routes deleted
  standing places  154005 a runner can get into, of 154005 found in 8 pockets
```

`chute-blind 0/20` is the line worth understanding. Delete the one-way routes and
**nothing** reaches the finish — so the audit cannot pass by accident through some
stairwell nobody meant to leave in. It also means the gate fails the instant one chute
breaks, which is the desired behaviour and the reason it is phrased that way.

**20/20, not the previous edition's 36/36**, because the audit now measures §01's field of
twenty rather than every spawn marker on the rim. `154005` standing places against the
previous `224236` is the same re-lay, not a shrunken building.

This is the check [B-008](BLOCKERS.md#b-008) was written after: the NavMesh audit read
1830/1830 for a building the player could not leave the ground floor of. Two bodies, two
audits, both required.

### 1.4 A runner completes the race — eight legs, seven chutes, 171 s

`DescentPlaythroughTests.A_runner_can_descend_from_the_rim_of_B1_to_the_middle_of_B8`,
PlayMode, owner's gate — `biggate/playthrough.xml` reads `total 1 passed 1 failed 0`,
transcript in `biggate/t_playthrough.log`:

```
[Test] §01 하강 완주 — B1 외곽에서 B8 중심까지, 투하구 7회.
씬 Map_FirstSketch_Solo · 시드 20260731 · 투하구 14개 (필요 14개, 층마다 남북 한 쌍)
층   외곽→중심 (B-010)                투하구
B1   PathComplete 105.2 m             ↓ B2  외곽 25.0 m
B2   PathComplete 127.4 m             ↓ B3  외곽 25.0 m
B3   PathComplete 111.0 m             ↓ B4  외곽 25.0 m
B4   PathComplete 127.3 m             ↓ B5  외곽 25.0 m
B5   PathComplete 99.7 m              ↓ B6  외곽 25.0 m
B6   PathComplete 127.2 m             ↓ B7  외곽 25.0 m
B7   PathComplete 126.1 m             ↓ B8  외곽 25.0 m
B8   PathComplete 127.2 m             도착점

도착점 (31.25, -23.45, 31.25) (판정은 X/Z만), 마지막 위치 (31.25, -26.10, 31.25),
중심까지 0.00 m — 판정 반경은 2.5 m
§02 Descended 7회 / 필요 7회 · 좌석 0의 층 B8 · 승자 0 · 완주 1명 · 경과 171초
```

Every leg returns `PathComplete` rather than `PathPartial`, which is the distinction that
hid a broken chase for a day in this project's history.

**89 s became 171 s, and that is the single most encouraging number in this section.** The
legs roughly doubled — 49.8–66.5 m became 99.7–127.4 m — because the 2026-08-10 re-lay
lengthened the rim-to-middle route to satisfy §12-D. **It is still a robot walking a
NavMesh path, not a match length.** §01 wants 12–20 minutes; 171 s is 2.9. Nothing here
measures what a human takes, because a human solving a maze they have never seen is not
what this test does.

### 1.5 Eight creatures, and a runtime that refuses to start if it disagrees with the map

Owner's gate, `biggate/t_playthrough.log:653`, printed by `MatchDirector.BeginMatch` on
every PlayMode match:

```
[Match] §06 창조물 8마리 — 8개 층에 선언된 시작점 8개. §12-B③ 층마다 1마리.
[Match] §01 총 4 자루 배선됨.
```

`MatchDirector.VerifyCreatureCount` (`MatchDirector.cs:2028`) compares the creatures it
stood up against `map.MonsterSpawns.Count` and **refuses to begin the match** when they
differ, naming both numbers. This is the direct answer to the failure this project keeps
repeating — an audit that describes a building the match is not played in. A green
`monster reach 196/196 over 8 of 8 storeys` can no longer coexist with a one-creature
game: every PlayMode test that calls `BeginMatch` would go red first.

### 1.6 Every match is no longer the same building — eight, gated one at a time

`unity/HorrorGame/Assets/Scenes/Generated/Resources/DescentRoster.txt` (measured here):

```
하강 descent roster 1
building	463793241	Map_Descent_0	gen-20260809-143312-seed463793241
building	1246502161	Map_Descent_1	gen-20260809-144138-seed1246502161
building	143331277	Map_Descent_2	gen-20260809-145019-seed143331277
building	5537973	Map_Descent_3	gen-20260809-145856-seed5537973
building	377221360	Map_Descent_4	gen-20260809-150731-seed377221360
building	1290368555	Map_Descent_5	gen-20260809-151610-seed1290368555
building	203597007	Map_Descent_6	gen-20260809-152455-seed203597007
building	20260802	Map_Descent_7	gen-20260809-153350-seed20260802
```

**Three buildings became eight**, and each slot is published only after passing every gate
three times — inside `Generate`, again after the dressing rebake, and again on the copied
slot scene with its own bake wired in ([B-018](BLOCKERS.md#b-018)). All eight scene files
carry a matching `SceneGen_` stamp on disk (measured here, greppable one per file).

The catch is in §2.4: the Release player on disk was built before this roster existed and
its Build Settings list **three** of the eight.

### 1.7 Two peers meet over real sockets, and a human's movement crosses the wire

**Carried, dated: 2026-08-08 16:51:56Z–16:55:47Z**, the last full PlayMode sweep
(`dist/test-results/playmode-results.xml`, *deleted by the 08-11 cleanup — the number is
carried, the artefact is not on disk*). Today's gate ran only three of the Net fixtures'
cases, so the fixture-level evidence below has not been re-taken since 08-08.

The Net fixtures hold **21 cases across seven files**; the twenty-seat tests still exist
under their original names and neither is `[Ignore]`d:

| Test | What it proves |
|---|---|
| `NetSocketTests.AHostAndAClientMeetOverARealSocketAndAMessageCrossesBothWays` | two Mirror peers, real transport, both directions |
| `NetTests.TheShippedNetworkManagerAcceptsARealRemoteClient` | the manager the game ships, not a test double |
| `NetHumanRunnerTests.AHumanWalkingTheShippedRigMovesTheHostsCopyOfTheirRunner` | input on the shipped rig → the host's copy moves |
| `NetRunnerTests.TheTwentiethRunnerIsAcceptedAndTheTwentyFirstIsRefused` | `RaceRunnersMax = 20` (`GameConstants.cs:618`) enforced at the socket |
| `NetHumanRunnerTests.TwentyRunnersGetTwentyDistinctPlacesOnB1sRim` | §11's field of 20 has 20 places to stand |

`NetHumanRunnerTests` **was** re-run in the owner's 2026-08-10 gate: `biggate/nethuman.xml`
reads `total 3 passed 3 failed 0`.

**What this is not.** Every socket is on `127.0.0.1`, Steam is offline (development App ID
480), and the peers are two objects in one editor process. Latency, packet loss, NAT,
Steam relay and twenty real machines are all untested. It is a great deal more than
"Mirror is installed", and it is a great deal less than a session.

### 1.8 The rules core — 357 tests, no engine (measured here)

```bash
~/.dotnet/dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

```
통과!  - 실패: 0, 통과: 357, 건너뜀: 0, 전체: 357, 기간: 1 m 33 s - HorrorGame.Core.Tests.dll (net9.0)
```

0 skipped, so the count is not inflated by disabled cases. Independently reproduced in the
owner's gate at `biggate/gate.log:4` — same 357, 1 m 34 s.

**512 → 357, and the drop is not a regression.** `e8c67ae` deleted the co-operative game
rather than gating it, taking its tests with it. A suite that shrinks when a game is
deleted is the suite working. What it does mean is that **no total on this page and no
total in TESTING.md may be compared across the pivot.**

`~/.dotnet/dotnet build core/HorrorGame.sln -c Release` → **오류 0개, 경고 4개** (measured
here). Run `dotnet clean -c Release` first if you want a trustworthy *warning* count — an
incremental build re-emits nothing. The error count is trustworthy either way.

### 1.9 The player is animated in the scene the game loads, and can finally see his own hands

Owner's gate, `biggate/8_solo.log`, `SoloPlaytest.BuildBatch`:

```
[SoloPlaytest] §05 animation wiring from Assets/Models/Player/Runner.fbx —
  Animator found (avatar: RunnerAvatar), 8 clip(s) imported.
[SoloPlaytest] Assets/Scenes/Map_FirstSketch_Solo.unity rebuilt from Assets/Scenes/Map_FirstSketch.unity.
[SoloPlaytest] §05 ANIMATION WIRING, read back from Assets/Scenes/Map_FirstSketch_Solo.unity
  — 1 PlayerAnimatorDriver block(s).

[Player] first-person view: 1 arm renderer(s) drawn (1 of them the RunnerArms viewmodel),
  1 hidden but still casting, 0 hand prop(s), 8 arm clip(s). Owner=True.
  arms:   RunnerArms bones=7 weighted=3 slots -> Unknown materials=3
  hidden: Runner    bones=17 weighted=5 slots -> Unknown materials=5
```

Two things closed here. The wiring is **read back out of the saved scene**, not asserted
about the object that was just built. And the line that used to read *"No renderer under
this rig reads as the owner's hands"* — [B-017](BLOCKERS.md#b-017), a first-person game in
which you could see no part of yourself — is gone: `RunnerArms.fbx` is a dedicated
viewmodel rather than a third-person body seen from inside. The rig also grew **13 → 17
bones** (elbows, neck, chest), which fixed both a scarecrow arm and a half-cycle leg phase
inversion, and the eight clips are retargeted Mixamo mocap rather than procedural curves.

`PlayerFirstPersonViewTests` 7/7 and `PlayerWorldArmsTests` 3/3 in the owner's gate
(`biggate/fpview.xml`, `biggate/worldarms.xml`).

### 1.10 A Release IL2CPP player exists, and the toolchain fault that blocked it is gone

`dist/last-build-summary.txt` (measured here):

```
HorrorGame build run — 2026-08-07T15:08:59Z
unity     : 6000.3.21f1 on macOS (OSXEditor)
exit code : 0

  macOS universal (Apple silicon + Intel)    Release     IL2CPP OK         2194.29 MB  51s
```

`dist/macos-universal/build-report.txt` adds the numbers that matter for a depot:
`ships to Steam: 438.16 MB (symbol folders excluded)`, `managed stripping: Low`,
`errors / warnings: 3 / 12 (0 this project's, 3 known third-party defect(s))` — all three
being Mirror's missing `.meta`, i.e. [B-004](BLOCKERS.md#b-004).

**The cause of the old failure is gone from this host — measured here, right now, with no
Unity involved:**

```bash
printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp
clang++ -std=c++17 /tmp/p.cpp -o /tmp/p        # exit 0, no diagnostics

ls -d /Library/Developer/CommandLineTools/usr/include/c++/v1          # No such file or directory
ls /Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1 | wc -l   # 185
```

The damaged directory that held 11 of 185 C++ headers **no longer exists**, so clang falls
through to the intact copy in the SDK. `'cmath' file not found` cannot reproduce here. The
previous edition said "the Release build fails to compile on this machine" and
[B-015](BLOCKERS.md#b-015) still says so; **both are false at HEAD**, and the two
explanations that entry could not separate — broken toolchain versus nobody exporting
`CPLUS_INCLUDE_PATH` — are now moot rather than resolved.

**This does not make the game shippable.** §2.4 and §2.5 are why, and §5 is what to do
about it.

### 1.11 The building is photographs now, and six of its eight zones are inside all four bands

Owner's gate, `biggate/9_shot.log`, the final table in `biggate/gate.log`. ART.md §1 gates
four measures — `black%` 10–40, `legible%` 30–75, `p50` 3–16, `blown%` < 0.5; `mean`,
`p90`, `p99` and `sat` are printed and not gated.

```
shot                                     mean    p50    p90    p99  black%  legible%  blown%    sat
allfour_Zone_B1_B1_Concrete.png          20.0    9.0   47.8  211.8    16.1      54.8    0.00    8.3
allfour_Zone_B2_B2_Wood.png               8.8    5.7   21.2   49.9    26.0      35.7    0.00    4.5
allfour_Zone_B3_B3_Metal.png             18.6    9.8   47.1  120.9    25.9      53.1    0.00    7.9
allfour_Zone_B4_B4_Gravel.png             9.4    5.8   22.6   57.6    25.5      37.4    0.00    9.6
allfour_Zone_B5_B5_Tile.png              14.2   11.1   30.2   58.4    11.8      63.6    0.00   12.4
allfour_Zone_B6_B6_Carpet.png            13.8   11.9   31.5   49.2    29.4      57.3    0.00    9.8
allfour_spawn0.png                        7.9    4.9   17.1   50.5    30.8      35.3    0.00    8.1
allfour_spawn1.png                        9.7    4.9   17.6   86.9    30.8      34.0    0.00    8.0
allfour_spawn2.png                       20.4    5.1   26.8  235.8    31.3      37.1    0.00    6.7
allfour_spawn3.png                        7.0    4.6   16.7   35.5    31.1      32.7    0.00    8.1
allfour_overhead.png                     11.2    0.1   61.5   66.2    81.8      18.1    0.00    7.7
```

**Ten of the eleven frames are inside all four gated bands; 24 of 24 gated zone measures
are in band.** The eleventh is the overhead, which ART.md excludes by construction
("explicitly *not* a game frame"). Thinnest margins: B5's `black%` at 11.8 against a floor
of 10, and spawn3's `legible%` at 32.7 against a floor of 30.

Underneath those numbers, since 2026-08-08: six zone materials carry CC0 photo-scan bases,
twelve `Dress_*` pieces are CC0 PolyHaven scans shipping their real PBR maps, the runner is
built on a CC0 human base mesh, the monster's hide was re-surfaced over three rounds, and
101 of the 168 audio clips carry a CC0 field recording as their base layer. Five
third-party families in all, Mixamo General Terms and CC0 1.0, itemised with per-file
provenance in [ASSETS.md](ASSETS.md).

**The headline is 6 of 8, not 8 of 8, and nothing in the tooling says so** — see §2.8.

---

## 2 · What does not work

### 2.1 The creature's grade has not moved — 10/10 TooEasy, 680/680 escapable

Owner's gate, `biggate/4_map.log` and `biggate/6_quality.log`:

```
§12 주자 테스트 — 하강 — 요양원 지하 8층: 10/10 (100%), TooEasy
  너무 쉽다 — 시야 차단 지점을 줄인다 (§12). Aggro is a threat the players can shrug
  off, so §06's chase never becomes the pressure the game is built on.

§12 실전 검증, every place rather than the ten §12 samples: 680/680 escapable (100%),
  against §12's 50%~70% band.
  B1 85/85 · B2 85/85 · B3 85/85 · B4 85/85 · B5 85/85 · B6 85/85 · B7 85/85 · B8 85/85
```

Every one of the ten sampled runners still releases with *"3 s of unbroken cover
(sprinted from the start)"*. **This is the fourth working pass in a row with the same
grade.**

**And yet the cause the project spent four passes on is fixed.** Same log:

```
[ok]   sight-break-spacing — 시야 차단 지점 간격 15~25m (질주 60m에 3~4번의 기회)
       160 시야 차단 지점 built from 456 bend(s), the deepest 12.5 m (cap 14.4 m),
       nearest-neighbour spacing 15 m~15 m inside §12's 15 m~25 m
```

**95 m of continuous cover became 12.5 m, against a 14.4 m cap, on all eight roster
seeds.** [B-007](BLOCKERS.md#b-007) is closed and its waiver is deleted. The grade did not
move anyway, which is the finding: [F-013](BALANCE-FINDINGS.md#f-013) predicted exactly
this. Its argument is arithmetic — with any §12-legal geometry a release fires after
16.8 m of route past one bend, and §12 *mandates* an S자 통로 of 10 m × 2 per zone —
so **no map that obeys §12's construction rules can fail §12's own 실전 검증.** The band
is a co-op-era instrument that measured whether one player in four could out-run the
creature, in a game where all twenty now can.

F-013's replacement instrument is 탈출 대가 — what a chase *costs* in §07's currency —
and on that one the map has got measurably worse in one respect:

```
§12 탈출 대가: 680 chases, min 3.4 · median 7.5 · p75 10.4 · max 37.1 s,
               against 3.4~20 s (한 문 ~ 한 층).
  0 below the floor
  20 over the flat ceiling, of which 3 over their own storey's
```

Median 7.5 s against 7.2 s before, still cheap and still at the low end of the band. But
the re-lay pushed **20 places past the 20 s ceiling and a maximum of 37.1 s**, where the
previous building's worst was 25.5 s and nothing was over. A chase dearer than the catch it
prevents is one a runner should stop running from. Twenty of 680 is small; it moved the
wrong way, and it is the first sign that lengthening the storeys has a price.

**What is honest to say to an owner:** nobody knows whether this creature is frightening,
because the only instrument that has ever judged it is a graph search, and the project's
own analysis says that instrument is asking a question the game no longer poses.

### 2.2 §12 still ships under a waiver, and the report still says FAIL

Owner's gate, `biggate/gate.log`:

```
[SceneGen] §12 is failing 1 rule(s) that KnownFailingRules waives by name, so the map was
written anyway. This build has KNOWN MAP DEFECTS in it.
[FAIL] centre-path — 외곽에서 중심까지 최단 90~140m (§12-D, 층마다)
```

**Five waived rules became one; thirteen of fourteen rules now pass; the one failure is
2.5 m wide.** From `biggate/4_map.log`:

```
22 storey entry point(s) walk 87.5 m~132.5 m to their own storey's middle
= 19.4~29.4 s at 달리기 4.5 m/s, against §12-D's 90 m~140 m. 1 of 22 OUTSIDE:
B1 하역장 87.5 m~132.5 m (1/8).
```

That is [B-019](BLOCKERS.md#b-019), down from **0 of 30 inside the band** to **21 of 22**.
The remaining one is a rim cell standing one step from a 외곽 관문, and the entry says the
cell could not be found: each of the three 관문 is already the longest §12 permits and the
storey has no spare radius. It is a measured miss, not a rounding.

**Two things this cost, both recorded in B-019 and neither visible in the report:**

- **Seed variation narrowed.** The four 외곽 관문 now stand at the same four bearings on
  every floor of every building, because band alignment admits exactly one jog pair per
  side. A player who learns where the rim's ways in are learns it once — which is against
  the point of having eight buildings at all (§1.6).
- **`straight-corridor` now measures 20.0 m against a 20 m cap with no slack**, so
  anything that adds a cell in line with a band leg trips a rule that currently passes.

And the report is still misread-able at a glance. The quality report's trailer line prints
the **raw bend** statistic:

```
시야 차단 지점 간격 (§12 수치 규칙 15 m~25 m): 456 corners, nearest-neighbour
2.5 m~15 m, mean 4.2 m, 40 inside the band.
```

`40 inside the band` out of 456 reads like a failure. The rule itself groups bends into
지점 first and passes 160/160. B-007's closing note is explicit that quoting the bend
figure instead of the 지점 figure "sends the next person to fix something that was never
broken" — and the tool still prints the bend figure last, where it is the line a reader
carries away.

### 2.3 Proximity voice is red on three tests, and one of them is a §06 mechanic

**Carried, dated: 2026-08-08 16:51:56Z**, `dist/test-results/playmode-results.xml` (*that
file was deleted by the 08-11 cleanup; the count is carried, not re-openable*) — the
last full PlayMode sweep, `total 124 passed 121 failed 3`. All three failures are in
`HorrorGame.Tests.PlayMode.Voice.VoiceSocketTests` (8 cases, 5 pass):

```
AVoiceCrossesARealSocketAndArrivesAttenuatedByTheRule
  The relay forwarded nothing. … Expected: greater than 0  But was: 0

AWallBetweenThemCostsTheRulesOcclusionAndNotTheEnginesRolloff
  Nothing was audible through the wall at all. … Expected: greater than 0  But was: 0

SpeakingIsReportedToTheCreatureEvenWithNobodyInRange
  MatchDirector.VoiceEffort reads Silent while the player is holding Shout.
  … a voice system that never sets it does not make you findable — which is half the
  reason voice is in this game.  Expected: Shout  But was: Silent
```

The third is not a plumbing failure. §13's 근접 음성 is priced by §06 — talking is supposed
to make you findable — and the mechanic that charges you for it never fires. The first two
say no voice frame reaches a listener at all, on a socket or through a wall.

**This is the largest red in the project and it is not in [BLOCKERS.md](BLOCKERS.md).**
Today's gate did not re-run the Voice fixture, so its state at HEAD — fourteen commits
later — is unknown.

### 2.4 The build on disk is not the game at HEAD, and it cannot host five of eight buildings

`dist/macos-universal/build-report.txt` (measured here):

```
git commit:           8bf2e75  (working tree dirty)
built at (UTC):       2026-08-07T15:08:08Z
shippable on Steam:   no — the App ID is still 480, Valve's Spacewar sample
steam app id:         480   (default (§13 Spacewar))

scenes (6, in load order)
  0: Assets/Scenes/Bootstrap.unity
  1: Assets/Scenes/Map_FirstSketch.unity
  2: Assets/Scenes/Map_FirstSketch_Solo.unity
  3: Assets/Scenes/Descent/Map_Descent_0.unity
  4: Assets/Scenes/Descent/Map_Descent_1.unity
  5: Assets/Scenes/Descent/Map_Descent_2.unity
```

Three facts, in order of how much they cost:

1. **`8bf2e75` is 25 commits behind HEAD, and the tree was dirty.** The player contains
   none of the CC0 texture pass, none of the twelve prop scans, neither the rebuilt runner
   nor the 17-bone rig nor the first-person viewmodel, none of the 101 re-based audio
   clips, and not the re-laid map. It is not a build of this game; it is a build of the
   game as it stood before the art existed. And "working tree dirty" means it is not a
   build of `8bf2e75` either — nothing on disk says what was in that tree.
2. **It ships three buildings; the roster names eight** (§1.6). `RaceLobby.VerifyRoster`
   refuses to host on a building Build Settings does not have, "because that failure ends
   with twenty people on a loading screen that never finishes"
   ([B-018](BLOCKERS.md#b-018)). So this player would refuse to host five of the eight
   seeds the roster can pick.
3. **App ID 480.** The report says so itself, in its own headline field.

**Nobody has launched this player.** There is no record on disk of `HorrorGame.app` being
run, by anyone, once.

### 2.5 The Windows player is a Development build, and no Release build of anything exists

**Rewritten 08-12.** The previous edition of this section quoted
`dist/windows-x64/MONO-FALLBACK-DO-NOT-SHIP.txt` and said the Windows player was Mono
*because IL2CPP was unavailable on this Mac*. That file is gone — the 08-10 rebuild
replaced the whole folder — and the claim it carried has since been contradicted twice.
[B-015](BLOCKERS.md#b-015) was downgraded on 08-11 when the stated cause was disproved:
`clang++` answers, and the corrupt-headers folder it blamed is not on disk.

What the current artefact says (`dist/windows-x64/build-report.txt`, built
2026-08-10T14:23:03Z at `471ffab`):

```
configuration:        Development
scripting backend:    Mono
backend reason:       development builds use Mono on purpose: it links in seconds and a
                      managed debugger can attach to it.
shippable on Steam:   no — this is a Development build (debug symbols and profiler are in it)
output folder:        2546.09 MB
```

So the accurate finding is narrower and harder to dismiss: **Mono here is a deliberate
choice for a testing build, and a Release/IL2CPP build of this game has never been
produced on any platform.** The macOS player (§2.4) is Development/Mono too. Nobody has
observed IL2CPP either succeeding or failing since the pivot, which means the Steam-facing
configuration is not "known broken" — it is *unattempted*, and unattempted things are
where schedules die.

A Mono player also ships plain managed assemblies that decompile in seconds, exposing the
host-only race logic §13 relies on. That is a reason not to ship this build, not a reason
it cannot be built.

**The next action is one command, not a purchase**: run a Release build for macOS and read
the exit code. If IL2CPP works locally, B-015 closes and only Windows remains open — and
Windows needs either a Windows machine or the runner in
`.github/workflows/unity.yml`, **which has never run because it needs a licence.**

### 2.6 🟢 CI's required job was red for six days; the cause was a program that was deleted

**Closed 08-11 — kept because the failure mode recurred three times and the record is the
only thing that stops a fourth.** Two commits were needed, and the gap between them is the
lesson. `471ffab` replaced the dead simulator step with a real §12 map-validator run
(4 `MapTests.Descent_*`, asserting ≥4 selected). It did *not* fix the job: the same file
still asserted a **512-test floor** against a suite that the pivot had cut to 357, and
`471ffab`'s own diff had edited a comment 118 lines below to read "357-test" while leaving
the gate at 512. `1ceb636` fixed the floor. A commit that describes the fix is not the fix.

The diagnosis below is preserved as history; the `dotnet run` step it quotes no longer
exists in `ci.yml`.

Measured 08-10. `.github/workflows/ci.yml:174` ran, inside the `core tests (dotnet)` job:

```bash
dotnet run --project core/HorrorGame.Sim -c Release -- validate
```

```
$ ~/.dotnet/dotnet run --project core/HorrorGame.Sim -c Release -- validate
실행할 프로젝트를 찾을 수 없습니다. 프로젝트가 core/HorrorGame.Sim에 존재하는지 확인하거나…
exit 1
```

`core/HorrorGame.Sim` has **zero tracked files at HEAD** (`git ls-files` → 0) and
`core/HorrorGame.sln` lists two projects, not three. The simulator was deleted at
`e8c67ae`, 2026-08-03 19:45. The step's own guard is
`if [ "${status}" -ne 6 ]; then … exit "${status}"; fi`, and 1 is not 6, so the job fails.

**That job is CI's required check.** It has therefore been red on `main` for **six days and
roughly thirty-seven commits** — and this is [B-013](BLOCKERS.md#b-013) happening for the
third time, in a repository whose CI file carries a forty-line comment about the previous
two. The same step's waiver list (`waived="sight-break-spacing"`) is also stale in the
opposite direction: that rule passes now (§2.1), so even a working simulator would emit the
"waiver is stale" warning.

**The good news buried in this:** the *other* red job is now green.
`bash tools/ci/verify_audio.sh` → **exit 0, `RESULT: PASS`** (measured here), with two
blocking defects both accepted by `tools/ci/audio_baseline.json`. `asset audit (§12 audio)`
was red on `main` in the previous edition; it is not now.

### 2.7 The bake's generation stamp is gone from the `.meta`, and the log still claims it is there

Measured here, on the artefact:

```bash
for f in unity/HorrorGame/Assets/Scenes/Generated/NavMesh/*.meta; do grep userData "$f"; done
#  userData:        ← all eleven, empty
```

At `a3e268e` that file read:

```
userData: gen-20260803-080103-seed20260802; the bake for Assets/Scenes/Map_FirstSketch.unity
```

Tracing it commit by commit (measured here) the stamp survives `8bf2e75`, `5547e1d` and
`629b305`, and is **empty from `d1f3a50` onward** — "the production asset pass". It is empty
at HEAD, for `NavMesh_Map_FirstSketch` and for all eight roster slots.

`MapSceneGenerator.Commit` still writes it (`MapSceneGenerator.cs:466`), still reads it back
(`ReadStamp`, `:562`), and still `LogError`s "could not stamp both artefacts … bake meta
MISSING" if the read-back fails. **That error did not fire in the owner's gate** — no gate
log contains the string. So the stamp was present when the generator checked and absent
afterwards, which points at something later in `MapPipeline` re-creating the asset and
resetting its importer. *That mechanism is inferred, not measured* — the author of this
page cannot run Unity to confirm it.

**Why it matters is not tidiness.** [B-009](BLOCKERS.md#b-009) cost three days of measuring
a NavMesh that was not the one just built, and it was closed by making the pair provable
from disk with two greps. One of those two greps now returns 0 on a correct build, so it
can no longer distinguish a correct build from a stale one — and the log line asserting
otherwise is worse than silence, because it invites someone to trust a check that cannot
fail.

### 2.8 B7 수몰층 and B8 굴착층 have never been photographed, and nothing says so

`unity/HorrorGame/Assets/Scripts/Editor/SceneShot.cs:166`:

```csharp
foreach (var zone in all.Where(t => t.name.StartsWith("Zone_", …)).Take(6))
```

A hard-coded `.Take(6)`, written at `4fb93cd` on 2026-07-31 for a three-storey building and
never touched since. Zones are created in storey order, so it deterministically keeps B1–B6
and **silently drops B7 and B8 — no warning, no log line, no skip message.** The same
`.Take(6)` is duplicated in `Rendering/FrameCost.cs:237`, so the frame-cost audit has the
identical blind spot; the spawn views are capped at `.Take(4)`, a four-player number, in a
twenty-player race.

So §1.11's "all four bands" and ART.md's "**all six zones**, all 18 measures in band" are
claims about **six eighths of the building**. The two floors nobody has ever seen are the
two with the newest materials — ART.md's own 2026-08-08 entry records giving 병동, 수몰층
and 굴착층 the `ZoneIdentity` rows they never had. Those rows exist in code
(`ZoneIdentity.cs:175,182`) and no camera has been pointed at two of them.

Neither ART.md nor BLOCKERS.md records this gap.

### 2.9 F-002 is half closed, and the half that is left is a design decision

Measured here:

```bash
tools/audio/.venv/bin/python tools/audio/verify_audio.py     # RESULT: FAIL
bash tools/ci/verify_audio.sh                                # RESULT: PASS, exit 0
```

```
§12 Listener alphabet: SUPPORTED — worst surface pair water vs gravel at 1.44x (need >= 1.4x)
at 25m through a wall it does NOT hold: worst pair metal vs gravel at 1.137x
HUD vs ears: 4 inverted pair(s) — gravel/concrete, gravel/earth, water/wood, tile/concrete
clips: 164   loops checked: 16   blocking defects: 2   warnings: 5
```

**Five inverted pairs became four and three blocking defects became two.** The CC0 footstep
pass found the cause of the worst one: gravel's synthesised band started at 1900 Hz, so
through a 600 Hz low-pass it had *literally zero* signal. A real 320–620 Hz substrate cut
gravel-vs-concrete 32.5 → 17.8 dB and gravel-vs-earth 28.5 → 13.8 dB, and gravel-vs-carpet
stopped inverting and was deleted from the baseline in the same commit, as that gate
requires.

The two verdicts disagree on purpose and both are correct: the verifier reports the finding,
the CI gate accepts defects that are baselined against a named finding. **The audio side has
done what the audio side can.** What is left is [F-002](BALANCE-FINDINGS.md#f-002)'s
original question — clarity as a function of occlusion, or constants re-derived at the
occlusion the role works through — and it is the designer's, not an engineer's. It also cost
something: occluded separation at 800 Hz fell 1.377× → 1.137×, a row that was already
failing and is classed as a warning rather than a defect.

### 2.10 What has never been measured at all

- **A human playing this game.** Not one match. Not one person walking a storey. There is
  no playtest record in `docs/`, no session note, no telemetry.
- **Twenty humans in one lobby.** Twenty sockets on `127.0.0.1` in one process is the whole
  of the evidence, and it is 2026-08-08 evidence.
- **How long a match takes.** §01 says 12–20 minutes. 171 s is a pathfinder. There is no
  measurement in between, and there is now no simulator to produce one.
- **Whether the race has a winner problem.** §02 gives one winner and nineteen ranked
  finishers; nobody has watched the nineteen.
- **Whether the shipped player runs.** It has been built and never launched.
- **What the store would show.** Every frame in `docs/store/` is of the five-storey co-op
  building, dated 2026-08-01; `05_the_shop.png` photographs a deleted economy and
  `06_five_storeys.png` is captioned for a building that now has eight. The store *copy*
  has been rewritten for the race; the store *media* has not, and `docs/store/checklist.md`
  correctly files it as a submission blocker.

---

## 3 · Gates that were re-run, and gates that were not

The previous edition's §3 listed five gates as unrunnable since the pivot. **Four of the
five have since run.** What follows is the current state, with the date attached to every
figure this document is entitled to quote.

| Gate | Figure | When | Standing |
|---|---|---|---|
| Core suite (`dotnet`) | **357/357**, 0 skipped | **2026-08-10, measured here** | current |
| Core solution build | **0 errors** | **2026-08-10, measured here** | current |
| Audio CI gate | **PASS, exit 0** | **2026-08-10, measured here** | current |
| Unity compile | **0 errors** | 2026-08-10, owner's gate | current |
| Import validator | 168 audio / 75 models, **0 failing** | 2026-08-10, owner's gate (`10_revalidate.log`) | current — see note |
| Map pipeline + §12 | exit 0, 13 of 14 rules, 1 waived | 2026-08-10, owner's gate | current |
| NavMesh + PlayerReach | PASS / PASS | 2026-08-10, owner's gate | current |
| PlayMode, six fixtures | **26/26** | 2026-08-10, owner's gate | a **subset**, not the suite |
| PlayMode, full sweep | **124 total, 121 passed, 3 failed** | **2026-08-08 16:51Z** | 14 commits stale; the 3 reds are §2.3 |
| EditMode | **95/95** | **2026-08-08 16:56Z** | 14 commits stale — but it ran, which closes the previous edition's worst unknown |
| Release build | IL2CPP OK, exit 0 | **2026-08-07 15:08Z** | 27 commits stale — §2.4 |
| Renders / `SceneShot` | 24/24 gated measures in band | 2026-08-10, owner's gate | **six zones of eight** — §2.8 |
| Balance simulator | — | — | **deleted at `e8c67ae`.** No figure from it may be quoted, ever again |

> **Note on the import validator.** The first pass in the owner's gate
> (`biggate/3_validate.log`, exit 1) failed one model — `RunnerArms.fbx`, graded outside the
> `CharacterGeneric` 1–3 m band. The re-run eighteen minutes later
> (`biggate/10_revalidate.log`) reports `0 failing` on the same file with nothing committed
> in between. That is the same shape as `a0c039c` — "the validator graded `Runner.fbx`
> against the raw policy the importer had already corrected" — i.e. a first pass judging an
> asset Unity had not finished importing. **The green is the later reading and it is the one
> quoted above, but a validator whose verdict depends on when you ask it is not yet a
> gate.**

**B-016 can be closed.** The previous edition said EditMode "may be red, may be testing a
game that no longer exists, and nobody knows which." It ran on 2026-08-08 at 95/95;
`UiTests.cs` is 17 cases rather than 59 (the shop's 39 were deleted on 2026-08-03); and
`SoloMatchLoopTests.cs` no longer exists at HEAD. Two of the three EditMode assemblies —
`Pivot` at 52 cases — exist to assert the co-op game *stays* deleted.

`MonsterChaseTests` re-ran in the owner's gate, 4/4 (`biggate/chase.xml`), and its numbers
moved with the map (`biggate/t_chase.log`):

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
[ChaseTest]   route            128.7 m of NavMesh path
[ChaseTest]   reached          26.64 s
[ChaseTest]   closing speed    4.79 m/s of route, against §06's 4.8 m/s of ground speed
[ChaseTest]   worst 1 s rise   0.0 m of route (0 is a monster that never backtracked)
```

The two control corridors still reproduce §06's central claim to 1 %: `monster speed
4.80 m/s against §06's 4.8`, `gap opened at 0.80 m/s against §06's 0.8`, single corner
`caught 12.54 s`, two 10 m legs `released 5.50 s at 12.0 m`.

---

## 4 · Reproducing every number on this page

[TESTING.md](TESTING.md) is the complete list — **but it is stale**: it opens at `a3e268e`
and still documents the deleted simulator's commands. Everything in §4 below was run for
this edition and is current.

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet test  core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj   # §1.8  357/357
dotnet build core/HorrorGame.sln -c Release                            # §1.8  0 errors
bash tools/ci/verify_audio.sh                                          # §2.6  exit 0, PASS
tools/audio/.venv/bin/python tools/audio/verify_audio.py               # §2.9  RESULT: FAIL
```

Without Unity, on the artefact:

```bash
# §1.1 — the scene half of the stamp holds, the bake half does not
grep -c SceneGen_gen-20260810-000424-seed20260802 unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity   # 1
grep userData unity/HorrorGame/Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta        # empty

# §2.6 — CI's required job, reproduced
dotnet run --project core/HorrorGame.Sim -c Release -- validate        # exit 1, no such project

# §1.10 — the IL2CPP blocker, gone
printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp && clang++ -std=c++17 /tmp/p.cpp -o /tmp/p
```

Everything Unity is in TESTING.md §3–§7, needs the editor closed, and produced the
`biggate/` logs this page quotes. **The author of this edition did not run Unity.** Every
Unity figure above is the owner's 2026-08-10 gate, cited by log file, or a dated carry.

---

## 5 · Could this be sold?

**No. Four things block it, and three of them are owner actions that no engineering can
shorten.**

### The clock has not been started, and it is the longest pole

[STEAM-RELEASE.md](STEAM-RELEASE.md) §I.1 costs this out. The $100 Steam Direct fee starts a
**hard 30-day waiting period**; a Coming Soon page must be public **≥ 14 days** before
release; identity and tax verification takes Valve **2–7 business days**; store review takes
**3–5**. None of it has begun. **Earliest conceivable release is 30 days after the day the
fee is paid** — so every day it is not paid is a day added to the end, and paying it costs
nothing but $100 and an hour of forms. **Owner only. Do this first, today, regardless of
everything below.**

### The App ID is one line, and it gates more than it looks

`unity/HorrorGame/Assets/Scripts/Steam/SteamAppConfig.cs:42` (measured here):

```csharp
public const uint DevAppId = 480u;       // :28
public const uint AppId    = DevAppId;   // :42
```

480 is Valve's Spacewar sample. The build report prints
`shippable on Steam: no — the App ID is still 480` on every release build, so the pipeline
already knows. Two things ride on it: `SteamAppConfig.IsDevelopmentAppId` decides whether
`steam_appid.txt` is written into the depot (Valve asks that it is not), and the real ID has
to land in `SteamAppConfig.cs` **and** `steam.config` together. It is one line of code —
**and the number does not exist yet, because it is minted by paying the fee.** Owner
action, then a one-line change.

### The networking supply chain is a stranger's repack, and it is now load-bearing

[B-004](BLOCKERS.md#b-004). `Packages/manifest.json` pulls `com.mirrornetworking.mirror`
96.6.4 from OpenUPM; the package's own `package.json` names an individual's fork while its
id and documentation URL read as official. Every byte of a twenty-player race goes through
it, and the Release build's three tolerated errors are all this package. Upstream Mirror has
no root `package.json`, so **every UPM-installable "Mirror" is somebody's repack** — the
official route is vendoring the `.unitypackage`. Engineering, its own change, full suite
after.

### Windows

§2.5. The product is a Windows player and one has never been built with IL2CPP. This needs
a Windows machine or a licensed CI runner. Engineering plus a licence.

### And underneath all four: nobody has played it

Not the owner, not a tester, not for one match. The build that exists has never been
launched. The map has been re-laid twice this week on the strength of a graph search whose
own governing finding says it is measuring the wrong question (§2.1). Every hour spent on
art or store copy before somebody plays a match is an hour spent tuning against an
instrument nobody has calibrated.

**Two people, two instances, twenty minutes.** It needs no App ID and no Windows machine.
It does now need a build: the 08-11 cleanup deleted `unity/HorrorGame/Builds/`, so the
path is the shipped player in `dist/macos-arm64/` (Development, 08-10, `471ffab`) or a
fresh `LocalTwoInstanceEntry` run, which rebuilds what it needs. It is cheaper than
every item above and it is the only one that can tell you whether the other four are worth
paying for.

### What is genuinely good, stated once

The gates, and they are better than they were a week ago. This project can say, from one
pipeline run and without a human in the loop, that its map is coherent for both bodies that
walk it, that the race it describes can be finished, that the creature count the runtime
stands up matches the one the map declares, that eight distinct buildings each passed every
gate three times before being published, and that six of its eight zones photograph inside
four exposure bands derived from first principles. It closed its oldest map blocker this
week by moving geometry rather than moving a threshold, and it wrote down what that cost.

**None of that is a game anybody has played, and the page should not let the two be
confused.** A large art pass does not make a game shippable. What it makes is a game worth
finding out about — and finding out costs one evening.

---

Companion documents: [BLOCKERS.md](BLOCKERS.md) — things that stop the game working ·
[TESTING.md](TESTING.md) — how to reproduce everything above, **stale as of `a3e268e`** ·
[BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) — numbers that contradict the design ·
[DESCENT-PIVOT.md](DESCENT-PIVOT.md) — what the pivot changed ·
[ASSETS.md](ASSETS.md) — what exists and where it came from ·
[CI.md](CI.md) — what runs automatically · [ARCHITECTURE.md](ARCHITECTURE.md) ·
[STEAM-RELEASE.md](STEAM-RELEASE.md) — the release administration ·
[game-design.md](game-design.md) — the authority for every rule ·
[ART.md](ART.md) — measurements current, **design rationale still describes the co-op
game** · [docs/store/](store) — **copy rewritten for the race, every image pre-pivot.**
