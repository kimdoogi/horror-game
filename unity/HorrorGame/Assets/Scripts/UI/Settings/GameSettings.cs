#nullable enable

using System;
using HorrorGame.Audio;
using HorrorGame.Gameplay.Player;
using UnityEngine;

namespace HorrorGame.UI.Settings
{
    /// <summary>
    /// Everything the player can change about the game, as plain data.
    /// <para>
    /// <b>This type applies nothing.</b> It holds values, clamps them and round-trips
    /// through JSON; <see cref="SettingsService"/> is what pushes them at the engine.
    /// The split is what makes the load/clamp/default behaviour testable in EditMode
    /// with no camera, no mixer and no window — which matters more here than usual,
    /// because a settings file is the one piece of state that survives a crash and is
    /// therefore the one piece that can poison every subsequent launch.
    /// </para>
    /// <para>
    /// <b>Fields are private and serialized, and every reader goes through a property.</b>
    /// <see cref="JsonUtility"/> writes the fields directly, which means a hand-edited
    /// or version-skewed file arrives unclamped — so <see cref="Clamp"/> runs after
    /// every load rather than trusting the writer. Assigning through a property clamps
    /// on the way in as well, so the in-memory value is never out of range even for one
    /// frame.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        /// <summary>
        /// Format of the file on disk. Bumped when a field changes meaning rather than
        /// when one is added: <see cref="JsonUtility"/> leaves unknown fields at their
        /// constructed defaults, so additions are already safe.
        /// </summary>
        public const int CurrentSchema = 1;

        [SerializeField] private int _schema = CurrentSchema;

        [SerializeField] private float _fieldOfViewDegrees = HorrorGame.Core.GameConstants.FovDefault;
        [SerializeField] private float _mouseSensitivity = SettingsLimits.MouseSensitivityDefault;
        [SerializeField] private bool _invertLookY;
        [SerializeField] private float _viewMotion = ViewMotionTuning.ScaleDefault;

        [SerializeField] private float _volumeMaster = SettingsLimits.VolumeMax;
        [SerializeField] private float _volumeSfx = SettingsLimits.VolumeMax;
        [SerializeField] private float _volumeAmbience = SettingsLimits.VolumeMax;
        [SerializeField] private float _volumeVoice = SettingsLimits.VolumeMax;

        [SerializeField] private float _brightness01 = -1f;

        [SerializeField] private int _resolutionWidth;
        [SerializeField] private int _resolutionHeight;
        [SerializeField] private int _refreshRateHz;
        [SerializeField] private int _fullScreenMode = (int)FullScreenMode.FullScreenWindow;
        [SerializeField] private int _vSyncCount = 1;
        [SerializeField] private int _qualityLevel = -1;

        [SerializeField] private bool _headphoneNoticeSeen;
        [SerializeField] private string _bindingOverridesJson = string.Empty;

        /// <summary>A settings object with every value at its shipped default.</summary>
        public GameSettings()
        {
            _brightness01 = SettingsLimits.BrightnessNeutral01;
        }

        /// <summary>Schema of the file this came from, so a migration can tell.</summary>
        public int Schema
        {
            get { return _schema; }
        }

        /// <summary>
        /// §05's balance value. Clamped by <see cref="PlayerCameraRig.ClampFov"/>, which
        /// is the one authority: "고정하면 멀미 민원이 발생하고, 무제한으로 열면 넓게
        /// 설정한 쪽이 유리해진다."
        /// </summary>
        public float FieldOfViewDegrees
        {
            get { return _fieldOfViewDegrees; }
            set { _fieldOfViewDegrees = PlayerCameraRig.ClampFov(value); }
        }

        /// <summary>Multiplier on the rig's authored degrees-per-count. A comfort value (§05 turning speed is not balance).</summary>
        public float MouseSensitivity
        {
            get { return _mouseSensitivity; }
            set { _mouseSensitivity = Mathf.Clamp(Sane(value, SettingsLimits.MouseSensitivityDefault), SettingsLimits.MouseSensitivityMin, SettingsLimits.MouseSensitivityMax); }
        }

        /// <summary>Whether pushing the mouse forward looks down. Some people cannot play without it.</summary>
        public bool InvertLookY
        {
            get { return _invertLookY; }
            set { _invertLookY = value; }
        }

        /// <summary>
        /// How much of <see cref="PlayerViewMotion"/>'s stride, landing, lean and
        /// breathing reaches the camera, 0..1.
        /// <para>
        /// A comfort value, and the reason it exists at all is the same one §05 gives
        /// for not fixing the field of view — "고정하면 멀미 민원이 발생하고". A
        /// first-person camera that moves makes some people ill within minutes, and a
        /// player who cannot look at the screen refunds the game.
        /// </para>
        /// <para>
        /// <b>Zero is a fully supported way to play and that is a design property, not
        /// a concession.</b> Nothing in <c>ViewMotionTuning</c> is load-bearing for any
        /// rule: the offsets go to the camera transform alone, §05's speed table is
        /// untouched, and the beam still points where <c>PlayerLook</c> says. So unlike
        /// the field of view — which is clamped precisely because a wider one is an
        /// advantage — this slider cannot be set to a competitive value. It is the one
        /// row on the settings screen with no balance consequence at all.
        /// </para>
        /// </summary>
        public float ViewMotion
        {
            get { return _viewMotion; }
            set { _viewMotion = Mathf.Clamp01(Sane(value, ViewMotionTuning.ScaleDefault)); }
        }

