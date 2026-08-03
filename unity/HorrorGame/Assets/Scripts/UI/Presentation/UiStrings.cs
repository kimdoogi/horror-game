#nullable enable

namespace HorrorGame.UI
{
    /// <summary>
    /// Every word the interface says that is not written at the point of use.
    /// <para>
    /// <b>This file used to be the co-op game's whole vocabulary and almost all of it
    /// was deleted.</b> DESCENT-PIVOT §7 step 7 — 「상점/전리품/단서 제거」. Gone with
    /// their screens: §04's five 직업 names with their 능력 and 제약 rows, §11's
    /// 돈으로 메우기 substitutes and its 이 조합으로 끝낼 수 있는가 table, §07's
    /// 초저녁→동트기 전 night phases and their warnings, §08's thirteen 아이템 with
    /// their 효과 · 대가 · 분류 columns and the 구매 refusals, §03's 단서 layers,
    /// 층의 성질 vocabulary and read interruptions, and §02's five 결과 rows with
    /// their 조건 sentences. Every one of them named a system the race does not have.
    /// </para>
    /// <para>
    /// <b>Nothing replaced them, on purpose.</b> The race says four things — a
    /// standing, a storey, a clock and a verdict — and <c>RaceHud</c> writes all four
    /// at the point it draws them, because a race label is a number with a suffix and
    /// routing "B6" through a lookup table would be indirection for its own sake. A
    /// string only belongs here when two screens have to agree on it.
    /// </para>
    /// </summary>
    public static class UiStrings
    {
        /// <summary>
        /// Shown wherever a value exists but is not known yet — an empty lobby seat,
        /// a seed before the host has settled one.
        /// </summary>
        public const string Unknown = "—";

        // DELETED with §09's 신호: RattleFailure(GhostSignalFailure). It turned the
        // two ways a rattle could be refused into 「아직 흔들 수 없다」 and 「너무
        // 멀다」. There is no rattle, so there is no refusal to word.
    }
}
