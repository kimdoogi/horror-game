#nullable enable

using HorrorGame.Core.Light;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// The battery, and therefore §03's lock. <em>"배터리가 떨어지면 단서를 읽을 수
    /// 없다."</em>
    /// <para>
    /// This is the single most consequential number the HUD carries, because §03
    /// routes the entire round-trip structure through it: the cell running down is
    /// simultaneously the reason to come out, the reason to go back in, and the
    /// reason the shop's cheapest shelf matters. §10 states the trade the readout has
    /// to make legible — "손전등을 켠다 / 괴물이 본다 · 배터리를 쓴다".
    /// </para>
    /// <para>
    /// <b>When it is drawn: while the beam is on, and once the supply is gone.</b>
    /// Not on a percentage threshold — a threshold would be a tuned number and no
    /// section supplies one, so it would have to be invented here, which
    /// ARCHITECTURE §2 forbids. "Spending it, or out of it" needs no tuning and picks
    /// out the two moments a player actually has a decision: whether to switch off,
    /// and whether to go up. Between them the corner of the screen is empty, which is
    /// what §01 wants.
    /// </para>
    /// </summary>
    public readonly struct LightReadout
    {
        /// <summary>Builds a readout directly. Prefer <see cref="From"/>.</summary>
        public LightReadout(
            bool hasLight,
            bool isOn,
            bool isLit,
            bool upgraded,
            float chargeFraction,
            int spareCells,
            float totalRemainingSeconds,
            bool isDead)
        {
            HasLight = hasLight;
            IsOn = isOn;
            IsLit = isLit;
            Upgraded = upgraded;
            ChargeFraction = chargeFraction;
            SpareCells = spareCells;
            TotalRemainingSeconds = totalRemainingSeconds;
            IsDead = isDead;
        }

        /// <summary>Whether the player has a flashlight in hand at all. False while carrying the objective (§03).</summary>
        public bool HasLight { get; }

        /// <summary>Whether the switch is on — what the player asked for.</summary>
        public bool IsOn { get; }

        /// <summary>Whether light is actually coming out. Differs from <see cref="IsOn"/> the instant a cell dies.</summary>
        public bool IsLit { get; }

        /// <summary>Whether this is §08's 강화 손전등 — twice the radius, and seen from twice as far.</summary>
        public bool Upgraded { get; }

        /// <summary>How full the installed cell is, 0–1.</summary>
        public float ChargeFraction { get; }

        /// <summary>Whole spare cells. §08 sells them one at a time.</summary>
        public int SpareCells { get; }

        /// <summary>Installed plus spares, in seconds of lit runtime. The figure §07's trade-offs are actually made against.</summary>
        public float TotalRemainingSeconds { get; }

        /// <summary>Empty cell and no spare — §03's lock closed with no key on the player.</summary>
        public bool IsDead { get; }

        /// <summary>See the class remarks: spending it, or out of it, and nothing in between.</summary>
        public bool IsVisible
        {
            get { return HasLight && (IsOn || IsDead); }
        }

        /// <summary>Whole minutes of light left in total, for a readout that does not tick every frame.</summary>
        public int RemainingMinutes
        {
            get { return (int)(TotalRemainingSeconds / 60f); }
        }

        /// <summary>Seconds within the current minute.</summary>
        public int RemainingSecondsPart
        {
            get { return (int)(TotalRemainingSeconds % 60f); }
        }

        /// <summary>
        /// True when the beam is on and the team is therefore paying §03's other
        /// price: "괴물이 잘 본다". Doubled by §08's upgrade, which is why the two
        /// facts are shown on one line.
        /// </summary>
        public bool DrawingAttention
        {
            get { return IsLit; }
        }

        /// <summary>Reads the flashlight. A null light — the objective carrier's state — reports <see cref="HasLight"/> false.</summary>
        public static LightReadout From(FlashlightState? flashlight)
        {
            if (flashlight == null)
            {
                return new LightReadout(false, false, false, false, 0f, 0, 0f, false);
            }

            var battery = flashlight.Battery;
            return new LightReadout(
                true,
                flashlight.IsOn,
                flashlight.IsLit,
                flashlight.IsUpgraded,
                battery.ChargeFraction,
                battery.SpareCells,
                battery.TotalRemainingSeconds,
                battery.IsDead);
        }
    }
}
