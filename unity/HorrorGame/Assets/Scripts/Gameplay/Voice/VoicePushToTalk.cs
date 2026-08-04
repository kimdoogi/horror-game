#nullable enable

using HorrorGame.Core.Voice;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// Turns three keys into §01's three efforts and hands them to
    /// <see cref="VoiceCapture"/>.
    /// <para>
    /// <b>Three keys and not one key with a modifier.</b> A modifier means the player has
    /// to think about which hand is where before they can say "괴물" — and the whole
    /// design is that whisper, talk and shout are a decision made under pressure, with the
    /// creature audible. Three separate keys make the decision a reflex, which is what it
    /// has to be. They sit on one row so the choice is spatial: quieter on the left,
    /// louder on the right.
    /// </para>
    /// <para>
    /// <b>Louder wins when two are held.</b> A player who mashes both is asking for the
    /// louder one — releasing into silence mid-word would lose the message, and this game
    /// charges for volume rather than forbidding it. So the resolution is deterministic
    /// and always upward.
    /// </para>
    /// <para>
    /// Reads <c>Keyboard.current</c> directly, the way <c>PlayerInteractor</c>,
    /// <c>ThirdPersonCamera</c> and <c>GameShell</c> already do — there is no input-action
    /// asset in this project to bind against, and inventing one for voice alone would put
    /// §05's controls in two places.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VoiceCapture))]
    [AddComponentMenu("HorrorGame/Voice/Voice Push To Talk")]
    public sealed class VoicePushToTalk : MonoBehaviour
    {
        /// <summary>속삭임. 4 m, and the creature does not hear it — the one safe way to speak.</summary>
        public const Key WhisperKey = Key.C;

        /// <summary>보통. A corridor, and audible through a wall.</summary>
        public const Key TalkKey = Key.V;

        /// <summary>외침. Crosses a band, and brings the creature from half again as far.</summary>
        public const Key ShoutKey = Key.B;

        private VoiceCapture? _capture;

        /// <summary>What the keys resolved to on the last frame. Read by the debug overlay.</summary>
        public VoiceEffort Effort { get; private set; } = VoiceEffort.Silent;

        private void Awake()
        {
            _capture = GetComponent<VoiceCapture>();
        }

        private void OnDisable()
        {
            // A key cannot stay held across a disable. Leaving the effort set would leave
            // §06 being told this runner is shouting, for ever.
            Effort = VoiceEffort.Silent;
            _capture?.SetEffort(VoiceEffort.Silent);
        }

        private void Update()
        {
            var capture = _capture;
            if (capture == null)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                // No keyboard at all — a headless host, a gamepad-only machine, or a
                // fixture. Say nothing rather than forcing Silent: a machine with no keys
                // has no opinion about the effort, and overwriting one that something else
                // set would silently mute anything driving VoiceCapture directly.
                return;
            }

            Effort = Read(keyboard);
            capture.SetEffort(Effort);
        }

        private static VoiceEffort Read(Keyboard keyboard)
        {
            if (IsHeld(keyboard, ShoutKey))
            {
                return VoiceEffort.Shout;
            }

            if (IsHeld(keyboard, TalkKey))
            {
                return VoiceEffort.Talk;
            }

            return IsHeld(keyboard, WhisperKey) ? VoiceEffort.Whisper : VoiceEffort.Silent;
        }

        private static bool IsHeld(Keyboard keyboard, Key key)
        {
            var control = keyboard[key];
            return control != null && control.isPressed;
        }
    }
}
