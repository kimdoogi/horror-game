# Where every number lives

> `unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs` is the single authority for
> every tuned value in this game. **A literal like `4.8f` anywhere else is a bug.**

184 public constants, 1137 lines, one file. Verify the count yourself:

```bash
grep -cE 'public (const|static readonly)' unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs
```

---

## 1. How the file is organised

Blocks in design-section order, each with a `// §NN —` banner. Find a block with:

```bash
grep -nE '^\s+// §' unity/HorrorGame/Assets/Scripts/Core/GameConstants.cs
```

| Block | Contains |
|---|---|
| §06 — Speed relationships | `WalkSpeed 2.0` `RunSpeed 4.5` `MonsterBaseSpeed 4.8` `RunnerSprintSpeed 5.6` |
| §05 — Directional multipliers | `MulForward 1.00` `MulDiagonal 0.95` `MulStrafe 0.90` `MulBackward 0.65` and the angles they sit at |
| §05 — Field of view | `FovDefault 80` `FovMin 70` `FovMax 90` — "a balance value, not a comfort setting" |
| §06 — Aggro, stamina, release | `AggroReleaseDistance 12` `AggroReleaseLineOfSightBreak 3` `SprintStaminaSeconds 12` `SprintMaxTravelDistance 60` |
| §06 — State machine timings | `AlertGiveUpSeconds 3` `SearchGiveUpSeconds 15` `StandstillSeconds 5` `SearchRadius 12` `MonsterSightRange 20` |
| §08 — Weight → movement | `WeightFreeMax 5` `WeightMulLight 0.85` `WeightMulHeavy 0.70` `WeightMulOverloaded 0.55` |
| §08 — 전리품 weights | the 무게 column verbatim: trinket 1, timepiece 1, safe document 2, large piece 5 |
| §08 / §16-2 — values and prices | **marked `PROPOSED FIRST PASS`** — §16-2 calls the price table the project's top open question and supplies no numbers |
| §03 / §05 — Objective carrying | `ObjectiveCarrySpeedMultiplier 0.80` `ObjectiveWeight 4` `ObjectiveEscortMinPlayers 2` |
| §04 — Role parameters | `ObserverRange 15` `ObserverStillSeconds 3` `ListenerClarity*` `FlashStunSeconds 2.5` `Engineer*` |
| §03 / §08 — Light and battery | `FlashlightRange 12` `FlashlightHalfAngle 22` `BatterySecondsPerCell 210` `LateNightFlashlightPenalty 0.30` |
| §07 — Threat curve | `ThreatTierSeconds 480` and the five speeds 4.4 / 4.6 / 4.8 / 5.0 / 5.2 |
| §09 — Ghost | `GhostRattleCooldownSeconds 45` `GhostRattleRange 4` |
| §13 — Networking and voice | `VoiceCutoffDistance 30` `PlayersPerMatch 4` `NetworkSendRate 30` |
| §13 — Telemetry buckets | the histogram geometry §13 writes out literally |
| §12 — Map rules | **derived from the numbers above** — see §3 of this page |
| §03 — Randomisation and 혼동쌍 | the confusion pairs and their weights |
| Simulation | `FixedStep = 1f / 50f` |

---

## 2. The tests assert the design's *reasoning*, not the values

This is the part that makes the file worth having. `GameConstants.Validate()` (line
~1073) is a list of relationships, each with the sentence that explains why it must
hold. A retune that breaks one throws on startup and fails the suite. Verbatim
examples:

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

`Validate()` is called by the test suite and by the simulator on startup, so a broken
relationship cannot reach a 500-match run and quietly bias its numbers.

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
SprintMaxTravelDistance   60 m     = 5.6 × 0.95 (the §05 peek) × 12 s, revised down from 67 m by §05
```

So:

> **If you change a speed, you have changed the map. If you change the map's scale,
> you have changed whether the escape maths still works.**

Both directions are guarded, and both guards must be run:

| Guard | Command | What it would catch |
|---|---|---|
| `GameConstants.Validate()` | `dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj` | a speed change that breaks §12's derived inequalities |
| §12 validation, 16 rules | `MapSceneGenerator.ReportQualityMenu` — [Verifying §3](06-verifying.md) | a map that no longer satisfies the rules |
| `MonsterChaseTests`, 4 tests | PlayMode, `-testFilter "MonsterChaseTests"` | a map on which two 10 m legs no longer break a chase, or one corner now does |

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
   §12 instead", and the §08 price block is labelled `PROPOSED FIRST PASS` because
   §16-2 has not decided.
3. **Extend `Validate()`** if it has a relationship with another constant.
4. **Add or extend a test that asserts the reasoning**, not the value.
5. If your new number *contradicts* something the design document says, stop and read
   [Open questions](09-open-questions.md) — the answer is a finding, not a retune.

---

## 5. Values that are deliberately not in `GameConstants`

Not everything is a game rule. Three other authorities exist, and putting their
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
| any §12 dimension, or the map's scale | §3 above, then [F-006](09-open-questions.md#f-006) and [F-007](09-open-questions.md#f-007) | §12 validation, the **주자 테스트 band**, `MonsterChaseTests`, *and* a re-run of the 500-match simulator — the map is compiled into it, so every balance number moves with the building |
| a weight band or a price | [F-001](09-open-questions.md) and [F-006](09-open-questions.md#f-006) — the economy now runs (0.70 of one of everything earned), so §16-2 is measurable for the first time, and every measurement taken before 2026-08-01 was taken on the wrong map | `dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1` |
| a `ListenerClarity*` value | [F-002](09-open-questions.md) — the table is already known to be wrong under occlusion | `tools/audio/verify_audio.py` |
| a threat-tier value | [F-006](09-open-questions.md#f-006) — all five tiers are now reached (심야 33.6%, 새벽 17.4%, 동트기 전 13.0%), so changing one changes content players see | the simulator |
