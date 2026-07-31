# Store copy — English

Paste-ready. The blocks below are the exact strings for the Steamworks store-page
editor; everything outside a fenced block is a note to whoever is pasting.

Every claim traces to a section of [`docs/game-design.md`](../game-design.md), and
every claim that **is not true of the current build** is marked ⚠ and listed again
in [checklist.md](checklist.md) §2. Nothing marked ⚠ may go on the page until it
is true or the sentence is cut.

---

## 1 · Name

The lockup on the capsules is bilingual, so both strings have to be the game's
name for Valve to accept it. Set it up this way and it is:

| Field | Value |
|---|---|
| App name (English store) | `Sanatorium Below` |
| Localized name (Korean) | `요양원 지하` |

`요양원 지하` is what the in-game main menu already says
(`MainMenuScreen.cs:128`), so it is the name of record. **`Sanatorium Below` is a
proposal, not a decision** — it is the only string in this folder nobody has
signed off. Change it in one place and regenerate:

```sh
python3 tools/render/store_capsules.py --title "요양원 지하" --subtitle "YOUR NAME HERE" --check
```

---

## 2 · Short description

Shown in search results, on hover, and in every recommendation queue. 300
characters is the hard limit; this is 296 including spaces (counted, not estimated), and it carries the
§05 headphone line because that is the one place a wishlister will definitely
read it.

```
Four-player co-op horror. You all see and hear the same things — only what you can DO is different. The monster cannot be killed. Clues cannot be carried out: read them where they stand, remember them, say them out loud. Time is the only currency and the wallet is shared. Headphones recommended.
```

---

## 3 · About This Game

Steam BBCode. Paste as-is.

```
[b]HEADPHONES RECOMMENDED.[/b] One of the five roles locates the monster by ear alone — direction, distance, and which floor it is walking on. On laptop speakers that role does not function.

[h2]Four people. Five roles. One of them is missing.[/h2]
Everybody sees the same darkness and hears the same footsteps. This is not a game where one player stares at a black screen while the others play. The asymmetry is in what you can [i]do[/i], never in what you are allowed to know.

[list]
[*][b]Listener[/b] — finds the monster by sound. Cannot hear while making noise.
[*][b]Observer[/b] — sees what the monster sees, and therefore who it is coming for. Only within 15 m, and only while standing perfectly still for three seconds.
[*][b]Runner[/b] — the only one who can outrun it. Twelve seconds of sprint, then you are everybody else.
[*][b]Engineer[/b] — locks doors, lights whole zones, opens safes. Every one of those decisions can kill a teammate.
[*][b]Flare[/b] — stuns it briefly, and can do it again.
[/list]

Four of the five go in. The one you left behind is what makes tonight different from last night. You can buy a worse substitute for the missing role — except for the Observer, whose information is the one thing money cannot buy.

[h2]It cannot be killed.[/h2]
There is no weapon in this game and there will not be one. A flare, a locked door, a barricade: all of it buys seconds, and seconds are all you get. Walking is 2.0 m/s. Running is 4.5. The monster is 4.8. It is faster than your run by three tenths of a metre a second, which is close enough to feel survivable and never is.

Only the Runner's sprint, at 5.6, is faster — and gaining 0.8 m/s for twelve seconds buys 9.6 m against a 12 m break distance. The arithmetic does not close. You do not escape by running. You escape by using the building: two ten-metre legs of an S-corridor, a loop, a door that shuts.

[h2]The clue does not come out with you.[/h2]
The thing you need is written on a plate on a wall, five storeys down, and it stays there. No screenshot, no item, no note. You read it in the dark, holding a torch steady, and then you have to remember it and [i]say it out loud[/i] to three other people.

Which is how "was that a 6 or a 9?" becomes the argument that kills you. Some clues are planted to be misread: 6 and 9 upside down, 1 and 7 in handwriting, left and right in a mirror. The building will not tell you which one you got wrong.

[h2]Time is the only currency.[/h2]
There is no stamina bar to manage, no hunger, no sanity meter. Everything you might do costs the same single resource: the clock. One more floor is about three minutes. Going out for a battery is one. Picking up one more piece of loot is forty seconds. Standing in the shop arguing is thirty.

And you can only find out what time it is by going outside, which costs a minute. The night gets worse in stages and does not go back: the torch shrinks, the patrols widen, the monster gets faster, and eventually it learns where the exit is.

[h2]One wallet. Four opinions.[/h2]
Credits are the team's, not yours. So every purchase is a negotiation held out loud, at the van, while the clock runs. An upgraded torch doubles what you can see and doubles the distance from which you can be seen. A silencer hides your footsteps from the monster and from your own Listener. Chalk marks the way back for you and for it.

Loot is optional. You can clear a run without picking up a single thing — and then you will be poor, and being poor is slow.

[h2]Looking back is how you die.[/h2]
Backing up is 65% speed, which is slower than the monster. So checking the distance costs you distance. The skill is the forty-five degree glance: 95% speed, and it is just at the edge of your vision. How far you turn your mouse is the whole difference between a player who gets away and one who does not.

[h2]Dying does not end your evening.[/h2]
The dead see the whole building through its walls — including exactly where the living are going wrong — and cannot say a word about it. You get to nudge an object every forty-five seconds. That is your entire vocabulary.

[h2]What this is, honestly[/h2]
This is a store page for a game that is not finished, put up early so it can collect wishlists, which is what Steam pages are for.

Everything above is the design. What is running today is a vertical slice: [b]one building of five basement storeys, one monster, and one player[/b]. The rules, the economy, the clue system and the five roles are all implemented and tested. The four-player networking layer is written and passes its own tests, and [b]nobody has yet played a four-player match[/b]. Proximity voice is built and unproven.

If that is not the deal you want, wishlist it and wait — the page will say so when it changes. Nothing on this page is a render, a mock-up or concept art. Every screenshot is the game, at the brightness the game actually runs at.
```

