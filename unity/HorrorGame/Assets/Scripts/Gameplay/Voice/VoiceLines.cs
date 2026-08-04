#nullable enable

using HorrorGame.Steam;
using UnityEngine;

namespace HorrorGame.Gameplay.Voice
{
    /// <summary>
    /// Picks the microphone and codec this session runs on. Steam when it is really
    /// there, Unity's microphone otherwise.
    /// <para>
    /// <b>The rule is copied, not invented.</b> <c>HorrorGameNetworkManager.AttachTransport</c>
    /// chooses the wire with exactly this test — <c>SteamServices.Current.IsOnline</c> plus
    /// a live platform component, KCP otherwise — and voice asks the same question so that
    /// the transport and the codec can never disagree about which world the session is in.
    /// A second rule here would be a way for the game to end up sending Steam-compressed
    /// frames down a KCP socket to a machine with no Steam, which is a bug that only shows
    /// up on somebody else's computer.
    /// </para>
    /// <para>
    /// <b>Availability is asked as well as onlineness.</b> §14 step 3 plays with
    /// 음성은 디스코드, so an offline build genuinely has no codec: <c>SteamServices</c>
    /// falls back to <c>NullSteamService</c>, whose <c>Voice</c> is a
    /// <c>SilentVoiceBackend</c> reporting <c>IsAvailable == false</c>. Choosing it anyway
    /// would produce a voice system that starts, reports healthy and never carries a
    /// sample — the exact failure this project keeps writing down. So a Steam backend that
    /// says it has no codec loses to the microphone, which at least works.
    /// </para>
    /// </summary>
    public static class VoiceLines
    {
        /// <summary>
        /// Test seam: when set, <see cref="Choose"/> returns this instead of choosing.
        /// <para>
        /// Deliberately a plain static rather than something a scene can serialise. It
        /// exists so a fixture can put an <see cref="InjectedVoiceLine"/> where a driver
        /// would be, and anything that leaves it set outside a fixture has muted the game.
        /// <see cref="ResetForTests"/> clears it.
        /// </para>
        /// </summary>
        public static IVoiceLine? Override { get; set; }

        /// <summary>
        /// The line this machine should use. Never null: a machine with no microphone and
        /// no codec still gets a <see cref="MicrophoneVoiceLine"/> that reports
        /// <see cref="IVoiceLine.IsAvailable"/> false, so no caller needs a null branch —
        /// the same promise <c>SteamServices.Current</c> makes for the platform.
        /// </summary>
        public static IVoiceLine Choose()
        {
            var chosen = Override;
            if (chosen != null)
            {
                return chosen;
            }

            var steam = SteamServices.Current;
            if (steam.IsOnline && steam.Voice.IsAvailable)
            {
                return new SteamVoiceLine(steam.Voice);
            }

            return new MicrophoneVoiceLine();
        }

        /// <summary>Says which line was chosen and why, once, at session start.</summary>
        /// <param name="line">The chosen line.</param>
        public static void Report(IVoiceLine line)
        {
            if (line == null)
            {
                return;
            }

            if (line.IsAvailable)
            {
                Debug.Log("[Voice] " + line.Name + " line at " + line.SampleRate + " Hz, codec "
                          + line.Codec + ". 근접 음성 is live: whisper "
                          + HorrorGame.Core.Voice.VoiceRules.WhisperRange + " m, talk "
                          + HorrorGame.Core.Voice.VoiceRules.TalkRange + " m, shout "
                          + HorrorGame.Core.Voice.VoiceRules.ShoutRange + " m.");
                return;
            }

            // Not an error. A headless host and an offline build both land here legally,
            // and both of them can still HEAR — only the local microphone is missing.
            Debug.Log("[Voice] " + line.Name + " line reports no capture on this machine ("
                      + SteamServices.Current.BackendName + "). This runner is mute; everybody else is "
                      + "still audible.");
        }

        /// <summary>Clears <see cref="Override"/>. Tests only.</summary>
        public static void ResetForTests()
        {
            Override = null;
        }
    }
}
