#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Ghost;
using HorrorGame.Core.Math;
using HorrorGame.Gameplay.Player;
using HorrorGame.UI;
using HorrorGame.UI.Screens;
using HorrorGame.UI.Settings;
using HorrorGame.UI.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Tests.EditMode.UI
{
    /// <summary>
    /// Assertions about what the interface is forbidden to do.
    /// <para>
    /// Most of this file is structural rather than behavioural, and deliberately so.
    /// The design's load-bearing UI rules are <em>absences</em> — no voice for the dead
    /// (§09), no brightness slider that undoes the dark (§03) — and an absence cannot be
    /// verified by exercising a feature. So these tests assert over the shape of the
    /// types: a ghost's microphone would have to introduce a member with a name. That is
    /// caught here, at build time, rather than in a playtest six months later.
    /// </para>
    /// <para>
    /// <b>What left this file on 2026-08-03 — DESCENT-PIVOT §7 step 7.</b> Thirty-nine of
    /// the fifty-nine tests here were about screens that no longer exist. Their subjects
    /// were deleted in the same round, not merely retired, so these are compile failures
    /// and not stale assertions:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>3 · §07's clock</b> — <c>Clock_IsUnreadableUnderground_WithoutAPocketWatch</c>,
    ///     <c>Clock_BecomesReadableUnderground_OnlyBecauseOfThePurchase</c>,
    ///     <c>Clock_CarriesAPhaseAndNeverAStopwatch</c>. <c>UI/Readouts/ClockReadout.cs</c> is
    ///     deleted. Two of the three were about the 회중시계 §08 sold and the 지상 you walked
    ///     back up to in order to read the hour; a race has neither.
    ///     COVERAGE LOST AND STILL WANTED: the third one's rule — <em>the HUD may carry a
    ///     phase and never an elapsed-seconds figure</em> — outlives the readout that carried
    ///     it. §07 still speeds the creature up on a schedule, and a runner who can read a
    ///     stopwatch off the screen can compute exactly how fast it is right now. When the
    ///     race grows a HUD, that assertion belongs on it.
    ///   </description></item>
    ///   <item><description>
    ///     <b>20 · §08's shop</b> — the four <c>Shop_*</c> board tests and the sixteen
    ///     <c>ShopScreen_*</c> drawn-screen tests. <c>Core/Economy/</c>, <c>ShopBoard</c>,
    ///     <c>ShopScreen</c> and <c>IShopRequests</c> are all deleted. There is no currency,
    ///     no 차량 to stand at and no 왕복 to spend between. Nothing needs this coverage.
    ///   </description></item>
    ///   <item><description>
    ///     <b>4 · §03's clue UI</b> — <c>ClueUi_ContainsNoCollectionOfAnyKind</c>,
    ///     <c>ClueUi_StoresNoStructuredClueContent</c>, <c>ClueRead_LosesItsProgress_WhenTheLightGoes</c>,
    ///     <c>ClueUi_ForgetsTheMark_AsSoonAsTheReadEnds</c>. <c>Core/Clues/</c> and both
    ///     reading views are deleted. The destination is known from the first frame of a
    ///     race, so there is nothing to deduce and nothing to forget.
    ///   </description></item>
    ///   <item><description>
    ///     <b>4 · §11's role lobby</b> — the <c>Lobby_*</c> tests over <c>LobbyBoard</c>,
    ///     deleted. §04 has no 직업, so there is no absence to price and no 대체 수단 to name.
    ///   </description></item>
    ///   <item><description>
    ///     <b>2 · §02's four endings</b> — <c>Endings_DifferInWhatSurvives_NotJustInAWord</c>
    ///     and <c>EndScreen_NeverRecapsAClue</c>. <c>EndScreenReadout</c> and <c>EndScreen</c>
    ///     are deleted; 완전 승리 / 부분 승리 / 생존 / 패배 were four ways a TEAM ended a
    ///     round-trip, and §02 now ends one runner's race four other ways — 승리 · 완주 ·
    ///     탈락 · 시간 초과.
    ///     COVERAGE LOST AND STILL WANTED, checked rather than assumed: those four ARE drawn
    ///     — <c>UI/RaceHud.cs</c> writes 탈락, "N위" and 완주 off <c>RaceState</c> — and the
    ///     drawing has no EditMode test at all. <c>RaceDirectorTests</c> pins the readout
    ///     INTERFACE (<c>TheReadoutIsAWindow_AndCarriesEverythingRaceHudNeeds</c>) and stops
    ///     at the seam, which is the exact shape the shop half of this file existed to catch:
    ///     a board that knew and a screen that drew nothing. The 탈락 / 완주 / 순위 panel is
    ///     where that test belongs now.
    ///   </description></item>
    ///   <item><description>
    ///     <b>6 · the HUD</b> — <c>Hud_IsBlank_ForAnUnburdenedPlayerUnderground</c>,
    ///     <c>Hud_ShowsTheLoadPenalty_AtTheMomentItIsPaid</c>,
    ///     <c>Hud_WarnsBeforeTheNextPieceCosts_NotAfter</c>,
    ///     <c>Hud_GivesTheSprintBarToTheRunnerAlone</c>,
    ///     <c>Hud_SaysWhatTheObjectiveTakesAway</c>,
    ///     <c>Hud_ShowsTheBattery_WhileItIsBeingSpent_AndWhenItIsGone</c>.
    ///     <c>HudReadout</c>, <c>LoadReadout</c>, <c>SprintReadout</c>, <c>LightReadout</c>,
    ///     <c>HudScreen</c> and <c>BatteryState</c> are all deleted. Four of the six were
    ///     about weight, the 목표물 and the battery. The fifth was <em>false</em> rather than
    ///     stale — §04 now gives 질주 to all twenty, so a sprint bar shown to one body and
    ///     hidden from the rest is the opposite of the rule. The sixth, "blank when nothing
    ///     is being paid", is the one worth rebuilding on whatever HUD the race grows.
    ///   </description></item>
    /// </list>
    /// <para>
    /// What is left is the two things the race still draws: §09's ghost overlay and the
    /// settings screen.
    /// </para>
    /// </summary>
    public sealed class UiTests
    {
        private const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // ====================================================================
        // §09 — 말하기: 불가능.
        // ====================================================================

        [Test]
        public void GhostUi_HasNoVoiceWidgetToDisable()
        {
            // The rattle words ride along for the same reason they do in
            // GhostSessionTests: this scan is what catches 신호 being re-added under
            // its old name, and §11's 탈락자 rule forbids it outright.
            var banned = new[]
            {
                "voice", "mic", "speak", "talk", "mute", "chat", "radio", "push",
                "rattle", "shake", "signal",
            };

            foreach (var type in new[] { typeof(GhostOverlay) })
            {
                foreach (var name in NamesOf(type))
                {
                    var lower = name.ToLowerInvariant();
                    foreach (var word in banned)
                    {
                        Assert.That(lower.Contains(word), Is.False,
                            "§09: '" + type.Name + "." + name + "' is a voice affordance. A muted icon or a greyed-out "
                            + "push-to-talk describes a control that exists in some other state and sends the player looking "
                            + "for it. The silence is structural — Core's GhostState has no method that takes a message — and "
                            + "§13 settles it for anyone who would rather mute a live channel: cutting at the receiver is not cutting.");
                    }
                }
            }
        }

        // ====================================================================
        // DELETED with §09's 신호. Two tests stood here:
        //   Ghost_ShowsTheWait_BecauseTheWaitIsTheExperience
        //   Ghost_TellsTheTwoFailuresApart
        // They pinned the cooldown bar and the two failure strings — 「아직 흔들 수
        // 없다」 and 「너무 멀다」 — on GhostReadout, which was the largest thing on
        // the ghost's overlay because "the wait WAS the experience".
        //
        // §11's 탈락자 deleted the verb, so there is no wait to show and no failure to
        // word. GhostReadout itself is deleted with them: once the rattle went it
        // carried a single field, IsGhost, which GhostOverlay reads from a null check.
        // What the overlay draws instead is the vantage the spectator is watching from,
        // asserted in GhostSessionTests where the camera actually moves.
        // ====================================================================

        // ====================================================================
        // Settings — §05's clamp, §03's dark, and a file that has to survive a restart.
        // ====================================================================

        [SetUp]
        public void RedirectSettingsToAScratchFolder()
        {
            // Never the real persistentDataPath: a test that wrote there would overwrite
            // the settings of whoever ran it and would then pass or fail depending on
            // what they happened to have configured.
            _settingsScratch = Path.Combine(Path.GetTempPath(), "HorrorGameSettingsTests", Guid.NewGuid().ToString("N"));
            SettingsStore.OverrideDirectory(_settingsScratch);
        }

        [TearDown]
        public void RestoreTheRealSettingsFolder()
        {
            SettingsStore.OverrideDirectory(null);

            if (!string.IsNullOrEmpty(_settingsScratch) && Directory.Exists(_settingsScratch))
            {
                Directory.Delete(_settingsScratch, true);
            }
        }

        private string _settingsScratch = string.Empty;

        [Test]
        public void Settings_StartAtTheDesignsOwnNumbers()
        {
            var settings = new GameSettings();

            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovDefault),
                "§05: '기본 80.' A settings screen that shipped a different default would be quietly re-tuning "
                + "the peek every new player learns the game on.");

            Assert.That(SettingsLimits.BrightnessExposure(settings.Brightness01), Is.EqualTo(0f).Within(0.0001f),
                "The default has to be the picture ART.md graded — every luminance number in that document was "
                + "measured with no player offset applied, so a non-zero default would make them describe a frame nobody sees.");

            Assert.That(settings.VolumeMaster, Is.EqualTo(1f));
            Assert.That(settings.VolumeSfx, Is.EqualTo(1f));
            Assert.That(settings.InvertLookY, Is.False);

            Assert.That(settings.ViewMotion, Is.EqualTo(ViewMotionTuning.ScaleDefault),
                "A new player gets the whole camera. §14's questions 1 and 2 are about how the chase feels, "
                + "and shipping them a still camera by default would answer them against a game nobody made.");
        }

        [Test]
        public void ViewMotion_IsTheOneRowWithNoBalanceConsequence()
        {
            var settings = new GameSettings();

            // Every other gameplay-adjacent row on this screen is clamped to a window a
            // player cannot escape, because escaping it is an advantage: §05's field of
            // view, §03's brightness. This one goes to zero and back, because nothing in
            // ViewMotionTuning reaches a rule — the offsets land on the camera transform,
            // §05's speed table is untouched and the beam still points where PlayerLook
            // says. A player made ill by head bob has to be able to switch it off without
            // switching off the game.
            settings.ViewMotion = 0f;
            Assert.That(settings.ViewMotion, Is.EqualTo(0f),
                "zero has to be reachable, or the accessibility answer is 'stop playing'");

            settings.ViewMotion = 1f;
            Assert.That(settings.ViewMotion, Is.EqualTo(1f));

            settings.ViewMotion = 4f;
            Assert.That(settings.ViewMotion, Is.EqualTo(1f), "and it cannot be pushed past the authored amount");

            settings.ViewMotion = -3f;
            Assert.That(settings.ViewMotion, Is.EqualTo(0f));

            settings.ViewMotion = float.NaN;
            Assert.That(settings.ViewMotion, Is.EqualTo(ViewMotionTuning.ScaleDefault),
                "a NaN out of a corrupt file must not be able to produce a camera that renders nowhere");
        }

        [Test]
        public void Settings_ClampFieldOfView_ToSection05sWindow()
        {
            var settings = new GameSettings();

            settings.FieldOfViewDegrees = 130f;
            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovMax),
                "§05: '95+ 곁눈질이 너무 쉬워 딜레마가 약화된다.' The 45° peek is the skill the whole chase is built on; "
                + "a player who can set 130 has bought it at a discount and §10's central trade stops being a trade.");

            settings.FieldOfViewDegrees = 10f;
            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovMin),
                "§05 caps the bottom too — at 60~70 '곁눈질이 거의 안 됨', and a player cannot opt out of a technique "
                + "the map's escape arithmetic assumes they have.");

            settings.FieldOfViewDegrees = float.NaN;
            Assert.That(settings.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovDefault),
                "A NaN field of view renders nothing at all. A corrupt file must not be able to produce a black "
                + "window with no way out of it.");
        }

        [Test]
        public void Brightness_CannotUnlockWhatSection03Locked()
        {
            // ONE ASSERTION WAS REMOVED HERE on 2026-08-03, and it is worth saying which so
            // nobody reads this as a weakened test. It was
            //     Assert.That(SettingsLimits.BrightnessGainSpan,
            //                 Is.EqualTo(GameConstants.ClueMinReadableLightQuality));
            // — the slider's ±20 % span was DERIVED from the light a 단서 needed to be read
            // by, which was the only threshold §03 put on light. The clue chain is deleted
            // (DESCENT-PIVOT §7 step 7) and SettingsLimits now states 0.20 f outright, so the
            // assertion would be comparing two constants that happen to match. The three
            // below are the ones that were ever about the player: the slider may not undo
            // the dark, and may not hide the creature in it.

            var brightest = SettingsLimits.BrightnessExposure(1f);
            var darkest = SettingsLimits.BrightnessExposure(0f);

            Assert.That(Mathf.Pow(2f, brightest), Is.LessThan(2f),
                "§03: 안쪽으로 갈수록 보이지 않는다. A slider that could double the light in the room would let a "
                + "runner read the 안쪽 고리 without a torch, and the darkness that makes the last two rings "
                + "cost something would be a graphics option.");

            Assert.That(Mathf.Pow(2f, darkest), Is.GreaterThan(0.5f),
                "The floor matters for the same reason the ceiling does: a player who halves the light has hidden the "
                + "monster ART.md §3.8 spent a shader budget making visible at 15 m.");

            Assert.That(brightest, Is.GreaterThan(0f));
            Assert.That(darkest, Is.LessThan(0f));
        }

        [Test]
        public void Settings_SurviveARestart()
        {
            var written = new GameSettings();
            written.FieldOfViewDegrees = 74f;
            written.MouseSensitivity = 2.5f;
            written.InvertLookY = true;
            written.ViewMotion = 0.35f;
            written.VolumeMaster = 0.4f;
            written.VolumeSfx = 0.9f;
            written.VolumeAmbience = 0.1f;
            written.VolumeVoice = 0.75f;
            written.Brightness01 = 0.2f;
            written.ResolutionWidth = 2560;
            written.ResolutionHeight = 1440;
            written.RefreshRateHz = 144;
            written.FullScreenMode = FullScreenMode.Windowed;
            written.VSyncCount = 0;
            written.QualityLevel = 2;
            written.HeadphoneNoticeSeen = true;
            written.BindingOverridesJson = "{\"bindings\":[{\"action\":\"Sprint\",\"path\":\"<Keyboard>/leftCtrl\"}]}";

            Assert.That(SettingsStore.Save(written), Is.True, "The settings file could not be written to " + SettingsStore.Path);
            Assert.That(SettingsStore.Exists(), Is.True);

            // A fresh read of the bytes on disk — the same thing the next launch does.
            var read = SettingsStore.Load();

            Assert.That(read.FieldOfViewDegrees, Is.EqualTo(74f).Within(0.001f));
            Assert.That(read.MouseSensitivity, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(read.InvertLookY, Is.True);
            Assert.That(read.ViewMotion, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(read.VolumeMaster, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(read.VolumeSfx, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(read.VolumeAmbience, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(read.VolumeVoice, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(read.Brightness01, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(read.ResolutionWidth, Is.EqualTo(2560));
            Assert.That(read.ResolutionHeight, Is.EqualTo(1440));
            Assert.That(read.RefreshRateHz, Is.EqualTo(144));
            Assert.That(read.FullScreenMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(read.VSyncCount, Is.EqualTo(0));
            Assert.That(read.QualityLevel, Is.EqualTo(2));
            Assert.That(read.HeadphoneNoticeSeen, Is.True);

            Assert.That(read.BindingOverridesJson, Is.EqualTo(written.BindingOverridesJson),
                "The key rebinds are the one setting a player cannot re-derive by looking at the game. Losing them "
                + "between sessions is the difference between a settings screen and a settings screen that works.");

            Assert.That(read.Matches(written), Is.True,
                "Every field has to survive the round trip, not merely the ones this test happens to list.");
        }

        [Test]
        public void Settings_ClampAFileNobodyHereWrote()
        {
            // A hand-edited file, a file from a future build, a file from a mod. All the
            // same thing to the loader: bytes it did not produce.
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(SettingsStore.Path,
                "{\"_schema\":1,\"_fieldOfViewDegrees\":400.0,\"_mouseSensitivity\":9999.0,"
                + "\"_volumeMaster\":12.0,\"_brightness01\":8.0,\"_vSyncCount\":97,\"_fullScreenMode\":41}");

            var read = SettingsStore.Load();

            Assert.That(read.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovMax),
                "§05's window is enforced on arrival, not on the slider. A player who edits the file to 400 has found "
                + "the cheat the clamp exists to prevent, and the clamp has to be somewhere they cannot reach.");
            Assert.That(read.MouseSensitivity, Is.EqualTo(SettingsLimits.MouseSensitivityMax));
            Assert.That(read.VolumeMaster, Is.EqualTo(1f));
            Assert.That(read.Brightness01, Is.EqualTo(1f), "0..1, and 1 is §03's own +20 % — not eight stops.");
            Assert.That(read.VSyncCount, Is.InRange(0, 2), "Unity accepts 0, 1 and 2. Anything else throws at apply time.");
            Assert.That(
                read.FullScreenMode == FullScreenMode.ExclusiveFullScreen
                || read.FullScreenMode == FullScreenMode.FullScreenWindow
                || read.FullScreenMode == FullScreenMode.MaximizedWindow
                || read.FullScreenMode == FullScreenMode.Windowed,
                Is.True);
        }

        [Test]
        public void Settings_RecoverFromAFileThatIsNotSettingsAtAll()
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(SettingsStore.Path, "this is not json {{{");

            var read = SettingsStore.Load();

            Assert.That(read.FieldOfViewDegrees, Is.EqualTo(GameConstants.FovDefault),
                "A settings file is the one piece of state that survives a crash, so it is also the one that can poison "
                + "every subsequent launch. Defaults are the only safe answer.");
            Assert.That(File.Exists(SettingsStore.Path + ".broken"), Is.True,
                "The bytes are kept rather than deleted: the player is losing their bindings either way, and the file "
                + "is occasionally the whole bug report.");
        }

        [Test]
        public void Settings_NaNInTheFileCannotBlackTheScreen()
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(SettingsStore.Path, "{\"_schema\":1,\"_fieldOfViewDegrees\":NaN,\"_brightness01\":NaN,\"_volumeMaster\":NaN}");

            var read = SettingsStore.Load();

            Assert.That(float.IsNaN(read.FieldOfViewDegrees), Is.False,
                "Mathf.Clamp passes NaN straight through, so a clamp alone is not a guard. A NaN field of view is a "
                + "camera that renders nothing and a player who cannot get back to the menu to fix it.");
            Assert.That(float.IsNaN(read.Brightness01), Is.False);
            Assert.That(float.IsNaN(read.VolumeMaster), Is.False);
        }

        [Test]
        public void Settings_GiveEveryAudioBusASlider()
        {
            var settings = new GameSettings();
            settings.VolumeMaster = 0.5f;
            settings.VolumeSfx = 0.25f;
            settings.VolumeAmbience = 0.75f;
            settings.VolumeVoice = 1f;

            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
            {
                Assert.That(settings.VolumeFor(bus), Is.InRange(0f, 1f),
                    "Every bus GameAudio knows about has to resolve to something, or one family of sound is left at "
                    + "whatever the scene happened to author.");
            }

            Assert.That(settings.VolumeFor(AudioBus.Master), Is.EqualTo(0.5f));
            Assert.That(settings.VolumeFor(AudioBus.Ambience), Is.EqualTo(0.75f));
            Assert.That(settings.VolumeFor(AudioBus.Voice), Is.EqualTo(1f));

            Assert.That(settings.VolumeFor(AudioBus.Footsteps), Is.EqualTo(0.25f),
                "§04 gives the 청음사 nothing but sound and §12 makes the five floor surfaces their whole alphabet, so "
                + "the effects slider is the one control on this screen that can switch a role off. It follows the "
                + "slider the player moved — the screen warns rather than pretending the connection is not there.");
            Assert.That(settings.VolumeFor(AudioBus.Monster), Is.EqualTo(0.25f));
        }

        [Test]
        public void Settings_CloneIsIndependent_SoAScreenCanBeAbandoned()
        {
            var original = new GameSettings();
            original.FieldOfViewDegrees = 88f;

            var copy = original.Clone();
            copy.FieldOfViewDegrees = 72f;

            Assert.That(original.FieldOfViewDegrees, Is.EqualTo(88f));
            Assert.That(copy.FieldOfViewDegrees, Is.EqualTo(72f));
            Assert.That(original.Matches(copy), Is.False);
        }

        [Test]
        public void Bars_ActuallyShowTheirFraction()
        {
            // Regression. Every meter in this game was an Image with type Filled and no
            // sprite, and Image.OnPopulateMesh falls through to Graphic's when the sprite
            // is null — so fillAmount was ignored and every bar drew full at every value.
            // §06's twelve seconds of 질주 and §12-B's 1.1 s door hold both looked plausible,
            // because a full bar is what most meters show most of the time. (The battery and
            // the clue-read bars this note also used to name went with §08 and §03 on
            // 2026-08-03; the two that are left are the two the race actually draws.)
            var host = new UnityEngine.GameObject("BarTest", typeof(RectTransform));
            try
            {
                var bar = UiFactory.CreateBar("Bar", host.transform, 200f, 4f);
                var fill = bar.Root.Find("Fill") as RectTransform;
                Assert.That(fill, Is.Not.Null);

                bar.SetFill(0.25f);
                Assert.That(fill!.anchorMax.x, Is.EqualTo(0.25f).Within(0.0001f),
                    "A quarter-full sprint bar has to draw a quarter of a bar. §04 makes the twelve seconds the "
                    + "race's central decision — 지금 쓸까, 관문에서 쓸까 — and a meter that cannot report "
                    + "'nearly out' takes the decision away.");

                bar.SetFill(0f);
                Assert.That(fill.anchorMax.x, Is.EqualTo(0f).Within(0.0001f));

                bar.SetFill(1f);
                Assert.That(fill.anchorMax.x, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(fill.offsetMax, Is.EqualTo(UnityEngine.Vector2.zero),
                    "Moving an anchor does not move the offsets with it, so a bar that shrank and grew again would "
                    + "otherwise draw a few pixels past its own end.");

                bar.SetFill(2f);
                Assert.That(fill.anchorMax.x, Is.EqualTo(1f).Within(0.0001f), "Clamped rather than rejected — a meter fed a bad number should look wrong, not throw mid-chase.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Pause_ActuallyStopsTheClock()
        {
            var scaleBefore = Time.timeScale;
            try
            {
                Assert.That(MatchPause.IsPaused, Is.False);

                MatchPause.Pause();

                Assert.That(Time.timeScale, Is.EqualTo(0f),
                    "§07's clock is the game's currency and MatchDirector steps the whole match — clock, creature, "
                    + "doors, chutes — from FixedUpdate. Unity does not run FixedUpdate at zero scale, so this single "
                    + "switch is what makes the pause menu's promise true. A paused clock that keeps running charges a "
                    + "player for the time they spent reading their own key bindings.");
                Assert.That(AudioListener.pause, Is.True,
                    "The mix runs on unscaled time, so stopping the clock alone would leave the building audible behind a "
                    + "stopped game — and §12 makes the ear the way every runner locates both the creature and the "
                    + "other nineteen.");
                Assert.That(MatchPause.IsPaused, Is.True);

                MatchPause.Resume();

                Assert.That(Time.timeScale, Is.EqualTo(scaleBefore),
                    "Resuming restores the scale it found rather than assuming 1, because §07's night must continue from "
                    + "where it stopped and not from a value this class invented.");
                Assert.That(AudioListener.pause, Is.False);
                Assert.That(MatchPause.IsPaused, Is.False);
            }
            finally
            {
                MatchPause.Clear();
                Time.timeScale = scaleBefore;
            }
        }

        /// <summary>
        /// The settings screen draws one row per <c>InputBindings.Rebindable()</c> entry,
        /// so a key that is missing here is a key a player cannot move.
        /// <para>
        /// §05's table stops at 마우스 · WASD · Shift · F, and 웅크리기 and 뛰기 were added
        /// on top of it. Both are labelled in §05's own language rather than falling
        /// through to the raw action name, because a row reading "Crouch" in a Korean
        /// screen is the kind of thing that ships.
        /// </para>
        /// </summary>
        [Test]
        public void The_settings_key_list_carries_the_two_controls_section05_does_not_list()
        {
            var entries = InputBindings.Rebindable();
            var labels = new System.Text.StringBuilder();
            var crouch = default(InputBindings.Entry);
            var jump = default(InputBindings.Entry);

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                labels.Append(entry.Label).Append(" = ").Append(entry.KeyText).Append(" · ");

                if (string.Equals(entry.Action.name, "Crouch", StringComparison.Ordinal))
                {
                    crouch = entry;
                }
                else if (string.Equals(entry.Action.name, "Jump", StringComparison.Ordinal))
                {
                    jump = entry;
                }
            }

            Assert.That(crouch.Action, Is.Not.Null,
                "웅크리기 has no rebindable row, so a player who wants it on a key they can hold cannot "
                + "move it. Rows were: " + labels);
            Assert.That(jump.Action, Is.Not.Null, "뛰기 has no rebindable row. Rows were: " + labels);

            Assert.That(crouch.KeyText, Is.EqualTo("C"));
            Assert.That(jump.KeyText, Is.EqualTo("Space"));
            Assert.That(crouch.Label, Does.Contain("웅크리기"),
                "the row falls through to the raw action name instead of §05's language: " + crouch.Label);
            Assert.That(jump.Label, Does.Contain("뛰기"), "same for the jump row: " + jump.Label);
        }

        [Test]
        public void Instantiate_DoesNotCarryBindingOverrides()
        {
            // The reason InputBindings listens to the Input System instead of just
            // writing the shipped asset. Measured, not assumed: this is the difference
            // between a rebind that reaches the player's hands and one that only reaches
            // the settings screen that shows it.
            var asset = InputBindings.Asset;
            if (asset == null)
            {
                Assert.Fail(
                    "§05's scheme ships at Resources/" + PlayerInputRouter.DefaultAssetResourcePath
                    + ". Without it there is nothing to rebind and PlayerInputRouter logs an error of its own.");
                return;
            }

            var entries = InputBindings.Rebindable();
            Assert.That(entries.Count, Is.GreaterThan(0), "§05's keyboard scheme has W/A/S/D, Shift and F on it.");

            InputActionAsset? clone = null;
            try
            {
                var entry = entries[0];
                entry.Action.ApplyBindingOverride(entry.BindingIndex, "<Keyboard>/numpad7");

                clone = UnityEngine.Object.Instantiate(asset);
                var clonedAction = FindClonedAction(clone, entry.Action.name);

                Assert.That(clonedAction.bindings[entry.BindingIndex].effectivePath, Is.Not.EqualTo("<Keyboard>/numpad7"),
                    "If Object.Instantiate ever starts carrying binding overrides, the re-apply-on-enable machinery in "
                    + "InputBindings is redundant and should be deleted rather than left running. Until then it is the "
                    + "only thing standing between a player's rebind and a control scheme that ignores it.");
            }
            finally
            {
                asset.RemoveAllBindingOverrides();
                if (clone != null)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
        }

        [Test]
        public void Rebinding_ReachesTheCopyThePlayerRigActuallyUses()
        {
            var asset = InputBindings.Asset;
            if (asset == null)
            {
                Assert.Fail("§05's scheme is missing from Resources.");
                return;
            }

            InputActionAsset? clone = null;
            try
            {
                var entries = InputBindings.Rebindable();
                var entry = entries[0];
                entry.Action.ApplyBindingOverride(entry.BindingIndex, "<Keyboard>/numpad7");

                var settings = new GameSettings();
                settings.BindingOverridesJson = InputBindings.CurrentOverridesJson();
                Assert.That(settings.BindingOverridesJson, Is.Not.Empty);

                // Exactly what PlayerInputRouter.Awake does: clone the shipped asset, then
                // enable it. Nothing tells this clone about the player's settings.
                clone = UnityEngine.Object.Instantiate(asset);
                clone.name = asset.name;

                InputBindings.Apply(settings);
                clone.Enable();

                var clonedAction = FindClonedAction(clone, entry.Action.name);
                Assert.That(clonedAction.bindings[entry.BindingIndex].effectivePath, Is.EqualTo("<Keyboard>/numpad7"),
                    "§05's scheme is 'the only place in the game that knows a key is involved', and the copy the player's "
                    + "hands are wired to is the clone — not the asset the settings screen reads. A rebind that lands only "
                    + "on the shipped asset shows the new key on screen and leaves the old one under the player's fingers, "
                    + "with nothing logged. That is the failure this listener exists to make impossible.");
            }
            finally
            {
                InputBindings.ResetToDefaults();
                if (clone != null)
                {
                    clone.Disable();
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
        }

        private static InputAction FindClonedAction(InputActionAsset clone, string actionName)
        {
            var map = clone.FindActionMap(InputBindings.PlayerMap, false);
            Assert.That(map, Is.Not.Null, "The clone lost §05's action map.");

            var action = map!.FindAction(actionName, false);
            Assert.That(action, Is.Not.Null, "The clone lost the '" + actionName + "' action.");
            return action!;
        }

        [Test]
        public void Rebinding_LeavesTheLookAxisAlone()
        {
            foreach (var entry in InputBindings.Rebindable())
            {
                Assert.That(entry.Action.name, Is.Not.EqualTo(InputBindings.LookAction),
                    "§05 makes the 45° peek '이산적 선택이 아니라 아날로그 조절' — a continuous rotation the player meters "
                    + "with the mouse. Offering it as a key would let somebody bind away the one skill the chase is built "
                    + "on, and leave a rig that cannot look at anything in between.");
            }
        }

        [Test]
        public void Sensitivity_ScalesTheLookAxis_AndInvertOnlyFlipsPitch()
        {
            var asset = InputBindings.Asset;
            if (asset == null)
            {
                Assert.Fail("§05's action asset is missing; there is no look axis to scale.");
                return;
            }

            try
            {
                InputBindings.ApplyLookScaling(2f, invertY: true);

                var map = asset.FindActionMap(InputBindings.PlayerMap, false);
                Assert.That(map, Is.Not.Null);

                var look = map!.FindAction(InputBindings.LookAction, false);
                Assert.That(look, Is.Not.Null);

                var processors = string.Empty;
                for (var i = 0; i < look!.bindings.Count; i++)
                {
                    if (look.bindings[i].groups.Contains(InputBindings.KeyboardScheme))
                    {
                        processors = look.bindings[i].effectiveProcessors;
                    }
                }

                Assert.That(processors, Does.Contain("x=2"),
                    "Sensitivity rides on the binding rather than on a field in the rig, so it survives the same clone "
                    + "the rebinds do and composes with them instead of racing them.");
                Assert.That(processors, Does.Contain("y=-2"),
                    "§05 gives yaw and pitch different jobs — yaw is 'forward' and pitch aims the beam — so invert must "
                    + "flip one axis and not both. Flipping yaw as well would reverse the definition of forward.");

                Assert.That(look.bindings[0].effectivePath, Does.Contain("Mouse"),
                    "ApplyBindingOverride clears any field the override leaves null, so carrying the path through is "
                    + "what stops a sensitivity change from unbinding the mouse entirely.");
            }
            finally
            {
                asset.RemoveAllBindingOverrides();
            }
        }

        // ====================================================================
        // Helpers.
        // ====================================================================

        /// <summary>Declared field and property names, so a base class's members are not blamed on this layer.</summary>
        private static IEnumerable<string> NamesOf(Type type)
        {
            foreach (var field in type.GetFields(Declared))
            {
                yield return field.Name;
            }

            foreach (var property in type.GetProperties(Declared))
            {
                yield return property.Name;
            }

            foreach (var method in type.GetMethods(Declared))
            {
                yield return method.Name;
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.Name;
                }
            }

            foreach (var constructor in type.GetConstructors(Declared))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    yield return parameter.Name;
                }
            }
        }
    }
}
