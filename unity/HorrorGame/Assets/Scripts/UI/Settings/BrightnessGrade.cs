#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.UI.Settings
{
    /// <summary>
    /// The player's brightness preference, expressed as a post-exposure offset in its
    /// own Volume.
    /// <para>
    /// <b>This is a gameplay setting.</b> §01's building gets darker the further down it
    /// goes, and that deepening is the horror the descent is made of. A player who lifts
    /// the black point until the inner rings are readable has not adjusted their
    /// monitor; they have deleted eight storeys of atmosphere and, in a race, bought
    /// themselves a corridor of sight the rest of the field does not have. So the range
    /// is clamped rather than opened — see <see cref="SettingsLimits"/>, which records
    /// where the ±20 % came from — and the settings screen says why on the row.
    /// </para>
    /// <para>
    /// <b>A separate Volume, at a higher priority than the game's own.</b>
    /// <c>ThreatAtmosphereDirector</c> writes §07's post-exposure into its private
    /// Volume every frame; anything this class wrote into that same override would be
    /// overwritten within the frame, and anything it wrote into the shared profile
    /// asset would be a player preference saved into the project. URP sums the
    /// post-exposure of overlapping global Volumes, so a second one adds the player's
    /// offset on top of the night's own without either knowing about the other.
    /// </para>
    /// <para>
    /// Exposure and not gamma: ART.md §3.7 grades in linear under ACES, and a gamma
    /// curve applied to the tonemapped image flattens the toe — the milky look §3.7
    /// warns about. Exposure goes in before the curve, so a brightened frame is the
    /// same picture with more light in it rather than a different picture.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/UI/Brightness Grade")]
    public sealed class BrightnessGrade : MonoBehaviour
    {
        /// <summary>
        /// Above <c>ThreatAtmosphereDirector</c>'s Volume, so this is applied last.
        /// URP adds post-exposure across Volumes rather than replacing it, so priority
        /// here only decides ordering — but ordering is what makes the value the player
        /// sees the value they set.
        /// </summary>
        private const float VolumePriority = 100f;

        private static BrightnessGrade? _instance;

        private Volume? _volume;
        private VolumeProfile? _profile;
        private ColorAdjustments? _colour;
        private float _exposure;

        /// <summary>The exposure offset currently applied, in EV. Zero is the graded picture untouched.</summary>
        public float ExposureEv
        {
            get { return _exposure; }
        }

        /// <summary>
        /// Applies a 0..1 slider position, creating the Volume on first use.
        /// <para>
        /// Static and self-installing because the settings screen exists in the menu
        /// scene and the thing it is grading is in the match scene. A component the
        /// player had to remember to place would be a brightness slider that silently
        /// did nothing in exactly one of the two scenes.
        /// </para>
        /// </summary>
        public static void Apply(float slider01)
        {
            var grade = Ensure();
            if (grade == null)
            {
                return;
            }

            grade.SetExposure(SettingsLimits.BrightnessExposure(slider01));
        }

        /// <summary>The live grade, or null when the application is shutting down.</summary>
        public static BrightnessGrade? Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("[Brightness]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BrightnessGrade>();
            return _instance;
        }

        /// <summary>Sets the offset directly, in EV. Clamped to the §03-derived range.</summary>
        public void SetExposure(float exposureEv)
        {
            var floor = SettingsLimits.BrightnessExposure(0f);
            var ceiling = SettingsLimits.BrightnessExposure(1f);
            _exposure = Mathf.Clamp(exposureEv, floor, ceiling);

            EnsureVolume();
            if (_colour != null)
            {
                _colour.postExposure.value = _exposure;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            EnsureVolume();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            if (_profile != null)
            {
                // Created at runtime rather than loaded, so nothing else can be holding
                // it and leaving it behind would leak one profile per domain reload.
                Destroy(_profile);
                _profile = null;
            }
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

                _volume.isGlobal = true;
                _volume.priority = VolumePriority;
                _volume.weight = 1f;
            }

            if (_profile == null)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "BrightnessPreference";
                _volume.sharedProfile = _profile;
            }

            if (_colour == null)
            {
                _colour = _profile.Has<ColorAdjustments>()
                    ? _profile.components.Find(c => c is ColorAdjustments) as ColorAdjustments
                    : _profile.Add<ColorAdjustments>();

                if (_colour != null)
                {
                    _colour.active = true;
                    _colour.postExposure.overrideState = true;
                    _colour.postExposure.value = _exposure;
                }
            }
        }
    }
}
