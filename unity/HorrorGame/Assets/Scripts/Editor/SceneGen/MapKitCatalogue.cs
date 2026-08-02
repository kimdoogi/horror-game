using System;
using HorrorGame.Core.Map;

namespace HorrorGame.EditorTools.SceneGen
{
    /// <summary>
    /// The MapKit pieces, as an enum the generator can switch on.
    /// <para>
    /// §12 opens with "맵은 아트가 아니라 시스템이다", so the kit is treated as a
    /// vocabulary rather than a set of props: the generator decides <em>which</em>
    /// piece a place needs from the graph's own topology (degree, turn, dead end),
    /// and the artist decides what that piece looks like. Renaming a piece therefore
    /// breaks the build loudly here instead of producing a scene with holes in it.
    /// </para>
    /// </summary>
    public enum MapKitPiece
    {
        /// <summary>Grid-unit filler and pass-through cell.</summary>
        CorridorStraight2m5,

        /// <summary>§12 straight section, 5 m.</summary>
        CorridorStraight5m,

        /// <summary>§12 straight section, 10 m. Longest straight in the kit — half §12's 20 m cap.</summary>
        CorridorStraight10m,

        /// <summary>90° turn. The kit's own note: "Composes the S-corridor by hand."</summary>
        CorridorCornerL,

        /// <summary>Pre-composed §12 기본 단위. See <see cref="MapKitCatalogue.SCorridorUnitUsable"/>.</summary>
        SCorridorUnit10mX2,

        /// <summary>§12 분기 / loop feeder.</summary>
        JunctionT,

        /// <summary>§12 순환로 crossing.</summary>
        JunctionCross4Way,

        /// <summary>
        /// §12-A's 중심 — the 7.5 m room a radial storey's 투하구 stand in, open at the
        /// middle of all four edges.
        /// <para>
        /// A room rather than nine corridor cells, and B-010 is why: the kit tiles a cell
        /// from its neighbour mask, so a filled 3 x 3 becomes nine 2.5 m passages meeting
        /// at their own walls, and the NavMesh audit finds it as a sealed island. On B8
        /// that middle is §02's finish.
        /// </para>
        /// </summary>
        ChamberOpen3x3,

        /// <summary>§12 막힌 길 — plinth and alcove built in, which is where the 보상 goes.</summary>
        DeadEndCap,

        /// <summary>§12 개방 공간, 20 m sight line.</summary>
        HallOpen20x20,

        /// <summary>§12 계단 = 금속, the loudest surface in the building (§04 청음사).</summary>
        StairwellMetal,

        /// <summary>§04 관측자: a 2층 난간 at +3 m, 14.7 m horizontal = 15 m slant.</summary>
        ObservationPostGallery,

        /// <summary>§12 창문 · 격자 variant of the same requirement.</summary>
        ObservationPostBarredWindow,

        /// <summary>§12 병목 — the opening a lockable door hangs in.</summary>
        DoorwayFrame,

        /// <summary>PROP. Two leaves fill one <see cref="DoorwayFrame"/>. §04 정비공.</summary>
        DoorPanelLockable,

        /// <summary>PROP. §12 전기 패널 구역당 1개 — §03's "어둠 = 목표의 잠금장치" needs a key.</summary>
        WallPanelElectrical,

        /// <summary>§12 구역 A — 나무, 삐걱.</summary>
        FloorTileWood,

        /// <summary>§12 구역 B — 타일, 딱딱 · 반향.</summary>
        FloorTileTile,

        /// <summary>§12 구역 C — 자갈, 부스럭.</summary>
        FloorTileGravel,

        /// <summary>§12 구역 D — 콘크리트, 둔탁.</summary>
        FloorTileConcrete,

        /// <summary>§12 계단 — 금속, 울림.</summary>
        FloorTileMetal,

        /// <summary>§12 "재질 경계를 명확히 할 것" — the recessed channel between two zones' floors.</summary>
        FloorBoundarySplit,
    }

    /// <summary>
    /// Facts about the MapKit that the generator needs and that live in the asset,
    /// not in the design document.
    /// <para>
    /// None of these are tuned game values, so none of them belong in
    /// <see cref="GameConstants"/>: they are measurements of
    /// <c>Assets/Models/MapKit/MapKit.manifest.json</c>. The generator re-reads that
    /// manifest and fails if these ever disagree, because a kit re-export that moved
    /// the grid would otherwise produce a scene full of 1.25 m seams that still
    /// validates — the graph would be right and the geometry wrong.
    /// </para>
    /// </summary>
    public static class MapKitCatalogue
    {
        /// <summary><c>grid_metres</c> in the kit manifest. Every tile snaps to this.</summary>
        public const float GridMetres = 2.5f;

