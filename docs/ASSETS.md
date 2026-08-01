# ASSETS — what exists, what it is for, and how to rebuild it

Everything in `Assets/Audio` and `Assets/Models` is **procedurally generated** from the
scripts in `tools/`. Nothing is sampled, downloaded or licensed from a third party —
§13 ships this on Steam and an asset of unclear provenance is a legal problem rather
than a mixing problem. Every file is deterministic from a seed, so a clean rebuild is
byte-identical and a diff after regeneration means something actually changed.

Verified on disk 2026-07-30 by `tools/audio/verify_audio.py` and by loading every
model in headless Blender.

| | count | size | notes |
|---|--:|--:|---|
| `Assets/Audio/**.wav` | **170** | 87.20 MB | all 48 kHz, 16-bit PCM; 130 positional, 40 non-diegetic |
| `Assets/Models/**.fbx` | **47** | 6.45 MB | 32,948 triangles total |
| `Assets/Models/**.glb` | **2** | 0.76 MB | preview copies of the two characters |
| manifests | 2 | — | `Monster/monster_audio.manifest.json`, `MapKit/MapKit.manifest.json` |

No generator reported a file that is not on disk, and no file on disk is unaccounted
for by a generator.

---

## 1. The setting that silently breaks the Listener

Read this before importing anything.

> **Every positional clip must be imported mono and played on a 3D AudioSource.**

Unity does not spatialise a stereo `AudioClip`. A 2-channel clip on a 3D
`AudioSource` plays at a fixed level with no distance attenuation and no panning —
it does not error, it does not warn, it just plays. The consequences:

* **§04's 청음사 stops working.** The role's whole ability is reading the monster's
  위치 · 거리 · 이동 방향 from sound. If footsteps do not attenuate, the monster is
  equally loud from everywhere and there is no distance and no direction to read.
* **§13's proximity voice stops working.** §13's design rests on one trick —
  "음성을 3D 오디오 소스로 재생하면 근접 음성이 자동으로 된다. 거리 계산 로직이
  필요 없다." A stereo voice clip means every teammate is at zero metres.
* **§09's ghost loses its only channel.** The ghost cannot speak; it rattles a nearby
  object. If the rattle does not localise, the signal carries no information.

All 130 positional clips currently ship mono. There is nothing to fix — the job is
to not break it at import. There are **no `.meta` files in the repo yet**, so Unity
has never imported these assets and every import setting is currently at its default.
Unity's defaults are wrong for this project in two ways: `AudioSource.spatialBlend`
defaults to **2D (0.0)**, and `Force To Mono` defaults to **off**.

### Unity import settings that matter

**Audio — positional clips** (`Footsteps/*`, `Monster/*`, `Items/*` except the two
UI clips below, `Ambience/sfx_*`, `Ambience/amb_generator_hum_loop`,
`UI/ghost_rattle_01..04`):

| setting | value | why |
|---|---|---|
| Force To Mono | **on** (already mono; keep the guard) | stereo silently disables spatialisation |
| `AudioSource.spatialBlend` | **1.0 (3D)** | the default of 0.0 is 2D and gives no attenuation |
| Load Type | Decompress On Load (short one-shots) / Streaming (long loops) | footsteps must not hitch |
| Compression | Vorbis, quality ~70 | ADPCM smears the high band gravel's identity lives in |
| Preload Audio Data | on for footsteps | first step must not be the one that is late |
| Loop | **on** for every `*_loop` clip, `monster_presence_bed`, `monster_breath_loop_*` | |

**Audio — non-diegetic clips** (`UI/*` except the four ghost rattles,
`Ambience/amb_zone_*`, `amb_stairwell_metal_loop`, `amb_surface_vehicle_loop`,
`amb_tension_t*`, `Items/shop_purchase_confirm`, `Items/loot_sell_credit`,
`Presence/pre_*` — all four):

> The four `Presence/pre_*` clips are non-diegetic **because the 그늘 has no position**
> (§10). Every other rule in this file sorts a clip by where the sound is coming from;
> these are the one set with nowhere to come from, so they are 2D by construction rather
> than by taste. `pre_gathering_loop` and `pre_close_loop` are the pool filling,
> `pre_taken` is the toll, `pre_return` is the voice coming back.
> They ship, and — see STATUS.md §1.11 — nothing plays them yet.

