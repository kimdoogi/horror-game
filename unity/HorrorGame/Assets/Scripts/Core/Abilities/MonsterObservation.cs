using HorrorGame.Core.Math;

namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// Everything the §04 abilities are allowed to know about the monster this tick.
    /// <para>
    /// The abilities and the monster brain are written independently, so they couple
    /// through this struct instead of through each other's types: the brain fills it
    /// from its own state machine, the abilities read it and never reach back. The
    /// one field that matters most is <see cref="IsSilent"/> — the brain maps §06's
    /// 정지 row onto it, and that single bool is what takes the Listener's feed away
    /// without either system knowing the other exists.
    /// </para>
    /// <para>
    /// It is a snapshot of the truth. Producing the *player-visible* version of that
    /// truth — an estimate with the right amount of error in it — is the ability's
    /// job, which is why nothing here is pre-fuzzed.
    /// </para>
    /// </summary>
    public readonly struct MonsterObservation
    {
        /// <summary>Value of <see cref="TargetPlayerIndex"/> meaning the monster is hunting nobody in particular.</summary>
        public const int NoTarget = -1;

        /// <summary>Where the monster actually is.</summary>
        public readonly Vec3 Position;

        /// <summary>Its current velocity, m/s. The Listener reads 이동 방향 from this (§04).</summary>
        public readonly Vec3 Velocity;

        /// <summary>
        /// Where it is looking, as a unit vector. The Observer's ability is
        /// "괴물의 시야를 본다" (§04), so this is the gaze the HUD draws.
        /// </summary>
        public readonly Vec3 Facing;

        /// <summary>
        /// True when the monster is making no sound. §06 gives exactly one state a
        /// 소리 column of 없음 — 정지 — and calls it the game's weapon, because it is
        /// what makes the Listener lose the monster mid-sentence.
        /// </summary>
        public readonly bool IsSilent;

        /// <summary>
        /// Index of the player it is hunting, or <see cref="NoTarget"/>. This is the
        /// answer the Observer exists to hand the team (§04: 누가 표적인지), and §11
        /// notes it is the only information in the game that cannot be bought.
        /// </summary>
        public readonly int TargetPlayerIndex;

        /// <summary>Builds a snapshot. The monster brain is the only intended caller.</summary>
        public MonsterObservation(Vec3 position, Vec3 velocity, Vec3 facing, bool isSilent, int targetPlayerIndex)
        {
            Position = position;
            Velocity = velocity;
            Facing = facing;
            IsSilent = isSilent;
            TargetPlayerIndex = targetPlayerIndex;
        }

        /// <summary>Horizontal speed, m/s. Height changes on a stairwell are not travel.</summary>
        public float SpeedFlat
        {
            get { return Velocity.MagnitudeFlat; }
        }

        /// <summary>
        /// A monster in any audible state — 순찰 · 경계 · 추격 · 수색 (§06). Facing is
        /// taken from the direction of travel, which is what those four states do.
        /// </summary>
        public static MonsterObservation Moving(Vec3 position, Vec3 velocity, int targetPlayerIndex)
        {
            return new MonsterObservation(position, velocity, velocity.NormalizedFlat, false, targetPlayerIndex);
        }

        /// <summary>
        /// A monster in 정지 (§06): stopped, listening, and making no sound. The
        /// Listener must return nothing at all for this — it is the state the design
        /// built to break the Listener, not a case to compensate for.
        /// </summary>
        public static MonsterObservation Standstill(Vec3 position, Vec3 facing, int targetPlayerIndex)
        {
            return new MonsterObservation(position, Vec3.Zero, facing, true, targetPlayerIndex);
        }
    }
}
