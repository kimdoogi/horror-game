# Pre-launch checklist — what is still missing before the page can go up

> **Rewritten 2026-08-03 for the race.** The previous version was measured on
> 2026-08-01 against a four-player co-op looting game that was deleted the next day
> ([DESCENT-PIVOT.md](../DESCENT-PIVOT.md)). Three of its six blocking defects went
> away with the systems they were about; one much larger one arrived.

[STEAM-RELEASE.md](../STEAM-RELEASE.md) owns the release process — partner
registration, the $100 App ID, W-8BEN, depots, branches, promotion — and Part I of it
owns the audit of where this repo stands. **None of that is repeated here.** This file
is only the store page.

---

## 0 · The one-line answer

**The copy can go up. The pictures cannot.**

That is the exact inverse of what this file said on 2026-08-01, and it is worth
saying in those words, because the instinct will be to reuse what was already there.
Both descriptions have been rewritten for the race and every claim in them is traced
in [copy-en.md §10](copy-en.md). What is now wrong is the imagery: **ten screenshots
and thirteen trailer reference frames all photograph the deleted co-op building**,
two of them photographing systems (the shop, "five storeys") that no longer exist in
the design at all.

Capsule *sizes* are all correct and were verified with `sips` on 2026-08-03
([STEAM-RELEASE.md §I.3.2](../STEAM-RELEASE.md)) — but the name printed on them is
one of the two open decisions below.

§14's warning still governs: **do not defer the page.** But a Coming Soon page whose
screenshots show a shop screen for a game with no economy is not "imperfect", it is
inaccurate, and Valve's store review reads screenshots.

---

## 1 · Blocking

