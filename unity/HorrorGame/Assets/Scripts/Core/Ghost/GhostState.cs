using HorrorGame.Core.Math;

namespace HorrorGame.Core.Ghost
{
    /// <summary>
    /// A caught runner. §09 — 사망 처리 — 유령.
    /// <para>
    /// §09 gives the state four rows and this class is those four rows and nothing
    /// else: the whole map through walls, no speech, no way out, free flight. §15
    /// records why it exists — the 영매 role failed because "아무도 하고 싶어하지
    /// 않는다", and moving it to a penalty state removed the problem because nobody
    /// chooses it.
    /// </para>
    /// <para>
    /// Two properties are structural rather than presentational, and that is
    /// deliberate:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Silence, and now total silence.</b> There is no method here that accepts a
    /// message, a target or a payload of any kind, and — since the rattle went —
    /// <b>nothing at all leaves a ghost</b>. That is a stronger sentence than the one
    /// this class used to carry. §09's balance argument ("죽은 사람이 정보를 주면
    /// 밸런스 붕괴") now holds by construction with no channel to audit, rather than
    /// by a muted microphone somebody could unmute: §13 already establishes that
    /// anything cut at the receiver is not cut at all.
    /// </description></item>
    /// <item><description>
    /// <b>No world probe.</b> This class never asks <c>IWorldProbe</c> anything.
    /// Taking one would imply that geometry could block a ghost's sight, and §09
    /// gives it "맵 전체를 자유롭게 본다 (벽 통과)". Distances here are straight-line
    /// for the same reason — the walls that make the living walk around simply do not
    /// apply to something that is only watching.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>DELETED — 신호 (the rattle), and the §08 loot the ghost used to watch.</b>
    /// <c>TryRattle</c>, <c>CanRattle</c>, <c>CanRattleAt</c>, <c>InRattleRange</c>,
    /// <c>RattleCooldownRemaining</c>, <c>RattleCharge01</c>, <c>RattleCount</c> and
    /// <c>Tick</c> let a ghost shake a nearby object once every 45 s. §11's 탈락자 rule
    /// forbids it in one sentence — 「살아 있는 사람에게 개입할 수 없다」 — and in a
    /// game where §12 makes 소리 the map, a placed noise is a forged footstep dropped
    /// by the only entity with 맵 전체 시야. It also reversed 탈락: §02 says elimination
    /// has no rank, and a verb that nudges somebody else's place puts the dead back
    /// into everyone's result but their own. <c>SeesOwnDroppedLoot</c>,
    /// <c>OwnDroppedLootValue</c>, <c>OwnDroppedLootPosition</c>,
    /// <c>DistanceToOwnDroppedLoot</c>, <c>NoteOwnLootDropped</c> and
    /// <c>NoteOwnLootRecovered</c> modelled §08's "유령이 된 본인은 자기 물건이 어디
    /// 있는지 보이는데 말할 수 없다" — a cruelty that needs a 전리품 to be cruel about.
    /// Nobody carries anything. What the spectator does instead lives in
    /// <c>GhostSession.CutToNextVantage</c>: it moves where it watches from, and moves
    /// nothing else.
    /// </para>
    /// </summary>
    public sealed class GhostState
    {
        private Vec3 _position;

        /// <summary>Creates a ghost at the spot its runner was caught.</summary>
        public GhostState(Vec3 deathPosition)
        {
            DeathPosition = deathPosition;
            _position = deathPosition;
        }

        /// <summary>
        /// Where the runner fell. §09 keeps it because it is one of the places the
        /// spectator can cut back to — your own body is the last thing you saw the
        /// creature standing over, and a race is long enough to want to look again.
        /// </summary>
        public Vec3 DeathPosition { get; }

        /// <summary>Where the ghost is looking from now. Free flight; see the class remarks on why no geometry is consulted.</summary>
        public Vec3 Position
        {
            get { return _position; }
        }

        /// <summary>
        /// Always false. §09's 말하기 row: "불가능".
        /// <para>
        /// Kept as a property because the voice layer has to be able to ask, and
        /// because the answer is a rule of the design rather than a per-platform
        /// microphone setting.
        /// </para>
        /// </summary>
        public bool CanSpeak
        {
            get { return false; }
        }

        /// <summary>
        /// Always false. §09's 탈출 row: "불가능 — 사망 페널티가 명확해진다".
        /// <para>
        /// §02 rests on this. 탈락 is out and unranked; a ghost that could leave and
        /// take a place would make being caught a route to the standings rather than
        /// the end of them.
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
        /// Moves the ghost. No collision, no navmesh, no line-of-sight test — §09
        /// grants free movement through the map.
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

        private static bool IsFinite(Vec3 v)
        {
            return !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsNaN(v.Z)
                   && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y) && !float.IsInfinity(v.Z);
        }
    }
}
