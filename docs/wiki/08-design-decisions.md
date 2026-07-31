# Design decisions and their reasons

> Every entry here is a decision that has already been made, with the reason that
> made it. If you are about to reverse one, the reason is what you have to answer —
> not the decision.
>
> §15 of [game-design.md](../game-design.md) is the graveyard of the ideas that were
> tried and dropped, "되돌아가지 않기 위해 기록한다". Read it before proposing anything
> that feels obvious.

---

## 1. Why the monster is exactly 0.3 m/s faster than a run

```
걷기 2.0  <  달리기 4.5  <  괴물 4.8  <  주자 질주 5.6   (m/s)      §06
```

§06 calls this line 「이 한 줄이 게임 전체를 정한다」, and the margin is the point:

> 괴물이 달리기보다 **0.3만 빠른** 것이 핵심이다. 아주 조금 빠르면
> **"거의 도망칠 수 있는데 안 되는"** 최악의 긴장이 생긴다.

Three consequences fall out of one number:

- **Ordinary roles cannot flee.** They hide, or depend on the 정비공 and the 섬광수.
- **Only the 주자 escapes**, and its identity is therefore a number rather than a
  description.
- **The 주자 is caught when its stamina runs out**, so there is no infinite escape.

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

A four-player PvE game is not going to hit a netcode performance wall. A solo
developer stuck at 2 a.m. on an obscure serialisation bug absolutely will hit a
documentation wall. The decision optimises for the failure mode that actually occurs.

The transport is abstracted anyway (`Assets/Scripts/Steam/Abstractions/`,
`Net/NetTransportRegistry.cs`), which §13 recommends explicitly: 「음성 · 네트워킹을
인터페이스로 추상화해두면 나중 확장 비용이 줄어든다. 코드 몇 줄 차이다.」

---

## 3. Why host authority, and why clue contents never leave the host

§13:

| Item | Decision |
|---|---|
| Authority model | **Host authority** — the host runs the monster AI |
| Anti-cheat | barely needed — friends playing PvE |
| Host leaves | **session ends**; no migration |
| **Clue contents · objective location** | **host only.** 클라이언트에 보내면 메모리에서 읽힌다 |

The last row is a *game design* constraint, not a security one. §03's core mechanic
is:

> 그 자리에서 보고, **기억해서, 말로 전달**해야 한다.

That constraint dies the moment the answer is in a client's memory. Sending it "but
only showing it when close" is the same as sending it — a memory reader has it, and
one player with a memory reader spoils the mechanic for the lobby, not just for
themselves.

### How the codebase enforces it

`ARCHITECTURE.md` §4 asks that a client ask the host "am I reading a clue?" and get
back the rendered glyph for *that clue only*. `Assets/Scripts/Net/Host/` implements it
with three layers of defence:

1. **`HostClueAuthority.TryRenderRead` returns a `string`.** Already rendered, already
   passed through `MisreadModel`. There is no overload returning a `ClueReport`,
   `SiteLabel` or `ClueGlyph`, "so the Net layer has no structured form of the answer
   to accidentally put in a SyncVar".
2. **The objective's position is a push, never a pull.** `TryPlaceObjective` forwards
   `ObjectiveResolver`'s one-shot callback and keeps nothing. No property returns it.
   "The core went to the trouble of having no getter for it; storing the value here
   would undo that in one field."
3. **`[HostOnly]` plus `NetReplicationAudit`.** The attribute marks a type as poison;
   the audit fails if one ever appears as a `[SyncVar]`'s type, inside a sync
   collection, in the parameters of a `[Command]`/`[ClientRpc]`/`[TargetRpc]`, or as a
   field of a `NetworkMessage`. `NetTests` runs it in PlayMode. `ClueDef` is
   `internal` to the core so the Net assembly literally cannot name it; the attribute
   covers the public types a careless SyncVar could still pick up.

> **Before you add a networked field, read `Assets/Scripts/Net/Host/HostOnlyAttribute.cs`.**
> Its doc comment explains why a marker plus a test was chosen over a language
> feature: C# has no way to say "this type is unserialisable".

Related, same principle: **voice is cut at the sender** at
`GameConstants.VoiceCutoffDistance` (30 m), not muted at the receiver. §13 is explicit
— 「전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다 들린다. …
대역폭 절감보다 **도청 방지**가 본질이다.」

---

## 4. Why proximity voice is "just" a 3D AudioSource

§13's trick, quoted because it is the whole implementation:

> **핵심 트릭: 음성을 3D 오디오 소스로 재생하면 근접 음성이 자동으로 된다.**
> 거리 계산 로직이 필요 없다. 엔진의 3D 오디오가 감쇠와 벽 차폐를 처리한다.

