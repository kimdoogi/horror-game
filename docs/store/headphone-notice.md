# The headphone notice

§13's setup table lists **헤드폰 권장 표기** as a shipping item, alongside the App
ID fee and the tax forms. It is not a courtesy line. §05 says why:

> 청음사의 방향 판별 — **몸을 돌려 삼각측량.** 3D 오디오는 카메라 기준 → **헤드폰 필수**

The 청음사 (Listener) locates the monster by ear: direction, distance, and which of
§12's five floor materials it is walking on. That is the whole role. On stereo
laptop speakers the cues collapse and **one of the five roles stops existing** —
not "works worse", stops existing. §14's validation question 5 is literally about
this, and it is a question about the player's output device as much as about the
mix.

So the notice goes in **four** places, because players arrive through different
doors and most of them never read a store page.

---

## 1 · Short description

Already in both drafts, as the closing clause — [copy-en.md](copy-en.md) §2,
[copy-ko.md](copy-ko.md) §2.

```
… Time is the only currency and the wallet is shared. Headphones recommended.
```

```
… 시간이 유일한 통화이고 지갑은 팀 공용이다. 헤드폰 권장.
```

Last clause rather than first: the hook has to survive the 300-character limit,
and a description that opens with a hardware note reads like an apology.

---

## 2 · About This Game — the first line, not the last

Already in both drafts as the opening line of the body. Above the fold, above the
first `[h2]`, before anything else competes with it.

```
[b]HEADPHONES RECOMMENDED.[/b] One of the five roles locates the monster by ear alone — direction, distance, and which floor it is walking on. On laptop speakers that role does not function.
```

```
[b]헤드폰을 권장합니다.[/b] 다섯 직업 중 하나는 오직 소리만으로 괴물의 방향과 거리, 그리고 그것이 어떤 바닥을 밟고 있는지를 알아냅니다. 노트북 스피커로는 그 직업이 작동하지 않습니다.
```

**Do not move this to the bottom of the page.** A "recommended peripherals" line
under the credits is where this notice goes to be ignored.

---

## 3 · System requirements → Additional Notes

Both minimum **and** recommended. Steam shows only one of the two tabs at a time,
and a player comparing against a five-year-old laptop is reading the minimum tab.
Full text in [copy-en.md](copy-en.md) §6 and [copy-ko.md](copy-ko.md) §6.

The minimum-tab wording is deliberately the blunt one:

> HEADPHONES STRONGLY RECOMMENDED. One of the five roles locates the monster by
> ear; stereo speakers make that role unplayable. A microphone is required for
> team play — the game is built around talking.

The microphone half matters as much. §03 forbids carrying a clue out, so the game
is unplayable in silence, and §13's proximity voice is the intended channel. A
buyer who owns neither a headset nor a microphone should be able to work that out
before paying.

---

## 4 · In game, on first launch

**The store page is not read by the friend who was invited into the lobby.** That
person's first contact with the game is a Steam invite, and nothing on the store
page ever reaches them. This is the placement that actually protects the 청음사.

Two things already exist and one does not:

| Where | State |
|---|---|
| Main menu, along the bottom | ✅ **already there.** `Shots/menu_main.png` carries §05's headphone warning across the foot of the title screen (STATUS §4.2) |
| Settings → audio bus rows | ✅ each row already carries the § reference for why its range is what it is |
| First-launch, dismissible, once | ❌ **not built.** Tracked in [checklist.md](checklist.md) §3 |

The third one is the one that catches the invited friend, because a person who
joins a lobby from a Steam invite may never see the main menu at all. One line, a
dismiss button, a flag in Steam Cloud so it is never shown twice.

Suggested strings, in the game's own voice rather than a legal one:

```
헤드폰을 쓰세요. 괴물의 위치는 소리로만 알 수 있습니다.       [알겠습니다]
```

```
Put headphones on. The monster is only ever located by sound.     [Got it]
```

---

## 5 · The store's own audio flags

Set on the partner site, not in the copy, and filterable — a player can browse
Steam by these.

| Flag | Set | Why |
|---|---|---|
| Stereo / surround / 3D audio support | ✅ 3D audio | §05 — the mix is camera-relative and binaural by design |
| Voice chat | ⚠ **hold** | §13's proximity voice is written (`ProximityVoiceAudio.cs`, `SteamworksVoiceBackend.cs`) and has never been heard by two people. Claiming a feature that fails on launch day is worse than not listing it |
| Captions / subtitles available | ❌ | there is no spoken dialogue to caption; do not tick it to look accessible |

---

## 6 · What this notice is not allowed to become

An excuse. §14 question 5 — 「청음사가 방향·거리를 구별할 수 있는가?」 — is currently
**unanswered**, and the measurements say it will be answered "yes near, no far":
2.13× spectral separation dry, **1.396× at 25 m through a wall**, and one
inverted clarity pair (gravel/concrete) where the HUD tells the player the
opposite of what their ears report. Those are [F-002](../BALANCE-FINDINGS.md#f-002)
and [F-003](../BALANCE-FINDINGS.md#f-003), both open.

A headphone notice does not fix an inverted clarity table. If the role still
misinforms the player at release, the notice becomes the thing reviewers quote
back. The notice is a hardware requirement, not a mix.
