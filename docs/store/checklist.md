# Pre-launch checklist — what is still missing before the page can go up

[STEAM-RELEASE.md §7](../STEAM-RELEASE.md) already owns the full release
checklist: partner registration, the $100 App ID, W-8BEN, depots, branches,
promotion. **None of that is repeated here.** This file is only the store page,
and only what is *not done* as of 2026-08-01.

---

## 0 · The one-line answer

**The page can go up. The trailer cannot, and four of its ten beats cannot be
photographed at all.**

Capsules exist at every size Valve asks for, ten screenshots exist at 1920×1080,
and both descriptions are written. That clears Valve's stated minimum for a Coming
Soon page. What is missing is a trailer — the single highest-leverage asset on the
page — and the reason it is missing is not scheduling. It is that §03's clue prop,
§03's objective prop and §08's vehicle all render as untextured white primitives,
and those three objects are what three of the four beats worth cutting are *about*.

§14's warning still governs: **do not defer the page waiting for those.** Put it
up with what exists, and add the trailer as an update — a page live two months
early with four screenshots beats a perfect page live two weeks early.

---

## 1 · Blocking, and found by taking the screenshots

Every item here was discovered by pointing a camera at it tonight. None of them
appears in [ART.md §7](../ART.md), [BLOCKERS.md](../BLOCKERS.md) or
[STATUS.md §3](../STATUS.md), because nothing else in the project photographs
these objects.

