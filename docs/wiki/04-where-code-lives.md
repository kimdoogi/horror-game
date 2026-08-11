# Where a file may live

> **Assembly layout decides where a file can go, and the folder decides the
> assembly.** Namespaces do not. Put a file in the wrong folder and it either fails
> to see the type it needs, or — worse — compiles and drags a dependency into an
> assembly that was deliberately kept clean.

Regenerate everything on this page from the repository at any time:

```bash
find unity/HorrorGame/Assets/Scripts unity/HorrorGame/Assets/Tests -name '*.asmdef' | sort
```

`Assets/Tests/` is half the answer and the old form of this command left it out.

---

## 1. The rule Unity actually applies

An `.asmdef` claims **its own folder and every subfolder**, until a deeper `.asmdef`
claims a subtree back. A folder with no `.asmdef` ancestor falls into Unity's
predefined assemblies: `Assembly-CSharp`, or `Assembly-CSharp-Editor` if any
path segment is literally named `Editor`.

Two consequences that surprise people in this repository:

- `Assets/Scripts/Gameplay/` **does** have an asmdef of its own, `HorrorGame.Gameplay`,
  and it claims every subfolder that has not claimed itself back — so `Gameplay/Match`,
  `Gameplay/Interaction`, `Gameplay/Race`, `Gameplay/Startle`, `Gameplay/Voice` and
  `Gameplay/Playtest` are all in it. Five subtrees claim themselves back:
  `Player`, `Player/Editor`, `Monster`, `Monster/Editor`, `Presence`,
  `Presence/Editor` and `Audio/Rig`.
- So `Gameplay/Audio/MatchAudioBridge.cs` and `MatchAudioBootstrap.cs` are in
  `HorrorGame.Gameplay`, while `Gameplay/Audio/Rig/MatchAudioRig.cs` is in
  `HorrorGame.Gameplay.Audio`. Two assemblies, one folder apart.

> 🔴 **This section used to say `Gameplay/` had no asmdef and that `MatchDirector` and
> friends lived in `Assembly-CSharp`.** They did; they do not now. **Measured
> 2026-08-12, no `.cs` file under `Assets/Scripts/` lands in `Assembly-CSharp` at
> all** — the only predefined-assembly residents left are `Assets/Scripts/Editor/`
> and its five un-asmdef'd subfolders (38 files, `Assembly-CSharp-Editor`) and four
> PlayMode test folders under `Assets/Tests/`. Several doc comments in the tree still
> say otherwise; see §2 below.

`Assembly-CSharp` automatically references every asmdef with `autoReferenced: true`,
which is all of them except `HorrorGame.EditorTools.TestRunner` and the ten test
assemblies. That is why the un-asmdef'd *test* folders can see everything, and why
moving a file *into* an asmdef is the step that usually breaks the build — the asmdef
sees only what it lists.

---

## 2. The ownership table

Computed from the `.asmdef` files on **2026-08-12 at `4ab204f`** — 21 under
`Scripts/`, 10 under `Tests/`. **The folder→assembly mapping is stable; the
"References" column is a snapshot** — an assembly gains references as the code grows,
and the `.asmdef` is always the authority. Recompute the whole table with:

```bash
find unity/HorrorGame/Assets/Scripts -name '*.asmdef' | sort | while read f; do
  python3 -c "import json,sys; d=json.load(open('$f')); print(d['name'], '<-', '$f'); print('   ', d.get('references'))"
done
```


