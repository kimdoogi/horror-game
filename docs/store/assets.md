# Store assets — every size, and where the number came from

> **Sizes are current. The pictures inside them are not.** Every capsule below was
> re-measured with `sips` on 2026-08-03 and every one matches Valve exactly
> ([STEAM-RELEASE.md §I.3.2](../STEAM-RELEASE.md)). But every screenshot and every
> trailer frame in this folder photographs the four-player co-op game deleted on
> 2026-08-02 ([DESCENT-PIVOT.md](../DESCENT-PIVOT.md)), and the capsules are built
> **from those renders** — so the art is stale even where the pixel count is right.
> §4 is now the re-shoot list.

Read off Steamworks on **2026-08-01** rather than recalled, because Valve has
changed these: the header capsule was 460 × 215 until the library redesign, and
[STEAM-RELEASE.md §2.1](../STEAM-RELEASE.md) — which says so itself — is out of
date on the icons.

| Source | Page |
|---|---|
| **store** | Store Graphical Assets · `https://partner.steamgames.com/doc/store/assets/standard` |
| **library** | Steam Library Assets · `https://partner.steamgames.com/doc/store/assets/libraryassets` |
| **index** | Graphical Asset Overview · `https://partner.steamgames.com/doc/store/assets` |

The partner site's uploader is the authority the moment it disagrees with any of
this.

---

## 1 · What is here

| Asset | Size | Source | File | Rule quoted on the source page |
|---|:--:|:--:|---|---|
| Header capsule | **920 × 430** | store | `capsules/header_capsule_920x430.png` | "The game's logotype should be easily legible against the background" |
| Small capsule | **462 × 174** | store | `capsules/small_capsule_462x174.png` | "should contain readable logo, even at smallest size… your logo should nearly fill the small capsule". Steam auto-generates **184 × 69** and **120 × 45** from it |
| Main capsule | **1232 × 706** | store | `capsules/main_capsule_1232x706.png` | "Do not include quotes or other strings of text beyond the title of your game" |
| Vertical capsule | **748 × 896** | store | `capsules/vertical_capsule_748x896.png` | same text rule |
| Page background | **1438 × 810** | store | `capsules/page_background_1438x810.png` | optional; derived from a screenshot if omitted |
| Library capsule | **600 × 900** | library | `capsules/library_capsule_600x900.png` | "graphically-centric"; **300 × 450** auto-generated |
| Library header | **920 × 430** | library | `capsules/library_header_920x430.png` | "should focus on the branding of your product" |
| Library hero | **3840 × 1240** | library | `capsules/library_hero_3840x1240.png` | **"This image cannot include any text."** 860 × 380 centre stays uncropped; **1920 × 620** auto-generated |
| Library logo | **1280 × 720** | library | `capsules/library_logo_1280x720.png` | "Either 1280px wide and/or 720px tall". Transparent PNG, logotype only |
| Community / app icon | **184 × 184** | index | `capsules/community_icon_184x184.png` | listed as `.jpg` on the index page; PNG here, convert on upload if the uploader insists |
| Shortcut icon | **256 × 256** | index | `capsules/shortcut_icon_256x256.png` | "256px x 256px .ico or .png" |
| Screenshots | **1920 × 1080 min, 16:9** | store | `screenshots/*.png` | "You must provide at least 5"; "Screenshots should exclusively show the gameplay of your game" |
| Bundle header | 707 × 232 | store | — | not built; only needed if you ever sell a bundle |
| Event cover / header | 800 × 450 / 1920 × 622 | index | — | not built; only needed for Steam events |

**Two corrections to STEAM-RELEASE.md §2.1**, which was written from memory:

- it lists a **32 × 32 TGA client icon**; the current index page says shortcut icon
  **256 × 256** `.ico`/`.png` and app icon **184 × 184** `.jpg`.
- it lists the library logo as a fixed 1280 × 720; the library page says *"either
  1280px wide and/or 720px tall"*, a constraint on one dimension. 1280 × 720
  satisfies it either way.

---

## 2 · Rules that get a page rejected

Straight off the source pages, and each one is a design constraint rather than a
formality:

