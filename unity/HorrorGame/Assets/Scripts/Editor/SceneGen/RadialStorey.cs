using System;
using System.Collections.Generic;
using HorrorGame.Core.Map;
using HorrorGame.Core.Session;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// One storey of §01's descent: three concentric ring bands, gates narrowing 4 → 2 → 1,
    /// and the 투하구 in the middle.
    /// <para>
    /// <b>Generated rather than drawn.</b> Every other storey in this project is a hand-typed
    /// ASCII plan, and that is right when a floor has a story to tell — 굴착층's spoil heap is
    /// in the wrong place on purpose and no generator would ever put it there. It is wrong
    /// here. §12-A gives every storey the same skeleton and the same three gate counts, so
    /// eight hand-drawn versions of one skeleton would be eight chances to typo the thing the
    /// whole game is made of. What varies between floors is a handful of numbers, and those
    /// are the arguments to <see cref="Build"/>.
    /// </para>
    /// <para>
    /// <b>The band is a zigzag, and that is load-bearing.</b> A ring band two cells thick could
    /// be drawn as two concentric squares, which would be a 52 m straight run on every side —
    /// four times §12's 20 m cap, and a sight line down which nothing survives. So the band
    /// alternates between its outer and inner track every few cells, connected by a rung. That
    /// single decision buys four §12 rules at once: no straight over the jog length, an S자
    /// 통로 at every jog, one 순환로 per band because the zigzag closes on itself, and a bend
    /// every few metres for <c>sight-break-spacing</c>. It also makes the ring longer to walk
    /// than it looks, which is what turns "get to the middle" into a race rather than a
    /// diagonal.
    /// </para>
    /// <para>
    /// <b>Gates are the design.</b> §12-A: four ways from the outer band to the middle, two
    /// from the middle to the inner, and exactly one into the centre. Twenty players share one
    /// cell on the last step. Each gate carries a door — 1.1 s to shut, 4.5 s to break, never
    /// repaired — which in a race is not protection but a toll charged to everybody behind.
    /// </para>
    /// </summary>
    public static class RadialStorey
    {
        /// <summary>Cells the outer band is thick. Two tracks and the rungs between them.</summary>
        public const int BandThickness = 2;

        /// <summary>
        /// Cells between jogs in a band. Four cells is 10 m of straight. Five measured 20.0 m
        /// exactly — passing §12's cap by nothing at all, because a band run and the gate
        /// passage leaving it are collinear and the graph reads them as one line.
        /// </summary>
        public const int JogEvery = 4;

        /// <summary>
        /// Cells that must separate two alcoves on the same rail. Two is the floor: adjacent
        /// alcoves would share a wall and the graph would read them as one two-cell room
        /// rather than as two blind ends.
        /// <para>
        /// There is no probability here on purpose. An earlier version rolled for each
        /// bearing and the dead-end ratio wandered with the seed — 18.0% at one rate, 19.1%
        /// at another, never reliably inside §12's 20~25% band. Punching every bearing that
        /// is legal lands it at 20.0% and lands it there every time, and a floor that obeys
        /// the rules by construction is worth more than one that usually does. The seed still
        /// decides the thing that should vary: where the gates are.
        /// </para>
        /// </summary>
        public const int AlcoveSpacing = 14;

        /// <summary>
        /// Draws one storey. The caller owns <see cref="MapSketch.AddZone"/> and
        /// <see cref="MapSketch.OnLevel"/>, because a zone's surface and a storey's place in
        /// the building are decisions about the building rather than about this floor.
        /// </summary>
        /// <param name="s">Sketch to draw into.</param>
        /// <param name="level">Storey.</param>
        /// <param name="centreX">Cell X of the middle.</param>
        /// <param name="centreZ">Cell Z of the middle.</param>
        /// <param name="random">Seeded. Decides gate angles and which spurs exist.</param>
        /// <param name="label">Prefix for named places, e.g. "B3".</param>
        /// <returns>Where things ended up, for the caller to hang chutes and spawns on.</returns>
        public static RadialStoreyResult Build(
            MapSketch s, int level, int centreX, int centreZ, IRandomSource random, string label)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            s.OnLevel(level);

            // Chebyshev radii. The walls between bands are the radii that get no corridor at
            // all except where a gate is punched, which is what makes a gate a gate.
            //
            //   d 0..1   중심      the chutes, and on B8 the finish
            //   d 2      wall      1 gate
            //   d 3..4   안쪽 고리
            //   d 5      wall      2 gates
            //   d 6..7   중간 고리
            //   d 8      wall      4 gates
            //   d 9..10  외곽 고리  players stand here
            var bands = new[]
            {
                // One rail, not two. The inner ring's perimeter is 24 cells and JogEvery is
                // 4, so a zigzag there jogs six times round a ring barely big enough to hold
                // them — and with corners and their neighbours pinned to the outer rail, what
                // is left of the inner rail is short disconnected stubs. Six of the storey's
                // 33 blind cells came from this one band, the largest single group, and every
                // one of them a place the graph called reachable and the bake could not path
                // to.
                //
                // Drawn as a plain ring it is 7 cells a side — 17.5 m, inside §12's 20 m cap
                // — where the two-rail version's outer track was 9 cells and 22.5 m, which is
                // where straight-corridor has been failing by exactly one cell all along.
                new Band("안쪽", 3, 3, 1),
                new Band("중간", 6, 7, 2),
                new Band("외곽", 9, 10, 4),
            };

            var result = new RadialStoreyResult(centreX, centreZ);
            var occupied = new HashSet<long>();

            // ── 중심 ────────────────────────────────────────────────────────────
            // A 3 × 3 room. Not a corridor: it is the one place on the floor where several
            // players can see each other arrive, and §01 needs the chutes to be a visible
            // choice rather than a cell you stumble into.
            // A filled 3 x 3, and the plus that replaced it for one measurement is worth
            // recording as a wrong turn. The block tiles into nine 2.5 m corridor pieces
            // that wall each other, and the audit found it as a sealed island — one marker
            // on its own, `[1] MonsterSpawn`. A plus reads unambiguously to the tiler, so
            // it should have been better. It was worse: 11 islands became 18 and the
            // completion rate fell 98.1% to 91.2%, because each of the four arms is a blind
            // cell that takes a DeadEndCap, and four caps ringing the middle seal it more
            // thoroughly than the block did.
            //
            // The middle needs a piece of its own — a room, not a pattern of corridor —
            // and that is the next change rather than another arrangement of cells.
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    Put(s, level, null, occupied, centreX + dx, centreZ + dz);
                }
            }

            // And ONE piece over the top of them. B-010.
            //
            // The cells stay — they are what the graph is built from, so the middle is nine
            // places §12 can count, the inner gate has a passage to hang its door on, and
            // §02's finish has somewhere to be marked. What changes is the GEOMETRY: the
            // corridor tiler skips any cell inside a room, so instead of nine 2.5 m passages
            // walling each other off it lays Chamber_Open_3x3, which is open at the middle of
            // all four edges and nothing else.
            //
            // That split is the whole answer. Room() alone produced geometry with no graph —
            // four dock cells with one neighbour each, §12 refusing to hang a door on a dead
            // end and refusing to mark a finish inside a room, both correctly. Corridor cells
            // alone produced a graph with sealed geometry — the audit found it as one marker
            // on its own, `[1] MonsterSpawn`, which is why §06's creature reached nothing.
            // Cells for the graph, a piece for the world.
            s.Room(MapKitPiece.ChamberOpen3x3, centreX - 1, centreZ - 1, 3, 3, 0f);

            result.Centre = new MapCell(centreX, centreZ, level);

            // ── the three bands, outermost first ────────────────────────────────
            for (var i = bands.Length - 1; i >= 0; i--)
            {
                var band = bands[i];
                var track = DrawZigzagBand(s, level, centreX, centreZ, band, random, occupied);
                result.Bands.Add(track);
            }

            // ── gates ───────────────────────────────────────────────────────────
            // Punched after the bands exist, so a gate can be placed where both sides
            // already have corridor to join. Angles are spread evenly and then jittered,
            // which keeps 20 players from all turning the same way off the spawn ring
            // while still guaranteeing the four outer gates are nowhere near each other.
            //
            // And each band's gates are pushed as far as the ring allows from the gates of
            // the band OUTSIDE it. That stagger is where the distance to the middle comes
            // from. Left to spread evenly on their own, the first version measured 35 m from
            // the rim to the centre against §12-D's 60~90 m band — with four outer gates
            // spread round the compass you are never more than a few metres from one, so the
            // floor was a diagonal with corners in it. Staggered, every band has to be walked
            // roughly half way round before the next way in appears, and the narrowing
            // becomes a distance instead of a fact.
            var previousGates = new List<MapCell>();
            var allGates = new List<MapCell>();

            // Bands were appended outermost-first, so the list runs the other way from the
            // array. Indexing it with the array's own index reads the wrong band — which for
            // the alcoves was silently the wrong shape and for the gates was a crash.
            for (var i = bands.Length - 1; i >= 0; i--)
            {
                var band = bands[i];
                var innerEdge = i == 0 ? 1 : bands[i - 1].Inner;
                var wall = band.Inner - 1;

                var gates = PunchGates(
                    s, level, centreX, centreZ, wall, innerEdge, band, random, previousGates, occupied,
                    result.GateMouths,
                    result.Bands[bands.Length - 1 - i],
                    i == 0 ? null : result.Bands[bands.Length - i]);
                result.Gates.Add(gates);
                previousGates = gates;
                allGates.AddRange(gates);

                // §12 wants 20~25% of a floor's places blind, each with a reason to walk in.
                // A generated zigzag ring has almost none — measured 14.0% before these —
                // because every cell of it leads somewhere. So each band gets alcoves punched
                // outward into the wall it sits against: one cell deep, ending nowhere. In a
                // race they are the only places to stand aside, which makes them the only
                // places to hide from another player, and §12 drops a 전리품 in each.
                result.Alcoves.AddRange(
                    PunchAlcoves(
                        s, level, centreX, centreZ, band, random, allGates,
                        result.Bands[bands.Length - 1 - i], occupied));

                // Doors go on the two innermost bands only, and never on the four outer
                // gates. Measured: a door on one of four parallel ways in is not a 병목 at
                // all — the graph says shutting it forces a detour of almost nothing, because
                // three other gates are right there. That is not a validator technicality, it
                // is the truth about the race. A door is worth carrying to the place where it
                // costs everybody behind you something, and at the rim it costs them a shrug.
                //
                // §12 caps a zone at 1~2 doors and a floor is one zone, so: the single inner
                // gate always, and one of the two middle gates. Twenty players share that
                // inner cell, and whoever gets there first decides what it costs the rest.
                if (i <= 1)
                {
                    s.Door(gates[0].X, gates[0].Z);
                }
            }

            result.Bands.Reverse();
            result.Gates.Reverse();
            return result;
        }

        /// <summary>
        /// Draws one band as a zigzag between its outer and inner track, and returns every
        /// cell it used. The zigzag is the reason the band obeys §12; see the class remarks.
        /// </summary>
        private static List<MapCell> DrawZigzagBand(
            MapSketch s, int level, int cx, int cz, Band band, IRandomSource random, HashSet<long> occupied)
        {
            var cells = new List<MapCell>();
            var ring = Perimeter(cx, cz, band.Outer);
            var depth = band.Outer - band.Inner;

            // Track offset: 0 is the outer rail, depth is the inner one. Flipping it every
            // JogEvery cells and drawing the rung between is the whole trick.
            var offset = 0;
            var sinceJog = 0;

            for (var i = 0; i < ring.Count; i++)
            {
                var step = ring[i];

                // Corners AND their two neighbours sit on the outer rail. A corner's inward
                // neighbour is diagonal and §12 has no diagonals, so a corner can only be
                // reached along the rail it is on — and if the cell before or after it had
                // jogged inward, the corner would be left touching nothing at all. That is
                // not a cosmetic gap: MapSketch refuses to build a map with an orphan cell
                // in it, which is how this was found rather than shipped.
                var nearCorner = step.Corner
                                 || ring[(i + 1) % ring.Count].Corner
                                 || ring[(i + ring.Count - 1) % ring.Count].Corner;
                var here = nearCorner ? 0 : offset;
                Put(s, level, cells, occupied, step.X + (step.InX * here), step.Z + (step.InZ * here));

                sinceJog++;
                if (nearCorner || sinceJog < JogEvery || i >= ring.Count - 2)
                {
                    continue;
                }

                for (var t = 0; t <= depth; t++)
                {
                    Put(s, level, cells, occupied, step.X + (step.InX * t), step.Z + (step.InZ * t));
                }

                offset = offset == 0 ? depth : 0;
                sinceJog = 0;
            }

            // The seam. The walk starts and ends on the outer rail (i >= Count - 2 above
            // suppresses a jog near the end), so the ring closes without a check — but if a
            // future JogEvery ever left it open, this is where it would show, and an open
            // ring is a band with no 순환로 rather than a crash.
            return cells;
        }

        /// <summary>
        /// Cuts <paramref name="band"/>'s gate cells through the wall at
        /// <paramref name="wall"/>, spread around the compass.
        /// </summary>
        private static List<MapCell> PunchGates(
            MapSketch s,
            int level,
            int cx,
            int cz,
            int wall,
            int innerEdge,
            Band band,
            IRandomSource random,
            List<MapCell> avoid,
            HashSet<long> occupied,
            List<MapCell> mouths,
            List<MapCell> bandCells,
            List<MapCell>? bandBelow)
        {
            var ring = Perimeter(cx, cz, band.Inner);
            var gates = new List<MapCell>();
            var span = band.Inner - innerEdge;

            // Gates go on the flat of a side, never near a diagonal — and this is a real
            // constraint rather than taste. On a Chebyshev ring the inward step is a single
            // axis, so it only reduces the radius while that axis is the dominant one. Punch
            // a gate five cells from the corner of a radius-6 ring and the passage runs three
            // cells without ever leaving radius 6: a spur into the wall, ending nowhere, with
            // a door asked for on a bend. MapSketch refuses it, which is how this was found.
            var clear = band.Inner - span - 1;
            // And only where the band's inner rail was actually drawn. The zigzag spends half
            // its length on the outer rail, and a gate leaving a bearing the band skipped is
            // not a branch off anything — the mouth cell has the gate passing straight through
            // it and nothing else, so it is a passage rather than a junction. §12 counts
            // 후보 지점 at junctions, and MapSketch refuses a mark on a passage rather than
            // letting the count come up short with nothing failing.
            // Both ENDS of the gate have to land on a rail that was actually drawn. The check
            // above catches a gate that branches off nothing; this one catches a gate that
            // arrives at nothing, which is worse because it looks like a way in and is a dead
            // end. Left unchecked they made every floor 34% blind against §12's 20~25% band,
            // and — the part that matters — a storey with four gates on the outer wall of
            // which two go nowhere is a storey with two gates.
            var open = ring.FindAll(p => !p.Corner
                                         && System.Math.Abs(p.InX != 0 ? p.Z - cz : p.X - cx) <= clear
                                         && bandCells.Exists(c => c.X == p.X && c.Z == p.Z)
                                         && Arrives(p, span, bandBelow));
            if (open.Count == 0)
            {
                return gates;
            }

            var spacing = open.Count / band.Gates;

            // A quarter of the spacing of jitter: enough that two floors do not have their
            // gates at identical bearings, not enough for two gates to end up adjacent.
            var jitter = System.Math.Max(1, spacing / 4);

            // The stagger. Start walking from wherever is furthest from the gates of the band
            // outside this one, so arriving through those gates leaves the most ring to cover.
            var start = FurthestFrom(open, avoid);
            if (avoid.Count == 0)
            {
                start = random.NextInt(0, open.Count);
            }

            for (var g = 0; g < band.Gates; g++)
            {
                var step = open[(start + (g * spacing) + random.NextInt(0, jitter)) % open.Count];

                // Every cell from this band's inner rail through the wall to the band inside.
                // A gate is a short passage, not a doorway in a line — which is what lets
                // MapSketch.Door hang a leaf mid-passage on it.
                for (var t = 0; t <= span; t++)
                {
                    var x = step.X + (step.InX * t);
                    var z = step.Z + (step.InZ * t);
                    Put(s, level, null, occupied, x, z);
                    if (band.Inner - t == wall)
                    {
                        gates.Add(new MapCell(x, z, level));
                    }

                    // The cell where the gate branches off the band is a T-junction, and it
                    // is the only kind of place on a ring floor that has three ways out. §12
                    // wants three 후보 지점 per zone with two exits each, and the gate cells
                    // themselves cannot be marked — they are deliberately mid-passage so a
                    // door can hang on them, and MapSketch refuses a mark on a passage.
                    if (t == 0)
                    {
                        mouths.Add(new MapCell(x, z, level));
                    }
                }
            }

            return gates;
        }

        /// <summary>
        /// Punches one-cell blind alcoves outward from a band, avoiding its gates.
        /// </summary>
        private static List<MapCell> PunchAlcoves(
            MapSketch s,
            int level,
            int cx,
            int cz,
            Band band,
            IRandomSource random,
            List<MapCell> allGates,
            List<MapCell> bandCells,
            HashSet<long> occupied)
        {
            var alcoves = new List<MapCell>();

            // Both rails, in both directions: outward from the outer rail into the wall above
            // the band, and inward from the inner rail into the wall below it. One rail alone
            // measured 15.8% against §12's 20~25% band, because the zigzag spends half its
            // length on the other rail and every bearing it skipped is a bearing with nothing
            // to hang an alcove off.
            Punch(s, level, cx, cz, band.Outer, +1, random, allGates, bandCells, alcoves, occupied);
            Punch(s, level, cx, cz, band.Inner, -1, random, allGates, bandCells, alcoves, occupied);
            return alcoves;
        }

        private static void Punch(
            MapSketch s,
            int level,
            int cx,
            int cz,
            int radius,
            int outward,
            IRandomSource random,
            List<MapCell> allGates,
            List<MapCell> bandCells,
            List<MapCell> into,
            HashSet<long> occupied)
        {
            var ring = Perimeter(cx, cz, radius);

            // Away from corners, away from gates, and never two in a row — a pair of adjacent
            // alcoves would read as a two-cell room and the graph would join them.
            var lastAt = -AlcoveSpacing;
            for (var i = 0; i < ring.Count; i++)
            {
                var step = ring[i];
                if (step.Corner || i - lastAt < AlcoveSpacing)
                {
                    continue;
                }

                var x = step.X - (step.InX * outward);
                var z = step.Z - (step.InZ * outward);

                // Not on a gate, and not beside one either. A gate is a passage and its cells
                // have to stay passages: an alcove touching a gate cell turns it into a
                // junction, and MapSketch.Door refuses to hang a leaf on a junction because
                // there is no single edge for the validator to measure the detour of. The
                // list is every gate cut so far, not just this band's — an alcove punched
                // outward from 중간 lands in the same wall 외곽's gates cross.
                if (allGates.Exists(gate => System.Math.Abs(gate.X - x) + System.Math.Abs(gate.Z - z) <= 1))
                {
                    continue;
                }

                // Only where the band's outer rail was actually drawn. The zigzag spends half
                // its length on the inner rail, and an alcove hung off a bearing the band
                // skipped is an orphan cell — MapSketch refuses the whole map for one.
                // Checking the returned cell list rather than the radius is the point: the
                // radius is right by construction and tells you nothing.
                if (!bandCells.Exists(c => c.X == step.X && c.Z == step.Z))
                {
                    continue;
                }

                // The cell has to become a genuine leaf: empty now, with exactly one occupied
                // neighbour to hang off. Without this check, two alcoves punched from opposite
                // sides of the same wall met in the middle and became a passage — dead ends
                // fell to 12.1% while loops jumped from 17 to 27 and a 25 m straight appeared,
                // all three from the same cause. An alcove that joins something is not an
                // alcove.
                if (occupied.Contains(Key(x, z)) || Neighbours(occupied, x, z) != 1)
                {
                    continue;
                }

                Put(s, level, into, occupied, x, z);
                lastAt = i;
            }
        }

        /// <summary>True when the far end of a gate passage lands on a cell that exists.</summary>
        private static bool Arrives(RingStep step, int span, List<MapCell>? bandBelow)
        {
            if (bandBelow == null)
            {
                // The innermost gate arrives in the 3 x 3 middle, which is solid by
                // construction — there is nothing to miss.
                return true;
            }

            var x = step.X + (step.InX * span);
            var z = step.Z + (step.InZ * span);
            return bandBelow.Exists(c => c.X == x && c.Z == z);
        }

        /// <summary>
        /// Index into <paramref name="open"/> of the cell furthest from every cell in
        /// <paramref name="avoid"/>. Chebyshev, because that is the metric the rings are in.
        /// </summary>
        private static int FurthestFrom(List<RingStep> open, List<MapCell> avoid)
        {
            var best = 0;
            var bestDistance = -1;

            for (var i = 0; i < open.Count; i++)
            {
                var nearest = int.MaxValue;
                for (var j = 0; j < avoid.Count; j++)
                {
                    var d = System.Math.Max(
                        System.Math.Abs(open[i].X - avoid[j].X),
                        System.Math.Abs(open[i].Z - avoid[j].Z));
                    nearest = System.Math.Min(nearest, d);
                }

                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = i;
                }
            }

            return best;
        }

        private static void Put(
            MapSketch s, int level, List<MapCell>? into, HashSet<long> occupied, int x, int z)
        {
            s.Corridor(x, z, x, z);
            occupied.Add(Key(x, z));
            into?.Add(new MapCell(x, z, level));
        }

        /// <summary>Packs a cell into one long so a HashSet can hold it without allocating.</summary>
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>How many of the four orthogonal neighbours are already drawn.</summary>
        private static int Neighbours(HashSet<long> occupied, int x, int z)
        {
            var n = 0;
            if (occupied.Contains(Key(x + 1, z))) { n++; }
            if (occupied.Contains(Key(x - 1, z))) { n++; }
            if (occupied.Contains(Key(x, z + 1))) { n++; }
            if (occupied.Contains(Key(x, z - 1))) { n++; }
            return n;
        }

        /// <summary>
        /// The cells of a square ring at Chebyshev radius <paramref name="radius"/>, walked
        /// once round, each carrying the unit step that moves it one cell toward the middle.
        /// <para>
        /// Chebyshev rather than Euclidean, so a "ring" is a square and every cell of it is
        /// axis-aligned with its neighbours. §12 has no diagonals — the kit docks on grid
        /// edges and a bend is measured from geometry — so a round ring would be a staircase
        /// of one-cell steps and every one of them a bend.
        /// </para>
        /// <para>
        /// The inward step is carried rather than computed because it is only well defined on
        /// a side: at a corner both axes lead inward and neither alone does. Corners are
        /// flagged so callers can leave them alone.
        /// </para>
        /// </summary>
        private static List<RingStep> Perimeter(int cx, int cz, int radius)
        {
            var ring = new List<RingStep>();
            if (radius <= 0)
            {
                ring.Add(new RingStep(cx, cz, 0, 0, true));
                return ring;
            }

            // South side, west to east, inward is +z. Then east, north, west.
            for (var x = cx - radius; x < cx + radius; x++)
            {
                ring.Add(new RingStep(x, cz - radius, 0, 1, x == cx - radius));
            }

            for (var z = cz - radius; z < cz + radius; z++)
            {
                ring.Add(new RingStep(cx + radius, z, -1, 0, z == cz - radius));
            }

            for (var x = cx + radius; x > cx - radius; x--)
            {
                ring.Add(new RingStep(x, cz + radius, 0, -1, x == cx + radius));
            }

            for (var z = cz + radius; z > cz - radius; z--)
            {
                ring.Add(new RingStep(cx - radius, z, 1, 0, z == cz + radius));
            }

            return ring;
        }

        /// <summary>One cell of a square ring, with the direction that leads inward from it.</summary>
        private readonly struct RingStep
        {
            public RingStep(int x, int z, int inX, int inZ, bool corner)
            {
                X = x;
                Z = z;
                InX = inX;
                InZ = inZ;
                Corner = corner;
            }

            /// <summary>Cell X.</summary>
            public int X { get; }

            /// <summary>Cell Z.</summary>
            public int Z { get; }

            /// <summary>Unit step toward the middle, perpendicular to this side.</summary>
            public int InX { get; }

            /// <summary>Unit step toward the middle, perpendicular to this side.</summary>
            public int InZ { get; }

            /// <summary>True at the four corners, where "inward" is diagonal and therefore not a step.</summary>
            public bool Corner { get; }
        }

        private readonly struct Band
        {
            public Band(string name, int inner, int outer, int gates)
            {
                Name = name;
                Inner = inner;
                Outer = outer;
                Gates = gates;
            }

            /// <summary>§12-A's label — 외곽 · 중간 · 안쪽.</summary>
            public string Name { get; }

            /// <summary>Chebyshev radius of the track nearest the middle.</summary>
            public int Inner { get; }

            /// <summary>Chebyshev radius of the track nearest the rim.</summary>
            public int Outer { get; }

            /// <summary>Ways through the wall inside this band. §12-A: 4 · 2 · 1.</summary>
            public int Gates { get; }
        }
    }

    /// <summary>What <see cref="RadialStorey.Build"/> put where, so the caller can hang the rest on it.</summary>
    public sealed class RadialStoreyResult
    {
        /// <summary>Builds an empty result for a storey centred on the given cell.</summary>
        public RadialStoreyResult(int centreX, int centreZ)
        {
            CentreX = centreX;
            CentreZ = centreZ;
            Bands = new List<List<MapCell>>();
            Gates = new List<List<MapCell>>();
            Alcoves = new List<MapCell>();
            GateMouths = new List<MapCell>();
        }

        /// <summary>Cell X of the middle.</summary>
        public int CentreX { get; }

        /// <summary>Cell Z of the middle.</summary>
        public int CentreZ { get; }

        /// <summary>The middle cell itself — where the 투하구 stand.</summary>
        public MapCell Centre { get; set; }

        /// <summary>Cells of each band, innermost first.</summary>
        public List<List<MapCell>> Bands { get; }

        /// <summary>Gate cells of each band, innermost first. Counts are 1 · 2 · 4.</summary>
        public List<List<MapCell>> Gates { get; }

        /// <summary>Blind alcoves — §12's 막힌 길, and the only place on a floor to stand aside.</summary>
        public List<MapCell> Alcoves { get; }

        /// <summary>Where each gate branches off its band — the floor's only three-way junctions.</summary>
        public List<MapCell> GateMouths { get; }
    }
}
