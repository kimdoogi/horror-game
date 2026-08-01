#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.Gameplay.Ghost
{
    /// <summary>
    /// Lets a dead player see. §09's 시야 row — <em>"맵 전체를 자유롭게 본다 (벽 통과)"</em>.
    /// <para>
    /// <b>Why this has to exist at all.</b> §09's answer to 죽으면 지루하다 is 볼 게 있고
    /// 할 게 있다, and the building this game ships is measured at a median pixel value of
    /// 4.2–8.4 of 255 with the practicals off (STATUS.md §4.3). Free flight through a
    /// five-storey basement with nothing to look at is not a reward, it is a black
    /// screen. The ghost is given the picture; it is not given a torch.
    /// </para>
    /// <para>
    /// <b>Why it is exposure and not a light.</b> A light in the world is a channel. A
    /// dead player flying ahead of the team with a lamp on would be telling them where to
    /// go, which is exactly the free information §09 removes speech to prevent —
    /// "죽은 사람이 정보를 주면 밸런스 붕괴". A post-exposure Volume changes nothing in
    /// the world: it is applied when this client's camera resolves its grade, and §13
    /// gives every player their own process, so the living see the basement they always
    /// saw. The one machine that can see more is the one that cannot say anything.
    /// </para>
    /// <para>
    /// <b>Why exposure and not a gamma lift.</b> ART.md §3.7 grades in linear under ACES.
    /// A curve applied after the tonemapper flattens the toe — the milky look that
    /// document warns about — while an exposure offset goes in before it, so the ghost
    /// gets the same picture with more light in it rather than a different picture. This
    /// is the argument <c>BrightnessGrade</c> already makes for the player's own
    /// brightness preference, and this class deliberately mirrors its shape so the two
    /// stack rather than fight (URP adds post-exposure across Volumes).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostViewGrade : MonoBehaviour
    {
        /// <summary>
        /// Stops on top of the graded picture while the local player is a ghost.
        /// <para>
        /// <b>Presentation, not balance.</b> It buys no advantage that can be spent —
        /// §09's ghost cannot speak, cannot escape and cannot touch anything but one
        /// object every forty-five seconds — so it belongs beside
        /// <c>UiStyle.GhostLookDegreesPerPixel</c> and <c>MonsterDebugView</c>'s label
        /// height rather than in <c>GameConstants</c>, which holds the numbers §06's
        /// chase and §08's economy are tuned on.
        /// </para>
        /// <para>
        /// <b>Where the value comes from — measured, not chosen.</b> ART.md's bands for a
        /// frame of this game are 10–40 % crushed, 30–75 % legible, median 3–16 and under
        /// 0.5 % blown. <c>GhostSeatShot.Batch -ghostExposureSweep</c> renders one ghost
        /// viewpoint at 0–5 EV and <c>tools/render/frame_stats.py</c> reads them; on
        /// 2026-08-01, from a B4 corridor at seed 20260731:
        /// <code>
        /// EV   crushed%  legible%  median      verdict
        ///  0     91.4       0.6      0.9       the black screen this class exists to prevent
        ///  1     49.9       9.7      2.0       over the crushed ceiling, far under the legible floor
        ///  2     20.0      54.2      8.8       inside all four bands
        ///  3     18.3      80.4     20.3       over the legible ceiling and the median ceiling
        ///  4     18.3      81.7     34.9       washed out
        ///  5      4.3      83.1     54.3       daylight
        /// </code>
        /// Two stops is the only row that lands, and it is not the brightest row — three
        /// and four stop being a horror game. Re-measure with the same two commands rather
        /// than adjusting this by eye.
        /// </para>
        /// <para>
        /// <b>Read the sweep beside the seven frames it was chosen for.</b> At 2 EV,
        /// <c>Shots/ghostseat_*_asplayed.png</c> measures 13.3–40.3 % crushed, 25.9–72.5 %
        /// legible and medians 3.0–17.7. Five of seven are inside every band; the two that
        /// are not are the extremes of the building rather than of the grade — looking at
        /// the corpse down an unlit corridor (40.3 / 25.9) and looking out over the
        /// rooflines from 8 m up (median 17.7). STATUS.md §4.3 measures the living
        /// player's own zone views at a comparable spread, 32.2–51.9 % legible, so the
        /// ghost's stops centre that distribution rather than flatten it.
        /// </para>
        /// <para>
        /// <b>The first version of this measured brighter, and it was wrong.</b>
        /// <see cref="Raise"/> used to mint a new object per call, and URP sums
        /// post-exposure across Volumes, so the capture rig was stacking a stop per frame.
        /// The table above is the re-measurement after that was fixed; it happens to pick
        /// the same value, which is luck rather than confirmation.
        /// </para>
        /// </summary>
        public const float PostExposureEv = 2f;

        /// <summary>
        /// Above <c>BrightnessGrade</c>'s Volume, which is itself above
        /// <c>ThreatAtmosphereDirector</c>'s. Ordering only — URP sums post-exposure —
        /// but it keeps the ghost's stops the last thing applied, so §07's tier grade and
        /// the player's own brightness slider both still read through it.
        /// </summary>
        private const float VolumePriority = 200f;

        private static GhostViewGrade? _instance;

        private Volume? _volume;
        private VolumeProfile? _profile;
        private ColorAdjustments? _colour;

        /// <summary>
        /// Turns the ghost's grade on for this client, creating it on first use.
        /// <para>
        /// <b>One instance, and it has to be one.</b> URP <em>adds</em> post-exposure
        /// across Volumes, so a second grade is not a second opinion, it is another two
        /// stops. The first version of this minted a fresh object on every call and the
        /// capture rig calls it once per frame — a ghost that flew for a minute would have
        /// walked out into daylight.
        /// </para>
        /// </summary>
        public static GhostViewGrade Raise(Transform? parent)
        {
            var grade = Ensure(parent);
            grade.SetActive(true);
            return grade;
        }

        /// <summary>The live grade, or null when nothing has raised one yet.</summary>
        public static GhostViewGrade? Current
        {
            get { return _instance; }
        }

        /// <summary>Puts the picture back the way the living see it.</summary>
        public void Lower()
        {
            SetActive(false);
        }

        /// <summary>Puts the ghost's stops back on. The instance twin of <see cref="Raise"/>.</summary>
        public void RaiseAgain()
        {
            SetActive(true);
        }

        /// <summary>The stops currently applied. Zero when the grade is down.</summary>
        public float AppliedEv
        {
            get { return _colour != null && _colour.active ? _colour.postExposure.value : 0f; }
        }

        private static GhostViewGrade Ensure(Transform? parent)
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("[GhostView]");
            if (parent != null)
            {
                go.transform.SetParent(parent, worldPositionStays: false);
            }

            _instance = go.AddComponent<GhostViewGrade>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void SetActive(bool on)
        {
            EnsureVolume();

            if (_colour != null)
            {
                _colour.active = on;
                _colour.postExposure.value = on ? PostExposureEv : 0f;
            }

            if (_volume != null)
            {
                _volume.weight = on ? 1f : 0f;
            }
        }

        private void EnsureVolume()
        {
            if (_volume == null)
            {
                _volume = gameObject.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = VolumePriority;
            }

            if (_profile == null)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "GhostView";
                _volume.sharedProfile = _profile;
            }

            if (_colour == null)
            {
                _colour = _profile.Has<ColorAdjustments>()
                    ? _profile.components.Find(c => c is ColorAdjustments) as ColorAdjustments
                    : _profile.Add<ColorAdjustments>();

                if (_colour != null)
                {
                    _colour.postExposure.overrideState = true;
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            if (_profile != null)
            {
                Destroy(_profile);
                _profile = null;
            }
        }
    }
}