- **Nothing textual beyond the title.** No review score, no award laurel, no
  "Wishlist now", no discount flash, no platform logo. The bilingual lockup used
  here is legal **only if both strings are the game's name** — see
  [copy-en.md §1](copy-en.md) for the two name fields that make that true.
- **The library hero carries no text at all**, because the library logo is
  composited over it at a position chosen on the partner site.
- **The small capsule has to be readable at its real size.** 462 × 174 is about a
  business card, and Steam then reduces it to 120 × 45. Proofs at both reduced
  sizes are written by `--check` into `capsules/legibility/`.
- **Screenshots are gameplay only.** No key art, no concept art, no UI mock-ups,
  no overlaid marketing text. The game's own HUD is gameplay and is fine; §14's
  developer guidance overlays are not, and the render rig switches them off for
  exactly that reason. (This line used to add "and §08's shop screen" — there is no
  shop. See §4.)
- **Keep important art out of the outer 10 %.** Steam crops.

---

## 3 · How every file here was made

```bash
# 1 · find camera positions in the current map (writes nothing to the project)
#     ⚠ seed 1204 was the five-storey co-op building. The descent map is
#       DescentMap.DefaultSeed = 20260802, eight storeys, all centred on cell (12,12)
python3 tools/render/store_shots.py probe --seed 20260802 --out /tmp/descent_probe.json

# 2 · render the screenshots at 1920×1080, in game, no -nographics
#     ⚠ tools/render/store_shots.json still names the old shots and holds coordinates
#       in a map that is no longer generated — rewrite it against §4's list first
python3 tools/render/store_shots.py shoot --spec tools/render/store_shots.json

# 3 · build every capsule from those renders, plus the legibility proofs
python3 tools/render/store_capsules.py --check

# 4 · the trailer's reference frames
python3 tools/render/store_shots.py shoot --spec tools/render/trailer_frames.json

# 5 · measure the frames against ART.md's bands
cd docs/store/screenshots && python3 ../../../tools/render/frame_stats.py '*.png'
```

Changing the name is one flag, and it re-bakes all eleven capsules:

```bash
python3 tools/render/store_capsules.py --title "요양원 지하" --subtitle "SANATORIUM BELOW" --check
python3 tools/render/store_capsules.py --variant latin --check      # Latin-first lockup
```

### The Unity half is staged, not resident

