#nullable enable

using HorrorGame.Audio;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Audio
{
    /// <summary>
    /// Prints what the scene is making noise with, and why it might not be.
    /// <para>
    /// <b>The substitute for listening.</b> This project is verified from
    /// <c>-batchmode</c>, where a correctly mixed build and a completely silent one
    /// produce byte-identical output. §05 makes 3D audio the game and §04 gives one role
    /// nothing else, so "is anything playing" is a first-order question and there was no
    /// way to ask it. The counting is <see cref="AudioSceneCensus"/>'s — this is the menu
    /// item that shows it, and the PlayMode test asserts on the same numbers so the two
    /// can never disagree about what the scene sounds like.
    /// </para>
    /// <para>
    /// Most useful in Play mode: outside it, beds have not started and a rig has not
    /// built its sources, so the honest answer is "nothing yet" and the report says so.
    /// </para>
    /// </summary>
    public static class AudioSourceReport
    {
        /// <summary>Logs every live source, what is in it, its bus level, filter corner and occlusion.</summary>
        [MenuItem("HorrorGame/Audio/Report Live Audio Sources", priority = 40)]
        public static void Report()
        {
            var census = AudioSceneCensus.Take();
            var text = census.Report;

            if (!Application.isPlaying)
            {
                text += "\n  (edit mode — beds have not started and the rig has not built its "
                        + "sources yet. Press Play and run this again for the real answer.)";
            }

            if (census.Playing == 0 && Application.isPlaying)
            {
                Debug.LogWarning(text + "\n  Nothing is audible. Check that the scene has a "
                    + "MatchAudioRig with a MatchAudioLibrary assigned — "
                    + "HorrorGame ▸ Audio ▸ Wire Audio Into Open Scene.");
                return;
            }

            Debug.Log(text);
        }

        /// <summary>
        /// Reports the clip set rather than the scene: how many of the shipped WAVs
        /// reached a slot, and which slots are still empty.
        /// </summary>
        [MenuItem("HorrorGame/Audio/Report Clip Library Coverage", priority = 41)]
        public static void ReportCoverage()
        {
            var library = AssetDatabase.LoadAssetAtPath<MatchAudioLibrary>(AudioSceneWiring.MatchLibraryPath);
            if (library == null)
            {
                Debug.LogWarning("[AudioCoverage] No library at " + AudioSceneWiring.MatchLibraryPath
                    + ". Run HorrorGame ▸ Audio ▸ Rebuild Audio Libraries.");
                return;
            }

            var gaps = library.Validate(out var clips, out var gapCount);
            var message = "[AudioCoverage] " + clips + " clips wired, " + gapCount + " gaps.\n" + gaps;

            if (gapCount == 0)
            {
                Debug.Log(message, library);
                return;
            }

            Debug.LogWarning(message, library);
        }

        /// <summary>Batch twin of <see cref="Report"/>, for a headless check.</summary>
        public static void ReportBatch()
        {
            var census = AudioSceneCensus.Take();
            Debug.Log(census.Report);
            EditorApplication.Exit(census.Total > 0 ? 0 : 1);
        }
    }
}
