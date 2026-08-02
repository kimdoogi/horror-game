# Balance findings

Contradictions found by running the design document's own numbers against each
other. Each entry states the sections that disagree, the arithmetic, and the
options — **not** a decision. Retuning is the designer's call.

Every finding is pinned by a test, so a later edit that changes the answer fails
the build instead of passing silently.

---

## F-001 · The weight table is a cliff, not a gradient

**Sections:** §06 (speeds) × §08 (weight bands) · **Priority:** feeds §16-2, the
document's own stated bottleneck · **Status:** 🔴 open, needs a decision

### The arithmetic

§08 defines four weight bands, which reads as a progressive slowdown:

| Total weight | Multiplier | Runner sprint | vs monster 4.8 |
|:--:|:--:|:--:|---|
| ≤ 5 | 1.00 | 5.60 | **+0.80** — escapes |
| 6–10 | 0.85 | **4.76** | **−0.04** — caught |
| 11–15 | 0.70 | 3.92 | −0.88 — caught |
| ≥ 16 | sprint disabled | — | caught |

`5.6 × 0.85 = 4.76 < 4.8`.

### Why it matters

The Runner's entire identity is §06's "주자만 도망칠 수 있다". It survives on a
+0.8 m/s margin, and the first penalty band removes 0.84 of it. So:

- The moment a Runner carries more than 5 weight, it stops being a Runner.
- Bands 2, 3 and 4 are **identical** from the Runner's point of view — all three
  mean "caught". Three quarters of the table describe one outcome.
- The cliff sits exactly at weight 5, which is the weight of a single 대형
  초상화·궤짝 (§08). Picking up one large piece of loot ends the escape.

§08 intends a dial — "욕심이 곧 속도 저하이고, 속도 저하가 곧 죽음이다" — but the
numbers produce a switch.

### Options

1. **Keep the cliff, own it.** "Drop it or die" is a strong moment, and §08 does
   say greed kills. Then the four bands should be documented as affecting
   *non-Runners* (who cannot escape regardless) and the table's apparent gradient
   should be redrawn so it stops implying something false.
2. **Preserve a slim escape in band 2.** Needs `WeightMulLight > 4.8 / 5.6 =
   0.857`. At 0.90 the Runner sprints 5.04 m/s (+0.24), which is §05's "측면"
   margin — real but demanding. Band 2 then means "you can still get out, but you
   must use the map perfectly."
3. **Give the Runner a load exemption.** Sprint ignores the first 5 weight. Makes
   the Runner the designated hauler, which cuts against §03's "누가 들 것인가"
   being a genuine question.

Option 2 is the smallest change that makes the table mean what it appears to
mean. It is still a design decision.

### Pinned by

`FoundationTests.WeightBands_AreACliffForTheRunner_NotAGradient` — asserts the
cliff. If the weight table is retuned into a gradient, that test fails and this
entry must be updated in the same commit.

---

## F-002 · The Listener's HUD contradicts the player's ears through a wall

**Sections:** §04 (청음사) × §12 (바닥 재질) · **Priority:** 🔴 blocking — the role
misinforms the player · **Status:** open

### What disagrees

`GameConstants.ListenerClarity*` ranks how much each surface gives the monster away.
Measuring the generated footsteps at several degrees of occlusion says otherwise:

| Surface | clarity in code | measured, dry | measured through a wall (600 Hz LPF) |
|---|:--:|:--:|:--:|
| Metal | 1.00 | 0.0 dB | 0.0 dB |
| Tile | 0.85 | −0.7 | −17.2 |
| Wood | 0.80 | −12.7 | −6.3 |
| **Gravel** | **0.70** | −12.4 | **−47.0** |
| **Concrete** | **0.50** | −24.8 | **−14.6** |

Dry, the ranking holds. Through a wall it inverts: **gravel measures 32.4 dB quieter
than concrete**, while the code tells the player gravel is the clearer of the two.

### Why it happens, and why the audio is not the bug

The sounds are physically right. 자갈 "부스럭" (§12) is broadband high-frequency
rustle, and a wall absorbs high frequencies — so gravel nearly vanishes. 콘크리트
"둔탁" is a low thud, and low frequencies pass through walls, so it survives.

§04 makes hearing through walls the *entire point* of the role. So the clarity table
is being applied in exactly the situation it was not authored for: the values were
picked by how the surfaces sound in the same room.

This is worse than either side being wrong on its own. A HUD that disagrees with the
player's ears teaches them to distrust the role, and §04 gives the Listener nothing
else to work with.

### Options

1. **Make clarity a function of occlusion, not a constant.** Physically honest and it
   makes the material map richer: which surface betrays the monster then depends on
   whether a wall is between you, which is a genuinely interesting thing for a
   Listener to learn. Costs a signature change on the ability.
2. **Re-derive the constants from measurement at the occlusion the role usually
   works through**, and document that they are wall-through values. Cheap, and
   removes the lie, but throws away the same-room ordering.
3. **Push the surfaces apart in the low end instead of the high end** so the ranking
   survives occlusion. Changes the generator, and risks making the five surfaces
   less distinct dry — which F-003 shows is already tight.

Option 1 is the only one that stays true at both ends. It is also the one that turns
a defect into a mechanic.

### Update — the mix reproduces the inversion, refuses to hide it, and now holds option 1's missing input

Three things the Unity side can now say about this.

