# Trailer — shot list

> **Rewritten 2026-08-04, and this time there is a file.** The previous version of this
> document was a storyboard with the standing note *"no video is edited here, and none
> can be."* That is no longer true: `tools/render/descent_film.py` shoots the cut below
> in-engine and encodes it, and `docs/store/trailer_descent.mp4` is its output. What
> changed in between is that the shot book stopped describing a game that had been
> deleted.

The single highest-leverage asset on the page: a visitor plays it before reading
anything. [STEAM-RELEASE.md §2.3](../STEAM-RELEASE.md) has the format requirements.

---

## 0 · The two files, measured

Both numbers below are `ffprobe` output, not intentions.

| | `party.mp4` (old) | `trailer_descent.mp4` (new) | Valve wants |
|---|---|---|---|
| resolution | 1280 × 720 | **1920 × 1080** | 1920 × 1080 |
| frame rate | 24 | **30** | 30/29.97 or 60/59.94 |
| bit rate | 1,625 Kbps | **11,960 Kbps** | 5,000+ Kbps |
| duration | 3.00 s | **41.57 s** (1247 frames) | 30–60 s |
| subject | a four-player co-op looting party | the race | — |

Re-measured 2026-08-12 with `ffprobe`; the new cut clears every row. `descent_film.py`
asks for 12,000 Kbps (`--bitrate`, the default) and the encode landed at 11,960.

`party.mp4` misses the resolution, the frame rate, the bit rate and the length, and is
named after a party of four that this design deleted on 2026-08-02. **It is not the
trailer and it never was.** It is left in place only because this pass does not delete
files; move it out of `docs/store/` before anybody mistakes it for an asset.

> **Why the new encode targets a bit rate instead of a quality.** `party_film.py` asked
> for `-crf 18` and got 1.62 Mbps. That is not an ffmpeg bug: this game is a dark
> corridor lit by one torch, and a quality-targeted encoder spends almost nothing on it.
> *Any* constant-quality setting fails the same way on the same footage however high the
> quality is set. The bit rate is therefore a target (`-b:v` + `-maxrate` + `-bufsize`),
> and `descent_film.py` reads the result back with `ffprobe` and exits non-zero if it
> missed — the check is in `check_against_valve`.

**The thirteen reference frames in `docs/store/trailer/` are stale and are not inputs to
this cut.** They are seed 1204 on the five-storey co-op building, and two of them
photograph systems the pivot deleted (`beat02_surface`, `beat12_shop`). The spec that
produced them, `tools/render/trailer_frames.json`, holds coordinates in a map that is no
longer generated — that is [checklist.md B-3](checklist.md). The new spec,
`tools/render/trailer_shots.json`, **contains no world coordinates at all**: every camera
is an anchor keyword the rig resolves against the scene it just opened, so a re-seeded
storey moves the framing with it instead of stranding it inside a wall.

---

## 1 · The pitch this trailer has to make

Not "scary multiplayer game". Four claims, in this order, because each is the reason the
next one matters:

1. **You are going down, and you are not alone.** Eight floors, one direction.
2. **Winning a floor puts you back at the start.** The 투하구 drops you on the *rim* of
   the floor below, and a floor is only ever entered from its rim. This is the idea
   nobody else has. If the trailer lands one thing, it is this.
3. **The last gate is one cell wide.** Which is what turns a maze into a race — everyone
   arrives at the same doorway, and §12-A refuses to widen it when the lobby grows.
4. **Something in here takes everything you have gained, and does not end your run.**

