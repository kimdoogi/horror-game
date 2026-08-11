# Store copy — English

> **Rewritten 2026-08-03.** The previous version (2026-08-01) sold a four-player
> asymmetric co-op looting game. That game was deleted on 2026-08-02
> ([DESCENT-PIVOT.md](../DESCENT-PIVOT.md)). Roles, the shop, the clue system, the
> shared wallet and every "four" are gone from this file.

Paste-ready. The blocks below are the exact strings for the Steamworks store-page
editor; everything outside a fenced block is a note to whoever is pasting.

[copy-ko.md](copy-ko.md) is the **original** — the design document is Korean and the
sentences are written there first. This file follows it.

Every claim traces to a section of [`docs/game-design.md`](../game-design.md) v1.1 or
to a file on disk, and the evidence for all of them is in one table at §10.
**Nothing marked ⚠ goes on the page until it is true or the sentence is cut.**

> **Audited 2026-08-12 — elimination is gone from this file.** On 2026-08-04
> (`e0fa042`) §06 stopped removing a caught runner from the race and started
> **sending them back to their starting cell on B1**. `RaceState.ReportCaught` puts
> the status back to `Running`, the storey back to 0 and adds one to `TimesCaught`;
> `GhostSession` is deleted from the repository. The design document still says
> 탈락 · 유령 in §02, §06, §09 and §11 — **the document is the stale one**, and this
> copy follows the code.

---

## 1 · Name — undecided, and it has to be decided before the page

The pivot re-opened this. Three places currently say different things:

| Where | Calls it | Evidence |
|---|---|---|
| The design documents | **하강** | `docs/game-design.md` v1.1 title, `docs/DESCENT-PIVOT.md` |
| The shipped main menu | **요양원 지하** | `Assets/Scripts/UI/Screens/MainMenuScreen.cs:145` |
| All eleven capsules | **요양원 지하 / SANATORIUM BELOW** | defaults at `tools/render/store_capsules.py:275,277` |
| The map the race runs down | **하강 — 요양원 지하 8층** | `DescentMap.MapName` |

That last row settles the argument on its own: the code that builds the building
already calls the game 하강 and the building 요양원 지하 8층. **The title is the
descent; the sanatorium is the setting.**

**These three have to agree.** Valve requires a capsule to carry the game's name as
it appears on the store ([STEAM-RELEASE.md §2.1](../STEAM-RELEASE.md)), so submitting
as things stand ships a page whose art disagrees with its title.

| Option | Cost | Consequence |
|---|---|---|
| **하강 / DESCENT** ← recommended | re-bake 11 capsules (one command) + one line in `MainMenuScreen.cs:145` | The name becomes the thing the game does. "요양원 지하" names a building, and that building was the stage of the five-storey co-op game. Now that going down eight floors *is* the game, the verb should be the title — and one word survives a 462 × 174 capsule better than two |
| 요양원 지하 / SANATORIUM BELOW | none | No rework. But the design documents and the store would then use different names for the same game, which is how a wiki, a press kit and a Discord end up with three |

```sh
python3 tools/render/store_capsules.py --title "하강" --subtitle "DESCENT" --check
```

| Field | Value (under the recommendation) |
|---|---|
| App name (English store) | `Descent` |
| Localized name (Korean) | `하강` |

> **The game's name does not appear once in the copy below.** That is deliberate —
> everything else can be reviewed, translated and pasted while this decision is open.
> One known risk to weigh: `Descent` is a heavily used title on Steam, and a
> generic name is a discovery cost. `SANATORIUM BELOW` does not have that problem.

---

## 2 · Short description — the block under the capsule

Search results, hover cards, every recommendation queue, and the store page itself
directly beneath the capsule. The hard limit is 300 characters; this is **296**
including spaces (re-counted 2026-08-12, not estimated).

Steamworks asks this field to answer *what makes this different from other games in
its genre*. So the first sentence gives the scale, and the second gives the one rule
that exists nowhere else: reaching the middle of a floor puts you on the outside of
the next one.

