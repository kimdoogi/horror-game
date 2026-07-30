using System.Collections.Generic;

namespace HorrorGame.Core.Monster
{
    /// <summary>
    /// Everything outside the monster that one tick of <see cref="MonsterBrain"/>
    /// depends on, passed as primitives.
    /// <para>
    /// §07 makes speed, patrol scope and standstill frequency all functions of the
    /// clock, and ARCHITECTURE §3 forbids the monster from importing the threat
    /// system to find that out: <c>ThreatTier</c> exposes floats and ints, and this
    /// is where they arrive. The brain therefore has no opinion about what time it
    /// is, which is also what lets a test drive 새벽 behaviour in one line.
    /// </para>
    /// </summary>
    public readonly struct MonsterTickInput
    {
        /// <summary>
        /// Metres per second, from <c>ThreatTier.MonsterSpeed</c>. §07 runs it from
        /// 4.4 at 초저녁 to 5.2 at 동트기 전, so nothing in the brain may reach for
        /// <see cref="GameConstants.MonsterBaseSpeed"/> directly.
        /// </summary>
        public readonly float MoveSpeed;

        /// <summary>
        /// How many zones patrol may currently range over, from
        /// <c>ThreatTier.PatrolZoneCount</c>. §07: 1개 구역 → 2개 구역 → 절반 → 전체.
        /// Values below 1 are treated as 1 — the monster always patrols where it stands.
        /// </summary>
        public readonly int PatrolZoneCount;

        /// <summary>
        /// Probability that a scheduled standstill actually happens, from
        /// <c>ThreatTier.StandstillChance</c>. §07 thins standstills out as the
        /// night goes on (잦음 → 보통 → 드묾 → 없음), and 0 means the monster never
        /// stops — the 새벽 rows, where the game stops giving the Listener a break.
        /// </summary>
        public readonly float StandstillChance;

        /// <summary>
        /// Every target the host wants considered, with true positions. May be null
        /// or empty — all four players dead or hidden is a normal state, not an error.
        /// </summary>
        public readonly IReadOnlyList<MonsterTarget>? Targets;

        /// <summary>
        /// Noises made this tick. May be null or empty. Cues are consumed by the
        /// tick they are handed to; the brain never buffers them, so a host that
        /// drops a tick drops the noise with it.
        /// </summary>
        public readonly IReadOnlyList<MonsterSoundCue>? Sounds;

        /// <summary>Creates the per-tick input.</summary>
        public MonsterTickInput(
            float moveSpeed,
            int patrolZoneCount,
            float standstillChance,
            IReadOnlyList<MonsterTarget>? targets = null,
            IReadOnlyList<MonsterSoundCue>? sounds = null)
        {
            MoveSpeed = moveSpeed;
            PatrolZoneCount = patrolZoneCount;
            StandstillChance = standstillChance;
            Targets = targets;
            Sounds = sounds;
        }

        /// <summary>
        /// The 초저녁 row of §07 with nobody sensed: base speed, one patrol zone,
        /// standstills always taken. This is what <see cref="MonsterBrain.Tick(float)"/>
        /// steps with, and it is the right input for any test that only cares about
        /// the timers.
        /// </summary>
        public static MonsterTickInput Default =>
            new MonsterTickInput(GameConstants.MonsterBaseSpeed, 1, 1f);

        /// <summary>This input with a different target list, for hosts that rebuild it every tick.</summary>
        public MonsterTickInput WithTargets(IReadOnlyList<MonsterTarget>? targets) =>
            new MonsterTickInput(MoveSpeed, PatrolZoneCount, StandstillChance, targets, Sounds);

        /// <summary>This input with a different sound list.</summary>
        public MonsterTickInput WithSounds(IReadOnlyList<MonsterSoundCue>? sounds) =>
            new MonsterTickInput(MoveSpeed, PatrolZoneCount, StandstillChance, Targets, sounds);
    }
}
