# How to test this game

**Re-dated against commit `4ab204f`, 2026-08-12.** This page opened at `a3e268e`
(2026-08-03) for nine days and ~45 commits, and in that time the pivot deleted the
four-player co-operative game out from under half of it. What that cost this document,
so you can judge the rest of it:

- **The core suite is 357 tests, not 512.** `e8c67ae` deleted the co-op game and its
  tests. The old number is in no sense a regression, and it is still wired into
  `.github/workflows/ci.yml` as a floor — see §1.
- **The balance simulator does not exist.** `core/HorrorGame.Sim` was deleted whole with
  the game it modelled. §9 is now its tombstone and every command that named it is gone.
- **§04's five roles and §08's economy are gone**, so a rationale on this page that
  names the 청음사 or the 상점 has been re-founded on what is true now, or marked as
  history. Nothing that names a deleted system was deleted merely for naming it.

Three kinds of provenance appear below, and they are not interchangeable — the
convention is [STATUS.md](STATUS.md)'s:

- **measured 2026-08-12** — re-run while re-dating this page. All of these are `dotnet`,
  shell, or a read of a file on disk. **The author of this pass did not run Unity and
  cannot re-run it**, so nothing Unity below carries that label.
- **carried, dated** — an older measurement quoted with the date it was taken, because
  nothing since has re-taken it. Treat the date as part of the number. Most of the Unity
  half of this page is this, and the newest readings are the owner's 2026-08-10 gate,
  quoted through [STATUS.md](STATUS.md).
- **history** — a superseded reading kept because it explains a number or a rule, marked
  🔴/🟢 in this project's usual way. Read it as the reason, never as the current value.

Where a figure comes from a log, the log is named. If a command does not reproduce for
you, that is a bug in the project or in this document — say so rather than working
around it.

This document's whole job is that every claim in [STATUS.md](STATUS.md) can be
re-measured by someone with no memory of the session that produced it.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
P=/Users/doogi/horror-game/unity/HorrorGame
cd /Users/doogi/horror-game
```

> **One Unity process may hold the project lock.** Close the editor before any batch
> command below, and run them one at a time. `dotnet` does **not** take that lock, so
> the .NET half can run in parallel with somebody else's Unity work.

---

## Read this before trusting any green

Seven ways this project has produced a false green, all of them real, all of them
recorded. They are the reason each section below says what to check rather than what to
run. Several happened to the co-operative game and name its sections; the traps are
properties of the tooling, not of that game, and every one of them can still fire today.

| Trap | What it looked like | The rule |
|---|---|---|
| `-quit` on a test run | exit 0, no results written, looks green | **Never `-quit` a `-runTests` run.** The runner is async and exits from its own callback |
| `-quit` on a build | a failed build reporting success | **Never `-quit` a build.** `BuildFromCommandLine` owns the exit code |
| Exit code instead of results | a run that died early has zero errors in its log | Read the XML / the report, not `$?` |
| A green suite over a broken build | Unity clean, 560 tests passing, `dotnet build` failing on 2 errors for four hours ([B-006](BLOCKERS.md#b-006), [B-013](BLOCKERS.md#b-013)) | §1 and §2 are different questions. Run both |
| A test that calls the method under the key | §08's pick-up key broken in the build with 575 tests green | Drive the input, and assert the input arrived |
| A gate whose floor describes a deleted game | `ci.yml` asserts `passed >= 512`; the suite is **357** since `e8c67ae` deleted the co-op tests. A required job red on arithmetic about a game that no longer exists | A count in a gate is a claim with a date on it. Delete tests and the floor moves in the same commit |
| A `grep` that cannot match what it is counting | `m_Name: MonsterSpawn` found 1 spawn in a scene holding 8 — Unity escapes Korean as `\uXXXX` and then **double-quotes the whole value**, so every name but the pure-ASCII one was invisible | Count with a parser, not a prefix. `re.findall(r"m_Name:\s*(.+)")` and strip the quotes. A pattern that finds *some* of a thing looks exactly like a thing that is mostly absent |

And one more, which is this project's own signature failure: **a gate that describes a
different artefact than the one you are about to ship.** It has now happened four times
and every occurrence was found by comparing a tool's output against the artefact rather
than against the tool's last output:

- [B-009](BLOCKERS.md#b-009) — three days of auditing a NavMesh that was not the one just
  baked.
- [B-012](BLOCKERS.md#b-012) — the balance simulator measuring the co-op building while
  the game shipped the descent. **Closed by deletion**: `core/HorrorGame.Sim` no longer
  exists (measured 2026-08-12: 0 tracked files, absent from `core/HorrorGame.sln`,
  absent from `core/` on disk). §9 below is its tombstone. BLOCKERS.md still carries the
  entry as 🔴 open and has not been re-judged since the deletion.
- The required CI job then spent six days and 37 commits running that deleted project,
  which is the same failure one layer out — a gate measuring nothing at all
  (`471ffab`, §2 below).
- `MapSceneGenerator.GenerateFromCommandLine` is **layout only** — no dressing, no
  decals, no glows, no bulbs — and art numbers were twice measured on its output and
  quoted as the game's. §5 below is how you check that the thing being measured is the
  thing on disk.

---

## The one command to run constantly

```bash
dotnet test /Users/doogi/horror-game/core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

Measured 2026-08-12:

```
통과!  - 실패:     0, 통과:   357, 건너뜀:     0, 전체:   357, 기간: 1 m 32 s - HorrorGame.Core.Tests.dll (net9.0)
```

**357 tests in about a minute and a half, and Unity never opens.** 0 skipped, so the
count is not inflated by disabled cases. Every tuned number and every rule lives here —
§05's speed multipliers, §06's aggro and state machine, §07's threat curve, §12's map
rules, §02's finish condition.

> **Both halves of the old headline — "512 tests in under half a second" — were wrong,
> and they were wrong for unrelated reasons. Neither is a regression.**
>
> **The count.** `e8c67ae` deleted the co-operative game, and its tests went with it:
> the clue chain, the economy, the shop, the wallet, the van, the battery. 357 is what
> is left when a suite stops testing a game that is not being made.
>
> **The duration.** The suite has not been sub-second for some time, and the reason is
> visible in its own console output: the run builds whole eight-storey buildings, over
> and over, several seeds deep. From the 2026-08-12 run above —
> `[MapSketch] [SceneGen] 막힌 길 152개 전부 막혔다 …` and
> `괴물 시작점: 8 declared, 8 MonsterSpawn markers written` — repeated per seed. §12's
> rules are geometric and `sight-break-spacing` compares bends against each other, so
> the cost is superlinear in the building. A minute and a half is what a rule suite
> costs once it measures the shipped building instead of a model of it. It is worth
> paying, and it is also long enough that "run it before every commit" is now a real
> decision rather than a free one.

