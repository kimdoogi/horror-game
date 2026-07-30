#nullable enable

using HorrorGame.Core.Ghost;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// What a dead player can see and do. §09.
    /// <para>
    /// §09 gives the ghost four rows and this readout is three of them. The fourth —
    /// 말하기: 불가능 — is represented by <b>nothing at all</b>.
    /// </para>
    /// <para>
    /// <b>Why there is no microphone state here.</b> A muted-mic icon, a greyed-out
    /// push-to-talk hint or a "you cannot speak" banner would all be the same
    /// mistake: they describe a control that exists in some other state, and they
    /// invite the player to look for the state. §09's silence is not a setting that
    /// happens to be off — it is the reason a dead player cannot break the game
    /// ("죽은 사람이 정보를 주면 밸런스 붕괴"), and <c>GhostState</c> holds that line
    /// in Core by having no method that accepts a message, a target or a payload of
    /// any kind. This struct holds the same line by having no field for one. The
    /// only outbound thing a ghost has is <see cref="CanRattle"/>, and §13 already
    /// establishes that cutting a channel at the receiver is not cutting it at all,
    /// so a UI-side mute would be theatre.
    /// </para>
    /// <para>
    /// The cooldown, by contrast, is shown prominently. §09's "최고의 순간" is the
    /// wait — <em>"쿨타임 45초 안에 다시 시도할 수 없다 … (유령의 절규)"</em> — and a
    /// wait the player cannot see is just a control that sometimes does nothing.
    /// </para>
    /// </summary>
    public readonly struct GhostReadout
    {
        /// <summary>Builds a readout directly. Prefer <see cref="From"/>.</summary>
        public GhostReadout(
            bool isGhost,
            float rattleCharge01,
            float cooldownRemaining,
            int rattleCount,
            bool seesOwnLoot,
            int ownLootValue,
            float ownLootDistance,
            GhostSignalFailure lastFailure)
        {
            IsGhost = isGhost;
            RattleCharge01 = rattleCharge01;
            CooldownRemaining = cooldownRemaining;
            RattleCount = rattleCount;
            SeesOwnLoot = seesOwnLoot;
            OwnLootValue = ownLootValue;
            OwnLootDistance = ownLootDistance;
            LastFailure = lastFailure;
        }

        /// <summary>Whether the local player is dead. The whole overlay hangs off this.</summary>
        public bool IsGhost { get; }

        /// <summary>Readiness, 0 right after a rattle and 1 when armed. §09's 45 s, as a bar.</summary>
        public float RattleCharge01 { get; }

        /// <summary>Seconds left on the cooldown.</summary>
        public float CooldownRemaining { get; }

        /// <summary>Rattles that landed this death. §13 counts them — a ghost that never uses the verb means 45 s is too long.</summary>
        public int RattleCount { get; }

        /// <summary>
        /// Whether the ghost can see the pile it dropped. §08 → §09: "유령이 된
        /// 본인은 자기 물건이 어디 있는지 보이는데 말할 수 없다."
        /// </summary>
        public bool SeesOwnLoot { get; }

        /// <summary>What the pile is worth, in credits. Watching it and being unable to say so is the point.</summary>
        public int OwnLootValue { get; }

        /// <summary>Straight-line metres to the pile. Straight because §09's ghost passes through walls; the living do not.</summary>
        public float OwnLootDistance { get; }

        /// <summary>Why the last rattle attempt failed, so the two kinds of anguish read differently. §09.</summary>
        public GhostSignalFailure LastFailure { get; }

        /// <summary>Whether the rattle would land right now, cooldown-wise.</summary>
        public bool CanRattle
        {
            get { return IsGhost && CooldownRemaining <= 0f; }
        }

        /// <summary>Whole seconds left, for a readout that counts down rather than flickers.</summary>
        public int CooldownSecondsLeft
        {
            get
            {
                var s = (int)(CooldownRemaining + 0.999f);
                return s < 0 ? 0 : s;
            }
        }

        /// <summary>Why the last attempt failed, in words. Empty when nothing has failed.</summary>
        public string FailureLabel
        {
            get { return UiStrings.RattleFailure(LastFailure); }
        }

        /// <summary>Nobody is dead. What a living player's overlay holds.</summary>
        public static GhostReadout None
        {
            get { return new GhostReadout(false, 0f, 0f, 0, false, 0, 0f, GhostSignalFailure.None); }
        }

        /// <summary>Reads a ghost. Null — a living player — reports <see cref="None"/>.</summary>
        public static GhostReadout From(GhostState? ghost, GhostSignalFailure lastFailure)
        {
            if (ghost == null)
            {
                return None;
            }

            return new GhostReadout(
                true,
                ghost.RattleCharge01,
                ghost.RattleCooldownRemaining,
                ghost.RattleCount,
                ghost.SeesOwnDroppedLoot,
                ghost.OwnDroppedLootValue,
                ghost.DistanceToOwnDroppedLoot,
                lastFailure);
        }
    }
}
