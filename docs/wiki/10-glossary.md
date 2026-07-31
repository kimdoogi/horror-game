# Glossary — the design document's Korean, and the code that implements it

> [`docs/game-design.md`](../game-design.md) is written in Korean and is the authority.
> The code is in English. Identifiers stay English; Korean appears only inside
> comments, quoting the document. This page is the join.
>
> Paths are under `unity/HorrorGame/Assets/Scripts/`. Section marks (§NN) refer to the
> design document.

---

## The five roles (§04, §11)

| Korean | Code | Where | The constraint that defines it |
|---|---|---|---|
| **청음사** Listener | `ListenerAbility`, `RoleId.Listener` | `Core/Abilities/ListenerAbility.cs` | 자기가 소리를 내면 못 듣는다 — `GameConstants.ListenerSelfNoiseThreshold`. Its output is a *fix with an error radius*, never the truth: "a caller that wants the monster's real position is holding the wrong object" |
| **관측자** Observer | `ObserverAbility`, `RoleId.Observer` | `Core/Abilities/ObserverAbility.cs` | 15 m 이내 + 이동 정지 3초 — `ObserverRange` 15, `ObserverStillSeconds` 3. §05 settled that the 3 s pin the **feet, not the head**: mouselook is allowed and excluded from the stillness test |
| **주자** Runner | `RunnerAbility`, `RoleId.Runner` | `Core/Abilities/RunnerAbility.cs` | 질주 5.6 m/s for `SprintStaminaSeconds` 12. **Aggro release is deliberately not in this class** — §06 makes it the monster's decision; the brain calls `NotifyAggroReleased` |
| **정비공** Engineer | `EngineerAbility`, `EngineerAction` | `Core/Abilities/EngineerAbility.cs` | 시간과 자재, 사전 준비형. §04's Design Note: 실수가 아군을 죽인다 — **do not treat that as a bug and remove it** |
| **섬광수** Flasher | `FlasherAbility`, `RoleId.Flasher` | `Core/Abilities/FlasherAbility.cs` | `FlashStunSeconds` 2.5, `FlashCooldownSeconds` 18 — weak but reusable |

`RoleSelection` (`Core/Match/RoleSelection.cs`) implements §11's 5-choose-4. §11's
absolute rule: 필수 직업이 있으면 풀이 가짜가 된다.

---

## The monster (§06)

| Korean | Code | Note |
|---|---|---|
| **순찰** Patrol | `MonsterStateId.Patrol` | footsteps only; the footstep family owns its audio |
| **경계** Alert | `MonsterStateId.Alert` | `AlertGiveUpSeconds` 3 |
| **추격** Chase | `MonsterStateId.Chase` | `MonsterBrain.IsRoaring` is exactly this state |
| **수색** Search | `MonsterStateId.Search` | `SearchGiveUpSeconds` 15, `SearchRadius` 12 |
| **정지** Standstill | `MonsterStateId.Standstill` | **silent**: `MonsterBrain.IsAudible => _state != Standstill`. 「침묵이 가장 무서운 소리다」 — `Assets/Audio/Monster/` deliberately has no clip for it |
| **어그로 해제** aggro release | `MonsterBrain.StepChase` → `EnterSearch()` | `Core/Monster/MonsterBrain.cs:408` |
| **마지막 목격 위치** last seen position | `MonsterBrain.LastSeenPosition` | why the *direction* a Runner flees is a strategy: breaking aggro near the team delivers the monster to the team |

The release itself, verbatim from `MonsterBrain.cs`:

```csharp
if (_lineOfSightBrokenSeconds >= GameConstants.AggroReleaseLineOfSightBreak
    && separation >= GameConstants.AggroReleaseDistance)
{
    EnterSearch();
```

Two details in that method are load-bearing and easy to undo:

- **Regaining sight resets the cover timer to zero.** Two 2 s hides are worth nothing.
  That is what forces §12's 연속 차단.
- **`separation` is `Vec3.DistanceFlat`, not path distance.** Using path distance
  "would hand the release away for free the moment an S-corridor doubled it back on
  itself" — and §12 exists precisely because 12 m must be expensive.
- A target the host has stopped reporting (dead → ghost, or disconnected) is
  infinitely far away by definition, so the cover clause alone releases the monster
  instead of pinning it in Chase.

Adapters: `Gameplay/Monster/MonsterAgent.cs` steps the brain; `NavMeshWorldProbe`
answers its questions; `MonsterAnimationDriver`, `MonsterAudioDriver`,
`MonsterFootsteps`, `MonsterStandstillHold` present it. None of them decide anything.

---

## The map (§12)

