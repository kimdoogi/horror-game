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

§12's 첫 맵 스케치 passes all sixteen validator rules and still grades **10/10
TooEasy** on 실전 검증. Passing the checklist means a map is not broken; it does not
mean it is good. §12 already implies this by specifying both, but it is easy to read
the checklist as the finish line.

Pinned by `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`.

---

## F-006 · Matches finish in 7.2 minutes against §01's 25~35 — and the 2.5 minutes this used to say was measured on a building the game does not have

**Sections:** §01 (한 판의 흐름) × §07 (시간 = 위협도) × §08 (경제)
· **Priority:** 🔴 highest — it invalidates the tuning of everything downstream
· **Status:** open, and moved a long way for the first time
· **Source:** 500 simulated matches, seeds 1–500, run on 요양원 지하 5층 itself

> **Re-run and re-confirmed 2026-08-01 03:20**, exit 0, after the four parallel passes
> were merged and the simulator's own build break was fixed (see
> [STATUS.md §1.2](STATUS.md) — `HorrorGame.Sim.csproj` did not compile `RunnerCensus.cs`,
> so `dotnet build core/HorrorGame.sln` failed on 2 errors before this pass). Every
> figure below reproduced **byte for byte**, including the banner census, which is the
> check this section asks you to make. Nothing here is carried forward.

### Measured — 2026-08-01, on the building the game ships

```
=== the building these matches were run in
  요양원 지하 5층 (B1 하역장 · B2 기록보관소 · B3 기계실 · B4 저탄장 · B5 저수조)  (seed 1204)
  5 zones · 164 places · 180 passages · 17 순환로 · 41 막힌 길 · footprint 50 m × 92.5 m
  §12 validation PASS · 후보 지점 15 · 전리품 41 · 금고 2 · monster spawn 217.5 m from the door

§01 match length — target 25~35 min
  median                                             7.2 min
  p10 / p90                                          4.2 min / 32.4 min
  inside the window                                  15.8%
  hit the sim's 40-min cap                           0.0%
  ended with every light dead                        40.6%
  median of the rest                                 17.1 min
  inside the window, of the rest                     26.6%

§07 threat curve
  mean tier at end (0=초저녁 … 4=동트기 전)                 1.12
  reached 심야 or later (tier 2, 16 min)               33.6%
  reached 새벽 or later (tier 3, 24 min)               17.4%
  reached 동트기 전 (tier 4, 32 min)                     13.0%
```

Reproduce with:

```
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1
```

**Read the first five lines of that output before reading any of the rest.** They are the
building, and they must reproduce [STATUS.md §1.6](STATUS.md), which measures the same
census from the Unity scene with a different tool. The day they stop agreeing, this
finding is being measured against the wrong map again — which is exactly what had
happened for every measurement before this one.

Seeds 1001–1500 give 6.2 min median, 17.0% inside the window, 41.0% ending with every
light dead, so the numbers above are the population and not the seed block.

