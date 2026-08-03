using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// One runner's light. It has a switch, and that is all it has.
    /// <para>
    /// <b>What used to be here, and why it went.</b> This class used to own a
    /// <c>BatteryState</c> — §03's 왕복 구조 clock: charge measured in seconds of lit
    /// runtime, a per-switch-on cost, an idle drain, spare cells bought from §08's shop,
    /// a 지상 발전기 that topped the installed cell back up, and a latched
    /// <c>WentDarkThisTick</c> so the HUD could say "battery" rather than just going
    /// black. It also owned §08's 강화 손전등 upgrade through a <c>FlashlightOptics</c>
    /// struct that existed to keep the item's reward and price the same number.
    /// </para>
    /// <para>
    /// All of it is deleted, and the judgement behind that is not a balance tweak — it is
    /// what the game is. 하강 is 선착순 미로탈출: twenty runners drop onto the rim of B1
    /// and the first to the middle of B8 wins. A light you have to maintain is a chore
    /// bolted onto a footrace, and it was the second half of the owner's own complaint —
    /// 「단서를 찾고 불을 밝히고 이러고있어」. Total darkness is unplayable and a torch
    /// with a fuel gauge is busywork, so the runner is issued one light that simply works
    /// and is never asked to think about it again. The darkness stays: the maze is dark
    /// and gets darker as you descend, because that is the horror. What is gone is the
    /// paperwork.
    /// </para>
    /// <para>
    /// Do not re-add a drain here. If a future round wants a resource on the light, it is
    /// a new design decision that has to be argued from the race, not restored from §03 —
    /// §03 was written for a four-player co-operative looting game that no longer exists.
    /// </para>
    /// <para>
    /// It still holds no geometry. Where the runner is standing and where they are looking
    /// arrive per call, because a beam is a per-frame reading of the world rather than
    /// state the torch owns — and because other runners' beams are information you can see
    /// down a corridor.
    /// </para>
    /// </summary>
    public sealed class FlashlightState
    {
        /// <summary>
        /// Whether the switch is in the on position.
        /// <para>
        /// Identical to <see cref="IsLit"/> now that there is no cell behind it. Both are
        /// kept because they answer different questions and the callers ask different
        /// ones: the first-person view asks whether the torch is out of the pocket and in
        /// the fist, and the spot light asks whether light is coming out. They were
        /// separate states while a flat cell could leave the torch in the hand and the
        /// corridor dark; keeping both names means neither call site had to change when
        /// that stopped being possible.
        /// </para>
        /// </summary>
        public bool IsOn { get; private set; }

        /// <summary>
        /// Whether light is actually coming out. The test everything that cares about
        /// being seen should use.
        /// </summary>
        public bool IsLit
        {
            get { return IsOn; }
        }

        /// <summary>
        /// Beam half-angle, degrees. The torch is narrow, which is what makes aiming it a
        /// skill and what makes a corridor junction a decision rather than a glance.
        /// </summary>
        public float HalfAngleDegrees
        {
            get { return GameConstants.FlashlightHalfAngle; }
        }

        /// <summary>
        /// Distance at which the creature notices this beam, metres, or 0 when unlit.
        /// <para>
        /// The one price the light still charges, and it is a hazard rather than an
        /// economy: a lit runner is a runner the creature can pick out further away.
        /// Deliberately not scaled by <paramref name="tierRangeMultiplier"/> — a shorter
        /// beam paints less wall but does not make its holder harder to notice, and
        /// reading a reach penalty as a stealth bonus would invert it.
        /// </para>
        /// </summary>
        public float NoticeDistance
        {
            get { return IsLit ? GameConstants.FlashlightNoticeDistance : 0f; }
        }

        /// <summary>
        /// Beam reach right now, metres, or 0 when unlit.
        /// </summary>
        /// <param name="tierRangeMultiplier">
        /// <c>ThreatTier.FlashlightRangeMultiplier</c> — the time-of-night reach penalty,
        /// arriving as a float so this system never imports the threat system
        /// (ARCHITECTURE §3). A negative or NaN multiplier puts the runner in the dark
        /// rather than handing them a NaN beam that swallows every later comparison.
        /// </param>
        public float RangeFor(float tierRangeMultiplier)
        {
            if (!IsLit)
            {
                return 0f;
            }

            if (float.IsNaN(tierRangeMultiplier) || tierRangeMultiplier <= 0f)
            {
                return 0f;
            }

            return GameConstants.FlashlightRange * tierRangeMultiplier;
        }

        /// <summary>
        /// This torch as geometry, for anything that needs to know what the beam is
        /// touching.
        /// </summary>
        /// <param name="origin">Where the light is — the runner's eye position.</param>
        /// <param name="aimDirection">Where they are looking, pitch included.</param>
        /// <param name="tierRangeMultiplier">The time-of-night reach penalty. See <see cref="RangeFor"/>.</param>
        /// <returns><see cref="LightCone.None"/> when the light is off.</returns>
        public LightCone ConeAt(Vec3 origin, Vec3 aimDirection, float tierRangeMultiplier)
        {
            if (!IsLit)
            {
                return LightCone.None;
            }

            return new LightCone(
                origin, aimDirection, RangeFor(tierRangeMultiplier), HalfAngleDegrees);
        }

        /// <summary>
        /// Switches the light on.
        /// <para>
        /// Always succeeds, and that is the change. It used to charge a per-switch-on cost
        /// against the cell and could come up dark; there is nothing to spend now, so
        /// pressing the key and getting light is the whole contract. Calling this every
        /// frame while the key is held is free.
        /// </para>
        /// </summary>
        /// <returns>True — light is now coming out. Kept as a bool so the call sites that read it did not have to change.</returns>
        public bool TryTurnOn()
        {
            IsOn = true;
            return true;
        }

        /// <summary>
        /// Switches the light off. Free and immediate — the only cost of going dark is
        /// that you cannot see.
        /// </summary>
        public void TurnOff()
        {
            IsOn = false;
        }

        /// <summary>
        /// The torch key.
        /// <para>
        /// Symmetric now. It used to be deliberately asymmetric — off free, on charged —
        /// so that flicking the light was worse than committing to it. With the cell gone
        /// there is no charge to make it asymmetric with, and the only thing toggling
        /// costs is whether the creature can pick you out at
        /// <see cref="NoticeDistance"/>.
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
    }
}
