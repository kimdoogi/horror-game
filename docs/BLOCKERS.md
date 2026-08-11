# Blockers

Things that stop the game working, as opposed to design questions. Balance
contradictions live in [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md). Art defects that do
not stop the game live in [ART.md](ART.md) §7.

**Last triaged: 2026-08-12, at commit `4ab204f` — every entry, not a subset.** Each
status below was re-tested rather than carried forward. **Five closed** — B-002, B-011,
B-012, B-016, B-017 — **B-015 was downgraded** once its stated cause was disproved, and
**B-022 was opened** for a red that had never been filed at all. The other sixteen were
re-tested and confirmed where they stood.

> ⚠️ **`/tmp` was wiped on 2026-08-10 and every log this file cites from it is gone** —
> `/tmp/r3`–`r7*`, `/tmp/editmode.xml`, `/tmp/r7_all.xml`, all of it. So are
> `dist/test-results/` and `biggate/`. The quotations below are kept as the record of what
> was measured, but **none of them can be re-opened**; where a 2026-08-12 status rests on
> one of those logs it says so, and where it rests on something re-runnable today
> (`dotnet test`, `tools/ci/verify_audio.sh`, `clang++`, the sources, `git`) it names that
> instead. This is why closures are now pinned to tests and commits rather than to logs.

<details><summary>Earlier triage passes, kept for provenance</summary>

**2026-08-03, at `a3e268e`.** The first full pass; where an entry was closed by a
measurement, the measurement and the log it came from are quoted.

**2026-08-05 — B-007, B-014 and the new B-019 only.** Those three were re-measured
against the §12 report the shipped graph produces today; nothing else was re-checked.

**2026-08-05, later the same night — B-018 closed and B-020 opened-and-closed.** Both
were measured on `gen-20260805-025901-seed20260802` and on the roster published at
`A135BAD7`.

**2026-08-10 — B-007 closed, B-019 nearly closed, B-021 opened.** By the `RadialStorey`
re-lay at `9f0f447` and the `SceneShot` cap removal at `471ffab`.

</details>

> **The game changed shape on 2026-08-02.** Four-player co-operative recovery became a
> twenty-player competitive descent ([DESCENT-PIVOT.md](DESCENT-PIVOT.md)). Several
> entries below were opened against the old game; each says so.