This works because the rules core has no engine dependency: the same `.cs` files Unity
compiles are pulled into a .NET project by a glob, so there is one copy of the truth,
checked two ways. `FoundationTests.CoreSources_DoNotReferenceUnityEngine` fails the build
if anyone breaks that arrangement.

Run it before every commit. If it is green, the game's rules are intact.

> **The count has been wrong in this file twice, in opposite directions. Re-read it from
> the run rather than from here.** 🔴 **476** was the 2026-08-01 reading, before the
> pivot; 🔴 **512** was `a3e268e`, after the pivot but before the deletion landed;
> 🟢 **357** is 2026-08-12. A test count is a measurement with a date on it, and this
> page has now twice been the last place still quoting an old one.

> 🔴 **`ci.yml` has not been told.** `.github/workflows/ci.yml` still runs an
> `Assert the suite actually ran` step with `floor=512` (read on disk 2026-08-12,
> line 102), so the required `core tests (dotnet)` job fails on 357 passing tests. The
> step is right to exist — a `--filter` that matches nothing exits 0, so the exit code
> cannot tell "512 passed" from "none ran" — and its own comment says the floor moves in
> the same commit that deletes tests. That commit has not happened. **This is a
> repository file, not a docs file; it is reported here and not fixed here.**

---

## The full sweep, in the order worth running

### 1 · Rules — 357 tests

```bash
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

Expect `실패: 0, 통과: 357, 건너뜀: 0`. Measured 2026-08-12, `기간: 1 m 32 s`.

### 2 · Everything compiles outside Unity

```bash
dotnet clean core/HorrorGame.sln -c Release      # or the warning count below is a lie
dotnet build core/HorrorGame.sln -c Release
```

```
빌드했습니다.
    오류 0개
