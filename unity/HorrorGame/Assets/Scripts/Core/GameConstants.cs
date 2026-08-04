using System;

namespace HorrorGame.Core
{
    /// <summary>
    /// Every tuned number from the design document, in one place.
    /// <para>
    /// This file is the single authority for balance values. Nothing else in the
    /// codebase may hard-code a gameplay number — Unity components, the headless
    /// simulator and the tests all read from here, so a balance change is a
    /// one-line edit that every consumer and every test sees at once.
    /// </para>
    /// <para>
    /// Each member cites the design-doc section it comes from. When you change a
    /// value, check the tests named after that section: they assert the
    /// relationships the document reasons about (for example "the monster must be
    /// faster than a running player but slower than a sprinting Runner"), so a
    /// change that breaks the design's internal logic fails the build rather than
    /// silently shipping.
    /// </para>
    /// </summary>
    public static class GameConstants
    {
        // ====================================================================
        // §06 — Speed relationships. "이 한 줄이 게임 전체를 정한다."
        //   걷기 2.0  <  달리기 4.5  <  괴물 4.8  <  주자 질주 5.6  (m/s)
        // ====================================================================

        /// <summary>Base walking speed, m/s. §06.</summary>
        public const float WalkSpeed = 2.0f;

        /// <summary>Running speed (Shift) for every role, m/s. §06.</summary>
        public const float RunSpeed = 4.5f;

        /// <summary>
        /// Monster base speed, m/s. §06. Only 0.3 m/s faster than a running
        /// player — the "거의 도망칠 수 있는데 안 되는" margin the design depends on.
        /// Scaled per time-of-night by <c>ThreatCurve</c>.
        /// </summary>
        public const float MonsterBaseSpeed = 4.8f;

        /// <summary>Runner sprint speed, m/s. §06. The only speed that beats the monster.</summary>
        public const float RunnerSprintSpeed = 5.6f;

        // ====================================================================
        // §05 — Directional speed multipliers. "뒷걸음은 괴물보다 느리다."
        // ====================================================================

        /// <summary>Forward (W). §05.</summary>
        public const float MulForward = 1.00f;

        /// <summary>Diagonal (W+A/D) — the 45° peek. §05.</summary>
        public const float MulDiagonal = 0.95f;

        /// <summary>Pure strafe (A/D). §05.</summary>
        public const float MulStrafe = 0.90f;

        /// <summary>
        /// Backward (S). §05. Deliberately below the monster's speed so that
        /// looking behind you costs distance instead of an unclear penalty.
        /// </summary>
        public const float MulBackward = 0.65f;

        /// <summary>
        /// Heading offset at which <see cref="MulDiagonal"/> applies exactly,
        /// degrees. §05's "45도 곁눈질". The multipliers above are knots on a
        /// continuous curve, not four buckets — §05 insists the trade is "이산적
        /// 선택이 아니라 아날로그 조절" — so this is where the curve passes through the
        /// table's diagonal row, and a player who turns 10° pays 10°'s worth.
        /// </summary>
        public const float PeekAngleDegrees = 45f;

        /// <summary>Heading offset at which <see cref="MulStrafe"/> applies exactly, degrees. §05's "90도 측면".</summary>
        public const float StrafeAngleDegrees = 90f;

        /// <summary>Heading offset at which <see cref="MulBackward"/> applies exactly, degrees. §05's "완전히 뒤".</summary>
        public const float BackwardAngleDegrees = 180f;

        // ====================================================================
        // §05 — Field of view. A balance value, not a comfort setting.
        // ====================================================================

        /// <summary>Default vertical FOV, degrees. §05: peeking works, view is still tight.</summary>
        public const float FovDefault = 80f;

        /// <summary>Minimum user-selectable FOV. §05.</summary>
        public const float FovMin = 70f;

        /// <summary>
        /// Maximum user-selectable FOV. §05 — above this, the 45° peek becomes
        /// free and the core dilemma weakens, so the range is capped rather than open.
        /// </summary>
        public const float FovMax = 90f;

        // ====================================================================
        // §05 · §12 — 웅크리기 and a hop. Two verbs §05's control table does not
        // list, and the bounds that stop them changing what §06 and §12 mean.
        //
        // §05 names 마우스 · WASD · Shift · F and stops. Neither verb below is in
        // that table, so none of these numbers is quoted from the design document;
        // every one of them is *derived* from a rule the document does state, and
        // the derivation is in the member's own remarks. The rule they all serve is
        // the same one: §06's chase arithmetic and §12's geometry are both
        // statements about a player moving along the ground, and a new verb is only
        // allowed if it leaves both of them saying exactly what they said before.
        // ====================================================================

        /// <summary>
        /// Speed multiplier while crouched. §05's 배율표 composes multiplicatively —
        /// 「§05 배율에 곱연산으로 적용된다」 — so this stacks onto the directional
        /// multiplier rather than replacing it. §08 is where that composition rule was
        /// written down, for the carry-weight bands it applied to; the bands and the
        /// section are deleted, and stance times direction is the whole product now.
        /// <para>
        /// Half. §12 asks for 은폐 지점 near the 출입구 for §07's 새벽 stage, and §07
        /// makes the price of everything the same currency: "시간이 유일한 통화다."
        /// So concealment is bought with time at the bluntest exchange rate there
        /// is — a crouched player covers a corridor in twice the seconds. The value
        /// is checked in <see cref="Validate"/> against §05's own worst row rather
        /// than asserted on its own: crouching forward has to be slower than 후진,
        /// the most expensive thing §05's table charges for, or it is not a real
        /// trade.
        /// </para>
        /// </summary>
        public const float CrouchSpeedMultiplier = 0.50f;

        /// <summary>
        /// Multiplier on a crouched player's own movement noise, on the 0~1 scale
        /// <see cref="ListenerSelfNoiseThreshold"/> is quoted against. §12.
        /// <para>
        /// The constraint it buys out of is 「자기가 소리를 내면 못 듣는다」, which §04 wrote
        /// as the 청음사's price and which §12 now charges everybody — 소리가 지도다, so a
        /// runner who is loud cannot read the map. The co-op game let you buy out of it
        /// with a 소음기 from §08's shop;
        /// there is no shop now, so crouching is the only version of the trade and
        /// the constant that priced the bought one is deleted. It stays deliberately
        /// partial: it costs half the player's speed, and it does not silence a door,
        /// a landing or anything else <c>NoiseMeter</c> raises as a transient. Only
        /// the continuous term from moving is scaled — a runner who wants to arrive
        /// quietly still cannot arrive fast.
        /// </para>
        /// </summary>
        public const float CrouchNoiseMultiplier = 0.45f;

        /// <summary>
        /// Crouched standing height as a fraction of the player's own height. §12.
        /// <para>
        /// Not chosen — measured off the asset. <c>tools/blender/gen_player_model.py</c>
        /// fails its own build if the <c>Crouch</c> clip leaves the eye above 1.28 m
        /// against a 1.635 m standing eye, so the pose the other three players see
        /// drops about 0.41 m of a 1.75 m body. The collider is sized to agree with
        /// it: a capsule that crouched deeper than the animation would fit through
        /// gaps the visible body does not, and §12's 은폐 지점 would then be a
        /// different size for the person hiding and for the person looking.
        /// </para>
        /// </summary>
        public const float CrouchHeightFraction = 0.75f;

        /// <summary>
        /// How high the player's own step carries them, metres — the
        /// <c>CharacterController.stepOffset</c> every rig is built with.
        /// <para>
        /// It lives here because §12 depends on it. The map's geometry is derived
        /// from what a player <em>cannot</em> climb, and a stairwell whose riser
        /// exceeded this number is the shape of map defect that has already cost
        /// this project a blocker. A literal in two rig builders and one Blender
        /// generator is a §12 constraint nobody can grep for.
        /// </para>
        /// </summary>
        public const float PlayerStepOffsetMetres = 0.40f;

        /// <summary>
        /// Apex of a jump, metres, measured from the floor the player left.
        /// <para>
        /// <b>Deliberately below <see cref="PlayerStepOffsetMetres"/>, and that is
        /// the whole design.</b> A player can already walk up anything 0.40 m or
        /// less, so a jump that peaks at 0.35 m reaches a strict <em>subset</em> of
        /// what walking reaches and adds no traversal at all. Everything §12
        /// assumes a player cannot climb stays unclimbable by construction rather
        /// than by playtesting: the stairwell riser, the crates, the debris and the
        /// 차량 are all taller than a step, therefore taller than this.
        /// </para>
        /// <para>
        /// §06's chase arithmetic — 0.8 m/s of gain, 3 s of cover, 12 m of release —
        /// is arithmetic about ground movement, and <c>MonsterChaseTests</c> measures
        /// it to 1 %. A hop that cannot mount anything cannot shorten a route, so
        /// none of those numbers has anything to answer for. It reads as stepping
        /// over a puddle or a pipe, which is what it is for.
        /// </para>
        /// </summary>
        public const float JumpApexMetres = 0.35f;

        /// <summary>
        /// Gravity the jump is sized against, m/s². Unity's default magnitude,
        /// declared so <see cref="JumpTakeoffSpeed"/> and
        /// <see cref="JumpAirtimeSeconds"/> are arithmetic rather than guesses. The
        /// engine adapter derives the actual impulse from the actual
        /// <c>Physics.gravity</c>, so the apex is honoured even if a project setting
        /// moves it.
        /// </summary>
        public const float JumpGravity = 9.81f;

        /// <summary>
        /// Upward speed, m/s, that reaches exactly <see cref="JumpApexMetres"/>.
        /// v = √(2gh).
        /// </summary>
        public static readonly float JumpTakeoffSpeed = MathF.Sqrt(2f * JumpGravity * JumpApexMetres);

        /// <summary>Seconds a jump spends off the floor, up and back down. 2v/g.</summary>
        public static readonly float JumpAirtimeSeconds = 2f * JumpTakeoffSpeed / JumpGravity;

        /// <summary>
        /// Shortest interval between two jumps, seconds.
        /// <para>
        /// A cooldown rather than a stamina cost. The bar is §06's, it is the same bar
        /// for all twenty runners, and it is the twelve seconds
        /// <see cref="SprintStaminaSeconds"/> spends the entire aggro-release
        /// calculation against — so charging a hop to it would quietly shorten every
        /// escape in the game by however much hopping a runner did. A cooldown is paid
        /// in a currency §06 never counts.
        /// </para>
        /// <para>
        /// <b>The original argument was a §04 one and it is gone.</b> It read "the bar
        /// belongs to the 주자 alone, so charging a jump to it would price the same verb
        /// differently for five roles". There are no roles and no five of anything; the
        /// conclusion survives the premise because the other half of it — that the bar is
        /// load-bearing for §06 — was never about who was carrying it.
        /// </para>
        /// <para>
        /// Sized against <see cref="JumpAirtimeSeconds"/> and checked in
        /// <see cref="Validate"/>: it is longer than the hop, so there is always a
        /// grounded beat between two jumps and no chain of them can become a way of
        /// travelling.
        /// </para>
        /// </summary>
        public const float JumpCooldownSeconds = 0.60f;

        /// <summary>
        /// Noise a landing raises, on <see cref="ListenerSelfNoiseThreshold"/>'s
        /// 0~1 scale. §12.
        /// <para>
        /// The opposite sign of <see cref="CrouchNoiseMultiplier"/> and the other
        /// half of the same idea: a jump buys a little height and pays the loudest
        /// price a body can pay in the one channel the game charges in. Set above
        /// <see cref="ListenerSelfNoiseThreshold"/> — checked in
        /// <see cref="Validate"/> — so a runner who jumps stops being able to read
        /// §12's floor for the transient, exactly as if they had opened a door.
        /// §04 charged this to the 청음사 alone; §12 makes 소리 the map, so it is
        /// charged to whoever made the noise.
        /// </para>
        /// </summary>
        public const float PlayerLandingNoiseLevel = 0.65f;

        // ====================================================================
        // §06 — Aggro, stamina, release.
        // ====================================================================

        // ── The lunge ───────────────────────────────────────────────────────
        // §06 gives the creature five states and none of them is an attack, because the
        // catch used to be geometry: the two capsules touched and the player died. That
        // is not a thing anybody can see happen, and the Grab clip — 1.37 s of it, built
        // and rigged — only ever played over a corpse.
        //
        // These five make it an act. The creature COMMITS at MonsterAttackRange, lunges
        // faster than it chases, and the strike lands MonsterAttackContactSeconds later
        // if the player is still inside MonsterAttackReach. A miss costs it
        // MonsterAttackRecoverySeconds, which is the only time in the game the monster is
        // not closing.
        //
        // The numbers are not free: Validate() below asserts the outcome §06 asks for,
        // which is that a running player cannot escape a committed lunge and a sprinting
        // one can. That used to be read as §04's 주자 role expressed as one moment; the
        // roles are deleted and every runner has 질주, so it is now the single instant in
        // which §06's 0.8 m/s of margin is the whole difference between 탈락 and a lead.

        /// <summary>Where the creature commits to a lunge, metres. §06.</summary>
        public const float MonsterAttackRange = 1.8f;

        /// <summary>How fast it travels during the lunge, m/s. Faster than a chase — a pounce is not a sprint.</summary>
        public const float MonsterLungeSpeed = 7.0f;

        /// <summary>Seconds from commit to the strike landing. The window the Grab clip plays in.</summary>
        public const float MonsterAttackContactSeconds = 0.55f;

        /// <summary>How far the strike reaches, metres. The two bodies touching.</summary>
        public const float MonsterAttackReach = 0.8f;

        /// <summary>Seconds a missed lunge costs the creature. The only time it is not closing.</summary>
        public const float MonsterAttackRecoverySeconds = 0.8f;

        // ── §12's doors, as a thing a player can shut ────────────────────────
        // The map has placed lockable doors at every storey's bottleneck since the first
        // sketch and none of them did anything — MapSketch.Door() marked an edge and
        // MapValidator measured the detour it would force, and that was the whole
        // feature. A door that cannot be shut is a doorway.
        //
        // §06 leaves a player exactly one currency, which is time, and these three
        // numbers are what a door is worth in it.

        /// <summary>
        /// Seconds the creature needs to come through a shut door.
        /// <para>
        /// Longer than <see cref="AggroReleaseLineOfSightBreak"/> on purpose: a door is
        /// enough to break a chase you are already winning, and it is far short of a §07
        /// patrol sweep, so it never makes a room safe.
        /// </para>
        /// </summary>
        public const float DoorBreakSeconds = 4.5f;

        /// <summary>
        /// Seconds a player spends pulling one shut, standing still, facing the wrong way.
        /// The only reason not to shut every door in the building.
        /// </summary>
        public const float DoorShutSeconds = 1.1f;

