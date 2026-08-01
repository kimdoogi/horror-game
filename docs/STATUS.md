# Project status

Where this game actually stands, on 2026-08-02 01:20.

> New since the last edition, and the shortest path into it:
> **[OVERNIGHT.md](OVERNIGHT.md)** — what changed overnight, in one page.

Every command below was run on this machine in one sitting, in the order shown, and
the output quoted under it is the real output of that run. Nothing here is carried
forward from an earlier pass. If a command does not reproduce for you, that is a bug
in the project or in this document — say so rather than working around it.

**Environment:** Unity 6000.3.21f1, macOS 24.3.0 (arm64), .NET 9. Two lines in your
shell profile for everything .NET:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
```

> Only one Unity process may hold the project lock. Close the editor before any batch
> command here, and run them one at a time.

---

## The one-line answer

**660 of 660 tests green (core 476, EditMode 101, PlayMode 83), the standalone IL2CPP
player starts a match with 0 exceptions, and §09's 유령 is wired: a dead player keeps an
eye, sees their own pile, can rattle a nearby 물건 every 45 s, and the match keeps
running around them. `MonsterChaseTests` is still 4/4 and — the number that mattered
this round — still *the same 4/4*: `caught at 12.54 s`, 4.80 m/s of corridor, 0.80 m/s
of gap, 38.86 s to reach a player over 189.6 m. The second entity did not move §06's
measured chase by a single decimal.**

> 🔴 **The 그늘 (§10) is not in the game.** It has 25 core rules tests, 5 PlayMode tests,
> a model, audio, materials, a prefab and a shot rig that photographs it — and **no
> consumer**. `PresenceDirector` and `PresenceSubject` appear in no scene and no prefab
> (checked by script GUID, not by name), `MatchDirector` does not contain the string
> `Presence`, and outside its own two test files nothing in `Assets/` or `core/`
> references `PresenceField`. Nothing ticks it, so it charges nobody: **no player has
> ever lost their voice to it and none can until it is wired.** It is landed here as a
> tested, unwired subsystem, deliberately and not silently — see [§1.11](#111-the-그늘-is-tested-photographed-and-not-connected-to-anything).
> Do not read the 660 as evidence that this entity exists in a match.

> ✅ **The player can see their own hands.** They were being drawn correctly all along
> and measured **3.5 of 255** against a floor at 52.7 — present and invisible. Every
> render before this was read after a 3× brightness gain, which is exactly the crop that
> hides it. `PlayerHandFill` puts a 0.62 m point light on the eye and takes them to
> **31.5**, moving the far floor by 0.2 of 255 and nothing beyond a metre at all, so
> §03's lock is untouched. See [ART.md](ART.md) — and read the same section for the three
> things about the arms that are still wrong, which are pose and framing rather than mesh.

> 🟠 **§12's 실전 검증 has never been measuring this map.** `RunnerTest` calls a Runner
> covered whenever *any* sight-breaking bend is between them and the monster, and the
> building has 79 bends at a mean spacing of 4.1 m — so cover never lapses and the escape
> reduces to `12 s × 0.8 m/s ≥ 12 m − aggroStart`, an inequality with no geometry in it.
> Measured over all 164 places: 3 m gives 0 %, 4 m gives 97 %, and 5~15 m all give 100 %.
> **No aggro start distance reaches §12's 5~7/10 band.** The lever is the 개방 공간 share
> (32.3 % today, 65~80 % needed) and it is blocked by B-003's headroom rule — see
> [BALANCE-FINDINGS F-011](BALANCE-FINDINGS.md#f-011). B-007 and F-007 are one defect and
> this is it.

> ~~🔴 **Neither art pass is shippable yet, and the renders say so plainly.**~~ **The
> hands are answered; the 차량 is not.** `gen_player_ai.py` no longer harvests hands
> out of the flayed vessel sculpt — `build_hand()` authors them from anthropometry:
> five separate lofted digits with 3.0–4.3 mm of measured daylight between them, a
> knuckle arch, four knuckles, five nails, a thumb built already opposed, and a wrist
> that is a **capped solid** the generator fails the build over. The forearm is the
> third-person coverall with a role-coloured cuff. 1,328 triangles a hand; 5,254 for
> the model against a 7,200 cap. The 차량 is untouched and is still a correctly painted
> box — no windscreen, no grille, no wheel arches, no tread. See
> [ART.md §7.14](ART.md) for what changed and §7.13 for what has not.

> ~~🔴 **`MatchDirector.cs` references a `GhostSession` type that is not in the tree.**~~
> **Closed.** `Assets/Scripts/Gameplay/Ghost/` is back — `GhostSession`,
> `GhostRattleTarget` and `GhostViewGrade` — and Unity compiles 0 CS errors from a cold
> batch run. Every Unity check below was re-run against this tree in one sitting on
> 2026-08-01, so the dates on them are current again rather than pre-deletion.

> **ART.md's "all five zone views inside all four bands" is no longer true, and the
> cause is now known.** It was never *"the identical scene"*: `Map_FirstSketch.unity`
> was regenerated twice between the two measurements (`47bc2d8`, `9b75a08`) and the
> atmosphere art pass was never re-run afterwards, so **all 44 of `ContactDecals`' and
> `PracticalGlow`'s meshes are gone from the scene** — 44 `Mesh`/`MeshRenderer`/
> `MeshFilter` at `ba3e482` against 0 at HEAD. The signature agrees: Zone C's p99 is
> 69.2 → 69.2 and Zone D's p90 18.9 → 18.9, so the beam-lit region is *identical* and
> only the mid-tone tail collapsed. Fixed by one command —
> `AtmosphereSetup.Batch` — which was **not run here** because it re-saves every
> `Map_*` scene and another workflow has uncommitted changes in
> `Map_FirstSketch_Solo.unity`. See ART.md §7.14.

> 🔴 **§03's lock stood open and the flashlight was not the reason.** §7.13 measured
> torch-off 10.6 mean / 40.3 % legible against torch-on 11.1 / 40.6 and read it as a
> broken beam. Differencing the two frames shows a clean 44° cone lighting the corridor
> floor and the far locker — the torch works. What was wrong is that the **torch-off**
> frame already shows readable brick courses, floor cracks and pipework:
> `NightAtmosphere`'s ambient was raised ~2.6× in sRGB (eight times in linear) to
> replace a daytime skybox and went about twice too far, and nothing measured a
> torch-off frame afterwards because every band on ART.md §1 is measured with the beam
> on. `NightAtmosphere.AmbientGain` is the one number that fixes it. See ART.md §7.14.

> **The 차량 was parked inside a wall, and had been since it was first placed.** §12
> marks the 출입구 on a stairwell cell in a **2.2 m** service corridor and the van is
> **2.81 m** wide and 6.69 m long, so §08's 안전 지대 · 상점 · 보급소 stood 0.3 m inside
> the brickwork on both sides with 3.4 m of itself up the stairwell. Photographed from
> the corridor it was not a vehicle: an unlit black slab spanning the passage with two
> headlamps in it. It is now parked by measurement — the nearest point to the 출입구
> that is inside §01's 지상, on the NavMesh, and whose box sweep comes back empty — which
> on this map is the 20 × 20 m 하역 베이, 11.5 m away. See §4.6.

> **One residual, measured rather than assumed.** A 대형 전리품 released while facing a
> wall lands on the player's side of it and stays pickable, but overlaps the wall by
> **0.138 m** — `DropPlacement`'s clearance sweep is sized from the crosshair's own aim
> tolerance (0.15 m radius) and not from the piece, so anything wider than the sweep
> touches. It reads as leaning against the wall rather than as a bug; the frame is
> `Shots/drop_50_dropped_facing_a_wall_fill.png`. See §3's defect table.

What the corner-density pass delivered was the *measurement*, not the fix. §12's
시야 차단 지점 간격 is now `MapValidator`'s 17th rule, it is correct, and it names the
cause exactly — **79 bends, mean nearest-neighbour spacing 4.1 m, zero inside §12's
15–25 m band**. The geometry it judges was never changed.

**That has a new cost: the map can no longer be regenerated.**
`MapSceneGenerator.Generate` gates on the checklist, the checklist now fails, so
`HorrorGame ▸ Scene Gen ▸ Generate First Map` exits 1 and writes nothing. The committed
scene still runs and every runtime measurement below was taken against it — but map
authoring is frozen until the corner density is fixed. This is
**[B-007](BLOCKERS.md#b-007)**, opened tonight, and it is the same defect as
[F-007](BALANCE-FINDINGS.md#f-007) seen from the other side. One fix closes both.

The building is now five storeys instead of three: 164 places and 180 passages
against 74 and 85. The monster's route to a player grew from 133.9 m to **189.6 m**
and it still arrives, in 38.86 s at 4.85 m/s, so [B-001](BLOCKERS.md#b-001) stays
closed at the larger scale. [B-002](BLOCKERS.md#b-002) has stopped reproducing and
one real seam between two of the four parallel passes was found and fixed
([B-005](BLOCKERS.md#b-005) — regenerating the map silently unregistered the scene
the 시작 button loads, so the main menu did nothing).

**F-006 moved for the first time, once the simulator was pointed at the real building.**
It had been measuring its own four-zone ring — 38 places, monster spawning 52 m from the
door — while the game ships 164 places and 217.5 m, so growing the Unity level could not
show up in it at all. With `SimMap` now built from `FirstMapSketch` (§2.3): median match
**2.5 → 7.2 min**, inside §01's 25–35 window **0.6% → 15.8%**, reaching 심야
**1.2% → 33.6%**, and all five of §07's tiers reached by real matches. A bigger map is
not the whole answer — §01 wants 25–35 as the *normal* match — but it is the first thing
tried against this finding that has moved it.

**The map's §12 주자 테스트 is still outside its band — 10/10 TooEasy against 7/10
Balanced before the map grew, and two consecutive passes aimed at it have not moved
it.** That is the one target this round was set and missed; it is
[F-007](BALANCE-FINDINGS.md#f-007), and §1.6 has the measurements that say what to do
about it (164/164 places escape, so it is not an unlucky sample; 79 corners at a mean
spacing of 4.1 m against §12's 15–25 m, so corner density is the cause).

**Two defects found by re-running everything from scratch this pass**, both of the same
shape — a green suite hiding a broken thing:

- `dotnet build core/HorrorGame.sln` **failed** on 2 errors while Unity compiled clean
  and 560 tests passed, because the simulator is the only consumer of the file that was
  missing from its project ([B-006](BLOCKERS.md#b-006), §1.2). The balance simulator —
  the tool this document's headline number comes from — could not be built at all.
- §14's in-game guidance overlay was still telling playtesters **"한 판 중앙값 2.5분"**,
  the number measured against the wrong map, in a string literal that nothing greps
  (§4.4). It now reads 7.2.

One thing got better than the last edition of this document recorded: doubling the
building pushed three of five zone views under ART.md's legible-pixel floor, and the
same night's art pass pulled **all five back inside all four bands** — re-measured from
scratch here (§4.3). And one thing got worse: [ART.md §7.11](ART.md) now records what
this project's art register had never mentioned — **every object the player touches is
an untextured white primitive**, including the 차량 the team returns to 2.94 times a
match, and one of them is sitting in a frame already picked for the store page.

**Nobody has yet sat down with two instances and played it**, which is still the
highest-value thing anyone can do here and still cannot be automated.

---

## 1 · Verified green — command, and the output it produced

### 1.1 The rules core — 476 tests, no engine

```bash
dotnet test /Users/doogi/horror-game/core/HorrorGame.sln -c Release
```

```
통과!  - 실패:     0, 통과:   476, 건너뜀:     0, 전체:   476, 기간: 413 ms - HorrorGame.Core.Tests.dll (net9.0)
```

451 → 476: the twenty-five added tests are `PresenceTests`, which pin §10's 그늘 — the
saturation and dispersal rates, the toll, the residual, the monster-cleared radius, and
`TheDarkCoversEveryZone_WhereThePatrolTableCoversOne`, which is the test
[F-010](BALANCE-FINDINGS.md#f-010) is pinned by. Before them, 448 → 451 pinned §12's
시야 차단 지점 간격 rule (`66ce930`).

**These 25 test rules that no match runs.** See
[§1.11](#111-the-그늘-is-tested-photographed-and-not-connected-to-anything).

0 skipped, so the count is not inflated by disabled cases. Every tuned number and rule
lives here — §05's speed multipliers, §06's aggro and state machine, §07's threat
curve, §08's economy, §03's clues and confusion pairs, §12's map rules — and Unity
never opens. Run it before every commit.

### 1.2 Everything compiles

```bash
dotnet build /Users/doogi/horror-game/core/HorrorGame.sln -c Release
```

```
    경고 11개
    오류 0개

