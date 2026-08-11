# Pre-launch checklist — what is still missing before the page can go up

> **Rewritten 2026-08-03 for the race.** The previous version was measured on
> 2026-08-01 against a four-player co-op looting game that was deleted the next day
> ([DESCENT-PIVOT.md](../DESCENT-PIVOT.md)). Three of its six blocking defects went
> away with the systems they were about; one much larger one arrived.
>
> **Audited 2026-08-12.** Three items on this list had stopped being true: a trailer
> now exists (B-4 was written when none did), S-4 is closed by the artefact, and the
> copy no longer sells an elimination rule the game deleted on 2026-08-04. B-1, B-2,
> B-3 and B-5 all survive the audit unchanged — B-5's numbers were re-measured and
> came back identical.

[STEAM-RELEASE.md](../STEAM-RELEASE.md) owns the release process — partner
registration, the $100 App ID, W-8BEN, depots, branches, promotion — and Part I of it
owns the audit of where this repo stands. **None of that is repeated here.** This file
is only the store page.

---

## 0 · The one-line answer

**The copy can go up. The pictures cannot.**

That is the exact inverse of what this file said on 2026-08-01, and it is worth
saying in those words, because the instinct will be to reuse what was already there.
Both descriptions have been rewritten for the race — twice now; the second pass, on
2026-08-12, cut an elimination rule the game deleted on 2026-08-04 — and every claim in
them is traced in [copy-en.md §10](copy-en.md). **The trailer moved to the other side of
this line:** `trailer_descent.mp4` is 1920 × 1080 / 30 fps / 41.57 s / 11,960 Kbps and
clears every Valve requirement. What is still wrong is the *still* imagery: **ten
screenshots and thirteen stale trailer reference frames all photograph the deleted co-op
building**, two of them photographing systems (the shop, "five storeys") that no longer
exist in the design at all.

Capsule *sizes* are all correct — re-verified with `sips` on **2026-08-12**, all eleven
plus all five legibility proofs, to the pixel
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
| B-3 | **The thirteen trailer reference frames and their camera spec are stale.** `tools/render/trailer_frames.json` holds coordinates on a map that is no longer generated. | Still true, and still not blocking the page — but it no longer blocks the trailer either. The cut that shipped uses `tools/render/trailer_shots.json`, which carries anchor keywords instead of world coordinates. `trailer_frames.json` and the thirteen `docs/store/trailer/*.png` are now dead weight, not a blocker. | [trailer.md §0](trailer.md) |
| B-4 | **`docs/store/party.mp4` is not a trailer and is named after the deleted party.** Re-measured 2026-08-12: 1280 × 720, 24 fps, 3.00 s, 1,625 Kbps against Valve's 1920 × 1080 / 30 or 60 fps / 5,000+ Kbps. | Only that somebody might mistake it for an asset — and there is now a real one beside it to be mistaken *for*. Move `party.mp4` out of `docs/store/`. | `ffprobe`, re-run 2026-08-12; also quoted in [STEAM-RELEASE.md §I.3.3](../STEAM-RELEASE.md) |
| B-5 | **Eight of the ten screenshots sit below ART.md's own legible-fraction floor** — 21.6 %–27.4 % against a 30 % floor, and 41.4 %–46.7 % crushed against a 40 % ceiling. | The page reads darker than every other horror page on Steam. **This is not a screenshot problem and must not be fixed in the screenshots** — STEAM-RELEASE.md §2.2: *"Do not brighten the game for marketing; frame it instead."* | `cd docs/store/screenshots && python3 ../../../tools/render/frame_stats.py '*.png'` — **re-run 2026-08-12, every figure identical.** The only two frames in band are the two being deleted |

B-5 survives the pivot unchanged: it is the open darkness regression from the map
growth, and it will arrive on the new screenshots too unless the re-shoot frames for
a subject. The descent gives it two subjects the old building did not have — the lit
outer ring, and the glowing chute mouth visible from anywhere on the inner ring
(§03). **Shoot those.**

### What stopped being a blocker

Four of the six defects on the old list were about props for systems that no longer
exist. They are not fixed; they are irrelevant.

