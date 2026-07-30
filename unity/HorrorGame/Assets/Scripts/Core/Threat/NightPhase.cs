namespace HorrorGame.Core.Threat
{
    /// <summary>
    /// The 시각 column of §07's threat table.
    /// <para>
    /// The phase exists as its own type because it is the only part of the threat
    /// state players are ever told directly. §07 gates the clock behind going up
    /// or buying a 회중시계, and what a player then asks is *"지금 몇 시쯤이야?"* —
    /// a phase, not a stopwatch reading. Handing the UI a phase rather than a
    /// number also keeps the HUD from implying a precision the fiction does not
    /// have, and leaves the display string to the localisation layer.
    /// </para>
    /// <para>
    /// Ordinals are the tier order, so comparing two phases compares how late the
    /// night is. Do not reorder.
    /// </para>
    /// </summary>
    public enum NightPhase
    {
        /// <summary>초저녁 — 0–8 min. §07. The first descent happens here.</summary>
        EarlyEvening = 0,

        /// <summary>밤 — 8–16 min. §07.</summary>
        Night = 1,

        /// <summary>심야 — 16–24 min. §07. The tier §06 and §12 derive their numbers from.</summary>
        LateNight = 2,

        /// <summary>새벽 — 24–32 min. §07. §01 places the objective escort here.</summary>
        PreDawn = 3,

        /// <summary>동트기 전 — 32 min and after. §07: "생존 불가 수준". Never ends.</summary>
        BeforeSunrise = 4,
    }
}
