# Design decisions and their reasons

> Every entry here is a decision that has already been made, with the reason that
> made it. If you are about to reverse one, the reason is what you have to answer —
> not the decision.
>
> §15 of [game-design.md](../game-design.md) is the graveyard of the ideas that were
> tried and dropped, "되돌아가지 않기 위해 기록한다". Read it before proposing anything
> that feels obvious.
>
> **Re-checked against the artefact at HEAD `017b489`, 2026-08-12.** Several decisions on
> this page were *reached* in the four-player co-operative game and are still in force in
> the twenty-player race; several were reached about systems the 2026-08-02 pivot deleted.
> Those are not the same thing, and the difference is now marked. A decision whose subject
> is gone is kept as 🔴 history where the *reason* still teaches something, and re-founded
> on what is true today where the reason outlived its example. Nothing here was deleted
> merely for naming a deleted role.

---

## 1. Why the monster is exactly 0.3 m/s faster than a run

```
걷기 2.0  <  달리기 4.5  <  괴물 4.8  <  질주 5.6   (m/s)      §06
```

Verified in `GameConstants` 2026-08-12: `WalkSpeed` 2.0, `RunSpeed` 4.5,
`MonsterBaseSpeed` 4.8, `RunnerSprintSpeed` 5.6. §06 calls this line
「이 한 줄이 게임 전체를 정한다」, and the margin is the point:

> 괴물이 달리기보다 **0.3만 빠른** 것이 핵심이다. 아주 조금 빠르면
> **"거의 도망칠 수 있는데 안 되는"** 최악의 긴장이 생긴다.

Three consequences fall out of one number:

- **Running is not escaping.** 4.5 against 4.8 means the gap closes, so the answer to
  being seen is geometry — a corner — and never a straight line.
- **The sprint is the only thing that outruns it**, at +0.8 m/s, so the ability to get
  away is a number rather than a description.
- **You are caught when your stamina runs out** (`SprintStaminaSeconds` 12,
  `SprintStaminaRecoverySeconds` 20), so there is no infinite escape.

> 🔴 **History, and it is why the sprint had to survive the pivot.** These three bullets
> used to read *"ordinary roles cannot flee — they hide, or depend on the 정비공 and the
> 섬광수; only the 주자 escapes."* §04 deleted all five roles and DESCENT-PIVOT §3 promoted
> 질주 to everyone: 「전원이 갖는 것: 손전등 · 문 · 질주 5.6 m/s · 귀」. The reason given
> there is this exact arithmetic — the lunge is tuned so that **달리는 사람(4.5)은 잡고
> 질주하는 사람(5.6)은 놓친다**, so deleting the sprint would mean nobody survives being
> seen and 「§12의 시야 차단 지형이 통째로 무의미해진다」. The margin did not change owner;
> it changed from one player in four to all twenty.

It is pinned in `GameConstants.Validate()` from both sides:

```csharp
Require(RunSpeed < MonsterBaseSpeed,   "…or ordinary roles could simply flee.");
Require(MonsterBaseSpeed - RunSpeed <= 0.5f,
    "§06: the monster's edge over running must stay small — that narrow margin is the tension.");
```

