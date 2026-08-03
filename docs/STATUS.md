# Project status

Where 하강 actually stands at **commit `a3e268e`**, 2026-08-03.

> **If you are the owner deciding whether to put this on Steam, read §2 first and then
> §5.** §1 is real and it is not the part that decides anything.

Every number on this page is quoted with the run it came from. Where a run is named —
`/tmp/r6_gen.log`, `/tmp/r7_all.xml` — that file is the evidence and the command that
produced it is in [TESTING.md](TESTING.md). Where a number was measured while writing
this page it says **measured here**. Nothing is carried forward from an earlier edition
of this document: the game changed shape three days ago and almost everything the
previous edition said was about a different game.

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
patrols each storey; being caught is elimination, not death-and-respawn, and there is no
way to kill it or to kill each other. Design target for one match: **12–20 minutes**
(§01).

Until 2026-08-02 this was a four-player co-operative looting game. The pivot
([DESCENT-PIVOT.md](DESCENT-PIVOT.md), design v1.0) **deleted §04's five roles and §08's
economy**. Large parts of this repository — the shop, the clue chain, the ghost that
helps its team, the balance simulator, most of the EditMode tests, most of ART.md — were
written for that game and have not all been retired. Where that matters below, it says
so.

---

## The one-line answer

**The building is real and correct, the race can be finished, and two machines can play
it — but the creature is not a threat to anybody, there is no shippable build, and no
human has ever played a match of this game.**

Specifically: the map writes through its own gates with no override; 8 of 8 storeys are
fully connected for the creature and fully walkable by the player capsule; a runner
descends B1→B8 through seven chutes in 89 s; twenty seats are accepted and a
twenty-first is refused over real sockets. Against that: **§12's 주자 테스트 grades
10/10 TooEasy and 720 of 720 places escape the creature**, §12's checklist passes 14 of
17 with the map shipping under a waiver, the Release build fails to compile on this
machine, the balance simulator is measuring a building the game deleted, and the only
evidence about 20 humans in one lobby is a test that stands up twenty sockets.

---

## 1 · What works, with the evidence

### 1.1 The map writes through its own gates, with no override

`/tmp/r6_gen.log`, 2026-08-03 08:01–08:05:

```
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.GenerateFromCommandLine \
  -logFile /tmp/r6_gen.log
```

No `-forceWrite` in that command line — read it back out of the log's own
`COMMAND LINE ARGUMENTS` block if you doubt it, which you should, because for two days
before this the only way to get a scene on disk was to force one.

```
[SceneGen] gen-20260803-080103-seed20260802: Assets/Scenes/Map_FirstSketch.unity and
Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset were BOTH written by this
run — 7127 vertices, 232,304 bytes; the same stamp is on
'SceneGen_gen-20260803-080103-seed20260802' in the scene and in
Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta
```

**The stamp is the point, and it is checkable without Unity** (measured here):

```bash
grep -c SceneGen_gen-20260803-080103-seed20260802 unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity   # 1
grep -c gen-20260803-080103-seed20260802 unity/HorrorGame/Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta   # 1
```

