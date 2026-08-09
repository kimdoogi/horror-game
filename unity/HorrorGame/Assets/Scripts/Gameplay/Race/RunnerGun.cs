#nullable enable

using System;
using HorrorGame.Core.Map;
using HorrorGame.Core.Race;
using HorrorGame.Core.Voice;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Player;
using HorrorGame.Net;
using HorrorGame.UI;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// The gun a runner is holding: one shot, the arm it hangs off, and the moment it is
    /// spent.
    /// <para>
    /// <b>It decides nothing.</b> <c>Core.Race.Gunplay</c> is the rule — one shot, twelve
    /// metres, a hit costs exactly what §06's creature costs — and this component measures
    /// the world and calls it. That split is the whole of §13 as it applies here: "was
    /// that a legal shot" is the single most attractive thing in a race to lie about, so
    /// the answer is computed by an engine-free type against <c>RaceState</c> and never by
    /// a raycast.
    /// </para>
    /// <para>
    /// <b>What this file is responsible for, and it is four things.</b> Turning a crosshair
    /// and a key into a gun in a hand; putting <c>Gun_Held</c> where another runner can see
    /// it; turning a left click into a seat, a distance and a call to
    /// <see cref="Gunplay.Fire"/>; and making the report LOUD — a gunshot is the loudest
    /// thing in this building and §06's creature has exactly one way out of 순찰, which is
    /// hearing something.
    /// </para>
    /// <para>
    /// <b>The shot is spent whether it hits or misses, and that is the rule not an
    /// oversight.</b> <c>Gunplay</c>'s own argument is that one shot is "a decision you make
    /// once and then have to live with"; a miss that cost nothing would make the decision
    /// free and turn the corridor into a place people fire down speculatively. So the count
    /// comes off before the judgement is asked for.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunnerGun : MonoBehaviour
    {
        /// <summary>
        /// How far along <see cref="ArmBone"/> <c>Gun_Held</c> hangs, measured as a multiple
        /// of the <c>Spine</c>→<c>Head</c> chain's length. 0.4376 on 주자.
        /// <para>
        /// <b>Why a ratio and not metres.</b> The rig's <c>RightLowerArm</c> is a leaf bone,
        /// <c>gen_runner.export_fbx</c> sets <c>add_leaf_bones=False</c> so no tip node is
        /// invented, and a bone's LENGTH lives in a tail that FBX does not write as a node.
        /// Unity therefore does not know where this rig's hand is, and cannot be made to.
        /// The number has to be authored, and the only unit-safe way to carry it is as a
        /// proportion of something the runtime can measure on the rig it actually loaded.
        /// Metres would be a bet on <c>FBX_SCALE_NONE</c>, <c>useFileScale</c> and the model
        /// importer's global scale all staying where they are.
        /// </para>
        /// <para>
        /// <b>Both ends of that ratio moved when 주자 grew from 13 bones to 17, and this
        /// value moved with them — it is NOT the 0.9904 that stood here.</b> The numerator
        /// was the whole arm hung off <c>RightUpperArm</c>; that bone now stops at the
        /// ELBOW, so the same offset on the same bone would have put the revolver in the
        /// crook of the runner's arm — silently, in the third-person view nobody is looking
        /// at while they play. The gun rides the FOREARM now and the number is the forearm's
        /// own length. The denominator was <c>Head.localPosition</c>, which worked only
        /// because <c>Head</c> was a direct child of <c>Spine</c>; a <c>Chest</c> and a
        /// <c>Neck</c> sit between them today, so <see cref="MountOffset"/> walks the chain
        /// and sums it instead. Same physical length as before plus the neck, and
        /// <c>gen_runner.report_arm</c> measures exactly that.
        /// </para>
        /// <para>
        /// <b>It is checked from the other end.</b> <c>gen_runner.verify_gun_mount</c> reads
        /// this file, greps this constant and fails the export if the skeleton it just built
        /// disagrees by more than 0.005. A constant copied into another language is a seam,
        /// and this repository's standing lesson is that seams go on matching after the
        /// thing they named has moved — so the generator is made to look.
        /// </para>
        /// </summary>
        public const float GunMountArmsPerSpine = 0.4376f;

        /// <summary>
        /// Bone the gun hangs off: the FOREARM, whose tail is the palm. 주자 still has no
        /// hand bone — 17 bones, two per arm, and the glove is a rigid shell weighted to
        /// this one. Mounting on <c>RightUpperArm</c> would hang it at the elbow.
        /// </summary>
        public const string ArmBone = "RightLowerArm";

        /// <summary>The two bones whose separation gives the rig's own unit of length.</summary>
        public const string SpineBone = "Spine";

        /// <summary>
        /// Descendant of <see cref="SpineBone"/>, no longer its direct child — the chain
        /// between them is the rig's unit of length. See <see cref="GunMountArmsPerSpine"/>.
        /// </summary>
        public const string HeadBone = "Head";

        /// <summary>
        /// Name of the disabled <c>Gun_Held</c> the generator leaves in the scene for this
        /// component to clone. Mirrors <c>MapSceneBuilder.HeldTemplateName</c>.
        /// <para>
        /// A scene template rather than <c>Resources.Load</c>, and the reason is ownership
        /// of the asset reference. <c>Resources/</c> is a build-wide folder whose contents
        /// ship whether anything uses them or not; a template the map generator instantiates
        /// puts the reference in the generated scene, where it is a serialised dependency
        /// Unity can see, strip and report on. It also means a map with no guns carries no
        /// gun asset at all.
        /// </para>
        /// </summary>
        public const string HeldTemplateName = "Gun_Held_Template";

        /// <summary>
        /// The local runner's gun, or null before anybody has picked one up.
        /// <para>
        /// Single-player-shaped, exactly like <c>PlayerInteractor.Active</c> and for the
        /// same reason: each machine drives one runner, and everything shipped reads its
        /// own reference. It exists so <see cref="GunPickup"/> can find the hands that
        /// pressed the key without every pickup holding a back-reference to a rig.
        /// </para>
        /// </summary>
        public static RunnerGun? Local { get; private set; }

        /// <summary>
        /// Turns a thing the crosshair hit into a seat in the race, or −1.
        /// <para>
        /// A hook rather than a hard-coded lookup, because "which runner is that" is the
        /// networking layer's question and this file must not answer it twice. The default
        /// reads a <see cref="RunnerGun"/> off the hit, which is right for a rig that has
        /// one and for every test; <c>NetPlayer</c> is what knows the answer for a
        /// replicated avatar, and installing it here is a one-line change in a file this
        /// one does not own.
        /// </para>
        /// </summary>
        public static Func<Transform, int>? SeatOf { get; set; }

        /// <summary>
        /// Raised on the machine that fired, after the shot has been spent, with the seat
        /// aimed at (−1 for a miss) and the plan distance to it.
        /// <para>
        /// This is the seam §13 needs and the one thing this file deliberately leaves
        /// unfinished: on a pure client the judgement is not ours to make, so the shot is
        /// spent, the noise is raised, and this event is where a <c>Command</c> goes.
        /// </para>
        /// </summary>
        public event Action<int, int, float>? Fired;

        [Header("Wiring")]
        [SerializeField]
        [Tooltip("The match this runner is in. Left empty, the one in the scene is found.")]
        private MatchDirector? _director;

        [SerializeField]
        [Tooltip("The standings a hit is reported to. Left empty, the match's own.")]
        private RaceDirector? _race;

        [SerializeField]
        [Tooltip("Eye the shot is cast from. Left empty, the first camera in children.")]
        private Transform? _eye;

        [SerializeField]
        [Tooltip("Rig root the arm bone is searched under. Left empty, this transform.")]
        private Transform? _rigRoot;

        [SerializeField]
        [Tooltip("Seat this runner holds in the race. §13's host is the authority; this is the local reading.")]
        private int _seat;

        private int _shotsLeft;
        private bool _hasGun;
        private GameObject? _held;
        private GunReadout? _readout;

        /// <summary>Shots left in the gun this runner is holding. 0 is no gun, or a spent one.</summary>
        public int ShotsLeft
        {
            get { return _shotsLeft; }
        }

        /// <summary>
        /// Whether this runner is carrying a gun at all — a spent gun is still a gun.
        /// <para>
        /// <b>The GUN, not the prop.</b> This used to read <c>_held != null</c>, which is
        /// the model hanging off the arm, and that made a rig <see cref="Mount"/> could not
        /// find a <c>RightUpperArm</c> on report "not armed" while holding a live shot.
        /// Three things read this and all three were wrong in that state:
        /// <see cref="TryTake"/> would hand the runner a SECOND gun, the readout would say
        /// 총 없음 over a loaded one, and §12's 문 shape — the runner ahead of you took this
        /// floor's gun — would be told to nobody. The prop is
        /// <see cref="Held"/>; whether the prop mounted is a presentation question and this
        /// is not.
        /// </para>
        /// </summary>
        public bool Armed
        {
            get { return _hasGun; }
        }

        /// <summary>Seat in the race. Set by whatever placed this runner; −1 means no seat.</summary>
        public int Seat
        {
            get { return _seat; }
            set { _seat = value; }
        }

        /// <summary>The clone of <c>Gun_Held</c> hanging off the arm, or null.</summary>
        public GameObject? Held
        {
            get { return _held; }
        }

        /// <summary>
        /// The standings a hit is reported to — the bound one, or the match's.
        /// <para>
        /// Bindable separately from <see cref="MatchDirector"/> because the RACE is what
        /// this component actually needs and the match is only where it usually finds one.
        /// A test that had to stand a whole <c>MatchDirector</c> up to prove that a shot
        /// sends somebody back to B1 would be testing the match, and the thing being proved
        /// would be buried under everything a match does on Awake.
        /// </para>
        /// </summary>
        private RaceState? Rules
        {
            get
            {
                var race = _race != null ? _race : (_director != null ? _director.Race : null);
                return race != null ? race.Rules : null;
            }
        }

        /// <summary>Points the gun at a race. Used by the scene wiring and by tests.</summary>
        /// <param name="race">The standings.</param>
        /// <param name="seat">This runner's seat in them.</param>
        public void Bind(RaceDirector race, int seat)
        {
            _race = race;
            _seat = seat;
        }

        /// <summary>
        /// How far a gunshot carries to §06's ear, metres, and how it is derived.
        /// <para>
        /// The loudest range <c>VoiceRules</c> will give any effort on any §12 surface —
        /// a shout on 금속 — asked of the rule that owns it rather than restated. That
        /// makes the gunshot the ceiling of everything a runner can do to be heard, and it
        /// moves when §04's voice table moves instead of drifting away from it.
        /// </para>
        /// <para>
        /// <b>Unattenuated, and that is the design.</b> A footstep is
        /// <c>MonsterFootstepHearingRange × clarity × effort</c> and a voice is scaled by
        /// the same §12 clarity, because both are sounds a FLOOR carries. A gunshot is not
        /// a floor sound — it is an explosion in a concrete corridor — so it reaches this
        /// far on 카펫 (clarity 0.22) exactly as on 금속, which is what makes it the loudest
        /// thing in the building on six of §12's eight surfaces rather than only on the two
        /// loud ones.
        /// </para>
        /// </summary>
        public static readonly float GunshotRangeMetres = LoudestNoiseRange();

        /// <summary>
        /// How loud the report is on <c>MonsterBrain</c>'s 0~1 scale. The top of it.
        /// <para>
        /// The brain takes the LOUDEST cue in a tick, so this is what decides whether the
        /// creature turns toward the shot or toward whatever else it heard. Nothing else
        /// reaches 1: <c>VoiceRules.SelfNoise</c> tops out at 0.95 for a shout, and a
        /// footstep's loudness is the runner's effort, which only reaches 1 at a sprint.
        /// A shot ties a sprint and beats everything else, which is the correct ordering —
        /// somebody firing is at least as interesting as somebody running.
        /// </para>
        /// </summary>
        public const float GunshotLoudness = 1f;

        /// <summary>
        /// Puts a gun in this runner's hands, if they have none.
        /// <para>
        /// One at a time, and a spent gun still counts. Picking up a second would be a
        /// reload with extra steps, and <c>Gunplay.ShotsPerGun</c>'s whole argument is that
        /// twenty players with repeating fire turns a maze into a shooting gallery.
        /// </para>
        /// </summary>
        /// <param name="pickup">The gun on the floor. Null is refused rather than thrown on.</param>
        /// <returns>True when this call is the one that got it.</returns>
        public bool TryTake(GunPickup? pickup)
        {
            if (pickup == null || Armed || !pickup.Take())
            {
                return false;
            }

            // Before Mount, so the fact is true even if the model cannot be hung on this
            // rig — Mount is loud about that separately and does not get to decide whether
            // the runner is holding a gun.
            _hasGun = true;
            _shotsLeft = Gunplay.ShotsPerGun;
            Mount();
            Draw();

            Debug.Log("[Gun] 총을 주웠다 — " + _shotsLeft + " 발, 사거리 "
                + Gunplay.RangeMetres.ToString("0") + " m.", this);
            return true;
        }

        /// <summary>
        /// Fires. Spends the shot, raises the report as a §06 noise, and — on the machine
        /// §13 lets judge — asks <see cref="Gunplay.Fire"/> what it did.
        /// <para>
        /// <b>The order is the rule.</b> The count comes off first, then the noise, then
        /// the judgement. A judgement that ran first could refuse and leave the shot
        /// unspent, which is a gun that only costs you something when it works.
        /// </para>
        /// <para>
        /// <b>The target comes from the camera, the range does not.</b> The raycast decides
        /// WHO — a crosshair on a body, through <see cref="SeatOf"/> — and
        /// <c>Gunplay.Judge</c> decides whether they are close enough, in the plan, without
        /// the vertical. Letting the ray's own length stand in for range would make a shot
        /// down a stairwell legal on a technicality, and letting the ray decide range at all
        /// would put a rule on the client.
        /// </para>
        /// </summary>
        /// <returns>What the shot did. <see cref="ShotRefusal.NoGun"/> when there was none.</returns>
        public ShotRefusal Fire()
        {
            if (_shotsLeft <= 0)
            {
                return ShotRefusal.NoGun;
            }

            _shotsLeft = 0;
            Draw();

            var target = Aim(out var hitPoint, out var metresApart);
            RaiseGunshot();

            // The report goes out before the judgement, and on every machine, because the
            // noise is a fact about the world rather than an outcome: the creature heard a
            // shot whether or not the host decides it landed.
            Fired?.Invoke(_seat, target, metresApart);

            if (!ThisMachineJudges)
            {
                // §13. A client that resolved its own shot would be the client deciding
                // another player's descent, which is the one thing the authority model
                // exists to prevent. The gun is gone either way; the CONSEQUENCE waits for
                // the host.
                //
                // The seat travels and the distance does not: the host measures the range
                // itself between the two bodies it tracks (RaceDirector.AcceptShot). Left
                // as a bare Debug.Log — which is what this was — a client's shot reached
                // nobody, and §01's 총 was a sound effect that spent itself.
                NetRace.SendShot(target);
                Debug.Log("[Gun] 쐈다 — 판정은 호스트가 한다 (대상 " + target + ").", this);
                return ShotRefusal.None;
            }

            var rules = Rules;
            if (rules == null || target < 0)
            {
                Debug.Log("[Gun] 빗나갔다 — " + hitPoint.ToString("0.0") + " (한 발 소모).", this);
                return ShotRefusal.NotASeat;
            }

            // ShotsPerGun rather than _shotsLeft, which is already 0: the judgement is
            // about the shot that was just fired, and handing over the count AFTER it was
            // spent would make Gunplay refuse every shot with NoGun.
            var elapsed = _director != null ? _director.Clock.ElapsedSeconds : 0f;
            var outcome = Gunplay.Fire(rules, _seat, target, Gunplay.ShotsPerGun, metresApart, elapsed);
            Debug.Log(outcome == ShotRefusal.None
                ? "[Gun] 맞혔다 — 좌석 " + target + " 은 출발선으로. " + metresApart.ToString("0.0") + " m."
                : "[Gun] 안 맞았다 — " + outcome + ", " + metresApart.ToString("0.0") + " m.", this);
            return outcome;
        }

        /// <summary>
        /// Whether §13 lets this machine resolve a shot: it does unless we are a pure
        /// client.
        /// <para>
        /// The same distinction <c>NetSocketTests</c> is built on — a host is not a client
        /// even though <c>NetworkClient.active</c> is true for it. Offline (a solo playtest,
        /// a PlayMode test, the editor loop) there is no client at all and this machine is
        /// the only authority there is, which is the correct answer rather than a loophole:
        /// with nobody else in the race there is nobody for it to lie to.
        /// </para>
        /// </summary>
        public static bool ThisMachineJudges
        {
            get { return !NetworkClient.active || NetworkServer.active; }
        }

        /// <summary>
        /// What the crosshair is on, as a seat, and how far away it is in the plan.
        /// <para>
        /// Cast to <see cref="Gunplay.RangeMetres"/> rather than to infinity: past that the
        /// shot cannot land anyway, and a longer ray would let a bad <see cref="SeatOf"/>
        /// nominate somebody the rule is about to refuse — a refusal that reads as a bug
        /// rather than as a miss.
        /// </para>
        /// </summary>
        private int Aim(out Vector3 hitPoint, out float metresApart)
        {
            var eye = Eye;
            hitPoint = eye != null ? eye.position : transform.position;
            metresApart = float.PositiveInfinity;

            if (eye == null)
            {
                return -1;
            }

            // Triggers ignored. Every interactable volume in this game is a trigger and a
            // gun lying in an alcove carries one; a shot that stopped on a pickup box would
            // be a bullet absorbed by a thing you can walk through.
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, Gunplay.RangeMetres,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                hitPoint = eye.position + (eye.forward * Gunplay.RangeMetres);
                return -1;
            }

            hitPoint = hit.point;

            var resolve = SeatOf;
            var seat = resolve != null
                ? resolve(hit.transform)
                : DefaultSeatOf(hit.transform);
            if (seat < 0 || seat == _seat)
            {
                return -1;
            }

            var flat = hit.transform.position - transform.position;
            flat.y = 0f;
            metresApart = flat.magnitude;
            return seat;
        }

        /// <summary>
        /// A rig answers for itself. Anything else is scenery.
        /// <para>
        /// <c>NetPlayer</c> first, because in a real session the thing under the crosshair
        /// is another machine's replicated body — <c>NetRunnerBody</c> builds it from the
        /// spawn message and it carries no <see cref="RunnerGun"/> of its own, so asking
        /// only for a <c>RunnerGun</c> resolved every remote runner in the building to −1
        /// and the gun could only ever hit somebody in a solo playtest. <c>SeatIndex</c> is
        /// the host-assigned §11 seat, which is the same number §02's standings are keyed
        /// on; the <c>RunnerGun</c> fallback below is what answers offline, where nothing
        /// is spawned and there are no NetPlayers.
        /// </para>
        /// </summary>
        private static int DefaultSeatOf(Transform hit)
        {
            var net = hit.GetComponentInParent<NetPlayer>();
            if (net != null && net.SeatIndex >= 0)
            {
                return net.SeatIndex;
            }

            var runner = hit.GetComponentInParent<RunnerGun>();
            return runner != null ? runner.Seat : -1;
        }

        /// <summary>
        /// Tells §06 a shot was fired here.
        /// <para>
        /// Delivered to the creature on the shooter's own floor, which is the shape
        /// <c>MatchDirector.StepCreatures</c> already has for a footstep and a voice: a
        /// creature cannot leave its storey — §12-C makes the 투하구 one-way and the NavMesh
        /// audit reads <em>islands 8</em> — so a creature on another floor could do nothing
        /// with the report if it had it.
        /// </para>
        /// <para>
        /// It is raised from here rather than through the director because there is nothing
        /// for the director to add: a footstep and a voice have to be TAKEN from an
        /// accumulator that only the match owns, and a gunshot is a single event at a known
        /// point. The one thing that would justify moving it is a shot fired by a runner
        /// this machine is not driving, and on that day the report belongs in
        /// <c>MatchDirector</c> beside the other two cues — see the diff in this round's
        /// report.
        /// </para>
        /// </summary>
        private void RaiseGunshot()
        {
            var monster = _director != null ? _director.LocalStoreyMonster : null;
            LastGunshot = new Gunshot(transform.position, GunshotRangeMetres, GunshotLoudness, monster != null);

            if (monster == null)
            {
                return;
            }

            monster.ReportSound(transform.position, GunshotRangeMetres, GunshotLoudness);
            Debug.Log("[Gun] 총성 — " + GunshotRangeMetres.ToString("0")
                + " m 안의 §06 이 듣는다.", this);
        }

        /// <summary>
        /// The last report this gun raised, and whether anything was there to hear it.
        /// <para>
        /// Recorded because a noise is the one part of firing that leaves no trace. A hit
        /// changes the standings and a miss changes the shot count, both of which a test
        /// can read; a cue handed to <c>MonsterBrain</c> is consumed inside the tick it
        /// arrived in (<c>MonsterSoundCue</c> lives for exactly one) and there is no
        /// counter on the far side. Without this, "the creature was told" is a claim
        /// nothing in the project can check — and this repository's whole standing lesson
        /// is about claims like that.
        /// </para>
        /// </summary>
        public Gunshot LastGunshot { get; private set; }

        /// <summary>One report, as it was handed to §06.</summary>
        public readonly struct Gunshot
        {
            internal Gunshot(Vector3 at, float rangeMetres, float loudness, bool heard)
            {
                At = at;
                RangeMetres = rangeMetres;
                Loudness = loudness;
                Heard = heard;
            }

            /// <summary>Where the shot was fired.</summary>
            public Vector3 At { get; }

            /// <summary>How far it carried. See <see cref="GunshotRangeMetres"/>.</summary>
            public float RangeMetres { get; }

            /// <summary>0~1 on <c>MonsterBrain</c>'s scale.</summary>
            public float Loudness { get; }

            /// <summary>
            /// Whether a creature on the shooter's own storey was handed it. False is not
            /// necessarily wrong — a floor with no creature is a floor with nobody to hear
            /// you — but it is the difference between a silent gun and a quiet building.
            /// </summary>
            public bool Heard { get; }
        }

        /// <summary>
        /// The loudest range any §04 voice reaches on any §12 surface. Computed over the
        /// table rather than reading 금속 by name, so a re-tuned clarity moves it.
        /// </summary>
        private static float LoudestNoiseRange()
        {
            var loudest = 0f;
            foreach (FloorMaterial floor in Enum.GetValues(typeof(FloorMaterial)))
            {
                var range = VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Shout, floor);
                if (range > loudest)
                {
                    loudest = range;
                }
            }

            return loudest;
        }

        /// <summary>
        /// Clones <c>Gun_Held</c> onto the arm bone.
        /// <para>
        /// <b>This is the whole networked-visual half of the feature.</b> §01's twenty
        /// runners are identical by design — DESCENT-PIVOT §5, 「모델 하나. 20명이 똑같이
        /// 생긴다」 — so the ONE thing another runner can read off a body at
        /// <c>Gunplay.RangeMetres</c> is its outline, and an armed runner has to have a
        /// different one. The pose does most of that work (<c>gen_runner.clip_gun_walk</c>
        /// holds the right arm 55° forward, 2.11× clear of the torso); this puts the object
        /// at the end of it.
        /// </para>
        /// </summary>
        private void Mount()
        {
            if (_held != null)
            {
                return;
            }

            var root = _rigRoot != null ? _rigRoot : transform;
            var arm = PlayerRigBones.Find(root, ArmBone);
            var template = FindTemplate();
            if (arm == null || template == null)
            {
                // Loud, because the failure is silent otherwise: the gun still works, the
                // shot still lands, and the only thing missing is the one signal every
                // other runner in the maze is supposed to read off you.
                Debug.LogWarning("[Gun] Gun_Held not mounted — "
                    + (arm == null ? "no '" + ArmBone + "' bone under " + root.name : "no '"
                        + HeldTemplateName + "' in the scene")
                    + ". The runner is armed and nobody can see it.", this);
                return;
            }

            _held = Instantiate(template, arm, false);
            _held.name = "Gun_Held";
            _held.SetActive(true);
            _held.transform.localRotation = Quaternion.identity;
            _held.transform.localPosition = MountOffset(root);

            // Undo the bone's scale, or the gun ships at bone scale. The rig's bones
            // carry the FBX's x100 node scaling (the rig root's 0.01 cancels it for the
            // BODY, but a clone parented to a bone inherits the bone's lossy scale
            // alone), and a localScale-1 clone under RightUpperArm photographed as a
            // 27-metre revolver — GunShot measured bounds (8.0, 26.96, 29.44) on the
            // real BuildRig rig, the frame nobody had ever taken because every gun test
            // asserts firing, not appearance. Match the TEMPLATE's world size instead:
            // its lossy scale is the import-correct one the pickup path renders at.
            var boneScale = arm.lossyScale;
            var wantScale = template.transform.lossyScale;
            _held.transform.localScale = new Vector3(
                boneScale.x != 0f ? wantScale.x / boneScale.x : 1f,
                boneScale.y != 0f ? wantScale.y / boneScale.y : 1f,
                boneScale.z != 0f ? wantScale.z / boneScale.z : 1f);

            // The POSE, not just the prop. Without this the runner holds a gun with the
            // empty-handed Idle/Walk arms and the right hand swings through it — the
            // §12 문 reading ("the runner ahead of you took this floor's gun") is exactly
            // what the silhouette is supposed to carry, and a prop clipping through a
            // swinging arm reads as debris rather than as a weapon. This class writes the
            // fact and PlayerAnimatorDriver.Resolve picks the clip; it does not reach in
            // and set a clip, so there is still one owner of the pose→clip mapping.
            var driver = root.GetComponentInChildren<PlayerAnimatorDriver>();
            if (driver != null)
            {
                driver.Armed = true;
            }
            else
            {
                Debug.LogWarning("[Gun] Gun_Held mounted but no PlayerAnimatorDriver under "
                    + root.name + " — the prop is there and the arms will not hold it.", this);
            }
        }

        /// <summary>
        /// Where along the arm bone the hand is, in the rig's own units. See
        /// <see cref="GunMountArmsPerSpine"/> for why it is measured rather than typed.
        /// </summary>
        /// <summary>
        /// Where the held gun sits in the arm bone's local space. Public because the
        /// editor's <c>GunShot</c> harness photographs this exact mount, and it used to
        /// reproduce the arithmetic by hand — a seam that silently went stale the day the
        /// rig grew a Neck and a Chest (the copy read Head's localPosition, which had
        /// stopped being the Spine chain and become the neck alone: 0.126 m where the
        /// answer is 0.739 m, hanging the photographed gun at the elbow). The generator is
        /// already made to look at this file rather than restate it; the harness now does
        /// the same, so there is one implementation and the photograph is of the game.
        /// </summary>
        public static Vector3 MountOffset(Transform root)
        {
            var head = PlayerRigBones.Find(root, HeadBone);
            var spine = PlayerRigBones.Find(root, SpineBone);
            if (head == null || spine == null)
            {
                // The rig is not the one this constant was measured on. Zero puts the gun
                // at the elbow, which is visibly wrong rather than subtly wrong — the
                // failure a reviewer notices beats the failure they do not.
                return Vector3.zero;
            }

            // Sum the chain from Head back up to Spine. Each bone's localPosition is the
            // offset from its parent's origin — i.e. that parent's own length — so the
            // sum is Spine + Chest + Neck, in whatever units the importer settled on, and
            // it is the same unit the arm bone's local space uses (bones do not scale
            // relative to one another). This used to be one hop: Head WAS a child of Spine
            // on the 13-bone rig. Walking it means the number survives another bone being
            // inserted in the neck, and it still refuses a rig where Head is not under
            // Spine at all.
            var length = 0f;
            for (var t = head; t != null && t != root; t = t.parent)
            {
                length += t.localPosition.magnitude;
                if (t.parent == spine)
                {
                    // Blender exports with primary_bone_axis='Y', so a bone's local +Y
                    // runs from its head to its tail: along the forearm, toward the palm.
                    return new Vector3(0f, length * GunMountArmsPerSpine, 0f);
                }
            }

            return Vector3.zero;
        }

        private GameObject? FindTemplate()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == HeldTemplateName)
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        private Transform? Eye
        {
            get
            {
                if (_eye == null)
                {
                    var camera = GetComponentInChildren<Camera>();
                    _eye = camera != null ? camera.transform : transform;
                }

                return _eye;
            }
        }

        private void Awake()
        {
            Local = this;

            if (_director == null)
            {
                _director = FindFirstObjectByType<MatchDirector>();
            }

            if (_rigRoot == null)
            {
                _rigRoot = transform;
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Local, this))
            {
                Local = null;
            }
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && _shotsLeft > 0)
            {
                Fire();
            }
        }

        /// <summary>
        /// Draws the one fact the runner needs: whether they still have the shot.
        /// <para>
        /// <b>It belongs in <c>RaceHud</c> and it is here.</b> That screen owns §03's
        /// darkness budget — it accounts for its own lit area down to 770 px² of a
        /// 1920×1080 frame — and a second overlay is a second thing spending it. This one
        /// is a single glyph at <c>UiStyle.Ink</c> and nothing else, so the cost is on the
        /// order of a standings row; the honest home is still the HUD, and the diff for it
        /// is in this round's report. Written here because the HUD is not this task's file
        /// and a readout that ships beats a readout that is somebody else's to add.
        /// </para>
        /// </summary>
        private void Draw()
        {
            if (_readout == null)
            {
                _readout = gameObject.AddComponent<GunReadout>();
            }

            _readout.Show(_shotsLeft, Armed);
        }
    }

    /// <summary>
    /// One glyph saying whether the runner still has their shot.
    /// <para>
    /// §03 keeps the maze dark and argues that a bright overlay hands back for free what
    /// the ring structure charges for, so this follows <c>RaceHud</c>'s four rules exactly:
    /// no backing plate, <c>UiStyle.Ink</c> for the fact that is the player's own,
    /// <c>UiStyle.InkFaint</c> once it is spent, and saturated colour never. It sits low
    /// and centre, under the crosshair, because it is a fact about the hands rather than
    /// about the race — the left edge is §01's depth gauge and the right is the clock.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class GunReadout : UiScreen
    {
        /// <summary>Pixels above the bottom edge. One line-gap clear of it, like every other margin here.</summary>
        private const float BaselineY = UiStyle.ScreenMargin + UiStyle.LineGap;

        private Text? _line;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderHud; }
        }

        /// <inheritdoc />
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            _line = UiFactory.CreateText(
                "Shot", root, Font, string.Empty, UiStyle.TextSize, UiStyle.Ink, TextAnchor.LowerCenter);
            UiFactory.Place(
                (RectTransform)_line.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, BaselineY),
                new Vector2(220f, UiStyle.TextSize + 8f));
        }

        /// <summary>
        /// Puts the count up. Hidden entirely while the runner has no gun — an empty
        /// counter would be a permanent reminder of a thing most runners never find.
        /// </summary>
        /// <param name="shotsLeft">Shots in the gun. 0 is spent.</param>
        /// <param name="armed">Whether there is a gun at all.</param>
        internal void Show(int shotsLeft, bool armed)
        {
            SetVisible(armed);
            if (!armed || _line == null)
            {
                return;
            }

            // 한 발 / 빈 총 — the two states there are. The number is spelled rather than
            // printed as "1/1", because a fraction invites the reading "there will be
            // more" and Gunplay's whole argument is that there will not be.
            _line.text = shotsLeft > 0 ? "총  한 발" : "총  빈 총";
            _line.color = shotsLeft > 0 ? UiStyle.Ink : UiStyle.InkFaint;
        }
    }
}
