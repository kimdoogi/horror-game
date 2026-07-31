#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Session;
using HorrorGame.EditorTools.SceneGen;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// One seeded scatter. Holds the placement passes and the running measurements.
    /// <para>
    /// The passes run in a fixed order over a fixed cell order and draw from one
    /// <see cref="DeterministicRandom"/> stream, which is what makes "same seed, same
    /// layout" true rather than approximately true. Nothing here reads Unity's
    /// hierarchy order, <c>System.Random</c> or a clock.
    /// </para>
    /// <para>
    /// Order is also a design decision, not just a determinism one. Services go up
    /// first (ceiling runs, then wall runs), because they are what make a corridor
    /// read as a building and they are the pieces a beam finds at head height.
    /// Bulk cover goes in second, against walls, subject to §08's carry channel.
    /// Debris and water go last and fill in around whatever the first two passes
    /// left, so the floor looks like a consequence of the room rather than a layer
    /// somebody added.
    /// </para>
    /// </summary>
    public sealed class ScatterSession
    {
        private readonly DressingSpace _space;
        private readonly DressingKit _kit;
        private readonly Dictionary<string, Material> _materials;
        private readonly GameObject _root;
        private readonly DressingScatter.CarryEnvelope _carry;
        private readonly IRandomSource _random;
        private readonly Dictionary<string, GameObject> _groups = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _placed = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _byGroup = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _byZone = new Dictionary<string, int>(StringComparer.Ordinal);

        private int _rejected;
        private int _workingBulbs;
        private int _clueSurfaces;
        private int _triangles;

        /// <summary>Creates a session. Nothing is placed until <see cref="Scatter"/> runs.</summary>
        public ScatterSession(DressingSpace space, DressingKit kit, Dictionary<string, Material> materials,
            GameObject root, DressingScatter.CarryEnvelope carry, IRandomSource random)
        {
            _space = space;
            _kit = kit;
            _materials = materials;
            _root = root;
            _carry = carry;
            _random = random;
        }

        /// <summary>How many kept placements violate the carry channel. Must be zero.</summary>
        public int ChannelViolations { get; private set; }

        // ====================================================================
        // Density.
        //
        // These are dressing densities, not gameplay values, so they are not
        // GameConstants' business — nothing in the design document has an opinion
        // about how many crates a basement contains. They are stated as chances per
        // opportunity rather than as counts so the pass scales with the map: a
        // bigger building gets proportionally more, and §12's dead-end ratio keeps
        // meaning what it says.
        //
        // The numbers are set so that "sparse enough to move through, dense enough
        // to look lived in" is achieved by the *services* passes, which cost a
        // player nothing, and the bulk pass stays deliberately thin — every bulk
        // piece is a metre of corridor §08's two-person carry cannot use.
        // ====================================================================

        private const float CeilingRunChance = 0.42f;
        private const float BulbChance = 0.20f;
        private const float HangingExtraChance = 0.15f;
        private const float WallRunChance = 0.40f;
        private const float WallFixtureChance = 0.30f;
        private const float SignChance = 0.15f;
        private const float CobwebChance = 0.55f;
        private const float BulkChance = 0.30f;
        private const float DeadEndBulkChance = 0.80f;
        private const float DebrisChance = 0.32f;
        private const float DecalChance = 0.32f;

        /// <summary>Chance a wall edge collects settled debris at its foot.</summary>
        private const float WallFootChance = 0.30f;

        /// <summary>Extra multiplier on wet dressing inside the palette that owns §03's water clue.</summary>
        private const float WetPaletteBoost = 2.4f;

        /// <summary>
        /// One working bulb in this many placed fittings.
        /// <para>
        /// §03 makes darkness the lock on the objective, so a lit corridor is a design
        /// bug and a corridor of dead fittings is a lie. One in five is enough to give
        /// the building depth and far too few to read a clue by — and the light's range
        /// is one MapKit cell, against the flashlight's
        /// <see cref="GameConstants.FlashlightRange"/> of 12 m.
        /// </para>
        /// </summary>
        private const int WorkingBulbInN = 5;

        /// <summary>
        /// How far a working fitting reaches. Under half
        /// <see cref="GameConstants.FlashlightRange"/>, so §03's beam is always the
        /// longer reach; see <see cref="MaybeLight"/> for why it is not one cell.
        /// </summary>
        private const float PracticalRangeMetres = 5.5f;

        /// <summary>Runs every pass, then re-checks its own work.</summary>
        public void Scatter()
        {
            CeilingPass();
            WallPass();
            CornerPass();
            BulkPass();
            FloorPass();
            Verify();
        }

        /// <summary>
        /// Re-derives every cell's free width from what was actually kept.
        /// <para>
        /// The placement test runs one candidate at a time against the cell's current
        /// occupancy, which is correct only if occupancy is registered in every cell a
        /// footprint touches. That is easy to get subtly wrong, and the failure —
        /// a corridor that two players carrying a 궤짝 cannot pass — would not show up
        /// in a screenshot. So the finished layout is measured again from scratch, and
        /// <see cref="ChannelViolations"/> being anything but zero is a bug in the
        /// placement rule rather than a density that wants tuning.
        /// </para>
        /// </summary>
        private void Verify()
        {
            ChannelViolations = 0;
            foreach (var cell in _space.Cells)
            {
                var info = _space.Info(cell);
                if (info.Occupied.Count == 0)
                {
                    continue;
                }

                _space.WalkableAxes(cell, out var alongX, out var alongZ);
                if (alongX && !GapOk(cell, Rect.zero, acrossX: false))
                {
                    ChannelViolations++;
                    continue;
                }

                if (alongZ && !GapOk(cell, Rect.zero, acrossX: true))
                {
                    ChannelViolations++;
                }
            }
        }

        // ====================================================================
        // Passes.
        // ====================================================================

        /// <summary>
        /// Ceiling services and hanging fittings.
        /// <para>
        /// First because the ceiling is half of every frame in a first-person game
        /// (§05) and the half nobody dresses. A 2.5 m pipe run tiles on the kit's grid,
        /// so consecutive cells produce one continuous run down a corridor rather than
        /// a dotted line.
        /// </para>
        /// </summary>
        private void CeilingPass()
        {
            foreach (var cell in _space.Cells)
            {
                var info = _space.Info(cell);
                if (info.IsDoorway || info.IsStair)
                {
                    continue;
                }

                _space.WalkableAxes(cell, out var alongX, out var alongZ);

                if (_random.Chance(CeilingRunChance))
                {
                    // Aligned with the cell's own axis, so the run reads as going
                    // somewhere. A junction or a hall cell has both axes, and the tie is
                    // broken toward X rather than skipped: a service run that stops at
                    // every junction reads as a decoration that was placed, and the
                    // point of these is that the building is plumbed.
                    var yaw = alongX ? 90f : 0f;
                    Place("Dress_PipeRun_Ceiling", cell, CellCentre(cell), yaw);
                }

                if (_random.Chance(BulbChance))
                {
                    var piece = PickCeilingFitting(info.Palette);
                    if (piece != null)
                    {
                        var jitter = Jitter(0.55f);
                        var placedObject = Place(piece.name, cell, CellCentre(cell) + jitter,
                            _random.NextFloat(0f, 360f));
                        if (placedObject != null && piece.name.StartsWith("Dress_Bulb", StringComparison.Ordinal))
                        {
                            MaybeLight(placedObject, info);
                        }
                    }
                }

                if (_random.Chance(HangingExtraChance))
                {
                    var piece = PickCeilingExtra(info, alongX, alongZ);
                    if (piece != null)
                    {
                        var yaw = piece.name == "Dress_SheetHanging"
                            ? (alongX ? 0f : 90f)
                            : _random.NextFloat(0f, 360f);
                        Place(piece.name, cell, CellCentre(cell) + Jitter(0.45f), yaw);
                    }
                }
            }
        }

        /// <summary>
        /// Wall services, fixtures and §03's signage.
        /// <para>
        /// Every wall edge is an opportunity, and a run is laid along the wall rather
        /// than across it so the 2.5 m piece docks with its neighbours. §04's
        /// observation posts are skipped: those cells exist so the Observer can see
        /// 15 m safely, and a pipe run across the opening would delete the role.
        /// </para>
        /// </summary>
        private void WallPass()
        {
            foreach (var cell in _space.Cells)
            {
                var info = _space.Info(cell);
                if (info.IsDoorway)
                {
                    continue;
                }

                foreach (var direction in MapDirections.All)
                {
                    if (!_space.HasWall(cell, direction))
                    {
                        continue;
                    }

                    var face = _space.WallFace(cell, direction);
                    var into = -DressingSpace.Normal(direction);
                    var yaw = Quaternion.LookRotation(into, Vector3.up).eulerAngles.y;

                    if (!info.IsObservationPost && _random.Chance(WallRunChance))
                    {
                        var run = _random.Chance(0.62f) ? "Dress_PipeRun_Wall" : "Dress_Conduit_Wall";
                        Place(run, cell, face, yaw);
                    }

                    if (_random.Chance(WallFixtureChance))
                    {
                        var piece = PickWallFixture(info.Palette);
                        if (piece != null)
                        {
                            Place(piece.name, cell, face + AlongWall(direction, 0.7f), yaw);
                        }
                    }

                    if (_random.Chance(SignChance))
                    {
                        var piece = PickSign(info);
                        if (piece != null)
                        {
                            var anchor = piece.mount == "CEILING" ? CellCentre(cell) : face;
                            Place(piece.name, cell, anchor + AlongWall(direction, 0.5f), yaw);
                        }
                    }

                    // The wall foot. Dust and rubble settle where the floor meets the
                    // wall and get kicked out of the middle by everything that walks
                    // through, so a corridor with a clean edge reads as swept — which is
                    // the opposite of what §07's building is. Costs nothing: everything
                    // in this pass is stepped over, not walked around.
                    if (_random.Chance(WallFootChance))
                    {
                        var piece = PickWallFoot(info.Palette);
                        if (piece != null)
                        {
                            var inset = -DressingSpace.Normal(direction)
                                        * _random.NextFloat(0.15f, 0.45f);
                            Place(piece.name, cell, face + inset + AlongWall(direction, 0.8f),
                                _random.NextFloat(0f, 360f));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cobwebs, in the corners where two walls and the ceiling meet.
        /// <para>
        /// A corner is the one place a first-person player's beam reliably lands
        /// without being aimed there, and it is also where a corridor's geometry stops
        /// being readable — the wall and the ceiling meet in one dark line. A bright
        /// web is a cheap, diegetic edge highlight.
        /// </para>
        /// </summary>
        private void CornerPass()
        {
            foreach (var cell in _space.Cells)
            {
                var info = _space.Info(cell);
                if (info.IsDoorway || info.IsStair)
                {
                    continue;
                }

                foreach (var pair in CornerPairs)
                {
                    if (!_space.HasWall(cell, pair.Item1) || !_space.HasWall(cell, pair.Item2))
                    {
                        continue;
                    }

                    if (!_random.Chance(CobwebChance))
                    {
                        continue;
                    }

                    var piece = FindPiece("Dress_CobwebCorner");
                    if (piece == null || !piece.AllowsPalette(info.Palette))
                    {
                        continue;
                    }

                    var n1 = DressingSpace.Normal(pair.Item1);
                    var n2 = DressingSpace.Normal(pair.Item2);
                    var half = (MapKitCatalogue.GridMetres * 0.5f) - DressingSpace.WallInset;
                    var centre = cell.Centre;
                    var corner = new Vector3(centre.X, info.CeilingY, centre.Z) + (n1 * half) + (n2 * half);
                    Place(piece.name, cell, corner, CornerYaw(-n1, -n2));
                }
            }
        }

        /// <summary>
        /// Floor bulk, hugging walls, subject to §08's carry channel.
        /// <para>
        /// Deliberately the thinnest pass. Every piece here is cover the Runner can use
        /// (§06 releases aggro on 3 s without line of sight) and simultaneously a metre
        /// of corridor two players carrying a 궤짝 cannot use, so the placement test is
        /// a real gate rather than a formality — <see cref="Fits"/> rejects anything
        /// that would drop a cell below the measured carry width and the piece is
        /// destroyed rather than nudged.
        /// </para>
        /// <para>
        /// Dead ends get three times the density. §12 puts 20~25 % of the map in 막힌
        /// 길 and requires them to carry a 보상; a dead end full of crates is both the
        /// reason to walk in and the thing the loot is hidden behind.
        /// </para>
        /// </summary>
        private void BulkPass()
        {
            foreach (var cell in _space.Cells)
            {
                var info = _space.Info(cell);
                if (info.IsDoorway || info.IsStair || info.IsObservationPost)
                {
                    continue;
                }

                var deadEnd = _space.Degree(cell) <= 1 || info.IsDeadEndCap;
                var chance = deadEnd ? DeadEndBulkChance : BulkChance;
                var attempts = deadEnd ? 3 : 2;

                for (var attempt = 0; attempt < attempts; attempt++)
                {
                    if (!_random.Chance(chance))
                    {
                        continue;
                    }

                    // Bulk hugs a wall, always. A wall-less cell is skipped rather than
                    // filled, and that is a measured decision, not an oversight.
                    //
                    // An "island" version of this pass was written and thrown away: it
                    // placed drums and crates at the corners of wall-less cells so §12's
                    // 개방 공간 would not read as a bare tiled rectangle. It failed
                    // <see cref="Reachability"/> every time it was tried — complete
                    // routes between §12's player spawns and 후보 지점 fell from 9 to 4 —
                    // and it kept failing when the pieces were restricted to footprints
                    // under a metre, and again when it was confined to hall cells alone.
                    // A cell is wall-less because corridors meet there, and the monster's
                    // agent radius needs more of that junction than a drum leaves. The
                    // hall gets its character from services overhead, wall-hugging bulk
                    // around its perimeter and floor debris instead.
                    var walls = MapDirections.All.Where(d => _space.HasWall(cell, d)).ToArray();
                    if (walls.Length == 0)
                    {
                        continue;
                    }

                    var piece = PickBulk(info.Palette);
                    if (piece == null)
                    {
                        continue;
                    }

                    var direction = walls[_random.NextInt(0, walls.Length)];
                    var face = _space.WallFace(cell, direction);
                    var into = -DressingSpace.Normal(direction);
                    var yaw = Quaternion.LookRotation(into, Vector3.up).eulerAngles.y
                              + _random.NextFloat(-7f, 7f);
                    Place(piece.name, cell, face + AlongWall(direction, 0.75f), yaw);
                }
            }
        }

        /// <summary>
        /// Floor debris, paper and water.
        /// <para>
        /// Flat pieces, so they are exempt from the channel test and keep no collider —
        /// walking over a puddle must not feel like stepping onto a kerb. The water
        /// pieces are weighted heavily toward the palette §12's 자갈 zone owns, because
        /// §03's worked clue is "그것은 물이 있는 층에 있다" and a clue that points at
        /// everywhere points at nothing.
        /// </para>
        /// </summary>
        private void FloorPass()
        {
            foreach (var cell in _space.Cells)
            {
                var info = _space.Info(cell);
                if (info.IsDoorway || info.IsStair)
                {
                    continue;
                }

                if (_random.Chance(DebrisChance))
                {
                    var piece = PickGroup("Debris", info.Palette);
                    if (piece != null)
                    {
                        Place(piece.name, cell, CellCentre(cell) + Jitter(0.6f), _random.NextFloat(0f, 360f));
                    }
                }

                var decalChance = DecalChance * (info.Palette == "wet" ? WetPaletteBoost : 1f);
                if (_random.Chance(Mathf.Min(0.85f, decalChance)))
                {
                    var piece = PickGroup("Decal", info.Palette);
                    if (piece != null)
                    {
                        Place(piece.name, cell, CellCentre(cell) + Jitter(0.55f), _random.NextFloat(0f, 360f));
                    }
                }
            }
        }

        // ====================================================================
        // Placement.
        // ====================================================================

        /// <summary>
        /// Instantiates a piece, seats it against its mounting surface and keeps it
        /// only if it leaves the cell passable.
        /// <para>
        /// Seating is done by matching <em>world bounds</em> to the surface rather than
        /// by trusting the FBX origin — the same argument
        /// <see cref="MapSceneBuilder"/> makes about kit pieces, and it matters more
        /// here because a dressing piece that floats 3 cm off a wall is the exact kind
        /// of mistake that reads as "cheap" without anyone being able to say why.
        /// </para>
        /// </summary>
        private GameObject? Place(string pieceName, CellKey cell, Vector3 anchor, float yaw)
        {
            var piece = FindPiece(pieceName);
            if (piece == null)
            {
                return null;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DressingManifest.KitRoot + "/" + piece.file);
            if (asset == null)
            {
                return null;
            }

            var parent = GroupFor(_space.Info(cell).ZoneName);
            var instance = PrefabUtility.InstantiatePrefab(asset, parent.transform) as GameObject;
            if (instance == null)
            {
                return null;
            }

            instance.name = piece.name + "_" + cell.X + "_" + cell.Z;
            instance.transform.SetPositionAndRotation(anchor, DressingImport.Rotation(yaw));

            if (!TryBounds(instance, out var bounds))
            {
                UnityEngine.Object.DestroyImmediate(instance);
                return null;
            }

            var info = _space.Info(cell);
            Seat(instance, piece, ref bounds, anchor, yaw, info);

            var footprint = new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);
            if (Blocks(piece, bounds, info) && !Fits(cell, footprint))
            {
                UnityEngine.Object.DestroyImmediate(instance);
                _rejected++;
                return null;
            }

            if (Blocks(piece, bounds, info))
            {
                Claim(footprint, cell.Level);
            }

            Finish(instance, piece);

            _placed[piece.name] = _placed.TryGetValue(piece.name, out var n) ? n + 1 : 1;
            _byGroup[piece.group] = _byGroup.TryGetValue(piece.group, out var g) ? g + 1 : 1;
            _byZone[info.ZoneName] = _byZone.TryGetValue(info.ZoneName, out var z) ? z + 1 : 1;
            _clueSurfaces += piece.clue_faces;
            _triangles += piece.triangles;
            return instance;
        }

        /// <summary>Pushes a placed piece flush against the surface its pivot convention names.</summary>
        private void Seat(GameObject instance, DressingPiece piece, ref Bounds bounds, Vector3 anchor, float yaw,
            DressingSpace.CellInfo info)
        {
            switch (piece.mount)
            {
                case "WALL":
                {
                    // The piece's authored front is Blender −Y, which becomes +Z in the
                    // corrected frame — so "into the room" is the yaw applied to +Z.
                    // Derived from the yaw rather than read off `transform.forward`,
                    // because the upright correction rotates the object's own axes and
                    // `forward` then points at the ceiling.
                    var into = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    var minAlong = Vector3.Dot(bounds.center, into)
                                   - Mathf.Abs(Vector3.Dot(bounds.extents, Abs(into)));
                    instance.transform.position += into * (Vector3.Dot(anchor, into) - minAlong);
                    instance.transform.position = new Vector3(instance.transform.position.x, info.FloorY,
                        instance.transform.position.z);
                    break;
                }

                case "CEILING":
                {
                    instance.transform.position += Vector3.up * (info.CeilingY - bounds.max.y);
                    break;
                }

                case "CORNER":
                {
                    instance.transform.position += Vector3.up * (info.CeilingY - bounds.max.y);
                    break;
                }

                default:
                {
                    instance.transform.position += Vector3.up * (info.FloorY - bounds.min.y);
                    break;
                }
            }

            TryBounds(instance, out bounds);
        }

        /// <summary>
        /// Whether a piece eats into the volume §08's two carried pieces sweep.
        /// <para>
        /// A pipe run at 2 m does not: a portrait is 1.63 m tall and passes under it.
        /// A wall stain that reaches the floor does. Deciding this from the measured
        /// bounds rather than from the piece's category is what lets a ceiling piece be
        /// generous and a floor piece be strict without a table of exceptions.
        /// </para>
        /// </summary>
        private bool Blocks(DressingPiece piece, Bounds bounds, DressingSpace.CellInfo info)
        {
            // Debris and decals are stepped over, not walked around. The generator
            // guarantees the height that makes that true — `gen_dressing.py` fails its
            // own build if any Debris piece passes 0.50 m, on §05/§06 grounds: backward
            // movement is 65 % speed against a 4.8 m/s monster, so anything a fleeing
            // player could trip on is a death written by set dressing. Trusting the
            // group here is trusting that assertion, not a guess.
            if (string.Equals(piece.group, "Decal", StringComparison.Ordinal)
                || string.Equals(piece.group, "Debris", StringComparison.Ordinal))
            {
                return false;
            }

            return bounds.min.y < info.FloorY + _carry.Height;
        }

        /// <summary>
        /// Whether a cell still passes §08's two-person carry after this footprint.
        /// <para>
        /// The test is the largest free gap across the cell on each axis a player can
        /// actually walk, inside the band <see cref="DressingSpace.Band"/> reports —
        /// which is the corridor's clear section where there is a wall and the whole
        /// cell where the neighbour is open. Junctions and hall cells therefore pass
        /// wider footprints than corridors do without the caller knowing which it is.
        /// </para>
        /// </summary>
        private bool Fits(CellKey cell, Rect footprint)
        {
            foreach (var touched in CellsUnder(footprint, cell.Level))
            {
                if (!_space.IsWalkable(touched))
                {
                    continue;
                }

                _space.WalkableAxes(touched, out var alongX, out var alongZ);
                if (alongX && !GapOk(touched, footprint, acrossX: false))
                {
                    return false;
                }

                if (alongZ && !GapOk(touched, footprint, acrossX: true))
                {
                    return false;
                }
            }

            return true;
        }

        private bool GapOk(CellKey cell, Rect candidate, bool acrossX)
        {
            _space.Band(cell, acrossX, out var bandMin, out var bandMax);

            var spans = new List<Vector2>();
            void Add(Rect r)
            {
                // An empty rect is the "measure what is already here" case Verify() uses.
                if (r.width <= 0f || r.height <= 0f)
                {
                    return;
                }

                var lo = acrossX ? r.xMin : r.yMin;
                var hi = acrossX ? r.xMax : r.yMax;
                if (hi > bandMin && lo < bandMax)
                {
                    spans.Add(new Vector2(Mathf.Max(lo, bandMin), Mathf.Min(hi, bandMax)));
                }
            }

            foreach (var r in _space.Info(cell).Occupied)
            {
                Add(r);
            }

            Add(candidate);

            spans.Sort((a, b) => a.x.CompareTo(b.x));
            var cursor = bandMin;
            var best = 0f;
            foreach (var span in spans)
            {
                best = Mathf.Max(best, span.x - cursor);
                cursor = Mathf.Max(cursor, span.y);
            }

            best = Mathf.Max(best, bandMax - cursor);
            return best >= _carry.Width;
        }

        private void Claim(Rect footprint, int level)
        {
            foreach (var cell in CellsUnder(footprint, level))
            {
                if (_space.IsWalkable(cell))
                {
                    _space.Info(cell).Occupied.Add(footprint);
                }
            }
        }

        private static IEnumerable<CellKey> CellsUnder(Rect footprint, int level)
        {
            var grid = MapKitCatalogue.GridMetres;
            var x0 = Mathf.FloorToInt(footprint.xMin / grid);
            var x1 = Mathf.FloorToInt((footprint.xMax - 0.001f) / grid);
            var z0 = Mathf.FloorToInt(footprint.yMin / grid);
            var z1 = Mathf.FloorToInt((footprint.yMax - 0.001f) / grid);
            for (var x = x0; x <= x1; x++)
            {
                for (var z = z0; z <= z1; z++)
                {
                    yield return new CellKey(new MapCell(x, z), level);
                }
            }
        }

        /// <summary>
        /// Gives a placed piece its collider, NavMesh contribution and materials.
        /// <para>
        /// The import policy gives every FBX under <c>Assets/Models</c> a convex mesh
        /// collider, which is right for a crate and wrong for a puddle — a 1 cm slab of
        /// physics on the floor is a step a player can feel. It is also wrong for a
        /// ceiling pipe: the NavMesh bake collects render meshes, so a horizontal run
        /// at 2.6 m would generate a walkable island in mid-air and the monster's
        /// pathfinder would have opinions about it. So the manifest's <c>solid</c> flag
        /// decides both, and the piece the generator marked non-solid loses its
        /// collider and is removed from the bake.
        /// </para>
        /// </summary>
        private void Finish(GameObject instance, DressingPiece piece)
        {
            DressingMaterials.Apply(instance, _materials);

            if (!piece.solid)
            {
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }
            }

            var modifier = instance.GetComponent<NavMeshModifier>();
            if (modifier == null)
            {
                modifier = instance.AddComponent<NavMeshModifier>();
            }

            modifier.overrideArea = false;
            modifier.ignoreFromBuild = !piece.solid;

            GameObjectUtility.SetStaticEditorFlags(instance,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic);
        }

        /// <summary>
        /// Gives a minority of bare bulbs a real light.
        /// <para>
        /// §03 makes darkness the lock on the whole objective, so this is the one place
        /// the dressing pass can do real damage. Only one fitting in
        /// <see cref="WorkingBulbInN"/> gets a light — 16 across 2469 m², about one per
        /// 150 m² — so the building still has to be walked with a torch, and the range
        /// stays under half the flashlight's
        /// <see cref="GameConstants.FlashlightRange"/> so a bulb never reaches as far
        /// as the player's own beam.
        /// </para>
        /// <para>
        /// The range was one MapKit cell, 2.5 m, and that was too literal a reading of
        /// the rule. Hung at a 3 m ceiling it put a 0.5 m disc on the floor directly
        /// underneath and was invisible from anywhere else, so the paragraph below —
        /// the actual reason these exist — was not true of the shipped scene: the
        /// corridors had no far end, and the review shots showed 16 bulbs that lit
        /// nothing at all. 5.5 m reaches the floor and stays visible from down a
        /// corridor, which is the whole job.
        /// </para>
        /// <para>
        /// It is not decoration. A pitch-black corridor with a beam in it has no depth
        /// cues at all — §05 puts the player in a 22° cone and everything outside it is
        /// a flat black field. A handful of warm points at ceiling height is what turns
        /// that into a corridor with a far end.
        /// </para>
        /// </summary>
        private void MaybeLight(GameObject bulb, DressingSpace.CellInfo info)
        {
            if (_random.NextInt(0, WorkingBulbInN) != 0)
            {
                return;
            }

            // Both bulb props are authored with dead glass, and the emissive material is
            // swapped in here. That keeps §03's rule — a lit fitting is the exception —
            // in one place instead of in the geometry of two FBXs, where the first
            // version of this kit got it wrong: every caged bulb in the building glowed.
            SwapMaterial(bulb, "Dress_BulbDead", "Dress_BulbLit");

            var go = new GameObject("Filament");
            go.transform.SetParent(bulb.transform, false);
            if (TryBounds(bulb, out var bounds))
            {
                go.transform.position = new Vector3(bounds.center.x, bounds.min.y + 0.05f, bounds.center.z);
            }

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = PracticalRangeMetres;
            light.intensity = 1.1f;
            light.color = PracticalColour(info.Palette);
            light.shadows = LightShadows.None;
            light.bounceIntensity = 0f;
            _workingBulbs++;
        }

        /// <summary>
        /// The colour of a working fitting, by §12 zone palette.
        /// <para>
        /// §12 makes the floor the 청음사's map — "구역별로 바닥 재질이 달라야 청음사가
        /// 위치를 판별할 수 있다" — and the floor does now read. But the floor is only
        /// under the beam, and every zone is otherwise the same cold blue box with the
        /// same brick walls, so a still frame taken above waist height does not say
        /// where it was taken. The zones need a second cue that survives at distance,
        /// and light colour is the one that costs nothing: a warm sodium glow at the
        /// end of a corridor is a different place from a green fluorescent one before
        /// you can see what either is attached to.
        /// </para>
        /// <para>
        /// Kept narrow on purpose. These are a few degrees apart, not a rainbow — the
        /// building is one building, and a zone lit in a flatly different hue reads as
        /// a menu screen rather than as a basement.
        /// </para>
        /// </summary>
        private static Color PracticalColour(string palette)
        {
            switch (palette)
            {
                // Old storage wing: bare tungsten, the warmest thing in the building.
                case "storage": return new Color(1f, 0.79f, 0.50f);

                // Institutional hall: failing fluorescent, faintly green.
                case "institutional": return new Color(0.86f, 0.95f, 0.88f);

                // The flooded end: whatever still works down there is cold and damp.
                case "wet": return new Color(0.72f, 0.84f, 1f);

                // Plant and services: inspection lamps, hard and slightly yellow.
                default: return new Color(1f, 0.90f, 0.72f);
            }
        }

        /// <summary>Replaces one named material slot on an instance with another from the kit.</summary>
        private void SwapMaterial(GameObject instance, string from, string to)
        {
            if (!_materials.TryGetValue(to, out var replacement) || replacement == null)
            {
                return;
            }

            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var shared = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < shared.Length; i++)
                {
                    if (shared[i] != null && string.Equals(shared[i].name, from, StringComparison.Ordinal))
                    {
                        shared[i] = replacement;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }
        }

        // ====================================================================
        // Picking.
        // ====================================================================

        private DressingPiece? FindPiece(string name)
        {
            foreach (var piece in _kit.pieces)
            {
                if (string.Equals(piece.name, name, StringComparison.Ordinal))
                {
                    return piece;
                }
            }

            return null;
        }

        private DressingPiece? PickGroup(string group, string palette)
        {
            var pool = _kit.pieces
                .Where(p => string.Equals(p.group, group, StringComparison.Ordinal) && p.AllowsPalette(palette))
                .ToArray();
            return WeightedPick(pool);
        }

        private DressingPiece? PickBulk(string palette) => PickGroup("Bulk", palette);

        /// <summary>
        /// The small, low pieces that collect against a wall.
        /// <para>
        /// Restricted to the two groups the generator guarantees are stepped over, and
        /// further to the ones small enough that a line of them along a wall reads as
        /// accumulation rather than as a barricade.
        /// </para>
        /// </summary>
        private DressingPiece? PickWallFoot(string palette)
        {
            var pool = _kit.pieces
                .Where(p => (string.Equals(p.group, "Debris", StringComparison.Ordinal)
                             || string.Equals(p.group, "Decal", StringComparison.Ordinal))
                            && p.size_x < 1.3f && p.AllowsPalette(palette))
                .ToArray();
            return WeightedPick(pool);
        }


        private DressingPiece? PickWallFixture(string palette)
        {
            var pool = _kit.pieces
                .Where(p => string.Equals(p.group, "Wall", StringComparison.Ordinal)
                            && !p.name.Contains("Run") && !p.name.Contains("Conduit")
                            && p.AllowsPalette(palette))
                .ToArray();
            return WeightedPick(pool);
        }

        private DressingPiece? PickCeilingFitting(string palette)
        {
            var pool = _kit.pieces
                .Where(p => p.name.StartsWith("Dress_Bulb", StringComparison.Ordinal) && p.AllowsPalette(palette))
                .ToArray();
            return WeightedPick(pool);
        }

        private DressingPiece? PickCeilingExtra(DressingSpace.CellInfo info, bool alongX, bool alongZ)
        {
            var pool = _kit.pieces
                .Where(p => string.Equals(p.group, "Ceiling", StringComparison.Ordinal)
                            && !p.name.StartsWith("Dress_Bulb", StringComparison.Ordinal)
                            && !p.name.Contains("Run")
                            && p.AllowsPalette(info.Palette))
                // A sheet hangs to 1.03 m, which is below head height on purpose (§06:
                // 3 s without line of sight releases aggro). It belongs against a wall
                // in a dead end or a hall edge, never slung across a through route a
                // Runner takes at 5.6 m/s.
                .Where(p => !p.hangs_low || _space.Degree(info.Cell) <= 1 || info.IsHall)
                .ToArray();
            return WeightedPick(pool);
        }

        private DressingPiece? PickSign(DressingSpace.CellInfo info)
        {
            var pool = _kit.pieces
                .Where(p => string.Equals(p.group, "Sign", StringComparison.Ordinal)
                            && p.AllowsPalette(info.Palette))
                .ToArray();
            return WeightedPick(pool);
        }

        private DressingPiece? WeightedPick(IReadOnlyList<DressingPiece> pool)
        {
            if (pool.Count == 0)
            {
                return null;
            }

            var total = 0f;
            for (var i = 0; i < pool.Count; i++)
            {
                total += Mathf.Max(0.0001f, pool[i].weight);
            }

            var roll = _random.NextFloat(0f, total);
            for (var i = 0; i < pool.Count; i++)
            {
                roll -= Mathf.Max(0.0001f, pool[i].weight);
                if (roll <= 0f)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }

        // ====================================================================
        // Geometry helpers.
        // ====================================================================

        private static readonly (MapDirection, MapDirection)[] CornerPairs =
        {
            (MapDirection.North, MapDirection.East),
            (MapDirection.East, MapDirection.South),
            (MapDirection.South, MapDirection.West),
            (MapDirection.West, MapDirection.North),
        };

        private Vector3 CellCentre(CellKey cell)
        {
            var centre = cell.Centre;
            return new Vector3(centre.X, _space.Info(cell).FloorY, centre.Z);
        }

        private Vector3 Jitter(float radius) =>
            new Vector3(_random.NextFloat(-radius, radius), 0f, _random.NextFloat(-radius, radius));

        private Vector3 AlongWall(MapDirection direction, float radius)
        {
            var tangent = direction == MapDirection.East || direction == MapDirection.West
                ? Vector3.forward
                : Vector3.right;
            return tangent * _random.NextFloat(-radius, radius);
        }

        /// <summary>
        /// Yaw that turns a corner piece so its two open faces point away from the two
        /// walls forming the corner.
        /// <para>
        /// Solved by trying the four grid yaws rather than derived, because the piece's
        /// occupied octant survives two axis conversions on the way out of Blender and a
        /// sign error would put every cobweb inside a wall — visibly wrong, and
        /// invisible in a diff.
        /// </para>
        /// </summary>
        private static float CornerYaw(Vector3 open1, Vector3 open2)
        {
            var best = 0f;
            var bestScore = float.NegativeInfinity;
            for (var i = 0; i < 4; i++)
            {
                var yaw = i * 90f;
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                // The piece is authored occupying Blender −X / −Y / −Z, which arrives in
                // Unity as local −X and local +Z.
                var a = rotation * Vector3.left;
                var b = rotation * Vector3.forward;
                var score = Mathf.Max(
                    Vector3.Dot(a, open1) + Vector3.Dot(b, open2),
                    Vector3.Dot(a, open2) + Vector3.Dot(b, open1));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = yaw;
                }
            }

            return best;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private GameObject GroupFor(string zoneName)
        {
            // "Dressing_" rather than "Zone_": SceneShot picks its per-zone camera
            // positions by scanning for objects whose name starts with Zone_, and a
            // second set would silently replace the shots that show what a zone looks
            // like.
            var name = "Dressing_" + zoneName.Replace(MapSceneBuilder.ZonePrefix, string.Empty);
            if (_groups.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            _groups[name] = go;
            return go;
        }

        // ====================================================================
        // Report.
        // ====================================================================

        /// <summary>Describes what was placed, in the terms the design cares about.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            var total = _placed.Values.Sum();
            var cells = _space.Cells.Count;
            var area = cells * MapKitCatalogue.GridMetres * MapKitCatalogue.GridMetres;

            text.Append("Import orientation: ")
                .Append(DressingImport.BlenderZUp ? "Blender Z-up, corrected by the scatterer" : "Unity Y-up")
                .Append('\n');
            text.Append("Placed ").Append(total).Append(" pieces of ").Append(_placed.Count)
                .Append(" kinds across ").Append(cells).Append(" walkable cells (")
                .Append(area.ToString("0")).Append(" m²) — ")
                .Append((total / Mathf.Max(1f, area) * 100f).ToString("0.0"))
                .Append(" per 100 m². ").Append(_rejected)
                .Append(" placements were rejected for narrowing §08's carry channel.\n");

            text.Append("  by group: ");
            foreach (var pair in _byGroup.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                text.Append(pair.Key).Append('=').Append(pair.Value).Append("  ");
            }

            text.Append('\n');

            text.Append("  by zone:  ");
            foreach (var pair in _byZone.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                text.Append(pair.Key).Append('=').Append(pair.Value).Append("  ");
            }

            text.Append('\n');
            text.Append("  §03: ").Append(_clueSurfaces)
                .Append(" Clue_Face surfaces placed (the host stamps a glyph on one at a time, §13); ")
                .Append(_workingBulbs).Append(" of the placed fittings still work, ranged at ")
                .Append(PracticalRangeMetres.ToString("0.0")).Append(" m against the flashlight's ")
                .Append(GameConstants.FlashlightRange).Append(" m.\n");
            text.Append("  cost: ").Append(_triangles).Append(" triangles of unique dressing geometry.\n");
            return text.ToString();
        }
    }
}
