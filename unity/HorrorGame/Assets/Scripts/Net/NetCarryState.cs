#nullable enable

using HorrorGame.Core.Match;

namespace HorrorGame.Net
{
    /// <summary>
    /// §05's 운반 상태 row, reduced to the four cases the other three players can
    /// actually see.
    /// <para>
    /// §05 asks for carry state because it drives "애니메이션 + 속도" — a teammate
    /// has to be able to tell, at a glance and across a dark corridor, whether the
    /// person ahead has their hands free. §03 makes that the last decision of the
    /// match: the objective takes both hands, so the carrier cannot hold a light and
    /// the escort has to.
    /// </para>
    /// <para>
    /// A byte enum rather than the <c>PlayerState</c> flags it is derived from,
    /// because ARCHITECTURE §3 couples systems through primitives: the animator
    /// needs one discriminator, not the economy's view of an inventory.
    /// </para>
    /// </summary>
    public enum NetCarryState : byte
    {
        /// <summary>Hands free. Can hold a flashlight (§03).</summary>
        Empty = 0,

        /// <summary>Carrying loot in a bag or pockets. Hands still free. §08.</summary>
        Loot = 1,

        /// <summary>
        /// A 대형품 in both hands. §08's oversize piece — no flashlight, and the
        /// same two-handed animation as the objective.
        /// </summary>
        OversizePiece = 2,

        /// <summary>
        /// The objective. §03: "양손을 쓴다 · 손전등을 들 수 없다 → 누군가 비춰줘야
        /// 한다." The most information-dense byte in the game.
        /// </summary>
        Objective = 3,
    }

    /// <summary>Maps the rules layer's carry flags onto the wire's one byte.</summary>
    public static class NetCarryStates
    {
        /// <summary>
        /// Collapses a <see cref="PlayerState"/> into the case a remote observer
        /// needs. Ordered by how much it constrains the player: the objective wins
        /// over an oversize piece because §03 forbids holding both.
        /// </summary>
        public static NetCarryState Of(PlayerState player)
        {
            if (player == null)
            {
                return NetCarryState.Empty;
            }

            if (player.IsCarryingObjective)
            {
                return NetCarryState.Objective;
            }

            if (player.OversizePieceInHands)
            {
                return NetCarryState.OversizePiece;
            }

            return player.HoldsLoot ? NetCarryState.Loot : NetCarryState.Empty;
        }

        /// <summary>
        /// Whether a player in this state can hold a light. §03's 운반 rule, restated
        /// on the wire so the remote avatar's flashlight is never drawn on a pair of
        /// full hands.
        /// </summary>
        public static bool CanHoldFlashlight(NetCarryState carry) =>
            carry == NetCarryState.Empty || carry == NetCarryState.Loot;
    }
}
