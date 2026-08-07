# Steam release — administration, depots, and the store page

> The authority on *what the game is* is `docs/game-design.md`. The authority on
> *where code goes* is `docs/ARCHITECTURE.md`. This file is the authority on
> **getting the game onto Steam** — the administrative work §13 calls
> "인프라가 아니라 행정", the depot pipeline in `tools/steam/`, and the store page.

> **This file has two parts.**
> **Part I (audit)** — added 2026-08-03 — is *where this repo actually stands*: what
> was measured on disk, what Valve requires as verified against Valve's own pages on
> that date, and what is genuinely between here and a release. Read it first.
> **Part II (process, §0–§9)** is the unchanged mechanics of registration, depots,
> branches and the store page. Its Valve numbers were re-checked on 2026-08-03 and
> are correct. Its *game* facts predate the 20인 경주 pivot
> ([DESCENT-PIVOT.md](DESCENT-PIVOT.md), 2026-08-02) and §I.6 lists which.

---
---

# PART I — THE AUDIT (2026-08-03)

## I.0 The one-paragraph answer

Nothing can be released tomorrow, and nothing can be released in a week, because
**the 30-day clock has not been started** — Valve requires 30 days between paying
the app fee and releasing, and the app fee has not been paid. That is not the
binding constraint for long, though. The binding constraint is that **no two peers
have ever connected in this repository, by any evidence on disk**, in a game whose
entire premise is twenty of them; that the shipped player's main menu contains no
networking code at all and its Play button loads a *solo* scene; and that every
word of the finished store copy describes a four-player co-op looting game that was
deleted on 2026-08-02. The build pipeline is in good shape and a release path
demonstrably works — on macOS. Windows, which is the product, has never once been
built with IL2CPP. The fastest defensible path is **not** a release: it is to start
the account clock today, put up a Coming Soon page for the race, and ship a Steam
Playtest. §I.5 gives the order.

---

## I.1 The two lists

The single most useful division, because the second list has lead times measured in
days that no amount of engineering can compress, and every day the owner delays it
is a day added to the end.

### ACCOUNT AND LEGAL — only the owner can do these, on Valve's website

Nothing in this repository blocks any of these. They can all start today. Several
of them have waiting periods that run in the background while engineering
continues, which is exactly why delaying them is pure dead time.

| # | Item | Who | Lead time | Blocks |
|:--:|---|---|---|---|
| 1 | Steamworks partner registration; sign NDA + Steam Distribution Agreement | owner | same day | everything |
| 2 | Bank account details; account holder name must match legal name | owner | days (Valve verifies) | being paid |
| 3 | Tax interview — W-8BEN (개인) or W-8BEN-E (사업자), **foreign TIN filled in** | owner | form is same-day | treaty rate; see §1.3 |
| 4 | Third-party identity/tax verification | Valve | **2–7 business days** | releasing |
| 5 | **Pay the $100 Steam Direct fee** → mints the real App ID | owner | same day | **starts the 30-day clock** |
| 6 | **30-day waiting period** between paying the fee and being allowed to release | Valve | **30 days, hard** | releasing |
| 7 | Content survey — general, mature, **and generative-AI disclosure** | owner | ~1 hour | store/build review |
| 8 | Store presence submitted for review | Valve | **3–5 business days** | page going live |
| 9 | Coming Soon page public **≥ 2 weeks** before the release date | — | **14 days, hard** | releasing |
| 10 | Click "Release App" — approved titles do not release themselves | owner | manual | releasing |
| 11 | Asset provenance: state the licence of the two source sculpts (§I.4.4) | owner | unknown | legal safety |

**Earliest possible release date = max(fee + 30 days, page public + 14 days,
review + 3–5 business days).** If the fee were paid today, 2026-08-03, the earliest
conceivable release is **2026-09-02**, and only if the store page also went public
by 2026-08-19 and passed review. Realistically add a week for the review round-trip
Valve tells you to budget: submit the page ≥ 7 days before you want it live.

### ENGINEERING — someone can fix these in this repo

Ordered by what blocks what, not by size.

| # | Item | Evidence | Size |
|:--:|---|---|---|
| 1 | **Get two peers to connect, once** | §I.4.2 — never done | the whole risk |
| 2 | A networked entry point in the shipped build (menu → host/join) | §I.4.1 — `GameShell` has none | days |
| 3 | The race exists as a scene (`RaceDirector` is in no scene) | §I.4.3 | in progress elsewhere |
| 4 | Reconcile the 4-vs-20 player cap | §I.4.3 | small, but touches Net |
| 5 | A Windows **IL2CPP** player — never once produced | §I.2.3 | needs a Windows host |
| 6 | Real App ID into `SteamAppConfig.cs` **and** `steam.config` together | §5.1 | one line each |
| ~~7~~ | ~~`steam_appid.txt` must not reach a depot~~ | §I.2.5 defects 1–3 | **done 2026-08-08** — and it was never one line |
| 8 | Store copy + screenshots rewritten for the race | §I.3.3 | days |
| 9 | A real trailer — the current file is a 3-second 720p clip | §I.3.3 | days |
| 10 | Scale 20 players: interest management is written for 4 | §I.4.3 | weeks |

---

## I.2 A — Build configuration

### I.2.1 Where it lives

| Path | What it is |
|---|---|
| `tools/ci/build.sh` | the shell entry point; `./tools/ci/build.sh windows release` |
| `unity/HorrorGame/Assets/Scripts/Editor/BuildPipelineOptions.cs` | argument parsing, App ID resolution |
| `…/BuildPipelineBackend.cs` | IL2CPP-vs-Mono decision and the fallback warning |
| `…/BuildPipelineSettingsScope.cs` | every setting `release` changes, and their restoration |
| `…/BuildPipelineRunner.cs` | the run; preflight, `BuildPlayer`, message triage |
| `…/BuildPipelineReport.cs` | `build-report.txt` / `build-report.json` |

The configuration is never guessed. `BuildPipelineOptions.TryParse` fails outright
with *"No `-buildConfig` given. It must be stated explicitly, because the difference
between the two is whether the player can ship."* The default, when constructed
rather than parsed, is `Development` — the safe default is the one that cannot ship.

### I.2.2 What `release` actually changes

Measured from `BuildPipelineSettingsScope.Apply` and `.OptionsFor`, not from prose:

| Setting | `development` | `release` |
|---|---|---|
| Scripting backend | **Mono2x, always, on purpose** (links in seconds, managed debugger attaches) | **IL2CPP if the host OS matches the target**, otherwise Mono + a loud fallback |
| IL2CPP compiler configuration | `Debug` | `Release` |
| Managed stripping | `Disabled` | **`Low`** — deliberately capped, see below |
| `stripEngineCode` | `false` | `true` |
| IL2CPP code generation | untouched | `OptimizeSpeed` (set reflectively) |
| `BuildOptions` | `Development \| AllowDebugging \| ConnectWithProfiler \| CompressWithLz4` | `CompressWithLz4HC` |
| `steam_appid.txt` (pipeline's own writer) | written | **not** written — but see §I.2.5 |

Architecture is a separate axis: `-buildPlatform win64 \| mac \| mac-arm64 \| mac-x64 \| all`.
macOS architecture is applied by reflection on `UnityEditor.OSXStandalone.UserBuildSettings`
so the editor assembly still compiles on a Windows runner without the macOS module.
All of it is a scope — set, build, restore — so a CI run does not leave
`ProjectSettings.asset` dirty.

**Stripping is capped at `Low` on purpose, and that cap is an untested risk.** The
source says why: Mirror's generated serialisers and Steamworks.NET's callback
dispatch both resolve types *by name* at runtime, and stripping above `Low` removes
exactly those. The comment then admits the gap — the alternative would need a
`link.xml` *"nobody has validated against a live … session yet"*. Since no live
session has ever happened (§I.4), nothing has validated `Low` either. **The first
IL2CPP release build that reaches a real lobby is the first test of this setting.**
Expect it to be where a "works in the editor, dead in the build" bug appears.

### I.2.3 Does a release path exist, and has it been run? Yes — and only on macOS

It exists and it works. Two runs prove it, and they prove opposite things.

**macOS Release / IL2CPP: succeeded.** `dist/logs/build-mac-release-20260801T103111Z.log`:

```
[BuildPipeline] Scripting backend for macOS universal (Apple silicon + Intel): IL2CPP
    — release build on a matching host, so the native toolchain for this target is available.
[BuildPipeline] Building macOS universal (Apple silicon + Intel) (Release, IL2CPP) to
    /Users/doogi/horror-game/dist/macos-universal/HorrorGame.app
  macOS universal (Apple silicon + Intel)    Release     IL2CPP OK        2025.36 MB  20s
```

That artefact no longer exists — it was overwritten by a Development build on
2026-08-02. But the path is proven on this machine.

**Windows Release: succeeded as Mono, and is marked unshippable.** This is the only
Windows release build on disk, `dist/windows-x64/build-report.txt`, built
**2026-07-31T14:28:02Z** at commit `4fb93cd`, build number **2**, in **5m 15s**:

```
configuration:        Release
scripting backend:    Mono
backend reason:       IL2CPP is unavailable for Windows x64 on macOS (OSXEditor); fell back to Mono.
shippable on Steam:   no — IL2CPP was unavailable on this host, so it is a Mono build
```

and the pipeline dropped `MONO-FALLBACK-DO-NOT-SHIP.txt` into the folder, ending
*"Delete this file only after replacing this folder with an IL2CPP build."*

> **No Windows IL2CPP player has ever been produced in this repository.** A Mac
> cannot make one: IL2CPP transpiles to C++ and then needs MSVC. The only two routes
> are a Windows machine or the CI job in `.github/workflows/unity.yml`, and that job
> is gated `if: needs.preflight.outputs.enabled == 'true'`, which requires
> `UNITY_EMAIL`/`UNITY_PASSWORD`/`UNITY_SERIAL` or `UNITY_LICENSE` secrets. There is
> no evidence in the repo that it has ever run.

The current state of `dist/` — the three build reports on disk:

| Folder | Configuration | Backend | Built (UTC) | Size | shippable |
|---|---|---|---|---|:--:|
| `dist/windows-x64/` | **Release** | Mono (fallback) | 2026-07-31T14:28:02Z | 144.09 MB | no |
| `dist/macos-arm64/` | Development | Mono | 2026-08-02T02:32:06Z | 277.68 MB | no |
| `dist/macos-universal/` | Development | Mono | 2026-08-02T15:02:48Z | 572.69 MB | no |

`dist/last-build-summary.txt` records the most recent run, and it was
`-buildConfig development`.

### I.2.4 The report format — how to recognise a good one

`ShippableOnSteam` is one expression, in `BuildPipelineReport.cs`:

```csharp
public bool ShippableOnSteam
{
    get { return Succeeded && Configuration == BuildConfigurationId.Release && !MonoFallback; }
}
```

A report you can upload from looks like this — every line below differs from what
the repo produces today:

```
result:               Succeeded
platform:             Windows x64 (windows-x64)
configuration:        Release
scripting backend:    IL2CPP
backend reason:       release build on a matching host, so the native toolchain for this target is available.
shippable on Steam:   yes

version:              1.0.0   (VERSION)
git commit:           <sha>          ← and NOT "(working tree dirty)"
host:                 Windows (WindowsEditor)
errors / warnings:    3 / n   (0 this project's, 3 known third-party defect(s), listed below)

steam app id:         <your real numeric App ID>   (…)

output folder contents (listed before this report was added)
  HorrorGame.exe
  HorrorGame_Data
  MonoBleedingEdge            ← absent on a real IL2CPP build
  UnityCrashHandler64.exe
  UnityPlayer.dll
                              ← no steam_appid.txt
                              ← no MONO-FALLBACK-DO-NOT-SHIP.txt
```

Four things to read every time, in order: `shippable on Steam: yes`; `scripting
backend: IL2CPP`; `steam app id:` is not 480; and the **output folder contents block
contains no `steam_appid.txt`**. The last one is not paranoia — see the next section.

### I.2.5 Three defects found in the artefacts

**Defect 1 — the Release build shipped `steam_appid.txt`, and the report says it
did not.**

`dist/windows-x64/build-report.txt` states, under `notes`:

> `* No steam_appid.txt was written: a release player must take its App ID from the`
> `Steam client, not from a file in the depot.`

The file is there. `dist/windows-x64/steam_appid.txt`, **3 bytes**, contents `480`,
mtime `2026-07-31T23:33:17` — the same second as `build-report.txt`. The report's
own `output folder contents` block lists it, four lines above the note denying it.

The byte count identifies the culprit. There are two writers:

| Writer | Gate | Writes |
|---|---|---|
| `BuildPipelineRunner.WriteSteamAppIdFile` | skips on Release | `AppId + "\n"` → **4 bytes** |
| `Assets/Scripts/Steam/Editor/SteamAppIdFileTool.OnPostprocessBuild` | `[PostProcessBuild(1)]`, gated on `SteamAppIdFile.ShouldWrite` | **3 bytes** |

and `SteamAppIdFile.ShouldWrite` is

```csharp
public static bool ShouldWrite => SteamAppConfig.IsDevelopmentAppId || Debug.isDebugBuild;
```

— true because `AppId == 480`, **regardless of build configuration**. The
configuration-aware writer stands down on Release; the App-ID-aware one does not
know about configurations at all, and fires. The 4-byte copies are in the
Development mac folders' roots; the 3-byte copies are in `windows-x64/` and in each
`HorrorGame.app/Contents/MacOS/`.

Valve is unambiguous about why this matters
([steam_api.h](https://partner.steamgames.com/doc/api/steam_api)): *"Make sure to
remove the steam_appid.txt file when uploading the game to your Steam depot!"* —
because *"if a steam_appid.txt file is present then `SteamAPI_RestartAppIfNecessary`
will return false regardless of how the application was launched."* A shipped copy
containing `480` also means the released game initialises Steamworks against
Spacewar: no lobbies, no voice, no stats, and nothing in any error message pointing
at the cause. That is the silent failure §5.1 already warns about, arriving by a
route §5.1 does not cover.

**And the depot stager would carry it up.** `tools/steam/lib/steampipe.py`
`EXCLUSIONS` has ten rules — `*_BurstDebugInformation_DoNotShip`,
`*_BackUpThisFolder_ButDontShipItWithYourGame`, `*.pdb`, `*.mdb`, `*.dSYM`,
`.DS_Store`, `._*`, `__MACOSX`, `*.log`, `.git*` — and **`steam_appid.txt` is not
among them**.

**Fixed 2026-08-08 — and the diagnosis above was half right, in the dangerous
direction.** This section originally closed by saying the condition *"self-corrects
the day `SteamAppConfig.AppId` becomes the real ID — `ShouldWrite` goes false and
the post-build step stands down"*, and recommended adding the `EXCLUSIONS` lines as
belt-and-braces. That is wrong, and acting on it would have left the defect in
place permanently: the second clause, `|| Debug.isDebugBuild`, is evaluated **inside
an editor callback**, where the editor is answering about itself. The editor is
always a debug build. `ShouldWrite` was therefore true for every build this project
has ever produced, and no App ID would ever have changed that.

Three things had to move, and all three are in:

| | Change | Why the others are not enough |
|---|---|---|
| cause | `SteamAppIdFile.ShouldWrite` is now `Debug.isDebugBuild` alone, and the post-build step is an `IPostprocessBuildWithReport` that reads `BuildOptions.Development` off the build being processed | the App ID and the configuration are different questions; asking one to answer the other is what produced the bug |
| detection | `BuildPipelineRunner` scans the finished output for `steam_appid.txt` and the report fails `shippable on Steam` on any hit | a note saying the pipeline did not write the file is a claim about the pipeline; two other writers exist |
| containment | `steam_appid.txt` and `MONO-FALLBACK-DO-NOT-SHIP.txt` are both in `EXCLUSIONS` | the point of that list is not to depend on another file being correct |

Measured on the rebuilt Release player, not inferred: `find dist/macos-universal
-name steam_appid.txt` returns nothing, and the post-build step logs the branch it
had never taken — *"Release build, so steam_appid.txt was deliberately not shipped
with it."*

**Defect 2 — `MONO-FALLBACK-DO-NOT-SHIP.txt` is not excluded either.** It sits in
`dist/windows-x64/` today. Staging that folder as a depot would publish the
pipeline's own do-not-ship marker to players.

**Fixed 2026-08-08.** In `EXCLUSIONS`, beside `steam_appid.txt`. The stale copy is
still in `dist/windows-x64/` and is meant to be: it is a real record that that
player fell back to Mono, and the uploader now refuses to carry it either way.

**Defect 3 — `shippable on Steam` does not look at the App ID.** The expression in
§I.2.4 checks `Succeeded && Release && !MonoFallback` and nothing else. A Release
IL2CPP build stamped with App ID 480, carrying a `steam_appid.txt` that says 480,
will print `shippable on Steam: yes`. The owner is being told to gate on a flag
that cannot see the most likely release-day mistake.

**Fixed 2026-08-08.** `ShippableOnSteam` now also requires a non-placeholder App ID
and an output with no `steam_appid.txt` in it. Both directions were checked against
real builds rather than reasoned about, because a gate that can only ever say *no*
is worth exactly as much as one that can only ever say *yes*:

| `--app-id` | report says |
|---|---|
| *(default)* | `no — the App ID is still 480, Valve's Spacewar sample` |
| `3216540` | `yes` |

The second build was made only to prove the gate discriminates, and the tree was
rebuilt back to 480 afterwards so nothing on disk claims to be shippable while
stamped with an App ID nobody owns.

---

## I.3 B — What Valve requires that this repo does not have

Every figure below was read off Valve's own partner documentation on **2026-08-03**.
These are Valve's numbers to change; re-read before committing to a date.

### I.3.1 The gates, verified

| Requirement | Valve's figure | Source |
|---|---|---|
| Steam Direct app fee | **$100 USD per app**, not refundable but **recoupable** in the payment after the product reaches **$1,000 adjusted gross revenue** | [appfee](https://partner.steamgames.com/doc/gettingstarted/appfee) |
| Wait between paying the fee and releasing | *"A 30-day waiting period between when you paid the app fee and when you can release your game."* | [onboarding](https://partner.steamgames.com/doc/gettingstarted/onboarding) |
| Identity / tax verification | **2–7 business days**; tax info cannot be modified while it runs | [onboarding](https://partner.steamgames.com/doc/gettingstarted/onboarding) |
| Store presence review | *"typically takes 3-5 business days"*; submit **≥ 7 days** before you want it live | [releasing](https://partner.steamgames.com/doc/store/releasing) |
| Coming Soon page public before release | *"set to 'coming soon' for a[t] least 2 weeks and the build is reviewed"* | [releasing](https://partner.steamgames.com/doc/store/releasing) |
| Release is manual | *"Approved titles will not release themselves -- you need to use these controls yourself"* | [releasing](https://partner.steamgames.com/doc/store/releasing) |
| Content survey | Must be completed **before** submitting for review. Three sections: general content, mature content, **generative AI** | [contentsurvey](https://partner.steamgames.com/doc/gettingstarted/contentsurvey) |
| Age ratings | The survey *"will generate ratings for several regional rating boards"*. Germany and Indonesia have mandatory regional requirements | [contentsurvey](https://partner.steamgames.com/doc/gettingstarted/contentsurvey) |
| Build upload | SteamPipe via `steamcmd +run_app_build <app_build.vdf>`; depots defined by `FileMapping`/`FileExclusion` | [uploading](https://partner.steamgames.com/doc/sdk/uploading) |
| Default branch | *"the 'default' branch can not be set live automatically. That must be done through the App Admin panel"* | [uploading](https://partner.steamgames.com/doc/sdk/uploading) |
| `steam_appid.txt` | *"Make sure to remove the steam_appid.txt file when uploading the game to your Steam depot!"* | [steam_api.h](https://partner.steamgames.com/doc/api/steam_api) |

Two notes for this project specifically:

- **The generative-AI disclosure is not optional and not cosmetic.** It asks
  separately about content generated *before* shipping and content generated *during
  play*, and requires a description of how it is used. This repo generates almost all
  of its art procedurally from Python (`tools/blender/gen_*.py`), which is not
  generative AI — but the owner is the only person who can state the provenance of
  `tools/blender/source/monster_creature_base.glb` and `monster_vessel_base.glb`
  (§I.4.4). Answer the survey from fact, not from memory.
- **Valve's rules are not overridden by disclosure**: *"Products on Steam must adhere
  to the content rules, regardless of whether it is disclosed in these surveys."*

### I.3.2 What this repo already HAS — with paths

This part is genuinely in good shape, and the sizes are not merely claimed. Every
capsule below was measured with `sips` on 2026-08-03 and **every one matches Valve's
current specification exactly**:

| Asset | Valve requires | On disk | Path |
|---|:--:|:--:|---|
| Header capsule | 920 × 430 | **920 × 430** ✅ | `docs/store/capsules/header_capsule_920x430.png` |
| Small capsule | 462 × 174 | **462 × 174** ✅ | `docs/store/capsules/small_capsule_462x174.png` |
| Main capsule | 1232 × 706 | **1232 × 706** ✅ | `docs/store/capsules/main_capsule_1232x706.png` |
| Vertical capsule | 748 × 896 | **748 × 896** ✅ | `docs/store/capsules/vertical_capsule_748x896.png` |
| Page background | 1438 × 810 | **1438 × 810** ✅ | `docs/store/capsules/page_background_1438x810.png` |
| Library capsule | 600 × 900 | **600 × 900** ✅ | `docs/store/capsules/library_capsule_600x900.png` |
| Library header | 920 × 430 | **920 × 430** ✅ | `docs/store/capsules/library_header_920x430.png` |
| Library hero | 3840 × 1240 | **3840 × 1240** ✅ | `docs/store/capsules/library_hero_3840x1240.png` |
| Library logo | 1280 wide and/or 720 tall, transparent PNG | **1280 × 720** ✅ | `docs/store/capsules/library_logo_1280x720.png` |
| Community icon | 184 × 184 | **184 × 184** ✅ | `docs/store/capsules/community_icon_184x184.png` |
| Screenshots | ≥ 5, 1920 × 1080 min, 16:9, gameplay only | **10 × 1920 × 1080** ✅ | `docs/store/screenshots/` |

Also present and useful: legibility proofs at the auto-generated sizes
(`docs/store/capsules/legibility/` — 120×45, 184×69, 292×136, 300×450, 374×214),
Korean and English store copy (`docs/store/copy-ko.md`, `copy-en.md`), the headphone
notice in the four places players see it (`docs/store/headphone-notice.md`), a
ten-beat trailer shot list with 13 reference frames (`docs/store/trailer.md`,
`docs/store/trailer/`), the asset spec with its sources (`docs/store/assets.md`),
and an honest page checklist (`docs/store/checklist.md`). The generators are
`tools/render/store_capsules.py`, `store_shots.py` and `store_shots.json`.

> `docs/store/README.md` already carries the right warning about all of it:
> **"Re-shoot before uploading."**

### I.3.3 What this repo does NOT have

**1. There is no trailer.** `docs/store/party.mp4` is the only video in the repo.
Measured with `ffprobe`:

```
codec_name=h264   width=1280   height=720   r_frame_rate=24/1
duration=3.000000  size=609331  bit_rate=1624882
```

**1280 × 720, three seconds, 24 fps, 1.62 Mbps.** Valve's trailer spec is *"up to
1920 x 1080 resolution, 30/29.97 or 60/59.94 fps"* at *"high bit rate (5,000+
Kbps)"*, H.264/AAC in `.mov`/`.wmv`/`.mp4`, audio at 44 or 48 kHz
([trailer](https://partner.steamgames.com/doc/store/trailer)). The file misses the
resolution, the frame rate and the bit rate, and three seconds is not a trailer in
any case. `docs/store/trailer.md` is a shot list and `docs/store/trailer/` is 13
still PNGs; **no video has been cut.** This is the single highest-leverage asset on
a store page and it does not exist.

**2. The client icon is missing.** Valve's client icon is a **32 × 32 TGA**. The
repo has `docs/store/capsules/shortcut_icon_256x256.png` — wrong size, wrong format.

**3. The library hero has an unchecked safe area.** Valve specifies a **860 × 380**
safe area at the centre of the 3840 × 1240 hero, and the library logo is composited
over it. `docs/store/assets.md` does not record this number. Re-check
`library_hero_3840x1240.png` against it before upload.

**4. Every word of the store copy describes a game that was deleted.**
`docs/store/copy-en.md` was written 2026-08-01; the pivot landed 2026-08-02. It
still says, verbatim:

- line 42 — *"Four-player co-op horror… Time is the only currency and the wallet is shared."*
- line 54 — *"Four people. Five roles. One of them is missing."*
- line 96 — *"the four-player networking layer is written and passes its own tests, and nobody has yet played a four-player match"*
- line 144 — tags: *"Co-op, Online Co-Op, Horror, Survival Horror, Multiplayer, Asymmetric…"*
- line 197 — feature flag: *"Online Co-op ✅ 4 players"*

The game is now a **20-player competitive race** with **no roles**, **no shared
wallet** and **no co-op** ([DESCENT-PIVOT.md](DESCENT-PIVOT.md)). The screenshots
match the old game too: `05_the_shop.png` photographs the §08 economy that the pivot
deleted, and `06_five_storeys.png` names five storeys where the design now has
eight. **Tags are a distribution decision** — shipping `Co-op` and `Online Co-Op` on
a competitive race would put the page in front of precisely the wrong queue, and tag
mistakes are slow to undo.

*This is not a small edit. The copy is good writing about a different game.*

---

## I.4 C — Multiplayer, without softening

The question is what the repo can *prove* about twenty players. The answer is
nothing, and the reason is not that the netcode is weak. It is that the networking
layer is not connected to the game that ships.

### I.4.1 The shipped build has no networking in it at all

The libraries are all present and correctly chosen —
`unity/HorrorGame/Packages/manifest.json`:

- `com.mirrornetworking.mirror` **96.6.4** (OpenUPM) — the transport-agnostic layer
- `com.rlabrecque.steamworks.net` **2025.164.0** — the Steamworks C# binding
- `com.mirror.steamworks.net` **FizzySteamworks-6.0.1** — Mirror over Steam sockets

and there is a real, considered Steam adapter layer under
`Assets/Scripts/Steam/`: `ILobbyService`, `IP2PTransportProvider`, `ICloudSaveService`,
`IStatsService`, `IVoiceBackend`, each with a `Steamworks/` implementation and an
`Offline/` null implementation, plus a full proximity-voice stack
(`Voice/VoiceSession`, `VoiceRelay`, `VoiceJitterBuffer`, `VoiceRoster`, …).
**NAT traversal is answered on paper**: `Assets/Scripts/Net/SteamTransport/FizzyTransportBackend.cs:45`
sets `transport.AllowSteamRelay = true;`, which is Steam Datagram Relay — free, and
the reason §8's "직접 띄울 서버가 0대" holds.

None of it is reachable from the game.

- **`Scenes/Bootstrap.unity` — scene 0 of the build — contains exactly three of this
  project's scripts**: `MenuBackdrop`, `ThreatAtmosphereDirector`, `GameShell`. No
  `NetworkManager`, no lobby, no Steam component.
- **`Assets/Scripts/UI/Shell/GameShell.cs` states it outright** in its own class
  comment:

  > *"**What it deliberately does not do.** It does not host, join, pick roles or step
  > a match… This class chooses which scene that is and gets out of the way, **which is
  > why it compiles without Mirror and without the Steam layer.**"*

- **And its assembly definition confirms it.** `Assets/Scripts/UI/HorrorGame.UI.asmdef`
  references only `HorrorGame.Core`, `HorrorGame.Audio`,
  `HorrorGame.Gameplay.Player`, `UnityEngine.UI`, `Unity.InputSystem` and the two URP
  assemblies. **Not `Mirror`. Not `HorrorGame.Net`. Not `HorrorGame.Steam`.** The menu
  literally cannot call them.
- **The Play button loads a solo scene.** `GameShell.DefaultMatchScene = "Map_FirstSketch_Solo"`.

`StartHost()` and `StartClient()` are called from exactly one file in the entire
runtime tree — `Assets/Scripts/Gameplay/Race/RaceLobby.cs`, lines **236** and **273**.

> **`RaceLobby` is referenced by nothing.** `grep -rn "RaceLobby"` across `Scripts/`
> and `Tests/`, excluding its own file, returns zero hits. Its GUID
> `d0193d794aaa24c97990a3069cb97066` appears in no `.unity`, no `.prefab` and no
> `.asset`. `LobbyScreen.cs` (GUID `1ee10fcc8a8e5432ba850d80d7667e1f`) is likewise in
> no scene or prefab.

**There is no path from the shipped main menu to a networked session.** This is
precisely the failure mode this repository's CLAUDE.md was written about — invisible
in the source, obvious in the artefact.

### I.4.2 No test has ever run more than one peer

`Assets/Tests/PlayMode/Net/NetTests.cs` is the only networking test file. What it
does, at lines 195, 274, 448 and 494:

```csharp
NetworkServer.Listen(GameConstants.PlayersPerMatch);

var far  = new NetworkConnectionToClient(1) { isAuthenticated = true };
var near = new NetworkConnectionToClient(2) { isAuthenticated = true };
NetworkServer.AddConnection(far);
NetworkServer.AddConnection(near);
```

`NetworkConnectionToClient` constructed directly and handed to `AddConnection` is an
**in-process object**. No socket is opened, no transport runs, no second process
exists, and no data crosses a wire. These are good tests of interest management and
host authority — they are not evidence that two machines can talk. **No test
anywhere in the repo calls `StartHost` or `StartClient`.**

**The two-instance harness is dead.**
`Assets/Scripts/Editor/SceneGen/LocalTwoInstance.cs` is §14 step 2 — *"Mirror 로컬
호스트 — 같은 PC 2인스턴스"* — and it builds a player, launches two copies, and passes
`-horror-host` to one and `-horror-client` to the other. Its own comment says:

> *"The two processes are told which side they are by `HostArgument` / `ClientArgument`.
> **Reading those is the Net layer's job** — this class only guarantees the argument is
> there."*

**Nothing reads them.** `grep -rn "horror-host\|horror-client\|HostArgument\|ClientArgument"`
across `Scripts/` and `Tests/`, excluding that file, returns nothing. Every runtime
consumer of `Environment.GetCommandLineArgs()` in the project is an Editor-only
screenshot tool. So the command launches **two identical single-player main menus**,
side by side, neither of which hosts or joins. §14 step 2 has never actually been
performed, and it looks like it has.

> **Peers ever connected in this repository, by any evidence on disk: zero.** Not
> twenty. Not four. Not two.

### I.4.3 The player cap is 4, not 20

Two constants, both in `Assets/Scripts/Core/GameConstants.cs`:

```csharp
public const int RaceRunnersMin =  2;   // line 466
public const int RaceRunnersMax = 20;   // line 478
public const int PlayersPerMatch = 4;   // line 1097  ← the pre-pivot co-op party
public const int RoleCount       = 5;   // line 1100  ← §04, deleted by the pivot
```

Which one executes:

- `Assets/Scripts/Net/HorrorGameNetworkManager.cs:60` — `maxConnections = GameConstants.PlayersPerMatch;`
  in `Awake()`, commented *"§11 fixes the party at four."*
- `…:202` — the fifth connection is refused: *"Refusing a connection: all 4 seats are
  taken."*, via `NetLobby`, which builds `PlayersPerMatch` seats.
- `Assets/Scripts/Gameplay/Race/RaceLobby.cs:427` — `manager.maxConnections = GameConstants.RaceRunnersMax;`
  with a careful comment explaining that twenty is a map limit, not a socket limit.

`RaceLobby` never runs (§I.4.1). **So the only cap that can execute today is four,
and a twentieth runner would be disconnected at the door.**

The rest of the `Net/` layer is still the deleted game: `NetClueTerminal.cs`,
`Host/HostClueAuthority.cs`, and `NetLobby`'s role seating. The tests assert it too —
`TheLobbySeatsFourOfFiveRolesAndLeavesExactlyOneAbsent`, `TwoPlayersCannotTakeTheSameRole`
— roles that §04 no longer has. `Net/Interest/HorrorInterestManagement.cs` documents
its cost as `PlayersPerMatch` distance checks per spawned object; at twenty that is
25× the work, and it has never been measured at any number.

**The race is not in a scene, either.** `RaceDirector` (GUID
`6d574d52940774f5b81d9ab2f033eb6d`) appears in **no** `.unity` file. The only
playable scene, `Map_FirstSketch_Solo.unity`, contains `MatchDirector` — the
pre-pivot co-op director. The eight-storey descent exists as code and as generators;
integrating B6/B7/B8 into the map is live work elsewhere in this repo right now.

### I.4.4 One legal item that is not multiplayer, found while looking

`tools/blender/source/` contains two mesh files that are inputs to the character
pipeline rather than outputs of it:

```
1568340  monster_creature_base.glb
1790836  monster_vessel_base.glb
```

`docs/ART.md` records no provenance, licence or origin for either. Everything else
in `tools/blender/` is procedural Python that generates its own geometry; these two
are the exception. **Only the owner can say where they came from**, and a commercial
Steam release needs that answer to be "I have the right to ship this" — plus the
right answer on the content survey's AI section if any part of them was generated.
This is a five-minute question with a potentially expensive wrong answer.

---

## I.5 D — The verdict, and one recommendation

### I.5.1 What could be released tomorrow

**Nothing.** Not "nothing good" — nothing at all. There is no app on Steam, the
$100 has not been paid, so Valve's 30-day clock has not started. Even a finished,
perfect game could not be released tomorrow.

What *can* happen tomorrow is the entire ACCOUNT AND LEGAL list in §I.1 — items 1
through 5 and 7. That is a few hours of forms and one card payment, and it starts
both the 30-day clock and the 2–7 business-day identity verification running in the
background. **Every day this is delayed is a day added to the earliest possible
release date, and it buys nothing.**

### I.5.2 What could be released in a week

**Still nothing, on Steam.** The 30-day gate makes that arithmetic, not opinion.

What can be *live* in a week is a **Coming Soon store page** — and only if the copy
is rewritten for the race first (§I.3.3). The assets are ready; the words are about
a deleted game. Budget: 2–3 days to rewrite `copy-ko.md`/`copy-en.md` and fix the
tags, then submit and wait 3–5 business days for review. Valve says submit ≥ 7 days
before you want it live.

A trailer is not required for a Coming Soon page and should not be allowed to delay
it. §0 of Part II is right about this and it is worth re-reading: the page exists to
accumulate wishlists *before* the game is finished.

### I.5.3 What is genuinely months away

A shipped 20-player race. In order of how long each takes, longest first:

1. **Twenty peers in one match.** Today the count is zero and the entry point does
   not exist in the built game (§I.4.1–2). The path is: build a menu that can host and
   join → get two peers connected once → four → twenty. Each step is where the bugs
   are, and none of them has been taken. Nothing about the schedule can be estimated
   honestly until step two happens, because step two is what tells you whether the
   Steam transport, the relay, the lobby and the interest manager work at all.
2. **Twenty-player scale.** Interest management, voice roster and lobby are all
   written and reasoned about at four. Twenty is 25× the interest-management work and
   a different bandwidth problem. Unmeasured at any player count.
3. **The race existing as a scene** with its eight storeys and its gates.
4. **A Windows IL2CPP player**, which needs a Windows machine or the licence secrets
   for `.github/workflows/unity.yml` — plus the first real test of the `Low` stripping
   setting (§I.2.2), which is where "worked in the editor, broken in the build" bugs
   live.
5. **Store content that matches the game**: copy, tags, screenshots, and a trailer.

### I.5.4 The recommendation

> **Pay the $100 today to start the 30-day clock. Put up a Coming Soon page for the
> race within two weeks. Then ship a Steam Playtest — not a release. Do not set a
> release date until twenty peers have finished one match.**

In order:

| When | Who | Do |
|---|---|---|
| **Today** | owner | Partner registration, agreements, bank details, tax interview (W-8BEN, **foreign TIN filled in**), **pay the $100**. Starts the 30-day and 2–7-day clocks. Nothing in the repo blocks it. |
| **Days 1–3** | engineering | Real App ID into `SteamAppConfig.cs` **and** `tools/steam/steam.config` together; `tools/steam/upload.sh --dry-run` clean. Add `steam_appid.txt` and `MONO-FALLBACK-DO-NOT-SHIP.txt` to `EXCLUSIONS` (§I.2.5). Make `ShippableOnSteam` check the App ID. |
| **Days 1–10** | content | Rewrite `copy-ko.md`/`copy-en.md` for a 20-player race; **fix the tags**; re-shoot screenshots once the race map lands; cut a real trailer (≥ 1920 × 1080, 30 or 60 fps, 5,000+ Kbps, H.264/AAC). |
| **~Day 12** | owner | Complete the **content survey** — it gates review — then submit store presence. Wait 3–5 business days. |
| **~Day 17** | — | **Coming Soon page live.** The two-week clock starts here. Wishlists accumulate from this moment and from no earlier moment. |
| **In parallel** | engineering | One Windows IL2CPP build. Then **two peers connected, once**. Then four. Then twenty. Report the peer count honestly at each step. |
| **When 20 peers finish a match** | both | Request a **Steam Playtest**, upload to it, and let strangers play. Only then choose a release date. |

**Why a Playtest and not a release.** It is a separate free app — no second $100 —
that puts a "Request Access" button on the store page and gives testers their own
download without any key hand-out. For a game whose entire proposition is twenty
simultaneous players and whose measured peer count is currently zero, releasing
before a playtest would mean charging money for something no group of humans has
ever played. The playtest is also the only realistic way to find the twenty-player
problems, because they cannot be found by one person on one machine, and this
project has never had more than one.

**The one thing not to do:** do not set a release date on the store page until
§I.4.2's number is twenty. A date is a promise Valve enforces with a two-week gate,
and moving it is worse for the page's algorithmic standing than never having set it.

---

## I.6 What in Part II is stale after the pivot

Part II's **Valve mechanics are correct** and were re-verified on 2026-08-03. Its
**game facts** were written on 2026-07-30, before the 20인 경주 pivot of 2026-08-02.
Where the two disagree, Part I wins. The specific lines:

| Part II location | Says | Actually |
|---|---|---|
| §2.4 tags | Co-op, Asymmetrical, Online Co-Op | a competitive race — see §I.3.3 |
| §2.5 | the 청음사 (Listener) class needs headphones | §04 roles were deleted; **the headphone requirement itself still holds** — §05's 3D audio is camera-relative regardless of roles |
| §4.2 | `internal` branch is "you + 3 friends", "a 4-player game needs four people" | twenty; a private branch matters *more*, not less |
| §4.3 | "a match is 25–35 minutes" | one descent, not a 25–35 min round trip |
| §7 | "A four-player match completed… by four people on four machines" | **twenty**, and the current number is zero (§I.4.2) |
| §7 | "Clue contents and objective location confirmed absent from client memory" | §03's clue system was deleted with the co-op game |
| §8 | "직접 띄울 서버가 0대" | still true, and Steam Datagram Relay is enabled (§I.4.1) — but it is an unproven claim at 20 players, not a measured one |

The checklist in §7 is still the right checklist. Read it with §I.1's two lists
beside it: §7 tells you what to tick, §I.1 tells you who can tick it and how long
they will wait.

---
---

# PART II — THE PROCESS

## 0. Read this part first

§14 orders the work and then attaches a warning to the last item. The warning is
the most consequential sentence in the development-order section, so it is
repeated here at the same volume:

> ### ⚠ 경고: 7번을 늦추지 말 것.
> ### 상점 페이지는 게임 완성 전에 올려서 위시리스트를 모으는 용도다.
> ### 출시일 알고리즘 노출이 여기 걸려 있고, 스팀에서 가장 흔한 실수다.
>
> **Do not defer the store page.** It exists to collect wishlists *before the
> game is finished*. Launch-day algorithmic visibility hangs on it, and deferring
> it is the single most common mistake on Steam.

Why it works this way, mechanically:

- A wishlist is a notification Valve sends **on your behalf, for free, on launch
  day and on every discount afterwards**. It is the only marketing channel in
  this project's budget (§13: 월 고정비 0원).
- Valve's front-page and "Popular Upcoming" surfaces are driven substantially by
  wishlist counts and their rate of change. A page that goes up two weeks before
  release has nothing to accumulate.
- Valve requires the store page to have been **public for at least two weeks**
  before your chosen release date, and there is a **minimum waiting period
  between paying the App ID fee and being allowed to release** (30 days at the
  time of writing). Both are hard gates, not recommendations. Re-read Valve's own
  launch checklist when you pick a date, because these numbers are Valve's to
  change.

So the correct order is not "finish game, then make store page". It is:

```
prototype validates (§14 검증 질문 5개)
        │
        ├──▶ pay $100, create the app, get the real App ID
        │        └──▶ put it in tools/steam/steam.config   (ONE line)
        │
        ├──▶ Coming Soon page live, wishlists accumulating   ◀── as early as
        │        (needs: capsules, 5 screenshots, 1 trailer,      this is
        │         descriptions, tags, headphone notice)          honest
        │
        └──▶ keep building; upload internal builds to a private branch
                 └──▶ promote to default on release day
```

"As early as this is honest" is the real constraint. A Coming Soon page needs
screenshots and a trailer of something that exists, and Valve rejects pages built
from concept art or mock-ups. The gate is therefore the **first playable slice
that looks like the game** — §14's 2주 프로토타입 plus enough lighting to
photograph — not feature completeness.

---

## 1. Administration, in order

§13 lists this under "인프라가 아니라 행정 — 세팅해야 할 것". None of it is
technical, all of it is blocking, and the parts involving other people (bank
verification, tax forms) take days to weeks of waiting.

| # | Item | Cost | Blocks |
|:--:|---|---|---|
| 1 | Steamworks partner registration — 사업자 정보 + 은행 계좌 | 0 | everything |
| 2 | Tax forms (W-8BEN / W-8BEN-E) | 0 | being paid |
| 3 | Bank account verification | 0 | being paid |
| 4 | Create the app — **App ID fee** | **$100** | store page, real depots |
| 5 | Depot configuration | 0 | uploading |
| 6 | Store page assets | **time** | wishlists |

### 1.1 Partner registration

`partner.steamgames.com` → sign the Steam Distribution Agreement as an
individual or as a 사업자. You will provide legal name/business name, address,
and a bank account that can receive USD. Valve verifies the bank account with a
small deposit, which takes days.

Registering **does not** cost anything and **does not** create an app. Do it
early; it is pure waiting time that can overlap with development.

### 1.2 The $100 App ID fee — and that it comes back

§13: **App ID → $100 (매출 $1,000 초과 시 환급)**.

The fee is a per-app "Steam Direct" recoupable deposit. It is charged when you
create the app, and Valve **credits it back to your payment balance once the app
has earned $1,000 in adjusted gross revenue**. It exists to make spamming the
store expensive, not to be a real cost of a game that sells at all.

Practical consequences:

- It is **per app**. A Steam Playtest app (see §4.4) does not cost another $100;
  a sequel does.
- Paying it is what mints the **real App ID**. Until then `480` (Spacewar) is the
  App ID, and `tools/steam/upload.sh` refuses to upload anywhere but a test
  branch while that is true.
- Pay it **early enough to clear the 30-day release waiting period** and the
  two-week store-page requirement. Paying it late is how a finished game waits a
  month to launch.

### 1.3 W-8BEN and the Korea–US tax treaty

§13: **W-8BEN 제출 — 한미조세조약으로 원천징수 감면.**

Valve is a US payer, so US law requires it to withhold tax on royalties paid to a
foreign person **at 30% by default**. Filing the right W-8 form claims the
reduced rate the Korea–US income tax treaty provides, and Valve applies it to
every subsequent payment.

| You are | Form |
|---|---|
| An individual (개인) | **W-8BEN** |
| A company / 사업자 as an entity | **W-8BEN-E** |

Filled out inside Steamworks' own tax interview — you do not mail anything to the
IRS. What it asks for:

- Country of residence: Korea, Republic of.
- **A foreign TIN** — your 주민등록번호 or 사업자등록번호. A US ITIN/EIN is *not*
  required for treaty benefits when a foreign TIN is supplied. This is the field
  that most often gets left blank, and leaving it blank silently means 30%.
- The treaty article and rate you are claiming. The Korea–US treaty taxes
  royalties at a reduced rate (commonly applied at 10% for copyright royalties,
  15% for others). **Confirm which article and rate apply to your case** — the
  form is a legal declaration you are signing, and this document is not tax
  advice. A 세무사 who has handled software royalties is worth one consultation.

Two further facts worth knowing before the first payment:

- A W-8BEN is valid for the year it is signed **plus the following three calendar
  years**, then expires. An expired form means withholding jumps back to 30%
  without warning. Put the expiry in a calendar.
- Withheld US tax is generally creditable against Korean tax under the same
  treaty, so the money is not simply gone — but that is a filing your accountant
  does, not something Steam handles.

---

## 2. Store page assets — the part that eats time

§13, on the store page assets: **"시간이 꽤 든다"** — it takes a fair amount of
time. It is listed last in the setup table and it is the longest item on it. Do
not schedule it as an afternoon.

### 2.1 Capsule images

Capsules are the game's face everywhere on Steam. Different surfaces use
different ones, and Valve will not scale one into another for you.

| Asset | Pixels | Where it appears | Needed by |
|---|:--:|---|:--:|
| **Header capsule** | **920 × 430** | Top of the store page, search results, most lists | Coming Soon |
| **Small capsule** | **462 × 174** | Search suggestions, top-sellers rows, most compact lists | Coming Soon |
| **Main capsule** | **1232 × 706** | Front-page featured carousel, daily deals | Coming Soon |
| **Vertical capsule** | **748 × 896** | Seasonal sale pages, "Featured & Recommended" | before a sale |
| Page background | 1438 × 810 | Store page backdrop (auto-derived from a screenshot if omitted) | optional |
| **Library capsule** | **600 × 900** | The player's own library grid | release |
| **Library header** | **920 × 430** | Library detail header | release |
| **Library hero** | **3840 × 1240** | Wide banner at the top of the library page | release |
| **Library logo** | **1280 × 720** | Transparent PNG logo, composited over the hero | release |
| Client icon | 32 × 32 (TGA) | Taskbar / friends list while running | release |
| Community icon | 184 × 184 | Community hub | release |

Rules that get pages rejected on review:

- **The small capsule must be legible at its real size.** 462 × 174 is about the
  width of a business card. Sub-title text and a five-word tagline vanish. The
  game's name, large, is the whole design.
- Capsules must show the **game's name as it appears on the store**, and nothing
  else textual — no review scores, no awards, no "Wishlist now", no discount
  flashes, no platform logos.
- The **library hero must contain no text or logo**, because the library logo is
  composited on top of it at a position you choose. Keep the centre clear.
- Don't put important art in the outer 10% of any capsule; Steam crops.

> Re-check every number above against Steamworks' own "Store Asset Guidelines"
> page before commissioning art. Valve has changed these — the header capsule was
> 460 × 215 before the library redesign — and the partner site's uploader is the
> only authority that matters.

### 2.2 Screenshots

- **Minimum 5.** Realistically 6–8; the first 4 are what a visitor actually sees.
- **1920 × 1080**, 16:9, PNG or JPG. Larger is accepted; smaller looks bad on the
  lightbox.
- **Gameplay only.** No overlaid text, no logos, no key art, no concept art, no
  award laurels, no UI mock-ups. Valve enforces this on review.
- This game is dark by design (§03 — 어둠 = 목표의 잠금장치), which is a real
  problem for screenshots: a thumbnail of a black rectangle converts nothing.
  Shoot the moments where the flashlight, a flare or a zone light gives the frame
  a subject — §05's flashlight-as-pointer, the monster at the edge of the cone.
  Do not brighten the game for marketing; frame it instead.

### 2.3 Trailer

- At least one. It is the single highest-leverage asset on the page; visitors
  play it before they read anything.
- **1920 × 1080 minimum** (upload the highest-resolution master you have — Steam
  transcodes down, never up), H.264 in an MP4 container, AAC stereo audio.
- No black bars — upload at the aspect ratio you shot.
- Structure that works for a co-op horror game: **gameplay in the first three
  seconds**, four players and voices audible early (this is the hook — it is a
  friends-and-a-microphone game), the monster seen briefly and late.
- Sound is the pitch here. §05 makes 3D audio a mechanic, so the trailer's audio
  mix is content, not garnish. Mix it for headphones.

### 2.4 Text

| Field | Limit | Notes |
|---|---|---|
| Short description | ~300 characters | Shown in search results and on hover. Written last, matters most. |
| About This Game | long | The body of the page. Screenshots and GIFs belong inside it. |
| Tags | up to 20 | Co-op, Horror, Asymmetrical, Multiplayer, Online Co-Op, Survival Horror. Tags drive discovery queues — treat them as a distribution decision. |
| System requirements | — | Fill in honestly. Include the headphone recommendation (§2.5). |

### 2.5 The headphone notice — required, not cosmetic

§13's setup table lists **헤드폰 권장 표기** as a shipping requirement, and §05
explains why: *"3D 오디오는 카메라 기준 → 헤드폰 필수"*. The 청음사 (Listener)
class exists to locate the monster by ear, and §14's validation question 5 —
*"청음사가 방향·거리를 구별할 수 있는가?"* — is a question about the player's
output device as much as about the audio implementation. On laptop speakers one
of the five classes does not function.

Put the notice in **four** places, because players see different ones:

1. **Short description** — one clause, e.g. "헤드폰 권장 / Headphones
   recommended".
2. **About This Game** — its own line near the top, not buried at the bottom.
3. **System requirements** — under "Additional Notes", both minimum and
   recommended.
4. **In game, on first launch** — a dismissible one-line notice. The store page
   is not read by the friend who was invited into the lobby.

Also set the Steam **audio feature tags** honestly (surround / 3D audio support),
and enable the "Voice Chat" feature flag — §13's proximity voice is a headline
feature and a filterable store attribute.

---

## 3. Build and platform matrix

| Platform | Depot | Scripting backend | Built on |
|---|:--:|---|---|
| Windows x64 | `AppID+1` | **IL2CPP preferred, Mono possible** | Windows machine or CI runner |
| macOS (Apple silicon + Intel) | `AppID+2` | Mono or IL2CPP | this Mac |

**A Mac cannot produce an IL2CPP Windows player — only Mono.** IL2CPP transpiles
C# to C++ and then needs the *target platform's* native toolchain (MSVC) to
compile it, which does not exist on macOS. Consequences to plan for now:

- Windows Mono builds are producible here and are fine for §14 steps 1–6 and for
  private test branches. Mono ships `Assembly-CSharp.dll` as ordinary IL, so the
  game's assemblies are trivially readable — irrelevant for a PvE co-op game
  (§13: 치팅 방어 거의 불필요) but worth knowing.
- **Shipping IL2CPP on Windows requires a Windows machine or a Windows CI
  runner.** Steam's audience is overwhelmingly Windows, so this is a real
  release-blocking dependency, not a nice-to-have. Decide before the store page
  goes up whether that is a spare PC or a GitHub Actions windows runner (§13
  lists 빌드 자동화 as optional: "초기엔 수동 steamcmd").
- The macOS depot **must be uploaded from macOS.** SteamPipe records the POSIX
  mode bits it observes, so the executable bit on
  `HorrorGame.app/Contents/MacOS/HorrorGame` only survives if the uploading
  machine has it. `tools/steam/lib/steampipe.py` checks that bit before it lets
  an upload proceed, because the failure it prevents — installs, then silently
  refuses to launch — is invisible until a player hits it.

---

## 4. Depots and branches

### 4.1 Depot layout

A **depot** is a set of files Steam installs. One per platform, so a Windows
player never downloads the Mac build.

| Depot | Default with App 480 | Real | Unity writes | Staged to | Configure on partner site as |
|---|:--:|:--:|---|---|---|
| Windows | 481 | `AppID+1` | `dist/windows-x64/` | `output/content/windows/` | OS Windows, 64-bit |
| macOS | 482 | `AppID+2` | `dist/macos-universal/` | `output/content/macos/` | OS macOS, 64-bit |

The editor build pipeline owns the `dist/` layout —
`BuildPipelinePaths.DistFolderName` and `BuildPipelineTargets.FolderName()`. It can
also emit `macos-arm64` and `macos-x64` separately; **one macOS depot fed by the
universal build is the right choice**, because Steam cannot hand different Mac
architectures to different machines out of a single depot, which is precisely what
a universal binary is for. Two Mac depots with an OS/architecture split is the
alternative, and it doubles the upload for no benefit at this scale.

The **operating system and architecture of a depot live on the partner site's
Depots page, not in any VDF.** A build script cannot declare them. A depot with
the wrong OS set will happily install Windows DLLs onto a Mac.

Depot IDs are `auto` in `tools/steam/steam.config`, meaning `AppID+1` and
`AppID+2` — the order Steamworks allocates them in for a fresh app. That is a
convention, not a promise: **open the Depots page and confirm the numbers**, then
either leave `auto` or paste the real IDs in.

### 4.2 Branch strategy

| Branch | Password | Who is on it | Purpose |
|---|:--:|---|---|
| `default` | no | the public | The live game. **Never a script target.** |
| `staging` | yes | you | Release candidate. The exact build that will be promoted. |
| `internal` | yes | you + everyone you can get | The workhorse. A 20-runner race needs a crowd, so this branch is how anything gets tested at all — and see §4.4, because twenty is past the number of friends you can ask twice. |
| `playtest` | yes | external testers | Wider testing without touching `default`. |

Set branch passwords on the partner site (Builds → Manage betas). §11's four-player
structure is the reason `internal` matters more here than on a single-player game:
you cannot test this design alone, and you cannot ask three friends to sideload a
zip every evening. Steam's branch mechanism *is* the test distribution channel.

### 4.3 Promoting a build to default

**SteamPipe will not set the default branch live from a build script.** That is
Valve's rule, and this repo agrees with it: `tools/steam/upload.sh` refuses
`--branch default` outright, and the validator rejects a VDF with
`"SetLive" "default"` even if someone hand-edits one.

The promotion procedure:

1. Upload to a branch: `tools/steam/upload.sh --upload --branch staging`
2. **Install that branch from the Steam client and play it.** Not "launch it" —
   play a full match. §01 says a match is 25–35 minutes; budget the time.
3. `partner.steamgames.com` → your app → **Builds**.
4. Find the BuildID (the upload printed it, and the `Desc` field carries the
   branch, commit and timestamp so you can identify it weeks later).
5. Set its branch to `default` in the dropdown, then **Preview Change** →
   **Set Build Live Now**.
6. Verify the store page's "last updated" and that a client actually pulls the
   patch.

Rolling back is the same operation pointed at the previous BuildID. Old builds
stay on Steam, so a bad release is a two-minute revert — *provided you did not
delete the build*. Keep them.

### 4.4 Steam Playtest

A separate, free app that gives testers a "Request Access" button on your store
page and their own download, without a key hand-out. For a game that needs four
people per session and whose validation questions (§14) are all about *feel*,
this is the cheapest way to get more than one group playing. Worth requesting
once the store page is up.

---

## 5. The tooling in `tools/steam/`

§13: **"빌드 자동화 — GitHub Actions (선택). 초기엔 수동 `steamcmd`"**. This is
the manual path, with the parts that are unrecoverable if done wrong turned into
refusals.

```
tools/steam/
  steam.config              ← THE one place App ID and depot IDs live
  upload.sh                 ← the only thing that runs steamcmd
  check_gitignore.sh        ← proves no credential file can be committed
  templates/
    app_build.vdf.template
    depot_windows.vdf.template
    depot_macos.vdf.template
  lib/steampipe.py          ← renders, stages, validates. Never touches the network.
  output/                   ← generated, gitignored: content/ vdf/ build/ logs/ manifest/ fixture/
```

### 5.1 Swapping in the real App ID — two lines, in two places

The App ID appears in the project exactly twice, because it answers two different
questions, and **both must be changed together**:

| File | Question it answers | The line |
|---|---|---|
| `tools/steam/steam.config` | Which app do we **upload the depot to**? | `APP_ID="480"` |
| `Assets/Scripts/Steam/SteamAppConfig.cs` | Which app does the **shipped player initialise Steamworks against**? | `public const uint AppId = DevAppId;` |

Depot IDs follow the first automatically (`auto` = `AppID+1`, `AppID+2`). Then:

```sh
tools/steam/upload.sh --dry-run
```

The validator **reads `SteamAppConfig.cs` and refuses to proceed if the two
disagree.** That check exists because the failure is otherwise silent: the game
installs correctly from the right depot, then initialises Steamworks against the
wrong app, and there are no lobbies, no voice and no stats with nothing in the
error message pointing at why. (`steampipe.py` only ever reads that file — the
Steam adapter layer owns it.)

Nothing else in the repository names an App ID.

### 5.2 Dry run — the offline mode

```sh
tools/steam/upload.sh --dry-run                      # against the real Unity builds
tools/steam/upload.sh --dry-run --fixture            # against a synthetic build
tools/steam/upload.sh --dry-run --branch internal    # exercise the SetLive path
```

A dry run:

- resolves and validates `steam.config`;
- assembles the depot content into `output/content/{windows,macos}/`, applying the
  exclusion rules, and writes a per-file manifest;
- checks the staged trees structurally (one `.exe` with the expected name, one
  `.app` bundle, `Info.plist` present, executable bit set);
- renders all three VDFs and **parses them back**, checking the App ID, both
  depot IDs, `ContentRoot`, `SetLive` and every `FileMapping` against the config;
- cross-checks the depot App ID against the one compiled into the player
  (§5.1);
- runs the credential `.gitignore` check;
- prints the exact `steamcmd` command it *would* run, with credentials redacted.

It **never contacts Steam, never logs in, and never needs a credential.** That is
what makes the pipeline testable before Unity is installed — `--fixture`
synthesises a Unity-shaped build tree, junk files included, so the exclusion
rules are exercised rather than assumed.

There is a second, weaker kind of dry run: `"Preview" "1"` in the app build
script, which `upload.sh --preview` renders. That one *does* log in — steamcmd
computes the build and reports it without uploading. Use it once, after the real
App ID exists, to confirm Steam agrees with the depot layout.

### 5.3 Uploading

```sh
export STEAM_BUILD_ACCOUNT="<your-steamworks-build-account>"
tools/steam/upload.sh --upload --branch internal
```

The script refuses to proceed if:

| Condition | Why it is fatal |
|---|---|
| **App ID is 480 and the branch is not a test branch** | 480 is Valve's Spacewar. A build sent to an app you do not own cannot be recalled by you, and the same mistake against a real-but-wrong App ID is worse. **No override flag exists.** |
| `--branch default` | SteamPipe cannot promote `default` from a script, and promotion should require looking at the build. |
| `--fixture` with `--upload` | Fixture content is text files pretending to be a game. |
| A depot's staged tree is empty | Steam would publish an empty install, and the next player to update gets an empty folder. |
| `STEAM_BUILD_ACCOUNT` unset | See §6. |
| The `.gitignore` check fails | A committed session file is a handed-over build account. |
| A rendered VDF disagrees with `steam.config` | Someone hand-edited a generated file. |
| `SteamAppConfig.AppId` disagrees with `APP_ID` | Right depot, wrong app at runtime — silent (§5.1). |

After a successful upload the script does **not** trust steamcmd's exit status —
it has historically returned 0 after a failed build. It greps the captured log
for SteamPipe's `Successfully finished` line and fails loudly if it is absent.

Allowed branch names while the App ID is still 480:
`test*`, `tests*`, `testing*`, `internal*`, `internal-test*`, `dev*`, `devtest*`,
`ci*`, `staging-test*`.

---

## 6. Credentials and Steam Guard

**No credential is stored in this repository, in any form, ever.** Not in
`steam.config`, not in a `.env`, not in a comment, not base64'd. `upload.sh` reads
them from the environment at the moment it calls `steamcmd` and nowhere else, and
`check_gitignore.sh` fails the build if a credential-shaped file could be
committed or a literal-looking secret appears in a tracked file.

| Variable | Required | Notes |
|---|:--:|---|
| `STEAM_BUILD_ACCOUNT` | for `--upload` / `--preview` | The Steamworks **build account**, not your personal Steam login. |
| `STEAM_BUILD_PASSWORD` | no | Only for an unattended run. See the warning below. |
| `STEAM_BUILD_GUARD_CODE` | no | A fresh Steam Guard code, valid for minutes. |

### 6.1 The first login on a machine must be interactive

This is not a limitation to work around — it is Steam Guard functioning. Before
`upload.sh --upload` can work on a machine, run once, by hand, at a terminal:

```sh
steamcmd +login "$STEAM_BUILD_ACCOUNT" +quit
```

It will prompt for the password, then for a **Steam Guard code** sent to email or
generated by the mobile authenticator. Type them. On success, steamcmd writes a
`config.vdf` holding a login token and an `ssfn*` "sentry" file that marks this
machine as authorised. Subsequent logins reuse them and prompt for nothing.

**There is no way to script the first login, and you should not want one.** The
prompt is the second factor.

Consequences:

- **Every new machine needs one interactive login.** A fresh CI runner is a new
  machine. So is a reinstalled OS.
- `config.vdf` and `ssfn*` are equivalent to the second factor. A committed one
  lets anyone with repo access upload a build to the app, and the only revocation
  is rotating the build account. This is why `check_gitignore.sh` runs on **every**
  invocation of `upload.sh`, dry runs included.
- Steam Guard codes expire in minutes, so `STEAM_BUILD_GUARD_CODE` is only ever
  useful for a run you are watching.

### 6.2 Passing a password at all

`STEAM_BUILD_PASSWORD` is supported and should normally be unset. When it is set,
`upload.sh` passes it to steamcmd as an argument, which means **it is visible in
the process list to every other process on that machine for the duration of the
run**. That is steamcmd's interface, not a choice this script makes.

Prefer, in order:

1. **Nothing set but `STEAM_BUILD_ACCOUNT`**, relying on the cached session from
   the interactive login. This is the normal case and needs no secret anywhere.
2. **For CI**: restore steamcmd's already-authorised `config.vdf` from the CI
   provider's secret store into the runner's steamcmd config directory before the
   job runs. Never from a repo file. The GitHub Actions Steam-deploy actions all
   work this way, and it is the only reason CI works with Steam Guard at all.
3. `STEAM_BUILD_PASSWORD`, for a run you are watching, on a machine you own.

Use a **dedicated Steamworks build account** with only the "Edit App Metadata"
and "Publish App Changes" permissions it needs, never the account that owns the
partner relationship. Then a leaked build session cannot change your bank
details.

Never put a credential in: `steam.config`, a VDF, a shell script, a git commit
message, a CI log, a screenshot, or a message to anyone. If one is exposed:
change the password, then **deauthorise all devices** in Steam settings, which
invalidates every cached `config.vdf` and `ssfn*` at once.

---

## 7. Pre-release checklist

Work down it. Anything unchecked is a launch-day problem.

### Administration
- [ ] Steamworks partner registration complete; Distribution Agreement signed
- [ ] Bank account added **and verified** (the test deposit has arrived)
- [ ] W-8BEN / W-8BEN-E filed, **foreign TIN filled in**, treaty article claimed
- [ ] Expiry of the W-8BEN in a calendar (signing year + 3)
- [ ] $100 App ID fee paid, real App ID issued
- [ ] **≥ 30 days** between paying the fee and the chosen release date
- [ ] Real App ID in **both** `tools/steam/steam.config` and
      `SteamAppConfig.AppId`; `--dry-run` clean (it cross-checks them)
- [ ] `steam_appid.txt` **absent from the staged depot** — verify by reading the
      manifest, not by trusting the build report, which stated it was not written
      while the file was in the folder (§I.2.5). `SteamAppIdFile.ShouldWrite` is
      `IsDevelopmentAppId || Debug.isDebugBuild`, so a *development* build still
      writes it after the App ID is real. Add it to `EXCLUSIONS` and the question
      stops depending on any of that
- [ ] Dedicated build account created with minimum permissions

### Store page
- [ ] Page **public** ≥ 2 weeks before the release date — see §0
- [ ] Header capsule 920 × 430
- [ ] Small capsule 462 × 174, **legible at actual size**
- [ ] Main capsule 1232 × 706
- [ ] Vertical capsule 748 × 896
- [ ] Library capsule 600 × 900, library header 920 × 430, library hero
      3840 × 1240 (no text), library logo 1280 × 720 (transparent)
- [ ] Client icon 32 × 32 TGA, community icon 184 × 184
- [ ] ≥ 5 screenshots at 1920 × 1080, gameplay only, no overlaid text
- [ ] Screenshots readable despite §03's darkness — flashlight/flare framing
- [ ] Trailer ≥ 1920 × 1080, H.264 MP4, gameplay in the first 3 seconds, mixed
      for headphones
- [ ] Short description (~300 chars) includes the headphone recommendation
- [ ] About This Game states the headphone recommendation near the top
- [ ] System requirements filled in, headphones under Additional Notes
- [ ] Tags set (Co-op, Horror, Asymmetrical, Online Co-Op, …)
- [ ] Feature flags: Online Co-op, 4 players, Voice Chat, Steam Cloud
- [ ] Release date set; page reviewed and approved by Valve (allow days)

### Technical
- [ ] Windows depot configured on the partner site: OS Windows, 64-bit
- [ ] macOS depot configured: OS macOS, 64-bit
- [ ] Depot IDs on the partner site match what `steam.config` resolves to
- [ ] Launch options set, executable name matches `WINDOWS_EXE_NAME` /
      `MACOS_APP_NAME` exactly
- [ ] Windows IL2CPP build reachable (a Windows machine or CI runner exists)
- [ ] macOS depot uploaded **from macOS**; executable bit verified
- [ ] `tools/steam/upload.sh --dry-run` passes with zero errors
- [ ] `tools/steam/check_gitignore.sh` passes
- [ ] Interactive steamcmd login completed on the release machine
- [ ] One `--preview` run against the real App ID, clean
- [ ] Build uploaded to `staging`, **installed from the Steam client and played
      through a full 25–35 minute match** (§01)
- [ ] **Two** peers connected on a private branch, ever — the count today is
      **zero** (§I.4.2), and every other networking box below is unverifiable
      until this one is ticked
- [ ] A **twenty**-runner match completed on a private branch by twenty people on
      twenty machines — §11's field means nothing else counts as tested
      *(was "four" pre-pivot; see §I.4.3)*
- [ ] Proximity voice cuts off at the sender past 30 m (§13 — receiving and
      muting locally is defeated by any client edit)
- [ ] Clue contents and objective location confirmed absent from client memory
      (§13 host authority; §03's whole constraint dies otherwise)
- [ ] Steam Cloud save paths configured and a save round-tripped
- [ ] Achievements/stats defined, including §13's telemetry bucket counters
- [ ] Crash reporting produces a symbolised stack from a release build
- [ ] Rollback rehearsed: promote an older BuildID to `default` and back

### Launch day
- [ ] Promote the verified BuildID to `default` from the Builds page (§4.3)
- [ ] Confirm a real client downloads the patch
- [ ] Launch announcement posted (Steam news — §13: 게임 내 공지 needs no server)
- [ ] Discord invite live on the store page (§13: 커뮤니티 — Discord가 호스팅)
- [ ] Watch the discussion forum for the first hours

---

## 8. What this release deliberately does not need

§13's headline result, restated so nobody adds it back later:

> **직접 띄울 서버가 0대. DB도 필요 없다. 월 고정비 0원, 초기 비용 $100.**

No dedicated servers, no matchmaking server, no relay (Steam Datagram Relay is
free and is what makes NAT traversal work), no account server, no database.
Wishlists, patching, lobbies, voice transport, saves, stats and leaderboards are
all Steamworks features that cost nothing. The only recurring cost of shipping
this game is the store page's assets, and those cost time.

---

## 9. References

- `docs/game-design.md` §13 — 인프라와 기술 스택; the administration table
- `docs/game-design.md` §14 — 개발 순서; the store-page warning
- `docs/game-design.md` §05 — 조작과 이동; why headphones are required
- `docs/ARCHITECTURE.md` §4 — host authority, and what must not reach a client
- `tools/steam/upload.sh --help` — the guard rails, in the script that enforces them