        // --------------------------------------------------------------------
        // GONE: ClueReadSeconds, and NOT replaced by a renamed twin.
        //
        // §03 charged 2.5 s of holding a beam steady on a mark before it became
        // readable. The clue chain is deleted, but the number had already been
        // borrowed by a live race feature — GhostSession.EndMatchHoldSeconds — so a
        // dead 단서 constant was load-bearing for §09's 탈출 hold, which is how a
        // deleted system keeps a foothold in the build.
        //
        // GhostSession now derives that hold from DoorShutSeconds above: 1.1 s is
        // the interval this game has already decided means "I meant it", the beat a
        // runner stands still for to shut a 관문 they can never re-open. That is a
        // race-native derivation and it needs no constant of its own, so none was
        // invented here. A constant whose only reader is an assertion about itself
        // is the same disease under a better name.
        // --------------------------------------------------------------------

        /// <summary>
        /// How fast a half-broken door recovers when nothing is hitting it, as a fraction
        /// of the break rate. §07.
        /// <para>
        /// Below 1 because the building has to degrade over a match: §07 escalates the
        /// night and a door that healed at the rate it broke would make 동트기 전 the same
        /// building as 초저녁. Above 0 because a 관문 that never recovered would let the
        /// first runner through it strip the floor for everyone behind them, which inverts
        /// §02's 먼저 닿는다 into a reward for having been first already. Both halves are
        /// asserted in <see cref="Validate"/>.
        /// </para>
        /// <para>
        /// The old derivation was "a §04 섬광수 that buys thirty seconds must not hand back
        /// a whole door". There is no 섬광수, the only stun left is
        /// <see cref="MonsterStunSeconds"/> at 2.5 s, and nothing calls it — so the number
        /// it was quoted against did not exist either.
        /// </para>
        /// </summary>
        public const float DoorRepairFraction = 0.25f;

        /// <summary>Distance at which aggro can break, metres. §06.</summary>
        public const float AggroReleaseDistance = 12f;

        /// <summary>Line of sight must stay broken this long to release aggro, seconds. §06.</summary>
        public const float AggroReleaseLineOfSightBreak = 3f;

        /// <summary>
        /// Seconds a chase must cost a runner before the creature counts as a decision
        /// rather than as scenery — 하강's replacement for §12's 주자 테스트 band.
        /// <para>
        /// <b>Why a toll and not a survival rate.</b> §12's 실전 검증 grades a map on whether
        /// aggro can be broken (5~7/10 적정), and that was the right question when §04 gave
        /// one player in four a sprint and the creature could wipe a team. v1.0 deleted §04
        /// and handed 질주 5.6 m/s to all twenty runners, so "can you get away" now has one
        /// answer for everybody and §06's own arithmetic already gives it: a runner who is
        /// 10 m clear opens the remaining 2 m of <see cref="AggroReleaseDistance"/> in 2.5 s.
        /// What the race grades instead is what getting away <em>costs</em>, because §07
        /// says outright 「시간이 유일한 통화다」 and §02 decides the match on 먼저 닿는다 —
        /// order of arrival, not survival. See docs/BALANCE-FINDINGS.md F-013.
        /// </para>
        /// <para>
        /// <b>Where 3.4 s comes from — §01's list of two.</b> It is not chosen; it is the
        /// race's own price list. §01 leaves a runner exactly two ways to hinder a rival:
        /// 「방해 수단은 문과 괴물을 남에게 떠넘기는 것뿐이다」, and §06 calls the second one
        /// 「협동판에서 이것은 사고였다. 경주에서는 전술이다」. Price the first: shutting a door
        /// costs the shutter <see cref="DoorShutSeconds"/> and costs the pursuer
        /// <see cref="DoorBreakSeconds"/>, a swing of 3.4 s — §12-B's 「닫고 도망치면 3.4초를
        /// 번다」. For the second tool to be a tactic rather than a worse door, handing the
        /// creature to somebody has to move at least as much time as shutting one in their
        /// face. A chase cheaper than that is one §01 lists a tool for and nobody would use:
        /// 「escapable」 becomes 「ignorable」, which is the failure 720/720 was suspected of and
        /// which the toll, not the rate, is what actually detects.
        /// </para>
        /// <para>
        /// <b>The ceiling is <see cref="ChaseTollSecondsMax"/> and it comes from the pivot.</b>
        /// Being caught no longer ends a race — §06 sends the runner back to the cell they
        /// started from on B1 and they keep running — so a catch has a price in the same
        /// currency as a chase, and the two can be compared for the first time. See that
        /// constant for the arithmetic.
        /// </para>
        /// <para>
        /// <b>Measured, seed 20260802, all 720 places of the shipped building</b> (headless,
        /// <c>DescentMap.Build</c> + <c>RunnerTest</c>, 심야 4.8 m/s): toll min 3.4 s · p25
        /// 6.0 s · median 7.2 s · p75 9.2 s · max 25.5 s — <b>0 below this floor</b>, and one
        /// place above the storey-scaled ceiling (B1's 중심, 25.5 s against B1's 20.0 s). The
        /// 중심 band gives back a median 15.0 m of ground and the 외곽 band −7.5 m, which is
        /// §12-B③ 「외곽은 안전하고 중심은 위험하다」 as a number.
        /// </para>
        /// <para>
        /// Corroboration rather than derivation: §07's own cost table prices 「괴물을 한 번
        /// 떼어내기」 at ~30 s. The toll proper is 7.2 s median, and the bar the escape spent
        /// takes <see cref="SprintRecoveryDelaySeconds"/> + a share of
        /// <see cref="SprintStaminaRecoverySeconds"/> to put back — 18.4 s median together,
        /// 23.6 s at p75. §07's estimate is the whole encounter including the bar; this
        /// constant bands the part that is unambiguously time lost.
        /// </para>
        /// </summary>
        public const float ChaseTollSecondsMin = DoorBreakSeconds - DoorShutSeconds;

        /// <summary>
        /// Seconds a chase may cost a runner before escaping stops being worth doing —
        /// the other end of <see cref="ChaseTollSecondsMin"/>'s band.
        /// <para>
        /// <b>Derived from what being caught now costs, which is a number the co-op game
        /// did not have.</b> §06 used to end a caught runner's match (「잡히면 — 탈락」), so a
        /// catch had no price, only a verdict, and nothing could be compared against it.
        /// The race sends a caught runner back to the cell they started from on B1 with
        /// every storey gone. A catch therefore costs exactly the storeys already
        /// descended: on storey <c>k</c>, <c>k</c> × one storey.
        /// </para>
        /// <para>
        /// <b>The binding case is B1, and that is what this constant is.</b> On B1 a catch
        /// costs one storey; on B8 it costs eight, so B1 is the cheapest catch in the game
        /// and the tightest ceiling. A chase that costs more than the catch it prevents is
        /// a chase a runner should rationally stop running from — and §02 has no way to
        /// express that choice, because 「탈락에는 순위가 없다」 was replaced by a send-home
        /// that keeps you racing. One storey is §12-D's shortest legal 외곽→중심 path,
        /// <see cref="CentrePathMetresMin"/>, walked at <see cref="RunSpeed"/>: 90 ÷ 4.5 =
        /// 20.0 s.
        /// </para>
        /// <para>
        /// Read against §12-D's <em>written</em> floor rather than against the shipped
        /// geometry on purpose. The shipped storey measures 60~82.5 m entry-to-middle
        /// (13.3~18.3 s), which is outside §12-D's own band — a map defect, not a reason to
        /// lower the ceiling. A constant derived from geometry that is currently wrong would
        /// move every time the geometry moved.
        /// </para>
        /// <para>
        /// <b>Measured:</b> 7 of 720 places charge more than this flat 20.0 s, one per storey
        /// except B8, every one of them the 중심 cell that throws an escaping runner 67.5 m
        /// back out. Against the storey-scaled ceiling (<c>k</c> × 20.0 s) only <b>B1's</b> is
        /// a breach; on B2–B7 a 25.5 s escape is a bargain against a 40~140 s catch.
        /// </para>
        /// </summary>
        public const float ChaseTollSecondsMax = CentrePathMetresMin / RunSpeed;

        /// <summary>Runner sprint duration on a full bar, seconds. §06.</summary>
        public const float SprintStaminaSeconds = 12f;

        /// <summary>Time to refill an empty stamina bar, seconds. §06.</summary>
        public const float SprintStaminaRecoverySeconds = 20f;

        /// <summary>
        /// Distance a Runner covers on one full sprint, metres. §05 revised this
        /// from 67 m to 60 m once the 95% peek multiplier was accounted for.
        /// Consumed by the map rules in §12.
        /// </summary>
        public const float SprintMaxTravelDistance = 60f;

        /// <summary>
        /// Seconds after a sprint ends before the bar starts refilling, seconds.
        /// <para>
        /// §06 gives a refill time but no restart rule, and the pair 12 s drain /
        /// 20 s refill means a burst returns 60% of what it costs — so a player
        /// alternating Shift every frame drains and refills in the same breath and
        /// parks the bar at whatever level they like, forever. The delay is what
        /// makes a burst shorter than the delay return nothing, so committing to a
        /// sprint always beats tapping one.
        /// </para>
        /// <para>
        /// Not a documented value: §16 lists §05's 속도 배율 and §06's 어그로 수치 as
        /// settled, and this is neither. It is the smallest rule that makes the bar
        /// a resource. It does not close the sprint-cycling gap in
        /// docs/BALANCE-FINDINGS.md, which is a §06 tuning decision.
        /// </para>
        /// </summary>
        public const float SprintRecoveryDelaySeconds = 1.0f;

        // ====================================================================
        // §06 — Monster state machine timings.
        // ====================================================================

        /// <summary>Alert gives up and returns to patrol after this long without a lead, seconds. §06.</summary>
        public const float AlertGiveUpSeconds = 3f;

        /// <summary>Search abandons the last known position after this long, seconds. §06.</summary>
        public const float SearchGiveUpSeconds = 15f;

        /// <summary>A standstill lasts this long before patrol resumes, seconds. §06.</summary>
        public const float StandstillSeconds = 5f;

        /// <summary>Lower bound of the random gap between standstills while patrolling, seconds. §06.</summary>
        public const float StandstillIntervalMin = 15f;

        /// <summary>Upper bound of the random gap between standstills while patrolling, seconds. §06.</summary>
        public const float StandstillIntervalMax = 30f;

        /// <summary>How far around the last known position Search sweeps, metres. §06.</summary>
        public const float SearchRadius = 12f;

        /// <summary>
        /// How far the monster can see a player, metres.
        /// <para>
        /// §06's state table names "시야 확보" as the way into 추격 but never numbers
        /// it, so the value is read off §12 instead: the open-space rule there
        /// guarantees 15~25 m sight lines and the first map sketch labels the hall
        /// "시야 20m". §12's aggro table also runs out to a 15 m start distance, so
        /// anything below 15 m would make the design's own best case unreachable,
        /// and anything above 25 m would let the monster acquire across the cover
        /// the map is required to provide. Provisional in the §16 sense.
        /// </para>
        /// </summary>
        public const float MonsterSightRange = 20f;

        /// <summary>
        /// Half-angle of the monster's vision, degrees.
        /// <para>
        /// §06 gives no cone and only presupposes that the vision has a direction —
        /// 시야 확보 is a transition into 추격, so there is a facing to be outside of.
        /// 90° is the weakest claim that keeps that true: the monster sees the
        /// hemisphere it faces and nothing behind it, which leaves §12's "괴물이
        /// 지나가도 즉시 발각 안 됨" a question of geometry rather than of a tuned cone.
        /// A runner who has slipped past a creature's shoulder is past it, and that is
        /// the one thing a race needs the cone to decide.
        /// </para>
        /// </summary>
        public const float MonsterSightHalfAngle = 90f;

        /// <summary>
        /// How far a footstep carries to the monster at full effort on a perfectly
        /// telling surface, metres. Scaled down by the surface's
        /// <c>ListenerClarity*</c> and by how hard the player is working, so this is a
        /// ceiling rather than a range.
        /// <para>
        /// <b>Nothing raised a sound cue before this existed.</b> §06's table gives 순찰
        /// exactly one transition — 소리 감지 → 경계 — and no sight edge at all, which
        /// is deliberate and correct: the monster is not supposed to acquire you across
        /// a room just by facing you. But the game never reported a single sound to it.
        /// The only caller of <c>MonsterAgent.ReportSound</c> in the whole project was
        /// an editor screenshot tool. So the one door out of 순찰 was nailed shut, and
        /// the creature patrolled forever — it could not chase, could not catch, could
        /// not kill, and §09's ghost could never be entered by anything but a fall. A
        /// player could stand 43 cm in front of it and be walked past. That is exactly
        /// what the owner reported, and <c>MonsterKillTests</c> now measures it.
        /// </para>
        /// <para>
        /// <b>Why 35 m.</b> It is bounded on both sides by rules that already exist.
        /// Below <see cref="AudibleRangeMetres"/> (40 m), because §12 makes 소리 the map
        /// and a creature that heard further than a runner does would be reading a map
        /// only it can read — the bound §04 used to justify by handing the 청음사 a job,
        /// and which <see cref="Validate"/> now asserts in §12's terms instead.
        /// Above <see cref="MonsterSightRange"/> (20 m), because §03 makes darkness the
        /// game's central information problem — a creature that saw further than it
        /// heard would be a creature the dark protects you from, and the dark is
        /// supposed to blind you, not it. Between those, 35 m is a zone
        /// diagonal (§12's band is 30~40 m): a player sprinting on 금속 grating is heard
        /// from anywhere in the zone they are in, and from nowhere outside it.
        /// </para>
        /// <para>
        /// The two multipliers do the rest, and they are §12's own surface table rather
        /// than a second set: 카펫 at 0.22 clarity carries a walk about 2 m
        /// and a sprint about 8, which is what makes 병동 the floor you can run on;
        /// 금속 at 1.00 carries a walk 10 m, which is what makes a stairwell the place
        /// you cannot. One quantity for "the noise that blinds you" and "the noise that
        /// draws it" is what keeps §10's dilemma a single dilemma.
        /// </para>
        /// </summary>
        public const float MonsterFootstepHearingRange = 35f;