| Old defect | State |
|---|---|
| S-1 · §03's clue prop is a plain emissive white square | **moot** — the clue system was deleted |
| S-2 · §08's surface vehicle is an untextured white box | **moot** — there is no surface and no round trip |
| S-3 · §03's objective prop is a plain emissive white capsule | **moot** — there is no objective to carry |
| S-4 · §08's loot props are ~36 white cubes across the map | ✅ **closed 2026-08-12 — moot, and checked rather than assumed.** No `LootSpawn` in any `Assets/Scenes/Descent/*.unity`. `DescentMap.cs:423` records what happened to them: the 152 전리품 at every 막힌 길 were folded into **176 `ReachProbe_` 도달 지점** at the same cells, and a 도달 지점 is a pathfinding probe with no prop on it. **Every framing in §4's list is free.** |
| S-5 · sky visible from a lower-storey corridor | ⚠ **still open, and it cannot be closed by reading.** The descent stacks eight storeys in one column instead of spiralling; whether the leak survived that needs a frame. Answer it while the camera is out for B-1 |

The frames in `docs/store/defects/` document the old building and should be kept as
evidence of what was measured, not deleted — but **S-5 is now the only live defect
report in that folder.**

---

## 2 · Copy claims that are not yet true

Every ⚠ in [copy-en.md §10](copy-en.md) and [copy-ko.md §9](copy-ko.md), in one
place. A claim here is either made true, or the sentence is cut before the page is
submitted.

| Claim | Why it is not true today | Where |
|---|---|---|
| "Every screenshot is the game" | The ten on disk are the deleted co-op building. **The single hardest gate on this page.** | B-1 above |
| Proximity voice | `Assets/Scripts/Steam/Voice/` exists (14 classes) and `GameConstants.VoiceCutoffDistance = 30f`. `VoiceSocketTests` runs it over a real socket. **No two people have ever heard each other.** The Voice Chat category stays unticked | `Assets/Scripts/Steam/Voice/`, `Assets/Tests/PlayMode/Voice/VoiceSocketTests.cs` |
| "Twenty players" | ✏️ **re-derived 2026-08-12, and the old wording here was wrong twice.** The executing cap is **twenty**, not four: `Net/HorrorGameNetworkManager.cs:88` sets `maxConnections = GameConstants.RaceRunnersMax` and Mirror enforces it; `PlayersPerMatch = 4` is `NetLobby`'s seat count on a branch the manager's own comment calls *"currently unreachable"*. And the peer count is not zero — twenty instances have connected on this desk. What is still true, and is the only thing the copy claims: **no person has played a match.** | `HorrorGameNetworkManager.cs:60–95, 330–346`; STATUS.md *"nobody has played it"*. STEAM-RELEASE.md §I.4.2–3 is the stale source of both errors |
| ~~"Caught is out, unranked"~~ | ✅ **fixed 2026-08-12.** The rule changed on 2026-08-04 (`e0fa042`) — caught sends a runner back to their B1 cell and they keep racing — and both descriptions sold elimination for eight days after. Rewritten in copy-ko §2/§3/§4 and copy-en §2/§3/§4, with the evidence rows in copy-en §10 and copy-ko §9 | `RaceState.ReportCaught`, `MatchDirector.cs:1666`, `CaughtScreen.cs`; [trailer.md §7](trailer.md) is where it was caught |
| Ring lighting — outer lit, inner torch-only | §03's rule. Confirm the descent map actually carries per-ring lighting before that paragraph ships | copy-en.md §10 |
| Battery fixed per match, no resupply | §03's rule, inherited from the deleted §08. Simpler now that there is no shop, never measured on a race map | copy-en.md §10 |
| Any match duration | **Not written into the copy at all, deliberately.** Do not add one. §01 says 12–20 minutes and nothing has measured it; F-006's 7.2-minute median was measured on the co-op game and does not transfer | §01 vs F-006 |
| Language: English interface | ⚠ **every screen in `Assets/Scripts/UI/` is Korean-only.** An English store page over a Korean-only UI is a refund queue. Either localise, or set the interface language honestly to Korean | copy-ko.md §8 |
| System requirements | Every figure is an estimate, made for a four-player game, now describing one with up to twenty networked players. **No player build has ever been launched**, here or anywhere, so nothing has been profiled — `dist/windows-x64/HorrorGame.exe` exists (2026-08-10) but is a Development **Mono** build, `shippableOnSteam: false`, and **no Windows IL2CPP player has ever been produced** | copy-en.md §7; `dist/windows-x64/build-report.json` |
| The store name | Undecided. See B-2 | copy-en.md §1 |

