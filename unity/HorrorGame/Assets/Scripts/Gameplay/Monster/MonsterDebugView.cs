#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Math;
using HorrorGame.Core.Monster;
using UnityEngine;

namespace HorrorGame.Gameplay.Monster
{
    /// <summary>
    /// Draws why the monster did what it did.
    /// <para>
    /// §06's behaviour is four timers and two distances, and every complaint a designer
    /// will ever have about it — "it gave up too early", "it should not have seen me",
    /// "why is it walking away" — is one of them. Without this, answering any of those
    /// means reading the brain's private fields in a debugger while the match is
    /// running; with it, the answer is on screen: the state, its elapsed time, who it
    /// is chasing, where it last saw them, and how far the two release conditions have
    /// got.
    /// </para>
    /// <para>
    /// The release pair is drawn together on purpose. §06 requires 거리 12m 이상 <b>and</b>
    /// 시야 차단 3초 유지, and the single most common misreading of the design is that
    /// either one alone is enough — so both bars are always visible, and neither
    /// completing on its own does anything.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MonsterAgent))]
    public sealed class MonsterDebugView : MonoBehaviour
    {
        /// <summary>Sight cone segments. Resolution of a drawing, not a rule.</summary>
        private const int ConeSegments = 24;

        /// <summary>
        /// Height the text readout floats at, metres. Clear of a 2.336 m creature
        /// (ASSETS.md §3.1) — a drawing offset, not a tuned value.
        /// </summary>
        private const float LabelHeightMetres = 2.6f;

        [SerializeField]
        [Tooltip("Draw without selecting the monster.")]
        private bool _alwaysDraw;

        [SerializeField]
        [Tooltip("§06's sight range and half-angle.")]
        private bool _drawSight = true;

        [SerializeField]
        [Tooltip("Chase target, last seen position, and the two release conditions.")]
        private bool _drawAggro = true;

        [SerializeField]
        [Tooltip("Current destination and the search radius.")]
        private bool _drawNavigation = true;

        [SerializeField]
        [Tooltip("State, timers and the standstill countdown, as text.")]
        private bool _drawReadout = true;

        private MonsterAgent? _agent;

#if UNITY_EDITOR
        private readonly System.Text.StringBuilder _readout = new System.Text.StringBuilder();
#endif

        private void OnDrawGizmos()
        {
            if (_alwaysDraw)
            {
                Draw();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!_alwaysDraw)
            {
                Draw();
            }
        }

        private void Draw()
        {
            if (_agent == null)
            {
                _agent = GetComponent<MonsterAgent>();
            }

            var agent = _agent;
            if (agent == null)
            {
                return;
            }

            var brain = agent.Brain;
            if (brain == null)
            {
                // Edit mode, or before the host initialised it. Draw the ranges anyway
                // so the sight cone can be checked against level geometry without
                // entering play mode.
                if (_drawSight)
                {
                    DrawSightCone(transform.position, transform.forward);
                }

                return;
            }

            var position = brain.Position.ToVector3();
            var facing = brain.Facing.ToVector3();

            Gizmos.color = ColourOf(brain.State, brain.IsStunned);
            Gizmos.DrawWireSphere(position, GameConstants.MonsterWaypointTolerance);
            Gizmos.DrawLine(position, position + facing);

            if (_drawSight)
            {
                DrawSightCone(position, facing);
            }

            if (_drawNavigation)
            {
                DrawNavigation(brain, position);
            }

            if (_drawAggro)
            {
                DrawAggro(agent, brain, position);
            }

            if (_drawReadout)
            {
                DrawReadout(agent, brain, position);
            }
        }

        private static void DrawSightCone(Vector3 position, Vector3 facing)
        {
            // §06's cone is ±90°, which is a half-disc rather than the narrow wedge
            // people picture. Drawing it to scale is the fastest way to see that
            // "I was behind it" means genuinely behind.
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);

            var flat = new Vector3(facing.x, 0f, facing.z);
            if (flat.sqrMagnitude <= 0f)
            {
                flat = Vector3.forward;
            }

            flat.Normalize();

            var range = GameConstants.MonsterSightRange;
            var half = GameConstants.MonsterSightHalfAngle;
            var previous = position + (Quaternion.Euler(0f, -half, 0f) * flat * range);
            Gizmos.DrawLine(position, previous);

            for (var i = 1; i <= ConeSegments; i++)
            {
                var angle = -half + (2f * half * i / ConeSegments);
                var point = position + (Quaternion.Euler(0f, angle, 0f) * flat * range);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }

            Gizmos.DrawLine(position, previous);
        }

        private void DrawNavigation(MonsterBrain brain, Vector3 position)
        {
            var destination = brain.Destination;
            if (destination.HasValue)
            {
                var point = destination.Value.ToVector3();
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(position, point);
                Gizmos.DrawWireCube(point, Vector3.one * GameConstants.MonsterWaypointTolerance * 4f);
            }

            if (brain.State == MonsterStateId.Search && brain.LastSeenPosition.HasValue)
            {
                // §06: 마지막 위치 반경을 뒤짐. Seeing the radius is what makes "run past
                // it and double back" legible as a real option rather than a rumour.
                Gizmos.color = new Color(1f, 0.6f, 0f, 0.6f);
                DrawCircle(brain.LastSeenPosition.Value.ToVector3(), GameConstants.SearchRadius);
            }
        }

