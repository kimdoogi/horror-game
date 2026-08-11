# Glossary — the design document's Korean, and the code that implements it

> [`docs/game-design.md`](../game-design.md) is written in Korean and is the authority.
> The code is in English. Identifiers stay English; Korean appears only inside
> comments, quoting the document. This page is the join.
>
> Paths are under `unity/HorrorGame/Assets/Scripts/`. Section marks (§NN) refer to the
> design document.
>
> **Every symbol on this page was checked against the artefact on 2026-08-12 at HEAD
> `017b489`.** The 2026-08-02 pivot deleted whole subsystems from the code, and a glossary
> that names a class which is not on disk is worse than no glossary — it sends a reader
> looking for a file. Deleted terms are kept, marked, and paired with what took their
> place, because the *design document still uses the Korean* in its deletion notices.

---

## What everyone has (§04, §11)

**스무 명이 완전히 같은 몸으로 출발한다.** 같은 속도, 같은 손전등, 같은 손, 같은 귀.

| Korean | Code | Where | The constraint that defines it |
|---|---|---|---|
| **손전등** flashlight | `FlashlightState`, `LightCone` | `Core/Light/` | 켜고 끈다, 관리할 것이 없다 (§03). The cost is on **both** sides of the switch: lit, 괴물이 본다 and 남들이 내가 어디 있는지 안다; unlit, §10's 그늘이 고인다 |
| **문** door | `DoorState`, `DoorInteractable` | `Core/Map/DoorState.cs`, `Gameplay/Interaction/` | **경주에 남은 유일한 상호작용.** 닫는 데 1.1 s · 부수는 데 4.5 s · 부서진 문은 다시 닫히지 않는다 (§12-B) |
| **질주** sprint | `StaminaState`, `SpeedResolver` | `Core/Movement/` | 5.6 m/s for `SprintStaminaSeconds` 12, recovering in `SprintStaminaRecoverySeconds` 20. §04 names the decision it creates: **「지금 쓸까, 관문에서 쓸까」** |
| **귀** ears | `FloorMaterial`, `Audio/FloorSurfaces.cs`, `SoundOccluder` | `Core/Map/`, `Audio/` | 발소리는 누구에게나 들린다. 바닥 재질에 따라 다르게 — **여덟 표면, 층마다 하나** (§01) |
| **최대 20인** | `NetLobbySeat`, `NetRaceStartPoints`, `RaceState` | `Net/`, `Core/Race/` | 관문 수는 인원과 무관하게 고정 (4 / 2 / 1), so 인원이 늘수록 병목이 심해진다. **관문 수를 인원에 맞춰 늘리면 안 된다** (§11) |

> 🔴 **History — the five roles, and the one line of §11 that outlived them.** §04 is now
> 「직업 — 삭제됨」 and every class below is **absent from disk**, along with the whole
> `Core/Abilities/` folder:
>
> | Korean | Was | Its defining constraint, which is why it is worth remembering |
> |---|---|---|
> | **청음사** Listener | `ListenerAbility`, `RoleId.Listener` | 자기가 소리를 내면 못 듣는다 — `GameConstants.ListenerSelfNoiseThreshold` **0.35, still in the code**. Its output was a *fix with an error radius*, never the truth |
> | **관측자** Observer | `ObserverAbility` | 15 m 이내 + 이동 정지 3초. §05 settled that the 3 s pinned the **feet, not the head**: mouselook was excluded from the stillness test |
> | **주자** Runner | `RunnerAbility` | 질주 5.6 m/s. **Aggro release was deliberately not in this class** — §06 makes it the *monster's* decision, and it still is |
> | **정비공** Engineer | `EngineerAbility` | 시간과 자재, 사전 준비형. §04's Design Note: 실수가 아군을 죽인다 |
> | **섬광수** Flasher | `FlasherAbility` | `FlashStunSeconds` 2.5 — weak but reusable. The stun survives as `MonsterStunSeconds` |
>
> `RoleSelection` implemented §11's 5-choose-4 and is gone with them. **§11's absolute
> rule survives in a stronger form:** 필수 직업이 있으면 풀이 가짜가 된다 became 「스무 명이
> 같은 몸이어야 한다」, and §04's argument is that a pick makes a loss explainable by the
> pick — 「진 사람은 자기 선택이 아니라 자기 픽에서 이유를 찾고, 그러면 다음 판에 고칠 것이
> 없다」. Two constants that were role-scoped are now everyone's, and that is the pattern
> to expect: **ListenerSelfNoiseThreshold** and the sprint.

