namespace HorrorGame.Core.Monster
{
    /// <summary>
    /// The five states of §06's 상태 기계. Nothing else exists — a "stunned" or
    /// "attacking" state would add transitions the design never wrote, so the flash
    /// (§04) is modelled as a suspension of the current state instead (see
    /// <see cref="MonsterBrain.Stun"/>).
    /// </summary>
    public enum MonsterStateId
    {
        /// <summary>순찰. Walks a route, audible. §06.</summary>
        Patrol = 0,

        /// <summary>경계. Moves toward a heard sound, audible. §06.</summary>
        Alert = 1,

        /// <summary>추격. Drives at the target, audible and roaring. §06.</summary>
        Chase = 2,

        /// <summary>수색. Sweeps the radius around the last seen position, audible. §06.</summary>
        Search = 3,

        /// <summary>
        /// 정지. Stands and listens, and — the point of the state — makes
        /// <em>no sound</em>, so the Listener loses it. §06: "침묵이 가장 무서운 소리다."
        /// </summary>
        Standstill = 4,
    }
}