```
Twenty runners start together on the rim of B1. Eight storeys down, each the same shape and a different maze, gates narrowing four to two to one. The chute in the middle drops you on the rim below — win a floor and you are last again. Get caught and you start again on B1. Headphones recommended.
```

### When one line is all there is

Discord, the press kit, a "Coming Soon" banner — places with about a hundred
characters rather than three hundred. The same difference, in one sentence:

```
A race to the middle. The hole in the middle drops you back out at the edge of the floor below — eight times.
```

---

## 3 · About This Game

Steam BBCode. Paste as-is.

```
[b]HEADPHONES RECOMMENDED.[/b] Footsteps are how you know where everybody else is. Every storey has a different floor under it — eight storeys, eight surfaces — so a footstep from below means somebody is already a floor ahead of you. On laptop speakers that information is simply gone.

[h2]Twenty runners. Eight storeys. One winner.[/h2]
Twenty of you are spread around the outer ring of B1, and you all start at the same moment. Nobody has to be told where to go. Down. The first person to reach the middle of B8 wins, and there is exactly one of those.

[h2]You solve the same problem eight times.[/h2]
All eight storeys are the same shape: an outer ring, a middle ring, an inner ring, and the centre. All eight storeys are a different maze.

So by the second floor you already know how this building thinks, and you still do not know the way. What you learn is not a map. It is a grammar.

[h2]Winning a floor puts you back at the start.[/h2]
There are two holes in the middle of every storey but the last. Step into one and you come out on the [i]outer ring[/i] of the floor below. Far side, back at the edge, beginning again.

That single rule is the whole race. A lead does not compound. Eight times you are put back on the same line as everybody else — only now you are more tired and your battery is shorter.

There are two holes because there has to be a choice. They land on opposite sides of the floor below, and which one is better depends on a floor you cannot see yet. The storeys do not reshuffle between matches, so knowing them is the skill this game rewards.

[h2]The last gate is one cell wide.[/h2]
Four ways lead from the outer ring to the middle ring. Two lead from the middle to the inner. [b]One[/b] leads into the centre.

The gates do not multiply when the lobby does. Twenty players means twenty players through the same doorway. The crowding is the design, not a symptom of it.

[h2]Each floor has one door.[/h2]
It takes 1.1 seconds to pull shut. During those seconds you are standing still, facing the wrong way, and the person behind you is running. It takes 4.5 seconds to break. And [b]a broken door does not come back[/b] — the door the leader used was never there at all for the third person through.

One door per floor is the entire extent of what you can do to another player. There is no weapon in this game and you cannot touch each other. The weapon is the building.

[h2]If they are near you, you can hear them. And they can hear you.[/h2]
Proximity voice: a player within 30 metres is audible from exactly where they are standing. Which is how you end up asking a stranger in a dark corridor whether their way is blocked, and how you end up two gates later fighting that same stranger for one doorway.

Cooperation works. It is always temporary. One person wins.

[h2]There is something in the building. It is not your opponent.[/h2]
It is not there to be beaten. It is terrain. You cannot kill it and you have no reason to want to — it is simply present, and it is present on the side of the floor you have to cross.

Walking is 2.0 m/s. Running is 4.5. It is 4.8: three tenths of a metre per second faster than your run, which is close enough to feel survivable and never is. Only the sprint at 5.6 outruns it, and the sprint lasts as long as your stamina does. Which makes this game's real decision a single one — [i]spend it now, or spend it at the last gate?[/i]

Being caught does not kill you. It sends you back to the cell you started in on B1, and that is worse. Every floor you had won is gone and you are on the start line again, running. Caught on B6 and that is six storeys. [b]The creature's punishment is that it does not finish you.[/b]

Finishing records a place. Come twentieth and twentieth is what the match remembers. The only way out of this race is to leave it.

[h2]It gets darker toward the middle.[/h2]
The outer ring is lit, and twenty people can see each other on it. The middle ring is not. On the inner ring, whatever is outside your torch does not exist. The battery is a fixed amount per match with nothing to top it up, so getting lost is spending it.

The only thing that glows is the hole in the middle. You can see it from anywhere. The difficulty of this maze is not losing your way — it is [i]watching the place you are trying to reach while you cannot get to it[/i].

[h2]What this is, honestly[/h2]
This is a store page for an unfinished game, put up early to collect wishlists, which is what Steam pages are for. There is no release date on it, because we do not yet know one we could keep.

What actually runs today: the eight storeys exist, and one runner has been walked from the rim of B1 to the middle of B8 — all eight legs, all seven holes, arriving where the fall was supposed to put them. The race rules — start, finish order, being caught, timeout — run in automated tests. Twenty instances on one computer have connected to each other over real sockets and raced.

What does not: [b]no person has ever played a match of this.[/b] Not the people making it, not a tester. The twenty that connected were twenty processes on one desk; two separate machines have never met across a network. Proximity voice is built, and no two people have heard each other through it.

So this page is a promise, and it only promises things the game can keep. If that is not the deal you want, wishlist it and wait — the page will say so when it changes. Nothing here is a render, a mock-up or concept art. Every screenshot is the game, at the brightness the game actually runs at.
```

