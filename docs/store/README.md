# The Steam store page

§14 attaches a warning to the last item in the development order and puts it in bold,
so it is repeated here at the same volume:

> **경고: 7번을 늦추지 말 것.** 상점 페이지는 게임 완성 전에 올려서 위시리스트를 모으는
> 용도다. 출시일 알고리즘 노출이 여기 걸려 있고, 스팀에서 가장 흔한 실수다.

This folder is that page: the assets, the copy in both languages, and an honest
account of what is still missing. It is the **content**.
[STEAM-RELEASE.md](../STEAM-RELEASE.md) is the **process** — registration, the $100
App ID, W-8BEN, depots, branches, promotion — plus Part I's audit of where the repo
actually stands. Nothing here repeats it.

> **Copy rewritten 2026-08-03 for the 20인 경주.** Everything in this folder was
> written on 2026-08-01 for a four-player asymmetric co-op looting game. That game was
> deleted on 2026-08-02 ([DESCENT-PIVOT.md](../DESCENT-PIVOT.md)). The words are now
> about the race. **The still pictures are not** — see below.
>
> **Audited 2026-08-12 against the artefact, not against these documents.** Four things
> this folder was asserting had stopped being true: the copy still sold an **elimination
> rule the game deleted on 2026-08-04**, it named `PlayersPerMatch = 4` as the executing
> player cap when the socket has enforced twenty since the manager's `Awake` was fixed,
> it carried NavMesh figures from the building that existed before the 2026-08-10 re-lay,
> and it said there was no trailer while a 41-second one sat in this directory. Every
> number below now comes from something run or read on 2026-08-12.

---

## Read in this order

| File | Answers |
|---|---|
| [checklist.md](checklist.md) | **Start here.** Can the page go up today, and what is stopping it? |
| [copy-ko.md](copy-ko.md) | The Korean copy — name, short description, About This Game, bullets, tags, requirements. **This is the original**; the English follows it |
| [copy-en.md](copy-en.md) | The same in English, plus the tag list, the genre and category fields, and one table tracing every claim to the file that backs it |
| [headphone-notice.md](headphone-notice.md) | §13's 헤드폰 권장 표기, in the four places players actually see, and why the pivot made the requirement broader rather than narrower |
| [trailer.md](trailer.md) | The cut, shot by shot: 23 shots, 41.57 s, what each proves, what the rig cannot do, and the five beats that still need a person at a keyboard |
| [assets.md](assets.md) | Every capsule size with the Steamworks page it was read off, the rules that get a page rejected, and the screenshot re-shoot list |

---

## What is in here

```
docs/store/
  capsules/              11 assets at Valve's current sizes, + legibility/ 5 proofs
  screenshots/           10 frames — ⚠ all of the deleted co-op game
  trailer/               13 reference frames — ⚠ same, and no longer an input to anything
  defects/               5 frames that are evidence, NOT for upload
  trailer_descent.mp4    ✅ the trailer. 1920×1080, 30 fps, 41.57 s, 11,960 Kbps, 59 MB
  party.mp4              ⚠ not a trailer: 1280×720, 24 fps, 3.00 s. Delete or move it
```

Every line of that tree was counted and every figure `ffprobe`d or `sips`ed on
2026-08-12.

---

## The short version

**The copy can go up. The stills cannot.** That is the exact inverse of what this
file said on 2026-08-01, which is why it is worth saying in those words. The moving
picture can go up too, which is new since 2026-08-04.

Both descriptions have been rewritten for the race, every claim in them is traced to
a file on disk in [copy-en.md §10](copy-en.md), and the tags no longer say `Co-op`.
Capsule **sizes** are all correct — re-verified with `sips` on 2026-08-12, all eleven
plus the five legibility proofs ([STEAM-RELEASE.md §I.3.2](../STEAM-RELEASE.md)).

**Two things block the page, in order** — it was three until the trailer got shot:

1. **All ten screenshots photograph the deleted game.** One of them is the shop; one
   is captioned "five storeys" for a building that now has eight. The capsules are
   composited from the same renders. [assets.md §4](assets.md) is the re-shoot list —
   ten frames, and it names the two that solve the darkness problem.
2. **The name is undecided and it is printed on eleven capsules.** The design
   documents say 하강, the shipped main menu says 요양원 지하, and the capsules say
   요양원 지하 / SANATORIUM BELOW. `DescentMap.MapName` already reconciles them —
   *하강 — 요양원 지하 8층* — which is the argument for making the title the descent
   and the sanatorium the setting. [copy-en.md §1](copy-en.md) has the command.

**And one that stopped blocking it.** There is a trailer now:
`trailer_descent.mp4`, 23 shots and 1247 frames rendered in-engine by
`tools/render/descent_film.py`, clearing every row of Valve's format table. It is
**silent** — the mix is an edit-suite job and [trailer.md §9](trailer.md) says what has
to happen first. `party.mp4` is still sitting beside it: a three-second 720p clip named
after a party of four that no longer exists. Move it before somebody uploads the wrong
file.

**One thing that is honestly missing and is nobody's marketing problem:** there is no
screenshot or frame with more than one *player* in it, on the store page of a
twenty-player race. Not because the peers cannot connect — twenty instances have
connected on this desk, and the trailer rig can stand two shipped `Runner.fbx` bodies on
two real spawn marks — but because **nobody has played a match of this game.** Not the
owner, not a tester, not once. The copy says so in its own words rather than hiding it.

> ⚠ **Do not re-import "no two peers have ever connected" from STEAM-RELEASE.md
> §I.4.2.** That paragraph was written before `LocalTwoInstanceEntry.cs` existed, and it
> is now the stale document. The true and much more uncomfortable sentence is the one
> above it: no person has played this.

---

## Everything regenerates

```bash
python3 tools/render/store_shots.py probe --seed 20260802 --out /tmp/descent_probe.json
python3 tools/render/store_shots.py shoot --spec tools/render/store_shots.json
python3 tools/render/store_capsules.py --check
python3 tools/render/descent_film.py --out docs/store/trailer_descent.mp4
```

⚠ **`store_shots.json` is stale and `trailer_frames.json` is dead.** Both hold camera
coordinates in the seed-1204 sanatorium; the descent is
`DescentMap.DefaultSeed = 20260802`, eight storeys, every one centred on cell (12, 12)
with radius 11 (`DescentMap.Centre = 12`, `DescentMap.Radius = 11`). Re-probe and rewrite
`store_shots.json` against [assets.md §4](assets.md), then shoot. **Do not rewrite
`trailer_frames.json`** — the trailer moved to `trailer_shots.json`, which names anchors
the rig resolves against the scene rather than coordinates that go stale on a re-seed
([trailer.md §0](trailer.md)).

Never add `-nographics`; the driver deliberately omits it, because that flag disables
the graphics device and every frame comes out black. Check the exit code before the
frame count. See [assets.md §3](assets.md).

---

## What the copy deliberately does not say

Recorded here because these are the sentences somebody will helpfully add back:

- **No match duration.** §01 says 12–20 minutes and nothing has measured it.
  [F-006](../BALANCE-FINDINGS.md#f-006)'s 7.2-minute median was measured on the co-op
  game and does not transfer.
- **No release date.** A date is a promise Valve enforces with a two-week gate, and
  moving it costs the page more than never setting one.
- **No claim that twenty players work.** Twenty is the design and the socket really does
  enforce it (`HorrorGameNetworkManager.cs:88`), but no person has played a match at any
  field size, and the About This Game says that out loud. Do not upgrade "twenty
  instances connected on one desk" into "twenty players work".
- **`Single-player` is not ticked.** It was, on the old page, because it was the only
  mode that worked. The game needs two players (§11) and a solo scene is a test
  harness.