---

## 3 · Missing store-page items

| Item | State | Blocked by |
|---|:--:|---|
| Screenshots of the descent | ❌ none exist | the re-shoot; the map is live work elsewhere in this repo |
| A screenshot with more than one player in it | ❌ nothing has recorded a player window | the rig can place a second *body* ([trailer.md §3](trailer.md)) but nothing in this repo captures a real session, and no two people have played |
| Trailer | ✅ **`docs/store/trailer_descent.mp4` exists** — 1920 × 1080, 30 fps, 41.57 s, 11,960 Kbps, clearing every Valve requirement (`ffprobe`, 2026-08-12) | nothing. It is uploadable. What it is *missing* is the five beats that need a human at a keyboard — [trailer.md §5](trailer.md) |
| ~~Client icon, 32 × 32 TGA~~ | ✅ **not a real item.** `capsules/shortcut_icon_256x256.png` is 256 × 256, which is what the current index page asks for | [assets.md §1](assets.md) corrects STEAM-RELEASE.md §2.1 on exactly this; the 32 × 32 TGA line was written from memory and this row was repeating it |
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
| Header capsule 920 × 430 | `capsules/header_capsule_920x430.png` | ✅ size verified with `sips` 2026-08-12 |
| Small capsule 462 × 174 | `capsules/small_capsule_462x174.png` | ✅ legible at 120 × 45 — proofs in `capsules/legibility/` |
| Main capsule 1232 × 706 | `capsules/main_capsule_1232x706.png` | ✅ |
| Vertical capsule 748 × 896 | `capsules/vertical_capsule_748x896.png` | ✅ |
| Page background 1438 × 810 | `capsules/page_background_1438x810.png` | ✅ optional |
| Library capsule / header / hero / logo | `capsules/library_*.png` | ✅ hero carries no text, logo is transparent |
| Community icon 184 × 184 | `capsules/community_icon_184x184.png` | ✅ |
| Short description, both languages | copy-ko.md §2, copy-en.md §2 | ✅ **190 / 296 characters** (re-counted 2026-08-12 after the elimination rewrite), both under the 300 limit, both written to answer "what makes this different" |
| About This Game, both languages | copy-ko.md §3, copy-en.md §3 | ✅ BBCode, paste-ready, race only, and no longer selling elimination |
| Feature bullets, both languages | §4 of each | ✅ |
| Tags | copy-en.md §5 | ✅ sixteen, with `Co-op` and `Online Co-Op` removed and a reason recorded for every removal |
| Genre and category fields | copy-en.md §6 | ✅ including the decision **not** to tick Single-player |
| Headphone notice, all four placements | headphone-notice.md | ✅ three written, one not built (§3 above) |
| Trailer, shot and encoded | trailer.md, `trailer_descent.mp4` | ✅ **23 shots · 1247 frames · 41.57 s**, rendered in-engine by `tools/render/descent_film.py` and verified against Valve's format table. Silent — the mix is an edit-suite job (trailer.md §9) |

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
   categories, headphone notice, **the trailer** — it exists and it passes Valve's
   format table — and **no release date**.
5. **Mix the trailer.** The encode is silent by design and the sound is the argument
   ([trailer.md §9](trailer.md)). Shots 8–15 are the eight surfaces and they are the
   beat that sells the headphone line; **resolve F-002 before mixing them**, because
   gravel still measures 17.8 dB quieter than concrete while the game says the opposite.
6. **Get two people to play it.** Not two peers — twenty *processes* already connect on
   one desk, and that is not the missing thing. Nobody has played a match. This is what
   trailer.md §5's five beats are waiting on, and it is what the whole page is waiting
   on.
7. **Request a Steam Playtest.** Free, and it is the only realistic way to find the
   twenty-player problems, because one person on one machine cannot.

> **The page is no longer blocked on marketing, on four textures, or on a trailer.**
> It is blocked on one camera pass over a map that is being built right now — ten
> frames — and everything after that is blocked on the thing this project has been
> blocked on for a fortnight: **not two computers talking to each other, which they
> already do, but two people playing a match.**
