using System;

namespace HorrorGame.Core.Match
{
    /// <summary>
    /// How a match ended and what the team keeps. §02.
    /// <para>
    /// Not a win/lose flag, because §02 is not a win/lose table. Its whole argument
    /// is an asymmetry in what <i>survives</i>: "한 명이라도 탈출하면 그 판에서
    /// 알아낸 정보가 보존된다. 전멸하면 전리품 · 크레딧 · 정보가 전부 사라진다." A
    /// boolean would collapse 생존 into 패배 and delete the reason "지금 나갈까?" is
    /// an argument rather than an optimisation, so the spoils are separate fields the
    /// caller has to look at.
    /// </para>
    /// </summary>
    public readonly struct MatchResolution
    {
        /// <summary>Which row of §02 this is.</summary>
        public readonly MatchOutcome Outcome;

        /// <summary>Players who got out alive. §02's "한 명이라도".</summary>
        public readonly int PlayersEscaped;

        /// <summary>Players left as ghosts. §09 — they could not leave, whatever the team did.</summary>
        public readonly int PlayersLost;

        /// <summary>Players still alive and still in the building. While this is above zero, §02 has not happened yet.</summary>
        public readonly int PlayersStillInPlay;

        /// <summary>Whether the objective left the basement. §02's first column.</summary>
        public readonly bool ObjectiveRecovered;

        /// <summary>
        /// Whether what the team learned this match is kept — clue readings, the
        /// objective's location, the map knowledge that made the round trips
        /// shorter. §02 hangs this on a single escape.
        /// <para>
        /// Readable mid-match too, and worth reading: once it is true the match can no
        /// longer be a total loss, which is the exact state the team is arguing about
        /// when somebody says "지금 나갈까?".
        /// </para>
        /// </summary>
        public readonly bool InformationKept;

        /// <summary>Whether the 전리품 already hauled out counts. §02: lost entirely on a wipe.</summary>
        public readonly bool LootKept;

        /// <summary>Whether the 크레딧 count. §02 loses these on a wipe too, alongside the loot and the information.</summary>
        public readonly bool CreditsKept;

        /// <summary>Builds a resolution. Produced by <see cref="OutcomeEvaluator"/>.</summary>
        public MatchResolution(
            MatchOutcome outcome,
            int playersEscaped,
            int playersLost,
            int playersStillInPlay,
            bool objectiveRecovered,
            bool informationKept,
            bool lootKept,
            bool creditsKept)
        {
            Outcome = outcome;
            PlayersEscaped = playersEscaped;
            PlayersLost = playersLost;
            PlayersStillInPlay = playersStillInPlay;
            ObjectiveRecovered = objectiveRecovered;
            InformationKept = informationKept;
            LootKept = lootKept;
            CreditsKept = creditsKept;
        }

        /// <summary>Players accounted for. Should equal <see cref="GameConstants.PlayersPerMatch"/> in a real match (§11).</summary>
        public int PlayerCount
        {
            get { return PlayersEscaped + PlayersLost + PlayersStillInPlay; }
        }

        /// <summary>Whether the match is over. §02 cannot be read off a match that still has someone in it.</summary>
        public bool IsFinal
        {
            get { return Outcome != MatchOutcome.InProgress; }
        }

        /// <summary>
        /// Whether the whole party got out alive. §02's 완전 승리 needs this and the
        /// objective; on its own it is 생존.
        /// </summary>
        public bool EveryoneGotOut
        {
            get { return PlayersLost == 0 && PlayersStillInPlay == 0 && PlayersEscaped > 0; }
        }

        /// <summary>
        /// Whether the match left nothing behind — §02's 패배 row, "그 판의 모든
        /// 것을 잃는다". The one state the design measures every other against.
        /// </summary>
        public bool NothingSurvived
        {
            get { return !InformationKept && !LootKept && !CreditsKept; }
        }
    }

