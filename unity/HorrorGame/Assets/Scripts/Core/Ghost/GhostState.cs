using HorrorGame.Core.Math;

namespace HorrorGame.Core.Ghost
{
    /// <summary>
    /// A dead player. §09 — 사망 처리 — 유령.
    /// <para>
    /// §09 gives the state four rows and this class is those four rows and nothing
    /// else: the whole map through walls, no speech, one weak rattle every 45 s, no
    /// way out. §15 records why it exists — the 영매 role failed because "아무도
    /// 하고 싶어하지 않는다", and moving it to a penalty state removed the problem
    /// because nobody chooses it.
    /// </para>
    /// <para>
    /// Two properties are structural rather than presentational, and that is
    /// deliberate:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Silence.</b> There is no method here that accepts a message, a target or
    /// a payload of any kind. <see cref="TryRattle"/> is the only thing that leaves
    /// a ghost, and it emits a position — one bit of "here", every 45 s. §09's
    /// balance argument ("죽은 사람이 정보를 주면 밸런스 붕괴") therefore holds by
    /// construction instead of by a muted microphone somebody could unmute: §13
    /// already establishes that anything cut at the receiver is not cut at all.
    /// </description></item>
    /// <item><description>
    /// <b>No world probe.</b> This class never asks <c>IWorldProbe</c> anything.
    /// Taking one would imply that geometry could block a ghost's sight or its
    /// signal, and §09 gives it "맵 전체를 자유롭게 본다 (벽 통과)". Distances here
    /// are straight-line for the same reason — the walls that make the living walk
    /// around simply do not apply, which is exactly why a ghost can watch its own
    /// loot lying somewhere the team cannot cheaply reach.
    /// </description></item>
    /// </list>
    /// </summary>
    public sealed class GhostState
    {
        private float _rattleCooldownRemaining;
        private Vec3 _position;
        private bool _seesOwnDroppedLoot;
        private int _ownDroppedLootValue;

        /// <summary>
        /// Creates a ghost at the spot its player died.
        /// <para>
        /// Starts off cooldown. §09 makes the 45 s the gap *between* signals, and a
        /// ghost that had to wait 45 s before its first attempt would spend the most
        /// informative moment it will ever have — the seconds right after it saw the
        /// monster kill it — unable to do the one thing it can do.
        /// </para>
        /// </summary>
        public GhostState(Vec3 deathPosition)
        {
            DeathPosition = deathPosition;
            _position = deathPosition;
        }

        /// <summary>
        /// Where the player fell. §08 drops their loot here — "회수하려면 시체가
        /// 있는 곳으로 돌아가야 한다" — so this doubles as the address of the pile.
        /// </summary>
        public Vec3 DeathPosition { get; }

        /// <summary>Where the ghost is looking from now. Free flight; see the class remarks on why no geometry is consulted.</summary>
        public Vec3 Position
        {
            get { return _position; }
        }

        /// <summary>Seconds left before another rattle can land. §09.</summary>
        public float RattleCooldownRemaining
        {
            get { return _rattleCooldownRemaining; }
        }

        /// <summary>True when a rattle would land, cooldown-wise.</summary>
        public bool CanRattle
        {
            get { return _rattleCooldownRemaining <= 0f; }
        }

        /// <summary>
        /// Readiness as a fraction, 0 right after a rattle and 1 when armed. §09's
        /// "최고의 순간" is the wait, so the wait is the thing the ghost's HUD shows.
        /// </summary>
        public float RattleCharge01
        {
            get { return MathX.Clamp01(1f - (_rattleCooldownRemaining / GameConstants.GhostRattleCooldownSeconds)); }
        }

        /// <summary>Rattles that landed. §13's counters — if this is near zero per death, the 45 s is too long to be worth using.</summary>
        public int RattleCount { get; private set; }

