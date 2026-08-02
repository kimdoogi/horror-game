using HorrorGame.Core.Math;

namespace HorrorGame.Core.Monster
{
    /// <summary>What a lunge did this tick.</summary>
    public enum LungeEvent
    {
        /// <summary>Nothing. Either out of range, not chasing, or mid-animation.</summary>
        None,

        /// <summary>It has just committed. Play the Grab clip and stop steering.</summary>
        Committed,

        /// <summary>The strike landed. The player is caught.</summary>
        Hit,

        /// <summary>The strike closed on nothing. The creature is in recovery.</summary>
        Missed,

        /// <summary>Recovery is over; it can chase and commit again.</summary>
        Recovered,
    }

    /// <summary>Where a lunge is in its own timeline.</summary>
    public enum LungeState
    {
        /// <summary>Chasing. It will commit as soon as the player is close enough.</summary>
        Ready,

        /// <summary>Committed and travelling. This cannot be cancelled.</summary>
        Committed,

        /// <summary>It missed and is paying for it.</summary>
        Recovering,
    }

    /// <summary>
    /// §06's catch, as an act instead of as geometry.
    /// <para>
    /// The catch used to be two capsules touching: the moment the monster's agent radius
    /// and the player's controller radius overlapped, the player died. Nothing about that
    /// is visible — there is no wind-up, no reach, no instant where a player knows it is
    /// happening — and the 1.37 s <c>Grab</c> clip the creature has been rigged with since
    /// the first pass only ever played over a body that was already dead.
    /// </para>
    /// <para>
    /// So: the creature <b>commits</b> at <see cref="GameConstants.MonsterAttackRange"/>,
    /// travels at <see cref="GameConstants.MonsterLungeSpeed"/> — faster than it chases,
    /// because a pounce is not a sprint — and the strike lands
    /// <see cref="GameConstants.MonsterAttackContactSeconds"/> later if the player is
    /// still inside <see cref="GameConstants.MonsterAttackReach"/>. A commit cannot be
    /// taken back, and a miss costs
    /// <see cref="GameConstants.MonsterAttackRecoverySeconds"/>.
    /// </para>
    /// <para>
    /// <b>The numbers decide the design, and GameConstants.Validate asserts it.</b> Over
    /// the 0.55 s of a commit the creature closes 1.375 m on a player running at §05's
    /// 4.5 m/s and 0.77 m on a 주자 sprinting at 5.6, against 1.0 m of gap to make up.
    /// So a running player is caught — §01's "이길 수 없는 존재" — and a sprinting Runner
    /// is not. §04's whole role becomes one moment, and §06's 0.8 m/s of margin is what
    /// buys it.
    /// </para>
    /// <para>
    /// Engine-free and a value type: the host owns one of these per player being chased,
    /// and the same code runs in the simulator.
    /// </para>
    /// </summary>
    public struct MonsterLunge
    {
        private float _elapsed;

        /// <summary>Where this lunge is in its own timeline.</summary>
        public LungeState State { get; private set; }

        /// <summary>Seconds since the current state began.</summary>
        public float Elapsed
        {
            get { return _elapsed; }
        }

        /// <summary>
        /// 0~1 through the strike, for whoever is driving the animation. Meaningless
        /// outside <see cref="LungeState.Committed"/> and returns 0 there.
        /// </summary>
        public float StrikeProgress
        {
            get
            {
                return State == LungeState.Committed
                    ? MathX.Clamp01(_elapsed / GameConstants.MonsterAttackContactSeconds)
                    : 0f;
            }
        }

        /// <summary>
        /// How fast the creature should be travelling right now. A commit overrides §07's
        /// tier speed — that is the whole difference between a chase and a pounce — and
        /// a recovery stops it dead, which is the player's reward for the sprint.
        /// </summary>
        /// <param name="chaseSpeed">What §07's tier would have it moving at.</param>
        public float SpeedNow(float chaseSpeed)
        {
            switch (State)
            {
                case LungeState.Committed:
                    return GameConstants.MonsterLungeSpeed;
                case LungeState.Recovering:
                    return 0f;
                default:
                    return chaseSpeed;
            }
        }

        /// <summary>
        /// Advances the lunge.
        /// </summary>
        /// <param name="deltaSeconds">Tick length. Non-positive does nothing.</param>
        /// <param name="chasing">§06 추격. Any other state cancels a commit outright.</param>
        /// <param name="distanceMetres">Flat distance to the player it is chasing.</param>
        /// <returns>What happened, for the host to act on.</returns>
        public LungeEvent Tick(float deltaSeconds, bool chasing, float distanceMetres)
        {
            if (deltaSeconds <= 0f)
            {
                return LungeEvent.None;
            }

            // Losing the target cancels everything, including a commit already in the
            // air. §06's four other states are not an attack, and a creature that
            // finished a lunge after being stunned would be killing people through a
            // §04 섬광수 that had already worked.
            if (!chasing)
            {
                State = LungeState.Ready;
                _elapsed = 0f;
                return LungeEvent.None;
            }

            _elapsed += deltaSeconds;

            switch (State)
            {
                case LungeState.Ready:
                    if (distanceMetres <= GameConstants.MonsterAttackRange)
                    {
                        State = LungeState.Committed;
                        _elapsed = 0f;
                        return LungeEvent.Committed;
                    }

                    return LungeEvent.None;

                case LungeState.Committed:
                    if (_elapsed < GameConstants.MonsterAttackContactSeconds)
                    {
                        return LungeEvent.None;
                    }

                    if (distanceMetres <= GameConstants.MonsterAttackReach)
                    {
                        State = LungeState.Ready;
                        _elapsed = 0f;
                        return LungeEvent.Hit;
                    }

                    State = LungeState.Recovering;
                    _elapsed = 0f;
                    return LungeEvent.Missed;

                default:
                    if (_elapsed < GameConstants.MonsterAttackRecoverySeconds)
                    {
                        return LungeEvent.None;
                    }

                    State = LungeState.Ready;
                    _elapsed = 0f;
                    return LungeEvent.Recovered;
            }
        }
    }
}
