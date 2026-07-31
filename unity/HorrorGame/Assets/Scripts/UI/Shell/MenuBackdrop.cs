#nullable enable

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HorrorGame.UI.Shell
{
    /// <summary>
    /// The slow drift behind the main menu.
    /// <para>
    /// <b>The menu is the store page's first screenshot.</b> §13 lists the store page as
    /// a deliverable, and a Steam page opens on whatever the game looks like when it is
    /// doing nothing. A flat colour behind a title says the renderer is not finished; a
    /// corridor under the real fog, the real grade and the real flashlight says the
    /// opposite, and costs one camera and this file.
    /// </para>
    /// <para>
    /// <b>Slow, and slower than it feels like it should be.</b> The whole cycle is
    /// <see cref="_cycleSeconds"/> long, which reads as "not quite still" rather than as
    /// a camera move. That is the point: the frame has to survive being looked at for as
    /// long as it takes to read four menu entries, and anything with a visible speed
    /// becomes a loop the eye starts predicting on the second pass.
    /// </para>
    /// <para>
    /// Driven from <c>Time.unscaledDeltaTime</c> so it keeps moving behind a paused
    /// game, and so a settings screen that has just set <c>timeScale</c> to zero does
    /// not freeze the menu the player is standing in.
    /// </para>
    /// <para>
    /// None of the numbers here are tuned game values (ARCHITECTURE §2): a camera that
    /// nobody is playing cannot change what happens in a match.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/UI/Menu Backdrop")]
    public sealed class MenuBackdrop : MonoBehaviour
    {
        [Tooltip("Metres travelled along the corridor over one half-cycle.")]
        [SerializeField]
        private float _dollyMetres = 2.4f;

        [Tooltip("Metres of lateral drift, so the walls parallax against each other.")]
        [SerializeField]
        private float _swayMetres = 0.35f;

        [Tooltip("Degrees of yaw across the cycle. Small: the corridor should not appear to be searched.")]
        [SerializeField]
        private float _yawDegrees = 3.5f;

        [Tooltip("Degrees of pitch across the cycle.")]
        [SerializeField]
        private float _pitchDegrees = 1.2f;

        [Tooltip("Seconds for one complete there-and-back.")]
        [SerializeField]
        private float _cycleSeconds = 46f;

        private Vector3 _origin;
        private Quaternion _rest;
        private float _phase;

        /// <summary>Re-reads the current transform as the centre of the drift.</summary>
        public void Recentre()
        {
            _origin = transform.position;
            _rest = transform.rotation;
        }

        private void Awake()
        {
            Recentre();
            EnsurePostProcessing();
        }

        /// <summary>
        /// Turns the grade on for this camera in a player build.
        /// <para>
        /// URP reads post processing off
        /// <see cref="UniversalAdditionalCameraData.renderPostProcessing"/>, which
        /// defaults to false, and a camera with no such component at all is treated as
        /// false too. The editor has <c>AtmosphereCameraPolicy</c> to catch that for
        /// review renders; a shipped player has nothing, so the menu camera would come
        /// up ungraded and untonemapped — the same silent failure that policy exists to
        /// prevent, on the one frame every player sees first.
        /// </para>
        /// </summary>
        private void EnsurePostProcessing()
        {
            var camera = GetComponent<Camera>();
            if (camera == null)
            {
                return;
            }

            if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
            {
                data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        private void OnEnable()
        {
            // A quarter turn in, so the very first frame a player sees is already off
            // centre. Starting at the extreme reads as a camera that has been parked.
            _phase = Mathf.PI * 0.25f;
        }

        private void Update()
        {
            if (_cycleSeconds <= 0.01f)
            {
                return;
            }

            _phase += Time.unscaledDeltaTime * (Mathf.PI * 2f / _cycleSeconds);
            if (_phase > Mathf.PI * 2f)
            {
                _phase -= Mathf.PI * 2f;
            }

            var along = Mathf.Sin(_phase);

            // The lateral term runs at half the rate of the dolly, so the two never
            // return to the same place at the same time and the path does not close into
            // an obvious ellipse.
            var across = Mathf.Sin(_phase * 0.5f);

            transform.position = _origin
                + (_rest * Vector3.forward * (along * _dollyMetres))
                + (_rest * Vector3.right * (across * _swayMetres));

            transform.rotation = _rest * Quaternion.Euler(
                -across * _pitchDegrees,
                along * _yawDegrees,
                0f);
        }
    }
}