    /// <summary>
    /// §02's table, as a function of who is where.
    /// <para>
    /// A pure function of the tally on purpose. Two players resolving in the same
    /// tick — one reaching the vehicle while the last one dies — must not be able to
    /// produce different endings depending on which the host processed first, and
    /// the only way to guarantee that is to make the ending depend on counts and not
    /// on the order they were reached.
    /// </para>
    /// </summary>
    public static class OutcomeEvaluator
    {
        /// <summary>
        /// Reads §02 off the tally.
        /// <list type="bullet">
        /// <item><description>Anyone still in play → <see cref="MatchOutcome.InProgress"/>. §02 needs everybody accounted for.</description></item>
        /// <item><description>Nobody escaped → <see cref="MatchOutcome.Wiped"/>, and nothing is kept.</description></item>
        /// <item><description>Objective out and nobody lost → <see cref="MatchOutcome.FullVictory"/>.</description></item>
        /// <item><description>Objective out with losses → <see cref="MatchOutcome.PartialVictory"/>.</description></item>
        /// <item><description>Somebody out without it → <see cref="MatchOutcome.Survived"/>, and the information is kept.</description></item>
        /// </list>
        /// <para>
        /// The wipe check comes before the objective check deliberately. §02's 패배
        /// row carries no exception for a recovered objective — it says "그 판의 모든
        /// 것을 잃는다" — and physically the objective cannot leave without a carrier
        /// leaving, so the combination is defensive rather than reachable. If a caller
        /// reports it anyway, the wipe wins.
        /// </para>
        /// <para>
        /// 완전 승리 is expressed as "nobody was lost" rather than "four escaped", so
        /// that a short-handed party gets the same reading of §02's "전원 탈출" instead
        /// of being told it cannot fully win.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
        public static MatchResolution Evaluate(
            int playersEscaped, int playersLost, int playersStillInPlay, bool objectiveRecovered)
        {
            RequireNonNegative(playersEscaped, nameof(playersEscaped));
            RequireNonNegative(playersLost, nameof(playersLost));
            RequireNonNegative(playersStillInPlay, nameof(playersStillInPlay));

            var outcome = Classify(playersEscaped, playersLost, playersStillInPlay, objectiveRecovered);
            return Build(outcome, playersEscaped, playersLost, playersStillInPlay, objectiveRecovered);
        }

        /// <summary>
        /// The session ended without §02 deciding it — §13 keeps host authority and
        /// does not migrate hosts, so the host leaving ends the match.
        /// <para>
        /// The spoils still follow §02's rule and not the outcome name: anyone who had
        /// already walked out keeps what they walked out with. "한 명이라도 탈출하면"
        /// does not ask why the match stopped.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
        public static MatchResolution Abandon(
            int playersEscaped, int playersLost, int playersStillInPlay, bool objectiveRecovered)
        {
            RequireNonNegative(playersEscaped, nameof(playersEscaped));
            RequireNonNegative(playersLost, nameof(playersLost));
            RequireNonNegative(playersStillInPlay, nameof(playersStillInPlay));

            return Build(
                MatchOutcome.Abandoned, playersEscaped, playersLost, playersStillInPlay, objectiveRecovered);
        }

        private static MatchOutcome Classify(
            int playersEscaped, int playersLost, int playersStillInPlay, bool objectiveRecovered)
        {
            if (playersEscaped == 0 && playersLost == 0 && playersStillInPlay == 0)
            {
                // No players at all. Reporting 전멸 for an empty party would put a
                // 패배 in telemetry that nobody played, so this stays undecided.
                return MatchOutcome.InProgress;
            }

            if (playersStillInPlay > 0)
            {
                return MatchOutcome.InProgress;
            }

            if (playersEscaped == 0)
            {
                return MatchOutcome.Wiped;
            }

            if (objectiveRecovered)
            {
                return playersLost == 0 ? MatchOutcome.FullVictory : MatchOutcome.PartialVictory;
            }

            return MatchOutcome.Survived;
        }

        private static MatchResolution Build(
            MatchOutcome outcome, int playersEscaped, int playersLost, int playersStillInPlay, bool objectiveRecovered)
        {
            // §02 ties all three spoils to the same condition — someone got out — and
            // keeping them as three fields rather than one is what lets a later
            // design split them without rewriting every caller.
            var someoneGotOut = playersEscaped > 0;

            return new MatchResolution(
                outcome,
                playersEscaped,
                playersLost,
                playersStillInPlay,
                objectiveRecovered,
                someoneGotOut,
                someoneGotOut,
                someoneGotOut);
        }

        private static void RequireNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "A player count cannot be negative.");
            }
        }
    }
}