`tools/render/unity/StoreShotRig.cs` is copied into
`unity/HorrorGame/Assets/StoreShotStaging/Editor/` for the duration of one batch
run and removed afterwards, including its `.meta`. The store page owns
`tools/render/` and `docs/store/`; it does not own `Assets/`, and a permanent
editor script left in somebody else's assembly is the seam that produced
[B-005](../BLOCKERS.md#b-005). Nothing the rig does is saved — no scene is
written, and every object it creates is destroyed before the editor exits.

### Three things that will otherwise cost a night

- **Never pass `-nographics`.** It disables the graphics device and every frame
  comes out black. The driver deliberately does not pass it.
- **Exit code before frame count.** A Unity run that died on the project lock
  writes a log with no errors in it. `store_shots.py` waits the lock out and
  retries — it waited twice during the pass that produced these frames, because
  another Unity process held the project.
- **Each frame is rendered twice and the first is discarded.** Measured, not
  assumed: the first frame of a batch came back at 16.2 % legible where a second
  render of the same camera gave 25.3 %, and in another pass came back
  untextured and differently lit. URP resolves shadow atlases, SSAO history and
  streamed mips on the frame that first needs them, and a one-shot
  `Camera.Render` in batch mode photographs that resolve happening.

---

## 4 · The screenshots — the re-shoot list

> **Every frame in `screenshots/` is of a game that no longer exists.** They were
> shot 2026-08-01 01:59 on seed 1204, the five-storey sanatorium with a shop and a
> clue system. The pivot landed the next day. Two of them photograph deleted systems
> by name. **The page cannot be submitted with these** — see
> [checklist.md B-1](checklist.md).

### What is on disk, and what happens to it

| File | Was | Verdict |
|---|---|---|
| `01_corridor_and_beam.png` | the beam down 18 m of B5 tile | re-shoot — the corridor is right, the building is not |
| `02_the_monster_at_distance.png` | it is down the corridor and has seen you | re-shoot on an inner ring |
| `03_it_is_closer_now.png` | the same corridor, nine metres | re-shoot |
| `04_the_glance_back.png` | §05's 45° glance | re-shoot — still the best single frame this game has |
| `05_the_shop.png` | §08's shared wallet, fourteen priced items | ❌ **delete.** There is no economy |
| `06_five_storeys.png` | the stairwells that made the monster's route 189.6 m | ❌ **delete.** There are eight storeys and no stairwells — the chutes replaced them |
| `07_the_gravel_floor.png` | §12's gravel zone | re-shoot as **B4 저탄장** |
| `08_the_wood_floor.png` | §12's wood corridor | re-shoot as **B2 기록보관소** |
| `09_monster_in_the_archive.png` | §12's wood zone, a different room | re-shoot |
| `10_the_hud.png` | the game's own HUD, live | re-shoot once the HUD shows a race |

### The ten the race needs

Upload in this order — Steam shows the first one largest and a visitor sees about
four. The first three have to carry the three claims the copy leads with, because a
visitor who reads nothing reads these.

| # | Shot | The claim it has to carry |
|:--:|---|---|
| 1 | **The chute mouth from the inner ring** — the one lit thing in a dark corridor, the hole visible at the end of it | §03: *the destination is visible and you cannot reach it.* The single most legible frame available, and the only one that shows the idea nobody else has |
| 2 | **Mid-fall** — three metres of dark between two floors, the ring below resolving | the loop, and that the drop is a fall and not a transition |
| 3 | **The last gate** — the one-cell gap into the centre, framed so the wall on both sides is in shot | §12-A: everybody goes through here |
| 4 | **The creature on an inner ring** at about 12 m, acquisition tell firing | §06 |
| 5 | **The lit outer ring of B1** with the start marks on it | the only bright frame the descent naturally has; also the one that reads as *twenty people go here* |
| 6 | **A door mid-shut**, beam swinging up the corridor behind it | §12-B, and the only thing you can do to another player |
| 7 | **B7 수몰층** — the water floor, because it looks like nothing else in the building | the eight surfaces, in one picture |
| 8 | **B8 굴착층** — the earth floor and the finish | where the race ends |
| 9 | **The 45° glance** during a chase | §05's dilemma |
| 10 | **The HUD, live**, during a descent | what the player actually sees |

Two of those — 1 and 5 — exist to solve B-5. ART.md's bands are **30–75 % legible**
and **10–40 % crushed**, and eight of the ten old frames missed both (21.6–27.4 %
legible, 41.4–46.7 % crushed). That is the open darkness regression arriving on the
store page, and it is **not fixed here**: STEAM-RELEASE.md §2.2 — *"Do not brighten
the game for marketing; frame it instead."* The descent hands the camera two lit
subjects the old building did not have. Use them.

**Re-measure after shooting** and record the numbers in this table:

```bash
cd docs/store/screenshots && python3 ../../../tools/render/frame_stats.py '*.png'
```

⚠ **The capsules are built from these renders** (`store_capsules.py` composites a
title over an in-game frame). Re-shoot first, then re-bake the capsules, then check
the legibility proofs — in that order, or the capsules keep the old building's art at
the right pixel count.

---

## 5 · Defect evidence

`defects/` holds five frames that are **not for upload**. They are the evidence for
what was measured on 2026-08-01, and three of them now document systems the pivot
deleted. Kept, not deleted: a measurement that was true when it was taken stays in
the record.

| File | Shows | Still live? |
|---|---|---|
| `S1_clue_prop_is_a_white_square.png` | §03's clue as an untextured emissive quad | **moot** — the clue system is gone |
| `S2_surface_vehicle_is_a_white_box.png` | §08's shop vehicle as an untextured box | **moot** — there is no surface |
| `S3_objective_prop_is_a_white_capsule.png` | §03's objective as an untextured capsule | **moot** — there is no objective |
| `S4_loot_props_are_white_cubes.png` | §08's loot in the open hall | ⚠ **check.** If the descent map still spawns these, they constrain every framing in §4's list exactly as they did before |
| `S5_sky_visible_from_B3.png` | night sky seen from six metres underground — [ART.md §7.4](../ART.md) predicted it | ⚠ **re-check.** The descent stacks eight storeys in one column instead of spiralling; whether the leak survived that is unknown |