        private void DrawAggro(MonsterAgent agent, MonsterBrain brain, Vector3 position)
        {
            var lastSeen = brain.LastSeenPosition;
            if (lastSeen.HasValue)
            {
                // Kept across states on purpose: it is the monster's memory, and §06
                // sends it here when aggro breaks — which is why breaking aggro next to
                // the team is "괴물을 팀에 배달하는 것".
                var point = lastSeen.Value.ToVector3();
                Gizmos.color = new Color(1f, 0.4f, 0f, 1f);
                Gizmos.DrawWireSphere(point, GameConstants.MonsterWaypointTolerance * 8f);
                Gizmos.DrawLine(position, point);
            }

            if (brain.ChaseTargetId == MonsterBrain.NoTarget)
            {
                return;
            }

            // The release ring. §06's 12 m is a flat, straight distance — not a walked
            // one — so it is drawn as a circle and not as a path.
            Gizmos.color = Color.red;
            DrawCircle(position, GameConstants.AggroReleaseDistance);

            var targets = agent.Targets;
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i].Id != brain.ChaseTargetId)
                {
                    continue;
                }

                var target = targets[i].Position.ToVector3();
                Gizmos.color = Color.red;
                Gizmos.DrawLine(position, target);
                Gizmos.DrawWireSphere(target, GameConstants.MonsterWaypointTolerance * 8f);
                break;
            }
        }

        private void DrawReadout(MonsterAgent agent, MonsterBrain brain, Vector3 position)
        {
#if UNITY_EDITOR
            _readout.Clear();
            _readout.Append(brain.State.ToString());
            if (brain.IsStunned)
            {
                _readout.Append("  [STUN ").Append(brain.StunSecondsRemaining.ToString("0.00")).Append("s]");
            }

            if (!brain.IsAudible)
            {
                // The whole point of 정지, called out in the readout because on screen
                // it is indistinguishable from a monster that simply stopped.
                _readout.Append("  [SILENT]");
            }

            _readout.Append('\n');
            _readout.Append("t=").Append(brain.StateElapsedSeconds.ToString("0.00")).Append("s");
            _readout.Append("  of ").Append(GiveUpSecondsFor(brain.State).ToString("0.##")).Append("s\n");

            _readout.Append("tier ").Append(agent.Tier.Phase.ToString())
                .Append("  v=").Append(agent.Tier.MonsterSpeed.ToString("0.0")).Append("m/s")
                .Append("  zones=").Append(agent.Tier.PatrolZoneCount)
                .Append("  standstill=").Append(agent.Tier.StandstillChance.ToString("0.00")).Append('\n');

            if (brain.State == MonsterStateId.Patrol)
            {
                _readout.Append("next standstill in ")
                    .Append(brain.SecondsUntilStandstill.ToString("0.0")).Append("s\n");
            }

            if (brain.ChaseTargetId != MonsterBrain.NoTarget)
            {
                // Both release conditions, always together. §06 needs both.
                var cover = brain.LineOfSightBrokenSeconds;
                _readout.Append("target #").Append(brain.ChaseTargetId).Append('\n');
                _readout.Append("cover ").Append(cover.ToString("0.00")).Append('/')
                    .Append(GameConstants.AggroReleaseLineOfSightBreak.ToString("0.#")).Append("s ")
                    .Append(Bar(cover / GameConstants.AggroReleaseLineOfSightBreak)).Append('\n');

                var separation = SeparationFrom(agent, brain);
                if (separation >= 0f)
                {
                    _readout.Append("gap   ").Append(separation.ToString("0.0")).Append('/')
                        .Append(GameConstants.AggroReleaseDistance.ToString("0.#")).Append("m ")
                        .Append(Bar(separation / GameConstants.AggroReleaseDistance));
                }
            }

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(position + (Vector3.up * LabelHeightMetres), _readout.ToString());
#endif
        }

        private static float SeparationFrom(MonsterAgent agent, MonsterBrain brain)
        {
            var targets = agent.Targets;
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i].Id == brain.ChaseTargetId)
                {
                    return Vec3.DistanceFlat(brain.Position, targets[i].Position);
                }
            }

            return -1f;
        }

        private static string Bar(float fraction)
        {
            const int Width = 10;
            var filled = Mathf.Clamp(Mathf.RoundToInt(fraction * Width), 0, Width);
            return new string('#', filled) + new string('.', Width - filled);
        }

        /// <summary>
        /// The give-up window §06 measures <c>StateElapsedSeconds</c> against for a
        /// state, or 0 where the state has no timed exit.
        /// </summary>
        private static float GiveUpSecondsFor(MonsterStateId state)
        {
            switch (state)
            {
                case MonsterStateId.Alert:
                    return GameConstants.AlertGiveUpSeconds;
                case MonsterStateId.Search:
                    return GameConstants.SearchGiveUpSeconds;
                case MonsterStateId.Standstill:
                    return GameConstants.StandstillSeconds;
                default:
                    // 순찰 and 추격 both leave on a condition rather than a clock.
                    return 0f;
            }
        }

        private static void DrawCircle(Vector3 centre, float radius)
        {
            var previous = centre + (Vector3.forward * radius);
            for (var i = 1; i <= ConeSegments * 2; i++)
            {
                var angle = 360f * i / (ConeSegments * 2);
                var point = centre + (Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }

        private static Color ColourOf(MonsterStateId state, bool stunned)
        {
            if (stunned)
            {
                return Color.magenta;
            }

            switch (state)
            {
                case MonsterStateId.Patrol:
                    return Color.green;
                case MonsterStateId.Alert:
                    return Color.yellow;
                case MonsterStateId.Chase:
                    return Color.red;
                case MonsterStateId.Search:
                    return new Color(1f, 0.6f, 0f, 1f);

                // 정지 draws grey — the state where the Listener has nothing.
                default:
                    return Color.grey;
            }
        }
    }
}