        /// <summary>
        /// How close a player has to be before a monster that is not hunting notices
        /// them by sight alone, metres.
        /// <para>
        /// <b>§06's table gives 순찰 no sight edge and that is right at range.</b> A
        /// creature that acquired you across a lit room the instant it faced you would
        /// erase §12's cover and the whole reason to move quietly. So the rule stands:
        /// a patrol does not hunt what it merely sees.
        /// </para>
        /// <para>
        /// <b>It is wrong at contact.</b> Under the literal table a player can stand
        /// 43 cm from the creature's face, in the open, and be walked past — measured,
        /// not imagined, in <c>MonsterKillTests</c>. Nothing about that reads as a
        /// designed rule from inside the game; it reads as a broken monster, and a
        /// player who discovers it stops being afraid of the thing the entire design
        /// is built to make them afraid of.
        /// </para>
        /// <para>
        /// <b>Why 4 m.</b> <see cref="MonsterAttackRange"/> (1.8 m) plus one second of
        /// a walking player (<see cref="WalkSpeed"/>, 2.0 m/s): if you are close enough
        /// that a second of walking puts you inside its reach, you have walked into it.
        /// That keeps noticing and striking two separate events — the creature turns
        /// before it commits — and it is a fifth of <see cref="MonsterSightRange"/>, so
        /// every acquisition beyond arm's length still has to be earned with noise.
        /// <c>MonsterTests.Patrol_DoesNotChaseOnSight_AsSection06LiterallyWrites</c>
        /// holds that line at 5 m and still passes.
        /// </para>
        /// </summary>
        public const float MonsterPatrolNoticeRange = 4.0f;

        /// <summary>
        /// Fewest runners a race needs. Two, because one runner is not a race — nothing in
        /// §12's geometry means anything without somebody to be ahead of.
        /// </summary>
        public const int RaceRunnersMin = 2;

        /// <summary>
        /// Most runners a race takes. §11.
        /// <para>
        /// The cap is not a network budget, it is the map. §12-A fixes the gate counts at
        /// 4 · 2 · 1 and refuses to scale them with the field, because widening them would
        /// turn a twenty-player race into twenty one-player races that never meet. Twenty is
        /// where the last gate is contested rather than queued — beyond it the inner cell
        /// stops being a decision and becomes a line.
        /// </para>
        /// </summary>
        public const int RaceRunnersMax = 20;

        /// <summary>
        /// Creatures the descent puts on each storey. §12-B③.
        /// <para>
        /// <b>§12-B③ states it and no section counts it.</b> 「괴물이 안쪽을 순찰한다 …
        /// 외곽은 안전하고 중심은 위험하다」 is written about every floor, and the audit says
        /// what the building actually does: <em>no creature on 7 of 8 storeys, so §06 was not
        /// asked of them</em> (/tmp/r4_gen.log, 2026-08-03). One <c>MonsterStart</c> exists,
        /// on B5.
        /// </para>
        /// <para>
        /// <b>Why the count is per storey rather than per building.</b> The descent's NavMesh
        /// bakes as one island per floor — the audit's own line, <em>islands 8 ← the surface
        /// is in pieces</em>, which is correct here because a 투하구 is a fall and not a path.
        /// A creature therefore cannot walk to another floor, ever. §07 compounds it: its
        /// 순찰 column is counted in 구역 (1 at 초저녁, 2 at 밤) and this building is one zone
        /// per storey, so even the floor that has a creature is patrolled by it for one or two
        /// zones' worth of a race §01 sizes at 12~20 minutes. Anything below one per storey
        /// means the storey has no §06 in it at all, and 「escapable from everywhere」 stops
        /// being a statement about the map.
        /// </para>
        /// <para>
        /// <b>Why exactly one, and why it does not scale with the field.</b> §12-A refuses to
        /// widen the gates for a bigger race — 「관문 수는 인원과 무관하게 고정이다 … 붐비는
        /// 것이 설계」 — and the creature is the other fixed hazard on the same floor, so it
        /// takes the same rule: two runners and twenty runners meet the same building. One is
        /// also what §01 needs to stay true — 「괴물은 이길 수 없는 적」 is a statement about a
        /// thing you avoid, and a floor with several of them is a floor you route around
        /// rather than descend.
        /// </para>
        /// <para>
        /// <b>Nothing reads it yet, and authoring alone will not make it true.</b>
        /// <c>DescentMap.PlaceStarts</c> now calls <c>MonsterStart</c> once per storey, but
        /// <c>MapSketch.MonsterStart</c> assigns a single <c>_monsterStart</c> field — last
        /// write wins — and the runtime below it is single-creature too: <c>MatchMap</c>
        /// exposes one <c>MonsterSpawn</c> and <c>MatchDirector</c> owns one <c>_monster</c>.
        /// So the shipped map still has exactly one creature and the audit still reads
        /// <em>1 of 8 storeys</em>. This constant is what the sketch's spawn list and the
        /// director's spawn loop should be written against. docs/BALANCE-FINDINGS.md F-013 §5
        /// has the three links and which one is done.
        /// </para>
        /// </summary>
        public const int MonstersPerStorey = 1;

        /// <summary>
        /// How close the monster must come to a path point to count as having
        /// reached it, metres. §06 — not a balance value; it exists so that path
        /// following terminates. It must stay below one <see cref="FixedStep"/> of
        /// travel (4.8 × 0.02 = 0.096 m), or the monster stalls short of every
        /// waypoint and jitters instead of walking.
        /// </summary>
        public const float MonsterWaypointTolerance = 0.05f;

        // ====================================================================
        // GONE: §08's carry-weight bands — WeightFreeMax, WeightLightMax,
        // WeightHeavyMax, WeightMulLight, WeightMulHeavy, WeightMulOverloaded,
        // and the three Validate() clauses that ordered them.
        //
        // The table was "≤5 정상 · 6~10 −15% · 11~15 −30% · ≥16 질주 불가": how much
        // slower you move for how much loot you are carrying out. Last round left
        // them in place with a note that "nothing in this file can decide whether
        // the race will ever put a load on a runner." That was the wrong test —
        // the question is not whether a load could exist one day, it is whether
        // anything reads these today. Nothing did: measured against the whole
        // tree, every reader was a test asserting the bands against each other.
        // Six numbers and three assertions, shipping in HorrorGame.Core.dll,
        // describing a curve no runner could ever be on.
        //
        // What still slows a runner is stance (crouch) and direction (§05's
        // 곁눈질), both of which are in MovementContext.LoadMultiplier and the
        // §05 table. Re-adding a weight band means re-adding a thing to carry.
        // ====================================================================

        // --------------------------------------------------------------------
        // GONE: the §08 economy. BaseCarryCapacity, BagCapacityBonus,
        // BagSpeedMultiplier, SharedCarryMaxCarriers, the four LootWeight* and
        // four LootValue* rows, WalletStartingCredits, ShopVendorMarkup, all
        // thirteen ShopCost*, EconomyReferenceHaulersPerDescent and
        // EconomyReferenceCellsPerDescent, plus the §16-2 price derivation essay
        // that tied them together.
        //
        // 하강 is 선착순 미로탈출. Twenty runners drop onto the rim of B1 and the
        // first to the middle of B8 wins; nobody picks anything up, so there is no
        // weight to carry, no credits to earn and no shop to spend them in. The
        // types went in the last round and these numbers were left behind — they
        // shipped in HorrorGame.Core.dll reachable and unread, which is exactly the
        // failure this round exists to end.
        //
        // The weight *bands* went with them, in the round recorded in the tombstone
        // immediately above. This paragraph used to say they were "a separate question
        // and are still asserted by Validate()", which was true when it was written and
        // false twenty lines later — two tombstones in one file disagreeing about
        // whether six constants existed. Corrected here rather than left as a footnote:
        // a tombstone that is wrong about what is buried is worse than no tombstone,
        // because the next reader trusts it instead of grepping.
        //
        // Do not re-add a price list. A shop is a thing you do between races, and
        // this game does not have a between.
        // --------------------------------------------------------------------

        // --------------------------------------------------------------------
        // GONE: §03/§05 objective carrying. ObjectiveCarrySpeedMultiplier,
        // ObjectiveWeight and ObjectiveEscortMinPlayers.
        //
        // The co-op game had one object that had to be walked out of the building
        // by a pair — "누가 들 것인가" was its last decision. The race has no such
        // object. Every runner carries nothing and races the same descent, so
        // there is no carry to slow, no weight for it to occupy, and no escort to
        // require a second living player.
        //
        // ObjectiveCarrySpeedMultiplier is the one that had teeth: it was still
        // being multiplied into every movement resolve by
        // SpeedResolver.ContextMultiplier, on a branch that PlayerMotor.BuildContext
        // hard-coded to false. A dead multiplier on the hot path of a footrace.
        // --------------------------------------------------------------------

        // ====================================================================
        // §04 — GONE. The five 직업 and every number that parameterised them.
        //
        // §04 v1.1: 「캐릭터는 다 똑같이 생겨도되지」. Twenty runners drop onto the
        // rim of B1 and the first to the middle of B8 wins. There is no role to
        // pick, so there is no ability to parameterise and no 자재 to spend on one.
        //
        // DELETED with Core/Abilities (ObserverAbility, ListenerAbility,
        // FlasherAbility, EngineerAbility, RunnerAbility) and Core/Roles — every
        // name below is absent from this file and from the tree:
        //   ObserverRange, ObserverStillSeconds
        //   ListenerErrorRadiusMax
        //   FlashCooldownSeconds, FlashRange, FlashConeHalfAngle
        //   EngineerDoorLockSeconds, EngineerBarricadeSeconds, EngineerTrapSeconds,
        //     EngineerSafeSeconds, EngineerZoneLightSeconds,
        //     EngineerTrapMaterialCost, EngineerBarricadeMaterialCost,
        //     EngineerSafeMaterialCost, EngineerZoneLightMaterialCost,
        //     EngineerTrapNoiseLevel
        //   RoleCount
        //
        // DELETED THIS ROUND, five that the list above already claimed were gone and
        // which had gone on shipping in HorrorGame.Core.dll with no reader anywhere —
        // not in core/, not in unity/, not even in a test asserting them against
        // themselves. Three still carried §04 in their own doc comment, which is how
        // they survived two rounds of deletion: a reader who greps for §04 finds the
        // tombstone, and a reader who greps for the constant finds a derivation that
        // sounds settled.
        //   ListenerFixIntervalSeconds        0.5 s   §04's fix cadence
        //   ListenerDirectionErrorMaxDegrees  30°     §04's bearing error
        //   ListenerMinDirectionSpeed         0.25    §04's "moving enough to have a bearing"
        //   ListenerNearErrorFactor           0.25    §04's error floor at zero distance
        //   RunnerTauntRange                  25 m    §04's 주자 pulling aggro deliberately
        // None of the five was named in Validate(), so no assertion went with them and
        // nothing about the race lost a guard. The one relationship any of them was
        // corroborating was FlashlightNoticeDistance == RunnerTauntRange, an argument
        // by coincidence rather than by derivation; the real bound on that constant is
        // §12's own sight-line cap, and Validate() now asserts it directly instead of
        // leaning on a role that no longer exists.
        //
        // RENAMED, not deleted — the mechanism survived and only the role's name went:
        //   FlashStunSeconds       -> MonsterStunSeconds     (below; it pins a clip)
        //   ObserverRange          -> HallClearSightMin      (below; it is §12's 개방 공간)
        //   EngineerReachDistance  -> InteractReachMetres    (below; it is arm's length)
        //
        // KEPT UNDER THEIR ORIGINAL NAMES, and this list said they were deleted for two
        // rounds while they ran on the hot path. They were never §04's; they were only
        // declared inside its block, and neither name carries a role stem to rename off:
        //   StillSpeedThreshold — the epsilon at which a runner counts as standing
        //     still. The race needs it because PlayerAnimatorDriver, PlayerFootsteps and
        //     ViewMotionTuning must all answer "is this body moving" with one number,
        //     and a runner who is idle to the animator, walking to the mix and bobbing
        //     to the camera is three bugs wearing one coat.
        //   AudibleRangeMetres — the outer bound of the audio world. The race needs it
        //     because FootstepAudio, MonsterAudio and NetInterestScope.PerceptionRange
        //     all set their falloff and their culling radius to it, so it is the
        //     distance past which the game stops being audible at all.
        //
        // KEPT, and they are §12's rather than §04's despite the stem:
        //   ListenerSelfNoiseThreshold, below. The level above which a runner's own
        //     noise masks what they can hear — now a fact about every runner rather
        //     than one role's price. §12 makes 발소리 the map; this decides when you
        //     have stopped being able to read it.
        //   ListenerClarity*, below. Eight floor materials, read by MapZone.ClarityOf,
        //     which is what MatchDirector's footsteps and
        //     VoiceRules.MonsterHearingRangeMetres both ask. §12's material table, not
        //     a role's stat block. These two are the only survivors whose names still
        //     want moving off the 청음사 stem; that is a rename with a blast radius in
        //     fourteen other files, and it is not this round's deletion.
        // ====================================================================

        /// <summary>
        /// How long the creature stays suspended once something stuns it, seconds.
        /// <para>
        /// <b>This was <c>FlashStunSeconds</c>, §04's 섬광수 flash.</b> The role is
        /// deleted and <c>MonsterAgent.Stun</c> currently has NO CALLER — nothing in
        /// the race can stun anything. The number survives the rename because the
        /// mechanism it measures is not §04's: <c>MonsterBrain.Stun</c> suspends the
        /// state machine including the aggro-release timer, the Stunned animation
        /// clip is authored to exactly this length, and
        /// <c>AssetImportValidator</c> fails the import if the two disagree.
        /// Deleting the constant would unpin the clip from anything.
        /// </para>
        /// <para>
        /// <b>This is a live finding, not a settled design.</b> Either the race grows
        /// a way to stun the creature or the whole suspended state — brain field,
        /// animator state, audio bank, import rule — should go together. It is
        /// recorded here rather than silently kept so the decision has an address.
        /// </para>
        /// </summary>
        public const float MonsterStunSeconds = 2.5f;

        /// <summary>
        /// Shortest clear sight line §12's 개방 공간 must hold at eye height, metres.
        /// <para>
        /// <b>The dressing pass used <c>ObserverRange</c> for this</b>, because §04's
        /// 관측자 worked at 15 m and a hall that could not hold 15 m of sight was a hall
        /// the role could not work in. The role is deleted; §12's own requirement —
        /// "개방 공간 15~25 m" and 멀리서 어그로 — is what the check was really enforcing
        /// and it now says so.
        /// </para>
        /// <para>
        /// Above <see cref="AggroReleaseDistance"/> (12 m) by design: a runner crossing
        /// a hall has to be able to SEE the creature from further than the distance at
        /// which it would lose them, or the open rooms are places you are surprised in
        /// rather than places you commit to crossing.
        /// </para>
        /// </summary>
        public const float HallClearSightMin = 15f;