        /// <summary>Everything, 0..1. <see cref="AudioBus.Master"/>.</summary>
        public float VolumeMaster
        {
            get { return _volumeMaster; }
            set { _volumeMaster = ClampVolume(value); }
        }

        /// <summary>
        /// Footsteps, the monster, items and interface, 0..1.
        /// <para>
        /// §04 gives the 청음사 nothing but sound to work with and §12 makes the five
        /// floor surfaces their entire alphabet, so this one slider can delete a role.
        /// The settings screen says so on the row. It does not stop the player — §08's
        /// 소음기 sets the precedent that this project warns and lets the team make its
        /// own mistake.
        /// </para>
        /// </summary>
        public float VolumeSfx
        {
            get { return _volumeSfx; }
            set { _volumeSfx = ClampVolume(value); }
        }

        /// <summary>§12's zone beds and §07's tension beds, 0..1. <see cref="AudioBus.Ambience"/>.</summary>
        public float VolumeAmbience
        {
            get { return _volumeAmbience; }
            set { _volumeAmbience = ClampVolume(value); }
        }

        /// <summary>§13's 근접 음성, 0..1. The channel §03's clue rule runs through.</summary>
        public float VolumeVoice
        {
            get { return _volumeVoice; }
            set { _volumeVoice = ClampVolume(value); }
        }

        /// <summary>
        /// Display brightness as a 0..1 slider position, mapped to exposure by
        /// <see cref="SettingsLimits.BrightnessExposure"/>. Narrow on purpose: §03.
        /// </summary>
        public float Brightness01
        {
            get { return _brightness01; }
            set { _brightness01 = Mathf.Clamp01(Sane(value, SettingsLimits.BrightnessNeutral01)); }
        }

        /// <summary>Window width in pixels. Zero means "whatever the display is", which is what a first launch wants.</summary>
        public int ResolutionWidth
        {
            get { return _resolutionWidth; }
            set { _resolutionWidth = Mathf.Max(0, value); }
        }

        /// <summary>Window height in pixels. Zero means "whatever the display is".</summary>
        public int ResolutionHeight
        {
            get { return _resolutionHeight; }
            set { _resolutionHeight = Mathf.Max(0, value); }
        }

        /// <summary>Refresh rate in whole hertz, or zero for the display's own.</summary>
        public int RefreshRateHz
        {
            get { return _refreshRateHz; }
            set { _refreshRateHz = Mathf.Max(0, value); }
        }

        /// <summary>How the window is presented. Borderless by default — alt-tabbing to Discord is §14's voice plan.</summary>
        public FullScreenMode FullScreenMode
        {
            get { return (FullScreenMode)_fullScreenMode; }
            set { _fullScreenMode = (int)value; }
        }

        /// <summary>Frames to wait per present, 0–2. Unity accepts nothing else.</summary>
        public int VSyncCount
        {
            get { return _vSyncCount; }
            set { _vSyncCount = Mathf.Clamp(value, 0, 2); }
        }

        /// <summary>Index into <c>QualitySettings.names</c>, or −1 for "leave the project default alone".</summary>
        public int QualityLevel
        {
            get { return _qualityLevel; }
            set { _qualityLevel = value < 0 ? -1 : value; }
        }

        /// <summary>
        /// Whether the player has been shown §05's headphone requirement. Stored so the
        /// notice can be loud exactly once and quiet forever after — §13 lists it as a
        /// store-page item, which means it is a purchase-time expectation the game is
        /// obliged to restate on first run.
        /// </summary>
        public bool HeadphoneNoticeSeen
        {
            get { return _headphoneNoticeSeen; }
            set { _headphoneNoticeSeen = value; }
        }

        /// <summary>
        /// The Input System's own binding-override blob, verbatim. Opaque here on
        /// purpose: the format belongs to the package, and parsing it in this layer
        /// would make a package upgrade a settings-file migration.
        /// </summary>
        public string BindingOverridesJson
        {
            get { return _bindingOverridesJson ?? string.Empty; }
            set { _bindingOverridesJson = value ?? string.Empty; }
        }

