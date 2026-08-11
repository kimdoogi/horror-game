# Architecture — the contract

> Read this before writing a line of code in this repo. It is the agreement that
> keeps independently written systems compiling together.
>
> The design document (`docs/game-design.md`, v1.1) is the authority on *what the
> game is*. This file is the authority on *where code goes and how pieces talk*.

---

## 1. The central decision: core sources live inside the Unity project

```
unity/HorrorGame/Assets/Scripts/Core/**/*.cs     ← the rules. One copy. Ever.
        ▲                                    ▲
        │ compiled by Unity                  │ compiled by dotnet via a glob in
        │ (HorrorGame.Core.asmdef,           │ core/HorrorGame.Core/HorrorGame.Core.csproj
        │  noEngineReferences: true)         │
```

`dotnet test` therefore verifies **the same files Unity ships**. There is no DLL
build step, no symlink, no copy to drift out of sync.

The price is one absolute rule:

> **No file under `Assets/Scripts/Core/` may reference `UnityEngine` or
> `UnityEditor`.** Not in a `using`, not fully qualified.

`FoundationTests.CoreSources_DoNotReferenceUnityEngine` enforces it. A violation
compiles fine inside Unity and breaks the entire .NET build, so the test is the
only thing that catches it early. Comments *may* mention engine types — the
scanner strips comments first.

**Never create a `.cs` file inside `core/HorrorGame.Core/`.** That directory holds
the `.csproj` and nothing else.

### What goes where

| Layer | Path | May reference Unity? | Purpose |
|---|---|:--:|---|
| **Core** | `Assets/Scripts/Core/` | ❌ | Rules, numbers, state machines. Pure functions and plain state. |
| **Gameplay** | `Assets/Scripts/Gameplay/` | ✅ | `MonoBehaviour`s that step core state and apply results to transforms. |
| **Net** | `Assets/Scripts/Net/` | ✅ | Mirror `NetworkBehaviour`s. Host authority. |
| **Steam** | `Assets/Scripts/Steam/` | ✅ | Steamworks.NET behind an interface. Must compile with the DLLs absent. |
| **Audio** | `Assets/Scripts/Audio/` | ✅ | Playback, 3D sources, occlusion, voice. |
| **UI** | `Assets/Scripts/UI/` | ✅ | Race HUD, lobby, menus, settings. No shop — §08 is deleted. |
| **Editor** | `Assets/Scripts/Editor/` | ✅ | Scene generation, map validation, build pipeline. Editor-only asmdef. |

If you are unsure whether something belongs in Core: **can it be tested without
an engine, and is it about a rule rather than a representation?** If yes, Core.

---

## 2. Style

Match the existing code — `GameConstants.cs`, `Vec3.cs`, `IRandomSource.cs` are
the reference.

- **C# 9.** `LangVersion` is pinned for Unity 6 compatibility. No file-scoped
  namespaces, no `record`, no raw strings, no list patterns.
- **Nullable enabled.** Annotate honestly; do not silence with `!` unless you can
  say why in a comment.
- Braces on their own line. Four spaces. `_camelCase` private fields.
- XML doc comments on every public member, and they explain **why**, citing the
  design section (`§06`) when the value or rule comes from it. Comments that
  restate the signature are noise — delete them.
- Korean is fine inside comments when quoting the design document. Identifiers
  stay English.

### Numbers

**Every tuned number lives in `GameConstants`.** No exceptions, including in
tests. If you need a new one, add it there with a `§` citation and extend
`GameConstants.Validate()` when it has a relationship worth guarding.

A literal like `4.8f` anywhere outside `GameConstants.cs` is a bug.

### Determinism

Core code must never touch `new Random()`, `DateTime.Now`, `Environment.TickCount`
or `UnityEngine.Random`. Take an `IRandomSource` and an explicit `float
deltaSeconds`. A seed must replay a match exactly — that is how a balance report
from a player becomes reproducible here. `FoundationTests` enforces this.

---

## 3. Seams between systems

Systems are written independently, so they couple through **primitives, not each
other's types**. This is the rule that lets ten people work at once.

Concretely: movement does not import the stance system to learn whether the player
is crouched. Stance exposes a `float` multiplier; movement multiplies by a `float`.

```
PlayerStance.SpeedMultiplier ──float──▶ MovementContext.LoadMultiplier
ThreatTier.MonsterSpeed      ──float──▶ MonsterBrain
IWorldProbe                  ──────────▶ MonsterBrain, map queries
IRandomSource                ──────────▶ anything that varies per match
ITelemetrySink               ──────────▶ anything worth measuring
```

> 🔴 **This example used to read `Inventory.SpeedMultiplier`.** `Inventory` and the
> whole of `Core/Economy/` went with §08 (game-design v1.1), so the *illustration*
> died — but the **seam did not**, and it is still load-bearing: `LoadMultiplier` is
> now the §05 stance multiplier, fed by `PlayerMotor.StanceMultiplier()` from
> `PlayerStance.SpeedMultiplier`, and core movement still knows nothing about the
> `MonoBehaviour` that produced it. Re-founded on what is true rather than deleted,
> because the rule the diagram teaches is the reason the file exists.

### Fixed signatures

These are depended on across systems. Produce them exactly as written; consume
them without redefining them.

