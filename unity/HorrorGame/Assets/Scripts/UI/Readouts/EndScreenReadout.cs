#nullable enable

using HorrorGame.Core.Match;

namespace HorrorGame.UI.Readouts
{
    /// <summary>One thing the match either kept or lost. §02's spoils, a line at a time.</summary>
    public readonly struct SpoilLine
    {
        /// <summary>Builds a line.</summary>
        public SpoilLine(string label, bool kept, string note)
        {
            Label = label;
            Kept = kept;
            Note = note;
        }

        /// <summary>What this line is about — 목표물 · 정보 · 전리품 · 크레딧.</summary>
        public string Label { get; }

        /// <summary>Whether the team still has it.</summary>
        public bool Kept { get; }

        /// <summary>The design's own sentence about why, so the four endings read differently even line by line.</summary>
        public string Note { get; }
    }

    /// <summary>
    /// The ending. §02.
    /// <para>
    /// <b>The four endings must not be interchangeable.</b> §02 is not a win/lose
    /// table with flavour text — its argument is an asymmetry in what <em>survives</em>:
    /// <em>"한 명이라도 탈출하면 그 판에서 알아낸 정보가 보존된다. 전멸하면 전리품 ·
    /// 크레딧 · 정보가 전부 사라진다. 이 비대칭이 '지금 나갈까?'를 진짜 갈등으로
    /// 만든다."</em> The argument the team had at minute 22 only pays off if the
    /// screen at minute 31 shows a different ledger depending on how it went. So this
    /// readout leads with the ledger and not with a score, and 생존 and 패배 differ in
    /// three of four lines rather than in a word.
    /// </para>
    /// <para>
    /// <b>What the end screen may not show.</b> Not the objective's location, not the
    /// clue chain, not the marks anybody read. §03's constraint is that the only copy
    /// is in a player's memory, and a results screen that recapped the clues would be
    /// the clue log arriving one screen later — the team would learn to stop reading
    /// carefully and wait for the recap. <see cref="Information"/> therefore reports
    /// <em>that</em> the knowledge survived, never <em>what</em> it was.
    /// </para>
    /// <para>
    /// The three keep-flags are taken from <c>MatchResolution</c> rather than
    /// recomputed from the outcome. §02's rule is "did anybody get out", not "which
    /// row is this", and <c>OutcomeEvaluator</c> already applies it — including for
    /// <see cref="MatchOutcome.Abandoned"/>, where §13's host departure still leaves
    /// whoever had already walked out holding what they walked out with.
    /// </para>
    /// </summary>
    public readonly struct EndScreenReadout
    {
        /// <summary>Builds a readout. Prefer <see cref="From"/>.</summary>
        public EndScreenReadout(
            MatchResolution resolution,
            SpoilLine objective,
            SpoilLine information,
            SpoilLine loot,
            SpoilLine credits)
        {
            Resolution = resolution;
            Objective = objective;
            Information = information;
            Loot = loot;
            Credits = credits;
        }

        /// <summary>The core's reading of §02.</summary>
        public MatchResolution Resolution { get; }

        /// <summary>Whether the objective left the basement — §02's first column.</summary>
        public SpoilLine Objective { get; }

        /// <summary>Whether what the team learned survives. The line §02's whole asymmetry rests on.</summary>
        public SpoilLine Information { get; }

        /// <summary>Whether the 전리품 already hauled out counts.</summary>
        public SpoilLine Loot { get; }

        /// <summary>Whether the 크레딧 count.</summary>
        public SpoilLine Credits { get; }

        /// <summary>§02's 결과 column — the single word the screen leads with.</summary>
        public string Headline
        {
            get { return UiStrings.Outcome(Resolution.Outcome); }
        }

        /// <summary>§02's 조건 column — the sentence that separates this ending from the one above it.</summary>
        public string Condition
        {
            get { return UiStrings.OutcomeCondition(Resolution.Outcome); }
        }

        /// <summary>Players who walked out. §02's "한 명이라도".</summary>
        public int Escaped
        {
            get { return Resolution.PlayersEscaped; }
        }

        /// <summary>Players left as ghosts. §09 — they could not leave, whatever the team did.</summary>
        public int Lost
        {
            get { return Resolution.PlayersLost; }
        }

        /// <summary>§02's 패배 row and nothing else: "그 판의 모든 것을 잃는다".</summary>
        public bool IsTotalLoss
        {
            get { return Resolution.NothingSurvived; }
        }

        /// <summary>Whether the objective came out — the difference between §02's top two rows and its third.</summary>
        public bool IsVictory
        {
            get
            {
                return Resolution.Outcome == MatchOutcome.FullVictory
                    || Resolution.Outcome == MatchOutcome.PartialVictory;
            }
        }

        /// <summary>
        /// The one thing this ending is worth saying beyond the ledger. Different for
        /// every row of §02 on purpose — the screen a team sees after 부분 승리 has to
        /// feel unlike the one they see after 완전 승리, or the three people who did
        /// not make it were free.
        /// </summary>
        public string Epilogue
        {
            get
            {
                switch (Resolution.Outcome)
                {
                    case MatchOutcome.FullVictory:
                        return "목표물을 안고 넷 다 걸어 나왔다.";
                    case MatchOutcome.PartialVictory:
                        return "목표물은 나왔다. 전부는 아니었다.";
                    case MatchOutcome.Survived:
                        return "빈손으로 나왔지만, 알아낸 것은 남는다.";
                    case MatchOutcome.Wiped:
                        return "아무도 나오지 못했다. 남은 것은 없다.";
                    case MatchOutcome.Abandoned:
                        return "호스트가 나갔다. 이미 나와 있던 사람의 몫은 그대로다.";
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>Builds the ledger for a finished match.</summary>
        public static EndScreenReadout From(MatchResolution resolution)
        {
            return new EndScreenReadout(
                resolution,
                new SpoilLine(
                    "목표물",
                    resolution.ObjectiveRecovered,
                    resolution.ObjectiveRecovered ? "회수했다" : "그 자리에 남았다"),
                new SpoilLine(
                    "정보",
                    resolution.InformationKept,
                    resolution.InformationKept
                        ? "이번 판에서 알아낸 것은 남는다"
                        : "알아낸 것까지 전부 사라졌다"),
                new SpoilLine(
                    "전리품",
                    resolution.LootKept,
                    resolution.LootKept ? "실어낸 만큼 남는다" : "전부 잃었다"),
                new SpoilLine(
                    "크레딧",
                    resolution.CreditsKept,
                    resolution.CreditsKept ? "공용 지갑 그대로" : "공용 지갑까지 비었다"));
        }
    }
}
