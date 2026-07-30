namespace HorrorGame.Core.Match
{
    /// <summary>
    /// How a match ended. §02.
    /// <para>
    /// The asymmetry between <see cref="Survived"/> and <see cref="Wiped"/> is the
    /// design's central lever: if even one player gets out, what the team learned
    /// that match is kept. A wipe erases loot, credits and knowledge alike. That
    /// gap is what turns "지금 나갈까?" into a real argument instead of an
    /// optimisation.
    /// </para>
    /// </summary>
    public enum MatchOutcome
    {
        /// <summary>Still playing.</summary>
        InProgress = 0,

        /// <summary>완전 승리 — objective recovered, all four out. §02.</summary>
        FullVictory = 1,

        /// <summary>부분 승리 — objective recovered, some escaped. §02.</summary>
        PartialVictory = 2,

        /// <summary>생존 — escaped without the objective. The match's information survives. §02.</summary>
        Survived = 3,

        /// <summary>패배 — wiped. Everything from that match is lost. §02.</summary>
        Wiped = 4,

        /// <summary>The host left, so the session ended. §13 does not migrate hosts.</summary>
        Abandoned = 5,
    }
}