```csharp
// Core/Movement/ — owner: movement
public struct MoveInput
{
    public float Forward;      // -1..1, +1 = W
    public float Strafe;       // -1..1, +1 = D
    public bool SprintHeld;
}

public struct MovementContext
{
    public float BaseSpeed;           // WalkSpeed 2.0 / RunSpeed 4.5 / RunnerSprintSpeed 5.6
    public float LoadMultiplier;      // §05 stance; 1 = upright. 0 on a default struct, deliberately
}
// 🔴 `CarryingObjective` and `BagEquipped` were DELETED with §03's objective and
// §08's economy. MovementContext.cs carries the reason in a comment: they were the
// last two ways one runner could be slower than another, and §11 starts twenty
// identical people. Re-adding either means re-adding a thing to carry.

public static class SpeedResolver
{
    /// Applies the §05 directional table, then the stance multiplier.
    public static float Resolve(MoveInput input, MovementContext context);

    /// The §05 multiplier alone, for tests and telemetry.
    public static float DirectionalMultiplier(MoveInput input);

    /// Picks walk / run / sprint. Sprint needs §04's stamina, not a role.
    public static float SelectBaseSpeed(MoveInput input, bool sprintUnlocked, bool staminaReady);
}

// Core/Threat/ — owner: threat
public readonly struct ThreatTier
{
    public float MonsterSpeed { get; }
    public int PatrolZoneCount { get; }
    public float StandstillChance { get; }
    public float FlashlightRangeMultiplier { get; }
    public bool MonsterKnowsExit { get; }
}

public static class ThreatCurve
{
    public static ThreatTier At(float elapsedSeconds);
}
```

Anything **not** on that list is private to its owner and may change freely.

### Stepping

Stateful core systems expose:

```csharp
public void Tick(float deltaSeconds);
```

They never read a clock themselves. The host drives them at
`GameConstants.FixedStep`.

---

## 4. Host authority (§13)

Mirror, host authority, no host migration.

> **Placement and arrival are decided on the host, and nowhere else.**

🔴 This section used to read "clue contents and the objective's location exist only
on the host". There is no clue and no objective any more — game-design §13 deleted
that row outright, because a race has no answer to hide. **The row was replaced, not
dropped**, and §13 says the swap is what best characterises v1.1: the host's job
moved from *concealing* to *adjudicating*.

So the constraint has the same teeth pointed at a different value. `RaceState`
(`Core/Race/`) owns `ReportDescent`, `ReportFinish`, `ReportCaught` and `Standings()`,
and only the host may call the reporting half. A client that can say "I arrived" is
the first value anyone forges in a racing game (§02). The HUD *reads* standings; per
§02 the screen side must not even have a method that can claim an arrival.

`ObjectiveResolver` is gone — do not reintroduce the name. It survives only as a
comment in `Gameplay/Match/MatchMap.cs` explaining what the 후보 지점 markers used
to feed.

Voice cuts off at `GameConstants.VoiceCutoffDistance` (30 m) **at the sender**
(§13). Receiving audio and muting it locally is trivially defeated.

---

## 5. Testing

| Suite | Where | Runs how | Covers | Size |
|---|---|---|---|---|
| **Core** | `core/HorrorGame.Core.Tests/` | `dotnet test` — no Unity needed | Every rule and number | **357 tests, ~1½ min** |
| **EditMode** | `Assets/Tests/EditMode/` | Unity Test Runner | Adapters, prefab wiring, generated scenes, pivot tombstones | 6 files |
| **PlayMode** | `Assets/Tests/PlayMode/` | Unity Test Runner | Movement feel, monster chases, networking, the race | 27 files |

> 🔴 **There was a fourth row: `core/HorrorGame.Sim/`, run by a `horrorsim` CLI, for
> balance sweeps over thousands of seeded matches.** The simulator was deleted with
> the co-op game at `e8c67ae` — the directory does not exist and the project is gone
> from `core/HorrorGame.sln`. Balance questions are now answered by the core suite
> and by `docs/BALANCE-FINDINGS.md`, not by a sweep. Do not cite `horrorsim`; there
> is nothing to run.

Core tests are the ones that must always be green — measured 2026-08-12 at `4ab204f`
and again at `017b489`: `dotnet test core/HorrorGame.sln` → **357 passed, 0 failed,
0 skipped**, in 1 m 29 s – 1 m 41 s across three runs. **It is not a three-second
suite**; quote the count, and treat the duration as a range.
Write them as **assertions about the design's own reasoning**, not as restatements
of the implementation:

```csharp
// Good — fails when the design's logic breaks.
Assert.That(sprintGain, Is.LessThan(GameConstants.AggroReleaseDistance),
    "§06: if one sprint could open the release distance, the map would stop mattering.");

// Useless — passes no matter how wrong the value is.
Assert.That(GameConstants.MonsterBaseSpeed, Is.EqualTo(GameConstants.MonsterBaseSpeed));
```

A system owns its own test file, named `<System>Tests.cs`. Do not edit another
system's test file. Two systems have earned a second file where one subject was
big enough to stand alone — `MonsterTests.cs` + `MonsterLungeTests.cs`, and
`MapTests.cs` + `MapSketchOffsetTests.cs`. That is the bar for splitting: a
distinct subject, not a long file.

Include the ugly cases: zero delta time, huge delta time (a frame spike must not
teleport the monster through a wall), empty inventory, an `IWorldProbe` that
reports nothing reachable, simultaneous state transitions.

---

## 6. Where a finding goes

The design document is a draft, and running its numbers surfaces contradictions
it does not acknowledge. Thirteen are already recorded in
`docs/BALANCE-FINDINGS.md` (F-001 … F-013), and twenty-one blockers in
`docs/BLOCKERS.md` (B-001 … B-021).

When you find another: **do not quietly "fix" the design.** Encode what the
document literally says, write a test that pins the actual consequence, and add
an entry to `docs/BALANCE-FINDINGS.md` stating the sections that disagree, the
arithmetic, and the options. Choosing between those options is the designer's
call, not ours.
