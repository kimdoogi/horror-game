# Where a file may live

> **Assembly layout decides where a file can go, and the folder decides the
> assembly.** Namespaces do not. Put a file in the wrong folder and it either fails
> to see the type it needs, or — worse — compiles and drags a dependency into an
> assembly that was deliberately kept clean.

Regenerate everything on this page from the repository at any time:

```bash
find unity/HorrorGame/Assets/Scripts -name '*.asmdef' | sort
```

---

## 1. The rule Unity actually applies

An `.asmdef` claims **its own folder and every subfolder**, until a deeper `.asmdef`
claims a subtree back. A folder with no `.asmdef` ancestor falls into Unity's
predefined assemblies: `Assembly-CSharp`, or `Assembly-CSharp-Editor` if any
path segment is literally named `Editor`.

Two consequences that surprise people in this repository:

- `Assets/Scripts/Gameplay/` has **no** asmdef of its own. `Gameplay/Player`,
  `Gameplay/Monster` and `Gameplay/Audio/Rig` each have one; **`Gameplay/Match`,
  `Gameplay/Interaction`, `Gameplay/Guidance` and `Gameplay/Playtest` do not** and
  therefore live in `Assembly-CSharp`.
- `Assets/Scripts/Gameplay/Audio/` has an asmdef only at `Rig/`. So
  `Gameplay/Audio/MatchAudioBridge.cs` and `MatchAudioBootstrap.cs` are in
  `Assembly-CSharp`, while `Gameplay/Audio/Rig/MatchAudioRig.cs` is in
  `HorrorGame.Gameplay.Audio`.

`Assembly-CSharp` automatically references every asmdef with `autoReferenced: true`,
which is all of them except `HorrorGame.EditorTools.TestRunner`. That is why the
un-asmdef'd gameplay folders can see everything, and why moving a file *into* an
asmdef is the step that usually breaks the build — the asmdef sees only what it
lists.

---

## 2. The ownership table

Computed from the `.asmdef` files on 2026-07-31 23:49. **The folder→assembly mapping
is stable; the "References" column is a snapshot** — an assembly gains references as
the code grows, and the `.asmdef` is always the authority. Recompute the whole table
with:

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
| `Net/`, `Net/Host/`, `Net/Interest/` | `HorrorGame.Net` | Core, Steam, Mirror + Components + Transports | |
| `Net/SteamTransport/` | `HorrorGame.Net.SteamTransport` | Net, Mirror, FizzySteamworks | platform-limited |
| `Steam/`, `Steam/Abstractions/`, `Steam/Offline/`, `Steam/Voice/` | `HorrorGame.Steam` | Core | |
| `Steam/Steamworks/` | `HorrorGame.Steam.SteamworksBackend` | Core, Steam, Steamworks.NET | platform-limited |
| `Steam/Editor/` | `HorrorGame.Steam.Editor` | Core, Steam | ✅ |
| `Gameplay/Player/` | `HorrorGame.Gameplay.Player` | Core, Rendering, Unity.InputSystem | |
| `Gameplay/Player/Editor/` | `HorrorGame.Gameplay.Player.Editor` | Core, Gameplay.Player, InputSystem | ✅ |
| `Gameplay/Monster/` | `HorrorGame.Gameplay.Monster` | Core | |
| `Gameplay/Monster/Editor/` | `HorrorGame.Gameplay.Monster.Editor` | Core, Gameplay.Monster, Rendering, URP | ✅ |
| `Gameplay/Audio/Rig/` | `HorrorGame.Gameplay.Audio` | Core, Audio | |
| **`Gameplay/Audio/`** (not `Rig/`) | `Assembly-CSharp` | everything auto-referenced | |
| **`Gameplay/Match/`** | `Assembly-CSharp` | everything auto-referenced | |
| **`Gameplay/Interaction/`** | `Assembly-CSharp` | everything auto-referenced | |
| **`Gameplay/Guidance/`** | `Assembly-CSharp` | everything auto-referenced | |
| **`Gameplay/Playtest/`** | `Assembly-CSharp` | everything auto-referenced | |
| `Gameplay/Match/Editor/`, `Gameplay/Audio/Editor/` | `Assembly-CSharp-Editor` | everything | ✅ |
| `Editor/SceneGen/` | `HorrorGame.EditorTools.SceneGen` | Core, Rendering, Unity.AI.Navigation, UnityEngine.UI | ✅ |
| `Editor/Dressing/` | `HorrorGame.EditorTools.Dressing` | Core, SceneGen, Unity.AI.Navigation | ✅ |
| `Editor/BuildPipelineTestRunner/` | `HorrorGame.EditorTools.TestRunner` | TestRunner only — **`autoReferenced: false`** | ✅ |
| `Editor/` and its other subfolders | `Assembly-CSharp-Editor` | everything | ✅ |