**Claim 4 changed on 2026-08-04 (`e0fa042`) and the store copy took until 2026-08-12 to
catch up.** Being caught is not death: `MatchDirector.cs:1666` — 「§02: caught is not
death. The creature sends a runner back to the place they started on B1 and they keep
racing.」 §09's spectator ghost was deleted in the same change, because nothing is
permanent any more and there is nobody to spectate. The previous version of this file had
a beat built on **탈락 · 순위 없음 / OUT · NO PLACING** and on filming the ghost. Both
were cut here, and [§7](#7--the-copy-no-longer-sells-elimination) records the day the
copy followed.

---

## 2 · The cut, beat by beat

**This table is the prose of `tools/render/trailer_shots.json` and nothing else.** Every
row is a shot that file names, in that file's order, at that file's duration. A row here
that is not in that file is a shot nobody can film, which is the same failure as a test
nobody ran — so when the cut changes, both change or neither does.

23 shots · 1247 frames · 30 fps · **41.57 s**.

| # | shot | t | Beat | What it proves |
|:--:|---|---|---|---|
| 1 | `01_mouth` | 2.20 | Stood in the middle of B4, looking down at the 투하구 at your feet. | Opens on the mechanic, not on a corridor. |
| 2 | `02_fall` | 0.50 | The drop. Half a second, eased t², onto a floor you have not seen. | Claim 2, before anybody has read a word. |
| 3 | `03_landed` | 2.60 | You are on B5's **rim** — the far outside of the new floor. | The twist in claim 2: winning a floor puts you at the back of the next one. |
| 4 | `04_start_line` | 3.60 | B1's lit outer ring. **Two** bodies, facing inward, idle. | People, in the light, before the dark. |
| 5 | `05_inward_rim` | 1.60 | The outer band, heading in. | The narrowing, 1 of 3. |
| 6 | `06_inward_gate` | 1.60 | The middle gate on B5 and the door it carries. | 2 of 3, and the only thing you can do to another runner. |
| 7 | `07_inward_centre` | 1.90 | Past the last gate. Tighter lens, flat pitch. | 3 of 3: everybody arrives here. |
| 8–15 | `08_surface_b1` … `15_surface_b8` | 1.15 ea | **Eight floors, eight surfaces.** Identical relative framing on every storey; the only variable is the floor. **No music under this section.** | The audio alphabet, demonstrated rather than captioned. |
| 16 | `16_gun` | 2.60 | A gun on the floor of a dead end. `Gun_B4`, a real prefab instance on a 막힌 길. | The other runner is a threat, and the game hands you the reason. |
| 17 | `17_seen_18m` | 1.40 | At 16 m it is a shape. | §06's speed ladder as three distances, not a number. |
| 18 | `18_seen_12m` | 1.40 | The acquisition tell fires. | |
| 19 | `19_seen_6m` | 1.90 | It fills the frame; the camera gives ground. | |
| 20 | `20_back_to_b1` | 2.80 | **Hard cut out of the creature onto B1's lit rim.** The cell you started in, eight floors up, still running. | Claim 4. The juxtaposition *is* the rule — no card, no caption. |
| 21 | `21_door` | 2.20 | One door per floor. 1.1 s to shut, 4.5 s to break, never comes back. | |
| 22 | `22_finish` | 2.80 | The middle of B8 — §12-C: 「B8의 중심은 투하구가 아니라 도착점」. | The one place in the building with no hole in it. |
| 23 | `23_ring_empty` | 3.40 | B1's outer ring with nobody on it. | Title plate goes here, in the edit — not baked into a frame. |

### The two beats that were cut, and why

- **"Twenty runners on the ring."** Not filmed, and deliberately. Twenty *instances* have
  connected on this desk — `LocalTwoInstance` records the run in its own doc comment
  (2.4 GB resident, load average 29) — but **twenty people never have, and neither have
  two.** §8's rule stands: staging twenty stand-ins to photograph the one claim on this
  page that no human has ever demonstrated is a lie, not a shortcut. Shot 4 shows two and
  the cut never says a number.
- **"Being sent back to B1", as the curtain.** `CaughtScreen` is a UI curtain driven by
  `MatchDirector`, and this rig is edit-mode — no play mode, no UI, no director. Shot 20
  gets the *rule* across by cutting from the creature to the lit rim, which is what the
  player sees anyway. Filming the curtain itself needs [route A](#5--what-still-needs-a-human).

---

## 3 · What the rig can and cannot do, stated plainly

`DescentFilmRig` runs in **edit mode**. The geometry, the lighting, the materials, the
models and the animation clips in every frame are the shipped ones. The *movement through
them* is authored in the shot book, and there is no `MatchDirector`, no physics, no AI
and no networking.

So: **the rig produces a second body, never a second player.** Shot 4's two runners are
the shipped `Runner.fbx` on the shipped `Idle` clip standing on two real
`PlayerSpawn_` marks — an accurate picture of two runners on the start line, and not a
recording of two people playing. The distinction matters for beats built on *behaviour*
(a scuffle at a gate, a door shut in somebody's face, two voices in a corridor). Those
are route A.

> **The bind-pose defect, recorded because it nearly shipped.** Until 2026-08-04 the rig
> read its clips from `Assets/Models/Characters/Player.fbx` — a path that does not exist;
> the runner is at `Assets/Models/Player/Runner.fbx`. `AssetDatabase` returns an empty set
> for a missing path rather than an error, so every body failed its clip lookup and stood
> in its **bind pose**. The 14:13 take put two T-posed figures on B1's start line, in the
> shot the cut exists to make. Frame count right, exit code zero, log clean. Only looking
> at the PNGs found it. It is now checked before the first frame is rendered
> (`RequireRunnerClips`) and a missing clip is fatal.

Two more failures that used to be warnings and are now fatal, for the same reason:

- **A camera inside geometry.** The 14:14 take spent 72 frames on a gun shot the rig had
  already called `INSIDE GEOMETRY … This shot will be a wall`. Framing a dead end needed
  `standOff`, which backs the camera along the corridor that actually leaves the cell
  rather than along a world axis nobody can predict after a re-seed.
- **Under 1.5 m of room ahead**, which reads as a wall at the default FOV.

---

## 4 · Two real instances — the honest answer

`LocalTwoInstance.Launch` does run a host and a client that connect, start and race by
themselves, and `LaunchFullField` runs twenty. **It is a measurement harness, not a
camera.** It reports frame time and bandwidth; there is no capture path in it, and
nothing in this repository records a player window.

What it would take, precisely:

1. The instances already accept `-screen-width 1920 -screen-height 1080`
   (`Assets/Scripts/Editor/SceneGen/LocalTwoInstance.cs:593`), so the window can be the
   right size. Clients default to `-batchmode -nographics` (line 583), which renders
   nothing — the ones being filmed have to be launched windowed.
2. Capture is then **desktop recording** — `ffmpeg -f avfoundation -i "3"` (`Capture
   screen 0` is present on this machine). That records the whole screen, so it needs a
   human to place the windows, and it needs macOS Screen Recording permission granted to
   whatever process runs it. Neither is something an unattended agent should do: it
   records the operator's desktop, not just the game.
3. It also has to be scheduled. A full field was running on this machine while this cut
   was being planned (20 processes, 19:33–19:44), and a GPU-heavy render alongside it
   would have corrupted the frame-time numbers that run existed to measure.

**So: not filmed here, and not because it was hard to think of.** It is the single
biggest upgrade available to this trailer — shots 4, 6 and 21 all get materially better
with two people actually playing — and it is route A below.

---

## 5 · What still needs a human

| Needs a person at a keyboard | Why the rig cannot |
|---|---|
| **§05's mouse-look** — the 45° glance back while running | An editor-authored camera path does not move like a hand on a mouse, and the glance is the game's central dilemma |
| **Proximity voice** — two strangers negotiating in a corridor | Cannot be faked, and it is the beat people will clip |
| **A door shut in somebody's face** | Two players and a decision; the rig can only place a door and a camera |
| **The 잡힘 curtain** and the HUD | Play-mode UI; the rig is edit-mode |
| **Anything with twenty people in it** | Twenty people |

**Route A (OBS or `ffmpeg` over a real session) is what closes all five, and it is now
the only thing standing between this cut and a good one.** The route-B rig has gone as
far as route B goes.

---

## 6 · Shooting it

```bash
# whole cut: Unity renders 1247 PNGs, ffmpeg encodes and the result is verified
python3 tools/render/descent_film.py --out docs/store/trailer_descent.mp4

# re-cut without re-rendering (fades, bit rate, ordering)
python3 tools/render/descent_film.py --encode-only
```

Four things that otherwise waste a night, all of them learned the expensive way:

- **Never add `-nographics`.** The driver deliberately does not pass it. That flag
  disables the graphics device and every frame comes out black.
- **Check the exit code before the frame count.** A run that died on the project lock
  writes a log with no errors in it.
- **Do not run Unity while another agent holds the project lock**, and do not run it
  while a field of instances is being measured. The driver waits on the lockfile; it
  cannot see the field.
- **Judge the frames at native brightness.** Do not open them in a viewer that
  auto-gains, and do not grade the cut to make them readable — [§8](#8--what-not-to-do).

---

## 7 · The copy no longer sells elimination

Raised here on 2026-08-04, fixed in the copy on **2026-08-12**. Recorded rather than
deleted, because it is the clearest example this folder has of a document outliving the
rule it described.

The shipped rule (`Core/Race/RaceState.cs` `ReportCaught`, `MatchDirector.cs:1666`,
`UI/Screens/CaughtScreen.cs`): caught → back to your own starting cell on B1 → keep
racing, with `TimesCaught` incremented and nothing else taken. Nothing in the game
eliminates a player; `RacerStatus.Eliminated` now means only *a seat that emptied*, and
`GhostSession` is not in the repository.

What was on the page, and what replaced it:

| File | Said | Now says |
|---|---|---|
| `copy-ko.md` §2 | 「잡히면 순위 없이 **탈락**한다」 | 「잡히면 지하 1층부터 다시다」 |
| `copy-ko.md` §3 | 「잡히면 **탈락**입니다 … 잘 죽는 방법은 없습니다」 | 「잡히면 지하 1층 출발선으로 돌아갑니다 … 끝내 주지 않는 것이 이 괴물의 벌입니다」 |
| `copy-ko.md` §3 | 경주 규칙에 「**탈락**」을 열거 | 「출발 · 완주 순위 · **잡힘** · 시간 초과」 |
| `copy-en.md` §3 | "start, finish order, **elimination**, timeout" | "start, finish order, **being caught**, timeout" |
| `copy-en.md` §10 | "Caught is out, unranked, no revival" citing `RaceDirector.cs:508` | the `ReportCaught` row above — that citation named a line that does not contain it, and the PlayMode log it quoted (`§02 0번 탈락 — B8, 11초`) is not in the code either |

The Korean line that had to go was the good one: 「잘 죽는 방법은 없습니다」 was the
emotional centre of the long description and it was about a rule that no longer exists.
Its replacement is about losing eight floors of progress and running anyway, which is a
different and — the copy now argues — better pitch.

**The gun does not appear in any store document.** Four of them ship, one each on B3–B6
(`Gun_B3`…`Gun_B6`, verified in the scene). It is in this cut at shot 16 and in no piece
of copy anywhere.

---

## 8 · What not to do

- **Do not stage a crowd.** Twenty AI stand-ins in the outer ring would make the "twenty
  runners" beat shootable tomorrow and would be a lie about the only claim on this page
  that has never once been demonstrated. If that beat is in the trailer, it is because
  twenty people were in the building.
- **No text overlays claiming things.** No "20 PLAYERS" card, no feature bullets burned
  into the video. The store page says those; the trailer shows them.
- **No review quotes, no laurels, no scores.** There are none, and Valve rejects capsules
  carrying them.
- **Do not brighten the footage.** [STEAM-RELEASE.md §2.2](../STEAM-RELEASE.md) is
  explicit and right: a buyer who bought a brighter game says so in a review. Frame for
  the beam instead. The darkness regression in [checklist.md B-5](checklist.md) is an art
  problem to fix in the game, not in the grade.
- **Do not let this file and `trailer_shots.json` drift.** §2 is the JSON in prose. A beat
  that exists only here is a shot nobody can film.

---

## 9 · Sound

§05 makes the mix a mechanic, so the trailer's audio is content, not garnish. **None of
it is in the encode** — `descent_film.py` produces a silent H.264 track and the mix is an
edit-suite job.

- **Mix for headphones.** Everything about the game assumes them.
- **Shots 8–15 have no music.** The eight surfaces have to be audible *as different
  surfaces*; anything under them defeats the only beat that argues for the mix.
- **Shots 1–3 have no music either.** The fall should be wind and then a floor.
- **The creature is heard twice at most** — once under shot 18's acquisition tell, once at
  shot 19. §06's 정지 state, 「침묵이 가장 무서운 소리다」, is the design's own argument for
  restraint.
- ⚠ **The gravel/concrete inversion is still audible, and it is half what it was.**
  [F-002](../BALANCE-FINDINGS.md#f-002) was halved on 2026-08-09 by the CC0 footstep pass.
  Re-run here on 2026-08-12 (`tools/ci/verify_audio.sh`): gravel measures **17.8 dB
  quieter** than concrete through the verifier's 600 Hz low-pass — down from 32.5 dB —
  while the game still tells the player gravel is the clearer of the two. The gate
  accepts it as a known baseline defect, so the audit passes and **the inversion is still
  there**. Shots 8–15 put both surfaces in a section whose entire purpose is that surfaces
  sound different. **Resolve F-002 before mixing that section.** The cut's order is fixed
  by storey (B1 concrete → B8), so the two cannot be separated by re-ordering — B1
  concrete and B4 gravel are three cuts apart.