        /// <summary>
        /// Ground speed below which a runner counts as standing still, m/s.
        /// <para>
        /// <b>This was <c>StillSpeedThreshold</c>.</b> §04's 관측자 had to hold
        /// still to read the creature, and §05 decided mouselook stayed free while the
        /// feet were pinned — so it gated translation only. The role is deleted and the
        /// threshold is not the role's: it is the epsilon three separate systems use to
        /// decide whether the player is moving at all. <c>PlayerAnimatorDriver</c> picks
        /// idle below it, <c>PlayerFootsteps</c> stops stepping below it, and
        /// <c>ViewMotionTuning</c> stops bobbing. One number so those three never
        /// disagree — a runner who is idle to the animator, walking to the mix and
        /// bobbing to the camera is three bugs wearing one coat.
        /// </para>
        /// </summary>
        public const float StillSpeedThreshold = 0.05f;

        /// <summary>
        /// The furthest a sound in this game is worth carrying at all, metres.
        /// <para>
        /// <b>This was <c>AudibleRangeMetres</c>, §04's 청음사 radius</b> — how far
        /// that role could localise the creature by ear. The role is deleted; the
        /// distance is not §04's and never was. It is the outer bound of the audio
        /// world: <c>FootstepAudio</c> and <c>MonsterAudio</c> set their 3D falloff to
        /// it, and <c>NetInterestScope.PerceptionRange</c> uses it as one of the three
        /// radii below which culling would delete something a player is entitled to
        /// perceive. Deleting it would have silently re-scaled every falloff in the mix.
        /// </para>
        /// <para>
        /// Above <see cref="MonsterFootstepHearingRange"/> (35 m) on purpose: a runner
        /// must be able to hear the creature from further away than it can hear them,
        /// or §12's 「소리 → 바닥 재질이 지도다」 is a map only the creature can read.
        /// </para>
        /// </summary>
        public const float AudibleRangeMetres = 40f;

        /// <summary>
        /// Self-noise above this level masks what a runner can hear. Was §04's
        /// "자기가 소리를 내면 못 듣는다" — the 청음사's price — and is now everyone's:
        /// §12 makes 소리 the map, so running flat out is how you stop being able
        /// to read it.
        /// </summary>
        public const float ListenerSelfNoiseThreshold = 0.35f;

        // --------------------------------------------------------------------
        // §12's surface table. Eight materials and one fallback, read by
        // MapZone.ClarityOf — the single answer to "how much does this floor give
        // away", asked by MatchDirector's footsteps, by MonsterAgent's hearing and
        // by VoiceRules.MonsterHearingRangeMetres.
        //
        // The 청음사 stem is historical. §04 owned the ability that read this table;
        // §12 owns the table — 「소리 → 바닥 재질이 지도다」 — and the table outlived
        // the ability because a race still has to answer which floor is worth
        // crossing. Validate() pins both ends of it against MonsterSightRange, which
        // is the version of the question a player can actually feel: on 카펫 the
        // creature sees you first, on 금속 it hears you first.
        //
        // The four §04 estimator constants that used to sit above this table —
        // ListenerNearErrorFactor, ListenerDirectionErrorMaxDegrees,
        // ListenerFixIntervalSeconds, ListenerMinDirectionSpeed — are gone. They
        // parameterised how wrong the 청음사's reading was allowed to be, and there
        // is no reading; nothing in the race consumed them and nothing asserted
        // them. See the §04 tombstone above.
        // --------------------------------------------------------------------

        /// <summary>How clearly 나무 gives the monster away. §12: 삐걱 — a creak pins the spot.</summary>
        public const float ListenerClarityWood = 0.80f;

        /// <summary>How clearly 타일 gives the monster away. §12: 딱딱, 반향 — loud, and the reverb helps.</summary>
        public const float ListenerClarityTile = 0.85f;

        /// <summary>How clearly 자갈 gives the monster away. §12: 부스럭 — broadband but soft.</summary>
        public const float ListenerClarityGravel = 0.70f;

        /// <summary>
        /// How clearly 콘크리트 gives the monster away. §12: 둔탁 — the dullest
        /// floor on the map, which makes a concrete zone the creature's best approach
        /// and gives a runner a reason to care which room it is in.
        /// </summary>
        public const float ListenerClarityConcrete = 0.50f;

        /// <summary>
        /// How clearly 금속 gives the monster away. §12: 울림 — a stairwell
        /// transit is the single clearest signal in the map, which is what makes
        /// "지금 계단이야" a usable call.
        /// </summary>
        public const float ListenerClarityMetal = 1.00f;

        /// <summary>
        /// 침수. §12 — the loudest thing to stand on, above even 금속.
        /// <para>
        /// Water is the one surface that cannot be crossed quietly at any speed, which is
        /// what makes a flooded storey a decision rather than a corridor: it is the
        /// fastest way down and it announces you the whole way.
        /// </para>
        /// </summary>
        public const float ListenerClarityWater = 1.00f;

        /// <summary>파헤쳐진 흙. §12 — soft, and quieter than concrete. Somebody dug this floor up.</summary>
        public const float ListenerClarityEarth = 0.40f;

        /// <summary>
        /// 카펫. §12 — the quietest surface, below even an unknown one.
        /// <para>
        /// Deliberately the table's blind spot. §12 makes 소리 the map, and a map with no
        /// blind spot on it has no route worth knowing; carpet is where the creature
        /// arrives without being announced, and where a runner who knows the building goes
        /// when they want the same. <see cref="Validate"/> holds it there: on carpet even
        /// a sprint must carry less far than <see cref="MonsterSightRange"/>.
        /// </para>
        /// </summary>
        public const float ListenerClarityCarpet = 0.22f;

        /// <summary>
        /// Clarity used for a surface with no material assigned. §12 fails a map
        /// with <c>FloorMaterial.None</c> in it, so this only ever applies to
        /// geometry that already violates the map rules — it degrades the floor's
        /// signal rather than crashing, and stays worse than every real floor
        /// except 카펫 so the omission is visible in play.
        /// <para>
        /// This doc comment used to sit above <see cref="ListenerClarityWater"/>, two
        /// members away, leaving that constant with two <c>&lt;summary&gt;</c> tags and
        /// this one with none. Nothing caught it because the core projects do not set
        /// <c>GenerateDocumentationFile</c>, so the compiler never reads these comments
        /// at all — the same class of blind instrument as a DLL sweep that cannot see a
        /// scene.
        /// </para>
        /// </summary>
        public const float ListenerClarityUnknown = 0.35f;

        // GONE with §04's 주자: RunnerTauntRange, 25 m. It was the furthest the role
        // could deliberately pull the creature's aggro onto itself so the rest of the
        // team could move. Nobody taunts in a race — 하강 has no team to buy time for,
        // and aggro starts wherever the creature happens to acquire you. It was already
        // named in the §04 tombstone above as deleted and was still declared here, with
        // no reader in core/, unity/ or tools/.

        /// <summary>
        /// Fraction of the sprint bar a runner must recover before sprinting
        /// again.
        /// <para>
        /// §06 promises "주자도 스태미나가 끝나면 잡힌다". Without a floor that
        /// promise is empty: drain is 1 s per second and refill is 12/20 = 0.6 s
        /// per second, so holding Shift on an empty bar would switch the sprint
        /// on and off every tick and hand out an unlimited 60%-duty sprint. See
        /// docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        public const float SprintReengageStaminaFraction = 0.25f;

        /// <summary>
        /// How close a runner must stand to something to work on it, metres.
        /// <para>
        /// <b>This was <c>EngineerReachDistance</c>, §04's 정비공 working distance</b> —
        /// "the work is hands-on at the site, so stepping away abandons it instead of
        /// finishing at range". The role is deleted and the distance is not the role's:
        /// it is arm's length, and it is what <c>PlayerInteractor</c> casts and what
        /// <c>Interactable</c> answers with. The race has exactly one thing to reach for
        /// — §12-B's doors — and this is how far away you can be and still shut one.
        /// </para>
        /// </summary>
        public const float InteractReachMetres = 2.5f;

        // GONE with §04's 정비 자재: EngineerDoorLockMaterialCost and
        // EngineerZoneLightMaterialCost. Locking a door and raising a zone light each
        // cost the 정비공 a material; there is no 정비공, no material, and a door is now
        // shut by standing still for DoorShutSeconds rather than by spending anything.





        // ====================================================================
        // §03 — Light. "어둠 = 목표의 잠금장치."
        //
        // The header used to read "§03 / §08 — Light and battery". §08 is deleted and
        // so is the cell; what is left is a torch that simply works and the two prices
        // below it. The section is §03's alone now.
        // ====================================================================

        /// <summary>Standard flashlight cone radius, metres. §03.</summary>
        public const float FlashlightRange = 12f;

        /// <summary>Standard flashlight cone half-angle, degrees. §03: the beam is narrow.</summary>
        public const float FlashlightHalfAngle = 22f;

        // GONE with §08's shop: UpgradedFlashlightRangeMultiplier. It doubled the
        // beam for a price, and 「brighter cuts both ways」 was the showpiece trade
        // of the whole item list. With one grade of torch there is nothing to
        // upgrade from. Its last reader was NetInterestScope.SecretRange, which
        // asked "how far can a player light something up" and multiplied by a
        // factor nobody could buy.

        // --------------------------------------------------------------------
        // GONE: the battery clock and the shop's light items.
        // BatterySecondsPerCell, BatteryIdleDrainMultiplier, BatterySwitchOnCost,
        // UpgradedFlashlightDetectionMultiplier and FlareSeconds.
        //
        // Core/Light/FlashlightState deleted the cell itself last round and says
        // why at length: "A light you have to maintain is a chore bolted onto a
        // footrace." The runner is issued one torch that simply works. These five
        // numbers are what was left of the fuel gauge, the 강화 손전등 and the
        // 조명탄 — all three were things you bought, and there is no shop.
        //
        // The darkness is untouched. It is priced by the two things that still
        // exist: FlashlightNoticeDistance (light is seen from further than the
        // monster can see you) and the 그늘 (dark fills a pool that takes your
        // voice). A third clock was redundant even when the co-op game shipped.
        // --------------------------------------------------------------------

        /// <summary>Fraction of range lost during 심야. §07: 손전등 반경 −30%.</summary>
        public const float LateNightFlashlightPenalty = 0.30f;

        /// <summary>Radius a zone light illuminates, metres. §03.</summary>
        public const float ZoneLightRadius = 18f;

        /// <summary>
        /// How far away the monster notices a lit standard flashlight, metres.
        /// <para>
        /// §03 lists "괴물이 잘 본다" as the flashlight's price and never gives the
        /// number. (§08 used to double it for the 강화 손전등; there is one grade of
        /// torch now, so there is nothing to double.) It is bounded
        /// from both sides. It must exceed <see cref="MonsterSightRange"/> (20 m) or
        /// §03's price is void — the monster would already see the body at that
        /// distance and switching the light on would cost nothing. And §12 caps the
        /// sight lines a legal map has to provide at
        /// <see cref="LineOfSightBreakSpacingMax"/> (25 m), so anything beyond that
        /// is a distance the map is never obliged to make usable — a beam noticed
        /// from 30 m would be noticed from further than any §12-legal map guarantees
        /// you can see the noticing. 25 m is the only value that satisfies both, and
        /// <see cref="Validate"/> now asserts both ends.
        /// </para>
        /// <para>
        /// The upper bound used to be corroborated by a third fact — that 25 m was also
        /// <c>RunnerTauntRange</c>, so "the 주자 can pull aggro from exactly as far as a
        /// lit teammate can be given away". That was an argument by coincidence between
        /// two numbers, not a derivation, and the 주자 is deleted; the constant went with
        /// it. §12's sight-line cap is the bound that was always doing the work, and it
        /// is asserted rather than admired.
        /// </para>
        /// <para>
        /// Provisional in the §16 sense — no section settles it. This paragraph used to
        /// end "×2 from here (§08) lands at 50 m, which no §12-legal map can supply a
        /// sight line for; that consequence is pinned by <c>LightTests</c>". Both halves
        /// are now false: <c>FlashlightState.NoticeDistance</c> returns this value or
        /// zero and has no multiplier to apply, and <c>LightTests</c> asserts exactly
        /// that — there is no 50 m case left for it to pin. The finding is recorded in
        /// docs/BALANCE-FINDINGS.md against the co-op game it belonged to.
        /// </para>
        /// </summary>
        public const float FlashlightNoticeDistance = 25f;

        // GONE with §08's 조명탄: FlareIgniteNoiseLevel. It priced striking a flare
        // at 0.70 on the self-noise scale so a 청음사 who lit one cut their own
        // feed. There are no flares to strike and no 청음사 to blind.

        // ====================================================================
        // §07 — Time is the only currency. Match length 25–35 min (§01).
        // ====================================================================

        /// <summary>Length of one threat tier, seconds. §07: eight-minute bands.</summary>
        public const float ThreatTierSeconds = 8f * 60f;

        /// <summary>Soft target match length, seconds. §01: 25–35 minutes.</summary>
        public const float TargetMatchSecondsMin = 25f * 60f;

        /// <summary>Upper end of the target match length, seconds. §01.</summary>
        public const float TargetMatchSecondsMax = 35f * 60f;

        /// <summary>
        /// Rows in §07's threat table. The last one has no end — after 32 min the
        /// night stops escalating and simply stays at "생존 불가 수준", so the curve
        /// saturates instead of running off the end of the table.
        /// </summary>
        public const int ThreatTierCount = 5;

        /// <summary>
        /// Monster speed during 초저녁 (0–8 min), m/s. §07.
        /// <para>
        /// §07's speed column is absolute, not a multiplier: it replaces
        /// <see cref="MonsterBaseSpeed"/> for the tier rather than scaling it. The
        /// 4.8 of §06 is the 심야 row, reached only at 16 min.
        /// </para>
        /// </summary>
        public const float ThreatSpeedEarlyEvening = 4.4f;

        /// <summary>Monster speed during 밤 (8–16 min), m/s. §07. Absolute — see <see cref="ThreatSpeedEarlyEvening"/>.</summary>
        public const float ThreatSpeedNight = 4.6f;

        /// <summary>
        /// Monster speed during 심야 (16–24 min), m/s. §07. This is the row that
        /// equals <see cref="MonsterBaseSpeed"/>, so every piece of geometry §12
        /// derives from 4.8 m/s is exact for this tier and only this tier.
        /// </summary>
        public const float ThreatSpeedLateNight = 4.8f;

        /// <summary>Monster speed during 새벽 (24–32 min), m/s. §07 — the escort tier.</summary>
        public const float ThreatSpeedPreDawn = 5.0f;

        /// <summary>Monster speed during 동트기 전 (32 min+), m/s. §07: "생존 불가 수준".</summary>
        public const float ThreatSpeedBeforeSunrise = 5.2f;

        /// <summary>Zones the monster patrols during 초저녁. §07: "1개 구역" — the map is mostly safe early.</summary>
        public const int ThreatPatrolZonesEarlyEvening = 1;