| Folder under `Assets/Scripts/` | Assembly | References | Editor-only |
|---|---|---|:--:|
| `Core/**` | `HorrorGame.Core` | **none**, `noEngineReferences: true` | |
| `Audio/` | `HorrorGame.Audio` | Core, Steam | |
| `Rendering/` | `HorrorGame.Rendering` | Core, URP runtime | |
| `UI/**` | `HorrorGame.UI` | Core, Audio, Gameplay.Player, UnityEngine.UI, InputSystem, URP | |
| `Net/`, `Net/Host/`, `Net/Interest/`, `Net/Race/` | `HorrorGame.Net` | Core, Steam, Mirror + Components + Transports | |
| `Net/PlayerBridge/` | `HorrorGame.Net.PlayerBridge` | Core, Net, Gameplay.Player, Mirror | |
| `Net/SteamTransport/` | `HorrorGame.Net.SteamTransport` | Net, Mirror, FizzySteamworks | platform-limited |
| `Steam/`, `Steam/Abstractions/`, `Steam/Offline/`, `Steam/Voice/` | `HorrorGame.Steam` | Core | |
| `Steam/Steamworks/` | `HorrorGame.Steam.SteamworksBackend` | Core, Steam, Steamworks.NET | platform-limited |
| `Steam/Editor/` | `HorrorGame.Steam.Editor` | Core, Steam | ✅ |
| **`Gameplay/`** and every subfolder not listed below | `HorrorGame.Gameplay` | Core, Audio, Gameplay.Audio, Gameplay.Monster, Gameplay.Player, Net, Steam, UI, Mirror, InputSystem, URP, UnityEngine.UI | |
| `Gameplay/Player/` | `HorrorGame.Gameplay.Player` | Core, Rendering, Audio, Unity.InputSystem, URP | |
| `Gameplay/Player/Editor/` | `HorrorGame.Gameplay.Player.Editor` | Core, Rendering, Gameplay.Player, InputSystem, URP | ✅ |
| `Gameplay/Monster/` | `HorrorGame.Gameplay.Monster` | Core, Audio | |
| `Gameplay/Monster/Editor/` | `HorrorGame.Gameplay.Monster.Editor` | Core, Gameplay.Monster, Rendering, URP | ✅ |
| `Gameplay/Presence/` | `HorrorGame.Gameplay.Presence` | Core, Gameplay.Monster | |
| `Gameplay/Presence/Editor/` | `HorrorGame.Gameplay.Presence.Editor` | Core, Gameplay.Presence, Rendering, URP | ✅ |
| `Gameplay/Audio/Rig/` | `HorrorGame.Gameplay.Audio` | Core, Audio | |
| `Editor/SceneGen/` | `HorrorGame.EditorTools.SceneGen` | Core, Gameplay.Player, Gameplay.Player.Editor, Rendering, Unity.AI.Navigation, UnityEngine.UI | ✅ |
| `Editor/Dressing/` | `HorrorGame.EditorTools.Dressing` | Core, SceneGen, Unity.AI.Navigation | ✅ |
| `Editor/BuildPipelineTestRunner/` | `HorrorGame.EditorTools.TestRunner` | TestRunner only — **`autoReferenced: false`** | ✅ |
| `Editor/` root + `Editor/Audio`, `Playtest`, `Props`, `Rendering`, `TextureImport` | `Assembly-CSharp-Editor` | everything | ✅ |

There is **no `HorrorGame.UI.Editor`** and no `Assets/Scripts/UI/Editor/` folder —
that row was in this table and neither the assembly nor the directory exists.

Tests are separate again, under `Assets/Tests/`, each with
`defineConstraints: ["UNITY_INCLUDE_TESTS"]` and `autoReferenced: false`. **Ten
assemblies, not seven:**

| Test assembly | Platform | Sees |
|---|---|---|
| `HorrorGame.Tests.EditMode.Audio` | Editor | Core, Audio |
| `HorrorGame.Tests.EditMode.Pivot` | Editor | **nothing but the test runner** — the tombstone tests assert that deleted things are *absent*, so they must not be able to name them |
| `HorrorGame.Tests.EditMode.UI` | Editor | Core, UI, Audio, Gameplay.Player, InputSystem |
| `HorrorGame.Tests.PlayMode.Audio` | any | Core, Audio, Gameplay.Audio |
| `HorrorGame.Tests.PlayMode.Monster` | any | Core, Gameplay.Monster, Unity.AI.Navigation |
| `HorrorGame.Tests.PlayMode.Net` | any | Core, Net, Net.PlayerBridge, Gameplay, Gameplay.Player, Steam, UI, Mirror, kcp2k |
| `HorrorGame.Tests.PlayMode.Player` | any | Core, Gameplay.Player, Audio, Unity.InputSystem |
| `HorrorGame.Tests.PlayMode.Presence` | any | Core, Gameplay.Presence, Gameplay.Monster |
| `HorrorGame.Tests.PlayMode.UI` | any | Core, UI |
| `HorrorGame.Tests.PlayMode.Voice` | any | Core, Gameplay, Net, Steam, Audio, Mirror, kcp2k |

Four PlayMode test folders live *outside* those assemblies, in `Assembly-CSharp`,
because they have no `.asmdef` at all: **`Tests/PlayMode/Interaction/` (3 files),
`Match/` (2), `Race/` (4) and `Startle/` (1)**.

> 🔴 **This paragraph used to name three *editor* test files as the forced ones —
> `Gameplay/Match/Editor/SoloMatchLoopTests.cs`,
> `Editor/Playtest/MatchGuidanceTests.cs` and
> `Gameplay/Interaction/Editor/DropPlacementTests.cs`. None of the three exists**;
> `Gameplay/Match/Editor/` and `Gameplay/Guidance/` are empty directories and
> `Gameplay/Interaction/Editor/` is gone. `MonsterKillTests` and `RaceDirectorTests`
> still explain themselves with "lives in the predefined assembly because
> `MatchDirector` / `RaceDirector` does, and an `.asmdef` cannot reference one" — that
> reason **expired** when `Gameplay/` got its own asmdef; both types are in
> `HorrorGame.Gameplay` now and could be referenced normally. The arrangement still
> compiles, so nothing is broken; it is just no longer forced.