> ⚠ **The last sentence has a condition on it.** "Every screenshot is the game" is
> true of how they are made, but the ten frames currently in
> `docs/store/screenshots/` photograph the **deleted co-op building** — including a
> shop screen and a frame captioned "five storeys". Until they are re-shot on the
> descent map that sentence cannot go on the page. Gated in
> [checklist.md §1](checklist.md).

---

## 4 · Feature bullets

Steam has no separate bullet field — these belong at the top of About This Game if
you want them, or in the first Store Page "Highlights". Kept short enough to read at
a glance.

```
[list]
[*] Up to twenty players start together on the rim of one building. One of them wins.
[*] Eight storeys, all the same shape and all a different maze. The same problem, eight times.
[*] The hole in the middle drops you on the rim below — winning a floor puts you last again.
[*] Gates narrow four → two → one. They do not multiply when the lobby does.
[*] One door per floor: 1.1 s to shut, 4.5 s to break, and it does not come back.
[*] 30 m proximity voice. Cooperating with a rival is possible and always temporary.
[*] Something in the building you cannot kill. Caught sends you back to B1 — every floor you won, gone.
[*] Eight floor surfaces, one per storey. Footsteps tell you which floor somebody is on.
[/list]
```

---

## 5 · Tags

Up to 20; there are 16 here. Tags are a **distribution decision**, not a description
— they decide which discovery queues this page appears in, and tag mistakes are slow
to undo.

```
Horror, Multiplayer, PvP, Competitive, Survival Horror, Psychological Horror,
First-Person, 3D, Atmospheric, Dark, Maze, Exploration, Stealth, Difficult,
Action, Indie
```

**What was removed, and why each one:**

| Tag | Why it is gone |
|---|---|
| `Co-op`, `Online Co-Op` | There is no co-op. These are the two most damaging tags on the old list: they would put a competitive race in front of the audience most certain to refund it |
| `Asymmetric` | §04 deleted the roles. Twenty identical runners is the opposite claim |
| `Team-Based` | There are no teams |
| `Singleplayer` | Was on the old list because it was the only mode that worked. The game needs two players minimum (§11), and a solo scene is a test harness, not a product |
| `Procedural Generation` | Tempting and false. The storeys are generated from a seed in the **editor** (`Assets/Scripts/Editor/SceneGen/`) and baked into the scene — a player gets the same eight floors every match. That is on purpose: §01 makes knowing the map the skill, which requires the map to stay still |
| `Massively Multiplayer` | Twenty is not massive, and the tag lands the page next to MMOs |
| `Racing` | Steam's racing queue is vehicles. Accurate as English, wrong as distribution |
| `Battle Royale` | Would drive traffic and is not true: no combat, no shrinking circle, no last-one-standing |

**Kept and worth defending:** `Maze` is small but exact, and it is the only tag that
describes the thing a player actually does. `Stealth` earns its place from the
torch-off decision and from the creature patrolling the inner rings, not from a
crouch button. `Difficult` is a promise this design keeps.

---

