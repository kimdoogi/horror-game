#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Presence;
using HorrorGame.Gameplay.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Presence
{
    /// <summary>
    /// Runs <see cref="PresenceField"/> against the real scene: steps it at
    /// <see cref="GameConstants.FixedStep"/>, feeds it the light on each player and the
    /// distance to the monster, and hands out the toll.
    /// <para>
    /// <b>The field is authoritative and this class decides nothing.</b> Same contract
    /// <c>MonsterAgent</c> holds with <c>MonsterBrain</c>, for the same reason: every
    /// threshold, rate and duration in §10's new row lives in Core where it is tested
    /// without an engine, so if Unity and the field ever disagree about whether a player
    /// has been taken, the field is right and this adapter has a bug.
    /// </para>
    /// <para>
    /// <b>Nothing here moves anything.</b> There is no transform on this component, no
    /// agent, no path and no target. That is what keeps §01's 「이길 수 없는 적」 with
    /// exactly one referent — see <see cref="PresenceField"/>'s remarks.
    /// </para>
    /// <para>
    /// Host authority (§13): this runs on the host. It reads the monster's true position,
    /// which is the thing §13 refuses to send; what it publishes per player is a stage and
    /// a pool fraction, which describe what that player can already see happening to them.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PresenceDirector : MonoBehaviour
    {
        /// <summary>
        /// Whole steps one <see cref="Simulate"/> may take. Mirrors <c>MonsterAgent</c>'s
        /// cap and exists for a related reason: a host that hitches must not let the dark
        /// fill in one frame, because a silence a player never saw coming is a bug however
        /// correct the arithmetic was.
        /// </summary>
        private const int MaxStepsPerSimulate = 8;

        [Header("Who is exposed")]
        [SerializeField]
        [Tooltip("Left empty, every PresenceSubject in the scene is found at Awake.")]
        private List<PresenceSubject> _subjects = new List<PresenceSubject>();

        [Header("The monster")]
        [SerializeField]
        [Tooltip("Left empty, the scene's MonsterAgent is found. No monster means no radius cleared of 그늘.")]
        private Transform? _monster;

        [Header("Simulation")]
        [SerializeField]
        [Tooltip("Steps itself from FixedUpdate. Turn off when a host loop calls Simulate().")]
        private bool _selfDriven = true;

        private PresenceField? _field;
        private double _accumulator;
        private double _matchElapsedSeconds;
        private bool _clockDriven;

        /// <summary>The field being driven, or null before <see cref="Initialize"/>.</summary>
        public PresenceField? Field => _field;

        /// <summary>Players the 그늘 is charging, in the order they were registered.</summary>
        public IReadOnlyList<PresenceSubject> Subjects => _subjects;

        /// <summary>
        /// Raised once per taking, on the step it happens. The session layer wires this to
        /// the two things §03 makes the game out of.
        /// <para>
        /// <b>Wiring note, because it is one line and the whole toll depends on it.</b>
        /// §13's voice gate must be
        /// <c>PlayerState.MayTransmitVoice &amp;&amp; PresenceState.MayTransmitVoice</c> — two
        /// independent reasons to be silent, one channel, and neither may overwrite the
        /// other. §09 owns the first (a ghost is silent permanently) and this owns the
        /// second (a taken player is silent for one sprint). §03's misread condition adds
        /// <c>PresenceState.RecallSmear01</c> the same way.
        /// </para>
        /// </summary>
        public event Action<PresenceToll>? Taken;

        /// <summary>§07's clock, in seconds. The 그늘 reads its boldness from the tier this lands in.</summary>
        public float MatchElapsedSeconds => (float)_matchElapsedSeconds;

        /// <summary>
        /// Builds the field for a match.
        /// </summary>
        /// <param name="playerCount">Ignored when subjects are already registered — the subject list is the truth.</param>
        public void Initialize(int playerCount = 0)
        {
            if (_subjects.Count == 0)
            {
                _subjects.AddRange(
                    UnityEngine.Object.FindObjectsByType<PresenceSubject>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None));
            }

            for (var i = 0; i < _subjects.Count; i++)
            {
                _subjects[i].PlayerIndex = i;
                _subjects[i].RefreshSceneLights();
            }

            var count = Mathf.Max(1, Mathf.Max(playerCount, _subjects.Count));
            _field = new PresenceField(count);

            if (_monster == null)
            {
                var agent = UnityEngine.Object.FindFirstObjectByType<MonsterAgent>();
                _monster = agent != null ? agent.transform : null;
            }

            _accumulator = 0d;
        }

        /// <summary>
        /// Sets §07's clock from the host's match clock. Once called, the director stops
        /// advancing its own — the same handover <c>MonsterAgent</c> uses, and for the same
        /// reason: two clocks that both look right drift, and §07's tier boundaries are
        /// where every escalation in the game lives.
        /// </summary>
        public void SetMatchElapsed(float seconds)
        {
            _matchElapsedSeconds = seconds;
            _clockDriven = true;
        }

        /// <summary>
        /// Advances the 그늘 by <paramref name="deltaSeconds"/> of wall clock, in whole
        /// fixed steps. Call this from a host loop when <c>_selfDriven</c> is off.
        /// </summary>
        public void Simulate(float deltaSeconds)
        {
            if (_field == null || float.IsNaN(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            _accumulator += deltaSeconds;
            if (!_clockDriven)
            {
                _matchElapsedSeconds += deltaSeconds;
            }

            var steps = 0;
            while (_accumulator >= GameConstants.FixedStep && steps < MaxStepsPerSimulate)
            {
                _accumulator -= GameConstants.FixedStep;
                steps++;
                Step(GameConstants.FixedStep);
            }

            if (steps >= MaxStepsPerSimulate)
            {
                // Drop the backlog rather than working through it next frame. A hitch is
                // time the player did not experience, and charging them for it is the one
                // way this mechanic can feel unfair without being wrong.
                _accumulator = 0d;
            }
        }

        /// <summary>One player's current exposure, or null before <see cref="Initialize"/>.</summary>
        public PresenceState? StateOf(int playerIndex)
        {
            if (_field == null || playerIndex < 0 || playerIndex >= _field.Players.Count)
            {
                return null;
            }

            return _field.StateOf(playerIndex);
        }

        /// <summary>
        /// Metres from a player to the monster, or <see cref="float.PositiveInfinity"/> when
        /// there is no monster in the scene.
        /// <para>
        /// Straight-line, not navigable. §06 uses path length because a chase is a walk;
        /// this is not a walk, and the rule it feeds — "there is no 그늘 where the monster
        /// can see you" — is about a sight line, which is straight by definition.
        /// </para>
        /// </summary>
        public float DistanceToMonster(PresenceSubject subject)
        {
            if (_monster == null || subject == null)
            {
                return float.PositiveInfinity;
            }

            return Vector3.Distance(subject.SamplePoint, _monster.position);
        }

        private void Awake()
        {
            if (_field == null)
            {
                Initialize();
            }
        }

        private void FixedUpdate()
        {
            if (_selfDriven)
            {
                Simulate(Time.fixedDeltaTime);
            }
        }

        private void Step(float deltaSeconds)
        {
            if (_field == null)
            {
                return;
            }

            var elapsed = (float)_matchElapsedSeconds;

            for (var i = 0; i < _subjects.Count; i++)
            {
                var subject = _subjects[i];
                if (subject == null)
                {
                    continue;
                }

                _field.Tick(deltaSeconds, elapsed, subject.ToInput(DistanceToMonster(subject)));
            }

            while (_field.TryTakeToll(out var toll))
            {
                Taken?.Invoke(toll);
            }
        }
    }
}
