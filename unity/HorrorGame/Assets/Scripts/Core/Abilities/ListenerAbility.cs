using System;
using HorrorGame.Core.Map;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// One acoustic fix on the monster: where the Listener believes it is, how far
    /// off that belief can be, and which floor it thinks it heard.
    /// <para>
    /// Nothing in here is the truth. §04 gives the Listener 위치 · 거리 · 이동 방향,
    /// and §12 makes the floor decide how good those three are, so the estimate and
    /// its error are the product — a caller that wants the monster's real position
    /// is holding the wrong object.
    /// </para>
    /// </summary>
    public readonly struct ListenerReading
    {
        /// <summary>Where the Listener believes the monster is.</summary>
        public readonly Vec3 EstimatedPosition;

        /// <summary>
        /// Distance from the Listener to <see cref="EstimatedPosition"/>, metres,
        /// measured horizontally so a stairwell does not read as extra range.
        /// </summary>
        public readonly float EstimatedDistance;

        /// <summary>
        /// Believed direction of travel as a unit vector on the XZ plane, or
        /// <see cref="Vec3.Zero"/> when none could be called. §05 expects this to be
        /// checked by turning the body — "몸을 돌려 삼각측량" — not trusted outright.
        /// </summary>
        public readonly Vec3 MovementDirection;

        /// <summary>False when the monster was too slow to give a bearing away.</summary>
        public readonly bool HasMovementDirection;

        /// <summary>
        /// Quality of the fix, 0 to 1. At the edge of hearing this equals the floor
        /// material's clarity exactly, which is what turns §12's material table into
        /// something the Listener can reason about out loud.
        /// </summary>
        public readonly float Precision01;

        /// <summary>Radius, metres, inside which the true position lies.</summary>
        public readonly float ErrorRadius;

        /// <summary>
        /// The floor material the Listener thinks it heard — sampled at
        /// <see cref="EstimatedPosition"/>, not at the monster. §12 asks for clear
        /// material boundaries; the consequence is that a bad fix near one names the
        /// wrong room, which is the confusion the section is designing for.
        /// </summary>
        public readonly FloorMaterial Floor;

        /// <summary>Zone containing <see cref="EstimatedPosition"/>, or -1 off the map. §07 talks in zones.</summary>
        public readonly int ZoneId;

        /// <summary>Builds a reading. Produced by <see cref="ListenerAbility"/>.</summary>
        public ListenerReading(
            Vec3 estimatedPosition,
            float estimatedDistance,
            Vec3 movementDirection,
            bool hasMovementDirection,
            float precision01,
            float errorRadius,
            FloorMaterial floor,
            int zoneId)
        {
            EstimatedPosition = estimatedPosition;
            EstimatedDistance = estimatedDistance;
            MovementDirection = movementDirection;
            HasMovementDirection = hasMovementDirection;
            Precision01 = precision01;
            ErrorRadius = errorRadius;
            Floor = floor;
            ZoneId = zoneId;
        }
    }

    /// <summary>
    /// 청음사 — localises the monster by sound. §04.
    /// <para>
    /// Two things switch this ability off, and both are load-bearing rather than
    /// incidental:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// §06's 정지. A stopped monster makes no sound, so there is nothing to localise
    /// and this returns nothing at all — not a stale fix, not a degraded one. That is
    /// the state the design built to break this role: "어디 갔어? 방금 여기 있었는데."
    /// </description></item>
    /// <item><description>
    /// §04's own constraint, "자기가 소리를 내면 못 듣는다". Running or opening a door
    /// puts the Listener above <see cref="GameConstants.ListenerSelfNoiseThreshold"/>
    /// and the feed cuts. §10 lists this as the role's price: 소리를 듣는다 / 자기가
    /// 조용해야 한다.
    /// </description></item>
    /// </list>
    /// <para>
    /// Accuracy comes from the floor under the monster (§12), which is what makes the
    /// material map a map rather than dressing: 금속 계단 gives the monster away
    /// completely, 콘크리트 barely at all. Sound is not blocked by geometry, so this
    /// never asks <see cref="IWorldProbe.HasLineOfSight"/> — hearing through a wall is
    /// the whole point of the role.
    /// </para>
    /// </summary>
    public sealed class ListenerAbility
    {
        private readonly IWorldProbe _probe;
        private readonly IRandomSource _random;

        private ListenerReading _reading;
        private bool _hasReading;
        private float _sinceFix;

        /// <summary>
        /// Creates the ability against the world it will query and the seeded stream
        /// its error is drawn from. §13 needs the error to replay identically from a
        /// seed, so it must not come from ambient randomness.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either seam is missing.</exception>
        public ListenerAbility(IWorldProbe probe, IRandomSource random)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            _probe = probe;
            _random = random;
            Failure = AbilityFailure.Inactive;

            // Start "overdue" so the very first audible tick produces a fix instead
            // of making the player wait out an interval they never heard begin.
            _sinceFix = GameConstants.ListenerFixIntervalSeconds;
        }

        /// <summary>True when <see cref="Reading"/> holds a fix from this tick's evaluation.</summary>
        public bool HasReading
        {
            get { return _hasReading; }
        }

        /// <summary>The current fix. Undefined unless <see cref="HasReading"/> is true.</summary>
        public ListenerReading Reading
        {
            get { return _reading; }
        }

        /// <summary>
        /// Why there is no fix: <see cref="AbilityFailure.MonsterSilent"/>,
        /// <see cref="AbilityFailure.SelfNoise"/>, <see cref="AbilityFailure.OutOfRange"/>,
        /// or <see cref="AbilityFailure.None"/> when there is one.
        /// </summary>
        public AbilityFailure Failure { get; private set; }

        /// <summary>
        /// True when the Listener is the reason it cannot hear. Worth its own flag
        /// because the HUD's fix differs: stop moving, rather than move closer.
        /// </summary>
        public bool IsSelfBlinded
        {
            get { return Failure == AbilityFailure.SelfNoise; }
        }

        /// <summary>Seconds until the next fix. Zero when one is due immediately.</summary>
        public float SecondsToNextFix
        {
            get
            {
                var remaining = GameConstants.ListenerFixIntervalSeconds - _sinceFix;
                return remaining > 0f ? remaining : 0f;
            }
        }

        /// <summary>
        /// Steps the ability. <paramref name="selfNoise"/> is the Listener's own
        /// noise on a 0~1 scale, supplied by whatever the player is doing: §04
        /// requires running and door-opening to land above
        /// <see cref="GameConstants.ListenerSelfNoiseThreshold"/>.
        /// </summary>
        /// <param name="deltaSeconds">Step length. Zero holds the previous fix; a frame spike costs at most one extra fix.</param>
        /// <param name="listenerPosition">Where the Listener is standing.</param>
        /// <param name="selfNoise">The Listener's own noise level, 0~1.</param>
        /// <param name="monster">This tick's monster snapshot.</param>
        public void Tick(float deltaSeconds, Vec3 listenerPosition, float selfNoise, in MonsterObservation monster)
        {
            var dt = deltaSeconds > 0f ? deltaSeconds : 0f;
            _sinceFix += dt;

            // §06: 정지 makes no sound, so this comes before anything about distance
            // or noise — there is nothing to be in range of.
            if (monster.IsSilent)
            {
                LoseFeed(AbilityFailure.MonsterSilent);
                return;
            }

            // §04: the Listener's own noise wins over everything it might have heard.
            if (selfNoise > GameConstants.ListenerSelfNoiseThreshold)
            {
                LoseFeed(AbilityFailure.SelfNoise);
                return;
            }

            var trueDistance = Vec3.DistanceFlat(listenerPosition, monster.Position);
            if (trueDistance > GameConstants.ListenerHearingRange)
            {
                LoseFeed(AbilityFailure.OutOfRange);
                return;
            }

            Failure = AbilityFailure.None;

            // Between footsteps the previous fix stands. Re-rolling the error every
            // tick would turn a position into a shimmer, and it would hide the
            // moment §06 cares about — the fix that stops arriving.
            if (_hasReading && _sinceFix < GameConstants.ListenerFixIntervalSeconds)
            {
                return;
            }

            _sinceFix = 0f;
            _reading = TakeFix(listenerPosition, monster, trueDistance);
            _hasReading = true;
        }

        /// <summary>
        /// Clears any fix and forgets the interval. Call when the Listener dies, when
        /// the team leaves the building (§03 resets the monster's state), or on a
        /// role swap.
        /// </summary>
        public void Reset()
        {
            _hasReading = false;
            _reading = default(ListenerReading);
            _sinceFix = GameConstants.ListenerFixIntervalSeconds;
            Failure = AbilityFailure.Inactive;
        }

        /// <summary>
        /// How much a floor material gives away, 0 to 1. §12's table in numbers:
        /// 금속 울림 tells all, 콘크리트 둔탁 tells least.
        /// <para>
        /// Public because §12 calls the material layout a systems decision — a map
        /// validator or an editor tool needs to be able to show what a zone's floor
        /// costs the Listener, using the same numbers the ability uses.
        /// </para>
        /// </summary>
        public static float ClarityOf(FloorMaterial material)
        {
            switch (material)
            {
                case FloorMaterial.Wood:
                    return GameConstants.ListenerClarityWood;
                case FloorMaterial.Tile:
                    return GameConstants.ListenerClarityTile;
                case FloorMaterial.Gravel:
                    return GameConstants.ListenerClarityGravel;
                case FloorMaterial.Concrete:
                    return GameConstants.ListenerClarityConcrete;
                case FloorMaterial.Metal:
                    return GameConstants.ListenerClarityMetal;
                default:
                    return GameConstants.ListenerClarityUnknown;
            }
        }

        /// <summary>
        /// Error radius, metres, for a monster that far away on that floor. Exposed
        /// so the HUD can size the uncertainty circle without taking a fix, and so
        /// §12's material choices can be compared without running a match.
        /// </summary>
        public static float ErrorRadiusFor(FloorMaterial material, float distance)
        {
            var clarity = MathX.Clamp01(ClarityOf(material));
            var distanceFactor = MathX.Clamp01(distance / GameConstants.ListenerHearingRange);
            var falloff = MathX.Lerp(GameConstants.ListenerNearErrorFactor, 1f, distanceFactor);
            return GameConstants.ListenerErrorRadiusMax * (1f - clarity) * falloff;
        }

        private ListenerReading TakeFix(Vec3 listenerPosition, in MonsterObservation monster, float trueDistance)
        {
            // Clarity comes from the floor the monster is actually standing on: that
            // is the sound being made. What gets *reported* is sampled at the
            // estimate instead, so a poor fix can name the neighbouring zone.
            var trueFloor = _probe.SampleFloor(monster.Position);
            var errorRadius = ErrorRadiusFor(trueFloor, trueDistance);
            var precision = MathX.Clamp01(1f - (errorRadius / GameConstants.ListenerErrorRadiusMax));

            // Draw the offset as a bearing plus a radius. The radius is not
            // area-uniform on purpose — a listener's mistakes cluster near the
            // truth rather than spreading evenly over the disc.
            var bearing = _random.NextFloat(0f, 360f);
            var offset = MathX.DirectionFromYaw(bearing) * (errorRadius * _random.NextFloat());
            var estimated = monster.Position + offset;

            var speed = monster.SpeedFlat;
            var hasDirection = speed >= GameConstants.ListenerMinDirectionSpeed;
            var direction = Vec3.Zero;
            if (hasDirection)
            {
                var trueYaw = MathX.YawOf(monster.Velocity.NormalizedFlat);
                var yawError = GameConstants.ListenerDirectionErrorMaxDegrees
                               * (1f - precision)
                               * _random.NextFloat(-1f, 1f);
                direction = MathX.DirectionFromYaw(MathX.NormalizeAngle(trueYaw + yawError));
            }

            return new ListenerReading(
                estimated,
                Vec3.DistanceFlat(listenerPosition, estimated),
                direction,
                hasDirection,
                precision,
                errorRadius,
                _probe.SampleFloor(estimated),
                _probe.ZoneIdAt(estimated));
        }

        private void LoseFeed(AbilityFailure why)
        {
            Failure = why;
            _hasReading = false;
            _reading = default(ListenerReading);

            // Leave the interval overdue: the instant the monster makes a sound
            // again — or the player stops running — the fix comes back immediately.
            // §04 cuts the feed while the noise lasts and not one moment longer.
            _sinceFix = GameConstants.ListenerFixIntervalSeconds;
        }
    }
}