        /// <summary>Zones the monster patrols during 밤. §07: "2개 구역". Later tiers are proportional to map size, not absolute.</summary>
        public const int ThreatPatrolZonesNight = 2;

        /// <summary>
        /// Probability that a patrolling monster takes the §06 standstill when its
        /// 15–30 s timer fires, during 초저녁. §07 says "잦음" and gives no number;
        /// the words 잦음/보통/드묾/없음 are read as this descending set of chances.
        /// Provisional in the sense of §16 — silence is the design's best weapon
        /// ("침묵이 가장 무서운 소리다", §06), so this wants playtest, not arithmetic.
        /// </summary>
        public const float ThreatStandstillChanceFrequent = 0.75f;

        /// <summary>Standstill chance during 밤. §07: "보통". See <see cref="ThreatStandstillChanceFrequent"/>.</summary>
        public const float ThreatStandstillChanceNormal = 0.45f;

        /// <summary>Standstill chance during 심야. §07: "드묾". See <see cref="ThreatStandstillChanceFrequent"/>.</summary>
        public const float ThreatStandstillChanceRare = 0.15f;

        /// <summary>
        /// Standstill chance from 새벽 onwards. §07: "없음" — the creature never stops
        /// again, which is also the moment a runner stops being able to lose it by
        /// waiting. From here the only way past it is through §12's geometry.
        /// <para>
        /// The old sentence ended "…and the escort has to move under continuous noise".
        /// There is no escort: the objective the co-op game walked out of the building
        /// in a pair is deleted, and §01's descent is one way.
        /// </para>
        /// </summary>
        public const float ThreatStandstillChanceNone = 0f;

        // ====================================================================
        // §09 — Ghost state. NO CONSTANTS, and that is the finding.
        //
        // GONE: GhostRattleCooldownSeconds (45 s) and GhostRattleRange (4 m), the
        // two numbers behind 신호 — a ghost shaking a nearby object once every 45 s.
        // §11's 탈락자 rule forbids it outright (「살아 있는 사람에게 개입할 수 없다」),
        // and in a game where §12 makes 소리 the map, a placed noise is a forged
        // footstep dropped by the only entity that can see the whole building.
        // The 45 s was also priced for three ghosts; §11's field is 2~20, and ten
        // ghosts at minute eight is a free bang somewhere every 4.5 s.
        //
        // §09's remaining rows are all booleans — 시야, 말하기, 탈출 — so it
        // correctly ends up with nothing to tune. What the spectator does now is
        // GhostSession.CutToNextVantage, which moves a camera and nothing else.
        // ====================================================================

        // ====================================================================
        // §13 — Networking and voice.
        // ====================================================================

        /// <summary>
        /// Beyond this distance voice packets are not sent at all, metres. §13 —
        /// "전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다 들린다."
        /// This is an anti-eavesdropping rule, not a bandwidth optimisation.
        /// </summary>
        public const float VoiceCutoffDistance = 30f;

        /// <summary>
        /// Seats a lobby is opened with. Four — the co-op party — and it is NOT the
        /// race's field.
        /// <para>
        /// <b>The doc used to read "Player count per match. §11." and that is false.</b>
        /// §11's field is <see cref="RaceRunnersMin"/>~<see cref="RaceRunnersMax"/>,
        /// 2~20, and <c>HorrorGameNetworkManager.Awake</c> says so at length: it stopped
        /// handing this number to <c>NetworkServer.Listen</c> precisely because a socket
        /// that enforced four would refuse the fifth runner. What still reads it is the
        /// lobby layer — <c>NetLobby</c>'s seat array, <c>SteamworksLobbyService</c>'s
        /// default capacity, <c>VoiceRoster</c>'s initial list size — where it is a
        /// starting capacity rather than a ceiling.
        /// </para>
        /// <para>
        /// <b>This is a live finding, not a settled design.</b> A four-seat lobby in front
        /// of a twenty-runner race is a co-op survivor that a DLL sweep cannot see either,
        /// because it is a number rather than a name. Either the lobby's seats become
        /// <see cref="RaceRunnersMax"/> or the seat concept goes — §11's race needs no
        /// seat, which <c>HorrorGameNetworkManager</c> already logs when it declines to
        /// refuse a seatless runner. Recorded here so the decision has an address rather
        /// than deleted from under twelve files this round does not own.
        /// </para>
        /// </summary>
        public const int PlayersPerMatch = 4;

        /// <summary>Network send rate for movement and camera state, Hz.</summary>
        public const int NetworkSendRate = 30;

        // --------------------------------------------------------------------
        // §13 텔레메트리 1단계 — bucket geometry for the Steam Stats counters.
        //
        // §13 writes the template out literally, and every other histogram
        // follows it — uniform bands plus one open-ended tail, so that a
        // distribution falls out of nothing but increments:
        //   aggro_duration_0_5s / 5_10s / 10_15s / 15s_plus
        // --------------------------------------------------------------------

        /// <summary>
        /// Width of one 어그로 지속 시간 band, seconds. §13 names its four counters
        /// literally and they are 5 s apart. The histogram exists to validate §06's
        /// 12 m 해제 거리 — whether a chase can be ended at all in §12's geometry.
        /// </summary>
        public const float TelemetryAggroBucketSeconds = 5f;

        /// <summary>
        /// Number of 어그로 지속 시간 counters, the open-ended tail included. §13
        /// lists exactly four: 0_5s, 5_10s, 10_15s, 15s_plus.
        /// </summary>
        public const int TelemetryAggroBucketCount = 4;

        /// <summary>
        /// Width of one 후진(S) 사용 시간 비율 band, as a fraction of all movement
        /// time. §13 asks for the share to validate §05's 65% multiplier but names
        /// no bands, so they follow §13's own template: uniform, five points of
        /// share each.
        /// </summary>
        public const float TelemetryBackpedalShareBucketFraction = 0.05f;

        /// <summary>
        /// Number of 후진 비율 counters, the open-ended tail included.
        /// <para>
        /// The tail is placed where the share stops changing the answer. A team
        /// spending share <c>s</c> of its movement backwards averages
        /// <c>1 − s × (1 − 0.65)</c> of full speed (§05), so at
        /// <c>s = (1 − 0.90) / (1 − 0.65) ≈ 0.286</c> they are moving worse on
        /// average than a player who never faced forward at all — §05's 측면 90%.
        /// Six 5-point bands put that crossover inside the last finite band and
        /// open the tail at 30%, past which "they are backpedalling too much" is
        /// already established and the exact figure adds nothing.
        /// </para>
        /// </summary>
        public const int TelemetryBackpedalShareBucketCount = 7;

        // ====================================================================
        // §12 — Map rules derived from the numbers above.
        // ====================================================================

        /// <summary>
        /// Minimum distance at which a single corner can break line of sight,
        /// metres. §12 derives it: 3 s of cover at 4.8 m/s.
        /// </summary>
        public const float SingleCornerMinDistance = 14.4f;

        /// <summary>Distance a Runner gains over a full sprint moving straight, metres. §12.</summary>
        public const float SprintDistanceGain = 9.6f;

        /// <summary>Shortest allowed gap between line-of-sight breaks, metres. §12.</summary>
        public const float LineOfSightBreakSpacingMin = 15f;

        /// <summary>Longest allowed gap between line-of-sight breaks, metres. §12.</summary>
        public const float LineOfSightBreakSpacingMax = 25f;

        /// <summary>
        /// Widest one 시야 차단 지점 may be — how far apart the bends belonging to a
        /// single piece of cover may stand, metres.
        /// <para>
        /// §12 counts <em>opportunities</em>, not corners: "질주 60m에 3~4번의 기회",
        /// and its 기본 단위 is an S자 통로 — two bends and one chance. So consecutive
        /// bends close enough to hold one sight line are one 시야 차단 지점, and this is
        /// the width past which that one point stops being a chance and becomes a
        /// certainty.
        /// </para>
        /// <para>
        /// It is §12's own arithmetic, rearranged. §12 needs
        /// <see cref="SingleCornerMinDistance"/> of gap for
        /// <see cref="AggroReleaseLineOfSightBreak"/> of cover, and its 어그로 시작 거리
        /// table endorses <see cref="RunnerTestAggroStartDistance"/> as the shortest
        /// start that works. A Runner leaving aggro at that distance already carries
        /// 10 m of the 14.4, so cover that is itself 4.4 m deep completes the release
        /// <em>wherever</em> it is picked up — at the very first step, at zero run-up.
        /// </para>
        /// <para>
        /// <b>NOT WHAT MAPVALIDATOR ENFORCES, and kept for the fixture that sizes itself
        /// from it.</b> The subtracted term is 「주자는 멀리서 어그로를 걸어야 한다」 — §12's
        /// conclusion about §04's 주자 <em>choosing</em> the range to be seen from, the same
        /// deleted premise <see cref="RunnerTestAggroStartDistance"/>'s own remarks record.
        /// Nobody chooses in a race: aggro starts where the creature finds you, which down
        /// one of this map's 15~17.5 m legs is most of a leg and at an 안쪽 고리 junction is
        /// one cell — §01's 「마주치면 피할 수 없다」. A runner therefore reaches cover carrying
        /// nothing, and nothing comes off the 14.4.
        /// </para>
        /// <para>
        /// So <c>MapValidator.SightBreakPointSpanCap</c> is
        /// <see cref="SingleCornerMinDistance"/> itself. For one round the whole cap was
        /// deleted on the strength of the argument above and the rule went green with the
        /// map unmoved; stripping only the term makes the cap WEAKER (14.4 m against 4.4 m)
        /// and the shipped map is still 6.6× over it at <b>95 m</b>. That is a map defect —
        /// B-007, waived by name in <c>MapSceneGenerator.KnownFailingRules</c>.
        /// </para>
        /// <para>
        /// The 탈출 대가 (<see cref="ChaseTollSecondsMin"/>) is measured beside it and does
        /// not stand in for it: <b>0 of 720</b> places charge less than one door while cover
        /// still runs 95 m unbroken. A toll priced on one runner's escape cannot see what
        /// continuous cover costs a race of twenty.
        /// </para>
        /// <para>
        /// The measured consequence, on the geometry the game ships: a 5 m connector
        /// between two bends releases from anywhere on the map, and only a 2.5 m one
        /// leaves the release conditional on how far out aggro was taken.
        /// </para>
        /// </summary>
        public const float SightBreakPointSpanMax = SingleCornerMinDistance - RunnerTestAggroStartDistance;

        /// <summary>
        /// Shortest legal 외곽→중심 path on one storey, metres. §12-D's <c>centre-path</c>
        /// band, written down as a number for the first time.
        /// <para>
        /// §12-D derives it from match length and shows its working: 「길을 아는 사람
        /// 118 m ÷ 4.5 m/s = 26초/층 × 8 = 3.5분; 처음 온 사람 길을 두 배 돌면 7분, 문과
        /// 괴물까지 더하면 12~20분」, which is §01's 한 판의 흐름. It also says why the floor
        /// matters more than the ceiling: 「60~90 m로 줄이면 아는 사람이 2분 만에 끝내고,
        /// 그러면 맵을 아는 것이 실력이라는 전제가 보상 없이 사라진다」 — §01's only two
        /// sources of difference are 길을 아는가 and 어디까지 감수하는가, and a storey short
        /// enough to solve blind deletes the first one.
        /// </para>
        /// <para>
        /// <b>Open map defect, measured headlessly on seed 20260802.</b> A runner enters a
        /// storey at a 투하구 landing on the rim; from there to the middle is <b>70~75 m</b>
        /// on B2–B6 and B8, <b>60~65 m</b> on B7, and <b>47.5~82.5 m</b> from B1's sixteen
        /// 외곽 rail cells — <b>30 of 30</b> entry points outside this band, short by
        /// 7.5~42.5 m. <c>MapValidator.RuleCentrePath</c> GATES it and it fails; the map is
        /// still written because <c>MapSceneGenerator.KnownFailingRules</c> waives it by
        /// name with those numbers in the entry (B-019). The fix is geometry in
        /// <c>RadialStorey</c> — a longer rim-to-middle route, not a wider band.
        /// </para>
        /// </summary>
        public const float CentrePathMetresMin = 90f;

        /// <summary>
        /// Longest legal 외곽→중심 path on one storey, metres. §12-D.
        /// <para>
        /// The other end of the band <see cref="CentrePathMetresMin"/> derives: 140 m ÷
        /// <see cref="RunSpeed"/> = 31 s a storey, ×8 = 4.1 minutes for someone who knows
        /// the way, which is the top of §01's 12~20 분 once a first-timer's doubling, the
        /// doors and the creature are added. Past it a storey takes longer to walk than
        /// §07 leaves the match.
        /// </para>
        /// </summary>
        public const float CentrePathMetresMax = 140f;

        /// <summary>Length of one leg of an S-corridor, metres. §12 — the map's base unit.</summary>
        public const float SCorridorLegLength = 10f;

        /// <summary>
        /// Longest straight corridor allowed, metres. §12: "넘으면 주자가 죽는다."
        /// </summary>
        public const float MaxStraightCorridor = 20f;

        /// <summary>Fewest zones a map may have. §12.</summary>
        public const int ZoneCountMin = 4;

        /// <summary>
        /// Most zones a map may have. §12 says 4~6.
        /// <para>
        /// Raised to 9 on 2026-08-02. §12's 첫 맵 스케치 is a single storey 100 m square,
        /// and on one floor 4~6 zones is what kept a zone big enough to hold §12's own
        /// geometry and small enough that a player could name the one they were in —
        /// the co-op reading of that was §04's 청음사 calling it out, and the race's is a
        /// runner recognising the floor under them from its surface. The shipped
        /// building is five storeys with one zone each, and the cap has been doing a
        /// different job since: it was the number of FLOORS the game could have.
        /// </para>
        /// <para>
        /// What the band was protecting is protected by other rules that did not change:
        /// entry points are still 2~3, and every zone still needs its own surface, which is
        /// now the real cap — eight materials, so nine zones only if one of them is the
        /// stairwells' 금속. That is also the whole of §01's audio map: 「여덟 개의 표면,
        /// 층마다 하나. 발소리가 어느 층에 있는지 말해 준다」.
        /// </para>
        /// </summary>
        public const int ZoneCountMax = 9;

