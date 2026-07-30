# Architecture — the contract

> Read this before writing a line of code in this repo. It is the agreement that
> keeps independently written systems compiling together.
>
> The design document (`docs/game-design.md`, v0.5) is the authority on *what the
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
| **UI** | `Assets/Scripts/UI/` | ✅ | HUD, shop, lobby. |
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

Concretely: movement does not import the economy to learn about carry weight. The
economy exposes a `float` multiplier; movement multiplies by a `float`.

```
Inventory.SpeedMultiplier ──float──▶ MovementContext.LoadMultiplier
ThreatCurve.MonsterSpeed  ──float──▶ MonsterBrain
IWorldProbe               ──────────▶ MonsterBrain, abilities, map queries
IRandomSource             ──────────▶ anything that varies per match
ITelemetrySink            ──────────▶ anything worth measuring
```

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
    public float BaseSpeed;           // WalkSpeed / RunSpeed / RunnerSprintSpeed
    public float LoadMultiplier;      // from Inventory.SpeedMultiplier
    public bool CarryingObjective;
    public bool BagEquipped;
}

public static class SpeedResolver
{
    /// Applies the §05 directional table, then load, then carry state.
    public static float Resolve(MoveInput input, MovementContext context);

    /// The §05 multiplier alone, for tests and telemetry.
    public static float DirectionalMultiplier(MoveInput input);
}

// Core/Economy/ — owner: economy
public sealed class Inventory
{
    public int TotalWeight { get; }
    public float SpeedMultiplier { get; }   // §08 bands
    public bool CanSprint { get; }          // false at weight ≥ 16
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

> **Clue contents and the objective's location exist only on the host.**

§03's constraint — see it, remember it, say it out loud — dies the moment the
answer is in a client's memory. Sending it "but only showing it when close" is
the same as sending it.

So: a client asks the host "am I reading a clue?", and the host replies with the
rendered glyph for *that* clue only. `ObjectiveResolver` and the clue tables are
host-side types; a `NetworkBehaviour` must never serialise them.

Voice cuts off at `VoiceCutoffDistance` **at the sender** (§13). Receiving audio
and muting it locally is trivially defeated.

---

## 5. Testing

| Suite | Where | Runs how | Covers |
|---|---|---|---|
| **Core** | `core/HorrorGame.Core.Tests/` | `dotnet test` — no Unity needed | Every rule and number |
| **EditMode** | `Assets/Tests/EditMode/` | Unity Test Runner | Adapters, prefab wiring, generated scenes |
| **PlayMode** | `Assets/Tests/PlayMode/` | Unity Test Runner | Movement feel, monster chases, networking |
| **Simulator** | `core/HorrorGame.Sim/` | `horrorsim` CLI | Balance sweeps over thousands of seeded matches |

Core tests are the ones that must always be green. Write them as **assertions
about the design's own reasoning**, not as restatements of the implementation:

```csharp
// Good — fails when the design's logic breaks.
Assert.That(sprintGain, Is.LessThan(GameConstants.AggroReleaseDistance),
    "§06: if one sprint could open the release distance, the map would stop mattering.");

// Useless — passes no matter how wrong the value is.
Assert.That(GameConstants.MonsterBaseSpeed, Is.EqualTo(GameConstants.MonsterBaseSpeed));
```

Each system owns exactly one test file, named `<System>Tests.cs`. Do not edit
another system's test file.

Include the ugly cases: zero delta time, huge delta time (a frame spike must not
teleport the monster through a wall), empty inventory, an `IWorldProbe` that
reports nothing reachable, simultaneous state transitions.

---

## 6. Where a finding goes

The design document is a draft, and running its numbers surfaces contradictions
it does not acknowledge. One is already recorded in
`docs/BALANCE-FINDINGS.md`.

When you find another: **do not quietly "fix" the design.** Encode what the
document literally says, write a test that pins the actual consequence, and add
an entry to `docs/BALANCE-FINDINGS.md` stating the sections that disagree, the
arithmetic, and the options. Choosing between those options is the designer's
call, not ours.