| # | State | One line |
|---|---|---|
| [B-001](#b-001) | 🟢 closed | The creature could not reach the player |
| [B-002](#b-002) | 🟢 closed by deletion 2026-08-03 | EditMode red on a Mirror package-cache `.meta` — the test is gone |
| [B-003](#b-003) | 🟢 closed by the pivot | Two 개방 공간 dropped from every generation |
| [B-004](#b-004) | 🔴 **open — blocks release** | The networking library is a stranger's repack |
| [B-005](#b-005) | 🟢 closed | Regenerating the map unregistered the scene 시작 loads |
| [B-006](#b-006) | 🟢 closed | The core solution did not build |
| [B-007](#b-007) | 🟢 closed 2026-08-10 | §12's sight-break-spacing: 95 m of cover against 14.4 m, waived by name |
| [B-008](#b-008) | 🟢 closed | A 계단 only the creature could use |
| [B-009](#b-009) | 🟢 closed | The NavMesh audited was not the one just built |
| [B-009b](#b-009b) | 🟢 closed | …and the chamber sealed the middle its own way |
| [B-010](#b-010) | 🟢 closed | The middle of a radial storey had no piece |
| [B-011](#b-011) | 🟢 closed 2026-08-03 | The one red test is on the path a human takes — the seat now gets a real body |
| [B-012](#b-012) | 🟢 closed by deletion 2026-08-03 | The simulator measured a building the game deleted; the simulator went too |
| [B-013](#b-013) | 🟠 open (process) | CI was red for three commits that said green — **and it happened again for six days** |
| [B-014](#b-014) | 🟢 closed | §12's report said FAIL for three different reasons and named none of them |
| [B-015](#b-015) | 🟠 open — owner action | No Release build exists. The toolchain fault it blamed is **fixed**; the claim is untested |
| [B-016](#b-016) | 🟢 closed 2026-08-08 | EditMode had not been run since the pivot — it ran, 95/95 |
| [B-017](#b-017) | 🟢 closed 2026-08-10 | The first-person view has no hands — `RunnerArms.fbx` and a viewmodel landed |
| [B-018](#b-018) | 🟢 closed | Every match is the same building — 3 in the roster, a second match loads another |
| [B-019](#b-019) | 🔴 **open** | §12-D's centre-path: 21 of 22 entry points in band, the last one 2.5 m short |
| [B-020](#b-020) | 🟢 closed | `PlayerReach` counted its own measuring body as a wall and refused eight roster slots |
| [B-021](#b-021) | 🟢 closed | B8 was the brightest room; the finish light was cleared by an invalid experiment and was the cause |
| [B-022](#b-022) | 🔴 **open** | Voice: three red PlayMode tests, unfiled for four days — the creature never hears anyone speak |

**Open: 6 of 23.** B-004 blocks release outright and B-015 gates it on one build nobody has
run; B-013 is process; B-019 and B-021 are measured map/lighting defects; B-022 is a
shipped feature that has never once been green.

---

## B-022 · Proximity voice has three red tests, has never been green, and nobody filed it

**Status:** 🔴 **open** · opened 2026-08-12 · red since it was written on 2026-08-04 ·
**cause found in the source, not yet confirmed in the engine**

Three PlayMode cases in
`Assets/Tests/PlayMode/Voice/VoiceSocketTests.cs` have been failing in every recorded
sweep, and until today **they appeared in no blocker**. STATUS.md §2.3 said so in as many
words — *"this is the largest red in the project and it is not in BLOCKERS.md"* — and
that sentence sat there for four days. This entry is that omission being closed.

The three, with the file's own line numbers:

| # | test | asserts |
|---|---|---|
| 242 | `AVoiceCrossesARealSocketAndArrivesAttenuatedByTheRule` | the relay forwards > 0 frames, KCP carries them, the client plays them at `VoiceRules.Gain` |
| 492 | `AWallBetweenThemCostsTheRulesOcclusionAndNotTheEnginesRolloff` | with a wall between, `FramesPlayed > 0` and the gain is the occluded one |
| 575 | `SpeakingIsReportedToTheCreatureEvenWithNobodyInRange` | `MatchDirector.VoiceEffort == Shout` while Shout is held |

Last measured 2026-08-08 16:51:56Z: `total 124 passed 121 failed 3`, and all three failures
were these. Two later commits (`58b22b9` 08-08, `8db3d78` 08-09) report the same 121/3.
`VoiceSocketTests.cs` has been touched by exactly **one** commit in its life — `4924ae5`,
2026-08-04, the one that created it. **No commit has ever claimed to fix or re-run it.**

### What is actually broken, and what is not

Two of the three symptoms are consequences, not defects. The forwarding path is present
and correct — `VoiceHostRelay.cs:186` builds a `VoiceDownstreamMessage` and `conn.Send`s
it, incrementing `FramesForwarded` on the same statement — and the occlusion path is
present and correct at `VoicePlayback.cs:376` (`Physics.Raycast` against
`GameAudio.OccluderMask`, both ends inset 0.35 m). **Neither is ever reached, because no
frame is ever produced.** `FramesForwarded == 0` means `Accept` was not called, not that
the send is missing.

The defect that explains all three is one line. `VoiceRuntime.cs:321–329` attaches
`VoicePushToTalk` to the **same GameObject** as `VoiceCapture`:

```csharp
_capture = gameObject.AddComponent<VoiceCapture>();
gameObject.AddComponent<VoicePushToTalk>();
```

and `VoicePushToTalk.Update` then writes the keyboard's answer over the top, every frame:

```csharp
// VoicePushToTalk.cs:83-84
Effort = Read(keyboard);
capture.SetEffort(Effort);
```

`Read` returns `Silent` when no key is held. So **anything driving `VoiceCapture`
programmatically is reset to Silent within one frame** — which is exactly what the fixture
does at `VoiceSocketTests.cs:595` and `:917`. That single cause produces these three
failures and no others: test 575 reads `Silent` at the director; tests 242 and 492 get
`CloseMicrophone()` (`VoiceCapture.cs:206`), which clears the injected queue
(`InjectedVoiceLine.cs:91`), so the frames the fixture pushed are discarded before `Drain`
ever sees them. The fourth voice test, `AWhisperDoesNotCarryAndTheMicrophoneCloses`,
asserts only zeros and therefore **passes vacuously** — it is green for the same reason
the others are red.

**What is not yet known** is whether that is what fires. `VoicePushToTalk.cs:73–81`
guards against precisely this, returning early when `Keyboard.current == null`, with a
comment saying the guard exists so it does not "silently mute anything driving
`VoiceCapture` directly". Whether `Keyboard.current` is null in the batch-mode test host
cannot be settled without running Unity. `InteractionKeyPathTests.cs:95` asserts a
batch-mode editor has no input devices — but `GunTests` and `PlayerStanceTests` both add a
`Keyboard` and remove it again, so a leaked device is possible. **Mechanism confirmed in
source; trigger inferred.** One PlayMode run settles it.

### The existing explanation in the repo is wrong, and it matters

`TESTING.md` §"floating failures" calls these three "environment-gated — they need the
microphone to be producing *sound*", citing a log line `[Voice] Microphone line at
16000 Hz`. Two things rule that out:

1. **The fixture replaces the line.** `VoiceSocketTests.cs:176` sets `VoiceLines.Override`,
   and `VoiceLines.cs:51` returns the override before consulting a device;
   `InjectedVoiceLine.IsAvailable` is unconditionally `true` and its `Name` is
   `"Injected"`. A real microphone is not in this path, and a Voice-fixture session would
   have logged `Injected`, not `Microphone`. `VoiceRuntime` installs itself in *every*
   PlayMode session, so that quoted line was written by some other fixture's run.
2. **Even granting a dead microphone, failure 575 is unexplainable by it.**
   `ReportToDirector` is called at `VoiceCapture.cs:186`, deliberately *before* the
   `IsAvailable` check at `:195`, with a comment saying it must not sit behind any gate
   because "the creature hears the room, not the network". A missing microphone cannot
   make `VoiceEffort` read `Silent`.

Calling a red "environmental" is the most expensive kind of wrong answer, because it
converts a defect into an expected result. That is [B-013](#b-013)'s lesson in a different
costume.

### What it costs the game

Not stamina — there is no cost for talking anywhere in the design, and §06 is the monster
section, not a voice section. What is lost is the **noise cue**: §06's patrol leaves
순찰 on 「소리 감지 → 경계」, `MatchDirector.cs:935–951` implements it, and
`VoiceRules.cs:115–136` prices it — Whisper 0 m, Talk 12 m × floor clarity, Shout 30 m ×
1.4 × clarity, i.e. **42 m on metal**. With `VoiceEffort` stuck at `Silent` the creature
is never told a runner is speaking, so **twenty people can shout to each other across a
floor and nothing hunts them for it.** That is the one thing voice was supposed to cost in
a game whose currency is time, and it is exactly the shape of [F-012](BALANCE-FINDINGS.md#f-012)
— the monster could never chase anybody, because nothing ever made a sound.

Meanwhile `dist/READ-ME-FIRST.txt` tells playtesters 「음성 대화는 동작이 확인되지
않았습니다」, which is honest, and this entry is why.

### What it would take

1. Decide who owns the effort. Either `VoicePushToTalk` stops writing when it has no
   device (make the `Keyboard.current == null` guard actually hold in batch mode, and
   test *that*), or `VoiceCapture.SetEffort` gains an explicit override the fixture can
   take and the push-to-talk component respects.
2. Re-run the fixture. The three tests already assert the right things; nothing about them
   needs weakening.
3. **Do not baseline these as expected failures**, and do not restore the "microphone"
   explanation. A vacuously-green fourth test is already in this fixture; a fifth would
   finish the job of making voice look tested.

> **Note the shape of this entry, because it recurs.** The measurement was four days old
> and correct, the diagnosis attached to it was wrong, and the wrong diagnosis was the
> reason nobody filed it. A red with an explanation stops being read.

---

## B-021 · 🟢 The deepest floor was the brightest room, and the fixture that did it had been cleared by an experiment that could not have found it

**Status:** 🟢 **closed 2026-08-12** · opened 2026-08-10 · cause found and fixed;
all eight storeys now in all four ART.md bands

`SceneShot.BuildViews` photographed `Zone_*` transforms `.Take(6)`. That cap was written
on 2026-07-31 for a three-storey building; 하강 has stacked **eight** storeys since
2026-08-05. So **B7 수몰층 and B8 굴착층 had never been photographed at all** — silently,
with no warning — while every luminance table in ART.md was presented as the building's
numbers. The cap is removed (every zone, not a count), and the first pictures of the
bottom two floors are also the first measurement of them:

```
shot                          mean    p50    p90    p99  black%  legible%  blown%
eight_Zone_B7_B7_Water.png    15.1    6.3   27.5  227.5    32.6      42.3    0.17
eight_Zone_B8_B8_Earth.png    32.0   28.9   60.5  102.6     4.5      87.8    0.00
```

B7 is in all four bands. **B8 breaks three at once** — crushed 4.5 % against a 10–40 %
band, legible 87.8 % against 30–75 %, median 28.9 against 3–16. ART.md's own wording for
the bottom of that first band is the point: *"below 10 %, the dark is not dark."*

**Why it matters more than a band number.** §07 promises the night deepens as the race
descends, and `ScatterSession.LightStratifiedBulbs` implements it — the working-bulb count
falls with depth, so B8 has the fewest lit fittings in the building. It is nonetheless the
brightest room in it. The deepest floor of a game whose central mechanic is darkness, the
one §02 puts the finish on, is lit like a corridor at head office.

**CLOSED 2026-08-12. The finish light was the cause, and the entry below said it was not.**

**The inference that cleared it was invalid.** This entry read: shadows were switched on,
the numbers did not move by a decimal, therefore `MapSceneBuilder.BuildFinishLight` is not
the cause. That does not follow, and it cost two days. Turning shadows on only removes
light arriving *through* geometry. This fixture and B8's zone camera stand in the same
room with nothing between them, so shadowing could never have changed what it contributes
directly. **The experiment that was owed — switching it off — had never been run**, by
anyone, once. `MapSceneBuilder.cs` carried the same wrong conclusion in a comment, so the
error was recorded in two places and checkable in neither.

Run here, one variable at a time, `SceneShot` at native brightness, controls in every row:

```
finish light            crushed  legible   median      B7 (control)      B1 (control)
18 m point (shipped)        1.7     94.3     36.4    24.6/46.3/7.2    16.1/54.8/9.0
 6 m point                  2.2     92.0     32.9    unchanged        unchanged
 4 m point                  7.7     81.2     25.8    unchanged        unchanged
 3 m point                 12.9     71.9     19.6    unchanged        unchanged
 2 m point                 15.9     59.6     12.2    unchanged        unchanged
 off                       18.9     57.7     11.2    unchanged        unchanged
 5.5 m spot, 80° (FIX)     16.0     61.1     13.0    24.6/46.3/7.2    16.1/54.8/9.0
```

Not one of the other seven storeys moved by a decimal across any of those runs, so this is
local to the fixture and not a global exposure shift.

**Radius could not fix it; the shape was wrong.** The fitting hangs 3.6 m above the floor,
so every point range short enough to stay out of the frame is also too short to reach the
floor at all — the 2 m row is "off" with an unlit finish, which breaks §02. Every range
that does reach the floor also fills the room. The fix is a **spot aimed down** at
`FinishLightRangeMetres` = 5.5 m, 80°, giving a ~3 m pool: §02's promise is that the
finish is lit and findable from the dark, not that the storey is.

**The 18 m was its own defect, luminance aside.** It came from
`GameConstants.ZoneLightRadius` — a *zone* radius on a *fitting*. Every other working
fitting in the building uses `ScatterSession.PracticalRangeMetres` = 5.5 m, chosen
deliberately under half of §03's `FlashlightRange` so **the torch you carry is always the
longest reach in the building**. The finish light broke that rule by 3.3×.

**What the new floor textures did, since it was the other hypothesis.** Nothing, and the
run is worth keeping: `Floor_Earth` replacing a variance-free placeholder moved B8 from
4.5 / 87.8 / 28.9 to 1.7 / 94.3 / 36.4 — *further* out of band, because the real material
is brighter than the black rectangle it replaced. The texture pass was not the cause and
did not become one; the fork it was set up to decide came down on light, not variance.

**All eight storeys are now in all four bands**, measured in the same pass:

```
zone              crushed  legible  median  blown      band: 10–40 / 30–75 / 3–16 / <0.5
B1 Concrete          16.1     54.8     9.0   0.00
B2 Wood              26.0     35.7     5.7   0.00
B3 Metal             25.9     53.1     9.8   0.00
B4 Gravel            25.5     37.4     5.8   0.00
B5 Tile              11.8     63.6    11.1   0.00
B6 Carpet            12.8     67.5    14.7   0.00
B7 Water             24.6     46.3     7.2   0.17
B8 Earth             16.0     61.1    13.0   0.00
```

**One thing this does not fix, stated rather than buried.** §07 wants the night to deepen
with depth, and B8's median 13.0 still sits above B2's 5.7 and B4's 5.8 — the deepest
floor is no longer the *brightest room*, but it is not the darkest either. It is the floor
§02 puts the finish on, so some of that is correct by design; whether all of it is has not
been measured and is not claimed here.

**The dead ends below are kept as history**, in the order they were going to be tried,
because each was a reasonable guess and none of them was it: the `ZoneIdentity` row for `Earth`
(tint 1.12/1.06/0.96, smoothness 0.80, occlusion 0.95 — its own note calls it "the
smallest lift in the table", which the measurement contradicts); whether B8's floor
material resolves to something far brighter than intended, since `Floor_Gravel` is the
brightest set in the kit at 0.44 linear and the manifest has no `Floor_Earth` entry at
all; and the atmosphere pass's per-storey ambient. Measure each against B7, which sits in
band on the same pass and is one storey away.

**Do not fix this by widening the band.** ART.md's history already records a round where
a band was fitted to a measurement, and the entry that undid it.

---

## B-020 · The player-reach audit counted its own measuring body as a wall

**Status:** 🟢 closed 2026-08-05 · found while a roster bake refused every slot it staged

`PlayerTraversal.Audit` decides whether a runner can stand somewhere with
`Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore)`. A `~0`
mask cannot tell a wall from a body, and `SoloPlaytest.Build` parks the harness rig on the
first `PlayerSpawn` with a `CharacterController` on it. So the audit's capsule met the
capsule the audit exists to simulate, and reported the marker unreachable. **재는 사람이
재는 대상 안에 서 있었다.**

Measured over one baked surface, both scenes referencing NavMesh guid
`26ffd78e0ece1459686bbf4580765605`:

| scene | starts | B1 walkable | verdict |
|---|---|---|---|
| `Map_FirstSketch.unity` (no rig) | 36/36 | 18 077 | PASS |
| `Map_FirstSketch_Solo.unity` (+ rig) | **34/36** | 18 065 | FAIL |

The two lost starts were `PlayerSpawn_10` and `PlayerSpawn_11`, both at
(23.75, 0.00, 6.25) — where the rig stands. That was the *entire* difference between the
two reports, and because `MapPipeline.AuditSlot` is the first audit in the pipeline whose
subject is the solo assembly rather than the layout, it refused all eight slots of the
2026-08-05 bake: 8 staged, 0 published.

**Fix.** `Audit` now disables every `CharacterController` in the scene, runs
`AuditTheBuilding`, and re-enables them in a `finally`. Nothing is saved; the toggle lives
in memory for the length of the audit. Only `CharacterController`s — props, walls, doors
and §06's creature all stay in, so a crate that seals a start is still a refusal — and each
body stood down is named in the report's notes, so the audit says what it excluded.

**Verified in the artefact**, 2026-08-05 on `gen-20260805-025901-seed20260802`:
`Map_FirstSketch_Solo.unity` now reads **PASS · starts 36/36 · B1 18 113**, identical to the
layout scene's 18 113, with the note `Stood Player down for the measurement — a
CharacterController at (23.75, 0.00, 6.25) …`. `m_Enabled: 1` on the `!u!143` block in the
solo scene and in all three `Map_Descent_*` slot scenes on disk, so the restore holds.

**The reading.** An audit that shares a mask with its own subject has to say so and exclude
it deliberately; a green number here would have been a body-shaped hole in the map. This is
the same class as [B-009](#b-009) — the NavMesh audited was not the one just built — and it
is why the two audits are always taken over a named, stamped surface.

---

## B-019 · Every storey is too short from the rim to the middle, and for two rounds nothing said so

**Status:** 🔴 **open** · opened 2026-08-05 · **21 of 22 entry points inside the band as of
2026-08-10**; one is 2.5 m short and the waiver stays until it is closed

> **Re-confirmed 2026-08-12, and it is now gated rather than merely pinned.**
> `MapTests.Descent_CentrePath_IsInsideSection12DsBandExceptAtTheRimCellBesideAGate`
> asserts the report contains `walk 87.5 m~132.5 m` and `1 of 22 OUTSIDE`, and it passes
> in today's `dotnet test core/HorrorGame.sln -c Release` — 357/357, 0 failed.
> `MapSceneGenerator.KnownFailingRules` holds exactly one entry, `RuleCentrePath`, which
> is the same fact from the generator's side. `471ffab` also made this one of the four
> §12 tests the required CI job runs by name, so a slide back toward 47.5 m now fails a
> push rather than a re-read.

> **Nearly closed, 2026-08-10, by the same re-lay that closed [B-007](#b-007).** The
> 중간 관문 now walks 20 m round the new d5 lane before turning in — length added to every
> route without widening the gap between the shortest and the longest, which is why one
> edit moved both blockers. `DescentMap.Pick` also changed: a 투하구 landing **is** a
> centre-path entry point on B2–B8, and choosing it "furthest along Z" put one beside a
> way in on some floors and half a ring away on others. It is now the cell furthest round
> the outermost ring from that storey's 외곽 관문.
>
> | storey | before | after |
> |---|---|---|
> | B1 하역장 | 47.5–82.5 m, **0/16** | 87.5–132.5 m, **7/8** |
> | B2–B8 (each) | 60–75 m, **0/2** | 102.5–132.5 m, **2/2** |
> | **total** | **0 / 30** | **21 / 22** |
>
> **The one that is still out is 2.5 m short — one cell.** It is the rim cell standing one
> step from a 외곽 관문, taking the shorter of the two ways in. The cell could not be
> found: each of the three 관문 is already the longest §12 permits, and the storey has no
> spare radius. That is a measured miss, not a rounding, and it is why the waiver stays.
> `MapTests.Descent_CentrePath_IsInsideSection12DsBandExceptAtTheRimCellBesideAGate` pins
> `87.5 m~132.5 m` and `1 of 22 OUTSIDE` — asserting a *failing* rule's exact measurement
> on purpose, so a slide back toward 47.5 m is the defect returning, and when the last
> 2.5 m is found that test and the waiver retire in the same commit.
>
> ⚠️ **What it cost, and it is a real regression: seed variation narrowed.** The four
> 외곽 관문 now stand at the same four bearings on every floor of every building, because
> band alignment admits exactly one jog pair per side. Buildings still differ (the arc's
> axis, which sides carry the 중간 관문, where the 막힌 길 sit) and
> `Descent_IsDeterministic_AndTwoSeedsAreNotTheSameBuilding` holds that — but **a player
> who learns where the rim's ways in are learns it once**, which is against the spirit of
> [B-018](#b-018). Also: `straight-corridor` now measures 20.0 m against a 20 m cap, with
> no slack, so anything that adds a cell in line with a band leg trips it.

§12-D writes the rule and says who checks it:

> | `centre-path` | 외곽에서 중심까지 최단 **90~140 m** | … | `MapValidator`와 씬 감사기가 층마다 확인한다 |

`MapValidator` did not. The number was measured and printed by `MapSceneGenerator` on
every generation instead, and it has never once been inside the band. It is
`MapValidator.RuleCentrePath` now, it gates, it fails, and
`MapSceneGenerator.KnownFailingRules` waives it by name with the numbers in the entry.

### The measurement

`MapValidator.Validate(DescentMap.Build(20260802).Graph)`, 2026-08-05:

```
[FAIL] centre-path — 외곽에서 중심까지 최단 90~140m (§12-D, 층마다)
  30 storey entry point(s) walk 47.5 m~82.5 m to their own storey's middle
  = 10.6~18.3 s at 달리기 4.5 m/s, against §12-D's 90 m~140 m. 30 of 30 OUTSIDE:
  B1 하역장 47.5 m~82.5 m (16/16); B2 기록보관소 70 m~75 m (2/2); B3 기계실 70 m~75 m (2/2);
  B4 저탄장 70 m~75 m (2/2); B5 저수조 70 m~75 m (2/2); B6 병동 70 m~75 m (2/2);
  B7 수몰층 60 m~65 m (2/2); B8 굴착층 70 m~75 m (2/2).
```

**Measured 47.5–82.5 m against a required 90–140 m: 30 of 30 entry points outside, every
storey 7.5–42.5 m short.** An entry point is where a runner actually arrives — a 투하구
landing on B2–B8 (§01: 「떨어지면 언제나 다음 층 외곽」), and on B1, which nothing drops
into, the sixteen cells of the 외곽 rail §01 stands twenty runners on.

### Why it is a defect in the map and not in the band

§12-D forbids exactly this move in its own text: 「60~90 m로 줄이면 아는 사람이 2분 만에
끝내고, 그러면 **맵을 아는 것이 실력이라는 전제**가 보상 없이 사라진다」. §01 leaves a race
two sources of difference — 길을 아는가 and 어디까지 감수하는가 — and a floor that can be
solved blind deletes the first. Fitting the band to the measurement would delete a premise
to make a number go green.

### What it would take

A longer rim-to-middle route in `RadialStorey`: more bands, or the ring gates set so the
way in spirals instead of cutting across. The rule reports per storey, so the fix has a
number that says the moment it lands — regenerate and read `all inside`.

It trades against [B-007](#b-007): more bands at the same 2.5 m cell size is more corners,
and corners are what B-007 already has too many of. **The two want the same change** — legs
long enough to be worth walking and straight enough to break sight — which is why they
should be fixed together rather than in sequence.

---

## B-018 · Every match is the same building, and the last five lines are in a file this change could not touch

**Status:** 🟢 closed 2026-08-05 · the seam landed, the roster names three buildings and a
second match measurably loads a different one

### How it closed, measured in the artefact

`DescentRoster.txt` names **3** buildings (씨앗 463793241 · 1246502161 · 20260802, 지문
`A135BAD7`); all three scenes exist under `Assets/Scenes/Descent/` and all three are
`enabled: 1` in `ProjectSettings/EditorBuildSettings.asset`; and the mac development player
built from them ships all three — `Level 3/4/5`, 85.6 / 77.8 / 78.3 MB compressed. The seam
`GameShell` was missing is in `UI/LobbyScreen.cs` (`LobbyEntry.MatchScene`), `GameShell`
takes it in `LoadMatchRoutine`, and `RaceLobby.BeginDescent` sets it.

Four independent matches on 2026-08-05, host path, `LobbyEntryWiringTests`:

| 씨앗 | 건물 | scene actually loaded |
|---|---|---|
| 2057322048 | Map_Descent_0 | `Assets/Scenes/Descent/Map_Descent_0.unity` |
| 1341029346 | Map_Descent_0 | `Assets/Scenes/Descent/Map_Descent_0.unity` |
| 505594061 | Map_Descent_2 | `Assets/Scenes/Descent/Map_Descent_2.unity` |
| 1984380182 | Map_Descent_2 | `Assets/Scenes/Descent/Map_Descent_2.unity` |

The choice is `DescentRoster.IndexFor(seed, count)` = `(uint)seed % count`, made once on
§13's authority and **sent**, so it is the log line and the loaded scene agreeing rather
than two machines each deriving a number. 「씨앗 = 건물」 is now true of the maze and not
only of the starting ring.

**One claim in the text below is now false and is left in place as the record:** the
paragraph saying `MapSceneGenerator.RegisterScenes` drops the slots "routinely" described
the hard-coded three-path version. It is manifest-driven now — a plain map regeneration was
run on 2026-08-05 (`gen-20260805-025901-seed20260802`, no `-forceWrite`) and Build Settings
still lists all six scenes afterwards. `RaceLobby.VerifyRoster` still refuses to host on a
building Build Settings does not have, because that failure ends with twenty people on a
loading screen that never finishes.

**Latent, N ≥ 10:** the registration check is `manifest.IndexOf(diskSceneName)`, so an
unpublished `Map_Descent_1` would be registered on the strength of a published
`Map_Descent_10` containing it as a substring. Harmless at three. Fix by matching
`Separator + name + Separator`.

### The original entry, as written on 2026-08-04

§11's lobby picks a seed, agrees it with everybody, broadcasts it and logs it. The lobby
screen tells players what that number means, in as many words: 「씨앗 *is* the building —
twenty players holding the same number are in the same maze」. It was half true. The scene
is pre-baked at `DescentMap.DefaultSeed = 20260802`, so the seed decided **only** where
runners start (which is real — §01 deals B1's ring from a Fisher-Yates shuffle of it). The
maze itself was 20260802 every match, forever.

For a race whose stated skill is 「맵을 아는 사람이 유리하다」 that makes map knowledge a
permanent asset rather than a per-match one, and the twentieth match is the first one
already solved.

### What is in the build now

`MapPipeline.BakeRoster` bakes one scene per seed and publishes only the slots that
survive every gate — §12's checklist, the NavMesh connectivity audit and the
player-traversal audit inside `MapSceneGenerator.Generate`, then the dressing pass and a
second connectivity + traversal audit in `MapPipeline.Regenerate`, then a **third** pair of
audits taken on the copied slot scene after its NavMesh reference has been moved to its own
copy of the bake. It writes `DescentRoster.txt` from what it actually published, so the
roster in the artefact is a list of buildings that passed rather than a list of seeds
somebody believed in. `-forceWrite` is refused outright: a forced generation is one with a
named defect in it, and a roster is a list of buildings twenty people may be put into.

`DescentRoster` reads that manifest at run time. `RaceLobby.RequestHost` refuses to claim
§13's authority if any listed building is missing from Build Settings — which it will be,
routinely, because `MapSceneGenerator.RegisterScenes` rewrites the scene list wholesale
from three hard-coded paths (this is B-005 with a wider blast radius). `RequestStart`
chooses the building on the host and **sends** it — index, building seed, scene name and a
fingerprint over the whole roster — rather than letting each machine derive it, and a client
whose fingerprint differs leaves the session instead of descending. After the scene loads,
`RaceLobby.OnSceneLoaded` compares the `SceneGen_` generation stamp inside the scene against
the one the roster recorded, which is the only check in the game that reads the artefact
rather than a name two machines agreed on.

### What is missing, exactly

`GameShell` owns the scene load — the loading screen, its minimum-display floor, the Build
Settings error path and `SettingsService.Apply()` after activation — and it loads
`GameShell.DefaultMatchScene`. There is no way to tell it which building to open, and there
cannot be one from the gameplay side: `HorrorGame.UI` does not reference
`HorrorGame.Gameplay` and must not, which is the entire reason `LobbyEntry` exists. The seam
has to be declared in the UI assembly and filled from the race assembly, exactly as
`LobbyEntry.Intercept` already is. That is five lines across `UI/LobbyScreen.cs` and
`UI/Shell/GameShell.cs`; both are outside the files this change owned, so the diff is in the
report rather than in the tree.

**Until it lands the maze does not vary.** Every machine loads the same default scene, so
the race is still fair — it is the variety that is missing, not the agreement — and
`VerifyLoadedBuilding` logs the mismatch once per descent naming this entry's cause rather
than letting it pass quietly.

### Why pre-baking and not generating at run time

Measured, not argued. `MapPipeline.ProbeSeeds` timed every phase on three fresh seeds
(`/private/tmp/.../logs/probe.log`, 2026-08-04, batch-mode editor, Apple silicon):

| phase | seed 463793241 | 1246502161 | 143331277 |
|---|---|---|---|
| sketch — `DescentMap.Build` | 0.04 s | 0.01 s | 0.01 s |
| geometry — prefab instantiation + first bake | 1.01 s | 1.00 s | 1.01 s |
| dressing scatter | 3.73 s | 3.77 s | 3.89 s |
| **`NavMeshSurface.BuildNavMesh`, dressed** | **0.81 s** | **0.86 s** | **0.83 s** |
| §12 gate — `MapValidator.Validate` | 69.20 s | 69.98 s | 70.28 s |
| NavMesh connectivity + player traversal | 129.74 s | 114.45 s | 125.34 s |

**Generating the building at run time is affordable. Proving it is not.** Everything a
player could actually run — sketch, geometry, dressing, bake — is **≈5.7 s**, an ordinary
loading screen. The gates are **≈190 s**, three minutes, on every machine before every
match. Cutting them means shipping a generator with no gate, and the 9-island dressing
failure that this project caught in the previous round is exactly what a runtime map would
have shipped instead.

The 5.7 s is also the reason not to shut the door: `RadialStorey` and `DescentMap` are
already engine-free, and what blocks route (b) is the cost of the audits, not the cost of
the building.

**Provenance, because these numbers are not all from the same validator.** The 96-seed
graph sweep ran against a `MapValidator` identical to `1b4b267`. The probe above ran a few
hours later, against a working tree in which another change had retired two rules as
obsolete — so it reports `passed=True failures=0` where HEAD reports three failures, all of
which `MapSceneGenerator.KnownFailingRules` waives anyway. **Neither the sweep's verdict nor
the probe's changed as a result**: every seed measured was writable under both rule sets.
But a §12 *tally* taken from either log is only a claim about the validator it ran with, and
`ProbeSeeds` now prints the rule count beside the verdict so the two can never be confused.

### Which of these gates actually has to run per seed

Worth separating, because it decides how many buildings the roster can afford:

| | per seed | does it gate? | has it ever caught anything? |
|---|---|---|---|
| `MapValidator.Validate` | 69–70 s | **yes** — `Buildable` *is* `Validation.Passed` | not once in 96 seeds; and structurally cannot see the failure that matters — «a graph is joined whatever the baked surface does» (B-001) |
| NavMesh connectivity + `PlayerTraversal` | 114–130 s | **yes** | B-001, B-008, B-009, and last round's 9-island dressing failure — all of them |
| the rest of `MapQualityReport.Measure` | ~75–100 s | **no** | n/a — it is report text |

The last row is free to drop and worth ~75–100 s per generation. `MapQualityReport
.BreakSpacings` has no reader outside `DescribeSpacing()` in its own file, and `Measure` has
exactly two callers: `ReportQualityMenu`, whose whole job is to print the report, and
`Generate`, which reads only `.Buildable` to decide and then puts `.Describe()` in a log
line. Splitting the gate from the prose — `Generate` calling `MapValidator.Validate`, and
building the full report only when it is about to be printed — is a small change in
`MapSceneGenerator.cs` / `MapQualityReport.cs` and is the single cheapest thing available
here.

**The audits are not cheap, and it would be convenient to believe they are.** 114–130 s for
the pair is the *most* expensive gate in the pipeline after §12, not the cheap one — and
`BakeRoster` runs them three times per slot (inside `Generate`, again after the dressing
rebake, and again on the copy). Each pass has a distinct subject: the undressed layout, the
dressed building, and the file the game will actually open with its own bake wired in. None
of the three can go without giving up something the previous rounds paid for. So the real
per-slot cost is ≈7 min of gate, and N=8 is roughly an hour of author time — which is fine,
because it is author time. What does *not* scale is screening candidate seeds, and it does
not have to: the screen only ever runs on the handful being considered for a slot, and
`BakeRoster` gates every slot again regardless of what the screen said.

### What it would take

1. The five-line seam (see the report). After that the roster is live and a player sees a
   repeat every N matches, N being however many slots the bake published.
2. `MapSceneGenerator.RegisterScenes` should keep the descent slots instead of dropping
   them, so the lobby's refusal is a safety net rather than the normal state after any map
   regeneration.
3. An EditMode test asserting every manifest line has a scene on disk and in Build
   Settings. The runtime refusal covers the shipped game; a test covers the build that
   never gets hosted.

---

## B-017 · The first-person view has no hands on the runner rig

**Status:** 🟢 **closed 2026-08-10** by `b92ae78` · found 2026-08-03 · **two of this
entry's own asks were not done, and they are carried below rather than dropped**

> **How it closed.** `b92ae78` — *"the player finally has hands — a dedicated viewmodel
> instead of a third-person body seen from inside"*, authored 2026-08-10 00:23:01 +0900.
> Not 08-11: that date has been corrected here because a closure dated a day late is a
> closure nobody can find in `git log`.
>
> The fix is neither of the two this entry proposed. Rather than re-exporting `Runner.fbx`
> with `MESH_SPLIT`, or teaching `PlayerFirstPersonView` to find arms by bone subtree, a
> **separate viewmodel asset** was authored: `Assets/Models/Player/RunnerArms.fbx`
> (558 620 bytes, tracked, 7 bones, 5 730 tris, 8 clips), instantiated under the rig's
> Camera by the new `PlayerFirstPersonView.EnsureFirstPersonArms()` (`:339`, called from
> `:160` and `Collect()` at `:437`). Classification is now **by parentage** (`:441–455`) —
> anything under `_firstPersonArms` is arms — with the old material-slot rule kept only as
> a fallback. That removes the export-flag dependency this entry complained about, which
> was the point of proposal two, without the bone-subtree heuristic.
>
> **Measured, from the commit's own figures:**
>
> | | before | after |
> |---|--:|--:|
> | frame covered by the body | 78–100 % | **7.1–12.2 %** |
> | gloves on screen | **none** (viewport y −0.21 to −2.25, or behind the camera) | both, in every live clip |
> | nearest geometry to the eye | 15.8 mm — inside the near plane | **151.9 mm**, 3× the near plane |
>
> The engine-side confirmation is a log line quoted in STATUS.md — `1 arm renderer(s)
> drawn (1 of them the RunnerArms viewmodel) … arms: RunnerArms bones=7 weighted=3` —
> from `biggate/8_solo.log`, **a file that no longer exists**. So the closure rests on the
> commit's measurements and on the asset being in the tree, not on a re-readable log.

### Two things this entry asked for that did **not** land

Carried forward deliberately, because both are the reason the defect was invisible for
five days in the first place:

1. **The warning is still a warning.** `PlayerFirstPersonView.cs:489` still calls
   `Debug.LogWarning`, with the same opening sentence — only the remedy text was
   rewritten. A *second* `LogWarning` was added at `:381` for a missing `RunnerArms.fbx`.
   This entry's own words stand: *a first-person game whose player has no body is not a
   warning*.
2. **No test covers the viewmodel.** `PlayerFirstPersonViewTests` was not touched by
   `b92ae78`; its last commit is `e8c67ae`. `RunnerArms`, `EnsureFirstPersonArms` and
   `FirstPersonArms` appear **nowhere** in `Assets/Tests/PlayMode/Player/`. The fixture's
   `BuildSplitRig()` builds from `Player_Body`/`Player_Arms`/`Player_Torch` — the
   vocabulary of a model `PlayerFirstPersonView.cs:448` says this project no longer ships
   — and contains no `Camera`, so `EnsureFirstPersonArms` returns null at `:365` and the
   viewmodel branch is never entered. The commit gates it out on purpose ("the synthetic
   test fixtures never grow a viewmodel"), so its reported `PlayerFirstPersonViewTests
   7/7` is true and says nothing about the fix.

**Net: a regression here would still be silent, and would still only be a warning.** That
is a smaller entry than this one and belongs to whoever owns those files; it is recorded
here rather than left as a fresh discovery for the next reader.

### The original entry, as written on 2026-08-03

Printed on **every** rig build, in `/tmp/r6_solo.log` and `/tmp/r6_net.log` (both since
deleted):

```
[Player] No renderer under this rig reads as the owner's hands, so they will see nothing
of themselves. §05 asks for 손. Player.fbx must export Player_Body, Player_Arms and
Player_Torch as separate meshes — check MESH_SPLIT in the gen_player_model.py output.
  hidden: Runner bones=13 weighted=1 slots -> Unknown materials=1
```

`PlayerFirstPersonView` splits body / arms / hand-prop by material slot. `Runner.fbx`
exports one slot, so it finds nothing to keep visible and hides the whole rig. **In first
person you see no part of yourself.** This was defect 3.23 in the previous edition of
STATUS.md, fixed on 2026-08-01 against `Player.fbx`, and it returned when the race rig
moved to `Runner.fbx`.

### What it would take

Either `gen_player_model.py` exports `Runner.fbx` with the same `MESH_SPLIT` the player
model already has, or `PlayerFirstPersonView` learns a second way to identify arms
(bone subtree rather than material slot). The first is the smaller change and keeps one
rule; the second stops the rule depending on an export flag nobody sees.

**Make it fail rather than warn.** `PlayerFirstPersonView.Report` calls
`Debug.LogWarning`. A first-person game whose player has no body is not a warning, and
the PlayMode fixture that would have caught it (`PlayerFirstPersonViewTests`, 7 cases,
all green) builds its rig from a different prefab.

---

## B-016 · EditMode has not been run since the pivot

**Status:** 🟢 **closed 2026-08-08** · found 2026-08-03 · the run happened: **95/95**

> **How it closed.** EditMode ran on **2026-08-08 at 16:56Z, 95 of 95 passing**, recorded
> in STATUS.md's verification table and committed at `471ffab`. Two earlier commit
> messages report the same figure independently — `8bf2e75` (2026-08-05) and `8db3d78`
> (2026-08-09), both "EditMode 95/95".
>
> **The XML is gone** (`/tmp` was wiped on 08-10), so the run itself cannot be re-read.
> What *can* be checked is whether 95 is the right number, and it is — counted from the
> sources at `4ab204f`, 2026-08-12:
>
> | file, under `Assets/Tests/EditMode/` | cases |
> |---|--:|
> | `Audio/AudioTests.cs` | 26 |
> | `UI/UiTests.cs` | 17 |
> | `Pivot/PivotTombstoneTests.cs` | 47 (6 `[Test]` + 41 from three `[TestCaseSource]`s) |
> | `Pivot/PivotSceneTombstoneTests.cs` | 4 |
> | `Pivot/PivotAssetTombstoneTests.cs` | 1 |
> | **total** | **95** |
>
> The 41 generated cases come from `Pivot/DeletedVocabulary.txt` — 10 `token|`, 24
> `probe|`, 7 `assetprobe|` rows, parsed by `PivotVocabulary.cs`. Pivot alone is **52**,
> which is exactly the figure STATUS.md quotes from the run. Two independent counts landing
> on the doc's two numbers is as close to re-reading the XML as this machine now gets.
>
> **And the answer to the entry's real question — red, or green while testing a game that
> no longer exists — is the third one it did not consider: the platform was rebuilt.**
> Of the five files this entry tabulated, three were deleted outright at `e8c67ae`
> (`DropPlacementTests.cs` 13, `MatchGuidanceTests.cs` 2, `SoloMatchLoopTests.cs` 1),
> `UiTests.cs` fell 59 → 17 as the §08 shop went with them, and `AudioTests.cs` is
> untouched at 26. Nothing under `Assets/Scripts/**/Editor/` carries a `[Test]` any more.
> The 52 new Pivot cases exist to assert the co-op game **stays** deleted — so the platform
> that was "green while testing a game that no longer exists" is now substantially a
> platform whose job is to prove that game is gone.
>
> The sentence this entry ended on — *"no document in this repository may quote a total
> that includes an EditMode number"* — is lifted as of 2026-08-08.

### The original entry, as written on 2026-08-03

Every test result in `/tmp` newer than the pivot is PlayMode. The newest EditMode XML on
this machine is `/tmp/editmode.xml`, `start-time 2026-08-01 11:51:06Z`, **101/101** —
against the four-player co-operative game. Every other EditMode result file is older
still.

What is in EditMode, counted from the sources at `a3e268e`:

| File | `[Test]`-family attributes | Covers |
|---|:--:|---|
| `Assets/Tests/EditMode/UI/UiTests.cs` | 59 | the §08 shop, **deleted by the pivot** |
| `Assets/Tests/EditMode/Audio/AudioTests.cs` | 26 | §12's material alphabet |
| `Assets/Scripts/Gameplay/Interaction/Editor/DropPlacementTests.cs` | 13 | dropping 전리품 |
| `Assets/Scripts/Editor/Playtest/MatchGuidanceTests.cs` | 2 | §14 guidance overlay |
| `Assets/Scripts/Gameplay/Match/Editor/SoloMatchLoopTests.cs` | 1 | the §01 co-op loop, **deleted by the pivot** |

So the platform is either red, or green while testing a game that no longer exists, and
**nobody knows which.** Both possibilities are bad and they need different fixes, which
is why this is filed rather than assumed.

### What it would take

One run — `-testPlatform EditMode`, no `-quit`, read the XML (TESTING.md §4). Then a
decision per fixture: retire, rewrite for the race, or keep. Until that run happens, no
document in this repository may quote a total that includes an EditMode number.

---

## B-015 · There is no shippable build — ~~IL2CPP will not compile on this host~~

**Status:** 🟠 **open — owner action, one build wide** · opened 2026-08-03 ·
**the cause this entry names is fixed; the claim in its title is now untested**

> **The host toolchain is repaired. Measured 2026-08-12, no Unity involved:**
>
> ```bash
> printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp
> clang++ -std=c++17 /tmp/p.cpp -o /tmp/p          # exit 0 — binary produced
> ```
>
> **Exit 0, with `CPLUS_INCLUDE_PATH` unset.** And the directory this entry blames is not
> merely repaired, it is **gone**: `/Library/Developer/CommandLineTools/usr/include/c++`
> no longer exists, so clang can no longer prefer a broken copy over the intact one. The
> SDK's copy is still whole at 185 headers. Apple clang 17.0.0, `xcode-select -p` →
> `/Library/Developer/CommandLineTools`. **The two explanations this entry could not
> separate are now moot — neither can reproduce.**
>
> **But the title is still literally true, and that is why this stays open.** `dist/`
> holds two players, both from 2026-08-10 at `471ffab`, and **both are Development/Mono**:
>
> | | config | backend | report's own verdict |
> |---|---|---|---|
> | `dist/macos-arm64` | Development | Mono | `shippable on Steam: no — this is a Development build` |
> | `dist/windows-x64` | Development | Mono | `shippable on Steam: no — this is a Development build` |
>
> No `MONO-FALLBACK-DO-NOT-SHIP.txt` exists anywhere under `dist/`, and no build log does
> either — `dist/logs/` is gone with `/tmp`. **So no Release build is recorded on this
> machine since 2026-08-03**, and there is no IL2CPP log to read, failing or passing.
> (Stated as an absence of evidence, which is what it is: a Release attempt could have been
> made and left nothing behind. Nothing suggests one was.)
>
> **So the honest state is: a blocker whose stated cause has been disproved and whose
> claim has never been re-tested.** It is one command from being closed or from being
> re-opened with a real cause, and until somebody runs that command it must not be quoted
> either way — least of all as "IL2CPP does not work here", which is now a claim with no
> evidence behind it at all. Step 2 below (`sudo rm -rf` the Command Line Tools) is
> **withdrawn**: the directory it would delete is already absent.
>
> Requires Unity, so it belongs to the owner. Step 3 — that a Windows Release player
> cannot be produced on a Mac at all — is unaffected by any of this and is still the
> reason Steam needs a Windows machine or the licensed runner.

### The original entry, as reproduced on 2026-08-03

```
exit code : 4
  macOS universal (Apple silicon + Intel)   Release   IL2CPP FAILED   125.27 MB   23s
.../libil2cpp/codegen/il2cpp-codegen.h:24:10: fatal error: 'cmath' file not found
```

`/tmp/r5_build_release.log`, and identically in `r4_build_release.log` and
`r3_build.log`. The Development/Mono player builds fine (exit 0, 387.92 MB,
`dist/last-build-summary.txt`), so what is missing is precisely the configuration that
ships.

### It is not Unity, and the proof takes two lines

Measured 2026-08-03 with no Unity involved:

```bash
printf '#include <cmath>\nint main(){return 0;}\n' > /tmp/p.cpp
clang++ -std=c++17 /tmp/p.cpp -o /tmp/p          # fatal error: 'cmath' file not found

ls /Library/Developer/CommandLineTools/usr/include/c++/v1           | wc -l   #  11
ls /Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1 | wc -l  # 185
```

This machine's Command Line Tools hold 11 of the 185 C++ headers, and clang prefers that
directory over the intact copy inside the SDK.

### The workaround works, and was not used

Also measured 2026-08-03 — the same compile with

```bash
export CPLUS_INCLUDE_PATH=/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/include/c++/v1
```

exits 0. None of the three failed Release runs in this session shows that variable being
set. **So two explanations fit the logs equally well** — the toolchain is broken beyond
what the workaround covers, or nobody exported the variable — and they have not been
separated. Separating them costs one build.

### What it would take, in order

1. Re-run the Release build with `CPLUS_INCLUDE_PATH` exported (TESTING.md §7). If it
   passes, this entry becomes a documentation problem, not a blocker.
2. If it still fails: `sudo rm -rf /Library/Developer/CommandLineTools && sudo xcode-select --install`.
   That is a system change and belongs to the owner, not to an agent.
3. **Neither fixes Windows.** IL2CPP calls the target platform's own compiler, so a
   Windows Release player cannot be produced on a Mac at all — a Windows Release build
   here silently falls back to Mono and the pipeline drops
   `MONO-FALLBACK-DO-NOT-SHIP.txt` beside it. Shipping to Steam needs a Windows machine
   or the Windows runner in `.github/workflows/unity.yml`, which has never run because
   it needs a licence. See [CI.md](CI.md) and [STEAM-RELEASE.md](STEAM-RELEASE.md).

`Horror ▸ Build ▸ Report Build Environment` reports `release backend IL2CPP` for macOS
either way — it asks whether the host OS matches the target, not whether the C++
toolchain is intact. Do not read it as a green light.

---

## B-014 · §12's report says FAIL for three different reasons and distinguishes none of them

**Status:** 🟢 closed 2026-08-05 · closed by deletion and by waiver, not by a third verdict

The three-way `FAIL` is gone, and the resolution is not the one proposed below. Two of the
three rules were obsolete and were **deleted with their reasoning at the rule's own
tombstone in `MapValidator`** rather than moved to an `n/a` verdict — `zone-diagonal` (it
sized a 구역 SMALLER than a 층, and on 하강 a 구역 IS a 층) and `concealment-near-exit`
(§07 새벽's ambush). The third, `open-adjacent-to-maze`, was deleted too and **that part was
wrong**: the rule has two clauses, only its 15~25 m clause rested on §04's 주자 picking an
aggro range, and the 인접 clause the shipped map *satisfies* went with it. It is back, minus
the deleted clause, and it passes.

What the report reads now, `MapValidator.Validate(DescentMap.Build(20260802).Graph)`:

```
§12 map validation — 하강 — 요양원 지하 8층: FAIL      ← 12 ok, 2 FAIL, both waived by name
[FAIL] sight-break-spacing   95 m of cover against 14.4 m      B-007
[FAIL] centre-path           47.5~82.5 m against 90~140 m      B-019
```

Both failures are genuine map defects, both are in `KnownFailingRules` with the measured
value, the required value and what fixing the geometry would take, and the generator prints
`This build has KNOWN MAP DEFECTS in it` on every write. A reader of that block can now tell
which to act on because there is nothing else in it. The **ID collision** noted below is
resolved by the straight-corridor entry being retired outright (the map measures 17.5 m).

**Below is the finding as it stood on 2026-08-03, kept because it is the record of what
was wrong.** Every present tense in it describes that day, not today.

`/tmp/r6_gen.log` ends its checklist with:

```
§12 map validation — 하강 — 요양원 지하 8층: FAIL
[FAIL] open-adjacent-to-maze · [FAIL] zone-diagonal · [FAIL] sight-break-spacing
```

and the map is written anyway, with a warning naming B-007. Three failures, and they are
three different kinds of thing:

| Rule | What it actually is |
|---|---|
| `sight-break-spacing` | a **genuine defect** — B-007, F-007, the reason the creature threatens nobody |
| `zone-diagonal` | **obsolete** — sized a zone for §03's clue chain, which the pivot deleted. A storey is now one 57.5 m ring system and its diagonal is 81.3 m by construction |
| `open-adjacent-to-maze` | **obsolete** — 개방 공간 existed so §04's 주자 could pull aggro from 15–25 m. Nobody pulls aggro for anybody in a race |

`MapSceneGenerator.KnownFailingRules` (`MapSceneGenerator.cs:640`) holds five entries:
those three plus `straight-corridor` (a real 22.5 m overshoot on the radial storeys,
currently passing at 17.5 m) and `concealment-near-exit` (obsolete — the 출입구 is now
the finish line, and a hiding place beside a finish line is somewhere to wait). The
source is candid about all of this; the comment says outright:

> *they belong in MapValidator as "not applicable to a descent map" rather than here
> beside genuine defects. The report should say obsolete, not FAIL.*

### Why it is a blocker rather than tidying

A gate whose top line is `FAIL` on the happy path teaches everyone to ignore its top
line. That is the same failure this project has recorded three times — [B-003](#b-003)
(two `LogError`s on every generation), [B-007](#b-007), and the CI incident in
[B-013](#b-013). And it currently obscures the one genuine failure inside the noise: a
reader of that block cannot tell which of the three to act on.

### What it would have taken — and the part of it still worth doing

The proposal was: `MapValidator` grows a third verdict beside ok/FAIL — `n/a (descent)` —
and the three obsolete rules move to it. Deleting them with the reason attached achieves the
same legibility without a third state to maintain, and it puts the reason where the next
reader is standing when they ask the question. **The lesson it cost:** "obsolete" is a claim
about a rule's *subject*, and it has to be checked clause by clause — one of the three was
obsolete in one clause and load-bearing in the other, and deleting it whole gave up a gate
the map passes for nothing.

Still open, and still worth doing: fix `NavMeshAudit.Report`, which prints
`← the surface is in pieces` on every run because eight islands is now correct.
**Re-checked 2026-08-12 — unchanged, and the line has moved to
`NavMeshConnectivity.cs:568`:** `sb.AppendLine($"  islands          {Islands}" + (Islands > 1
? "  ← the surface is in pieces" : string.Empty))`. One island per storey is the designed
answer (a 투하구 is a fall, not a path), so this scolds the reader on every correct run —
the same happy-path-cries-wolf defect the whole entry is about, surviving inside its own
closure.

---

## B-013 · CI was red for three commits whose messages said the suite was green

**Status:** 🟠 open (process, not code) · found 2026-08-03 · **both named holes are now
closed, and the failure they describe recurred anyway and ran for six days**

> **Re-triaged 2026-08-12. Read the recurrence first, because it is the whole entry.**
>
> From `471ffab`, 2026-08-10: the required `core tests (dotnet)` job had been running
> `dotnet run --project core/HorrorGame.Sim -- validate` — **a project deleted at
> `e8c67ae` on 2026-08-03** ([B-012](#b-012)). The gate therefore failed on **every push
> for six days and 37 commits**, on a command that could never succeed again. The commit's
> own reading is the right one: *"a red X nobody can fix is how people learn to stop
> reading it."*
>
> It was not found by anybody reading CI. It was found while rewriting STATUS.md against
> the artefact. **That is hole 1 — "nobody looked" — reproducing exactly, four days after
> this entry named it, on the very job this entry asked to be made required.** Making a
> check required does not make anyone read it.
>
> **Hole 2 is fixed.** `.github/workflows/ci.yml:28` now reads
> `cancel-in-progress: ${{ github.ref != 'refs/heads/main' }}`, so `main` no longer
> cancels its own runs and a published commit gets a verdict rather than a grey tick.
>
> **The step was repaired rather than deleted**, and how it was repaired is worth keeping:
> it now runs the four `MapTests` that own §12 by name — `Descent_EveryOtherSection12Rule
> _StillPasses`, `Descent_MeetsSection12sSightBreakSpacing` (pins B-007's closure at
> 12.5 m), `Descent_CentrePath_…` (pins B-019's exact remaining miss) and
> `Descent_IsDeterministic_…` (holds B-018) — **and asserts the filter selected at least
> four**, because a `--filter` matching nothing exits 0 while checking nothing, which is
> the same vacuum the deleted project left. A gate that can pass vacuously is the thing
> this entry is about, and that assertion is the only defence against it in the file.
>
> **The second red job named below is closed.** `asset audit (§12 audio)` passes:
> `bash tools/ci/verify_audio.sh` on 2026-08-12 → `RESULT: PASS`, 2 blocking defects, both
> accepted against `tools/ci/audio_baseline.json` and both pointing at
> [F-002](BALANCE-FINDINGS.md#f-002). The `gravel vs carpet` inversion that helped fail it
> in 08-03 no longer reproduces at all. **Note what the gate's PASS does and does not
> mean:** the audit underneath it still reads `RESULT: FAIL` with four inverted pairs. The
> gate passes because two of those are baselined with a written finding, which is exactly
> what `audio_baseline.json` asks for — but "the audio job is green" and "the audio is
> right" are different sentences.
>
> **Why this stays open.** Both mechanical holes are shut and the code half is fixed twice
> over; what has not been demonstrated is a human reading a CI result. The recurrence
> above is the evidence that the missing thing is not a setting.

`ChamberDockProbe.cs` landed in `Assets/Scripts/Editor/SceneGen/` at **a89cf64**
(2026-08-03 04:55). Both engine-free projects glob that folder, and the file does
`using UnityEditor`, so it took `HorrorGame.Core.Tests` and `HorrorGame.Sim` down with
14 × `CS0246` each — **`dotnet test` could not reach a single one of its 512 tests.**

It was excluded only at **a3e268e** (08:40). Verified per commit:

```bash
for c in a89cf64 af2563d 43cf488 a3e268e; do
  git show "${c}:core/HorrorGame.Sim/HorrorGame.Sim.csproj" | grep -c ChamberDockProbe
done
# 0  0  0  3
```

So three commits over 3 h 45 min were pushed with `ci.yml`'s `core tests (dotnet)` job
failing at its first step, and **43cf488's subject line is "…and the suite is green."**
It was: the Unity PlayMode suite was green, and that is what had been run. The dotnet
half — which is the half that runs on every push, needs no licence, and is the required
check — was dark and nothing said so.

### Two separate holes, and both are still open

1. **Nobody looked.** There is no record in this repository of any CI run being read
   during this session. A red push and a cancelled push look different on GitHub and
   identical here.
2. **`main` used to cancel its own runs.** `concurrency.cancel-in-progress` was
   unconditional, so a second push to `main` cancelled the first commit's run — leaving
   a published commit CI never finished judging, and a **grey** tick rather than a red
   one, which nobody goes looking at. (`.github/workflows/ci.yml` is being changed for
   this as of 2026-08-03; that file is not owned by this document. See [CI.md](CI.md).)

### There is a second red job right now

`asset audit (§12 audio)` also fails at `a3e268e` — measured 2026-08-03:

```
RESULT: FAIL — 2 unbaselined blocking defect(s)
  [consistency] gravel vs earth    — 28.5 dB quieter at low-pass 600 Hz
  [consistency] gravel vs carpet   — 12.2 dB quieter at low-pass 600 Hz
```

Carpet, Water and Earth were added as floor materials for B6/B7/B8 with clarity
constants but without the occlusion work, so F-002's contradiction — the clarity number
the HUD shows disagreeing with the loudness the ears get through a wall — now covers
five pairs instead of one. The gate is behaving exactly as designed: an unbaselined
blocking defect fails the build. Either fix the constants or write the finding up and
baseline it in the same commit, per `tools/ci/audio_baseline.json`'s own rule.

### What it would take

Run both jobs' commands locally before pushing (TESTING.md §1, §2 and §6 — they take
about a minute together), and make `core tests (dotnet)` a required status check on
`main` so a red run blocks rather than scrolls past.

---

## B-012 · The balance simulator measures a building the game deleted — F-006, a second time

**Status:** 🟢 **closed by deletion 2026-08-03** (`e8c67ae`) · found 2026-08-03 ·
**a defect in a thing that no longer exists is closed, and this entry says so rather than
disappearing**

> **How it closed, and it is not any of the three fixes proposed below.**
> `core/HorrorGame.Sim` was **deleted whole** at `e8c67ae` — *"DESCENT-PIVOT step 7 finally
> ran — the co-op game is deleted, not gated"* — in the same commit that removed §03's clue
> chain, §08's economy, the shop, the wallet and the battery economy. The commit is explicit
> about why: *"every subject it modelled is gone, so there is no balance simulator until one
> is written for a race."*
>
> **Verified in the artefact, 2026-08-12:**
>
> ```bash
> git ls-files core/HorrorGame.Sim | wc -l      # 0
> ls core/                                       # Directory.Build.props  HorrorGame.Core
>                                                #  HorrorGame.Core.Tests  HorrorGame.sln
> grep 'Project(' core/HorrorGame.sln            # HorrorGame.Core, HorrorGame.Core.Tests — no Sim
> ```
>
> Zero tracked files, no directory on disk, and gone from the solution. `dotnet test
> core/HorrorGame.sln -c Release` runs 357/357 against Core alone.
>
> **Option 3 in the list below is what happened, taken to its limit** — "retire or rewrite
> the §03/§08 half" turned out to be the whole thing. Options 1 and 2 are void: there is
> no `SimMap.Build` to point at `DescentMap.Build`, and no second number to compare.
>
> **The prohibition survives its own entry, and should be read as permanent rather than
> pending:** *no figure from `horrorsim` may be quoted anywhere* — not match length, not
> the threat curve, not the outcome mix. There is now no binary that could produce one, so
> any such figure appearing in this repository is a quotation from a document, and every
> such document is quoting a building **two** pivots old. [F-006](BALANCE-FINDINGS.md#f-006)
> is closed by the same deletion for the same reason.
>
> **The lesson this entry paid for is not deleted with the code**, and it is the reason
> this closure is written out instead of the entry being dropped: a build-time include
> guarantees the *sources* agree and guarantees nothing about **which function you call**.
> F-006 found that once, this entry found the identical defect a second time in the same
> file, and both times the mechanism was a compiled-in call that had silently stopped being
> the one the game makes. The next simulator — if one is written for the race — needs the
> runtime comparison proposed in option 2, not a csproj glob.
>
> **And the deletion had a tail nobody saw for six days.** CI's required `core tests
> (dotnet)` job went on invoking `dotnet run --project core/HorrorGame.Sim -- validate`
> until `471ffab` on 2026-08-10 — 37 commits of a gate failing on a command that could
> never succeed. That is filed under [B-013](#b-013), where it belongs, but it is recorded
> here too because **deleting a project is not finished when the project is gone**; it is
> finished when nothing invokes it.

### The original entry, as measured on 2026-08-03

Measured 2026-08-03, against the then-existing simulator:

```bash
~/.dotnet/dotnet run -c Release --project core/HorrorGame.Sim -- map
```

```
요양원 지하 8층 …  8 zones · 254 places · 285 passages · footprint 50 m × 95 m
built by FirstMapSketch.Build — the same call MapSceneGenerator makes before it lays
a single FBX, compiled into this binary rather than exported to it (F-006).
```

The game's map is **720 places, 814 passages, 57.5 m × 57.5 m** (`/tmp/r6_gen.log`). The
claim in that last line is false at `a3e268e`:

```
MapSceneGenerator.cs:146   map = DescentMap.Build(seed);          ← the race map
SimMap.cs:216              var sketch = FirstMapSketch.Build(seed);  ← the co-op map
```

`DescentMap.cs` is engine-free and **is** compiled into the simulator by
`HorrorGame.Sim.csproj`'s glob. It is simply never called. The simulator also still
resolves §03's clue chain and §08's economy, both deleted by the pivot; its own output
now says `§03 was deleted (game-design.md v1.0 · DESCENT-PIVOT §3)` while continuing to
simulate it.

### Why this is worse than it looks

[F-006](BALANCE-FINDINGS.md#f-006) is *this exact defect*, found on 2026-08-01: the
simulator built its own four-zone ring while the game shipped 164 places, so months of
economy tuning described a building nobody played. The fix was to compile the game's own
map sources into the simulator, and `HorrorGame.Sim.csproj` carries three long comments
explaining why an exported copy would drift and a compiled call cannot. **The call
drifted instead.** A build-time include guarantees the *sources* agree; it guarantees
nothing about which function you call.

### What it would take

1. `SimMap.Build` calls `DescentMap.Build(seed)`, and the header line prints the same
   place/passage counts the generator prints, so the two can be diffed by eye in one
   line.
2. A test that fails when they differ — the same shape as
   `MatchDirector.VerifyCreatureCount`, which is the pattern this project already found
   works: compare the two numbers at runtime and refuse rather than report.
3. Retire or rewrite the §03/§08 half of the simulator, or mark every command that
   depends on it as not applicable to the race.

Until (1) lands, **no figure from `horrorsim` may be quoted anywhere** — not match
length, not the threat curve, not the outcome mix.

> `core/HorrorGame.Sim/SimMap.cs` and `SimCommands.cs` have uncommitted working-tree
> changes from another workstream as this is written. The divergence above was verified
> against the committed `a3e268e` sources as well as against the run.

---

## B-011 · The one red test is on the path a human takes to host a game

**Status:** 🟢 **closed 2026-08-03** by `f072827` · found 2026-08-03 · green in every
sweep since; **the residual gap this entry identified is real and is recorded below**

> **How it closed.** `f072827`, 2026-08-03 09:42:57 — 62 minutes after `a3e268e`, which is
> where this entry's evidence came from. It deleted the bare seat:
>
> ```diff
> -   NetworkServer.AddConnection(new NetworkConnectionToClient(SecondRunnerConnectionId));
> ```
>
> and replaced it with `SeatASecondRunnerWithABody()` —
> `LobbyEntryWiringTests.cs:596`, called at `:380`:
>
> ```csharp
> var conn = new NetworkConnectionToClient(SecondRunnerConnectionId) { isAuthenticated = true };
> Assert.That(NetworkServer.AddConnection(conn), Is.True, …);
> // Mirror's ReadyMessage handler, called by hand because this seat has no
> // socket to send one over. Everything it reaches — TryAddRunner,
> // NetRunner.Build, NetworkServer.AddPlayerForConnection — is the shipped path.
> manager!.OnServerReady(conn);
> ```
>
> **So the red was the fake connection**, exactly as `a3e268e` claimed and this entry
> declined to assume. The second seat now gets a real body through the shipped
> `OnServerReady → TryAddRunner → NetRunner.Build → AddPlayerForConnection` chain, the
> bodiless seat the test used to manufacture no longer exists, and `ReportStartLine` stops
> having anything to report.
>
> **It was not closed by silencing the log**, which this entry forbade in bold.
> `grep -n LogAssert` over the whole 851-line fixture returns **one** hit — line 132, inside
> a doc comment that rejects that fix on this project's own grounds: *"Silencing it with
> `LogAssert.Expect` would have left a future reader unable to tell that expectation from a
> genuine regression, which is this repo's one recurring defect wearing a test's clothes."*
>
> **And the production code did not move**, which is the part that makes this a closure
> rather than a patch. `RaceRunners.ReportStartLine` (now `RaceRunners.cs:371`, the error at
> `:396–400`; this entry cites the pre-`2c4ed98` line 298) is unchanged, still
> `Debug.LogError`, still carrying the identical Korean text. `RaceLobby.KeepBodiesAcrossTheLoad`
> (`RaceLobby.cs:1269`) is **byte-identical** from `8bf2e75` to HEAD. The lobby detector
> that would see a room full of invisible people is intact; it simply has nothing to see.
>
> **Green since, three independent times**, from the commit messages of the agents that ran
> the suite: `8bf2e75` 2026-08-05 "PlayMode 121/121"; `58b22b9` 2026-08-08 and `8db3d78`
> 2026-08-09 both "PlayMode 121 passed / 3 failed", where the three are
> [B-022](#b-022)'s voice tests and this fixture is not among them. The fixture itself has
> not been touched since `8bf2e75` on 2026-08-05, whose only change to it was the §13
> roster-landing assertions.
>
> ⚠️ **The XML is gone.** `/tmp/r7_all.xml` and `dist/test-results/playmode-results.xml`
> both no longer exist, so the 121/121 is commit-message and STATUS.md testimony rather
> than a re-readable artefact. The *fix*, by contrast, is in the tree and can be read.
>
> ### What this closure does not cover, and it is what the entry actually worried about
>
> This entry asked for "a real socket and a real body — a second `KcpClient`". **Half of
> that landed: the body is real, the socket is still synthetic.** The fixture argues the
> case at `:110–141` and `:583–594` — host mode forbids a second `NetworkClient` in one
> process, so a genuinely remote peer needs a second process, which is what `NetSocketTests`
> does at the cost of giving up host mode, the one thing this fixture cannot give up. It
> also measures why the fake socket is safe: vendored `kcp2k` `KcpServer.Send` is
> `if (connections.TryGetValue(…)) c.SendData(…)` with **no `else`**, so an unknown
> `connectionId` is dropped silently rather than throwing.
>
> That reasoning is sound and the closure stands. But **the production claim underneath
> this entry is still uncovered**: no test anywhere puts *two genuinely remote seats*
> through the descent's scene load, which is the thing `KeepBodiesAcrossTheLoad` exists
> for. It is a smaller thing than a blocker — the shipped host path is now exercised
> end-to-end and the detector is live — so it is left here as a named gap rather than
> re-opened. The next multi-process test is where it gets closed.

### The original entry, as written on 2026-08-03

`/tmp/r7_all.xml`, 2026-08-03 08:37 — `total 113 passed 112 failed 1`:

```
HorrorGame.Tests.PlayMode.Net.LobbyEntryWiringTests
  .HostingFromTheMenuReachesTheMazeWithARunnerStillAlive   Failed

Unhandled log message: '[Error] [Race] §01 출발선이 완성되지 않았다 — 2석 중 1명에게
몸이 없다. 씬 로드가 주자를 지웠다는 뜻이고, 그 사람들은 아무에게도 보이지 않는다.
RaceLobby.KeepBodiesAcrossTheLoad 를 보라.'
  at RaceRunners.ReportStartLine () (RaceRunners.cs:298)
```

### The two readings, and which one is evidenced

**a3e268e's own claim** is that the test manufactures the state it trips over: it adds a
second seat with `NetworkServer.AddConnection(new NetworkConnectionToClient(id))`
(`LobbyEntryWiringTests.cs:300`) purely to clear §11's two-runner floor, that connection
has no socket and never spawned a body, and `ReportStartLine` correctly reports one
bodiless seat of two. Reading `RaceRunners.cs:250–305`, that is consistent: the method
walks `RaceParty.SeatConnectionIds`, counts connections whose `identity` is null, and
`LogError`s if any are.

**What has not been shown** is the production claim underneath it — that two *real*
seats keep their bodies across the descent's scene load. `KeepBodiesAcrossTheLoad` exists
because they did not, once. No test covers two real connections through that load;
`NetHumanRunnerTests` covers movement, not the scene transition.

So the red is probably the test's fault and **that is not the same as knowing.** This is
the one test that walks the shipped menu path, and it is the test that caught the defect
`a3e268e` fixed — assuming it innocent is how the previous defect on this path survived.

### What it would take

Give the second seat a real socket and a real body — a second `KcpClient`, as
`NetSocketTests` already stands one up — and assert both survive the load. If it then
passes, the red was the fake connection and this closes. If it fails, the fix is in
`RaceLobby.KeepBodiesAcrossTheLoad` and the shipped 호스트 path is broken for everyone
but the host. Either way one measurement settles it.

**Do not close this by silencing the log.** `LogAssert.Expect` on that message would make
the suite green and delete the only thing that can see a lobby full of invisible people.

---

## B-010 · The middle of a radial storey had no piece, so it sealed itself

**Status:** 🟢 **CLOSED** 2026-08-03 · verified by `/tmp/r6_gen.log`

`RadialStorey` drew the 3 × 3 middle as nine ordinary corridor cells. The kit tiles a
cell from its neighbour mask, so nine cells in a square became four L corners, four T
edges and a cross — nine 2.5 m passages meeting at their own walls. The audit found it as
a sealed island containing one marker:

```
[1] MonsterSpawn
```

That line is why §06's creature reached **0 of 3** targets in every run of this map, and
— because the middle of B8 is §02's finish — why nobody could win.

### The fix that landed

`Chamber_Open_3x3` in `tools/blender/gen_mapkit.py`: a 7.5 m square with corner piers and
a mid-edge opening on each side, registered in `MapKitCatalogue`, placed by `RadialStorey`
as a room. A plus-shaped middle was tried first and measured **worse** (91.2 % complete,
18 islands, against the block's 98.1 % / 11) because each arm became a blind cell and
took a `DeadEndCap`; the reasoning is left in `RadialStorey` because the next person will
have the same idea.

The wiring needed `OpenRoom()` rather than `Room()` — §12 counts 개방 공간 as graph
nodes, so the chamber's cells have to be **in** the graph while being excluded from
corridor tiling and covered by one piece. `Room()` produces geometry and no graph, which
left the four dock cells as dead ends and made §12 refuse, correctly, to hang the inner
gate's door on one.

### The closing measurement

`/tmp/r6_gen.log`, 2026-08-03 — against 0/3 on 3 storeys when this was opened:

```
monster reach 212/212 markers reachable from a MonsterSpawn on the SAME storey,
              over 8 of 8 storeys (§06)
runner reach  storeys 8/8 · starts 36/36 reach the finish · finish REACHED
```

### What this cost, and the lesson worth keeping

Three days, most of them spent on [B-009](#b-009) and [B-009b](#b-009b) blaming the
measuring instrument. **Both failures inside the corridor kit named themselves at
generation time** once a room piece was used — `VerifyRoomWalls` reported three separate
defects at exact coordinates while this was being wired — where the same two defects,
expressed as corridor cells, showed up only as one coordinate in an audit island list
three days later. Prefer the check that fails at authoring time.

---

## B-009b · ~~The audit is still stale~~ — the chamber sealed the middle its own way

**Status:** 🟢 **CLOSED** 2026-08-03, with B-010 · **it was never a stale audit**

Kept in full because the wrong conclusion here cost more than the bug.

`NavMesh.RemoveAllNavMeshData()` before the bake moved the numbers once — 93.5 % → 98.1 %,
17 islands → 11 — so B-009 was real. Then **four** regenerations with genuinely different
geometry produced a byte-identical audit:

```
complete 6863 (98.1 %)   islands 11   monster reach 0/3
```

The instrumentation that was meant to convict the audit exonerated it. The bake reported
the geometry it consumed — 9824, 8975, 9542 vertices across runs, from 2857 meshes and
3.9 M triangles — and `NavMesh.CalculateTriangulation()`, read *after* the bake, returned
the fresh global mesh that `SamplePosition` and `CalculatePath` query.

**A vertex count that moves and a connectivity result that does not is exactly what you
get when the geometry changes and the topology of isolation does not.** Nine corridor
tiles and one chamber have different vertex counts and cut off the same markers. 6863 and
11 islands was the true answer, five times.

The lesson, which is the reason this entry survives its own closure: *a number that will
not move is not evidence the instrument is broken.* Two days were spent on the one
component that had already been cleared.

---

## B-009 · The NavMesh being audited was not the one for the geometry just built

**Status:** 🟢 **CLOSED** 2026-08-03 · verified by the generation stamp

The original evidence: three regenerations, three genuinely different sets of geometry,
one identical audit (8717 complete · 93.5 % · 17 islands · monster 0/3), byte-identical to
the pair — including a run that placed **zero** dead-end caps.

### The real cause, and the fix

The global NavMesh was not cleared before baking, so the surface being sampled carried
data from the previous bake. `MapSceneGenerator` now calls
`NavMesh.RemoveAllNavMeshData()` first, and — the part that closes this rather than
merely fixing it — the whole generation is a **transaction with a stamp**:

```
[SceneGen] gen-20260803-080103-seed20260802: Assets/Scenes/Map_FirstSketch.unity and
Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset were BOTH written by this
run — 7127 vertices, 232,304 bytes; the same stamp is on
'SceneGen_gen-20260803-080103-seed20260802' in the scene and in
…/NavMesh_Map_FirstSketch.asset.meta, so anything else claiming to be this map is one
grep from being caught.
```

Verifiable without Unity, which is the point:

```bash
grep -c SceneGen_gen-20260803-080103-seed20260802 \
  unity/HorrorGame/Assets/Scenes/Map_FirstSketch.unity                                # 1
grep -c gen-20260803-080103-seed20260802 \
  unity/HorrorGame/Assets/Scenes/Generated/NavMesh/NavMesh_Map_FirstSketch.asset.meta  # 1
```

A generation forced past a gate writes `-forced` into that stamp, so a build made that
way is identifiable from the artefact rather than from somebody's memory of the run.

**Why it mattered beyond this map:** `NavMeshAudit` is the gate that decides whether a
level ships. While this was open, every green audit in the project's history — including
the ones that closed [B-001](#b-001) — was worth less than it looked. The stamp is what
makes that class of doubt cheap to settle.

---

## B-008 · The player could not leave the entrance storey — a 계단 only the creature could use

**Status:** 🟢 **CLOSED** 2026-08-01 · superseded in scope by the pivot (there are no
계단 in the descent map — `one-way routes 14/14 투하구 usable · no 계단`)

A `CharacterController` with the player's dimensions could stand in 8 019 places, all of
them on B1, and reach 3 of 15 후보 지점. At the same moment `NavMeshAudit` reported
**1830/1830 pairs complete, 100 %, 1 island, monster reach 19/19**. Both numbers were
correct. They measure different bodies:

| | climbs | stands | is wide |
|---|---|---|---|
| NavMesh agent | `agentClimb` 0.75 m | 2.00 m | eroded region, no body |
| Player capsule | `stepOffset` 0.40 m | 1.75 m | 0.60 m capsule |

Two defects, and neither was a tall step: every 계단 was floored over 0.015 m below its
top landing (a zone floor slab poured across the cells a stairwell rises through, excluded
from the bake but keeping its `MeshCollider` — a lid to a capsule, a floor to nothing);
and no tread was deep enough for a 0.60 m capsule to stand on without being inside the
next riser (0.275 m usable against a 0.293 m forward reach).

**The important half** is that this is [B-001](#b-001) a second time and the project did
not notice, because the gate B-001 produced measures the antagonist and was read as
measuring the level. `PlayerReachAudit` now measures the player's own capsule with
`Physics` casts and never consults the NavMesh, because the premise is that the two
disagree. It is what §1.3 of [STATUS.md](STATUS.md) quotes, and it is the reason the
descent map's chutes are gated rather than assumed.

> **Do not fix a `PlayerReach` failure by raising `stepOffset`.** §12's escape geometry
> is derived from what a player *cannot* climb; a player who can step 0.65 m can climb
> crates and debris.

> **ID collision:** `MapSceneGenerator.cs:645` cites "B-008" for a `straight-corridor`
> deferral on the radial storeys. That is not this entry. See [B-014](#b-014).

---

## B-007 · §12's sight-break-spacing rejects the map that ships, and the map ships anyway

**Status:** 🟢 **closed 2026-08-10** · opened 2026-08-01 · the waiver is deleted and the
rule passes on all eight roster seeds

> **How it closed.** `RadialStorey` was re-laid so the bands jog **outward** instead of
> inward. The old 중간 band sat on d6/d7 pressed against d5, so nothing could be built
> there; on d7/d8 it leaves d4–d6 clear and **d5 becomes a lane with empty neighbours on
> both sides** — the only radius in the storey where a corridor can run without welding
> itself to a band. Every bend on the floor is then placed against one number, **12.5 m**,
> because that is the widest a 시야 차단 지점 may be on a 2.5 m grid, and 관문 are 2, 3 or
> ≥6 steps and never 4 or 5: a short 관문 welds the bend it leaves to the bend it arrives
> at, and the welded span is 15 m or 17.5 m at four or five steps. That single rule is
> what killed the 95 m group — the old 관문 was three cells joining two *multi-bend*
> clusters, and four of them chained all three bands.
>
> | | before | after |
> |---|--:|--:|
> | 시야 차단 지점 | 48 | **160** |
> | 지점 inside 15–25 m | 48 / 48 | **160 / 160** |
> | deepest continuous cover run | **95.0 m** | **12.5 m** (cap 14.4) |
> | 지점 over the cap | **16 / 48** | **0 / 160** |
>
> Identical on the shipped seed and all seven other roster seeds. Confirmed in Unity, not
> just in `dotnet`: `MapPipeline.RegenerateFromCommandLine` prints
> `[ok] sight-break-spacing — 시야 차단 지점 간격 15~25m`, and the generator's warning now
> reads **1** waived rule instead of 2. `MapSceneGenerator.KnownFailingRules` holds only
> `RuleCentrePath`.
>
> **A correction to this entry's own numbers, worth carrying forward.** The measurement
> below — *"496 corners, nearest-neighbour 2.5 m~7.5 m, mean 3.5 m, 0 inside the band"* —
> is the **raw bend** figure. The rule groups bends into 지점 first, and the 지점-level
> spacing was already 15.0 m and already 48/48 inside the band before any of this work.
> **The only thing that ever failed was the span**, and the span is what closed. An entry
> that quotes the wrong one of two available statistics sends the next person to fix
> something that was never broken.
>
> Cost, recorded rather than buried: see B-019 for the seed-variation regression this
> shares with it.

`66ce930` implemented 시야 차단 지점 간격 as `MapValidator`'s 17th rule. The rule is right
and the map has never satisfied it. For a day the generator therefore refused to write the
level the game ships; now it writes it under a named waiver
(`MapSceneGenerator.KnownFailingRules`) that carries the numbers, and prints the failure
every single time:

```
[SceneGen] §12 is failing 2 rule(s) that KnownFailingRules waives by name, so the map was
written anyway. This build has KNOWN MAP DEFECTS in it — read KnownFailingRules for what
each one measured, what §12 required, and what fixing the geometry would take.
```

The waiver was the right trade — freezing all map authoring behind one already-measured
defect cost more than it protected — and it is a debt, not a fix.

### The measurement (`/tmp/r6_gen.log` 2026-08-03, re-measured 2026-08-05)

```
[FAIL] sight-break-spacing — 시야 차단 지점 간격 15~25m (질주 60m에 3~4번의 기회)
  48 시야 차단 지점 from 496 bend(s). One 시야 차단 지점 is 95 m deep — #4 B1 하역장(9,2@L0)
  to #85 B1 하역장(15,22@L0) with nothing further than 15 m between any two of its bends …
  The cap is 14.4 m — §12's 14.4 m single-corner requirement with nothing subtracted.

시야 차단 지점 간격: 496 corners, nearest-neighbour 2.5 m~7.5 m, mean 3.5 m, 0 inside the band.
```

**95 m of continuous cover against 14.4 m allowed — 6.6× over.** The five-storey building
was 79 corners at a mean of 4.1 m; the eight-storey radial map is 496 at 3.5 m. The pivot
made this worse, because a concentric maze of 2.5 m cells is a corner every few metres by
construction.

### The cap moved on 2026-08-05, and the map failed the weaker one too

It was 4.4 m: §12's 14.4 m single-corner requirement **less** the 10 m head start its
어그로 시작 거리 table endorses. That subtraction is §04's 주자 *choosing* a range to be seen
from, and §01 replaced the choice with 「마주치면 피할 수 없다」 — a runner reaches cover
carrying nothing, so nothing comes off the 14.4.

For one round the whole cap was deleted on the strength of that argument and the rule went
green. **The map had not moved**: the same 48 지점, the same 95 m, the same 496 bends on
both sides of the diff. Stripping the deleted term makes the cap 14.4 m, which is *weaker*
than the one the map was already failing, and 95 m is still 6.6× over it. A gate the
geometry cannot meet at the most generous honest number is a map defect, which is what this
entry has always said.

Note what the toll does **not** say. `MapSceneGenerator`'s 탈출 대가 finds 0 of 720 places
charging less than one door, i.e. no chase on this map is free — and cover still runs 95 m
unbroken. The toll prices one runner's escape; it cannot see what continuous cover costs a
race of twenty, so it is not a substitute measurement for this one.

### It is the same defect as the 주자 테스트 grade

10/10 TooEasy, and **720 of 720 places escapable** against §12's 50–70 % band. Every
sampled runner releases with *"3 s of unbroken cover"*. The rule and the grade are one
defect measured twice ([STATUS.md §2.1](STATUS.md), [F-007](BALANCE-FINDINGS.md#f-007)).

> **"…and it is the reason the creature is decoration" — struck 2026-08-08.** That clause
> was written when seven of eight storeys had no creature on them at all, which
> [F-013](BALANCE-FINDINGS.md#f-013) showed was the actual cause and which is now fixed:
> the scene carries eight `MonsterSpawn` objects, a running match logs
> `§06 창조물 8마리`, and `MatchDirector.VerifyCreatureCount` refuses to start if those two
> ever disagree. The creature is on every floor. What B-007 still costs is the *price* of
> meeting one — F-013 §3 measures it at a 7.2 s median against a 3.4~20 s band — so this
> blocker is about how cheap a chase is, not about whether there is anything to flee.

### What it would take

**Not** relaxing the cap — and not deleting it either; that was tried and cost three gates
for nothing. The lever is the geometry: bands of the ring that run straight for 15–25 m
between turns, so a sprint has three or four discrete chances to break line of sight rather
than continuous cover. `RadialStorey` generates the bands, so this is a change to one
generator with a number that says the moment it succeeds — re-run the generator and read
`0 inside the band`.

Closing this closes B-007 and moves F-007's grade in one change. It is the single most
valuable piece of work available on this project. Do it together with
[B-019](#b-019): that one wants a longer rim-to-middle route and this one wants longer
straight legs, and they are the same edit to the same generator.

---

## B-006 · The core solution did not build — the simulator never compiled a file it depends on

**Status:** 🟢 **CLOSED** 2026-08-01 · re-verified 2026-08-12:
`dotnet test core/HorrorGame.sln -c Release` → **357 passed, 0 failed**

> **Note what the solution now contains, because this entry is about a project that is no
> longer in it.** `HorrorGame.Sim` was deleted at `e8c67ae` ([B-012](#b-012)), so
> `core/HorrorGame.sln` holds `HorrorGame.Core` and `HorrorGame.Core.Tests` and nothing
> else, and the 512-test figure [B-013](#b-013) quotes is now 357. The **inversion** this
> entry describes — Unity-only files named, the engine-free half globbed — survived the
> deletion in `HorrorGame.Core.Tests.csproj` and is still the arrangement that makes a
> stray `using UnityEngine` break the build loudly. That judgement is unchanged and was
> vindicated twice more since: once by B-013's `ChamberDockProbe`, once by `9f0f447`'s map
> re-lay, which broke three §12 rules and had them fail in `dotnet` rather than in a scene.

`MapQualityReport` gained a `RunnerCensus`; `HorrorGame.Sim.csproj` listed the engine-free
map sources **by name**, so the new file was never compiled and the solution failed on
2 × `CS0246`. Unity compiled clean and 560 tests passed throughout, because nothing in the
Unity project or the test project references the simulator.

The list was later **inverted** — the Unity-only files are now named and the engine-free
half is globbed — so that the same mistake breaks the build loudly on the first
`using UnityEngine` instead of measuring the wrong map quietly. That inversion is what
produced [B-013](#b-013)'s failure mode two days later, in the opposite direction, and the
project's judgement stands: a build error tells you, a stale measurement does not.

---

## B-005 · Regenerating the map unregistered the scene 시작 loads

**Status:** 🟢 **CLOSED** 2026-08-01 · verified by
`UiFlowTests.Menu_ComesUp_AndStartReachesTheMatchScene` (still green in `/tmp/r7_all.xml`)

`MapSceneGenerator.RegisterScenes()` rewrote Build Settings wholesale and named only the
bootstrap and the raw map, so regenerating deleted `Map_FirstSketch_Solo.unity` — the
assembled scene 시작 loads — from the build list. `SceneManager.LoadSceneAsync` returns
`null` rather than throwing for an unlisted scene, so the shell bounced silently back to
the menu: **the main menu's start button did nothing, with no error anywhere.**

Fixed by naming the scene once in `SceneGenPaths.MatchScene` and having both writers use
it. Worth remembering as the archetype: only a PlayMode test can see a button that does
nothing.

---

## B-004 · The networking library is a stranger's repack, not Mirror

**Status:** 🔴 **open — blocks release** · supply chain · **more urgent since the pivot** ·
re-confirmed 2026-08-12, nothing has changed

> **Re-checked 2026-08-12.** `unity/HorrorGame/Packages/manifest.json:4` still reads
> `"com.mirrornetworking.mirror": "96.6.4"`, still resolved through the OpenUPM scoped
> registry declared at `:47–52`. No vendored `.unitypackage` under `Assets/`.
>
> Second-order confirmation, from a place that is easy to stop noticing: **both** build
> reports of 2026-08-10 list three tolerated errors, all the same one —
> `Asset Packages/com.mirrornetworking.mirror/Mirror/Assets has no meta file, but it's in
> an immutable folder` — with the note that this is "a known defect in the OpenUPM package
> … the package repacks Mirror's git submodule". Every shipped build carries a written
> acknowledgement that the networking layer is a repack, and it reads as routine because it
> is printed every time. That is the same mechanism as [B-014](#b-014), on the supply chain
> instead of the map.

`Packages/manifest.json` pulls `com.mirrornetworking.mirror` 96.6.4 from OpenUPM. Its own
`package.json`, read from the package cache:

```
name               com.mirrornetworking.mirror
author             Chaoyang <960208781@qq.com>
                   https://github.com/960208781/UnityMirror.git
documentationUrl   https://github.com/MirrorNetworking/Mirror/blob/master/README.md
```

The package **id claims Mirror Networking** and the documentation URL points at the
official repository, so everything visible from `manifest.json` reads as official. The
code being compiled comes from an individual's fork.

### Why this is worse than it was

When this was opened, Mirror was compiled in and not exercised — the playable build was
single-player, so it blocked the four-player milestone rather than the next playtest. As
of `a3e268e` it carries every byte of a twenty-player race: `PlayerRigNetView` replicates
position, camera rotation, torch, carry state and stamina, and the shipped
`HorrorGameNetworkManager` accepts real remote clients
([STATUS.md §1.6](STATUS.md)). **It is now load-bearing.**

### The official route, verified against upstream

`github.com/MirrorNetworking/Mirror` has **no `package.json`** at the root or under
`Assets/Mirror`, so it cannot be installed as a UPM git dependency — every
UPM-installable "Mirror" is somebody's repack. Official distribution is a
`.unitypackage` from GitHub releases (v96.11.1, five versions ahead of the repack) or the
Asset Store.

### What it would take

Vendor the official `.unitypackage` into `Assets/`, delete the OpenUPM dependency and its
scoped registry. Costs a larger repository and manual updates; buys a dependency whose
origin can be pointed at. Do it as its own change with the full suite after: it swaps the
assembly the whole of `Assets/Scripts/Net/` compiles against, and FizzySteamworks sits on
top of it. `com.mirror.steamworks.net` and `com.rlabrecque.steamworks.net` both come from
their own projects' repositories and are fine; Mirror is the only one whose publisher is
not the project.

---

## B-003 · Two 개방 공간 were silently dropped from every map generation

**Status:** 🟢 **CLOSED by the pivot** 2026-08-03 · verified: `grep -c HallOpen20x20
/tmp/r6_gen.log` → **0**

`MapSketch` placed two `HallOpen20x20` rooms under a corridor on the storey above and then
refused to build them, at `LogError`, on every generation — 6.3 m of room on a 3.75 m
storey, leaving places above with under 2 m of headroom.

The descent map is built by `DescentMap`/`RadialStorey` and places no `HallOpen20x20` at
all, so the errors are gone with the piece. **The complaint underneath it was not about
the room** and is still live: a generator that prints `LogError` on the happy path means
nobody can use "the log is clean" as a gate. That is now [B-014](#b-014)'s subject, one
level up — the *checklist* prints `FAIL` on the happy path.

---

## B-002 · The EditMode solo-match test fails on a broken Mirror package install

**Status:** 🟢 **closed by deletion 2026-08-03** (`e8c67ae`) · never a code regression ·
**"dormant" was the wrong word and is retired**

> **Why not dormant.** Dormant claimed the defect was asleep and could wake. It cannot:
> **`Assets/Scripts/Gameplay/Match/Editor/SoloMatchLoopTests.cs` was deleted at
> `e8c67ae`**, 2026-08-03 19:45:12, with the rest of the co-op game. There is no
> `Solo_match_runs_the_whole_round_trip` anywhere in the tree. The entry's own second
> paragraph anticipated this — *"the test itself drives the §01 co-operative loop the pivot
> deleted, so it may not be a test worth keeping"* — and that is what was decided, four
> hours after the entry was written.
>
> Both of the questions this entry said "are answered by one EditMode run" now have
> answers, and neither needed the run: the test does not reproduce **because it does not
> exist**, and it was not worth keeping. The run happened anyway on 2026-08-08 — 95/95,
> [B-016](#b-016) — and `SoloMatchLoopTests` is not among the 95.
>
> **The underlying package fault is [B-004](#b-004) and is emphatically not closed** — it
> is still printed three times in every build report of 2026-08-10. What is closed is this
> entry's subject: a specific test failing on it. If the message ever surfaces in a
> *different* test, the guidance below still stands and is the reason this entry is kept
> rather than dropped: `LogAssert.Expect` on that one message in that one test, **not**
> widening the harness's log tolerance in general.

`SoloMatchLoopTests.Solo_match_runs_the_whole_round_trip` failed on an unhandled log
message — `Asset Packages/com.mirrornetworking.mirror/Mirror/Assets has no meta file, but
it's in an immutable folder` — not on an assertion. It stopped reproducing on 2026-08-01
when the package cache was rewritten; nothing was fixed.

**As of 2026-08-03 there is no way to say whether it reproduces**, because EditMode has
not been run since the pivot ([B-016](#b-016)) — and the test itself drives the §01
co-operative loop the pivot deleted, so it may not be a test worth keeping. Both questions
are answered by one EditMode run.

The underlying package fault is [B-004](#b-004) and is expected in every build; the build
pipeline names it in `BuildPipelineKnownDefects` and does not fail on it. If it returns in
a test, the fix is `LogAssert.Expect` on that one message in that one test — **not**
widening the harness's log tolerance in general.

---

## B-001 · The creature could not reach the player

**Status:** 🟢 **CLOSED** 2026-07-31 · re-verified in a different shape 2026-08-03

Closed originally by `MonsterChaseTests.MonsterClosesDistanceAndReachesAPlayerAcrossTheMap`
— 133.9 m of route across two storey boundaries at 4.83 m/s, `worst 1 s rise 0.0 m`,
against a monster that had been stalled 95 m away for 220 consecutive seconds. Both halves
of the fix landed: the kit's stairs became walkable geometry with every `NavMeshLink`
deleted, and `NavMeshWorldProbe.TryGetNextPathPoint` stopped deadlocking on a duplicated
path corner.

**The question changed with the game.** A creature cannot use a 투하구, so it can no
longer cross the building, and the test now asks whether it can reach a runner on its own
storey (`/tmp/r6_all.log`, 2026-08-03):

```
[ChaseTest] §14 Q1 — can the creature reach a runner on its own storey at all?
  route 71.0 m of NavMesh path · reached 14.54 s · closing speed 4.81 m/s against §06's 4.8
```

The two control corridors still reproduce §06's central claim —
「괴물이 달리기보다 0.3만 빠른 것이 핵심이다」 — to 1 %: `monster speed 4.80 m/s`,
`gap opened at 0.80 m/s`, single corner `caught 12.54 s`, two 10 m legs
`released 5.50 s at 12.0 m`.

**What it does not say.** That the creature *can* reach a runner is not that it *does*.
[B-007](#b-007) is the measurement that says it never has to be dealt with, and it is the
one that matters now.