Which is also why [the asset pipeline's](05-asset-pipeline.md) mono rule is
load-bearing: a stereo voice clip means every teammate is at zero metres, and §13's
design collapses into a party chat.

---

## 5. Why there is no server, no database, and no meta-progression

§13 opens with 「직접 띄울 서버가 0대. DB도 필요 없다. 월 고정비 0원, 초기 비용 $100.」
Steam supplies P2P sockets, NAT traversal and **free relay** (SDR), lobbies, voice
capture and codec, identity, cloud saves, stats and distribution.

The load-bearing link is at the bottom of that section: **§15 dropped between-match
meta-progression, and that came back as an infrastructure win.** Persistent progress
would need storage; storage needs cheat validation; cheat validation needs a real
server. §08's growth curve completes *inside a single 25–35 minute match*, so none of
it is required.

Telemetry follows the same logic in three stages (§13): Steam Stats bucket counters
first (a histogram with zero infrastructure), then JSONL files in object storage if
detail is ever needed, then a hosted analytics service. **DB가 아니라 파일이 쌓인다.**
The bucket geometry is already in `GameConstants` under the §13 telemetry block, and
the simulator prints the buckets exactly as the shipped build would send them.

---

## 6. Why the design refuses several things that sound like improvements

From §15, with the reason that killed each. These are the ones most likely to be
re-proposed:

| Rejected | Why |
|---|---|
| A gun / 사수 role | 괴물을 죽일 수 있으면 공포가 사라진다. All interference must be temporary |
| A dedicated puzzle-solver (해독가) | one person owning the objective turns the other three into bodyguards |
| A medic / support role | 필수인데 재미없는 역할. Healing is an **item**, not a job |
| A sensory-asymmetric 영매 | 화면이 검은 역할은 아무도 하고 싶어하지 않는다. Recovered in §09 as the **death state**, where nobody chooses it |
| Time-based aggro release | 기다리는 것은 실력이 아니다. Replaced by distance + line-of-sight break |
| Adaptive boss patterns | rubber-banding erases the reward for improving |
| A runtime LLM | per-sale revenue with per-play cost — the better it sells the worse it loses money; latency is wrong for action anyway |
| Descent-count-based threat | leaving becomes free and needs an artificial counter. **시간 기반이 더 단순하고 압박이 연속적** |
| "You bump into a wall" as a look-behind penalty | too vague. Replaced by a number: **후진 속도 65 %** |
| A dedicated server / own relay | Steam SDR does it free; not a solo developer's problem to own |

The principle underneath all of them, §15's closing line:

> **패턴은 학습하게 두고, 내용을 매번 바꾼다.** 공략을 막으려 하지 말고,
> **공략을 봐도 남에게 전이되지 않는 축**을 만들 것.

That is why the *map structure* is fixed and learnable while the objective's location,
the clue contents and the loot placement are randomised per match (§03's randomisation
table).

---

## 7. Why every purchasable item has a cost attached

§10 is one line — **얻으려면 위험을 만들어야 한다** — and §08 applies it to the shop
without exception. 강화 손전등 doubles your radius **and doubles the range at which the
monster sees you**; 조명탄 lights a zone **and makes noise**; 분필 marks your route
**and the monster follows it**; 밧줄 is a shortcut **one way only**; 소음기 quietens
your steps **and deafens you, invalidating the 청음사 entirely**.

This is stated as the adoption test for new content: *"이 기능은 이득과 위험을
교환하는가?"* If a proposed item has no cost, it does not belong in this game.

---

## 8. Why the look is a mechanic, not decoration

[ART.md §1](../ART.md) derives four targets from §03, §05, §07 and §12, and the first
one is the reason the rest exist: **the beam is the source of information; outside it,
shape only.** If a room is readable without the flashlight, §03's lock is open and the
objective is no longer gated.

That is why the luminance bands are numbers with a measuring tool
(`tools/render/frame_stats.py`) rather than a judgement: "the eye adapts to whatever
it saw last, and every iteration looks like an improvement on the one before."

---

## 9. Read before reversing any of these

| If you are about to | Read | Because |
|---|---|---|
| change a speed | §06 + [Where numbers live §3](03-where-numbers-live.md) | you are changing the map specification |
| replace Mirror | §13's table above | the criterion was searchability, not performance |
| send anything about a clue to a client | §3 above + `Net/Host/HostOnlyAttribute.cs` | you would be deleting §03's central mechanic |
| add a shop item | §7 above + §08's 구매 목록 | an item with no cost fails the design's own adoption test |
| add a role or remove one | §11 + §15 | 필수 직업이 있으면 풀이 가짜가 된다 — and only the 관측자 has no purchasable substitute, on purpose |
| make the match shorter or longer | [F-006](09-open-questions.md) | this is the top open question and it blocks the economy |