| setting | value |
|---|---|
| Force To Mono | **off** — these are stereo on purpose |
| `AudioSource.spatialBlend` | **0.0 (2D)** |
| Loop | on for the twelve ambience beds |

**Models — all FBX:**

| setting | value | why |
|---|---|---|
| Scale Factor | 1, Convert Units **on** | measured heights are already correct in metres (Player 1.750 m, Monster 2.336 m) |
| Import Cameras / Lights | off | none present; keeps the hierarchy clean |
| Mesh Compression | off | these are 200–2500 triangle meshes; compression buys nothing and costs precision |
| Read/Write Enabled | off unless a script needs mesh data | |
| Generate Colliders | off | props need authored colliders, not per-triangle ones |

**Models — `Player.fbx`:** Animation Type **Humanoid**. The 26-bone rig uses standard
humanoid names (`Hips / Spine / Chest / UpperChest / Neck / Head / Left…`) and maps
cleanly to a Unity Avatar. The four non-standard bones — `HeadCameraAnchor`,
`FlashlightMount`, `ObjectiveMount`, `BackpackMount` — are attachment points for §05's
flashlight-as-pointer and §03's objective carry; expose them as extra transforms or
they will be lost by the avatar mapping.

**Models — `Monster.fbx`:** Animation Type **Generic**, root node `Monster_Rig`.
Do **not** use Humanoid: the 29-bone rig has `Jaw`, `Crest1..3`, `LeftScapulaSpur`
and `LeftForearmExtra`, none of which exists in Unity's humanoid skeleton, and a
Humanoid avatar would silently drop their animation.

Both FBX files carry animation stacks named `Rig|Clip` (`Monster_Rig|Chase`,
`Player_Rig|Run`, …), which is how they will appear in the importer's Animations tab:
7 clips for the monster, 9 for the player. The `.glb` copies are for eyeballing in a
viewer and should **not** be imported into Unity — importing both formats would give
you two Avatars for the same character.

---

## 2. Audio

### 2.1 Footsteps — 60 clips, 2.79 MB (`Assets/Audio/Footsteps/`)

`step_{surface}_{actor}_{01..04}.wav` — 5 surfaces × 3 actors × 4 variants. All mono,
all positional.

**This family is a gameplay channel, not decoration.** §12 says so in as many words:
"구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다. **아트 결정이 아니라
시스템 결정이다.**" §04 gives the Listener exactly one ability — read the monster's
위치 · 거리 · 이동 방향 from sound — and its 맵 요구 is that floor material differs per
zone. So these clips are five mutually distinguishable spectral identities that map
onto §12's zone table:

| zone | §12 floor | §12 word | clip prefix | measured centroid | ring |
|:--:|---|---|---|--:|--:|
| A | 나무 | 삐걱 | `step_wood_` | 573 Hz | 122 ms |
| B | 타일 | 딱딱, 반향 | `step_tile_` | 2752 Hz | 405 ms |
| C | 자갈 | 부스럭 | `step_gravel_` | 6239 Hz | 68 ms |
| D | 콘크리트 | 둔탁 | `step_concrete_` | 242 Hz | 30 ms |
| 계단 | 금속 | 울림 | `step_metal_` | 1291 Hz | 554 ms |

The three actors exist for three different reasons:

* `player_walk` — 2.0 m/s (§06). The quietest thing in the set, ~9 dB under a monster
  step, because §04 also penalises the Listener for their *own* noise.
* `player_run` — 4.5 m/s (§06). Loud enough that a running teammate genuinely blinds
  the Listener — §04's "자기가 소리를 내면 못 듣는다" made audible.
* `monster_step` — the only clip the team tracks the monster by. §06 gives it
  footsteps in 순찰 / 경계 / 추격 / 수색 and **nothing** in 정지. Built to be
  identifiable in a single step: transposed down ~6.5 semitones, decays doubled, a
  body-weight sub-thump, a drag after the impact, and an off-beat second contact
  ~85 ms late that no human gait produces.

Four variants each because a machine-gun-identical step reads as a looping sound cue
rather than as a creature walking — which is exactly the information §04 asks the
Listener to extract.

### 2.2 Monster — 15 clips, 5.81 MB (`Assets/Audio/Monster/`)

