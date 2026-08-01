namespace HorrorGame.Core.Presence
{
    /// <summary>
    /// What the 그늘 is doing to one player right now — the only presence state the
    /// player is ever shown, and the only one telemetry buckets.
    /// <para>
    /// The stage exists as its own type for the same reason
    /// <see cref="Threat.NightPhase"/> does: the underlying number is continuous and
    /// nothing outside the rules should reason about it. A HUD, a mixer or a mote
    /// system asking "how full is the pool" would be inventing its own thresholds, and
    /// the moment two of them disagreed the sound would tighten at a different instant
    /// from the one the shape resolves at. There is one threshold
    /// (<see cref="GameConstants.PresenceWarnPooling"/>) and it is here.
    /// </para>
    /// <para>
    /// Ordinals ascend with danger, so comparing two stages compares how far gone a
    /// player is. Do not reorder.
    /// </para>
    /// </summary>
    public enum PresenceStage
    {
        /// <summary>
        /// 걷힘 — nothing is pooling. Either the player is standing in light §03 could
        /// read by, or they are on §01's 지상, or the monster is close enough to have
        /// pushed the dark out (<see cref="GameConstants.PresenceMonsterClearRadius"/>).
        /// <para>
        /// The third of those is the interesting one, and it is why this row is not
        /// simply "safe": a player watching the 그늘 clear around them while holding no
        /// light has learnt something.
        /// </para>
        /// </summary>
        Clear = 0,

        /// <summary>
        /// 고임 — the dark is gathering and has not yet announced itself. Motes at the
        /// edge of the beam, a bed that was not there a minute ago. Nothing has been
        /// taken and nothing is about to be.
        /// </summary>
        Gathering = 1,

        /// <summary>
        /// 임박 — past <see cref="GameConstants.PresenceWarnPooling"/>. The warning, and
        /// it is a real one: 40% of <see cref="GameConstants.PresenceSaturationSeconds"/>
        /// remains, which §12's cover spacing guarantees is enough to walk to somewhere
        /// worth switching a light on.
        /// <para>
        /// This is the stage the 형상 stands up in. §01 makes the monster the thing you
        /// cannot beat; this is the thing you cannot look at.
        /// </para>
        /// </summary>
        Close = 2,

        /// <summary>
        /// 빼앗김 — the pool filled and the 그늘 took its toll. Lasts
        /// <see cref="GameConstants.PresenceSilenceSeconds"/>, during which §13's voice
        /// channel is shut and §03's certainty is smeared.
        /// </summary>
        Taken = 3,
    }
}