Tests are separate again, under `Assets/Tests/`, each with
`defineConstraints: ["UNITY_INCLUDE_TESTS"]` and `autoReferenced: false`:

| Test assembly | Platform | Sees |
|---|---|---|
| `HorrorGame.Tests.EditMode.Audio` | Editor | Core, Audio |
| `HorrorGame.Tests.EditMode.UI` | Editor | Core, UI |
| `HorrorGame.Tests.PlayMode.Audio` | any | Core, Audio, Gameplay.Audio |
| `HorrorGame.Tests.PlayMode.Monster` | any | Core, Gameplay.Monster, Unity.AI.Navigation |
| `HorrorGame.Tests.PlayMode.Net` | any | Core, Net, Steam, Mirror |
| `HorrorGame.Tests.PlayMode.Player` | any | Core, Gameplay.Player, Unity.InputSystem |

Note that two test fixtures live *outside* `Assets/Tests/`, inside the code they
test: `Gameplay/Match/Editor/SoloMatchLoopTests.cs` and
`Editor/Playtest/MatchGuidanceTests.cs`. Both are in `Assembly-CSharp-Editor`.

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
   `Net/SteamTransport/` with `HORRORGAME_FIZZYSTEAMWORKS`.
5. **Is it playback, occlusion, footsteps or voice output?** → `Assets/Scripts/Audio/`.
6. **Is it a HUD, shop or lobby screen?** → `Assets/Scripts/UI/`.
7. **Is it a `MonoBehaviour` that steps core state onto a transform?** →
   `Assets/Scripts/Gameplay/<System>/`, namespace `HorrorGame.Gameplay.<System>`.
   Check the table above for whether that folder has its own asmdef; if it does, add
   your reference to the asmdef in the same edit, and expect a compile error the
   moment you reach for a type the asmdef does not list.

---

## 4. The failure modes, in the order you will hit them

| Symptom | Cause | Fix |
|---|---|---|
| `The type or namespace name 'X' could not be found` in the editor only | the file landed in an asmdef that does not reference X's assembly | add the reference to the `.asmdef`, or move the file |
| The .NET build breaks and Unity is fine | a `using UnityEngine` reached `Assets/Scripts/Core/` | delete it; the file belongs in `Gameplay/` |
| A test cannot see the class it tests | test asmdefs are `autoReferenced: false` and list their references explicitly | add the assembly to the test's `.asmdef` |
| A new gameplay file cannot see `MatchDirector` | `MatchDirector` is in `Assembly-CSharp`, which **nothing with an asmdef can reference** | the dependency is backwards — invert it, or give `Gameplay/Match` an asmdef and re-check every consumer |
| Everything compiles, but the Steam build breaks | a file in `Steam/` (no constraint) used a type from `Steam/Steamworks/` (constrained) | go through `Steam/Abstractions/` |

The last row is the one worth remembering: `Assembly-CSharp` sees every asmdef, but
**no asmdef can see `Assembly-CSharp`**. Anything an asmdef'd assembly needs to call
must itself live in an asmdef.

---

## 5. Read before changing

| Before you | Read | Then run |
|---|---|---|
| add a `.cs` anywhere | §3 above | a Unity compile check — [Verifying §2](06-verifying.md) |
| add or edit an `.asmdef` | §1 and §2 above | a Unity compile check, and `dotnet build core/HorrorGame.sln -c Release` if `Core` was touched |
| move a file between folders | §4 above | both of the above; a move can change an assembly silently |
| add a Steamworks call | §3 step 4 | build once with the package present and once with it absent |
