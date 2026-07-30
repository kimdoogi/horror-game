# Steam release — administration, depots, and the store page

> The authority on *what the game is* is `docs/game-design.md`. The authority on
> *where code goes* is `docs/ARCHITECTURE.md`. This file is the authority on
> **getting the game onto Steam** — the administrative work §13 calls
> "인프라가 아니라 행정", the depot pipeline in `tools/steam/`, and the store page.

---

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
| `internal` | yes | you + 3 friends | The workhorse. A 4-player game needs four people, so this branch is how anything gets tested at all. |
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
- [ ] `steam_appid.txt` no longer shipped beside the player — `SteamAppIdFile`
      stops writing it once `AppId != DevAppId`, which is what Valve asks for
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
- [ ] A four-player match completed on a private branch by four people on four
      machines — §11's structure means nothing else counts as tested
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
