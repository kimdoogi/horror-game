# Where every number lives

> `unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs` is the single authority for
> every tuned value in this game. **A literal like `4.8f` anywhere else is a bug.**

**138 public constants, 2191 lines, one file** — counted 2026-08-12 at `4ab204f`.
Verify both yourself:

```bash
grep -cE 'public (const|static readonly)' unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs   # 138
wc -l < unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs                                     # 2191
```

Fewer constants and twice the lines than this page used to claim (184 / 1137), and both
directions are deliberate: §04's five 직업 and §08's economy took their numbers with
them, and what replaced them is prose. **Four whole blocks are now tombstones** — §04,
§05's weight table, §09's ghost and §03's 혼동쌍 keep their banner, name every deleted
constant, and spend a paragraph on why it went, because a reader who greps for a
deleted constant has to find the deletion rather than nothing. The §04 block also
records the three constants that were *renamed* rather than deleted and the four that
were kept because they were never §04's, which is the distinction that matters when
you are deciding whether something is safe to remove.

---

## 1. How the file is organised

Blocks with a `// §NN —` banner, roughly but **not strictly** in design-section order —
§06 comes first because "이 한 줄이 게임 전체를 정한다", and §05 and §06 alternate
after it. Find a block with:

```bash
grep -nE '^\s+// §' unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs
```

| Block | Line | Contains |
|---|--:|---|
| §06 — Speed relationships | 25 | `WalkSpeed 2.0` `RunSpeed 4.5` `MonsterBaseSpeed 4.8` `RunnerSprintSpeed 5.6` |
| §05 — Directional multipliers | 46 | `MulForward 1.00` `MulDiagonal 0.95` `MulStrafe 0.90` `MulBackward 0.65` and the angles they sit at |
| §05 — Field of view | 80 | `FovDefault 80` `FovMin 70` `FovMax 90` — "a balance value, not a comfort setting" |
| §05 · §12 — 웅크리기 and a hop | 96 | the two verbs §05's control table does not list, and the bounds that stop them changing what §06 and §12 mean |
| §06 — Aggro, stamina, release | 255 | `AggroReleaseDistance 12` `AggroReleaseLineOfSightBreak 3` `SprintStaminaSeconds 12` `SprintMaxTravelDistance 60`, the lunge, and the door prices |
| §06 — State machine timings | 482 | `AlertGiveUpSeconds 3` `SearchGiveUpSeconds 15` `StandstillSeconds 5` `SearchRadius 12` `MonsterSightRange 20` |
| §11 — The field | ~606 | `RaceRunnersMin 2` `RaceRunnersMax 20` |
| §05 — 🔴 **the weight table, GONE** | 689 | a tombstone: "Re-adding a weight band means re-adding a thing to carry" |
| §04 — 🔴 **the five 직업, GONE** | 736 | a tombstone naming every deleted constant, plus the three that were *renamed* rather than deleted (`FlashStunSeconds`→`MonsterStunSeconds`, `ObserverRange`→`HallClearSightMin`, `EngineerReachDistance`→`InteractReachMetres`) and the ones kept because they were never §04's |
| §12 — Surface table | 888 | `ListenerClarity*` — **eight** floor materials and one fallback (wood 0.80, tile 0.85, gravel 0.70, concrete 0.50, metal 1.00, water 1.00, earth 0.40, carpet 0.22, unknown 0.35), read by `MapZone.ClarityOf` |
| §03 — Light | 1016 | `FlashlightRange 12` `FlashlightHalfAngle 22` `LateNightFlashlightPenalty 0.30`. 🔴 The header used to read "§03 / §08 — Light **and battery**"; the cell went with §08 |
| §07 — Threat curve | 1100 | `ThreatTierSeconds 8×60` and the five speeds 4.4 / 4.6 / 4.8 / 5.0 / 5.2 |
| §09 — 🔴 **Ghost, NO CONSTANTS** | 1179 | "and that is the finding" — `GhostRattleCooldownSeconds` and `GhostRattleRange` are gone because §11's 탈락자 rule forbids a ghost touching the living |
| §13 — Networking and voice | 1195 | `VoiceCutoffDistance 30` `NetworkSendRate 30` `PlayersPerMatch 4` — and read `PlayersPerMatch`'s doc comment before you use it, because it is **lobby seats, not the race's field**, and the file flags the four-seat-lobby-in-front-of-a-twenty-runner-race as a live finding |
| §13 — Telemetry buckets | 1234 | the histogram geometry §13 writes out literally |
| §12 — Map rules | 1279 | **derived from the numbers above** — see §3 of this page. Also `CentrePathMetresMin/Max 90/140`, the `RunnerTest*` band (`PassRateMin 0.50` / `Max 0.70`) and `RunnerTestSampleCount 10` |
| §03 — 🔴 **혼동쌍, GONE** | 1665 | the probabilistic model of misremembering a number, deleted with the clue chain |
| §10 / §03 — 그늘 | 1698 | `Presence*` — saturation 45 s, dispersal 15 s, silence = `SprintStaminaSeconds`, the boldness ramp. The second thing in the building, and it has no position |
| Simulation | 1914 | `FixedStep = 1f / 50f` |