        // --------------------------------------------------------------------
        // ZoneDiagonalMin/Max are NO LONGER a §12 rule. MapValidator's
        // `zone-diagonal` was deleted in the §12 re-derivation; the two constants
        // stay because six live systems read them as a reference distance and none
        // of the six is §12. Their doc comments below say which.
        //
        // The rule sized a 구역 as a SUB-AREA of a floor — small enough to search,
        // big enough to name. On 하강 a 구역 IS a 층: eight zones, eight storeys,
        // one surface each, and DescentMap.Radius 11 makes every one of them
        // 57.5 m square and 81.3 m across. Both of the rule's own reasons went with
        // that identity — a footstep now names a STOREY by design (§01), and 「주자가
        // 구역 2~3개 관통 가능」 would mean crossing two or three FLOORS on one
        // sprint, which §01 forbids structurally: a floor is only ever left through
        // the 투하구 in its middle. What still needs bounding is how far a runner
        // must travel on a floor, and that is §12-D's CentrePathMetresMin~Max.
        // --------------------------------------------------------------------

        /// <summary>
        /// Reference distance: the small end of §12's retired 구역 대각선 band, metres.
        /// <para>
        /// Read by <c>PlayerFeelHarness</c> (the width of a test floor patch) and
        /// <c>MonsterTests</c>. Kept at 30 m rather than re-derived because nothing that
        /// reads it is making a §12 claim — see the block above for why the rule went.
        /// </para>
        /// </summary>
        public const float ZoneDiagonalMin = 30f;

        /// <summary>
        /// Reference distance: how far the game bothers to render, patrol and replicate,
        /// metres. Was §12's 구역 대각선 cap.
        /// <para>
        /// Its live readers are all engine-side and all of them mean "about one room-scale
        /// distance": <c>NetInterestScope</c> floors its replication range at it,
        /// <c>MonsterBrain</c> picks a search waypoint inside it, <c>AtmosphereSetup</c>
        /// sets the shadow distance to it, and <c>NightAtmosphere</c> quotes it as the
        /// distance at which fog reaches 83 %. Four systems agreeing on one number is worth
        /// more than four literals; that is the job the constant now has.
        /// </para>
        /// </summary>
        public const float ZoneDiagonalMax = 40f;

        /// <summary>Map extent along one axis, metres. §12: 100 × 100.</summary>
        // Raised from 100 m on 2026-08-02. The 100 came from "주자가 구역 2~3개 관통
        // 가능 on one sprint of 60 m", and that argument was about ZONE size rather than
        // the building's footprint. The zone half of it is gone with `zone-diagonal`
        // above; what bounds the footprint now is the tower — DescentMap stacks eight
        // 57.5 m storeys in one column, so the measured extent is 57.5 m and this is the
        // ceiling nothing has needed to approach.
        public const float MapExtent = 170f;

        /// <summary>Lowest acceptable dead-end ratio. §12: below this, map knowledge stops mattering.</summary>
        public const float DeadEndRatioMin = 0.20f;

        /// <summary>Highest acceptable dead-end ratio. §12: above this, players die to luck.</summary>
        public const float DeadEndRatioMax = 0.25f;

        /// <summary>Loops required per zone. §12 — a tree-shaped map is a death sentence.</summary>
        public const int LoopsPerZoneMin = 1;

        /// <summary>Loops required across the whole map. §12.</summary>
        public const int LoopsTotalMin = 3;

        /// <summary>Fewest entry points between two adjacent zones. §12.</summary>
        public const int ZoneEntryPointsMin = 2;

        /// <summary>Most entry points between two adjacent zones. §12.</summary>
        public const int ZoneEntryPointsMax = 3;

        /// <summary>Lockable doors per zone, lower bound. §12.</summary>
        public const int LockableDoorsPerZoneMin = 1;

        /// <summary>
        /// Lockable doors per zone, upper bound. §12.
        /// <para>
        /// The reason used to be "more than this makes the Engineer omnipotent" — §04's
        /// 정비공 could lock a door for a material, so the quota was a cap on one role's
        /// power. There is no 정비공 and no material. The cap survives on §06's terms
        /// instead: a door costs a pursuer <see cref="DoorBreakSeconds"/>, so a zone with
        /// several of them is a zone the runner in front can seal behind themselves and
        /// §02's 먼저 닿는다 becomes a reward for already being first.
        /// </para>
        /// </summary>
        public const int LockableDoorsPerZoneMax = 2;

        // --------------------------------------------------------------------
        // GONE: ObservationPostsPerZoneMin, CandidateSitesPerZone and
        // CandidateSiteMinExits — the map quotas the clue hunt needed.
        //
        // A 후보 지점 was somewhere a clue or the objective could be hidden, and an
        // 관측 지점 was somewhere the 관측자 could watch from. The race hides nothing
        // and assigns no roles: a storey is a concentric maze whose gates narrow
        // 4 → 2 → 1 toward a 투하구 in the middle, and every runner wants the same
        // point on it. The generated scene already reports 0 후보 지점.
        // --------------------------------------------------------------------

        /// <summary>
        /// Runner-test success rate below which the map is too hard. §12 — <b>RETIRED. It is
        /// not the race's grade and §12 no longer states it.</b>
        /// <para>
        /// <b>What it meant, and why the pivot took the meaning away.</b> 「도망칠 수 있다」
        /// meant 「죽지 않는다」. §06 ended a caught runner's match, §02 gave them no 순위, and
        /// a 5~7/10 escape rate was a statement about how often a player kept playing. §06 now
        /// sends a caught runner back to the cell they started from on B1 and they keep
        /// racing: they lose every storey they had and nothing else. Escape is no longer the
        /// difference between playing and not playing, so its rate is no longer a grade — what
        /// a race grades is the <em>price</em>, <see cref="ChaseTollSecondsMin"/> ~
        /// <see cref="ChaseTollSecondsMax"/>.
        /// </para>
        /// <para>
        /// <b>Kept as a compiling symbol, not as a threshold.</b> Its remaining readers are
        /// <c>RunnerTest.Verdict</c>, <c>RunnerCensus</c>'s report line and three assertions in
        /// <c>MapTests</c> — none of them this round's files. The value is NOT moved: a band
        /// nudged until the map cleared it would be exactly the failure this re-derivation
        /// exists to undo. It goes when those readers do.
        /// </para>
        /// <para>
        /// <b>And it could never discriminate anyway — arithmetic, not opinion.</b> Three
        /// working passes were spent trying to land inside it and the grade never moved off
        /// 10/10. With cover continuous (which §12's own 시야 차단 지점 간격 guarantees past
        /// the first 20 m of any route) a release needs
        /// <code>
        /// RunnerSprintSpeed × max(AggroReleaseLineOfSightBreak,
        ///                         (AggroReleaseDistance − RunnerTestAggroStartDistance)
        ///                         ÷ (RunnerSprintSpeed − MonsterBaseSpeed))
        ///   = 5.6 × max(3 s, 2 m ÷ 0.8 m/s)  =  16.8 m of route past one bend
        /// </code>
        /// and §12 mandates an S자 통로 of 10 m × 2 per zone, caps a straight at
        /// <see cref="MaxStraightCorridor"/>, and requires <see cref="LoopsTotalMin"/> 순환로.
        /// A map cannot obey §12's construction rules and fail §12's own 실전 검증. The band
        /// describes maps the rest of §12 forbids.
        /// </para>
        /// <para>
        /// It holds across the whole of §07 too, so this is not an artefact of grading at
        /// 심야: 초저녁/밤/심야 all need 16.8 m, 새벽 needs 18.7 m and 동트기 전 —
        /// <see cref="ThreatSpeedBeforeSunrise"/>, the fastest the creature ever gets — needs
        /// 28.0 m, against §12-D's own 90~140 m centre path per storey.
        /// </para>
        /// <para>
        /// <b>What replaced it.</b> The band asks the co-op question, "can aggro be broken";
        /// the race asks §07's and §02's question, "what did breaking it cost". See
        /// <see cref="ChaseTollSecondsMin"/>, <see cref="ChaseTollSecondsMax"/> and
        /// docs/BALANCE-FINDINGS.md F-013.
        /// </para>
        /// </summary>
        public const float RunnerTestPassRateMin = 0.50f;

        /// <summary>
        /// Runner-test success rate above which the map is too easy. §12 — <b>RETIRED with
        /// <see cref="RunnerTestPassRateMin"/></b>, on whose remarks the whole reason lives.
        /// Held at §12's written value; the race's grade is
        /// <see cref="ChaseTollSecondsMin"/> ~ <see cref="ChaseTollSecondsMax"/>.
        /// </summary>
        public const float RunnerTestPassRateMax = 0.70f;

        /// <summary>
        /// Turn, in degrees, at which a corner counts as breaking the sight line
        /// down a corridor.
        /// <para>
        /// §12 recognises exactly two kinds of corridor — 직선 통로, capped at
        /// <see cref="MaxStraightCorridor"/>, and 굽음, which is what an S-corridor
        /// is made of — but never says how sharp a bend has to be to stop being
        /// straight. Every structure §12 draws turns at a right angle, so this is
        /// the threshold below which a corridor is treated as one straight run.
        /// </para>
        /// <para>
        /// Provisional in the §16 sense, and deliberately used the same way on both
        /// sides so that the error is always in the strict direction: a 25° kink
        /// still counts toward the straight-run limit <em>and</em> still fails to
        /// qualify as an S-corridor bend. A sloppy map therefore fails validation
        /// rather than slipping through on a technicality.
        /// </para>
        /// </summary>
        public const float MapSightBreakingBendDegrees = 30f;

        /// <summary>
        /// Points the 주자 테스트 samples. §12: "10개 지점에서 시도한다." The pass
        /// bands (<see cref="RunnerTestPassRateMin"/> · 5~7/10) are quoted against
        /// this count, so changing it changes what the bands mean.
        /// </summary>
        public const int RunnerTestSampleCount = 10;

        /// <summary>
        /// Distance the monster starts behind the Runner in the 주자 테스트, metres.
        /// <para>
        /// §12's 어그로 시작 거리 table rates 3 m ❌, 5 m ⚠️ "이론상 겨우", 10 m ✅
        /// "여유" and 15 m ✅ "안정". 10 m is the shortest distance §12 endorses
        /// without a caveat, which makes it the right yardstick for grading a map:
        /// at 15 m a single corner already clears
        /// <see cref="SingleCornerMinDistance"/> on its own, so the test would stop
        /// measuring the 연속 차단 structures §12 exists to require.
        /// </para>
        /// <para>
        /// <b>That justification died with §04 and the value is kept anyway — deliberately.</b>
        /// The table's conclusion is 「주자는 멀리서 어그로를 걸어야 한다」, a rule about §04's
        /// 주자 <em>choosing</em> the range to taunt from. §04 is deleted (v1.0: 「직업 —
        /// 삭제됨. 전원 동일하다」) and nobody taunts in a race: aggro starts wherever the
        /// creature acquires you, which down one of this map's 15~20 m legs is most of a leg
        /// and at a junction in the 안쪽 고리 is one cell — §01's 「마주치면 피할 수 없다」,
        /// which is §12's own ❌ row.
        /// </para>
        /// <para>
        /// <b>It is kept because it is not a knob.</b> Every value inside <see cref="Validate"/>'s
        /// feasible region is on one side or the other of a cliff, never on a slope. A release
        /// needs the runner to sprint <c>(AggroReleaseDistance − this) ÷ 0.8 m/s</c>, i.e.
        /// <c>7 × (12 − this)</c> metres of route, against the 60 m of
        /// <see cref="SprintMaxTravelDistance"/> the test explores: below 3.43 m nothing on
        /// any map escapes, above it a §12-legal map escapes from everywhere. RadialStorey's
        /// own sweep of the shipped floor measured that cliff at 3.4 m → 0% and 3.6 m → 100%,
        /// which is 7 × 8.6 = 60.2 m against 7 × 8.4 = 58.8 m exactly. Moving this constant
        /// changes which side of the cliff the report lands on and nothing about the game.
        /// </para>
        /// <para>
        /// <b>It used to be load-bearing elsewhere, and that circle is now broken.</b>
        /// <see cref="SightBreakPointSpanMax"/> is defined as
        /// <see cref="SingleCornerMinDistance"/> less this value, so the two were holding each
        /// other up: this constant was kept because the span cap needed it, and the span cap
        /// was derived from this constant. The §12 re-derivation took the span cap out of the
        /// gate — it is measured and printed, not enforced — so nothing a map is judged by
        /// depends on this number any more. It stays because of the cliff above and because
        /// the 주자 테스트 report still quotes it. docs/BALANCE-FINDINGS.md F-013.
        /// </para>
        /// </summary>
        public const float RunnerTestAggroStartDistance = 10f;

        /// <summary>
        /// Escape routes the 주자 테스트 will explore from one sample point before
        /// giving up. Not a balance value — it is the bound that keeps an exhaustive
        /// simple-path search terminating on a densely looped map (§12 requires
        /// loops, so the search space is genuinely exponential).
        /// </summary>
        public const int RunnerTestRouteLimitPerPoint = 4096;

        // ====================================================================
        // GONE: ExpectedRoundTripsMin / ExpectedRoundTripsMax. A 왕복 was a trip
        // down and back up to the van, and §03 expected 2~5 of them per match.
        // 하강 goes one way: eight storeys down, and the 투하구 is a fall rather
        // than a path, so there is no back. TelemetryBuckets replaced the
        // round_trips histogram with deepest_storey, whose bound is
        // RaceState.Storeys.
        // ====================================================================

        // --------------------------------------------------------------------
        // GONE: §03's 혼동쌍 — the misread model. ClueConfidentReadSeconds,
        // ClueReadStillSpeedThreshold, ClueIllegibleBlur, ClueInvertedViewAngle,
        // ClueBlurConfusionThreshold, ClueMisreadChanceMax,
        // ClueMisreadFocusedFraction and the four ClueMisreadWeight* rows, plus
        // CluesRequiredToLocate above them.
        //
        // §03 built a whole probabilistic model of remembering a number wrong —
        // 6↔9 뒤집힌 각도, 1↔7 손글씨체, ㅁ↔ㅇ 흐릿할 때, 좌↔우 거울 — and called the
        // resulting error "이 게임의 주된 웃음이자 사망 원인". It was the funniest
        // thing in the co-op game and it is meaningless in this one: 하강 asks the
        // runner to reach the middle of B8 first, and there is no number to carry
        // out of a dark room and misremember on the way.
        //
        // One value survives the cull under a name that is true, immediately
        // below: the brightness that separates lit from dark.
        // --------------------------------------------------------------------

        /// <summary>
        /// Light quality (0–1) at or above which a place counts as lit. The one
        /// brightness in the game, and everything that cares about the dark reads it.
        /// <para>
        /// <b>This was <c>ClueMinReadableLightQuality</c>.</b> §03 set it as the hard
        /// gate below which a clue could not be read at all, and <c>Core/Presence</c>
        /// deliberately reused the same number rather than picking a similar one —
        /// <c>PresenceDensity.SafeLightQuality</c> is exactly this, so that "light
        /// enough to read by" and "light enough that no 그늘 forms" were one thing a
        /// player had to learn. The reading half is deleted; the 그늘 half is live and
        /// on the hot path, so the number is renamed rather than dropped.
        /// </para>
        /// <para>
        /// It is still a hard gate rather than a penalty, and it is still a single
        /// threshold on purpose: a runner descending in the dark has to be able to
        /// tell, at a glance, whether where they are standing is filling the pool or
        /// not. Two brightnesses would make that unlearnable.
        /// </para>
        /// </summary>
        public const float MinSafeLightQuality = 0.20f;