경과 시간: 00:00:04.27
```

11 warnings, all `CS8625` nullable-literal plus one `CS0649`, all in the test project.
Measured after `dotnet clean -c Release`, so this is a from-scratch build.

> **Run `dotnet clean` first or the warning count is a lie.** An incremental build of
> an already-built solution prints `경고 0개` — nothing recompiled, so nothing
> re-emitted a warning — and it takes 1.20 s instead of 4.27 s. That is how this line
> read `0` during tonight's pass before it was re-measured properly. The **error**
> count is trustworthy either way; the warning count is not.

> **This command failed when this pass started, and the previous edition of this
> document quoted the passing output anyway.** The map pass added
> `Editor/SceneGen/RunnerCensus.cs` and made `MapQualityReport` hold one;
> `HorrorGame.Sim.csproj` lists the engine-free map sources **by name** rather than
> globbing them, on purpose, so the new file was never compiled and the solution failed
> on 2 × `CS0246: 'RunnerCensus'`. Unity compiled clean throughout and all 560 tests
> passed, because nothing in the Unity project and nothing in the test project
> references the simulator — **the only command in this document that could see it is
> this one.** Fixed by adding the file to the `<Compile Include>` list; written up as
> [B-006](BLOCKERS.md#b-006). Run the solution build *before* the simulator, not after.

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame -logFile /tmp/u.log
grep -cE '^Assets/.*error CS' /tmp/u.log
```

Exit 0, and the grep prints `0`. `grep -c 'error CS' /tmp/u.log` also prints `0`.

### 1.3 The monster reaches the player — the headline

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath /Users/doogi/horror-game/unity/HorrorGame -runTests -testPlatform PlayMode \
  -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log
```

Exit 0. `<test-run result="Passed" total="4" passed="4" failed="0" …>`

| Test | Result |
|---|:--:|
| `MonsterClosesDistanceAndReachesAPlayerAcrossTheMap` | **Passed** |
| `AggroReleaseSendsTheMonsterToTheLastSeenPositionNotThePlayer` | **Passed** |
| `AnSCorridorOfTwoTenMetreLegsBreaksAChase` | **Passed** |
| `ASingleCornerDoesNotBreakAChase` | **Passed** |

```
[ChaseTest] §14 Q1 — can the monster reach a player at all?
[ChaseTest]   route            189.6 m of NavMesh path, monster spawn → (41.25, 0.15, 88.75)
[ChaseTest]   straight line    82.5 m
[ChaseTest]   chase entered    37.18 s
[ChaseTest]   reached          38.86 s
[ChaseTest]   closing speed    4.85 m/s of route, against §06's 4.8 m/s of ground speed
[ChaseTest]   worst 1 s rise   0.0 m of route (0 is a monster that never backtracked)
```

**This is the number the map growth was supposed to move, and it moved.** Against the
three-storey building: route 133.9 m → **189.6 m**, straight line 60.1 → **82.5 m**,
reached 27.52 s → **38.86 s**. The monster now walks four storeys instead of two and
still never backtracks — `worst 1 s rise` stays at 0.0 m, which is the measurement
that would expose a fragmented NavMesh.

Note what this does *not* say. A 39 % longer route bought 11.3 s of monster travel. What
it bought in match length is a separate measurement, taken over the objective loop rather
than over one chase — §2.3, where the same growth turned out to be worth 2.5 min → 7.2.

The control tests are the ones that keep this honest — `ASingleCornerDoesNotBreakAChase`
still ends `caught at 12.54 s`, and the two numbers worth repeating are **4.80 m/s of
corridor against §06's 4.8** and **0.80 m/s of gap opened while sprinting against
§06's 0.8**, which is the design's central speed claim —
「괴물이 달리기보다 0.3만 빠른 것이 핵심이다」 — measured to 1 % on real geometry.

### 1.4 The whole solo match loop

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SoloPlaytest.VerifyBatch -logFile /tmp/solo.log
```

Exit 0, no errors in the log.

```
[SoloPlaytest] §01 solo loop verification
  §03 layout varies per seed: objective moved no, clue set changed yes
  placed 4 clues, 1 objective, 36 pocketable loot, 1 oversize, 1 safe   (planned round trips 4)
  §08 대형 전리품 dropped from 1.79 m to 0.16 m, resting 0.020 m above the surface
  §08 picked up 은수저 · 잡동사니 — weight 1/10, speed ×1.00
  §04 safe: refused 주자, opened for 정비공, 문서 taken
  §03 objective refused while holding loot: §03 전리품 동시 소지 불가 — 들고 있는 전리품을 먼저 처리해야 한다.
  §03 read cancels when the light goes — progress reset to zero
  §03 read completed and the overlay drew: "녹"
  §01 descended — §07 clock hidden, 4.3s elapsed
  §03 partial reset — monster back at spawn (was 19.0 m away), clock untouched
  §08 sold on arrival — team wallet 50 credits
  §08 shop stayed shut on arrival and opened on [E] at the 차량 — cheapest item 15 credits
  §03 objective taken — no flashlight, no loot, speed ×1.00
  §02 FullVictory — escaped 1, lost 0, clock 6.9s
  §02 회수 released the objective — load 0
  §02 Survived — information kept without the objective
  §13 second BeginMatch — empty hands, load 0 (dropped 1 carried-over piece(s))
PASS — §01's loop ran end to end.
```

**Two of the four defects the owner found by playing are visible in that transcript.**
The 대형 전리품 line is new and it is the whole of defect 3.22 — held at 1.79 m, resting
at 0.16 m, 0.020 m clear of the surface under it. The shop line used to read
`§08 shop open at the vehicle`; it now says the panel stayed shut on arrival and opened
on `[E]` at the 차량, which is defect 3.25. Selling still happens on arrival, because
selling costs the player nothing and takes nothing away — it is putting a mouse-driven
panel over the camera on a position test that read as 갑자기 상점이 열림.

The bigger building shows up here too: **36 pocketable loot pieces against 10**, from
the same four planned round trips. That is the loot side of §1.6's place count, and it
is worth noticing next to §2.3 — more to pick up did not make a match longer.

