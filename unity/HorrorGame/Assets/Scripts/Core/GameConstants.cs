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
        /// Scaled per time-of-night by <see cref="Threat"/>.
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
        /// §08 states the rule for its own penalties, "§05 배율에 곱연산으로
        /// 적용된다" — so this stacks onto the directional multiplier and the carry
        /// weight rather than replacing either.
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
        /// <see cref="ListenerSelfNoiseThreshold"/> is quoted against. §04.
        /// <para>
        /// §04 prices the 청음사 with one constraint — "자기가 소리를 내면 못
        /// 듣는다" — and §08 sells a 소음기 for <see cref="ShopCostSuppressor"/> to
        /// buy out of it. Crouching is the free version of that trade and is
        /// deliberately weaker than the bought one: it costs half the player's speed
        /// and it does not silence a door, a landing or anything else
        /// <c>NoiseMeter</c> raises as a transient. Only the continuous term from
        /// moving is scaled.
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
        /// A cooldown rather than a stamina cost, and the choice is a §04 one: the
        /// bar belongs to the 주자 alone, so charging a jump to it would price the
        /// same verb differently for five roles and would spend the twelve seconds
        /// §06's entire aggro-release calculation is built on. A cooldown costs
        /// every role the same and touches none of §06's numbers.
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
        /// 0~1 scale. §04.
        /// <para>
        /// The opposite sign of <see cref="CrouchNoiseMultiplier"/> and the other
        /// half of the same idea: a jump buys a little height and pays the loudest
        /// price a body can pay in the one channel §04 charges in. Set above
        /// <see cref="ListenerSelfNoiseThreshold"/> — checked in
        /// <see cref="Validate"/> — so a 청음사 who jumps loses their feed for the
        /// transient, exactly as if they had opened a door.
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
        // 주자 can. That is §04's whole role expressed as one moment.

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

        /// <summary>
        /// How fast a half-broken door recovers when nothing is hitting it, as a fraction
        /// of the break rate. Below 1 because a §04 섬광수 that buys thirty seconds must
        /// not hand back a whole door — the building degrades over a match.
        /// </summary>
        public const float DoorRepairFraction = 0.25f;

        /// <summary>Distance at which aggro can break, metres. §06.</summary>
        public const float AggroReleaseDistance = 12f;

        /// <summary>Line of sight must stay broken this long to release aggro, seconds. §06.</summary>
        public const float AggroReleaseLineOfSightBreak = 3f;

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
        /// §06 gives no cone, and §04's 관측자 — "괴물의 시야를 본다 → 누가 표적인지" —
        /// only presupposes that the vision has a direction. 90° is the weakest
        /// claim that keeps that true: the monster sees the hemisphere it faces and
        /// nothing behind it, which leaves §12's "괴물이 지나가도 즉시 발각 안 됨" a
        /// question of geometry rather than of a tuned cone.
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
        /// Below <see cref="ListenerHearingRange"/> (40 m), because §04 gives the
        /// 청음사 one job and a monster that heard as far as they do would take it.
        /// Above <see cref="MonsterSightRange"/> (20 m), because §03 makes darkness the
        /// game's central information problem — a creature that saw further than it
        /// heard would be a creature the dark protects you from, and the dark is
        /// supposed to blind you, not it. Between those, 35 m is a zone
        /// diagonal (§12's band is 30~40 m): a player sprinting on 금속 grating is heard
        /// from anywhere in the zone they are in, and from nowhere outside it.
        /// </para>
        /// <para>
        /// The two multipliers do the rest, and they are the same numbers §04 hears
        /// with rather than a second set: 카펫 at 0.22 clarity carries a walk about 2 m
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
        /// erase §12's cover, §04's 관측자, and the whole reason to move quietly. So
        /// the rule stands: a patrol does not hunt what it merely sees.
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
        /// How close the monster must come to a path point to count as having
        /// reached it, metres. §06 — not a balance value; it exists so that path
        /// following terminates. It must stay below one <see cref="FixedStep"/> of
        /// travel (4.8 × 0.02 = 0.096 m), or the monster stalls short of every
        /// waypoint and jitters instead of walking.
        /// </summary>
        public const float MonsterWaypointTolerance = 0.05f;

        // ====================================================================
        // §08 — Carry weight to movement penalty.
        //   ≤5 정상 · 6~10 −15% · 11~15 −30% · ≥16 질주 불가
        // ====================================================================

        /// <summary>Highest total weight that carries no penalty. §08.</summary>
        public const int WeightFreeMax = 5;

        /// <summary>Highest total weight in the −15% band. §08.</summary>
        public const int WeightLightMax = 10;

        /// <summary>Highest total weight in the −30% band. §08.</summary>
        public const int WeightHeavyMax = 15;

        /// <summary>Multiplier for weight 6–10. §08.</summary>
        public const float WeightMulLight = 0.85f;

        /// <summary>Multiplier for weight 11–15. §08.</summary>
        public const float WeightMulHeavy = 0.70f;

        /// <summary>
        /// Multiplier at weight 16+. §08 states sprinting becomes impossible;
        /// the walk penalty continues the curve at the same slope.
        /// </summary>
        public const float WeightMulOverloaded = 0.55f;

        /// <summary>Base loot capacity in weight units, before a bag. §08.</summary>
        public const int BaseCarryCapacity = 10;

        /// <summary>Extra capacity a bag grants. §08.</summary>
        public const int BagCapacityBonus = 5;

        /// <summary>Movement multiplier while a bag is equipped. §08: −10%.</summary>
        public const float BagSpeedMultiplier = 0.90f;

        /// <summary>
        /// How many players one oversize 전리품 may be shared between. §08's
        /// 대형 초상화·궤짝 row says "2인 운반", and the section's "최고의 순간" is
        /// two players walking a chest down a narrow corridor — so this is two by
        /// design, not the first value of a general N-person rule.
        /// </summary>
        public const int SharedCarryMaxCarriers = 2;

        // --------------------------------------------------------------------
        // §08 전리품 table — weights. The 무게 column, verbatim.
        // --------------------------------------------------------------------

        /// <summary>은수저 · 잡동사니 weight. §08 — the conspicuous temptation.</summary>
        public const int LootWeightTrinket = 1;

        /// <summary>회중시계 · 반지 weight. §08 — "효율 최고" comes from this being 1.</summary>
        public const int LootWeightTimepiece = 1;

        /// <summary>금고 속 문서 weight. §08 — behind an 8 s safe, so the Engineer gates it.</summary>
        public const int LootWeightSafeDocument = 2;

        /// <summary>대형 초상화 · 궤짝 weight. §08 — equals <see cref="WeightFreeMax"/>, which is why one piece ends the free band.</summary>
        public const int LootWeightLargePiece = 5;

        // ====================================================================
        // §08 / §16-2 — Loot values and the price table. PROPOSED FIRST PASS.
        //
        // §16-2 calls this the project's top open question and supplies no
        // numbers at all, so every value below is *derived* from §08's growth
        // curve rather than picked. The chain, so the simulator can sweep from a
        // documented starting point:
        //
        //  1. Unit. One 회중시계·반지 = 25 credits. Every other number is quoted
        //     against it, so a price reads as "how many watches".
        //
        //  2. Loot values follow §08's 가치 column (낮음 · 중간 · 높음 · 매우 높음)
        //     under one hard constraint: §08 calls the watch "효율 최고", so its
        //     value per weight must beat every other piece —
        //       은수저 10/1 = 10 · 회중시계 25/1 = 25 · 문서 40/2 = 20 · 궤짝 100/5 = 20.
        //     The watch wins on efficiency; the chest wins on absolute value,
        //     which is what makes "2명이 궤짝을 들고" worth the risk.
        //
        //  3. Reference haul. A cautious hauler fills the free band
        //     (WeightFreeMax = 5); a confident one fills the −15% band
        //     (WeightLightMax = 10). Mixed small loot averages
        //     (10 + 25) / 2 = 17.5 credits per weight. §01 puts two of the four
        //     players on loot while the other two read clues:
        //       1차 (cautious):                    2 × 5  × 17.5        = 175
        //       2차 (cautious + §01's 궤짝):        2 × 5  × 17.5 + 100  = 275
        //       3차 (confident + a 금고 문서):       2 × 10 × 17.5 + 40   = 390
        //       4차 (confident):                   2 × 10 × 17.5        = 350
        //     Cumulative: 175 · 450 · 840 · 1190.
        //
        //  4. Restock per descent: two lights running ≈ 4 cells, plus one
        //     materials bundle → 4 × 25 + 40 = 140.
        //
        //  5. Prices are then pinned by §08's own growth table:
        //       · 1차 잠입 전 — wallet 0, and the cheapest item is 15. Nothing.
        //       · 1차 귀환 후 — 175 − 140 restock = 35, below the cheapest
        //         capability upgrade (밧줄 75). "소모품 몇 개" and nothing more.
        //       · 2~3차 귀환 후 — 310 in hand buys exactly one 강화 손전등 (250)
        //         and not two (500). It also reproduces §08's quoted argument
        //         "강화 손전등 하나 살까, 배터리 3개 살까?" — 310 covers either the
        //         flashlight or a full restock, never both.
        //       · 후반 — by 4차, 1190 earned − 4 × 140 restock = 630 covers
        //         강화 손전등 250 + 가방 100 + the dearest §11 role substitute
        //         (감지기 150) + 밧줄 75 = 575, with change. Credits stop being
        //         the constraint: "필요한 건 다 있는데 시간이 없다."
        //
        //  6. Anti-arbitrage: a shop item that is also loot must cost more than
        //     that loot sells for, or the team sells watches and buys them back
        //     forever. Vendor margin ×2 → 회중시계 item 50 vs loot value 25.
        //
        // EconomyTests recomputes all of this from these constants, so a retune
        // that breaks §08's curve fails the build instead of shipping.
        // ====================================================================

        /// <summary>은수저 · 잡동사니 sale value. §08 가치 "낮음" — 10 per weight, the worst efficiency.</summary>
        public const int LootValueTrinket = 10;

        /// <summary>회중시계 · 반지 sale value. §08 가치 "중간" and "효율 최고" — the pricing unit.</summary>
        public const int LootValueTimepiece = 25;

        /// <summary>금고 속 문서 sale value. §08 가치 "높음" — 20 per weight, under the watch's 25.</summary>
        public const int LootValueSafeDocument = 40;

        /// <summary>
        /// 대형 초상화 · 궤짝 sale value. §08 가치 "매우 높음" — four watches in one
        /// piece, at 20 per weight. High absolute value, mediocre efficiency: the
        /// chest is a decision, not an optimisation.
        /// </summary>
        public const int LootValueLargePiece = 100;

        /// <summary>Credits at match start. §08: "1차 잠입 전 구매력 0 — 맨몸으로 들어간다."</summary>
        public const int WalletStartingCredits = 0;

        /// <summary>Multiple of a loot piece's value that the shop charges for the same object. §08 — see the derivation above, item 6.</summary>
        public const int ShopVendorMarkup = 2;

        /// <summary>분필. §08 — the cheapest thing on the shelf; it records ground you already walked, so it buys no new reach.</summary>
        public const int ShopCostChalk = 15;

        /// <summary>배터리. §08 — the pricing unit: one 회중시계 pays for one cell, which is what makes selling loot feel like buying time.</summary>
        public const int ShopCostBattery = 25;

        /// <summary>정비 자재. §08 — one 금고 속 문서 funds one bundle, so the Engineer pays for himself.</summary>
        public const int ShopCostRepairMaterials = 40;

        /// <summary>응급킷. §08 — two watches. Rarely needed (only a survived grab), so it must not compete with the battery line.</summary>
        public const int ShopCostFirstAidKit = 50;

        /// <summary>회중시계 (정보). §08 lists no 대가, so price is the whole cost — and <see cref="ShopVendorMarkup"/> keeps it above the loot value.</summary>
        public const int ShopCostPocketWatch = 50;

        /// <summary>조명탄. §08 — an inferior stand-in for the Engineer's zone light (§11), single use, so it is priced just above a materials bundle.</summary>
        public const int ShopCostFlare = 60;

        /// <summary>
        /// 밧줄. §08 quotes the saving itself — "3층까지 걸어가면 5분이야". 300 s at
        /// the battery's implied 25 / 210 s ≈ 36 credits of time, doubled because
        /// the rope also removes the danger of that walk, rounded to 75.
        /// </summary>
        public const int ShopCostRope = 75;

        /// <summary>가방. §08 — one 대형 전리품 pays for it, and it makes room for exactly one more (see <see cref="BagCapacityBonus"/>).</summary>
        public const int ShopCostBag = 100;

        /// <summary>소음기. §08 — priced like the bag, but it voids the Listener; a team with one must not buy it at any price.</summary>
        public const int ShopCostSuppressor = 100;

        /// <summary>미끼. §08 marks it "1회용 · 비쌈" — five watches, gone in one use. It stands in for a missing 주자 (§11).</summary>
        public const int ShopCostBait = 125;

        /// <summary>감지기. §08 — the dearest §11 role substitute, because the Listener is the role whose absence §11 calls "공포 최대".</summary>
        public const int ShopCostDetector = 150;

        /// <summary>건물 도면. §08 marks it "비쌈" — and §12 makes the map learnable, so a veteran team should never want it.</summary>
        public const int ShopCostBlueprint = 200;

        /// <summary>
        /// 강화 손전등. §08's flagship ("이 목록의 대표작"). Set above the modelled
        /// first haul (175) so it cannot arrive after 1차, and below the second
        /// return's 310 so it arrives once, inside §08's "2~3차 귀환 후".
        /// </summary>
        public const int ShopCostUpgradedFlashlight = 250;

        /// <summary>Players on loot duty per descent in the reference model. §01 — the other two are reading clues.</summary>
        public const int EconomyReferenceHaulersPerDescent = 2;

        /// <summary>Battery cells the team burns per descent in the reference model — two lights running for roughly <see cref="BatterySecondsPerCell"/> each. §03 / §16-5.</summary>
        public const int EconomyReferenceCellsPerDescent = 4;

        // ====================================================================
        // §03 / §05 — Objective carrying.
        // ====================================================================

        /// <summary>
        /// Movement multiplier while carrying the objective. Both hands are used,
        /// so there is no flashlight and no sprint (§03). The escort, not the
        /// carrier, provides light.
        /// </summary>
        public const float ObjectiveCarrySpeedMultiplier = 0.80f;

        /// <summary>Weight units the objective occupies — it fills the hands entirely. §03.</summary>
        public const int ObjectiveWeight = 4;

        /// <summary>
        /// Living players an objective escort needs. §03: the carrier "양손을 쓴다"
        /// so "누군가 비춰줘야 한다", and the section settles it as "사실상 2인 1조
        /// 호송" — one pair of hands on the objective, one flashlight in front of it.
        /// <para>
        /// Read by §11's completability check, where it is the reason a party can
        /// always finish whichever role is absent: the escort is a property of the
        /// party size, not of anybody's ability. Kept at or below
        /// <see cref="PlayersPerMatch"/> for that argument to hold.
        /// </para>
        /// </summary>
        public const int ObjectiveEscortMinPlayers = 2;

        // ====================================================================
        // §04 — Role parameters.
        // ====================================================================

        /// <summary>Observer must be within this distance of the monster to read its vision, metres. §04.</summary>
        public const float ObserverRange = 15f;

        /// <summary>Observer must hold still this long before the read begins, seconds. §04.</summary>
        public const float ObserverStillSeconds = 3f;

        /// <summary>
        /// Speed below which the Observer counts as stationary, m/s. §05 decided
        /// mouselook stays free while the feet are pinned, so this gates
        /// translation only.
        /// </summary>
        public const float ObserverStillSpeedThreshold = 0.05f;

        /// <summary>How far the Listener can localise the monster by sound, metres. §04.</summary>
        public const float ListenerHearingRange = 40f;

        /// <summary>
        /// Self-noise above this level blinds the Listener. §04: "자기가 소리를
        /// 내면 못 듣는다" — running or opening a door cuts the feed.
        /// </summary>
        public const float ListenerSelfNoiseThreshold = 0.35f;

        /// <summary>Flash stun duration on the monster, seconds. §16-3 — provisional, needs playtest.</summary>
        public const float FlashStunSeconds = 2.5f;

        /// <summary>Flash cooldown, seconds. §04 trades strength for reusability. §16-3 provisional.</summary>
        public const float FlashCooldownSeconds = 18f;

        /// <summary>Maximum range at which a flash still stuns, metres. §04.</summary>
        public const float FlashRange = 8f;

        /// <summary>Cone half-angle the flash must contain the monster within, degrees. §04.</summary>
        public const float FlashConeHalfAngle = 35f;

        /// <summary>Seconds the Engineer needs to lock a door. §04 — a preparation role, no instant use.</summary>
        public const float EngineerDoorLockSeconds = 3.5f;

        /// <summary>Seconds the Engineer needs to place a barricade. §04.</summary>
        public const float EngineerBarricadeSeconds = 6f;

        /// <summary>Seconds the Engineer needs to arm a noise trap. §04.</summary>
        public const float EngineerTrapSeconds = 4f;

        /// <summary>Seconds the Engineer needs to open a safe. §04 / §08 (금고 속 문서).</summary>
        public const float EngineerSafeSeconds = 8f;

        /// <summary>Seconds to throw the zone breaker. §04.</summary>
        public const float EngineerZoneLightSeconds = 2f;

        /// <summary>
        /// Metres the Listener's fix can be off by at the very edge of hearing on
        /// a floor that gives nothing away.
        /// <para>
        /// §04 promises 위치 · 거리 · 이동 방향 but names no precision, and §12
        /// insists the precision has to come from the floor — "구역별로 바닥
        /// 재질이 달라야 청음사가 위치를 판별할 수 있다." This is the scale the
        /// material map modulates. Kept small against a 30~40 m zone diagonal
        /// (§12) so a bad fix names the wrong corner, and only names the wrong
        /// zone when the monster is close to a material boundary — which is
        /// exactly where §12 wants the confusion to live.
        /// </para>
        /// </summary>
        public const float ListenerErrorRadiusMax = 6f;

        /// <summary>
        /// Share of a material's error that survives at zero distance. §12 — the
        /// floor has to matter at every range, not just at the edge of hearing,
        /// or the material map stops being a map as soon as the monster is close.
        /// </summary>
        public const float ListenerNearErrorFactor = 0.25f;

        /// <summary>
        /// Worst-case error on the called movement direction, degrees. §04 gives
        /// the Listener 이동 방향; §05 answers how it is used — "몸을 돌려
        /// 삼각측량" — so the direction is a bearing to be checked, not a truth.
        /// </summary>
        public const float ListenerDirectionErrorMaxDegrees = 30f;

        /// <summary>
        /// Seconds between Listener fixes. §04 — the ability reads footsteps, so
        /// information arrives one step at a time. Re-randomising an estimate
        /// every simulation tick would read as jitter rather than as a position,
        /// and it would hide §06's real weapon: in 정지 the fix simply stops.
        /// </summary>
        public const float ListenerFixIntervalSeconds = 0.5f;

        /// <summary>
        /// Slowest movement, m/s, from which the Listener can still call a
        /// direction. §04 promises 이동 방향 for something that is actually
        /// moving; a monster shuffling below this has a position but no bearing.
        /// </summary>
        public const float ListenerMinDirectionSpeed = 0.25f;

        /// <summary>How clearly 나무 gives the monster away. §12: 삐걱 — a creak pins the spot.</summary>
        public const float ListenerClarityWood = 0.80f;

        /// <summary>How clearly 타일 gives the monster away. §12: 딱딱, 반향 — loud, and the reverb helps.</summary>
        public const float ListenerClarityTile = 0.85f;

        /// <summary>How clearly 자갈 gives the monster away. §12: 부스럭 — broadband but soft.</summary>
        public const float ListenerClarityGravel = 0.70f;

        /// <summary>
        /// How clearly 콘크리트 gives the monster away. §12: 둔탁 — the dullest
        /// floor on the map, which makes zone D the monster's best approach and
        /// gives the Listener a reason to care which room it is in.
        /// </summary>
        public const float ListenerClarityConcrete = 0.50f;

        /// <summary>
        /// How clearly 금속 gives the monster away. §12: 울림 — a stairwell
        /// transit is the single clearest signal in the map, which is what makes
        /// "지금 계단이야" a usable call.
        /// </summary>
        public const float ListenerClarityMetal = 1.00f;

        /// <summary>
        /// Clarity used for a surface with no material assigned. §12 fails a map
        /// with <c>FloorMaterial.None</c> in it, so this only ever applies to
        /// geometry that already violates the map rules — it degrades the
        /// Listener rather than crashing, and stays worse than every real floor
        /// so the omission is visible in play.
        /// </summary>
        /// <summary>
        /// 침수. §04 — the loudest thing to stand on, above even 금속.
        /// <para>
        /// Water is the one surface that cannot be crossed quietly at any speed, which is
        /// what makes a flooded storey a decision rather than a corridor: it is the
        /// fastest way down and it announces you the whole way.
        /// </para>
        /// </summary>
        public const float ListenerClarityWater = 1.00f;

        /// <summary>파헤쳐진 흙. §04 — soft, and quieter than concrete. Somebody dug this floor up.</summary>
        public const float ListenerClarityEarth = 0.40f;

        /// <summary>
        /// 카펫. §04 — the quietest surface, below even an unknown one.
        /// <para>
        /// Deliberately the Listener's blind spot. §04's ability is the team's early
        /// warning and a game where it always works has no route worth taking; carpet is
        /// where the monster arrives without being announced, and where a player who
        /// knows the building goes when they want the same.
        /// </para>
        /// </summary>
        public const float ListenerClarityCarpet = 0.22f;

        public const float ListenerClarityUnknown = 0.35f;

        /// <summary>
        /// Farthest distance from which the Runner can force the monster's aggro
        /// onto itself, metres. §04 gives the ability no number; §12 supplies one
        /// — the 개방 공간 exists so aggro is taken from 15~25 m out, and §12's
        /// own table concludes "주자는 멀리서 어그로를 걸어야 한다" because a
        /// 3 m start makes the release unreachable. This is the top of that band.
        /// </summary>
        public const float RunnerTauntRange = 25f;

        /// <summary>
        /// Fraction of the sprint bar the Runner must recover before sprinting
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
        /// How close the Engineer must stand to a door, panel, safe or trap
        /// anchor to work on it, metres. §04 makes the role 사전 준비형: the work
        /// is hands-on at the site, so stepping away abandons it instead of
        /// finishing at range.
        /// </summary>
        public const float EngineerReachDistance = 2.5f;

        /// <summary>정비 자재 spent locking one door. §04 (시간과 자재); quantities are §16-4, still open.</summary>
        public const int EngineerDoorLockMaterialCost = 1;

        /// <summary>
        /// 정비 자재 spent bringing a zone light up. §04 / §16-4. Cutting a zone
        /// back to darkness costs nothing — which is precisely why §04's "조명
        /// 끔" accident is so easy to have.
        /// </summary>
        public const int EngineerZoneLightMaterialCost = 1;

        /// <summary>정비 자재 spent arming one noise trap. §04 / §16-4.</summary>
        public const int EngineerTrapMaterialCost = 1;

        /// <summary>정비 자재 spent on one barricade. §04 / §16-4 — the heaviest install, and the hardest to undo.</summary>
        public const int EngineerBarricadeMaterialCost = 2;

        /// <summary>정비 자재 spent opening one safe. §04 / §08 (금고 속 문서) / §16-4.</summary>
        public const int EngineerSafeMaterialCost = 1;

        /// <summary>
        /// Noise level a tripped 소음 함정 emits, on the same 0~1 scale as
        /// <see cref="ListenerSelfNoiseThreshold"/>. §04 — the trap's entire job
        /// is to be the loudest thing in the zone, so it saturates the scale.
        /// </summary>
        public const float EngineerTrapNoiseLevel = 1.0f;

        // ====================================================================
        // §03 / §08 — Light and battery. "어둠 = 목표의 잠금장치."
        // ====================================================================

        /// <summary>Standard flashlight cone radius, metres. §03.</summary>
        public const float FlashlightRange = 12f;

        /// <summary>Standard flashlight cone half-angle, degrees. §03: the beam is narrow.</summary>
        public const float FlashlightHalfAngle = 22f;

        /// <summary>Upgraded flashlight range multiplier. §08: 반경 2배.</summary>
        public const float UpgradedFlashlightRangeMultiplier = 2.0f;

        /// <summary>
        /// How much further the monster notices an upgraded flashlight. §08 —
        /// the item's whole point is that brighter cuts both ways.
        /// </summary>
        public const float UpgradedFlashlightDetectionMultiplier = 2.0f;

        /// <summary>
        /// Battery life of one cell with the light on, seconds. §16-5 flags this
        /// as the value that sets the round-trip rhythm; 210 s ≈ 3.5 min is the
        /// starting point the simulator sweeps.
        /// </summary>
        public const float BatterySecondsPerCell = 210f;

        /// <summary>Battery drain multiplier while the light is off. §03 — time alone still costs.</summary>
        public const float BatteryIdleDrainMultiplier = 0.15f;

        /// <summary>Extra drain charged the moment the light is switched on. §03: "켤 때마다".</summary>
        public const float BatterySwitchOnCost = 1.5f;

        /// <summary>Fraction of range lost during 심야. §07: 손전등 반경 −30%.</summary>
        public const float LateNightFlashlightPenalty = 0.30f;

        /// <summary>Radius a zone light or flare illuminates, metres. §03.</summary>
        public const float ZoneLightRadius = 18f;

        /// <summary>Flare burn duration, seconds. §08 — single use.</summary>
        public const float FlareSeconds = 45f;

        /// <summary>
        /// Seconds a clue must be held in the beam before it becomes readable.
        /// §03: "오래 비춰야 읽힌다" is what makes reading a clue dangerous.
        /// </summary>
        public const float ClueReadSeconds = 2.5f;

        /// <summary>Distance within which a clue can be read at all, metres. §03.</summary>
        public const float ClueReadRange = 3f;

        /// <summary>
        /// How far away the monster notices a lit standard flashlight, metres.
        /// <para>
        /// §03 lists "괴물이 잘 본다" as the flashlight's price and §08 doubles it for
        /// the 강화 손전등, but neither section gives the base number. It is bounded
        /// from both sides. It must exceed <see cref="MonsterSightRange"/> (20 m) or
        /// §03's price is void — the monster would already see the body at that
        /// distance and switching the light on would cost nothing. And §12 caps the
        /// sight lines a legal map has to provide at
        /// <see cref="LineOfSightBreakSpacingMax"/> (25 m), so anything beyond that
        /// is a distance the map is never obliged to make usable. 25 m is the only
        /// value that satisfies both, and it coincides with
        /// <see cref="RunnerTauntRange"/>: the Runner can pull aggro from exactly as
        /// far as a lit teammate can be given away.
        /// </para>
        /// <para>
        /// Provisional in the §16 sense — no section settles it. Note that ×2 from
        /// here (§08) lands at 50 m, which no §12-legal map can supply a sight line
        /// for; that consequence is pinned by <c>LightTests</c> and recorded in
        /// docs/BALANCE-FINDINGS.md rather than fixed here.
        /// </para>
        /// </summary>
        public const float FlashlightNoticeDistance = 25f;

        /// <summary>
        /// Noise a 조명탄 makes when it is struck, on the same 0~1 scale as
        /// <see cref="ListenerSelfNoiseThreshold"/>. §08 lists "소리를 낸다" as the
        /// flare's price and gives no level.
        /// <para>
        /// Above <see cref="ListenerSelfNoiseThreshold"/> (0.35), so a 청음사 who
        /// strikes one cuts their own feed — §04's "자기가 소리를 내면 못 듣는다". Without
        /// that the flare would be a free substitute for the Engineer rather than
        /// §11's stated 열등한 대체재, and the noise clause would price nothing. Below
        /// <see cref="EngineerTrapNoiseLevel"/> (1.0), which saturates the scale
        /// because being the loudest thing in the zone is the trap's entire job.
        /// </para>
        /// </summary>
        public const float FlareIgniteNoiseLevel = 0.70f;

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
        /// Standstill chance from 새벽 onwards. §07: "없음" — the monster never
        /// stops again, which is also the moment the Listener stops being able to
        /// lose it and the escort has to move under continuous noise.
        /// </summary>
        public const float ThreatStandstillChanceNone = 0f;

        // ====================================================================
        // §09 — Ghost state.
        // ====================================================================

        /// <summary>Cooldown between object rattles, seconds. §09 — the source of the ghost's anguish.</summary>
        public const float GhostRattleCooldownSeconds = 45f;

        /// <summary>How far from a ghost an object can be rattled, metres. §09.</summary>
        public const float GhostRattleRange = 4f;

        // ====================================================================
        // §13 — Networking and voice.
        // ====================================================================

        /// <summary>
        /// Beyond this distance voice packets are not sent at all, metres. §13 —
        /// "전부 받아놓고 볼륨만 0으로 재생하면 클라이언트 조작으로 다 들린다."
        /// This is an anti-eavesdropping rule, not a bandwidth optimisation.
        /// </summary>
        public const float VoiceCutoffDistance = 30f;

        /// <summary>Player count per match. §11.</summary>
        public const int PlayersPerMatch = 4;

        /// <summary>Roles to choose from. §11 — one is always missing.</summary>
        public const int RoleCount = 5;

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
        /// Anything wider than this inverts §12's first conclusion, 「주자는 멀리서
        /// 어그로를 걸어야 한다」: distance stops being what the Runner has to buy.
        /// </para>
        /// <para>
        /// The measured consequence, on the geometry the game ships: a 5 m connector
        /// between two bends releases from anywhere on the map, and only a 2.5 m one
        /// leaves the release conditional on how far out aggro was taken.
        /// </para>
        /// </summary>
        public const float SightBreakPointSpanMax = SingleCornerMinDistance - RunnerTestAggroStartDistance;

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
        /// and on one floor 4~6 zones is what keeps a zone big enough to hold §12's own
        /// geometry and small enough that §04's Listener can name it. The shipped
        /// building is five storeys with one zone each, and the cap has been doing a
        /// different job since: it was the number of FLOORS the game could have.
        /// </para>
        /// <para>
        /// What the band was protecting is protected by other rules that did not change —
        /// <see cref="ZoneDiagonalMin"/>~<see cref="ZoneDiagonalMax"/> still keeps a zone
        /// inside one sprint, entry points are still 2~3, and every zone still needs its
        /// own surface, which is now the real cap: eight materials, so nine zones only if
        /// one of them is the stairwells' 금속.
        /// </para>
        /// </summary>
        public const int ZoneCountMax = 9;

        /// <summary>Smallest zone diagonal, metres. §12.</summary>
        public const float ZoneDiagonalMin = 30f;

        /// <summary>Largest zone diagonal, metres. §12.</summary>
        public const float ZoneDiagonalMax = 40f;

        /// <summary>Map extent along one axis, metres. §12: 100 × 100.</summary>
        // Raised from 100 m on 2026-08-02. The 100 came from "주자가 구역 2~3개 관통
        // 가능 on one sprint of 60 m", and that argument is about ZONE size, not about the
        // building's footprint: a zone is still capped at a 30~40 m diagonal, so a sprint
        // still crosses two or three of them whatever the outline is. What the old cap
        // actually bounded was how many zones could exist at all, and with five floor
        // surfaces that was five — which is why every storey read the same.
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

        /// <summary>Observation posts required per zone. §12 — without them the Observer must walk into death.</summary>
        public const int ObservationPostsPerZoneMin = 1;

        /// <summary>Lockable doors per zone, lower bound. §12.</summary>
        public const int LockableDoorsPerZoneMin = 1;

        /// <summary>Lockable doors per zone, upper bound. §12 — more than this makes the Engineer omnipotent.</summary>
        public const int LockableDoorsPerZoneMax = 2;

        /// <summary>Clue/objective candidate sites per zone. §12 — one is chosen per match.</summary>
        public const int CandidateSitesPerZone = 3;

        /// <summary>Escape routes every candidate site must have. §12.</summary>
        public const int CandidateSiteMinExits = 2;

        /// <summary>Runner-test success rate below which the map is too hard. §12.</summary>
        public const float RunnerTestPassRateMin = 0.50f;

        /// <summary>Runner-test success rate above which the map is too easy. §12.</summary>
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
        // §03 — Randomisation.
        // ====================================================================

        /// <summary>Clues needed to pin the objective down. §03 narrows floor, then zone, then site.</summary>
        public const int CluesRequiredToLocate = 3;

        /// <summary>Fewest round trips a match can take. §03: lucky.</summary>
        public const int ExpectedRoundTripsMin = 2;

        /// <summary>Most round trips a match can take. §03: unlucky.</summary>
        public const int ExpectedRoundTripsMax = 5;

        // --------------------------------------------------------------------
        // §03 혼동쌍 — the deliberate confusion pairs and what makes them fire.
        //   6↔9 뒤집힌 각도 · 1↔7 손글씨체 · ㅁ↔ㅇ 흐릿할 때 · 좌↔우 거울·반사면
        // §03 calls the resulting memory error "이 게임의 주된 웃음이자 사망 원인",
        // so these are the numbers that decide how often the joke lands.
        // --------------------------------------------------------------------

        /// <summary>
        /// Seconds of unbroken reading after which a mark is as clear as it will
        /// ever get. §03 lists "급하게 봐야 하는 상황" as a reading obstacle, so time
        /// spent has to be a term in the misread model and not just a gate:
        /// <see cref="ClueReadSeconds"/> buys the read, this buys confidence in it.
        /// </summary>
        public const float ClueConfidentReadSeconds = 5f;

        /// <summary>
        /// Speed below which a reader counts as holding still, m/s. §03: a clue is
        /// read by holding a narrow beam on it, which cannot be done while walking.
        /// Deliberately separate from <see cref="ObserverStillSpeedThreshold"/> —
        /// §04's Observer and §03's reader are different rules and must be
        /// retunable apart.
        /// </summary>
        public const float ClueReadStillSpeedThreshold = 0.05f;

        /// <summary>
        /// Light quality (0–1) below which nothing can be read at all. §03's
        /// "어둠 = 목표의 잠금장치" is a lock, not a penalty, so this is a hard gate:
        /// below it the read fails outright rather than becoming unreliable.
        /// </summary>
        public const float ClueMinReadableLightQuality = 0.20f;

        /// <summary>
        /// Blur (0–1) at which a mark stops being a clue at all. §03 plants
        /// "낡아서 지워진 부분" as an obstacle; past this point the mark carries no
        /// information rather than wrong information.
        /// </summary>
        public const float ClueIllegibleBlur = 0.90f;

        /// <summary>
        /// How far out of upright a glyph must be rotated before it reads as
        /// inverted, degrees. §03's 6↔9 pair fires "뒤집힌 각도에서"; below this the
        /// mark is merely tilted and a 6 is still a 6.
        /// </summary>
        public const float ClueInvertedViewAngle = 120f;

        /// <summary>
        /// Blur (0–1) above which ㅁ and ㅇ begin to merge. §03's third pair fires
        /// "흐릿할 때", and this is where "흐릿" starts.
        /// </summary>
        public const float ClueBlurConfusionThreshold = 0.45f;

        /// <summary>
        /// Highest probability a confusion pair may reach, at full condition
        /// strength under the worst still-legible viewing. High on purpose — §03
        /// wants misremembering to be a main cause of death — but below 1, because
        /// a mark that is always wrong is a lie rather than an ambiguity.
        /// </summary>
        public const float ClueMisreadChanceMax = 0.65f;

        /// <summary>
        /// Fraction of <see cref="ClueMisreadChanceMax"/> that survives full light
        /// and an unhurried look. Above zero on purpose: §03 plants the ambiguity
        /// in the mark itself, so care reduces the error without removing it.
        /// </summary>
        public const float ClueMisreadFocusedFraction = 0.50f;

        /// <summary>
        /// Weight of §03's 6↔9 row. The strongest pair — an inverted 6 simply is a
        /// 9, so there is nothing for care to work with.
        /// </summary>
        public const float ClueMisreadWeightInvertedAngle = 1.00f;

        /// <summary>
        /// Weight of §03's 1↔7 row. The weakest pair: however bad the handwriting,
        /// a 1 still has one stroke and a 7 has two.
        /// </summary>
        public const float ClueMisreadWeightHandwriting = 0.70f;

        /// <summary>Weight of §03's ㅁ↔ㅇ row (흐릿할 때).</summary>
        public const float ClueMisreadWeightBlur = 0.85f;

        /// <summary>
        /// Weight of §03's 좌↔우 row (거울 · 반사면). As strong as the inverted pair:
        /// a reflection genuinely reverses the mark, so the reader is not making a
        /// mistake so much as believing what is in front of them.
        /// </summary>
        public const float ClueMisreadWeightReflection = 1.00f;

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
        /// being a choice. It must be <b>shorter</b> than
        /// <see cref="BatterySecondsPerCell"/> (210 s), or never switching the
        /// light on would be strictly safer than switching it on, which is the
        /// exact failure the 그늘 exists to remove.
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
        /// answer would be to flick the torch once a minute and §03's dilemma
        /// would be priced at one <see cref="BatterySwitchOnCost"/>. Three-to-one
        /// makes travelling dark genuinely cheaper than travelling lit and still
        /// makes it something you have to keep paying for.
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
        /// Extra §03 misread-condition strength carried by a player the 그늘 has
        /// just taken. The second half of the toll: you cannot say it, and then
        /// you are not sure of it.
        /// <para>
        /// Exactly <c>1 − <see cref="ClueMisreadFocusedFraction"/></c>, which is the
        /// share of §03's misread chance that a careful, well-lit look removes. So
        /// the 그늘 can take back precisely the benefit of having been careful and
        /// no more — it makes a good read into an average one, never into a lie.
        /// </para>
        /// </summary>
        public const float PresenceRecallSmear = 1f - ClueMisreadFocusedFraction;

        /// <summary>
        /// Seconds over which <see cref="PresenceRecallSmear"/> fades once the
        /// player is out of the dark. Longer than
        /// <see cref="PresenceSilenceSeconds"/> on purpose: the voice comes back
        /// before the certainty does, so the player says the number while still
        /// unsure of it. §03: "6이었나 9였나…"
        /// </summary>
        public const float PresenceRecallFadeSeconds = 30f;

        /// <summary>
        /// Radius around the monster inside which there is no 그늘 at all, metres.
        /// <para>
        /// Derived, not chosen: it is <see cref="MonsterSightRange"/>. §01 keeps
        /// its horror with <b>one</b> unkillable pursuer, so wherever the monster
        /// can see you the 그늘 is not there — the two are never pressure at the
        /// same moment on the same player. Three consequences fall out of that one
        /// line and each is asserted by a test:
        /// </para>
        /// <list type="bullet">
        /// <item><description>§06's chase is untouched. A chase requires a sight
        /// line, a sight line requires ≤ 20 m, and at ≤ 20 m the density is
        /// zero.</description></item>
        /// <item><description>§04's 관측자 is structurally immune. The role works
        /// at <see cref="ObserverRange"/> = 15 m and must hold still for 3 s in the
        /// dark, which is otherwise exactly what the 그늘 punishes.</description></item>
        /// <item><description>The dark withdrawing is a tell. §07's 정지 makes the
        /// monster silent and §04's 청음사 loses it; the pool falling is the one
        /// cue that still says "it is close", and it is available to every role
        /// because §01 says 네 사람은 같은 것을 보고 같은 것을 듣는다.</description></item>
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
                "§07: a door that fully repairs makes the building the same at 동트기 전 "
                + "as at 초저녁, and one that never repairs makes a single flash permanent.");

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
                "§04 주자: the sprint has to survive one. If a lunge catches a sprinting "
                + "Runner too, §04's role has no moment of its own and §06's 0.8 m/s of "
                + "margin buys nothing at the only instant it matters.");
            Require(MonsterAttackReach < MonsterAttackRange,
                "§06: the creature must commit from further away than it can reach, or "
                + "the lunge is the old touch-kill with an animation in front of it.");
            Require(MonsterAttackRecoverySeconds > MonsterAttackContactSeconds * 0.5f,
                "§06: a miss has to cost something. Recovery shorter than half the strike "
                + "would let it re-commit before the player has covered any ground.");

            Require(MonsterFootstepHearingRange > MonsterSightRange,
                "§03: 어둠이 정보를 가린다. Hearing shorter than sight would make the dark a "
                + "thing that protects the player from the monster, when the whole design "
                + "is that it blinds the player and not the creature.");
            Require(MonsterFootstepHearingRange < ListenerHearingRange,
                "§04 청음사: 「소리로 위치를 파악한다」 is the whole role. A monster that heard "
                + "as far as the Listener does would leave them with nothing to add.");
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
                "§04: crouching is the free 소음기 — it has to be quieter than walking and it must not be "
                + "silent, or §08 could not sell the bought version.");
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
                "§04: a landing has to cut the 청음사's feed. A jump that is silent is a free verb, and "
                + "§04 charges for 뛰거나 문을 열면 정보가 끊긴다.");

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

            Require(WeightFreeMax < WeightLightMax && WeightLightMax < WeightHeavyMax,
                "§08: the weight bands must be ordered.");
            Require(WeightMulOverloaded < WeightMulHeavy && WeightMulHeavy < WeightMulLight && WeightMulLight < 1f,
                "§08: heavier bands must be strictly slower.");
            Require(RunnerSprintSpeed * WeightMulHeavy < MonsterBaseSpeed,
                "§08: greed must cost the Runner its escape — at 11+ weight the sprint has to fall below the monster.");

            Require(ThreatTierSeconds * 3f < TargetMatchSecondsMax,
                "§07: a match must be long enough to reach the late tiers.");
            Require(ObserverRange > AggroReleaseDistance,
                "§04/§06: the Observer must be able to work from outside the release distance.");
            Require(VoiceCutoffDistance > ZoneLightRadius,
                "§13: voice must carry at least as far as a lit zone, or coordination inside one breaks.");

            Require(ZoneCountMin <= ZoneCountMax, "§12: zone bounds must be ordered.");
            Require(DeadEndRatioMin < DeadEndRatioMax, "§12: dead-end ratio bounds must be ordered.");
            Require(RunnerTestPassRateMin < RunnerTestPassRateMax, "§12: runner-test bounds must be ordered.");
            Require(LineOfSightBreakSpacingMin < LineOfSightBreakSpacingMax, "§12: cover-spacing bounds must be ordered.");
            Require(SightBreakPointSpanMax > 0f && SightBreakPointSpanMax < LineOfSightBreakSpacingMin,
                "§12: one 시야 차단 지점 must be narrower than the gap to the next one, or the two "
                + "readings of 간격 — how wide a chance is and how far apart chances are — collapse into each other.");

            Require(PresenceSaturationSeconds > 3f * LineOfSightBreakSpacingMax / WalkSpeed,
                "§10/§12: the 그늘 must take longer to fill than three cover-to-cover crossings at walking "
                + "pace, or the building §12 specifies cannot be walked across in the dark and the dark stops "
                + "being a choice.");
            Require(PresenceSaturationSeconds < BatterySecondsPerCell,
                "§10/§03: the 그늘 must fill faster than a battery empties. If it did not, never switching the "
                + "light on would be strictly safer than switching it on — which is the exact one-way switch "
                + "the 그늘 exists to close.");
            Require(PresenceDispersalSeconds > 0f && PresenceDispersalSeconds < PresenceSaturationSeconds,
                "§10: light must clear the 그늘 faster than dark fills it, and must not clear it instantly — "
                + "an instant reset prices §03's dilemma at one BatterySwitchOnCost.");
            Require(PresenceResidualPooling >= 0f && PresenceResidualPooling < PresenceWarnPooling,
                "§01: what survives a taking must sit below the warning, or the second taking arrives out of a "
                + "state the player was never shown leaving.");
            Require(PresenceWarnPooling > 0f && PresenceWarnPooling < 1f,
                "§10: the 그늘 has to announce itself before it takes anything.");
            Require((1f - PresenceWarnPooling) * PresenceSaturationSeconds > LineOfSightBreakSpacingMax / WalkSpeed,
                "§12: the warning must last long enough to walk to the nearest cover §12 guarantees.");
            Require(PresenceSilenceSeconds > ClueReadSeconds,
                "§03: the silence has to outlast a clue read, or a player could read and speak through it and "
                + "the toll would cost nothing.");
            Require(PresenceRecallFadeSeconds > PresenceSilenceSeconds,
                "§03: certainty must come back after the voice does — the player has to say the number while "
                + "still unsure of it.");
            Require(PresenceRecallSmear > 0f && PresenceRecallSmear <= 1f - ClueMisreadFocusedFraction,
                "§03: the 그늘 may take back the benefit of a careful look and no more. Above this it would be "
                + "inventing misreads rather than removing care.");
            Require(PresenceMonsterClearRadius >= MonsterSightRange,
                "§01/§06: there must be no 그늘 anywhere the monster can already see you. One unkillable "
                + "pursuer — 이길 수 없는 적 → 공포가 유지된다 — and two pressures on one player at one moment "
                + "is how that stops being true.");
            Require(PresenceMonsterClearRadius > ObserverRange,
                "§04/§11: the 관측자 holds still in the dark 15 m from the monster, which is otherwise exactly "
                + "what the 그늘 punishes. §11 gives that role no purchasable substitute, so it must be immune "
                + "while doing its job rather than merely discouraged.");
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
