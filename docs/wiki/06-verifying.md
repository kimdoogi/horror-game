# How to verify anything

> **Do not hand back something you did not run.** And do not trust a green tick you
> did not read the exit code of. Half the checks in this project have a failure mode
> where they look green while proving nothing.

The full inventory of checks is [`docs/TESTING.md`](../TESTING.md) — **but it opens at
`a3e268e` and still documents the deleted simulator's commands, so read it against
[`docs/STATUS.md`](../STATUS.md) §4, which is the current list.** STATUS.md also carries
the last real output of each. This page is the routing table and the list of false greens.

> **Everything below was re-derived at HEAD `4ab204f`..`017b489` on 2026-08-12** by running or
> reading the artefact. The 2026-08-02 pivot ([DESCENT-PIVOT.md](../DESCENT-PIVOT.md))
> deleted the five roles, the economy and the clue chain from the game *and from the
> code*, and this page had been routing readers to commands that went with them.

Shell preamble for everything below:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
P=/Users/doogi/horror-game/unity/HorrorGame
cd /Users/doogi/horror-game
```

---

## 1. Which check answers which question

| The question you actually have | The check | Cost |
|---|---|---|
| Are the rules and every tuned number still correct? | `dotnet test core/HorrorGame.Core.Tests/…csproj` | **~1.5 min** |
| Does everything compile? | `dotnet build core/HorrorGame.sln -c Release` | 4 s |
| Does Unity compile? | `$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log` then `grep -cE '^Assets/.*error CS' /tmp/u.log` | ~1 min |
| **Can the monster reach a player at all?** | PlayMode `-testFilter "MonsterChaseTests"` | ~1 min |
| Does the solo scene build, with the runner's animator wired? | `-executeMethod HorrorGame.EditorTools.SoloPlaytest.BuildBatch` (**not `SoloPlaytest.VerifyBatch`** — that one existed and was **deleted with the co-op loop**, along with the `SoloMatchLoopTests` it drove; see the tombstone at `SoloPlaytest.cs:227`) | ~1 min |
| Is the navigation surface connected? | `-executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch -auditScene …` | ~1 min |
| Is the map legal under §12, and how does it grade? | `-executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu` | ~1 min |
| Does a re-rolled map still write, dressed? | `-executeMethod HorrorGame.EditorTools.MapPipeline.RegenerateFromCommandLine` — **the only regen entry that dresses.** `MapSceneGenerator.GenerateFromCommandLine` is layout-only, so its band numbers describe a building the game does not ship | ~5 min |
| Will a stereo import have made a footstep unplaceable? | `-executeMethod …AssetImportValidator.ValidateAllBatch` | ~1 min |
| Are the eight floor surfaces still tellable apart? | `tools/audio/verify_audio.py` | ~1 min |
| Can you see the monster at 15 m? | `HorrorGame.Gameplay.MonsterEditor.MonsterShot.StageBatch`, **no `-nographics`** | ~2 min |
| Do you have hands, and is the torch in one? | `HorrorGame.Gameplay.PlayerEditor.FirstPersonHandsShot.Batch`, **no `-nographics`** — reads out each hand's viewport coordinate | ~3 min |
| Is the picture inside ART.md's luminance bands? | `SceneShot.Batch` then `tools/render/frame_stats.py` | ~3 min |
| Do the Blender generators still work? | `tools/ci/run_blender_generators.sh` | ~10 s |
| Does it produce a shippable player? | `-executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine -buildPlatform windows-x64 -buildConfig release`, then read `dist/last-build-summary.txt` **and** check for `MONO-FALLBACK-DO-NOT-SHIP.txt` | ~2 min each |
| **Is the game fun?** | Humans, several instances, Discord. `LocalTwoInstance.LaunchSmallField` (`DefaultFieldSize` **4**). §14 | unautomatable |

The last row is not a joke. §14 says questions 1 and 2 decide the project and
「직접 만져봐야 나온다」. Every automated gate above is green or explained; none of
them can answer it. §14's Q3 — 「관문에서 붐비는 것이 재밌는가」 — is the one the pivot
added, it 「이 게임이 20인에서 재미있는지를 정한다」, and §14's own prototype note says it
needs **at least four** people at once: 「문 앞에서 붐비는지 보려면 최소 넷은 필요하다」.
Two instances cannot ask it. Twenty is `LaunchFullField`, and it is not the size to
reach for casually — ask before running one.

---

## 2. The exact commands, in the order worth running

```bash
dotnet test  core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj      # 357/357, 건너뜀 0
dotnet build core/HorrorGame.sln -c Release                               # 오류 0개, 경고 4개

