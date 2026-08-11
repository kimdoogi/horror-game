# `tools/steam/` — depot upload

Full documentation, including the store page and the administrative work, is in
**[`docs/STEAM-RELEASE.md`](../../docs/STEAM-RELEASE.md)**. This file is the
thirty-second version.

```sh
# Test the whole pipeline offline. Needs no Steam, no credential, no Unity.
tools/steam/upload.sh --dry-run --fixture

# Same, against whatever Unity has actually built.
tools/steam/upload.sh --dry-run

# Prove no Steam credential file can be committed.
tools/steam/check_gitignore.sh -v

# steamcmd's own preview: logs in, computes the build, uploads nothing.
# Worth doing once, to confirm Steam agrees with our depot layout.
export STEAM_BUILD_ACCOUNT="<your-steamworks-build-account>"
tools/steam/upload.sh --preview --branch internal

# The real thing.
tools/steam/upload.sh --upload --branch internal
```

Three modes, and they are mutually exclusive: `--dry-run`, `--preview`, `--upload`.
`--fixture` is a modifier on the first. `--desc TEXT` labels an upload; `--list-limit N`
(default 25) caps how much of the staged tree is printed. `--branch internal` is not an
example picked at random — while `APP_ID` is `480` the branch name has to match
`upload.sh`'s `TEST_BRANCH_PATTERN`, and `internal` is one of the nine names that do.

## The one edit

`steam.config` holds the App ID that depots are uploaded to.

```sh
APP_ID="480"        # → the real one, when Steamworks issues it
```

Depot IDs are `auto`, meaning `AppID+1` (Windows) and `AppID+2` (macOS). Confirm
those against the partner site's Depots page and paste explicit numbers if Valve
allocated differently.

> **The macOS build path does not match what Unity is writing today.** `steam.config`
> points `MACOS_BUILD_DIR` at `dist/macos-universal`, and the only Mac player on disk is
> `dist/macos-arm64` (2026-08-10). That is the config being *right* and the build being
> narrow — one macOS depot wants the universal binary, because Steam cannot hand
> different Mac architectures to different machines from one depot. Until a universal
> player is built, the macOS depot stages empty. A dry run tolerates that; an upload does
> not (`--require-content`).

The App ID appears in exactly one other place — `SteamAppConfig.AppId`, which is
the App ID the *shipped player* initialises Steamworks against. Both must change
together, and `--dry-run` **reads that file and refuses if they disagree**: the
failure it prevents is a game that installs from the right depot and then talks to
the wrong app, with nothing in the error pointing at why.

While `APP_ID` is `480` — Spacewar, Valve's shared test app (§13's 개발용 App ID)
— `upload.sh` refuses to upload to anything but an explicitly-named test branch.
**There is no flag that turns that off.** A build sent to an app you do not own
cannot be recalled.

## Layout

| Path | What |
|---|---|
| `steam.config` | App ID, depot IDs, build paths. The only source of truth. |
| `upload.sh` | The only thing in the repo that runs `steamcmd`. Owns the guard rails. |
| `check_gitignore.sh` | Proves `ssfn*`, `config.vdf` and sentry files cannot be committed. Run by `upload.sh` in every mode. |
| `templates/*.vdf.template` | The app build script and one depot script per platform. Edit these, never the rendered output. |
| `lib/steampipe.py` | Renders, stages, and parses the VDFs back to validate them. Never contacts the network. |
| `output/` | Generated and gitignored: staged depot content, rendered VDFs, `BuildOutput`, logs, manifests, the fixture. |

## Credentials

Read from the environment, never from a file here. `STEAM_BUILD_ACCOUNT` is
usually the only one needed, because steamcmd caches the session after **one
interactive login per machine** — which is Steam Guard working, and cannot be
scripted. See `docs/STEAM-RELEASE.md` § "Credentials and Steam Guard".