        // ====================================================================
        // §10 / §03 — 그늘. The second thing in the building.
        //
        //   「얻으려면 위험을 만들어야 한다」(§10)
        //
        // §03 sells the flashlight as one switch answering two questions, and
        // then only charges for one of them: light on, 괴물이 잘 본다. Light off
        // costs nothing but the clue you cannot read, so moving dark is free and
        // the optimal play is to travel unlit and flick the beam on to read. The
        // 그늘 is the other half of that switch. It is not a second pursuer —
        // §01's horror is one 이길 수 없는 적 and a second one would halve it. It
        // has no position, no path and no speed. It is a *condition* that fills
        // while a player stands in less light than §03 needs to read by, and when
        // it is full it takes the two things §03 makes this game out of: the
        // player's voice, and their confidence in what they just read.
        //
        // Every number below is bounded by a number that already exists. See
        // Validate() for the relationships and Core/Presence for the rules.
        // ====================================================================

        /// <summary>
        /// Seconds of unbroken, total darkness before the 그늘 takes something.
        /// The one genuinely new number here, and it is bounded from both sides.
        /// <para>
        /// It must be <b>longer</b> than three §12 cover-to-cover crossings at
        /// walking pace (3 × <see cref="LineOfSightBreakSpacingMax"/> ÷
        /// <see cref="WalkSpeed"/> = 37.5 s), or a player could not cross the
        /// building §12 specifies without being taken and the dark would stop
        /// being a choice. That bound is asserted in <see cref="Validate"/>.
        /// </para>
        /// <para>
        /// <b>It used to be bounded from above too, and no longer is.</b> The upper
        /// bound was <c>BatterySecondsPerCell</c> — the 그늘 had to fill faster than a
        /// cell emptied, or never switching the torch on was strictly safer than
        /// switching it on. The battery is deleted (see Core/Light/FlashlightState),
        /// so that assertion had nothing left to compare against and went with it.
        /// </para>
        /// <para>
        /// What it was protecting still matters and is now carried by
        /// <see cref="FlashlightNoticeDistance"/> instead: light is seen from further
        /// than the monster can see you, so travelling lit costs something even
        /// though the torch is free. Dark costs this pool. Those two prices are what
        /// keep the switch a decision, and <see cref="Validate"/> asserts the first.
        /// The second — how long 45 s may grow to before dark is underpriced — is
        /// bounded by no other constant in this file and is the one derivation this
        /// deletion genuinely weakened. Retune it against playtest, not against a
        /// number.
        /// </para>
        /// <para>
        /// Provisional in the §16 sense: no section of the design fixes it, and
        /// 45 s is the round number in the middle of the bracket. Retune it here.
        /// </para>
        /// </summary>
        public const float PresenceSaturationSeconds = 45f;

        /// <summary>
        /// Seconds of readable light needed to clear a full pool. One third of
        /// <see cref="PresenceSaturationSeconds"/>, so one second of light buys
        /// back three seconds of dark.
        /// <para>
        /// Not instant, on purpose. If a single frame of beam reset the pool, the
        /// answer would be to flick the torch once a minute and the dark would be
        /// free — the co-op game charged a <c>BatterySwitchOnCost</c> for that flick
        /// and this file no longer can, because the torch has no cell. Three-to-one
        /// is now the whole price of the flick: a second of beam buys back three
        /// seconds of dark and no more, so travelling dark stays genuinely cheaper
        /// than travelling lit and still costs something you keep paying.
        /// </para>
        /// </summary>
        public const float PresenceDispersalSeconds = 15f;

        /// <summary>
        /// Seconds a taken player cannot transmit voice. §13's channel, off.
        /// <para>
        /// Deliberately <see cref="SprintStaminaSeconds"/> — one sprint. §06 already
        /// fixes 12 s as the longest single unbroken bad moment the design asks a
        /// player to survive, so the 그늘's toll is one of those, and the two move
        /// together if either is ever retuned.
        /// </para>
        /// <para>
        /// This is §09's ghost condition applied to somebody still alive: "자기
        /// 물건이 어디 있는지 보이는데 말할 수 없다." §09 calls that the game's
        /// 최고의 순간 and reaches it only by killing you. The 그늘 reaches it for
        /// twelve seconds, for the price of walking in the dark.
        /// </para>
        /// </summary>
        public const float PresenceSilenceSeconds = SprintStaminaSeconds;

        /// <summary>
        /// How much of the pool survives a taking. §01: "저지는 전부 일시적" —
        /// nothing in this game is dealt with permanently, and a player who is
        /// still standing in the dark when the 그늘 lets go is still standing in
        /// the dark.
        /// <para>
        /// Below <see cref="PresenceWarnPooling"/> so the second taking is
        /// announced from the beginning again rather than arriving out of a state
        /// the player was never shown leaving.
        /// </para>
        /// </summary>
        public const float PresenceResidualPooling = 0.50f;

        /// <summary>
        /// Pool fraction at which the 그늘 stops being ambience and becomes a
        /// figure. Everything the player is told is told here: the sound tightens,
        /// the motes resolve, and something is standing at the edge of the beam.
        /// <para>
        /// 0.6 leaves 40% of <see cref="PresenceSaturationSeconds"/> — 18 s — to
        /// react in. §12's cover spacing puts a lit-able place inside 25 m, which
        /// is 12.5 s at <see cref="WalkSpeed"/>, so the warning is long enough to
        /// walk out of and short enough to be a warning.
        /// </para>
        /// </summary>
        public const float PresenceWarnPooling = 0.60f;

        /// <summary>
        /// Strength of the second half of the 그늘's toll, 0–1: after it takes a
        /// player's voice it also takes their certainty, fading back in over
        /// <see cref="PresenceRecallFadeSeconds"/>.
        /// <para>
        /// <b>Its derivation was deleted with the co-op game and this is the
        /// replacement.</b> It used to read <c>1 − ClueMisreadFocusedFraction</c>: §03's
        /// misread model said a careful, well-lit look removed half of a mark's
        /// ambiguity, and the 그늘 was allowed to take back exactly that half and no
        /// more. There are no marks and no misreads now, so the old expression cannot
        /// be evaluated and the half is stated directly.
        /// </para>
        /// <para>
        /// Half, still, and for a reason that survives the clue chain: the toll has to
        /// be felt without being disabling. <see cref="PresenceSilenceSeconds"/> is
        /// absolute — the voice is simply off — so if this were also total the 그늘
        /// would take everything at once and become the second unkillable pursuer §01
        /// spends its argument refusing ("이길 수 없는 적" is one entity, and two halve
        /// each other). At 0.5 it degrades rather than removes.
        /// </para>
        /// <para>
        /// <b>Consumer warning.</b> <c>Core/Presence/PresenceState</c> still computes
        /// this and hands it out through <c>PresenceToll.RecallSmear</c>, but with the
        /// misread model gone nothing in the race reads the result yet. It is kept
        /// because the toll's shape is a live design question for the race — what a
        /// runner loses besides their voice — and not because anything consumes it.
        /// </para>
        /// </summary>
        public const float PresenceRecallSmear = 0.50f;

        /// <summary>
        /// Seconds over which <see cref="PresenceRecallSmear"/> fades once the
        /// player is out of the dark. Longer than
        /// <see cref="PresenceSilenceSeconds"/> on purpose: the voice comes back
        /// before the certainty does, so a runner is talking again while still unsure
        /// of what they saw.
        /// <para>
        /// The old line ended §03: "6이었나 9였나…", the misread model's own example.
        /// That model is deleted — there is no mark to carry out of a dark room and no
        /// number to remember wrong — and what the overlap is for now is §10's: the two
        /// halves of the 그늘's toll have to overlap or the second half is a separate
        /// event nobody connects to the dark. <see cref="Validate"/> asserts the ordering.
        /// </para>
        /// </summary>
        public const float PresenceRecallFadeSeconds = 30f;

        /// <summary>
        /// Radius around the monster inside which there is no 그늘 at all, metres.
        /// <para>
        /// Derived, not chosen: it is <see cref="MonsterSightRange"/>. §01 keeps
        /// its horror with <b>one</b> unkillable pursuer, so wherever the monster
        /// can see you the 그늘 is not there — the two are never pressure at the
        /// same moment on the same player. Two consequences fall out of that one
        /// line and each is asserted by a test. (There was a third, and it was §04's:
        /// the 관측자 was structurally immune because the role worked at
        /// <c>ObserverRange</c> and had to hold still in the dark, which is exactly what
        /// the 그늘 punishes. Nobody stands still on purpose in a race, the role is
        /// deleted, and the constant it named went with it — the bullet was left behind
        /// pointing at a member this file no longer has.)
        /// </para>
        /// <list type="bullet">
        /// <item><description>§06's chase is untouched. A chase requires a sight
        /// line, a sight line requires ≤ 20 m, and at ≤ 20 m the density is
        /// zero.</description></item>
        /// <item><description>The dark withdrawing is a tell. §07's 정지 makes the
        /// creature silent, so §12's floor stops reporting it; the pool falling is the
        /// one cue that still says "it is close", and it is available to every runner
        /// because §04 v1.1 leaves them all identical.</description></item>
        /// </list>
        /// </summary>
        public const float PresenceMonsterClearRadius = MonsterSightRange;

        /// <summary>
        /// Radius at which the monster stops thinning the 그늘, metres — the outer
        /// edge of the tell. <see cref="LineOfSightBreakSpacingMax"/>, which is the
        /// furthest §12 obliges a map to provide a sight line for and therefore the
        /// furthest a withdrawal could honestly mean anything.
        /// </summary>
        public const float PresenceMonsterFringeRadius = LineOfSightBreakSpacingMax;

        /// <summary>
        /// How much of its strength the 그늘 has at 초저녁 — §07's first row.
        /// <para>
        /// Half, so §01's 맨몸 first descent is about learning the building rather
        /// than about this. It is not zero, because a player who never meets the
        /// 그늘 on the first descent has no reason to believe the second one.
        /// </para>
        /// </summary>
        public const float PresenceBoldnessFloor = 0.50f;

        /// <summary>
        /// Strength the 그늘 gains per §07 threat tier. Sized so it reaches exactly
        /// 1.0 on 동트기 전 — 「생존 불가 수준」 — over
        /// <see cref="ThreatTierCount"/> rows. §07 owns the night; this only reads
        /// its row number.
        /// </summary>
        public const float PresenceBoldnessPerTier = 0.125f;

        // ====================================================================
        // Simulation.
        // ====================================================================

        /// <summary>Fixed simulation step, seconds. Every core system is stepped at this rate.</summary>
        public const float FixedStep = 1f / 50f;

