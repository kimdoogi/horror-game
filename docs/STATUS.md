# Project status

Where this game actually stands, on 2026-07-31.

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

**The game has an antagonist now.** [B-001](BLOCKERS.md#b-001) is closed: the monster
walks 133.9 m from its B3 spawn, up two storeys, and reaches a player in 27.52 s at
4.83 m/s. §14 says that question decides the project, and it is the only reason this
document reads differently from the last one.

Everything else is roughly where it was. One test is red (B-002, an environment
problem). The economy still resolves a match in 2.5 minutes against a 25–35 minute
design target (F-006), so §14's question 3 remains unaskable. **Nobody has yet sat
down with two instances and played it**, which is now the highest-value thing anyone
can do here and cannot be automated.

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
경과 시간: 00:00:03.69
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
[ChaseTest]   route            133.9 m of NavMesh path, monster spawn → (33.75, 0.18, 71.25)
[ChaseTest]   straight line    60.1 m
[ChaseTest]   chase entered    27.12 s
[ChaseTest]   reached          27.52 s
[ChaseTest]   closing speed    4.83 m/s of route, against §06's 4.8 m/s of ground speed
[ChaseTest]   worst 1 s rise   0.0 m of route (0 is a monster that never backtracked)
```

`MonsterSpawn` is at `(36.25, -7.50, 11.25)` on B3 and the player at
`(33.75, 0.00, 71.25)` on B1, so that route climbs 7.5 m across two storey
boundaries. All four tests' full output is in
[BLOCKERS.md B-001](BLOCKERS.md#b-001); the two numbers worth repeating here are
**4.80 m/s of corridor against §06's 4.8** and **0.80 m/s of gap opened while
sprinting against §06's 0.8**, which is the design's central speed claim —
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
  §03 layout varies per seed: objective moved yes, clue set changed yes
  placed 4 clues, 1 objective, 10 pocketable loot, 1 oversize, 1 safe   (planned round trips 4)
  §08 picked up 회중시계 · 반지 — weight 1/10, speed ×1.00
  §04 safe: refused 주자, opened for 정비공, 문서 taken
  §03 objective refused while holding loot: §03 전리품 동시 소지 불가 — 들고 있는 전리품을 먼저 처리해야 한다.
  §03 read cancels when the light goes — progress reset to zero
  §03 read completed and the overlay drew: "ㅁ-4 우"
  §01 descended — §07 clock hidden, 4.3s elapsed
  §03 partial reset — monster back at spawn (was 15.7 m away), clock untouched
  §08 sold on arrival — team wallet 65 credits
  §08 shop open at the vehicle, cheapest item 15 credits
  §03 objective taken — no flashlight, no loot, speed ×1.00
  §02 FullVictory — escaped 1, lost 0, clock 6.9s
  §02 Survived — information kept without the objective
PASS — §01's loop ran end to end.
```

This is the same code path as the one red test in §2.1. It is red inside the test
harness and green outside it, which is what "environment problem" means here.

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
  markers          36
  pairs            630
  complete         630 (100.0 %, need 98 %)
  partial          0
  invalid          0
  islands          1
  worst snap       0.44 m  (CandidateSite_B 저수조_14)
  monster reach    19/19 player spawns and 후보 지점 reachable from MonsterSpawn (§06)
```

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
§12 map validation — 요양원 지하 (B1 하역장 · B2 기계층 · B3 저수조): PASS
```

All **16 of 16** rules `[ok]`: straight-corridor, open-adjacent-to-maze,
s-corridor-per-zone, loops, dead-ends, floor-materials, observation-posts,
lockable-doors, candidate-sites, zone-entry-points, concealment-near-exit,
zone-count, zone-diagonal, map-extent, connectivity, zone-membership. Selected
measurements, verbatim:

```
Longest unbroken sight line is 20 m, inside §12's 20 m limit.
Independent 순환로: 12 map-wide (need 3+).
막힌 길: 16 of 74 places = 21.6% (§12 band 20%~25%).
Distinct and non-overlapping: D 하역장=Concrete, A 기록보관소=Wood, C 저탄장=Gravel,
  E 기계실=Metal, B 저수조=Tile.
5 zones, inside §12's 4~6.   Footprint 60 m × 65 m, inside §12's 100 m square.
One walkable piece, 74 places, 85 passages.
```

And the grade:

```
§12 주자 테스트 — 요양원 지하 (B1 하역장 · B2 기계층 · B3 저수조): 7/10 (70%), Balanced
  적정 (§12). Breaking aggro is possible from most of the map and never free.
```

**7/10, at the top of §12's 5–7/10 band.** Three of the ten sampled routes end
`CAUGHT`, all three descending into zones C and E, all three reporting *"No
sight-breaking corner was ever rounded. §12 asks for 3~4 chances inside that
distance; this route offered none that held."* That is a real weakness in the lower
storeys, and it is inside tolerance rather than outside it.

### 1.7 Asset import settings

```bash
… -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log
```

```
[AssetImport] Audio import settings: 166 inspected, 0 excluded by marker, 0 failing, 0 warnings.
[AssetImport] Model import settings: 85 inspected, 0 excluded by marker, 0 failing, 0 warnings.
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

---

## 2 · Verified red

### 2.1 The full Unity suite — 93 of 94

```bash
# NOTE: no -quit. The runner is async and exits from its own callback; -quit kills it
# before any results are written. TESTING.md's version of this command has -quit and
# therefore silently reports nothing — that is defect 3.6.
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -nographics -silent-crashes -projectPath /Users/doogi/horror-game/unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine -logFile /tmp/t2.log
```

Exit code **6**.

```
[TestRunner] Summary
  editmode    52 passed     1 failed     0 skipped     0 inconclusive  in 1.5s
  playmode    41 passed     0 failed     0 skipped     0 inconclusive  in 56.5s
  total: 94 (93 passed, 1 failed, 0 skipped, 0 inconclusive) in 61.2s
[TestRunner] Failed: HorrorGame.Gameplay.MatchEditor.SoloMatchLoopTests.Solo_match_runs_the_whole_round_trip
```

The one failure is [B-002](BLOCKERS.md#b-002): a missing `.meta` in the Mirror
package cache raises a `Debug.LogError` inside `AssetDatabase.Refresh()`, and the
harness fails any test that logs an unexpected error. The loop itself passes — §1.4.

PlayMode is 41 tests, not the 27 older docs claim; the four `MonsterChaseTests` and
the audio scene suite are new.

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
five threat tiers are dead content. [F-006](BALANCE-FINDINGS.md).

Note the collision with §1.3: `MonsterChaseTests` pins §07 to 심야 to measure against
§06's 4.8 m/s, and the simulator says a real match reaches 심야 1.2 % of the time. The
chase numbers are correct for the tier they are measured at, and that tier is one
players almost never see. Fixing F-006 is what makes §1.3's numbers the numbers of
the game rather than of a scenario.

---

## 3 · Every known defect, with a pointer

| # | Defect | Where | Kind |
|:--:|---|---|---|
| 3.1 | `SoloMatchLoopTests` red on a Mirror package-cache `.meta` | [B-002](BLOCKERS.md#b-002) | environment |
| 3.2 | Two `HallOpen20x20` rooms dropped at `LogError` on every generation | [B-003](BLOCKERS.md#b-003) · `MapSketch.cs:1101` | design intent lost |
| 3.3 | The monster is invisible past ~8 m | §4.2 | art |
| 3.4 | §12's 15–25 m 시야 차단 spacing rule violated, reported not enforced | [ART.md §7.2](ART.md) · `Sightlines.cs:174` | design rule unmet |
| 3.5 | Zone C over-crushed, zone D under-crushed, both out of ART.md's band | §4.2 | art |
| 3.6 | `TESTING.md`'s suite command has `-quit` and so reports nothing | §2.1 | documentation |
| 3.7 | `TESTING.md` says PlayMode 27 (it is 41); `ART.md` says 16–39 % crushed (it is 8.8–43.8 %) | §2.1, §4.2 | documentation |
| 3.8 | No test asserts a non-`None` floor material on **generated** geometry | §3a below | test gap |
| 3.9 | Gravel/concrete clarity is inverted against measured loudness | [F-002](BALANCE-FINDINGS.md) | gameplay |
| 3.10 | Matches end in 2.5 min; §07 tiers 2–4 unreachable | [F-006](BALANCE-FINDINGS.md) | gameplay |
| 3.11 | Weight table is a cliff at band 2, not a gradient | [F-001](BALANCE-FINDINGS.md) | gameplay |
| 3.12 | Runner sprint-timing dilemma cannot exist at these numbers | [F-004](BALANCE-FINDINGS.md) | gameplay |
| 3.13 | §12 states two loop rules; only one can ever bind | [F-005](BALANCE-FINDINGS.md) | design |
| 3.14 | The 12 m monster shot photographs the end wall, not the monster | §4.2 | tooling |
| 3.15 | Every room is the same room above knee height | [ART.md §7.3](ART.md) | art |
| 3.16 | `verify_overhead.png` is a blue rectangle, not a map | §4.2 | tooling |

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
and every shot comes out black:

```bash
… -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag verify -logFile /tmp/shot.log
… -executeMethod HorrorGame.Gameplay.MonsterEditor.MonsterShot.Batch \
  -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag verifymon -logFile /tmp/mon.log
```

Both exit 0 — 10 map shots and 15 monster shots into `unity/HorrorGame/Shots/verify*`.
Measured with `python3 tools/render/frame_stats.py` against [ART.md](ART.md)'s own
targets: 10–40 % crushed, 30–75 % legible, median luminance 3–16, blown < 0.5 %.
Compared against `Shots/final_*.png` (09:13–10:28) and `Shots/chase_*.png` (11:35).

### 4.1 What genuinely improved

**The stairwell is now a place.** `Shots/verify_spawn2.png` shows diamond-plate
treads, a mid-landing and headroom, lit and walkable. The previous pass had no
comparable shot because there was no comparable geometry — only a `NavMeshLink`
across a gap. This is what B-001's fix looks like from the inside.

**The corridor is finally in its own luminance band.** The monster corridor measured
mean 41.0 / median 37.7 in `final_Chase_*.png`; it now measures mean 15.0 / median
14.8 in `verifymon_Chase_*.png`, against ART.md's median target of 3–16. The older
`final_*` shots were roughly 2.7× too bright and out of band. This is a correction,
not a regression.

**The monster reads as a creature at close range.** At 3 m the new silhouette is
bulky and hunched, with a plated skull and heavy hanging forearms; `chase_Chase_3m.png`
was an articulated shop mannequin with visible segment joints. That is a real
improvement, and 3 m is the only distance at which it is one.

### 4.2 What did not improve, stated plainly

**Past 8 m the monster is not there.** This is the honest headline of the render pass,
and the rebuild did not change it:

| Shot | mean | p50 | p99 | `resolve` (the tool's own score) |
|---|---:|---:|---:|---:|
| `chase_Chase_8m.png` (before) | 15.0 | 14.8 | 45.7 | — |
| `verifymon_Chase_8m.png` (after) | **14.9** | **14.8** | **45.6** | **0.70** |
| `chase_Chase_12m.png` (before) | 15.0 | 14.8 | 48.3 | — |
| `verifymon_Chase_12m.png` (after) | **15.0** | **14.8** | **47.8** | **0.45** |

The after-frames are statistically indistinguishable from the before-frames, and both
are indistinguishable from an empty corridor. Looking at `verifymon_Chase_12m.png`
directly: there is no creature visible in it. Whatever the rebuild changed, it did not
change what the player sees at the distance a chase is decided at.

**At 3 m the new monster is darker than the old one** — p99 86.7 → 69.4, mean 15.8 →
15.3. Better silhouette, less light coming off it.

**The 12 m shot is measuring shadows.** `changed=70.180% px=646781` on every 12 m
pose, in all six §06 states. The lane is 13.5 m clear so the rig clamps to 12 m and
stands the creature in the end wall; 70 % of the frame "changing" is every shadow in
the corridor moving, not a silhouette. `MonsterShot.cs` says so itself. Until there is
a 20 m run to shoot down, the 12 m column is not evidence of anything.

**Two zone views drifted out of ART.md's band.**

| Zone view, `final_` → `verify_` | crushed % (target 10–40) | legible % (target 30–75) |
|---|---:|---:|
| `Zone_C_B2_Gravel` | 37.1 → **43.8** ✗ | 32.2 → **29.2** ✗ |
| `Zone_D_B1_Concrete` | 15.6 → **8.8** ✗ | 56.6 → 58.6 ✓ |
| `Zone_A_B1_Wood` | 38.8 → 37.4 ✓ | 30.9 → 32.8 ✓ |
| `Zone_B_B3_Tile` | 16.6 → 16.4 ✓ | 47.7 → 48.3 ✓ |
| `Zone_E_B2_Metal` | 19.3 → 19.1 ✓ | 52.3 → 44.1 ✓ |

Gravel is now too dark to read *and* out the bottom of the legible band; concrete is
no longer dark enough to gate anything, which is §03's whole mechanic. `blown %` is
0.00 on all five. ART.md's claim of "16–39 % crushed and 31–57 % legible" is stale —
the real range is **8.8–43.8 %** and **29.2–58.6 %**.

**`verify_overhead.png` remains useless** — a small flat blue rectangle, which is the
roof of the top storey seen from above. A bug in the shot rig, not a picture of the
map, and equally useless before.

### 4.3 The verdict

The stair rebuild improved the game — measurably, and in the way that mattered most.
The monster rebuild improved the close-up silhouette and changed nothing at the
distances a chase is fought at, while pulling two zone views out of their band. That
is a partial improvement, and calling the monster work finished would be wrong.

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
