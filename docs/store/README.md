# The Steam store page

§14 attaches a warning to the last item in the development order and puts it in
bold, so it is repeated here at the same volume:

> **경고: 7번을 늦추지 말 것.** 상점 페이지는 게임 완성 전에 올려서 위시리스트를 모으는
> 용도다. 출시일 알고리즘 노출이 여기 걸려 있고, 스팀에서 가장 흔한 실수다.

This folder is that page: the assets, the copy in both languages, and an honest
account of what is still missing. It is the **content**.
[STEAM-RELEASE.md](../STEAM-RELEASE.md) is the **process** — registration, the
$100 App ID, W-8BEN, depots, branches, promotion — and nothing here repeats it.

Built 2026-08-01, against the map at seed 1204 / dressing seed 4703.

> An art pass was landing while these frames were taken — untracked work in
> `Assets/Scripts/Editor/Rendering/` and `Assets/Textures/`, and the Unity project
> lock was held against this pass twice. **Re-shoot before uploading.** It is one
> command, and every measurement in [assets.md §4](assets.md) is the thing to
> re-measure.

---

## Read in this order

| File | Answers |
|---|---|
| [checklist.md](checklist.md) | **Start here.** Can the page go up today, and what is stopping the rest? |
| [copy-ko.md](copy-ko.md) | The Korean copy — short description, About This Game, bullets, tags, requirements. This is the original; the English follows it |
| [copy-en.md](copy-en.md) | The same in English, plus the two name fields |
| [headphone-notice.md](headphone-notice.md) | §13's 헤드폰 권장 표기, in the four places players actually see |
| [trailer.md](trailer.md) | The shot list: ten beats, what each proves, the command that renders each reference frame, and the four that cannot be shot yet |
| [assets.md](assets.md) | Every capsule size with the Steamworks page it was read off, the rules that get a page rejected, and how to rebuild all of it |

---

## What is in here

```
docs/store/
  capsules/          11 assets at Valve's current sizes, + legibility/ proofs
  screenshots/       10 frames, 1920×1080, gameplay only
  trailer/           13 reference frames, one per beat
  defects/           5 frames that are evidence, NOT for upload
```

Everything is generated, and everything regenerates:

```bash
python3 tools/render/store_shots.py probe --seed 1204 --out /tmp/store_probe.json
python3 tools/render/store_shots.py shoot --spec tools/render/store_shots.json
python3 tools/render/store_capsules.py --check
python3 tools/render/store_shots.py shoot --spec tools/render/trailer_frames.json
```

Never add `-nographics`; the driver deliberately omits it, because that flag
disables the graphics device and every frame comes out black. Check the exit code
before the frame count. See [assets.md §3](assets.md).

---

## The short version

**The page can go up now. It should go up now.** Capsules exist at every size
Valve asks for, ten 1920×1080 gameplay screenshots exist, and both descriptions
are written and paste-ready.

**Three things are honestly missing**, and only one of them is marketing work:

1. **No trailer.** The highest-leverage asset on the page. Not blocked on editing
   time — blocked on §03's clue prop, §03's objective prop and §08's vehicle,
   which all render as untextured white primitives, and which are what three of
   the four beats worth cutting are *about*.
2. **No screenshot with four players in it**, on the store page of a four-player
   co-op game, because no four-player session has ever been run.
3. **The page will look darker than every other horror page on Steam.** Eight of
   the ten screenshots sit below ART.md's own legible-fraction floor. That is the
   open art regression from the map growth, and STEAM-RELEASE.md §2.2 is right
   that the fix is not to brighten the marketing.

The copy is written for the game as designed and every claim that is not yet true
is marked ⚠ in place and collected in [checklist.md §2](checklist.md). The
sentences that would have been easiest to write — "25–35 minute matches", "four
players and proximity voice" — are deliberately absent, because
[F-006](../BALANCE-FINDINGS.md#f-006) says the first is false and STATUS §5 says
nobody has tested the second.

> **One ⚠ came off on 2026-08-01 and one number under it moved.** The threat-tier
> claim — "the night gets worse in stages" — was carrying a warning that only 1.2 %
> of matches ever reach tier 2. That figure had been measured against a four-zone
> ring the game does not ship; on the real building it is **33.6 %**, with 17.4 %
> reaching tier 3 and 13.0 % tier 4, so the sentence is now simply true. Match length
> moved with it, 2.5 → **7.2 min**, which is no longer an order of magnitude off
> §01's 25–35 but is still about 3.5× off. **Do not add a duration to the copy.**
> Every ⚠ in [checklist.md §2](checklist.md) was re-checked against the same run.

**One decision is open and it is baked into eleven files.** The game's name of
record is `요양원 지하` (the main menu says so). The Latin rendering on every
capsule is `SANATORIUM BELOW`, and **nobody has approved it.** One flag re-bakes
the set:

```bash
python3 tools/render/store_capsules.py --title "요양원 지하" --subtitle "YOUR NAME" --check
```
