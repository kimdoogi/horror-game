# Trailer — shot list

The single highest-leverage asset on the page: a visitor plays it before reading
anything. STEAM-RELEASE.md §2.3 has the format requirements (1920×1080 minimum,
H.264 in MP4, AAC stereo, no black bars, gameplay in the first three seconds).
This file is the content.

**No video is edited here, and none can be.** What this file delivers is the
storyboard, what each beat has to prove, and a command per beat that renders the
exact frame at 1920×1080 so the framing is settled before anybody records
anything. §7 covers the two honest routes to actual motion, neither of which
exists in the repository today.

Every reference frame below was rendered by:

```bash
python3 tools/render/store_shots.py shoot --spec tools/render/trailer_frames.json
```

Exit 0, 13 frames, in `docs/store/trailer/`. The spec file carries every camera
position, so changing a framing is an edit to one JSON entry and a re-run — not a
conversation about where the camera was.

---

## 1 · The pitch this trailer has to make

Not "scary co-op game". Four claims, in this order, because each one is the reason
the next one matters:

1. **You cannot fight it.** Establishes the genre contract in three seconds.
2. **You have to talk.** §03's clue cannot come out of the building. This is the
   thing that makes it a *friends* game rather than a horror game with lobbies.
3. **Everything costs the same clock.** §07. This is what makes the decisions
   decisions.
4. **The wallet is shared.** §08. This is where the argument happens, and the
   argument is the content people will clip.

A trailer that shows a monster and four torches has made claim 1 and none of the
others, and there are two hundred of those on Steam.

---

## 2 · The cut, beat by beat

Target 65–75 s. Timings are a first pass; the audio decides the real ones.

| # | t | Beat | What it proves | Reference frame |
|:--:|---|---|---|---|
| 1 | 0:00–0:03 | **Cold open.** A torch sweeps a brick wall. At the edge of the beam, two eye-lights and a mouth, four metres away, already facing us. Cut to black on the first frame of the roar. | Gameplay in the first three seconds, per Valve's own advice and every retention curve. Also states the whole visual language: the beam is the only information. | `beat01_cold_open.png` |
| 2 | 0:03–0:09 | **The surface.** The vehicle, the loading apron, the clock ticking. One voice: *"몇 시야?" / "Nine. Go."* | There is a place to come back to, and a reason to leave it. Establishes the round-trip before the trailer shows anything underground. | `beat02_surface.png` ⚠ |
| 3 | 0:09–0:14 | **The descent.** A stairwell, torchlight down five flights. Footsteps change from concrete to metal on the treads. | §12's vertical scale, and the first audio beat: the floor *sounds* different. | `beat03_descent.png` — a landing, not the whole shaft; the five flights only exist as motion |
| 4 | 0:14–0:22 | **Four floors, four cuts.** Wood, gravel, tile, metal — one second each, each cut carrying only its own footstep. No music under this section. | §04's Listener, without explaining it. A viewer who is wearing headphones works out what this section is for by themselves, which is the best possible way to make the headphone argument. | `beat04..07_floor_*.png` |
| 5 | 0:22–0:30 | **The clue.** The beam holds on a plate. A number, half-worn. A voice reads it out. A second voice repeats it wrong. | §03 — the whole game. This is the beat that sells it, and it is the one beat that is **currently unshootable**: see §4. | ⚠ **blocked** |
| 6 | 0:30–0:42 | **Seen.** Three cuts on the same corridor: at 18 m it is a shape; at 12 m the acquisition tell fires; at 6 m it fills the frame. Then the glance back at 45°, the corridor tilting as the runner turns. | §06's speed ladder and §05's central dilemma, shown rather than stated. The three-cut approach is what makes "0.3 m/s faster than your run" a feeling instead of a number. | `beat08_seen_18m.png`, `beat09_seen_12m.png`, `beat10_seen_6m.png`, `beat11_glance.png` ⚠ |
| 7 | 0:42–0:52 | **The shop.** The price list, the shared wallet at zero, four people talking over each other. Hold on the 소음기 row — *"발소리 감소 / 자기도 못 듣게 됨 → 청음사 무효"* — while somebody argues for it. | §08. An item whose downside is *disabling a teammate's whole role* is a better advertisement for this design than any description of it. | `beat12_shop.png` |
| 8 | 0:52–1:02 | **The carry.** Two hands on the objective, no torch, walking backwards up a corridor while somebody else lights the way. The monster arrives. | §03's carry rule, which is the design's best set piece and the reason the last trip is a two-person escort. **Currently unshootable**: see §4. | ⚠ **blocked** |
| 9 | 1:02–1:08 | **Out, or not.** The apron, one player short. Cut before the outcome resolves. | §02 — somebody has to get out, and it is not always everybody. | ⚠ **blocked** |
| 10 | 1:08–1:15 | **Title.** The name over the long tile corridor, held, with the light at the far end going out. Wishlist card. | — | `beat13_title_plate.png` |

---

## 3 · Sound is the pitch, not the garnish

§05 makes the mix a mechanic, so the trailer's audio is content:

- **Mix for headphones.** Everything about the game assumes them, so a trailer
  mixed for phone speakers advertises the wrong product.
- **Beat 4 has no music.** The four floor materials have to be audible *as
  different materials*. Anything under them defeats the only beat that argues for
  the Listener.
