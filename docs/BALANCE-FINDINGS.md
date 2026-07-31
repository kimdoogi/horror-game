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

## F-006 · Matches finish in 2.5 minutes, so three of §07's five threat tiers are dead

**Sections:** §01 (한 판의 흐름) × §07 (시간 = 위협도) × §08 (경제)
· **Priority:** 🔴 highest — it invalidates the tuning of everything downstream
· **Status:** open · **Source:** 500 simulated matches, seeds 1–500

### Measured

```
§01 match length — target 25~35 min
  median                    2.5 min
  p10 / p90                 1.3 min / 7.9 min
  inside the window         0.6%

§07 threat curve
  mean tier at end          0.12   (0 = 초저녁 … 4 = 동트기 전)
  reached 심야 or later      1.2%
```

Reproduce with:

```
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1
```

### What it means

§07 declares "시간이 유일한 통화다" and builds the entire pressure system on the
clock. At these numbers the clock barely moves. Matches end in tier 0, so:

- 심야's flashlight −30% never happens
- 새벽's "괴물이 출입구를 안다" never happens
- the monster's speed never leaves 4.4 — it never reaches the 4.8 that §06's whole
  speed ladder is built around, let alone 5.0 or 5.2
- **three of the five tiers are content nobody sees**

§07's action costs (한 층 더 탐색 ~3분, 나가서 배터리 교체 ~1분) imply roughly
15–25 minutes across 3–5 round trips. The simulation gets 2.81 round trips in 2.5
minutes, which means the traversal itself costs almost nothing.

### The honest caveat

Simulated agents do not hesitate, argue, get lost, or freeze. Some of the missing
25 minutes is human friction the simulator cannot model, and §03's "6이었나 9였나"
argument is exactly that kind of time.

But that is the finding, not a reason to dismiss it: **the design currently has no
mechanism that consumes 25–35 minutes — only the hope that players will be slow.**
Skilled, coordinated players will trend toward the simulator's number, and they are
the ones who will never see three quarters of the threat system.

### Options

1. **Make traversal cost real time.** Larger maps, or §12's zone diagonals and floor
   counts scaled so one descent genuinely costs the ~3 minutes §07 assumes.
2. **Compress the threat curve** to fit the real match length. Cheapest, but §07's
   eight-minute bands were chosen to feel gradual; at 30-second bands they will not.
3. **Add required dwell time.** §03 already gates clue reading on sustained light and
   stillness; extending that principle to safes, zone lights and objective retrieval
   would spend time on tension rather than on walking.
4. **Accept a shorter match** and rewrite §01 and §07 around it.

Option 1 preserves the design as written. Option 2 is the one to reach for only after
measuring a real playtest, because it trades away §07's gradualness.

### This blocks §16-2, which the document calls the current bottleneck

The same 500 matches:

```
peak weight band reached by anyone        0.82   (band 1 = no penalty)
chases on a loaded 주자                    ~16% of matches
earned ÷ cost of one of everything        0.17
purchase_upgraded_flashlight              23 of 500 matches
```

Sweeping F-001's `WeightMulLight` across 0.85 → 0.95 over 400 matches per point
moves almost nothing (clear 11.25% → 11.5%, wipe 22% → 16.25%, Runner-escape-while-
loaded shows no trend). Reproduce with:

```
dotnet run -c Release --project core/HorrorGame.Sim -- sweep weight-mul-light --matches 400 --seed 1
```

The reason is visible in the same table: teams never accumulate enough loot to leave
weight band 1, so the cliff is rarely tested at all. §08's growth curve — "후반:
필요한 건 다 있는데 시간이 없다" — needs a late game to happen in.

**So the economy cannot be tuned before match length is fixed.** §16 ranks the price
table as the top open question; on this evidence match length outranks it, because
every economy measurement taken now is taken from a game that ends before the
economy starts.

### Also visible in the same run

- §11 holds: role picks are near-even across 500 matches (378–419 of 500 each), so
  no role is compulsory.
- Deaths average 2.1 of 4 per match, and 21.6% of matches are total wipes. §02 makes
  a wipe lose everything, so that rate deserves a deliberate decision.
- §08 calls the 강화 손전등 "이 목록의 대표작", and it is bought in 23 of 500
  matches. Either the price is wrong or the doubled visibility cost is not worth it.
