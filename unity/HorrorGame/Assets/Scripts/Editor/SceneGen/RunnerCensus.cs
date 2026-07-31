using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Map;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// §12's 실전 검증 taken from every place on a map instead of the ten §12 samples.
    /// <para>
    /// <b>Why this exists.</b> §12 grades a map by taking aggro at ten points and asking
    /// how many can break it, and its bands — 8/10 이상 너무 쉽다, 5~7/10 적정, 3/10 이하
    /// 너무 어렵다 — are quoted against exactly ten tries. That makes the published grade
    /// a ten-point sample of an underlying rate, and near the band a sample of ten is
    /// noisy: a map whose true rate is 60% still reports outside 5~7 on about a third of
    /// seeds. Two very different maps therefore both report "10/10 TooEasy" — one that is
    /// borderline and got an unlucky ten, and one where <em>every</em> place escapes.
    /// Measured on 요양원 지하 5층 on 2026-08-01 the answer was the second: <b>164 of 164
    /// places, 100%</b>. That is a much stronger statement than 10/10 and it is the one a
    /// designer needs before deciding how far to move a wall.
    /// </para>
    /// <para>
    /// <b>Why per zone.</b> §12's prescription for a TooEasy map is "시야 차단 지점을
    /// 줄인다", which is only actionable once you know which 구역 is handing out the
    /// releases. The escape a Runner takes is a simple path over the whole graph and it
    /// crosses 계단 freely, so a storey with no cover of its own still grades escapable
    /// when the storey above it has plenty — which is exactly the coupling that makes
    /// straightening one corridor at a time feel like it changes nothing.
    /// </para>
    /// <para>
    /// This is a report, never a gate: <see cref="MapValidator"/> decides whether a map
    /// can be built and this only describes the one it built. It is deterministic — the
    /// sample is every node, so unlike <see cref="RunnerTest.Run"/> there is no seed and
    /// no shuffle.
    /// </para>
    /// </summary>
    public sealed class RunnerCensus
    {
        private readonly string[] _zoneNames;
        private readonly int[] _placesPerZone;
        private readonly int[] _escapablePerZone;

        private RunnerCensus(string[] zoneNames, int[] placesPerZone, int[] escapablePerZone, int places, int escapable)
        {
            _zoneNames = zoneNames;
            _placesPerZone = placesPerZone;
            _escapablePerZone = escapablePerZone;
            Places = places;
            Escapable = escapable;
        }

        /// <summary>Every place §12 could have sampled — the whole graph.</summary>
        public int Places { get; }

        /// <summary>Places from which §06's release conditions can both be met.</summary>
        public int Escapable { get; }

        /// <summary>
        /// Share of the map a Runner can break aggro from, 0 on an empty graph. Compare
        /// against <see cref="GameConstants.RunnerTestPassRateMin"/>~
        /// <see cref="GameConstants.RunnerTestPassRateMax"/>: this is the rate §12's ten
        /// samples are drawn from, so it is what a change to the map has to move.
        /// </summary>
        public float EscapableFraction => Places == 0 ? 0f : Escapable / (float)Places;

        /// <summary>Runs §12's 주자 테스트 from every node of <paramref name="graph"/>.</summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="graph"/> is null.</exception>
        public static RunnerCensus Take(MapGraph graph)
        {
            if (graph == null)
            {
                throw new System.ArgumentNullException(nameof(graph));
            }

            var starts = new int[graph.Nodes.Length];
            for (var i = 0; i < starts.Length; i++)
            {
                starts[i] = i;
            }

            var report = HorrorGame.Core.Map.RunnerTest.RunAt(graph, starts);
            var zoneNames = new string[graph.Zones.Length];
            for (var z = 0; z < graph.Zones.Length; z++)
            {
                zoneNames[z] = graph.Zones[z].Name;
            }

            var places = new int[graph.Zones.Length];
            var escapable = new int[graph.Zones.Length];
            var total = 0;

            for (var i = 0; i < starts.Length; i++)
            {
                var zone = graph.Nodes[i].ZoneId;
                if (zone >= 0 && zone < places.Length)
                {
                    places[zone]++;
                }

                if (!report.Attempts[i].Released)
                {
                    continue;
                }

                total++;
                if (zone >= 0 && zone < escapable.Length)
                {
                    escapable[zone]++;
                }
            }

            return new RunnerCensus(zoneNames, places, escapable, starts.Length, total);
        }

        /// <summary>The census as text, for the same log the §12 grade is printed to.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("§12 실전 검증, every place rather than the ten §12 samples: ")
                .Append(Escapable).Append('/').Append(Places).Append(" escapable (")
                .Append(Percent(EscapableFraction)).Append("), against §12's ")
                .Append(Percent(GameConstants.RunnerTestPassRateMin)).Append('~')
                .Append(Percent(GameConstants.RunnerTestPassRateMax)).Append(" band.\n");

            for (var z = 0; z < _zoneNames.Length; z++)
            {
                var inZone = _placesPerZone[z];
                text.Append("  ").Append(_zoneNames[z]).Append(": ")
                    .Append(_escapablePerZone[z]).Append(" of ").Append(inZone)
                    .Append(" escapable");
                if (inZone > 0)
                {
                    text.Append(" (").Append(Percent(_escapablePerZone[z] / (float)inZone)).Append(')');
                }

                text.Append('\n');
            }

            return text.ToString();
        }

        /// <inheritdoc />
        public override string ToString() => Describe();

        private static string Percent(float fraction) =>
            (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