| # | Defect | Consequence for the page | Evidence |
|:--:|---|---|---|
| S-1 | **§03's clue prop is a plain emissive white square.** No text, no plate, no document — a glowing rectangle on a wall. | The design's central mechanic — read it here, remember it, say it out loud — **cannot be shown at all**, in a screenshot or in the trailer. This is the biggest single gap on the page. | rendered three ways from two sites and one surface site; all three are the same white square |
| S-2 | **§08's surface vehicle is an untextured white box.** | The shop screenshot works only because §08's panel covers most of it; the box still shows through the panel as a grey rectangle. The "surface / round trip" beat cannot be shot. | `docs/store/trailer/beat02_surface.png` |
| S-3 | **§03's objective prop is a plain emissive white capsule.** | The carry beat — two hands, no torch, somebody has to light you — cannot be shot. §03 calls this the last decision of a match. | rendered at the objective site, seed 1204 |
| S-4 | **§08's loot props are plain emissive white cubes**, ~36 of them at 5 m spacing across the map. | Constrains framing everywhere. A search over six corridor anchors, every 0.5 m of advance, six glance angles and every monster distance from 3 m to 8 m found **exactly 6 clean framings, all at one anchor**, from which the monster is in frame and no white cube is. | search in `store_shots.py`'s probe output; the escape screenshot uses one of the six |
| S-5 | **Sky is visible from a lower-storey corridor** at (68.75, −5.87, 62.50), looking at the oversize loot. | A screenshot of a fifth basement level with a night sky in the corner is the kind of detail a Steam commenter finds in an hour. | ART.md §7.4 predicted it; this is the first frame that photographs it |
| S-6 | **Eight of the ten screenshots sit below ART.md's own legible-fraction floor** — 21.6 %–27.4 % against a 30 % floor — and the same eight are 41.4 %–46.7 % crushed against a 40 % ceiling. | The page will read as darker than every other horror page on Steam. **This is not a screenshot problem and must not be fixed in the screenshots** — STEAM-RELEASE.md §2.2: "Do not brighten the game for marketing; frame it instead." | `tools/render/frame_stats.py 'docs/store/screenshots/*.png'` |

S-6 is the open darkness regression from the map growth (ART.md § Measured
targets, STATUS §4.3), arriving on the store page. The two frames that *are* in
band are the two with a bright subject — the shop panel (50.7 % legible) and the
stairwell (33.0 %).

---

## 2 · Copy claims that are not yet true

Every ⚠ in [copy-en.md](copy-en.md) and [copy-ko.md](copy-ko.md), in one place. A
claim here is either made true, or the sentence is cut before the page is
submitted.

| Claim | Why it is not true today | Where |
|---|---|---|
| "The night gets worse in stages" | ✅ **no longer a problem** — **33.6 % of simulated matches reach tier 2 of 5**, 17.4 % tier 3, 13.0 % tier 4. The 1.2 % this row used to carry was measured against a map the game does not ship. **Still do not add a duration to that paragraph**: the median match is 7.2 min against §01's 25–35. | [F-006](../BALANCE-FINDINGS.md#f-006) |
| "One more floor is about three minutes… a battery run is one" | §07's *intended* costs, still not measured ones. The median simulated match is **7.2 minutes end to end** (re-measured 2026-08-01 on the real map; 2.5 was the wrong building), against §01's 25–35. | F-006 |
| Any "25–35 minute matches" line | **not written into the copy at all, deliberately.** Do not add it. It is the most natural sentence to write about this design and it is currently false by a factor of about 3.5 — 7.2 min median, 15.8 % of matches inside the window. It was an order of magnitude out until 2026-08-01; it moved a long way and it is still false. | §01 vs F-006 |
| Voice chat feature flag | `ProximityVoiceAudio.cs` and `SteamworksVoiceBackend.cs` exist; no two people have ever heard each other | STATUS §5 |
| Four-player anything | Mirror, Steamworks.NET and FizzySteamworks wired, `NetTests` green in PlayMode, **no two-instance session ever run** | STATUS §5 |
| Language: English interface | ⚠ **every screen in `Assets/Scripts/UI/` is Korean-only.** An English store page for a Korean-only UI is a refund queue. Either localise, or set the interface language honestly to Korean. | `docs/store/copy-ko.md` §7 |
| System requirements | every figure is an estimate; no player build has been profiled on any machine but this M1 Pro | copy-en.md §6 |
| The store name `Sanatorium Below` | a proposal. `요양원 지하` is the name of record (`MainMenuScreen.cs:128`). **Nobody has signed off the Latin name, and it is baked into every capsule.** | copy-en.md §1 |

---

## 3 · Missing store-page items

| Item | State | Blocked by |
|---|:--:|---|
| Trailer | ❌ none exists | S-1/S-2/S-3, plus no capture route in the repo (trailer.md §6) |
| A screenshot showing four players | ❌ impossible | no four-player session has ever been run |
| A screenshot of a clue being read | ❌ impossible | S-1 |
| First-launch headphone notice, in game | ❌ not built | nothing — this is an afternoon (headphone-notice.md §4) |
| Real system requirements | ❌ estimates | no profiled player build |
| Steam Playtest app | ❌ not requested | needs the page to exist first; then it is free and it is how §14's four-player questions get answered |
| Discord invite for the page | ❌ | §13 lists Discord as the community host |
| Store page in Korean *and* English | ⚠ copy written, UI is Korean-only | localisation |

---

## 4 · What is done

| Item | Where | Note |
|---|---|---|
| Header capsule 920×430 | `capsules/header_capsule_920x430.png` | ✅ |
| Small capsule 462×174 | `capsules/small_capsule_462x174.png` | ✅ legible at 120×45 — proofs in `capsules/legibility/` |
| Main capsule 1232×706 | `capsules/main_capsule_1232x706.png` | ✅ |
| Vertical capsule 748×896 | `capsules/vertical_capsule_748x896.png` | ✅ |
| Page background 1438×810 | `capsules/page_background_1438x810.png` | ✅ optional |
| Library capsule / header / hero / logo | `capsules/library_*.png` | ✅ hero carries no text, logo is transparent |
| Community icon 184×184, shortcut icon 256×256 | `capsules/*_icon_*.png` | ✅ |
| ≥ 5 screenshots at 1920×1080 | `screenshots/` | ✅ ten, gameplay only, no overlaid marketing text |
| Short description, both languages | copy-ko.md §2, copy-en.md §2 | ✅ 141 / 296 characters, both under the 300 limit |
| About This Game, both languages | copy-ko.md §3, copy-en.md §3 | ✅ BBCode, paste-ready |
| Feature bullets, both languages | §4 of each | ✅ |
| Tags | copy-en.md §5 | ✅ |
| Headphone notice, all four placements | headphone-notice.md | ✅ three written, one not built (§3 above) |
| Trailer shot list | trailer.md | ✅ with 13 rendered reference frames |

Two corrections to [STEAM-RELEASE.md §2.1](../STEAM-RELEASE.md), which was written
from memory and says so:

- The client icon is listed there as **32 × 32 TGA**. Steamworks' current asset
  index says **shortcut icon 256 × 256 .ico or .png** and **app icon 184 × 184
  .jpg**. Both are generated here at the current sizes.
- The library logo is listed as 1280 × 720; Steamworks says **"either 1280px wide
  and/or 720px tall"**, which is a constraint on one dimension, not a fixed
  canvas. 1280 × 720 satisfies it.

---

## 5 · Recommended order

1. **Pay the $100 and create the app.** It is the long pole: Valve requires ≥ 30
   days between paying and releasing, and the store page public ≥ 2 weeks before
   the release date. Everything else can happen while that clock runs.
   ([STEAM-RELEASE.md §1.2](../STEAM-RELEASE.md))
2. **Decide the Latin name.** It is baked into eleven capsule files and one
   command re-bakes them. Do it before the page is submitted, not after.
3. **Put the page up with what exists.** Capsules, ten screenshots, both
   descriptions, tags, headphone notice, no trailer. §14: 「7번을 늦추지 말 것」.
4. **Texture the four interactable props** — clue plate, objective, vehicle, loot.
   Four assets. They unblock the trailer's three missing beats, the clue
   screenshot and the surface screenshot, and they are the difference between a
   page that looks finished and one that looks like a prototype with good
   corridors.
5. **Play a four-player match.** Then re-shoot beat 2 and take the screenshot this
   page most needs and cannot have.
6. **Cut the trailer** (trailer.md §6, route A).
7. **Request a Steam Playtest.** Free, and it is how §14's questions 1 and 2 get
   answered by people who are not you.

Items 4 and 5 are not store-page work and they are what the store page is waiting
for. That is worth saying plainly: **the page is not blocked on marketing, it is
blocked on four textures and one multiplayer session.**