        /// <summary>
        /// <c>storey_metres</c> in the kit manifest — how far one floor sits below the
        /// one above it.
        /// <para>
        /// Not a tuned number and not a choice: it is what
        /// <see cref="MapKitPiece.StairwellMetal"/> was modelled to climb. That piece
        /// carries two docks on the same edge, one at local height 0 and one at 3.75,
        /// so a storey of any other height would leave the upper flight opening into a
        /// wall. §03's clue chain narrows by floor ("물이 있는 층은 지하 3층이다"), which
        /// only means something if the floors are separated by a stair that fits.
        /// </para>
        /// </summary>
        public const float StoreyMetres = 3.75f;

        /// <summary>
        /// World Y of a storey's floor. Level 0 is the 출입구 floor and larger indices
        /// go down, so the sign matches §03's 지하 N층 wording.
        /// </summary>
        public static float FloorY(int level) => -level * StoreyMetres;

        /// <summary><c>corridor_clear.width</c>. Corridor centre lines sit at the middle of a cell.</summary>
        public const float CorridorClearWidth = 2.2f;

        /// <summary>
        /// <c>corridor_clear.height</c> — floor to soffit inside a corridor, metres.
        /// <para>
        /// This is where a storey stops being somewhere you can stand and starts being
        /// structure. The NavMesh bake needs the distinction: every kit piece is one
        /// merged mesh whose ceiling slab is as horizontal and as walkable-looking as
        /// its floor, so a bake that is not told where the storey ends puts a second
        /// navigation surface on the roof of the building. See
        /// <see cref="MapSceneBuilder"/>'s bake volume.
        /// </para>
        /// </summary>
        public const float CorridorClearHeight = 3.0f;

        /// <summary><c>gallery_rise_metres</c> — how high above the floor an <see cref="MapKitPiece.ObservationPostGallery"/> sits.</summary>
        public const float GalleryRiseMetres = 3.0f;

        /// <summary>Side of <see cref="MapKitPiece.HallOpen20x20"/>, metres.</summary>
        public const float HallSideMetres = 20f;

        /// <summary>Side of a floor tile, metres — two grid cells.</summary>
        public const float FloorTileMetres = 5f;

        /// <summary>
        /// Whether the pre-composed S unit can carry a §12-qualifying S자 통로.
        /// <para>
        /// It cannot, and the generator therefore builds every S-corridor from
        /// <see cref="MapKitPiece.CorridorCornerL"/> plus straights, which is what
        /// that piece's own manifest note prescribes. Two independent reasons, both
        /// checkable against the manifest:
        /// </para>
        /// <para>
        /// 1. The unit is 5 × 17.5 m with docks at local (1.25, 0) and (3.75, 17.5)
        /// and <c>max_straight_run_metres</c> 10, so its bend falls 10 m along a
        /// 17.5 m body — on a cell <em>boundary</em>, 1.25 m off the cell centres
        /// every other piece in the kit docks on. Dropping it into the tile grid
        /// would offset its 2.2 m corridor by more than half its own width.
        /// </para>
        /// <para>
        /// 2. Its second leg is 17.5 − 10 = 7.5 m, under
        /// <see cref="GameConstants.SCorridorLegLength"/>, so
        /// <see cref="MapGraph.FindSCorridor"/> would not count it however it were
        /// placed: §12 asks for "10m 구간 2개" and this is 10 m + 7.5 m.
        /// </para>
        /// </summary>
        public const bool SCorridorUnitUsable = false;

        /// <summary>Asset path of a piece's FBX, for <c>AssetDatabase</c> lookups.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Unknown piece.</exception>
        public static string AssetPath(MapKitPiece piece) =>
            "Assets/Models/MapKit/" + FileName(piece) + ".fbx";

