using System;

namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// The team's credits. There is one of these per match, not one per player.
    /// <para>
    /// §08 is unusually blunt about why: <em>"공용 지갑이 핵심. 크레딧은 팀 공용이다.
    /// 그래서 매번 협상이 발생한다. … 돈이 개인 것이면 협동 게임이 아니게 된다."</em>
    /// The negotiation §08 quotes — "강화 손전등 하나 살까, 배터리 3개 살까?" — only
    /// exists because there is no second pocket to spend from.
    /// </para>
    /// <para>
    /// So this class has no player parameter anywhere, and no way to acquire one:
    /// no id, no index, no per-player dictionary, no split. That is a deliberate
    /// absence rather than an omission, and <c>EconomyTests</c> asserts it by
    /// reflection so a later "just for the UI" convenience cannot reintroduce
    /// private money by accident.
    /// </para>
    /// </summary>
    public sealed class Wallet
    {
        private int _credits;

        /// <summary>A wallet at §08's starting balance — zero. "1차 잠입 전 구매력 0 — 맨몸으로 들어간다."</summary>
        public Wallet()
            : this(GameConstants.WalletStartingCredits)
        {
        }

        /// <summary>
        /// A wallet seeded with a balance. Only the simulator should need this: it
        /// sweeps §16-2 by starting mid-curve instead of replaying every descent.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A negative opening balance — debt is not a mechanic in §08.</exception>
        public Wallet(int startingCredits)
        {
            if (startingCredits < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startingCredits), startingCredits, "§08 has no debt: a wallet cannot open below zero.");
            }

            _credits = startingCredits;
            TotalEarned = startingCredits;
        }

        /// <summary>Credits the team can spend right now. Never negative.</summary>
        public int Credits => _credits;

        /// <summary>Everything ever banked this match. Feeds <c>MatchSummary.CreditsEarned</c> for §16-2.</summary>
        public int TotalEarned { get; private set; }

        /// <summary>Everything ever spent this match. With <see cref="TotalEarned"/> this is how §08's growth curve gets measured in the wild.</summary>
        public int TotalSpent { get; private set; }

        /// <summary>Whether a price is payable. Zero is always affordable; browsing is free.</summary>
        public bool CanAfford(int cost) => cost <= _credits;

        /// <summary>
        /// Banks the proceeds of a sale. §08: "전리품을 차량에 실으면 → 가치만큼
        /// 크레딧" — the vehicle pays face value, and it pays the team.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="credits"/> is negative. Use <see cref="TrySpend"/> to take money out.</exception>
        public void Deposit(int credits)
        {
            if (credits < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(credits), credits, "Deposit cannot be negative — spending goes through TrySpend.");
            }

            _credits += credits;
            TotalEarned += credits;
        }

        /// <summary>
        /// Takes credits out if they are there.
        /// <para>
        /// Returns false and changes nothing when the team cannot afford it — the
        /// balance can never go below zero, because a shared wallet that could would
        /// let any one player mortgage the other three.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cost"/> is negative, which would be a deposit in disguise.</exception>
        public bool TrySpend(int cost)
        {
            if (cost < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cost), cost, "A negative cost would be a deposit — refunds go through Deposit.");
            }

            if (cost > _credits)
            {
                return false;
            }

            _credits -= cost;
            TotalSpent += cost;
            return true;
        }

        /// <summary>
        /// Wipes the balance. §02: "전멸하면 전리품 · 크레딧 · 정보가 전부 사라진다."
        /// <see cref="TotalEarned"/> and <see cref="TotalSpent"/> survive, because
        /// telemetry still wants to know what the team had before it lost everything.
        /// </summary>
        public void LoseEverything() => _credits = 0;

        /// <inheritdoc />
        public override string ToString() => "Wallet(" + _credits + " credits, team)";
    }
}
