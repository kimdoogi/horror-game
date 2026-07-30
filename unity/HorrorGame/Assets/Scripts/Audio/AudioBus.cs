#nullable enable

namespace HorrorGame.Audio
{
    /// <summary>
    /// The mix families. One per authored audio folder, plus voice.
    /// <para>
    /// The split is not by loudness or by convenience — it is by <em>what a player
    /// is supposed to do with the sound</em>, because that is what decides how the
    /// bus is treated when the mix has to make room. §05 makes headphones mandatory
    /// on the grounds that 3D audio is the game, and §04 gives the 청음사 nothing but
    /// sound to work with, so two of these families carry information a player acts
    /// on (<see cref="Footsteps"/>, <see cref="Monster"/>) and the rest carry mood or
    /// feedback. When something has to be ducked, the informational buses are the
    /// last to move.
    /// </para>
    /// <para>
    /// Ordinals are stable and used as array indices by <see cref="GameAudio"/>.
    /// <see cref="Master"/> is index 0 and is the parent of every other bus.
    /// </para>
    /// </summary>
    public enum AudioBus
    {
        /// <summary>Everything. The player's single volume slider and the only bus that is not routed into another.</summary>
        Master = 0,

        /// <summary>
        /// §12's five-surface alphabet — the Listener's entire input. Loudest
        /// positional family in the project by design; see <see cref="AudioTuning"/>
        /// for the measured trim and why it is the largest one.
        /// </summary>
        Footsteps = 1,

        /// <summary>
        /// §06's growl · roar · search · grab · stun, plus the presence and breath
        /// beds. Information (where is it, is it chasing) and dread at once.
        /// </summary>
        Monster = 2,

        /// <summary>
        /// §12's zone beds, §07's five tension beds and the heartbeat. Non-diegetic
        /// and diegetic mood share a bus because they are ducked together: they are
        /// the only thing in the mix that may be pushed aside for a cue.
        /// </summary>
        Ambience = 3,

        /// <summary>Doors, flashlights, flares, traps, loot, the safe. §08 and §04's 정비공 hardware.</summary>
        Items = 4,

        /// <summary>Non-diegetic feedback — clue reads, shop, objective, match end. §03 / §08.</summary>
        Interface = 5,

        /// <summary>
        /// §13's 근접 음성, played from the speaker's character. Never ducked and
        /// never occluded as hard as the world, because §03's whole clue constraint
        /// — "기억해서 말로 전달해야 한다" — runs through this bus.
        /// </summary>
        Voice = 6,
    }
}