        /// <summary>File name without extension, matching the kit on disk.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Unknown piece.</exception>
        public static string FileName(MapKitPiece piece)
        {
            switch (piece)
            {
                case MapKitPiece.CorridorStraight2m5: return "Corridor_Straight_2m5";
                case MapKitPiece.CorridorStraight5m: return "Corridor_Straight_5m";
                case MapKitPiece.CorridorStraight10m: return "Corridor_Straight_10m";
                case MapKitPiece.CorridorCornerL: return "Corridor_Corner_L";
                case MapKitPiece.ChamberOpen3x3: return "Chamber_Open_3x3";
                case MapKitPiece.SCorridorUnit10mX2: return "SCorridor_Unit_10m_x2";
                case MapKitPiece.JunctionT: return "Junction_T";
                case MapKitPiece.JunctionCross4Way: return "Junction_Cross_4Way";
                case MapKitPiece.DeadEndCap: return "DeadEnd_Cap";
                case MapKitPiece.HallOpen20x20: return "Hall_Open_20x20";
                case MapKitPiece.StairwellMetal: return "Stairwell_Metal";
                case MapKitPiece.ObservationPostGallery: return "ObservationPost_Gallery";
                case MapKitPiece.ObservationPostBarredWindow: return "ObservationPost_BarredWindow";
                case MapKitPiece.DoorwayFrame: return "Doorway_Frame";
                case MapKitPiece.DoorPanelLockable: return "Door_Panel_Lockable";
                case MapKitPiece.WallPanelElectrical: return "WallPanel_Electrical";
                case MapKitPiece.FloorTileWood: return "FloorTile_Wood";
                case MapKitPiece.FloorTileTile: return "FloorTile_Tile";
                case MapKitPiece.FloorTileGravel: return "FloorTile_Gravel";
                case MapKitPiece.FloorTileConcrete: return "FloorTile_Concrete";
                case MapKitPiece.FloorTileMetal: return "FloorTile_Metal";
                case MapKitPiece.FloorBoundarySplit: return "FloorBoundary_Split";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(piece), piece, "No MapKit file is mapped to this piece.");
            }
        }

        /// <summary>
        /// A piece's own height, metres — <c>height_metres</c> in the kit manifest.
        /// <para>
        /// Needed because two pieces are taller than a storey. §12's 20 m 개방 공간 is
        /// 6.3 m to the roof against a <see cref="StoreyMetres"/> of 3.75, so a hall on
        /// B2 pushes its roof 2.55 m up into B1 and leaves any corridor over it 1.99 m
        /// of headroom — under the agent's own height, so §06's monster cannot go
        /// there and the bake, correctly, does not pretend it can. Nothing else in the
        /// project can see that: §12's checklist is per-storey and horizontal.
        /// </para>
        /// </summary>
        public static float HeightMetres(MapKitPiece piece)
        {
            switch (piece)
            {
                case MapKitPiece.HallOpen20x20: return 6.3f;
                case MapKitPiece.StairwellMetal: return 7.05f;
                case MapKitPiece.FloorTileWood:
                case MapKitPiece.FloorTileTile:
                case MapKitPiece.FloorTileGravel:
                case MapKitPiece.FloorTileConcrete:
                case MapKitPiece.FloorTileMetal:
                case MapKitPiece.FloorBoundarySplit: return 0.154f;
                case MapKitPiece.DoorPanelLockable: return 2.4f;
                case MapKitPiece.WallPanelElectrical: return 1.9f;
                default: return 3.3f;
            }
        }

        /// <summary>
        /// The floor tile that sounds like a zone's surface. §12 청음사: "구역별로
        /// 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다."
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A surface with no tile in the kit.</exception>
        public static MapKitPiece FloorTileFor(FloorMaterial material)
        {
            switch (material)
            {
                case FloorMaterial.Wood: return MapKitPiece.FloorTileWood;
                case FloorMaterial.Tile: return MapKitPiece.FloorTileTile;
                case FloorMaterial.Gravel: return MapKitPiece.FloorTileGravel;
                case FloorMaterial.Concrete: return MapKitPiece.FloorTileConcrete;
                case FloorMaterial.Metal: return MapKitPiece.FloorTileMetal;
                // The three new surfaces reuse the closest existing tile MESH — a floor
                // tile is a flat quad and its material is what a player reads — so the
                // kit does not grow and the §04 channel does.
                case FloorMaterial.Water: return MapKitPiece.FloorTileTile;
                case FloorMaterial.Earth: return MapKitPiece.FloorTileGravel;
                case FloorMaterial.Carpet: return MapKitPiece.FloorTileWood;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(material), material,
                        "§12 gives every zone a surface; None would leave the Listener a silent zone.");
            }
        }
    }
}
