namespace HorrorGame.Core.Presence
{
    /// <summary>
    /// What the 그늘 took, handed out once per taking.
    /// <para>
    /// <b>It is not damage and there is nothing to heal.</b> §01 has exactly one thing
    /// that can end a player and it is the monster; a second source of death would make
    /// the dark a pursuer with no legs. What this takes is the two things §03 builds the
    /// game out of — the ability to say what you saw, and the certainty that you saw it.
    /// §03 forbids carrying a clue out of the building precisely so that both of those
    /// have to travel through a person: "그 자리에서 보고, 기억해서, 말로 전달해야
    /// 한다." The 그늘 charges for the dark in the currency §03 already made scarce.
    /// </para>
    /// <para>
    /// Both fields are durations and fractions rather than commands. The rules layer
    /// never reaches into the voice mixer or the misread model; it publishes what is
    /// owed and the session layer applies it, which is what keeps this file free of
    /// every other system.
    /// </para>
    /// </summary>
    public readonly struct PresenceToll
    {
        /// <summary>Which player was taken.</summary>
        public readonly int PlayerIndex;

        /// <summary>
        /// Seconds §13's voice channel stays shut.
        /// <see cref="GameConstants.PresenceSilenceSeconds"/> — one sprint (§06).
        /// </summary>
        public readonly float SilenceSeconds;

        /// <summary>
        /// Extra §03 misread-condition strength the player carries out of it, 0–1,
        /// fading over <see cref="GameConstants.PresenceRecallFadeSeconds"/>. It outlasts
        /// the silence deliberately: the voice comes back before the certainty does.
        /// </summary>
        public readonly float RecallSmear;

        /// <summary>
        /// How many times this player has been taken this match, counting this one.
        /// Telemetry only. §16 will want to know whether the 그늘 is a thing that
        /// happens once a match or once a minute before anybody tunes
        /// <see cref="GameConstants.PresenceSaturationSeconds"/>.
        /// </summary>
        public readonly int Ordinal;

        /// <summary>Builds a toll.</summary>
        public PresenceToll(int playerIndex, float silenceSeconds, float recallSmear, int ordinal)
        {
            PlayerIndex = playerIndex;
            SilenceSeconds = silenceSeconds;
            RecallSmear = recallSmear;
            Ordinal = ordinal;
        }
    }
}
