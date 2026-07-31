#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HorrorGame.UI.Settings
{
    /// <summary>
    /// The display choices this machine actually offers, and how to name them.
    /// <para>
    /// Built from <see cref="Screen.resolutions"/> and <c>QualitySettings.names</c>
    /// rather than from a list in code, because a list in code is a list that is wrong
    /// on somebody's monitor. The one thing that is filtered is duplicates: Unity
    /// reports the same width and height once per refresh rate, and a resolution
    /// stepper that needs eleven presses to get past 1920×1080 is a stepper nobody
    /// reaches the end of.
    /// </para>
    /// <para>
    /// Nothing here is a tuned game value. A resolution cannot change what the monster
    /// does; it changes how many pixels the same 80° of §05 arrives on.
    /// </para>
    /// </summary>
    public static class DisplayOptions
    {
        /// <summary>Distinct width×height pairs the display supports, ascending, with the current one guaranteed present.</summary>
        public static IReadOnlyList<Vector2Int> Resolutions()
        {
            var seen = new HashSet<long>();
            var list = new List<Vector2Int>();

            foreach (var resolution in Screen.resolutions)
            {
                Add(list, seen, resolution.width, resolution.height);
            }

            // A batch-mode or headless editor reports no modes at all, and a settings
            // screen with an empty stepper looks broken rather than headless.
            Add(list, seen, Screen.width, Screen.height);

            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        /// <summary>Highest refresh rate the display reports for a mode, in whole hertz. Zero when it reports none.</summary>
        public static int BestRefreshRateFor(int width, int height)
        {
            var best = 0;
            foreach (var resolution in Screen.resolutions)
            {
                if (resolution.width != width || resolution.height != height)
                {
                    continue;
                }

                var hz = Mathf.RoundToInt((float)resolution.refreshRateRatio.value);
                if (hz > best)
                {
                    best = hz;
                }
            }

            return best;
        }

        /// <summary>The four window modes, in the order a stepper should walk them.</summary>
        public static IReadOnlyList<FullScreenMode> FullScreenModes()
        {
            return new[]
            {
                UnityEngine.FullScreenMode.FullScreenWindow,
                UnityEngine.FullScreenMode.ExclusiveFullScreen,
                UnityEngine.FullScreenMode.MaximizedWindow,
                UnityEngine.FullScreenMode.Windowed,
            };
        }

        /// <summary>Quality preset names, as the project defines them.</summary>
        public static IReadOnlyList<string> QualityLevels()
        {
            var names = QualitySettings.names;
            return names != null && names.Length > 0 ? names : new[] { "기본" };
        }

        /// <summary>Korean label for a window mode.</summary>
        public static string Describe(FullScreenMode mode)
        {
            switch (mode)
            {
                case UnityEngine.FullScreenMode.ExclusiveFullScreen:
                    return "전체 화면 (독점)";

                case UnityEngine.FullScreenMode.FullScreenWindow:
                    return "전체 화면 (테두리 없음)";

                case UnityEngine.FullScreenMode.MaximizedWindow:
                    return "최대화 창";

                default:
                    return "창 모드";
            }
        }

        /// <summary>Korean label for a vsync count. Unity accepts 0, 1 and 2 and nothing else.</summary>
        public static string DescribeVSync(int count)
        {
            switch (count)
            {
                case 0:
                    return "끔 — 프레임 제한 없음";

                case 2:
                    return "절반 (2프레임마다)";

                default:
                    return "켬 — 화면 주사율에 맞춤";
            }
        }

        /// <summary>"1920 × 1080", with a refresh rate when one is known.</summary>
        public static string Describe(int width, int height, int refreshHz)
        {
            var text = width.ToString(CultureInfo.InvariantCulture) + " × " + height.ToString(CultureInfo.InvariantCulture);
            return refreshHz > 0 ? text + "  " + refreshHz.ToString(CultureInfo.InvariantCulture) + " Hz" : text;
        }

        /// <summary>
        /// Index of <paramref name="wanted"/> in <paramref name="list"/>, or the closest
        /// entry by pixel count. A settings file written on a different monitor names a
        /// mode this one may not have, and refusing to select anything would leave the
        /// stepper reading the first entry while the window is at another size.
        /// </summary>
        public static int NearestIndex(IReadOnlyList<Vector2Int> list, Vector2Int wanted)
        {
            var best = 0;
            var bestDistance = long.MaxValue;

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == wanted)
                {
                    return i;
                }

                var distance = Math.Abs(((long)list[i].x * list[i].y) - ((long)wanted.x * wanted.y));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Pushes the display half of <paramref name="settings"/> at the engine.
        /// <para>
        /// Skipped entirely in batch mode. <c>Screen.SetResolution</c> on a headless
        /// editor either does nothing or resizes the offscreen target the review shots
        /// are rendered into, and a settings applier that changes what a screenshot
        /// measures would make the art numbers depend on somebody's monitor.
        /// </para>
        /// </summary>
        public static void Apply(GameSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            QualitySettings.vSyncCount = settings.VSyncCount;

            var levels = QualitySettings.names;
            if (settings.QualityLevel >= 0 && levels != null && settings.QualityLevel < levels.Length)
            {
                // False: do not destroy and reload every loaded texture mid-session. The
                // preset still takes effect for everything rendered after this frame.
                QualitySettings.SetQualityLevel(settings.QualityLevel, applyExpensiveChanges: false);
            }

            if (Application.isBatchMode)
            {
                return;
            }

            var width = settings.ResolutionWidth > 0 ? settings.ResolutionWidth : Screen.width;
            var height = settings.ResolutionHeight > 0 ? settings.ResolutionHeight : Screen.height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            var alreadyThere = Screen.width == width
                && Screen.height == height
                && Screen.fullScreenMode == settings.FullScreenMode;

            if (alreadyThere)
            {
                return;
            }

            if (settings.RefreshRateHz > 0)
            {
                Screen.SetResolution(width, height, settings.FullScreenMode, new RefreshRate
                {
                    numerator = (uint)settings.RefreshRateHz,
                    denominator = 1u,
                });
            }
            else
            {
                Screen.SetResolution(width, height, settings.FullScreenMode);
            }
        }

        private static void Add(List<Vector2Int> list, HashSet<long> seen, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var key = ((long)width << 20) | (uint)height;
            if (seen.Add(key))
            {
                list.Add(new Vector2Int(width, height));
            }
        }
    }
}
