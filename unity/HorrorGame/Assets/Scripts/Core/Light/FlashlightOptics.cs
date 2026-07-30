namespace HorrorGame.Core.Light
{
    /// <summary>
    /// Every dimension of a flashlight beam, resolved for one flashlight.
    /// <para>
    /// This type exists so that §08's 강화 손전등 cannot be half-implemented. §08 calls
    /// it "이 목록의 대표작 — 밝으면 더 잘 보이지만 더 잘 보인다": the reward (반경 2배)
    /// and the price (괴물이 2배 멀리서 본다) are the same sentence, and the item is
    /// only interesting while they stay the same number. So there is exactly one
    /// number here — <see cref="UpgradeFactor"/> — and both
    /// <see cref="RangeMultiplier"/> and <see cref="DetectionMultiplier"/> return it.
    /// Buffing the beam without paying for it is not a value change; it is a code
    /// change, and it fails <c>LightTests</c>.
    /// </para>
    /// <para>
    /// <see cref="GameConstants"/> carries the factor twice (as
    /// <see cref="GameConstants.UpgradedFlashlightRangeMultiplier"/> and
    /// <see cref="GameConstants.UpgradedFlashlightDetectionMultiplier"/>) because §08
    /// states it twice. Only the first is read; <see cref="LightRules.Validate"/>
    /// insists the second still agrees, so a retune of one alone is caught rather
    /// than silently ignored.
    /// </para>
    /// </summary>
    public readonly struct FlashlightOptics
    {
        private readonly float _factor;

        private FlashlightOptics(bool upgraded, float factor)
        {
            IsUpgraded = upgraded;
            _factor = factor;
        }

        /// <summary>
        /// The flashlight every player is issued. §11: "손전등을 전원 기본 지급했다 —
        /// 정비공은 효율이지 자격이 아니다", which is why this is the baseline rather
        /// than a role's equipment.
        /// </summary>
        public static FlashlightOptics Standard
        {
            // 1.0 is the identity, not a tuned value: an un-upgraded light is §03's
            // numbers unmodified.
            get { return new FlashlightOptics(false, 1f); }
        }

        /// <summary>§08's 강화 손전등, bought for <see cref="GameConstants.ShopCostUpgradedFlashlight"/>.</summary>
        public static FlashlightOptics Upgraded
        {
            get { return new FlashlightOptics(true, GameConstants.UpgradedFlashlightRangeMultiplier); }
        }

        /// <summary>True when this is §08's 강화 손전등.</summary>
        public bool IsUpgraded { get; }

        /// <summary>
        /// The one number §08 buys. Multiplies the beam and the distance the monster
        /// notices it, and nothing else — §08 lists no battery penalty for the upgrade,
        /// so inventing one would be a second price the design did not ask for.
        /// </summary>
        public float UpgradeFactor
        {
            get { return _factor; }
        }

        /// <summary>How much further this beam reaches than the issued one. §08: 반경 2배.</summary>
        public float RangeMultiplier
        {
            get { return _factor; }
        }

        /// <summary>
        /// How much further the monster notices this beam than the issued one. §08:
        /// 괴물이 2배 멀리서 본다. Identical to <see cref="RangeMultiplier"/> by
        /// construction — see the type remarks.
        /// </summary>
        public float DetectionMultiplier
        {
            get { return _factor; }
        }

        /// <summary>
        /// Beam half-angle, degrees. §03's "빛이 좁다" is a property of the flashlight
        /// as such, and §08 buys reach rather than spread — a wider beam would make
        /// clue reading easier without making the reader more visible, which is the one
        /// trade §03 does not allow.
        /// </summary>
        public float HalfAngleDegrees
        {
            get { return GameConstants.FlashlightHalfAngle; }
        }

        /// <summary>Beam reach at full brightness, metres, before §07's time-of-night penalty.</summary>
        public float Range
        {
            get { return GameConstants.FlashlightRange * _factor; }
        }

        /// <summary>
        /// Distance at which the monster notices this beam, metres. §03 / §08.
        /// <para>
        /// Deliberately <b>not</b> scaled by §07's 심야 penalty. §07 takes 30% off the
        /// 반경 and says nothing about the monster, and reading it as a detection cut
        /// would make 심야 partly a stealth *bonus* — the one direction §07's own
        /// argument ("압박: 연속적", pressure only rising) forbids. The penalty still
        /// shows up in <see cref="LightRules.BeamConspicuousness"/>, because a shorter
        /// beam genuinely paints less wall. That split is a finding, not a preference;
        /// see docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        public float NoticeDistance
        {
            get { return GameConstants.FlashlightNoticeDistance * _factor; }
        }

        /// <summary>Picks the optics for a flashlight the team has or has not upgraded (§08).</summary>
        public static FlashlightOptics For(bool upgraded)
        {
            return upgraded ? Upgraded : Standard;
        }

        /// <summary>
        /// Beam reach after §07's time-of-night penalty, metres.
        /// </summary>
        /// <param name="tierRangeMultiplier">
        /// <c>ThreatTier.FlashlightRangeMultiplier</c> — 1.0 until 심야, then 0.7 and it
        /// stays there. Taken as a float so this system never imports the threat
        /// system's types (ARCHITECTURE §3). A negative or NaN multiplier is treated as
        /// 0 — a host that has lost track of the clock puts the player in the dark
        /// rather than handing them a NaN beam that swallows every later comparison.
        /// </param>
        public float RangeAt(float tierRangeMultiplier)
        {
            if (float.IsNaN(tierRangeMultiplier) || tierRangeMultiplier <= 0f)
            {
                return 0f;
            }

            return Range * tierRangeMultiplier;
        }
    }
}