        /// <summary>
        /// Always false. §09's 말하기 row: "불가능".
        /// <para>
        /// Kept as a property because the voice layer has to be able to ask, and
        /// because the answer is a rule of the design rather than a per-platform
        /// microphone setting. The living are gated the same way from
        /// <c>PlayerState.MayTransmitVoice</c>.
        /// </para>
        /// </summary>
        public bool CanSpeak
        {
            get { return false; }
        }

        /// <summary>
        /// Always false. §09's 탈출 row: "불가능 — 사망 페널티가 명확해진다".
        /// <para>
        /// §02 rests on this. If a ghost could leave, "누군가는 살아서 나가야 한다"
        /// would cost nothing and a wipe would be unreachable, which is the one
        /// outcome the whole "지금 나갈까?" argument is measured against.
        /// </para>
        /// </summary>
        public bool CanEscape
        {
            get { return false; }
        }

        /// <summary>Always true. §09's 시야 row: "맵 전체를 자유롭게 본다 (벽 통과)".</summary>
        public bool SeesEntireMap
        {
            get { return true; }
        }

        /// <summary>
        /// Whether the ghost can currently see its own dropped haul. §08 → §09:
        /// "유령이 된 본인은 자기 물건이 어디 있는지 보이는데 말할 수 없다."
        /// <para>
        /// False until the drop is reported, because an empty-handed death leaves no
        /// pile at all, and false again once the team picks it up — the ghost watches
        /// that happen too.
        /// </para>
        /// </summary>
        public bool SeesOwnDroppedLoot
        {
            get { return _seesOwnDroppedLoot; }
        }

        /// <summary>Credits lying in that pile, or 0 when there is none. §08 — the number the ghost can see and cannot say.</summary>
        public int OwnDroppedLootValue
        {
            get { return _seesOwnDroppedLoot ? _ownDroppedLootValue : 0; }
        }

        /// <summary>
        /// Where that haul is, or null when there is none. §08 drops it where the
        /// player died, so this is <see cref="DeathPosition"/> — the ghost knows the
        /// exact coordinate and has no channel able to express it.
        /// </summary>
        public Vec3? OwnDroppedLootPosition
        {
            get { return _seesOwnDroppedLoot ? (Vec3?)DeathPosition : null; }
        }

        /// <summary>
        /// Straight-line distance from the ghost to its own pile, or
        /// <see cref="float.PositiveInfinity"/> when there is no pile.
        /// <para>
        /// Straight-line on purpose: the walls that make the recovery trip dangerous
        /// for the living (§08, §12) do not exist for the ghost. The living team's
        /// side of this question is a navigable distance and belongs to the economy's
        /// dropped-loot field.
        /// </para>
        /// </summary>
        public float DistanceToOwnDroppedLoot
        {
            get { return _seesOwnDroppedLoot ? Vec3.Distance(_position, DeathPosition) : float.PositiveInfinity; }
        }

        /// <summary>
        /// Moves the ghost. No collision, no navmesh, no line-of-sight test — §09
        /// grants free movement through the map, and the only thing position gates is
        /// which object is close enough to shake.
        /// <para>
        /// A non-finite position is ignored rather than stored, so one bad frame
        /// cannot poison every later range check into NaN.
        /// </para>
        /// </summary>
        public void MoveTo(Vec3 position)
        {
            if (!IsFinite(position))
            {
                return;
            }

            _position = position;
        }

        /// <summary>
        /// Reports that the player's loot hit the floor. Called by the session layer
        /// after the economy has actually made the pile, because an empty-handed
        /// death makes none.
        /// </summary>
        /// <param name="creditValue">
        /// What the pile is worth. A value of zero or less is treated as no pile:
        /// §08's reason for the rule is "위험을 감수할 또 하나의 이유", and a
        /// worthless pile is not a reason to walk back into the basement.
        /// </param>
        public void NoteOwnLootDropped(int creditValue)
        {
            if (creditValue <= 0)
            {
                _seesOwnDroppedLoot = false;
                _ownDroppedLootValue = 0;
                return;
            }

            _seesOwnDroppedLoot = true;
            _ownDroppedLootValue = creditValue;
        }

