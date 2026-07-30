using HorrorGame.Core.Math;

namespace HorrorGame.Core.Monster
{
    /// <summary>
    /// A noise the monster might hear this tick — the only thing that opens §06's
    /// 소리 감지 → 경계 transition.
    /// <para>
    /// The cue carries its own <see cref="RangeMetres"/> rather than the brain
    /// holding one hearing radius, because the emitters are the ones that own those
    /// numbers: a footstep, a door, the Engineer's 소음 함정 and a 조명탄 (§08, "소리를
    /// 낸다") do not carry equally far. Giving the monster a single global radius
    /// would quietly re-tune every one of them from here.
    /// </para>
    /// </summary>
    public readonly struct MonsterSoundCue
    {
        /// <summary>Where the noise was made. 경계 walks to this point (§06).</summary>
        public readonly Vec3 Position;

        /// <summary>
        /// Relative strength in [0, 1]. Only used to pick a winner when several
        /// noises land on the same tick — the design gives no threshold, so any
        /// audible noise is enough to trigger 경계.
        /// </summary>
        public readonly float Loudness;

        /// <summary>
        /// How far the noise carries as a walked distance, metres. Measured along
        /// the navigable path rather than through walls, so a noise two rooms away
        /// pulls the monster the way §12's geometry says it must travel.
        /// </summary>
        public readonly float RangeMetres;

        /// <summary>Creates a cue. <paramref name="loudness"/> is only a tie-break, not a gate.</summary>
        public MonsterSoundCue(Vec3 position, float rangeMetres, float loudness = 1f)
        {
            Position = position;
            RangeMetres = rangeMetres;
            Loudness = loudness;
        }
    }
}
