#nullable enable

using HorrorGame.Gameplay.Match;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorrorGame.Gameplay.Audio
{
    /// <summary>
    /// Puts the mix into any scene that runs a match, whether or not anyone wired it.
    /// <para>
    /// <b>Why this is not redundant with the editor wiring step.</b>
    /// <c>SoloPlaytest.BuildScene</c> discards <c>Map_FirstSketch_Solo.unity</c> and
    /// rebuilds it from the generated map on every invocation — and it is called by the
    /// menu item, by the batch verifier and by <c>SoloMatchLoopTests</c>. Any of those
    /// silently removes a hand-wired rig, and the symptom is a game that makes no
    /// sound and reports no error. That is the single most likely way this work gets
    /// undone, and it already happened once during development: an EditMode run of the
    /// match-loop test un-wired the scene between two audio test runs.
    /// </para>
    /// <para>
    /// So the scene reference is the convenience and this is the guarantee. Serialised
    /// wiring is still better — it is visible in the hierarchy and inspectable before
    /// Play — and when it is present this does nothing.
    /// </para>
    /// <para>
    /// The gate is a <see cref="MatchDirector"/>, not an <see cref="AudioListener"/>: a
    /// main menu has ears too, and installing a basement's worth of ambience beds into
    /// one would be worse than the problem being solved.
    /// </para>
    /// </summary>
    public static class MatchAudioBootstrap
    {
        /// <summary>Name of the object the mix is installed on. Matches the editor wiring step's.</summary>
        public const string HostName = "[Audio]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            EnsureRig();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureRig();
        }

        /// <summary>
        /// Adds the rig and the bridge when a match is present and a mix is not.
        /// Idempotent, and safe to call from anywhere.
        /// </summary>
        public static MatchAudioRig? EnsureRig()
        {
            var existing = Object.FindFirstObjectByType<MatchAudioRig>();
            if (existing != null)
            {
                return existing;
            }

            if (Object.FindFirstObjectByType<MatchDirector>() == null)
            {
                return null;
            }

            var host = new GameObject(HostName);
            var rig = host.AddComponent<MatchAudioRig>();
            host.AddComponent<MatchAudioBridge>();

            Debug.Log(
                "[MatchAudioBootstrap] " + SceneManager.GetActiveScene().name + " had a match and no "
                + "mix, so one was installed. Serialise it instead with "
                + "HorrorGame ▸ Audio ▸ Wire Audio Into Open Scene.", host);

            return rig;
        }
    }
}
