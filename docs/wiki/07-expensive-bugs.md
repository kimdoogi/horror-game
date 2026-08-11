# The bugs that cost the most, and what they taught

> Two bugs in this project's history each cost about a day. Neither produced an error
> message pointing at its cause. Both are recorded here because the *lesson* is
> reusable and the next reader is going to meet the same shape of problem.
>
> **This page is deliberately history.** Both entries describe the three-storey
> co-operative building that the 2026-08-02 pivot replaced, and that is the correct tense
> for them — a bug is allowed to have happened to a game that no longer exists. What has
> been re-checked at HEAD `4ab204f`..`017b489` on 2026-08-12 is every *live* claim: the files the
> fixes live in, the commands you are told to run, and the "still open" table at the
> bottom. Where the artefact had moved underneath a sentence, the sentence moved.

---

## B-001 · A fragmented NavMesh silently deleted the game's antagonist

**Status: 🟢 closed 2026-07-31.** The full write-up, with every measurement, is
[`docs/BLOCKERS.md` B-001](../BLOCKERS.md#b-001). Read it in full before touching
monster movement, the map kit's stairwells, or anything that produces a NavMesh.

### What it looked like

Nothing. No exception, no warning, no `LogError`. The monster stood at
`(26.8, −5.4, 36.5)` for **220 consecutive seconds** while a player waited 95 m away.
The chase test's output was:

```
chase entered   never
NOT REACHED in 240.00 s
worst 1 s rise  0.0 m
```

Read as bad AI, or as balance, or as "the monster's patrol route is wrong". It was
none of those. **§14 says question 1 — 「추격이 재밌는가?」 — decides the project, and
the game had no antagonist at all.**

### Why every gate was green while it was broken

This is the part that matters. The NavMesh audit reported:

```
[NavMeshAudit] PASS
  pairs 630 · complete 630 (100.0 %, need 98 %) · islands 1 · monster reach 19/19
```

**That output was produced while the monster was frozen.** The reason is a difference
between two questions that look like one question:

- The audit asks `NavMesh.CalculatePath(a, b)` — *is there a path?*
- The monster walks `NavMeshPath.corners` one at a time — *can I take the next step?*

A `NavMeshLink` answers the first and not the second. The map's three storeys were
joined by links, so connectivity was perfect and traversal was impossible.

### The two halves of the fix, and why both were needed

**1 · Stairs are walkable geometry; the links are gone.**
`tools/blender/gen_mapkit.py`'s `build_stairwell` no longer runs the dog-leg spine the
full depth of the mid-landing, so both flights bake as one surface.
`MapSceneBuilder.ForbidStairLinks` now **deletes** any `NavMeshLink` it finds, and
`MapSceneBuilder.VerifyStairwellsAreWalkable` **fails the generation** if a shaft
bakes as more than one island. Both still exist and both still run — `MapSceneBuilder.cs`
calls them back to back (re-read 2026-08-12). Confirm with
`grep -c NavMeshLink unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity` → `0`, which
is still `0` today.

> **The guard outlived the geometry it was built for, and that is the interesting part.**
> The descent map has no 계단 at all: `RadialStorey` moves you between storeys through a
> 투하구 you fall down, so the stairwell this bug was about is gone from the building.
> `ForbidStairLinks` is still worth its keep because it forbids a *class* of object rather
> than a piece of a kit — a `NavMeshLink` is a gap with nothing to step onto no matter
> what draws it — and because eight one-way storeys mean the audit now reports
> `islands 8` by design, which is precisely the reading in which a stray link would hide
> best.

This half fixes the player too, and that is the deeper point: **a `NavMeshLink` is a
gap with nothing to step onto, and a human being cannot use one at all.** The map had
storeys the player could not reach either.

**2 · The probe no longer deadlocks on a duplicated path corner.**
`NavMeshWorldProbe.TryGetNextPathPoint` returned `_corners[1]` unconditionally. At the
mouth of a link, `CalculatePath` emits `corners[0]` and `corners[1]` at the same
position — so the brain got a waypoint it was already standing on, measured the
distance as within `MonsterWaypointTolerance`, "arrived" without moving, and asked
again. Forever.

It now returns the first corner further than `MinWaypointAdvanceSqr` (0.09 m²) from
the mover, falling back to the last corner when every corner is coincident. Both are
still in `Assets/Scripts/Gameplay/Monster/NavMeshWorldProbe.cs` at HEAD —
`TryGetNextPathPoint` at line 205, the constant at line 258 — and the comment above the
loop is where the reason is written down:

> "Distinct" is load-bearing, not defensive tidying. … That deadlocked the monster 95 m
> from the player on a path the audit reported as complete, and it produced no error of
> any kind — see `docs/BLOCKERS.md` B-001.

The same comment gives the clause that is nothing to do with links, and it is why the fix
outlives them: corner 0 is the point being left, so aiming at the next *distinct* corner
is **"the reason a 4.8 m/s creature cannot cut the chord of §12's S-corridor."**

### What it measured when it closed, and what it measures now

At the close, on the three-storey building:

```
[ChaseTest]   route            133.9 m of NavMesh path, monster spawn → (33.75, 0.18, 71.25)
[ChaseTest]   straight line    60.1 m
[ChaseTest]   reached          27.52 s
[ChaseTest]   closing speed    4.83 m/s of route, against §06's 4.8 m/s of ground speed
[ChaseTest]   worst 1 s rise   0.0 m of route (0 is a monster that never backtracked)
```

`MonsterSpawn (36.25, −7.50, 11.25)` on B3 to `PlayerSpawn_2 (33.75, 0.00, 71.25)` on
B1 — 7.5 m of vertical across two storey boundaries.

Today, on the eight-storey radial building ([STATUS.md §3](../STATUS.md), owner's gate
2026-08-10, `MonsterChaseTests` 4/4):

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
[ChaseTest]   route            128.7 m of NavMesh path
[ChaseTest]   reached          26.64 s
[ChaseTest]   closing speed    4.79 m/s of route, against §06's 4.8 m/s of ground speed
[ChaseTest]   worst 1 s rise   0.0 m of route (0 is a monster that never backtracked)
```

**Read the question in the first line.** It is *"on its own storey"* now, because the
creature cannot climb a 투하구 — the vertical clause that made the original bug possible no
longer exists to be tested. The two control corridors still reproduce §06's central claim
to 1 %: single corner `caught 12.54 s`, two 10 m legs `released 5.50 s at 12.0 m`.

### The four lessons

1. **A necessary check is not a sufficient check, and it will not tell you which it
   is.** The audit remains valuable and remains insufficient. STATUS.md now says so
   next to the output.
2. **Test the thing the game does, not the thing the engine offers.** The engine
   offers `CalculatePath`. The game does `corners[i]`. Only the second is evidence.
3. **Silence is a failure mode.** The most expensive bugs here have produced zero log
   output. When you write a system, make the impossible state *say something* —
   `VerifyStairwellsAreWalkable` failing the generation is worth more than any
   documentation of the rule.
4. **A fix that only helps the AI is half a fix.** Ask whether a human could use the
   thing you built.

### The rule that follows

> **Never let the NavMesh audit stand in for a chase test again.** Run both. The
> audit is the fast gate; `MonsterChaseTests` is the answer.

---

## The `manifest.json` incident · 180 errors that all looked like Mirror's fault

**Status: fixed before the first commit.** No blocker entry exists, which is why it
is written down here.

### What it looked like

A Unity compile produced roughly **180 `error CS` lines**, and essentially all of
them named Mirror types — `NetworkBehaviour`, `SyncVar`, `NetworkServer`,
`[Command]`. Every visible symptom pointed at "the Mirror install is broken", which
is a plausible and completely wrong diagnosis that can absorb a day: reinstalling the
package, changing the version, switching to the OpenUPM registry, trying FishNet.

### What it actually was

`Packages/manifest.json` was missing Unity's **built-in module packages** —
`com.unity.modules.*`. Those are not third-party dependencies; they are the engine's
own subsystems (physics, audio, animation, UI, `unitywebrequest`, …), delivered as
packages. Without them the engine assemblies they provide are absent, so:

- Mirror's own code fails to compile against the missing engine assemblies;
- every error surfaces *inside Mirror's files*, because that is where the
  compiler was when it ran out of types;
- the project's own scripts then fail because Mirror failed;
- and the count is large enough that nobody reads to the bottom of the list.

**The error location and the error cause were in different packages.** That is the
whole trap.

### What the file looks like now

`unity/HorrorGame/Packages/manifest.json` explicitly lists **34** `com.unity.modules.*`
entries alongside **nine** non-module dependencies — three genuinely third-party (Mirror
`96.6.4`, Steamworks.NET `2025.164.0`, FizzySteamworks `6.0.1`) and six of Unity's own
(AI Navigation, Input System, Multiplayer Center, URP, Test Framework, uGUI). Verify, both
still true 2026-08-12:

```bash
grep -c 'com.unity.modules' unity/HorrorGame/Packages/manifest.json      # 34
```

Its companion rule is written down in
`Assets/Scripts/Editor/PackageBootstrap.cs`, and it is the reason the manifest holds
*only* third-party versions:

> `Packages/manifest.json` deliberately lists only third-party dependencies, where
> the version is a real decision (Mirror, Steamworks.NET, FizzySteamworks). Unity's
> own packages are added from here **without** a version, so the editor resolves
> whatever is correct for itself. […] Hand-pinning Unity package versions in the
> manifest is how a project ends up refusing to open.

`Horror ▸ Setup ▸ Install Required Packages` (`PackageBootstrap.InstallRequired`, also
available as `InstallRequiredBatch` for CI) installs the four that carry a gameplay
justification — Input System, AI Navigation, Test Framework, URP — each with the
reason recorded in the source.

### The three lessons

1. **Read the first error, not the loudest one.** 180 errors are one error with 179
   consequences. Sort by file and look at what compiled *before* the flood.
2. **When every error is inside a dependency, suspect the environment, not the
   dependency.** A published package with 180 compile errors in it would not be
   published.
3. **Record the version decision and its reason next to the file it constrains.**
   `PackageBootstrap`'s doc comment is what stops the next person "tidying" the
   manifest by pinning Unity's own packages, which reintroduces a different version
   of the same day.

---

## The pattern both share

| | B-001 | manifest.json |
|---|---|---|
| What the evidence pointed at | the monster's AI | Mirror |
| Where the cause was | the navigation surface, one layer down | the package manifest, one layer up |
| Why it was expensive | the check that should have caught it reported PASS | the errors were numerous, consistent, and all in the wrong file |
| The generalisable move | **ask what the check actually asks** | **ask what compiled last, not what failed loudest** |

Both are instances of one habit worth keeping: **when a system fails silently or
uniformly, stop debugging the symptom and go and read what the passing check is
actually measuring.**

---

## Still open, and cheaper only because they are known

Re-checked against `docs/BLOCKERS.md` on 2026-08-12. **The three rows this table used to
carry are all closed or dormant** — they are kept below the live ones so nobody re-opens
them.

| | Summary | Where |
|---|---|---|
| B-004 | The networking library in the package cache is a stranger's repack of Mirror, not Mirror. It is load-bearing and it blocks release | [BLOCKERS.md](../BLOCKERS.md#b-004) |
| B-015 | There is no shippable build — IL2CPP will not compile for Windows on a macOS host, and Windows is what §13 ships to | [BLOCKERS.md](../BLOCKERS.md#b-015) |
| B-019 | Every storey is 7.5–42.5 m too short from the rim to the middle. §12-D's `centre-path` gates on it, fails, and is waived by name in `MapSceneGenerator.KnownFailingRules` | [BLOCKERS.md](../BLOCKERS.md#b-019) |
| B-021 | The deepest floor is the brightest room, and for ten days nothing had ever photographed it | [BLOCKERS.md](../BLOCKERS.md#b-021) |
| B-013 | The *process* half is still open: three commits whose messages said the suite was green while CI was red. The arithmetic half closed in `1ceb636` — the `floor=512` that outlived the 155 tests the pivot deleted is `floor=357` now | [BLOCKERS.md](../BLOCKERS.md#b-013) |

**Closed since this table was written:**

| | Now |
|---|---|
| B-002 · one EditMode test red on a missing `.meta` in the Mirror package cache | 🟡 **dormant.** It stopped reproducing when the package cache was rewritten, and `SoloMatchLoopTests.cs` no longer exists at HEAD — it drove §01's 왕복, which the pivot deleted. EditMode has since run: **95/95**, 2026-08-08 |
| B-003 · two `HallOpen20x20` rooms dropped at `LogError` on every map generation | 🟢 **closed by the pivot**, 2026-08-03. `DescentMap`/`RadialStorey` place no `HallOpen20x20` at all, so the piece and its error went together. **The complaint underneath it did not close** — it moved up a level to [B-014](../BLOCKERS.md#b-014): the *checklist* now prints `FAIL` on the happy path, so "the log is clean" still cannot be a gate |
| defect 3.8 · the floor-material chain is wired and not pinned | 🔴 **the section it cited is gone.** STATUS.md has no §3a and no numbered defect list any more; its §3 is now "gates that were re-run, and gates that were not". The *worry* is still the right worry — a chain that only fake probes exercise can break as quietly as B-001 did — but it no longer has an address, and a lesson without an address is how one goes quiet |
