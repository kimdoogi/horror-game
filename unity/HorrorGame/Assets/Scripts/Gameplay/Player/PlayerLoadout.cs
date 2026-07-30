#nullable enable

using HorrorGame.Core.Economy;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// Holds one player's <see cref="Inventory"/> and hands movement the three primitives
    /// it is allowed to see.
    /// <para>
    /// The rules live in <see cref="Inventory"/> and <c>CarryLoad</c>; this class adds
    /// none. It exists because a <c>MonoBehaviour</c> is the only thing a prefab, a scene
    /// and the Mirror layer can all point at, and because the alternative — every system
    /// reaching into a shared inventory — is how the seam in ARCHITECTURE §3 gets lost.
    /// </para>
    /// <para>
    /// §13 makes the host authoritative, and this is a place where that is easy to get
    /// wrong in the harmless direction: a client's copy of its own inventory is fine to
    /// hold and display, because §08's loot is not a secret. What is secret is clue
    /// contents and the objective's <em>location</em>, and neither of those is here.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-80)]
    public sealed class PlayerLoadout : MonoBehaviour, IPlayerLoad
    {
        private readonly Inventory _inventory = new Inventory();

        [Tooltip("Debug only: carrying §03's objective. In a match this follows MatchState, not the inspector.")]
        [SerializeField]
        private bool _carryingObjective;

        [Tooltip("Debug only: §08's 대형 전리품 in both hands.")]
        [SerializeField]
        private bool _carryingOversizePiece;

        /// <summary>
        /// This player's loot, weight and capacity. The rules object, not a copy — callers
        /// mutate it through its own API so §08's capacity and weight checks cannot be
        /// bypassed.
        /// </summary>
        public Inventory Inventory
        {
            get { return _inventory; }
        }

        /// <inheritdoc />
        public float SpeedMultiplier
        {
            // Includes §08's bag; see IPlayerLoad's remarks on why nobody may apply it again.
            get { return _inventory.SpeedMultiplier; }
        }

        /// <inheritdoc />
        public bool CanSprint
        {
            get { return _inventory.CanSprint; }
        }

        /// <inheritdoc />
        public bool CarryingObjective
        {
            get { return _carryingObjective || _inventory.CarryingObjective; }
        }

        /// <inheritdoc />
        public bool CarryingOversizePiece
        {
            get { return _carryingOversizePiece; }
        }

        /// <summary>
        /// Picks up or puts down §03's objective, keeping the inventory's weight in step.
        /// <para>
        /// Routed through <see cref="Inventory.TryPickUpObjective"/> rather than set as a
        /// flag, because §03 forbids carrying loot at the same time ("전리품 동시 소지
        /// 불가") and that refusal belongs to the economy.
        /// </para>
        /// </summary>
        /// <param name="carrying">True to take it up.</param>
        /// <returns>False when the economy refused the pick-up.</returns>
        public bool SetCarryingObjective(bool carrying)
        {
            if (!carrying)
            {
                _inventory.PutDownObjective();
                _carryingObjective = false;
                return true;
            }

            _carryingObjective = _inventory.TryPickUpObjective();
            return _carryingObjective;
        }

        /// <summary>Sets the §08 대형 전리품 pose flag. Carries no speed term of its own — §08 charges it as weight.</summary>
        /// <param name="carrying">True while the piece is in both hands.</param>
        public void SetCarryingOversizePiece(bool carrying)
        {
            _carryingOversizePiece = carrying;
        }
    }
}
