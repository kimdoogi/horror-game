#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// How far the first-person camera is allowed to move on its own: stride
    /// amplitudes, landing depth, breathing rate, flinch length.
    /// <para>
    /// <b>Why these are not in <c>GameConstants</c>.</b> ARCHITECTURE §2 puts every
    /// tuned number there, and every number below that the <em>design</em> reasons
    /// about is read from there and only from there — <see cref="StrideCycleMetres"/>
    /// is the one exception and it is anatomy rather than balance. What is left is
    /// millimetres and degrees of camera displacement: it changes how the game feels
    /// to hold, not what happens in it. <c>HorrorGame.Audio.AudioTuning</c> set this
    /// precedent for gain staging and states the escape clause this file inherits —
    /// if one of these ever becomes a balance dial it moves to <c>GameConstants</c>
    /// with a § citation at that point.
    /// </para>
    /// <para>
    /// <b>The line is drawn at "can this change who catches whom".</b> Nothing here
    /// can. The offsets are applied to the camera transform alone; §05's speed table
    /// is untouched, and the beam's rotation still comes from <c>PlayerLook</c>
    /// because §05 makes beam direction information a teammate reads.
    /// </para>
    /// <para>
    /// <b>Field of view is absent, and the omission is the decision.</b> A sprint FOV
    /// punch is the standard way to sell speed and it is unavailable here: §05 makes
    /// FOV a balance item — "95+ 곁눈질이 너무 쉬워 딜레마가 약화된다" — because a
    /// wider view reveals the monster at a smaller and therefore cheaper turn of the
    /// mouse. Widening it while sprinting would hand the player a discount on §05's
    /// peek at the exact moment §14's question 2 is being asked, and it would do it
    /// invisibly, because the settings screen would still read 80. Speed is sold by
    /// stride rate and stride weight instead, and neither costs anything.
    /// </para>
    /// <para>
    /// <b>Amplitudes are half-amplitudes, in metres and degrees.</b> A vertical
    /// figure of 0.013 travels 2.6 cm peak to peak. Real walking moves a head about
    /// 5 cm and running about 10, and a camera has to run well under life because
    /// the player's inner ear is not moving and will not cancel any of it. These sit
    /// at roughly half — 2.6 of 5 at §06's 걷기, 5.8 of 10 at its 달리기 — rising to
    /// about three quarters at §04's 질주, which is the twelve seconds §14's first
    /// question is actually about. The first draft was near life-size at every speed
    /// and <c>ViewMotionShot</c>'s measurement is what caught it, because 8.4 cm of
    /// eye travel is a number and "it looks a bit much" is not.
    /// </para>
    /// </summary>
    public static class ViewMotionTuning
    {
        // ====================================================================
        // Stride. §05's speeds decide the rate; only the size is chosen here.
        // ====================================================================

        /// <summary>
        /// One pace, metres — the distance between two footfalls.
        /// <para>
        /// Anatomy, not balance: <c>ASSETS.md</c> stands <c>Player.fbx</c> at 1.75 m
        /// and a walking step is close to 0.45 of standing height. It is declared
        /// here rather than in two places because <see cref="PlayerFootsteps"/>'s
        /// no-animator fallback measures the same distance, and a stride length that
        /// disagreed with the bob would put the footstep sound off the bottom of the
        /// step — which is the single most noticeable defect this whole file exists
        /// to avoid.
        /// </para>
        /// </summary>
        public const float StrideMetres = 0.8f;

        /// <summary>
        /// How tall the player stands, metres. <c>ASSETS.md</c>'s figure for
        /// <c>Player.fbx</c>, declared here because two things in this file are
        /// derived from it and <see cref="PlayerFeelHarnessMenu"/> sizes the
        /// collider from the same number. A description of the model, not a tuned
        /// game value.
        /// </summary>
        public const float RigHeightMetres = 1.75f;

        /// <summary>
        /// Metres travelled per full bob cycle. Two paces: the head rises and falls
        /// once per foot, and sways left and right once per <em>pair</em> of feet,
        /// which is the figure-eight a walking head actually traces.
        /// </summary>
        public const float StrideCycleMetres = StrideMetres * 2f;

        /// <summary>Vertical half-amplitude at §06's 걷기 2.0 m/s, metres.</summary>
        public const float StrideVerticalWalk = 0.013f;

        /// <summary>
        /// Vertical half-amplitude at §06's 달리기 4.5 m/s, metres. Two and a half
        /// times the walk figure: the difference between walking and running has to
        /// be felt without looking at a speedometer, because §05's whole table is
        /// about knowing how fast you are going while facing away from the thing
        /// chasing you.
        /// </summary>
        public const float StrideVerticalRun = 0.029f;

        /// <summary>Lateral half-amplitude at walk, metres. Half the vertical, once per pair of steps.</summary>
        public const float StrideLateralWalk = StrideVerticalWalk * 0.5f;

        /// <summary>Lateral half-amplitude at run, metres.</summary>
        public const float StrideLateralRun = StrideVerticalRun * 0.5f;

        /// <summary>
        /// Roll into the sway at walk, degrees.
        /// <para>
        /// Roll is the component that makes a bob read as a body rather than as a
        /// lift, and it is also the component that makes people ill, so it is the
        /// smallest of the three. It is additionally the only one with a rule
        /// downstream: <c>PlayerInteractor</c> feeds camera roll to §03's
        /// <c>MisreadCondition.InvertedAngle</c>, which ramps from
        /// <c>GameConstants.ClueInvertedViewAngle</c> upward. These values are two
        /// orders of magnitude below that, and roll is the one term that scales to
        /// exactly zero at a standstill — which is where §03 reads a clue from.
        /// </para>
        /// </summary>
        public const float StrideRollWalkDegrees = 0.45f;

        /// <summary>
        /// Roll into the sway at run, degrees. Held down so that §04's 주자 질주 —
        /// which extrapolates past this by 1.44 — lands under a degree: roll is what
        /// a plate diff punishes hardest, because rotating the frame moves every
        /// pixel at the edges rather than a band of them.
        /// </summary>
        public const float StrideRollRunDegrees = 0.82f;

        /// <summary>
        /// Ground speed below which the stride is silent, m/s. §04's own
        /// "이동 정지" threshold, reused: the Observer's three seconds of stillness
        /// and the camera's idea of standing still must be the same instant, or a
        /// player holding position for §04 would watch the view keep walking.
        /// </summary>
        public static float StrideIdleSpeed
        {
            get { return GameConstants.ObserverStillSpeedThreshold; }
        }

        // ====================================================================
        // §05's directional cost, felt rather than read.
        // ====================================================================

        /// <summary>
        /// How far the view leans back when §05's directional multiplier is at its
        /// worst, degrees of pitch.
        /// <para>
        /// §05's central claim is "뒷걸음은 괴물보다 느리다. 뒤를 보면 잡힌다", and
        /// §14's question 2 asks whether the player <em>agonises</em> over it. Today
        /// the rules charge 35 % of the player's speed for looking back and the
        /// player's hands feel nothing at all — the cost is real, invisible and
        /// therefore not a dilemma. This is the cost as a sensation: backing away
        /// tips the view up a degree, like leaning against the direction of travel.
        /// </para>
        /// <para>
        /// It is deliberately not a screen effect or a HUD arrow. §05 insists the
        /// penalty is <em>analogue</em> — "이산적 선택이 아니라 아날로그 조절이라는
        /// 점이 중요하다 — 마우스를 몇 도 돌릴지가 곧 실력이다" — so the signal has
        /// to be continuous in the heading angle as well, which is why
        /// <see cref="PlayerViewMotion"/> drives it from
        /// <c>SpeedResolver.DirectionalMultiplier</c> and never from which key is
        /// down.
        /// </para>
        /// </summary>
        public const float DragPitchDegrees = 1.1f;

        /// <summary>
        /// Extra stride weight at the worst directional multiplier, as a fraction of
        /// the base amplitude. Backing away is heavier as well as slower.
        /// </summary>
        public const float DragStrideGain = 0.45f;

        /// <summary>
        /// Seconds for the drag to follow a change of heading.
        /// <para>
        /// Slower than the mouse on purpose. The lean is a body reacting, and a body
        /// that snapped between headings would read as the screen tilting rather
        /// than as effort. Short enough that the 45° peek §05 calls the skilled play
        /// still resolves inside the time it takes to look and look back.
        /// </para>
        /// </summary>
        public const float DragFollowSeconds = 0.22f;

        // ====================================================================
        // Landing. §12 stacks five storeys and seven stairwells; the descent is
        // most of the match and currently weighs nothing.
        // ====================================================================

        /// <summary>
        /// Downward speed below which a landing is not a landing, m/s.
        /// <para>
        /// Derived rather than picked: <c>PlayerMotor.ApplyGravity</c> keeps one
        /// step of gravity pressed into the floor so <c>isGrounded</c> does not
        /// flicker, so a player standing still is always descending a little. Four
        /// fixed steps of free fall is the point past which the controller is
        /// genuinely unsupported rather than being held down.
        /// </para>
        /// </summary>
        public static float LandingIgnoredBelowMps
        {
            get { return 9.81f * (GameConstants.FixedStep * 4f); }
        }

        /// <summary>
        /// Downward speed at which the landing dip is at full depth, m/s — the speed
        /// reached falling your own standing height. Below it the dip scales down, so
        /// a stair riser ticks and a drop off a gantry thumps, from one rule.
        /// <para>
        /// The first draft used one pace (0.8 m) as the reference and
        /// <c>ViewMotionShot</c> caught it: stepping off a 40 cm kerb came out at 63 %
        /// of a full impact, which is a stumble rather than a step. Your own height is
        /// both a bigger number and a better story — "you fell further than you are
        /// tall" is the drop that should jar.
        /// </para>
        /// </summary>
        public static float LandingFullMps
        {
            get { return Mathf.Sqrt(2f * 9.81f * RigHeightMetres); }
        }

        /// <summary>How far the eye drops on a full-strength landing, metres.</summary>
        public const float LandingDipMetres = 0.085f;

        /// <summary>How far the view pitches down on a full-strength landing, degrees.</summary>
        public const float LandingPitchDegrees = 2.6f;

        /// <summary>
        /// Seconds for the dip to spring back. Short: a landing is an impact, and an
        /// impact that takes half a second to recover reads as the camera being
        /// broken rather than as the floor being hard.
        /// </summary>
        public const float LandingRecoverSeconds = 0.22f;

        /// <summary>
        /// Minimum seconds between landings, so a flight of §12's stairs produces a
        /// rhythm rather than one continuous shudder.
        /// </summary>
        public const float LandingRefractorySeconds = 0.12f;

        // ====================================================================
        // §06's stamina bar, made visible without a HUD element.
        // ====================================================================

        /// <summary>
        /// Breaths per second with a full bar. About 17 a minute — a person standing
        /// in a basement, not a person at rest.
        /// </summary>
        public const float BreathRateRestedHz = 0.28f;

        /// <summary>Breaths per second with an empty bar. About 50 a minute.</summary>
        public const float BreathRateSpentHz = 0.85f;

        /// <summary>
        /// Vertical half-amplitude of a spent breath, metres. It is the one component
        /// that survives standing still, so it is the smallest.
        /// </summary>
        public const float BreathVerticalMetres = 0.011f;

        /// <summary>Pitch half-amplitude of a spent breath, degrees.</summary>
        public const float BreathPitchDegrees = 0.42f;

        /// <summary>
        /// How much of the breath survives a full bar, 0–1. Not zero: §12 puts the
        /// player four storeys underground and a camera that is perfectly still is a
        /// camera on a tripod.
        /// </summary>
        public const float BreathRestedShare = 0.22f;

        /// <summary>
        /// Seconds for the breath to follow the bar.
        /// <para>
        /// Sized against §06's own recovery, not chosen: the bar refills in
        /// <c>SprintStaminaRecoverySeconds</c> and §06 makes that window the
        /// Runner's vulnerable one. A breath that dropped the instant the bar
        /// started refilling would say "you are safe" at the exact moment §06 says
        /// you are not, so it lags by a tenth of the recovery and therefore is still
        /// audible in the body when the bar looks healthy.
        /// </para>
        /// </summary>
        public static float BreathFollowSeconds
        {
            get { return GameConstants.SprintStaminaRecoverySeconds * 0.1f; }
        }

        // ====================================================================
        // §06's acquisition, and the blunt dread that outlives it.
        // ====================================================================

        /// <summary>
        /// Length of the flinch when §06's brain enters 추격, seconds.
        /// <para>
        /// Derived from the same 3 s sight break <c>MonsterAcquireTell</c> derives
        /// its flare from, and deliberately shorter than it: the creature announces
        /// for most of the interval, the player's body reacts for the first quarter
        /// of it. Anything longer is still running when the player reaches the first
        /// §12 corner and is trying to judge whether they lost it, and a camera that
        /// is still shaking at that moment is a camera lying about the answer.
        /// </para>
        /// </summary>
        public static float FlinchSeconds
        {
            get { return GameConstants.AggroReleaseLineOfSightBreak * 0.25f; }
        }

        /// <summary>Peak downward pitch of the flinch, degrees.</summary>
        public const float FlinchPitchDegrees = 2.2f;

        /// <summary>Peak vertical drop of the flinch, metres.</summary>
        public const float FlinchDipMetres = 0.05f;

        /// <summary>
        /// Peak tremble at full dread, metres.
        /// <para>
        /// <b>This is the one component that could leak information, and it is sized
        /// so it cannot.</b> It is driven by the same 0–1 <c>DangerSense.Danger01</c>
        /// the heartbeat uses, pushed in as a float, and that type's remarks are the
        /// authority for why a blunt proximity signal is allowed at all: §04 sells
        /// the monster's position to the 청음사 and §11 makes the 관측자's read
        /// unbuyable, so anything sharp enough to navigate by would hand two roles
        /// away for free. A non-directional millimetre of tremble adds no bearing and
        /// no distance the heartbeat has not already given.
        /// </para>
        /// </summary>
        public const float DreadTrembleMetres = 0.0045f;

        /// <summary>Tremble frequency at full dread, Hz. Fast and small, so it reads as a hand rather than as a wobble.</summary>
        public const float DreadTrembleHz = 9.5f;

        // ====================================================================
        // Bounds and the comfort scale.
        // ====================================================================

        /// <summary>
        /// Hard ceiling on the total translation the camera may apply, metres.
        /// <para>
        /// A backstop, not a tuning value. Every component above is bounded by
        /// construction, but they add, and the sum of a landing on the beat of a
        /// sprinting stride while flinching is the frame nobody previews. The clamp
        /// makes the worst case a stated number instead of an emergent one, and
        /// <see cref="PlayerViewMotion"/> is tested against it.
        /// </para>
        /// </summary>
        public const float MaxTranslationMetres = 0.16f;

        /// <summary>Hard ceiling on the total rotation the camera may apply, degrees. Same argument.</summary>
        public const float MaxRotationDegrees = 6f;

        /// <summary>
        /// Camera motion is a comfort preference and this is its default: all of it.
        /// <para>
        /// §05 already concedes the point for field of view — "고정하면 멀미 민원이
        /// 발생하고" — and the same is true of a moving camera, more so. A player who
        /// cannot look at the screen refunds the game, so the scale goes to zero and
        /// zero is a supported way to play: nothing in this file is load-bearing for
        /// any rule, which is precisely what makes turning it off safe.
        /// </para>
        /// </summary>
        public const float ScaleDefault = 1f;
    }
}
