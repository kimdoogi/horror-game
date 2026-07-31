#nullable enable

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// What the scene is actually making noise with, right now.
    /// <para>
    /// <b>Why this exists.</b> A silent build and a correctly wired build look identical
    /// from batch mode, which is where this project is verified — and the failure this
    /// layer is most likely to have is not a wrong sound but no sound, from a library
    /// that never got assigned or a source that was configured and never played. There
    /// is no way to listen from <c>-batchmode</c>, so the substitute for listening is
    /// counting: how many sources exist, how many are audible, which bus each is on and
    /// what clip is in it.
    /// </para>
    /// <para>
    /// It is in the runtime layer rather than in the editor menu that prints it because
    /// the PlayMode test asserts on exactly these numbers. A report the test cannot read
    /// would be a second implementation of the same question, and the two would
    /// eventually disagree about which one was right.
    /// </para>
    /// </summary>
    public static class AudioSceneCensus
    {
        /// <summary>One live <see cref="AudioSource"/>, as the report sees it.</summary>
        public readonly struct Entry
        {
            /// <summary>Builds an entry.</summary>
            public Entry(string path, string clip, bool playing, float volume, bool spatial, float cutoffHz, float occlusion)
            {
                Path = path;
                Clip = clip;
                Playing = playing;
                Volume = volume;
                Spatial = spatial;
                CutoffHz = cutoffHz;
                Occlusion = occlusion;
            }

            /// <summary>Hierarchy path, so a missing sound can be found in the scene.</summary>
            public string Path { get; }

            /// <summary>The clip assigned or last played, or "-".</summary>
            public string Clip { get; }

            /// <summary>Whether the source is producing sound this frame.</summary>
            public bool Playing { get; }

            /// <summary>Final volume after bus gain, occlusion and rolloff level.</summary>
            public float Volume { get; }

            /// <summary>True for a 3D source. §05 makes the stereo image load-bearing, so a flat cue is a decision.</summary>
            public bool Spatial { get; }

            /// <summary>The low-pass corner, hertz. <see cref="AudioTuning.OcclusionOpenCutoffHz"/> when nothing is in the way.</summary>
            public float CutoffHz { get; }

            /// <summary>Occlusion, 0 (clear) to 1 (blocked), or −1 when the source has no occluder.</summary>
            public float Occlusion { get; }

            /// <summary>One aligned line for a log.</summary>
            public string Line
            {
                get
                {
                    return (Playing ? "  ▶ " : "  · ")
                        + Path.PadRight(52)
                        + " " + Clip.PadRight(34)
                        + " vol " + Volume.ToString("0.000")
                        + (Spatial ? "  3D" : "  2D")
                        + "  lpf " + Mathf.RoundToInt(CutoffHz).ToString().PadLeft(5) + " Hz"
                        + (Occlusion >= 0f ? "  occ " + Occlusion.ToString("0.00") : "  occ   —");
                }
            }
        }

        /// <summary>Everything the census found.</summary>
        public readonly struct Result
        {
            /// <summary>Builds a result.</summary>
            public Result(IReadOnlyList<Entry> entries, int playing, bool hasListener, bool hasMix)
            {
                Entries = entries;
                Playing = playing;
                HasListener = hasListener;
                HasMix = hasMix;
            }

            /// <summary>Every source, sorted so the audible ones come first.</summary>
            public IReadOnlyList<Entry> Entries { get; }

            /// <summary>How many are producing sound.</summary>
            public int Playing { get; }

            /// <summary>Whether the scene has an <see cref="AudioListener"/>. Without one, §05's whole premise is off.</summary>
            public bool HasListener { get; }

            /// <summary>Whether a <see cref="GameAudio"/> is present to own the bus gains.</summary>
            public bool HasMix { get; }

            /// <summary>How many sources exist at all.</summary>
            public int Total => Entries.Count;

            /// <summary>The whole report, ready to log.</summary>
            public string Report
            {
                get
                {
                    var text = new StringBuilder();
                    text.Append("[AudioCensus] ").Append(Playing).Append(" of ").Append(Total)
                        .Append(" sources audible");

                    if (!HasListener)
                    {
                        text.Append("   ⚠ no AudioListener — §05 puts the ears on the camera and there is no camera");
                    }

                    if (!HasMix)
                    {
                        text.Append("   ⚠ no GameAudio — every bus falls back to its trim");
                    }

                    text.AppendLine();

                    for (var i = 0; i < Entries.Count; i++)
                    {
                        text.AppendLine(Entries[i].Line);
                    }

                    return text.ToString();
                }
            }
        }

        /// <summary>
        /// Walks every <see cref="AudioSource"/> in the loaded scenes and reports it.
        /// <para>
        /// Inactive objects are included on purpose: a source that was wired and then
        /// left on a disabled object is the exact bug this is looking for, and skipping
        /// it would hide it.
        /// </para>
        /// </summary>
        public static Result Take()
        {
            var sources = Object.FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var entries = new List<Entry>(sources.Length);
            var playing = 0;

            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source == null)
                {
                    continue;
                }

                var filter = source.GetComponent<AudioLowPassFilter>();
                var occluder = source.GetComponent<SoundOccluder>();

                var entry = new Entry(
                    PathOf(source.transform),
                    source.clip != null ? source.clip.name : "-",
                    source.isPlaying,
                    source.volume,
                    source.spatialBlend > 0.5f,
                    filter != null ? filter.cutoffFrequency : AudioTuning.OcclusionOpenCutoffHz,
                    occluder != null ? occluder.Occlusion : -1f);

                if (entry.Playing)
                {
                    playing++;
                }

                entries.Add(entry);
            }

            // Audible first, then by path. A hundred idle bed voices would otherwise
            // bury the four things actually making noise.
            entries.Sort((a, b) =>
            {
                if (a.Playing != b.Playing)
                {
                    return a.Playing ? -1 : 1;
                }

                return string.CompareOrdinal(a.Path, b.Path);
            });

            return new Result(
                entries,
                playing,
                Object.FindFirstObjectByType<AudioListener>() != null,
                GameAudio.Instance != null);
        }

        private static string PathOf(Transform transform)
        {
            var path = transform.name;
            var parent = transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