Line numbers are as of `4ab204f`; regenerate the whole table with
`grep -nE '^\s+// §' unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs`.

---

## 2. The tests assert the design's *reasoning*, not the values

This is the part that makes the file worth having. `GameConstants.Validate()` (line
**1921**, and it is 65 `Require` clauses long) is a list of relationships, each
with the sentence that explains why it must hold. A retune that breaks one throws on
startup and fails the suite. Verbatim examples, checked against the file on 2026-08-12:

```csharp
Require(RunSpeed < MonsterBaseSpeed,
    "§06: the monster must out-run a running player, or ordinary roles could simply flee.");
Require(MonsterBaseSpeed - RunSpeed <= 0.5f,
    "§06: the monster's edge over running must stay small — that narrow margin is the tension.");
Require(RunnerSprintSpeed * MulBackward < MonsterBaseSpeed,
    "§05: even a sprinting Runner must lose ground while backpedalling.");
Require(RunnerSprintSpeed * MulDiagonal > MonsterBaseSpeed,
    "§05: the 45° peek must still out-pace the monster, or the skill ceiling disappears.");

var gain = (RunnerSprintSpeed - MonsterBaseSpeed) * SprintStaminaSeconds;
Require(gain < AggroReleaseDistance,
    "§06: one sprint must not be enough to open the release distance — breaking aggro has to mean using the map.");

Require(SCorridorLegLength * 2f / MonsterBaseSpeed > AggroReleaseLineOfSightBreak,
    "§12: two S-corridor legs must take the monster longer to clear than the line-of-sight break requires.");
Require(MaxStraightCorridor <= LineOfSightBreakSpacingMax,
    "§12: a straight corridor must never be longer than the widest allowed gap between cover.");
```

`Validate()` is called by the test suite —
`FoundationTests.GameConstants_Validate_Passes` — so a broken relationship cannot reach
a green commit. 🔴 *This paragraph used to add "and by the simulator on startup, so it
cannot reach a 500-match run and quietly bias its numbers".* `core/HorrorGame.Sim/` was
deleted at `e8c67ae`; the test-suite half is the whole of it now, which is one caller
fewer than the guard used to have.

**When you add a constant that has a relationship worth guarding, extend
`Validate()` in the same edit.** A constant with no relationship and no citation is
indistinguishable from a magic number that moved house.

---

## 3. §12's dimensions are computed from §06's speeds — this is the trap

The §12 block is not a second set of design choices. It is arithmetic:

```
SingleCornerMinDistance   14.4 m   = AggroReleaseLineOfSightBreak 3 s × MonsterBaseSpeed 4.8
SprintDistanceGain         9.6 m   = (RunnerSprintSpeed 5.6 − MonsterBaseSpeed 4.8) × SprintStaminaSeconds 12
SCorridorLegLength        10 m     → 2 legs / 4.8 = 4.17 s of cover > the 3 s required
MaxStraightCorridor       20 m     ≤ LineOfSightBreakSpacingMax 25 m
SightBreakPointSpanMax     4.4 m   = SingleCornerMinDistance 14.4 − RunnerTestAggroStartDistance 10
SprintMaxTravelDistance   60 m     ≈ 5.6 × 0.95 (the §05 peek) × 12 s = 63.8, revised down
                                     from 67 m by §05 and pinned to a band, not an equality:
                                     Validate() requires peekTravel ∈ [59, 68]
```

So:

> **If you change a speed, you have changed the map. If you change the map's scale,
> you have changed whether the escape maths still works.**

Both directions are guarded, and both guards must be run:

| Guard | Command | What it would catch |
|---|---|---|
| `GameConstants.Validate()` | `dotnet test core/HorrorGame.sln` | a speed change that breaks §12's derived inequalities |
| §12 validation, **14 rules** | `MapSceneGenerator.ReportQualityMenu` — [Verifying §3](06-verifying.md) — or engine-free via `dotnet test core/HorrorGame.sln --filter "FullyQualifiedName~MapTests.Descent_"` (4 tests, ~36 s) | a map that no longer satisfies the rules. **13 of 14 pass; `centre-path` fails and is waived by name** in `MapSceneGenerator.KnownFailingRules` ([B-019](../BLOCKERS.md#b-019), [STATUS.md §2.2](../STATUS.md)) |
| `MonsterChaseTests`, 4 tests | PlayMode, `-testFilter "MonsterChaseTests"` | a map on which two 10 m legs no longer break a chase, or one corner now does |

> 🔴 **This table used to say "17 rules … currently failing rule 17, the corner-density
> rule" and cite [B-007](../BLOCKERS.md#b-007).** `MapValidator` declares 14 rules —
> count them with `grep -c 'public const string Rule'` — and B-007's
> `sight-break-spacing` **closed on 2026-08-10**: 95 m of continuous cover became
> 12.5 m against a 14.4 m cap, on all eight roster seeds, and its waiver is deleted.
> One rule was also deleted outright in the §12 re-derivation (`RuleZoneDiagonal`,
> "구역 대각선 30~40m"), which is where part of the arithmetic went.

`MonsterChaseTests` is the only one of the three that measures the arithmetic against
real geometry. From [BLOCKERS.md B-001](../BLOCKERS.md#b-001), the two lines to hold a
change against:

```
monster speed    4.80 m/s of corridor, against §06's 4.8 m/s
gap opened at    0.80 m/s while sprinting, against §06's 0.8 m/s (5.6 − 4.8)
```

Correct to 1 % on real geometry. That is the design's most load-bearing claim —
「괴물이 달리기보다 0.3만 빠른 것이 핵심이다」 — and it is measured, not argued.

---

## 4. Adding a constant

1. **Put it in `GameConstants.cs`**, in the block for its design section.
2. **Cite the section in the doc comment, and say why the value is what it is.** If
   the design document does not state it, say so explicitly — the file already does
   this in several places, e.g. `MonsterSightRange` notes that "§06's state table
   names 시야 확보 as the way into 추격 but never numbers it, so the value is read off
   §12 instead" and calls itself "provisional in the §16 sense", and
   `PlayersPerMatch` spends two paragraphs saying its own doc comment used to be false.
   *(The `PROPOSED FIRST PASS` price block this step used to point at went with §08;
   there is no longer a string `PROPOSED FIRST PASS` in the file.)*
3. **Extend `Validate()`** if it has a relationship with another constant.
4. **Add or extend a test that asserts the reasoning**, not the value.
5. If your new number *contradicts* something the design document says, stop and read
   [Open questions](09-open-questions.md) — the answer is a finding, not a retune.

---

## 5. Values that are deliberately not in `GameConstants`

Not everything is a game rule. Four other authorities exist, and putting their
numbers in `GameConstants` would be as wrong as putting a speed in a prefab:

| Domain | Authority | Example |
|---|---|---|
| Audio mix and DSP | `Assets/Scripts/Audio/AudioTuning.cs` | `ListenerChannelOcclusionFloorHz` 800, `RolloffExponent` 0.6 — derived by measurement against the engine's own filter, see [F-003](09-open-questions.md) |
| The look | `Assets/Scripts/Rendering/*` and [ART.md §3](../ART.md) | `NightAtmosphere.ReflectionIntensity` 0.25, `FlashlightBeam.Intensity` 2.6, fog density `√(ln 2)/25` |
| Asset import policy | `Assets/Scripts/Editor/AssetImportPolicy.cs` | `PolicyVersion` — bump it and every governed asset reimports |
| Voice transport | `Assets/Scripts/Steam/Voice/VoiceTuning.cs` | codec and jitter-buffer settings |

The test is the same one from [the layering rule](02-layering-rule.md): *is this a
rule of the game, or a property of a representation?* `VoiceCutoffDistance = 30` is a
rule (§13 cuts transmission at the sender, so it decides who can hear whom) and lives
in `GameConstants`. The jitter-buffer depth is a representation and does not.

---

## 6. Read before changing

| Before you change | Read | Then run |
|---|---|---|
| a speed, a multiplier, a stamina figure | §06 and §05 of [game-design.md](../game-design.md) | `dotnet test`, then `MonsterChaseTests` |
| any §12 dimension, or the map's scale | §3 above, then [F-007](09-open-questions.md#f-007) and [F-013](../BALANCE-FINDINGS.md#f-013) | §12 validation, the **주자 테스트 band**, `MonsterChaseTests`, *and* `MapPipeline.RegenerateFromCommandLine` on **all eight roster seeds** — a change that only holds on the shipped seed is not a change that holds |
| a `ListenerClarity*` value | [F-002](09-open-questions.md) — the table is known to contradict the ears under occlusion, at **four** inverted pairs as of 2026-08-12 | `tools/audio/.venv/bin/python tools/audio/verify_audio.py`, section 6 |
| a threat-tier value | [F-010](../BALANCE-FINDINGS.md#f-010) — §07's patrol table is a *count of zones*, so it shrank when the building grew | `dotnet test core/HorrorGame.sln --filter "FullyQualifiedName~ThreatTests"` |

> 🔴 **Two rows were deleted from this table and one command was.** "A weight band or a
> price" pointed at §08's economy, which no longer exists — there is no `Inventory`, no
> `Core/Economy/`, no shop and no 전리품. And every "then run" that read
> `dotnet run --project core/HorrorGame.Sim -- run --matches 500` is unrunnable:
> `core/HorrorGame.Sim/` was deleted at `e8c67ae`, there is no `horrorsim`, and **there
> is no sweep tool in this repository at all**. Balance is answered by the core suite
> and by [BALANCE-FINDINGS.md](../BALANCE-FINDINGS.md) now, which is a real loss and is
> worth knowing before you plan a retune around a sweep you cannot run.
