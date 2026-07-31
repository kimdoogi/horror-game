#nullable enable

using System;
using HorrorGame.Audio;
using HorrorGame.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.UI.Settings
{
    /// <summary>
    /// The one live copy of the player's settings, and the only thing that pushes them
    /// at the engine.
    /// <para>
    /// <b>Static, because the settings outlive every scene.</b> The screen that changes
    /// them is in the menu; three of the five things they change — the field of view,
    /// the mix, the grade — are in the match. Anything scene-owned would have to be
    /// carried across the load or rebuilt on the far side, and rebuilding is how a
    /// setting quietly reverts on the second match of an evening.
    /// </para>
    /// <para>
    /// <b>Re-applied on every scene load.</b> §05's field of view belongs to a
    /// <c>PlayerCameraRig</c> that does not exist until the match scene is up, so
    /// applying once at boot would leave every match on the authored 80 no matter what
    /// the player chose. The re-apply is idempotent and costs a handful of property
    /// writes.
    /// </para>
    /// <para>
    /// This layer never invents a value. §05's clamp is
    /// <see cref="PlayerCameraRig.ClampFov"/>'s, the bus trims are
    /// <c>AudioTuning</c>'s, and §03's brightness bound is derived from
    /// <c>GameConstants</c> in <see cref="SettingsLimits"/>.
    /// </para>
    /// </summary>
    public static class SettingsService
    {
        private static GameSettings? _current;
        private static bool _hooked;

        /// <summary>
        /// Raised after <see cref="Apply"/> has run, so a screen that is already open
        /// can redraw. Cleared on domain reload with everything else here.
        /// </summary>
        public static event Action<GameSettings>? Changed;

        /// <summary>
        /// The settings in force. Loaded from disk on first touch; never null.
        /// </summary>
        public static GameSettings Current
        {
            get
            {
                if (_current == null)
                {
                    _current = SettingsStore.Load();
                }

                return _current;
            }
        }

        /// <summary>Whether a settings file has ever been written. False on a first launch.</summary>
        public static bool HasSavedFile
        {
            get { return SettingsStore.Exists(); }
        }

        /// <summary>
        /// Loads from disk and applies. Called once at boot; safe to call again.
        /// </summary>
        public static void Initialize()
        {
            _current = SettingsStore.Load();
            HookSceneLoads();
            Apply();
        }

        /// <summary>
        /// Replaces the live settings with <paramref name="settings"/>, applies them and
        /// writes them to disk.
        /// </summary>
        /// <param name="settings">The new values. Copied, not adopted.</param>
        /// <returns>False when the disk write failed; the settings are still live.</returns>
        public static bool Commit(GameSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            Current.CopyFrom(settings);
            Apply();
            return SettingsStore.Save(Current);
        }

        /// <summary>Writes the live settings without changing them. For a screen that edited <see cref="Current"/> in place.</summary>
        public static bool Save()
        {
            return SettingsStore.Save(Current);
        }

        /// <summary>Throws the file away and goes back to the shipped defaults, applied immediately.</summary>
        public static void ResetToDefaults()
        {
            InputBindings.ResetToDefaults();
            SettingsStore.Delete();
            _current = new GameSettings();
            Apply();
            SettingsStore.Save(Current);
        }

        /// <summary>
        /// Pushes the live settings at the engine: mix, display, grade, field of view,
        /// bindings. Idempotent.
        /// </summary>
        public static void Apply()
        {
            var settings = Current;

            ApplyAudio(settings);
            DisplayOptions.Apply(settings);
            BrightnessGrade.Apply(settings.Brightness01);
            InputBindings.Apply(settings);
            ApplyFieldOfView(settings);
            ApplyViewMotion(settings);

            Changed?.Invoke(settings);
        }

        /// <summary>
        /// Writes every bus's user volume. <c>GameAudio</c> smooths them, so this is
        /// safe to call while a slider is being dragged — §04's mix must not click when
        /// a player is listening for the monster.
        /// </summary>
        public static void ApplyAudio(GameSettings settings)
        {
            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
            {
                GameAudio.SetUserVolume(bus, settings.VolumeFor(bus));
            }
        }

        /// <summary>
        /// §05's field of view, onto every camera rig currently loaded. Nothing happens
        /// in the menu, where there is no rig — that is the normal case and not a
        /// warning.
        /// </summary>
        public static void ApplyFieldOfView(GameSettings settings)
        {
            var rigs = UnityEngine.Object.FindObjectsByType<PlayerCameraRig>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < rigs.Length; i++)
            {
                rigs[i].FieldOfView = settings.FieldOfViewDegrees;
            }
        }

        /// <summary>
        /// The camera-motion comfort scale, onto every player body currently loaded.
        /// <para>
        /// Separate from <see cref="ApplyFieldOfView"/> even though both write to the
        /// same rig, because they are opposite kinds of value and the settings screen
        /// says so: field of view is §05 balance and is clamped to a window a player
        /// cannot escape, and this one has no balance consequence and goes all the way
        /// to zero. Merging them would invite the next person to clamp this too.
        /// </para>
        /// </summary>
        public static void ApplyViewMotion(GameSettings settings)
        {
            var bodies = UnityEngine.Object.FindObjectsByType<PlayerViewMotion>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < bodies.Length; i++)
            {
                bodies[i].Scale = settings.ViewMotion;
            }
        }

        /// <summary>
        /// Loads and applies before the first scene comes up, so a player never sees one
        /// frame of somebody else's mix or a match at the wrong field of view.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Initialize();
        }

        private static void HookSceneLoads()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only the parts that live in a scene. Re-running the display half would
            // resize the window on every load, which reads as the game crashing and
            // recovering.
            var settings = Current;
            ApplyAudio(settings);
            ApplyFieldOfView(settings);
            ApplyViewMotion(settings);
            BrightnessGrade.Apply(settings.Brightness01);
        }
    }
}
