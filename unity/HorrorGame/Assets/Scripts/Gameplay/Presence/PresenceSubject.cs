#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Presence;
using UnityEngine;

namespace HorrorGame.Gameplay.Presence
{
    /// <summary>
    /// One player, as the 그늘 sees them: a position, how much light is on them, and
    /// whether they are still in the building.
    /// <para>
    /// <b>Light on the player, not light on what they are pointing at.</b> That
    /// distinction is the whole of §03's escort and it is why this samples the scene's
    /// lights against the body rather than asking the flashlight what it is illuminating.
    /// A player holding a lit torch is standing in its spill and reads 1.0. A player
    /// carrying 목표물 cannot hold a torch at all — §03: "양손을 쓴다" — so their light is
    /// whatever a teammate is putting on them, and §03's 누군가 비춰줘야 한다 acquires a
    /// consequence for the first time.
    /// </para>
    /// <para>
    /// Host authority (§13): this reports, it does not decide. <see cref="PresenceDirector"/>
    /// runs the rules and only on the host.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PresenceSubject : MonoBehaviour
    {
        /// <summary>
        /// Unity light intensity that counts as fully lit for §03's purposes.
        /// <para>
        /// <b>Not a game number, and deliberately not in <c>GameConstants</c>.</b> It is a
        /// units conversion between Unity's photometric intensity and the 0–1 light quality
        /// §03's rules are written in, so it means nothing outside an engine and
        /// <c>GameConstants</c> may not reference one. <c>AssetImportPolicy</c> sets the
        /// precedent and gives the same reasoning for its own thresholds.
        /// </para>
        /// <para>
        /// Derived from ART.md §3.6: a §12 practical is intensity 1.1 over a 5.5 m range,
        /// and standing under a practical is the canonical "you are lit" situation in this
        /// building. So one practical at its own centre is 1.0 here.
        /// </para>
        /// </summary>
        public const float ReferenceLightIntensity = 1.1f;

        /// <summary>
        /// Height above the transform the light is sampled at, metres. Chest rather than
        /// feet: a floor-level sample sits in every piece of furniture's shadow and would
        /// report a player standing under a lamp as unlit.
        /// </summary>
        public const float SampleHeightMetres = 1.2f;

        [Header("Identity")]
        [SerializeField]
        [Tooltip("Index into the match's player list. §11 makes that four.")]
        private int _playerIndex;

        [Header("Light")]
        [SerializeField]
        [Tooltip("Lights this player carries. A lit one of these means fully lit — §03's beam spill.")]
        private List<Light> _carriedLights = new List<Light>();

        [SerializeField]
        [Tooltip("Layers that block light for the occlusion check. Level geometry, not characters.")]
        private LayerMask _lightBlockers = ~0;

        [SerializeField]
        [Tooltip("Off for a test rig that wants to state the light quality itself.")]
        private bool _sampleSceneLights = true;

        [Header("Standing")]
        [SerializeField]
        [Tooltip("TOMBSTONE: §01's 지상, set when a player climbed back out of the basement. A race starts on B1 and ends in B8, so nothing sets this any more.")]
        private bool _onSurface;

        [SerializeField]
        [Tooltip("False for a §09 ghost or a §02 escapee.")]
        private bool _inPlay = true;

        private readonly List<Light> _sceneLights = new List<Light>();
        private float _overrideLightQuality = -1f;

        /// <summary>Index into the match's player list.</summary>
        public int PlayerIndex
        {
            get { return _playerIndex; }
            set { _playerIndex = value; }
        }

        /// <summary>§01's 지상 — the 안전 지대 is safe from the 그늘 too.</summary>
        public bool OnSurface
        {
            get { return _onSurface; }
            set { _onSurface = value; }
        }

        /// <summary>False for a §09 ghost or a §02 escapee. See <c>PresenceField.Tick</c>.</summary>
        public bool InPlay
        {
            get { return _inPlay; }
            set { _inPlay = value; }
        }

        /// <summary>Where the 그늘 measures this player, in world space.</summary>
        public Vector3 SamplePoint => transform.position + (Vector3.up * SampleHeightMetres);

        /// <summary>
        /// Forces the light quality instead of sampling the scene, or -1 to sample. For
        /// test rigs and for the shot rig, which needs the exact number in the caption
        /// rather than whatever the corridor happened to be doing.
        /// </summary>
        public float OverrideLightQuality
        {
            get { return _overrideLightQuality; }
            set { _overrideLightQuality = value; }
        }

        /// <summary>Adds a light this player is carrying. A lit one means fully lit.</summary>
        public void AddCarriedLight(Light light)
        {
            if (light != null && !_carriedLights.Contains(light))
            {
                _carriedLights.Add(light);
            }
        }

        /// <summary>
        /// Rebuilds the cache of scene lights. Called by <see cref="PresenceDirector"/> when
        /// a match begins, because §04's 정비공 can bring a whole zone up mid-match and a
        /// stale cache would leave the 그늘 pooling in a lit room.
        /// </summary>
        public void RefreshSceneLights()
        {
            _sceneLights.Clear();
            _sceneLights.AddRange(
                Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        /// <summary>
        /// How lit this player is, 0–1, on §03's scale — the same scale
        /// <c>LightField.SampleAt</c> answers on and the same threshold
        /// <c>GameConstants.MinSafeLightQuality</c> gates safety with.
        /// </summary>
        public float LightQuality()
        {
            if (_overrideLightQuality >= 0f)
            {
                return Mathf.Clamp01(_overrideLightQuality);
            }

            for (var i = 0; i < _carriedLights.Count; i++)
            {
                var carried = _carriedLights[i];
                if (carried != null && carried.enabled && carried.gameObject.activeInHierarchy
                    && carried.intensity > 0f)
                {
                    // §03 prices a lit beam as "괴물이 잘 본다" — the holder is conspicuous,
                    // which is the same statement as "the holder is lit". No falloff: you
                    // cannot stand outside your own torch.
                    return 1f;
                }
            }

            if (!_sampleSceneLights)
            {
                return 0f;
            }

            var point = SamplePoint;
            var best = 0f;

            for (var i = 0; i < _sceneLights.Count; i++)
            {
                var light = _sceneLights[i];
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var quality = QualityFrom(light, point);
                if (quality > best)
                {
                    best = quality;
                }

                if (best >= 1f)
                {
                    break;
                }
            }

            return best;
        }

        /// <summary>Builds the tick input the rules consume.</summary>
        public PresenceTickInput ToInput(float distanceToMonster) =>
            new PresenceTickInput(_playerIndex, LightQuality(), distanceToMonster, _onSurface, _inPlay);

        private float QualityFrom(Light light, Vector3 point)
        {
            // A directional light is the sky. §12's building is five storeys underground
            // and ART.md §7.4 already files daylight down there as a defect; treating one
            // as light on a player would make the 그늘 depend on that bug.
            if (light.type == LightType.Directional)
            {
                return 0f;
            }

            var toPoint = point - light.transform.position;
            var distance = toPoint.magnitude;
            if (distance > light.range || light.range <= 0f)
            {
                return 0f;
            }

            // Inverse-square-ish falloff normalised to the range, which is what URP's own
            // attenuation approximates and is close enough for a threshold test.
            var t = Mathf.Clamp01(distance / light.range);
            var falloff = (1f - t) * (1f - t);

            if (light.type == LightType.Spot)
            {
                var half = light.spotAngle * 0.5f;
                var angle = Vector3.Angle(light.transform.forward, toPoint);
                if (angle > half)
                {
                    return 0f;
                }

                falloff *= Mathf.Clamp01(1f - (angle / Mathf.Max(1f, half)) * 0.5f);
            }

            var quality = falloff * Mathf.Clamp01(light.intensity / ReferenceLightIntensity);
            if (quality <= 0f)
            {
                return 0f;
            }

            // A lamp on the other side of a wall does not light you, and §12's building is
            // mostly wall. Skipped for anything already too dim to matter, because this is
            // the only raycast in the whole system and it runs per light per player.
            if (quality >= GameConstants.MinSafeLightQuality * 0.5f
                && Physics.Linecast(light.transform.position, point, _lightBlockers,
                    QueryTriggerInteraction.Ignore))
            {
                return 0f;
            }

            return Mathf.Clamp01(quality);
        }
    }
}