All mono, all positional. `monster_audio.manifest.json` maps clips to §06's state
machine and is the file the engine should read rather than hardcoding names.

| §06 state | clips | note |
|---|---|---|
| 순찰 Patrol | *(none)* | footsteps only — owned by the footstep family |
| 경계 Alert | `monster_growl_01..03` | the "something is coming" cue; power centroid under 400 Hz, closed mouth |
| 추격 Chase | `monster_roar_01..03` | §06's 발소리+포효; ≥1.4× the growl centroid — mouth open |
| 수색 Search | `monster_search_01..02` | 15 s of hunting the last known position |
| 정지 Standstill | **deliberately empty** | §06: 소리 없음. "침묵이 가장 무서운 소리다." **Do not add a clip here** — the Listener losing the monster is the mechanic |

| event / bed | clips | design |
|---|---|---|
| Grab | `monster_grab_01..02` | the kill |
| FlashStun | `monster_stun_01..02` | exactly 2.5 s, mirroring `GameConstants.FlashStunSeconds` (§04 섬광수). Regenerate if that constant changes |
| proximity bed | `monster_presence_bed` (25.6 s loop), `monster_breath_loop_01..02` | crossfade **by distance only, never by state** — a state-gated bed would leak the position §06's 정지 exists to hide |

### 2.3 Ambience — 21 clips, 62.32 MB (`Assets/Audio/Ambience/`)

Twelve stereo 2D beds and nine mono positional one-shots.

| file(s) | role | section |
|---|---|---|
| `amb_zone_{a_wood,b_tile,c_gravel,d_concrete}_loop`, `amb_stairwell_metal_loop` | zone identity. §12 makes floor material a gameplay channel, which only works if every player already knows which zone they are standing in — in the dark, with no UI | §12, §05 |
| `amb_tension_t{1..5}_{dusk,night,deepnight,dawn,predawn}_loop` | §07's five threat tiers *are* the clock. "안에서는 시간 감각이 없다" — underground the player cannot read a clock, so the bed is the readout. All built on one 45 Hz root so a crossfade is not a key change; a measured 3 dB per tier | §07 |
| `amb_surface_vehicle_loop` | §08's 지상 차량 — safe zone, shop, 보급소. The only bed whose job is relief, and the contrast that makes the other five oppressive | §08, §07 |
| `amb_generator_hum_loop` | **mono, positional.** §03 makes the surface generator the battery source and darkness the lock on progress. This is the sound the player walks *toward* when the flashlight is dying | §03 |
| `sfx_water_drip_01..04` | **mono, positional.** §03's worked clue is literally "그것은 물이 있는 층에 있다" — dripping is diegetic information, not decoration | §03 |
| `sfx_creak_distant_01..05` | **mono, positional.** What remains when the footsteps stop, so §06's 정지 reads as *ominous* rather than as *the audio dropped out*. Also the false alarms: "어디 갔어? 방금 여기 있었는데" | §06 |

### 2.4 Items — 43 clips, 4.02 MB (`Assets/Audio/Items/`)

Mono and positional except `shop_purchase_confirm` and `loot_sell_credit`, which are
shop UI.