Both hit. The scene committed at `a3e268e` and its bake came out of the run whose audit
§1.2 and §1.3 quote. A generation forced past a gate writes `-forced` into that stamp;
this one does not carry it. That check exists because [B-009](BLOCKERS.md#b-009) was
three days of measuring a surface that was not the one just built.

The building it wrote, from the same log:

```
8 zones · 720 places · 814 passages · 95 순환로 · 152 막힌 길 (21.1%)
footprint 57.5 m × 57.5 m
1160 kit pieces, 8 props, 824 markers
B1 하역장=Concrete · B2 기록보관소=Wood · B3 기계실=Metal · B4 저탄장=Gravel
B5 저수조=Tile · B6 병동=Carpet · B7 수몰층=Water · B8 굴착층=Earth
```

### 1.2 The creature can reach everything on its own storey — 8 of 8

`/tmp/r6_gen.log`, the audit `MapSceneGenerator` runs before it commits:

```
[NavMeshAudit] PASS
  markers          220
  pairs            3482
  complete         3482 (100.0 %, need 98 %)
  partial          0
  invalid          0
  islands          8  ← the surface is in pieces
  worst snap       0.25 m  (CandidateSite_B6 병동_15)
  monster reach    212/212 markers reachable from a MonsterSpawn on the SAME storey,
                   over 8 of 8 storeys (§06)
```

**Read the two qualifiers before quoting this.**

- **Eight islands is correct here and the log line is not.** A tower whose only vertical
  links are one-way falls *is* eight surfaces by construction; `NavMeshConnectivity`
  was rewritten to judge a storey rather than a building, and `NavMeshAudit.Report`
  still prints `← the surface is in pieces` whenever `islands > 1`. The sentence is
  now wrong on the happy path. Cosmetic, and exactly the kind of line somebody reads as
  a failure at 3 a.m.
- **The question got easier when the game did.** This used to ask whether the creature
  could cross the whole building to a player. It cannot climb a chute, so it can no
  longer be asked; what is measured now is per storey. That is the right question for
  this game, and it is a weaker one than the number it replaced.

### 1.3 A player can walk all of it, and the chutes are load-bearing — 36/36

Same log, the gate that runs after the NavMesh audit:

```
[PlayerReach] PASS
  body             height 1.75 m · radius 0.30 m · stepOffset 0.40 m · slopeLimit 50°
  runner reach     storeys 8/8 · starts 36/36 reach the finish · finish REACHED
  finish (§02)     (31.25, -26.25, 31.25)
  one-way routes   14/14 투하구 usable  ·  no 계단
  chute-blind      0/36 starts reach the finish with the one-way routes deleted
  standing places  224236 a runner can get into, of 224236 found in 8 pockets
  후보 지점         24/24     전리품 spawns 152/152     player spawns 36/36
  tallest climb    0.045 m   tightest headroom 2.53 m   worst reach gap 0.33 m
```

`chute-blind 0/36` is the line worth understanding. Delete the one-way routes and
**nothing** reaches the finish — so the audit cannot pass by accident through some
stairwell nobody meant to leave in. It also means the gate fails the instant one chute
breaks, which is the desired behaviour and the reason it is phrased that way.

This is the check [B-008](BLOCKERS.md#b-008) was written after: the NavMesh audit read
1830/1830 for a building the player could not leave the ground floor of. Two bodies, two
audits, both required.

### 1.4 A runner completes the race — eight legs, seven chutes, 89 s

`DescentPlaythroughTests.A_runner_can_descend_from_the_rim_of_B1_to_the_middle_of_B8`,
PlayMode, `/tmp/r6_all.log:2870` (and green again in `/tmp/r7_all.xml`):

```
[Test] §01 하강 완주 — B1 외곽에서 B8 중심까지, 투하구 7회.
씬 Map_FirstSketch_Solo · 시드 20260731 · 투하구 14개 (필요 14개, 층마다 남북 한 쌍)
층   외곽→중심                        투하구
B1   PathComplete 49.8 m              ↓ B2  외곽 25.0 m
B2   PathComplete 61.6 m              ↓ B3  외곽 25.0 m
B3   PathComplete 66.5 m              ↓ B4  외곽 25.0 m
B4   PathComplete 61.6 m              ↓ B5  외곽 25.0 m
B5   PathComplete 66.5 m              ↓ B6  외곽 25.0 m
B6   PathComplete 61.6 m              ↓ B7  외곽 25.0 m
B7   PathComplete 58.7 m              ↓ B8  외곽 25.0 m
B8   PathComplete 61.6 m              도착점

도착점 (31.25, -23.45, 31.25) (판정은 X/Z만), 마지막 위치 (31.25, -26.10, 31.25),
중심까지 0.00 m — 판정 반경은 2.5 m
§02 Descended 7회 / 필요 7회 · 좌석 0의 층 B8 · 승자 0 · 완주 1명 · 경과 89초
```

Every leg returns `PathComplete` rather than `PathPartial`, which is the distinction
that hid a broken chase for a day in this project's history. **89 s is a robot walking
a NavMesh path, not a match length.** §01 wants 12–20 minutes; nothing here measures
that, because a human solving a maze they have never seen is not what this test does.

### 1.5 Eight creatures, and a runtime that refuses to start if it disagrees with the map

`/tmp/r6_all.log`, printed by `MatchDirector.BeginMatch` on every PlayMode match:

```
[Match] §06 창조물 8마리 — 8개 층에 선언된 시작점 8개. §12-B③ 층마다 1마리.
```

`MatchDirector.VerifyCreatureCount` (`MatchDirector.cs:2595`) compares the creatures it
stood up against `map.MonsterSpawns.Count` and **refuses to begin the match** when they
differ, naming both numbers. This is the direct answer to the failure this project keeps
repeating — an audit that describes a building the match is not played in. A green
`monster reach 212/212 over 8 of 8 storeys` can no longer coexist with a one-creature
game: every PlayMode test that calls `BeginMatch` would go red first.

### 1.6 Two peers meet over real sockets, and a human's movement crosses the wire

`/tmp/r6_net.xml` — 22 cases in the Net fixtures, 21 passed (the one red is §2.7):

| Test | What it proves |
|---|---|
| `AHostAndAClientMeetOverARealSocketAndAMessageCrossesBothWays` | two Mirror peers, real transport, both directions |
| `TheShippedNetworkManagerAcceptsARealRemoteClient` | the manager the game ships, not a test double |
| `AHumanWalkingTheShippedRigMovesTheHostsCopyOfTheirRunner` | input on the shipped rig → the host's copy moves |
| `TheOwningClientsRunnerMovesOnTheHostBecauseACommandCrossedTheWire` | the only causal path is a private `[Command]` the test cannot call |
| `TwentyRunnersGetTwentyDistinctPlacesOnB1sRim` | §11's field of 20 has 20 places to stand |
| `TheTwentiethRunnerIsAcceptedAndTheTwentyFirstIsRefused` | `RaceRunnersMax = 20` enforced at the socket |
| `ARunnerSomebodyElseOwnsIsDrawnWithTheShippedModelAndTheOwnerIsNotDrawnTwice` | other people have bodies, you do not see your own twice |

From the same run's log (`/tmp/r6_net.log`):

```
[Net] Transport: local KCP on localhost (Steam backend present but offline:
      Steam did not initialise: Could not determine Steam client install directory.)
Server listening on port 7777
[Net] §01's starting line: 28 distinct points from 36 markers on B1's rim, for a field of up to 20.
[Net] Local runner bound to PlayerRigNetView — §05's 위치 · 카메라 회전 · 손전등 ·
      운반 상태 · 스태미나 are now leaving this machine.
Server full, client connectionId=35677738 with address=127.0.0.1 will be kicked
```

**What this is not.** Every socket is on `127.0.0.1`, Steam is offline (development App
ID 480), and the peers are two objects in one editor process. Latency, packet loss, NAT,
Steam relay and twenty real machines are all untested. It is a great deal more than
"Mirror is installed", and it is a great deal less than a session.

### 1.7 The rules core — 512 tests, no engine (measured here)

```bash
~/.dotnet/dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj -c Release
```

```
통과!  - 실패: 0, 통과: 512, 건너뜀: 0, 전체: 512, 기간: 419 ms - HorrorGame.Core.Tests.dll (net9.0)
```

0 skipped, so the count is not inflated by disabled cases. **476 → 512.** Both
TESTING.md and this document said 476 until now; that figure predates the pivot.

`~/.dotnet/dotnet build core/HorrorGame.sln -c Release` → **0 errors** (measured here).
Run `dotnet clean -c Release` first if you want a trustworthy *warning* count — an
incremental build re-emits nothing and prints `경고 0개` whatever the truth is. The error
count is trustworthy either way.

### 1.8 The player is animated in the scene the game loads

`/tmp/r6_solo.log`, `SoloPlaytest.BuildBatch`, which assembles
`Map_FirstSketch_Solo.unity` from the raw map:

```
[SoloPlaytest] §05 animation wiring from Assets/Models/Player/Runner.fbx —
  Animator found (avatar: RunnerAvatar), 9 clip(s) imported.
  Idle ← "Idle" · Walk ← "Walk" · Run ← "Run" · Crouch ← "Crouch" ·
  CrouchWalk ← "CrouchWalk" · Carry ← "Carry" · CarryIdle ← "CarryIdle" ·
  CarryHeavy ← "CarryHeavy" · Death ← "Death"   (all exact)
[SoloPlaytest] Assets/Scenes/Map_FirstSketch_Solo.unity rebuilt from Assets/Scenes/Map_FirstSketch.unity.
[SoloPlaytest] §05 ANIMATION WIRING, read back from Assets/Scenes/Map_FirstSketch_Solo.unity
  — 1 PlayerAnimatorDriver block(s).
```

The second line is the one that matters: the wiring is **read back out of the saved
scene**, not asserted about the object that was just built. A runner in the shipped
scene slides no longer.

### 1.9 A Development player builds and is 388 MB

`dist/last-build-summary.txt`, 2026-08-02T23:23:45Z (08:23 local):

```
exit code : 0
  macOS universal (Apple silicon + Intel)    Development Mono   OK   387.92 MB   17s
```

That is a Mono player. The Release/IL2CPP player does not build on this machine at all —
§2.4.

---

## 2 · What does not work

### 2.1 The creature threatens nobody — 10/10 TooEasy, 720/720 escapable

This is the largest problem in the project and it has not moved for four passes across
two different games.

`/tmp/r6_gen.log`:

```
§12 주자 테스트 — 하강 — 요양원 지하 8층: 10/10 (100%), TooEasy
  너무 쉽다 — 시야 차단 지점을 줄인다 (§12). Aggro is a threat the players can shrug
  off, so §06's chase never becomes the pressure the game is built on.

§12 실전 검증, every place rather than the ten §12 samples: 720/720 escapable (100%),
  against §12's 50%~70% band.
  B1 90/90 · B2 90/90 · B3 90/90 · B4 90/90 · B5 90/90 · B6 90/90 · B7 90/90 · B8 90/90
```

Every one of the ten sampled runners releases with *"3 s of unbroken cover (sprinted
from the start)"* after rounding 2–6 corners at 12.8–17.7 m. The census rules out an
unlucky sample: **720 of 720, every zone, no exceptions.** §12 wants 50–70 %.

The cause is measured and is a single number:

```
시야 차단 지점 간격 (§12 수치 규칙 15 m~25 m):
  496 corners, nearest-neighbour 2.5 m~7.5 m, mean 3.5 m, 0 inside the band.
```

A concentric maze of 2.5 m cells is a corner every few metres by construction, and cover
that dense means aggro can always be shed. **The creature is decoration.** Nothing in §1
contradicts this: the audit proves it *can* reach you, and the runner test proves it
never has to be dealt with. Tracked as [F-007](BALANCE-FINDINGS.md#f-007) and
[B-007](BLOCKERS.md#b-007), which are the same defect.

### 2.2 §12 passes 14 of 17, and the map ships under a waiver

`/tmp/r6_gen.log`:

```
§12 map validation — 하강 — 요양원 지하 8층: FAIL
[FAIL] open-adjacent-to-maze · [FAIL] zone-diagonal · [FAIL] sight-break-spacing
[SceneGen] §12 is failing a rule that is already recorded as a known defect, so the map
was written anyway. This is not permission to ignore it — see docs/BLOCKERS.md B-007.
```

The waiver is `MapSceneGenerator.KnownFailingRules` and it now holds **five** rules, not
one. Three of them (`open-adjacent-to-maze`, `concealment-near-exit`, `zone-diagonal`)
are annotated in the source as **obsolete** — written for the co-operative game the
pivot deleted — but the validator still runs them, still prints `[FAIL]`, and the report
still says `FAIL` at the top. So the headline verdict on this map is a mix of one
genuine defect, one deferred one, and three rules that no longer describe the game, and
nothing in the output distinguishes them. See [B-014](BLOCKERS.md#b-014).

### 2.3 The map's proportions are outside §12's range, structurally

Two of the three failures are not small misses.

```
[FAIL] zone-diagonal — 구역 대각선 30~40m
  B1 하역장 is 81.3 m across, over §12's 40 m … (and B2 … B8, all 81.3 m)
[FAIL] sight-break-spacing — 시야 차단 지점 간격 15~25m
  48 시야 차단 지점 from 496 bend(s). One 시야 차단 지점 is 95 m deep …
  §12 allows 4.4 m — its own 14.4 m single-corner requirement less the 10 m head start
```

**81.3 m against a 40 m cap** is the diagonal of a 57.5 m storey, and a storey *is* a
zone in a radial map, so the rule as written cannot be satisfied without either a
smaller building or a different definition of zone. **95 m of continuous cover against
4.4 m allowed** is a factor of 21. Neither is a tuning miss; both are the shape of the
map. The code's position is that zone-diagonal is obsolete and sight-break-spacing is
real; whichever way that lands, someone has to decide, and until they do the report's
`FAIL` is not actionable.

### 2.4 There is no shippable build — IL2CPP will not compile on this machine

`/tmp/r5_build_release.log` (and `r4`, `r3` — same failure every time):

```
exit code : 4
  macOS universal (Apple silicon + Intel)   Release   IL2CPP FAILED   125.27 MB   23s
.../libil2cpp/codegen/il2cpp-codegen.h:24:10: fatal error: 'cmath' file not found
```

**This is not Unity.** Measured here, right now, with no Unity involved:

```bash
printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp
clang++ -std=c++17 /tmp/p.cpp -o /tmp/p        # fatal error: 'cmath' file not found

ls /Library/Developer/CommandLineTools/usr/include/c++/v1          | wc -l   #  11
ls /Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1 | wc -l  # 185
```

This host's Command Line Tools are damaged: 11 headers where there should be 185, and
clang prefers that directory over the intact copy in the SDK. The workaround still works
(measured here — the same compile with
`CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1`
exits 0), and it was **not** used in any of the three Release builds in this session, so
"IL2CPP is broken" and "nobody exported the variable" are both consistent with the logs
and neither has been separated from the other by measurement.

Either way, as of `a3e268e`: **no Release player exists**, and the Windows player — which
is the product, since Steam's audience is Windows — cannot be IL2CPP-built on a Mac at
all. See [B-015](BLOCKERS.md#b-015) and [STEAM-RELEASE.md](STEAM-RELEASE.md).

### 2.5 The balance simulator is measuring a building the game deleted

Measured here:

```bash
~/.dotnet/dotnet run -c Release --project core/HorrorGame.Sim -- map
```

```
요양원 지하 8층 (B1 하역장 · B2 기록보관소 · … )  (seed 1204)
8 zones · 254 places · 285 passages · 32 순환로 · 57 막힌 길 · footprint 50 m × 95 m
built by FirstMapSketch.Build — the same call MapSceneGenerator makes before it lays
a single FBX
```

The game's map is **720 places, 814 passages, 57.5 m × 57.5 m** (§1.1). The simulator's
is 254 places in a 50 × 95 m rectangle. The line claiming they are the same call is
false: `MapSceneGenerator.Generate` calls `DescentMap.Build(seed)`
(`MapSceneGenerator.cs:146`); `SimMap.Build` calls `FirstMapSketch.Build(seed)`
(`SimMap.cs:216` at `a3e268e`). `DescentMap.cs` is compiled into the simulator by the
project's glob and never called.

**This is [F-006](BALANCE-FINDINGS.md#f-006) happening a second time**, in a project
whose build files carry three separate comments about how F-006 must never happen again.
Every economy, match-length and threat-curve figure the simulator can print describes the
retired co-operative building. Do not quote any of them. See
[B-012](BLOCKERS.md#b-012).

> Note: `core/HorrorGame.Sim/SimMap.cs` and `SimCommands.cs` have uncommitted changes in
> the working tree from another workstream as this is written. The divergence above was
> re-verified against the committed `a3e268e` sources, not just the run.

### 2.6 The audio alphabet has three blocking defects and CI is red on two of them

Measured here:

```bash
tools/audio/.venv/bin/python tools/audio/verify_audio.py            # exit 1
python3 tools/ci/check_audio_baseline.py --audit audit.json         # RESULT: FAIL
```

```
§12 Listener alphabet: SUPPORTED — worst surface pair water vs gravel at 1.50x (need >= 1.4x)
at 25m through a wall it does NOT hold: worst pair water vs tile at 1.377x
HUD vs ears: 5 inverted pair(s) — water/tile, gravel/concrete, gravel/earth, water/wood, gravel/carpet
clips: 205   loops checked: 21   blocking defects: 3   warnings: 6
RESULT: FAIL

RESULT: FAIL — 2 unbaselined blocking defect(s)   [gravel vs earth, gravel vs carpet]
```

The three new surfaces the eight-storey map needed (Carpet, Water, Earth) landed with
clarity constants but without the occlusion analysis, so F-002's shape — the HUD's
clarity table disagreeing with what the ears get through a wall — now applies to three
more pairs. Two are not in `tools/ci/audio_baseline.json`, so **the `asset audit (§12
audio)` job is red on `main` right now.**

### 2.7 One red test, and it is on the path a human takes

`/tmp/r7_all.xml`, PlayMode, 2026-08-03 08:37:

```
total 113   passed 112   failed 1   skipped 0
Failed: HorrorGame.Tests.PlayMode.Net.LobbyEntryWiringTests
        .HostingFromTheMenuReachesTheMazeWithARunnerStillAlive
  Unhandled log message: '[Error] [Race] §01 출발선이 완성되지 않았다 — 2석 중 1명에게
  몸이 없다. 씬 로드가 주자를 지웠다는 뜻이고, 그 사람들은 아무에게도 보이지 않는다.'
```

`a3e268e`'s own message argues this is the test's fault: it fakes a second seat with
`NetworkServer.AddConnection(new NetworkConnectionToClient(id))` to clear §11's
two-runner floor, that seat has no socket and no body, and `RaceRunners.ReportStartLine`
correctly reports a bodiless seat. That reading is plausible and it is **not verified** —
the production claim ("two real seats keep their bodies across the descent's scene load")
has never been demonstrated with two real seats. See [B-011](BLOCKERS.md#b-011). This is
the exact test that caught the previous defect on this path, so it is the wrong test to
assume innocent.

### 2.8 The first-person view has no hands on the runner rig

Repeated on every rig build in `/tmp/r6_solo.log` and `/tmp/r6_net.log`:

```
[Player] No renderer under this rig reads as the owner's hands, so they will see nothing
of themselves. §05 asks for 손. Player.fbx must export Player_Body, Player_Arms and
Player_Torch as separate meshes — check MESH_SPLIT in the gen_player_model.py output.
  hidden: Runner bones=13 weighted=1 slots -> Unknown materials=1
```

The rig was switched to `Runner.fbx`, which is not split by material slot the way
`Player.fbx` was, so `PlayerFirstPersonView` finds nothing to show. A first-person game
in which you can see no part of yourself was defect 3.23 here once already, fixed, and
is back. Warning only — nothing fails — which is why it survived a green suite.

### 2.9 What has never been measured at all

- **A human playing this game.** Not one match. Not one person walking a storey and
  saying whether solving the same maze eight times is interesting.
- **Twenty humans in one lobby.** Twenty sockets on `127.0.0.1` in one process is the
  whole of the evidence.
- **How long a match takes.** §01 says 12–20 minutes. 89 s is a pathfinder. There is no
  measurement in between.
- **Whether the race has a winner problem.** §02 gives one winner and nineteen ranked
  finishers; nobody has watched the nineteen.
- **What any of it looks like.** No render of the eight-storey map exists. Every frame in
  ART.md and `docs/store/` is of the five-storey co-op building, and half of them contain
  the 차량 shop the pivot deleted.

---

## 3 · Gates that were not re-run after the pivot

These are not failures. They are numbers this document is **not entitled to quote**,
because the last run of each predates the game changing shape.

| Gate | Last real figure | When | Why it is suspect now |
|---|---|---|---|
| EditMode suite | 101/101 (`/tmp/editmode.xml`) | 2026-08-01 11:51Z | Not run once since the pivot. `SoloMatchLoopTests` drives the deleted §01 co-op loop; `UiTests` (59 cases) covers the deleted §08 shop. It may be red, it may be testing a game that no longer exists, and nobody knows which. [B-016](BLOCKERS.md#b-016) |
| `AssetImportValidator` | 166 audio / 86 models | 2026-08-01 | There are now 209 WAVs and 91 FBX. The check that keeps a positional clip from importing as stereo has not seen 43 of the clips. |
| Renders / `SceneShot` | five-storey map | 2026-08-01 | The map is a different building. |
| `MonsterShot` visibility | 8/8 frames pass | 2026-08-01 | The creature is unchanged, the lighting and the map are not. |
| Balance simulator | 7.2 min median | 2026-08-01 | Measures the deleted building — §2.5. |

`MonsterChaseTests` **did** re-run (4/4 in `/tmp/r7_all.xml`) and its headline changed
with the game, which is worth reading rather than skipping:

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
[ChaseTest]   route            71.0 m of NavMesh path
[ChaseTest]   reached          14.54 s
[ChaseTest]   closing speed    4.81 m/s of route, against §06's 4.8 m/s of ground speed
```

and the two control corridors still reproduce §06's central claim to 1 %:
`monster speed 4.80 m/s against §06's 4.8`, `gap opened at 0.80 m/s against §06's 0.8`,
single corner `caught 12.54 s`, two 10 m legs `released 5.50 s at 12.0 m`.

---

## 4 · Reproducing every number on this page

[TESTING.md](TESTING.md) is the complete list, in the order worth running, with the exit
code each command should produce and the traps that make a green run meaningless. The
short version:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet test  core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj   # §1.7  512/512
dotnet build core/HorrorGame.sln -c Release                            # §1.7  0 errors
dotnet run -c Release --project core/HorrorGame.Sim -- map             # §2.5  wrong building
tools/audio/.venv/bin/python tools/audio/verify_audio.py               # §2.6  exit 1
```

Everything Unity is in TESTING.md §3–§7 and needs the editor closed.

---

## 5 · Could this be sold?

**No, and the reasons are not the ones a screenshot would show.**

There is no Release build, so there is nothing to upload (§2.4). The Windows player —
which is what Steam's audience runs — cannot be produced on this machine at any
configuration (§2.4). The networking layer that every byte of a 20-player race travels
through is an unaudited third-party repack ([B-004](BLOCKERS.md#b-004)).

Underneath the build problems is a design one, and it is the one to fix first: **the
creature does not threaten anybody.** 720 places out of 720 escape it (§2.1). A horror
race whose antagonist can always be shrugged off is a maze with a timer. Every hour spent
on art or store copy before that is answered buys nothing, because the fix is the shape
of the map and the map is what every screenshot is of.

And nobody has played it. Not the owner, not a tester, not for one match. The single
highest-value action available on this project is still two people, two instances,
twenty minutes — and it is cheaper than any of the above.

**What is genuinely good, stated once:** the gates. This project can now say, from one
generation run and without a human in the loop, that its map is coherent for both bodies
that walk it, that the race it describes can be finished, that the creature count the
runtime stands up matches the one the map declares, and that the artefact on disk is the
one that was measured. Very few projects at this stage can say any of that, and it is
what will make the map fix measurable when somebody makes it.

---

Companion documents: [BLOCKERS.md](BLOCKERS.md) — things that stop the game working ·
[TESTING.md](TESTING.md) — how to reproduce everything above ·
[BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) — numbers that contradict the design ·
[DESCENT-PIVOT.md](DESCENT-PIVOT.md) — what the pivot changed ·
[CI.md](CI.md) — what runs automatically · [ARCHITECTURE.md](ARCHITECTURE.md) ·
[game-design.md](game-design.md) — the authority for every rule ·
[ART.md](ART.md) and [docs/store/](store) — **both still describe the co-op game.**
