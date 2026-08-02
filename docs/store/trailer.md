# Trailer — shot list

> **Rewritten 2026-08-03 for the race.** The previous list was ten beats about a
> four-player co-op looting game: the surface van, the shop, the clue plate, the
> carry. All four of those things were deleted on 2026-08-02
> ([DESCENT-PIVOT.md](../DESCENT-PIVOT.md)), and with them three of that list's
> blockers. This is a shot list for the game that exists.

The single highest-leverage asset on the page: a visitor plays it before reading
anything. [STEAM-RELEASE.md §2.3](../STEAM-RELEASE.md) has the format requirements
and they are the thing the current file fails — see §0.

**No video is edited here, and none can be.** What this file delivers is the
storyboard, what each beat has to prove, which beats can be shot today, and the
route to actual motion.

---

## 0 · The file in this folder is not a trailer

`docs/store/party.mp4`, measured with `ffprobe` (STEAM-RELEASE.md §I.3.3):

```
codec_name=h264   width=1280   height=720   r_frame_rate=24/1
duration=3.000000  size=609331  bit_rate=1624882
```

Valve asks for **up to 1920 × 1080, 30/29.97 or 60/59.94 fps, 5,000+ Kbps**,
H.264/AAC. That file misses the resolution, the frame rate and the bit rate, is
three seconds long, and is named after a party of four that no longer exists in this
design. **It has to be reshot from scratch regardless**, which is the one good thing
about the pivot landing on it: nothing was thrown away that was going to be used.

> It is not deleted here on purpose — this pass does not remove files. Delete it, or
> move it out of `docs/store/`, before anybody mistakes it for an asset.

**The thirteen reference frames in `docs/store/trailer/` are stale for the same
reason.** They are seed 1204 on the five-storey co-op building, and four of them
photograph objects the pivot deleted (`beat02_surface`, `beat12_shop`). The camera
spec that produced them, `tools/render/trailer_frames.json`, holds coordinates in a
map that is no longer generated. **Re-probe before re-shooting** — §5.

---

## 1 · The pitch this trailer has to make

Not "scary multiplayer game". Four claims, in this order, because each one is the
reason the next one matters:

1. **You are going down, and you are not alone.** Twenty people, one building, one
   direction. Three seconds.
2. **Winning a floor puts you back at the start.** The chute is the idea nobody else
   has. If the trailer lands one thing, it is this.
3. **The last gate is one cell wide.** Which is what turns a maze into a race —
   everyone arrives at the same doorway.
4. **Something in here ends your run, and it does not care who you are.** The stake,
   stated late and briefly.

A trailer that shows a dark corridor and a monster has made claim 4 and none of the
others, and there are two hundred of those on Steam. **Claim 2 is the trailer.**

---

## 2 · The cut, beat by beat

Target 60–70 s. Timings are a first pass; the audio decides the real ones.

