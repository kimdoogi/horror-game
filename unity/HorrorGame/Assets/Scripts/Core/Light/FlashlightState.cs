using System;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// One player's flashlight: the `F` key of §05, the battery behind it, and whether
    /// §08's upgrade has been bought.
    /// <para>
    /// §11 makes this the one piece of equipment nobody has to earn — "손전등을 전원 기본
    /// 지급했다 — 정비공은 효율이지 자격이 아니다" — so every one of §10's "누구나" rows
    /// runs through this object: turning it on buys sight and spends both battery and
    /// safety, and turning it off buys darkness and loses the clue.
    /// </para>
    /// <para>
    /// It holds no geometry of its own. Where the player is standing and where they are
    /// looking arrive per call, because §05 puts camera rotation on the wire precisely so
    /// that <i>other people's</i> beams are information — the beam is a per-frame reading
    /// of the world, not state the flashlight owns.
    /// </para>
    /// </summary>
    public sealed class FlashlightState
    {
        private readonly BatteryState _battery;

        /// <summary>
        /// Creates a flashlight over a battery supply.
        /// </summary>
        /// <param name="battery">The supply. Shared with nothing else — one light, one cell.</param>
        /// <param name="upgraded">True once §08's 강화 손전등 has been bought.</param>
        /// <exception cref="ArgumentNullException"><paramref name="battery"/> is missing.</exception>
        public FlashlightState(BatteryState battery, bool upgraded = false)
        {
            if (battery == null)
            {
                throw new ArgumentNullException(nameof(battery));
            }

            _battery = battery;
            IsUpgraded = upgraded;
        }

        /// <summary>The supply. §03's round-trip clock; see <see cref="BatteryState"/>.</summary>
        public BatteryState Battery
        {
            get { return _battery; }
        }

        /// <summary>True once §08's 강화 손전등 has been bought.</summary>
        public bool IsUpgraded { get; private set; }

        /// <summary>
        /// Every dimension of this beam, reward and price together. Read through here
        /// rather than from <see cref="GameConstants"/> so that §08's two effects can
        /// never come apart — see <see cref="FlashlightOptics"/>.
        /// </summary>
        public FlashlightOptics Optics
        {
            get { return FlashlightOptics.For(IsUpgraded); }
        }

        /// <summary>
        /// Whether the switch is in the on position. Not the same as
        /// <see cref="IsLit"/>: a player standing in the dark with a flat cell still has
        /// the switch on, and §03 wants that distinction visible so the HUD can say
        /// "battery" instead of just going black.
        /// </summary>
        public bool IsOn { get; private set; }

        /// <summary>
        /// Whether light is actually coming out. This is the one to test before treating
        /// the player as visible (§03: 괴물이 잘 본다) or a mark as readable.
        /// </summary>
        public bool IsLit
        {
            get { return IsOn && _battery.CanPowerLight; }
        }

        /// <summary>
        /// True when the last <see cref="Tick"/> was the one that ran the cell out.
        /// <para>
        /// Latched for exactly one step rather than inferred, because a host stepping at
        /// <see cref="GameConstants.FixedStep"/> after a frame spike would otherwise never
        /// see the transition — it would only see a dark flashlight and be unable to say
        /// whether the player switched it off or the battery died mid-read. §03 makes those
        /// two very different sentences.
        /// </para>
        /// </summary>
        public bool WentDarkThisTick { get; private set; }

        /// <summary>Beam half-angle, degrees. §03: 빛이 좁다.</summary>
        public float HalfAngleDegrees
        {
            get { return Optics.HalfAngleDegrees; }
        }

        /// <summary>
        /// Distance at which the monster notices this beam, metres, or 0 when it is not
        /// lit. §03's price, doubled by §08's upgrade. Unaffected by §07 — see
        /// <see cref="FlashlightOptics.NoticeDistance"/>.
        /// </summary>
        public float NoticeDistance
        {
            get { return IsLit ? Optics.NoticeDistance : 0f; }
        }

        /// <summary>
        /// Records §08's purchase. Idempotent, and it changes both halves of the item at
        /// once because there is only one number to change (<see cref="FlashlightOptics"/>).
        /// </summary>
        public void SetUpgraded(bool upgraded)
        {
            IsUpgraded = upgraded;
        }

        /// <summary>
        /// Beam reach right now, metres, or 0 when unlit.
        /// </summary>
        /// <param name="tierRangeMultiplier">
        /// <c>ThreatTier.FlashlightRangeMultiplier</c> — §07's 심야 −30%, arriving as a
        /// float so this system never imports the threat system (ARCHITECTURE §3).
        /// </param>
        public float RangeFor(float tierRangeMultiplier)
        {
            return IsLit ? Optics.RangeAt(tierRangeMultiplier) : 0f;
        }

        /// <summary>
        /// This flashlight as geometry, for the clue and perception queries.
        /// </summary>
        /// <param name="origin">Where the light is — the player's eye position.</param>
        /// <param name="aimDirection">Where they are looking. §05: 마우스 방향 aims the beam, pitch included.</param>
        /// <param name="tierRangeMultiplier">§07's 심야 penalty. See <see cref="RangeFor"/>.</param>
        /// <returns><see cref="LightCone.None"/> when the light is off or the cell is flat.</returns>
        public LightCone ConeAt(Vec3 origin, Vec3 aimDirection, float tierRangeMultiplier)
        {
            if (!IsLit)
            {
                return LightCone.None;
            }

            return new LightCone(origin, aimDirection, Optics.RangeAt(tierRangeMultiplier), HalfAngleDegrees);
        }

        /// <summary>
        /// Switches the light on, paying §03's "켤 때마다" charge.
        /// <para>
        /// Already-on is free and does not pay again, so a host that calls this every frame
        /// while `F` is held does not accidentally invent a per-frame drain. An empty cell
        /// refuses; a nearly empty one accepts, pays, and comes up dark — the charge is
        /// spent either way, because §03 charges the switch and not the outcome.
        /// </para>
        /// </summary>
        /// <returns>True when light is now coming out.</returns>
        public bool TryTurnOn()
        {
            if (IsOn)
            {
                return _battery.CanPowerLight;
            }

            if (!_battery.TryChargeSwitchOn())
            {
                // Either there was nothing to spend, or the switch-on cost took the last
                // of it. Both leave the player in the dark.
                IsOn = false;
                return false;
            }

            IsOn = true;
            return true;
        }

        /// <summary>
        /// Switches the light off. Free, and immediate — §10 prices this as "괴물이 본다 ·
        /// 배터리를 쓴다", so the only cost of going dark is the clue you were reading.
        /// </summary>
        public void TurnOff()
        {
            IsOn = false;
        }

        /// <summary>
        /// The `F` key of §05.
        /// <para>
        /// Toggling is deliberately asymmetric: off is free, on costs
        /// <see cref="GameConstants.BatterySwitchOnCost"/>. That is what makes flicking the
        /// light worse than committing to it, and
        /// <see cref="LightRules.SwitchOffBreakEvenSeconds"/> is the exact interval below
        /// which going dark loses charge instead of saving it.
        /// </para>
        /// </summary>
        /// <returns>True when light is now coming out.</returns>
        public bool Toggle()
        {
            if (IsOn)
            {
                TurnOff();
                return false;
            }

            return TryTurnOn();
        }

        /// <summary>
        /// Steps the battery and closes §03's lock if the cell ran out.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Step length. Zero changes nothing. A huge spike drains proportionally and, if
        /// that empties the cell, ends the step with the switch forced off and
        /// <see cref="WentDarkThisTick"/> set — the transition happens once and is
        /// reported once, however long the step was.
        /// </param>
        public void Tick(float deltaSeconds)
        {
            WentDarkThisTick = false;

            var wasLit = IsLit;
            _battery.Tick(deltaSeconds, IsOn);

            if (IsOn && !_battery.CanPowerLight)
            {
                // The switch is on but there is nothing behind it. Forcing it off keeps
                // IsOn honest, and means swapping a cell in does not silently relight the
                // beam without paying §03's switch-on charge again.
                IsOn = false;
            }

            WentDarkThisTick = wasLit && !IsLit;
        }
    }
}
