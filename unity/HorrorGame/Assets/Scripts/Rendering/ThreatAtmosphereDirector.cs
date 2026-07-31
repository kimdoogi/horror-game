#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Threat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.Rendering
{
    /// <summary>
    /// Drives the global grade and the scene's air from §07's clock, so the five
    /// threat tiers look different and not merely play different.
    /// <para>
    /// §07 says the only currency is time, and every other system spends it
    /// invisibly: the monster is 0.4 m/s faster at 심야 and the player has no way
    /// to measure that. The picture is where the cost becomes legible — the room
    /// gets colder, the air thicker, the frame narrower. §07 even makes this a
    /// mechanic at 심야 (손전등 반경 −30%); this component is the rest of that
    /// sentence.
    /// </para>
    /// <para>
    /// The tier comes from <see cref="ThreatCurve"/> and nowhere else. A second
    /// definition of the night — even one that only tinted the screen — would be a
    /// second place to look when a match felt wrong, which is the thing
    /// <see cref="ThreatCurve"/>'s remarks exist to prevent.
    /// </para>
    /// <para>
    /// It owns a private <see cref="Volume"/> rather than editing the project's
    /// default profile. The default profile is the film stock (tonemapper, bloom,
    /// grain, chromatic aberration) and is the same all night; only exposure,
    /// temperature, saturation and vignette move, so only those are overridden
    /// here. Anything a player can see changing has to be in one small, obvious
    /// set or the grade becomes impossible to reason about.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Horror/Threat Atmosphere Director")]
    public sealed class ThreatAtmosphereDirector : MonoBehaviour
    {
        /// <summary>
        /// Sorts above a level designer's own volumes but below anything a
        /// designer deliberately pushes higher, so a set-piece can still take the
        /// screen for a scripted moment.
        /// </summary>
        private const float VolumePriority = 100f;

        [Header("§07 clock")]
        [Tooltip("Seconds into the match. The host drives this; the slider is for previewing the night in the editor.")]
        [SerializeField]
        [Min(0f)]
        private float _elapsedSeconds;

        [Header("Environment")]
        [Tooltip("Also write ambient and fog into the scene. Off in edit mode by default, because that dirties the scene asset.")]
        [SerializeField]
        private bool _drivesEnvironment = true;

        private Volume? _volume;
        private VolumeProfile? _profile;
        private ColorAdjustments? _colour;
        private WhiteBalance? _whiteBalance;
        private Vignette? _vignette;

        /// <summary>
        /// Seconds into the match, as §07 counts them. Setting it re-grades
        /// immediately.
        /// <para>
        /// Set by the host rather than read from a clock here: ARCHITECTURE.md's
        /// stepping rule exists so a match replays identically from a seed, and a
        /// presentation layer that quietly consulted <c>Time.time</c> would be the
        /// one thing in the frame that a replay could not reproduce.
        /// </para>
        /// </summary>
        public float ElapsedSeconds
        {
            get => _elapsedSeconds;
            set
            {
                _elapsedSeconds = value;
                Apply();
            }
        }

        /// <summary>The §07 row currently on screen. Useful to HUD and telemetry that want to agree with the picture.</summary>
        public ThreatTier CurrentTier => ThreatCurve.At(_elapsedSeconds);

        /// <summary>
        /// Advances the night by <paramref name="deltaSeconds"/> and re-grades.
        /// <para>
        /// Provided so a host that already ticks its systems at
        /// <see cref="GameConstants.FixedStep"/> can tick this one the same way.
        /// A negative delta is ignored rather than rewound: §07's night does not
        /// run backwards, and a frame spike that produced one would otherwise
        /// brighten the room mid-match.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (!(deltaSeconds > 0f))
            {
                return;
            }

            ElapsedSeconds = _elapsedSeconds + deltaSeconds;
        }

        /// <summary>Pushes the current moment of the night into the Volume, and into the scene if allowed.</summary>
        public void Apply()
        {
            EnsureVolume();

            var settings = NightAtmosphere.At(_elapsedSeconds);

            if (_colour != null)
            {
                _colour.postExposure.value = settings.PostExposure;
                _colour.saturation.value = settings.Saturation;
                _colour.contrast.value = settings.Contrast;
            }

            if (_whiteBalance != null)
            {
                _whiteBalance.temperature.value = settings.Temperature;
            }

            if (_vignette != null)
            {
                _vignette.intensity.value = settings.VignetteIntensity;
            }

            // Ambient and fog are scene state, and writing scene state from
            // [ExecuteAlways] would mark the map dirty every time someone dragged
            // the preview slider. In play mode there is no asset to dirty, and the
            // night has to reach the fog or half the effect is missing.
            if (_drivesEnvironment && Application.isPlaying)
            {
                NightAtmosphere.ApplyEnvironment(settings);
            }
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            // Only re-grade an object that is already live; OnValidate also fires
            // during deserialisation, when creating a Volume would be illegal.
            if (isActiveAndEnabled)
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            // The overrides are ours and hidden from the project, so nothing else
            // can free them. Left behind they would keep grading the screen after
            // the component that explains them is gone.
            if (_volume != null)
            {
                _volume.sharedProfile = null;
            }

            if (_profile != null)
            {
                DestroyOwned(_profile);
                _profile = null;
            }

            _colour = null;
            _whiteBalance = null;
            _vignette = null;
        }

        private void EnsureVolume()
        {
            if (_volume == null)
            {
                _volume = GetComponent<Volume>();
                if (_volume == null)
                {
                    _volume = gameObject.AddComponent<Volume>();
                }
            }

            _volume.isGlobal = true;
            _volume.priority = VolumePriority;
            _volume.weight = 1f;

            if (_profile == null)
            {
                // HideAndDontSave: this profile is derived from §07 every frame, so
                // serialising it would save a snapshot of one arbitrary minute of
                // one match into the scene and then contradict the component.
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "ThreatAtmosphere (runtime)";
                _profile.hideFlags = HideFlags.HideAndDontSave;

                // Override exactly the parameters the night moves, and no others.
                // Adding these with every parameter overridden would quietly stamp
                // the defaults over the project profile's colour filter and
                // vignette shape — the grade would still work, but it would stop
                // being the grade anyone authored.
                _colour = _profile.Add<ColorAdjustments>();
                _colour.postExposure.overrideState = true;
                _colour.saturation.overrideState = true;
                _colour.contrast.overrideState = true;

                _whiteBalance = _profile.Add<WhiteBalance>();
                _whiteBalance.temperature.overrideState = true;

                _vignette = _profile.Add<Vignette>();
                _vignette.intensity.overrideState = true;

                _volume.sharedProfile = _profile;
            }
        }

        private static void DestroyOwned(Object owned)
        {
            if (Application.isPlaying)
            {
                Destroy(owned);
            }
            else
            {
                DestroyImmediate(owned);
            }
        }
    }
}
