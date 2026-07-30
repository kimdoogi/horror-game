using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// One flashlight's power supply: the cell that is in it, the spares in the bag, and
    /// the accounting §03 needs.
    /// <para>
    /// §03 puts 손전등 배터리 at the top of its 왕복 구조 table with two consumption
    /// conditions — "시간 경과 + 켤 때마다" — and one consequence: "배터리가 떨어지면
    /// 단서를 읽을 수 없다." That single line is what turns §03's "어둠 = 목표의 잠금장치"
    /// into resource pressure, and it is the reason a team ever walks back out. So this
    /// class is not a fuel gauge; it is the clock on the round trip.
    /// </para>
    /// <para>
    /// Charge is measured in <b>seconds of lit runtime</b> rather than as a fraction, so
    /// every quantity in the model is directly comparable to §07's currency: a cell is
    /// <see cref="GameConstants.BatterySecondsPerCell"/> seconds, a switch-on costs
    /// <see cref="GameConstants.BatterySwitchOnCost"/> seconds, and an hour spent walking
    /// with the light off costs <see cref="GameConstants.BatteryIdleDrainMultiplier"/> of
    /// an hour. §07's "나가서 배터리 교체 ~1분" is then a comparison between two numbers in
    /// the same unit.
    /// </para>
    /// <para>
    /// Nothing here refuses anything. A player may burn the last of a cell on a switch-on
    /// that lights nothing, and may swap out a cell that was 90% full and lose it. §03
    /// wants the battery to be a decision; a supply that protected the player from bad
    /// decisions would not be one.
    /// </para>
    /// </summary>
    public sealed class BatteryState
    {
        private float _charge;
        private int _spareCells;
        private float _wastedSeconds;
        private int _switchOnCount;

        /// <summary>
        /// Creates a supply with a full cell installed and no spares — what a player
        /// walks in with on 1차 잠입, when §08 puts the wallet at 0.
        /// </summary>
        public BatteryState()
            : this(1f, 0)
        {
        }

        /// <summary>
        /// Creates a supply in an arbitrary state, including the partial cell a team
        /// carries back in after cutting a descent short.
        /// </summary>
        /// <param name="installedChargeFraction">
        /// How full the installed cell is, 0–1. NaN is read as empty rather than
        /// propagating through every later comparison.
        /// </param>
        /// <param name="spareCells">
        /// Whole cells in the bag. §08 sells 배터리 as a unit, so a spare is always full.
        /// Negative is read as none.
        /// </param>
        public BatteryState(float installedChargeFraction, int spareCells)
        {
            var fraction = float.IsNaN(installedChargeFraction)
                ? 0f
                : MathX.Clamp01(installedChargeFraction);

            _charge = fraction * GameConstants.BatterySecondsPerCell;
            _spareCells = spareCells > 0 ? spareCells : 0;
        }

        /// <summary>Seconds of lit runtime left in the installed cell.</summary>
        public float Charge
        {
            get { return _charge; }
        }

        /// <summary>How full the installed cell is, 0–1. The HUD's bar.</summary>
        public float ChargeFraction
        {
            get { return MathX.Clamp01(_charge / GameConstants.BatterySecondsPerCell); }
        }

        /// <summary>Whole spare cells in the bag.</summary>
        public int SpareCells
        {
            get { return _spareCells; }
        }

        /// <summary>
        /// Seconds of light the player has in total, installed plus spares. This is the
        /// number §07's trade-offs are actually made against — it is how much building
        /// time the team has left before the surface stops being optional.
        /// </summary>
        public float TotalRemainingSeconds
        {
            get { return _charge + (_spareCells * GameConstants.BatterySecondsPerCell); }
        }

        /// <summary>
        /// True when the installed cell can still light the beam. The flashlight reads
        /// this every tick; a false answer is §03's lock closing.
        /// </summary>
        public bool CanPowerLight
        {
            get { return _charge > 0f; }
        }

        /// <summary>
        /// True when the installed cell is empty and there is nothing to swap in — §03's
        /// "배터리가 떨어지면 단서를 읽을 수 없다" with no way out of it but the surface.
        /// Distinct from <see cref="CanPowerLight"/>: a player between cells is dark but
        /// not stranded.
        /// </summary>
        public bool IsDead
        {
            get { return _charge <= 0f && _spareCells <= 0; }
        }

        /// <summary>
        /// Seconds of charge thrown away by swapping cells early.
        /// <para>
        /// Recorded rather than hidden because it is the price of the safe play: swapping
        /// at half a cell discards <see cref="GameConstants.BatterySecondsPerCell"/>/2 of
        /// light, which at §08's <see cref="GameConstants.ShopCostBattery"/> is real
        /// credits and at §07's rates is real minutes. The simulator sweeps it to find out
        /// whether the round-trip rhythm §16-5 is looking for comes from the cell life or
        /// from players hedging against it.
        /// </para>
        /// </summary>
        public float WastedSeconds
        {
            get { return _wastedSeconds; }
        }

        /// <summary>
        /// How many times the light has been switched on. §03 charges "켤 때마다", so this
        /// is both the count of those charges and the telemetry that says whether players
        /// actually strobe the light the way the cost anticipates.
        /// </summary>
        public int SwitchOnCount
        {
            get { return _switchOnCount; }
        }

        /// <summary>
        /// Drains the installed cell for one step.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Step length. Zero drains nothing; negative and NaN are read as zero rather than
        /// recharging the cell by rewinding. A frame spike simply drains more, and the
        /// charge floors at empty instead of going negative, so no amount of delta can
        /// push the supply into a state the rules cannot describe.
        /// </param>
        /// <param name="lightOn">
        /// Whether the beam was lit for this step. §03 charges time either way — "시간
        /// 경과" is listed before "켤 때마다" — but lit costs
        /// 1/<see cref="GameConstants.BatteryIdleDrainMultiplier"/> times as much.
        /// </param>
        public void Tick(float deltaSeconds, bool lightOn)
        {
            var dt = Sanitize(deltaSeconds);
            if (dt <= 0f)
            {
                return;
            }

            // A lit second costs a second of charge: that is what defines
            // BatterySecondsPerCell, so 1 here is a unit and not a tuned value.
            var rate = lightOn ? 1f : GameConstants.BatteryIdleDrainMultiplier;
            Drain(dt * rate);
        }

        /// <summary>
        /// Charges §03's per-switch cost.
        /// <para>
        /// Called by <see cref="FlashlightState.TryTurnOn"/> and nowhere else. Returns
        /// false when there was nothing to spend, and also when the cost consumed the last
        /// of the cell — in that second case the charge is <b>still gone</b>. §03 charges
        /// on the switch, not on the result, and a player who presses F with a second left
        /// has spent it.
        /// </para>
        /// </summary>
        /// <returns>True when the cell can still light the beam after paying.</returns>
        public bool TryChargeSwitchOn()
        {
            if (_charge <= 0f)
            {
                return false;
            }

            _switchOnCount++;
            Drain(GameConstants.BatterySwitchOnCost);
            return _charge > 0f;
        }

        /// <summary>
        /// Swaps a fresh cell in, discarding whatever was left in the old one.
        /// <para>
        /// The remainder is lost on purpose. §03's resupply is the 지상 발전기, which is on
        /// the surface, so a cell pulled out in a basement corridor has nowhere to go — and
        /// that is what makes "swap now or push on" a decision rather than a formality. The
        /// discarded amount comes back out so the host can show it and telemetry can total
        /// it.
        /// </para>
        /// </summary>
        /// <param name="discardedSeconds">Runtime lost with the old cell.</param>
        /// <returns>False when there is no spare, leaving the installed cell untouched.</returns>
        public bool TrySwapCell(out float discardedSeconds)
        {
            if (_spareCells <= 0)
            {
                discardedSeconds = 0f;
                return false;
            }

            discardedSeconds = _charge;
            _wastedSeconds += _charge;
            _spareCells--;
            _charge = GameConstants.BatterySecondsPerCell;
            return true;
        }

        /// <summary>
        /// Adds spare cells — §08's 배터리 at the 지상 차량, or a dead teammate's kit
        /// (§08: 사망자의 전리품은 떨어진다). Non-positive counts are ignored.
        /// </summary>
        public void AddCells(int count)
        {
            if (count > 0)
            {
                _spareCells += count;
            }
        }

        /// <summary>
        /// Tops the installed cell back up at §03's 지상 발전기.
        /// <para>
        /// Does not create cells — §08 is the only place a spare comes from. This is why
        /// the surface trip is worth a minute of §07's clock even when the bag is empty:
        /// the generator turns a half-used cell back into a full descent without spending
        /// a credit.
        /// </para>
        /// </summary>
        public void Recharge()
        {
            _charge = GameConstants.BatterySecondsPerCell;
        }

        private void Drain(float seconds)
        {
            _charge -= seconds;
            if (!(_charge > 0f))
            {
                // Covers NaN as well as the ordinary underflow: an empty cell is a
                // describable state, a NaN one is not.
                _charge = 0f;
            }
        }

        private static float Sanitize(float deltaSeconds)
        {
            return float.IsNaN(deltaSeconds) || deltaSeconds < 0f ? 0f : deltaSeconds;
        }
    }
}
