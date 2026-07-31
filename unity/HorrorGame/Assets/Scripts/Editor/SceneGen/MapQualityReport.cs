using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// What a generated map scores, on §12's own two measures.
    /// <para>
    /// §12 splits judging a map in two and this keeps the split: the
    /// <see cref="MapValidationReport">checklist</see> is a gate — a map that breaks
    /// one of the eleven items is not a map and generation stops — while the
    /// <see cref="RunnerTestReport">주자 테스트</see> is a grade, and §12 says what to
    /// do about a bad one ("시야 차단 지점을 줄인다" / "S자 통로를 추가한다") rather than
    /// refusing to build it. Failing generation on the grade would mean a designer
    /// could not look at the map that scored badly.
    /// </para>
    /// </summary>
    public sealed class MapQualityReport
    {
        private MapQualityReport(
            int seed,
            MapValidationReport validation,
            RunnerTestReport runnerTest,
            float[] breakSpacings,
            RunnerCensus census)
        {
            Census = census;
            Seed = seed;
            Validation = validation;
            RunnerTest = runnerTest;
            BreakSpacings = breakSpacings;
        }

        /// <summary>Seed the map was generated from. Quote it in any bug about this layout.</summary>
        public int Seed { get; }

        /// <summary>§12's 검증 체크리스트, as a gate.</summary>
        public MapValidationReport Validation { get; }

        /// <summary>§12's 실전 검증, as a grade.</summary>
        public RunnerTestReport RunnerTest { get; }

        /// <summary>
        /// Distance in metres from each 시야 차단 지점 to its nearest neighbouring one.
        /// <para>
        /// §12's 수치 규칙 asks for 15~25 m ("질주 60m에 3~4번의 기회") but its checklist
        /// never states it, so <see cref="MapValidator"/> does not check it. It is
        /// reported here because it is the single number that moves the 주자 테스트
        /// score: cover chains when consecutive corners are close, and §12's advice
        /// for a map that scores too easy is precisely to spread them out.
        /// </para>
        /// </summary>
        public float[] BreakSpacings { get; }

        /// <summary>
        /// §12's 실전 검증 run from <em>every</em> place instead of the ten it samples.
        /// <para>
        /// §12's bands are quoted against ten tries, so the grade
        /// <see cref="RunnerTest"/> returns is a ten-point sample and nothing more. On a
        /// map whose true escape rate is anywhere near the 5~7/10 band that sample is a
        /// coin flip: at a true rate of 60% a single seed lands outside the band a third
        /// of the time. This is the same simulation over the whole graph, so a designer
        /// can tell "this map is borderline and the seed was unlucky" from "every place
        /// on this map escapes" — which are the same 10/10 to §12.
        /// </para>
        /// <para>
        /// It also localises the problem. The per-zone split says which 구역 is handing
        /// out the releases, and §12's prescription for a TooEasy map — "시야 차단 지점을
        /// 줄인다" — is only actionable once you know where they are.
        /// </para>
        /// </summary>
        public RunnerCensus Census { get; }

        /// <summary>True when every §12 rule passed — the gate.</summary>
        public bool Buildable => Validation.Passed;

        /// <summary>Runs both measures against a generated map.</summary>
        public static MapQualityReport Measure(MapSketchResult map)
        {
            var graph = map.Graph;
            return new MapQualityReport(
                map.Seed,
                MapValidator.Validate(graph),
                HorrorGame.Core.Map.RunnerTest.Run(graph, new DeterministicRandom(map.Seed)),
                MeasureBreakSpacing(graph),
                RunnerCensus.Take(graph));
        }

        /// <summary>The whole thing as text, ready to paste into a bug or read out of a CI log.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("=== §12 map quality — seed ").Append(Seed).Append(" ===\n");
            text.Append(Validation.Describe());
            text.Append('\n');
            text.Append(RunnerTest.Describe());
            text.Append('\n');
            text.Append(Census.Describe());
            text.Append('\n');
            text.Append(DescribeSpacing());
            return text.ToString();
        }

        /// <summary>The 시야 차단 지점 간격 distribution against §12's 15~25 m rule.</summary>
        public string DescribeSpacing()
        {
            if (BreakSpacings.Length == 0)
            {
                return "시야 차단 지점 간격: no sight-breaking corners on the map at all — every chase is a "
                       + "straight-line speed comparison the Runner loses.\n";
            }

            var min = float.PositiveInfinity;
            var max = 0f;
            var sum = 0f;
            var inBand = 0;
            for (var i = 0; i < BreakSpacings.Length; i++)
            {
                var value = BreakSpacings[i];
                min = value < min ? value : min;
                max = value > max ? value : max;
                sum += value;
                if (value >= GameConstants.LineOfSightBreakSpacingMin
                    && value <= GameConstants.LineOfSightBreakSpacingMax)
                {
                    inBand++;
                }
            }

            var text = new StringBuilder();
            text.Append("시야 차단 지점 간격 (§12 수치 규칙 ")
                .Append(Metres(GameConstants.LineOfSightBreakSpacingMin)).Append('~')
                .Append(Metres(GameConstants.LineOfSightBreakSpacingMax)).Append("): ")
                .Append(BreakSpacings.Length).Append(" corners, nearest-neighbour ")
                .Append(Metres(min)).Append('~').Append(Metres(max)).Append(", mean ")
                .Append(Metres(sum / BreakSpacings.Length)).Append(", ")
                .Append(inBand).Append(" inside the band.\n");
            return text.ToString();
        }

        /// <inheritdoc />
        public override string ToString() => Describe();

        /// <summary>
        /// Walking distance from every 시야 차단 지점 to the closest other one.
        /// A corner counts when a bend of at least
        /// <see cref="GameConstants.MapSightBreakingBendDegrees"/> meets there and the
        /// place is not 개방 공간 — the same test <see cref="RunnerTest"/> applies, so
        /// this measures the thing that actually decides the score.
        /// </summary>
        private static float[] MeasureBreakSpacing(MapGraph graph)
        {
            var corners = new List<int>();
            for (var i = 0; i < graph.Nodes.Length; i++)
            {
                if (!graph.Nodes[i].Has(MapNodeKind.OpenSpace) && graph.IsSightBreakingCorner(i))
                {
                    corners.Add(i);
                }
            }

            var spacings = new float[corners.Count];
            for (var i = 0; i < corners.Count; i++)
            {
                var best = float.PositiveInfinity;
                for (var k = 0; k < corners.Count; k++)
                {
                    if (i == k)
                    {
                        continue;
                    }

                    var distance = graph.PathLength(corners[i], corners[k]);
                    if (distance < best)
                    {
                        best = distance;
                    }
                }

                spacings[i] = float.IsPositiveInfinity(best) ? 0f : best;
            }

            return spacings;
        }

        private static string Metres(float value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture) + " m";
    }
}