Nothing leaks into a shipped player either way: Unity strips `UNITY_INCLUDE_TESTS`
code from predefined assemblies when it builds one. *(The old wording here cited a
grep of the macOS IL2CPP player's `global-metadata.dat`. That artefact is not on this
machine — `dist/` currently holds a **Development Mono** macOS build from 2026-08-10,
which has no `global-metadata.dat` — so treat the claim as unverified until an IL2CPP
player is built again.)*

---

## 3. Choosing a home — the decision list

Work top to bottom and stop at the first match.

1. **Is it a rule, a number, or a state machine that could be tested with no
   engine?** → `Assets/Scripts/Core/<System>/`, namespace `HorrorGame.Core.<System>`.
   It may not mention `UnityEngine`. See [the layering rule](02-layering-rule.md).
2. **Is it an `AssetPostprocessor`, a scene generator, a validator, a menu item or a
   batch entry point?** → under `Assets/Scripts/Editor/`. Pick `SceneGen/` or
   `Dressing/` only if it belongs to those systems; otherwise the `Editor/` root,
   which is `Assembly-CSharp-Editor` and can see everything.
3. **Does it touch Mirror?** → `Assets/Scripts/Net/`. Host authority applies — read
   [Design decisions §3](08-design-decisions.md) before adding a field.
4. **Does it touch Steamworks.NET?** → `Assets/Scripts/Steam/Steamworks/` behind one
   of the interfaces in `Steam/Abstractions/`. It **must compile with the DLLs
   absent**: that assembly carries `defineConstraints: ["HORRORGAME_STEAMWORKS"]`
   and a `versionDefines` entry keyed on the `com.rlabrecque.steamworks.net` package,
   so the whole assembly vanishes when the package is not installed and the offline
   implementations in `Steam/Offline/` take over. The same pattern guards
   `Net/SteamTransport/` with `HORRORGAME_FIZZYSTEAMWORKS`, keyed on
   `com.mirror.steamworks.net`.
5. **Is it playback, occlusion, footsteps or voice output?** → `Assets/Scripts/Audio/`.
6. **Is it a HUD, shop or lobby screen?** → `Assets/Scripts/UI/`.
7. **Is it a `MonoBehaviour` that steps core state onto a transform?** →
   `Assets/Scripts/Gameplay/<System>/`, namespace `HorrorGame.Gameplay.<System>`.
   Every folder here now lands in an asmdef — `HorrorGame.Gameplay` by default, or one
   of the five that claim themselves back (`Player`, `Monster`, `Presence`,
   `Audio/Rig`, and their `Editor` siblings). Find which from the table above, add your
   reference to *that* `.asmdef` in the same edit, and expect a compile error the
   moment you reach for a type it does not list. **A new subfolder of `Gameplay/` is
   the well-referenced default; a new asmdef is a decision, not a formality.**

---

## 4. The failure modes, in the order you will hit them

| Symptom | Cause | Fix |
|---|---|---|
| `The type or namespace name 'X' could not be found` in the editor only | the file landed in an asmdef that does not reference X's assembly | add the reference to the `.asmdef`, or move the file |
| The .NET build breaks and Unity is fine | a `using UnityEngine` reached `Assets/Scripts/Core/` | delete it; the file belongs in `Gameplay/` |
| A test cannot see the class it tests | test asmdefs are `autoReferenced: false` and list their references explicitly | add the assembly to the test's `.asmdef` |
| A new file cannot see `MatchDirector` | it is in `HorrorGame.Gameplay` now — your assembly does not reference it | add `HorrorGame.Gameplay` to your `.asmdef`. 🔴 *This row used to say `MatchDirector` was in `Assembly-CSharp` and therefore unreachable; that stopped being true when `Gameplay/` got its own asmdef, and several doc comments in the tree have not caught up* |
| A PlayMode test in `Tests/PlayMode/{Interaction,Match,Race,Startle}` cannot see something | those four folders have no `.asmdef`, so they are in `Assembly-CSharp` and see everything auto-referenced — if one *cannot* see a type, that type is in an assembly with `autoReferenced: false` | give the folder an `.asmdef` that lists it |
| Everything compiles, but the Steam build breaks | a file in `Steam/` (no constraint) used a type from `Steam/Steamworks/` (constrained) | go through `Steam/Abstractions/` |

The rule worth remembering is still the asymmetry: `Assembly-CSharp` sees every
auto-referenced asmdef, but **no asmdef can see `Assembly-CSharp`**. It just binds far
less than it used to, because as of 2026-08-12 nothing under `Assets/Scripts/` is in
`Assembly-CSharp` at all.

---

## 5. Read before changing

| Before you | Read | Then run |
|---|---|---|
| add a `.cs` anywhere | §3 above | a Unity compile check — [Verifying §2](06-verifying.md) |
| add or edit an `.asmdef` | §1 and §2 above | a Unity compile check, and `dotnet build core/HorrorGame.sln -c Release` if `Core` was touched |
| move a file between folders | §4 above | both of the above; a move can change an assembly silently |
| add a Steamworks call | §3 step 4 | build once with the package present and once with it absent |