## 6 · Genre, category and the store's own fields

Not copy, but they are filterable store attributes and they live or die with it.
Steamworks' own field names are English, so both languages read this section.

### Genres

| Field | Set to | Why |
|---|---|---|
| Primary genre | **Action** | It is a real-time first-person race with a chase in it |
| Additional genre | **Indie** | Accurate, and it is how a page this size gets found |
| Not `Racing` | — | Valve's racing genre is vehicles; the queue is wrong even though the word is right |
| Not `Massively Multiplayer` | — | Twenty players, one building, one match. Not a persistent world |
| Not `Adventure` | — | There is no story to advance and nothing to collect |

### Categories — the checkbox column on the store page

| Category | Set | Why |
|---|:--:|---|
| Multi-player | ✅ | The whole product |
| PvP | ✅ | Twenty people competing for one win |
| Online PvP | ✅ | Steam P2P over Steam Datagram Relay (§13) |
| **Single-player** | ❌ | ⚠ **This is a change from the old page, and it is deliberate.** §11 sets the floor at two players. The solo scene the Play button currently loads is a test harness. Ticking this to describe today's build would sell a single-player horror game that does not exist |
| Co-op / Online Co-op / LAN Co-op | ❌ | See §5 |
| Shared/Split Screen | ❌ | Never built, never intended |
| Steam Cloud | ✅ | §13 — settings and the first-launch headphone flag cost nothing to store |
| Steam Leaderboards | ⚠ hold | §02 records a finishing place, which is exactly what a leaderboard is for, and nothing writes one yet |
| Steam Achievements / Stats | ⚠ hold | Tick when §13's telemetry buckets are defined; they are the balance data for §06 |
| Voice chat | ⚠ **hold** | §13's proximity voice is written and has never been heard by two people. A feature flag that fails on launch day is worse than a missing one |
| Full controller support | ❌ | §05 is a mouse-look design — the 45° glance is an analogue mouse gesture. Nothing has been tested on a pad |
| Remote Play Together | ❌ | It streams a local-multiplayer session; this is an online race |
| Captions available | ❌ | There is no scripted dialogue to caption. Do not tick it to look accessible |

### Other partner-site fields

| Field | Set to | Why |
|---|---|---|
| Audio: 3D audio support | ✅ | §05 — the mix is camera-relative by design, which is the whole headphone argument |
| Languages | Korean (interface, full audio); English (interface) | ⚠ **the game's UI is Korean-only today** — every screen in `Assets/Scripts/UI/`. Either localise it or state Korean only. An English store page over a Korean-only UI is a refund queue |
| Release date | **"Coming soon", no date** | §I.5.4: a date is a promise Valve enforces with a two-week gate, and moving it costs the page more than never setting one. Do not set one until twenty peers have finished a match |

---

## 7 · System requirements

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
  Additional Notes: HEADPHONES STRONGLY RECOMMENDED. Every storey has a different
  floor surface, and footsteps are the only way to tell which floor someone is on
  and how close they are. Stereo speakers lose that. A microphone is needed for
  proximity voice.

Recommended:
  OS:        Windows 11 64-bit / macOS 14 (Apple silicon)
  Processor: 6-core, 3.0 GHz
  Memory:    16 GB RAM
  Graphics:  GTX 1660 / RX 5600 / Apple M1 or better
  Network:   Broadband internet connection
  Storage:   4 GB available space
  Additional Notes: Headphones. Closed-back if you have them.
