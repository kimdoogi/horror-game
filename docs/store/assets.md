# Store assets — every size, and where the number came from

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
  no overlaid marketing text. The game's own HUD and §08's shop screen are
  gameplay and are fine; §14's developer guidance overlays are not, and the render
  rig switches them off for exactly that reason.
- **Keep important art out of the outer 10 %.** Steam crops.

---

## 3 · How every file here was made

```bash
# 1 · find camera positions in the current map (writes nothing to the project)
python3 tools/render/store_shots.py probe --seed 1204 --out /tmp/store_probe.json

# 2 · render the screenshots at 1920×1080, in game, no -nographics
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

## 4 · The screenshots, and what each one is for

> **These frames are dated 2026-08-01 02:0x, and the art pipeline was being
> changed while they were taken.** A concurrent pass has untracked work in
> `Assets/Scripts/Editor/Rendering/` (contact decals, detail maps, practical glow)
> and in `Assets/Textures/`, and it held the Unity project lock twice during this
> pass. Every luminance figure below is a measurement of the build at that
> moment. Re-shoot before uploading — it is one command (§3), and the numbers
> below are the thing to re-measure.


Upload in this order — Steam shows the first one largest and a visitor sees about
four.

| # | File | Shows | Measured legible % / crushed % |
|:--:|---|---|---|
| 1 | `04_the_glance_back.png` | §05's 45° glance: the beam on brick, the creature four metres away at the edge of vision | 27.4 / 45.5 |
| 2 | `02_the_monster_at_distance.png` | §06 — it is down the corridor and it has seen you | 24.9 / 44.2 |
| 3 | `05_the_shop.png` | §08's shared wallet: fourteen items, every one with its cost written next to it | **50.7 / 19.0** |
| 4 | `03_it_is_closer_now.png` | the same corridor, nine metres | 24.9 / 44.2 |
| 5 | `01_corridor_and_beam.png` | §03's beam as the only information, down 18 m of B5 tile | 25.4 / 42.0 |
| 6 | `06_five_storeys.png` | the stairwells that make the monster's route 189.6 m | **33.0 / 36.8** |
| 7 | `09_monster_in_the_archive.png` | §12's wood zone — a different floor, a different room | 21.6 / 46.7 |
| 8 | `10_the_hud.png` | the game's own HUD, live | 24.7 / 44.2 |
| 9 | `07_the_gravel_floor.png` | §12's gravel zone | 25.3 / 41.4 |
| 10 | `08_the_wood_floor.png` | §12's wood corridor | 22.9 / 44.8 |

ART.md's bands are **30–75 % legible** and **10–40 % crushed**. Eight of the ten
miss both, and that is the open darkness regression from the map growth arriving
on the store page, not a photography choice — see
[checklist.md](checklist.md) S-6. **Do not fix it here.**

---

## 5 · Defect evidence

`defects/` holds five frames that are **not for upload**. They are the evidence
for [checklist.md §1](checklist.md), because a claim in this project carries the
thing that demonstrates it:

| File | Shows |
|---|---|
| `S1_clue_prop_is_a_white_square.png` | §03's clue, the design's central mechanic, as an untextured emissive quad |
| `S2_surface_vehicle_is_a_white_box.png` | §08's shop vehicle as an untextured box |
| `S3_objective_prop_is_a_white_capsule.png` | §03's objective as an untextured capsule |
| `S4_loot_props_are_white_cubes.png` | §08's loot in the open hall |
| `S5_sky_visible_from_B3.png` | night sky with a horizon gradient, seen from six metres underground — [ART.md §7.4](../ART.md) predicted this and this is the first frame that photographs it |
