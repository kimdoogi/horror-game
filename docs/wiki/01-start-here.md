# Start here

## The game, in five lines

1. **Up to twenty-player competitive first-person horror**, sold on Steam, built in
   Unity 6 with Mirror. There is **no asymmetry anywhere** — twenty identical runners,
   same body, same senses. The only differences are knowing the building and how much
   you are willing to risk (§01, §11).
2. **A race down eight storeys.** Everyone starts on the rim of B1 of a 57.5 m square
   column. Each storey is a concentric maze; the middle holds 2–3 **투하구**, and
   dropping down one puts you on the **rim** of the storey below — so the maze is
   solved eight times, not once. First to the middle of B8 wins; everyone else who
   arrives is ranked (§01, §02). One match should take 12–20 minutes.
3. **The monster cannot be killed.** It moves 4.8 m/s against a 4.5 m/s run — only
   0.3 faster — and a sprint is 5.6 m/s for 12 seconds of stamina (§06). 🔴 **That
   sprint used to be the 주자's alone.** With §04's roles deleted it belongs to all
   twenty, and every number above it is unchanged: §06's lunge is still tuned to catch
   a runner at 4.5 and miss a sprinter at 5.6, so the arithmetic outlived the role that
   motivated it (game-design §05, 「질주는 남는다 — 전원에게, 체력으로」).
4. **Breaking a chase is a map problem, not a speed problem.** A sprint opens at most
   9.6 m against a 12 m release distance, so you must round two corners, not one.
   Every map rule in §12 is reverse-engineered from that arithmetic.
5. **Time is the only currency.** One clock runs across five threat tiers; if nobody
   reaches B8 before the last one passes, everybody loses (§07, §02).

The authority on all of it is [`docs/game-design.md`](../game-design.md) **v1.1**. Read
§01, §02, §06 and §12 before changing anything that moves. §04 (직업) and §08 (경제) are
**tombstones**: the section numbers were kept deliberately when their contents were
deleted, because code cites design sections by number and a vanished §04 would orphan
every constant that quotes it. They contain no live rules.

> **One thing this page cannot tell you: what being caught costs.** §02's table says
> 탈락 — no rank, become a ghost, watch the rest. §12-D's own escape-cost derivation
> says 「잡히면 B1로 돌아가 가지고 있던 층을 전부 잃는다」, and the code agrees with
> §12-D: `RaceState.ReportCaught` puts the runner back on storey 0 as `Running` and
> increments `TimesCaught`, and `RacerStatus.Eliminated`'s doc comment says outright
> that nothing in the game eliminates a player any more — it is what a *disconnect*
> resolves to. **The design document contradicts itself and the code has picked a
> side.** Do not encode either without reading both.

---

## The three commands that prove it works

Put these two lines in your shell profile first — everything `.NET` needs them:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
```

### 1 · The rules are intact — 357 tests, no engine, about a minute and a half

```bash
dotnet test core/HorrorGame.sln
```

Measured 2026-08-12 at `4ab204f` on this machine:

```
통과!  - 실패:     0, 통과:   357, 건너뜀:     0, 전체:   357, 기간: 1 m 41 s
```

`건너뜀: 0` matters as much as the total — the count is not inflated by disabled
cases. Every tuned number and every rule lives in this suite: §05's speed
multipliers, §06's aggro and state machine, §07's threat curve, §02's race rules,
§12's map rules including a full generate-and-validate of the shipped building.

> 🔴 **This used to read "451 tests … 363 ms", and before that 512.** `e8c67ae`
> deleted the co-operative game rather than gating it, and the co-op tests went with
> it; the 90-second runtime is the §12 map work that arrived afterwards, not a
> slowdown. **No total may be compared across the pivot.** The suite is *smaller and
> slower* than the page used to claim and both directions are the suite working.

**Run it before every commit.**

### 2 · The whole solution compiles in Release

```bash
dotnet build core/HorrorGame.sln -c Release
```

Measured 2026-08-12, after `dotnet clean -c Release` (an incremental build re-emits no
warnings, so a warning count from one is meaningless):

```
    경고 4개
    오류 0개