| # | t | Beat | What it proves | Shootable today |
|:--:|---|---|---|---|
| 1 | 0:00–0:04 | **Cold open: the drop.** First person, standing in the lit middle of a floor, the mouth of a 투하구 at your feet. A step forward, three metres of fall in the dark, and a corridor you have never seen resolving as you land — on the *outside* of the next floor. No cut. | The whole loop in one unbroken shot, gameplay in the first three seconds, and the game's only genuinely new idea before anybody has read a word. | ✅ `Chute.DropHeightMetres = 3.0f` and the controller's own gravity — the fall is real, not a transition |
| 2 | 0:04–0:09 | **The start line.** The outer ring of B1, lit, twenty identical figures spread around it facing inward. They all move at the same instant. Hold two seconds longer than is comfortable. | Twenty, and that they are identical — §04 deleted the roles, and a viewer should understand there is no character select. | ❌ **needs twenty connected peers.** Today the number is zero |
| 3 | 0:09–0:17 | **Four, two, one.** Three cuts down one storey. Four doorways at the rim with people spilling through them; two, with a scuffle at one; one, with a queue. Each cut is tighter than the last and has more bodies in it. | §12-A's whole design: the gates do not multiply when the lobby does. | ⚠ geometry ✅, crowd ❌ — the doorways are shootable now, the people are not |
| 4 | 0:17–0:24 | **The door.** A runner stops at the middle gate. 1.1 seconds of standing still facing the wrong way while a beam swings up the corridor behind. It shuts. Hard cut: two runners on the far side, 4.5 seconds of breaking, and it does not come back. | The only thing you can do to another player, and its price, without a word of explanation. One door per floor — this is the floor's whole social event. | ⚠ door mechanics ✅ (`DoorState`, `DoorInteractable`), second runner ❌ |
| 5 | 0:24–0:32 | **Eight floors, eight surfaces.** One second each, deepest last, each cut carrying only its own footstep and nothing else: concrete 하역장, wood 기록보관소, metal 기계실, gravel 저탄장, tile 저수조, carpet 병동, water 수몰층, earth 굴착층. **No music under this section.** | The audio alphabet, and the headphone argument made by demonstration rather than by a caption. A viewer wearing headphones works out what this section is for by themselves. | ✅ all eight exist: `DescentMap.Storey`, `Assets/Audio/Footsteps/` |
| 6 | 0:32–0:39 | **Voices.** Two runners who have never met, in the same corridor, talking — *"그쪽 막혔어?"* Then the same two, two gates later, at the one-cell gate, not talking. | Proximity voice, and what it is worth: cooperation is real and it is temporary. This is the beat people will clip. | ❌ **needs two peers and working voice.** Neither has ever happened |
| 7 | 0:39–0:50 | **Seen.** Three cuts on the inner ring: at 18 m it is a shape; at 12 m the acquisition tell fires; at 6 m it fills the frame. Then the 45° glance, the corridor tilting, and the stamina bar emptying while the gap does not open. | §06's speed ladder — 4.5 against 4.8 — and §05's central dilemma, shown rather than stated. The three-cut approach is what makes "three tenths of a metre per second" a feeling instead of a number. | ✅ shot today, and the strongest sequence in the list |
| 8 | 0:50–0:55 | **Out.** The catch. Then the same building seen through its walls, from above, silent — §09's ghost. One line: **탈락 · 순위 없음 / OUT · NO PLACING.** | The stake, and the sentence that makes it different from every other horror game: there is no good order to die in. | ✅ `GhostSession` is built and has been rendered |
| 9 | 0:55–1:02 | **B8.** The earth floor. Two runners in the same inner corridor and one doorway. Cut on the doorway, before it resolves. | §02: one winner, and the trailer refuses to say which. | ⚠ geometry ✅, second runner ❌ |
| 10 | 1:02–1:10 | **Title.** The name over the empty lit outer ring of B1 — twenty start marks, nobody standing on them. Hold. Wishlist card. | — | ✅ |

**Four of the ten need other people in the frame** (2, 6, and half of 3, 4 and 9),
and that is the honest state of this game: its trailer is blocked on the same thing
its release is blocked on. See §4.

---

## 3 · Sound is the pitch, not the garnish

§05 makes the mix a mechanic, so the trailer's audio is content:

- **Mix for headphones.** Everything about the game assumes them. A trailer mixed for
  phone speakers advertises the wrong product.
- **Beat 5 has no music.** The eight surfaces have to be audible *as different
  surfaces*. Anything under them defeats the only beat that argues for the mix.
- **Beat 1 has no music either.** The fall should be wind and then a floor.
- **Voices early and unpolished** (beat 6). Two strangers negotiating in a corridor is
  the product. Clean VO would make it look like a different, more expensive, less
  interesting game.
- **The creature is heard twice at most** — once under beat 7's acquisition tell, once
  at beat 8. §06's 정지 state, 「침묵이 가장 무서운 소리다」, is the design's own
  argument for restraint.