        /// <summary>Reports that the team came back and took it. §08's recovery trip, seen from the ghost's side.</summary>
        public void NoteOwnLootRecovered()
        {
            _seesOwnDroppedLoot = false;
            _ownDroppedLootValue = 0;
        }

        /// <summary>
        /// Runs the 45 s cooldown down. §09.
        /// <para>
        /// A frame spike is honoured in full and clamps at ready, which cannot skip a
        /// transition: the only transition here is not-ready → ready, and finishing a
        /// wait early is what a long frame means. Zero, negative, NaN and infinite
        /// deltas are ignored instead of clamped — the 45 s is a promise the design
        /// leans on ("쿨타임 45초 안에 다시 시도할 수 없다"), so a garbage delta must
        /// not be able to hand out a free signal.
        /// </para>
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            // NaN fails every relational test, so this one guard rejects NaN, zero
            // and negatives together.
            if (!(deltaSeconds > 0f) || float.IsPositiveInfinity(deltaSeconds))
            {
                return;
            }

            if (_rattleCooldownRemaining <= 0f)
            {
                return;
            }

            _rattleCooldownRemaining -= deltaSeconds;
            if (_rattleCooldownRemaining < 0f)
            {
                _rattleCooldownRemaining = 0f;
            }
        }

        /// <summary>
        /// Whether an object at <paramref name="objectPosition"/> could be shaken
        /// right now. §09 — cooldown and "근처" both have to hold.
        /// </summary>
        public bool CanRattleAt(Vec3 objectPosition)
        {
            return CanRattle && InRattleRange(objectPosition);
        }

        /// <summary>
        /// Shakes a nearby object. §09's 신호 row, and the only outbound channel a
        /// ghost has.
        /// <para>
        /// A failed attempt costs nothing — it neither arms nor extends the cooldown.
        /// That is the opposite of the Flasher's rule (§04, where pressing the button
        /// is what you pay), and the reason is §09's own scene: the ghost's problem is
        /// that it must wait 45 s, so charging it another 45 s for clicking a chair
        /// that turned out to be 5 m away would add a trap on top of a penalty and
        /// buy the design nothing.
        /// </para>
        /// </summary>
        /// <param name="objectPosition">The thing to shake. Must be within <see cref="GameConstants.GhostRattleRange"/>.</param>
        /// <param name="rattle">What happened, including why nothing moved.</param>
        /// <returns>True when the object moved and the 45 s started again.</returns>
        public bool TryRattle(Vec3 objectPosition, out GhostRattle rattle)
        {
            var distance = Vec3.Distance(_position, objectPosition);

            if (_rattleCooldownRemaining > 0f)
            {
                rattle = new GhostRattle(
                    false, objectPosition, distance, _rattleCooldownRemaining, GhostSignalFailure.OnCooldown);
                return false;
            }

            if (!InRattleRange(objectPosition))
            {
                rattle = new GhostRattle(false, objectPosition, distance, 0f, GhostSignalFailure.OutOfRange);
                return false;
            }

            _rattleCooldownRemaining = GameConstants.GhostRattleCooldownSeconds;
            RattleCount++;
            rattle = new GhostRattle(
                true, objectPosition, distance, GameConstants.GhostRattleCooldownSeconds, GhostSignalFailure.None);
            return true;
        }

        /// <summary>
        /// True when the object is close enough. Phrased as "is inside" rather than
        /// "is beyond" so that a NaN coordinate — every comparison against which is
        /// false — reads as out of range rather than passing the check.
        /// </summary>
        private bool InRattleRange(Vec3 objectPosition)
        {
            var distance = Vec3.Distance(_position, objectPosition);
            return distance <= GameConstants.GhostRattleRange;
        }

        private static bool IsFinite(Vec3 v)
        {
            return !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsNaN(v.Z)
                   && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y) && !float.IsInfinity(v.Z);
        }
    }
}