| clips | role | section |
|---|---|---|
| `flashlight_on/off_01..02` | §05's F key | §05 |
| `battery_insert_01..02`, `battery_low_warning`, `battery_dead` | §03's 보충 loop; the warning is the clue-reading window closing, and `battery_dead` is 단서 becoming unreadable | §03 |
| `door_open_01..02`, `door_close_01..02` | opening a door is loud enough to blind the 청음사 — §04's constraint in practice | §04 |
| `door_lock_01..02` | 정비공 traps a route; §12 puts a lockable door at the neck of a 순환로 | §04, §12 |
| `barricade_place_01..02` | 차단물, quiet on purpose — 정비공 is 사전 준비형 | §04 |
| `barricade_break_01..02` | the monster coming through: a threat cue, loud on purpose | §04 |
| `noisetrap_arm` / `noisetrap_trigger` | quiet then loud on purpose. The trigger saturates the noise scale (`EngineerTrapNoiseLevel = 1.0`) and can catch the 주자 — §04's "실수가 아군을 죽인다" | §04 |
| `safe_dial_turn_loop`, `safe_open` | 정비공 opens the safe holding §08's 금고 속 문서 | §04, §08 |
| `breaker_throw`, `zone_hum_loop` | §10's dilemma made audible: 밝히면 괴물이 온다. The hum is a standing warning that the zone is lit | §10, §03 |
| `flare_ignite`, `flare_burn_loop`, `flare_die` | §08's 조명탄: 1회용 · 소리를 낸다. The loop is the noise cost paid continuously; its level sits above `ListenerSelfNoiseThreshold`, so a 청음사 who strikes one blinds themself | §08, §04 |
| `chalk_mark_01..03` | §08's 분필. Cost is the trail, not the noise — 괴물도 흔적을 따라온다 | §08 |
| `rope_deploy` | §08's 밧줄: 지름길, **편도만** | §08 |
| `loot_pickup_metal_small_*` (은수저, weight 1), `loot_pickup_glass_jewel_*` (회중시계·반지, 1), `loot_pickup_paper_*` (금고 문서, 2), `loot_pickup_wood_heavy_*` (궤짝, 5 — two-person carry) | §08's 무게 vs 가치 ladder, one material per weight class so weight is audible | §08 |
| `loot_sell_credit`, `shop_purchase_confirm` | §08's 공용 지갑 at the surface vehicle | §08 |
| `detector_ping` | §11's 청음사 substitute. §08 prices it: 작동 시 소리를 낸다 — the cost *is* this noise | §11, §08 |
| `muffler_equip` | §08's 소음기: 발소리 감소, **자기도 못 듣게 됨 → 청음사 무효.** The one item that invalidates a role | §08 |

### 2.5 UI — 27 clips, 10.47 MB (`Assets/Audio/UI/`)

Stereo 2D, except the four ghost rattles. Everything peaks at or below −6 dBFS,
deliberately underneath positional world audio at −3, because UI that masks a
footstep is a §12 gameplay bug rather than a taste question.

| clips | role | section |
|---|---|---|
| `clue_read_success` / `clue_read_failed` | §03: a clue costs sustained light and stillness and cannot be carried out. Failure is the same metering texture *cut off mid-tick* plus a long empty tail — §07 makes time the currency, so failure must read as spent time, not as a buzzer | §03, §07 |
| `objective_found` / `objective_pickup` | §03's decision point: "지금 들고 나갈까, 전리품 더 챙길까?" Nothing ascends and nothing resolves — a reward jingle would answer the question the design leaves open | §03 |
| `death_transition_01..02` | §09: death is not an ending, it is the ghost state. A severance — the shared soundscape gated to true silence, then a drone that belongs to nobody | §09 |
| `ghost_rattle_01..04` | **MONO, positional.** §09's only channel, 45 s cooldown, 4 m range. Ambiguity is the deliverable — "방금 뭔가 흔들렸어?" / "바람이겠지". Soft onset (≥25 ms) so it never startles; four different objects so a repeat is not a tell; energy in 600–4500 Hz so a low level is still audible | §09 |
| `ghost_rattle_ready` | the quietest clip in the set (−22 dBFS); only the ghost hears it, in a different register from the rattles so it cannot be mistaken for one | §09 |
| `threat_night`, `threat_late_night`, `threat_pre_dawn`, `threat_before_sunrise` | §07's tier boundaries. **Four, not five** — 초저녁 is where the match starts, so there is no transition into it. Root drops 98→82→69→55 Hz; 심야 carries the 손전등 반경 −30% and 새벽 the 괴물이 출입구를 안다 | §07 |
| `heartbeat_low/mid/high` | 60 / 90 / 120 BPM, all exactly 4.000 s with a shared +50 ms downbeat so an engine crossfade never lands mid-thump | §06 |
| `escape_success` / `match_failure_wipe` | §02's asymmetry, built as opposites: escape opens and releases, the wipe closes and its last half-second is hard-gated to silence — 전멸 leaves nothing | §02 |
| `shop_open` / `shop_close` / `shop_denied` | §08's surface vehicle. Denial is not a buzzer: the wallet is shared, so a refusal happens mid-negotiation | §08 |
| `voice_activity_blip` / `voice_out_of_range` | §13's proximity voice, cut at the sender at 30 m. The out-of-range cue is the channel losing carrier, so a player learns where 30 m is instead of guessing why nobody answered | §13 |
| `descend_basement` / `surface_reached` | §03's round trip. Surfacing is "숨 돌리기이지 리셋이 아니다", so it is purely environmental — `escape_success` owns the pitched gesture | §03 |

