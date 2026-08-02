#nullable enable

using HorrorGame.Gameplay.Player;
using UnityEngine;

namespace HorrorGame.Net.PlayerBridge
{
    /// <summary>
    /// §05's five rows, read off the rig a person is actually driving. The first
    /// implementation of <see cref="INetPlayerViewSource"/> in shipped code.
    /// <para>
    /// <b>Why this class exists.</b> The interface said "Implemented by the first-person
    /// controller in Assets/Scripts/Gameplay/" and nothing was. Every fixture that proved
    /// a runner replicates handed <see cref="NetPlayer"/> a scripted view of its own, so
    /// the wire was measured end to end with no human anywhere near it, and the sentence
    /// "a second runner replicates BOTH ways" was true of a test and false of a game.
    /// </para>
    /// <para>
    /// <b>It is a reader, not a controller.</b> Nothing here decides anything: every
    /// property is a field of the rig the local player already drives — the same
    /// <see cref="PlayerMotor"/> §05's speed table runs on, the same
    /// <see cref="PlayerLook"/> the camera turns with, the same
    /// <see cref="PlayerFlashlight"/> §10's battery drains. That is the point. The
    /// networked runner must be the <em>same</em> runner; a bridge that recomputed
    /// anything would be a second character that agreed with the first until it did not.
    /// </para>
    /// <para>
    /// <b>Which assembly, and why not the obvious one.</b> ARCHITECTURE §3 says the Net
    /// layer must not import the movement controller and the controller must not import
    /// Mirror. Both halves of that survive here: this file lives in
    /// <c>HorrorGame.Net.PlayerBridge</c>, a satellite assembly that references both and
    /// that neither references back — the same arrangement
    /// <c>HorrorGame.Net.SteamTransport</c> uses to keep Steamworks.NET out of
    /// <c>HorrorGame.Net</c>. Nothing in <c>HorrorGame.Gameplay.Player</c> changes, and
    /// <c>HorrorGame.Net</c> still compiles with no knowledge that a
    /// <see cref="PlayerMotor"/> exists.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Net/Player Rig Net View")]
    public sealed class PlayerRigNetView : MonoBehaviour, INetPlayerViewSource
    {
        private PlayerMotor? _motor;
        private PlayerLook? _look;
        private PlayerFlashlight? _flashlight;
        private PlayerLoadout? _loadout;

        /// <summary>
        /// §05's 위치 row: the rig root's world position, which is the character
        /// controller's feet.
        /// <para>
        /// The root and not the camera. §05 syncs a body and a view separately —
        /// <see cref="NetPlayer.ApplyRemoteView"/> turns the body with yaw and the head
        /// anchor with pitch — so sending the eye's position would put a remote runner's
        /// feet 1.63 m in the air the moment they looked down.
        /// </para>
        /// </summary>
        public Vector3 Position
        {
            get { return transform.position; }
        }

        /// <summary>
        /// §05's yaw. Clockwise from +Z, the convention <c>SpeedResolver.ResolveVelocity</c>
        /// resolves movement against, so the direction a teammate is seen running and the
        /// direction they are actually running are one number.
        /// </summary>
        public float YawDegrees
        {
            get { return _look != null ? _look.YawDegrees : transform.eulerAngles.y; }
        }

        /// <summary>
        /// §05's pitch, <b>negated</b>, and that sign is the whole reason this property is
        /// not a one-liner.
        /// <para>
        /// The two layers use opposite conventions and both of them say so out loud.
        /// <c>PlayerLook.PitchDegrees</c> is documented "positive looking down (Unity's
        /// sign)"; <see cref="INetPlayerViewSource.PitchDegrees"/> is documented "negative
        /// looking down", and <see cref="NetPlayer.ApplyRemoteView"/> agrees with the
        /// interface — it builds the head anchor's rotation as
        /// <c>Quaternion.Euler(-PitchDegrees, 0, 0)</c>, so a remote head only tips
        /// downward when the value on the wire is negative.
        /// </para>
        /// <para>
        /// Passed through unchanged, every player sweeping the floor would be seen
        /// sweeping the ceiling. §05 asks for pitch precisely because those are two
        /// different sentences — "바닥·천장을 비추는 것도 신호" — so the failure would not
        /// have been cosmetic, it would have inverted the message. Converting here rather
        /// than changing either convention keeps the sign local to the one file that has
        /// to know both.
        /// </para>
        /// </summary>
        public float PitchDegrees
        {
            get { return _look != null ? -_look.PitchDegrees : 0f; }
        }