        /// <summary>
        /// Guards against a tuning edit that quietly breaks the design's own
        /// reasoning. Called by the test suite and by the simulator on startup.
        /// </summary>
        /// <exception cref="InvalidOperationException">A relationship the design depends on no longer holds.</exception>
        public static void Validate()
        {
            Require(WalkSpeed < RunSpeed, "§06: walking must be slower than running.");
            Require(DoorBreakSeconds > AggroReleaseLineOfSightBreak,
                "§06: a door has to be worth more than the 3 s of broken sight a release "
                + "needs, or shutting one is a beat of standing still that buys nothing.");
            Require(DoorShutSeconds < AggroReleaseLineOfSightBreak,
                "§06: pulling a door shut has to cost less than the release it might buy, "
                + "or the honest play is always to keep running and the door is scenery.");
            Require(DoorRepairFraction > 0f && DoorRepairFraction < 1f,
                "§07/§02: a door that fully repairs makes the building the same at 동트기 전 as "
                + "at 초저녁, and one that never repairs lets the runner in front strip every "
                + "관문 behind them — 먼저 닿는다 would reward having been first rather than "
                + "being fastest.");
            Require(ChaseTollSecondsMin > DoorShutSeconds,
                "§12-B②/DESCENT-PIVOT: the toll a chase charges has to beat the cheapest "
                + "deliberate way to cost a rival time, which is shutting a door on them. "
                + "Below that, being chased is not worse than being overtaken and the "
                + "creature stops entering anybody's decisions.");
            Require(ChaseTollSecondsMin > AggroReleaseLineOfSightBreak,
                "§06: a release fires as soon as AggroReleaseLineOfSightBreak of cover has "
                + "held, so a toll at or under that is paid by the release itself — the chase "
                + "would cost nothing beyond the seconds it was already on screen, which is "
                + "「escapable」 collapsing into 「ignorable」.");
            Require(ChaseTollSecondsMax > ChaseTollSecondsMin, "§12: toll bounds must be ordered.");
            // No epsilon: ChaseTollSecondsMax IS CentrePathMetresMin ÷ RunSpeed, and 90, 4.5
            // and 20 are all exact in binary32, so the round trip is exact too. An epsilon
            // here would hide a future value that is not.
            Require(ChaseTollSecondsMax * RunSpeed <= CentrePathMetresMin,
                "§01/§06: being caught sends a runner back to their B1 cell and takes every storey, "
                + "so the cheapest catch in the game costs one storey — §12-D's shortest legal "
                + "centre path. A chase allowed to cost more than that is a chase it is rational "
                + "to stop running from, and the race has no way to express that choice.");

            Require(MonsterLungeSpeed > MonsterBaseSpeed,
                "§06: a lunge that is no faster than the chase is not a lunge — the "
                + "creature would commit and then fail to arrive.");
            Require((MonsterLungeSpeed - RunSpeed) * MonsterAttackContactSeconds
                    > MonsterAttackRange - MonsterAttackReach,
                "§06: 「괴물은 이길 수 없는 존재」. A player who is merely running has to be "
                + "caught by a committed lunge, or the creature has an attack that misses "
                + "everybody and the chase stops meaning anything.");
            Require((MonsterLungeSpeed - RunnerSprintSpeed) * MonsterAttackContactSeconds
                    < MonsterAttackRange - MonsterAttackReach,
                "§06: the sprint has to survive one. If a lunge catches a sprinting runner "
                + "too, then 「이길 수 없는 적」 becomes 「피할 수 없는 적」 — anything that gets "
                + "within MonsterAttackRange is 탈락 regardless of what the player does, and "
                + "§06's 0.8 m/s of margin buys nothing at the only instant it matters. This "
                + "used to be argued as §04's 주자 having a moment of its own; v1.0 gave 질주 "
                + "to all twenty runners, so it is now every runner's moment.");
            Require(MonsterAttackReach < MonsterAttackRange,
                "§06: the creature must commit from further away than it can reach, or "
                + "the lunge is the old touch-kill with an animation in front of it.");
            Require(MonsterAttackRecoverySeconds > MonsterAttackContactSeconds * 0.5f,
                "§06: a miss has to cost something. Recovery shorter than half the strike "
                + "would let it re-commit before the player has covered any ground.");

            Require(AudibleRangeMetres > MonsterFootstepHearingRange,
                "§12: 소리가 지도다. A runner who could not hear the creature from further than it "
                + "hears them would be reading a map only it can read.");
            Require(MonsterFootstepHearingRange > MonsterSightRange,
                "§03: 어둠이 정보를 가린다. Hearing shorter than sight would make the dark a "
                + "thing that protects the player from the monster, when the whole design "
                + "is that it blinds the player and not the creature.");
            // GONE with §04: an assertion that the creature heard less far than the
            // 청음사 did, "or the role has nothing to add". There is no role. What
            // now bounds MonsterFootstepHearingRange from above is the surface
            // table below — on the quietest floor a sprint must not carry as far
            // as the creature can see.
            // The two ends of §12's surface table, stated as the thing a player would
            // actually notice: which sense the creature finds you with.
            Require(MonsterFootstepHearingRange * ListenerClarityCarpet < MonsterSightRange,
                "§12 병동: on carpet even a sprint has to carry less far than the creature "
                + "can see, or the maze storey is a maze it can follow you through and the "
                + "quietest surface in the game buys nothing.");
            Require(MonsterFootstepHearingRange * ListenerClarityMetal > MonsterSightRange,
                "§12 계단: on grating the creature has to hear you before it could ever see "
                + "you. That contrast with 병동 is the whole reason the building has more "
                + "than one surface.");

            Require(MonstersPerStorey >= 1,
                "§12-B③: 「괴물이 안쪽을 순찰한다 … 외곽은 안전하고 중심은 위험하다」 is written "
                + "about every floor, and the descent's NavMesh is one island per storey — the "
                + "괴물 cannot walk to a floor it is not standing on. Below one per storey a "
                + "runner on that floor is not escaping anything, and the 실전 검증's "
                + "「escapable」 stops being a statement about the map.");

            Require(MonsterPatrolNoticeRange > MonsterAttackRange,
                "§06: the creature has to notice before it strikes, or the two are one "
                + "event and there is nothing on screen between being unseen and being dead.");
            Require(MonsterPatrolNoticeRange < MonsterSightRange * 0.25f,
                "§06 gives 순찰 no sight edge on purpose, and this is a contact exception "
                + "rather than a repeal. Anything approaching a quarter of the sight range "
                + "would let a patrol acquire across a room, which erases §12's cover.");

            Require(RunSpeed < MonsterBaseSpeed,
                "§06: the monster must out-run a running player, or ordinary roles could simply flee.");
            Require(MonsterBaseSpeed < RunnerSprintSpeed,
                "§06: the Runner's sprint must beat the monster, or the role has no identity.");
            Require(MonsterBaseSpeed - RunSpeed <= 0.5f,
                "§06: the monster's edge over running must stay small — that narrow margin is the tension.");

            Require(MulBackward < MulStrafe && MulStrafe < MulDiagonal && MulDiagonal <= MulForward,
                "§05: the directional multipliers must decrease monotonically as you turn away from your heading.");
            Require(RunSpeed * MulBackward < MonsterBaseSpeed,
                "§05: walking backwards must be slower than the monster — looking behind you has to cost distance.");
            Require(RunnerSprintSpeed * MulBackward < MonsterBaseSpeed,
                "§05: even a sprinting Runner must lose ground while backpedalling.");
            Require(RunnerSprintSpeed * MulDiagonal > MonsterBaseSpeed,
                "§05: the 45° peek must still out-pace the monster, or the skill ceiling disappears.");

            Require(FovMin < FovDefault && FovDefault < FovMax, "§05: the default FOV must sit inside the allowed range.");

            Require(CrouchSpeedMultiplier > 0f && CrouchSpeedMultiplier < 1f,
                "§05: crouching must cost speed without stopping the player.");
            Require(WalkSpeed * CrouchSpeedMultiplier < WalkSpeed * MulBackward,
                "§05: crouching forward must be slower than 후진 65%, the most expensive row in §05's own "
                + "table — otherwise concealment is cheaper than looking behind you and §12's 은폐 지점 "
                + "cost nothing.");
            Require(CrouchNoiseMultiplier > 0f && CrouchNoiseMultiplier < 1f,
                "§12: crouching is the only 소음기 left — it has to be quieter than walking, or the "
                + "half of their speed a runner paid for it bought nothing, and it must not be silent, "
                + "or 소리가 지도다 has an eraser in it. §08 used to sell a bought version and the "
                + "'must not be silent' end was argued from that; there is no shop, and the argument "
                + "is now §12's own.");
            Require(CrouchHeightFraction > 0f && CrouchHeightFraction < 1f,
                "§12: a crouch must lower the player without removing them.");

            Require(JumpApexMetres < PlayerStepOffsetMetres,
                "§12: a jump must not reach a ledge walking cannot already reach. The map's geometry is "
                + "derived from what a player cannot climb, so the apex has to stay under the step the "
                + "controller already takes for free.");
            Require(JumpCooldownSeconds > JumpAirtimeSeconds,
                "§06: two jumps must be separated by time on the ground. §06's chase arithmetic is about "
                + "ground movement, and a chain of hops with no grounded beat between them would be a "
                + "second way to travel.");
            Require(PlayerLandingNoiseLevel > ListenerSelfNoiseThreshold,
                "§12: a landing has to cut the runner's own feed. A jump that is silent is a free "
                + "verb, and 「뛰거나 문을 열면 정보가 끊긴다」 — written for §04's 청음사, charged "
                + "to everybody now that 소리 is the map — is what makes the hop cost anything at all.");

            var gain = (RunnerSprintSpeed - MonsterBaseSpeed) * SprintStaminaSeconds;
            Require(gain < AggroReleaseDistance,
                "§06: one sprint must not be enough to open the release distance — breaking aggro has to mean using the map.");

            var peekTravel = RunnerSprintSpeed * MulDiagonal * SprintStaminaSeconds;
            Require(peekTravel >= SprintMaxTravelDistance - 1f && peekTravel <= SprintMaxTravelDistance + 8f,
                "§05: SprintMaxTravelDistance no longer matches sprint speed × stamina at the peek multiplier.");

            Require(SCorridorLegLength * 2f / MonsterBaseSpeed > AggroReleaseLineOfSightBreak,
                "§12: two S-corridor legs must take the monster longer to clear than the line-of-sight break requires.");
            Require(SingleCornerMinDistance > AggroReleaseLineOfSightBreak * MonsterBaseSpeed - 0.01f,
                "§12: the single-corner distance must cover the full line-of-sight break at monster speed.");
            Require(MaxStraightCorridor <= LineOfSightBreakSpacingMax,
                "§12: a straight corridor must never be longer than the widest allowed gap between cover.");

            // GONE with §08's carry weight: three clauses ordering the bands and
            // pinning that a heavily-loaded sprint fell below monster speed —
            // "greed must cost the Runner its escape". Nobody carries anything, so
            // there is no greed to price. The surviving statement of the same
            // concern is the sprint margin asserted above: an unloaded runner at
            // RunnerSprintSpeed must stay ahead of MonsterBaseSpeed, and §07 is
            // what closes that gap over a race rather than a backpack.
            Require(ThreatTierSeconds * 3f < TargetMatchSecondsMax,
                "§07: a match must be long enough to reach the late tiers.");
            Require(VoiceCutoffDistance > ZoneLightRadius,
                "§13: voice must carry at least as far as a lit zone, or coordination inside one breaks.");

            Require(ZoneCountMin <= ZoneCountMax, "§12: zone bounds must be ordered.");
            Require(DeadEndRatioMin < DeadEndRatioMax, "§12: dead-end ratio bounds must be ordered.");
            Require(RunnerTestPassRateMin < RunnerTestPassRateMax, "§12: runner-test bounds must be ordered.");
            Require(LineOfSightBreakSpacingMin < LineOfSightBreakSpacingMax, "§12: cover-spacing bounds must be ordered.");
            Require(SightBreakPointSpanMax > 0f && SightBreakPointSpanMax < LineOfSightBreakSpacingMin,
                "§12: one 시야 차단 지점 must be narrower than the gap to the next one, or the two "
                + "readings of 간격 — how wide a chance is and how far apart chances are — collapse into each other.");
            Require(CentrePathMetresMin < CentrePathMetresMax, "§12-D: centre-path bounds must be ordered.");
            Require(CentrePathMetresMin > SprintMaxTravelDistance,
                "§12-D/§05: a storey has to be longer than one full sprint, or a runner crosses it "
                + "from rim to middle on a single bar and 「스태미나를 언제 쓸 것인가」 — §06's stated "
                + "dilemma — has one answer on every floor.");
            Require(LineOfSightBreakSpacingMin * 4f <= SprintMaxTravelDistance,
                "§12: 「질주 60m에 3~4번의 기회」 is what the 시야 차단 지점 간격 band means. If four "
                + "gaps at the floor of the band no longer fit inside one sprint, the band has "
                + "stopped saying what §12 says it says.");

            Require(PresenceSaturationSeconds > 3f * LineOfSightBreakSpacingMax / WalkSpeed,
                "§10/§12: the 그늘 must take longer to fill than three cover-to-cover crossings at walking "
                + "pace, or the building §12 specifies cannot be walked across in the dark and the dark stops "
                + "being a choice.");
            // The upper bound on PresenceSaturationSeconds used to be
            // BatterySecondsPerCell — the 그늘 had to fill faster than a cell emptied or
            // never switching the torch on was strictly safer than switching it on. The
            // battery is deleted, so that comparison had nothing left on its right-hand
            // side. What it protected is the switch being a real decision, and the half
            // of that which survives is asserted here instead: light is free now, so its
            // only remaining price is being seen from further away than the monster can
            // see you. FlashlightNoticeDistance's own doc has always claimed this
            // relationship; nothing tested it until the battery went.
            Require(FlashlightNoticeDistance > MonsterSightRange,
                "§03/§06: switching the light on has to cost something. The torch has no cell to "
                + "burn any more, so the whole price is that the beam is noticed from further than "
                + "the creature could have seen the body — at or below sight range the light is "
                + "free, and a free light in a dark maze is not a decision.");
            // The upper bound. It was never asserted, because the doc corroborated it with
            // RunnerTauntRange — 25 m == 25 m, an argument by coincidence between two
            // numbers rather than a derivation. The 주자 is deleted and so is that constant,
            // so the bound is stated against the rule that was always doing the work.
            Require(FlashlightNoticeDistance <= LineOfSightBreakSpacingMax,
                "§12: a lit beam must not be noticed from further than the longest sight line a "
                + "legal map is obliged to provide. Past that, the runner is given away at a "
                + "distance the map never promised they could see the creature across, and the "
                + "price of the torch stops being a thing they can weigh.");
            Require(PresenceDispersalSeconds > 0f && PresenceDispersalSeconds < PresenceSaturationSeconds,
                "§10: light must clear the 그늘 faster than dark fills it, and must not clear it instantly — "
                + "an instant reset would make one flick of a torch that costs nothing to flick undo "
                + "a minute of walking dark, and the dark would stop being a price.");
            Require(PresenceResidualPooling >= 0f && PresenceResidualPooling < PresenceWarnPooling,
                "§01: what survives a taking must sit below the warning, or the second taking arrives out of a "
                + "state the player was never shown leaving.");
            Require(PresenceWarnPooling > 0f && PresenceWarnPooling < 1f,
                "§10: the 그늘 has to announce itself before it takes anything.");
            Require((1f - PresenceWarnPooling) * PresenceSaturationSeconds > LineOfSightBreakSpacingMax / WalkSpeed,
                "§12: the warning must last long enough to walk to the nearest cover §12 guarantees.");
            // Was PresenceSilenceSeconds > ClueReadSeconds ("the silence has to outlast a
            // clue read"). Nothing is read now. The same argument survives against the
            // longest beat a player is actually asked to stand still and commit for,
            // which is shutting a 관문 — and which GhostSession.EndMatchHoldSeconds also
            // derives its 탈출 hold from.
            Require(PresenceSilenceSeconds > DoorShutSeconds,
                "§10: the silence has to outlast the longest deliberate act the game asks a player "
                + "to hold still for, or they could spend the whole toll completing that act and "
                + "speak the moment it ends — and losing the voice would have cost nothing.");
            Require(PresenceRecallFadeSeconds > PresenceSilenceSeconds,
                "§10: certainty must come back after the voice does. The two halves of the toll have to "
                + "overlap, or the second half is a separate event the player never connects to the dark.");
            Require(PresenceRecallSmear > 0f && PresenceRecallSmear < 1f,
                "§01/§10: the 그늘's second toll must degrade the player without disabling them. "
                + "PresenceSilenceSeconds already takes the voice outright, so a total second toll would "
                + "make the 그늘 a second unkillable pressure — and §01 spends its argument on there "
                + "being exactly one.");
            Require(PresenceMonsterClearRadius >= MonsterSightRange,
                "§01/§06: there must be no 그늘 anywhere the monster can already see you. One unkillable "
                + "pursuer — 이길 수 없는 적 → 공포가 유지된다 — and two pressures on one player at one moment "
                + "is how that stops being true.");
            // GONE with §04: a clause holding the 그늘's clear radius above
            // ObserverRange so the 관측자 could stand still in the dark and work.
            // Nobody stands still on purpose in a race. The clause above —
            // PresenceMonsterClearRadius >= MonsterSightRange — is the one that
            // carried the actual rule, that no runner ever faces §06 and the 그늘
            // in the same moment.
            Require(PresenceMonsterClearRadius > AggroReleaseDistance,
                "§06: a 주자 breaking aggro is inside the release distance by definition, so the 그늘 must not "
                + "be able to reach them mid-chase.");
            Require(PresenceMonsterFringeRadius > PresenceMonsterClearRadius,
                "§10: the withdrawal has to be gradual to be readable — a step function is a boolean nobody can "
                + "see arriving.");
            Require(PresenceBoldnessFloor > 0f
                && System.Math.Abs(PresenceBoldnessFloor + (PresenceBoldnessPerTier * (ThreatTierCount - 1)) - 1f) < 1e-4f,
                "§07: the 그늘 must reach exactly full strength on the last row of the threat table and not "
                + "before it. 동트기 전 is 생존 불가 수준; anything earlier invents a sixth night.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("GameConstants.Validate failed — " + message);
            }
        }
    }
}
