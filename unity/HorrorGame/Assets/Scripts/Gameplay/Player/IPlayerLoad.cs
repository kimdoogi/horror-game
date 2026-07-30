#nullable enable

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Everything movement needs to know about what a player is carrying, expressed as
    /// primitives.
    /// <para>
    /// ARCHITECTURE §3 is explicit about this seam: "movement does not import the economy
    /// to learn about carry weight. The economy exposes a <c>float</c> multiplier;
    /// movement multiplies by a <c>float</c>." So <see cref="PlayerMotor"/> depends on
    /// this interface and never on <c>Inventory</c>, which means the economy can be
    /// rewritten, and the network layer can supply a replicated stand-in, without
    /// movement changing.
    /// </para>
    /// <para>
    /// <b>The one trap.</b> <see cref="SpeedMultiplier"/> is the value
    /// <c>CarryLoad.Resolve</c> produces, and that already folds §08's bag multiplier in.
    /// Implementations must not apply the bag a second time, and
    /// <see cref="PlayerMotor"/> deliberately leaves <c>MovementContext.BagEquipped</c>
    /// false for the same reason. Two −10% penalties where §08 wrote one is a 19% error
    /// that would look like a feel problem rather than a bug.
    /// </para>
    /// </summary>
    public interface IPlayerLoad
    {
        /// <summary>
        /// §08's carry-weight multiplier, 1 when unloaded, bag already included. Goes
        /// straight into <c>MovementContext.LoadMultiplier</c>.
        /// </summary>
        float SpeedMultiplier { get; }

        /// <summary>
        /// Whether this load still permits 질주 at all. §08 refuses it at weight ≥ 16;
        /// combined with §04 this is what decides whether Shift buys 5.6 or 4.5.
        /// </summary>
        bool CanSprint { get; }

        /// <summary>
        /// Carrying §03's objective. Costs a further ×0.80, forbids sprinting and takes
        /// the flashlight away — "양손을 쓴다".
        /// </summary>
        bool CarryingObjective { get; }

        /// <summary>
        /// Holding something too big to stow — §08's 대형 전리품, the two-person carry.
        /// Not a speed term of its own; it exists so §03's "legible at a glance" carry
        /// pose can be chosen correctly by <see cref="PlayerAnimatorDriver"/>.
        /// </summary>
        bool CarryingOversizePiece { get; }
    }
}
