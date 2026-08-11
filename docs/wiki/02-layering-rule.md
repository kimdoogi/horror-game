# The layering rule, and why

> The one-sentence version: **the rules of this game are a plain .NET library that
> happens to live inside a Unity project, so `dotnet test` verifies the exact files
> Unity ships.**

The contract is [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) §1–§3. This page
explains what it buys, what it costs, and what breaks when you get it wrong.

---

## 1. The arrangement

```
unity/HorrorGame/Assets/Scripts/Core/**/*.cs      ← the rules. One copy. Ever.
        ▲                                    ▲
        │ compiled by Unity                  │ compiled by dotnet, via a glob in
        │ HorrorGame.Core.asmdef             │ core/HorrorGame.Core/HorrorGame.Core.csproj
        │ "noEngineReferences": true         │ TargetFramework netstandard2.1
```

The glob is literal, in `core/HorrorGame.Core/HorrorGame.Core.csproj`:

```xml
<CoreSourceRoot>$(MSBuildThisFileDirectory)../../unity/HorrorGame/Assets/Scripts/Core/</CoreSourceRoot>
...
<Compile Include="$(CoreSourceRoot)**/*.cs" />
```

There is **no DLL build step, no symlink, no copy** to drift out of sync. The csproj
also fails loudly rather than compiling an empty assembly if the layout moves:

```xml
<Target Name="VerifyCoreSourcesFound" BeforeTargets="CoreCompile">
  <Error Condition="'@(Compile)' == ''" Text="No core sources matched ..." />
</Target>
```

### What it buys

- **357 tests with no engine, no licence and no GPU.** Measured 2026-08-12 at
  `4ab204f`: `실패: 0, 통과: 357, 건너뜀: 0, 전체: 357, 기간: 1 m 41 s`.
  🔴 *This bullet used to promise 451 tests in 363 ms.* Both numbers moved: `e8c67ae`
  deleted the co-operative game and its tests, and the §12 map work that arrived after
  generates and validates whole buildings, which is where the ninety seconds went. The
  claim the arrangement actually makes is "no engine", not "fast".
- Every one of those tests is testing the file that ships. There is no "the DLL was
  rebuilt from an older branch" failure mode in this project.
- CI can protect §05, §06, §07, §02 and §12 on a free Linux runner with no Unity
  licence ([CI.md §2.1](../CI.md)). The licence-gated half of CI has never run; the
  rules half runs on every push. *(§08 was in this list and is deleted.)*

### The price — one absolute rule

> **No file under `Assets/Scripts/Core/` may reference `UnityEngine` or
> `UnityEditor`.** Not in a `using`, not fully qualified.

A violation **compiles perfectly inside the editor** and breaks the entire .NET
build. Whoever adds it will not notice. `FoundationTests.CoreSources_DoNotReferenceUnityEngine`
is the only thing that catches it early — it scans the source text with comments
stripped, so a comment may mention an engine type.

### Two corollaries people get wrong

- **Never create a `.cs` file inside `core/HorrorGame.Core/`.** That directory holds
  the `.csproj` and nothing else. A file there would be compiled by `dotnet` and
  never by Unity — the exact drift the arrangement exists to prevent.
- **`core/HorrorGame.Core.Tests/` is a normal directory with real `.cs` files in it.**
  It is not globbed from anywhere. One test file per system, named `<System>Tests.cs`
  — 15 of them on 2026-08-12 (Door, Foundation, Gunplay, Light, MapSketchOffset, Map,
  MonsterLunge, Monster, Movement, PivotDeletion, Presence, Race, Telemetry, Threat,
  Voice) plus three helpers that are not fixtures: `CSharpSource.cs`,
  `CoreSourceRootAttribute.cs`, `UnityDebugShim.cs`.

---

## 2. What breaks if you put a rule in a `MonoBehaviour`

Concretely, in the order you will discover them:

| # | What breaks | How it shows up |
|:--:|---|---|
| 1 | **The rule stops being testable without an engine.** | It now needs the editor, a licence and the project lock. In practice it means it stops being run before commits — and CI's engine-free half, the only half that has ever run, stops seeing it. |
| 2 | **The rule stops being deterministic.** | A `MonoBehaviour` reaches for `Time.deltaTime`, `UnityEngine.Random` and `Update()` ordering. A seed no longer replays a match, so a balance report from a player stops being reproducible here — §13's entire diagnosis loop. |
| 3 | **Nothing can sweep it.** | 🔴 *This row used to read "the balance simulator cannot see it — `core/HorrorGame.Sim` runs 500 matches in seconds because it drives core objects directly".* The simulator was deleted with the co-op game at `e8c67ae`; there is no `horrorsim` and nothing to run. **The cost is now higher, not lower:** a rule outside Core cannot be swept by anything at all, and balance questions are answered by the core suite and by [BALANCE-FINDINGS.md](../BALANCE-FINDINGS.md) reading it. `MapTests.Descent_*` is what a sweep looks like today — it builds and validates the whole shipped building inside `dotnet test`, which it can only do because the map rules are Core. |
| 4 | **The number leaks.** | Tuned values follow rules. A rule in a behaviour becomes a serialized field in a prefab, and now the authority for that number is a `.prefab` binary that no test reads and no reviewer diffs. |
| 5 | **The seam breaks.** | Systems couple through primitives (§3 below). A behaviour that owns a rule ends up importing another system's *type*, and independently-written systems stop compiling together. |
| 6 | **It cannot move to the host.** | §13 makes the host authoritative. Core objects are host-side state the host can step; a `MonoBehaviour` on a client prefab is not. |

**The test to apply, from ARCHITECTURE §1:** *can it be tested without an engine, and
is it about a rule rather than a representation?* If yes → Core.

### The shape a correct adapter has

`MonsterAgent` (`Assets/Scripts/Gameplay/Monster/MonsterAgent.cs`) is the reference.
Core owns `MonsterBrain`, which is pure: it holds a position, a state, a destination,
and asks an `IWorldProbe` questions. The Unity side owns:

- `NavMeshWorldProbe` — implements `IWorldProbe` against Unity's NavMesh.
- `MonsterAgent` — steps the brain at a fixed rate and copies the resulting position
  onto a `Transform`.
- `MonsterAnimationDriver`, `MonsterAudioDriver`, `MonsterFootsteps` — read the
  brain's state and turn it into presentation.

Nothing in that list decides *anything*. The state machine transitions, the 3-second
line-of-sight rule, the 12 m release distance, the 15 s search timeout are all in
`Assets/Scripts/Core/Monster/MonsterBrain.cs`, cited to §06 in the comments.

---

## 3. The seams — systems couple through primitives, never through each other's types

This is the rule that lets separately-written systems compile together
([ARCHITECTURE §3](../ARCHITECTURE.md)). Movement does not import the stance system to
learn whether the player is crouched; stance exposes a `float` and movement multiplies
by a `float`.

```
PlayerStance.SpeedMultiplier ──float──▶ MovementContext.LoadMultiplier
ThreatCurve.MonsterSpeed     ──float──▶ MonsterBrain
IWorldProbe                  ──────────▶ MonsterBrain, map queries, audio occlusion
IRandomSource                ──────────▶ anything that varies per match
ITelemetrySink               ──────────▶ anything worth measuring
```

> 🔴 **The first line used to read `Inventory.SpeedMultiplier`.** `Inventory` and the
> whole of `Core/Economy/` went with §08 — there is no `class Inventory` anywhere in
> `unity/` or `core/`. **The seam did not go with it**, and it is still load-bearing:
> `LoadMultiplier` is now the §05 *stance* multiplier, fed by
> `PlayerMotor.StanceMultiplier()` from `PlayerStance.SpeedMultiplier`, and core
> movement still knows nothing about the `MonoBehaviour` that produced it. Re-founded
> on what is true rather than deleted, because the rule the diagram teaches is the
> reason this page exists.

