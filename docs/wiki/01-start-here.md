# Start here

## The game, in five lines

1. **Four-player co-operative first-person horror**, sold on Steam, built in Unity 6
   with Mirror. Senses are symmetric; only abilities differ — 5 roles, pick 4 (§01,
   §11).
2. The team **descends into a basement, reads clues that cannot be carried out,
   takes optional loot, surfaces, sells, buys, and descends again** until it can
   carry the objective out (§01, §03, §08).
3. **The monster cannot be killed.** It moves 4.8 m/s against a 4.5 m/s run — only
   0.3 faster, and only the 주자 sprints past it at 5.6 for 12 seconds (§06).
4. **Breaking a chase is a map problem, not a speed problem.** A sprint opens at most
   9.6 m against a 12 m release distance, so you must round two corners, not one.
   Every map rule in §12 is reverse-engineered from that arithmetic.
5. **Time is the only currency.** One clock runs across five threat tiers; leaving is
   a breather, not a reset (§07, §03).

The authority on all of it is [`docs/game-design.md`](../game-design.md) v0.5. Read
§01, §03, §06 and §12 before changing anything that moves.

---

## The three commands that prove it works

Put these two lines in your shell profile first — everything `.NET` needs them:

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
```

### 1 · The rules are intact — 451 tests, no engine, under a second

```bash
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

Measured 2026-08-01 05:58 on this machine:

```
통과!  - 실패:     0, 통과:   451, 건너뜀:     0, 전체:   451, 기간: 363 ms
```

`건너뜀: 0` matters as much as the total — the count is not inflated by disabled
cases. Every tuned number and every rule lives in this suite: §05's speed
multipliers, §06's aggro and state machine, §07's threat curve, §08's economy, §03's
clues and confusion pairs, §12's map rules. **Run it before every commit.**

### 2 · Everything compiles, including the balance simulator

```bash
dotnet build core/HorrorGame.sln -c Release
```

Measured 2026-07-31 23:32:

```
    경고 11개
    오류 0개
경과 시간: 00:00:04.97
```

The 11 warnings are `CS8625` nullable-literal plus one `CS0649`, all inside the test
project. The solution is three projects — `HorrorGame.Core`, `HorrorGame.Core.Tests`,
`HorrorGame.Sim`. The test project does **not** reference `HorrorGame.Sim`, so
without this second command the balance simulator can stop compiling with every test
still green ([CI.md §2.1](../CI.md)).

### 3 · The game has an antagonist that reaches you

```bash
/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath /Users/doogi/horror-game/unity/HorrorGame -runTests -testPlatform PlayMode \
  -testFilter "MonsterChaseTests" -testResults /tmp/chase.xml -logFile /tmp/chase.log
```

Expect exit 0 and `<test-run result="Passed" total="4" passed="4" failed="0" …>`.
Last run recorded in [STATUS.md §1.3](../STATUS.md) and
[BLOCKERS.md B-001](../BLOCKERS.md#b-001):

```
[ChaseTest]   route            133.9 m of NavMesh path, monster spawn → (33.75, 0.18, 71.25)
[ChaseTest]   reached          27.52 s
[ChaseTest]   closing speed    4.83 m/s of route, against §06's 4.8 m/s of ground speed
```

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
its 2026-08-01 edition, in one paragraph:

**Every test in the project is green — 575 of 575** (core 451, EditMode 71, PlayMode
53). Unity compiles clean and so does the core solution. The monster crosses the map
and catches you. A solo match runs end to end — descend, read clues, take loot,
surface, sell, buy, descend, carry the objective out. The macOS IL2CPP player builds,
launches and starts a match with no exceptions ([STATUS.md §1.10](../STATUS.md)).

The map is a **five**-storey basement, 164 places against the old 74 — and it is where
the bad news is. It grades **10/10 TooEasy** on the 주자 테스트, *outside* §12's 5–7
band, which the three-storey map held at 7/10 ([F-007](09-open-questions.md#f-007)),
and as of 2026-08-01 it also **fails §12's checklist** — 16 of 17 rules, the 17th
being the corner-density rule that measures exactly what makes it too easy. Because
generation gates on the checklist, **the map can no longer be regenerated**
([B-007](../BLOCKERS.md#b-007)). The committed scene still runs; authoring is frozen.

The economy resolves a match in **7.2 minutes** against a 25–35 minute design target
([F-006](09-open-questions.md#f-006)) — up from 2.5, and all five of §07's threat
tiers are now reached by real matches, but 25–35 is still not the normal match.

**Nobody has yet sat down with two instances and played it.** §14 says questions 1
and 2 decide the project and 「직접 만져봐야 나온다」. That is the highest-value thing
anyone can do here and it cannot be automated:

```
HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)
```

---

## Playing it alone, in ninety seconds

```bash
open -a "Unity Hub"     # add /Users/doogi/horror-game/unity/HorrorGame
```

First import takes several minutes — it resolves Mirror, Steamworks.NET and
FizzySteamworks. Then open `Assets/Scenes/Map_FirstSketch_Solo.unity` and press Play:
one player, one monster, a `MatchDirector`, the whole §01 loop. The map is already
generated and committed; you do not need to build it.

The editor menus are the fastest way to see what exists. The ones worth knowing:

| Menu | What it does |
|---|---|
| `HorrorGame ▸ Play ▸ ▶ START PLAYTEST` | the one-click path into the game |
| `HorrorGame ▸ Play ▸ Launch Two Instances (§14 step 2)` | two instances on one PC, local hosting, Discord for voice |
| `HorrorGame ▸ Scene Gen ▸ Report Map Quality` | prints the §12 checklist and the 주자 테스트 grade, writes nothing |
| `HorrorGame ▸ Scene Gen ▸ Regenerate Map (layout → dressing → atmosphere)` | rebuilds the map; **fails** rather than saving an illegal one |
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