---

## 3. Models

All FBX are single-mesh, unit-scaled, UV-mapped and materialled. No object carries a
non-unit transform, so nothing has a baked-scale hazard.

### 3.1 Characters — 4 files, 20.66 MB (3.24 MB of it FBX) (`Assets/Models/Characters/`)

| file | tris | bones | clips | height | materials |
|---|--:|--:|--:|--:|---|
| `Player.fbx` | 1,252 | 26 | 9 | **1.750 m** | `Player_Skin`, `Player_Coverall`, `Player_Gear` |
| `Monster.fbx` | 5,704 | 29 | 7 | **2.336 m** | `Monster_Hide`, `Monster_Eyes`, `Monster_Maw` |
| `Player.glb` / `Monster.glb` | same | same | same | preview only — do not import | |

**There is exactly one monster in this folder, and that is a rule.** Its three material
names are contracts, not labels: `MonsterSkin` puts §04's constant eye glow on whatever
matches `Monster_Eyes` and `MonsterAcquireTell` fires §06's acquisition flare on whatever
matches `Monster_Maw`, so a creature carrying only a hide disconnects both without
logging anything. Anything else dropped into `Assets/Models/Characters/` is graded
against the *player's* humanoid policy by `AssetImportValidator` and reported as broken
on every run — two unadopted monster variants once cost four failures a run there.
A generator publishing a variant writes it to `artifacts/`, not here.

§05 corrects an earlier cost estimate in one line: "1인칭이어도 캐릭터 모델이
필요하다. 협동 게임에서는 다른 3명이 보여야 한다." §16-1 lists these two as the
project's hidden bottleneck — "이 둘은 우회가 안 된다".

**Player animations** (9): `Idle`, `Walk`, `Run`, `Crouch`, `CrouchWalk`, `Carry`,
`CarryHeavy`, `CarryIdle`, `Death`. `CarryHeavy` covers §08's 궤짝 무게5 two-person
carry; `CarryIdle` covers §04's 관측자, whose ability needs 이동 정지 3초.
The rig's four mount bones serve §05's flashlight-as-pointer (`FlashlightMount`),
§03's objective carry (`ObjectiveMount`), §08's 가방 (`BackpackMount`) and the
first-person camera (`HeadCameraAnchor`).

**Monster animations** (7) map one-to-one onto §06's state machine plus two events:

| clip | §06 state |
|---|---|
| `Patrol` | 순찰 |
| `Alert` | 경계 |
| `Chase` | 추격 |
| `Search` | 수색 |
| `Standstill` | 정지 — moves, makes no sound; the silence is the weapon |
| `Grab` | the kill |
| `Stunned` | §04's 섬광수 |

The monster is deliberately 0.59 m taller than the player. Both heights are inside the
1–3 m sanity band, so there is no unit-scale error in either direction.

### 3.2 MapKit — 21 files, 0.63 MB, 12,166 tris (`Assets/Models/MapKit/`)

§12 opens with "맵은 아트가 아니라 시스템이다" and every dimension here is derived
from a numbered rule. `MapKit.manifest.json` carries the grid (2.5 m), storey height
(3.75 m), corridor clear section (2.2 × 3.0 m) and every footprint and dock point.

