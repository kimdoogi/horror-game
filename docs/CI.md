# CI

> What runs automatically, what it protects, what you can run yourself, and what is
> switched off until someone buys a Unity licence.

Two workflows:

| File | Needs a licence? | Runs today |
|---|:--:|:--:|
| [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) | no | ✅ every push |
| [`.github/workflows/unity.yml`](../.github/workflows/unity.yml) | **yes** | ❌ skips with a note |

Everything in `ci.yml` was executed locally before it was committed, and the exact
output is quoted below. Nothing in `unity.yml` has ever run — see
[§5](#5-what-cannot-run-yet).

---

## 1. Why CI is structural here, not hygiene

macOS cannot build a Windows IL2CPP player. IL2CPP transpiles C# to C++ and then
needs the **target** platform's native toolchain — MSVC — to compile and link it. A
Mac can cross-compile a Windows *Mono* player and nothing more.

Mono is not a shippable answer for this game:

* §13 makes Steam the entire backend, and Steam's audience is overwhelmingly Windows.
  The Windows player *is* the product.
* Mono ships the game as IL, which decompiles in seconds. `docs/ARCHITECTURE.md` §4
  makes "clue contents and the objective's location exist only on the host" a design
  constraint; host-side secrets survive a decompiler, but a Mono build still hands
  out the clue tables, glyph rendering and `ObjectiveResolver` in readable form to
  anyone who wants to spoil §03 for a lobby.
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

```sh
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
```

Run here on 2026-07-30 with .NET SDK 9.0.316 on macOS arm64:

```
Test run for .../HorrorGame.Core.Tests/bin/Debug/net9.0/HorrorGame.Core.Tests.dll (.NETCoreApp,Version=v9.0)
VSTest version 17.14.1 (arm64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   387, Skipped:     0, Total:   387, Duration: 275 ms
```

387 tests, 275 ms, no Unity, no licence, no GPU. The whole job — checkout, SDK
install, restore, build, test — is a couple of minutes.

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

A second step builds the whole solution in Release:

```sh
dotnet build core/HorrorGame.sln --configuration Release
```

The test project does not reference `HorrorGame.Sim`, so the balance simulator — the
tool §16-2's loot-value question depends on — can stop compiling without a single
test failing. Two extra seconds closes that gap.

> **Make this the required status check** on `main`: Settings → Branches → branch
> protection rule for `main` → *Require status checks to pass* → add
> **`core tests (dotnet)`**. Nothing else in CI should be required — see §2.2 and
> §2.3 for why.

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
in its own words, 시스템 결정 rather than an art decision: §04's 청음사 reads the
monster's position from the surface it walks on. That makes the five materials an
alphabet, and "the alphabet is still legible" is a property of the *set* of clips.
No single generator can check it — retune gravel and the check that breaks lives in
the wood file. It is also invisible in play right up until someone reports that the
role "doesn't work", which is the hardest kind of bug to trace to a commit.

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
| Warnings (F-003's 1.396×, the flare loop seam) | reported, never gated |

The third row is the one worth arguing for. `docs/BALANCE-FINDINGS.md` opens with
"Every finding is pinned by a test, so a later edit that changes the answer fails the
build instead of passing silently." Applying that to an asset check means a *fix*
also has to fail the build once, so the write-up moves in the same commit as the
change, while the reasoning is still in someone's head.

Every baseline entry must name a finding id; the gate rejects one that does not. That
is the rule that stops the file becoming a mute button. It currently holds exactly
one entry.

Warnings are never gated because F-003 sits 0.004 from its threshold. Gating it would
make the build a coin toss on filter rounding, and it is already pinned by the
audit's own output and its write-up.

Local run, 2026-07-30 (Python 3.9.6, numpy 2.0.2, scipy 1.13.1):

```
  §12 Listener alphabet: SUPPORTED — worst surface pair metal vs tile at 2.13x (need >= 1.4x)
  worst within a single actor: 1.98x
  at 25m through a wall it does NOT hold: worst pair wood vs metal at 1.396x
  clips: 166   loops checked: 18   blocking defects: 1   warnings: 2
  RESULT: FAIL
...
AUDIO GATE — blocking defects against tools/ci/audio_baseline.json
  KNOWN     [consistency] gravel vs concrete  → F-002
            still reproducing, still awaiting a decision in docs/BALANCE-FINDINGS.md
  RESULT: PASS
```

The audit says FAIL, the gate says PASS, and both are correct — that is the whole
point of the split.

### 2.3 `blender generators` — and the exit code that lies

```sh
tools/ci/run_blender_generators.sh                  # all four
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

Local run, 2026-07-30, Blender 5.2.0 LTS — all four generators, 8 seconds:

```
  OK   gen_mapkit  21 asset(s)
  OK   gen_props  24 asset(s)
  OK   gen_player_model  1 asset(s)
  OK   gen_monster_model  1 asset(s)
  all 4 generator(s) ran clean
```

**What this job does *not* do:** assert byte-identical regeneration.
`docs/ASSETS.md` says a clean rebuild is byte-identical, and for the mesh data it is
— but the containers are not. Regenerating `Crate.fbx` here produced a file of
*identical length* differing in 53 bytes, every one of them inside the FBX header's
`CreationTime` (Hour / Minute / Second / Millisecond). A `git diff --exit-code` gate
would therefore fail on every run for a reason that has nothing to do with the game.
The real signal — a changed vertex count, triangle count or bounding box — is in the
`ASSET_REPORT` lines the job prints, where a reviewer can diff it by eye. The job
prints how many files differ, as information only.

**Also not covered:** the *audio* generators are not re-run. The audit in §2.2 checks
their committed output but never invokes `gen_footsteps.py` and friends, so those five
scripts can rot the same way the Blender ones can. They are fast and pure-Python;
adding them is a small job for whoever owns `tools/audio/`.

---

## 3. Running the whole engine-free suite locally

```sh
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"

dotnet test core/HorrorGame.Core.Tests/HorrorGame.Core.Tests.csproj
dotnet build core/HorrorGame.sln --configuration Release
tools/ci/verify_audio.sh
tools/ci/run_blender_generators.sh
```

That is the entire green tick, reproducible on a laptop in well under a minute of
work. The workflow calls the same two scripts, so a red run is a copy-paste away from
a local repro rather than an archaeology exercise in YAML.

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
| Tests | `tools/ci/build.sh test` → EditMode + PlayMode, results under `dist/test-results` |
| Build | `tools/ci/build.sh windows release --require-il2cpp` → `dist/windows-x64` |
| Upload | the player, plus `dist/logs` and `dist/test-results`, all under `always()` |
| Return the licence | `-returnlicense`, under `always()`, serial route only |

Two flags worth explaining:

* **`--require-il2cpp`.** Without it the pipeline is allowed to fall back to Mono, and
  this job would quietly produce exactly the build it exists to prevent — with an
  artifact that looks right. `build.sh` exits 5 instead.
* **`--require-tests` is deliberately *not* passed.** `Assets/Tests/EditMode/` and
  `Assets/Tests/PlayMode/` are still empty, so `build.sh test` passes today by running
  nothing. Add `--require-tests` in the same commit as the first suite, so that "the
  tests silently stopped being discovered" becomes a failure rather than a green tick.

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
  install needs `--changeset <hash>` from Unity's download archive. It cannot be read
  from this repository: `ProjectSettings/ProjectVersion.txt` records
  `m_EditorVersionWithRevision: 6000.3.21f1` with no revision hash. The install step
  prints this instruction when it cannot find the editor afterwards.
* **`Assets/Tests/EditMode/` and `Assets/Tests/PlayMode/` are empty.** `build.sh test`
  runs and passes without executing a single test. Until suites appear, the Unity half
  of `docs/ARCHITECTURE.md` §5's table is unenforced — adapters, prefab wiring,
  generated scenes, movement feel, chases and networking are uncovered by any
  automated check.
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