---

## The monster (§06)

| Korean | Code | Note |
|---|---|---|
| **순찰** Patrol | `MonsterStateId.Patrol` | footsteps only; the footstep family owns its audio |
| **경계** Alert | `MonsterStateId.Alert` | `AlertGiveUpSeconds` 3 |
| **추격** Chase | `MonsterStateId.Chase` | `MonsterBrain.IsRoaring` is exactly this state |
| **수색** Search | `MonsterStateId.Search` | `SearchGiveUpSeconds` 15, `SearchRadius` 12 |
| **정지** Standstill | `MonsterStateId.Standstill` | **silent**: `MonsterBrain.IsAudible => _state != Standstill`. 「침묵이 가장 무서운 소리다」 — `Assets/Audio/Monster/` deliberately has no clip for it |
| **어그로 해제** aggro release | `MonsterBrain.StepChase` → `EnterSearch()` | `Core/Monster/MonsterBrain.cs` — the test at line 429, the call at 432, `EnterSearch` itself at 524 |
| **덮치기** lunge | `MonsterLunge` | `Core/Monster/MonsterLunge.cs`. Tuned so 달리는 사람(4.5)은 잡고 질주하는 사람(5.6)은 놓친다 — the reason §04 could not delete the sprint |
| **마지막 목격 위치** last seen position | `MonsterBrain.LastSeenPosition` | why the *direction* you flee is a strategy. §10 makes it a dilemma row of its own: 어그로를 떼어내면 떼어낸 쪽에 있는 사람에게 **배달된다** — in the race that is a weapon rather than a betrayal |

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
- A target the host has stopped reporting (sent home, or disconnected) is
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
| **막힌 길** dead end | `DeadEndRatioMin/Max` 0.20–0.25 · `RuleDeadEnds` · `DeadEnd_Cap.fbx` | 152 of them per building. **They no longer carry a reward** — §12-D re-declares them 도달성 프로브: markers the NavMesh audit pairs, not loot spawns. Do not change the count |
| **개방 공간** open space | `RuleOpenAdjacentToMaze` | 두 성격의 공간이 인접해야 한다. On 하강 the one 개방 공간 per storey is the **중심 chamber** (`Chamber_Open_3x3.fbx`), and what §12-C asks of it is that the 투하구 choice be visible. `Hall_Open_20x20.fbx` is still in the kit and is no longer placed — which is how [B-003](../BLOCKERS.md#b-003) closed |
| **미로 공간** maze space | corridor kit pieces + `Corridor_Corner_L`, `Junction_T`, `Junction_Cross_4Way` | |
| **동심 3겹 · 관문** rings and gates | `RadialStorey`, `DescentMap` · `Editor/SceneGen/` | 외곽 → 중간 4개 → 안쪽 2개 → 중심 1개. **마지막 관문은 하나** (§12-A). No validator rule measures the gate counts — §12-D's 「누가 재는가」 column says **아무도** |
| **투하구** chute | `ChamberDockProbe` · `Editor/SceneGen/ChamberDockProbe.cs` | 층마다 2~3개, each landing at a fixed 외곽 point of the storey below. 무작위가 아니므로 맵을 아는 사람이 유리하다 (§12-C) |
| **시야 차단 지점** sight-break point | `LineOfSightBreakSpacingMin/Max` 15–25 m · `RuleSightBreakSpacing` · span cap `SingleCornerMinDistance` 14.4 m | 질주 60 m에 3~4번의 기회. **160 지점, deepest 12.5 m, 160/160 inside the band** on all eight roster seeds since 2026-08-10 ([B-007](../BLOCKERS.md#b-007) closed) |
| **직선 통로 최대** longest straight | `MaxStraightCorridor` 20 m · `RuleStraightCorridor` | 넘으면 주자가 죽는다. Currently measures **20.0 m against the 20 m cap with no slack** |
| **구역 간 진입점** zone entry point | `ZoneEntryPointsMin/Max` 2–3 · `RuleZoneEntryPoints` | also the 0.5-occlusion case in [F-003](09-open-questions.md#f-003) |
| **바닥 재질** floor material | `FloorMaterial` {Wood, Tile, Gravel, Concrete, Metal, **Water, Earth, Carpet**} · `Audio/FloorSurfaces.cs` · `RuleFloorMaterials` | 아트 결정이 아니라 시스템 결정이다. **Eight now, one per storey** (§01) — the three extra 「extend the alphabet rather than the rule」. Note `Assets/Models/MapKit/` still carries only the original five `FloorTile_*.fbx` |
| **외곽에서 중심까지** centre path | `CentrePathMetresMin/Max` 90–140 m · `RuleCentrePath` | §12-D's only numbered rule, added 2026-08-05. **It gates and the map fails it** — 21 of 22 entry points are inside the band and one is not, waived by name in `MapSceneGenerator.KnownFailingRules` ([B-019](../BLOCKERS.md#b-019)) |
| **주자 테스트** runner test | `Core/Map/RunnerTest.cs` · census in `Editor/SceneGen/RunnerCensus.cs` | the 실전 검증 grade; §12 wants 5–7/10 and it reads **10/10 TooEasy, 680/680 escapable**. [F-013](../BALANCE-FINDINGS.md#f-013) shows **no §12-legal map can ever score inside that band** — treat the grade as retired, not as a target |
| **검증 체크리스트** checklist | `Core/Map/MapValidator.cs` — **14 rules** | necessary, not sufficient: `MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy` |

> 🔴 **Three §12 rules were deleted with the systems they gated, and their kit pieces are
> still on disk.** `RuleObservationPosts` (관측 지점, 없으면 관측자는 죽으러 가야 한다),
> `RuleCandidateSites` (후보 지점, 구역당 3개) and `RuleConcealmentNearExit` (은폐 지점,
> for §07's 새벽) went when §04's roles, §03's clue chain and the light economy went.
> `MapValidator`'s own comment is the right reading: *"a rule that counts places for a
> role nobody plays is not a weakened gate; it is a gate on a door that has been removed
> from the building."* `ObservationPost_Gallery.fbx` and `ObservationPost_BarredWindow.fbx`
> are still in `Assets/Models/MapKit/`; **there is no `HidingSpot_Locker.fbx`.**
>
> **`RuleZoneDiagonal` (구역 대각선 30–40 m) went for a different reason** and it is the
> more interesting one: the band sized a 구역 as a *sub-area of a floor*, and on 하강
> **a 구역 IS a 층** — eight zones, eight storeys, one surface each. Both of §12's own
> justifications for it invert at that scale. `GameConstants.ZoneDiagonalMin/Max` stay,
> because six live systems read them as a reference distance and none of them is making a
> §12 claim.

---

## Darkness (§03)

| Korean | Code | Note |
|---|---|---|
| **고리별 조명** ring lighting | `Editor/Rendering/ZoneIdentity.cs`, `PracticalGlow.cs` | 외곽 상시 켜짐 · 중간 꺼져 있음 · 안쪽 손전등 사거리 바깥은 존재하지 않는다 · 중심 투하구만 빛난다 |
| **손전등** flashlight | `FlashlightState`, `LightCone` · `Core/Light/` | 그냥 켜진다. 관리할 것이 없다 — 「유지해야 하는 빛은 심부름이고, 들고 다니는 빛은 게임이다」 |
| **빛이 있다 / 없다** the one brightness | `GameConstants.MinSafeLightQuality` **0.20** · `PresenceDensity.SafeLightQuality` | **This constant was `ClueMinReadableLightQuality`.** The reading half is deleted; the 그늘 half is live and on the hot path, so it was renamed rather than dropped. One threshold on purpose — two brightnesses would be unlearnable |
| **밝기 슬라이더 ±20 %** | `UI/Settings/SettingsService.cs` | 어둠은 규칙이다. 두 배로 밝히면 안쪽 고리를 손전등 없이 읽고, 절반이면 15 m 실루엣이 사라진다 |
| **그늘** the shade (§10) | `Core/Presence/` — `PresenceField`, `PresenceDensity`, `PresenceState`, `PresenceStage`, `PresenceReading`, `PresenceToll` · `Gameplay/Presence/` | 가득 차는 데 45 s (`PresenceSaturationSeconds`), 빛으로 지우는 데 15 s (`PresenceDispersalSeconds`), 경고 60 % (`PresenceWarnPooling`), 괴물 반경(`MonsterSightRange` 20 m) 안에는 **없음**. Its price is 목소리 — 12 s, which is `SprintStaminaSeconds` |

> 🔴 **The whole of §03's old vocabulary is deleted from disk** — 단서 (`ClueDef`,
> `ClueReader`, `ClueChain`, `ClueReport`), 목표물 (`ObjectiveResolver`), 혼동쌍
> (`MisreadModel`, `ClueGlyph`, `GlyphViewing`), 후보 지점 (`SiteCatalog`, `SiteLabel`),
> the light economy (`BatteryState`, `LightRules`, `LightField`, `Flare`), and the host
> side that protected them (`HostClueAuthority`). §03 is now 「어둠과 시야」 and the reason
> is one line: **목적지가 처음부터 알려져 있다: 아래.** A race has no answer to hide.
>
> **What survived is the sentence the clue chain was built to serve** — 「어둠 = 잠금장치,
> 목표와 위험이 같은 스위치에 걸린다」 — and it survived by having its *unlit* side priced.
> §03 admits the old version was broken: 값은 켠 쪽에만 매겨져 있었고, 「최적 전략은 언제나
> 꺼놓고 다니기였고, 딜레마는 딜레마가 아니었다」. 그늘 is the other side of that switch.
>
> **부분 리셋** is gone with 왕복 — there is no surfacing to reset from. Its nearest
> descendant is what the creature now does, below.

---

## Time and threat (§07)

| Korean | Code |
|---|---|
| **시간 = 위협도** | `MatchClock`, `ThreatCurve`, `ThreatTier` · `Core/Match/MatchClock.cs`, `Core/Threat/` |
| **초저녁 / 밤 / 심야 / 새벽 / 동트기 전** | `NightPhase` {EarlyEvening, Night, LateNight, PreDawn, BeforeSunrise} — `ThreatSpeed*` 4.4 / 4.6 / 4.8 / 5.0 / 5.2 m/s, `ThreatTierSeconds` 8 min each |
| **순찰 반경** patrol scope | `PatrolScope` · 괴물은 중심에서 시작해 안쪽 두 고리를 돈다. 외곽은 안전하고 중심은 위험하다 (§12-B③) |
| **지각자 처벌** | 늦게 갈수록 괴물이 빠르다 — the race's use for §07, and why 시간 초과 exists at all (§02) |

> 🔴 **§08 (경제) is deleted in full and every symbol this section used to list is absent
> from disk:** `LootId`, `LootDefinition`, `DroppedLootField`, `Inventory`, `CarryLoad`,
> `SharedLootCarry`, `LootSafe`, `Wallet`, `Shop`, `ShopItemId`, `ShopItemDefinition`, and
> the whole `Core/Economy/` folder. 경주에 재보급이 없고 통화도 없다. **`MovementContext`
> now has exactly two fields, `BaseSpeed` and `LoadMultiplier`, and `LoadMultiplier` is
> the *stance* multiplier — not carry weight.** See [F-001](09-open-questions.md#f-001)
> for the arithmetic that made the weight table worth deleting rather than tuning.
>
> §07's own line, 「시간이 유일한 통화다」, was written when there was a second currency to
> contrast with. It is now literally true.

---

## Finishing, and what being caught costs (§02, §09)

| Korean | Code |
|---|---|
| **승리 / 완주 / 탈락** | `RacerStatus` {Running, Finished, Eliminated} · `RaceState` (`Core/Race/RaceState.cs`) · `Racer.TimesCaught` |
| **순위 판정은 호스트가 한다** | `RaceState.ReportDescent` / `ReportFinish` / `ReportCaught` / `Standings()` · `Net/Race/NetRace.cs` counts `DescentsAccepted` against `DescentsRefused`. 화면 쪽에는 「도착했다」고 말할 수 있는 메서드 자체가 없어야 한다 |
| **B1으로 돌려보내진다** sent home | `RaceState.ReportCaught` — resets `Storey` to 0, increments `TimesCaught`, and leaves status `Running` · `Assets/Audio/UI/caught_sent_home.wav` |

> 🔴 **A live contradiction between the code and the design document, worth knowing about
> before you quote either.** `game-design.md` §02 says 탈락 | 괴물에게 잡힌다. 순위 없음.
> **유령이 되어 남은 경주를 본다**, and §01 says 잡히면 끝이다, 부활이 없다. **The code
> does something else.** `RaceState.ReportCaught` sends a caught runner back to the rim of
> B1 and they keep running; `RacerStatus.Eliminated` is explicitly *"a seat that emptied,
> not a runner the creature caught"* and exists so `RaceState.Over` can resolve when
> somebody disconnects. There is **no `GhostState`, no `GhostOverlay`, no
> `GhostFreeCamera`, no `GhostRattle`** and no `ghost_rattle_*.wav` on disk;
> `GameConstants` records §09 as 「NO CONSTANTS, and that is the finding」 and names
> `GhostRattleCooldownSeconds` (45 s) and `GhostRattleRange` (4 m) as **GONE**. The audio
> that shipped instead is `caught_sent_home.wav`. **One of the two has to move**, and it
> is not a wiki page's call which.

---

## Movement and the peek (§05)

| Korean | Code |
|---|---|
| **속도 배율표** | `SpeedResolver.Resolve` / `.DirectionalMultiplier` · `MulForward/Diagonal/Strafe/Backward` |
| **45도 곁눈질** the peek | `PeekAngleDegrees` 45, `MulDiagonal` 0.95. The multipliers are knots on a **continuous** curve, not four buckets — §05 insists the trade is analogue |
| **뒷걸음** backpedal | `MulBackward` 0.65 — 뒤를 보면 잡힌다. Pinned twice in `GameConstants.Validate()` |
| **스태미나** | `StaminaState` · `SprintStaminaSeconds` 12, `SprintStaminaRecoverySeconds` 20 |
| **질주 최대 이동 거리** | `SprintMaxTravelDistance` 60 m — revised down from 67 by §05, see [F-004](09-open-questions.md#f-004) |
| **근접 음성** proximity voice | `Steam/Voice/*` · `VoiceCutoffDistance` 30 m, cut **at the sender**. 대역폭 절감보다 도청 방지가 본질이다 — and §13 adds that 경주에서 도청은 협동판보다 훨씬 큰 이득이므로 이 결정은 더 중요해졌다 |
| **1인칭 손** first-person hands | `Gameplay/Player/` viewmodel · pinned by `PlayerFirstPersonViewTests` and `PlayerWorldArmsTests`. A dedicated viewmodel (`b92ae78`), not a third-person body seen from inside |

---

## Terms that appear in tooling and logs

| Term | Means |
|---|---|
| `ASSET_REPORT` / `ASSET_FAILED` | the markers every Blender generator prints. Trust these, not the exit code |
| `주자 테스트 10/10, TooEasy` | `RunnerTest`'s grade band. §12 wants 5–7/10, so it reads as a failing grade — but [F-013](../BALANCE-FINDINGS.md#f-013) shows **no §12-legal map can score inside that band.** It is a retired instrument, not a target |
| `680/680 escapable` | `RunnerCensus` — the same test from every place instead of §12's ten samples. 85 places on each of eight storeys |
| `탈출 대가` | F-013's replacement for the grade: what a chase *costs* in §07's currency. 680 chases, median 7.5 s, against a 3.4–20 s band |
| `[ChaseTest]` | `MonsterChaseTests` log prefix — the §14 Q1 measurement. Now asks *"on its own storey"*: the creature cannot climb a 투하구 |
| `[NavMeshAudit]` | connectivity only. Necessary, **not** sufficient — [expensive bugs](07-expensive-bugs.md). `islands 8` is correct on this map even though the tool prints `← the surface is in pieces` ([B-014](../BLOCKERS.md#b-014)) |
| `[SoloPlaytest]` | `SoloPlaytest.BuildBatch` — rebuilds `Map_FirstSketch_Solo.unity` from the generated map and reads §05's animation wiring back off disk. **`SoloPlaytest.VerifyBatch` was deleted with the co-op loop**; `VerifyBatch` on `BootstrapSceneGenerator` is a different check |
| `KNOWN MAP DEFECTS` | `MapSceneGenerator.KnownFailingRules` waived a §12 rule by name and wrote the map anyway. One entry today: `centre-path` ([B-019](../BLOCKERS.md#b-019)) |
| `crushed / legible / blown` | ART.md's luminance bands, measured by `tools/render/frame_stats.py` |
| `contrast / coverage / peak` | `MonsterShot.StageBatch`'s three gates for "can you see the monster at 15 m" |
| `MONO-FALLBACK-DO-NOT-SHIP.txt` | `BuildPipelineBackend.FallbackMarkerFileName`. A *release* build wrote it after falling back to Mono. Never ship a build that produced it — [B-015](../BLOCKERS.md#b-015) |
| `F-0NN` | a balance finding — [open questions](09-open-questions.md). There are thirteen |
| `B-0NN` | a blocker — [BLOCKERS.md](../BLOCKERS.md). There are twenty-one |
| `§NN` | a section of [game-design.md](../game-design.md), which is **v1.1** |
