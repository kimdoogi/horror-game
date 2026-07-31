#nullable enable

namespace HorrorGame.Gameplay.Guidance
{
    /// <summary>
    /// Where a player is in §01's loop, as far as the match state can actually tell.
    /// <para>
    /// §01 draws the loop as a cycle — 잠입 → 단서 → 전리품 → 귀환 → 판매 · 구매 →
    /// 다시 잠입 → … → 목표물 회수 → 탈출 — and a first-time tester has no way to see
    /// which arc of it they are on. This enum is that arc, and it is deliberately
    /// derived from live state every frame rather than advanced by a script: a tester
    /// who takes the objective before reading a single 표식, or who walks back up with
    /// empty pockets, must still be told something true. <see cref="MatchGuidance"/>
    /// resolves it by testing the most committing state first, so an out-of-order
    /// action wins over the step the loop would otherwise have expected.
    /// </para>
    /// </summary>
    public enum GuidancePhase
    {
        /// <summary>No match is being stepped. Nothing to say.</summary>
        Idle = 0,

        /// <summary>On the surface, never been down. §01's 맨몸으로 1차 잠입.</summary>
        Descend = 1,

        /// <summary>Underground, fewer than §03's <c>CluesRequiredToLocate</c> marks read.</summary>
        ReadClues = 2,

        /// <summary>Underground, the chain has converged — §03's 3차 잠입: 목표물 발견.</summary>
        FindObjective = 3,

        /// <summary>The objective is in both hands. §03's 운반: no flashlight, no loot.</summary>
        CarryObjectiveOut = 4,

        /// <summary>On the surface with 전리품 still in the pockets. §08's 판매.</summary>
        SellLoot = 5,

        /// <summary>On the surface, sold up, deciding what to buy before going back down. §08 · §10.</summary>
        ShopAndDescendAgain = 6,

        /// <summary>§02 has decided. The end screen is up.</summary>
        MatchOver = 7,
    }
}