The fixed signatures — `MoveInput`, `MovementContext`, `SpeedResolver`, `ThreatTier`,
`ThreatCurve` — are listed verbatim in ARCHITECTURE §3. **Produce them exactly as
written; consume them without redefining them.** Anything not on that list is private
to its owner and may change freely. `MovementContext` now has exactly two fields,
`BaseSpeed` and `LoadMultiplier`; `CarryingObjective` and `BagEquipped` were deleted
with §03's objective and §08's economy, and `MovementContext.cs` carries the reason in
a comment — they were the last two ways one runner could be slower than another, and
§11 starts twenty identical people.

Stateful core systems expose `public void Tick(float deltaSeconds)` and never read a
clock. The host drives them at `GameConstants.FixedStep` (1/50 s).

`MonsterBrain` is the example of the discipline that is still in the tree: it is handed
an `IWorldProbe` and an `IRandomSource`, holds a position, a state and a destination,
and names no Unity type and no other system's type anywhere. Aggro *release* lives
inside it and nowhere else, because §06 makes that the monster's decision rather than
the runner's.

---

## 4. Determinism

Core code must never touch `new Random()`, `DateTime.Now`, `Environment.TickCount` or
`UnityEngine.Random`. Take an `IRandomSource` and an explicit `float deltaSeconds`.

Why it is not a style preference: §13 plans telemetry as "판 종료 시 요약 1건 전송",
and the whole point is that a player's match summary can be replayed here. Prove it
still works with:

```bash
dotnet test core/HorrorGame.sln --filter "FullyQualifiedName~DeterministicRandom|FullyQualifiedName~Descent_IsDeterministic"
# 2026-08-12: 실패: 0, 통과: 6, 건너뜀: 0, 전체: 6 — if it selects fewer than 6, the
# filter has stopped matching and a green run means nothing (CI.md §0.3).
```

`FoundationTests.CoreSources_DoNotUseAmbientRandomnessOrClock` enforces the
source-level half; `DeterministicRandom_SameSeed_SameSequence` and
`MapTests.Descent_IsDeterministic_AndTwoSeedsAreNotTheSameBuilding` enforce the
behavioural half.

> 🔴 **This block used to say `dotnet run -c Release --project core/HorrorGame.Sim --
> replay --seed 42 --times 3`.** `core/HorrorGame.Sim/` was deleted at `e8c67ae` and
> there is no `horrorsim` CLI — do not cite it. The *determinism requirement* was never
> the simulator's; it is what makes the simulator possible, and it outlived it.

---

## 5. Tests are assertions about the design's reasoning, not about the implementation

From ARCHITECTURE §5, and it is the difference between a suite that catches a
regression and one that just fails to compile:

```csharp
// Good — fails when the design's logic breaks.
Assert.That(sprintGain, Is.LessThan(GameConstants.AggroReleaseDistance),
    "§06: if one sprint could open the release distance, the map would stop mattering.");

// Useless — passes no matter how wrong the value is.
Assert.That(GameConstants.MonsterBaseSpeed, Is.EqualTo(GameConstants.MonsterBaseSpeed));
```

Include the ugly cases: zero delta time, a frame spike large enough to teleport the
monster through a wall, a default-constructed `MovementContext` (whose
`LoadMultiplier` is 0, deliberately), an `IWorldProbe` that reports nothing reachable,
simultaneous transitions.

Each system owns exactly one test file. **Do not edit another system's test file.**

---

## 6. Read before changing

| Before you change | Read | Then run |
|---|---|---|
| anything under `Assets/Scripts/Core/` | [ARCHITECTURE §1–§3](../ARCHITECTURE.md) | `dotnet test` — and check `건너뜀: 0` |
| a fixed signature in ARCHITECTURE §3 | ARCHITECTURE §3, then every consumer | `dotnet build core/HorrorGame.sln -c Release` |
| the csproj glob or the asmdef | this page | `dotnet build`, then a Unity compile check |
| where a new file goes | [Where a file may live](04-where-code-lives.md) | Unity compile check |
