# Project status

Where this game actually stands, on 2026-08-01.

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

**Every test in the project is green for the first time — 560 of 560 — and the
headline number the map was grown to move did not move at all.**

The building is now five storeys instead of three: 164 places and 180 passages
against 74 and 85. The monster's route to a player grew from 133.9 m to **189.6 m**
and it still arrives, in 38.86 s at 4.85 m/s, so [B-001](BLOCKERS.md#b-001) stays
closed at the larger scale. [B-002](BLOCKERS.md#b-002) has stopped reproducing and
one real seam between two of the four parallel passes was found and fixed
([B-005](BLOCKERS.md#b-005) — regenerating the map silently unregistered the scene
the 시작 button loads, so the main menu did nothing).

**F-006 is exactly where it was: median match 2.5 min against §01's 25–35, and 1.2%
of matches reach 심야.** Identical to three significant figures, and it could not have
been otherwise — the simulator builds its own four-zone map from `GameConstants` and
never reads the Unity level. Growing the building was option 1 in F-006's own list of
fixes, and it was applied to the wrong map. That is the single most important thing
on this page.

Two things also got worse and are written up rather than buried: the map's §12 주자
테스트 fell out of its band, 7/10 Balanced → **10/10 TooEasy** (F-007), and three of
five zone views now miss the legible-pixel floor the three-storey map cleared (ART.md
§ Measured targets).

**Nobody has yet sat down with two instances and played it**, which is still the
highest-value thing anyone can do here and still cannot be automated.

---

## 1 · Verified green — command, and the output it produced

### 1.1 The rules core — 448 tests, no engine

```bash
dotnet test /Users/doogi/horror-game/core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

```
통과!  - 실패:     0, 통과:   448, 건너뜀:     0, 전체:   448, 기간: 337 ms
```

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
경과 시간: 00:00:01.42
```

11 warnings, all `CS8625` nullable-literal plus one `CS0649`, all in the test project.

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

Note what this does *not* say. A 39 % longer route bought 11.3 s of monster travel; it
did not buy a longer match, because the thing that ends a match is the objective loop
and that is measured somewhere else entirely (§2.3, F-006).

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
  §08 picked up 회중시계 · 반지 — weight 1/10, speed ×1.00
  §04 safe: refused 주자, opened for 정비공, 문서 taken
  §03 objective refused while holding loot: §03 전리품 동시 소지 불가 — 들고 있는 전리품을 먼저 처리해야 한다.
  §03 read cancels when the light goes — progress reset to zero
  §03 read completed and the overlay drew: "녹 → 4"
  §01 descended — §07 clock hidden, 4.3s elapsed
  §03 partial reset — monster back at spawn (was 19.0 m away), clock untouched
  §08 sold on arrival — team wallet 65 credits
  §08 shop open at the vehicle, cheapest item 15 credits
  §03 objective taken — no flashlight, no loot, speed ×1.00
  §02 FullVictory — escaped 1, lost 0, clock 6.9s
  §02 회수 released the objective — load 0
  §02 Survived — information kept without the objective
  §13 second BeginMatch — empty hands, load 0 (dropped 1 carried-over piece(s))
PASS — §01's loop ran end to end.
```

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
§12 map validation — 요양원 지하 5층 (B1 하역장 · B2 기록보관소 · B3 기계실 · B4 저탄장 · B5 저수조): PASS
```

All **16 of 16** rules `[ok]`: straight-corridor, open-adjacent-to-maze,
s-corridor-per-zone, loops, dead-ends, floor-materials, observation-posts,
lockable-doors, candidate-sites, zone-entry-points, concealment-near-exit,
zone-count, zone-diagonal, map-extent, connectivity, zone-membership. Selected
measurements, verbatim:

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

More passages means more corners, and nothing in the sixteen rules constrains corner
*density*. So the map passes the entire checklist and fails the one grade §12 gives
it. Written up as [F-007](BALANCE-FINDINGS.md#f-007) with the named node chains to
straighten; it is the clearest open piece of map work.

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

### 1.9 The full Unity suite — 112 of 112, and one seam it caught

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
EditMode   total 70 passed 70 failed 0 result Passed
PlayMode   total 42 passed 42 failed 0 result Passed
```

**112 of 112, against 93 of 94 last pass.** EditMode grew 52 → 70 and PlayMode
41 → 42 as the four parallel passes added tests. Two things changed to get here and
only one of them was work:

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

### 2.3 The economy — matches are 2.5 minutes long

```bash
dotnet run -c Release --project /Users/doogi/horror-game/core/HorrorGame.Sim -- run --matches 500 --seed 1
```

Exit 0, and the numbers are the problem:

```
§01 match length — target 25~35 min
  median                                             2.5 min
  p10 / p90                                          1.3 min / 7.9 min
  inside the window                                  0.6%

§07 threat curve
  mean tier at end (0=초저녁 … 4=동트기 전)                 0.12
  reached 심야 or later                                1.2%
  chases per match                                   5.19
  chases broken                                      59.6%
  deaths per match                                   2.1

§02 outcome mix
  완전 승리 10.6% · 부분 승리 53.2% · 생존 14.6% · 패배 21.6% · objective recovered 63.8%
```

**0.6 % of matches land in §01's window and 1.2 % ever reach 심야.** Three of §07's
five threat tiers are dead content. [F-006](BALANCE-FINDINGS.md#f-006).

### The map grew and these numbers did not change at all

Not "barely moved" — **byte-identical to the run taken before the building went from
three storeys to five.** Same median, same p10/p90, same 1.2%.

That is not a coincidence and it is not a bug in the simulator. It is a coupling that
does not exist: `core/HorrorGame.Sim/SimMap.cs` builds **its own four-zone ring map**
out of `GameConstants` (`ZoneDiagonalMin`/`Max`, `CandidateSitesPerZone`) and never
reads `FirstMapSketch`. `git diff` confirms `core/` and `GameConstants.cs` are
unchanged by the pass that grew the Unity map. The two buildings are separate
artifacts that both cite §12.

F-006's own option 1 is *"make traversal cost real time — larger maps"*. A larger map
was built, in Unity, and the simulator cannot see it. **So F-006 is not just still
open, it is untested by the work aimed at it.** The next move is either to grow the
constants `SimMap` derives its geometry from, or — better — to have `SimMap` consume
the same `MapGraph` `FirstMapSketch` emits, so that "the map got bigger" and "matches
got longer" become one measurement instead of two unrelated ones. Both sides already
speak `MapGraph`.

Note also the collision with §1.3: `MonsterChaseTests` pins §07 to 심야 to measure
against §06's 4.8 m/s, and the simulator says a real match reaches 심야 1.2 % of the
time. The chase numbers are correct for the tier they are measured at, and that tier
is one players almost never see. Fixing F-006 is what makes §1.3's numbers the numbers
of the game rather than of a scenario.

---

## 3 · Every known defect, with a pointer

| # | Defect | Where | Kind |
|:--:|---|---|---|
| 3.1 | ~~`SoloMatchLoopTests` red on a Mirror package-cache `.meta`~~ **not reproducing** — dormant, not fixed | [B-002](BLOCKERS.md#b-002) | environment |
| 3.2 | Two `HallOpen20x20` rooms dropped at `LogError` on every generation | [B-003](BLOCKERS.md#b-003) · `MapSketch.cs:1101` | design intent lost |
| 3.3 | ~~The monster is invisible past ~8 m~~ **fixed** — all 8 staged frames pass, 15 m contrast 0.0592 against a 0.015 floor | §4.1 | art |
| 3.4 | §12's 15–25 m 시야 차단 spacing rule violated, reported not enforced | [ART.md §7.2](ART.md) · `Sightlines.cs:174` | design rule unmet |
| 3.5 | **Worse.** Four zone-view misses against ART.md's bands, was one; zone A wood 40.6 % crushed, 25.9 % legible, median 2.9 | §4.3 · [ART.md](ART.md) | art |
| 3.6 | `TESTING.md`'s suite command has `-quit` and so reports nothing | §1.9 | documentation |
| 3.7 | `TESTING.md` quotes EditMode 55 / PlayMode 27 and NavMesh 630; they are **70 / 42** and **1830** | §1.5, §1.9 | documentation |
| 3.8 | No test asserts a non-`None` floor material on **generated** geometry | §3a below | test gap |
| 3.9 | Gravel/concrete clarity is inverted against measured loudness | [F-002](BALANCE-FINDINGS.md) | gameplay |
| 3.10 | Matches end in 2.5 min; §07 tiers 2–4 unreachable — **and the simulator cannot see the enlarged map**, so growing it did not test this | [F-006](BALANCE-FINDINGS.md#f-006) | gameplay |
| 3.11 | Weight table is a cliff at band 2, not a gradient | [F-001](BALANCE-FINDINGS.md) | gameplay |
| 3.12 | Runner sprint-timing dilemma cannot exist at these numbers | [F-004](BALANCE-FINDINGS.md) | gameplay |
| 3.13 | §12 states two loop rules; only one can ever bind | [F-005](BALANCE-FINDINGS.md) | design |
| 3.14 | ~~The 12 m monster shot photographs the end wall~~ — the staged rig has clearance and all four distances read | §4.1 | tooling |
| 3.15 | Every room is the same room above knee height; the 개방 공간 are near-undressed boxes | [ART.md §7.3](ART.md) · §4.4 | art |
| 3.17 | Map passes 16/16 §12 rules and grades **10/10 TooEasy**, out of the 5–7 band — was 7/10 | [F-007](BALANCE-FINDINGS.md#f-007) · §1.6 | gameplay |
| 3.18 | Settings screen's 해상도 row reads `640 × 480` in a batch shot, so the real default is unconfirmed | §4.2 | ui |
| 3.16 | `map_overhead.png` is still a blue rectangle, not a map | §4.4 | tooling |

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

### 4.3 The map is measurably darker than the smaller building it replaced

Measured with `tools/render/frame_stats.py` on `Shots/map_Zone_*.png` against
[ART.md](ART.md)'s targets — 10–40 % crushed, 30–75 % legible, median 3–16,
blown < 0.5 %:

```
shot                                     mean    p50    p90    p99  black%  legible%  blown%    sat
map_Zone_A_B2_Wood.png                    6.9    2.9   15.8   62.8    40.6      25.9    0.00    5.6
map_Zone_B_B5_Tile.png                    7.5    3.4   17.0   63.7    36.1      29.2    0.00    7.9
map_Zone_C_B4_Gravel.png                  7.4    3.3   17.5   69.2    38.0      26.6    0.00    7.3
map_Zone_D_B1_Concrete.png                9.2    6.2   18.9   59.5    17.7      40.4    0.00   10.8
map_Zone_E_B3_Metal.png                  12.7    8.4   31.1   49.9    17.0      52.5    0.00   14.6
```

**Four misses where the three-storey map had one**, and the same command and
viewpoints produced both:

| Band | Was (`real8_*`) | Now (`map_*`) | |
|---|---|---|:--:|
| crushed 10–40 % | 10.4–37.4 % | 17.0–**40.6 %** | zone A over |
| legible 30–75 % | 28.4–54.8 % | **25.9**–52.5 % | A, B, C under |
| median 3–16 | 3.9–9.1 | **2.9**–8.4 | zone A under |
| blown < 0.5 % | 0.00 % | 0.00 % | ok |

Every zone lost legible fraction and every median fell — a consistent direction, not
noise. The grade was not retuned when the building doubled, so the same ambient and
fog now light rooms that are further apart and more often outside a practical's
falloff. Zone A (wood, B2) is worst on all three moving measures.

### 4.4 What still does not hold up

**The rooms are empty.** `map_Zone_D` and `map_Zone_E` are large textured boxes with a
pillar and a pool of light in them. The corridors carry the game — `map_spawn0` and
`map_spawn3` have skirting, wall panels, ceiling beams, conduit and a crate, and they
look like a horror game — but the open spaces have almost no set dressing, and §12
requires one 개방 공간 per zone. A player will spend real time in those.

**`map_overhead.png` is still useless** — a small flat blue rectangle, the roof of the
top storey seen from above. Same shot-rig bug as the previous two passes.

**The guidance overlays are developer instrumentation, not UI.** That is what they are
for, and they work; worth stating only so nobody mistakes `guide_*.png` for the
player-facing HUD. The §14 panel in `guide_underground.png` prints F-006 against
itself in red: *"아직 물을 수 없습니다 — 한 판 중앙값 2.5분, §01 목표는 25~35분"*.

### 4.5 The verdict — could this be sold?

**The menu could ship tomorrow. The corridors could ship after a dressing pass. The
open rooms could not, and the game as a whole could not — but not for visual
reasons.**

Nothing here looks like a prototype in the way prototypes usually look: the materials
are consistent, the fog and grading are coherent, the monster is a creature rather
than a capsule, and the interface is written by someone who knows what the settings
are for. Someone shown `menu_main.png` and `map_spawn0.png` would believe it was a
commercial product.

What stops it is that a buyer would then play it, and a match would end in two and a
half minutes without the threat curve ever starting (§2.3), against a map every
sampled runner escapes from (§1.6). **The look is ahead of the game.** The honest
next spend is not more art — it is F-006, and the zone-A luminance and the open-room
dressing after it.

---

## 5 · Built but unverified

- **Networking.** Mirror, Steamworks.NET and FizzySteamworks are installed and the
  transport is wired; `NetTests` passes in PlayMode. No two-instance session has been
  run in this pass. §14 step 2 is `HorrorGame ▸ Play ▸ Launch Two Instances`.
- **A player build.** `dist/` contains logs and test results and **no player
  executable** — no build has been produced from this working copy. macOS cannot
  produce an IL2CPP Windows player, only Mono; a shipping build needs a Windows
  machine or `.github/workflows/unity.yml`.
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
- **§14 Q3 「지금 나갈까?」** — **cannot be asked.** F-006: a 2.5-minute match never
  builds the pressure the question is about.
- **§14 Q4 「6이었나 9였나」** — the confusion pairs are implemented and tested; whether
  they produce the argument is a human question.
- **§14 Q5 청음사 방향·거리** — headphones required, and expect it to work close and
  fail far: 2.13× dry, 1.396× at 25 m through a wall.
- **Art above knee height.** Five zones, five floors that genuinely read, and the same
  brick wall and central pillar in all of them. [ART.md §7.3](ART.md).
- **A monster you can see at 12 m.** §4.2.
- **Version control.** The repository has exactly one commit, `ef45b18 fix: init`,
  with **197 changed or untracked paths** on top of it — including all of the stair
  and monster work and every number in this document. None of it is committed.

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
| The match lasting long enough to matter | it will not — ~2.5 min | §2.3 |

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

dotnet test  core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj      # §1.1  448/448
dotnet build core/HorrorGame.sln -c Release                               # §1.2  0 errors
$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log
grep -cE '^Assets/.*error CS' /tmp/u.log                                  # §1.2  0

$U -batchmode -projectPath $P -runTests -testPlatform PlayMode \
   -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log   # §1.3
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SoloPlaytest.VerifyBatch -logFile /tmp/solo.log # §1.4
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log                 # §1.5
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu \
   -logFile /tmp/quality.log                                                             # §1.6
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log   # §1.7

# §2.1 — NO -quit. The runner exits from its own callback.
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine -logFile /tmp/t2.log

tools/audio/.venv/bin/python tools/audio/verify_audio.py                                 # §2.2  FAIL
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1        # §2.3

# §4 — WITHOUT -nographics, or every shot is black.
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
   -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag verify -logFile /tmp/shot.log
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.Gameplay.MonsterEditor.MonsterShot.Batch \
   -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag verifymon -logFile /tmp/mon.log
cd unity/HorrorGame/Shots && python3 ../../../tools/render/frame_stats.py 'verify_Zone_*.png'
```

Expected exit codes: **0** everywhere except the full test suite (**6** — one failure,
B-002) and `verify_audio.py` (**1** — one blocking defect, F-002).

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
