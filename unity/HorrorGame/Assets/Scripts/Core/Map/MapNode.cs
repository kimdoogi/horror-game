using System;
using System.Text;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Map
{
    /// <summary>
    /// What a place on the map is <em>for</em>, as flags.
    /// <para>
    /// §12 opens with "맵은 아트가 아니라 시스템이다", and this enum is where that
    /// claim becomes checkable: every role requirement in §12 is phrased as "구역당
    /// N개" of some kind of place, so the kinds have to be first-class data that
    /// <see cref="MapValidator"/> can count. A level designer who forgets to mark
    /// an observation post gets a failing test rather than a role that has to walk
    /// into death (§12 관측자).
    /// </para>
    /// <para>
    /// Flags rather than a single enum because §12's own sketch stacks them — the
    /// B 타일 hall is simultaneously 개방 공간 and holds [관측지점]. Dead-endedness is
    /// deliberately <b>not</b> a flag: it is derived from graph degree by
    /// <see cref="MapGraph.IsDeadEnd"/>, because §12's 막힌 길 비율 has to measure
    /// the topology that was actually built and not the intention that was
    /// declared.
    /// </para>
    /// </summary>
    [Flags]
    public enum MapNodeKind
    {
        /// <summary>Nothing declared. Legal — most junctions are just junctions.</summary>
        None = 0,

        /// <summary>
        /// 개방 공간 — hall, yard or stairwell. §12: where the Runner takes aggro
        /// from 15~25 m out, which is the only start distance §12's own table calls
        /// safe.
        /// </summary>
        OpenSpace = 1 << 0,

        /// <summary>
        /// 미로 공간 — S-corridor, loop or narrow passage. §12: where aggro is
        /// broken, and (§12 섬광수) the only stage on which a flash is worth using.
        /// </summary>
        MazeSpace = 1 << 1,

        /// <summary>
        /// 관측 지점 — a 2층 난간, window or grate. §12 관측자: somewhere the monster
        /// can be watched from <see cref="GameConstants.ObserverRange"/> without
        /// being reachable from where it stands.
        /// </summary>
        ObservationPost = 1 << 2,

        /// <summary>전기 패널. §12 정비공: "전기 패널 구역당 1개" — what makes a zone lightable at all.</summary>
        ElectricalPanel = 1 << 3,

        /// <summary>
        /// 단서 · 목표물 후보 지점. §12 builds three per zone and picks one per match,
        /// so all three must satisfy the site conditions — the choice is made after
        /// the map is built and cannot rescue a bad site.
        /// </summary>
        CandidateSite = 1 << 4,

        /// <summary>
        /// 은폐 지점 — somewhere to wait out a monster that already knows where you
        /// are going. §12's last checklist item exists for §07 새벽, the tier at
        /// which <c>MonsterKnowsExit</c> turns the way out into an ambush.
        /// </summary>
        Concealment = 1 << 5,

        /// <summary>
        /// 출입구 — the way out of the map. Marked rather than inferred because a
        /// map exit is topologically a leaf and must not be counted as a 막힌 길:
        /// it is the opposite of a dead end, and demanding loot at the door
        /// (§12 막힌 길 보상) would be nonsense.
        /// </summary>
        Entrance = 1 << 6,

        /// <summary>
        /// 계단 — a vertical transit. Called out separately because §12's floor
        /// table gives stairs 금속 and <see cref="GameConstants.ListenerClarityMetal"/>
        /// makes them the single loudest surface in the building.
        /// </summary>
        Stairwell = 1 << 7,
    }

    /// <summary>
    /// One place on the map: a junction, a room, a corner or a site.
    /// <para>
    /// A node is a <em>point</em>, not a volume, and every edge leaving it is a
    /// straight segment (see <see cref="MapGraph"/>). That is the modelling
    /// decision the whole of §12 rests on: if corridors only ever bend <em>at</em>
    /// nodes, then "20m 넘는 직선 통로" and "S자 통로(10m×2)" are both measurable
    /// from geometry instead of being asserted by whoever built the level. A
    /// curving corridor is expressed as more nodes, never as a longer edge.
    /// </para>
    /// </summary>
    public readonly struct MapNode
    {
        /// <summary>
        /// Builds a node. Prefer <see cref="MapGraphBuilder.AddNode"/>, which
        /// assigns the id and keeps the zone reference honest.
        /// </summary>
        /// <param name="id">Index into <see cref="MapGraph.Nodes"/>.</param>
        /// <param name="zoneId">Index into <see cref="MapGraph.Zones"/>.</param>
        /// <param name="position">Metres, world space. §12's map is 100 × 100 m.</param>
        /// <param name="kind">What §12 requirements this place satisfies.</param>
        /// <param name="deadEndRewardValue">
        /// Value of the 전리품 · 자재 stashed here, in the credits of §08. §12
        /// requires every 막힌 길 to carry one; anywhere else it may be zero.
        /// </param>
        /// <param name="name">Human label used in validator failures. Optional.</param>
        public MapNode(int id, int zoneId, Vec3 position, MapNodeKind kind, int deadEndRewardValue, string? name)
        {
            Id = id;
            ZoneId = zoneId;
            Position = position;
            Kind = kind;
            DeadEndRewardValue = deadEndRewardValue;
            Name = name;
        }

        /// <summary>Index into <see cref="MapGraph.Nodes"/>. Stable for the lifetime of the graph.</summary>
        public int Id { get; }

        /// <summary>
        /// The zone this place belongs to. §12's floor materials, observation
        /// posts, doors and candidate sites are all counted per zone, so an
        /// unassigned node would be invisible to half the checklist —
        /// <see cref="MapValidator"/> therefore also checks that this agrees with
        /// the zone whose bounds actually contain <see cref="Position"/>.
        /// </summary>
        public int ZoneId { get; }

        /// <summary>Position in metres. Y carries floors; §12's distance rules are horizontal (see <see cref="Vec3.MagnitudeFlat"/>).</summary>
        public Vec3 Position { get; }

        /// <summary>What §12 role requirements this place answers.</summary>
        public MapNodeKind Kind { get; }

        /// <summary>
        /// Reward waiting here, in §08 credits. §12: "막힌 길 보상 — 전리품 · 자재 …
        /// 위험을 감수할 이유." A dead end with nothing in it is a trap with no
        /// upside, which is the failure mode §12's 20~25% band is guarding against
        /// from the other side.
        /// </summary>
        public int DeadEndRewardValue { get; }

        /// <summary>Optional label, used verbatim in validator failure text so a designer can find the place.</summary>
        public string? Name { get; }

        /// <summary>True when this node carries every flag in <paramref name="flags"/>.</summary>
        public bool Has(MapNodeKind flags) => (Kind & flags) == flags;

        /// <summary>True when this node carries any flag in <paramref name="flags"/>.</summary>
        public bool HasAny(MapNodeKind flags) => (Kind & flags) != 0;

        /// <summary>
        /// One-line description for validator and runner-test messages. §12's
        /// checklist is only useful if a failure tells the designer <em>which</em>
        /// place broke it, so every message routes through here.
        /// </summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append('#').Append(Id);
            if (!string.IsNullOrEmpty(Name))
            {
                text.Append(' ').Append(Name);
            }

            text.Append(" (zone ").Append(ZoneId);
            if (Kind != MapNodeKind.None)
            {
                text.Append(", ").Append(Kind);
            }

            text.Append(", ").Append(Position).Append(')');
            return text.ToString();
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }
}
