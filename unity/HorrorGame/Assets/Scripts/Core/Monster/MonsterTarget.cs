using HorrorGame.Core.Math;

namespace HorrorGame.Core.Monster
{
    /// <summary>
    /// One thing the monster could chase, as the brain sees it.
    /// <para>
    /// A primitive rather than a player type on purpose (ARCHITECTURE §3): the
    /// monster must not import the role, movement or networking systems to learn
    /// where a player is. The host fills these in each tick from whatever it
    /// happens to have — a Mirror <c>NetworkBehaviour</c>, a simulator agent or a
    /// test fixture.
    /// </para>
    /// </summary>
    public readonly struct MonsterTarget
    {
        /// <summary>
        /// Stable identifier for the target across ticks. §06's release rule tracks
        /// one specific chase target, so the brain has to be able to recognise it
        /// again next tick without holding a reference to it.
        /// </summary>
        public readonly int Id;

        /// <summary>Where the target actually is, in world metres.</summary>
        public readonly Vec3 Position;

        /// <summary>
        /// True when the target cannot be seen however clear the sight line is —
        /// inside a locker, under a desk. §06 leaves ordinary roles "숨거나", and
        /// hiding is the host's business: the brain must not know what a locker is.
        /// </summary>
        public readonly bool IsConcealed;

        /// <summary>Creates a target reading for one tick.</summary>
        public MonsterTarget(int id, Vec3 position, bool isConcealed = false)
        {
            Id = id;
            Position = position;
            IsConcealed = isConcealed;
        }
    }
}