경과 시간: 00:00:02.37
```

The 4 warnings are all `CS8625` nullable-literal, all in `MapTests.cs` in the test
project. The solution is **two** projects — `HorrorGame.Core` (netstandard2.1) and
`HorrorGame.Core.Tests` (net9.0).

> 🔴 **There was a third, `HorrorGame.Sim`, and this command existed because of it** —
> the test project did not reference the balance simulator, so the simulator could stop
> compiling with every test still green. `core/HorrorGame.Sim/` was deleted at
> `e8c67ae`; there is no `horrorsim`. The step is kept because its subject is
> "whatever is in `HorrorGame.sln` tomorrow", which is the argument
> [CI.md §2.1](../CI.md) makes for keeping the same step in CI.

### 3 · The game has an antagonist that reaches you

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath /Users/doogi/horror-game/unity/HorrorGame -runTests -testPlatform PlayMode \
  -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log
```

Expect exit 0 and `<test-run result="Passed" total="4" passed="4" failed="0" …>` — the
fixture holds exactly four cases, verified 2026-08-12 by counting `[UnityTest]` in
`Assets/Tests/PlayMode/Monster/MonsterChaseTests.cs`. Last run recorded in
[BLOCKERS.md B-001](../BLOCKERS.md#b-001), 2026-08-03:

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
  route 71.0 m of NavMesh path · reached 14.54 s · closing speed 4.81 m/s against §06's 4.8
```

> 🔴 **This used to quote 133.9 m across two storey boundaries, and the question
> changed with the game.** A creature cannot use a 투하구, so it can no longer cross
> the building; the test now asks whether it can reach a runner *on its own storey*.
> That is the right question for a race and it is a weaker one than the number it
> replaced. The two control corridors still reproduce §06's central claim to 1 %
> (`monster speed 4.80 m/s`, `gap opened at 0.80 m/s`), and those are the lines to hold
> a change against.

**Three things about this command, all of which have already burned someone:**

- **Never add `-quit`.** The test runner is asynchronous and exits from its own
  callback; `-quit` kills the editor before results are written, so the run reports
  nothing and looks green.
- **Check the exit code before reading any error count.** A Unity run that died early
  on a held project lock writes a log with zero errors in it, and that is not the
  same thing as a clean run.
- **Only one process may hold the project lock.** Close the editor, and do not run
  two Unity batch commands at once. Check with
  `ps aux | grep "[U]nity.app/Contents/MacOS/Unity"` before you start.

---

## Where the project actually stands

[`docs/STATUS.md`](../STATUS.md) is the authority and is rewritten each pass. As of
its 2026-08-10 edition (commit `9f0f447`), in one paragraph:

**The rules core is green — 357 of 357, 0 skipped** ([STATUS.md §1.8](../STATUS.md)).
The other two platforms are not a number anyone may quote: the last full PlayMode sweep
was 2026-08-08 at **121 of 124, three red**, all in `VoiceSocketTests`
([STATUS.md §2.3](../STATUS.md)), and **EditMode has not been run since the pivot** —
its newest result is 2026-08-01, against the four-player game that no longer exists
([B-016](../BLOCKERS.md#b-016)). Until that run happens **no document here may quote a
project-wide total.** A runner does descend B1→B8 through seven chutes and finish, and a
macOS Release IL2CPP player builds at exit 0 ([STATUS.md §1.4, §1.10](../STATUS.md)) —
though the player sitting in `dist/` today is a *Development Mono* build from
2026-08-10, not that one.

The map is an **eight**-storey column, 57.5 m × 57.5 m, **680 places** and 766 passages
on the shipped seed — and there are eight buildings now, not one
(`DescentRoster.txt`, [STATUS.md §1.1, §1.6](../STATUS.md)). It grades **10/10 TooEasy**
on the 주자 테스트, *outside* §12's 5–7 band ([F-007](09-open-questions.md#f-007)), and
that grade has not moved for four working passes. §12's checklist is **14 rules and 13
of them pass**; the one failure, `centre-path`, is waived by name in
`MapSceneGenerator.KnownFailingRules`, so **the map regenerates and stamps
「이 빌드에는 알려진 지도 결함이 있다」 on itself** ([STATUS.md §2.2](../STATUS.md)).

> 🔴 **This paragraph used to say the map was a five-storey basement of 164 places
> failing 1 of 17 rules on the corner-density rule, and that generation was frozen.**
> Every one of those numbers is dead: [B-007](../BLOCKERS.md#b-007) closed on
> 2026-08-10, its waiver is deleted, and 95 m of continuous cover became 12.5 m against
> a 14.4 m cap on all eight roster seeds. The grade did not move anyway, which is the
> finding — see [F-013](../BALANCE-FINDINGS.md#f-013), which argues the 50–70 % band is
> a co-op-era instrument that no §12-legal map can fail.

> 🔴 **A paragraph about the economy resolving a match in 7.2 minutes against a 25–35
> minute target used to sit here.** §08 is deleted; there is no economy and no battery,
> and §01's target is now **12–20 minutes**. Nobody has measured a real match length:
> the only number in existence is a pathfinding robot's 171 s
> ([STATUS.md §1.4](../STATUS.md)).

**Nobody has yet sat down with two instances and played it.** §14's 검증 질문 says
questions 1 and 2 decide the project and 「직접 만져봐야 나온다」. That is the
highest-value thing anyone can do here and it cannot be automated:

```
HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)
```

---

## Playing it alone, in ninety seconds

```bash
open -a "Unity Hub"     # add /Users/doogi/horror-game/unity/HorrorGame
```

First import takes several minutes — it resolves Mirror 96.6.4, Steamworks.NET
2025.164.0 and FizzySteamworks 6.0.1. Then open
`Assets/Scenes/Map_FirstSketch_Solo.unity` and press Play: one runner at a §12 spawn
marker, one creature per storey, and a `MatchDirector` that owns the whole descent —
§07's clock, §06's creature, §01's 투하구 and §02's standings. The map is already
generated and committed; you do not need to build it. (`SoloPlaytest`'s own doc comment
records what this sentence used to promise: 「§03's clue chain and objective, §08's loot
and shop」 — 하강 is a footrace, and nothing is read, carried, sold or bought in it.)

The editor menus are the fastest way to see what exists. The ones worth knowing:

| Menu | What it does |
|---|---|
| `HorrorGame ▸ Play ▸ ▶ START PLAYTEST` | the one-click path into the game |
| `HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)` | two instances on one PC, local hosting, Discord for voice |
| `HorrorGame ▸ Play ▸ Launch a Small Field (4)` | four runners, headless clients — `LocalTwoInstance.DefaultFieldSize`. **The everyday one** |
| `HorrorGame ▸ Play ▸ Launch a Full Field (§11 · 20)` | `GameConstants.RaceRunnersMax` runners on this machine. Its own doc comment says it "costs the machine" — do not reach for it when four would answer the question |
| `HorrorGame ▸ Scene Gen ▸ Report Map Quality` | prints the §12 checklist and the 주자 테스트 grade, writes nothing |
| `HorrorGame ▸ Scene Gen ▸ Regenerate Map (layout → dressing → atmosphere)` | `MapPipeline.RegenerateMenu` — the **only** way to rebuild the map. It aborts and writes nothing on a §12 failure, unless the rule is waived by name in `MapSceneGenerator.KnownFailingRules`, in which case it writes and stamps the scene with 「알려진 지도 결함」. `MapSceneGenerator.GenerateFromCommandLine` is **layout-only** — no dressing, no decals, no glows — so its numbers describe a building that is not the game |
| `Horror ▸ Test ▸ Run EditMode + PlayMode` | the full Unity suite |
| `Horror ▸ Assets ▸ Reimport Audio And Models` | after regenerating any asset — see [the asset pipeline](05-asset-pipeline.md) |
| `Horror Game ▸ Player ▸ Feel Harness - Section 05 Movement` | live speed, the §05 directional multiplier, and your margin over the monster |

Full list: `grep -rh 'MenuItem("' unity/HorrorGame/Assets/Scripts | sed 's/.*MenuItem("\([^"]*\)".*/\1/' | sort -u`

---

## What you are not allowed to do

| Never | Why |
|---|---|
| Edit `unity/HorrorGame/Assets/Scripts/Core/` or `core/HorrorGame.Core.Tests/` unless that is explicitly your task | They are the shared contract; two writers produce a merge nobody can review |
| Hard-code a tuned number | [Where every number lives](03-where-numbers-live.md) |
| Create a `.cs` file inside `core/HorrorGame.Core/` | That directory holds the `.csproj` and nothing else — [the layering rule](02-layering-rule.md) |
| Change map scale without re-running the chase tests and §12 validation | §12's dimensions are derived from §06's speeds; the tests are the guard |
| Quietly retune the design to resolve a contradiction | Encode what the document says, pin the consequence with a test, and write it up — [Open questions](09-open-questions.md) |
| Trust a Blender exit code | `--background` exits 0 after a Python exception — [the asset pipeline](05-asset-pipeline.md) |