        /// <summary>The 0..1 volume for one bus, so an applier does not need a switch of its own.</summary>
        public float VolumeFor(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Master:
                    return _volumeMaster;

                case AudioBus.Ambience:
                    return _volumeAmbience;

                case AudioBus.Voice:
                    return _volumeVoice;

                default:
                    // Footsteps, Monster, Items, Interface. Grouped because a player
                    // thinks in "sound effects"; the §04 consequence of turning them
                    // down is on the label rather than in a fifth slider nobody moves.
                    return _volumeSfx;
            }
        }

        /// <summary>
        /// Forces every value back inside its range. Run after any load — a file that
        /// came off disk was written by some version of this game and not necessarily
        /// this one.
        /// </summary>
        public GameSettings Clamp()
        {
            FieldOfViewDegrees = _fieldOfViewDegrees;
            MouseSensitivity = _mouseSensitivity;
            ViewMotion = _viewMotion;
            Brightness01 = _brightness01;

            VolumeMaster = _volumeMaster;
            VolumeSfx = _volumeSfx;
            VolumeAmbience = _volumeAmbience;
            VolumeVoice = _volumeVoice;

            ResolutionWidth = _resolutionWidth;
            ResolutionHeight = _resolutionHeight;
            RefreshRateHz = _refreshRateHz;
            VSyncCount = _vSyncCount;
            QualityLevel = _qualityLevel;

            if (!IsKnownFullScreenMode(_fullScreenMode))
            {
                _fullScreenMode = (int)UnityEngine.FullScreenMode.FullScreenWindow;
            }

            _bindingOverridesJson ??= string.Empty;
            _schema = CurrentSchema;
            return this;
        }

        /// <summary>An independent copy, so a settings screen can be cancelled.</summary>
        public GameSettings Clone()
        {
            var copy = new GameSettings();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>Overwrites every value from <paramref name="other"/>. Null is ignored.</summary>
        public void CopyFrom(GameSettings? other)
        {
            if (other == null)
            {
                return;
            }

            _schema = other._schema;
            _fieldOfViewDegrees = other._fieldOfViewDegrees;
            _mouseSensitivity = other._mouseSensitivity;
            _invertLookY = other._invertLookY;
            _viewMotion = other._viewMotion;
            _volumeMaster = other._volumeMaster;
            _volumeSfx = other._volumeSfx;
            _volumeAmbience = other._volumeAmbience;
            _volumeVoice = other._volumeVoice;
            _brightness01 = other._brightness01;
            _resolutionWidth = other._resolutionWidth;
            _resolutionHeight = other._resolutionHeight;
            _refreshRateHz = other._refreshRateHz;
            _fullScreenMode = other._fullScreenMode;
            _vSyncCount = other._vSyncCount;
            _qualityLevel = other._qualityLevel;
            _headphoneNoticeSeen = other._headphoneNoticeSeen;
            _bindingOverridesJson = other._bindingOverridesJson;
            Clamp();
        }

        /// <summary>
        /// Value equality over everything a player can change. Used to skip a disk write
        /// when a screen was opened and closed without touching anything.
        /// </summary>
        public bool Matches(GameSettings? other)
        {
            if (other == null)
            {
                return false;
            }

            return Mathf.Approximately(_fieldOfViewDegrees, other._fieldOfViewDegrees)
                && Mathf.Approximately(_mouseSensitivity, other._mouseSensitivity)
                && _invertLookY == other._invertLookY
                && Mathf.Approximately(_viewMotion, other._viewMotion)
                && Mathf.Approximately(_volumeMaster, other._volumeMaster)
                && Mathf.Approximately(_volumeSfx, other._volumeSfx)
                && Mathf.Approximately(_volumeAmbience, other._volumeAmbience)
                && Mathf.Approximately(_volumeVoice, other._volumeVoice)
                && Mathf.Approximately(_brightness01, other._brightness01)
                && _resolutionWidth == other._resolutionWidth
                && _resolutionHeight == other._resolutionHeight
                && _refreshRateHz == other._refreshRateHz
                && _fullScreenMode == other._fullScreenMode
                && _vSyncCount == other._vSyncCount
                && _qualityLevel == other._qualityLevel
                && _headphoneNoticeSeen == other._headphoneNoticeSeen
                && string.Equals(BindingOverridesJson, other.BindingOverridesJson, StringComparison.Ordinal);
        }

        private static float ClampVolume(float value)
        {
            return Mathf.Clamp(Sane(value, SettingsLimits.VolumeMax), SettingsLimits.VolumeMin, SettingsLimits.VolumeMax);
        }

        /// <summary>
        /// NaN and infinity back to a default rather than through the clamp.
        /// <c>Mathf.Clamp</c> passes NaN straight through, and a NaN field of view is a
        /// camera that renders nothing — a corrupt settings file must not be able to
        /// produce a black window the player cannot get out of.
        /// </summary>
        private static float Sane(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        private static bool IsKnownFullScreenMode(int mode)
        {
            return mode == (int)UnityEngine.FullScreenMode.ExclusiveFullScreen
                || mode == (int)UnityEngine.FullScreenMode.FullScreenWindow
                || mode == (int)UnityEngine.FullScreenMode.MaximizedWindow
                || mode == (int)UnityEngine.FullScreenMode.Windowed;
        }
    }
}