| # | Item | Consequence for the page | Evidence |
|:--:|---|---|---|
| B-1 | **All ten screenshots are of the deleted game.** `05_the_shop.png` photographs the §08 economy the pivot removed; `06_five_storeys.png` names five storeys where the design has eight; the rest are the five-storey sanatorium the descent map replaced. | The page cannot be submitted. It also makes the copy's closing sentence — *"Every screenshot is the game"* — false, and that sentence is the page's whole credibility argument. | `docs/store/screenshots/`, all dated 2026-08-01 01:59, seed 1204 |
| B-2 | **The name is undecided and it is printed on eleven capsules.** The design documents say 하강, the shipped menu says 요양원 지하, the capsules say 요양원 지하 / SANATORIUM BELOW. | Valve requires the capsule to carry the store name. Submitting with the three disagreeing is a review round-trip. | [copy-en.md §1](copy-en.md) — with a recommendation and the one command that re-bakes the set |
| B-3 | **The thirteen trailer reference frames and their camera spec are stale.** `tools/render/trailer_frames.json` holds coordinates on a map that is no longer generated. | Not blocking the page (Valve does not require a trailer for Coming Soon) but blocking every frame anybody tries to shoot. | [trailer.md §0](trailer.md) |
| B-4 | **`docs/store/party.mp4` is not a trailer and is named after the deleted party.** 1280 × 720, 3.00 s, 24 fps, 1.62 Mbps against Valve's 1920 × 1080 / 30 or 60 fps / 5,000+ Kbps. | Only that somebody might mistake it for an asset. | `ffprobe`, quoted in [STEAM-RELEASE.md §I.3.3](../STEAM-RELEASE.md) |
| B-5 | **Eight of the ten screenshots sit below ART.md's own legible-fraction floor** — 21.6 %–27.4 % against a 30 % floor, and 41.4 %–46.7 % crushed against a 40 % ceiling. | The page reads darker than every other horror page on Steam. **This is not a screenshot problem and must not be fixed in the screenshots** — STEAM-RELEASE.md §2.2: *"Do not brighten the game for marketing; frame it instead."* | `tools/render/frame_stats.py 'docs/store/screenshots/*.png'` |

B-5 survives the pivot unchanged: it is the open darkness regression from the map
growth, and it will arrive on the new screenshots too unless the re-shoot frames for
a subject. The descent gives it two subjects the old building did not have — the lit
outer ring, and the glowing chute mouth visible from anywhere on the inner ring
(§03). **Shoot those.**

### What stopped being a blocker

Three of the six defects on the old list were about props for systems that no longer
exist. They are not fixed; they are irrelevant.

| Old defect | State |
|---|---|
| S-1 · §03's clue prop is a plain emissive white square | **moot** — the clue system was deleted |
| S-2 · §08's surface vehicle is an untextured white box | **moot** — there is no surface and no round trip |
| S-3 · §03's objective prop is a plain emissive white capsule | **moot** — there is no objective to carry |
| S-4 · §08's loot props are ~36 white cubes across the map | ⚠ **unresolved, and possibly still true.** The loot economy is deleted from the design. Whether the props are still spawned into the descent map decides whether they still constrain every framing in the building. **Check this while re-shooting** — it is the difference between six clean framings in a building and any framing you like |
| S-5 · sky visible from a lower-storey corridor | ⚠ **re-check.** The descent stacks eight storeys in one column instead of spiralling; whether the leak survived that is unknown |

The frames in `docs/store/defects/` document the old building and should be kept as
evidence of what was measured, not deleted — but nothing there is a live defect
report any more except S-4 and S-5.

---

## 2 · Copy claims that are not yet true

Every ⚠ in [copy-en.md §10](copy-en.md) and [copy-ko.md §9](copy-ko.md), in one
place. A claim here is either made true, or the sentence is cut before the page is
submitted.

| Claim | Why it is not true today | Where |
|---|---|---|
| "Every screenshot is the game" | The ten on disk are the deleted co-op building. **The single hardest gate on this page.** | B-1 above |
| Proximity voice | `Assets/Scripts/Steam/Voice/` exists and `VoiceCutoffDistance = 30f`. **No two people have ever heard each other.** The Voice Chat category stays unticked | STEAM-RELEASE.md §I.4.1 |
| "Twenty players" | Design, not measurement. `RaceRunnersMax = 20` but the cap that executes is `PlayersPerMatch = 4`, and **the measured peer count in this repository is zero**. The copy states this outright in its honest paragraph — do not let it drift into a boast anywhere else | STEAM-RELEASE.md §I.4.2–3 |
| Ring lighting — outer lit, inner torch-only | §03's rule. Confirm the descent map actually carries per-ring lighting before that paragraph ships | copy-en.md §10 |
| Battery fixed per match, no resupply | §03's rule, inherited from the deleted §08. Simpler now that there is no shop, never measured on a race map | copy-en.md §10 |
| Any match duration | **Not written into the copy at all, deliberately.** Do not add one. §01 says 12–20 minutes and nothing has measured it; F-006's 7.2-minute median was measured on the co-op game and does not transfer | §01 vs F-006 |
| Language: English interface | ⚠ **every screen in `Assets/Scripts/UI/` is Korean-only.** An English store page over a Korean-only UI is a refund queue. Either localise, or set the interface language honestly to Korean | copy-ko.md §8 |
| System requirements | Every figure is an estimate, made for a four-player game, now describing one with up to twenty networked players. No player build has been profiled on any machine but this M1 Pro, and **no Windows IL2CPP player has ever been produced** | copy-en.md §7 |
| The store name | Undecided. See B-2 | copy-en.md §1 |

---

## 3 · Missing store-page items

| Item | State | Blocked by |
|---|:--:|---|
| Screenshots of the descent | ❌ none exist | the re-shoot; the map is live work elsewhere in this repo |
| A screenshot with more than one player in it | ❌ impossible | no two peers have ever connected |
| Trailer | ❌ none exists | B-3, B-4, and four of ten beats need other players ([trailer.md §4](trailer.md)) |
| Client icon, 32 × 32 TGA | ❌ wrong size and format on disk | `capsules/shortcut_icon_256x256.png` — see STEAM-RELEASE.md §I.3.3 |
| Library hero 860 × 380 safe area | ⚠ unchecked | the number is not recorded in [assets.md](assets.md); re-check before upload |
| First-launch headphone notice, in game | ❌ not built | nothing — it is an afternoon ([headphone-notice.md §4](headphone-notice.md)) |
| Real system requirements | ❌ estimates | no profiled player build; no Windows IL2CPP build at all |
| Steam Playtest app | ❌ not requested | needs the page to exist first; then it is free and it is how the twenty-player questions get answered |
| Discord invite for the page | ❌ | §13 lists Discord as the community host |
| Store page in Korean *and* English | ⚠ copy written, UI is Korean-only | localisation |

---

## 4 · What is done

| Item | Where | Note |
|---|---|---|
| Header capsule 920 × 430 | `capsules/header_capsule_920x430.png` | ✅ size verified with `sips` 2026-08-03 |
| Small capsule 462 × 174 | `capsules/small_capsule_462x174.png` | ✅ legible at 120 × 45 — proofs in `capsules/legibility/` |
| Main capsule 1232 × 706 | `capsules/main_capsule_1232x706.png` | ✅ |
| Vertical capsule 748 × 896 | `capsules/vertical_capsule_748x896.png` | ✅ |
| Page background 1438 × 810 | `capsules/page_background_1438x810.png` | ✅ optional |
| Library capsule / header / hero / logo | `capsules/library_*.png` | ✅ hero carries no text, logo is transparent |
| Community icon 184 × 184 | `capsules/community_icon_184x184.png` | ✅ |
| Short description, both languages | copy-ko.md §2, copy-en.md §2 | ✅ **189 / 295 characters**, both under the 300 limit, both written to answer "what makes this different" |
| About This Game, both languages | copy-ko.md §3, copy-en.md §3 | ✅ BBCode, paste-ready, race only |
| Feature bullets, both languages | §4 of each | ✅ |
| Tags | copy-en.md §5 | ✅ sixteen, with `Co-op` and `Online Co-Op` removed and a reason recorded for every removal |
| Genre and category fields | copy-en.md §6 | ✅ including the decision **not** to tick Single-player |
| Headphone notice, all four placements | headphone-notice.md | ✅ three written, one not built (§3 above) |
| Trailer shot list | trailer.md | ✅ ten beats for the race; four shootable today |

**Every capsule is the right number of pixels and possibly the wrong name.** Sizes
and names are separate problems and only the first one is solved (B-2).

---

## 5 · Recommended order

This is the store-page slice of
[STEAM-RELEASE.md §I.5.4](../STEAM-RELEASE.md)'s recommendation, and it does not
disagree with it.

1. **Pay the $100 and create the app.** Not store-page work, and still the longest
   pole: Valve requires ≥ 30 days between paying and releasing and the page public
   ≥ 2 weeks before the release date. Everything below runs while that clock does.
2. **Decide the name** (B-2). It is baked into eleven capsule files and one menu
   string, and one command re-bakes the set. Do it before the page is submitted, not
   after. [copy-en.md §1](copy-en.md) recommends 하강 / DESCENT and says why.
3. **Re-shoot the ten screenshots on the descent map** (B-1). This is the one item
   that actually blocks submission. Frame for the two subjects the descent has that
   the old building did not: the lit outer ring with the start marks on it, and the
   glowing chute mouth seen from the dark inner ring. Check S-4 and S-5 while the
   camera is out.
4. **Put the page up.** Capsules, ten new screenshots, both descriptions, tags,
   categories, headphone notice, no trailer, **no release date**.
5. **Cut the 30-second teaser** from beats 1, 5, 7 and 8 ([trailer.md §4](trailer.md)).
   Four beats, all shootable today, and they are the four strongest in the list.
6. **Get two peers connected.** Then four. Then twenty. This is what beats 2, 6 and
   half of 3, 4 and 9 are waiting on, and it is what the whole page is waiting on.
7. **Request a Steam Playtest.** Free, and it is the only realistic way to find the
   twenty-player problems, because one person on one machine cannot.

> **The page is no longer blocked on marketing and it is no longer blocked on four
> textures.** It is blocked on one camera pass over a map that is being built right
> now, and everything after that is blocked on two computers talking to each other.