| piece(s) | §12 rule it implements |
|---|---|
| `Corridor_Straight_2m5 / _5m / _10m` | 직선 통로 최대 **20m** — no single piece can break the rule, and 10 m is the largest offered |
| `SCorridor_Unit_10m_x2` | ① S자 통로, 10 m × 2회 굽음 — 통과 4.2초 > 차단 3초. §12 calls this 가장 확실한 연속 차단 구조 |
| `Corridor_Corner_L`, `Junction_T`, `Junction_Cross_4Way` | ② 순환로 and ③ 분기; §12 requires 순환로 1+ per zone and 3+ overall — "트리 구조는 사형선고" |
| `DeadEnd_Cap` | 막힌 길 비율 20~25%, each with 보상 |
| `Hall_Open_20x20` | §12's 개방 공간, which must be adjacent to maze space — "두 성격의 공간이 인접해야 한다" |
| `FloorTile_{Wood,Tile,Gravel,Concrete,Metal}` | the five zone materials. **These are the geometry half of the Listener's alphabet** — the footstep clips are the other half, and the two must stay paired |
| `FloorBoundary_Split` | "재질 경계를 명확히 할 것" — the boundary is authored, not implied |
| `Stairwell_Metal` | 계단 금속 울림. §12 makes a stairwell transit the clearest signal on the map |
| `ObservationPost_Gallery`, `ObservationPost_BarredWindow` | §12's 관측자 requirement: 15 m sightline, safe, 구역당 1~2개. "없으면 관측자는 죽으러 가야 한다" |
| `Doorway_Frame`, `Door_Panel_Lockable` | §12's 정비공 requirement: 구역당 잠글 수 있는 문 1~2개, at a bottleneck. More than that and 정비공 becomes 만능 |
| `WallPanel_Electrical` | 전기 패널 구역당 1개; §03 requires clue sites to have panel access so 정비공 can light them |

### 3.3 Props — 24 files, 0.95 MB, 17,068 tris (`Assets/Models/Props/`)

| prop(s) | role | section |
|---|---|---|
| `Loot_Trinket_SilverSpoons`, `Loot_Trinket_Junk`, `Loot_Timepiece_PocketWatch`, `Loot_Timepiece_Ring`, `Loot_SafeDocument`, `Loot_LargePiece_Portrait`, `Loot_LargePiece_Chest` | §08's 무게 vs 가치 ladder, one model per class. The chest is the weight-5 two-person carry | §08 |
| `Safe_Closed` / `Safe_Open` | §08's 금고 속 문서; §04 gives 정비공 the job of opening it | §08, §04 |
| `Clue_WallBoard`, `Clue_LedgerStand`, `Clue_EngravedPlate` | §03's three clue presentations. The clue cannot leave the room — "그 자리에서 보고, 기억해서, 말로 전달해야 한다" | §03 |
| `ElectricalPanel` | §04's 정비공 lighting a zone; §10's dilemma — 밝히면 괴물이 온다 | §04, §10 |
| `SurfaceGenerator` | §03's battery source, paired with the mono positional `amb_generator_hum_loop` | §03 |
| `Vehicle` | §08's 지상 차량 — safe zone, shop, 보급소, and where §07's clock is legible | §08, §07 |
| `Barricade` | §04's 차단물 | §04 |
| `NoiseTrap` | §04's 소음 함정, the 정비공 mistake that kills the 주자 | §04 |
| `Flare_Unlit` / `Flare_Lit` | §08's 조명탄: 1회용 · 소리를 낸다 | §08 |
| `HidingSpot_Locker` | §12's checklist item "출입구 근처에 은폐 지점이 있다", for §07's 새벽 stage when 괴물이 출입구를 안다 | §12, §07 |
| `Crate`, `Pipes`, `Shelving`, `Debris` | sightline blockers. §12 puts 시야 차단 지점 every 15~25 m so a 60 m 질주 has 3~4 chances to break aggro | §12, §04 |

---

## 4. How to regenerate everything

Run from the repo root. Both toolchains are deterministic: a clean rebuild produces
byte-identical output, so `git diff` after a regeneration is a real change.

### 4.1 Audio

```sh
# 60 footstep clips — the Listener's alphabet (§04, §12)
tools/audio/.venv/bin/python tools/audio/gen_footsteps.py

# 21 ambience beds and positional one-shots (§03, §07, §08, §12)
tools/audio/.venv/bin/python tools/audio/gen_ambience.py

# 43 item and interaction sounds (§03, §04, §08, §10)
tools/audio/.venv/bin/python tools/audio/gen_items.py

# 15 monster clips + monster_audio.manifest.json (§06)
tools/audio/.venv/bin/python tools/audio/gen_monster_audio.py

# 27 non-diegetic UI clips and the ghost's channel (§02, §03, §07, §09, §13)
tools/audio/.venv/bin/python tools/audio/gen_ui.py
```

Each script verifies its own output with `synth.assert_usable` and its own
design-specific assertions, and fails loudly rather than writing something unusable.
`tools/audio/synth.py` is the shared DSP library; it is not a generator and produces
no files.

### 4.2 Models

