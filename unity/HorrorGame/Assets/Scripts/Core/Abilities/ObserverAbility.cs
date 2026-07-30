using HorrorGame.Core.Math;

namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// 관측자 — reads the monster's vision to reveal who it is hunting. §04.
    /// <para>
    /// Activation is "괴물로부터 15m 이내 + 이동 정지 3초", and §05 settled the part
    /// that decides how the role feels: the 3 seconds pin the feet, not the head.
    /// "화면이 얼면 조작감 최악. 발이 묶인 채 둘러보는 게 더 무섭다." So mouselook is
    /// accepted here and deliberately excluded from the stillness test, while a single
    /// step cancels the read and the 3 seconds restart from zero rather than resuming.
    /// </para>
    /// <para>
    /// Two literal readings of §04 are worth stating, because both are choices:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// §04's 지속 clause names movement as the only thing that breaks the read, so the
    /// stillness timer keeps running while the monster is out of range. Standing still
    /// for four seconds and then having the monster walk into 15 m produces the reveal
    /// on that same tick — the Observer was already holding its breath.
    /// </description></item>
    /// <item><description>
    /// §04 gates on distance alone, with no sight line, so this reads through walls.
    /// §12's 관측 지점 (a second-floor railing, a window, a grate) therefore exist to
    /// make the read *survivable*, not possible — "없으면 관측자는 죽으러 가야 한다."
    /// </description></item>
    /// </list>
    /// <para>
    /// §11 notes this is the one role whose information cannot be bought, which is why
    /// it is also the one that has to be protected.
    /// </para>
    /// </summary>
    public sealed class ObserverAbility
    {
        private float _stillSeconds;

        /// <summary>Creates the ability. It needs no world seam — §04 gates on distance and stillness only.</summary>
        public ObserverAbility()
        {
            Failure = AbilityFailure.Inactive;
            RevealedTargetPlayerIndex = MonsterObservation.NoTarget;
        }

        /// <summary>
        /// Seconds of uninterrupted stillness, clamped to §04's requirement — holding
        /// longer earns nothing, so the value stops climbing once the read is live.
        /// </summary>
        public float StillSeconds
        {
            get { return _stillSeconds; }
        }

        /// <summary>Progress toward the 3 s window, 0 to 1. The HUD's ring.</summary>
        public float Progress01
        {
            get { return MathX.Clamp01(_stillSeconds / GameConstants.ObserverStillSeconds); }
        }

        /// <summary>True while the monster's vision is being read.</summary>
        public bool IsReading { get; private set; }

        /// <summary>
        /// Who the monster is hunting, or <see cref="MonsterObservation.NoTarget"/>.
        /// While <see cref="IsReading"/> is true, <see cref="MonsterObservation.NoTarget"/>
        /// is itself an answer — "아무도 안 보고 있어" is information the team can act on.
        /// </summary>
        public int RevealedTargetPlayerIndex { get; private set; }

        /// <summary>Compass yaw of the monster's gaze at the last read, degrees. Only meaningful while reading.</summary>
        public float MonsterGazeYawDegrees { get; private set; }

        /// <summary>
        /// The Observer's own camera yaw, recorded for the HUD so the gaze can be
        /// drawn relative to where the player is looking. It is never consulted by the
        /// stillness test — that is §05's decision made visible in the code.
        /// </summary>
        public float HeadYawDegrees { get; private set; }

        /// <summary>Horizontal distance to the monster at the last tick, metres.</summary>
        public float DistanceToMonster { get; private set; }

        /// <summary>
        /// Why there is no reveal: <see cref="AbilityFailure.Moving"/>,
        /// <see cref="AbilityFailure.NotStillLongEnough"/>,
        /// <see cref="AbilityFailure.OutOfRange"/>, or <see cref="AbilityFailure.None"/>
        /// while reading.
        /// </summary>
        public AbilityFailure Failure { get; private set; }

        /// <summary>
        /// Steps the ability.
        /// </summary>
        /// <param name="deltaSeconds">Step length. Zero neither advances nor cancels the window.</param>
        /// <param name="observerPosition">Where the Observer is standing.</param>
        /// <param name="translationSpeed">
        /// Ground speed in m/s. Anything above
        /// <see cref="GameConstants.ObserverStillSpeedThreshold"/> counts as a step and
        /// resets the window — §04: "움직이면 끊긴다."
        /// </param>
        /// <param name="headYawDegrees">Camera yaw. Recorded, never tested against. §05.</param>
        /// <param name="monster">This tick's monster snapshot.</param>
        public void Tick(
            float deltaSeconds,
            Vec3 observerPosition,
            float translationSpeed,
            float headYawDegrees,
            in MonsterObservation monster)
        {
            var dt = deltaSeconds > 0f ? deltaSeconds : 0f;

            HeadYawDegrees = headYawDegrees;
            DistanceToMonster = Vec3.DistanceFlat(observerPosition, monster.Position);

            if (translationSpeed > GameConstants.ObserverStillSpeedThreshold)
            {
                // §04: the window restarts from zero. It does not pause and resume —
                // one step costs the whole three seconds, which is what makes an
                // observation post worth walking to instead of shuffling in place.
                _stillSeconds = 0f;
                EndRead(AbilityFailure.Moving);
                return;
            }

            _stillSeconds += dt;
            if (_stillSeconds > GameConstants.ObserverStillSeconds)
            {
                _stillSeconds = GameConstants.ObserverStillSeconds;
            }

            if (DistanceToMonster > GameConstants.ObserverRange)
            {
                // Out of range suppresses the reveal but not the stillness: §04's
                // 지속 clause names movement as the only thing that breaks it.
                EndRead(AbilityFailure.OutOfRange);
                return;
            }

            if (_stillSeconds < GameConstants.ObserverStillSeconds)
            {
                EndRead(AbilityFailure.NotStillLongEnough);
                return;
            }

            IsReading = true;
            Failure = AbilityFailure.None;
            RevealedTargetPlayerIndex = monster.TargetPlayerIndex;
            MonsterGazeYawDegrees = MathX.YawOf(monster.Facing);
        }

        /// <summary>
        /// Drops the read and the accumulated stillness. Call on death, on leaving the
        /// building, or on a role swap.
        /// </summary>
        public void Reset()
        {
            _stillSeconds = 0f;
            IsReading = false;
            RevealedTargetPlayerIndex = MonsterObservation.NoTarget;
            MonsterGazeYawDegrees = 0f;
            DistanceToMonster = 0f;
            Failure = AbilityFailure.Inactive;
        }

        private void EndRead(AbilityFailure why)
        {
            IsReading = false;
            RevealedTargetPlayerIndex = MonsterObservation.NoTarget;
            Failure = why;
        }
    }
}
