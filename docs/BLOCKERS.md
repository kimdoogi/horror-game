# Blockers

Things that stop the game working, as opposed to design questions. Balance
contradictions live in [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md). Art defects that do
not stop the game live in [ART.md](ART.md) §7.

**Last triaged: 2026-08-03, at commit `a3e268e`.** Every status below was re-checked
against the artefact at that commit; where an entry was closed by a measurement, the
measurement and the log it came from are quoted.

> **The game changed shape on 2026-08-02.** Four-player co-operative recovery became a
> twenty-player competitive descent ([DESCENT-PIVOT.md](DESCENT-PIVOT.md)). Several
> entries below were opened against the old game; each says so.

| # | State | One line |
|---|---|---|
| [B-001](#b-001) | 🟢 closed | The creature could not reach the player |
| [B-002](#b-002) | 🟡 dormant | EditMode red on a Mirror package-cache `.meta` |
| [B-003](#b-003) | 🟢 closed by the pivot | Two 개방 공간 dropped from every generation |
| [B-004](#b-004) | 🔴 **open — blocks release** | The networking library is a stranger's repack |
| [B-005](#b-005) | 🟢 closed | Regenerating the map unregistered the scene 시작 loads |
| [B-006](#b-006) | 🟢 closed | The core solution did not build |
| [B-007](#b-007) | 🔴 **open** | §12's sight-break-spacing: the map ships under a waiver |
| [B-008](#b-008) | 🟢 closed | A 계단 only the creature could use |
| [B-009](#b-009) | 🟢 closed | The NavMesh audited was not the one just built |
| [B-009b](#b-009b) | 🟢 closed | …and the chamber sealed the middle its own way |
| [B-010](#b-010) | 🟢 closed | The middle of a radial storey had no piece |
| [B-011](#b-011) | 🔴 **open** | The one red test is on the path a human takes |
| [B-012](#b-012) | 🔴 **open** | The simulator measures a building the game deleted |
| [B-013](#b-013) | 🟠 open (process) | CI was red for three commits that said green |
| [B-014](#b-014) | 🟠 open | §12's report says FAIL for three different reasons and names none of them |
| [B-015](#b-015) | 🔴 **open — blocks release** | No shippable build: IL2CPP will not compile here |
| [B-016](#b-016) | 🟠 open | EditMode has not been run since the pivot |
| [B-017](#b-017) | 🟠 open | The first-person view has no hands on the runner rig |

---

## B-017 · The first-person view has no hands on the runner rig

**Status:** 🟠 open · found 2026-08-03 · warning only, which is why a green suite kept it

Printed on **every** rig build, in `/tmp/r6_solo.log` and `/tmp/r6_net.log`:

```
[Player] No renderer under this rig reads as the owner's hands, so they will see nothing
of themselves. §05 asks for 손. Player.fbx must export Player_Body, Player_Arms and
Player_Torch as separate meshes — check MESH_SPLIT in the gen_player_model.py output.
  hidden: Runner bones=13 weighted=1 slots -> Unknown materials=1
```

`PlayerFirstPersonView` splits body / arms / hand-prop by material slot. `Runner.fbx`
exports one slot, so it finds nothing to keep visible and hides the whole rig. **In first
person you see no part of yourself.** This was defect 3.23 in the previous edition of
STATUS.md, fixed on 2026-08-01 against `Player.fbx`, and it returned when the race rig
moved to `Runner.fbx`.

### What it would take

Either `gen_player_model.py` exports `Runner.fbx` with the same `MESH_SPLIT` the player
model already has, or `PlayerFirstPersonView` learns a second way to identify arms
(bone subtree rather than material slot). The first is the smaller change and keeps one
rule; the second stops the rule depending on an export flag nobody sees.

**Make it fail rather than warn.** `PlayerFirstPersonView.Report` calls
`Debug.LogWarning`. A first-person game whose player has no body is not a warning, and
the PlayMode fixture that would have caught it (`PlayerFirstPersonViewTests`, 7 cases,
all green) builds its rig from a different prefab.

---

## B-016 · EditMode has not been run since the pivot

**Status:** 🟠 open · found 2026-08-03 · a whole test platform is unmeasured

Every test result in `/tmp` newer than the pivot is PlayMode. The newest EditMode XML on
this machine is `/tmp/editmode.xml`, `start-time 2026-08-01 11:51:06Z`, **101/101** —
against the four-player co-operative game. Every other EditMode result file is older
still.

What is in EditMode, counted from the sources at `a3e268e`:

| File | `[Test]`-family attributes | Covers |
|---|:--:|---|
| `Assets/Tests/EditMode/UI/UiTests.cs` | 59 | the §08 shop, **deleted by the pivot** |
| `Assets/Tests/EditMode/Audio/AudioTests.cs` | 26 | §12's material alphabet |
| `Assets/Scripts/Gameplay/Interaction/Editor/DropPlacementTests.cs` | 13 | dropping 전리품 |
| `Assets/Scripts/Editor/Playtest/MatchGuidanceTests.cs` | 2 | §14 guidance overlay |
| `Assets/Scripts/Gameplay/Match/Editor/SoloMatchLoopTests.cs` | 1 | the §01 co-op loop, **deleted by the pivot** |

So the platform is either red, or green while testing a game that no longer exists, and
**nobody knows which.** Both possibilities are bad and they need different fixes, which
is why this is filed rather than assumed.

### What it would take

One run — `-testPlatform EditMode`, no `-quit`, read the XML (TESTING.md §4). Then a
decision per fixture: retire, rewrite for the race, or keep. Until that run happens, no
document in this repository may quote a total that includes an EditMode number.

---

## B-015 · There is no shippable build — IL2CPP will not compile on this host

**Status:** 🔴 **open — blocks release** · owner action · reproduced 2026-08-03

```
exit code : 4
  macOS universal (Apple silicon + Intel)   Release   IL2CPP FAILED   125.27 MB   23s
.../libil2cpp/codegen/il2cpp-codegen.h:24:10: fatal error: 'cmath' file not found
```

`/tmp/r5_build_release.log`, and identically in `r4_build_release.log` and
`r3_build.log`. The Development/Mono player builds fine (exit 0, 387.92 MB,
`dist/last-build-summary.txt`), so what is missing is precisely the configuration that
ships.

### It is not Unity, and the proof takes two lines

Measured 2026-08-03 with no Unity involved:

```bash
printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp
clang++ -std=c++17 /tmp/p.cpp -o /tmp/p          # fatal error: 'cmath' file not found

ls /Library/Developer/CommandLineTools/usr/include/c++/v1           | wc -l   #  11
ls /Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1 | wc -l  # 185
```

This machine's Command Line Tools hold 11 of the 185 C++ headers, and clang prefers that
directory over the intact copy inside the SDK.

### The workaround works, and was not used

Also measured 2026-08-03 — the same compile with

```bash
export CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1
```

exits 0. None of the three failed Release runs in this session shows that variable being
set. **So two explanations fit the logs equally well** — the toolchain is broken beyond
what the workaround covers, or nobody exported the variable — and they have not been
separated. Separating them costs one build.

### What it would take, in order

1. Re-run the Release build with `CPLUS_INCLUDE_PATH` exported (TESTING.md §7). If it
   passes, this entry becomes a documentation problem, not a blocker.
2. If it still fails: `sudo rm -rf /Library/Developer/CommandLineTools && sudo xcode-select --install`.
   That is a system change and belongs to the owner, not to an agent.
3. **Neither fixes Windows.** IL2CPP calls the target platform's own compiler, so a
   Windows Release player cannot be produced on a Mac at all — a Windows Release build
   here silently falls back to Mono and the pipeline drops
   `MONO-FALLBACK-DO-NOT-SHIP.txt` beside it. Shipping to Steam needs a Windows machine
   or the Windows runner in `.github/workflows/unity.yml`, which has never run because
   it needs a licence. See [CI.md](CI.md) and [STEAM-RELEASE.md](STEAM-RELEASE.md).

`Horror ▸ Build ▸ Report Build Environment` reports `release backend IL2CPP` for macOS
either way — it asks whether the host OS matches the target, not whether the C++
toolchain is intact. Do not read it as a green light.

---

## B-014 · §12's report says FAIL for three different reasons and distinguishes none of them

**Status:** 🟠 open · found 2026-08-03 · the gate is honest; the report is not

`/tmp/r6_gen.log` ends its checklist with:

```
§12 map validation — 하강 — 요양원 지하 8층: FAIL
[FAIL] open-adjacent-to-maze · [FAIL] zone-diagonal · [FAIL] sight-break-spacing
```

and the map is written anyway, with a warning naming B-007. Three failures, and they are
three different kinds of thing:

| Rule | What it actually is |
|---|---|
| `sight-break-spacing` | a **genuine defect** — B-007, F-007, the reason the creature threatens nobody |
| `zone-diagonal` | **obsolete** — sized a zone for §03's clue chain, which the pivot deleted. A storey is now one 57.5 m ring system and its diagonal is 81.3 m by construction |
| `open-adjacent-to-maze` | **obsolete** — 개방 공간 existed so §04's 주자 could pull aggro from 15–25 m. Nobody pulls aggro for anybody in a race |

`MapSceneGenerator.KnownFailingRules` (`MapSceneGenerator.cs:640`) holds five entries:
those three plus `straight-corridor` (a real 22.5 m overshoot on the radial storeys,
currently passing at 17.5 m) and `concealment-near-exit` (obsolete — the 출입구 is now
the finish line, and a hiding place beside a finish line is somewhere to wait). The
source is candid about all of this; the comment says outright:

> *they belong in MapValidator as "not applicable to a descent map" rather than here
> beside genuine defects. The report should say obsolete, not FAIL.*

### Why it is a blocker rather than tidying

A gate whose top line is `FAIL` on the happy path teaches everyone to ignore its top
line. That is the same failure this project has recorded three times — [B-003](#b-003)
(two `LogError`s on every generation), [B-007](#b-007), and the CI incident in
[B-013](#b-013). And it currently obscures the one genuine failure inside the noise: a
reader of that block cannot tell which of the three to act on.

### What it would take

`MapValidator` grows a third verdict beside ok/FAIL — `n/a (descent)` — and the three
obsolete rules move to it with the reason each was retired. `KnownFailingRules` then
shrinks back to genuine deferrals (`sight-break-spacing`, `straight-corridor`), the
headline reads `14/14 + 3 n/a` instead of `FAIL`, and B-007 becomes visible again. Also
fix `NavMeshAudit.Report`, which prints `← the surface is in pieces` on every run because
eight islands is now correct (`NavMeshConnectivity.cs:556`).

**Note the ID collision while you are in there.** `MapSceneGenerator.cs:645` cites
"B-008" for the straight-corridor deferral. B-008 in this file is the stairwell defect
and has been closed since 2026-08-01. That deferral has no entry here; it should cite
B-014 or get its own.

---

## B-013 · CI was red for three commits whose messages said the suite was green

**Status:** 🟠 open (process, not code) · found 2026-08-03 · the code half is fixed

`ChamberDockProbe.cs` landed in `Assets/Scripts/Editor/SceneGen/` at **a89cf64**
(2026-08-03 04:55). Both engine-free projects glob that folder, and the file does
`using UnityEditor`, so it took `HorrorGame.Core.Tests` and `HorrorGame.Sim` down with
14 × `CS0246` each — **`dotnet test` could not reach a single one of its 512 tests.**

It was excluded only at **a3e268e** (08:40). Verified per commit:

```bash
for c in a89cf64 af2563d 43cf488 a3e268e; do
  git show "${c}:core/HorrorGame.Sim/HorrorGame.Sim.csproj" | grep -c ChamberDockProbe
done
# 0  0  0  3
```

So three commits over 3 h 45 min were pushed with `ci.yml`'s `core tests (dotnet)` job
failing at its first step, and **43cf488's subject line is "…and the suite is green."**
It was: the Unity PlayMode suite was green, and that is what had been run. The dotnet
half — which is the half that runs on every push, needs no licence, and is the required
check — was dark and nothing said so.

### Two separate holes, and both are still open

1. **Nobody looked.** There is no record in this repository of any CI run being read
   during this session. A red push and a cancelled push look different on GitHub and
   identical here.
2. **`main` used to cancel its own runs.** `concurrency.cancel-in-progress` was
   unconditional, so a second push to `main` cancelled the first commit's run — leaving
   a published commit CI never finished judging, and a **grey** tick rather than a red
   one, which nobody goes looking at. (`.github/workflows/ci.yml` is being changed for
   this as of 2026-08-03; that file is not owned by this document. See [CI.md](CI.md).)

### There is a second red job right now

`asset audit (§12 audio)` also fails at `a3e268e` — measured 2026-08-03:

```
RESULT: FAIL — 2 unbaselined blocking defect(s)
  [consistency] gravel vs earth    — 28.5 dB quieter at low-pass 600 Hz
  [consistency] gravel vs carpet   — 12.2 dB quieter at low-pass 600 Hz
```

Carpet, Water and Earth were added as floor materials for B6/B7/B8 with clarity
constants but without the occlusion work, so F-002's contradiction — the clarity number
the HUD shows disagreeing with the loudness the ears get through a wall — now covers
five pairs instead of one. The gate is behaving exactly as designed: an unbaselined
blocking defect fails the build. Either fix the constants or write the finding up and
baseline it in the same commit, per `tools/ci/audio_baseline.json`'s own rule.

### What it would take

Run both jobs' commands locally before pushing (TESTING.md §1, §2 and §6 — they take
about a minute together), and make `core tests (dotnet)` a required status check on
`main` so a red run blocks rather than scrolls past.

---

## B-012 · The balance simulator measures a building the game deleted — F-006, a second time

**Status:** 🔴 **open** · found 2026-08-03 · every simulator figure is void

Measured 2026-08-03:

```bash
~/.dotnet/dotnet run -c Release --project core/HorrorGame.Sim -- map
```

```
요양원 지하 8층 …  8 zones · 254 places · 285 passages · footprint 50 m × 95 m
built by FirstMapSketch.Build — the same call MapSceneGenerator makes before it lays
a single FBX, compiled into this binary rather than exported to it (F-006).
```

The game's map is **720 places, 814 passages, 57.5 m × 57.5 m** (`/tmp/r6_gen.log`). The
claim in that last line is false at `a3e268e`:

```
MapSceneGenerator.cs:146   map = DescentMap.Build(seed);          ← the race map
SimMap.cs:216              var sketch = FirstMapSketch.Build(seed);  ← the co-op map
```

`DescentMap.cs` is engine-free and **is** compiled into the simulator by
`HorrorGame.Sim.csproj`'s glob. It is simply never called. The simulator also still
resolves §03's clue chain and §08's economy, both deleted by the pivot; its own output
now says `§03 was deleted (game-design.md v1.0 · DESCENT-PIVOT §3)` while continuing to
simulate it.

### Why this is worse than it looks

[F-006](BALANCE-FINDINGS.md#f-006) is *this exact defect*, found on 2026-08-01: the
simulator built its own four-zone ring while the game shipped 164 places, so months of
economy tuning described a building nobody played. The fix was to compile the game's own
map sources into the simulator, and `HorrorGame.Sim.csproj` carries three long comments
explaining why an exported copy would drift and a compiled call cannot. **The call
drifted instead.** A build-time include guarantees the *sources* agree; it guarantees
nothing about which function you call.

### What it would take

1. `SimMap.Build` calls `DescentMap.Build(seed)`, and the header line prints the same
   place/passage counts the generator prints, so the two can be diffed by eye in one
   line.
2. A test that fails when they differ — the same shape as
   `MatchDirector.VerifyCreatureCount`, which is the pattern this project already found
   works: compare the two numbers at runtime and refuse rather than report.
3. Retire or rewrite the §03/§08 half of the simulator, or mark every command that
   depends on it as not applicable to the race.

Until (1) lands, **no figure from `horrorsim` may be quoted anywhere** — not match
length, not the threat curve, not the outcome mix.

> `core/HorrorGame.Sim/SimMap.cs` and `SimCommands.cs` have uncommitted working-tree
> changes from another workstream as this is written. The divergence above was verified
> against the committed `a3e268e` sources as well as against the run.

---

## B-011 · The one red test is on the path a human takes to host a game

**Status:** 🔴 **open** · found 2026-08-03 · the only red in 113 PlayMode cases

`/tmp/r7_all.xml`, 2026-08-03 08:37 — `total 113 passed 112 failed 1`:

```
HorrorGame.Tests.PlayMode.Net.LobbyEntryWiringTests
  .HostingFromTheMenuReachesTheMazeWithARunnerStillAlive   Failed

Unhandled log message: '[Error] [Race] §01 출발선이 완성되지 않았다 — 2석 중 1명에게
몸이 없다. 씬 로드가 주자를 지웠다는 뜻이고, 그 사람들은 아무에게도 보이지 않는다.
RaceLobby.KeepBodiesAcrossTheLoad 를 보라.'
  at RaceRunners.ReportStartLine () (RaceRunners.cs:298)
```

### The two readings, and which one is evidenced

**a3e268e's own claim** is that the test manufactures the state it trips over: it adds a
second seat with `NetworkServer.AddConnection(new NetworkConnectionToClient(id))`
(`LobbyEntryWiringTests.cs:300`) purely to clear §11's two-runner floor, that connection
has no socket and never spawned a body, and `ReportStartLine` correctly reports one
bodiless seat of two. Reading `RaceRunners.cs:250–305`, that is consistent: the method
walks `RaceParty.SeatConnectionIds`, counts connections whose `identity` is null, and
`LogError`s if any are.

**What has not been shown** is the production claim underneath it — that two *real*
seats keep their bodies across the descent's scene load. `KeepBodiesAcrossTheLoad` exists
because they did not, once. No test covers two real connections through that load;
`NetHumanRunnerTests` covers movement, not the scene transition.

So the red is probably the test's fault and **that is not the same as knowing.** This is
the one test that walks the shipped menu path, and it is the test that caught the defect
`a3e268e` fixed — assuming it innocent is how the previous defect on this path survived.

### What it would take

Give the second seat a real socket and a real body — a second `KcpClient`, as
`NetSocketTests` already stands one up — and assert both survive the load. If it then
passes, the red was the fake connection and this closes. If it fails, the fix is in
`RaceLobby.KeepBodiesAcrossTheLoad` and the shipped 호스트 path is broken for everyone
but the host. Either way one measurement settles it.

**Do not close this by silencing the log.** `LogAssert.Expect` on that message would make
the suite green and delete the only thing that can see a lobby full of invisible people.

---

## B-010 · The middle of a radial storey had no piece, so it sealed itself

**Status:** 🟢 **CLOSED** 2026-08-03 · verified by `/tmp/r6_gen.log`

`RadialStorey` drew the 3 × 3 middle as nine ordinary corridor cells. The kit tiles a
cell from its neighbour mask, so nine cells in a square became four L corners, four T
edges and a cross — nine 2.5 m passages meeting at their own walls. The audit found it as
a sealed island containing one marker:

```
[1] MonsterSpawn
```

That line is why §06's creature reached **0 of 3** targets in every run of this map, and
— because the middle of B8 is §02's finish — why nobody could win.

### The fix that landed

`Chamber_Open_3x3` in `tools/blender/gen_mapkit.py`: a 7.5 m square with corner piers and
a mid-edge opening on each side, registered in `MapKitCatalogue`, placed by `RadialStorey`
as a room. A plus-shaped middle was tried first and measured **worse** (91.2 % complete,
18 islands, against the block's 98.1 % / 11) because each arm became a blind cell and
took a `DeadEndCap`; the reasoning is left in `RadialStorey` because the next person will
have the same idea.

The wiring needed `OpenRoom()` rather than `Room()` — §12 counts 개방 공간 as graph
nodes, so the chamber's cells have to be **in** the graph while being excluded from
corridor tiling and covered by one piece. `Room()` produces geometry and no graph, which
left the four dock cells as dead ends and made §12 refuse, correctly, to hang the inner
gate's door on one.

### The closing measurement

`/tmp/r6_gen.log`, 2026-08-03 — against 0/3 on 3 storeys when this was opened:

```
monster reach 212/212 markers reachable from a MonsterSpawn on the SAME storey,
              over 8 of 8 storeys (§06)
runner reach  storeys 8/8 · starts 36/36 reach the finish · finish REACHED
```

### What this cost, and the lesson worth keeping

Three days, most of them spent on [B-009](#b-009) and [B-009b](#b-009b) blaming the
measuring instrument. **Both failures inside the corridor kit named themselves at
generation time** once a room piece was used — `VerifyRoomWalls` reported three separate
defects at exact coordinates while this was being wired — where the same two defects,
expressed as corridor cells, showed up only as one coordinate in an audit island list
three days later. Prefer the check that fails at authoring time.

---

## B-009b · ~~The audit is still stale~~ — the chamber sealed the middle its own way

**Status:** 🟢 **CLOSED** 2026-08-03, with B-010 · **it was never a stale audit**

Kept in full because the wrong conclusion here cost more than the bug.

`NavMesh.RemoveAllNavMeshData()` before the bake moved the numbers once — 93.5 % → 98.1 %,
17 islands → 11 — so B-009 was real. Then **four** regenerations with genuinely different
geometry produced a byte-identical audit:

```
complete 6863 (98.1 %)   islands 11   monster reach 0/3
```

The instrumentation that was meant to convict the audit exonerated it. The bake reported
the geometry it consumed — 9824, 8975, 9542 vertices across runs, from 2857 meshes and
3.9 M triangles — and `NavMesh.CalculateTriangulation()`, read *after* the bake, returned
the fresh global mesh that `SamplePosition` and `CalculatePath` query.

**A vertex count that moves and a connectivity result that does not is exactly what you
get when the geometry changes and the topology of isolation does not.** Nine corridor
tiles and one chamber have different vertex counts and cut off the same markers. 6863 and
11 islands was the true answer, five times.

The lesson, which is the reason this entry survives its own closure: *a number that will
not move is not evidence the instrument is broken.* Two days were spent on the one
component that had already been cleared.

---

## B-009 · The NavMesh being audited was not the one for the geometry just built

**Status:** 🟢 **CLOSED** 2026-08-03 · verified by the generation stamp

The original evidence: three regenerations, three genuinely different sets of geometry,
one identical audit (8717 complete · 93.5 % · 17 islands · monster 0/3), byte-identical to
the pair — including a run that placed **zero** dead-end caps.

### The real cause, and the fix

The global NavMesh was not cleared before baking, so the surface being sampled carried
data from the previous bake. `MapSceneGenerator` now calls
`NavMesh.RemoveAllNavMeshData()` first, and — the part that closes this rather than
merely fixing it — the whole generation is a **transaction with a stamp**:

```
[SceneGen] gen-20260803-080103-seed20260802: Assets/Scenes/Map_FirstSketch.unity and
Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset were BOTH written by this
run — 7127 vertices, 232,304 bytes; the same stamp is on
'SceneGen_gen-20260803-080103-seed20260802' in the scene and in
…/NavMesh_Map_FirstSketch.asset.meta, so anything else claiming to be this map is one
grep from being caught.
```

Verifiable without Unity, which is the point:

```bash
grep -c SceneGen_gen-20260803-080103-seed20260802 \
  unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity                                # 1
grep -c gen-20260803-080103-seed20260802 \
  unity/HorrorGame/Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta  # 1
```

A generation forced past a gate writes `-forced` into that stamp, so a build made that
way is identifiable from the artefact rather than from somebody's memory of the run.

**Why it mattered beyond this map:** `NavMeshAudit` is the gate that decides whether a
level ships. While this was open, every green audit in the project's history — including
the ones that closed [B-001](#b-001) — was worth less than it looked. The stamp is what
makes that class of doubt cheap to settle.

---

## B-008 · The player could not leave the entrance storey — a 계단 only the creature could use

**Status:** 🟢 **CLOSED** 2026-08-01 · superseded in scope by the pivot (there are no
계단 in the descent map — `one-way routes 14/14 투하구 usable · no 계단`)

A `CharacterController` with the player's dimensions could stand in 8 019 places, all of
them on B1, and reach 3 of 15 후보 지점. At the same moment `NavMeshAudit` reported
**1830/1830 pairs complete, 100 %, 1 island, monster reach 19/19**. Both numbers were
correct. They measure different bodies:

| | climbs | stands | is wide |
|---|---|---|---|
| NavMesh agent | `agentClimb` 0.75 m | 2.00 m | eroded region, no body |
| Player capsule | `stepOffset` 0.40 m | 1.75 m | 0.60 m capsule |

Two defects, and neither was a tall step: every 계단 was floored over 0.015 m below its
top landing (a zone floor slab poured across the cells a stairwell rises through, excluded
from the bake but keeping its `MeshCollider` — a lid to a capsule, a floor to nothing);
and no tread was deep enough for a 0.60 m capsule to stand on without being inside the
next riser (0.275 m usable against a 0.293 m forward reach).

**The important half** is that this is [B-001](#b-001) a second time and the project did
not notice, because the gate B-001 produced measures the antagonist and was read as
measuring the level. `PlayerReachAudit` now measures the player's own capsule with
`Physics` casts and never consults the NavMesh, because the premise is that the two
disagree. It is what §1.3 of [STATUS.md](STATUS.md) quotes, and it is the reason the
descent map's chutes are gated rather than assumed.

> **Do not fix a `PlayerReach` failure by raising `stepOffset`.** §12's escape geometry
> is derived from what a player *cannot* climb; a player who can step 0.65 m can climb
> crates and debris.

> **ID collision:** `MapSceneGenerator.cs:645` cites "B-008" for a `straight-corridor`
> deferral on the radial storeys. That is not this entry. See [B-014](#b-014).

---

## B-007 · §12's sight-break-spacing rejects the map that ships, and the map ships anyway

**Status:** 🔴 **open** · opened 2026-08-01 · unchanged by the pivot except in scale

`66ce930` implemented 시야 차단 지점 간격 as `MapValidator`'s 17th rule. The rule is right
and the map has never satisfied it. For a day the generator therefore refused to write the
level the game ships; now it writes it under a named waiver
(`MapSceneGenerator.KnownFailingRules`) and prints the failure every single time:

```
[SceneGen] §12 is failing a rule that is already recorded as a known defect, so the map
was written anyway. This is not permission to ignore it — see docs/BLOCKERS.md B-007.
```

The waiver was the right trade — freezing all map authoring behind one already-measured
defect cost more than it protected — and it is a debt, not a fix.

### The measurement, on the descent map (`/tmp/r6_gen.log`, 2026-08-03)

```
[FAIL] sight-break-spacing — 시야 차단 지점 간격 15~25m (질주 60m에 3~4번의 기회)
  48 시야 차단 지점 from 496 bend(s). One 시야 차단 지점 is 95 m deep …
  §12 allows 4.4 m — its own 14.4 m single-corner requirement less the 10 m head start
  its 어그로 시작 거리 table endorses.

시야 차단 지점 간격: 496 corners, nearest-neighbour 2.5 m~7.5 m, mean 3.5 m, 0 inside the band.
```

**95 m of continuous cover against 4.4 m allowed.** The five-storey building was 79
corners at a mean of 4.1 m; the eight-storey radial map is 496 at 3.5 m. The pivot made
this worse, because a concentric maze of 2.5 m cells is a corner every few metres by
construction.

### It is the same defect as the 주자 테스트 grade

10/10 TooEasy, and **720 of 720 places escapable** against §12's 50–70 % band. Every
sampled runner releases with *"3 s of unbroken cover"*. The rule and the grade are one
defect measured twice, and it is the reason the creature is decoration
([STATUS.md §2.1](STATUS.md), [F-007](BALANCE-FINDINGS.md#f-007)).

### What it would take

**Not** relaxing `SightBreakPointSpanMax`. The lever is the geometry: bands of the ring
that run straight for 15–25 m between turns, so a sprint has three or four discrete
chances to break line of sight rather than continuous cover. `RadialStorey` generates the
bands, so this is a change to one generator with a number that says the moment it
succeeds — re-run the generator and read `0 inside the band`.

Closing this closes B-007 and moves F-007's grade in one change. It is the single most
valuable piece of work available on this project.

---

## B-006 · The core solution did not build — the simulator never compiled a file it depends on

**Status:** 🟢 **CLOSED** 2026-08-01 · re-verified 2026-08-03:
`dotnet build core/HorrorGame.sln -c Release` → `오류 0개`

`MapQualityReport` gained a `RunnerCensus`; `HorrorGame.Sim.csproj` listed the engine-free
map sources **by name**, so the new file was never compiled and the solution failed on
2 × `CS0246`. Unity compiled clean and 560 tests passed throughout, because nothing in the
Unity project or the test project references the simulator.

The list was later **inverted** — the Unity-only files are now named and the engine-free
half is globbed — so that the same mistake breaks the build loudly on the first
`using UnityEngine` instead of measuring the wrong map quietly. That inversion is what
produced [B-013](#b-013)'s failure mode two days later, in the opposite direction, and the
project's judgement stands: a build error tells you, a stale measurement does not.

---

## B-005 · Regenerating the map unregistered the scene 시작 loads

**Status:** 🟢 **CLOSED** 2026-08-01 · verified by
`UiFlowTests.Menu_ComesUp_AndStartReachesTheMatchScene` (still green in `/tmp/r7_all.xml`)

`MapSceneGenerator.RegisterScenes()` rewrote Build Settings wholesale and named only the
bootstrap and the raw map, so regenerating deleted `Map_FirstSketch_Solo.unity` — the
assembled scene 시작 loads — from the build list. `SceneManager.LoadSceneAsync` returns
`null` rather than throwing for an unlisted scene, so the shell bounced silently back to
the menu: **the main menu's start button did nothing, with no error anywhere.**

Fixed by naming the scene once in `SceneGenPaths.MatchScene` and having both writers use
it. Worth remembering as the archetype: only a PlayMode test can see a button that does
nothing.

---

## B-004 · The networking library is a stranger's repack, not Mirror

**Status:** 🔴 **open — blocks release** · supply chain · **more urgent since the pivot**

`Packages/manifest.json` pulls `com.mirrornetworking.mirror` 96.6.4 from OpenUPM. Its own
`package.json`, read from the package cache:

```
name               com.mirrornetworking.mirror
author             Chaoyang <960208781@qq.com>
                   https://github.com/960208781/UnityMirror.git
documentationUrl   https://github.com/MirrorNetworking/Mirror/blob/master/README.md
```

The package **id claims Mirror Networking** and the documentation URL points at the
official repository, so everything visible from `manifest.json` reads as official. The
code being compiled comes from an individual's fork.

### Why this is worse than it was

When this was opened, Mirror was compiled in and not exercised — the playable build was
single-player, so it blocked the four-player milestone rather than the next playtest. As
of `a3e268e` it carries every byte of a twenty-player race: `PlayerRigNetView` replicates
position, camera rotation, torch, carry state and stamina, and the shipped
`HorrorGameNetworkManager` accepts real remote clients
([STATUS.md §1.6](STATUS.md)). **It is now load-bearing.**

### The official route, verified against upstream

`github.com/MirrorNetworking/Mirror` has **no `package.json`** at the root or under
`Assets/Mirror`, so it cannot be installed as a UPM git dependency — every
UPM-installable "Mirror" is somebody's repack. Official distribution is a
`.unitypackage` from GitHub releases (v96.11.1, five versions ahead of the repack) or the
Asset Store.

### What it would take

Vendor the official `.unitypackage` into `Assets/`, delete the OpenUPM dependency and its
scoped registry. Costs a larger repository and manual updates; buys a dependency whose
origin can be pointed at. Do it as its own change with the full suite after: it swaps the
assembly the whole of `Assets/Scripts/Net/` compiles against, and FizzySteamworks sits on
top of it. `com.mirror.steamworks.net` and `com.rlabrecque.steamworks.net` both come from
their own projects' repositories and are fine; Mirror is the only one whose publisher is
not the project.

---

## B-003 · Two 개방 공간 were silently dropped from every map generation

**Status:** 🟢 **CLOSED by the pivot** 2026-08-03 · verified: `grep -c HallOpen20x20
/tmp/r6_gen.log` → **0**

`MapSketch` placed two `HallOpen20x20` rooms under a corridor on the storey above and then
refused to build them, at `LogError`, on every generation — 6.3 m of room on a 3.75 m
storey, leaving places above with under 2 m of headroom.

The descent map is built by `DescentMap`/`RadialStorey` and places no `HallOpen20x20` at
all, so the errors are gone with the piece. **The complaint underneath it was not about
the room** and is still live: a generator that prints `LogError` on the happy path means
nobody can use "the log is clean" as a gate. That is now [B-014](#b-014)'s subject, one
level up — the *checklist* prints `FAIL` on the happy path.

---

## B-002 · The EditMode solo-match test fails on a broken Mirror package install

**Status:** 🟡 **dormant** · never a code regression · **unverifiable right now**

`SoloMatchLoopTests.Solo_match_runs_the_whole_round_trip` failed on an unhandled log
message — `Asset Packages/com.mirrornetworking.mirror/Mirror/Assets has no meta file, but
it's in an immutable folder` — not on an assertion. It stopped reproducing on 2026-08-01
when the package cache was rewritten; nothing was fixed.

**As of 2026-08-03 there is no way to say whether it reproduces**, because EditMode has
not been run since the pivot ([B-016](#b-016)) — and the test itself drives the §01
co-operative loop the pivot deleted, so it may not be a test worth keeping. Both questions
are answered by one EditMode run.

The underlying package fault is [B-004](#b-004) and is expected in every build; the build
pipeline names it in `BuildPipelineKnownDefects` and does not fail on it. If it returns in
a test, the fix is `LogAssert.Expect` on that one message in that one test — **not**
widening the harness's log tolerance in general.

---

## B-001 · The creature could not reach the player

**Status:** 🟢 **CLOSED** 2026-07-31 · re-verified in a different shape 2026-08-03

Closed originally by `MonsterChaseTests.MonsterClosesDistanceAndReachesAPlayerAcrossTheMap`
— 133.9 m of route across two storey boundaries at 4.83 m/s, `worst 1 s rise 0.0 m`,
against a monster that had been stalled 95 m away for 220 consecutive seconds. Both halves
of the fix landed: the kit's stairs became walkable geometry with every `NavMeshLink`
deleted, and `NavMeshWorldProbe.TryGetNextPathPoint` stopped deadlocking on a duplicated
path corner.

**The question changed with the game.** A creature cannot use a 투하구, so it can no
longer cross the building, and the test now asks whether it can reach a runner on its own
storey (`/tmp/r6_all.log`, 2026-08-03):

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
  route 71.0 m of NavMesh path · reached 14.54 s · closing speed 4.81 m/s against §06's 4.8
```

The two control corridors still reproduce §06's central claim —
「괴물이 달리기보다 0.3만 빠른 것이 핵심이다」 — to 1 %: `monster speed 4.80 m/s`,
`gap opened at 0.80 m/s`, single corner `caught 12.54 s`, two 10 m legs
`released 5.50 s at 12.0 m`.

**What it does not say.** That the creature *can* reach a runner is not that it *does*.
[B-007](#b-007) is the measurement that says it never has to be dealt with, and it is the
one that matters now.