This is the same code path as `SoloMatchLoopTests`, which used to be the project's one
red test ([B-002](BLOCKERS.md#b-002)) and now passes inside the harness as well as
outside it.

### 1.5 NavMesh connectivity

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch \
  -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log
```

Exit 0.

```
[NavMeshAudit] PASS
  markers          61
  pairs            1830
  complete         1830 (100.0 %, need 98 %)
  partial          0
  invalid          0
  islands          1
  worst snap       0.23 m  (CandidateSite_E 기계실_8)
  monster reach    19/19 player spawns and 후보 지점 reachable from MonsterSpawn (§06)
```

The five-storey map nearly tripled the sample — 36 markers and 630 pairs became
**61 and 1830** — and every one of them still completes, on one island, with the
worst marker snap *improving* from 0.44 m to 0.23 m. A bigger building did not
fragment the surface.

**Read this one with B-001 in mind.** This exact output was green while the monster
was frozen 95 m from the player: the audit asks `NavMesh.CalculatePath`, the monster
walks `NavMeshPath.corners`, and a `NavMeshLink` answers the first question and not
the second. It is a necessary gate and it is not sufficient. §1.3 is the sufficient
one. The links are gone now —
`grep -c NavMeshLink Assets/Scenes/Map_FirstSketch.unity` prints `0` — but never let
this audit stand in for a chase test again.

### 1.6 §12 map validation and the 주자 테스트 band

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu -logFile /tmp/quality.log
```

```
=== §12 map quality — seed 1204 ===
§12 map validation — 요양원 지하 5층 (B1 하역장 · B2 기록보관소 · B3 기계실 · B4 저탄장 · B5 저수조): FAIL
```

**16 of 17 rules `[ok]`, and the 17th fails.** The passing sixteen are
straight-corridor, open-adjacent-to-maze, s-corridor-per-zone, loops, dead-ends,
floor-materials, observation-posts, lockable-doors, candidate-sites,
zone-entry-points, concealment-near-exit, zone-count, zone-diagonal, map-extent,
connectivity, zone-membership. The seventeenth, added by `66ce930`, is the one that
measures what §1.6 has been calling the cause for two passes:

```
[FAIL] sight-break-spacing — 시야 차단 지점 간격 15~25m (질주 60m에 3~4번의 기회)
           3 시야 차단 지점 from 79 bend(s). One 시야 차단 지점 is 122.5 m deep …
           The nearest other 시야 차단 지점 to #43 A 기록보관소(19,23@L1) is 62.5 m away,
           over §12's 25 m.
```

> **A failing checklist is a build gate, not just a report.**
> `MapSceneGenerator.Generate` refuses to write a scene the checklist rejects, so
> `HorrorGame ▸ Scene Gen ▸ Generate First Map` now exits 1 and produces nothing —
> **[B-007](BLOCKERS.md#b-007)**. The committed `Map_FirstSketch.unity` predates the
> rule and still runs; §1.3, §1.5 and §1.8 were all measured on it tonight. Map
> authoring is what is blocked, not the game.

Selected passing measurements, verbatim:

```
Longest unbroken sight line is 20 m, inside §12's 20 m limit.
Independent 순환로: 17 map-wide (need 3+).
막힌 길: 41 of 164 places = 25% (§12 band 20%~25%).
Distinct and non-overlapping: D 하역장=Concrete, A 기록보관소=Wood, E 기계실=Metal,
  C 저탄장=Gravel, B 저수조=Tile.
5 zones, inside §12's 4~6.   Footprint 50 m × 92.5 m, inside §12's 100 m square.
One walkable piece, 164 places, 180 passages.
```

The building roughly doubled: 74 places → **164**, 85 passages → **180**, loops
12 → **17**, dead-ends 21.6% → **25%** (at the top of the band, not through it).

And the grade — **this is the regression**:

```
§12 주자 테스트 — 요양원 지하 5층: 10/10 (100%), TooEasy
  너무 쉽다 — 시야 차단 지점을 줄인다 (§12). Aggro is a threat the players can shrug
  off, so §06's chase never becomes the pressure the game is built on.
```

**10/10, outside §12's 5–7/10 band, against 7/10 Balanced before the map grew.**
Every one of the ten sampled runners now escapes, each releasing with *"3 s of
unbroken cover"* after rounding **2–4 sight-breaking corners** at 12.8–18.5 m. The
three routes that used to end `CAUGHT` — the ones descending into zones C and E,
reporting *"No sight-breaking corner was ever rounded"* — are gone, and they were the
only thing holding the grade inside the band.

More passages means more corners. Until `66ce930` **nothing in the checklist
constrained corner *density***, so the map passed every rule and failed the one grade
§12 gives it. That hole is now closed — the checklist and the grade finally disagree
with the map in the same direction — but closing it changed only the diagnosis, not
the geometry. Written up as [F-007](BALANCE-FINDINGS.md#f-007) with the named node
chains to straighten; it is the clearest open piece of map work, and now also the
thing blocking map regeneration.

**Two lines the report prints underneath the grade settle what to do about it**, and
both are new this pass — `RunnerCensus` did not exist before it:

```
§12 실전 검증, every place rather than the ten §12 samples: 164/164 escapable (100%),
  against §12's 50%~70% band.
  D 하역장 35/35 · A 기록보관소 34/34 · E 기계실 43/43 · C 저탄장 28/28 · B 저수조 24/24

시야 차단 지점 간격 (§12 수치 규칙 15 m~25 m):
  79 corners, nearest-neighbour 2.5 m~10 m, mean 4.1 m, 0 inside the band.
```

The first rules out an unlucky sample. §12's bands are quoted against ten tries, so
10/10 on its own could not distinguish "borderline map, bad seed" from "every place
escapes" — near the band a ten-point sample is close to a coin flip. It is the second:
**164 of 164, every zone, no exceptions.** There is nothing to re-roll.

The second is the cause as a number. §12's 수치 규칙 wants sight-breaking corners
15–25 m apart; **not one of the 79 is**, and the mean is 4.1 m. That is what buys every
sampled runner "3 s of unbroken cover".

> **This is the number the last two passes were sent to fix, and it has not moved.**
> 10/10 before, 10/10 after, 10/10 again tonight. What those passes produced is the
> census, the spacing measurement, and now a validator rule that fails on it — so the
> next attempt can aim at a quantity, and will know the moment it succeeds. What none
> of them produced is a change to `FirstMapSketch`'s geometry, which is the only thing
> that can move the grade.

Reproducible from the headless simulator too, which shares no measurement code path
with the editor menu and agrees exactly:

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- map
```

### 1.7 Asset import settings

```bash
… -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log
```

```
[AssetImport] Audio import settings: 166 inspected, 0 excluded by marker, 0 failing, 0 warnings.
[AssetImport] Model import settings: 86 inspected, 0 excluded by marker, 0 failing, 0 warnings.
```

Not housekeeping. A positional clip imported as stereo is not spatialised, and §04's
Listener localises the monster **by ear alone** — one wrong checkbox silently deletes
a role and nothing else in the project would notice.

### 1.8 The stairs, which are the reason §1.3 passes

From `/tmp/mapgen.log` at 12:09, the run that wrote the current
`Assets/Scenes/Map_FirstSketch.unity`:

```
[SceneGen] 7 계단 verified as single walkable surfaces, no NavMeshLink anywhere in the
map. §06's monster steps along NavMeshPath.corners, so every storey boundary it
crosses has to be geometry it can stand on.
Placed 932 pieces of 37 kinds across 298 walkable cells (1863 m²) — 50.0 per 100 m².
152 placements were rejected for narrowing §08's carry channel.
Corridor sight lines: 146 sampled, mean 8.2 m, longest 21.2 m; 20 fall inside §12's
15–25 m 시야 차단 지점 spacing.
```

Two things changed there and both matter. The stairs are geometry rather than links,
so **the player can climb them too** — a `NavMeshLink` is a gap with nothing to step
onto and a human cannot use one at all. And the built-scene sight-line sampler now
reports a longest run of **21.2 m** where it used to report **100.0 m**; §3.4 covers
what is left of that defect.

### 1.9 The full Unity suite — 184 of 184

Run the two platforms separately and read the XML rather than the exit code.
**No `-quit`**: the runner is async and exits from its own callback, and `-quit`
kills it before any results are written.

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath /Users/doogi/horror-game/unity/HorrorGame -runTests -testPlatform EditMode \
  -testResults /tmp/editmode.xml -logFile /tmp/em.log
# then again with -testPlatform PlayMode
python3 -c "import xml.etree.ElementTree as ET,sys; r=ET.parse(sys.argv[1]).getroot(); print(r.get('total'),r.get('passed'),r.get('failed'),r.get('result'))" /tmp/editmode.xml
```

Both exit 0.

```
EditMode   total 101 passed 101 failed 0 skipped 0 result Passed
PlayMode   total 83 passed 83 failed 0 skipped 0 result Passed
```

**184 of 184, against 173 of 173 last pass.** EditMode 71 → 100 → **101** (unchanged
this pass) and PlayMode 55 → 64 → 66 → 72 → **83**. The eleven added this pass are
`GhostSessionTests` (6) and `PresenceSessionTests` (5); with core's 476 the project
total is **660 of 660**. Note the asymmetry those eleven hide: the six ghost cases
cover a subsystem a player meets every time they die, and the five 그늘 cases cover one
no player can reach at all — see
[§1.11](#111-the-그늘-is-tested-photographed-and-not-connected-to-anything).
The seven before them cover the crouch/hop verbs and the
rebuilt player rig. The two PlayMode cases before them are `SurfaceApronTests`, and the first of
them is the one to know about: it walks every stripe of §01's painted boundary and
asserts each is within 0.35 m of `MatchMap.SurfaceRadius` measured from
`MatchMap.Entrance` — the same two values `MatchMap.IsOnSurface` uses. Worst radial
error on this map is **0.000 m**. A line on the floor that disagreed with the rule it
draws would be worse than the invisible boundary it replaced. The second drives the
crossing beat both ways and asserts it settles back to rest.
The thirty-eight new cases are the four-defect pass: `DropPlacementTests` (EditMode,
ten cases over the five §12 floor materials, stairs, slopes, another interactable's
trigger box and the dropper's own body), the rewritten `UiTests` shop coverage,
`InteractionDropTests` and `PlayerFirstPersonViewTests` (PlayMode).

**`InteractionDropTests.Dropping_a_piece_with_the_key_puts_it_on_the_floor_and_leaves_it_pickable`
is the one to know about.** It queues a real `Keyboard` state event for
`PlayerInteractor.InteractKey`, waits for `Keyboard.current` to actually report the key
down — and fails loudly if the Input System never delivered it, so the run cannot pass
by proving nothing — then asserts the piece fell, is resting on a surface, is not
inside geometry, and that the crosshair finds it again after walking back. Every earlier
interaction test called `OnPressed` directly, which is how a broken pickup key survived
575 green tests for a day.

**With core's 451, the project total is 624 of 624 green.**

Two earlier seams this suite caught, both still worth knowing about:

- [B-002](BLOCKERS.md#b-002) stopped reproducing on its own.
  `SoloMatchLoopTests.Solo_match_runs_the_whole_round_trip` passes. Nothing was fixed;
  the package cache was rewritten during the art passes. It is dormant, not closed.
- [B-005](BLOCKERS.md#b-005) was real, and this suite is the only thing that saw it.
  `UiFlowTests.Menu_ComesUp_AndStartReachesTheMatchScene` failed because
  `MapSceneGenerator.RegisterScenes()` rewrites Build Settings wholesale and named
  only the bootstrap and the raw map — so regenerating the enlarged map deleted
  `Map_FirstSketch_Solo.unity`, the scene 시작 actually loads. `LoadSceneAsync`
  returns `null` rather than throwing for an unlisted scene, so the shell bounced
  silently back to the menu: **the main menu's start button did nothing, with no error
  anywhere.** Fixed by naming the scene once in `SceneGenPaths.MatchScene` and having
  both writers use it.

That is the seam worth knowing about between four agents who could not see each
other's work: the map pass and the front-end pass were each correct alone.

### 1.10 The standalone player — built, launched, and it starts a match

```bash
export CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine \
   -buildPlatform macos-arm64 -buildConfig release -logFile /tmp/b.log; echo "exit=$?"
```

```
exit=0
  macOS Apple silicon                        Release     IL2CPP OK         1347.18 MB  235s
```

A real IL2CPP Release player, from scratch, in 235.6 s. **`CPLUS_INCLUDE_PATH` is
required on this machine** — its Command Line Tools are damaged and clang otherwise
fails on `'cmath' file not found`; [TESTING.md](TESTING.md) has the diagnosis. Never
pass `-quit`: `BuildFromCommandLine` owns the exit code.

Then launched, and read from the player's own log rather than from the fact that a
window appeared:

```bash
open dist/macos-arm64/HorrorGame.app
grep -E "\[Match\] seed|Exception" ~/Library/Logs/DefaultCompany/HorrorGame/Player.log
```

```
[Match] seed 20260731 · 4 clues (§03 needs 3) · planned round trips 4 · 5 zones · local role Runner
```

**0 lines matching `Exception`.** That line is `MatchDirector.BeginMatch` completing
inside a shipped player — §14's guidance overlay builds its canvas in the same frame
and would throw here if it could not.

> **Scene 0 is now `Bootstrap`, not `Map_FirstSketch_Solo`.** The front end took the
> first slot, so a double-click opens the menu and 시작 loads the match
> (`GameShell.LoadMatchRoutine`, pinned by `UiFlowTests`). TESTING.md said the player
> "boots straight into a match" — that was true before the front end landed and is now
> corrected there. To reproduce the log line above without clicking, run
> `StandaloneBuild.PrepareBatch` first, which puts the solo scene at index 0; that is
> how it was measured here, and Build Settings were restored afterwards.

Two log lines are expected and harmless: `[Steam] Running offline on development App
ID 480 (Spacewar).` and `Failed to create agent because there is no valid NavMesh` —
a player-only load-order artefact. The warning that would prove real breakage,
`[Monster] NavMeshAgent is off the NavMesh at …`, does not appear.

### 1.11 The 그늘 is tested, photographed, and not connected to anything

This is the one section on this page that reports a **green** result as a problem, so it
is worth stating plainly before the evidence:

> **§10's second entity does not exist in a match.** 25 core tests, 5 PlayMode tests, a
> mesh, four clips, three materials, a prefab and a shot rig all pass and all describe a
> thing no player can encounter. Nothing ticks `PresenceField`, so it charges nobody.

Three independent checks, because "is this wired?" is exactly the question a green suite
answers wrongly:

```bash
# 1 — who references the runtime types, outside their own tests?
grep -rn "PresenceDirector\|PresenceField\|PresenceSubject\|PresenceTickInput" \
  unity/HorrorGame/Assets --include="*.cs" \
  | grep -v "Assets/Scripts/Gameplay/Presence/\|Assets/Scripts/Core/Presence/"
```

```
9 matches, all in Assets/Tests/PlayMode/Presence/PresenceSessionTests.cs
```

```bash
# 2 — is the component in any scene or prefab? Unity references scripts by GUID,
#     so grepping for the type NAME finds nothing whether it is wired or not.
grep -m1 '^guid:' Assets/Scripts/Gameplay/Presence/PresenceDirector.cs.meta   # 794669c0…
grep -rl 794669c0d93b3453b967a5aa051a523e Assets/Scenes/ Assets/Prefabs/
```

```
(no output — PresenceDirector is in no scene and no prefab)
(same for PresenceSubject, 60c36766…; only PresenceView, 4bee23cf…, appears,
 on Assets/Prefabs/Presence/Presence.prefab, which is the shot rig's own prop)
```

```bash
# 3 — does the thing that runs a match know about it?
grep -c Presence unity/HorrorGame/Assets/Scripts/Gameplay/Match/MatchDirector.cs
```

```
0
```

**What is actually missing** is small and specific, which is why this is worth landing
rather than reverting: a `PresenceDirector` in the match scene, a `PresenceSubject` on
the player rig, and two lines wiring `PresenceDirector.Taken` to the toll —
`PlayerState.MayTransmitVoice && PresenceState.MayTransmitVoice` for §13's channel, and
`PresenceState.RecallSmear01` added to §03's misread condition. `PresenceDirector` is
already self-driven from `FixedUpdate` and finds its own subjects and monster in
`Awake`, so no host-loop surgery is needed.

**Why it was not wired here.** Doing it changes what a player experiences — twelve
seconds of enforced silence and a misread penalty — and every number behind that is
marked provisional in the §16 sense by its own author: `PresenceSaturationSeconds` is
"the round number in the middle of the bracket". Wiring it is a balance decision with a
measurable cost to §03 and §13, not a build fix, and it should be taken deliberately and
then measured, not smuggled in at the end of a landing pass. The rules are correct and
tested; what they are worth is unmeasured.

**Do not let the 660 imply otherwise.** [F-010](BALANCE-FINDINGS.md#f-010) already says
the 그늘 "puts a cost in the 80 % of the building the monster is not in" — that is a
statement about the design, and as of this commit it is not yet a statement about the
software.

---

## 2 · Verified red

### 2.2 The audio alphabet — one blocking defect

```bash
/Users/doogi/horror-game/tools/audio/.venv/bin/python /Users/doogi/horror-game/tools/audio/verify_audio.py
```

Exit code **1**.

```
  §12 Listener alphabet: SUPPORTED — worst surface pair metal vs tile at 2.13x (need >= 1.4x)
  worst within a single actor: 1.98x
  at 25m through a wall it does NOT hold: worst pair wood vs metal at 1.396x
  HUD vs ears: 1 inverted pair(s) — gravel/concrete.
  clips: 166   loops checked: 18   blocking defects: 1   warnings: 3
  RESULT: FAIL
```

The blocking one, in full:

```
  [consistency] gravel vs concrete
      GameConstants says gravel (clarity 0.70) gives the monster away more than
      concrete (clarity 0.50), but gravel measures 32.4 dB quieter than concrete at
      low-pass 600 Hz. Dry, the two agree — so gravel's audibility lives in a band a
      wall removes, and ListenerAbility is explicit that the role hears through walls
```

Tracked as [F-002](BALANCE-FINDINGS.md) and [F-003](BALANCE-FINDINGS.md). The three
warnings: `wood vs metal` separates only 1.40× occluded against a 1.4× requirement;
`Items/flare_burn_loop.wav` has a −9.7 dB hole at every wrap; `Audio/Resources/`
belongs to no known family.

### 2.3 The economy — matches are 7.2 minutes long, and the 2.5 this said was the wrong map

**Superseded later the same night.** The simulator was measuring a building the game
does not have: `core/HorrorGame.Sim/SimMap.cs` built its own four-zone ring out of
`GameConstants` — **38 places, 47 passages, the monster spawning 52 m from the door** —
while the game ships 164 places, 180 passages and 217.5 m. `SimMap` now calls
`FirstMapSketch.Build`, the same call `MapSceneGenerator` makes, so the two cannot
drift.

```bash
dotnet run -c Release --project /Users/doogi/horror-game/core/HorrorGame.Sim -- run --matches 500 --seed 1
```

Exit 0 — **re-run 03:20 after the §1.2 build fix, and every figure below reproduced
byte for byte**, which is the point of a seeded simulator and is the only reason this
section can be trusted over the four that preceded it. The first five lines are the
building, and they reproduce §1.6 above:

```
=== the building these matches were run in
  요양원 지하 5층 (B1 하역장 · B2 기록보관소 · B3 기계실 · B4 저탄장 · B5 저수조)  (seed 1204)
  5 zones · 164 places · 180 passages · 17 순환로 · 41 막힌 길 · footprint 50 m × 92.5 m
  §12 validation PASS · 후보 지점 15 · 전리품 41 · 금고 2 · monster spawn 217.5 m from the door

§01 match length — target 25~35 min
  median                                             7.2 min
  p10 / p90                                          4.2 min / 32.4 min
  inside the window                                  15.8%
  ended with every light dead                        40.6%
  median of the rest                                 17.1 min
  inside the window, of the rest                     26.6%

§07 threat curve
  mean tier at end (0=초저녁 … 4=동트기 전)                 1.12
  reached 심야 or later (tier 2, 16 min)               33.6%
  reached 새벽 or later (tier 3, 24 min)               17.4%
  reached 동트기 전 (tier 4, 32 min)                     13.0%
  chases per match                                   5.52
  chases broken                                      87.7%
  deaths per match                                   0.68

§02 outcome mix
  완전 승리 11.2% · 부분 승리 32.6% · 생존 53.8% · 패배 2.4% · objective recovered 43.8%
```

**The bigger map worked, and did not finish the job.** Against the ring: median
2.5 → **7.2 min**, inside §01's window 0.6% → **15.8%**, 심야 1.2% → **33.6%**. All five
of §07's tiers are now reached — 심야 33.6%, 새벽 17.4%, 동트기 전 13.0% — so the "three
dead tiers" this document has led with since the finding was opened is no longer true.
§01's 25–35 minutes is still not the normal match.

Most of what is left is one population: **40.6% of matches end because every light is
dead and the wallet cannot buy another cell.** A first descent across five storeys can
spend its whole battery walking to 후보 지점 and surface with nothing to sell, and a team
with nothing to sell has no second descent. Excluding them the median is **17.1 min** and
**26.6%** land in §01's window. Full write-up, before-and-after table and revised options
in [F-006](BALANCE-FINDINGS.md#f-006).

Note the collision with §1.3 has eased rather than closed: `MonsterChaseTests` pins §07
to 심야 to measure against §06's 4.8 m/s, and a third of matches now get there against
1.2% before. Also read it beside §1.6 — deaths fell 2.1 → 0.68 and wipes 21.6% → 2.4%,
which is [F-007](BALANCE-FINDINGS.md#f-007)'s TooEasy grade showing up in the match
numbers.

---

## 3 · Every known defect, with a pointer

| # | Defect | Where | Kind |
|:--:|---|---|---|
| 3.1 | ~~`SoloMatchLoopTests` red on a Mirror package-cache `.meta`~~ **not reproducing** — dormant, not fixed | [B-002](BLOCKERS.md#b-002) | environment |
| 3.2 | Two `HallOpen20x20` rooms dropped at `LogError` on every generation | [B-003](BLOCKERS.md#b-003) · `MapSketch.cs:1101` | design intent lost |
| 3.3 | ~~The monster is invisible past ~8 m~~ **fixed** — all 8 staged frames pass, 15 m contrast 0.0592 against a 0.015 floor | §4.1 | art |
| 3.4 | §12's 15–25 m 시야 차단 spacing rule violated, reported not enforced — **79 corners, mean spacing 4.1 m, 0 inside the band**, and this is the measured cause of 3.17 | [ART.md §7.2](ART.md) · [F-007](BALANCE-FINDINGS.md#f-007) · §1.6 | design rule unmet |
| 3.5 | ~~Four zone-view misses against ART.md's bands~~ **fixed by the art pass** — all five zone views inside all four bands on `final_*`, re-measured this pass. Note it was measured with 1 of 123 lights on ([ART.md §7.0](ART.md)) | §4.3 · [ART.md](ART.md) | art |
| 3.19 | **Every interactive object is an untextured white primitive** — clue quad, objective capsule, 전리품 cubes, and the 차량 the team returns to 2.94×/match. Visible in a store screenshot | [ART.md §7.11](ART.md) · §4.4 · `Interactable.cs:110` | art |
| 3.20 | Daylight sky at the vanishing point of a B4 corridor, four storeys underground | [ART.md §7.4](ART.md) · §4.4 | art |
| 3.21 | ~~§14 guidance overlay quotes the wrong-map 2.5 min median to playtesters~~ **fixed** — reads 7.2분, re-rendered to confirm | §4.4 · `PlaytestGuidanceScreen.Q3Caveat()` | documentation-in-code |
| 3.22 | ~~`dotnet build core/HorrorGame.sln` fails while Unity and all 560 tests are green~~ **fixed** — the simulator's project did not compile `RunnerCensus.cs` | [B-006](BLOCKERS.md#b-006) · §1.2 | build |
| 3.6 | `TESTING.md`'s suite command has `-quit` and so reports nothing | §1.9 | documentation |
| 3.7 | `TESTING.md` quotes EditMode 55 / PlayMode 27 and NavMesh 630; they are **70 / 42** and **1830** | §1.5, §1.9 | documentation |
| 3.8 | No test asserts a non-`None` floor material on **generated** geometry | §3a below | test gap |
| 3.9 | Gravel/concrete clarity is inverted against measured loudness | [F-002](BALANCE-FINDINGS.md) | gameplay |
| 3.10 | Matches end in 7.2 min against §01's 25–35, and 40.6% of them end because every light is dead and the wallet is empty. Measured on the real map at last — the 2.5 min this row used to say was the simulator's own building | [F-006](BALANCE-FINDINGS.md#f-006) · §2.3 | gameplay |
| 3.11 | Weight table is a cliff at band 2, not a gradient | [F-001](BALANCE-FINDINGS.md) | gameplay |
| 3.12 | Runner sprint-timing dilemma cannot exist at these numbers | [F-004](BALANCE-FINDINGS.md) | gameplay |
| 3.13 | §12 states two loop rules; only one can ever bind | [F-005](BALANCE-FINDINGS.md) | design |
| 3.14 | ~~The 12 m monster shot photographs the end wall~~ — the staged rig has clearance and all four distances read | §4.1 | tooling |
| 3.15 | Every room is the same room above knee height; the 개방 공간 are near-undressed boxes | [ART.md §7.3](ART.md) · §4.4 | art |
| 3.17 | Map passes 16/16 §12 rules and grades **10/10 TooEasy**, out of the 5–7 band — was 7/10 | [F-007](BALANCE-FINDINGS.md#f-007) · §1.6 | gameplay |
| 3.18 | Settings screen's 해상도 row reads `640 × 480` in a batch shot, so the real default is unconfirmed | §4.2 | ui |
| 3.16 | `final_overhead.png` is still a blue rectangle, not a map | §4.4 | tooling |
| 3.22 | ~~A 전리품 put down hung in mid-air at chest height~~ **fixed** — `DropPlacement`; lands 0.020 m above the surface, upright, pickable. Driven by the real key in `InteractionDropTests`, photographed in `Shots/drop_*` | §1.4 · §1.9 | gameplay |
| 3.23 | ~~First person drew no hands at all — §05's "자기 몸은 안 보이므로 손만 있으면 된다" was met by hiding everything~~ **fixed** — `PlayerFirstPersonView` splits body/arms/hand-prop by material slot; both hands on screen in all six states, body still `ShadowsOnly` | §4.5 | art · gameplay |
| 3.24 | ~~The torch was never in the hand — the only cue for §03's four states was the lighting~~ **fixed** — `PlayerFlashlight.InHand` draws it on `IsOn`, and it is legible at the game's own exposure | §4.5 | art |
| 3.25 | ~~The shop opened itself on walking into the 차량's apron — 갑자기 상점이 열림, mouse and camera taken on a position test~~ **fixed** — `MatchDirector.Surfaced` sells and says so; `SurfaceVehicleInteractable`'s key is the only thing that opens the panel | §1.4 | ui |
| 3.26 | A 대형 전리품 dropped facing a wall overlaps it by **0.138 m**. `DropPlacement.ClearanceRadiusMetres` is `Interactable.MinimumTargetMetres * 0.5` = 0.15 m — the crosshair's aim tolerance, not the piece's own half-size, so anything wider than the sweep touches. Still on the floor, upright and pickable; reads as leaning | §1 · `DropPlacement.cs` | gameplay |
| 3.27 | ~~§01's 지상 had no physical presence — no line, no gate, no threshold, no light or sound change. One line of §14 text was the only thing telling a player which side of the 안전 지대 they were on~~ **fixed** — `SurfaceApron` paints the boundary at `MatchMap.SurfaceRadius`, frames and lights every walkable crossing, and lights the walk. §4.6 | `SurfaceApron.cs` · §1.9 | design |
| 3.28 | ~~§08's 차량 was parked inside the 출입구 corridor's walls — a 2.81 m body in a 2.2 m passage~~ **fixed** — `SurfaceApron.Park` measures a fit and puts it in the 하역 베이 11.5 m out | §4.6 · `SurfaceApron.cs` | gameplay · art |
| 3.29 | **The 차량's body is `Prop_Iron` — albedo 0.11, metallic 0.90.** A 90 % metallic surface has almost no diffuse response, so the shop the team returns to 2.94 times a match renders as a black silhouette from every angle no matter what is shining on it. Its shape reads; its surface does not. §4.6 | [ART.md §7.12](ART.md) · `Props.manifest.json` | art |
| 3.30 | **`PlayerSpawn_0` sits at exactly 15.000 m from the 출입구 — precisely on §01's boundary.** `MatchMap.IsOnSurface` uses `<=`, so which side the player starts on is decided by float rounding. `BeginMatch` forces `_onSurface = true` afterwards, so nothing breaks today; a spawn one epsilon out would fire a spurious 잠입 on the first tick | `MapSketch.cs:1552` · `MatchMap.cs:156` | latent |
| 3.31 | **§03's room labels are not written on any room.** `MatchMap.SignFor` derives a `SiteLabel` per 후보 지점, the chain narrows to it, and `ClueReadingView` renders it — *「ㅁ-6 좌」* — but **nothing in the world draws it.** Grep: `SiteLabel` appears in Core, Net and one UI readout and in no scene object. §03 says the sign "is part of the building… which is what lets a veteran hear ㅁ-6 좌 and already know where to run", and §12 makes 맵 구조 the fixed column precisely so it can be learned. So the clue chain converges on a name with no referent: a player who reads all three marks correctly still has no way to identify the room. The dressing pass scatters generic `Sign` props at a 15 % chance and none of them carries a label | §3a · `MatchMap.cs:376` · `ScatterSession.cs:953` | design — same class as 3.27, and larger |
| 3.32 | **The 출입구 is marked by one burning point light and nothing else.** The stairwell shaft is built (`FirstMapSketch.cs:193`) but its upper flight deliberately lands off-plan because the surface is not modelled, so the way out of the building is a lit dead end. The apron now says *where the safe zone is*; nothing says *these are the stairs up* | `MapSceneBuilder.BuildLight` · §4.6 | design |

### 3a · The floor-material chain — previously S-001, now wired

The last edition of this document led with **S-001**: §12's floor material never
reached the runtime on a generated map, so §04's Listener channel was silent. **That
wiring now exists.**

- `MatchAudioBridge.BindFloorProbe` takes `MonsterAgent.Probe` and calls
  `MatchAudioRig.SetFloorProbe`, which assigns `FloorSurfaces.Probe`. Deferred to
  `Update` because the probe does not exist until `MonsterAgent.Initialize` has run.
- `AudioSceneWiring.Wire(scene)` puts the rig and the bridge into the playtest scene.
- `AudioSceneTests` loads `Map_FirstSketch_Solo.unity` in PlayMode and asserts the
  rig is there, has a clip library, has a listener, has a mix, and that
  `rig.Zones.CurrentBed` is not null. It passes — §2.1, 41/41, 0 skipped:

```
[AudioSceneTests] solo playtest scene:
[AudioCensus] 10 of 26 sources audible
```

**What is still missing is the test that would have caught it (defect 3.8).** Every
floor-material assertion in the suite injects a fake:
`FloorSurfaces.Probe = _ => FloorMaterial.Gravel`. Nothing generates a map, stands a
player on it and asserts the surface underneath is not `FloorMaterial.None`. The
chain is wired and is not pinned, so the next reshuffle can break it exactly as
quietly as last time.

---

## 4 · The look, as of this pass

Rendered fresh, **without** `-nographics` — that flag disables the graphics device
and every shot comes out black. Four passes, each exit 0:

```bash
… -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag map          # 10 shots
… -executeMethod HorrorGame.EditorTools.SceneGen.BootstrapSceneGenerator.ShotBatch \
  -shotTag menu                                                        # 5 shots
… -executeMethod HorrorGame.EditorTools.Playtest.GuidanceShot.Batch \
  -shotTag guide                                                       # 5 shots
… -executeMethod HorrorGame.Gameplay.MonsterEditor.MonsterShot.StageBatch \
  -shotTag stage                                                       # 12 shots
```

### 4.1 The monster is visible at every gated distance — all 8 frames pass

```
[MonsterShot] staged readings  (pass: contrast >= 0.015, coverage >= 0.40, peak >= 0.040)
  dist  state       footprint    diff  coverage    peak     body    ring  contrast   verdict
     8m Chase         1946px  0.0337    0.728  0.1048  0.1166  0.0956    0.0331   PASS
     8m Patrol        2039px  0.0330    0.742  0.0970  0.1167  0.0968    0.0318   PASS
    12m Chase          889px  0.0581    0.927  0.1530  0.1200  0.1021    0.0586   PASS
    12m Patrol         920px  0.0573    0.935  0.1583  0.1157  0.1022    0.0576   PASS
    15m Chase          599px  0.0588    0.947  0.1631  0.1376  0.1202    0.0592   PASS
    15m Patrol         610px  0.0544    0.918  0.1558  0.1294  0.1205    0.0550   PASS
    20m Chase          345px  0.0530    0.936  0.1325  0.1522  0.1491    0.0532   PASS
    20m Patrol         345px  0.0615    0.962  0.1373  0.1570  0.1492    0.0617   PASS
[MonsterShot] §04's 관측자 range passes: every frame at 15 m is above the visibility floor.
```

**This is the defect the last two passes of this document called the honest headline,
and it is fixed.** At 15 m the creature clears the contrast floor by 3.9× (0.0592
against 0.015) and the peak floor by 4.1×. It is legible at 20 m, past §03's 12 m
beam. §04's 관측자 and §12's 주자 table both need that and now have it.

Looking at the frames rather than the numbers: at 8 m it is a gaunt hunched biped with
elongated arms, a bladed head and two pinprick eye lights — recognisably a creature.
At 15 m it is a dark upright smudge with two eyes, which is the right amount. The
sculpt detail the art pass added is invisible past about 5 m, so it is doing less work
than its cost suggests.

### 4.2 The menu and settings screens look commercial

`Shots/menu_main.png` is the strongest frame in the project: the title over a
receding brick corridor with a practical at the far end, three buttons with
subtitles, and §05's headphone warning along the bottom. Nothing about it reads as
programmer art.

`Shots/menu_settings.png` is a real settings screen — FOV, sensitivity, Y-invert, four
audio buses, resolution, screen mode, vsync, quality preset, six rebindable keys,
기본값 and 닫기·저장, each row carrying the §-reference for why the range is what it
is. This is further along than the rest of the game.

One defect visible in it: the 해상도 row reads `640 × 480`, which is the batch-mode
window rather than a real default, so the shot cannot confirm what a player would see.

### 4.3 The map got darker when it doubled, and the art pass pulled it back into band

Two measurements, same command, same viewpoints, same scene — only the shot tag
differs. `map_*` is the five-storey building before the art pass's detail normals,
decals, practical glows and zone skins; `final_*` is after, re-rendered from scratch
this pass:

```bash
… -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag final
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'final_Zone_*.png'
```

```
shot                                     mean    p50    p90    p99  black%  legible%  blown%    sat
final_Zone_A_B2_Wood.png                  8.0    4.2   18.6   62.9    33.9      32.2    0.00    4.5
final_Zone_B_B5_Tile.png                  8.1    4.7   17.7   63.5    31.6      33.2    0.00    8.4
final_Zone_C_B4_Gravel.png                8.5    5.1   18.2   69.2    30.9      34.3    0.00    9.2
final_Zone_D_B1_Concrete.png             10.0    7.4   18.9   59.5    14.5      47.8    0.00   12.4
final_Zone_E_B3_Metal.png                12.6    8.4   31.0   49.7    17.6      51.9    0.00   13.9
```

Against [ART.md](ART.md)'s targets — 10–40 % crushed, 30–75 % legible, median 3–16,
blown < 0.5 %:

| Band | 3 storeys (`real8_*`) | 5 storeys, before art (`map_*`) | 5 storeys, now (`final_*`) |
|---|---|---|---|
| crushed 10–40 % | 10.4–37.4 % ✓ | 17.0–**40.6 %** ✗ | 14.5–33.9 % ✓ |
| legible 30–75 % | 28.4–54.8 % | **25.9**–52.5 % ✗ (A, B, C) | 32.2–51.9 % ✓ |
| median 3–16 | 3.9–9.1 ✓ | **2.9**–8.4 ✗ (A) | 4.2–8.4 ✓ |
| blown < 0.5 % | 0.00 % ✓ | 0.00 % ✓ | 0.00 % ✓ |

**Four misses became none, and all five zone views are inside all four bands for the
first time.** The middle column is what doubling the building cost before anything was
done about it: every zone lost legible fraction and every median fell, because the
grade was not retuned when the rooms moved further apart and more often fell outside a
practical's falloff.

Two qualifiers, both ART.md's and both load-bearing:

- **This was measured with the lights off.** One of the 123 lights in the saved scene
  is switched on — the dressing pass's output is not in `Map_FirstSketch.unity`
  ([ART.md §7.0](ART.md)). When the lights come back the band will have to be judged
  again, probably downward, which is the good direction to have to move in.
- **None of it came from the grade**, which ART.md §3.13 is the measurement that
  proves; it came from surface detail that survives being unlit.

The earlier edition of this document quoted the `map_*` column as the current state
and the one-line answer at the top said three of five views miss the legible floor.
That was true when it was written and stopped being true in the same night's art pass.

### 4.4 What still does not hold up

**Every object the player touches is an untextured white primitive.** The clue is a
white quad, the objective a white capsule, 전리품 white cubes, and the 차량 — the shop,
where §01 sends the team at the end of every one of 2.94 round trips — is a plain white
box two metres on a side. `Interactable.CreateProp` calls
`GameObject.CreatePrimitive`. In a building dressed to 7.5 cm brick courses with
contact dirt in the corners, the eye goes to these before anything else in the frame.
Evidence in `docs/store/defects/S1`–`S4`; written up as [ART.md §7.11](ART.md), which
is new this pass — the art register had never carried it, because the pass that found
it was the store pass and it filed under `docs/store/`.

**The rooms are empty.** `final_Zone_D` and `final_Zone_E` are large textured boxes
with a pillar and a pool of light in them. The corridors carry the game — `final_spawn0`
and `final_spawn3` have skirting, wall panels, ceiling beams, conduit and a crate, and
they look like a horror game — but the open spaces have almost no set dressing, and §12
requires one 개방 공간 per zone. A player will spend real time in those.

**Daylight is visible from four storeys underground.** `final_Zone_C_B4_Gravel.png`
puts a bright blue rectangle of sky at the vanishing point of a brick tunnel on B4 —
dead centre of frame, and the brightest thing in it. [ART.md §7.4](ART.md) had this
filed as an edge-of-frame artefact on B2/B3 and as blocked on a broken NavMesh; it is
neither. The NavMesh audit reads 1830/1830 (§1.5), so nothing is blocking the fix.

**`final_overhead.png` is still useless** — a small flat blue rectangle, the roof of the
top storey seen from above. Same shot-rig bug as the previous two passes.

**The guidance overlays are developer instrumentation, not UI.** That is what they are
for, and they work; worth stating only so nobody mistakes `guide_*.png` for the
player-facing HUD.

> **Fixed this pass.** The §14 panel in `guide_underground.png` printed F-006 against
> itself in red: *"아직 물을 수 없습니다 — 한 판 중앙값 **2.5분**, §01 목표는 25~35분"*.
> The previous edition of this document noticed it was stale and left it. It was not
> merely stale prose — it was the wrong-map measurement, in a string literal in
> `PlaytestGuidanceScreen.Q3Caveat()`, on the one screen a §14 playtester is told to
> read. It now says 7.2분, verified by re-rendering the overlay. The window either side
> of it comes from `GameConstants`; only the median is a literal, and the doc comment
> now says where it was measured and what to re-run.

### 4.5 The four defects, seen rather than asserted

Two photographers were run **without `-nographics`**, which is the only way any of this
is evidence — that flag turns the graphics device off and every frame comes out black.

```bash
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.Gameplay.PlayerEditor.FirstPersonHandsShot.Batch -shotTag hands
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.DropShot.Batch -shotTag drop
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.UI.EditorTools.ShopShot.Batch -shotTag shop
```

**Hands (3.23) and torch (3.24) — fixed, and legible.** `FirstPersonHandsShot` reports
the viewport coordinate of each hand, and every state has both on screen:

```
  frame                                            left hand        right hand       torch          arms  torch
  empty hands, torch off [Idle]                   (0.15,0.26) on   (0.79,0.28) on   (0.67,0.38) on     1   -      off
  torch in hand, lit [Idle]                       (0.15,0.26) on   (0.79,0.28) on   (0.67,0.38) on     1   drawn  on
  §08 대형 전리품, both hands [CarryHeavy]            (0.12,0.24) on   (0.88,0.23) on   (0.82,0.38) on     1   -      off
  §03 목표물, both hands, no torch [CarryIdle]      (0.32,0.44) on   (0.68,0.44) on   (0.62,0.64) on     1   -      off
  walking, stride phase A [Walk]                  (0.15,0.27) on   (0.80,0.27) on   (0.68,0.37) on     1   drawn  on
```

Read as pixels rather than as numbers: with the torch lit, the right forearm and its
pale hand and the torch barrel in the fist are plainly there at the game's own exposure
(`Shots/hands_10_torch.png`). **With the torch off they are close to invisible** — dark
blue-grey slabs against a dark blue-grey floor, findable in the frame but not readable
as hands until the exposure is raised. That is §03's darkness doing what §03 says it
does, not a regression, and it is worth knowing before anyone re-reports it.

The arms themselves are low-poly angular forms with mitten hands. They read as *arms*,
not as *hands*, at any exposure. That is defect 3.19's family — the whole prop set is
still untextured primitives — and it is the next art job, not this one.

**The two carry frames photograph the pose, not the cargo.** `FirstPersonHandsShot`
builds a bare rig, so `20_loot` and `30_objective` show the arms in the carry pose with
nothing in them. What a carrier actually sees is in `DropShot`'s
`drop_10_in_the_hands_*`: the 대형 전리품 fills the top of the frame with a hand on each
edge of it, which is §03's "carrier cannot see past it" happening.

**The drop (3.22) — fixed.**

```
  00_on_the_floor        spawned at (58.75, 0.16, 83.75), 0.020 m above the surface
  10_in_the_hands        held at (58.75, 1.53, 84.49), 1.24 m above the boots, torch stowed — §03 takes it from full hands
  20_dropped_at_my_feet  resting at (58.75, 0.16, 84.49), fell 1.37 m, 0.020 m above the surface, tilt 0.0°, clear of geometry
  40_witness_side        side view, 0.020 m above the surface
  50_dropped_facing_a_wall  wall 0.75 m ahead at hand height (carry offset 1.05 m), landed 0.60 m from the boots
                            — on this side of it, 0.020 m above the surface, overlapping HallOpen20x20_L0_20_31 by 0.138 m
```

`drop_40_witness_side.png` is the frame that settles it: a lit side view with the piece
standing on the floor and its own shadow under it. A first-person frame cannot answer
"is it touching" — the base is behind the front face from every angle the eye can take.

The wall row is defect 3.26 and the number is the point of it. The piece stops on the
player's side of the wall, upright and pickable, but 0.138 m of it is inside the bricks,
because the sweep radius is the crosshair's aim tolerance rather than the piece's own
half-width. In `drop_50_dropped_facing_a_wall_fill.png` it reads as a portrait leaning
against a pillar. The fix is one number — size the sweep from the piece's fitted
collider — and it was deliberately not made in this pass, after the suite had already
been run against the code as it stands.

**The shop (3.25) — fixed, and the screen behind it holds up.** Ten frames.
`shop_broke.png` is the 구매력 0 state §08 opens with: every row priced, every 대가 on
the same line as its 효과 and in colour, `— 가격 미정` where §08 gives no answer,
`N 모자란다` under each price, §11's missing role across the top in red, §07's hour and
the countdown to it under the title, and `↑↓ 이동 · Enter 구매 · Tab 싣기 · E 닫기` in
the corner — the list is operable without a mouse. `shop_pressure.png` shows the visit
timer gone amber at `여기서 40초` with the night bar down to a stub, which is §07
charging for deliberation where a player can see it.

One cosmetic thing in every shop frame: two pale HUD bar segments show below the panel's
bottom edge, bottom-left and bottom-centre. The panel does not quite cover the HUD it is
drawn over. Not filed as a defect — it is a two-pixel inset — but it is in the pictures.

### 4.6 §01's 지상 now exists as geometry — photographed from five viewpoints

The boundary the whole loop turns on had **no physical presence whatsoever**. §01 makes
the ground around the 출입구 the 안전 지대, §08 puts the shop, the sale and the 보급소 on
it, §02 ends the match when the objective crosses into it — and it was a
`sqrMagnitude` test against a light, on the same unbroken concrete as the rest of B1
하역장. No line, no gate, no threshold, no lighting change. The only thing telling a
player which side of it they stood on was one line of §14 guidance text, which read
「출입구 **앞마당**을 벗어나면 잠입입니다」. The owner played the build and asked
**앞마당이 어디야**. That is the correct question, and it should not have been possible.

`SurfaceApron` builds four things into the match world at `BeginMatch`, all measured
against `MatchMap.Entrance` and `MatchMap.SurfaceRadius` rather than authored:

```
[Apron] §08 차량 parked at (52.4, 0.1, 93.3), yaw 0°, 11.5 m from the 출입구 with 0.55 m
        of clearance — inside §01's 15 m 지상. Its body is 2.8 × 6.7 m and the 출입구
        corridor is 2.2 m wide, which is why it could not stay where §12 marks the door.
[Apron] gate 0 — 21.2 m wide, from (55.5, 0.1, 100.9) to (48.8, 0.1, 83.3)
[Apron] gate 1 —  3.1 m wide, from (44.4, 0.1,  81.6) to (42.0, 0.1, 81.3)
[Apron] §01 지상 painted at 15 m — 33 of 120 stripes found floor, 2 walkable crossing(s)
        over 25.1 m of the ring, 7 practical(s) inside it, 15 warm source(s) in all.
```

**A painted hazard line.** 120 steps round the ring, each raycast for floor, a surface
facing up, a height within one step of the door and clear headroom; 33 survive. §12 put
the 출입구 in the north-west corner of B1, so three-quarters of a 15 m circle is outside
the building — what is left is a 26 m arc sweeping out of the service corridor and
across the 20 × 20 m 하역 베이, which is the one 개방 공간 on the storey and the best
place in the building to see a line from. Alternating amber and bone, 0.40 m wide, 5 cm
proud of the floor, emissive at 0.45 of its own colour so it still reads where no lamp
reaches (0.30 was tried first and the far half of the arc disappeared at 25 m).

**Lit crossings.** Every run of ring that is on the NavMesh gets a bollard at each jamb
and a warm lamp every 5 m over it. Two on this map: the 21.2 m mouth of the bay and the
3.1 m service passage at `PlayerSpawn_0`.

**Warm inside, cold out.** §07 cools the grade as the night advances and §12's zone
fittings are a blue that starts switched off; the apron is the opposite. A bay lamp over
the door, and practicals every 4.5 m along the NavMesh path from the door to each gate —
*along the walk, not on a circle*. A ring of practicals was tried and fitted 2 of 16,
because the apron here is mostly a 2.2 m corridor and a circle drawn through it is
mostly wall. Path-based fits 7, and the difference is the whole read from the spawn:
before it, the frame was a cold corridor with two headlamps at the end.

**The crossing, both ways.** The threshold lamps swell 55 % on 귀환 and dip 45 % on
잠입, over 1.4 s. Cosmetic by construction — the only thing it writes is
`Light.intensity` on lamps the class made, so §13's replay cannot see it. The audio half
already existed and was already wired: `AudioCueId.SurfaceReached` / `Descend` fire off
the same edge in `MatchAudioBridge`, and `amb_surface_vehicle_loop` is the surface bed
`ZoneAmbienceDirector` crossfades to. Nothing there needed changing.

Photographed with `StoreShotRig.Shoot` — the only rig that can see any of this, because
it is the only one that begins a match; `SceneShot` opens the raw map, which has no
`MatchDirector` and therefore no apron. Five rounds, `unity/HorrorGame/Shots/apron5`:

| Frame | What it answers |
|---|---|
| `12_spawn_looking_down` | Standing at the spawn, the stripes are under the boots. `PlayerSpawn_0` is exactly 15.000 m out — on the line (defect 3.30) |
| `10_spawn_toward_the_door` | Looking in: a warm lit run of practicals up the corridor. Cold behind, warm ahead |
| `11_spawn_toward_the_way_out` | Looking out: bollard and paint in the foreground, darkness past them |
| `21_bay_from_ten_out` | Ten metres outside, across the bay: the arc, and the van inside it with its headlamps on |
| `22_bay_far_corner` | From the far corner, ~28 m: line, bollard, van, roof beacon, all still legible |
| `41_the_van_rear` | The line at eye level beside the shop end |
| `60_bay_from_above` | The whole ring, its two bollards and the parked van in one frame |

`frame_stats.py` over those frames: mean 16–73, black 0–20 %, blown ≤ 0.1 % except two
frames where the camera is a metre from a wall with the torch on it. **ART.md's zone
bands are untouched** — `SceneShot`'s `final_Zone_*` views contain no apron, by
construction.

**What is still wrong in these frames.** The van reads as a *shape* — cab, box, wheels,
loading ramp — and not as a *surface*: its body is `Prop_Iron`, albedo 0.11 at metallic
0.90, and a 90 % metallic material has almost no diffuse response, so it is a black
silhouette however many lamps are on it. That is defect 3.29 and it is an asset
decision, not a lighting one; the fix belongs in `gen_props.py`, not in the runtime.
And the van is not visible from the 출입구 itself — the corridor east of the door is a
dead end, so a player surfacing through the small gate walks to the door and has to
turn into the bay to find the shop.

### 4.7 The verdict — could this be sold?

**The menu could ship tomorrow. The corridors could ship after a dressing pass. The
open rooms could not, and the game as a whole could not — but not for visual
reasons.**

Nothing about the *environment* looks like a prototype in the way prototypes usually
look: the materials are consistent, the fog and grading are coherent, the monster is a
creature rather than a capsule, and the interface is written by someone who knows what
the settings are for. Someone shown `menu_main.png` and `final_spawn0.png` would
believe it was a commercial product, and `docs/store/screenshots/03_it_is_closer_now.png`
— the creature at range with its eyes and maw lit, down a brick corridor — is a
screenshot that would sell the game on its own.

**One thing does look like a prototype, and it is unavoidable rather than incidental:
the props.** A white cube where the shop is, a white quad where the clue is. It is in
`docs/store/screenshots/01_corridor_and_beam.png` — a frame already chosen for the
store page. This is the cheapest large improvement available to the project: four
props, one generator, one binder change (§4.4, [ART.md §7.11](ART.md)).

And a buyer would then play it. The median match ends in seven minutes rather than
§01's twenty-five — four times in ten because the last battery died and the wallet was
empty (§2.3) — on a map every one of 164 places escapes the monster from (§1.6).

**The look is ahead of the game, and the props are behind the look.** In spend order:
the four props (hours, and it unblocks the trailer and the store page); then F-006's
bootstrap — the 40.6 % that end broke, which is most of the remaining match length;
then F-007's corner density; then open-room dressing. None of those four is more art
except the first.

---

## 5 · Built but unverified

- **Networking.** Mirror, Steamworks.NET and FizzySteamworks are installed and the
  transport is wired; `NetTests` passes in PlayMode. No two-instance session has been
  run in this pass. §14 step 2 is `HorrorGame ▸ Play ▸ Launch Two Instances`.
- **A Windows player.** The macOS arm64 IL2CPP player is built and verified (§1.10).
  macOS cannot produce an IL2CPP **Windows** player, only Mono; a shipping Windows
  build needs a Windows machine or `.github/workflows/unity.yml`.
- **Steam upload.** `tools/steam/` dry-runs without contacting Steam and refuses to
  upload while the App ID is still 480. Never exercised for real —
  [STEAM-RELEASE.md](STEAM-RELEASE.md).
- **All five roles in a real match.** The solo loop exercises the 정비공 and 주자
  gates; 청음사, 관측자 and 섬광 have unit tests and no play evidence.
- **The floor-material chain end to end** — wired, not pinned. §3a.

---

## 6 · Missing

- **§14 Q1 「추격이 재밌는가?」** — now *askable* for the first time, and unanswered. A
  machine can say the monster arrives at 4.83 m/s; only a person can say whether
  getting away from it is a good time.
- **§14 Q2 「곁눈질 딜레마가 작동하는가?」** — needs a human at a mouse. The Player Feel
  Harness shows live speed, the §05 directional multiplier and the margin over the
  monster.
- **§14 Q3 「지금 나갈까?」** — **askable in a minority of matches.** F-006: at a 7.2-minute
  median the pressure the question is about exists in the 33.6% that reach 심야 and in
  the 15.8% inside §01's window, and not in the 40.6% that end broke on the first
  descent.
- **§14 Q4 「6이었나 9였나」** — the confusion pairs are implemented and tested; whether
  they produce the argument is a human question.
- **§14 Q5 청음사 방향·거리** — headphones required, and expect it to work close and
  fail far: 2.13× dry, 1.396× at 25 m through a wall.
- **Art above knee height.** Five zones, five floors that genuinely read, and the same
  brick wall and central pillar in all of them. [ART.md §7.3](ART.md).
- **Props.** Every object §01's loop is *about* — clue, objective, 전리품, the 차량 —
  is an untextured primitive. [ART.md §7.11](ART.md), §4.4, defect 3.19. This is the
  cheapest large improvement available to the project.
- ~~**A monster you can see at 12 m.**~~ Done — all eight staged frames pass, 12 m
  contrast 0.0585 against a 0.015 floor. §4.1.

---

## 7 · How to play it

### First open

```bash
open -a "Unity Hub"
```

Add `/Users/doogi/horror-game/unity/HorrorGame`. The first import takes several
minutes — it resolves Mirror, Steamworks.NET and FizzySteamworks.

The map is already generated and on disk. To rebuild it:
`HorrorGame ▸ Scene Gen ▸ Regenerate Map (layout → dressing → atmosphere)`.
Generation fails if any §12 rule breaks or a 계단 bakes as two surfaces, so a bad map
cannot reach you. `HorrorGame ▸ Scene Gen ▸ Report Map Quality` prints §1.6's report
without writing anything.

### Play it alone

Open `Assets/Scenes/Map_FirstSketch_Solo.unity` and press Play. One player, one
monster, a `MatchDirector`, the full §01 loop from §1.4 — and **the monster will find
you**, which is new as of today. It will cross storeys to do it.

### Play it as intended — §14 step 2

`HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)`

Two instances on one PC, Discord for voice, local hosting. §14 puts this before Steam
deliberately: 「직접 만져봐야 나온다」. One player takes aggro and runs for an
S-corridor; the other watches. **This is the single highest-value thing anyone can do
with this project right now** — every automated gate above is green or explained, and
§14 says questions 1 and 2 decide the project.

What to watch for, and the number to hold it against:

| Watch | Expect | From |
|---|---|---|
| The monster crossing a storey to reach you | it can, at 4.83 m/s of route | §1.3 |
| Breaking aggro round two 10 m legs | released ~5.5 s after aggro, at ~12 m | §1.3 |
| Breaking aggro round a single corner | caught, ~12.5 s | §1.3 |
| Where the monster goes when it loses you | the last sighting, not you | §1.3 |
| Sprinting away in a straight line | you gain 0.8 m/s — and only unloaded | §1.3, F-001 |
| Seeing it approach down a corridor | you will not, past ~8 m | §4.2 |
| Telling zones apart by floor sound | works in the room, fails at 25 m through a wall | §2.2 |
| The match lasting long enough to matter | ~7.2 min, and 4 in 10 end when the last battery dies | §2.3 |

### Where the rules live

`docs/game-design.md` is the authority for every rule. `GameConstants.cs` is the
authority for every number — a literal anywhere else is a bug.

---

## 8 · Reproducing this document

In order, one at a time, with the Unity editor closed:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
P=/Users/doogi/horror-game/unity/HorrorGame
cd /Users/doogi/horror-game

dotnet test  core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj      # §1.1  451/451
dotnet clean core/HorrorGame.sln -c Release                               # or §1.2's warning count lies
dotnet build core/HorrorGame.sln -c Release                               # §1.2  0 errors, 11 warnings
$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log
grep -cE '^Assets/.*error CS' /tmp/u.log                                  # §1.2  0

# §1.9 — the full Unity suite. NEVER -quit: the runner is async and -quit shuts the
# editor down before results are written, which reports nothing and exits 0.
$U -batchmode -projectPath $P -runTests -testPlatform EditMode \
   -testResults /tmp/editmode.xml -logFile /tmp/edit.log                                 # §1.9  101/101
$U -batchmode -projectPath $P -runTests -testPlatform PlayMode \
   -testResults /tmp/playmode.xml -logFile /tmp/play.log                                 # §1.9  72/72
python3 -c "import xml.etree.ElementTree as ET,sys; r=ET.parse(sys.argv[1]).getroot(); \
  print(r.get('total'), r.get('passed'), r.get('failed'), r.get('result'))" /tmp/playmode.xml

$U -batchmode -projectPath $P -runTests -testPlatform PlayMode \
   -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log   # §1.3  4/4
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SoloPlaytest.VerifyBatch -logFile /tmp/solo.log # §1.4
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log                 # §1.5
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu \
   -logFile /tmp/quality.log                                                             # §1.6  FAIL, 16/17

# B-007 — the same verdict as a build gate. Expect exit=1 and no scene written.
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.GenerateFromCommandLine \
   -logFile /tmp/gen.log; echo "exit=$?"                                                 # B-007  exit=1
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log   # §1.7

# §2.1 — NO -quit. The runner exits from its own callback.
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine -logFile /tmp/t2.log

tools/audio/.venv/bin/python tools/audio/verify_audio.py                                 # §2.2  FAIL
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1        # §2.3

dotnet run -c Release --project core/HorrorGame.Sim -- map                                # §1.6, the same grade headless

# §4 — WITHOUT -nographics, or every shot is black.
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
   -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag final -logFile /tmp/shot.log   # §4.3
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.Gameplay.MonsterEditor.MonsterShot.StageBatch \
   -shotTag stage -logFile /tmp/mon.log                                                   # §4.1  8 frames
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.BootstrapSceneGenerator.ShotBatch \
   -logFile /tmp/boot.log                                                                 # §4.2  menu, settings
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.Playtest.GuidanceShot.Batch -logFile /tmp/guide.log  # §4.4
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'final_Zone_*.png'
```

Expected exit codes: **0** everywhere except `verify_audio.py` (**1** — one blocking
defect, F-002). The full Unity suite exits 0: B-002 has stopped reproducing and
EditMode and PlayMode are 70/70 and 42/42, so an exit 6 here means a real regression
rather than the known environment failure this line used to excuse.

> **Run `dotnet build core/HorrorGame.sln` before the simulator, not after.** It is the
> only command in this list that can see a break in `HorrorGame.Sim`, and on
> 2026-08-01 it was the only one that did — Unity compiled clean and all 560 tests
> passed over a simulator that would not build ([B-006](BLOCKERS.md#b-006)).

**Check the exit code before reading any error count.** A Unity run that died early
writes a log with zero errors in it, and that is not the same thing as a clean run.
§2.1 is exactly that trap, and the documented command in TESTING.md falls into it.

---

Companion documents: [BLOCKERS.md](BLOCKERS.md) for things that stop the game
working · [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) for numbers that contradict the
design · [ART.md](ART.md) for the look · [TESTING.md](TESTING.md) for the test
inventory · [ASSETS.md](ASSETS.md) for the asset pipeline ·
[ARCHITECTURE.md](ARCHITECTURE.md) for how the code is arranged ·
[game-design.md](game-design.md) for what any of it is for.