**The inversion arrives for free and is not being papered over.** The shipped clips
already have those spectra, so the low-pass produces the inversion without a table.
Re-measured at the corner the mix actually uses (800 Hz, not the finding's 600 Hz):
자갈 **−25.8 dB**, 콘크리트 **+0.3 dB** — a **26.1 dB** inversion against the finding's
32.4 dB. Gentler corner, same direction, same problem.

**The audio layer deliberately does not apply a second clarity.**
`AudioOcclusion.OccludedLevelChangeDb` records those numbers and drives nothing. A
parallel clarity living in the presentation layer would produce exactly the
HUD-disagrees-with-ears failure this finding is about, one layer further down and
harder to see. The resolution belongs in `ListenerAbility`, and that is a Core change
and a designer's decision.

**Option 1's cost has dropped.** It reads "costs a signature change on the ability",
and the implicit second cost was computing an occlusion term at all. That term now
exists and is already measured per emitter, every 0.1 s, as a 0–1 fraction —
`SoundOccluder.Occlusion`, weighted so a §12 doorway reads 0.5 and a solid wall 1.0
(see F-003's update). Making clarity a function of occlusion is therefore a plumbing
change plus the signature, not new physics.

### Pinned by

`tools/audio/verify_audio.py` section 6 — reports every inversion between the clarity
the code claims and measured audibility. Currently one blocking inversion.

`AudioTests.OccludedAudibility_InvertsTheClarityTable_AsF002Reports` fails if anyone
"fixes" the finding by quietly changing the mix instead of the rule.

---

## F-003 · The five-surface alphabet holds in the room, not at range through a wall

**Sections:** §12 (바닥 재질) × §04 (청음사) · **Priority:** 🟡 needs a mix decision
· **Status:** open

### Measured

§12 requires the Listener to tell zones apart by floor material. Taking 1.4×
spectral-centroid separation as the threshold for "reliably distinguishable":

- **Dry / same room: PASSES.** Worst pair metal vs tile at **2.13×**; worst pair
  within a single actor 1.98×.
- **At 25 m through a wall: FAILS.** Worst pair wood vs metal at **1.396×**.

So the alphabet is legible where the Listener needs it least and illegible where the
role actually operates.

### Why this is not a generator defect

Every surface passes on its own, and the dry separation is comfortable. What closes
the gap is occlusion removing the high-frequency content the surfaces differ in.
Generating "brighter" footsteps cannot fix it, because the brightness is what the
wall removes.

The answer is therefore in the Unity mix rather than in synthesis:

1. **Tune the occlusion filter and 3D rolloff** so the cue degrades gracefully — the
   Listener should lose *precision* with distance, not lose the *identity* of the
   surface.
2. **Give each surface a low-frequency signature** that survives a wall, so identity
   lives below the occlusion corner and precision lives above it.
3. **Accept it as the role's range limit** and make that limit legible in play, so a
   Listener learns "I have to get closer to tell which floor that is." §10's dilemma
   map would welcome another entry: better information for more exposure.

Option 2 composes with F-002's option 1 and would resolve both at once.

### Update — option 1 is built, and it removes the failure condition

Option 1 has now been implemented in Unity and measured against the engine's own
filter rather than an offline model. Two things came out of it, and neither closes the
finding on its own.

**1. The corner is clamped, so the alphabet never actually reaches the failing case.**
The finding's 1.396× was measured by sweeping the low-pass toward zero.
`AudioOcclusion.CutoffHz` does not: it stops at
`AudioTuning.ListenerChannelOcclusionFloorHz`, chosen as the lowest corner from which
*every* higher corner also clears 1.4× with 5% margin. Under the engine's filter
(one biquad, 12 dB/oct) that floor is 796 Hz and 800 is the round number above it,
where the worst pair reads **1.476×**.

**The two numbers are the same measurement through different filters.**
`verify_audio.py` measures through `butter(order=2) + filtfilt`, which is zero-phase
and therefore ~24 dB/oct — twice the engine's slope. The same 800 Hz corner reads
1.396× there and 1.476× here; under the steeper model the floor would be 873 Hz.
The engine is what the player hears, so the mix is built on the engine's curve and
the verifier's figure is the conservative one. Neither is wrong, and the gap between
them is the whole margin — which is why this stays 🟡 rather than closing.

**2. Occlusion is a fraction, and §12's map is mostly the middle case.**
`SoundOccluder` casts the direct ray plus a ring of four and weights them half and
half, so the same geometry the map is specified in resolves to three situations, not
one:

| Situation | measured occlusion | resulting corner |
|---|:--:|:--:|
| Clear line of sight | `0.00` | 22 000 Hz |
| §12's 구역 간 진입점 — clear down the middle, blocked at the edges | `0.50` | **4 195 Hz** |
| Solid wall across the whole aperture | `1.00` | **800 Hz** |

The corner interpolates geometrically, so the doorway case is `√(22000 × 800)`. The
sweep's two bracketing rows — dry at 2.030 and 3 000 Hz at 1.707 — put that case
comfortably above 1.7×. §12 connects every pair of zones through two or three entry
points and builds the map out of S-corridors and 순환로, so **the common case has
roughly twice the margin the finding's single "through a wall" figure suggests**, and
the tight case is a floor slab or a long blank run of wall.

That makes **option 3 a much smaller concession than it looked.** The limit a Listener
would have to learn is not "further than N metres" but "when there is a whole wall in
the way" — something they can see and reason about, and something §12's own geometry
tells them is rare. Option 2 remains the only one that buys margin at `1.00` and still
composes with F-002's option 1. The decision is unchanged and still the designer's;
what has changed is the price of each answer.

The rolloff half of option 1 is built too: `AudioTuning.RolloffExponent` is 0.6
(3.6 dB per doubling) rather than free-field's 1, derived from requiring a footstep to
stand above the zone bed at 25 m and fall under it by 40 m. That is the "lose
precision, not identity" shape this finding asks for.

### Pinned by

`tools/audio/verify_audio.py` sections 2 and 5 — the 5×5 separation matrix, run dry
and at each occlusion step.

`AudioTests.ADoorway_ReadsAsHalfOccluded_NotAsAWall` pins the 0.5 above, and
`AudioSceneTests.AWallBetweenTheMonsterAndTheEars_LowersTheFilterCorner` measures the
whole chain in a live scene: it builds a wall between an emitter and the ears and
asserts the corner lands on the floor and not below it.

---

## F-004 · The Runner's sprint-timing dilemma cannot exist at these numbers

**Sections:** §06 (주자의 진짜 딜레마) × §05 (질주 최대 이동 거리) × §12 (실전 검증)
· **Priority:** 🔴 a named skill expression is unreachable · **Status:** open

### The arithmetic

```
sprint capacity   = RunnerSprintSpeed 5.6 m/s × SprintStaminaSeconds 12 s = 67.2 m
route reach       = SprintMaxTravelDistance                               = 60.0 m
```

The bar outlasts the route by 7.2 m. A Runner can therefore sprint from the moment
aggro lands until it breaks, every time, on every route the map grader considers.

### Why it matters

§06 devotes a subsection to this exact decision:

> **주자의 진짜 딜레마 — 질주를 언제 쓸 것인가**
> 처음부터 질주 → 거리는 벌지만 차단 지점 도달 전에 소진
> 아껴두면 → 그 사이에 잡힐 수 있음
> **맵을 알아야 최적화된다.** 실력이 개입하는 지점이고, 그래서 반복해도 재밌다.

The premise of that paragraph is that the sprint runs out before the cover does. It
does not. "Spend it immediately" dominates at every instant, so:

- there is no timing decision, and no skill to learn
- `RunnerTest` evaluates hold-until-corner-*k* strategies that can never win, so
  `RunnerTestAttempt.SprintDelaySeconds` is always zero and every route pays for N
  discarded simulations
- most seriously, **a map cannot be graded on sprint timing**, so §12's 실전 검증
  is blind to the very thing §06 says makes maps worth learning

### Options

1. **Shorten the stamina bar** below the route reach. At 10 s the capacity is 56 m
   against 60 m of route, and the decision becomes real. §06 currently pairs 12 s
   with a 20 s recovery, so this also lengthens the vulnerable window.
2. **Lengthen the routes** so cover sits further apart than one bar. This pushes on
   §12's 15–25 m cover spacing, which is derived from these same numbers — expect it
   to cascade.
3. **Accept it** and delete the dilemma from §06, along with `RunnerTest`'s dead
   strategy search. Honest, cheaper, and loses a stated pillar of replayability.

Option 1 is the smallest change that makes §06's paragraph true.

### Pinned by

`MapTests.RunnerTest_SpendingTheSprintAtOnce_DominatesHoldingIt` — fails the moment
§05's 60 m or §06's 12 s moves, so whichever way this is resolved, the test notices.

---

## F-005 · §12 states two loop rules and only one can ever bind

**Sections:** §12 (순환로 개수) · **Priority:** 🟢 redundant rule · **Status:** open

```
ZoneCountMin 4 × LoopsPerZoneMin 1 = 4   ≥   LoopsTotalMin 3
```

Any map legal under the per-zone rule already has at least four loops, so the
map-wide minimum of three is unreachable as a binding constraint. §12 presents them
as two requirements — "순환로 개수: 구역당 1+, 전체 3+" — but the second can never
fail on its own.

Harmless today. It matters if the per-zone rule is ever relaxed, because there would
then be nothing underneath: raising `LoopsTotalMin` above 4 is what would make it a
real floor.

### Also worth knowing — the checklist is necessary, not sufficient

§12's 첫 맵 스케치 passes all seventeen validator rules and still grades **10/10
TooEasy** on 실전 검증. Passing the checklist means a map is not broken; it does not
mean it is good. §12 already implies this by specifying both, but it is easy to read
the checklist as the finish line.

Pinned by `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`.

---

## F-006 · Matches finish in 7.2 minutes against §01's 25~35 — and what ends them is the battery, not the clock

**Sections:** §01 (한 판의 흐름) × §07 (시간 = 위협도) × §08 (경제) × §03 (배터리)
· **Priority:** 🔴 highest — it invalidates the tuning of everything downstream
· **Status:** open, and for the first time this entry ends in a recommendation rather than a list
· **Source:** 500 matches on 요양원 지하 5층 itself (seeds 1–500), plus five swept axes at
300–400 matches per point over identical seeds
· **Measured:** 2026-08-01, 03:43–07:10

> ### ⚠ Re-run every number below once F-007's reshape lands
>
> These matches were run on the building the game shipped this morning — 164 places,
> 180 passages, 217.5 m, seed 1204, verified identical in the banner of every run and
> against [STATUS.md §1.6](STATUS.md). **At 04:11, mid-sweep, a seventeenth `MapValidator`
> rule landed from [F-007](#f-007)'s pass and this building now fails it:**
>
> ```
> dotnet run -c Release --project core/HorrorGame.Sim -- validate
> §12 map validation: failed [sight-break-spacing]      → exit 6
> ```
>
> Nothing in the numbers below moved — the graph is unchanged and
> `replay --seed 42 --times 3` still gives `PartialVictory, 11.19 min, 1115 credits earned,
> 11 clues read`, exactly as it did before the rule existed. But `MapSceneGenerator` will
> now refuse to write that scene, so **the building is going to change**, and when it does
> every figure on this page changes with it. The commands are in §8; run them, do not
> re-read this.

---

### Read this part first: it was measured against the wrong building for weeks

`core/HorrorGame.Sim/SimMap.cs` used to build **its own** four-zone ring out of
`GameConstants` and never read the level:

```
§12 첫 맵 스케치 (sim): 4 zones, 38 nodes, 47 edges, 10 loops
sites 12  loot 9  safes 2  monster spawn 52 m from the door
§12 validation PASS
```

Against the shipped building: **38 places against 164, 47 passages against 180, and the
monster spawning 52 m from the door against 217.5 m.** Both maps pass §12's core
rules. They are two different buildings that both cite §12. So when the Unity level grew
from three storeys to five and this document reported the simulator's numbers as
"identical to three significant figures", that was not a coincidence and not a bug — the
grown map was never in the measurement.

`SimMap` now calls `FirstMapSketch.Build(seed)`, the same call `MapSceneGenerator` makes
before it lays a single FBX, compiled into the simulator rather than exported to it. The
first five lines of every population report are the building's own §12 census and must
reproduce [STATUS.md §1.6](STATUS.md), which measures the same census from the Unity
scene with a different tool. **The day they stop agreeing, this finding is being measured
against the wrong map again.**

**That banner earned itself again tonight.** Halfway through these sweeps it flipped from
`§12 validation PASS` to `§12 validation FAIL` while every census line stayed
byte-identical — a seventeenth `MapValidator` rule landing from [F-007](#f-007)'s pass,
not a change of building. Every number below is from the same 164-place, 180-passage,
217.5-metre building; without the banner that would have been a guess.

---

### 1 · The zero point — 500 matches, seeds 1–500

```
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1
```

```
§01 match length — target 25~35 min
  median                                             7.2 min
  p10 / p90                                          4.2 min / 32.4 min
  inside the window                                  15.8%
  hit the sim's 40-min cap                           0.0%
  ended with every light dead                        40.6%
  median of the rest                                 17.1 min
  inside the window, of the rest                     26.6%
  best 10-min window this population offers          2.8 min~12.8 min holds 58.2%

§07 threat curve
  mean tier at end (0=초저녁 … 4=동트기 전)                 1.12
  reached 심야 or later (tier 2, 16 min)               33.6%
  reached 새벽 or later (tier 3, 24 min)               17.4%
  reached 동트기 전 (tier 4, 32 min)                     13.0%
```

Unchanged from the 2026-08-01 03:20 run, byte for byte, after the ledger below was added.
The bigger map is what moved this from 2.5 min / 0.6% / 1.2%; that half of the finding is
settled and the before-and-after table is at the end of this entry.

**The one line to carry forward is the last one.** §01 asks for a 10-minute window and
names it 25~35. The 10-minute window this population actually offers is **2.8~12.8 min**,
and it holds 58.2%. The game is not a 25-minute game that finishes early. It is a
7-minute game.

---

### 2 · A new instrument: what §07 prices, against what the simulator charges

§07 is the only section that writes down what an action is supposed to *cost*:

| 행동 | 비용 |
|---|:--:|
| 한 층 더 탐색 | ~3분 |
| 나가서 배터리 교체 | ~1분 |
| 전리품 하나 더 줍기 | ~40초 |
| 상점에서 고민 | ~30초 |

Nothing in this project had ever measured what the simulator charges for the same four
things. `SimTimeLedger` now does: every fixed step of every living player lands in exactly
one of seven buckets, and the counters underneath are the denominators.

```
§07 행동 · 비용 — what the design prices, and what these agents spent
  후보 지점 searched / 전리품 lifted / 왕복                   13.42 / 21.06 / 4.01 per match
  한 층 더 탐색 (§07 ~3분 → ~60s per 후보 지점)                46.54s measured   ×1.29 to reach §07
  전리품 하나 더 줍기 (§07 ~40초)                             25.12s measured   ×1.59 to reach §07
  나가서 + 상점, match seconds (§07 ~1분 + ~30초)           8.02s measured   ×11.23 to reach §07
  …and the walk out to reach the door, per player    105.97s measured   ×0.57 to reach §07
  agent-seconds: 단서 walk / stand                     573.44 / 51.17
  agent-seconds: 전리품 walk / stand                    526.09 / 2.88
  agent-seconds: 왕복 walk / at the vehicle            425.37 / 681.94
  agent-seconds: fleeing (§06)                       321.43
  §07's bill ÷ what was spent, in agent-seconds      3092.68 / 2260.89  =  ×1.37
```

§07's 한 층 더 탐색 is converted at 3 후보 지점 per storey (§12 puts 15 across 5 storeys),
so ~3분 per storey is ~60 s per site. The first two rows are one player's decision and are
compared in that player's seconds; the last two are the team's and §07 prices them in
wall-clock with everybody present, so they are compared in match seconds.

**Three things fall out, and the third is the whole finding.**

**The simulator is mildly short on the two rows it charges at all** — ×1.29 on searching a
후보 지점, ×1.59 on lifting a 전리품. Those are the gaps a hesitating human would fill.

**It is already over on the walk.** §07 prices 나가서 배터리 교체 at about a minute; the
walk to the door alone measures **105.97 s per player**. §07's table was written against
a building the game no longer has — the same mistake, one layer up, in the design document
rather than in the simulator.

**And the round trip §03 is built on costs eight seconds.** The team is above ground
together for **8.02 match seconds** per round trip against §07's 90. §07's whole argument
for a clock over a descent counter — 나가서 쉬는 것에 대가가 있다, 쇼핑이 결정이 된다 — is
worth eight seconds. That is the largest single gap in the ledger and it is not a rounding
error; it is a missing mechanic.

---

### 3 · The four options, swept

Every table below is 400 matches per point on identical seeds, so the difference between
rows is attributable to the knob. All are reproducible from the simulator's new axes.

#### Option 1 — make traversal cost real time · **DONE, and it delivered**

38 places → 164, 52 m → 217.5 m from the door to the monster. Median 2.5 → 7.2 min,
inside §01's window 0.6% → 15.8%, 심야 1.2% → 33.6%. §12 leaves little room for more:
five floor materials cap the building at five zones and a 40 m zone diagonal caps their
area, so a sixth storey needs a sixth surface as well as a sixth zone.

#### Option 2 — compress §07's threat curve

```
dotnet run -c Release --project core/HorrorGame.Sim -- sweep tier-minutes --matches 400 --seed 1
```

```
tier (min)    med     p10     p90     in25-35 dark%   medRest inRest  밤%     심야%   새벽%   동트기% trips   deaths  clear   surv    earned
8             8.33    4.17    32.49   17.5    39.75   17.25   29.05   50.25   35      19      15      3.02    0.69    11.5    55      878.64
6             8.39    4.17    24.59   2.5     39.75   16.61   4.15    52      44.5    24.75   18.25   2.77    0.61    15      57.75   872.69
4             7.47    4.17    16.86   0       39.5    12.67   0       96      48.75   32.5    26      2.33    0.57    13.5    63.75   820.29
3             7.1     4.17    12.85   0       39.5    12.01   0       98.5    51.5    45      30.5    2.11    0.49    14.75   66.75   802.49
2             5.05    4.17    8.93    0       39.75   8.56    0       99.75   95.75   43.25   38.75   1.7     0.41    10.25   77.25   639.03
1.5           4.94    4.17    7       0       38      6.56    0       100     98.75   54.5    43      1.63    0.38    12      77.75   607.15
```

**It works on the thing it is for, and it makes match length worse.** At 4-minute bands
심야 goes 35% → 48.75%, 새벽 19% → 32.5%, 동트기 전 15% → 26%, and 밤 becomes near-universal
at 96%. But the median falls 8.33 → 7.47 and at 2-minute bands to 5.05, because §07's last
tier is a **forced evacuation** — `MustSurface` fires at 동트기 전 and the shop sets
`_evacuating` at the same row. Compressing the curve brings the exit forward. Earnings fall
with it, 878.64 → 639.03.

**What it costs is not what the previous edition of this entry said.** That reading was
"compressing throws away §07's gradualness, which was chosen deliberately". Gradualness is
measured in tiers-a-team-lives-through, not in minutes-per-tier, and at 8-minute bands the
*typical* match lives through **one**. Compression does not spend gradualness; it delivers
it to the two thirds of matches that currently see one row of a five-row table. What it
actually costs is **eight minutes off the top of the long matches and 5% of the economy.**

#### Option 3 — add required dwell time

`SimScenario` charges §07's four costs as dwell an agent owes to whatever it is standing
over. Scale 0 is the shipped simulator, 1 is §07's table exactly as written.

```
dotnet run -c Release --project core/HorrorGame.Sim -- sweep dwell --matches 400 --seed 1
```

```
§07 costs ×   med     p10     p90     in25-35 dark%   medRest inRest  밤%     심야%   새벽%   동트기% trips   deaths  clear   surv    earned
0             8.33    4.17    32.49   17.5    39.75   17.25   29.05   50.25   35      19      15      3.02    0.69    11.5    55      878.64
0.5           6.67    6.67    6.81    6.25    91.25   32.56   71.43   8.25    7.75    6.25    4.75    1.33    0.11    2       95.5    121.05
1             9.41    9.41    9.6     0       100     0       0       100     0       0       0       1       0.01    0       100     0
1.5           13.01   11.56   13.05   0       100     0       0       100     0       0       0       1       0       0       100     0
2             14.88   14.88   14.92   0       100     0       0       100     0       0       0       1       0       0       100     0
3             21.89   21.89   21.93   0       100     0       0       100     100     0       0       1       0       0       100     0
```

**Charging §07's own action-cost table ends the game on the first descent, every time.**
At ×1: 100% end with every light dead, 1.00 round trips, 0 credits earned, 0% objective
recovered, 0% 완전 승리, and p10 = p90 = the median. Every match is the same length because
every match ends the same way.

The arithmetic behind that is short, and it is a contradiction between two sections rather
than a defect in either:

```
light the team walks in with   4 players × BatterySecondsPerCell 210 s   =   840 player-seconds
§07's underground bill         13.42 sites × 60 s + 21.06 전리품 × 40 s  =  1 648 player-seconds
```

**§07's action costs are twice the light §03 and §08 give the team to spend, before a
single step is walked.** §16-5 already flags `BatterySecondsPerCell` as "the value that
sets the round-trip rhythm"; this is that flag coming due on a building 2.2× the size it
was set for.

So option 3 is not "still open and cheaper than it was". **It is unaffordable at the
current battery, and it is the correct change once the battery is fixed** — §01 and §03
both want time spent on tension rather than on corridors, and the ledger says the
corridors currently take 1 100 of the 2 260 agent-seconds the priced actions consume.

#### Option 4 — accept a shorter match and rewrite §01 and §07 around it

This one is a documentation change, so what the simulator can contribute is the two
numbers the rewrite would have to contain.

**What §01 would have to say.** §01's window is 10 minutes wide. The 10-minute window this
population actually offers is **2.8~12.8 min, holding 58.2%** — computed over the real
match lengths rather than over a grid, so it is the true optimum. §01 would read
「한 판의 흐름 (3~13분)」.

**What §07 would then be forced to say, whether or not anybody meant it to.**
`GameConstants.Validate()` asserts `ThreatTierSeconds * 3f < TargetMatchSecondsMax` —
"§07: a match must be long enough to reach the late tiers." At a 13-minute maximum that
caps a threat band at **4.3 minutes**, so §07's table would have to be rewritten to roughly
2.5-minute bands. The option-2 sweep has already measured that row: at 2-minute bands the
median falls to **5.05 min** and earnings to **639.03 from 878.64 — 27% of the economy
gone.** Options 2 and 4 are the same decision wearing two hats, and the build fails rather
than letting them drift apart.

**And after recommendations 1 and 2 below, the same measurement gives a completely
different answer.** With four cells' worth of light the best 10-minute window is
**14.6~24.6 min holding 60.7%** — §01 would read 「15~25분」, which needs no change to §07
at all because three 8-minute bands still fit inside it. **That is the argument for doing
the recommendation before rewriting anything:** the window §01 would be rewritten to today
is a window produced by a defect.

#### The bootstrap — the population option 2 and 3 both trip over

40.6% of matches end because every light is dead and the wallet cannot buy another cell.
`round_trips_1` is **205 of 500**. The mechanism is a wallet, not a torch:
**could afford a 소모품 after the 1st return — 59.8%**, and the 40.2% who cannot are the
40.6% who end dark.

N spare cells carried in is (N+1) × 210 s of light per player, so this axis is really
"how long must a cell be", expressed in a knob reachable from outside Core. 300 matches,
seeds 1–300 — the zero point is 8.9 min rather than 7.2 because it is a different seed
block, and every row here must be read against that zero, not against §1.

```
spare cells   light/player  med     p10     in25-35 dark%   medRest inRest  best 10-min window   trips   2~5왕복 심야%   새벽%   earned
0             3.5 min       8.9     4.2     18.3    40.0    17.6    30.6    2.8~12.8 @ 57.3%     3.07    44.7    35.3    20.0    882.87
1             7 min         10.5    7.6     20.0    39.7    19.2    33.1    7.6~17.6 @ 59.0%     2.97    45.0    38.7    20.7    881.38
2             10.5 min      13.2    11.1    24.3    40.3    22.2    40.8    10.2~20.2 @ 58.3%    2.73    53.7    44.0    25.7    847.95
3             14 min        16.7    14.6    31.0    39.0    26.7    50.8    14.6~24.6 @ 60.7%    2.53    56.3    51.7    31.3    850.25
```

**This is the strongest single lever measured tonight, and it is not the lever it looks
like.** Four cells' worth of light takes the median from 8.9 to 16.7 min, §01's window from
18.3% to 31.0%, §03's 2~5 왕복 compliance from 44.7% to 56.3%, and 심야 from 35.3% to 51.7%.
The median of the teams that do not end dark reaches **26.7 min — inside §01's window** —
with 50.8% of them in it.

**And the dark share does not move at all: 40.0 → 39.7 → 40.3 → 39.0%.** Earnings do not
move either (882.87 → 850.25), nor loot sold (19.84 → 19.35). More light buys *minutes*,
not *solvency*: the same teams still surface broke, they just take longer to get there.
Whatever fixes the 40% has to be in §08's price table, which is §16-2's question and is
swept separately below.

#### And it is not a price problem either — those teams surface with nothing at all

§16-2's own axis is what 전리품 fetches against §08's unchanged prices. Doubling it,
300 matches, seeds 1–300, against the same zero point:

| | shipped | 전리품 × 2 | 전리품 × 3 |
|---|:--:|:--:|:--:|
| credits after the 1st return | 312.71 | **648.02** | **976.27** |
| could afford a 소모품 after the 1st return | 59.8% | 61.3% | 61.3% |
| **ended with every light dead** | **40.0%** | **39.3%** | **39.0%** |
| median match | 8.9 min | 8.7 min | 8.7 min |
| inside §01's 25~35 | 18.3% | 18.0% | 18.3% |
| earned per match | 882.87 | 1752.33 | 2648.55 |
| loot sold / left behind | 19.84 / 19.67 | 19.73 / 19.75 | 19.87 / 19.58 |

**The economy triples and the population moves one point.** That settles what the 40% is.
They are not poor — they are **empty-handed**: twice nothing is still nothing. A team that
comes back from its first descent with no 전리품 in its pockets cannot buy a battery at any
price, and it is the same 40% at 1×, 2× and 4× the light.

**Why they come back empty is a collision between two of the design's own rules.** §12 puts
전리품 in 막힌 길 — "막힌 길에 좋은 것을 둔다" — and §03 puts 단서 on 후보 지점. On 74
places those were the same journey. On **164 places over five storeys they are two
different journeys**, and a first descent that follows §03's chain, which §03 makes the
only thing that *must* happen, walks past no loot at all.

---

### 4 · The part no simulator can measure, with the arithmetic instead of the hope

Simulated agents do not hesitate, argue, get lost or freeze. §07 is the one section that
put a number on how long a person takes, so it is the only non-speculative estimate
available — and the ledger in §2 is what it can now be compared against.

**§07's own table says the overhead is ×1.37, not ×3.** Weighted by how often this
population takes each action — 13.42 후보 지점, 21.06 전리품, 4.01 왕복 per match — §07
bills **3 092.68 agent-seconds** for the four priced actions and the simulator spends
**2 260.89**. That is the honest multiplier the design document itself implies, and it is
2.2× smaller than a 3× guess.

**Multiplied out, it says 7.2 min becomes 9.9.** `7.2 × 1.37 = 9.86`.

**Simulated rather than multiplied, it says 9.41.** The dwell sweep at ×1 gives a median of
**9.41 min** — within 5% of the 9.86 the multiplication predicts, from a different seed
block whose own zero is 8.33.

**But look at what the two disagree about.** As a *multiplier* the simulation says ×1.13
where the arithmetic says ×1.37, and the gap is not noise: §07's bill is per action, and at
×1 the match dies before most of the actions are taken. Round trips fall 3.02 → **1.00**.
A team that never makes a second descent never pays for the second descent's 후보 지점.
**The arithmetic overcharges because it assumes the match survives to be charged.**

**And the 3× hypothetical is arithmetically right and directionally wrong.** `7.2 × 3 = 21.6`;
the simulator at §07's costs × 3 measures **21.89 min**. The multiplication lands. What it
lands on is this:

```
§07 costs × 3   med 21.89   dark% 100   trips 1.00   earned 0   objective 0%   clear 0%   새벽% 0
```

**A team three times slower than the simulator does not play a 22-minute version of this
game. It plays the first four minutes of it for 22 minutes** — one descent, no sale, no
second trip, no objective, and §07's clock arriving at 심야 over a team that has nothing
left to spend there. The picture changes, and not in the direction the arithmetic
suggests.

**What §07 does not price at all is the talking**, and §01 and §03 make the talking the
game: 단서 반출 금지 means "그 자리에서 보고, 기억해서, 말로 전달해야 한다", and §08's
공용 지갑 makes every purchase a four-way negotiation. None of that is in §07's four rows,
none of it is in the simulator, and the only instrument that can size it is a human at a
mouse. What the ledger contributes is the scale to hold the answer against: **13.42 후보
지점 per match means every 10 seconds a real team adds to searching one is 2.2 minutes of
match length.** That is the sensitivity, and it is why measurement 4 in §6 below is the one
worth instrumenting first.

---

### 5 · RECOMMENDATION

**Ship these three, in this order. Do not touch §07's curve and do not rewrite §01 yet.**

**1 · Put 전리품 on the first descent's route.** This is the only change that addresses the
40.6% directly, and it is a §12 placement rule rather than a number: §12's 막힌 길 and §03's
후보 지점 stopped being the same journey when the building doubled. Doubling what loot
fetches moves the population **0.7 points**; quadrupling the light moves it **1.0 point**;
nothing else tried tonight moves it at all. Until it moves, every other lever is being
measured on a population that is already out of the game — which is exactly why option 3
looks catastrophic below.

**2 · `BatterySecondsPerCell` 210 s → 700–840 s**, or the same light delivered as 2–3 spare
cells carried in. §16-5 already flags this constant as "the value that sets the round-trip
rhythm"; the building has grown 2.2× since it was set, and the walk to the door alone now
measures 105.97 s against a cell's 210. Measured, on its own, 300 matches:

| | shipped (3.5 min of light) | 3 spare cells (14 min) |
|---|:--:|:--:|
| median match | 8.9 min | **16.7 min** |
| inside §01's 25~35 | 18.3% | **31.0%** |
| **median of the teams that do not end dark** | 17.6 min | **26.7 min — inside §01's window** |
| …and the share of them inside it | 30.6% | **50.8%** |
| §03's 2~5 왕복 | 44.7% | **56.3%** |
| reached 심야 / 새벽 | 35.3% / 20.0% | **51.7% / 31.3%** |
| earned per match | 882.87 | 850.25 |

Adding §16-2's economy on top of it changes what the team *owns*, not how long the match
runs — 전리품 × 2 with the same 3 cells gives median 16.9 min and 30.7% in the window
against 16.7 and 31.0%, with 완전 승리 11.3% → **16.3%** and 강화 아이템 affordable after the
first return to **61.0%**. **Light is the length lever; price is the §16-2 lever; neither is
the 40%.**

**Together 1 and 2 are the whole recommendation.** Fix the empty-handed descent and the
population converges on the row this table already measures: **a 26.7-minute median, with
half of it inside §01's window, without touching §07 at all.** That is the argument for
doing these two and stopping to look.

**3 · Then charge §07's action costs** — option 3 — because §01's remaining minutes should
come from tension rather than from corridors, and the ledger says corridors currently take
1 099 of the 2 261 agent-seconds the priced actions consume. It is **unaffordable today**:
at one spare cell it produces 97.7% dark and 17.35 credits a match, at two spare cells
97.0% and 20.18. It costs roughly **3.3 cells of light per player per sweep of the
building**, which is the same decision as 2 and must be priced with it, not after it.

**Do NOT compress §07's curve.** Option 2 delivers the tiers — 심야 35% → 48.75% at
4-minute bands — and pays **eight minutes off the long matches and 5% of the economy** for
them, because §07's last row is a forced evacuation and compressing the curve brings the
exit forward. After 1 and 2 it is unnecessary: at a 26.7-minute match an 8-minute band
gives the team three of five rows, which is §07 working as written.

**Do NOT rewrite §01 yet.** The honest 10-minute window today is **2.8~12.8 min holding
58.2%**, and rewriting §01 around that would be writing the defect into the design. After
1 and 2 the non-broke population's median is 26.7 min, which is inside the window §01
already has.

> **One hard constraint the designer must know before choosing between options 2 and 4:
> they are not independent, and `GameConstants` already says so.**
> `Validate()` asserts `ThreatTierSeconds * 3f < TargetMatchSecondsMax` — "§07: a match
> must be long enough to reach the late tiers." Shortening §01's window below 24 minutes
> forces §07's bands below 8. Whichever is decided first decides the other, and the build
> fails rather than drifting.

---

### 5b · Recommendation 2 was attempted on 2026-08-01 and costs more than this entry priced

`BatterySecondsPerCell` was raised 210 → 700 s, the bottom of the range recommended
above. **The core suite failed in three places, and every one of them is §08 rather than
§03.** The numbers, verbatim:

```
LightTests.OneCell_LastsItsRatedTime_MinusTheSwitchOnCharge
    Assert.That(BatterySecondsPerCell * EconomyReferenceCellsPerDescent,
                Is.LessThan(TargetMatchSecondsMin))
    Expected: less than 1500.0f
    But was:  2800.0f
```

That assertion is not incidental — it is §03's 왕복 구조 written down: "four cells must
not cover a whole match, or the team would never have to surface." At 700 s a reference
restock carries **46 minutes** of light into a 25-minute match, so the light stops being
the thing that sends anybody back up.

Re-deriving the reference restock to match (a descent is ~7 min, a 700 s cell covers one,
two lit players → `EconomyReferenceCellsPerDescent` 4 → 2) fixes that assertion and breaks
two more, because §08's growth table is derived from the restock **cost**, not from the
cell count:

```
EconomyTests.GrowthCurve_AfterTheFirstReturn_BuysConsumablesAndNothingMore
    §08: after 1차 the team has 소모품 몇 개 and no 강화 아이템.
    Expected: less than 50      But was: 85

EconomyTests.GrowthCurve_AfterTheSecondReturn_ForcesSection08sOwnArgument
    §08's quoted negotiation only exists if the flashlight and the batteries
    cannot both be bought.
    Expected: less than 340     But was: 360
```

Halving the cells halves the restock — `4 × 25 + 40 = 140` becomes `2 × 25 + 40 = 90` —
and 50 credits of slack per descent is enough to buy 밧줄 (75) after the first return,
which §08 says is exactly what must not happen yet. The two ways out both cost something
the design has written down:

| way out | what it keeps | what it breaks |
|---|---|---|
| `ShopCostBattery` 25 → 50 | restock stays 140, growth table intact | §08's pricing unit, "one 회중시계 pays for one cell" — the line that makes selling loot feel like buying time |
| re-pin the growth table | §08's pricing unit | every row of the table in `GameConstants`' §08 derivation, which is §16-2's top open question |

**So recommendation 2 is a §08 pass, not a §03 constant.** It was reverted rather than
half-landed. The suite is the reason this entry can say that precisely instead of
discovering it after a playtest.

### 5c · The version of recommendation 2 that does not touch §08

Light the team **finds** is priced by nobody, so it moves §01's length without moving
§08's table: a 배터리 that spawns in the building the way §12's 막힌 길 보상 already does.
It also happens to be recommendation 1 — "put 전리품 on the first descent's route" — with
the one payload that addresses the 40.6 % directly, because the thing those matches ran
out of was not money.

It is additive: `ShopItemId.Battery` already exists, `BatteryState.AddCells` already
exists, `LightReadout.SpareCells` already renders, and `MapMarkerKind.LootSpawn` already
places objects at every dead end. What is missing is a spawn rule that puts some of them
on the route rather than at the end of a detour, and the simulator agents' willingness to
pick one up. **Not attempted on 2026-08-01 — recorded here as the cheapest untried
option, and the one this entry should have recommended first.**

### 6 · What to measure in the first real playtest — §14 Q3, 「지금 나갈까?」

Five numbers, in the order they decide things. Every one of them has a simulated value to
be surprised by.

| # | Measure | Sim says | What it settles |
|:--:|---|:--:|---|
| 1 | Did the first descent come back with anything to sell? | **59.8%** could afford the cheapest item | Whether the 40.6% is real or is the simulator's priority order. A human deviates 10 m for a silver spoon; the agent does not until §03's chain is exhausted. **This is the single most important thing to watch, and it is watchable without any instrumentation.** |
| 2 | Did the descent end because you chose to leave, or because the torch died? | the torch, **40.6%** of the time | Recommendation 2. If humans surface by choice, `BatterySecondsPerCell` is fine and the simulator's agents are simply worse at leaving. |
| 3 | Seconds from "we should go" to back underground | **8.02** — §07 says 90 | Whether §07's 왕복 rows describe anything. If four humans take 90 s, the simulator is missing a mechanic; if they take 8, §07's row is wrong. |
| 4 | Seconds to search one 후보 지점 | **46.54** — §07 says ~60 | The whole match length. 13.42 sites a match: every 10 s of error is **2.2 minutes**. |
| 5 | Which §07 tier were you in when you decided to leave? | 초저녁 or 밤, **66.4%** of matches | Whether §07's curve is content or decoration. If everybody answers 초저녁, compress it after all. |

Numbers 1 and 4 are worth writing down by hand on the first session; 2, 3 and 5 are worth a
line of telemetry each. **None of them needs a build.** Two instances, Discord, four people
— §14 step 2, which every automated gate in this project now points at.

---

### 7 · What is honest to doubt in these numbers

- **The 40.6% is partly the simulator's own policy.** `MatchSimulator.ChooseIntent` ranks
  §03's chain above §08's 전리품, because §03 calls loot optional outright. A human walking
  past a 은수저 picks it up. The size of the artefact is bounded from one side — doubling
  loot value moves the population 0.7 points, so it is not a *price* problem — but only a
  playtest separates "the loot is off the route" from "the agent is a worse player than a
  person". Measurement 1 above is exactly that test.
- **The simulator's graph is a plan, not a section.** `MapGraph.NearestNode` measures
  horizontally. `horrorsim validate` reports **6 places it cannot tell apart across
  storeys**, one of them a 후보 지점 (`C_저탄조`), whose clue marker resolves onto the
  storey above. Self-consistent, and it *understates* traversal for one host in fifteen.
  Pinned against by `MapTests.MapDistances_AreHorizontal`, so it is a decision, not a patch.
- **The dwell model is a choice, not a measurement.** A chase cancels a dwell and the
  seconds already spent buy nothing; §07's 한 층 더 탐색 is converted at 3 후보 지점 per
  storey. Both are stated on `SimScenario` and both are arguable.
- **Nothing ran away.** 0.0% of the zero-point population hit the simulator's 40-minute
  cap, so the long tail is real play and not a stuck agent. Under §07's costs the *whole*
  population collapses onto one length (p10 = p90 = median), which is a result rather than
  a bug — every match ends the same way.
- **These numbers belong to one census and no other.** 164 places, 180 passages, 217.5 m,
  seed 1204. [F-007](#f-007) is a live proposal to reshape that building and a
  seventeenth `MapValidator` rule landed during these runs; the banner records which
  building each number came from, and re-quoting this entry after F-007 lands is the first
  thing to do.

---

### 8 · Before and after the map grew — the half of this finding that is settled

Same command, same seeds, the ring against the shipped building:

| | on the ring (38 places) | on 요양원 지하 5층 (164 places) |
|---|:--:|:--:|
| median match | 2.5 min | **7.2 min** |
| p10 / p90 | 1.3 / 7.9 min | **4.2 / 32.4 min** |
| inside §01's 25~35 | 0.6% | **15.8%** |
| mean §07 tier at end | 0.12 | **1.12** |
| reached 심야 | 1.2% | **33.6%** |
| reached 새벽 | — | **17.4%** |
| reached 동트기 전 | — | **13.0%** |
| deaths per match | 2.1 | **0.68** |
| total wipes | 21.6% | **2.4%** |
| chases broken | 59.6% | **87.7%** |
| loot sold / left | 4.68 / 2.34 | **19.49 / 19.94** |
| earned ÷ cost of one of everything | 0.17 | **0.70** |
| `purchase_upgraded_flashlight` per 500 matches | 23 | **283** |
| objective recovered | 63.8% | **43.8%** |

All five of §07's tiers are reached by real matches, which is the specific claim this
finding was opened about. Seeds 1001–1500 give 6.2 min median, 17.0% inside the window and
41.0% ending dark, so the figures above are the population and not the seed block.

**The clue chain now fails more often than it succeeds** — it pins a site in 51.2% of
matches against 86.4% on the ring, with clue reads 9.66 → 20.11 and misreads 1.93 → 3.34.
Five storeys means a misread on the floor mapping costs a whole extra descent instead of a
corridor. That is §03 working as written — "이 게임의 주된 웃음이자 사망 원인" — and it is
also why 43.8% of matches recover the objective against 63.8%. Whether that is good is a
design decision, and §14 Q4 is the only thing that can answer it.

---

### This blocks §16-2 — less absolutely than it did

```
peak weight band reached by anyone                 0.96   (band 1 = no penalty)
chases opened on a 주자 already in band 2+          0.57 per match   (was 0.16)
earned ÷ cost of one of everything (1240)          0.70             (was 0.17)
purchase_upgraded_flashlight                       283 events / 500 matches   (was 23)
credits after the 1st return                       312.71           (was 160.04)
unspent at the end                                 245.74           (was 24.78)
```

The economy has started. The team earns 0.70 of one of everything against 0.17, the
강화 손전등 §08 calls "이 목록의 대표작" is bought twelve times as often, and 245.74 credits
sit unspent at the end against 24.78 — §08's "필요한 건 다 있는데 시간이 없다" is visible for
the first time. So §16-2 can now be measured, and every number it was measured against
before is void.

The `weight-mul-light` sweep re-run on the real building still shows **no trend** across
0.85–0.95, and the excuse for that has gone: `rChaseL`, chases opened on a 주자 already in
band 2 or worse, is 190–242 of 400 matches against ~16% before. The 주자 gets caught loaded
and escapes anyway, 87.8–92.8% of the time, at every value. §07 puts the monster at 4.4 m/s
until 심야 and F-001's arithmetic is about 4.8, so the band-2 cliff cannot bite in the tier
two thirds of matches end in. **The sweep worth running is
`sweep weight-mul-light --matches 400 --seed 1 --start-minutes 16`**, which starts the clock
at 심야 and is the only version of this experiment that tests what F-001 is about.

### Also visible in the same run

- §11 holds: role picks are near-even across 500 matches (378–419 of 500 each), unchanged
  by the building, so no role is compulsory.
- 생존 is the modal outcome at 53.8%. §02 makes 생존 "information kept without the
  objective"; a game whose usual ending is walking out empty-handed is a different game
  from the one §01 describes.
- Total wipes fell to 2.4%. §02 makes a wipe lose everything, and at 2.4% that threat is
  close to theoretical. Read it beside [F-007](#f-007): the five-storey building is
  measurably more forgiving to run away in.

### Reproducing every number in this entry

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet build core/HorrorGame.sln -c Release          # B-006: the only command that sees the simulator

S="dotnet run -c Release --project core/HorrorGame.Sim --"
$S run   --matches 500 --seed 1                                   # §1, §2, §8
$S sweep tier-minutes --matches 400 --seed 1                      # option 2
$S sweep dwell        --matches 400 --seed 1                      # option 3 · §4
$S run   --matches 300 --seed 1 --start-cells 3                   # recommendation 2
$S run   --matches 300 --seed 1 --loot-value 2                    # §3, the price test
$S run   --matches 300 --seed 1 --start-cells 1 --dwell 1         # option 3 unaffordable
```

**Check the first five lines of every one of them.** They are the building, and they must
reproduce [STATUS.md §1.6](STATUS.md). The 300-match rows are seeds 1–300 and have their own
zero point (8.9 min, not 7.2); compare within a block, never across one.

### Pinned by

Nothing yet, and that is a gap. Every other finding on this page fails a test when its
answer changes; this one is checked by re-running a command and reading it. The candidate
is an assertion that `SimScenario.Default` reproduces the zero point — a seeded population
whose median moves only when somebody means it to.

---

## F-007 · The five-storey map lost the 주자 테스트 band the three-storey one held

**Sections:** §12 (실전 검증 · 주자 테스트) × §06 (추격)
· **Priority:** 🟠 high — it is the one grade §12 gives the map, and it moved the wrong way
· **Status:** **open — still 10/10 as of 2026-08-01 06:25.** Two passes have now been
spent on it and the grade has not moved
· **Source:** `MapSceneGenerator.ReportQualityMenu` and `horrorsim map`, seed 1204
· **Found:** 2026-08-01, integrating the map-scale pass

> **Read this before the numbers below.** This finding has now been assigned to two
> working passes whose brief was to bring the grade back inside 5–7/10. It is still
> 10/10. Both tools — the Unity editor menu and the headless simulator, which share no
> measurement code path — agree exactly.
>
> **What the second pass changed is the checklist, not the map.** §12's corner-density
> rule is now `MapValidator`'s 17th (`66ce930`), the map fails it, and the checklist
> verdict is therefore **FAIL at 16 of 17** rather than the PASS quoted below. That is
> progress — the checklist and the grade finally point the same way — but it also
> means **`MapSceneGenerator` now refuses to write the map**
> ([B-007](BLOCKERS.md#b-007)). This finding is no longer only a balance question; it
> is what is blocking map authoring.
>
> **Do not close B-007 by relaxing `SightBreakPointSpanMax`.** The rule is derived from
> §12's own arithmetic (14.4 m single-corner requirement less the 10 m head start its
> 어그로 시작 거리 table endorses). Fix the geometry and both close together.

### Measured

Same command, same seed, before and after the building grew:

```
before (3 storeys, 74 places, 85 passages, 60 m × 65 m)
§12 주자 테스트 — 요양원 지하: 7/10 (70%), Balanced
  적정 (§12). Breaking aggro is possible from most of the map and never free.

after  (5 storeys, 164 places, 180 passages, 50 m × 92.5 m)
§12 주자 테스트 — 요양원 지하 5층: 10/10 (100%), TooEasy
  너무 쉽다 — 시야 차단 지점을 줄인다 (§12). Aggro is a threat the players can
  shrug off, so §06's chase never becomes the pressure the game is built on.
```

Two lines the report prints underneath the grade decide what to do about it:

```
§12 실전 검증, every place rather than the ten §12 samples:
  164/164 escapable (100%), against §12's 50%~70% band.
  D 하역장 35/35 · A 기록보관소 34/34 · E 기계실 43/43 · C 저탄장 28/28 · B 저수조 24/24

시야 차단 지점 간격 (§12 수치 규칙 15 m~25 m):
  79 corners, nearest-neighbour 2.5 m~10 m, mean 4.1 m, 0 inside the band.
```

**The first kills the "unlucky seed" explanation.** §12's bands are quoted against ten
tries, and near the band a ten-point sample is a coin flip — at a true rate of 60% a
single seed lands outside 5–7 about a third of the time, so 10/10 alone could not
distinguish "borderline map, bad seed" from "every place escapes". `RunnerCensus` runs
the same simulation from all 164 places and the answer is the second, in every zone,
without exception. There is nothing to re-roll.

**The second is the cause, stated as a number.** §12's 수치 규칙 asks sight-breaking
corners to sit 15–25 m apart — 「질주 60m에 3~4번의 기회」. The map has 79 corners and
**not one** of them is that far from its nearest neighbour; the mean is 4.1 m, a
seventh of the low end of the band. Every escape in the sample releases after rounding
2–4 corners with "3 s of unbroken cover", which is what 4 m corner spacing buys. This
is the rule to fix against, and `MapValidator` does not check it — which is why 16 of
16 still passes.

Reproduce with either tool; they share no measurement code and agree exactly:

```
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -nographics -silent-crashes -projectPath unity/HorrorGame \
  -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu -logFile /tmp/quality.log

dotnet run -c Release --project core/HorrorGame.Sim -- map
```

### What it means

§12 asks for **5–7 of 10** sampled runners to escape. Ten of ten now do, and the
report says why in every row: each escape releases with *"3 s of unbroken cover"*
after rounding **2–4 sight-breaking corners**, at 12.8–18.5 m. The old map's three
`CAUGHT` routes all reported the opposite — *"No sight-breaking corner was ever
rounded"* — and they were the routes descending into zones C and E.

The growth removed exactly the weakness that was holding the grade. Going from 85
passages to 180 raised the loop count from 12 to 17 and the dead-end share from
21.6% to 25%, and more connectivity means more corners; a runner who can always find
a third corner inside 15 m can always break line of sight. The checklist did not
notice, because at the time more corners was not a rule any of the sixteen checked —
the map passed **16 of 16** while failing its only grade. As of `66ce930` there are
**17** rules and the map passes **16 of 17**: the corner-density rule below was
written, and it fails on exactly this.

This is F-005's "the checklist is necessary, not sufficient" arriving in practice
rather than in the abstract, and it is the second time the same lesson has cost
something.

### Options

1. **Thin the corners on the escape routes.** The report names the exact node chains
   (`#90`, `#80`, `#135`, `#2`, `#156`, `#106`, `#21`, `#150`). Straightening the
   worst of them is the smallest change that moves the grade.
2. **Make the descent legs longer and barer.** §12 caps a straight run at 20 m, and
   the map is currently at that cap; the escapes are not being broken by long sight
   lines, they are being won by short ones close together. A minimum spacing between
   sight-breaking corners would be a new rule, and it is the one this map wants.
3. **Accept a more forgiving map and raise monster pressure instead** — §06's speed
   ladder, or the aggro-release rule. This trades a §12 target for a §06 retune and
   should not be done without a human playtest.

Option 2 is the one that generalises, and it is the one that was taken: nothing in the
original sixteen rules constrained corner *density*, which is the quantity that
actually decides this grade. **The rule now exists** — `sight-break-spacing`,
`MapValidator`'s 17th — so option 2's diagnostic half is done. Its authoring half,
changing the geometry so the rule passes, is not, and until it is the map cannot be
regenerated at all ([B-007](BLOCKERS.md#b-007)).

**And it now has a number to be written against.** §12 already states the rule in prose
— 시야 차단 지점 간격 15~25 m — it is simply not in `MapValidator`. Adding it as a
seventeenth rule turns this finding from a judgement call into a gate the generator
cannot ship past, the same way `MapValidator` already refuses to build a map with a
21 m sight line. The measurement exists (`MapQualityReport.BreakSpacings`); what is
missing is the `Rule` constant and the decision to fail generation on it. That
decision is the designer's, because on this map it fails 79 corners out of 79 and the
generator would have to lay the building out differently rather than adjust it.

### Not to be confused with F-005's note

F-005 records that Core's own fixture map grades 10/10 TooEasy and always has —
pinned by `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`. That is a
different map from the Unity scene graded here, and it was already TooEasy. What is
new is that the **Unity** map, which was 7/10 and inside the band, has joined it.

---

## F-008 · §12's escape geometry and §01's match length pull in opposite directions

**Sections:** §12 (실전 검증) × §01 (한 판의 흐름) × §06 (어그로 해제)
· **Priority:** 🔴 the two cannot both be satisfied by scale · **Status:** open

### The arithmetic, measured

Aggro starts at 10 m (§12's own endorsed row). A sprinting Runner gains 0.8 m/s
(§06: 5.6 − 4.8). So a bend reached *d* metres along a route holds the sight line for

```
(10 + 0.143·d) / 4.8  seconds
```

which crosses §06's 3-second requirement at **d ≈ 31 m**. `RunnerTest` explores 60 m
of route, so on any route longer than about 31 m **a single bend releases a chase on
its own**, wherever it sits.

### Why this is a tension and not a bug

The building was enlarged to fix F-006 — matches ending in 2.5 minutes against §01's
25-35, with three of §07's five threat tiers unreachable. It worked: median match
length went 2.5 → 7.2 min and 심야 went 1.2% → 33.6%.

The same change took the 주자 테스트 from 7/10 Balanced to **10/10 TooEasy**, because
longer routes mean more route past the 31 m threshold.

```
smaller map  →  §12's escape geometry works,  §01's match length does not
bigger map   →  §01's match length improves,  §12's escape geometry does not
```

Both rules come from §06's speeds. They were derived separately and never checked
against each other at scale.

### Spacing cannot resolve it — this was tested

B5 저수조 was rewritten to the spacing rule exactly: bends grouped into clumps under
the 4.4 m span cap, every clump 15.0 m from its neighbour. The zone's bend count fell
from 15 to 8, its places from 24 to 19, and it stayed **19/19 escapable**.

A control run widened the zone's 개방 공간 until only two counted bends remained.
Still **19/19**.

So the lever for 실전 검증 is not spacing. It is **route length and the bend count
within it** — which is §12's own advice, "시야 차단 지점을 줄인다", and which is the
thing enlarging the building necessarily increases.

### Two §12 rules also disagree with each other

At exactly 15.0 m spacing — the minimum the rule permits — a bend arrives every 15 m
of route, which is the worst legal choice for the escape rate. And §12's S자 통로 rule
*requires* two bends: the first release traced in the experiment came from a two-bend
clump, where one bend gave 2.53 s and its partner 2.5 m later stretched the window to
3.05 s.

`SightBreakPointSpanMax` is 4.4 m — `SingleCornerMinDistance` 14.4 less the 10 m
aggro start — so on a 2.5 m grid a legal 시야 차단 지점 is one bend, or two in adjacent
cells. The S자 통로 rule then mandates the shape the spacing rule is trying to prevent.

### Options

1. **Raise the aggro start distance in `RunnerTest`.** The 10 m comes from §12's table
   as the first *reliably survivable* distance, not as a rule about where aggro must
   begin. Grading a big map from 10 m asks whether escape is possible at the easiest
   endorsed range, and the answer will always be yes. Grading from 3-5 m — §12's own
   ❌ and ⚠️ rows — measures the case that actually kills players.
2. **Make the band scale-aware.** 5-7/10 was written for a 100 × 100 m single-storey
   map. A five-storey building may deserve a different target, stated as such.
3. **Shorten routes and accept a shorter match**, reopening F-006.
4. **Give the monster something that does not decay with distance** — §07's 새벽 tier
   already says "괴물이 출입구를 안다", and that class of rule is unaffected by how far
   the Runner can get.

Option 1 is the smallest change and the most honest: the current measurement asks an
easy question and gets an easy answer. It is still a design decision, because it
changes what "a good map" means.

### Pinned by

`MapValidator`'s `sight-break-spacing` rule and Core's `RunnerTest`. The experiment
was reverted — `FirstMapSketch.cs` is unchanged — because a recipe that provably
cannot reach the band should not be applied to four more storeys first.

---

## F-009 · A jump reaches its apex *plus* the free step — §12's climbing constraint is the sum of two numbers, not one

**Sections:** §12 (맵 설계 규칙) × §06 (추격 수치) × §05 (조작과 이동)
· **Priority:** 🟢 the defect is fixed; the finding is the arithmetic
· **Status:** closed by measurement, but the *shape* of it is open

§05's control table lists 마우스 · WASD · Shift · F and no jump. One was added
anyway, so the first question was how high it may go without changing what §12's
geometry means — the map is derived from what a player **cannot** climb.

### The arithmetic that looked sufficient, and was not

The apex was set at `GameConstants.JumpApexMetres = 0.35 m`, below
`PlayerStepOffsetMetres = 0.40 m`, on the reasoning that a player already walks up
anything 0.40 m or shorter — so a 0.35 m hop reaches a strict *subset* of what
walking reaches and adds no traversal at all.

**That reasoning is wrong, and it is wrong by 0.40 m.** A `CharacterController`
takes its free step wherever it is, including in mid-air. Measured on a 0.58 m box
— `Crate`'s own modelled height, `tools/blender/gen_props.py:1654`:

```
[StanceTest] t=0.28 rise 0.414 at (0.00, 0.51, 0.10)   ← apex, feet 0.35 m up
[StanceTest] t=0.30 rise 0.654 at (0.00, 0.73, 0.34)   ← standing on the crate
[StanceTest] t=1.07 rise 1.010 at (0.00, 1.09, 4.65)   ← jumping again from on top of it
```

**apex 0.35 + step 0.40 = 0.75 m of reachable ledge.** That is above `Crate` (0.58),
above `Dress_BarrelToppled` (0.62), above `Loot_LargePiece_Chest` (0.73), and exactly
at the 차량's cargo-deck height (0.750, `gen_props.py:1107`). Every one of them
became a place to stand.

A second, independent path was measured after the step offset was zeroed: a capsule
has a rounded bottom, so a player pressed against a ledge and rising past it is
carried up and over by ordinary collide-and-slide, with no step logic involved at
all.

### The fix, and the two properties it buys

`PlayerStance` zeroes `stepOffset` for the duration of a hop and restores it on
landing; `PlayerMotor` drops the frozen horizontal velocity the moment a mid-air
step reports `CollisionFlags.Sides`. Together they make the constant's claim true
rather than merely stated, and `PlayerStanceTests.The_hop_cannot_mount_a_ledge_a_walk_cannot`
fails the build if either is undone.

The apex is also corrected for the integration step — `v₀ − ½gΔt`. Uncorrected it
measured **0.374 m at 50 Hz and would have been 0.415 m at 20 Hz**, i.e. past
`PlayerStepOffsetMetres`, which would have made the bound a function of the player's
frame rate. It now measures 0.349 m
(`FirstPersonHandsShot`, `-shotTag stance`).

### The chase numbers did not move

Run on the same commit as this entry:

```
[ChaseTest]   route            189.6 m of NavMesh path
[ChaseTest]   reached          38.86 s
[ChaseTest]   closing speed    4.85 m/s of route, against §06's 4.8 m/s
[ChaseTest]   monster speed    4.80 m/s of corridor, against §06's 4.8 m/s
[ChaseTest]   gap opened at    0.80 m/s while sprinting, against §06's 0.8 m/s
[ChaseTest]   caught           12.54 s   (single-corner control)
```

Identical, to every digit, to the figures in [STATUS.md §1.3](STATUS.md). The
simulator is likewise unmoved — median 7.2 min, 15.8 % inside §01's window, 33.6 %
reaching 심야 — and it *cannot* move, because `HorrorGame.Sim` models the objective
loop and not a controller. **That is the reason this entry exists rather than a
sentence in a commit message:** a verb that touches §05's table and §06's ladder and
leaves both numerically identical is a claim that has to be pinned, not assumed.

### What is still open — the 차량's rear ladder

Not caused by the jump, and found while bounding it.
`tools/blender/gen_props.py:1176` builds four rear rungs at **z = 0.980, 1.380,
1.780, 2.180 — a pitch of exactly 0.400 m**, which is exactly
`PlayerStepOffsetMetres`. The deck below them folds to the ground as a 41° ramp
(`gen_props.py:1141`), under the rig's 50° `slopeLimit`, and `Vehicle` is in
`AssetImportPolicy.ConcaveProps` so every rung is real per-triangle geometry.

Read as a ladder of steps: ground → ramp → cargo floor 0.750 → 0.980 (+0.23) →
1.380 (+0.40) → 1.780 (+0.40) → 2.180 (+0.40) → box roof 2.570 (+0.39). **A player
may already be able to walk onto the roof of the 차량 without jumping**, which would
put them on top of §01's 안전 지대 · 상점 · 보급소 at 2.94 m.

Measured from the generator source, **not reproduced in-engine** — whether a 40 mm
bar supports a 0.30 m-radius capsule is a question for PhysX, not for arithmetic.
The jump does not change the answer either way: 0.35 < 0.40, so every rung that is
reachable with a hop is reachable without one. Worth an hour of somebody's time and
one PlayMode test.

### Pinned by

`PlayerStanceTests` (PlayMode, 6 cases, presses the real keys),
`GameConstants.Validate`'s `JumpApexMetres < PlayerStepOffsetMetres` and
`JumpCooldownSeconds > JumpAirtimeSeconds`, and `MonsterChaseTests` 4/4 for the
figures above.

---

## F-010 · §07's patrol table is a count of zones, so it shrank when the building grew — and the median match never leaves the row where it is smallest

**Sections:** §07 (시간 = 위협도 · 순찰) × §12 (맵 설계) × §01 (한 판의 흐름)
· **Priority:** 🟠 high — it is the measured reason the monster feels absent
· **Status:** open, and **not** silently changed. Recording it, per the rule that §07 is the
designer's to retune
· **Source:** `ThreatTier.PatrolZoneCountFor`, the census in [STATUS.md §1.6](STATUS.md), and
the 500-match population in [F-006](#f-006)
· **Found:** 2026-08-01, while building the 그늘 (§10) and asking the same question of it

### The arithmetic

§07's 순찰 column names an **absolute number of zones** for its first two rows and a
**proportion** for the last three. `ThreatTier.PatrolZoneCountFor(5)` resolves it against
the shipped five-zone building:

| §07 row | 경과 | 순찰 as written | zones on this map | places of 164 | share |
|---|:--:|---|:--:|:--:|:--:|
| 초저녁 | 0~8분 | 1개 구역 | **1** | 24–43 | **15–26 %** |
| 밤 | 8~16분 | 2개 구역 | **2** | 48–78 | 29–48 % |
| 심야 | 16~24분 | 절반 | 3 | ~98 | 60 % |
| 새벽 | 24~32분 | 전체 | 5 | 164 | 100 % |
| 동트기 전 | 32분+ | 전체 | 5 | 164 | 100 % |

The first two rows are the ones that do not scale. "1개 구역" was 1 of 4 zones on a
single-storey 100 × 100 m map (§12 as written); it is 1 of 5 zones over five storeys now,
and a zone is 2.2× the size it was.

### Why that is worse than it looks: the median match ends inside the first row

[F-006](#f-006) measures the median match at **7.2 min** against §07's 초저녁 band of
**0~8 min**. So:

> **In the median match the monster patrols one zone of five for one hundred per cent of
> the match.**

That is not a tail case. Weighting §07's rows by F-006's own measured population
(밤 50.25 %, 심야 33.6 %, 새벽 17.4 %, 동트기 전 13.0 %, so 49.75 % of matches never leave
초저녁):

```
expected zones patrolled at match end = 0.4975×1 + 0.1665×2 + 0.162×3 + 0.044×5 + 0.130×5
                                      = 2.19 of 5   (43.7 % of the building)
```

…and that is the figure **at the end**, which is the most patrolled moment of a match. For
most of a match's duration the scope is lower, because tier is a step function of elapsed
time and the elapsed time is small.

A team can therefore finish a whole match in four fifths of a building the antagonist is
not in, without doing anything clever. It is the same defect as [F-006](#f-006)'s and
[F-007](#f-007)'s seen from a third side: **numbers derived from the small map were not
re-derived when the map became 2.2× larger.**

### Options

1. **Make the first two rows proportional too.** "1개 구역" becomes "1/5 of the map" —
   which is what it meant when it was written, and would then be 1 zone on a 5-zone map
   and 1 on a 4-zone one. This changes nothing today and stops the row going stale again.
   It is honest and it does not fix the absence.
2. **Re-derive the rows from a walking time rather than from a count.** §07's own
   currency is time: a row could say "as much of the building as the monster can sweep in
   N minutes at that row's speed", which scales with the map by construction. Costs a
   pathing measurement per zone at generation time; `RunnerCensus` already walks every
   place, so the instrument exists.
3. **Leave §07 and fix the match length.** F-006's recommendation raises the median to
   ~26.7 min, which puts the typical match into 심야 (3 zones) and 새벽 (5). §07's table
   is then working as written and this finding closes itself. **This is the cheapest
   option and it is already the top of F-006's list.**

Option 3 first, then measure again. Option 1 is a one-line safeguard worth doing at the
same time; option 2 is the right long answer and the most expensive.

### What the 그늘 does about it, and does not

The 그늘 (§10, `Core/Presence`) was deliberately built so it **cannot** acquire this
defect: it is a predicate on a place — unlit, underground, more than
`PresenceMonsterClearRadius` from the monster — rather than a count of zones, so it covers
every dark place in the building at every hour and grows with the building by
construction. There is no number in it that could shrink.

That is a different thing from fixing this. The 그늘 puts a cost in the 80 % of the
building the monster is not in; it does not put the *monster* there, and §01's horror is
the monster. Read this finding as still open after the 그늘 lands.

### Pinned by

`PresenceTests.TheDarkCoversEveryZone_WhereThePatrolTableCoversOne` asserts the 초저녁 row
is an absolute count that does not grow with the map, so this entry fails a test if
anybody makes it proportional without updating the page.

---

## F-011 · §12's 실전 검증 has not been measuring the map — it has been measuring one sprint against 12 metres

**Sections:** §12 (실전 검증 · 개방 공간/미로 공간) × §06 (어그로 해제) × §05 (질주)
· **Priority:** 🔴 the map's only quality metric is blind to the map
· **Status:** measured and open. It supersedes [F-008](#f-008)'s option 1, which is now
disproved rather than untried
· **Source:** a harness over `FirstMapSketch.Build(1204)` and `RunnerTest`, sweeping
`RunnerTestAggroStartDistance` and the 개방 공간 share
· **Found:** 2026-08-01, while trying to act on F-008's recommendation

### The mechanism

`RunnerTest` calls the Runner covered whenever **any** sight-breaking bend lies between
the monster's arc position and the Runner's:

```csharp
if (monster < arc[i] && arc[i] < runner) covered = true;   // RunnerTest.cs:675
```

The shipped building has **79 bends at a mean nearest-neighbour spacing of 4.1 m** — the
map report's own last line, and 0 of them inside §12's 15~25 m rule. So from the first
tick of any chase there is always a bend in between, `brokenFor` never resets, and cover
stops being something a Runner *reaches*. It is the ambient condition of the whole
building.

When cover never lapses, the escape collapses to one inequality with no geometry in it:

```
SprintStaminaSeconds × (RunnerSprintSpeed − MonsterBaseSpeed)  ≥  AggroReleaseDistance − aggroStart
              12 s   ×            0.8 m/s            = 9.6 m   ≥        12 m − aggroStart
                                                     aggroStart ≥ 2.4 m
```

**That is the whole test.** Every one of the ten sampled escapes in the map report
releases at 3.4~10.6 s, and every one reports exactly `3 s of unbroken cover` — the
minimum §06 asks for, reached because the clock started at t=0 and never stopped.

### The prediction, and the measurement that confirms it

If the inequality above is the test, then the aggro start distance should behave as a
**cliff at 2.4 m** and be flat everywhere above it, rather than as the gradient F-008
assumed. Measured over every one of the 164 places:

| aggro start | §12's ten samples | every place (164) |
|:--:|:--:|:--:|
| 3.0 m | 0/10 TooHard | 0 (0.0 %) |
| 4.0 m | 9/10 TooEasy | 159 (97.0 %) |
| 5.0 m | 10/10 TooEasy | 163 (99.4 %) |
| 5.8 m | 10/10 TooEasy | 164 (100.0 %) |
| **10.0 m — shipped** | **10/10 TooEasy** | **164 (100.0 %)** |
| 15.0 m | 10/10 TooEasy | 164 (100.0 %) |

**There is no aggro start distance that produces §12's 5~7/10 band.** The dial goes from
0 % to 97 % between 3 m and 4 m and is saturated thereafter.

> This retires [F-008](#f-008)'s option 1. That option's arithmetic was right — the
> largest aggro start that keeps a *single* bend under §06's 3 s is
> `SingleCornerMinDistance − (sprintGain ÷ sprintSpeed) × SprintMaxTravelDistance` =
> **5.83 m**, against the shipped 10 m — but it does not matter, because on this
> building the release does not come from a single bend. It comes from all 79 at once.

### The one term that can make cover lapse

`BuildRoute` skips a bend that sits inside an 개방 공간 node, for a reason it states in
the source: "§12 gives 개방 공간 15~25 m sight lines on purpose — that is where aggro is
taken — so a bend drawn inside one hides nobody." 개방 공간 is therefore the only thing
in the model that can interrupt cover, and the escape rate should be a function of how
much of the building is 개방 공간 rather than of where its bends are.

Measured three ways, because the authoring primitive is a **rectangle** (`MapSketch.OpenRoom`)
and an answer that only holds for scattered nodes is not an answer a level author can act on:

| 개방 공간 share | promoted by id | by named room | by growing the existing halls |
|:--:|:--:|:--:|:--:|
| **32.3 % — shipped** | **100.0 %** | **100.0 %** | **100.0 %** |
| 52.4 % | 100.0 % | 100.0 % | 98.8 % |
| 62.8 % | 79.9 % | 100.0 % | 87.8 % |
| 66.5 % | **73.2 %** | 100.0 % | 87.8 % |
| 69.5 % | 45.1 % | 100.0 % | **75.0 %** |
| 73.2 % | 45.1 % | 98.2 % | **49.4 %** |
| 79.9 % | 45.1 % | **79.3 %** | 40.2 % |
| 100 % | 0.0 % | 0.0 % | 0.0 % |

Every policy has a band; they disagree only on where it sits, which is what "it depends
how the space is distributed" looks like when it is measured instead of asserted. The
building would need its 개방 공간 to roughly **double**, from 32.3 % to somewhere in
65~80 % of places, for §12's own grade to come back.

### Why the building is 68 % corridor in the first place

| storey | places | 개방 공간 |
|---|:--:|:--:|
| B1 D 하역장 | 35 | the 20 × 20 m bay, ~10 |
| B2 A 기록보관소 | 34 | **none** |
| B3 E 기계실 | 43 | all 43 (hall + gallery) |
| B4 C 저탄장 | 28 | **none** |
| B5 B 저수조 | 24 | **none** |

**Three of the five storeys contain no 개방 공간 at all.** That is a defect on §12's own
terms and independently of this finding's band: §12 endorses 15~25 m as the aggro start
distance a Runner survives, and 개방 공간 is where the section says that distance exists.
On 86 of 164 places there is nowhere in the storey to take aggro from a distance §12 calls
safe — the Runner's whole role, on three floors out of five, has no stage.

### Options

1. **Give each storey one real room.** 기록보관소 is a hall of stacks, 저탄장 is a bunker,
   저수조 is a vaulted tank — every one of them is a large volume in its own fiction, and
   all three are currently drawn as nothing but corridor. This is worth doing whatever the
   band says, and it is the cheapest step toward it.
2. **Then re-measure and see how far short of 65~80 % it lands.** One 15 × 15 m room per
   storey is roughly +18 places, or 32 % → 43 %. Reaching the band means the storeys are
   *mostly* room, which is a different building from the one §12's 첫 맵 스케치 sketches.
3. **Or state the band as scale-dependent** — [F-008](#f-008) option 2 — and grade a
   five-storey building against a number derived for a 100 × 100 m single-storey one only
   after re-deriving it. The band 5~7/10 has never been checked against a map this size.

Options 1 and 2 are level authoring; option 3 is a design decision. **All three are left
open**, and the paragraph below is why option 1 turned out not to be the cheap step it
looks like.

### Option 1 is blocked by B-003, and the geometry says so exactly

There is one 개방 공간 piece in the kit — `HallOpen20x20`, **8 × 8 cells and 6.3 m to the
roof** — against a `StoreyMetres` of 3.75. A hall therefore rises **2.55 m into the storey
above it** and may only be placed where that storey is solid ground, which is B-003: the
defect that used to drop two of these rooms with a `LogError` on every generation, because
1.99 m of headroom does not admit a 2.30 m monster.

Checking the three storeys that have no 개방 공간 against the footprint stacked on top of
each one:

| storey | needs 20 × 20 m of solid ground above | verdict |
|---|---|---|
| B2 A 기록보관소 (x 8–19, z 21–30) | B1 D 하역장 occupies x 16–27, z 29–38 | **a hall fits at x 8–15, z 21–28** |
| B4 C 저탄장 (x 8–19, z 8–17) | B3 E 기계실 occupies x 15–27, z 15–23 | **no 8 × 8 block avoids it** — clear of E needs x ≤ 14 (7 wide) or z ≤ 14 (7 deep) |
| B5 B 저수조 (x 14–26, z 2–10) | B4 C 저탄장 occupies x 8–19, z 8–17 | **no 8 × 8 block avoids it** — clear of C needs x ≥ 20 (7 wide) or z ≤ 7 (6 deep) |

Two of the three are off by **one cell**, which is not bad luck: the storeys are stacked
with deliberately overlapping footprints so that every descent is a traverse, and that
same overlap is what leaves no 20 × 20 m column of solid ground beneath it.

And the one place a hall *does* fit — A at x 8–15, z 21–28 — is the stacks lattice
itself: 배전반 Q, 후보 지점 4 and 5, 예배실, 등사실 and the aisles between them all sit
inside that rectangle. Putting a hall there does not add a room to 기록보관소, it deletes
기록보관소.

**So option 1 costs either a second 개방 공간 piece smaller than 20 × 20 m, or a re-stack
of the storey footprints.** Both are real work with a design decision inside them, which
is why this entry ends here rather than in a commit.

### What is honest to doubt here

The measurement grades a **graph**, and the graph's notion of cover is cruder than the
engine's raycast. `MonsterChaseTests` on baked geometry disagrees with the graph in the
direction this finding predicts: there, an S-corridor (10 m × 2) yields 3.02 s and
releases, while a **single corner yields 1.98 s and the player is caught**. The engine
does not hand out continuous cover; the graph does. So the real building is somewhere
between 100 % escapable and whatever the geometry says, and nobody has measured the real
one at scale. That measurement — `MonsterChaseTests` run from every place rather than from
two hand-picked routes — is the missing instrument, and it is the honest next step before
anyone rebuilds a storey.

### Pinned by

Nothing yet. It should be: a test asserting that the 개방 공간 share of `FirstMapSketch`
is inside whatever band the designer picks would fail the moment a storey is re-authored
past it.

---

## F-012 · The monster could never chase anybody, because nothing ever made a sound

**Sections:** §06 (괴물) × §04 (청음사) × §10 (그늘) · **Priority:** 🔴 was blocking —
the game had no antagonist · **Status:** fixed 2026-08-02 ·
**Source:** the owner playing the solo build — *괴물앞에있어도 안죽는데?* — then
`MonsterKillTests.Standing_in_front_of_the_creature_kills_you`

### What was wrong

§06's state table gives 순찰 exactly one transition:

| 상태 | 전이 조건 |
|---|---|
| **순찰** | 소리 감지 → 경계 |

No sight edge. That is deliberate and `MonsterTests` pinned it. It is also the *only*
way out of patrol.

**Nothing in the shipping game ever raised a sound cue.** The only caller of
`MonsterAgent.ReportSound` in the entire project was `GhostSeatShot.cs` — an editor
screenshot tool. `NoiseMeter` computed how loud the player was, fed it to the audio duck
and to §04's Listener, and its own class comment said the number "also feeds whatever
raises §06's `MonsterSoundCue`". That *whatever* did not exist.

So the monster's one door out of 순찰 opened onto nothing. Measured, on the shipped solo
scene, with the real director:

```
  0.0s  §06 Patrol   1.45 m        ← a player standing 43 cm from its face
  2.6s  §06 Patrol   8.70 m
  9.8s  §06 Patrol  22.87 m        ← it walked away and kept walking
```

It could not chase, could not catch, could not kill. §09's ghost, §06's lunge, the
whole grab path, §04's 섬광수, §10's 그늘 — none of it could ever fire in play. The
game had no antagonist and the test suite was green, because `MonsterChaseTests` calls
`ReportSound` itself.

### Two further defects the same test found

1. **The lunge was ticked on the wrong clock.** `MatchDirector.CheckGrab` runs inside
   `StepFixed`, which `StepMatch` may call several times in one frame — and it advanced
   `MonsterLunge` by `Time.deltaTime`. A 0.55 s commit therefore resolved in a different
   number of steps at 30 fps than at 144, on the one window where survival is decided by
   tenths of a second. Now `GameConstants.FixedStep`.
2. **§06's table is wrong at contact range.** Even with sound wired, a player who stands
   perfectly still makes none, so the creature would still walk through them. From inside
   the game that does not read as restraint; it reads as a broken monster. Patrol now has
   a sight edge inside `MonsterPatrolNoticeRange` — 4 m,
   `MonsterAttackRange` + one second of walking — and nowhere beyond it.
   `Patrol_DoesNotChaseOnSight_AsSection06LiterallyWrites` holds the design line at 5 m
   and still passes; concealment still beats it.

### The fix, and the constant it turns on

`MatchDirector.ReportFootsteps` raises one cue per
`AudioTuning.FootstepStrideMetres` of ground covered — the same rule `FootstepAudio`
plays a clip on, so what the monster hears and what the room hears are one event.
Range is `MonsterFootstepHearingRange` (35 m) scaled by the surface's own
`ListenerClarity*` and by `NoiseMeter.Noise01`:

| surface | clarity | walk ≈ | sprint ≈ | what it means |
|---|:--:|:--:|:--:|---|
| 금속 계단 | 1.00 | 10 m | 35 m | heard long before it could be seen |
| 타일 | 0.85 | 9 m | 30 m | |
| 자갈 | 0.70 | 7 m | 25 m | |
| 콘크리트 | 0.50 | 5 m | 18 m | |
| 흙 (B8) | 0.40 | 4 m | 14 m | |
| **카펫 (B6 병동)** | **0.22** | **2 m** | **8 m** | the one floor you can run on |

35 m is bounded on both sides by rules that already existed: under
`ListenerHearingRange` (40 m) so §04 keeps its job, over `MonsterSightRange` (20 m) so
§03's darkness blinds the player rather than the creature. `GameConstants.Validate`
asserts both, plus the two ends of the surface table.

### Why nobody noticed

Every test that needed a chase built one. `MonsterChaseTests` calls `ReportSound`
directly; `MonsterTests` hands the brain a cue array. Both are correct unit tests of the
brain and both are blind to whether anything in the game ever calls them. The gap was
between two green suites, which is where this class of defect always is — and it took a
person playing the build to find it.

### Pinned by

`MonsterKillTests.Standing_in_front_of_the_creature_kills_you` — walks a body at the
creature on the shipped solo scene, holds it there, and asserts §09's ghost. On failure
it names which link of the chain was open rather than only that the player survived.
`MonsterTests.Patrol_ChasesSomethingStandingInItsFace`,
`Patrol_DoesNotNoticeJustPastContactRange` and
`Patrol_DoesNotNoticeAConcealedPlayerAtContactRange` hold the contact exception's edges.