$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log
grep -cE '^Assets/.*error CS' /tmp/u.log                                  # 0

$U -batchmode -projectPath $P -runTests -testPlatform PlayMode \
   -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SoloPlaytest.BuildBatch -logFile /tmp/solo.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu -logFile /tmp/quality.log
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log

# NO -quit. The runner exits from its own callback.
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine -logFile /tmp/t2.log

tools/audio/.venv/bin/python tools/audio/verify_audio.py                   # exit 1, RESULT: FAIL
bash tools/ci/verify_audio.sh                                             # exit 0, RESULT: PASS

# WITHOUT -nographics, or every shot is black.
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneShot.Batch \
   -shotScene Assets/Scenes/Map_FirstSketch.unity -shotTag verify -logFile /tmp/shot.log
```

Expected exit codes: **0** everywhere except `verify_audio.py` (**1** — two blocking
defects, both [F-002](09-open-questions.md#f-002); re-run 2026-08-12). The two audio
commands disagree on purpose: `verify_audio.py` reports the raw verdict, and
`tools/ci/verify_audio.sh` suppresses the two F-002 rows against
`tools/ci/audio_baseline.json` so the gate fails on *new* defects only. Read both.

**The Unity suite's exit code is not on this list any more.** It used to say 6 for
`SoloMatchLoopTests`; that test's file is gone with the co-operative loop it drove, and
[B-002](../BLOCKERS.md#b-002) is 🟡 dormant. What the suite actually reports, carried and
dated because Unity was not run for this pass:

| Platform | Result | Dated |
|---|---|---|
| core (`dotnet`) | **357 / 357**, 건너뜀 0, 1 m 41 s | **2026-08-12, run here** |
| EditMode | **95 / 95** — 3 assemblies, one of them `Pivot`, whose job is to fail if 금고 or 상점 comes back | 2026-08-08, [TESTING.md §8](../TESTING.md) |
| PlayMode | **124 total, 121 passed, 3 failed** — all three `VoiceSocketTests` | 2026-08-08, [STATUS.md §2.3](../STATUS.md) |

Anything other than exactly those three PlayMode reds is a regression. **Do not add these
three numbers together and quote the sum across the pivot** — `e8c67ae` deleted the
co-operative game and its tests, so no total from before it is comparable
([STATUS.md §1.8](../STATUS.md)).

Read PlayMode results properly rather than trusting the exit code:

```bash
python3 -c "import xml.etree.ElementTree as ET; r=ET.parse('/tmp/chase.xml').getroot(); print(r.get('total'), r.get('passed'), r.get('failed'), r.get('result'))"
```

---

## 3. The false greens — checks that pass while failing

### 3.1 `-quit` on a test run reports nothing and exits 0

Unity's test runner is **asynchronous** and exits from its own callback. `-quit`
shuts the editor down before results are written. The run then prints no summary,
writes no XML, exits 0, and looks green. A failing EditMode test went unnoticed here
for exactly this reason, and [TESTING.md's own §4 correction](../TESTING.md) records
it. **Never put `-quit` on a `-runTests` or `RunFromCommandLine` invocation.**

### 3.2 A held project lock makes an error count meaningless

Only one process may hold `unity/HorrorGame/Temp/UnityLockfile`. A second Unity exits
early — and a log from a run that died early contains zero errors, which is not the
same thing as a clean run. **Check the exit code first, always, and check for another
Unity before you start:**

```bash
ps aux | grep "[U]nity.app/Contents/MacOS/Unity"
```

Serialise and retry. This is not hypothetical: while this page was being written, a
concurrent `BuildPipelineRunner.BuildFromCommandLine` held the lock, and every Unity
command on this page was therefore attributed to STATUS.md rather than re-run.

### 3.3 The NavMesh audit passes while the monster is deadlocked

The most expensive false green in the project's history. On the three-storey building it
printed:

```
[NavMeshAudit] PASS
  pairs 630 · complete 630 (100.0 %) · islands 1 · monster reach 19/19
