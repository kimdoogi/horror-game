#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core;
using HorrorGame.UI.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// Settings, and they are real settings: every control here writes through
    /// <see cref="SettingsService"/> to a file that survives a restart.
    /// <para>
    /// <b>Two of these are gameplay and the screen says so.</b> §05 makes field of view
    /// a balance item — "95+ 곁눈질이 너무 쉬워 딜레마가 약화된다" — and §03 makes
    /// darkness the lock on progress. A player who widens the view buys the 45° peek at
    /// a discount, and a player who lifts the black point stops needing the flashlight
    /// they came back up to buy batteries for. Both are therefore clamped, and both
    /// carry a line explaining the clamp, because an unexplained cap reads as a bug and
    /// gets worked around with a config file.
    /// </para>
    /// <para>
    /// <b>Everything else is comfort and is left alone.</b> Turning speed, invert-Y, the
    /// mix and the window can be set to whatever a person needs; §05 puts no number on
    /// any of them, so neither does this screen. The one comfort control with a
    /// consequence is the effects volume, which carries §04's warning without blocking
    /// the choice — the same posture §08's shop takes with the 소음기.
    /// </para>
    /// <para>
    /// <b>Applied live, saved on close.</b> Brightness and field of view are judged by
    /// looking, so they take effect as the slider moves; the disk write happens once,
    /// when the screen closes, because a slider drag would otherwise be sixty writes.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsScreen : UiScreen
    {
        private const float PanelWidth = 1780f;
        private const float PanelHeight = 890f;
        private const float ColumnWidth = 830f;

        private Action? _onClose;

        private readonly List<KeyRow> _keyRows = new List<KeyRow>();
        private InputActionRebindingExtensions.RebindingOperation? _rebind;

        private Slider? _fovSlider;
        private Text? _fovValue;
        private Slider? _sensitivitySlider;
        private Text? _sensitivityValue;
        private Text? _invertValue;

        private Slider? _masterSlider;
        private Text? _masterValue;
        private Slider? _sfxSlider;
        private Text? _sfxValue;
        private Slider? _ambienceSlider;
        private Text? _ambienceValue;
        private Slider? _voiceSlider;
        private Text? _voiceValue;

        private Slider? _brightnessSlider;
        private Text? _brightnessValue;
        private Text? _resolutionValue;
        private Text? _fullScreenValue;
        private Text? _vSyncValue;
        private Text? _qualityValue;

        private Text? _pathNote;

        private IReadOnlyList<Vector2Int> _resolutions = Array.Empty<Vector2Int>();
        private IReadOnlyList<FullScreenMode> _fullScreenModes = Array.Empty<FullScreenMode>();
        private IReadOnlyList<string> _qualityNames = Array.Empty<string>();

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderEnd; }
        }

        /// <inheritdoc />
        protected override bool Interactive
        {
            get { return true; }
        }

        /// <summary>Opens the screen. <paramref name="onClose"/> runs after the settings are written.</summary>
        public void Open(Action onClose)
        {
            _onClose = onClose;
            SetVisible(true);
            Refresh();

            // §05 · §13: the requirement has now been put in front of the player once,
            // so the menu's band can go quiet.
            SettingsService.Current.HeadphoneNoticeSeen = true;
        }

        /// <summary>Writes the settings to disk and closes.</summary>
        public void Close()
        {
            CancelRebind();
            SettingsService.Save();
            SetVisible(false);
            _onClose?.Invoke();
        }

        /// <summary>Redraws every control from the live settings.</summary>
        public void Refresh()
        {
            EnsureBuilt();

            var settings = SettingsService.Current;

            _fovSlider?.SetValueWithoutNotify(settings.FieldOfViewDegrees);
            _sensitivitySlider?.SetValueWithoutNotify(settings.MouseSensitivity);
            _masterSlider?.SetValueWithoutNotify(settings.VolumeMaster);
            _sfxSlider?.SetValueWithoutNotify(settings.VolumeSfx);
            _ambienceSlider?.SetValueWithoutNotify(settings.VolumeAmbience);
            _voiceSlider?.SetValueWithoutNotify(settings.VolumeVoice);
            _brightnessSlider?.SetValueWithoutNotify(settings.Brightness01);

            DrawReadouts(settings);
            DrawKeys();
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            _resolutions = DisplayOptions.Resolutions();
            _fullScreenModes = DisplayOptions.FullScreenModes();
            _qualityNames = DisplayOptions.QualityLevels();

            // §08's shop panel is deliberately translucent — the team is standing in the
            // open with the night advancing behind it. This screen is the opposite: the
            // match is either not running or stopped, and the world showing through at
            // the panel's 6 % put a bloomed corridor light straight through the middle
            // of the 밝기 column, which is the one row a player has to judge by eye.
            var dim = UiFactory.CreateImage("Dim", root, new Color(0.006f, 0.006f, 0.010f, 0.985f), raycastTarget: true);
            UiFactory.Stretch((RectTransform)dim.transform);

            var panel = UiFactory.CreateImage("Panel", root, UiStyle.Panel);
            var panelRect = UiFactory.Place(
                (RectTransform)panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidth, PanelHeight));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Title", panelRect, Font, "설정", UiStyle.TextSizeTitle, UiStyle.Ink, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -22f), new Vector2(500f, 44f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Subtitle", panelRect, Font,
                    "시야와 밝기는 취향이 아니라 밸런스다. 왜 범위가 막혀 있는지 각 항목에 적어 두었다.",
                    UiControls.NoteSize, UiStyle.InkFaint, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -66f), new Vector2(1100f, 22f));

            var left = UiFactory.CreateRect("Left", panelRect);
            UiFactory.Place(left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -100f), new Vector2(ColumnWidth, 700f));

            var right = UiFactory.CreateRect("Right", panelRect);
            UiFactory.Place(right, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(PanelWidth - ColumnWidth - 32f, -100f), new Vector2(ColumnWidth, 700f));

            BuildViewAndControl(left);
            BuildAudio(left);
            BuildDisplay(right);
            BuildKeys(right);
            BuildFooter(panelRect);

            // Complete on build, so a review render — which never calls Open — is a
            // photograph of the screen rather than of its empty frame.
            DrawReadouts(SettingsService.Current);
            DrawKeys();
        }

        // ------------------------------------------------------------------
        // Left column — §05's view, §05's hands, §04's ears.
        // ------------------------------------------------------------------

        private void BuildViewAndControl(RectTransform column)
        {
            var y = 0f;
            UiControls.CreateSection(column, Font, "시야 · 조작", y, ColumnWidth);
            y -= 46f;

            var fovRow = UiControls.CreateRow(column, Font, "시야각 (FOV)",
                "§05 밸런스 항목. " + GameConstants.FovMin.ToString("0", CultureInfo.InvariantCulture)
                + "~" + GameConstants.FovMax.ToString("0", CultureInfo.InvariantCulture)
                + "로 막혀 있다 — 넓히면 45° 곁눈질이 공짜가 되고 추격의 핵심 딜레마가 약해진다.",
                y, ColumnWidth);
            _fovSlider = UiControls.CreateSlider(fovRow, GameConstants.FovMin, GameConstants.FovMax, GameConstants.FovDefault, OnFovChanged);
            _fovValue = UiControls.CreateValueText(fovRow, Font, string.Empty);
            y -= UiControls.SettingRowHeight + UiControls.SettingRowGap;

            var sensitivityRow = UiControls.CreateRow(column, Font, "마우스 감도",
                "편의 설정이다. FOV와 달리 회전 속도는 한 번에 보이는 범위를 바꾸지 않는다.", y, ColumnWidth);
            _sensitivitySlider = UiControls.CreateSlider(
                sensitivityRow, SettingsLimits.MouseSensitivityMin, SettingsLimits.MouseSensitivityMax,
                SettingsLimits.MouseSensitivityDefault, OnSensitivityChanged);
            _sensitivityValue = UiControls.CreateValueText(sensitivityRow, Font, string.Empty);
            y -= UiControls.SettingRowHeight + UiControls.SettingRowGap;

            var invertRow = UiControls.CreateRow(column, Font, "Y축 반전", "마우스를 밀면 아래를 본다.", y, ColumnWidth);
            _invertValue = UiControls.CreateSwitch(invertRow, Font, string.Empty, OnInvertToggled);
            y -= UiControls.SettingRowHeight + UiControls.SettingRowGap;

            _audioSectionY = y;
        }

        private float _audioSectionY;

        private void BuildAudio(RectTransform column)
        {
            var y = _audioSectionY - 10f;
            UiControls.CreateSection(column, Font, "소리", y, ColumnWidth);
            y -= 46f;

            var master = UiControls.CreateRow(column, Font, "마스터", string.Empty, y, ColumnWidth);
            _masterSlider = UiControls.CreateSlider(master, SettingsLimits.VolumeMin, SettingsLimits.VolumeMax, 1f, OnMasterChanged);
            _masterValue = UiControls.CreateValueText(master, Font, string.Empty);
            y -= UiControls.SettingRowHeight + UiControls.SettingRowGap;

            var sfx = UiControls.CreateRow(column, Font, "효과음",
                "발소리 · 괴물 · 도구 · 인터페이스. §04 청음사는 이 소리만으로 괴물을 찾는다 — 줄이면 직업 하나가 꺼진다.",
                y, ColumnWidth);
            _sfxSlider = UiControls.CreateSlider(sfx, SettingsLimits.VolumeMin, SettingsLimits.VolumeMax, 1f, OnSfxChanged);
            _sfxValue = UiControls.CreateValueText(sfx, Font, string.Empty);
            y -= UiControls.SettingRowHeight + UiControls.SettingRowGap;

            var ambience = UiControls.CreateRow(column, Font, "환경음", "§12 구역 배드와 §07 긴장 배드.", y, ColumnWidth);
            _ambienceSlider = UiControls.CreateSlider(ambience, SettingsLimits.VolumeMin, SettingsLimits.VolumeMax, 1f, OnAmbienceChanged);
            _ambienceValue = UiControls.CreateValueText(ambience, Font, string.Empty);
            y -= UiControls.SettingRowHeight + UiControls.SettingRowGap;

            var voice = UiControls.CreateRow(column, Font, "음성", "§13 근접 음성. §03의 「말로 전달해야 한다」가 지나가는 채널.", y, ColumnWidth);
            _voiceSlider = UiControls.CreateSlider(voice, SettingsLimits.VolumeMin, SettingsLimits.VolumeMax, 1f, OnVoiceChanged);
            _voiceValue = UiControls.CreateValueText(voice, Font, string.Empty);
            y -= UiControls.SettingRowHeight + UiControls.SettingRowGap;

            var band = UiFactory.CreateImage("HeadphoneBand", column, new Color(0.16f, 0.10f, 0.03f, 0.62f));
            UiFactory.Place((RectTransform)band.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(ColumnWidth, 54f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Headphones", band.transform, Font,
                    "헤드폰 필수 — §05: 「3D 오디오는 카메라 기준 → 헤드폰 필수」.\n"
                    + "스피커로는 몸을 돌려 삼각측량하는 §04 청음사의 방향 판별이 성립하지 않는다.",
                    UiControls.NoteSize, UiStyle.Trade, TextAnchor.MiddleLeft).transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(ColumnWidth - 28f, 46f));
        }

        // ------------------------------------------------------------------
        // Right column — §03's lock, and the window.
        // ------------------------------------------------------------------

        private void BuildDisplay(RectTransform column)
        {
            var y = 0f;
            UiControls.CreateSection(column, Font, "화면", y, ColumnWidth);
            y -= 46f;

            var brightnessRow = UiControls.CreateRow(column, Font, "밝기",
                "§03 밸런스 항목. 「어둠 = 목표의 잠금장치」 — 전부 밝히면 손전등도, 배터리도, 왕복할 이유도 사라진다.\n"
                + "그래서 폭이 §03의 판독 한계(" + Percent(GameConstants.ClueMinReadableLightQuality)
                + ")와 같은 ±" + Percent(SettingsLimits.BrightnessGainSpan) + "로 묶여 있다.",
                y, ColumnWidth);
            _brightnessSlider = UiControls.CreateSlider(brightnessRow, 0f, 1f, SettingsLimits.BrightnessNeutral01, OnBrightnessChanged);
            _brightnessValue = UiControls.CreateValueText(brightnessRow, Font, string.Empty);
            y -= UiControls.SettingRowHeight + 34f;

            var resolutionRow = UiControls.CreateRow(column, Font, "해상도", string.Empty, y, ColumnWidth);
            _resolutionValue = UiControls.CreateStepper(resolutionRow, Font, string.Empty, StepResolution);
            y -= UiControls.SettingRowHeight - 14f;

            var fullScreenRow = UiControls.CreateRow(column, Font, "화면 모드", string.Empty, y, ColumnWidth);
            _fullScreenValue = UiControls.CreateStepper(fullScreenRow, Font, string.Empty, StepFullScreen);
            y -= UiControls.SettingRowHeight - 14f;

            var vSyncRow = UiControls.CreateRow(column, Font, "수직 동기화", string.Empty, y, ColumnWidth);
            _vSyncValue = UiControls.CreateStepper(vSyncRow, Font, string.Empty, StepVSync);
            y -= UiControls.SettingRowHeight - 14f;

            var qualityRow = UiControls.CreateRow(column, Font, "품질 프리셋", string.Empty, y, ColumnWidth);
            _qualityValue = UiControls.CreateStepper(qualityRow, Font, string.Empty, StepQuality);
            y -= UiControls.SettingRowHeight - 14f;

            _keySectionY = y;
        }

        private float _keySectionY;

        private void BuildKeys(RectTransform column)
        {
            var y = _keySectionY - 8f;
            UiControls.CreateSection(column, Font, "키 설정", y, ColumnWidth);
            y -= 40f;

            var entries = InputBindings.Rebindable();
            if (entries.Count == 0)
            {
                UiFactory.Place(
                    (RectTransform)UiFactory.CreateText(
                        "NoBindings", column, Font,
                        "PlayerControls 액션 에셋을 찾지 못했다. §05의 조작 구성표가 없는 빌드다.",
                        UiControls.NoteSize, UiStyle.Spent, TextAnchor.UpperLeft).transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(ColumnWidth, 24f));
                return;
            }

            foreach (var entry in entries)
            {
                var row = UiFactory.CreateRect("Key_" + entry.Label, column);
                UiFactory.Place(row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(ColumnWidth, 30f));

                UiFactory.Place(
                    (RectTransform)UiFactory.CreateText(
                        "Label", row, Font, entry.Label, UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleLeft).transform,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(520f, 26f));

                var captured = entry;
                var button = UiFactory.CreateButton("Bind", row, UiStyle.Row, delegate { BeginRebind(captured); });
                UiFactory.Place((RectTransform)button.transform,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(220f, 28f));

                var text = UiFactory.CreateText("Key", button.transform, Font, string.Empty, UiStyle.TextSize, UiStyle.Trade, TextAnchor.MiddleCenter);
                UiFactory.Place((RectTransform)text.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(206f, 24f));

                _keyRows.Add(new KeyRow(captured, button, text));
                y -= 34f;
            }

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "KeyNote", column, Font,
                    "마우스 시점은 바꿀 수 없다. §05의 45° 곁눈질은 아날로그 조절이고, 키로는 성립하지 않는다.",
                    UiControls.NoteSize, UiStyle.InkFaint, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - 4f), new Vector2(ColumnWidth, 22f));
        }

        private void BuildFooter(RectTransform panel)
        {
            _pathNote = UiFactory.CreateText("Path", panel, Font, string.Empty, UiControls.NoteSize, UiStyle.InkFaint, TextAnchor.LowerLeft);
            UiFactory.Place((RectTransform)_pathNote.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 24f), new Vector2(1100f, 20f));

            var reset = UiFactory.CreateButton("Reset", panel, UiStyle.Row, OnResetClicked);
            UiFactory.Place((RectTransform)reset.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-250f, 22f), new Vector2(200f, 44f));
            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Label", reset.transform, Font, "기본값", UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleCenter).transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 26f));

            var close = UiFactory.CreateButton("Close", panel, UiStyle.Row, delegate { Close(); });
            UiFactory.Place((RectTransform)close.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-34f, 22f), new Vector2(200f, 44f));
            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Label", close.transform, Font, "닫기 · 저장", UiStyle.TextSize, UiStyle.Ink, TextAnchor.MiddleCenter).transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 26f));
        }

        // ------------------------------------------------------------------
        // Handlers. Each writes the live settings and applies immediately.
        // ------------------------------------------------------------------

        private void OnFovChanged(float value)
        {
            SettingsService.Current.FieldOfViewDegrees = value;
            SettingsService.ApplyFieldOfView(SettingsService.Current);
            DrawReadouts(SettingsService.Current);
        }

        private void OnSensitivityChanged(float value)
        {
            var settings = SettingsService.Current;
            settings.MouseSensitivity = value;
            InputBindings.ApplyLookScaling(settings.MouseSensitivity, settings.InvertLookY);
            settings.BindingOverridesJson = InputBindings.CurrentOverridesJson();
            DrawReadouts(settings);
        }

        private void OnInvertToggled()
        {
            var settings = SettingsService.Current;
            settings.InvertLookY = !settings.InvertLookY;
            InputBindings.ApplyLookScaling(settings.MouseSensitivity, settings.InvertLookY);
            settings.BindingOverridesJson = InputBindings.CurrentOverridesJson();
            DrawReadouts(settings);
        }

        private void OnMasterChanged(float value)
        {
            SettingsService.Current.VolumeMaster = value;
            AfterVolumeChange();
        }

        private void OnSfxChanged(float value)
        {
            SettingsService.Current.VolumeSfx = value;
            AfterVolumeChange();
        }

        private void OnAmbienceChanged(float value)
        {
            SettingsService.Current.VolumeAmbience = value;
            AfterVolumeChange();
        }

        private void OnVoiceChanged(float value)
        {
            SettingsService.Current.VolumeVoice = value;
            AfterVolumeChange();
        }

        private void AfterVolumeChange()
        {
            SettingsService.ApplyAudio(SettingsService.Current);
            DrawReadouts(SettingsService.Current);
        }

        private void OnBrightnessChanged(float value)
        {
            SettingsService.Current.Brightness01 = value;
            BrightnessGrade.Apply(SettingsService.Current.Brightness01);
            DrawReadouts(SettingsService.Current);
        }

        private void StepResolution(int direction)
        {
            if (_resolutions.Count == 0)
            {
                return;
            }

            var settings = SettingsService.Current;
            var current = new Vector2Int(
                settings.ResolutionWidth > 0 ? settings.ResolutionWidth : Screen.width,
                settings.ResolutionHeight > 0 ? settings.ResolutionHeight : Screen.height);

            var index = Wrap(DisplayOptions.NearestIndex(_resolutions, current) + direction, _resolutions.Count);
            settings.ResolutionWidth = _resolutions[index].x;
            settings.ResolutionHeight = _resolutions[index].y;
            settings.RefreshRateHz = DisplayOptions.BestRefreshRateFor(_resolutions[index].x, _resolutions[index].y);

            DisplayOptions.Apply(settings);
            DrawReadouts(settings);
        }

        private void StepFullScreen(int direction)
        {
            var settings = SettingsService.Current;
            var index = _fullScreenModes.Count == 0 ? 0 : Wrap(IndexOfMode(settings.FullScreenMode) + direction, _fullScreenModes.Count);
            settings.FullScreenMode = _fullScreenModes.Count == 0 ? settings.FullScreenMode : _fullScreenModes[index];

            DisplayOptions.Apply(settings);
            DrawReadouts(settings);
        }

        private void StepVSync(int direction)
        {
            var settings = SettingsService.Current;
            settings.VSyncCount = Wrap(settings.VSyncCount + direction, 3);
            DisplayOptions.Apply(settings);
            DrawReadouts(settings);
        }

        private void StepQuality(int direction)
        {
            if (_qualityNames.Count == 0)
            {
                return;
            }

            var settings = SettingsService.Current;
            var current = settings.QualityLevel >= 0 ? settings.QualityLevel : QualitySettings.GetQualityLevel();
            settings.QualityLevel = Wrap(current + direction, _qualityNames.Count);

            DisplayOptions.Apply(settings);
            DrawReadouts(settings);
        }

        private void OnResetClicked()
        {
            CancelRebind();
            SettingsService.ResetToDefaults();
            Refresh();
        }

        // ------------------------------------------------------------------
        // Rebinding.
        // ------------------------------------------------------------------

        private void BeginRebind(InputBindings.Entry entry)
        {
            CancelRebind();

            for (var i = 0; i < _keyRows.Count; i++)
            {
                if (_keyRows[i].Entry.BindingIndex == entry.BindingIndex && _keyRows[i].Entry.Action == entry.Action)
                {
                    _keyRows[i].Text.text = "…  아무 키나";
                    _keyRows[i].Text.color = UiStyle.Spent;
                }

                _keyRows[i].Button.interactable = false;
            }

            _rebind = InputBindings.StartRebind(entry, completed =>
            {
                _rebind = null;

                var settings = SettingsService.Current;
                settings.BindingOverridesJson = InputBindings.CurrentOverridesJson();
                SettingsService.Save();

                for (var i = 0; i < _keyRows.Count; i++)
                {
                    _keyRows[i].Button.interactable = true;
                }

                DrawKeys();
            });

            if (_rebind == null)
            {
                for (var i = 0; i < _keyRows.Count; i++)
                {
                    _keyRows[i].Button.interactable = true;
                }

                DrawKeys();
            }
        }

        private void CancelRebind()
        {
            if (_rebind == null)
            {
                return;
            }

            var operation = _rebind;
            _rebind = null;
            operation.Cancel();
        }

        /// <inheritdoc />
        protected override void OnDestroy()
        {
            CancelRebind();
            base.OnDestroy();
        }

        // ------------------------------------------------------------------
        // Drawing.
        // ------------------------------------------------------------------

        private void DrawReadouts(GameSettings settings)
        {
            if (_fovValue != null)
            {
                _fovValue.text = Mathf.RoundToInt(settings.FieldOfViewDegrees).ToString(CultureInfo.InvariantCulture) + "°";
                _fovValue.color = Mathf.Approximately(settings.FieldOfViewDegrees, GameConstants.FovMax)
                    ? UiStyle.Spent
                    : UiStyle.Trade;
            }

            if (_sensitivityValue != null)
            {
                _sensitivityValue.text = "×" + settings.MouseSensitivity.ToString("0.00", CultureInfo.InvariantCulture);
            }

            if (_invertValue != null)
            {
                _invertValue.text = settings.InvertLookY ? "켬" : "끔";
            }

            SetVolume(_masterValue, settings.VolumeMaster);
            SetVolume(_sfxValue, settings.VolumeSfx);
            SetVolume(_ambienceValue, settings.VolumeAmbience);
            SetVolume(_voiceValue, settings.VolumeVoice);

            if (_sfxValue != null)
            {
                // §04's warning, on the number rather than only in the note: a 청음사 at
                // zero effects volume is a role that has been switched off.
                _sfxValue.color = settings.VolumeSfx <= 0.001f ? UiStyle.Spent : UiStyle.Trade;
            }

            if (_brightnessValue != null)
            {
                var percent = SettingsLimits.BrightnessPercent(settings.Brightness01);
                _brightnessValue.text = (percent > 0 ? "+" : string.Empty)
                    + percent.ToString(CultureInfo.InvariantCulture) + " %  ("
                    + SettingsLimits.BrightnessExposure(settings.Brightness01).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)
                    + " EV)";
                _brightnessValue.color = percent == 0 ? UiStyle.Calm : UiStyle.Trade;
            }

            if (_resolutionValue != null)
            {
                var width = settings.ResolutionWidth > 0 ? settings.ResolutionWidth : Screen.width;
                var height = settings.ResolutionHeight > 0 ? settings.ResolutionHeight : Screen.height;
                _resolutionValue.text = DisplayOptions.Describe(width, height, settings.RefreshRateHz);
            }

            if (_fullScreenValue != null)
            {
                _fullScreenValue.text = DisplayOptions.Describe(settings.FullScreenMode);
            }

            if (_vSyncValue != null)
            {
                _vSyncValue.text = DisplayOptions.DescribeVSync(settings.VSyncCount);
            }

            if (_qualityValue != null)
            {
                var level = settings.QualityLevel >= 0 ? settings.QualityLevel : QualitySettings.GetQualityLevel();
                _qualityValue.text = level >= 0 && level < _qualityNames.Count ? _qualityNames[level] : "기본";
            }

            if (_pathNote != null)
            {
                _pathNote.text = "설정은 다시 켜도 남는다 — " + SettingsStore.Path;
            }
        }

        private void DrawKeys()
        {
            for (var i = 0; i < _keyRows.Count; i++)
            {
                _keyRows[i].Text.text = _keyRows[i].Entry.KeyText;
                _keyRows[i].Text.color = UiStyle.Trade;
            }
        }

        private static void SetVolume(Text? text, float value)
        {
            if (text != null)
            {
                text.text = Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + " %";
            }
        }

        private int IndexOfMode(FullScreenMode mode)
        {
            for (var i = 0; i < _fullScreenModes.Count; i++)
            {
                if (_fullScreenModes[i] == mode)
                {
                    return i;
                }
            }

            return 0;
        }

        private static string Percent(float fraction)
        {
            return Mathf.RoundToInt(fraction * 100f).ToString(CultureInfo.InvariantCulture) + " %";
        }

        private static int Wrap(int value, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return ((value % count) + count) % count;
        }

        /// <summary>One drawn key row, kept so a rebind rewrites rather than rebuilds.</summary>
        private sealed class KeyRow
        {
            public KeyRow(InputBindings.Entry entry, Button button, Text text)
            {
                Entry = entry;
                Button = button;
                Text = text;
            }

            public InputBindings.Entry Entry { get; }

            public Button Button { get; }

            public Text Text { get; }
        }
    }
}
