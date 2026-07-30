#nullable enable

using HorrorGame.Core.Monster;
using HorrorGame.Gameplay.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Playtest
{
    /// <summary>
    /// Feeds one local player's position to the monster, so a single person can play
    /// the real map without a host, a lobby or a second machine.
    /// <para>
    /// In a real match this job belongs to the host (§13), which reports all four
    /// players each tick. That path needs Mirror running and two instances connected,
    /// which is the right way to test the game and the wrong way to answer "does the
    /// chase feel good" — §14 puts that question first and says it "직접 만져봐야
    /// 나온다". This component is the shortest path from the editor's Play button to
    /// that question.
    /// </para>
    /// <para>
    /// It is a playtest fixture, not a game system. Nothing shipped should reference
    /// it, and it deliberately does no perception work of its own: it reports the
    /// true position and lets §06's rules in Core decide what the monster knows.
    /// </para>
    /// </summary>
    [AddComponentMenu("Horror Game/Playtest/Solo Playtest Link")]
    public sealed class SoloPlaytestLink : MonoBehaviour
    {
        /// <summary>Identifier reported for the local player. Any stable value works with one player.</summary>
        private const int LocalPlayerId = 0;

        [SerializeField]
        [Tooltip("The monster to feed. Left empty, the first one in the scene is found.")]
        private MonsterAgent? _monster;

        [SerializeField]
        [Tooltip("The player to report. Left empty, this object's transform is used.")]
        private Transform? _player;

        [SerializeField]
        [Tooltip("Log the monster's state whenever it changes. The fastest way to see why it did something.")]
        private bool _logStateChanges = true;

        private MonsterStateId _lastState = MonsterStateId.Patrol;
        private bool _warned;

        private void Awake()
        {
            if (_player == null)
            {
                _player = transform;
            }

            if (_monster == null)
            {
                _monster = FindFirstObjectByType<MonsterAgent>();
            }
        }

        private void FixedUpdate()
        {
            if (_monster == null || _player == null)
            {
                WarnOnce("[Playtest] No MonsterAgent in the scene — the map is walkable but nothing is hunting.");
                return;
            }

            // The true position, every tick. §06's perception rules live in Core and
            // decide what the monster can actually see; hiding information here would
            // put a second, contradictory set of rules in the fixture.
            _monster.ReportTarget(LocalPlayerId, _player.position);

            if (!_logStateChanges)
            {
                return;
            }

            var state = _monster.State;
            if (state != _lastState)
            {
                Debug.Log($"[Playtest] monster {_lastState} → {state}"
                    + $"   distance {Vector3.Distance(_player.position, _monster.transform.position):0.0} m"
                    + $"   audible {_monster.IsAudible}");
                _lastState = state;
            }
        }

        private void WarnOnce(string message)
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
            Debug.LogWarning(message);
        }
    }
}
