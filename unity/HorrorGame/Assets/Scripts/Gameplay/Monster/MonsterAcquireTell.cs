#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// The moment it decides on you: a visible flare when §06's brain enters 추격.
    /// <para>
    /// §06's transition into Chase is the single most consequential event in a match —
    /// it starts the clock on the aggro-release arithmetic (12 m of separation the
    /// Runner cannot open by sprinting, 3 s of broken sight the map has to supply) and
    /// it decides which §12 corner someone is running for. A transition that
    /// consequential cannot be inferred from the audio alone: §04's 청음사 hears it,
    /// but the person actually being chased is usually the one who is running and
    /// therefore the one who cannot hear anything.
    /// </para>
    /// <para>
    /// So the creature announces it. Three things fire together, over
    /// <see cref="GameConstants.AggroReleaseLineOfSightBreak"/>-scaled time:
    /// </para>
    /// <list type="bullet">
    /// <item>the crest snaps flared — the same tell §06 gives Alert, driven past its
    /// authored angle so the moment of commitment out-reads the moment of listening;</item>
    /// <item>the mandible gapes;</item>
    /// <item>the maw lights, using the emission map <c>MonsterSkin</c> armed. The glow
    /// takes the shape of the folds, so it reads as a throat rather than a lamp.</item>
    /// </list>
    /// <para>
    /// Duration is not arbitrary. §06 gives the Runner 12 s of sprint against a monster
    /// 0.8 m/s slower, and a tell that outlasts the first corner stops being a signal
    /// and becomes a light source attached to the antagonist. It is scaled off the
    /// 3-second sight break because that is the unit of time the chase is measured in:
    /// the tell must be over before the first opportunity to break line of sight, or it
    /// is still glowing when the player is trying to judge whether they lost it.
    /// </para>
    /// <para>
    /// <b>Presentation only.</b> The brain has already decided; this is how the decision
    /// looks. It reads <see cref="MonsterStateId"/> and writes to bones and a material
    /// property block, and nothing reads it back.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAcquireTell : MonoBehaviour
    {
        private static readonly int EmissionColourId = Shader.PropertyToID("_EmissionColor");

        /// <summary>
        /// Bones the flare drives, outermost last. These are the four §06 gave the
        /// creature instead of a face — <c>Jaw</c> and <c>Crest1..3</c> — and they are
        /// the reason the model has no eyes to read.
        /// </summary>
        private static readonly string[] CrestBoneNames = { "Crest1", "Crest2", "Crest3" };

        [SerializeField]
        [Tooltip("Left empty, every renderer under this object is driven.")]
        private Renderer[] _renderers = System.Array.Empty<Renderer>();

        [SerializeField]
        [Tooltip("Material name whose emission carries the flare. Matches the generator's material.")]
        private string _mawMaterialName = "Monster_Maw";

        [SerializeField]
        private Transform? _jaw;

        [SerializeField]
        private Transform[] _crest = System.Array.Empty<Transform>();

        [Header("Shape")]
        [SerializeField]
        [Tooltip("Peak maw emission. Sized to beat a clipped highlight — see the note on the field.")]
        private float _peakEmission = 6f;

        [SerializeField]
        [Tooltip("How far past its authored angle the crest snaps, degrees.")]
        private float _crestFlareDegrees = 34f;

        [SerializeField]
        [Tooltip("How far the mandible swings open, degrees.")]
        private float _jawGapeDegrees = 26f;

        [SerializeField]
        [Tooltip("Rise time, seconds. Short enough to read as a snap.")]
        private float _attackSeconds = 0.12f;

        private MaterialPropertyBlock? _block;
        private Quaternion[] _crestRest = System.Array.Empty<Quaternion>();
        private Quaternion _jawRest = Quaternion.identity;
        private bool _restCaptured;

        private MonsterStateId _lastState = MonsterStateId.Patrol;
        private bool _seenState;
        private float _elapsed = float.MaxValue;
        private float _appliedProgress = -1f;

        /// <summary>
        /// Total length of the tell, seconds.
        /// <para>
        /// Derived from §06's 3-second sight break rather than tuned: the flare has to
        /// be finished before the player's first chance to break line of sight, or it is
        /// still burning while they are deciding whether they lost it.
        /// </para>
        /// </summary>
        public static float DurationSeconds => GameConstants.AggroReleaseLineOfSightBreak * 0.55f;

        /// <summary>How far through the flare, 0–1. Zero when it is not running.</summary>
        public float Progress => _elapsed >= DurationSeconds ? 0f : 1f - _elapsed / DurationSeconds;

        /// <summary>
        /// Tells the flare which §06 row the brain is on. Called from
        /// <see cref="MonsterAnimationDriver"/>, so the brain reaches every presentation
        /// component down one path.
        /// </summary>
        public void SetState(MonsterStateId state, bool isStunned)
        {
            var entered = _seenState && state == MonsterStateId.Chase && _lastState != MonsterStateId.Chase;
            _lastState = state;
            _seenState = true;

            if (isStunned)
            {
                // §04's flash interrupts. A creature announcing an acquisition while it
                // is recoiling would tell the Flasher their window did not open when it
                // did, and that window is the whole of the ability.
                _elapsed = float.MaxValue;
                return;
            }

            if (entered)
            {
                Fire();
            }
        }

        /// <summary>Starts the flare now. Public so a host event or a capture rig can trigger it.</summary>
        public void Fire()
        {
            CaptureRest();
            _elapsed = 0f;
        }

        /// <summary>
        /// Advances and applies the flare. Public and delta-argued so the batch capture
        /// rig can pose it at an exact moment rather than photographing whatever frame
        /// it happened to catch.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (_elapsed >= DurationSeconds)
            {
                ApplyProgress(0f);
                return;
            }

            _elapsed += Mathf.Max(0f, deltaSeconds);
            ApplyProgress(Shape(Mathf.Clamp01(_elapsed / DurationSeconds)));
        }

        /// <summary>
        /// Poses the flare at an exact strength, 0–1, ignoring the clock.
        /// <para>
        /// The bone half has to run in <c>LateUpdate</c> order — the Animator writes the
        /// authored crest angle every frame and would overwrite anything applied before
        /// it. Calling this directly is how a capture rig gets a still of the flare.
        /// </para>
        /// </summary>
        public void ApplyProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);

            // Bones are re-applied every frame regardless: the Animator has already
            // overwritten them by the time this runs, so an early-out on an unchanged
            // value would leave the flare lasting exactly one frame.
            CaptureRest();
            PoseBones(progress);

            if (!Mathf.Approximately(progress, _appliedProgress))
            {
                _appliedProgress = progress;
                PushEmission(progress);
            }
        }

        private void Awake()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }

            if (_crest == null || _crest.Length == 0 || _jaw == null)
            {
                BindBones();
            }
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// A hard attack and a long decay. A symmetric curve reads as a pulse, which
        /// looks decorative; asymmetric reads as a reaction, which is what it is.
        /// </summary>
        private float Shape(float t)
        {
            var attack = DurationSeconds > 0f ? Mathf.Clamp01(_attackSeconds / DurationSeconds) : 0.1f;
            if (t <= attack)
            {
                return attack > 0f ? t / attack : 1f;
            }

            var decay = Mathf.Clamp01((t - attack) / Mathf.Max(1e-4f, 1f - attack));
            return (1f - decay) * (1f - decay);
        }

        private void CaptureRest()
        {
            if (_crest == null || _crest.Length == 0 || _jaw == null)
            {
                BindBones();
            }

            if (_restCaptured || _crest == null)
            {
                return;
            }

            _crestRest = new Quaternion[_crest.Length];
            for (var i = 0; i < _crest.Length; i++)
            {
                _crestRest[i] = _crest[i] != null ? _crest[i].localRotation : Quaternion.identity;
            }

            _jawRest = _jaw != null ? _jaw.localRotation : Quaternion.identity;
            _restCaptured = true;
        }

        /// <summary>
        /// Rotates the crest and jaw on top of whatever the Animator wrote.
        /// <para>
        /// Applied as a delta to the <em>current</em> pose, not to a stored rest pose:
        /// the crest already carries a per-state angle — folded on Patrol, flared on
        /// Alert, slammed back on Chase — and replacing it would throw that away. The
        /// flare has to read as "further than it has ever been", which is only true
        /// relative to where the clip has it right now.
        /// </para>
        /// </summary>
        private void PoseBones(float progress)
        {
            if (progress <= 0f)
            {
                return;
            }

            if (_crest != null)
            {
                for (var i = 0; i < _crest.Length; i++)
                {
                    var bone = _crest[i];
                    if (bone == null)
                    {
                        continue;
                    }

                    // Outer blades swing furthest, so the fan opens rather than tilting.
                    var share = (i + 1f) / _crest.Length;
                    bone.localRotation *= Quaternion.Euler(_crestFlareDegrees * progress * share, 0f, 0f);
                }
            }

            if (_jaw != null)
            {
                _jaw.localRotation *= Quaternion.Euler(-_jawGapeDegrees * progress, 0f, 0f);
            }
        }

        private void PushEmission(float progress)
        {
            var renderers = _renderers;
            if (renderers == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            var colour = MawEmission * (_peakEmission * progress);

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    if (materials[slot] == null || !materials[slot].name.Contains(_mawMaterialName))
                    {
                        continue;
                    }

                    // Per-slot, because the maw shares a mesh with the hide and the
                    // carapace and lighting all three would turn the creature into a
                    // lantern — the exact opposite of §03, where darkness is the lock.
                    renderer.GetPropertyBlock(_block, slot);
                    _block.SetColor(EmissionColourId, colour);
                    renderer.SetPropertyBlock(_block, slot);
                }
            }
        }

        /// <summary>
        /// The flare's colour. Deep orange-red: the only warm thing in a basement §12
        /// grades cold, so it is unmistakable at a glance without being bright.
        /// </summary>
        private static Color MawEmission => new Color(1f, 0.17f, 0.06f);

        private void BindBones()
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true))
            {
                bones[t.name] = t;
            }

            var found = new List<Transform>(CrestBoneNames.Length);
            foreach (var name in CrestBoneNames)
            {
                if (bones.TryGetValue(name, out var bone))
                {
                    found.Add(bone);
                }
            }

            _crest = found.ToArray();
            if (_jaw == null && bones.TryGetValue("Jaw", out var jaw))
            {
                _jaw = jaw;
            }
        }
    }
}
