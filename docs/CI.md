# CI

> What runs automatically, what it protects, what you can run yourself, and what is
> switched off until someone buys a Unity licence.

Two workflows:

| File | Needs a licence? | Runs today | Green means |
|---|:--:|:--:|---|
| [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) | no | ✅ every push | the rules, the numbers, §12's map checklist and the assets are all good |
| [`.github/workflows/unity.yml`](../.github/workflows/unity.yml) | **yes** | ❌ every job skips | **nothing.** No player was built and no Unity test ran — see [§0.4](#04-the-second-green-tick-means-nothing) |

Everything in `ci.yml` was executed locally before it was committed, and the exact
output is quoted below. Nothing in `unity.yml` has ever run — see
[§5](#5-what-cannot-run-yet).

**If you read one section, read [§6](#6-the-one-thing-the-owner-has-to-click).**

> 🟢 **Fixed 2026-08-12: the TRX floor was stale and the required job was red on
> arithmetic.** `e8c67ae` deleted the co-operative game and the ~155 tests that drove
> it — a legitimate deletion — and nobody moved the floor with it. So `core tests
> (dotnet)` failed on **every push for nine days and ~45 commits** while the suite
> underneath it was green the whole time. The floor is now `357`, measured at
> `4ab204f`.
>
> **This is §0's own failure mode turned on §0's own fix.** §0.1 was a red required job
> nobody read; this was a red required job whose *gate* had rotted. The lesson is not
> "pick a lower number" — it is that **a count inside a gate is a claim with a date on
> it, and the date is the part that rots.** Both numbers in this file that came from
> `a3e268e` were wrong by 2026-08-12; both are now re-measured and stamped.

---

## 0. Why this file changed on 2026-08-03 — an integrity problem

CI was red on `main` for three consecutive commits and nobody noticed, and two of
those three commit messages assert that the suite is green. Everything below in this
section is measured on the artefact, not recalled.

### 0.1 The three red commits

`a89cf64` added `unity/HorrorGame/Assets/Scripts/Editor/SceneGen/ChamberDockProbe.cs`,
which declares `using UnityEditor`. `core/HorrorGame.Sim/HorrorGame.Sim.csproj` compiles
that folder with a **glob and a denylist**, and the new file was on neither list, so it
was compiled into an engine-free project. Verified from the objects themselves:

```
commit    ChamberDockProbe uses UnityEditor    excluded in Sim.csproj
a89cf64                 yes (2 usings)                 no
af2563d                 yes (2 usings)                 no
43cf488                 yes (2 usings)                 no
a3e268e                 yes (2 usings)                 YES  ← the fix
```

With the file globbed in, `HorrorGame.Sim` cannot compile, so
`dotnet build core/HorrorGame.sln --configuration Release` cannot succeed, so the
`core tests (dotnet)` job was **red on all three commits**.

Two details that made it easy to walk past:

* **The tests were fine.** At those three commits `HorrorGame.Core.Tests.csproj` did
  not compile `Editor/SceneGen` at all — it gained that glob only at `a3e268e`, in the
  same commit as the fix. So `dotnet test` passed and the *solution build* step failed.
  The check that went red is called **`core tests (dotnet)`**, and the tests were not
  the problem. A reader glancing at the name learns the wrong thing.
* **The error text points at the wrong file.** 28 × `CS0246`, reading
  `'GameObject' 형식을 찾을 수 없습니다`. That looks like a broken source file, so the
  reader opens `ChamberDockProbe.cs` — which is correct Unity code — instead of the
  exclusion list in two `.csproj` files.

Both csprojs now carry a `VerifySceneGenExclusions` MSBuild target that fails with one
sentence naming the file and the fix, in **both** directions. That closes the diagnosis
problem. It does not close the enforcement problem, which is [§6](#6-the-one-thing-the-owner-has-to-click).

### 0.2 The simulator was built and never run

`ci.yml` built `HorrorGame.Sim` and never executed it. Measured at `a3e268e`:

```sh
$ dotnet build core/HorrorGame.sln --configuration Release
빌드했습니다.  경고 0개  오류 0개                        # ← what CI checked

$ dotnet run --project core/HorrorGame.Sim -c Release -- validate
Balance constants are internally consistent.
BalanceOverrides reproduces CarryLoad exactly at the shipped values.
Unhandled exception. System.InvalidOperationException: The map has 8 storeys and
  only 5 signs are written here. §03's second clue names a floor by its sign, so
  every storey needs one and no two may share.
   at HorrorGame.Sim.SimMap.SignTheStoreys(...) SimMap.cs:line 342
$ echo $?
134                                                     # ← what was true
```

The descent pivot grew the tower to eight storeys; `SimMap`'s sign table stayed at
five. The simulator was then the only tool that answered §16-2 and
[F-006](BALANCE-FINDINGS.md#f-006), it aborted on its first command for a day, and CI
stayed green on it the whole time because compiling is not running.

> 🔴 **The simulator no longer exists.** `core/HorrorGame.Sim/` was deleted with the
> co-op game at `e8c67ae` — 0 tracked files, gone from `core/HorrorGame.sln`. This
> subsection is kept as **history**, because its lesson is the one this whole file
> turns on and it outlived its subject: *checking that a tool compiles is not checking
> that it runs.* What the section describes is no longer reproducible; what it teaches
> is still the reason step 4 exists. See [§2.1](#21-core-tests-dotnet--the-one-that-must-always-be-green)
> for what step 4 runs today.

This is [B-006](BLOCKERS.md#b-006) one layer out. B-006 was *"the tool does not build
and the tests do not notice"*, and the answer was to build the solution. This was *"the
tool builds and nothing runs it"*, and the answer is [§2.1](#21-core-tests-dotnet--the-one-that-must-always-be-green)'s
new step.

### 0.3 `dotnet test` exits 0 when it runs no tests

Measured here on 2026-08-03, .NET SDK 9.0.316:

```
$ dotnet test core/HorrorGame.Core.Tests/... --filter "FullyQualifiedName~ZZZNoSuchTestZZZ"
No test matches the given testcase filter `FullyQualifiedName~ZZZNoSuchTestZZZ` in
  .../HorrorGame.Core.Tests.dll
$ echo $?
0
```

So a suite that stops being *discovered* — a renamed namespace, an adapter that no
longer loads, an `asmdef` change — is reported by CI as a green tick over zero tests,
and the exit code cannot tell "357 passed" from "none ran". Closed by the TRX floor in
[§2.1](#21-core-tests-dotnet--the-one-that-must-always-be-green). The Unity side had
the identical hole and it is closed the same way — see [§4.2](#42-what-the-job-does).

### 0.4 The second green tick means nothing

`unity.yml`'s jobs are gated `if: needs.preflight.outputs.enabled == 'true'`, which is
false without a licence. **A job that skips cannot fail.** `preflight` itself runs and
succeeds, so the run has one successful job and no failed ones, and GitHub puts a
**green tick on the `Unity` workflow next to every commit** — over a Windows IL2CPP
player that was never built and two Unity test suites that never ran.

Stated plainly, because a reader has no way to tell the two ticks apart:

> Today CI proves that the engine-free rules, the tuned numbers, §12's map checklist
> and the committed assets are sound. **It proves nothing at all about whether the
> game builds, launches, or plays.** Every check that touches the engine is switched
> off, and it is switched off in a way that looks like success.

Two consequences:

1. `preflight` now emits a `::warning::` when the Unity half is off, so the caveat
   appears on the run page beside the tick instead of one click inside a step summary.
2. **Never make a Unity job a required status check.** A skipped required check either
   auto-passes or blocks forever depending on how GitHub scores it; both outcomes are
   wrong, and neither is a verification. [§6](#6-the-one-thing-the-owner-has-to-click)
   lists exactly which checks to require.

### 0.5 What CI would have caught in this session, and what it would not

The descent pivot ran as a series of rounds. Going through them one at a time — this
table is the entire argument for [§6](#6-the-one-thing-the-owner-has-to-click):

| Round | The defect | Would CI have caught it? |
|---|---|---|
| `a89cf64`–`43cf488` | `ChamberDockProbe.cs` takes `HorrorGame.Sim` down; the whole solution stops building | **Yes — and it did.** Red on three pushes. Nothing and nobody acted on it. Not a coverage gap: an *enforcement* gap |
| ongoing, ~1 day | `SimMap` signs 5 storeys of 8; `horrorsim validate` aborts with exit 134 | **No** — built, never run. Moot now: the simulator was deleted at `e8c67ae`. The *lesson* is what step 4 still enforces ([§2.1](#21-core-tests-dotnet--the-one-that-must-always-be-green)) |
| `43cf488` | §12's `open-adjacent-to-maze` was passing on a node pair joined only by a one-way 투하구 — a dishonest pass | **Not then. Yes now**: `validate` runs `MapValidator` over the shipped building headlessly, so a §12 rule changing verdict is a CI event for the first time |
| `3fa35b3` | The shipped scene had five storeys, not eight; `ThirdPersonCamera` was in no scene; `Runner.fbx` was referenced by nothing | **No, and still no.** Nothing asserts the contents of the scene the player loads — not even with a licence. See [§5](#5-what-cannot-run-yet) |
| `a89cf64` | The race could not be won — `MatchDirector` wrote descents into a `RaceState` nothing read | **No.** PlayMode only, licence-gated |
| `a89cf64` | The player had no animation in the scene it ships in (importer `animationType: 0`) | **No.** Unity importer, licence-gated |
| `af2563d` | `PlayerTraversal` flooded from the 출입구 *upward* through one-way chutes — a co-op instrument pointed at a race | **No.** Unity editor tool, licence-gated |
| `43cf488` | One leaked scene contaminated four PlayMode fixtures; a "dark" assertion read 0.166 instead of 0 | **No.** PlayMode only, licence-gated |
| `a3e268e` | 호스트 started a session on a `NetworkManager` Mirror had never configured | **No.** PlayMode only, licence-gated |
| [B-009](BLOCKERS.md#b-009) | The NavMesh being audited was not the one just built | **No.** Unity only |

Two conclusions, and they point in opposite directions:

* **The engine-free half is worth protecting and is not protected.** It caught the one
  defect it could catch, said so three times, and was overruled by silence.
* **The engine half is most of the game and CI covers none of it.** That is not fixed
  by a setting; it is fixed by a licence ([§4.1](#41-secrets-and-where-each-one-comes-from)).

---

## 1. Why CI is structural here, not hygiene

macOS cannot build a Windows IL2CPP player. IL2CPP transpiles C# to C++ and then
needs the **target** platform's native toolchain — MSVC — to compile and link it. A
Mac can cross-compile a Windows *Mono* player and nothing more.

Mono is not a shippable answer for this game:

* §13 makes Steam the entire backend, and Steam's audience is overwhelmingly Windows.
  The Windows player *is* the product.
* Mono ships the game as IL, which decompiles in seconds. 🔴 This bullet used to
  invoke "clue contents and the objective's location exist only on the host" — there
  is no clue and no objective now, and `ObjectiveResolver` is gone. **The bullet
  survives on the value that replaced them.** `docs/ARCHITECTURE.md` §4 now makes
  *placement and arrival* host-only, and §02 says the arrival call is the first value
  anyone forges in a racing game. Host authority survives a decompiler; what a Mono
  build hands out is the client half — `RaceState`'s shape, the Mirror message
  layouts, and the seeded map generator — which is the difference between forging a
  finish being hard and being an afternoon's work. Weaker than the old argument, and
  still the right way round.
* IL2CPP is what the game gets profiled and tuned against. A pipeline that only ever
  produces Mono means the build that ships has never been built by CI at all.

**A Windows runner is therefore the only path from this repository to a shippable
artifact.** That is the reason `unity.yml` exists even though it cannot run yet.

---

## 2. `ci.yml` — the three jobs that need no engine

No `paths:` filters, deliberately. Filtering the audio job on `Assets/Audio/**` looks
obvious and is wrong: `tools/audio/verify_audio.py` parses
`Assets/Scripts/Core/GameConstants.cs` to compare the Listener's clarity table
against the measured clips, so a one-line edit to a core constant can break the audio
gate. A filter would hide exactly that.

### 2.1 `core tests (dotnet)` — the one that must always be green

Four steps, in this order. Each one exists because the step above it can be green while
the thing below it is broken.

| # | Step | Catches |
|:-:|---|---|
| 1 | `dotnet test …` (writes a TRX) | a rule or a tuned number that changed meaning |
| 2 | **Assert the suite actually ran** | the suite silently not being *discovered* ([§0.3](#03-dotnet-test-exits-0-when-it-runs-no-tests)) |
| 3 | `dotnet build core/HorrorGame.sln -c Release` | a project in the solution that the test project does not reference no longer compiling ([B-006](BLOCKERS.md#b-006), and [§0.1](#01-the-three-red-commits)) |
| 4 | **Run §12's map validator** | `MapValidator` compiling and never being pointed at the shipped building ([§0.2](#02-the-simulator-was-built-and-never-run)) |

```sh
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

Run here on **2026-08-12**, .NET SDK 9.x on macOS arm64, at `4ab204f` and again at
`017b489` (`dotnet test core/HorrorGame.sln`):

```
통과!  - 실패:     0, 통과:   357, 건너뜀:     0, 전체:   357, 기간: 1 m 29 s - HorrorGame.Core.Tests.dll (net9.0)
```

**357 tests in about 1½ minutes** — three runs gave 1 m 29 s, 1 m 34 s and 1 m 41 s, so
quote the count and treat the duration as a range. No Unity, no licence, no GPU.
🔴 This block used to read
"512 … Duration: 3 s" and "512 tests, seconds" — both were `a3e268e` numbers, and both
were wrong in the direction that matters: the suite shrank when the pivot deleted the
co-op tests, and it is *not* a three-second suite. Budget a minute and a half locally;
the whole job — checkout, SDK install, restore, build, test, map gate — is a few
minutes.

**Step 2, the floor.** CI adds `--logger "trx;LogFileName=core-tests.trx"` and then
reads the counters out of the TRX, because [§0.3](#03-dotnet-test-exits-0-when-it-runs-no-tests)
measured that a zero-test run exits 0. The TRX is used rather than the console text
because the console wording follows `DOTNET_CLI_UI_LANGUAGE` and the XML does not:

```
TRX counters: <Counters total="357" executed="357" passed="357" failed="0" … />
```

It is a floor rather than an equality on purpose: **adding** tests must never turn the
build red, and losing several hundred is the failure it exists to catch. Deliberately
deleting tests means lowering the number in `.github/workflows/ci.yml` in the same
commit — which is the conversation this repo wants, for the same reason
`BALANCE-FINDINGS.md` makes a *fix* fail the build once.

> 🟢 **That conversation did not happen for nine days, and this step is what caught
> it.** `e8c67ae` deleted the co-op game and its ~155 tests without lowering the floor,
> so step 2 printed `::error::357 tests passed (total 357); the floor is 512` on every
> push for **~45 commits**. The floor is now `357`, measured at `4ab204f`.
>
> The step worked exactly as designed — several hundred tests *were* lost and it
> refused to call that green. What failed was the human half of the protocol. **Set
> the floor from a run at HEAD and record which commit it was measured at**, which is
> what `ci.yml`'s comment now does; that is the only thing that lets the next reader
> tell a stale floor from a real regression.

Both directions were exercised before the step was committed: a real TRX passed, and a
hand-written TRX with `passed="0"` failed with
`::error::0 tests passed (total 0); the floor is …`.

**What it protects.** `docs/ARCHITECTURE.md` §1 keeps the core sources inside the
Unity project and compiles them a second time through
`core/HorrorGame.Core/HorrorGame.Core.csproj`, so this job tests *the exact files
Unity ships*. §5 says these are the tests that must always be green, and §2 puts
every tuned number in `GameConstants`. That makes this the job that catches the
regressions that actually happen: a retuned speed, a reversed comparison, a weight
band that stops meaning what `docs/BALANCE-FINDINGS.md` says it means.

It also catches the one failure mode the layering invites:
`FoundationTests.CoreSources_DoNotReferenceUnityEngine`. A `using UnityEngine` inside
`Assets/Scripts/Core/` compiles perfectly in the editor and breaks the entire .NET
build. Whoever adds it will not notice; this job notices in under three minutes.

**Step 3, the solution build.**

```sh
dotnet build core/HorrorGame.sln --configuration Release
```

A project can sit in the solution without the test project referencing it, and then
stop compiling without a single test failing. Two extra seconds closes that gap. This
is the step that was red on `a89cf64`, `af2563d` and `43cf488`;
[§0.1](#01-the-three-red-commits) is the account. (The project it was written to
protect, `HorrorGame.Sim`, no longer exists — the step outlived it and still guards
whatever is in `HorrorGame.sln` tomorrow.)

**Step 4, running §12's map validator.**

```sh
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj \
  -c Release --no-build --filter "FullyQualifiedName~MapTests.Descent_"
```

🔴 **This step used to run `dotnet run --project core/HorrorGame.Sim -- validate`.**
The simulator was deleted with the co-op game at `e8c67ae`, so for **six days and 37
commits the required job was failing on a command that can never succeed again** — a
red X nobody could fix, which teaches people to stop reading the X. That is worse than
the defect the step was written to catch, and it is the same argument
[§2.2](#22-asset-audit-12-audio--and-the-f-002-decision) makes against a permanently
red gate.

**The step's subject survived the deletion, because `MapValidator` did.** "Run §12's
validator, do not merely build it" is now enforced by name in the core suite. Measured
here on 2026-08-12 at `4ab204f`:

```
통과!  - 실패: 0, 통과: 4, 건너뜀: 0, 전체: 4, 기간: 36 s
```

Four tests, and the filter must match all four:

| Test | Pins |
|---|---|
| `Descent_EveryOtherSection12Rule_StillPasses` | walks the passing §12 rules **by name** — names the rule that broke |
| `Descent_MeetsSection12sSightBreakSpacing` | [B-007](BLOCKERS.md#b-007) closed, at 12.5 m |
| `Descent_CentrePath_IsInsideSection12DsBandExceptAtTheRimCellBesideAGate` | [B-019](BLOCKERS.md#b-019)'s exact remaining miss — 21 of 22 entry points, one 2.5 m short, so a slide back is a failure rather than a smaller green |
| `Descent_IsDeterministic_AndTwoSeedsAreNotTheSameBuilding` | [B-018](BLOCKERS.md#b-018) |

**The filter has to select something.** A `--filter` matching zero tests exits 0 and
prints "No test matches" ([§0.3](#03-dotnet-test-exits-0-when-it-runs-no-tests)) —
which would make this step green by describing nothing, the same vacuum the deleted
project left behind. So the step counts the tests out of the log and fails when fewer
than four ran, before it looks at the exit code at all.

There is **no exit-code waiver any more.** The old arrangement — waive exit `6` by rule
id, `sight-break-spacing`, naming B-007/F-007 — went with the simulator. B-007 is
closed now (`9f0f447`), so there is nothing to waive: the map tests are expected to
pass outright, and the waiver's job is done by `Descent_CentrePath_…` pinning B-019's
*measured* miss instead of excusing it. **If this check comes back red, that is the
check working** — read `artifacts/map-validate.log`, which names the rule.

> **Where the "make this required" instruction went.** It is now
> [§6](#6-the-one-thing-the-owner-has-to-click), with the caveat that turns out to
> matter more than the setting: a required status check gates a *pull request*, and
> `main`'s history is 100 % direct pushes.

### 2.2 `asset audit (§12 audio)` — and the F-002 decision

```sh
tools/ci/verify_audio.sh                 # venv, deps, audit, gate
tools/ci/verify_audio.sh --json out.json # keep the machine-readable results
```

The script creates the venv at `tools/audio/.venv` — the path `docs/ASSETS.md` §4.1
already tells you to use — installs the pinned `numpy` and `scipy` from
`tools/ci/requirements-audio.txt`, runs `tools/audio/verify_audio.py`, and then gates
the result against `tools/ci/audio_baseline.json`.

**What it protects.** §12's rule that each zone has a different floor material is,
in its own words, 시스템 결정 rather than an art decision.

🟢 **The rule outlived the role that motivated it, and got bigger.** This paragraph
used to read "§04's 청음사 reads the monster's position from the surface it walks on".
The 청음사 was deleted with the other four roles. game-design §12 re-founds the
requirement in one line — 「귀는 스무 명 전부가 가지고 있다」 — and the audience went
from one listener to twenty. It is now also how a runner hears **which gate the field
is piling up at**, not just where the monster is, so the alphabet is load-bearing for
§11's bottleneck as well as §06's chase.

**And it is eight letters, not five.** `Core/Map/FloorMaterial.cs` declares Wood,
Tile, Gravel, Concrete, Metal, Water, Earth, Carpet, and
`Editor/SceneGen/DescentMap.cs` spends one per storey so that a footstep names a
floor:

| B1 하역장 | B2 기록보관소 | B3 기계실 | B4 저탄장 | B5 저수조 | B6 병동 | B7 수몰층 | B8 굴착층 |
|---|---|---|---|---|---|---|---|
| concrete | wood | metal | gravel | tile | carpet | water | earth |

That is why the separation matrix is **8 × 8** and `Assets/Audio/Footsteps` holds
**96 clips — 8 surfaces × 12**. "The alphabet is still legible" is a property of the
*set*: no single generator can check it, because retuning gravel breaks a check that
lives in the earth file. It is also invisible in play right up until someone reports
that they cannot tell which floor they are on.

**The awkward part, stated plainly:** the audit currently reports one blocking
defect. It is F-002 in `docs/BALANCE-FINDINGS.md` — `GameConstants.ListenerClarity*`
ranks gravel above concrete, and measured through a wall gravel is ~32 dB *quieter*.
So the job had to choose, and the two obvious choices are both bad:

* **Fail the build.** The check is red on `main` from day one and stays red until a
  designer picks between F-002's three options. That is a design decision, not a CI
  task, and it may reasonably take weeks. A permanently red gate is not a gate:
  people learn to ignore it, and the *next* blocking defect — the real regression —
  ships behind the same red X.
* **Ignore the exit code** (`|| true`). Then the §12 separation matrix and the
  channel-policy check stop being enforced at all and nothing says so. This is also
  what `docs/ARCHITECTURE.md` §6 forbids: a finding gets encoded and pinned, never
  quietly dropped.

**What it actually does:** the defect is written down by fingerprint in
`tools/ci/audio_baseline.json`, with its finding id, why the audio is not the bug,
and what would resolve it. The gate is then two-sided.

| Situation | Result |
|---|---|
| Only baselined blocking defects reproduce | **pass** — each one printed as `KNOWN … → F-002` |
| A blocking defect that is not baselined | **fail** — this is the regression case the job exists for |
| A baselined defect stops reproducing | **fail** — the finding is answered; update `docs/BALANCE-FINDINGS.md` and delete the baseline entry in the same commit |
| Warnings (the occluded pair, the two clarity inversions, two layout notes) | reported, never gated |

The third row is the one worth arguing for. `docs/BALANCE-FINDINGS.md` opens with
"Every finding is pinned by a test, so a later edit that changes the answer fails the
build instead of passing silently." Applying that to an asset check means a *fix*
also has to fail the build once, so the write-up moves in the same commit as the
change, while the reasoning is still in someone's head.

Every baseline entry must name a finding id; the gate rejects one that does not. That
is the rule that stops the file becoming a mute button. It currently holds **two**
entries, `gravel vs concrete` and `gravel vs earth` — both F-002, the second being the
same mechanism on a surface pair that only began existing when the tower grew to eight
storeys.

Warnings are never gated because a warning is a range limit, not a regression: the
worst occluded pair sits at 1.137× against a 1.4× requirement, and no amount of
regenerating clips closes that — game-design §12 says so, and the answer is in the
Unity mix (occlusion filter strength, 3D rolloff). Gating on it would make the build
red on a thing the generators cannot fix.

Run here on **2026-08-12** at `4ab204f` (`tools/ci/verify_audio.sh`):

```
  §12 Listener alphabet: SUPPORTED — worst surface pair water vs gravel at 1.44x (need >= 1.4x)
  worst within a single actor: 1.41x
  at 25m through a wall it does NOT hold: worst pair metal vs gravel at 1.137x
  HUD vs ears: 4 inverted pair(s) — gravel/concrete, gravel/earth, water/wood, tile/concrete
  clips: 164   loops checked: 16   blocking defects: 2   warnings: 5
  RESULT: FAIL
...
AUDIO GATE — blocking defects against tools/ci/audio_baseline.json
  audit result:        FAIL
  blocking defects:    2
  accepted (baseline): 2
  warnings:            5
  KNOWN     [consistency] gravel vs concrete  → F-002
  KNOWN     [consistency] gravel vs earth     → F-002
  RESULT: PASS
```

> **Read the margin, not just the verdict.** The alphabet passes at **1.44×** against
> a 1.4× requirement, and the worst pair *within one actor* is **1.41×**. That is
> 0.04 and 0.01 of headroom — the previous numbers in this file were 2.13× and 1.98×,
> measured on 2026-07-30 before three surfaces were added. Adding a ninth material, or
> retuning any of the eight, is now overwhelmingly likely to put this check red. It is
> the closest thing in this repo to a gate that is about to bite.

> **`clips: 164` versus 168 `.wav` on disk.** The audit ignores `Audio/Presence/`
> (4 clips) — it warns `folder belongs to no known family`, along with the empty
> `Audio/Resources/`. So §09's 유령 audio is committed and **audited by nothing**.
> Filed here because `tools/audio/` is not this document's to change.

The audit says FAIL, the gate says PASS, and both are correct — that is the whole
point of the split.

### 2.3 `blender generators` — and the exit code that lies

```sh
tools/ci/run_blender_generators.sh                  # all six
tools/ci/run_blender_generators.sh gen_props        # one, while iterating
BLENDER=/path/to/blender tools/ci/run_blender_generators.sh
```

On macOS the script finds `/Applications/Blender.app/Contents/MacOS/Blender` by
itself; on the runner `tools/ci/install_blender_linux.sh` downloads a pinned Blender
(5.2.0, sha256-verified) and the workflow passes its path through `$BLENDER`.

**What it protects.** The `.fbx` and `.glb` files under `Assets/Models` are committed
on purpose, so the project opens playable without a Blender install
(`docs/ASSETS.md`). The consequence is that a generator can rot for weeks and nothing
notices: the committed asset keeps working while the only way to *change* it quietly
stops existing. You find out on the day you need to adjust §12's corridor width.

**The trap.** `Blender --background` **exits 0 after a Python exception.** Measured
on Blender 5.2.0:

| Failure | Output | Exit code | Caught by |
|---|---|:--:|---|
| module-level `raise RuntimeError` | `Traceback (most recent call last)` | **0** | traceback grep |
| `SyntaxError` in the generator | `SyntaxError:`, *no* traceback header | **0** | "wrote nothing" check |
| `blendkit.fail()` | `ASSET_FAILED <reason>` on stderr | 1 | marker grep, exit code |

So the script runs four checks per generator and fails on any of them:

1. `ASSET_FAILED` present — the generator reported its own failure;
2. a Python traceback present — it died without reporting;
3. no `ASSET_REPORT` line — it exited early and wrote nothing;
4. a non-zero exit code — the weakest signal, checked last.

Rows two and three of that table are why 1–3 all exist. Trusting the exit code alone
would put a green tick over a dead toolchain, which is worse than having no job.

**Six generators, read out of `run_blender_generators.sh` on 2026-08-12:**

```
DEFAULT_GENERATORS="gen_mapkit gen_dressing gen_props gen_player_model gen_monster_ai gen_ghost"
```

Six of `tools/blender/`'s **fourteen** scripts. Five of the other eight are libraries
with no `main()` that writes anything — `blendkit`, `gen_mapkit_detail`,
`gen_monster_model`, `gen_player_ai`, `monster_fit` — and the script's comments say so
for the two it mentions. The remaining three are a gap: **`gen_gun`, `gen_presence` and
`gen_runner` write shipped assets and are in nobody's list** (`Gun_Held.fbx`,
`Gun_Pickup.fbx`, `Presence_Figure.fbx`, `Presence_Mote.fbx`, `Runner.fbx`,
`RunnerArms.fbx`).

> 🔴 **And the list runs two generators whose output the repo does not contain.**
> `gen_player_model` declares `Assets/Models/Characters/Player.fbx`; `gen_ghost`
> declares `Ghost.fbx` + `Ghost.glb`. **`Assets/Models/Characters/` holds `Monster.fbx`
> and `Monster.glb` and nothing else** (checked 2026-08-12). Neither missing file is
> gitignored — the assets were deleted and the generators were not.
>
> So the six-generator run is: four that regenerate committed assets (`gen_mapkit`,
> `gen_dressing`, `gen_props`, `gen_monster_ai`) and **two that build things nothing
> consumes** — while three generators that *do* own committed assets are never
> exercised. The job still catches a broken `gen_mapkit`, which is most of its value.
> It is nonetheless checking the wrong six.

> ⚠️ **`gen_ghost` builds something the repo does not keep, for a rule that no longer
> exists.** It declares `Assets/Models/Characters/Ghost.fbx` + `Ghost.glb` as its
> output, and **neither file is in the repo** — not committed, not gitignored. Its
> subject, §09's 유령, was deleted from game-design on 2026-08-12 because nothing
> eliminates a player any more. What *is* committed is the debris: `Ghost.textures.json`,
> `GhostMaterials.cs`, `GhostShot.cs` and a set of `Ghost_*.mat` files, three of which
> (`Ghost_Role_Observer`, `Ghost_Role_Engineer`, `Ghost_Role_Runner`) are named after
> §04 roles deleted two rounds ago. So this job spends Blender time on a generator whose
> product nothing consumes — the opposite of the rot the job exists to catch, and worth
> resolving in the same commit either way.

> 🔴 **The run that used to be quoted here is gone, not updated.** It read
> `all 4 generator(s) ran clean` and listed `gen_monster_model  1 asset(s)`, from
> 2026-07-30. Both are now wrong: the list is six, and `gen_monster_ai` replaced
> `gen_monster_model` as the thing that writes `Monster.fbx`. **No replacement run is
> quoted because none was performed** — Blender is not run from this audit, and this
> file does not print numbers nobody measured. Whoever next runs the script should
> paste the real output here.

What *is* verified today is the committed output the job protects — 75 `.fbx` and
1 `.glb` under `Assets/Models`: MapKit 22, Dressing 39, Props 9, Player 2
(`Runner.fbx`, `RunnerArms.fbx`), Presence 2, Characters 1 (`Monster.fbx`).

> **Three generators write assets and are in nobody's list:** `gen_gun` (`Gun_Held.fbx`,
> `Gun_Pickup.fbx`), `gen_runner`, and `gen_presence` (`Presence_Figure.fbx`,
> `Presence_Mote.fbx`). Their output is committed under `Assets/Models`, so they can rot
> exactly the way §2.3 says a generator rots — this is the same gap the paragraph below
> describes for the audio generators, and it is not called out anywhere else.

**What this job does *not* do:** assert byte-identical regeneration.
`docs/ASSETS.md` says a clean rebuild is byte-identical, and for the mesh data it is
— but the containers are not. Regenerating `Crate.fbx` (a §08 loot prop, since deleted
along with the economy — the measurement stands, the file does not) produced one of
*identical length* differing in 53 bytes, every one of them inside the FBX header's
`CreationTime` (Hour / Minute / Second / Millisecond). A `git diff --exit-code` gate
would therefore fail on every run for a reason that has nothing to do with the game.
The real signal — a changed vertex count, triangle count or bounding box — is in the
`ASSET_REPORT` lines the job prints, where a reviewer can diff it by eye. The job
prints how many files differ, as information only.

**Also not covered, and this one is live:** the *audio* generators are not re-run. The
audit in §2.2 checks their committed output but never invokes `gen_footsteps.py` and
friends, so those **seven** scripts — `gen_ambience`, `gen_caught`, `gen_footsteps`,
`gen_items`, `gen_monster_audio`, `gen_scares`, `gen_ui` — can rot exactly as the
Blender ones can. The
committed `.wav` keeps working while the only way to *change* it quietly stops
existing. This is not hypothetical any more: `tools/audio/gen_ambience.py` and
`tools/audio/gen_footsteps.py` were both edited during the descent pivot, and new
`amb_zone_*` and `step_carpet_*` clips landed with them. Nothing in CI has ever run
either script.

It is the same shape as [§0.2](#02-the-simulator-was-built-and-never-run) — a tool
whose output is checked and whose execution is not — and the fix is the same shape as
`blender-generators`: a job that runs them headlessly and fails on a traceback or on
"wrote nothing". They are fast and pure-Python. **It has not been added here because
it was not measured here**; whoever owns `tools/audio/` should add it and quote the
run, rather than have this file grow a job nobody has watched work.

---

## 3. Running the whole engine-free suite locally

```sh
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"

dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
dotnet build core/HorrorGame.sln --configuration Release
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj \
  -c Release --filter "FullyQualifiedName~MapTests.Descent_"   # ← §12, and RUN it
tools/ci/verify_audio.sh
tools/ci/run_blender_generators.sh
```

That is the entire green tick. Budget **about four minutes**, not "well under a
minute": the suite alone is 1 m 34 s and the map filter another 36 s, both measured on
2026-08-12. The workflow calls the same two scripts, so a red run is a copy-paste away
from a local repro rather than an archaeology exercise in YAML.

The third line is the one that is easy to skip and is the point of
[§0.2](#02-the-simulator-was-built-and-never-run): the line above it succeeded for a
day while the tool it built aborted on its first command. Check that the filter
actually **selected** something — a filter that matches nothing exits 0.

| File | What it is |
|---|---|
| `tools/ci/verify_audio.sh` | venv + pinned deps + audit + gate |
| `tools/ci/check_audio_baseline.py` | the gate; stdlib only, so it still reports when numpy is the thing that broke |
| `tools/ci/audio_baseline.json` | known blocking defects, each with a `docs/BALANCE-FINDINGS.md` id |
| `tools/ci/requirements-audio.txt` | `numpy==2.0.2`, `scipy==1.13.1` — pinned because the findings quote measured dB to one decimal |
| `tools/ci/run_blender_generators.sh` | headless regeneration + the four failure checks |
| `tools/ci/install_blender_linux.sh` | pinned, checksum-verified Blender for a Linux runner |

`tools/ci/build.sh` also lives there but belongs to the build-pipeline area rather
than to CI. It needs the editor, so it is not part of this list —
`unity.yml` calls it, and §4.2 covers it.

---

## 4. `unity.yml` — the licence-gated half

Two jobs. `preflight` runs on Linux, costs seconds, and decides whether the rest
runs. `windows-player` runs on `windows-2022` and does everything else.

The gate is deliberately a **skip, not a failure**: a fork or a contributor without
access to the secrets should see a clear "not configured" note in the run summary,
not a red X they have no way to fix.

**Say the consequence out loud, because a skip looks like a pass.** Every job in this
file is gated `if: needs.preflight.outputs.enabled == 'true'`. Without a licence that
is false, those jobs skip, and *a job that skips cannot fail*. `preflight` runs and
succeeds, so the workflow's own conclusion is **success** and GitHub draws a green tick
beside the commit. Nothing was built and no Unity test ran. A reader looking at two
green ticks has no way to tell that one of them is a real verification and the other is
an empty room — so `preflight` now also emits a `::warning::` saying so on the run page.
[§0.4](#04-the-second-green-tick-means-nothing) is the full statement, and it is the
reason the Unity jobs must **never** be made required status checks
([§6](#6-the-one-thing-the-owner-has-to-click)).

The gate needs **both**:

1. every secret in the table below, and
2. the repository variable `UNITY_CI_ENABLED` set to `true`
   (Settings → Secrets and variables → Actions → *Variables*).

The explicit opt-in variable exists so that adding the secrets — which you may want
to do for a manual `workflow_dispatch` run first — does not immediately start a
two-hour Windows job on every push to `main`.

### 4.1 Secrets, and where each one comes from

**No credential value appears anywhere in this repository, and none should.** Add
these under Settings → Secrets and variables → Actions → *Secrets*.

| Name | What it is | Where it comes from |
|---|---|---|
| `UNITY_EMAIL` | the Unity account's email address | the Unity ID the licence is attached to |
| `UNITY_PASSWORD` | that account's password | same account. Note that 2FA on the account breaks headless activation — CI generally needs a dedicated account without it |
| `UNITY_SERIAL` | a Unity Pro/Plus serial | id.unity.com → *My Account* → *Subscriptions* / Organizations → the seat's serial (`XX-XXXX-XXXX-XXXX-XXXX-XXXX`) |
| `UNITY_LICENSE` | the **entire contents** of a Personal `Unity_lic.ulf` | activate the editor once, by hand, on a machine you control, then copy the file: Windows `C:\ProgramData\Unity\Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`. Paste the whole XML document as the secret value |

`UNITY_SERIAL` **or** `UNITY_LICENSE` — not both. If both are present the serial
wins, because it is the licence type Unity supports for automated builds.

Two things about seats, because they are the operational surprise:

* **A serial is a floating seat.** The last step of the job returns it with
  `-returnlicense`, and it runs under `always()` — a cancelled run leaks a seat just
  as easily as a failed one. A leaked seat stays checked out to a runner that no
  longer exists, and the next run fails with "no seats available".
* **A `.ulf` is not a seat** and needs no return, but a Personal licence is also
  node-locked and can be refused on a fresh runner. If the job fails at activation
  with a licence error, that is the first thing to check.

And one non-secret:

| Name | Kind | Default | Why |
|---|---|:--:|---|
| `STEAM_APP_ID` | repository **variable** | none — the build pipeline supplies §13's `480` | §13 develops against Spacewar because the real App ID does not exist yet. An App ID is public, so it is a variable rather than a secret |

`BuildPipelineOptions` resolves the App ID from `-buildSteamAppId`, then
`$STEAM_APP_ID`, then a repository-root `steam_appid.txt`, then §13's development
default, and stamps `steam_appid.txt` beside the player — which is how
`SteamAPI_Init` identifies the app for a build the Steam client did not launch, i.e.
every build a tester downloads from CI.

CI therefore sets **only** the environment variable, from the repository variable,
and leaves it empty when the variable is unset. That keeps one default (the
pipeline's) and adds one override point (the variable). Passing a `--app-id` flag with
its own fallback would have created a second default, which is how a build reaches a
depot pointing at the wrong app.

### 4.2 What the job does

Everything after activation goes through **`tools/ci/build.sh`**, the project's
batch-mode wrapper (owned by the build-pipeline area, not by CI). It discovers the
editor, streams the log, applies a watchdog and passes documented exit codes through,
and every decision that affects the produced player is made in
`Assets/Scripts/Editor/BuildPipeline*.cs`. CI calls it instead of assembling its own
`-executeMethod` line so that a build started by a runner and a build started by a
developer are *the same build*.

| Step | Note |
|---|---|
| Cache `Library/` | Regenerated from `Assets/` on open and gitignored. Without the cache every run reimports every asset and shader — the difference between an eight-minute job and a forty-minute one |
| Install Unity Hub | `choco install unity-hub` |
| Install the editor | `--version 6000.3.21f1 --module windows-il2cpp --childModules`. The IL2CPP module is the entire reason for the Windows runner |
| Activate | serial or `.ulf`, per §4.1. The only raw `Unity.exe` calls in the workflow, because `build.sh` deliberately does not handle credentials |
| Packages | `tools/ci/build.sh bootstrap` → `PackageBootstrap.InstallRequiredBatch` |
| Tests | `tools/ci/build.sh test --require-tests` → EditMode + PlayMode, results under `dist/test-results` |
| Build | `tools/ci/build.sh windows release --require-il2cpp` → `dist/windows-x64` |
| Upload | `dist/logs` and `dist/test-results` under `always()`; **the player under `success()` with `if-no-files-found: error`** |
| Return the licence | `-returnlicense`, under `always()`, serial route only |

Two flags worth explaining:

* **`--require-il2cpp`.** Without it the pipeline is allowed to fall back to Mono, and
  this job would quietly produce exactly the build it exists to prevent — with an
  artifact that looks right. `build.sh` exits 5 instead.
* **`--require-tests` IS now passed.** This entry used to read "deliberately not
  passed — `Assets/Tests/EditMode/` and `Assets/Tests/PlayMode/` are still empty; add
  it in the same commit as the first suite". They are not empty: at `4ab204f` there are
  **33 test files, 6 EditMode and 27 PlayMode**, and the whole descent pivot was
  verified through them. The stated condition was met rounds ago and the flag was
  simply never added — which is its own small instance of the theme of this document.
  `build.sh` exits **9** when no test ran, so "the suites silently stopped being
  discovered" is a failure rather than a green tick. It is the Unity-side twin of
  [§0.3](#03-dotnet-test-exits-0-when-it-runs-no-tests)'s TRX floor.

And one upload rule worth explaining, because it is the same class of defect one
notch smaller: the player artifact is uploaded with **`if-no-files-found: error`**, not
`warn`, and only on `success()`. The player is this job's entire product. A `build.sh`
that exits 0 while writing somewhere unexpected would otherwise leave a green job, no
artifact, and a yellow line in a log nobody reads. Logs and test results keep `always()`
and a soft setting, because a genuinely failed run legitimately may not have written
them.

`build.sh` also decides not to pass `-nographics` to the test run, and that is the
right call: PlayMode tests instantiate the real player loop, and a false green from a
missing graphics device is worse than a slower job. If the editor cannot create a
device on the runner, set `HORROR_NOGRAPHICS=1` on the test step rather than editing
the script.

---

## 5. What cannot run yet

Plainly:

* **Nothing that needs the Unity editor has ever executed.** The editor is not
  installed on this machine and no licence exists. Every Unity Hub and `Unity.exe`
  invocation in `unity.yml` is a documented first attempt, not a verified one. Expect
  the first real run to need fixes — most likely in the Hub's headless install, which
  is the flakiest part of the toolchain.
* **The Unity Hub may not list `6000.3.21f1` by version alone.** If it does not, the
  install needs `--changeset <hash>` from Unity's download archive. 🔴 This bullet used
  to say the changeset "cannot be read from this repository". **It can.**
  `ProjectSettings/ProjectVersion.txt` reads
  `m_EditorVersionWithRevision: 6000.3.21f1 (c02631ffc030)` — the revision is right
  there, so the install can pass `--changeset c02631ffc030` without going to the
  download archive at all. (`.github/workflows/unity.yml` still prints the old claim in
  its failure branch; it is wrong there too.)
* **The Unity suites exist and no automated system has ever run them.** This bullet
  used to say the test folders were empty. They are not: **33 test files at `4ab204f`,
  6 EditMode and 27 PlayMode** (counted here on 2026-08-12; the number of *tests* is
  whatever the last hand-run reported, see `docs/TESTING.md`) — up from 23 at
  `a3e268e` — covering adapters, prefab wiring,
  generated scenes, movement feel, chases and networking — the whole Unity half of
  `docs/ARCHITECTURE.md` §5's table.
  Every one of them has only ever been run by a person, by hand, on this Mac. They are
  written, they are good, and **CI has executed none of them, not once**, because the
  job that would is skipped for a missing licence. That is now the single largest gap
  between "green" and "verified" in this project.
* **Nothing asserts the contents of the scene the game actually loads.** `3fa35b3` is
  the case: the shipped solo scene had five storeys instead of eight, `ThirdPersonCamera`
  was attached in no scene at all, and `Runner.fbx` was referenced by nothing — while
  the code, the commit messages and every test were green. A licence would not catch
  this either; it needs an EditMode test that opens `Map_FirstSketch_Solo.unity` and
  asserts what is in it. Filed here rather than fixed here because
  `Assets/Tests/**` is not this document's to write.
* **`tools/ci/build.sh` has not been exercised by CI.** It and the
  `Assets/Scripts/Editor/BuildPipeline*.cs` classes behind it are the
  build-pipeline area's, and the workflow calls them exactly as their own
  documentation describes. Whether Git Bash on `windows-2022` drives them cleanly —
  the `tail -f` log streaming and the watchdog subshell in particular — is unverified.
* **The Linux Blender job has not been run on Linux.** The generators, the marker
  checks and the whole `run_blender_generators.sh` flow were verified on macOS with
  Blender 5.2.0. What is unverified is the runner side: the apt packages Blender links
  against even in `--background`, and the extraction. The URL, the pinned sha256 and
  the archive's internal layout *were* checked against
  `download.blender.org`. If the Linux path turns out to be a fight, the job can move
  to `runs-on: macos-latest` with `brew install --cask blender` and
  `BLENDER=/Applications/Blender.app/Contents/MacOS/Blender`, which is exactly the
  command `docs/ASSETS.md` §4.2 documents — at ten times the billed minutes on a
  private repository.

What this means in practice: **the rules and the numbers are covered, the assets are
covered, and the engine integration is not covered at all.** That split is worth
keeping in mind when reading a green tick — it is a strong statement about §05–§08
and §12, and it says nothing whatsoever about whether the game runs.

---

## 6. The one thing the owner has to click

Everything above is code, and code cannot enforce itself. **Nothing in a workflow file
can stop a red commit reaching `main`.** That is repository configuration, and only the
owner's GitHub account can set it. This section is the exact click path.

### 6.1 The recommendation, in one line

> **Make `core tests (dotnet)` a required status check on `main`, and pair it with
> "Require a pull request before merging".** Without the second half the first half
> does nothing here.

### 6.2 Why the second half is not optional

A required status check gates **merging a pull request**. This repository does not use
pull requests: every commit in recent history is a direct push, and `main` is a
straight line —

```
$ git log --merges -5     # (nothing)
$ git log -6 --pretty='%h parents:%p'
a3e268e parents:43cf488
43cf488 parents:af2563d
af2563d parents:a89cf64
a89cf64 parents:3fa35b3
3fa35b3 parents:560051b
560051b parents:96dea8c
```

— `a89cf64`..`43cf488` were **pushed, not merged**, so a rule that only gates merges
never saw them.

**One honest uncertainty.** GitHub's classic branch protection is generally understood
to *also* reject a direct `git push` whose head commit has no passing required check,
which would make the PR requirement unnecessary. That behaviour could not be tested
from here — it needs the owner's account and a live repository, and this document does
not report numbers nobody measured. So the recommendation pairs the two settings, which
is unambiguous either way, and [§6.5](#65-then-verify-it-because-that-is-the-whole-point-of-this-document)
is a two-minute experiment that settles it on the real repo. If step 3 there shows the
direct push already being rejected, the PR requirement can be dropped again.

**The cost, stated up front:** requiring a PR changes how work lands here. Agents and
the owner both push to `main` today; afterwards they will have to open a branch and a
pull request, and the PR cannot merge until `core tests (dotnet)` is green. That is the
price of the tick meaning something, and it is the right trade — but it is a workflow
change, not a checkbox that is free.

### 6.3 The clicks (rulesets — the current UI)

1. **Settings → Rules → Rulesets → New ruleset → New branch ruleset**
2. Name it something like `main must be green`.
3. **Enforcement status: Active.** (If your plan offers **Evaluate**, run it there for
   a day first — it records what *would* have been blocked without blocking anything,
   which is the cheapest possible way to find out whether this setting bites.)
4. **Target branches → Include → Include default branch** (that is `main`).
5. Under **Rules**, tick:
   * **Require a pull request before merging.** Set *Required approvals* to **0** — a
     solo owner cannot approve their own PR, and 0 still forces the PR, which is all
     that is needed here.
   * **Require status checks to pass.** Then **Add checks** and type
     **`core tests (dotnet)`**. The picker only offers checks it has seen recently, so
     if it is not listed, push any commit first and come back.
   * **Block force pushes.**
6. **Bypass list: leave it empty.** An entry for "Repository admin" hands the owner —
   and anything pushing with the owner's token, which includes every agent in this
   repo — a silent exemption, and a rule everyone is exempt from is the state this
   document exists to describe.
7. **Create.**

Classic UI instead, if you prefer it: **Settings → Branches → Add branch protection
rule** → pattern `main` → *Require a pull request before merging* + *Require status
checks to pass before merging* → add **`core tests (dotnet)`** → and tick **Do not
allow bypassing the above settings**.

### 6.4 Which checks to require, and which never to

The name GitHub shows is the job's `name:`, not the job id.

| Check | Require it? | Why |
|---|:--:|---|
| **`core tests (dotnet)`** | **YES** | 357 tests, the solution build, and §12's map validator actually running. A few minutes. This is the gate |
| `asset audit (§12 audio)` | reasonable second | Fast, and its gate is already two-sided (§2.2). Add it once you are comfortable with the first |
| `blender generators` | no | Downloads and installs Blender, up to 30 minutes, and [§5](#5-what-cannot-run-yet) notes the Linux path has never actually been run. Blocking every merge on that is blocking on infrastructure, not on the game |
| `licence preflight` | **never** | It passes unconditionally. Requiring it requires nothing |
| `windows IL2CPP player + unity tests` | **never, until a licence exists** | It **skips** ([§0.4](#04-the-second-green-tick-means-nothing)). A skipped required check either auto-passes or blocks forever depending on how GitHub scores it, and neither is a verification. Revisit the day `UNITY_CI_ENABLED` is `true` and the job has actually run green once |

### 6.5 Then verify it, because that is the whole point of this document

Do not trust the settings page. The repo's own rule is that a green number you did not
verify is worse than a red one, and that applies to the protection rule too:

1. Branch off `main`, deliberately break one test (change a number in
   `GameConstants.cs`), push the branch and open a PR.
2. Confirm the PR shows `core tests (dotnet)` **red** and that the merge button is
   **blocked**, not merely discouraged.
3. Try `git push origin main` with that same commit and confirm it is **rejected**.
4. Revert. Write the date it was verified next to this line.

**Verified on: _(not yet — this is repository configuration and no agent can apply or
test it)_.**