- **Voices early and unpolished.** Four friends talking over each other is the
  product. Clean VO would make it look like a different, more expensive, less
  interesting game.
- **The monster's roar appears twice**: once at 0:03 under the cut to black, once
  at 0:42. Not more. §06's 정지 state — 「침묵이 가장 무서운 소리다」 — is the
  design's own argument for restraint here.
- ⚠ **The gravel/concrete inversion is audible.** [F-002](../BALANCE-FINDINGS.md#f-002):
  gravel measures **26.1 dB quieter** than concrete at the 800 Hz corner the mix
  actually uses (32.4 dB at the finding's original 600 Hz) while the game tells the
  player gravel is the clearer of the two. Beat 4 shoots those surfaces
  dry, in the same room, which is where the alphabet holds — do not cut a
  through-a-wall version of beat 4 until F-002 is resolved.

---

## 4 · Four beats cannot be shot yet, and a fifth has a caveat

Not scheduling. Placeholder art, in the frame, on the objects the beats are about.
Found while shooting the screenshots (see [checklist.md](checklist.md) §1):

| Beat | Blocker |
|---|---|
| 5 · the clue | §03's clue prop renders as a **plain emissive white square**. There is nothing to read, so the beat that carries the design's central mechanic cannot be photographed at all. |
| 8 · the carry | the objective prop is a **plain emissive white capsule**. |
| 2 · the surface | the vehicle is an **untextured white box**. `beat02_surface.png` is rendered and shows it; the frame is usable only if the box is out of shot, which it is not. |
| 9 · out, or not | needs the apron and the vehicle, so it inherits beat 2's blocker. |
| 6 · the glance | shootable, but `beat11_glance.png` carries a §08 loot prop — a white cube — in the lower left. The screenshot version of this beat uses one of the six framings in the whole building that avoid one (checklist.md S-4); the trailer's moving version cannot dodge them, because the runner passes them. |

Beats 2, 5, 8 and 9 are all the same defect: the interactable props are
primitives, while the map kit, the dressing and the monster are finished. The
trailer's four strongest beats — 1, 3, 6 and 7 — are shootable today.

---

## 5 · Rendering the reference frames

```bash
# every beat, 1920×1080, into docs/store/trailer/
python3 tools/render/store_shots.py shoot --spec tools/render/trailer_frames.json

# one beat, while iterating: trim the "shots" array in the spec and re-run
python3 tools/render/store_shots.py shoot --spec /tmp/one_beat.json --log /tmp/beat.log

# re-derive camera positions after the map is regenerated
python3 tools/render/store_shots.py probe --seed 1204 --out /tmp/store_probe.json
```

Three things that will otherwise waste a night:

- **Never add `-nographics`.** The driver deliberately does not pass it. That flag
  disables the graphics device and every frame comes out black.
- **Check the exit code before the frame count.** A run that died on the project
  lock writes a log with no errors in it. The driver waits the lock out and
  retries rather than reporting a green run that never happened — it waited twice
  during the pass that produced these frames.
- **The camera positions are tied to the map seed.** These are seed 1204 with
  dressing seed 4703. Regenerate the map and every coordinate in
  `trailer_frames.json` points somewhere else; re-run `probe` first.

---

## 6 · Capturing motion — the two honest routes

Neither exists in this repository. Both are a real evening's work, and the choice
matters:

**A · OBS over a real play session.** Record a human playing at 1920×1080, 60 fps,
and cut from the take. This is the only route that captures §05's mouse-look — the
45° glance in beat 6 *is* a hand on a mouse, and a camera path authored in an
editor does not look like one. It also captures the voices, which are half the
pitch. Costs: someone has to play well, and you need at least two people for the
beats with talking in them.

**B · Unity Recorder in the editor.** `com.unity.recorder`, added to
`Packages/manifest.json`, records the Game view to an image sequence or MP4 with a
`RecorderWindow` timeline. Deterministic, repeatable, and shoots at whatever frame
rate you like regardless of what the machine can render live. Costs: it is
editor-side, so it captures the editor's frame — ART.md §6's numbers are editor
renderer timings with no physics, AI or UI, and a recorded take inherits that
caveat. It also cannot produce beats 2, 5, 8 or 9 any more than the still rig can.

**Recommendation: A, once four-player works.** The trailer's whole argument is
that this is a game about four people talking, and route B cannot record a
conversation. Until then, route B produces an internal animatic from the four
shootable beats — useful for timing the cut, not for publishing.

---

## 7 · What not to do

- **No text overlays claiming things.** No "4 PLAYER CO-OP" card, no feature
  bullets burned into the video. The store page says those; the trailer shows
  them.
- **No review quotes, no laurels, no scores.** There are none, and Valve rejects
  capsules carrying them — a trailer that carries them is a page that reads as
  desperate.
- **Do not brighten the footage.** STEAM-RELEASE.md §2.2 is explicit and it is
  right: a buyer who bought a brighter game will say so in a review. Frame for the
  beam instead. Every frame in `docs/store/trailer/` is at the game's own
  exposure, and four of them are measurably below ART.md's own legibility floor —
  which is [an open art regression](../ART.md), not a photography choice.
- **Do not show a four-player lobby.** It does not exist yet. The moment it does,
  it becomes beat 2 and the trailer gets much better.