| Korean | Code | Proven by |
|---|---|---|
| **S자 통로** S-corridor | `GameConstants.SCorridorLegLength` 10 m · `MapKitCatalogue.SCorridorUnit10mX2` · `Assets/Models/MapKit/SCorridor_Unit_10m_x2.fbx` · `MapGraph.FindSCorridor` · `MapValidator.RuleSCorridorPerZone` | `MonsterChaseTests.AnSCorridorOfTwoTenMetreLegsBreaksAChase` — released 5.50 s after aggro at 12.0 m, not caught. Its control is `ASingleCornerDoesNotBreakAChase` — same route, same aggro distance, one corner: **caught at 12.54 s** |
| **순환로** loop | `LoopsPerZoneMin` 1, `LoopsTotalMin` 3 · `MapValidator.RuleLoops` | 트리 구조는 사형선고 — and see [F-005](09-open-questions.md) |
| **막힌 길** dead end | `DeadEndRatioMin/Max` 0.20–0.25 · `RuleDeadEnds` · `DeadEnd_Cap.fbx` | each one must carry a reward |
| **개방 공간** open space | `Hall_Open_20x20.fbx` · `RuleOpenAdjacentToMaze` | must be adjacent to maze space — 두 성격의 공간이 인접해야 한다. See [B-003](../BLOCKERS.md#b-003): two of these are currently dropped on every generation |
| **미로 공간** maze space | corridor kit pieces + `Corridor_Corner_L`, `Junction_T`, `Junction_Cross_4Way` | |
| **관측 지점** observation post | `ObservationPost_Gallery`, `ObservationPost_BarredWindow` · `RuleObservationPosts` | 없으면 관측자는 죽으러 가야 한다 |
| **시야 차단 지점** sight-break point | `LineOfSightBreakSpacingMin/Max` 15–25 m | 질주 60 m에 3~4번의 기회 |
| **직선 통로 최대** longest straight | `MaxStraightCorridor` 20 m · `RuleStraightCorridor` | 넘으면 주자가 죽는다 |
| **구역 간 진입점** zone entry point | `ZoneEntryPointsMin/Max` 2–3 · `RuleZoneEntryPoints` | also the 0.5-occlusion case in [F-003](09-open-questions.md) |
| **바닥 재질** floor material | `FloorMaterial` {Wood, Tile, Gravel, Concrete, Metal} · `FloorTile_*.fbx` · `Audio/FloorSurfaces.cs` · `RuleFloorMaterials` | 아트 결정이 아니라 시스템 결정이다 |
| **후보 지점** candidate site | `SiteCatalog`, `SiteLabel` · `RuleCandidateSites` | 구역당 3개, all satisfying the same conditions |
| **은폐 지점** concealment | `Assets/Models/Props/HidingSpot_Locker.fbx` · `RuleConcealmentNearExit` | for §07's 새벽, when 괴물이 출입구를 안다 |
| **주자 테스트** runner test | `Core/Map/RunnerTest.cs` · census in `Editor/SceneGen/RunnerCensus.cs` | the 실전 검증 grade; §12 wants 5–7/10. **Currently 10/10 TooEasy — outside the band** ([F-007](09-open-questions.md#f-007)). The census says 164/164 places, so it is not an unlucky ten |
| **검증 체크리스트** checklist | `Core/Map/MapValidator.cs` — **16 rules** | necessary, not sufficient: `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy` |

---

## Clues and the objective (§03)

| Korean | Code | Note |
|---|---|---|
| **단서** clue | `ClueDef` (**`internal` to Core on purpose**), `ClueReader`, `ClueChain`, `ClueReport` | 반출 불가 — it cannot leave the room |
| **목표물** objective | `ObjectiveResolver` · `Gameplay/Interaction/ObjectivePropInteractable.cs` | its location has no getter; placement is a one-shot callback |
| **혼동쌍** confusion pair | `MisreadModel`, `ClueGlyph`, `GlyphViewing` · `GameConstants.ClueMisreadWeight*` | 6↔9, 1↔7, ㅁ↔ㅇ, 좌↔우 — 「이 게임의 주된 웃음이자 사망 원인」 |
| **어둠 = 잠금장치** darkness as the lock | `Core/Light/*`, `LightRules`, `FlashlightState`, `BatteryState` | 목표와 위험이 같은 스위치에 걸린다 |
| **부분 리셋** partial reset | `MatchState` / `Gameplay/Match/MatchDirector.cs` — surfacing resets the monster's aggro and position and leaves the clock running. Observed in [STATUS.md §1.4](../STATUS.md): `§03 partial reset — monster back at spawn (was 15.7 m away), clock untouched` | 나가는 것은 숨 돌리기이지 리셋이 아니다 |

Host side: `Net/Host/HostClueAuthority.cs` — the only place in the Net assembly
allowed to name a clue type, and it returns a rendered `string`. See
[Design decisions §3](08-design-decisions.md).

---

## Economy and time (§07, §08)

| Korean | Code |
|---|---|
| **전리품** loot | `LootId` {Trinket, Timepiece, SafeDocument, LargePiece}, `LootDefinition`, `DroppedLootField` |
| **무게 / 속도 저하** weight bands | `Inventory.SpeedMultiplier`, `Inventory.CanSprint`, `CarryLoad` · `WeightMul*` — see [F-001](09-open-questions.md) |
| **2인 운반** two-person carry | `SharedLootCarry` · `SharedCarryMaxCarriers` 2 |
| **금고** safe | `LootSafe` · `Gameplay/Interaction/LootSafeInteractable.cs` · `EngineerSafeSeconds` 8 |
| **공용 지갑** shared wallet | `Wallet` — 돈이 개인 것이면 협동 게임이 아니게 된다 |
| **상점 / 지상 차량** shop | `Shop`, `ShopItemDefinition`, `ShopItemId` · `UI/Screens/ShopScreen.cs` · `SurfaceVehicleInteractable` |
| **시간 = 위협도** | `MatchClock`, `ThreatCurve`, `ThreatTier` |
| **초저녁 / 밤 / 심야 / 새벽 / 동트기 전** | `NightPhase` {EarlyEvening, Night, LateNight, PreDawn, BeforeSunrise} — speeds 4.4 / 4.6 / 4.8 / 5.0 / 5.2 |

---

## Outcomes, death, and the ghost (§02, §09)

| Korean | Code |
|---|---|
| **완전 승리 / 부분 승리 / 생존 / 패배** | `MatchOutcome` {FullVictory, PartialVictory, Survived, Wiped} + `InProgress`, `Abandoned` · `OutcomeEvaluator` |
| **유령** ghost | `GhostState` · `UI/Screens/GhostOverlay.cs`, `GhostFreeCamera.cs` |
| **물건을 흔든다** rattle | `GhostRattle` · `GhostRattleCooldownSeconds` 45, `GhostRattleRange` 4 · `Assets/Audio/UI/ghost_rattle_01..04.wav` (**mono, positional**) |

---

## Movement and the peek (§05)

| Korean | Code |
|---|---|
| **속도 배율표** | `SpeedResolver.Resolve` / `.DirectionalMultiplier` · `MulForward/Diagonal/Strafe/Backward` |
| **45도 곁눈질** the peek | `PeekAngleDegrees` 45, `MulDiagonal` 0.95. The multipliers are knots on a **continuous** curve, not four buckets — §05 insists the trade is analogue |
| **뒷걸음** backpedal | `MulBackward` 0.65 — 뒤를 보면 잡힌다. Pinned twice in `GameConstants.Validate()` |
| **스태미나** | `StaminaState` · `SprintStaminaSeconds` 12, `SprintStaminaRecoverySeconds` 20 |
| **질주 최대 이동 거리** | `SprintMaxTravelDistance` 60 m — revised down from 67 by §05, see [F-004](09-open-questions.md) |
| **근접 음성** proximity voice | `Steam/Voice/*` · `VoiceCutoffDistance` 30 m, cut **at the sender** |

---

## Terms that appear in tooling and logs

| Term | Means |
|---|---|
| `ASSET_REPORT` / `ASSET_FAILED` | the markers every Blender generator prints. Trust these, not the exit code |
| `주자 테스트 10/10, TooEasy` | `RunnerTest`'s grade band — §12 wants 5–7/10, so this one is a failing grade, not a good score |
| `164/164 escapable` | `RunnerCensus` — the same test from every place instead of §12's ten samples |
| `[ChaseTest]` | `MonsterChaseTests` log prefix — the §14 Q1 measurement |
| `[NavMeshAudit]` | connectivity only. Necessary, **not** sufficient — [expensive bugs](07-expensive-bugs.md) |
| `[SoloPlaytest]` | `SoloPlaytest.VerifyBatch`, the §01 loop end to end |
| `crushed / legible / blown` | ART.md's luminance bands, measured by `tools/render/frame_stats.py` |
| `contrast / coverage / peak` | `MonsterShot.StageBatch`'s three gates for "can you see the monster at 15 m" |
| `F-00N` | a balance finding — [open questions](09-open-questions.md) |
| `B-00N` | a blocker — [BLOCKERS.md](../BLOCKERS.md) |
| `§NN` | a section of [game-design.md](../game-design.md) |