```

**Do not skip this because §1 was green**, even though the reason it was written no
longer applies. The solution is two projects now — `HorrorGame.Core` and
`HorrorGame.Core.Tests`, read off `core/HorrorGame.sln` on 2026-08-12 — so there is no
longer a third project the tests do not reference. What survives is the narrower point
that a *test* run and a *build* are different questions: `dotnet test` builds only what
the test project references, and the solution is the only thing that compiles
everything.

> 🔴 **History, and the reason this section exists at all. Both occurrences are about a
> project that has since been deleted, and the lesson is not.** On 2026-08-01 the
> simulator's project did not compile a file `MapQualityReport` depends on
> ([B-006](BLOCKERS.md#b-006)). On 2026-08-03 `ChamberDockProbe.cs` landed in
> `Editor/SceneGen/`, matched the glob that pulls the engine-free map sources into
> **both** headless projects, and did `using UnityEditor` — so `dotnet test` could not
> reach a single one of its then-512 tests for three commits, one of which is titled
> *"and the suite is green"* ([B-013](BLOCKERS.md#b-013)). The glob that caused the
> second one still pulls `Assets/Scripts/Core/**` into `HorrorGame.Core.csproj`; a
> `using UnityEditor` in the wrong folder can still do it.

> **What `ci.yml`'s `core tests (dotnet)` job actually runs, read on disk 2026-08-12.**
> Four steps, not two: `dotnet test`; the `floor=512` assertion above; `dotnet build`
> of the whole solution in Release; and then **§12's map validator**, as
> `dotnet test --filter "FullyQualifiedName~MapTests.Descent_"`, asserting that the
> filter selected at least four tests. That last step used to be
> `dotnet run --project core/HorrorGame.Sim -- validate` and was failing on every push
> for six days and 37 commits after the simulator was deleted — a required job red on a
> command that could never succeed again. `471ffab` rewrote it around the four tests
> that own §12 by name (`Descent_EveryOtherSection12Rule_StillPasses`,
> `Descent_MeetsSection12sSightBreakSpacing`, `Descent_CentrePath_…`,
> `Descent_IsDeterministic_…`), because `MapValidator` survived the deletion even though
> the simulator did not. **The job is still red, on the `floor=512` step instead.**

> **`dotnet clean` first, or the warning count is meaningless.** An incremental build of
> an already-built solution recompiles nothing and therefore re-emits no warnings; it
> prints `경고 0개` and takes 1.5 s instead of 4.3 s. The **error** count is trustworthy
> either way.

### 3 · Unity compiles

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P -logFile /tmp/u.log
grep -cE '^Assets/.*error CS' /tmp/u.log
```

Prints `0`. Anything else is the error count — read `/tmp/u.log`. `-quit` is correct
*here*, and only here: this run has no async work to wait for.

### 4 · PlayMode — carried, dated

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -runTests -testPlatform PlayMode \
   -testResults /tmp/playmode.xml -logFile /tmp/playmode.log
python3 -c "import xml.etree.ElementTree as ET,sys; r=ET.parse(sys.argv[1]).getroot(); \
  print(r.get('total'), r.get('passed'), r.get('failed'), r.get('skipped'), r.get('result'))" \
  /tmp/playmode.xml
```

**Carried, dated: 2026-08-08 16:51:56Z**, `dist/test-results/playmode-results.xml` (*that
file was deleted by the 08-11 cleanup — the numbers are carried from the run, the XML is
not on disk to re-read*) — the newest full sweep, quoted through
[STATUS.md](STATUS.md) §2.3:

```
124 121 3 0 Failed(Child)
```

**The three reds are all `Voice.VoiceSocketTests`** and they are environment-gated
rather than a code signal — see "What is not tested at all" below, which is the section
that explains why a green there means somebody was making noise near the machine.
**Anything other than exactly those three is a regression.** List the failures rather
than counting them:

```bash
python3 -c "
import xml.etree.ElementTree as ET,sys
for tc in ET.parse(sys.argv[1]).getroot().iter('test-case'):
    if tc.get('result')!='Passed': print(tc.get('fullname'), tc.get('result'))
" /tmp/playmode.xml
```

> **Never add `-quit`.** Unity's test runner is asynchronous and `-quit` shuts the editor
> down before results are written. The run then reports nothing, exits 0, and looks
> green.

> 🟢 **[B-011](BLOCKERS.md#b-011) is not what this section used to say it was.** This
> page's previous edition quoted `113 112 1 0` from 2026-08-03 and named
> `LobbyEntryWiringTests.HostingFromTheMenuReachesTheMazeWithARunnerStillAlive` as the
> one expected red, failing on `[Race] §01 출발선이 완성되지 않았다`. In the
> 2026-08-08 sweep that fixture passes and the only reds are the three voice tests.
> BLOCKERS.md still carries B-011 as 🔴 open with "the only red in 113 PlayMode cases"
> in its status line; **nothing here re-ran it, so this is an observation about a newer
> sweep and not a closure.**

🔴 **The fixture table below is 2026-08-03's 113 and has not been re-taken.** It is 11
cases short of the 124 the newest sweep counted, and it lists no `GunTests` and no
`VoiceSocketTests` — 총, 깜짝 and 근접 음성 all landed after it. Read it as a map of
what the suite covers, never as a count; the rows naming a red are that run's reds and
not today's.

| Fixture | n | What it is for |
|---|:--:|---|
| `Racing.RaceDirectorTests` | 17 | §02 — descent count, ranking, the finish |
| `PlayerRig.PlayerTests` | 16 | §05 movement |
| `Net.NetTests` | 11 | §13 host authority, replication, what a client may not learn |
| `PlayerRig.PlayerViewMotionTests` | 11 | §05 view motion |
| `Audio.AudioSceneTests` | 10 | the audio rig exists in the shipped scene |
| `PlayerRig.PlayerFirstPersonViewTests` | 7 | §05's 손 — but see [B-017](BLOCKERS.md#b-017) |
| `Ghosts.GhostSessionTests` | 6 | §09 elimination |
| `PlayerRig.PlayerStanceTests` | 6 | crouch / hop |
| `Presence.PresenceSessionTests` | 5 | §10 그늘 — **still in no scene and no prefab** |
| `Monster.MonsterChaseTests` | 4 | §06 — §14's Q1, below |
| `Net.LobbyEntryWiringTests` | 3 | the shipped 호스트 menu path — **1 red** |
| `Net.NetHumanRunnerTests` | 3 | a human's input crossing a real socket |
| `Net.NetRunnerTests` | 3 | other people's bodies |
| `Net.NetSocketTests` | 2 | two peers, real transport |
| `Interaction.InteractionPickupTests` | 2 | the real key, not `OnPressed` |
| `Racing.DescentPlaythroughTests` | 1 | **B1 → B8, the whole race** |
| `Interaction.InteractionDropTests` | 1 | the real key |
| `Match.MonsterKillTests` | 1 | standing in front of the creature kills you |
| `UI.UiFlowTests` | 1 | 시작 reaches the match scene |

Single fixtures with `-testFilter`:

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P -runTests -testPlatform PlayMode \
   -testFilter "DescentPlaythroughTests" -testResults /tmp/pt.xml -logFile /tmp/pt.log
```

**The two to know.**

`DescentPlaythroughTests.A_runner_can_descend_from_the_rim_of_B1_to_the_middle_of_B8` is
§01 end to end. It prints a table and the table is the evidence
(`/tmp/r6_all.log:2870`):

```
층   외곽→중심                        투하구
B1   PathComplete 49.8 m              ↓ B2  외곽 25.0 m
…                                     (eight legs, seven chutes)
B8   PathComplete 61.6 m              도착점
§02 Descended 7회 / 필요 7회 · 좌석 0의 층 B8 · 승자 0 · 완주 1명 · 경과 89초
```

`PathComplete` on every leg is the load-bearing word: a `PathPartial` is how a broken
NavMesh looks like bad AI rather than like an error.

`MonsterChaseTests` is §14's Q1 turned into something a machine can answer
(`/tmp/r6_all.log`):

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
  route 71.0 m of NavMesh path · reached 14.54 s · closing speed 4.81 m/s against §06's 4.8
[ChaseTest] §12 ① S자 통로 — released 5.50 s after aggro, at 12.0 m, caught no
[ChaseTest] §12 단일 모퉁이 — caught 12.54 s
  monster speed 4.80 m/s against §06's 4.8 · gap opened at 0.80 m/s against §06's 0.8
```

Those last two numbers are §06's central design claim —
「괴물이 달리기보다 0.3만 빠른 것이 핵심이다」 — measured on real geometry to 1 %. Watch
them: if either moves, a speed constant moved with it.

### 5 · Generating the map — and the three gates it runs

This is the most important command in this document, because it is the only one that
produces the artefact everything else measures. **It is `MapPipeline`, and using the
other entry point is a documented way to measure a building that is not the game.**

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.MapPipeline.RegenerateFromCommandLine \
   -logFile /tmp/gen.log; echo "exit=$?"
```

> 🔴 **This page used to name
> `MapSceneGenerator.GenerateFromCommandLine` here, and that is the layout pass alone.**
> `MapPipeline`'s own header (read 2026-08-12) fixes the order **layout → dressing →
> atmosphere** precisely because the three passes were written independently and each
> one saves the scene, so running them out of order silently discards work rather than
> failing. Generate on its own writes a building with **no dressing, no decals, no
> glows and no lit bulbs**, and leaves Unity's default daytime skybox in
> `RenderSettings` — which is the brightest thing in the frame, so every smooth floor
> comes out glowing. Nothing errors. [ART.md](ART.md) §1 records art numbers being
> measured on that output and quoted as the game's **twice**, the second time after the
> first had already been written up. If you want layout only, say so out loud; if you
> want the game, run `MapPipeline`.

Expect **exit 0** and, in order in the log: §12's checklist, the 주자 테스트 grade, the
census, `[NavMeshAudit] PASS`, `[PlayerReach] PASS`, and a commit line with a stamp.

**Carried, dated: 2026-08-10**, the owner's gate (`biggate/4_map.log`), quoted through
[STATUS.md](STATUS.md) §1.1:

```
Scene contents: 1144 kit pieces, 0 props, 241 markers; graph has 680 places, 766
passages, 87 순환로, 152 막힌 길.
footprint 57.5 m × 57.5 m
B1 하역장=Concrete · B2 기록보관소=Wood · B3 기계실=Metal · B4 저탄장=Gravel
B5 저수조=Tile · B6 병동=Carpet · B7 수몰층=Water · B8 굴착층=Earth
```

**680 places, not 720.** The 2026-08-10 re-lay changed the geometry, so every per-place
figure moved with it and **any figure quoting 720 is about a building that no longer
exists** — including the one in the historical block below. `0 props` is the layout
stage reporting before the dressing pass runs, not an empty building.

🔴 **The block that follows is `/tmp/r6_gen.log`, 2026-08-03, and every count in it is
superseded.** It is kept because the three notes under it explain how to read the
*shape* of this output, and those notes are still how you read it:

```
§12 map validation — 하강 — 요양원 지하 8층: FAIL      ← 14 of 17 ok; see below
§12 주자 테스트: 10/10 (100%), TooEasy                 ← the project's largest problem
§12 실전 검증: 720/720 escapable (100%), against §12's 50%~70% band
시야 차단 지점 간격: 496 corners, mean 3.5 m, 0 inside §12's 15~25 m band
Scene contents: 1160 kit pieces, 8 props, 824 markers;
  graph has 720 places, 814 passages, 95 순환로, 152 막힌 길

[NavMeshAudit] PASS
  markers 220 · pairs 3482 · complete 3482 (100.0 %, need 98 %) · partial 0 · invalid 0
  islands 8  ← the surface is in pieces
  worst snap 0.25 m
  monster reach 212/212 markers reachable from a MonsterSpawn on the SAME storey,
                over 8 of 8 storeys (§06)

[PlayerReach] PASS
  runner reach   storeys 8/8 · starts 36/36 reach the finish · finish REACHED
  one-way routes 14/14 투하구 usable · no 계단
  chute-blind    0/36 starts reach the finish with the one-way routes deleted
  standing places 224236 · 후보 지점 24/24 · 전리품 152/152 · player spawns 36/36
  tallest climb 0.045 m · tightest headroom 2.53 m · worst reach gap 0.33 m
```

**Four things about that output that will mislead you if nobody says them.** All four
are about how to read the report, so all four still apply to a run you do today.

- **`islands 8  ← the surface is in pieces` is wrong on the happy path.** A tower whose
  only vertical links are one-way falls is eight surfaces by construction. The audit
  judges a storey now, not a building; only the arrow text was left behind
  ([B-014](BLOCKERS.md#b-014)).
- **`§12 … : FAIL` is not the whole story either**, and what is waived has changed.
  Of the three failures above, `zone-diagonal` and `open-adjacent-to-maze` were rules
  written for the deleted co-operative game, and `sight-break-spacing` was a genuine
  defect. 🟢 **[B-007](BLOCKERS.md#b-007) closed on 2026-08-10** — `RadialStorey` was
  re-laid so the bands jog outward, the deepest continuous cover run went 95.0 m → 12.5 m
  against a 14.4 m cap, and the rule now passes on all eight roster seeds.
  `MapSceneGenerator.KnownFailingRules` holds only `RuleCentrePath` today, for
  [B-019](BLOCKERS.md#b-019). So the generator still writes the map under a named waiver
  and still says so every time — but it now names **one** rule, not two, and the older
  wording quoted below names B-007 because that was the rule of the day:

  ```
  [SceneGen] §12 is failing a rule that is already recorded as a known defect, so the map
  was written anyway. This is not permission to ignore it — see docs/BLOCKERS.md B-007.
  ```

  If a **different** rule fails, the generator stops and writes nothing. That is correct.
  Do not add to `MapSceneGenerator.KnownFailingRules` to get past a new failure.
- **`chute-blind 0/36` is a good number.** It means deleting the one-way routes leaves
  nothing able to reach the finish — so the gate cannot pass by accident through some
  route nobody meant to leave in, and it fails the moment one 투하구 breaks.
- **`전리품 152/152` in the `[PlayerReach]` block is not loot, and there is no loot.**
  §08's 전리품 were deleted at `e8c67ae`. That line counted one marker per 막힌 길 —
  152 of them — which was quietly the only proof that every dead end of every floor is
  reachable, so the markers were kept and renamed: they are `ReachProbe` markers now,
  emitted structurally from the leaf list rather than sampled. **The number was doing a
  second job the whole time, which is why deleting it would have cost a gate.** If a run
  today still prints the old label, that is a stale string and not a returning economy.

#### 5a · Prove the scene on disk is the one that was audited

Every generation stamps the scene **and** the bake with the same string, and prints it:

```
[SceneGen] gen-20260803-080103-seed20260802: … BOTH written by this run — 7127 vertices,
232,304 bytes; the same stamp is on 'SceneGen_gen-20260803-080103-seed20260802' in the
scene and in …/NavMesh_Map_FirstSketch.asset.meta
```

Check it without Unity, which is the entire point:

```bash
grep -o 'SceneGen_gen-[0-9T-]*-seed[0-9]*' \
  unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity | head -1
grep -o 'gen-[0-9T-]*-seed[0-9]*' \
  unity/HorrorGame/Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta | head -1
```

**Measured 2026-08-12, and only the first of those two commands still answers.**

```
SceneGen_gen-20260810-005411-seed20260802     ← the scene, exactly one occurrence
                                              ← the bake meta: no output at all
```

> 🔴 **The bake half of the stamp is gone, and its absence looks like agreement.** The
> `.meta` beside `NavMesh_Map_FirstSketch.asset` is nine lines with an **empty
> `userData:`** — read on disk 2026-08-12 — which is where `MapSceneGenerator` writes
> the generation id. So the second `grep` prints nothing, and nothing is not a
> disagreement you would notice while skimming. The likely cause is benign and worth
> knowing: the pipeline's dressing pass re-bakes the NavMesh *after* the layout pass
> stamped it, and Unity rewrites the `.meta` on a re-bake. **What survives is the scene
> half**, which identifies which generation the level came from; what is lost is the
> half that ties a *bake* to it — and that is exactly the tie
> [B-009](BLOCKERS.md#b-009) was closed on, three days of auditing a stale surface.
> Until the pipeline re-stamps after its last bake, treat a green NavMesh audit as
> evidence about a surface whose provenance you cannot check from disk.
>
> **Until then, compare against the scene and use the whole string.** A bare
> `grep -c` for a stamp you copied out of an older document answers `0` and reads as
> "the scene is stale" when the truth is that your stamp is. [STATUS.md](STATUS.md) §4
> currently quotes `gen-20260810-000424-seed20260802`; the scene at HEAD carries
> `gen-20260810-005411-seed20260802`, one generation later, and STATUS.md's own
> reproduction command therefore prints `0` where it says it prints `1`.

If the two disagree, or if either carries `-forced`, the measurements in STATUS.md do
not describe what is on disk.

#### 5b · The report without writing anything

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SceneGen.MapSceneGenerator.ReportQualityMenu \
   -logFile /tmp/quality.log
```

Same checklist and grade, no disk writes. In the editor:
`HorrorGame ▸ Scene Gen ▸ Report Map Quality`.

#### 5c · The two audits on their own

Both run inside §5, and both can be pointed at any scene:

```bash
$U -batchmode -quit -nographics -projectPath $P \
   -executeMethod HorrorGame.EditorTools.NavMeshAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/nav.log

$U -batchmode -quit -nographics -projectPath $P \
   -executeMethod HorrorGame.EditorTools.PlayerReachAudit.AuditBatch \
   -auditScene Assets/Scenes/Map_FirstSketch.unity -logFile /tmp/reach.log
```

**There are two bodies in this game and they are not the same size.** The NavMesh agent
climbs `agentClimb` 0.75 m and stands 2.00 m; the player's `CharacterController` climbs
`stepOffset` 0.40 m and stands 1.75 m. Every surface between those numbers is a route
only the antagonist can take, and the NavMesh audit cannot see one — Recast erodes a
walkable *region*, it carries no body. This once produced `1830/1830 pairs complete,
100 %, 1 island` for a building the player could not leave the ground floor of
([B-008](BLOCKERS.md#b-008)). **Run both. Neither substitutes for the other.**

`PlayerReachAudit` reads the capsule off `PlayerFeelHarnessMenu.BuildRig()` rather than
restating it, so it follows the controller if anyone retunes it, and it reports headroom,
which fails identically and just as silently: a beam at 1.60 m stops a 1.75 m capsule dead
and bakes a perfectly good agent surface underneath itself.

> **Do not fix a `PlayerReach` failure by raising `stepOffset`.** §12's escape geometry is
> derived from what a player *cannot* climb. Fix the geometry — `tools/blender/gen_mapkit.py`
> holds the kit to the player's capsule as well as the agent's and fails at export.

### 6 · Audio — §12's floor-material alphabet

One command does both halves, and it is the same one CI runs:

```bash
bash tools/ci/verify_audio.sh --json /tmp/audit.json; echo "exit=$?"
```

**Measured 2026-08-12** (the audit's verdict, then the gate's):

```
§12 Listener alphabet: SUPPORTED — worst surface pair water vs gravel at 1.44x (need >= 1.4x)
worst within a single actor: 1.41x
at 25m through a wall it does NOT hold: worst pair metal vs gravel at 1.137x
HUD vs ears: 4 inverted pair(s) — gravel/concrete, gravel/earth, water/wood, tile/concrete
clips: 164   loops checked: 16   blocking defects: 2   warnings: 5
RESULT: FAIL                                   ← verify_audio.py exits 1

  audit result:        FAIL
  blocking defects:    2
  accepted (baseline): 2
  KNOWN  [consistency] gravel vs concrete  → F-002
  KNOWN  [consistency] gravel vs earth     → F-002
  RESULT: PASS                                 ← the CI gate, exit 0
```

`verify_audio.py` exiting 1 is **expected**: F-002 is a known, baselined design
contradiction, and the two blocking defects it reports are both in
`tools/ci/audio_baseline.json`. **The gate passes.** It fails both ways on purpose — a
blocking defect absent from the baseline is a regression, and a baselined defect that has
stopped reproducing means the finding was fixed and the write-up has to move in the same
commit.

> 🟢 **"This job is red on `main`" is no longer true, and the previous edition's
> explanation of the redness was right about the cause.** At `a3e268e` the gate reported
> `2 unbaselined blocking defect(s)` — `gravel vs earth` and `gravel vs carpet` — because
> Carpet, Water and Earth had been added for B6/B7/B8 with clarity constants but without
> the occlusion analysis, so F-002's shape had grown past its baseline. The baseline has
> since been extended to cover it. What is baselined today is `gravel vs concrete` and
> `gravel vs earth`; `gravel vs carpet` no longer reproduces as blocking, and
> `carpet vs concrete` is now an INFO note that the two are 1.04× apart on ring time and
> rest on centroid alone. **The count moving is the signal this gate exists to give**, so
> re-read it from a run rather than from here.

**Why this is a gameplay invariant and not an audio nicety, now that the role it was
written for is gone.** §04's 청음사 read the creature's position off the floor it was
walking on, and this check was built to protect that one ability. **The ability is
deleted and the requirement outlived it**, because §12 charges the whole field for it
instead: 「소리 → 바닥 재질이 지도다」. Every runner now navigates by ear, so a pair of
surfaces that sound alike is a pair of storeys that read alike to twenty people rather
than to one. `GameConstants` says this in its own words — *"the table outlived the
ability because a race still has to answer which floor is worth crossing"* — and the
`Listener*` names in the code are a historical stem, not a live role. Re-run this after
touching any generator, and after touching `GameConstants`: the script parses the
clarity table out of it.

> **The alphabet is eight surfaces now, not five.** `FloorMaterial` (read 2026-08-12)
> is Wood, Tile, Gravel, Concrete, Metal, Water, Earth, Carpet — the last three added
> when the building went to eight storeys. Anything on this page or in
> [ART.md](ART.md) that says "five floor materials" is counting the co-op building's
> five zones.

### 7 · Builds

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine \
   -buildPlatform mac -buildConfig development -logFile /tmp/build.log; echo "exit=$?"
```

`dist/last-build-summary.txt` is rewritten by every run, so it describes the **last**
build and not the best one. Read on disk 2026-08-12:

```
HorrorGame build run — 2026-08-10T14:36:02Z
arguments : … -buildPlatform mac-arm64 -buildConfig development
exit code : 0

  macOS Apple silicon                        Development Mono   OK         2569.62 MB  94s
```

🟢 **A Release IL2CPP player builds on this machine, and the toolchain fault that
blocked it is gone.** Carried, dated 2026-08-07T15:08:59Z, quoted through
[STATUS.md](STATUS.md) §1.10 — that summary has since been overwritten by the
development build above, so `dist/macos-universal/build-report.txt` is where the
evidence lives now:

```
  macOS universal (Apple silicon + Intel)    Release     IL2CPP OK         2194.29 MB  51s
  ships to Steam: 438.16 MB (symbol folders excluded) · managed stripping: Low
```

> 🔴 **This page said "Release does not build on this machine" for nine days after it
> stopped being true, and the diagnosis it carried was correct right up to the moment
> the cause disappeared.** The failure was
> `il2cpp-codegen.h:24:10: fatal error: 'cmath' file not found`, exit 4, in three
> consecutive runs — and it was this host rather than Unity: the damaged
> `/Library/Developer/CommandLineTools/usr/include/c++/v1` held 11 of the 185 C++
> headers the SDK copy has, so clang found a partial directory first and stopped. **That
> directory no longer exists**, so clang now falls through to the intact SDK copy and
> the failure cannot reproduce (STATUS.md §1.10 re-ran the two-line proof on
> 2026-08-10). [B-015](BLOCKERS.md#b-015) still says it fails; both it and the old
> paragraph here are false at HEAD. The two explanations that entry could never separate
> — a broken toolchain versus nobody exporting `CPLUS_INCLUDE_PATH` — are now moot
> rather than resolved, which is a weaker outcome than it looks and is why the
> workaround is kept below.

The two-line proof, and the workaround, both still worth knowing because the failure
mode returns with any Command Line Tools reinstall:

```bash
printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp
clang++ -std=c++17 /tmp/p.cpp -o /tmp/p
ls -d /Library/Developer/CommandLineTools/usr/include/c++/v1                       # gone
ls /Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1 | wc -l  # 185

export CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1
```

**None of this makes the game shippable**, and the reason is §7's table below rather
than the Mac: the product is a Windows player and one has never been built with IL2CPP.

> **Never pass `-quit` to a build.** `BuildFromCommandLine` owns the exit code and calls
> `EditorApplication.Exit` itself; `-quit` overrides a failure with 0.

Exit codes: `0` ok · `1` unexpected · `2` arguments · `3` scenes · `4` build failed ·
`5` IL2CPP required but unavailable · `7` scripts do not compile · `8` module missing.
Output is `dist/<platform>/`, wiped and rewritten each time;
`dist/<platform>/build-report.txt` holds every message off Unity's `BuildReport`, which
the raw log does not carry reliably.

| Target | Development | Release | Shippable on Steam |
|---|---|---|---|
| macOS arm64/universal | Mono, ~20 s | **IL2CPP — failing here** | not until §7's fix |
| Windows x64 | Mono | **Mono only, never IL2CPP** | **no** |

Windows IL2CPP cannot be produced on a Mac at all — IL2CPP transpiles to C++ and calls the
*target's* compiler. A Windows Release build made here falls back to Mono and the pipeline
drops `MONO-FALLBACK-DO-NOT-SHIP.txt` beside it. Steam's audience is mostly Windows, so
shipping needs a Windows machine or the runner in `.github/workflows/unity.yml`, which has
never run for want of a licence — [CI.md](CI.md), [STEAM-RELEASE.md](STEAM-RELEASE.md).

**Mirror's meta-file error is expected in every build** and does not fail it:
`Asset Packages/com.mirrornetworking.mirror/Mirror/Assets has no meta file, but it's in an
immutable folder.` The OpenUPM repack includes Mirror's submodule, whose Unity project root
legitimately has no `.meta`. It cannot be fixed from this repository — `Library/PackageCache`
is immutable and regenerated from the tarball on every resolve.
`BuildPipelineKnownDefects` matches on both the symptom and that exact package path, prints
it, counts it, and lists it under **known third-party defects**. Every other error still
fails the build. This is also why `BuildOptions.StrictMode` is not set: it fails a build
when *any* error was logged and blames the first scene, which made every scene here
unbuildable including the near-empty bootstrap menu.

### 8 · EditMode — it ran, and it is green

```bash
$U -batchmode -nographics -silent-crashes -projectPath $P \
   -runTests -testPlatform EditMode -testResults /tmp/editmode.xml -logFile /tmp/em.log
```

**Carried, dated: 2026-08-08 16:56Z — 95/95**, quoted through [STATUS.md](STATUS.md) §3.
Fourteen commits stale, and it ran, which is what matters: this is the one platform
nobody could say anything about for a week.

> 🟢 **[B-016](BLOCKERS.md#b-016) can be closed, and the doubt it recorded was
> well-founded.** This section used to say the newest EditMode result was
> `/tmp/editmode.xml`, 2026-08-01, 101/101 against the four-player co-operative game —
> so the platform was "either red or green-about-nothing and nobody knew which". Both
> named suspects checked out: `UiTests` is **17 cases rather than 59** (the shop's 39
> were deleted on 2026-08-03), and `SoloMatchLoopTests.cs` **no longer exists at HEAD**
> — confirmed on disk 2026-08-12, the file is absent and `UiTests.cs` is the only
> survivor of the pair.
>
> What replaced them is the more interesting half. There are three EditMode assemblies
> on disk today — `HorrorGame.Tests.EditMode.UI`, `.Audio`, and **`.Pivot`** — and the
> third exists to assert that the co-operative game *stays* deleted:
> `PivotTombstoneTests`, `PivotSceneTombstoneTests`, `PivotAssetTombstoneTests` and a
> shared `PivotVocabulary`. A suite whose job is to fail if 금고 or 상점 comes back is
> the reason a page like this one can be corrected once rather than continuously.

### 9 · The balance simulator — deleted, and there is no replacement

**There is no simulator. Every command this section used to list fails, and should.**

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- map     # exit 1, no such project
```

Measured 2026-08-12: `core/HorrorGame.Sim` has **0 tracked files**, is **absent from
`core/HorrorGame.sln`** (which lists `HorrorGame.Core` and `HorrorGame.Core.Tests` and
nothing else), and is **not on disk under `core/`**. It went at `e8c67ae` with the game
it modelled — every subject it simulated, §03's clue chain and §08's economy and the
loot-value sweep, was deleted the same day. **So there is no balance instrument for the
race, and nothing on order.** That is a real gap and it is worth saying plainly rather
than leaving a section that looks runnable: §16-2's question — how long is a match, and
does the descent stay interesting for eight storeys — has no tool that answers it, and
the only number anyone has is 89 s for a pathfinder that already knew the way.

> 🔴 **[B-012](BLOCKERS.md#b-012) is closed by deletion, and BLOCKERS.md has not been
> told** — the entry still reads 🔴 open, "every simulator figure is void", which is
> true in a way that no longer needs a blocker. Its content is worth keeping as the
> clearest statement this project has of its signature failure. What it recorded: the
> simulator reported `254 places · 285 passages · footprint 50 m × 95 m` while the game
> shipped a building of a different size entirely, because `MapSceneGenerator` called
> `DescentMap.Build` and `SimMap` called `FirstMapSketch.Build` — the retired
> co-operative building, compiled into the same binary. It was
> [F-006](BALANCE-FINDINGS.md#f-006) happening a **second** time, and the fix that was
> supposed to prevent the first — compiling the game's own map sources into the
> simulator so the two could not drift — had worked exactly as designed and prevented
> nothing. **A build-time include guarantees the sources agree; it guarantees nothing
> about which function you call.** Whoever writes the race's balance tool should read
> B-012 before writing a line of it.

**The habit that would have caught both occurrences, and which now belongs to §5:**
read the first five lines of any tool's output — they are the building — before quoting
anything under them. That is the same check as §5a's stamp and the same lesson as the
layout-only entry point at the top of §5.

### 10 · Asset import settings — stale, and worth re-running

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.AssetImportValidator.ValidateAllBatch -logFile /tmp/a.log
```

Last recorded run, 2026-08-01: `166 audio inspected, 0 failing` / `86 models inspected,
0 failing`. **Counted on disk 2026-08-12: 168 WAVs and 75 FBX.**

> 🔴 **The gap this section used to report has closed from the wrong end.** It said
> "209 WAVs and 91 FBX on disk, so at least 43 clips have never been through this
> check." Both counts have since gone **down** — the pivot deleted the co-op game's
> audio and its props — so the arithmetic no longer says anything about coverage in
> either direction. 168 against 166 inspected is not evidence that two clips are
> unchecked; it is two counts of different sets taken eleven days apart. **Re-run the
> validator rather than subtracting.** STATUS.md §3 also records this tool returning
> different verdicts eighteen minutes apart on an unchanged file, so a single green from
> it is not yet a gate.

Not housekeeping. It enforces the Humanoid animation type, a valid `isHuman` Avatar, the
four non-humanoid mount bones (`AssetImportPolicy.PlayerMountBones` — Optimize Game
Objects strips exactly these and the Avatar does not protect them), the 1.750 m
`PlayerHeightMetres`, and the expected clip count. And a positional clip imported as
stereo is not spatialised by Unity at all — one wrong checkbox silently deletes §12's
floor-material cue for every runner, and nothing else in the project notices.

### 11 · The creature is visible at 15 m

```bash
$U -batchmode -quit -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.Gameplay.MonsterEditor.MonsterShot.StageBatch \
   -shotTag stage -logFile /tmp/mon.log
```

**No `-nographics`**, or every frame is black. Last run 2026-08-01: 8 of 8 staged frames
pass (`contrast ≥ 0.015`, `coverage ≥ 0.40`, `peak ≥ 0.040`), 15 m contrast 0.0592.
Not re-run since the map changed, so treat it as describing the creature rather than the
game.

Three numbers per frame, each gating: `contrast` (mean per-pixel luminance separation from
the wall — ≈4 code values, where an observer who has not been told where to look picks a
shape out of a dark field), `coverage` (fraction of the silhouette that differs at all —
separates a creature from a glint), `peak` (95th-percentile change inside the silhouette —
one genuinely legible feature). The silhouette is ground truth, rendered unlit white on
black with fog and grading off; taking "the pixels that changed" as the silhouette would be
circular.

> **Judge every render at native brightness first.** A 3× gain in the viewer is exactly the
> crop that hides an exposure defect, and reading renders that way cost this project five
> review rounds.

---

## What is not tested at all

Stated here because a test inventory that only lists what exists is the most misleading
document a project can have.

- **A human playing.** Not one match, by anybody, ever.
- **Twenty humans.** `TwentyRunnersGetTwentyDistinctPlacesOnB1sRim` and
  `TheTwentiethRunnerIsAcceptedAndTheTwentyFirstIsRefused` stand up twenty sockets on
  `127.0.0.1` in one editor process. Latency, loss, NAT, Steam relay and twenty machines
  are untested.
- **How long a match takes.** §01 wants 12–20 minutes. The only number that exists is 89 s
  for a pathfinder that already knows the way.
- **§10's 그늘.** 25 core tests and 5 PlayMode tests pass, and `PresenceDirector` /
  `PresenceSubject` are in no scene and no prefab (checked by script GUID, not by name) and
  `MatchDirector` never mentions `Presence`. A green suite is not evidence the entity
  exists in a match.
- **No balance instrument at all**, since `HorrorGame.Sim` was deleted — §9. Nothing
  answers §16-2.
- **Everything in `docs/store/`.** Several frames still contain the 차량 shop the pivot
  deleted. [ART.md](ART.md) is **no longer** in this bullet: its zone measurements were
  re-taken on the eight-storey building on 2026-08-09 (tag `fullpipe3`) and it now says
  which of its own figures describe the co-op building. Its **prop** photography is
  still of deleted objects, and it says so.
- **근접 음성, on any machine that is not listening to a live microphone.** Three
  `VoiceSocketTests` — `AVoiceCrossesARealSocketAndArrivesAttenuatedByTheRule`,
  `AWallBetweenThemCostsTheRulesOcclusionAndNotTheEnginesRolloff` and
  `SpeakingIsReportedToTheCreatureEvenWithNobodyInRange` — drive a real socket with a real
  `VoiceCapture`, so they need the microphone to be producing *sound*, not merely to open.
  On 2026-08-08 the log says `[Voice] Microphone line at 16000 Hz` and all three fail with
  "the relay forwarded nothing", which is what silence looks like from the far end. **This
  is not a pass/fail signal about the voice code and must not be read as one in either
  direction** — green means somebody was making noise near the machine.

### 🟢 The floating PlayMode failure — closed 2026-08-09, kept because the wrong diagnosis is the lesson

*History. The counts below are that week's; the newest sweep is §4's `124 121 3`. Read
this for the method, not the numbers.*

Two consecutive runs on the same tree, docs-only changes, nothing touching voice or
movement:

| run | failed |
|---|---|
| 1 | the 3 voice tests **+** `PlayerStanceTests.The_hop_cannot_mount_a_ledge_a_walk_cannot` |
| 2 | the 3 voice tests **+** `GunTests.Firing_hands_the_creature_a_sound_it_can_act_on` |

The three voice tests fail identically both times — see above, and treat them as
environment-gated rather than as a regression. **The fourth is a different test each run,
which makes it the more interesting one:** something in the PlayMode fixture is
order- or timing-dependent, and whichever test draws the short straw is the one that
fails. A suite with one floating failure reports a different set every time and will,
sooner or later, report an empty one — at which point the flake looks like a fix.
Neither has been chased down. The 121/121 on record was a real measurement of a run where
the straw fell elsewhere and the room was not silent.

**Update, same day, the flake showed its face.** Two later runs both failed
`GunTests.Firing_hands_the_creature_a_sound_it_can_act_on` with a message the earlier
sightings never captured:

> moving the rig beside the creature changed which creature is local to it.
> Expected: Monster @ MonsterSpawn_B1 하역장_45. But was: Monster @ MonsterSpawn_B2 기록보관소_135.

So the floating failure is not in any test's own subject — it is the harness's
"which creature is local to this rig" storey resolution flipping to the storey below,
sensitive to whatever position or scene state the previous test left behind (B1's floor
sits directly on B2's ceiling at a 3.75 m pitch; a rig or sample point landing in the
seam bin resolves down). Whoever chases it: reproduce by running the suite in order, not
the test alone — alone it passes, which is the signature of the whole class.

> 🟢 **Chased down and closed, 2026-08-09 — and the diagnosis above was wrong in the
> way that matters.** `MatchDirector.LocalStoreyCreature` was never at fault: its
> threshold is `MapGraph.StoreyChangeMetres` = 1.8 m, half the 3.75 m storey, so a
> creature a full floor away cannot qualify and there is no "seam bin". The fault was
> in the test, twice over. (1) It teleported the rig 3 m north of the creature and then
> **re-enabled the CharacterController**, letting two frames of gravity run before the
> assertions — and 3 m north of a creature is not promised to be floor. Over a 투하구
> mouth the rig falls, and past 1.8 m of fall the storey answer legitimately becomes the
> floor below. On a regenerated building that spot became a hole and the failure went
> from intermittent to **deterministic**, which is what finally made it findable. (2)
> With the fall fixed it still failed about one run in three, because the assertion was
> `LocalStoreyMonster` **is unchanged** while every creature in the building is
> patrolling: §12-B③'s creature one floor down can climb a 계단 to within 1.8 m of the
> runner's height and, being nearer in flat distance than the 3 m stand-off, win the
> tie-break honestly. The test was asserting that a live building holds still.
>
> Fixed by making the test take the answer instead of demanding one: the controller
> stays off through the shot, and the local creature is re-resolved and stood beside in
> the same frame it is acted on — the subject was always "the creature on the shooter's
> floor is told", and it is told whoever that turns out to be. **Five consecutive runs,
> 8/8 each.** The lesson for the rest of this page: "flaky harness" was a story that
> fit; the message named monster spawns, and nobody had asked why a test whose own
> comment says it is avoiding one floor's geometry then hard-coded a 3 m offset into it.

---

## Regenerating assets

Everything is generated by code — no samples, no downloads, no licensed content.

```bash
tools/audio/.venv/bin/python tools/audio/gen_footsteps.py
tools/audio/.venv/bin/python tools/audio/gen_monster_audio.py
tools/audio/.venv/bin/python tools/audio/gen_ambience.py
tools/audio/.venv/bin/python tools/audio/gen_items.py
tools/audio/.venv/bin/python tools/audio/gen_ui.py

BL=/Applications/Blender.app/Contents/MacOS/Blender
$BL --background --factory-startup --python tools/blender/gen_monster_ai.py
$BL --background --factory-startup --python tools/blender/gen_player_model.py
$BL --background --factory-startup --python tools/blender/gen_mapkit.py
$BL --background --factory-startup --python tools/blender/gen_props.py
```

Each generator measures its own output and refuses to emit something unusable — silence,
clipping, a DC offset, an empty mesh, a model at the wrong unit scale, animations exported
twice.

> **Blender's `--background` exits 0 even after a Python exception.** Never trust its exit
> code. Grep for `ASSET_FAILED`, which is what the generators emit on failure.
> `tools/ci/run_blender_generators.sh` does this for you and also fails when a generator
> writes nothing.

After regenerating, reimport so the post-processors run — Unity does not apply them
retroactively: `Horror ▸ Assets ▸ Reimport Audio And Models`, then §10 above.

---

## Playing it

### First open

`open -a "Unity Hub"`, add `/Users/doogi/horror-game/unity/HorrorGame`. First import takes
several minutes — it resolves Mirror, Steamworks.NET and FizzySteamworks.

### Alone

Open `Assets/Scenes/Map_FirstSketch_Solo.unity` and press Play. One runner, eight
creatures, a `MatchDirector`, the whole descent. The scene is assembled from the raw map
by:

```bash
$U -batchmode -quit -nographics -silent-crashes -projectPath $P \
   -executeMethod HorrorGame.EditorTools.SoloPlaytest.BuildBatch -logFile /tmp/solo.log
```

which also reads the animation wiring back **out of the saved scene** rather than
asserting it about the object it just built:

```
[SoloPlaytest] §05 ANIMATION WIRING, read back from Assets/Scenes/Map_FirstSketch_Solo.unity
  — 1 PlayerAnimatorDriver block(s).
```

Expect one warning you cannot fix from here: `[Player] No renderer under this rig reads as
the owner's hands` — [B-017](BLOCKERS.md#b-017). In first person you will see no part of
yourself.

### Two players on one machine

`HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)`

Local hosting, Discord for voice, Steam offline on development App ID 480. §14 puts this
before Steam deliberately: 「직접 만져봐야 나온다」.

**This is still the single highest-value thing anyone can do with this project.** Every
automated gate above is green or explained; not one of them can say whether solving the
same maze eight times is worth doing. Watch for:

| Watch | What is known about it |
|---|---|
| Does the descent feel like a race? | nothing — unmeasured |
| Can you shake the creature? | **always.** 680/680 places escape it, 2026-08-10. This did **not** move when [B-007](BLOCKERS.md#b-007) closed, which is the point: the defect everyone blamed for it is fixed and the grade is unchanged. [F-013](BALANCE-FINDINGS.md#f-013) argues the 50–70 % band is a co-op instrument no §12-legal map can satisfy |
| Does choosing a 투하구 matter? | the mapping is fixed, so it should reward map knowledge — unmeasured |
| How long does one descent take? | 89 s for a pathfinder; unknown for a person. §9 — there is no longer a tool that could answer this |
| Does the host's copy of you match what you did? | yes, on `127.0.0.1`, per `NetHumanRunnerTests` |

---

## What to check when something breaks

| Symptom | Look here first |
|---|---|
| Rules behaving oddly | `dotnet test` — 357 tests name the section they defend |
| A test suite that reports nothing and exits 0 | you passed `-quit` to `-runTests` |
| A build that "succeeded" but is broken | you passed `-quit` to the build |
| A creature that walks partway and stops | `PathPartial`. §5 — and `NavMeshWorldProbe` must use path length, not straight-line distance |
| A map that plays badly | §5b, then [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md) F-007 |
| A map with no dressing, no decals and a glowing floor | you ran `MapSceneGenerator.GenerateFromCommandLine`, which is layout only. §5 |
| `no such project core/HorrorGame.Sim` | correct. It was deleted. §9 |
| CI red on `core tests (dotnet)` with 357 passing | `ci.yml`'s `floor=512`, not your commit. §1 |
| Unity batch command fails immediately | the editor holds the project lock. Close it |
| `'cmath' file not found` | damaged Command Line Tools, not Unity. §7 |
| `Failed to process scene before export` | almost never the scene — `StrictMode` blaming the first scene for an unrelated logged error. §7 |
| A gate that is green for a scene you did not build | §5a. Check the stamp |
| A number disagrees with the design | `GameConstants.cs` is the only authority. A literal anywhere else is a bug |