- ⚠ **The gravel/concrete inversion is audible.** [F-002](../BALANCE-FINDINGS.md#f-002):
  gravel measures **26.1 dB quieter** than concrete at the 800 Hz corner the mix uses,
  while the game tells the player gravel is the clearer of the two. Beat 5 puts those
  two surfaces four seconds apart in a section whose entire purpose is that surfaces
  sound different. **Resolve F-002 before cutting beat 5**, or order the eight so
  gravel and concrete are not adjacent — they currently are not (B1 concrete, B4
  gravel), which is luck rather than design.

---

## 4 · What blocks this trailer, and what does not

The pivot cleared three of the old list's four blockers and left one new one that is
larger than all of them.

| Old blocker | State |
|---|---|
| §03's clue prop is a white square | **gone** — the clue system was deleted with the co-op game |
| §08's vehicle is a white box | **gone** — there is no surface and no round trip |
| §03's objective prop is a white capsule | **gone** — there is no objective to carry |
| §08's loot props are white cubes | ⚠ **check.** The loot economy was deleted from the design; whether the props are still being spawned into the descent map is a question for whoever regenerates it. If they are, they constrain every framing in the building exactly as [checklist.md S-4](checklist.md) recorded |

| New blocker | State |
|---|---|
| **Other players in the frame** | Beats 2, 6, and half of 3, 4 and 9. `RaceLobby` is referenced by nothing, `RaceDirector` is in no scene, and no two peers have ever connected ([STEAM-RELEASE.md §I.4](../STEAM-RELEASE.md)). This is not an art problem and cannot be worked around with a render rig |

**The four beats that are shootable today — 1, 5, 7, 8 — are the four strongest in the
list.** A 30-second cut of exactly those four is a real teaser and it can be made this
week. That is worth saying plainly, because the instinct will be to wait for beat 2.

---

## 5 · Rendering the reference frames

The spec is stale (§0) and has to be re-derived before any of this is shot:

```bash
# 1. the map moved: re-derive camera anchors on the descent seed
python3 tools/render/store_shots.py probe --seed 20260802 --out /tmp/descent_probe.json

# 2. rewrite tools/render/trailer_frames.json against those anchors — one JSON
#    entry per beat above, so changing a framing is an edit and a re-run, not a
#    conversation about where the camera was

# 3. shoot
python3 tools/render/store_shots.py shoot --spec tools/render/trailer_frames.json
```

`DescentMap.DefaultSeed` is **20260802** and every storey is centred on cell (12, 12)
with a radius of 11 — the tower does not move between floors, so a camera anchor is a
storey index plus an offset from the middle, which is a far simpler spec than the old
building needed.

Three things that will otherwise waste a night:

- **Never add `-nographics`.** The driver deliberately does not pass it. That flag
  disables the graphics device and every frame comes out black.
- **Check the exit code before the frame count.** A run that died on the project lock
  writes a log with no errors in it.
- **Do not run Unity while another agent holds the project lock.** Parallel runs
  corrupt each other's measurements; the driver waits and retries.

---

## 6 · Capturing motion — the two honest routes

Neither exists in this repository. Both are a real evening's work.

**A · OBS over a real play session.** Record a human playing at 1920 × 1080, 60 fps,
and cut from the take. The only route that captures §05's mouse-look — the 45° glance
in beat 7 *is* a hand on a mouse, and an editor-authored camera path does not look
like one. It also captures beat 6's voices, which cannot be faked. Costs: somebody has
to play well, and beats 2, 3, 4, 6 and 9 need more than one person.

**B · Unity Recorder in the editor.** `com.unity.recorder`, added to
`Packages/manifest.json`, records the Game view to an image sequence or MP4.
Deterministic, repeatable, any frame rate. Costs: it captures the editor's frame —
ART.md §6's numbers are editor renderer timings with no physics, AI or UI, and a
recorded take inherits that caveat. It cannot produce a second player any more than
the still rig can.

**Recommendation: B now, A later.** This is a change from the previous version of this
file, which recommended waiting. Route B can shoot beats 1, 5, 7 and 8 today — the
fall, the eight surfaces, the chase, the elimination — and those four are a publishable
30-second teaser, not merely an internal animatic. Route A becomes right the moment
two peers connect, and the trailer roughly doubles in value at that moment.

---

## 7 · What not to do

- **No text overlays claiming things.** No "20 PLAYERS" card, no feature bullets burned
  into the video. The store page says those; the trailer shows them. The one exception
  is beat 8's 탈락 · 순위 없음, which is the game's own UI, not a marketing card.
- **No review quotes, no laurels, no scores.** There are none, and Valve rejects
  capsules carrying them.
- **Do not brighten the footage.** [STEAM-RELEASE.md §2.2](../STEAM-RELEASE.md) is
  explicit and right: a buyer who bought a brighter game says so in a review. Frame for
  the beam instead. The darkness regression recorded in
  [checklist.md S-6](checklist.md) is an art problem to fix in the game, not in the
  grade.
- **Do not stage a crowd.** Twenty AI stand-ins spawned in the outer ring would make
  beat 2 shootable tomorrow and would be a lie about the only claim on this page that
  has never once been demonstrated. If beat 2 is in the trailer, it is because twenty
  people were in the building.