```

⚠ **Every number in that block is an estimate**, and the estimate is now worse than
it was: it was made for a four-player co-op game, and this one puts up to twenty
networked players in one building. **No player build has ever been launched**, on this
machine or any other, so there is nothing to profile: `dist/windows-x64/HorrorGame.exe`
does exist on disk (2026-08-10) but it is a Development **Mono** build whose own report
says `shippableOnSteam: false`, and **a Windows IL2CPP player has never once been
produced** ([STEAM-RELEASE.md §I.2.3](../STEAM-RELEASE.md)). Measure a real build before
this goes public — a wrong minimum spec is a refund queue.

---

## 8 · What is deliberately not in the copy

Recorded so it does not get helpfully added back by somebody who reads the design
document and notices a gap.

| Not written | Why |
|---|---|
| **A match duration** | §01 says 12–20 minutes and nothing has measured it. F-006's 7.2-minute median was measured on the co-op game and does not transfer. The most natural sentence to write here is the one most likely to be false |
| **"Twenty players" as an achievement** | It appears as the design's number and the honest paragraph says no person has played a match. Twenty *processes* on one desk is not twenty players, and the difference is the whole claim. Do not let it drift into a boast |
| Anything about a story, a sanatorium, or why you are racing | There is none written. Inventing one on the store page is how a page ends up promising a narrative game |
| Early Access framing | This is a Coming Soon page. Whether it launches into Early Access is a separate decision with its own Valve questionnaire |
| Review quotes, laurels, scores | There are none, and Valve rejects capsules carrying them |

---

## 9 · Cross-references that move when this file does

| File | What it holds |
|---|---|
| [copy-ko.md](copy-ko.md) | The original. Edit Korean first |
| [checklist.md](checklist.md) | What is still blocking the page, including the screenshot gate |
| [trailer.md](trailer.md) | The shot list, rewritten for the descent |
| [headphone-notice.md](headphone-notice.md) | The four placements of the headphone line |
| [assets.md](assets.md) | Capsule sizes, and which frames need re-shooting |

---

## 10 · Every claim, and what backs it

**Measured and designed are separated on purpose.** ⚠ must become true, or the
sentence is cut before the page is submitted.

| Claim in the copy | Status | Evidence |
|---|---|---|
| Eight storeys, same shape, different mazes | ✅ measured | NavMesh audit after the 2026-08-10 re-lay (STATUS §1.2): **204 markers, 2674 pairs, 100.0 % complete, 8 islands** (one per storey), worst snap **0.25 m** (`PlayerSpawn_14`), creature reach 196/196 over 8/8 storeys. *The 289/6993/0.22 m figures this row used to carry were measured on the building before that re-lay.* |
| Rim of B1 to the middle of B8 is actually connected | ✅ measured | One runner walked it (STATUS §1.4): 8/8 legs PathComplete on the real bake, 7/7 chutes, **0.00 m from the middle of B8**, 171 s elapsed. That is a NavMesh robot, not a match length |
| Gates 4 → 2 → 1 | ✅ code | `Assets/Scripts/Editor/SceneGen/RadialStorey.cs` — the four 외곽 관문 cross the wall at **d9**, the two 중간 관문 take the **d5** lane, and the single 중심 관문 goes through **d2** into the chamber. *(The bands jogged outward on 2026-08-11: 안쪽 d3, 중간 d7/d8, 외곽 d10/d11. This row used to say "bands at d8/d5/d2", which named the old layout.)* |
| **Two** holes per storey, landing on opposite sides of the rim below | ✅ code | `Editor/SceneGen/DescentMap.cs` `HangChutes` — two per floor, `Pick(rim, +1)` / `Pick(rim, -1)`. The loop runs `level < Storeys - 1`, so **B8 has none** — it carries the finish instead. Note the design document says "2~3"; the artefact says two, and the copy follows the artefact |
| It is a fall, not a teleport | ✅ code | `Gameplay/Race/Chute.cs` — `DropHeightMetres = 3.0f`, then the controller's own gravity |
| **One door per floor** | ✅ code | `RadialStorey.cs`: *"One door a storey, on one of the two 중간 관문."* A door on one of four parallel rim gates is not a bottleneck; a door on the single inner gate would delete the centre |
| 1.1 s to shut, 4.5 s to break | ✅ code | `Core/GameConstants.cs` — `DoorShutSeconds = 1.1f`, `DoorBreakSeconds = 4.5f` |
| A broken door does not come back | ✅ code | `Core/DoorState.cs`: `DoorPhase.Broken` is *"Broken open for the rest of the match. It cannot be shut again."*, and the type comment says *"A broken door stays broken."* `DoorRepairFraction = 0.25f` only backs off **partial** break progress, so a door somebody stopped hitting recovers and a door somebody finished never does |
| Walk 2.0 / run 4.5 / creature 4.8 / sprint 5.6 | ✅ measured | §06, measured on real geometry: 4.80 m/s in a corridor, 0.80 m/s sprint gap (STATUS §1.3) |
| Caught sends you back to your starting cell on B1 | ✅ code | `Core/Race/RaceState.cs` `ReportCaught` — status back to `Running`, storey back to 0, `TimesCaught` + 1. `Gameplay/Match/MatchDirector.cs:1666` — *"§02: caught is not death. The creature sends a runner back to the place they started on B1 and they keep racing."* `UI/Screens/CaughtScreen.cs` draws a 0.5 s curtain reading `B6 → B1` |
| **Nothing in this game eliminates a player** | ✅ code | `RacerStatus.Eliminated` survives, but only for **a seat that emptied** — `RaceState.cs:21`: *"Nothing in the game eliminates a player any more."* `GhostSession` is not in the repository. The design document's §02, §06, §09 and §11 still describe 탈락 and the 유령; **they are the stale side** |
| Finishing records a place | ✅ code | `RaceDirector.CheckFinish` → `RaceState.ReportFinish` returns the place; `Finished` fires for every finisher |
| Proximity voice at 30 m | ⚠ **code exists, nobody has heard it** | `GameConstants.VoiceCutoffDistance = 30f`; `Assets/Scripts/Steam/Voice/`. Never heard by two humans. **Do not tick the Voice Chat category** |
| Eight floor surfaces | ✅ on disk | `Assets/Audio/Footsteps/` — carpet, concrete, earth, gravel, metal, tile, water, wood |
| "One surface per storey" | ✅ code | `DescentMap.Storey` assigns them deepest-last: B1 concrete 하역장, B2 wood 기록보관소, B3 metal 기계실, B4 gravel 저탄장, B5 tile 저수조, B6 carpet 병동, B7 water 수몰층, B8 earth 굴착층 |
| Ring lighting: outer lit, inner torch-only | ⚠ **design** | §03. Confirm the descent map actually carries per-ring lighting before this paragraph ships |
| Battery fixed per match, no resupply | ⚠ **design** | §03 (inherited from the deleted §08). The rule is simpler now that there is no shop, but it has not been measured on a race map |
| Twenty players | ⚠ **the socket takes twenty; twenty people have not** | The cap that executes **is twenty**: `Net/HorrorGameNetworkManager.cs:88` sets `maxConnections = GameConstants.RaceRunnersMax` and Mirror enforces it in `NetworkServer.IsConnectionAllowed`. `PlayersPerMatch = 4` is `NetLobby`'s seat count and **nothing on the race path spawns a NetLobby** — the manager's own comment (lines 330–346) calls it *"a latent cap … currently unreachable"*. What is true is that every peer that has ever connected was a process on this one desk. *(This row used to say the executing cap was four and that no two peers had ever connected; both came from [STEAM-RELEASE.md §I.4](../STEAM-RELEASE.md), which is now the stale document — `LocalTwoInstanceEntry.cs` reads `-horror-host`/`-horror-client` and drives the real lobby, and `NetSocketTests`/`NetHumanRunnerTests`/`NetRunnerTests` all call `StartHost`/`StartClient`.)* |
| **Nobody has played it** | ✅ measured, and this is the honest claim | STATUS.md: *"nobody has played it. Not the owner, not a tester, not for one match."* This — not the peer count — is the sentence the page's credibility rests on |
| "The storeys do not reshuffle between matches" | ✅ code | Generation is Editor-side and baked (`Assets/Scripts/Editor/SceneGen/`). This is also why `Procedural Generation` is not a tag |
| Match length | — | **No number is written anywhere in this copy.** §01's 12–20 minutes is unmeasured. Do not add one |
| "Every screenshot is the game" | ⚠ **false until they are re-shot** | The ten on disk are the deleted co-op building, all stamped 2026-08-01 01:59 on seed 1204. [checklist.md §1](checklist.md) |