        /// <summary>
        /// §05's 손전등 row. <see cref="PlayerFlashlight.IsLit"/> and not "the player
        /// pressed F": §03 takes the torch out of the hand when the objective is lifted
        /// and §10 runs the battery down, and both of those turn the beam off without any
        /// input. The monster's notice distance (§06) is computed from this flag on the
        /// host, so it has to be the light's truth rather than the key's.
        /// </summary>
        public bool FlashlightOn
        {
            get { return _flashlight != null && _flashlight.IsLit; }
        }

        /// <summary>
        /// §05's 운반 상태 row, in the order <c>NetCarryStates.Of</c> uses: the objective
        /// beats an oversize piece because §03 forbids holding both, and loot is only
        /// reported when there is some.
        /// </summary>
        public NetCarryState Carry
        {
            get
            {
                var loadout = _loadout;
                if (loadout == null)
                {
                    return NetCarryState.Empty;
                }

                if (loadout.CarryingObjective)
                {
                    return NetCarryState.Objective;
                }

                if (loadout.CarryingOversizePiece)
                {
                    return NetCarryState.OversizePiece;
                }

                return loadout.Inventory.LootCount > 0 ? NetCarryState.Loot : NetCarryState.Empty;
            }
        }

        /// <summary>
        /// §05's 스태미나 row, exact. <see cref="NetPlayer"/> is what quantises it for
        /// everybody else ("본인만 정확히, 남은 대략") — sending an approximation from here
        /// would take the owner's own exact bar away as well.
        /// <para>
        /// A rig with no motor answers 1: an avatar nobody is driving is not tired, and 0
        /// would draw every idle teammate's bar as empty.
        /// </para>
        /// </summary>
        public float StaminaFraction
        {
            get { return _motor != null ? _motor.Stamina.Fraction : 1f; }
        }

        /// <summary>
        /// Puts this reader on a rig, adding it if the rig has not got one.
        /// <para>
        /// Idempotent, and the only way this component is ever created. The rig is
        /// assembled by <c>PlayerFeelHarnessMenu.BuildRig</c> and <em>saved into a
        /// scene</em>, so a scene written before this class existed is a player who cannot
        /// be seen by anybody — which is precisely the defect that hid a missing
        /// <c>PlayerStance</c> for a day and is why <c>PlayerMotor.Awake</c> adds one
        /// rather than trusting the scene. Same trap, same answer.
        /// </para>
        /// </summary>
        /// <param name="rig">The rig root — the object carrying <see cref="PlayerMotor"/>.</param>
        /// <returns>The reader on that rig.</returns>
        public static PlayerRigNetView Attach(PlayerMotor rig)
        {
            var view = rig.GetComponent<PlayerRigNetView>();
            if (view == null)
            {
                view = rig.gameObject.AddComponent<PlayerRigNetView>();
            }

            view.Resolve();
            return view;
        }

        private void Awake()
        {
            Resolve();
        }

        /// <summary>
        /// Finds the four components this reads.
        /// <para>
        /// <c>GetComponentInChildren</c> for everything except the motor, because that is
        /// how <c>MatchDirector.ResolveWiring</c> finds the same four and a bridge that
        /// searched differently would be reading a different rig on some layout nobody has
        /// built yet. Called from <c>Awake</c> and again from <see cref="Attach"/>, so a
        /// component added to a live object at run time is wired before its first frame.
        /// </para>
        /// </summary>
        private void Resolve()
        {
            if (_motor == null)
            {
                _motor = GetComponent<PlayerMotor>();
            }

            if (_look == null)
            {
                _look = GetComponentInChildren<PlayerLook>();
            }

            if (_flashlight == null)
            {
                _flashlight = GetComponentInChildren<PlayerFlashlight>();
            }

            if (_loadout == null)
            {
                _loadout = GetComponentInChildren<PlayerLoadout>();
            }
        }
    }
}