**Measured on real geometry** ([BLOCKERS.md B-001](../BLOCKERS.md#b-001)): 4.80 m/s of
corridor against 4.8, and 0.80 m/s of gap opened while sprinting against 0.8 — correct
to 1 %.

### And why that forces the entire map specification

```
sprint gain = (5.6 − 4.8) × 12 s = 9.6 m   <   release distance 12 m
```

> **어그로 해제는 거리가 아니라 맵을 쓰는 것이다.**

A sprint alone can *never* open the release distance. So you must round corners, and
one corner is not enough: cover has to last 3 s, which at 4.8 m/s means the runner
must be 14.4 m ahead when it rounds — hence §12's S-corridor of two 10 m legs
(20 m / 4.8 = 4.2 s > 3 s) as the map's base unit. Every §12 dimension is that
arithmetic. See [Where every number lives §3](03-where-numbers-live.md).

`MonsterChaseTests` proves both directions on real geometry: two 10 m legs release at
5.50 s, one corner does not release and catches at 12.54 s.

---

## 2. Why Mirror

§13's table, and the reason is not performance:

| Area | Choice | Reason |
|---|---|---|
| Engine | Unity | networking + 3D audio + Steam ecosystem is the most mature |
| Networking | **Mirror + FizzySteamworks** | 자료·커뮤니티 최다 — **막혔을 때 검색이 된다** |
| Alternative considered | FishNet + FishySteamworks | faster and more featureful, **less material to search** |
| Steamworks | Steamworks.NET | low-level wrapper, stable, free |

> 성능 차이보다 **막혔을 때 답을 찾을 수 있는지**가 1인 개발에서 훨씬 중요하다.
> 4인 게임에서 성능 한계에 부딪힐 일은 없다.

A solo developer stuck at 2 a.m. on an obscure serialisation bug will absolutely hit a
documentation wall. The decision optimises for the failure mode that actually occurs, and
that half of it is untouched.

> 🔴 **The second sentence of that quote is no longer true, and §16 says so itself.**
> 「4인 게임에서 성능 한계에 부딪힐 일은 없다」 was written about four players.
> [§16-1](../game-design.md) now names **20인 동시 접속** as the project's 🔴 최우선 open
> problem in as many words: 「'4인 게임에서 성능 한계에 부딪힐 일은 없다'는 v0.5의 문장은
> 20인에서 참이 아니다.」 The moment §11 is designed around — twenty players funnelling
> through one final 관문 — is 「관심 관리로 잘라낼 수 없는 유일한 장면」, because they are
> all in the same room by design.
>
> **This does not reverse the choice**, and that is the point of writing it down here: the
> criterion was searchability, and searchability did not change. What changed is that the
> decision now carries a cost it did not carry when it was made, and that cost has an
> owner — §16-1 — rather than being a surprise.

The transport is abstracted anyway (`Assets/Scripts/Steam/Abstractions/`,
`Net/NetTransportRegistry.cs`), which §13 recommends explicitly: 「음성 · 네트워킹을
인터페이스로 추상화해두면 나중 확장 비용이 줄어든다. 코드 몇 줄 차이다.」

---

## 3. Why host authority — and what the host's job became

§13:

| Item | Decision |
|---|---|
| Authority model | **Host authority** — the host runs the monster AI |
| Host leaves | **session ends**; no migration |
| **Placement and arrival** | **decided on the host, and nowhere else** |

[`ARCHITECTURE.md` §4](../ARCHITECTURE.md) states the swap in one line, and it is the
sentence that best characterises v1.1: **the host's job moved from *concealing* to
*adjudicating*.**

The teeth are the same and they point at a different value. `RaceState` (`Core/Race/`)
owns `ReportDescent`, `ReportFinish`, `ReportCaught` and `Standings()`, and **only the
host may call the reporting half** — a client that can say "I arrived" is the first value
anyone forges in a racing game (§02). The HUD *reads* standings; per §02 the screen side
must not even have a method that can claim an arrival. `Net/Race/NetRace.cs` counts
`DescentsAccepted` against `DescentsRefused` and `CatchesAccepted` against
`CatchesRefused`, which is what makes a refusal visible rather than silent.

> 🔴 **History — the row this section used to be about, and the reason it is worth
> keeping.** The table's fourth row was **「단서 내용 · 목표물 위치 → host only.
> 클라이언트에 보내면 메모리에서 읽힌다」**, and it was a *game design* constraint rather
> than a security one: §03's mechanic was 「그 자리에서 보고, **기억해서, 말로 전달**해야
> 한다」, and that dies the moment the answer is in a client's memory. Sending it "but only
> showing it when close" is the same as sending it — one player with a memory reader
> spoils the mechanic for the lobby, not just for themselves.
>
> **There is no clue and no objective any more.** §13 deleted the row outright, because a
> race has no answer to hide: the destination is 「처음부터 알려져 있다: 아래」. With them
> went `HostClueAuthority`, `MisreadModel`, `ClueDef`/`ClueReport`/`ClueGlyph`/`SiteLabel`,
> `ObjectiveResolver`, and the `NetReplicationAudit` that policed them — **confirmed
> absent from `unity/` and `core/` on 2026-08-12.** `ObjectiveResolver` in particular is a
> name not to reintroduce; ARCHITECTURE §4 says so.
>
> The generalisable half is what survives: **the layer that decides a value must not hand
> the network a structured form of it.** That is why `TryRenderRead` returned an
> already-rendered `string` and why the objective's position was a one-shot push with no
> getter, and it is the same instinct that now stops the client side owning an arrival.

**One artefact of the deleted defence is still on disk and no longer does anything.**
`Assets/Scripts/Net/Host/HostOnlyAttribute.cs` is the only file left in `Net/Host/`. Its
doc comment is still worth reading — it explains why a marker plus a test was chosen over
a language feature, because C# has no way to say "this type is unserialisable" — but the
`NetReplicationAudit` it names **does not exist**, so the attribute currently marks
nothing and enforces nothing. `NetTests` says so itself: *"`NetReplicationAudit`
(deleted)"*. Treat it as a pattern to copy if the race ever needs a poison type, not as a
live gate.

Related, same principle, and **entirely current**: **voice is cut at the sender** at
`GameConstants.VoiceCutoffDistance` (30 m, verified 2026-08-12), not muted at the
receiver. §13 is explicit — 「전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다
들린다. … 대역폭 절감보다 **도청 방지**가 본질이다.」 — and adds that
「경주에서 도청은 협동판보다 훨씬 큰 이득이므로, 이 결정은 더 중요해졌다.」

---

## 4. Why proximity voice is "just" a 3D AudioSource

§13's trick, quoted because it is the whole implementation:

> **핵심 트릭: 음성을 3D 오디오 소스로 재생하면 근접 음성이 자동으로 된다.**
> 거리 계산 로직이 필요 없다. 엔진의 3D 오디오가 감쇠와 벽 차폐를 처리한다.

Which is also why [the asset pipeline's](05-asset-pipeline.md) mono rule is
load-bearing: a stereo voice clip means every other player is at zero metres, and §13's
design collapses into a party chat. In a race that is worse than it was in the co-op
game — §10 makes 정보 a dilemma row of its own, 「남에게 위치를 알려 준다 → 그 사람이
나보다 빨라진다」, and the whole row only exists because you can hear *where* a voice is
coming from.

---

## 5. Why there is no server, no database, and no meta-progression

§13 opens with 「직접 띄울 서버가 0대. DB도 필요 없다. 월 고정비 0원, 초기 비용 $100.」
Steam supplies P2P sockets, NAT traversal and **free relay** (SDR), lobbies, voice
capture and codec, identity, cloud saves, stats and distribution.

The load-bearing link is at the bottom of that section: **§15 dropped between-match
meta-progression, and that came back as an infrastructure win.** Persistent progress
would need storage; storage needs cheat validation; cheat validation needs a real
server. Everything a match produces completes *inside that match* —
「영구 진행도 · 인벤토리 → 없음 — 한 판 안에서 완결된다」 — so none of it is required.

**Deleting §08 hardened this decision rather than weakening it.** §13's own note:
「§08을 지운 v1.1은 이 결정을 한 번 더 굳혔다 — 이제 판 안에서도 저장할 상태가 없다.」
The earlier version of this paragraph justified it by "§08's growth curve completes
inside a single 25–35 minute match", which was true and is now a stronger claim without
the growth curve: there is no wallet, no inventory and no shop to persist.

**And the race pulled one Steam feature the other way.** §13 marks 리더보드 as having
*gained* value: 「선착순 게임은 기록이 남아야 다시 하고, Steam이 그것을 공짜로 준다」 —
층별 최속, 완주 시간, 판당 순위. That is still zero infrastructure.

Telemetry follows the same logic in three stages (§13): Steam Stats bucket counters
first (a histogram with zero infrastructure), then JSONL files in object storage if
detail is ever needed, then a hosted analytics service. **DB가 아니라 파일이 쌓인다.**
The bucket geometry is in `Core/Telemetry/TelemetryBuckets.cs` and in `GameConstants`
under the §13 telemetry block (both read 2026-08-12).

> 🔴 **This paragraph used to end "…and the simulator prints the buckets exactly as the
> shipped build would send them."** `core/HorrorGame.Sim` was deleted at `e8c67ae`, so
> **nothing prints them today.** The buckets are still defined and still shipped; what is
> gone is the only thing that ever showed you what they would contain, and nothing
> replaced it ([TESTING.md §9](../TESTING.md)).

---

## 6. Why the design refuses several things that sound like improvements

From §15, with the reason that killed each. These are the ones most likely to be
re-proposed:

| Rejected | Why |
|---|---|
| A gun / 사수 role | 괴물을 죽일 수 있으면 공포가 사라진다. All interference must be temporary |
| A dedicated puzzle-solver (해독가) | 목표를 한 명이 담당하면 나머지는 호위병이 된다 |
| A medic / support role | 필수인데 재미없는 역할 |
| A sensory-asymmetric 영매 | 화면이 검은 역할은 아무도 하고 싶어하지 않는다. Recovered in §09 as the **elimination state**, where nobody chooses it |
| Time-based aggro release | 기다리는 것은 실력이 아니다. Replaced by distance + line-of-sight break |
| Adaptive boss patterns | rubber-banding erases the reward for improving |
| A runtime LLM | per-sale revenue with per-play cost — the better it sells the worse it loses money; latency is wrong for action anyway |
| Descent-count-based threat | leaving becomes free and needs an artificial counter. **시간 기반이 더 단순하고 압박이 연속적** |
| "You bump into a wall" as a look-behind penalty | too vague. Replaced by a number: **후진 속도 65 %** |
| 의식 복원 as the objective's shape | **서사 장치라 반복하면 빨리 낡는다** |
| **The four-player asymmetric co-op recovery game itself** (v1.0) | 오너의 한 문장으로 끝났다: 「그냥 선착순 미로탈출게임이야…..! 귀신피해서 지하로 들어가서 나가는」 |
| **The five roles** (v1.0) | 픽이 있으면 진 이유를 픽에서 찾는다. 20인은 같은 몸이어야 한다 (§04) |
| **왕복 · 지상 · 상점 · 전리품 · 크레딧** (decided v1.0, deleted v1.1) | 경주에 재보급이 없고 통화도 없다 (§08) |
| **단서 3층위** (decided v1.0, deleted v1.1) | 목적지가 처음부터 알려져 있다 (§03) |
| **배터리 · 배전반 · 발전기** (v1.1) | **유지해야 하는 빛은 심부름이다.** 어둠은 남기고 관리를 없앴다 (§03) |
| A dedicated server / own relay | Steam SDR does it free; not a solo developer's problem to own |
| 텔레메트리용 DB (Supabase 등) | **append-only 로그에 DB는 과하다** |
| 판 사이 메타 프로그레션 | 한 판 안에 완결된다. 나중에 추가 가능 |

The last five of those are the pivot, and §15 draws the lesson that matters more than any
of them: **「v1.0과 v1.1의 차이는 "결정"과 "삭제"의 차이다.」** v1.0 made the right calls
and gated the code behind them, expecting to delete it in a day or two. Days later the
shop was still being constructed on the first tick of every race and the map was still
placing 152 loot markers. **막아 둔 것은 없어진 것이 아니다** — do not separate the commit
that changes the document from the commit that deletes the code.

The principle underneath all of them, §15's closing line:

> **패턴은 학습하게 두고, 내용을 매번 바꾼다.** 공략을 막으려 하지 말고,
> **공략을 봐도 남에게 전이되지 않는 축**을 만들 것.

**The axis moved and the principle did not.** It used to be §03's randomisation table —
the objective's location, the clue contents, the loot placement, all re-rolled per match
while the map structure stayed fixed and learnable. None of those exist now. §15 names the
replacement itself, and it is a better answer than any of them: 「경주에서 그 축은 **다른
열아홉 명**이다. 맵을 외워도 그들이 어디서 문을 닫을지는 모른다.」 The map is *supposed*
to be learnable — §01 makes 「맵을 아는 것이 실력」 one of the two things that separate
players — so the unlearnable axis had to come from somewhere else, and nineteen other
people are the one source that cannot be written down in a guide.

---

## 7. Why nothing in this game is free

§10 is one line — **얻으려면 위험을 만들어야 한다** — and it is the adoption test for new
content: *"이 기능은 이득과 위험을 교환하는가?"* If a proposed feature has no cost, it does
not belong in this game.

**The shop was where this rule used to be demonstrated. The rule is not the shop.** §10 is
now a 딜레마 지도 of ten rows against the race, and every one of them is the same shape:

| 얻는 것 | 대가 |
|---|---|
| 손전등을 켠다 | 괴물이 본다 · **남들이 내가 어디 있는지 안다** |
| 손전등을 끄고 다닌다 | **§10의 그늘이 고인다** |
| 뒤를 봐서 거리를 확인한다 | **속도 65 %** → 거리가 좁혀진다 (§05) |
| 문으로 뒤를 막는다 | 1.1초 서 있어야 하고, **내가 돌아올 수도 없다** |
| 질주로 관문을 뚫는다 | 다음 관문에서 쓸 것이 없다 |
| 소리를 듣는다 | 자기가 조용해야 한다 (웅크리면 느리다) |
| 지름길 | 안쪽 고리 = 괴물의 순찰 반경 (§07) |
| 안전한 길 | **모두가 나보다 앞서 있다** |
| 어그로를 떼어낸다 | 떼어낸 쪽에 있는 사람에게 **배달된다** (§06) |
| 남에게 위치를 알려 준다 | 그 사람이 나보다 빨라진다 — **거짓말이 가능한 이유** |

The last three are new to the race; the rest came straight from the co-op game with the
payer changed from 「팀 전체」 to 「나」. **That is the strongest possible evidence that the
rule was never about purchasing** — none of these rows involves a currency, and the design
did not have to invent a single new principle to price them.

> 🔴 **History — the shop rows, which are the clearest worked examples this rule ever
> had.** §08's 구매 목록 applied 얻으려면 위험을 만들어야 한다 without exception: 강화
> 손전등 doubled your radius **and doubled the range at which the monster saw you**; 조명탄
> lit a zone **and made noise**; 분필 marked your route **and the monster followed it**;
> 밧줄 was a shortcut **one way only**; 소음기 quietened your steps **and deafened you,
> invalidating the 청음사 entirely**. §08 is deleted — there is no currency in a race —
> and `Core/Economy/` is gone from disk. The examples are kept because the pattern they
> teach is exact: **name the cost in the same sentence as the benefit, or the feature is
> not finished.**

**And §10 grew a row the shop could not have written.** 그늘 exists because §03 priced
only the *lit* side of the flashlight switch, so 「최적 전략은 언제나 *꺼놓고 다니기*였고,
딜레마는 딜레마가 아니었다」. Its price is **목소리** — 12 s of it, which is
`SprintStaminaSeconds` — chosen because the race has proximity voice and voice is the only
way to lie to someone. A dilemma with a free side is not a dilemma, and that is the same
sentence as the shop rule read from the other end.

> 🔴 **Implementation status, from §10 itself:** 「규칙과 표현은 있고, 판에는 없다.」 The
> core rules, the PlayMode tests, the model, the audio and the prefab all pass, but nothing
> ticks `PresenceField` in a match — **no player has ever actually paid the price in that
> table.**

---

## 8. Why the look is a mechanic, not decoration

[ART.md §1](../ART.md) derives four targets from §03, §05, §07 and §12, and the first
one is the reason the rest exist: **the beam is the source of information; outside it,
shape only.** If a room is readable without the flashlight, §03's 안쪽 고리 is not dark and
the maze stops being a maze — 「손전등 사거리 바깥은 존재하지 않는다」.

§03 states the same thing as a settings decision, and it is the sharper version because it
is a number: **the brightness slider is clamped to ±20 %.** 두 배로 밝힐 수 있으면 안쪽
고리를 손전등 없이 읽을 수 있고, 절반으로 어둡게 할 수 있으면 15 m에서 보이도록 만들어 둔
괴물의 실루엣이 사라진다. Darkness is a rule, so it is not a preference.

> 🟢 **The clause this paragraph used to end on — "the objective is no longer gated" —
> named a deleted system, and the constant behind it is the best worked example on this
> page of a value outliving its reason.** It was `ClueMinReadableLightQuality`: §03's hard
> gate below which a clue could not be read. There is no clue. But `Core/Presence`
> deliberately reused *the same number* rather than picking a similar one, so that "light
> enough to read by" and "light enough that no 그늘 forms" were one thing a player had to
> learn — and the 그늘 half is live and on the hot path. **So it was renamed, not dropped:
> `GameConstants.MinSafeLightQuality` = `0.20f` (read 2026-08-12), with
> `PresenceDensity.SafeLightQuality` reading it.** It is still one threshold on purpose —
> 「a runner descending in the dark has to be able to tell, at a glance, whether where they
> are standing is filling the pool」 — and two brightnesses would make that unlearnable.
>
> **`game-design.md` has not caught up with its own request.** §03 still says the constant
> 「`GameConstants.ClueMinReadableLightQuality`라는 이름으로 남아 있고」 and §16-3 still
> lists the rename as an open 🟡 task naming `MinLitLightQuality`. The rename happened, and
> it picked a better name than the one the document asked for. That row of §16 is done.

That is why the luminance bands are numbers with a measuring tool
(`tools/render/frame_stats.py`) rather than a judgement: "the eye adapts to whatever
it saw last, and every iteration looks like an improvement on the one before."

---

## 9. Read before reversing any of these

| If you are about to | Read | Because |
|---|---|---|
| change a speed | §06 + [Where numbers live §3](03-where-numbers-live.md) | you are changing the map specification. `GameConstants.Validate()` will stop you from both sides |
| replace Mirror | §2 above + §13's table | the criterion was searchability, not performance — and §16-1 is where the twenty-player cost is owned |
| let a client claim an arrival | §3 above + [ARCHITECTURE.md §4](../ARCHITECTURE.md) + `Core/Race/RaceState.cs` | an arrival a client can assert is the first value anyone forges in a racing game (§02) |
| add a feature with no downside | §7 above + §10's 딜레마 지도 | **얻으려면 위험을 만들어야 한다.** A feature with no cost fails the design's own adoption test |
| give one player something the other nineteen do not have | §04 + §15 | 픽이 있으면 진 이유를 픽에서 찾는다 — and a pick that eighteen of twenty take is not a choice, it is a balancing problem |
| widen the brightness slider | §8 above + §03 | ±20 % is the whole clamp. Beyond it the 안쪽 고리 is readable without a torch, or the creature stops being visible at 15 m |
| add or remove a §12 rule | §3.6 of [How to verify](06-verifying.md) + `Core/Map/MapValidator.cs` | three rules were deleted with the systems they gated and one arrived to replace them. Know which kind yours is before you touch the count |
| grade a map on 실전 검증 | [F-013](../BALANCE-FINDINGS.md#f-013) | the 5–7/10 band is a co-op-era instrument and **no §12-legal map can score inside it.** Three passes were spent proving that |

> 🔴 **Two rows are gone because their subjects are.** *"Send anything about a clue to a
> client"* and *"add a shop item"* both pointed at systems the pivot deleted, and *"make
> the match shorter or longer → F-006 … it blocks the economy"* pointed at an economy that
> does not exist and at a finding whose evidence came from a deleted simulator. Match
> length is still a live question — §01 asks for 12~20분 and nothing has measured a real
> one — but it is now a playtest question, not a sweep.