**These numbers belong to that census and no other.** They were taken against the
building whose §12 report is [STATUS.md §1.6](STATUS.md) — 164 places, 180 passages,
16/16 rules, 주자 테스트 10/10. [F-007](#f-007) is a live proposal to reshape that
building, and reshaping it will move every figure on this page. That is now a feature
rather than a hazard: the banner records which building each run measured, so the next
person to quote a match length can check it against the map they think they have.
`horrorsim validate` exits 6 rather than 0 when §12 rejects the map, so a measurement
taken mid-edit announces itself — as it did at 02:34 on 2026-08-01, when F-007's reshape
was in flight and `validate` returned
`failed [straight-corridor, s-corridor-per-zone, dead-ends]`. **The first thing to do
after F-007 lands is to re-run the one command above and re-quote this section**; nothing
here survives a change to the building, and now nothing here can silently outlive one.

### What the simulator used to be measuring

`core/HorrorGame.Sim/SimMap.cs` built **its own** four-zone ring out of `GameConstants`
and never read the level. Compiling the previous version against Core and asking it for
its own census:

```
§12 첫 맵 스케치 (sim): 4 zones, 38 nodes, 47 edges, 10 loops
zones 4  places 38  passages 47  loops 10  dead ends 9  footprint 49.5 x 49.5
sites 12  loot 9  safes 2  monster spawn 52 m from the door
§12 validation PASS
```

Against the shipped building: **38 places against 164, 47 passages against 180, and the
monster spawning 52 m from the door against 217.5 m.** Both maps pass §12's sixteen
rules; they are two different buildings that both cite §12. So when the Unity level grew
from three storeys to five on 2026-08-01 and this document reported that the simulator's
numbers were "identical to three significant figures", that was not a coincidence and not
a bug — the grown map was not in the measurement at all.

`SimMap` now calls `FirstMapSketch.Build(seed)`, which is the same call
`MapSceneGenerator.Generate` makes before it lays a single FBX. Not an export of the
graph — the authoring sources themselves, compiled into the simulator by
`HorrorGame.Sim.csproj`, so the two cannot drift: change the building and the next
`dotnet run` measures the change.

### Before and after, same command, same seeds

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
| `purchase_upgraded_flashlight` events / 500 matches | 23 | **283** |
| bought a 강화 아이템 at all | 63.8% | 59.2% |
| objective recovered | 63.8% | **43.8%** |

### The verdict: a bigger map moved F-006 a long way and did not close it

**Option 1 works.** It is the first thing tried against this finding that has moved it at
all, and it moved it by a lot: the median match nearly tripled, the share landing in
§01's window went up 26-fold, and the share that ever sees 심야 went up 28-fold. **All
five of §07's tiers are now reached by real matches**, which is the specific claim this
finding was opened about — 심야's −30% flashlight, 새벽's "괴물이 출입구를 안다" and
동트기 전's 생존 불가 수준 are content now, seen by 33.6%, 17.4% and 13.0% of matches
respectively.

**And it is not enough on its own.** §01 asks for 25–35 minutes as the *normal* match, and
7.2 minutes is the normal match. The window is reachable rather than typical. Two things
sit between here and 25 minutes, and they are different problems:

**1. Two fifths of matches end broke, not beaten.** 40.6% end because every light is dead
and the wallet cannot buy another cell — §02 files them as 생존, the team walks out
alive, and the run simply stops. That population did not exist at this size before
(the ring's 생존 row was 14.6% in total, which bounds it). It is a bootstrap failure in
§08: a first descent across a five-storey building can spend its whole battery walking to
후보 지점 and surface with nothing to sell, and a team with nothing to sell has no second
descent. **Excluding them, the median match is 17.1 minutes and 26.6% land inside §01's
window** — so this one population is most of the remaining gap. It is also the most
interesting thing the bigger map produced, because it is a real §08 tension the small map
could not express, and §08 has a knob for it: the starting battery, `BatteryCells`, or a
first-descent grubstake.

**2. The clue chain now fails more often than it succeeds.** The chain pins a site in
51.2% of matches, down from 86.4%. Clue reads went 9.66 → 20.11 per match and misreads
1.93 → 3.34: five storeys means a misread on the floor mapping costs a whole extra
descent instead of a corridor. That is §03 working as written — "이 게임의 주된 웃음이자
사망 원인" — but at 51% it is also why 43.8% of matches recover the objective against
63.8% before. Whether that is good is a design decision, not a defect, and §14's human
playtest is the only thing that can answer it.

Note also which way the difficulty moved: **deaths fell from 2.1 to 0.68 per match and
wipes from 21.6% to 2.4%**, while chases broken rose from 59.6% to 87.7%. That is
[F-007](#f-007) showing up in the match numbers rather than in the map grader — the
five-storey building is measurably more forgiving to run away in, and the two findings are
now measuring the same thing from two directions.

### What is honest to doubt in these numbers

- **The simulator's graph is a plan, not a section.** `MapGraph.NearestNode` measures
  horizontally, which was harmless when §12's map was four zones side by side and is not
  when the storeys are stacked. On the building measured here `horrorsim validate`
  reports **6 places it cannot tell apart across storeys**, one of which is a 후보 지점
  (`C_저탄조`), whose clue marker therefore resolves onto the storey above. The error is self-consistent — the simulator
  always resolves that position the same way — and it *understates* traversal for one
  host in fifteen. The fix belongs in Core and is pinned against by
  `MapTests.MapDistances_AreHorizontal`, so it is a decision, not a patch.
- **Simulated agents do not hesitate, argue, get lost, or freeze.** Unchanged from the
  original finding, and it now cuts the other way as well: the 40.6% that end broke are
  agents that never picked up loot they walked past, because the policy ranks §03's chain
  above §08's 전리품. A human would grab the trinket. Treat 7.2 minutes as a floor.
- **Nothing ran away.** 0.0% hit the simulator's 40-minute cap, so the long tail is real
  play and not a stuck agent.

### Options, revised

1. ~~**Make traversal cost real time.**~~ **Done, and it delivered.** 38 places → 164,
   52 m → 217.5 m from the door to the monster. §12 leaves little room for more: five
   surfaces cap the building at five zones and a 40 m zone diagonal caps their area, so
   the next storey needs a sixth floor material as well as a sixth zone.
2. **Fix the bootstrap before anything else.** 40.6% of the population never gets to play
   §08's growth curve at all. This is now the single biggest lever on match length and it
   is a price-table question, which makes it §16-2's — the two are the same question.
3. **Compress the threat curve.** Cheapest, and now clearly the wrong move: the tiers are
   being reached. Compressing an 8-minute band that 33.6% of matches already cross would
   throw away the thing the map growth just bought.
4. **Add required dwell time.** Still open, and cheaper than it was: with 41 dead ends
   carrying 전리품 and 19.94 pieces left behind per match, there is somewhere to spend it.
5. **Accept a shorter match** and rewrite §01 and §07 around ~15 minutes rather than
   ~7. Worth reconsidering now that the honest number is 17.1 minutes for a team that can
   fund its second descent.

### This blocks §16-2 — less absolutely than it did

The same 500 matches:

```
peak weight band reached by anyone                 0.96   (band 1 = no penalty)
chases opened on a 주자 already in band 2+          0.57 per match   (was 0.16)
earned ÷ cost of one of everything (1240)          0.70             (was 0.17)
purchase_upgraded_flashlight                       283 events / 500 matches   (was 23)
credits after the 1st return                       312.71           (was 160.04)
unspent at the end                                 245.74           (was 24.78)
```

The economy has started. The team now earns 0.70 of one of everything against 0.17, the
강화 손전등 §08 calls "이 목록의 대표작" is bought twelve times as often, and 245.74
credits sit unspent at the end against 24.78 — §08's "필요한 건 다 있는데 시간이 없다" is
visible for the first time. **So §16-2 can now be measured, and every number it was
measured against before is void**, including the `weight-mul-light` sweep this document
used to quote: it was run on the ring.

It has been re-run on the real building — `sweep weight-mul-light --matches 400 --seed 1`,
same seeds across every point:

```
WeightMulLight  clear   partial survive wipe    len_med trips   deaths  earned  rChaseL runEscL
0.85            11.5    31.25   55      2.25    8.33    3.02    0.69    878.64  242     91.32
0.86            11.25   31.5    55.5    1.75    8.03    3.05    0.71    861.36  221     92.76
0.88            12.25   33      52.5    2.25    8.33    2.96    0.7     864.21  216     91.67
0.90            12      31      55.25   1.75    7.19    3.02    0.68    867.48  190     88.42
0.92            12.5    32      54.25   1.25    7.66    2.97    0.67    871.89  230     89.13
0.95            11.75   30.75   55.5    2       9.82    3.1     0.68    883.64  204     87.75
```

**Still no trend — and the excuse for that has gone.** The old reading of this sweep was
that teams never accumulate enough loot to leave weight band 1, so §08's cliff was
rarely tested. On the real building it *is* tested: `rChaseL`, chases opened on a 주자
already in band 2 or worse, is 190–242 of 400 matches, against ~16% before. The 주자 gets
caught loaded and escapes anyway, 87.8–92.8% of the time, at every value of the
multiplier. That points somewhere new: §07 puts the monster at 4.4 m/s until 심야 and
F-001's arithmetic is about 4.8, so the band-2 cliff cannot bite in the tier two thirds
of matches end in. **The sweep worth running now is
`sweep weight-mul-light --matches 400 --seed 1 --start-minutes 16`**, which starts the
clock at 심야 and is the only version of this experiment that tests what F-001 is about.

What has *not* changed is the other half of F-001's population: peak weight band 0.96
still means the *typical* team stays in band 1, and 19.94 pieces of 전리품 are still left
in the building every match.

### Also visible in the same run

- §11 holds: role picks are near-even across 500 matches (378–419 of 500 each), unchanged
  by the building, so no role is compulsory.
- 생존 is now the modal outcome at 53.8%, against 부분 승리 53.2% before. §02 makes 생존
  "information kept without the objective"; a game where the usual ending is walking out
  empty-handed is a different game from the one §01 describes, and this is the first
  number that says so.
- Total wipes fell to 2.4%. §02 makes a wipe lose everything, and at 2.4% that threat is
  close to theoretical.

---

## F-007 · The five-storey map lost the 주자 테스트 band the three-storey one held

**Sections:** §12 (실전 검증 · 주자 테스트) × §06 (추격)
· **Priority:** 🟠 high — it is the one grade §12 gives the map, and it moved the wrong way
· **Status:** **open — still 10/10 as of 2026-08-01 03:25.** A pass was spent on it and
the grade did not move
· **Source:** `MapSceneGenerator.ReportQualityMenu` and `horrorsim map`, seed 1204
· **Found:** 2026-08-01, integrating the map-scale pass

> **Read this before the numbers below.** This finding was assigned to a working pass
> whose brief was to bring the grade back inside 5–7/10. It is still 10/10. The map
> that shipped from that pass is the map measured here, and both tools — the Unity
> editor menu and the headless simulator, which share no measurement code path — agree
> exactly. Do not read the 16/16 checklist PASS as the map being fine; §12 gives two
> verdicts and this is the other one.

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
notice because more corners is not a rule any of the sixteen check — the map still
passes **16 of 16**.

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

Option 2 is the one that generalises: nothing in the sixteen rules constrains corner
*density*, which is the quantity that actually decides this grade.

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
