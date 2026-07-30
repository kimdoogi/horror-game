namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// The four kinds of 전리품 in §08's table.
    /// <para>
    /// §03 classes loot as the match's <em>currency</em> and as the only optional
    /// layer — a team can clear without a single piece. Everything about these
    /// four entries exists to make "안 챙겨도 클리어된다. 챙기면 강해진다" a real
    /// decision: each one trades weight, time or a role dependency for credits.
    /// </para>
    /// </summary>
    public enum LootId
    {
        /// <summary>Nothing. Returned by lookups that fail, so callers never get a silently valid piece.</summary>
        None = 0,

        /// <summary>은수저 · 잡동사니 — weight 1, low value, and §08's "눈에 잘 보임 (유혹)": the piece you pick up before you have decided to.</summary>
        Trinket = 1,

        /// <summary>회중시계 · 반지 — weight 1, mid value, §08's "효율 최고". The correct greedy choice, which is why it is the rarest sight.</summary>
        Timepiece = 2,

        /// <summary>금고 속 문서 — weight 2, high value, but locked behind a safe, so §08 makes it the Engineer's own income.</summary>
        SafeDocument = 3,

        /// <summary>대형 초상화 · 궤짝 — weight 5, very high value, "2인 운반 or 극심한 감속". §08 says outright it exists to create one scene.</summary>
        LargePiece = 4,
    }
}
