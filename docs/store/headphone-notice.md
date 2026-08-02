# The headphone notice

> **Rewritten 2026-08-03 for the race.** The old version made the whole argument out
> of the 청음사 — one of five roles, whose entire function was locating the creature
> by ear. §04 deleted the roles on 2026-08-02, and the first reaction to that should
> be *"so the headphone requirement is gone."* It is not. **It got broader**: what
> used to be one player's job is now everybody's only source of information about
> nineteen other people.

§13's setup table lists **헤드폰 권장 표기** as a shipping item, alongside the App ID
fee and the tax forms. It is not a courtesy line. §05 says why:

> 소리의 방향 판별 — **몸을 돌려 삼각측량.** 3D 오디오는 카메라 기준 → **헤드폰 필수**

---

## 0 · The argument, after the pivot

Three things in the race are delivered by sound and by nothing else. None of them is
a role any more; all of them apply to every player.

**1 · Which floor somebody is on.** `DescentMap` gives each of the eight storeys its
own surface — B1 concrete, B2 wood, B3 metal, B4 gravel, B5 tile, B6 carpet, B7
water, B8 earth — and §12 calls that a system decision, not an art decision. A
footstep is a floor number. On stereo speakers it is a noise.

**2 · Where the one door is going.** One door per storey, at a middle gate. Whether
somebody is closing it in front of you is a directional question with about a second
to answer it (`DoorShutSeconds = 1.1f`).

**3 · Proximity voice.** §13 plays a speaker's voice as a 3D audio source at their
position — that is the entire implementation, and it is why there is no distance
logic in it. Which means **the location of a voice is the information**, and on
speakers a voice has no location. Cooperating with a stranger in a dark corridor
stops being a thing you can do.

And one thing that has not changed: §06's 정지 state, where the creature stops and
goes silent. 「침묵이 가장 무서운 소리다」 only works on a player who was tracking it
by ear in the first place.

> **A microphone matters differently now.** The co-op game was unplayable in silence
> — §03 forbade carrying a clue out, so the whole game was talking. The race is
> playable in silence: you can win without saying a word. What a microphone buys is
> the option to make a temporary ally, and the design says out loud that this is one
> of the more interesting things in it. **Recommend it, do not require it.** The
> minimum-spec wording below reflects that change.

---

## 1 · Short description

Already in both drafts as the closing clause — [copy-en.md §2](copy-en.md),
[copy-ko.md §2](copy-ko.md).

```
… Being caught is being out, unranked. Headphones recommended.
```

```
… 지하 8층 한가운데에 먼저 닿은 한 명이 이긴다. 헤드폰 권장.
```

Last clause rather than first: the hook has to survive the 300-character limit, and a
description that opens with a hardware note reads like an apology.

---

## 2 · About This Game — the first line, not the last

Already in both drafts as the opening line of the body. Above the fold, above the
first `[h2]`, before anything else competes with it.

```
[b]HEADPHONES RECOMMENDED.[/b] Footsteps are how you know where everybody else is. Every storey has a different floor under it — eight storeys, eight surfaces — so a footstep from below means somebody is already a floor ahead of you. On laptop speakers that information is simply gone.
```

```
[b]헤드폰을 권장합니다.[/b] 이 게임에서 남이 어디 있는지는 발소리로 압니다. 층마다 바닥이 다르고 — 여덟 층, 여덟 가지 — 발밑에서 나는 소리는 누군가 이미 한 층 아래에 있다는 뜻입니다. 노트북 스피커에서는 그 정보가 통째로 사라집니다.
```

It is a better line than the one it replaces, and for a reason worth keeping: the old
one asked a reader to care about a role they had not met yet. This one describes a
thing that happens to them.

**Do not move it to the bottom of the page.** A "recommended peripherals" line under
the credits is where this notice goes to be ignored.

---

## 3 · System requirements → Additional Notes

Both minimum **and** recommended. Steam shows one tab at a time, and a player
comparing against a five-year-old laptop is reading the minimum tab. Full text in
[copy-en.md §7](copy-en.md) and [copy-ko.md §7](copy-ko.md).

The minimum-tab wording is deliberately the blunt one:

> HEADPHONES STRONGLY RECOMMENDED. Every storey has a different floor surface, and
> footsteps are the only way to tell which floor someone is on and how close they
> are. Stereo speakers lose that. A microphone is needed for proximity voice.

Note the last sentence is weaker than the one it replaces — *needed for proximity
voice*, not *required for team play*. That is the honest version now (§0).

---

## 4 · In game, on first launch

**The store page is not read by the person who was invited into a lobby.** That
person's first contact with the game is a Steam invite, and nothing on the store page
ever reaches them.

| Where | State |
|---|---|
| Main menu, along the bottom | ✅ **already there.** `Shots/menu_main.png` carries §05's headphone warning across the foot of the title screen (STATUS §4.2) |
| Settings → audio bus rows | ✅ each row already carries the § reference for why its range is what it is |
| First-launch, dismissible, once | ❌ **not built.** Tracked in [checklist.md §3](checklist.md) |

The third is the one that catches the invited player. One line, a dismiss button, a
flag in Steam Cloud so it is never shown twice.

Suggested strings, in the game's own voice rather than a legal one — updated, because
the old pair said the monster is the thing you locate by sound and in a race the more
useful sentence is about the other nineteen people:

```
헤드폰을 쓰세요. 남이 몇 층에 있는지는 발소리로만 알 수 있습니다.      [알겠습니다]
```

```
Put headphones on. Footsteps are the only thing that tells you what floor
somebody is on.                                                          [Got it]
```

---

## 5 · The store's own audio flags

Set on the partner site, not in the copy, and filterable — a player can browse Steam
by these. The full category list is [copy-en.md §6](copy-en.md).

| Flag | Set | Why |
|---|---|---|
| Stereo / surround / 3D audio support | ✅ 3D audio | §05 — the mix is camera-relative and binaural by design |
| Voice chat | ⚠ **hold** | §13's proximity voice is written (`Assets/Scripts/Steam/Voice/`, `VoiceCutoffDistance = 30f`) and **has never been heard by two people**. Claiming a feature that fails on launch day is worse than not listing it |
| Captions / subtitles available | ❌ | there is no scripted dialogue to caption; do not tick it to look accessible |

---

## 6 · What this notice is not allowed to become

An excuse. And the pivot made the underlying measurement problem **worse**, not
better, which is the opposite of what deleting a role suggests.

The old game asked one player to hear the creature. The new copy asks every player to
hear *what floor another player is on* — through a floor, through walls, at range.
That is a strictly larger claim about the mix, and the two open findings against it
were both measured under the smaller one:

- [F-003](../BALANCE-FINDINGS.md#f-003) — spectral separation between surfaces is
  **2.13× dry** and **1.396× at 25 m through a wall.** Through-structure is precisely
  the case the new headline claim depends on.
- [F-002](../BALANCE-FINDINGS.md#f-002) — gravel measures **26.1 dB quieter** than
  concrete at the 800 Hz corner the mix uses, while the game tells the player gravel
  is the clearer of the two. Those two surfaces are now **B4 and B1**, so an inverted
  clarity pair is an inverted *floor number*.

A headphone notice does not fix an inverted clarity table, and it does not make
1.396× audible. If the copy still says "footsteps tell you which floor somebody is
on" while F-002 and F-003 are open, the notice becomes the thing reviewers quote
back. **The notice is a hardware requirement, not a mix**, and §14's validation
question 5 — 「발소리의 방향·거리를 구별할 수 있는가?」 — is still unanswered.
