#nullable enable

using System;
using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// The four <c>step_{surface}_monster_step_*</c> variants for one §12 surface.
    /// </summary>
    [Serializable]
    public sealed class MonsterFootstepSurface
    {
        [SerializeField]
        [Tooltip("Which §12 floor these clips belong to.")]
        private FloorMaterial _material = FloorMaterial.None;

        [SerializeField]
        [Tooltip("step_{surface}_monster_step_01..04.")]
        private AudioClip[] _clips = Array.Empty<AudioClip>();

        /// <summary>The §12 surface this set answers for.</summary>
        public FloorMaterial Material => _material;

        /// <summary>True when the set can actually make a sound.</summary>
        public bool HasClips => _clips != null && _clips.Length > 0;

        /// <summary>
        /// A variant, never the one played last.
        /// <para>
        /// ASSETS.md §2.1: four variants exist because "a machine-gun-identical step
        /// reads as a looping sound cue rather than as a creature walking" — and it is
        /// the creature the Listener is being asked to track.
        /// </para>
        /// </summary>
        public AudioClip? Pick(ref int lastIndex)
        {
            if (!HasClips)
            {
                return null;
            }

            if (_clips.Length == 1)
            {
                lastIndex = 0;
                return _clips[0];
            }

            var index = UnityEngine.Random.Range(0, _clips.Length);
            if (index == lastIndex)
            {
                index = (index + 1) % _clips.Length;
            }

            lastIndex = index;
            return _clips[index];
        }
    }

    /// <summary>
    /// The monster's footsteps — §04's 청음사 has exactly one input and this is it.
    /// <para>
    /// The surface is sampled under the <em>monster</em>, never under the listener.
    /// §12 assigns every zone a different floor specifically so the Listener can answer
    /// 위치, and that only works when the clip identifies where the creature is
    /// standing: a Listener on tile hearing a monster on gravel must hear gravel.
    /// </para>
    /// <para>
    /// Silence is not a special case handled here — it comes from
    /// <see cref="HorrorGame.Core.Monster.MonsterBrain.IsAudible"/>, which is false in
    /// 정지 and true everywhere else. Nothing in this class may decide to be quiet for
    /// any other reason, or the silence stops being information the team can trust.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterFootsteps : MonoBehaviour
    {
        /// <summary>
        /// Steps per gait cycle. Two, because the creature has two feet — a fact about
        /// the rig, not a tuned value.
        /// </summary>
        private const float StepsPerGaitCycle = 2f;

        [SerializeField]
        [Tooltip("3D source for steps. Separate from the voice so a roar never cuts a step.")]
        private AudioSource? _source;

        [SerializeField]
        [Tooltip("Animator whose locomotion cycle times the steps. Left empty, one is searched for.")]
        private Animator? _animator;

        [SerializeField]
        [Tooltip("Layer the locomotion clips play on.")]
        private int _animatorLayer;

        [SerializeField]
        [Tooltip("Metres between steps when no Animator cycle is available. 0 uses the animation.")]
        private float _fallbackStrideMetres;

        [SerializeField]
        [Tooltip("Per-§12-surface clip sets. A surface with no entry falls back to the first set.")]
        private MonsterFootstepSurface[] _surfaces = Array.Empty<MonsterFootstepSurface>();

        private readonly int[] _lastIndexBySurface = new int[16];

        private float _distanceSinceStep;
        private float _previousCycle;
        private int _previousStateHash;
        private bool _hasCycle;
        private bool _warnedNoCadence;

        /// <summary>The surface the last step was played from. The debug view reads it.</summary>
        public FloorMaterial CurrentSurface { get; private set; } = FloorMaterial.None;

        /// <summary>
        /// Advances the step cadence by one fixed step of travel.
        /// </summary>
        /// <param name="distanceMetres">How far the brain moved the monster this step.</param>
        /// <param name="surface">The §12 floor under the monster right now.</param>
        /// <param name="isAudible">
        /// <c>MonsterBrain.IsAudible</c>. The single authority on whether the monster
        /// makes any sound at all — false in 정지 and nowhere else.
        /// </param>
        public void Advance(float distanceMetres, FloorMaterial surface, bool isAudible)
        {
            CurrentSurface = surface;

            if (!isAudible)
            {
                // §06's 정지. The cadence is reset rather than paused so that resuming
                // 순찰 starts a fresh stride instead of firing a step the instant the
                // monster moves again — a step that would give away the position the
                // silence just hid.
                _distanceSinceStep = 0f;
                _hasCycle = false;
                return;
            }

            if (distanceMetres <= 0f)
            {
                // Standing still — in 경계 waiting out the 3 s, or with nowhere reachable
                // to walk. The gait baseline is dropped so that resuming does not fire
                // every half cycle the clip advanced through while the feet were planted.
                _hasCycle = false;
                return;
            }

            if (_fallbackStrideMetres > 0f)
            {
                _distanceSinceStep += distanceMetres;
                while (_distanceSinceStep >= _fallbackStrideMetres)
                {
                    _distanceSinceStep -= _fallbackStrideMetres;
                    PlayStep(surface);
                }

                return;
            }

            // The gait clip is the cadence. It already carries §07's speed, because
            // MonsterAnimationDriver scales playback by the tier's own m/s — so the
            // 동트기 전 monster steps faster without anyone tuning a second number.
            if (!TryAdvanceGaitCycle(surface) && !_warnedNoCadence)
            {
                _warnedNoCadence = true;
                Debug.LogWarning(
                    $"[{name}] no Animator locomotion cycle and no fallback stride: the monster is "
                    + "moving silently. §04's Listener has no other input — set one of the two.",
                    this);
            }
        }

        /// <summary>
        /// Plays one step on the current surface. Public so an Animation Event on the
        /// gait clips can drive the cadence from the art instead of from this code.
        /// </summary>
        public void PlayStep()
        {
            PlayStep(CurrentSurface);
        }

        private void Awake()
        {
            if (_source == null)
            {
                _source = GetComponent<AudioSource>();
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
        }

        private bool TryAdvanceGaitCycle(FloorMaterial surface)
        {
            var animator = _animator;
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            var info = animator.GetCurrentAnimatorStateInfo(_animatorLayer);
            var cycle = info.normalizedTime * StepsPerGaitCycle;

            if (!_hasCycle || info.shortNameHash != _previousStateHash)
            {
                _hasCycle = true;
                _previousStateHash = info.shortNameHash;
                _previousCycle = cycle;
                return true;
            }

            // One step each time the clip passes a half cycle — the other foot landing.
            var crossings = Mathf.FloorToInt(cycle) - Mathf.FloorToInt(_previousCycle);
            _previousCycle = cycle;

            for (var i = 0; i < crossings; i++)
            {
                PlayStep(surface);
            }

            return true;
        }

        private void PlayStep(FloorMaterial surface)
        {
            var source = _source;
            if (source == null)
            {
                return;
            }

            var index = IndexOfSurface(surface);
            if (index < 0)
            {
                return;
            }

            var clip = _surfaces[index].Pick(ref _lastIndexBySurface[index % _lastIndexBySurface.Length]);
            if (clip != null)
            {
                source.PlayOneShot(clip);
            }
        }

        private int IndexOfSurface(FloorMaterial surface)
        {
            var fallback = -1;
            for (var i = 0; i < _surfaces.Length; i++)
            {
                if (!_surfaces[i].HasClips)
                {
                    continue;
                }

                if (_surfaces[i].Material == surface)
                {
                    return i;
                }

                if (fallback < 0)
                {
                    fallback = i;
                }
            }

            // An unmapped surface still has to make a noise. A monster that goes silent
            // because a floor was never tagged looks exactly like §06's 정지, and the
            // team would read a level-authoring gap as the monster stopping.
            return fallback;
        }
    }
}