⚠ **Checks before this goes up:**

| Line | Status |
|---|---|
| "One of the five roles locates the monster by ear" | true in the same room; degrades badly through a wall (F-002, F-003). The sentence is honest as written because it does not claim a range. |
| The five role descriptions | all five implemented and unit-tested; only Engineer and Runner have been exercised in a played loop (STATUS §5) |
| "buy a worse substitute for the missing role" | §11, implemented in the shop |
| Every speed figure | measured on real geometry: 4.80 m/s corridor against §06's 4.8, 0.80 m/s sprint gap against §06's 0.8 (STATUS §1.3) |
| "Some clues are planted to be misread" | §03 confusion pairs, implemented and tested |
| "The night gets worse in stages" | ✅ **implemented and reached.** 33.6 % of simulated matches reach tier 2 of 5, 17.4 % tier 3, 13.0 % tier 4 (F-006, re-measured 2026-08-01 on the real map; the 1.2 % this row used to say was measured against a building the game does not ship). The copy still does not say how long it takes, and should not — the median match is 7.2 min. |
| "One more floor is about three minutes" | ⚠ **§07's stated cost, not a measured one.** Median simulated match is 7.2 min total (was 2.5 against the wrong map), so three minutes for one floor is no longer absurd on its face — but it is still unmeasured. Measure it or cut this paragraph's numbers. |
| Proximity voice | code exists (`ProximityVoiceAudio.cs`, `SteamworksVoiceBackend.cs`); never heard by two humans |
| "nobody has yet played a four-player match" | true, and stated |

---

## 4 · Feature bullets

Steam has no separate bullet field — these belong at the top of About This Game if
you want them, or in the first Store Page "Highlights". Kept short enough to read
at a glance.

```
[list]
[*] Four players, five roles, one of them missing every run — and the gap is the run's character.
[*] A monster you cannot kill, that is exactly 0.3 m/s faster than your run.
[*] Clues that cannot be carried out. Read it, remember it, say it out loud, be wrong.
[*] Time is the only currency: every choice is priced in the same clock, and the night gets worse.
[*] One shared wallet, so every purchase is an argument.
[*] Looking over your shoulder costs 35% of your speed. The 45° glance is the skill.
[*] Death makes you a ghost who can see everything and say nothing.
[*] Built for headphones — 3D audio is a mechanic, not a garnish.
[/list]
```

---

## 5 · Tags

Up to 20. Tags are a distribution decision, not a description — they decide which
discovery queues the page appears in.

```
Co-op, Online Co-Op, Horror, Survival Horror, Multiplayer, Asymmetric, First-Person,
3D, Atmospheric, Dark, Team-Based, Exploration, Psychological Horror, Stealth,
Difficult, Singleplayer
```

`Singleplayer` is last and is there because ⚠ **it is the only mode that currently
works.** Remove it the day four-player is real; leaving it on afterwards attracts
exactly the wrong audience.

---

## 6 · System requirements

`Additional Notes` is where §13's 헤드폰 권장 obligation is discharged for the
requirements block — see [headphone-notice.md](headphone-notice.md) for the other
three places.

```
Minimum:
  OS:        Windows 10 64-bit / macOS 12
  Processor: 4-core, 2.5 GHz
  Memory:    8 GB RAM
  Graphics:  DX11-capable, 2 GB VRAM
  Network:   Broadband internet connection
  Storage:   4 GB available space
  Additional Notes: HEADPHONES STRONGLY RECOMMENDED. One of the five roles locates
  the monster by ear; stereo speakers make that role unplayable. A microphone is
  required for team play — the game is built around talking.

Recommended:
  OS:        Windows 11 64-bit / macOS 14 (Apple silicon)
  Processor: 6-core, 3.0 GHz
  Memory:    16 GB RAM
  Graphics:  GTX 1660 / RX 5600 / Apple M1 or better
  Network:   Broadband internet connection
  Storage:   4 GB available space
  Additional Notes: Headphones. Closed-back if you have them.
```

⚠ **Every number in that block is an estimate.** No player build has been profiled
on any machine other than the M1 Pro this was written on, and the only frame
figures that exist are editor-side renderer timings (ART.md §6: 8.89 ms typical,
20.25 ms on the worst viewpoint, no physics/AI/UI). Measure a real build before
this goes public — a wrong minimum spec is a refund queue.

---

## 7 · Feature flags to set on the partner site

Not copy, but they live or die with it, and they are filterable store attributes.

| Flag | Set to | Why |
|---|---|---|
| Online Co-op | ✅ 4 players | §11 |
| Single-player | ✅ | ⚠ currently the only true one |
| Steam Cloud | ✅ | §13 — saves cost nothing |
| Voice chat | ⚠ hold | §13's proximity voice is built and unproven; do not claim it until two people have heard each other |
| Full controller support | ❌ | §05 is a mouse-look design; nothing has been tested on a pad |
| Steam Achievements / Stats | ✅ when §13's telemetry buckets are defined | they are the balance data for §06 |
| Languages | Korean (interface, full audio), English (interface) | ⚠ the game's own UI is Korean-only today — every screen in `Assets/Scripts/UI/` |