```sh
BLENDER=/Applications/Blender.app/Contents/MacOS/Blender

# 21 MapKit pieces + MapKit.manifest.json (§12)
$BLENDER --background --factory-startup --python tools/blender/gen_mapkit.py

# 24 props (§03, §04, §08, §10, §12)
$BLENDER --background --factory-startup --python tools/blender/gen_props.py
# iterate on a subset: ... --python tools/blender/gen_props.py -- Chest Safe

# Player.fbx + Player.glb, 26 bones, 9 clips (§05)
$BLENDER --background --factory-startup --python tools/blender/gen_player_model.py

# Monster.fbx + Monster.glb, 29 bones, 7 clips (§06, §16-1)
$BLENDER --background --factory-startup --python tools/blender/gen_monster_ai.py
```

`tools/blender/blendkit.py` is the shared mesh/rig library, not a generator. So, now,
is `tools/blender/gen_monster_model.py`: it built the monster out of convex hulls until
the committed sculpt replaced it, and it is kept because `gen_monster_ai.py` imports its
seven §06 clip authors and its procedural skin pipeline verbatim. It refuses to write
`Monster.fbx` unless run with `-- --hull`, which is only wanted for a before/after
comparison against the creature that ships.

### 4.3 Verify after any retune

```sh
tools/audio/.venv/bin/python tools/audio/verify_audio.py
tools/audio/.venv/bin/python tools/audio/verify_audio.py --json /tmp/audit.json
```

Exit code is non-zero when a blocking defect is present. This is the cross-family
audit — the checks no single generator can do:

1. inventory, strays and cross-family basename collisions
2. the **§12 5×5 material separation matrix**, at four strictnesses: per surface, per
   actor, per clip, and low-passed to stand in for distance and wall occlusion
3. loop seamlessness — click, level pulse, and fade-notch, per channel
4. levels and format — no clipping, nothing silent, no DC offset, 48 kHz throughout
5. **channel policy** — every positional clip mono, every non-diegetic clip stereo
6. **HUD versus ears** — `GameConstants.ListenerClarity*` against the measured
   A-weighted audibility of each surface, swept across occlusion. `ListenerAbility`
   hands the player an error radius from a hand-authored constant while their ears get
   the actual clip; nothing else in the repo compares the two.

### 4.4 Known open items

Recorded here so they are not rediscovered. See the audit output for numbers.

* **`gravel` versus `concrete` clarity is inverted under occlusion.**
  `GameConstants.ListenerClarityGravel = 0.70` versus
  `ListenerClarityConcrete = 0.50` says gravel gives the monster away more. Dry, the
  audio agrees. Low-passed to 2 kHz or below — mild occlusion, roughly air absorption
  at 25 m before any wall — gravel falls 25–32 dB *below* concrete, because gravel's
  whole identity is a 6.2 kHz noise band that a wall removes. `ListenerAbility` states
  that the role hears through walls, so the two channels contradict each other in
  exactly the situation the role is used in. Fix on either side: give gravel a
  low-frequency component, or lower `ListenerClarityGravel` below concrete's.
* **§12 separation has no margin at range.** The worst surface pair (wood versus metal,
  monster steps) is 2.03× dry, 1.65× at one corner, and **1.396×** through a wall —
  fractionally under the 1.4× floor. Any additional occlusion in the Unity mix eats the requirement.
* `Items/flare_burn_loop.wav` has a ~8 ms fade-notch at the loop seam that lands on
  audible material rather than in a trough (−9.7 dB below the clip's own 5th
  percentile). Marginal on broadband fire hiss; fix by shaping a trough at the
  boundary the way `gen_ambience.py` and `gen_monster_audio.py` do.
* `Items/loot_sell_credit.wav` is mono although it is non-diegetic shop UI. Harmless —
  it just does not use the stereo image §05's mandatory headphones exist for.
* Shop items in §08's 구매 목록 with no model or sound yet: 응급킷, 정비 자재,
  가방, 건물 도면, 미끼. 감지기 and 소음기 have sounds (`detector_ping`,
  `muffler_equip`) but no models.
* No player animation for entering `HidingSpot_Locker`, and no separate 질주 clip —
  the Runner's 5.6 m/s sprint (§04, §05) will play `Run`, which was authored slower, so
  expect foot-sliding until it is either re-authored or speed-matched.
