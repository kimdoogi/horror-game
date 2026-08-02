using System.Linq;
using HorrorGame.Core.Map;
using HorrorGame.Core.Session;
using HorrorGame.EditorTools.SceneGen;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// <see cref="MapSketch.Offset"/> — the thing that lets a storey be drawn once and
    /// placed anywhere.
    /// <para>
    /// <b>Why it exists.</b> A <see cref="MapSketch.Stair"/> is fixed geometry: its two
    /// landings are <c>(x, z)</c> and <c>(x + 1, z)</c> on adjacent floors, so two
    /// storeys can only be joined where their plans genuinely overlap. Three deep
    /// storeys authored in parallel landed in three unrelated corners of the grid and
    /// one of them shared no row at all with the floor it hangs from. Rewriting a floor
    /// plan at new coordinates means retyping every mark and every door by hand and
    /// re-verifying a graph that had already been measured; an offset means the storey
    /// says what it looks like and the building says where it is.
    /// </para>
    /// <para>
    /// <b>What has to be true for that to be worth anything.</b> The whole value is
    /// that moving a floor does not change it. Every assertion here is a form of that
    /// one claim, because the failure mode is not a crash — it is a storey that was
    /// measured at the origin, shipped somewhere else, and is quietly a different shape
    /// there. That is F-006's pathology with a translation on it.
    /// </para>
    /// </summary>
    public sealed class MapSketchOffsetTests
    {
        private const int Seed = 20260731;

        /// <summary>A small plan with a loop, a spur, a mark and a door — one of each thing an offset could drop.</summary>
        private static MapSketchResult BuildProbe(int dx, int dz)
        {
            var sketch = new MapSketch()
                .Named("OffsetProbe")
                .DefaultKind(MapNodeKind.MazeSpace);

            sketch.AddZone("P", FloorMaterial.Concrete, 0, 10 + dx, 10 + dz, 8, 8);
            sketch.OnLevel(0);
            sketch.Offset(dx, dz);

            //          0 1 2 3 4 5 6 7   <- cell X (10..17)
            //   z17     . . . . . . . .
            //   z16     . . . . S . . .     a spur, ending in the way out
            //   z15     . . . . # . . .
            //   z14     . # # # # # . .
            //   z13     . # . . . # . .     the ring — one independent 순환로
            //   z12     . # . . . # . .
            //   z11     . # # # # # . .
            //   z10     . . . E . . . .     a second spur, ending in a mark
            sketch.Plan(10, 10,
                "........",
                "....S...",
                "....#...",
                ".#####..",
                ".#...#..",
                ".#...#..",
                ".#####..",
                "...E....");

            sketch.Mark('E', MapNodeKind.CandidateSite, "probe_site");
            sketch.Mark('S', MapNodeKind.Entrance, "probe_exit");

            // (12,11) is the body of the ring's south run: the corner is at (11,11) and
            // the spur's junction at (13,11), so a passage genuinely runs through it.
            // Door() rejects a junction, a bend or an end, which is what makes this the
            // right cell to test with — an offset that missed would not just misplace
            // the leaf, it would throw.
            sketch.Door(12, 11);

            return sketch.Build(Seed, new DeterministicRandom(Seed));
        }

        [Test]
        public void Moving_a_storey_does_not_change_its_shape()
        {
            var home = BuildProbe(0, 0);
            var moved = BuildProbe(17, -6);

            Assert.That(moved.Graph.Nodes.Length, Is.EqualTo(home.Graph.Nodes.Length));
            Assert.That(moved.Graph.Edges.Length, Is.EqualTo(home.Graph.Edges.Length));
            Assert.That(moved.Graph.IndependentLoopCount, Is.EqualTo(home.Graph.IndependentLoopCount));
            Assert.That(moved.Graph.IsConnected, Is.EqualTo(home.Graph.IsConnected));

            var homeDegrees = Enumerable.Range(0, home.Graph.Nodes.Length)
                .Select(home.Graph.Degree).OrderBy(d => d).ToArray();
            var movedDegrees = Enumerable.Range(0, moved.Graph.Nodes.Length)
                .Select(moved.Graph.Degree).OrderBy(d => d).ToArray();

            Assert.That(movedDegrees, Is.EqualTo(homeDegrees),
                "the degree sequence is the storey's shape. A translation that changed it "
                + "would mean cells landed on top of each other or off the grid, and the map "
                + "would have been measured somewhere it does not ship.");

            var homeLengths = home.Graph.Edges.Select(e => e.Length).OrderBy(l => l).ToArray();
            var movedLengths = moved.Graph.Edges.Select(e => e.Length).OrderBy(l => l).ToArray();
            Assert.That(movedLengths, Is.EqualTo(homeLengths).Within(0.001f));
        }

        [Test]
        public void Everything_the_storey_declared_moves_with_it()
        {
            var dx = 17;
            var dz = -6;
            var home = BuildProbe(0, 0);
            var moved = BuildProbe(dx, dz);

            // 2.5 m per cell — MapKitCatalogue.GridMetres, quoted because this is the
            // conversion the whole offset rests on.
            var shiftX = dx * MapKitCatalogue.GridMetres;
            var shiftZ = dz * MapKitCatalogue.GridMetres;

            foreach (var name in new[] { "probe_site", "probe_exit" })
            {
                var a = home.Graph.Nodes.Single(n => n.Name == name);
                var b = moved.Graph.Nodes.Single(n => n.Name == name);

                Assert.That(b.Position.X - a.Position.X, Is.EqualTo(shiftX).Within(0.001f), name);
                Assert.That(b.Position.Z - a.Position.Z, Is.EqualTo(shiftZ).Within(0.001f), name);
            }

            // The door is the one that would silently go missing: MapSketch matches a
            // door cell against the passage running through it, so an un-offset Door()
            // against an offset plan finds nothing and hangs no leaf — no exception, no
            // log, just a §12 bottleneck that cannot be shut.
            Assert.That(home.Graph.Edges.Count(e => e.HasLockableDoor), Is.EqualTo(1),
                "the probe declared one door");
            Assert.That(moved.Graph.Edges.Count(e => e.HasLockableDoor), Is.EqualTo(1),
                "the door did not move with the storey, so it matched no passage and was "
                + "dropped without a word.");
        }

        [Test]
        public void An_offset_storey_and_a_hand_drawn_one_are_the_same_map()
        {
            // The strongest form of the claim: drawing a plan at (27, 4) by hand and
            // drawing it at (10, 10) with an offset of (17, -6) must produce the same
            // geometry, not merely the same topology.
            var offsetBuilt = BuildProbe(17, -6);

            var hand = new MapSketch()
                .Named("OffsetProbe")
                .DefaultKind(MapNodeKind.MazeSpace);
            hand.AddZone("P", FloorMaterial.Concrete, 0, 27, 4, 8, 8);
            hand.OnLevel(0);
            hand.Plan(27, 4,
                "........",
                "....S...",
                "....#...",
                ".#####..",
                ".#...#..",
                ".#...#..",
                ".#####..",
                "...E....");
            hand.Mark('E', MapNodeKind.CandidateSite, "probe_site");
            hand.Mark('S', MapNodeKind.Entrance, "probe_exit");
            hand.Door(29, 5);

            var handBuilt = hand.Build(Seed, new DeterministicRandom(Seed));

            var a = offsetBuilt.Graph.Nodes.OrderBy(n => n.Position.X).ThenBy(n => n.Position.Z)
                .Select(n => n.Position.X.ToString("0.00") + "," + n.Position.Z.ToString("0.00")).ToArray();
            var b = handBuilt.Graph.Nodes.OrderBy(n => n.Position.X).ThenBy(n => n.Position.Z)
                .Select(n => n.Position.X.ToString("0.00") + "," + n.Position.Z.ToString("0.00")).ToArray();

            Assert.That(a, Is.EqualTo(b),
                "an offset is supposed to be indistinguishable from having drawn the plan "
                + "there in the first place. Anything else makes it a second way to author a "
                + "map, and two ways to author one map is how the two halves of F-006 "
                + "drifted apart.");
        }

        [Test]
        public void The_offset_is_a_cursor_and_can_be_put_back()
        {
            var sketch = new MapSketch().Named("Reset").DefaultKind(MapNodeKind.MazeSpace);
            sketch.AddZone("P", FloorMaterial.Concrete, 0, 10, 10, 10, 6);
            sketch.OnLevel(0);

            sketch.Offset(5, 5);
            sketch.Corridor(10, 10, 14, 10);
            sketch.Offset(0, 0);
            sketch.Corridor(10, 10, 14, 10);

            // Build refuses a map with no way out, and rightly — but that check is about
            // §02, not about the cursor, so the exit goes on the run drawn AFTER the
            // reset. If the offset had leaked, this mark would land on the moved run and
            // the assertion below would still be the thing that catches it.
            sketch.Mark(new MapCell(10, 10, 0), MapNodeKind.Entrance, "reset_exit");

            var built = sketch.Build(Seed, new DeterministicRandom(Seed));

            Assert.That(built.Graph.ConnectedComponentCount, Is.EqualTo(2),
                "the two runs were drawn 12.5 m apart and must not have merged — an offset "
                + "that leaked past Offset(0, 0) would put them on top of each other.");
        }
    }
}