```

**This exact output was green while the monster was frozen 95 m from the player.**
The audit asks `NavMesh.CalculatePath`; the monster walks `NavMeshPath.corners` one
at a time. Those are different questions, and a `NavMeshLink` answers the first and
not the second. The audit is necessary and **not sufficient**. `MonsterChaseTests` is
the sufficient one. Full story: [the expensive bugs](07-expensive-bugs.md).

The shape of the output has since changed and so has the shape of the trap. Today's is
[STATUS.md §1.2](../STATUS.md) — `markers 204 · pairs 2674 · complete 2674 (100.0 %) ·
islands 8 · monster reach 196/196 on the same storey, 8 of 8` — and it carries two
qualifiers you have to read before quoting it. **`islands 8` is correct** on a tower whose
only vertical links are one-way falls, even though the audit still prints
`← the surface is in pieces` next to it ([B-014](../BLOCKERS.md#b-014)). And **the
question got weaker when the game did:** the creature cannot climb a chute, so the audit
now asks about reach *within* a storey, not across the building.

### 3.4 `-nographics` makes every render black

`-nographics` disables the graphics device. Any command that photographs anything —
`SceneShot.Batch`, `MonsterShot.Batch`, `MonsterShot.StageBatch`,
`AtmosphereSetup.ShotBatch`, `StartleShot.Batch`, `GunShot.Batch`,
`FirstPersonHandsShot.Batch` — must run **without** it. A black PNG is a
plausible-looking output for a horror game, which is what makes this one expensive.

### 3.5 Blender exits 0 after a Python exception

Covered in [the asset pipeline §3](05-asset-pipeline.md). Grep for `ASSET_FAILED`,
for a traceback, and for the presence of an `ASSET_REPORT` line. The exit code is the
fourth and weakest signal.

### 3.6 A green §12 checklist does not mean a good map

`MapValidator` runs exactly **14** rules (count them, 2026-08-12:
`grep -c 'public const string Rule' unity/HorrorGame/Assets/Scripts/Core/Map/MapValidator.cs`
→ `14`, and `Validate` calls fourteen `Check*` methods). Core's own fixture map passes
them all and grades **10/10 TooEasy** on the 주자 테스트 — pinned by
`MapTests.SketchMap_PassesTheChecklistAndStillGradesTooEasy`. The checklist is necessary,
not sufficient; the grade is the second half.

> 🔴 **History — it used to be 17, and three of the deletions are the pivot, not a
> weakening.** `RuleObservationPosts`, `RuleCandidateSites` and `RuleConcealmentNearExit`
> counted places for §04's 관측자, §03's clue chain and §07's ambush; all three systems
> are gone, and `MapValidator`'s own comment is the right way to read it — *"a gate on a
> door that has been removed from the building."* `RuleZoneDiagonal` went with the §12
> re-derivation, because on 하강 a 구역 **is** a 층. What arrived in exchange is
> `RuleCentrePath` (§12-D's 외곽→중심 90–140 m), which gates and which **the shipped map
> fails on every storey** — waived by name in `MapSceneGenerator.KnownFailingRules`, and
> the only entry left in it. That is [B-019](../BLOCKERS.md#b-019).

**The shipped map still grades 10/10 TooEasy**, and the census rules out an unlucky
ten-point sample: `680/680 escapable (100%)`, 85 places on each of the eight storeys
([STATUS.md §2.1](../STATUS.md)).

> 🟢 **`sight-break-spacing` is no longer the cause, and no longer blocks generation.**
> [B-007](../BLOCKERS.md#b-007) closed 2026-08-10 on all eight roster seeds: the bands
> jog outward, `160 시야 차단 지점` from 456 bends, the deepest **12.5 m** against a
> 14.4 m cap, nearest-neighbour spacing 15 m and **160/160 inside** §12's 15–25 m. The
> earlier figures on this page — *79 corners, mean 4.1 m, 0 inside the band* — were the
> five-storey building, and were also the **raw bend** statistic rather than the 지점
> statistic the rule actually applies.
>
> **The grade did not move anyway, and that is the finding.**
> [F-013](../BALANCE-FINDINGS.md#f-013) predicted it as arithmetic: with any §12-legal
> geometry a release fires after 16.8 m of route past one bend, and §12 *mandates* an
> S자 통로 of 10 m × 2 per zone — so no map that obeys §12's construction rules can fail
> §12's own 실전 검증. The 5–7/10 band is a co-op-era instrument that asked whether one
> player in four could out-run the creature, in a race where all twenty can. Its
> replacement is 탈출 대가 — what a chase *costs* in §07's currency.

### 3.7 A passing suite with skipped tests

`dotnet test` prints `건너뜀: N`. If N is not 0, the total is inflated by disabled
cases. Read both numbers.

### 3.8 The chase numbers are measured at one tier, and nothing measures how often a match reaches it

`MonsterChaseTests` sets the clock to 20 minutes so §07 reads 심야 — the row whose monster
speed is `ThreatSpeedLateNight` 4.8 m/s, which is `MonsterBaseSpeed` and therefore §06's
whole derivation. The numbers are correct *for the tier they are measured at*, and §07's
table runs 4.4 / 4.6 / **4.8** / 5.0 / 5.2.

> 🔴 **This section used to say "a third of matches reach it — 33.6 %".** That figure, and
> every tier percentage that went with it, came from `core/HorrorGame.Sim`, and
> **the simulator was deleted entirely at `e8c67ae`.** There is no `horrorsim`, no
> `dotnet run --project core/HorrorGame.Sim`, and no replacement
> ([TESTING.md §9](../TESTING.md)). So the honest statement today is not a smaller
> number — it is that **nobody knows the tier distribution of a real match**, and the
> only instrument that ever claimed to was measuring a four-zone ring the game never
> shipped ([B-012](../BLOCKERS.md#b-012)). Treat any tier percentage you find in an
> older document as void rather than stale.

---

## 4. What each red thing currently means

Re-derived 2026-08-12. The three rows this table used to carry are all resolved or moot,
and are kept in the second table below so nobody re-opens them.

| Red thing | Status | Do not "fix" it by |
|---|---|---|
| `verify_audio.py` exit 1, two BLOCKING rows | [F-002](09-open-questions.md#f-002) — an open design decision. `tools/ci/verify_audio.sh` is green because `tools/ci/audio_baseline.json` suppresses exactly these two | changing the mix to make the number go away. `AudioTests.OccludedAudibility_InvertsTheClarityTable_AsF002Reports` fails if you do |
| `[FAIL] centre-path` on every map generation, waived by name | [B-019](../BLOCKERS.md#b-019) — a *map* defect with an address, not a rule defect. 21 of 22 storey entry points are now inside §12-D's 90–140 m band | relaxing the band. §12-D forbids it in as many words: 60–90 m means 「맵을 아는 것이 실력이라는 전제」 disappears without a reward |
| ~~CI's `core tests (dotnet)` fails on `floor=512` against a 357-test suite~~ | 🟢 **fixed `1ceb636`, 2026-08-12** — `floor=357` now, and the comment beside it records what nine days of red cost. It had failed every push for ~45 commits while the suite underneath was green | lowering it again without saying why. The floor exists so a run that executes *nothing* cannot pass — **a count in a gate is a claim with a date on it** |
| Three `VoiceSocketTests` red in PlayMode | [STATUS.md §2.3](../STATUS.md), carried 2026-08-08 — and one of the three is §06's mechanic, not plumbing: `MatchDirector.VoiceEffort` reads `Silent` while the player holds Shout | treating it as environment. Two of the three say no voice frame arrives at all |
| `← the surface is in pieces` next to `islands 8` | cosmetic, [B-014](../BLOCKERS.md#b-014)'s last open piece. Eight one-way storeys **are** eight surfaces | "fixing" the map. Fix the log line |

**Closed, and listed so they are not re-opened:**

| Was red | Now |
|---|---|
| `SoloMatchLoopTests` fails on a Mirror package `.meta` | 🟡 [B-002](../BLOCKERS.md#b-002) **dormant and unverifiable** — it stopped reproducing when the package cache was rewritten, and the test file itself no longer exists at HEAD. It drove the §01 co-operative loop the pivot deleted |
| Two `HallOpen20x20` `LogError`s on every map generation | 🟢 [B-003](../BLOCKERS.md#b-003) **closed by the pivot** 2026-08-03 — `DescentMap`/`RadialStorey` place no `HallOpen20x20` at all. The complaint underneath it survives one level up: a generator that prints `FAIL` on the happy path still means "the log is clean" cannot be a gate |
| `sight-break-spacing` refuses to write the map | 🟢 [B-007](../BLOCKERS.md#b-007) **closed 2026-08-10** — see §3.6 |

---

## 5. Where the existing docs are already stale

Not a criticism — they are dated snapshots and they say so. But **re-measure before
quoting**. Re-derived 2026-08-12, re-checked at HEAD `017b489`; every "Actually" below was run or read
that day.

| Claim | Where | Actually |
|---|---|---|
| any figure from the balance simulator — "매치 7.2 min", "심야 33.6 %", "완전 승리 11.2 %", `weight-mul-light` sweeps | CI.md §0.2, TESTING.md, older wiki pages | **void, not stale.** `core/HorrorGame.Sim` was deleted at `e8c67ae` and `core/` now holds only `HorrorGame.Core`, `HorrorGame.Core.Tests` and the solution. There is no `horrorsim` and no replacement |
| "`Assets/Tests/EditMode/` and `Assets/Tests/PlayMode/` are still empty" | CI.md §4.2 | **6 EditMode and 27 PlayMode test files**, across **10 test assemblies**, counted on disk |
| "23 test files … 2 EditMode and 21 PlayMode at `a3e268e`" | `.github/workflows/unity.yml` | **33 files — 6 EditMode, 27 PlayMode** |
| "Nothing that needs the Unity editor has ever executed" | CI.md §5 | still true of *CI* — `unity.yml` has never run. Not true of this machine |
| `floor=512` | `.github/workflows/ci.yml` | 🟢 **fixed** in `1ceb636` — `floor=357`, matching the suite. It had been red on every push for nine days and ~45 commits |
| "the project records no revision hash, so it cannot be read" | `.github/workflows/unity.yml` | `ProjectVersion.txt` **does** record one: `6000.3.21f1 (c02631ffc030)` |
| "`ClueMinReadableLightQuality`라는 이름으로 남아 있고" / §16-3's rename task | game-design.md §03, §16-3 | **already done, and better than asked** — the constant is `GameConstants.MinSafeLightQuality` = `0.20f` |
| "map validates against all sixteen §12 rules" | `MapSketch.cs:1101` (a source comment) | **fourteen** |
| audio separation "2.10× / 1.389× / 32.5 dB" | wherever it survives | re-measured 2026-08-12 on the eight-surface alphabet: dry worst pair **water vs gravel 1.44×**; at 25 m through a wall **metal vs gravel 1.137×**; gravel is **17.8 dB** quieter than concrete at low-pass 600 Hz |
| "주자 테스트 7/10, Balanced" / "164/164 escapable" | anywhere | **10/10 TooEasy, 680/680 escapable** on the eight-storey building — and [F-013](../BALANCE-FINDINGS.md#f-013) retires the 5–7 band as a co-op-era instrument |
| "the player's first-person hands are done" / "the van repaint is done" | anywhere | the hands landed in `b92ae78`; **the 차량 is deleted with §08**, so the repaint is moot — ART.md §7.12, §7.13 |
| "`dist/windows-x64/MONO-FALLBACK-DO-NOT-SHIP.txt`" | STATUS.md §2.5 | **not on disk today.** The folder was rewritten on 2026-08-10 by a `-buildConfig development` run, which is Mono *on purpose* and writes no marker. Read `dist/windows-x64/build-report.txt` — it says `shippable on Steam: no` for a different reason |

**The one that cost most, and it is a rule rather than a row.** The old "matches finish in
2.5 min" was not stale prose — it was a live measurement of the wrong object, and it sat
in a §14 playtest overlay where a tester would read it as fact. **A number in a string
literal is a copy; grep for it when the thing it copies moves.** The screen that held it is
gone with the co-op UI, and the lesson is why this page now names a file and a line number
for every row above.

---

## 6. What is verified, what is built-but-unverified, and what is missing

Short form; [STATUS.md §1, §2 and §3](../STATUS.md) are the authority — §1 is what works
with the evidence, §2 what does not, §3 which gates were re-run and which are carried.

- **Verified:** rules core (357/357, run 2026-08-12), the solution build, Unity compile,
  the chase, the solo scene build with §05's animation wiring, NavMesh connectivity per
  storey, §12 validation under one named waiver, asset import settings, the audio
  alphabet.
- **Built, never exercised:** the Steam upload path, and a twenty-player field — every
  networking figure on record is two peers or four (`LocalTwoInstance.DefaultFieldSize`),
  and §16-1 names 20인 동시 접속 as the project's top open risk for exactly that reason.
- **Player builds — check `dist/last-build-summary.txt`, do not quote a doc.** It records
  only the *most recent* build, whatever platform that was; today it is a macOS
  Development Mono player from 2026-08-10. The Windows folder is a Development Mono build
  from the same day, and Windows is what §13 ships to. **The marker to look for is
  `MONO-FALLBACK-DO-NOT-SHIP.txt`** (`BuildPipelineBackend.FallbackMarkerFileName`), which
  a *release* build writes when it falls back: macOS cannot cross-compile IL2CPP for
  Windows, and a Mono player ships plain managed assemblies that decompile in seconds —
  which hands out the host-authoritative race logic §13 depends on. Never ship a build
  that printed that warning; pass `-buildRequireIl2cpp` to make it a hard failure, and
  produce the real one on a Windows machine or `.github/workflows/unity.yml`.
  [B-015](../BLOCKERS.md#b-015) is the entry.
- **Missing:** every §14 verification question. All five are askable today and none is
  answered. Q3 — 「관문에서 붐비는 것이 재밌는가」 — needs four people at once and is the
  one the pivot added; §14 says it decides whether this game works at twenty.

> 🔴 **History.** This section used to say "three of the five roles" had never been
> exercised in a real match, and that Q3 could not be asked until F-006 was fixed. Both
> statements are about the four-player co-operative game: §04's five roles are deleted,
> and §14's Q3 is no longer 「지금 나갈까?」 but 「관문에서 붐비는 것이 재밌는가」. The
> *habit* they encoded survives untouched — say which things are built and which are
> exercised, and never let "it compiles" stand in for "somebody played it."
