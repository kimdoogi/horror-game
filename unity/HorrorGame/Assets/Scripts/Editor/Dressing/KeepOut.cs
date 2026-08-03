#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.EditorTools.SceneGen;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// The volumes the dressing pass may not put anything into, read off the scene the
    /// map generator has just written.
    /// <para>
    /// <b>Why this exists.</b> §12 says 맵은 아트가 아니라 시스템이다, and a scatter that
    /// runs after the map is finished can break the system without touching a wall. It
    /// did: run 11 dropped <c>Dress_RubblePile_22_22</c> under B5's 투하구, and every
    /// runner who took that chute landed on 0.26 m of scenery instead of on the storey
    /// below — 「the chute drop is the whole game and it now lands on scenery」. Nothing
    /// in the pass could have noticed, because the pass measured corridors and the
    /// defect was in a column of air.
    /// </para>
    /// <para>
    /// <b>Everything here is a place the game puts a body, or swings a door.</b> A
    /// runner appears at a 착지 and falls; a runner steps into a 투하구; twenty runners
    /// start on B1's rim; §06's creature starts on every storey; a §12 문 has to be able
    /// to shut. None of them is a thing the scatterer can see from the walkable cell
    /// grid, and all of them are markers in the scene with a position, so all of them
    /// are read from the scene rather than restated.
    /// </para>
    /// <para>
    /// <b>The radii are measured, not chosen.</b> A standing column is the player
    /// capsule's own radius (<see cref="PlayerTraversal.PlayerBody"/>, the same body
    /// <c>MapSceneBuilder.VerifyChutesDropIntoOpenAir</c> measures the drop against)
    /// plus <see cref="DressingSpace.WallInset"/>, which is the kit's own standoff
    /// between a corridor's clear section and its cell — this building's idea of "not
    /// touching a surface". A door's column is the leaf's own reach: a shut leaf spans
    /// <see cref="MapKitCatalogue.CorridorClearWidth"/> from its hinge, so every point
    /// within that of the hinge is somewhere the leaf can be.
    /// </para>
    /// <para>
    /// The shell plates are deliberately <em>not</em> in this list. Nothing needs to
    /// name them: <see cref="DressingSpace.InsideTheClearBand"/> keeps every solid piece
    /// inside the floor a runner can stand on, and a piece that never leaves the clear
    /// band can never reach a shell, a cap's outer face, or the ground between corridors.
    /// A keep-out keyed on the shells would be a second, weaker statement of that one.
    /// </para>
    /// </summary>
    public sealed class KeepOut
    {
        private readonly List<Column> _columns = new List<Column>();

        private KeepOut()
        {
        }

        /// <summary>How many 착지 drop columns are enforced.</summary>
        public int Landings { get; private set; }

        /// <summary>How many 투하구 mouths are enforced.</summary>
        public int Mouths { get; private set; }

        /// <summary>How many spawn points — runners' and the creature's — are enforced.</summary>
        public int Spawns { get; private set; }

        /// <summary>How many §12 door swings are enforced.</summary>
        public int Doors { get; private set; }

        /// <summary>The capsule every standing column was measured from.</summary>
        public PlayerTraversal.PlayerBody Body { get; private set; } = new PlayerTraversal.PlayerBody();

        /// <summary>Anything the read could not do, said out loud rather than assumed away.</summary>
        public string Note { get; private set; } = string.Empty;

        /// <summary>
        /// How long §01's fall lasts, seconds.
        /// <para>
        /// The runtime's <c>Chute.FallSeconds</c> and <c>MapSceneBuilder</c>'s
        /// <c>ChuteFallSeconds</c>, restated here for the third time and for the same
        /// stated reason: this is an editor assembly that does not reference the runtime's
        /// Race assembly. Restating an ARITHMETIC is safe in a way that restating a
        /// typed-in height was not — all three sides read
        /// <see cref="GameConstants.JumpGravity"/> and all three say half a second, so
        /// they can only disagree if somebody changes §01's fiction.
        /// </para>
        /// </summary>
        private const float ChuteFallSeconds = 0.5f;

        /// <summary>
        /// Metres above its 착지 a 투하구 puts a runner down — <c>Chute.DropHeightMetres</c>,
        /// derived rather than copied. ½ × 9.81 × 0.5² = 1.226 m.
        /// </summary>
        public const float ChuteDropHeightMetres =
            0.5f * GameConstants.JumpGravity * ChuteFallSeconds * ChuteFallSeconds;

        /// <summary>
        /// Reads every keep-out volume out of a generated map.
        /// </summary>
        /// <param name="mapRoot">The <c>Map</c> object <see cref="MapSceneBuilder"/> creates.</param>
        public static KeepOut Read(GameObject mapRoot)
        {
            var keepOut = new KeepOut();
            var markers = mapRoot.transform.Find(MapSceneBuilder.MarkerRootName);
            if (markers == null)
            {
                keepOut.Note = "No '" + MapSceneBuilder.MarkerRootName + "' under " + mapRoot.name
                    + ", so no 투하구, spawn or 문 could be kept clear. This is a map the generator "
                    + "did not write.";
                return keepOut;
            }

            var body = keepOut.Body;
            var stand = body.Radius + DressingSpace.WallInset;

            // 착지 — the column a dropped runner falls down and then stands in. Its top is
            // where the runner appears, plus the body that hangs below the crown: exactly
            // the capsule VerifyChutesDropIntoOpenAir builds, so the two checks refuse the
            // same volume and a piece this pass allows cannot fail that one.
            foreach (var landing in Group(markers, MapMarkerKind.ChuteLanding))
            {
                keepOut._columns.Add(new Column("착지", landing.name, landing.position, stand,
                    landing.position.y, landing.position.y + ChuteDropHeightMetres + body.Height));
                keepOut.Landings++;
            }

            // 투하구 — the mouth a runner has to be able to walk into. Same capsule: a hole
            // with a crate over it is not a hole.
            foreach (var mouth in Group(markers, MapMarkerKind.Chute))
            {
                keepOut._columns.Add(new Column("투하구", mouth.name, mouth.position, stand,
                    mouth.position.y, mouth.position.y + body.Height));
                keepOut.Mouths++;
            }

            // §01's 36 rim starts. The reach audit proves every one of them stands on
            // ground a runner can get into — and it proves it before this pass runs, so
            // a crate dropped on a start is a defect no existing gate would ever see.
            foreach (var spawn in Group(markers, MapMarkerKind.PlayerSpawn))
            {
                keepOut._columns.Add(new Column("출발점", spawn.name, spawn.position, stand,
                    spawn.position.y, spawn.position.y + body.Height));
                keepOut.Spawns++;
            }

            // §06's creature, one per storey. Its own baked agent radius, because the
            // creature is not a 0.30 m capsule and using the runner's would keep a
            // 0.45 m circle clear for a body that needs more.
            var agent = AgentRadius(out var agentNote);
            keepOut.Note = agentNote;
            foreach (var spawn in Group(markers, MapMarkerKind.MonsterSpawn))
            {
                keepOut._columns.Add(new Column("창조물", spawn.name, spawn.position,
                    agent + DressingSpace.WallInset, spawn.position.y, spawn.position.y + body.Height));
                keepOut.Spawns++;
            }

            // §12's 문. Found by its hinge rather than by its name: MapSceneBuilder builds
            // a door as a marker object with a "Hinge" child, and a name prefix is the kind
            // of seam that goes on matching after the thing it named has moved.
            var leafHeight = MapKitCatalogue.HeightMetres(MapKitPiece.DoorPanelLockable);
            foreach (Transform child in markers)
            {
                var hinge = child.Find("Hinge");
                if (hinge == null)
                {
                    continue;
                }

                keepOut._columns.Add(new Column("문", child.name, hinge.position,
                    MapKitCatalogue.CorridorClearWidth, hinge.position.y, hinge.position.y + leafHeight));
                keepOut.Doors++;
            }

            return keepOut;
        }

        /// <summary>
        /// Metres between a placed piece and the nearest keep-out volume: negative when
        /// the piece is inside one, and then the depth of the shallowest intrusion.
        /// <para>
        /// The same reading, and the same reasoning, as
        /// <c>MapSceneBuilder.Separation</c>: two volumes are apart the moment one axis
        /// separates them, so the largest per-axis gap is the answer. The piece is taken
        /// as its world bounding box, which claims the corners a barrel does not fill —
        /// conservative in the direction that matters, since a volume this call reports
        /// clear is clear for the mesh too.
        /// </para>
        /// </summary>
        /// <param name="bounds">World bounds of the piece.</param>
        /// <param name="where">Which volume was nearest, and its kind.</param>
        public float Separation(Bounds bounds, out string where)
        {
            where = "nothing";
            var nearest = float.PositiveInfinity;
            for (var i = 0; i < _columns.Count; i++)
            {
                var gap = _columns[i].Separation(bounds);
                if (gap < nearest)
                {
                    nearest = gap;
                    where = _columns[i].Kind + " " + _columns[i].Name;
                }
            }

            return nearest;
        }

        /// <summary>Whether a piece with these bounds may not be kept.</summary>
        /// <param name="bounds">World bounds of the piece.</param>
        /// <param name="where">Which volume it is inside.</param>
        public bool Rejects(Bounds bounds, out string where)
        {
            return Separation(bounds, out where) < 0f;
        }

        /// <summary>The volumes, in the terms the design cares about. One line.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append(Landings).Append(" 착지 columns (r ")
                .Append(F(Body.Radius + DressingSpace.WallInset)).Append(" m × ")
                .Append(F(ChuteDropHeightMetres + Body.Height)).Append(" m — 착지 + the ")
                .Append(F(ChuteDropHeightMetres)).Append(" m drop and a ")
                .Append(F(Body.Height)).Append(" m body), ")
                .Append(Mouths).Append(" 투하구 mouths, ")
                .Append(Spawns).Append(" spawns, ")
                .Append(Doors).Append(" 문 swings (r ")
                .Append(F(MapKitCatalogue.CorridorClearWidth)).Append(" m, the leaf's own reach)");
            return text.ToString();
        }

        private static string F(float metres) => metres.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Children of the marker group <see cref="MapSceneBuilder"/> writes for a kind.</summary>
        private static IEnumerable<Transform> Group(Transform markers, MapMarkerKind kind)
        {
            // The group's name is derived the way BuildMarkers derives it — kind + "s" —
            // rather than spelled out, so a renamed enum value moves both ends at once.
            var group = markers.Find(kind.ToString() + "s");
            if (group == null)
            {
                yield break;
            }

            foreach (Transform child in group)
            {
                yield return child;
            }
        }

        /// <summary>
        /// The radius §06's creature is baked to walk at, from the NavMesh settings the
        /// bake itself used. Falls back to the runner's capsule and says so.
        /// </summary>
        private static float AgentRadius(out string note)
        {
            if (NavMesh.GetSettingsCount() > 0)
            {
                note = string.Empty;
                return NavMesh.GetSettingsByIndex(0).agentRadius;
            }

            note = "No NavMesh agent settings were available, so §06's spawn columns were kept clear "
                + "at the runner's capsule radius instead of the creature's. That is the smaller of "
                + "the two: a monster spawn this run calls clear may still be tight.";
            return new PlayerTraversal.PlayerBody().Radius;
        }

        /// <summary>One upright cylinder nothing may be placed inside.</summary>
        private readonly struct Column
        {
            /// <summary>What it protects: 착지 · 투하구 · 출발점 · 창조물 · 문.</summary>
            public readonly string Kind;

            /// <summary>The marker it was read from, so a failure names a scene object.</summary>
            public readonly string Name;

            private readonly Vector2 _centre;
            private readonly float _radius;
            private readonly float _bottomY;
            private readonly float _topY;

            public Column(string kind, string name, Vector3 at, float radius, float bottomY, float topY)
            {
                Kind = kind;
                Name = name;
                _centre = new Vector2(at.x, at.z);
                _radius = radius;
                _bottomY = bottomY;
                _topY = topY;
            }

            /// <summary>Metres between this column and a box; negative when they overlap.</summary>
            public float Separation(Bounds bounds)
            {
                // Circle against the box's plan rectangle: the nearest point of the
                // rectangle to the centre, less the radius.
                var dx = Mathf.Max(bounds.min.x - _centre.x, _centre.x - bounds.max.x, 0f);
                var dz = Mathf.Max(bounds.min.z - _centre.y, _centre.y - bounds.max.z, 0f);
                var flat = Mathf.Sqrt((dx * dx) + (dz * dz)) - _radius;

                var vertical = Mathf.Max(_bottomY - bounds.max.y, bounds.min.y - _topY);
                return Mathf.Max(flat, vertical);
            }
        }
    }
}
