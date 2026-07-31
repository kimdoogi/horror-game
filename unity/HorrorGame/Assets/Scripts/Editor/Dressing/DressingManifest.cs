#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools.Dressing
{
    /// <summary>
    /// The dressing kit as <c>tools/blender/gen_dressing.py</c> measured it.
    /// <para>
    /// Every number about a piece — footprint, height, how far it reaches off the
    /// surface it mounts to, whether it keeps a collider — is read from
    /// <c>Assets/Models/Dressing/Dressing.manifest.json</c> rather than restated
    /// here. That is the same argument <see cref="SceneGen.MapKitCatalogue"/> makes
    /// about the MapKit and it matters more for dressing, because dressing is
    /// placed against a clearance budget: a re-export that makes a shelf 8 cm
    /// deeper has to make the scatterer stop fitting it beside §08's two-person
    /// carry channel, and it only can if it never knew the old figure.
    /// </para>
    /// <para>
    /// Fields are flat scalars because <see cref="JsonUtility"/> cannot deserialise
    /// nested arrays. Names match the generator's keys exactly.
    /// </para>
    /// </summary>
    public static class DressingManifest
    {
        /// <summary>Folder the kit's FBXs and manifest live in.</summary>
        public const string KitRoot = "Assets/Models/Dressing";

        /// <summary>Path of the manifest the Blender generator writes.</summary>
        public const string ManifestPath = KitRoot + "/Dressing.manifest.json";

        /// <summary>Loads and validates the manifest, or returns null with a reason.</summary>
        public static DressingKit? Load(out string error)
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (text == null)
            {
                error = ManifestPath + " is missing. Run:\n"
                    + "  /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup "
                    + "--python tools/blender/gen_dressing.py";
                return null;
            }

            DressingKit kit;
            try
            {
                kit = JsonUtility.FromJson<DressingKit>(text.text);
            }
            catch (Exception ex)
            {
                error = ManifestPath + " could not be parsed: " + ex.Message;
                return null;
            }

            if (kit == null || kit.pieces == null || kit.pieces.Length == 0)
            {
                error = ManifestPath + " parsed but lists no pieces.";
                return null;
            }

            var missing = new List<string>();
            foreach (var piece in kit.pieces)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(KitRoot + "/" + piece.file) == null)
                {
                    missing.Add(piece.file);
                }
            }

            if (missing.Count > 0)
            {
                error = "The manifest names " + missing.Count + " FBX(s) that are not imported: "
                    + string.Join(", ", missing) + ". Re-run gen_dressing.py, or let Unity finish importing.";
                return null;
            }

            error = string.Empty;
            return kit;
        }
    }

    /// <summary>The whole kit.</summary>
    [Serializable]
    public sealed class DressingKit
    {
        /// <summary>Generator that produced this file. Printed with the scatter report.</summary>
        public string generated_by = string.Empty;

        /// <summary>The kit's authoring grid, metres. Must agree with <see cref="SceneGen.MapKitCatalogue.GridMetres"/>.</summary>
        public float grid_metres;

        /// <summary>Figures the generator flagged as not coming from the design document.</summary>
        public DressingAssumptions assumptions = new DressingAssumptions();

        /// <summary>The four §12 zone palettes and what each one is for.</summary>
        public DressingPalette[] palettes = Array.Empty<DressingPalette>();

        /// <summary>Every material the kit authored, with the values Blender used.</summary>
        public DressingMaterial[] materials = Array.Empty<DressingMaterial>();

        /// <summary>Every piece.</summary>
        public DressingPiece[] pieces = Array.Empty<DressingPiece>();
    }

    /// <summary>Figures the generator could not source from the design document.</summary>
    [Serializable]
    public sealed class DressingAssumptions
    {
        /// <summary>Standing eye height, metres. §05 never states one.</summary>
        public float eye_height_metres = 1.63f;

        /// <summary>Corridor clear height from the MapKit manifest, metres.</summary>
        public float ceiling_clear_metres = 3.0f;

        /// <summary>Gap left over a standing player's crown, metres.</summary>
        public float head_clearance_metres = 0.35f;

        /// <summary>Always "NOT_IN_DESIGN_DOC" — kept so the report can say so out loud.</summary>
        public string source = string.Empty;
    }

    /// <summary>A §12 zone's dressing palette.</summary>
    [Serializable]
    public sealed class DressingPalette
    {
        /// <summary>Palette key: storage · institutional · wet · utility.</summary>
        public string name = string.Empty;

        /// <summary>Why this palette belongs to that zone.</summary>
        public string note = string.Empty;
    }

    /// <summary>One authored material, exactly as the Blender Principled BSDF had it.</summary>
    [Serializable]
    public sealed class DressingMaterial
    {
        /// <summary>Material name. This is the seam: the FBX slot carries the same string.</summary>
        public string name = string.Empty;

        /// <summary>Linear base colour.</summary>
        public float r;

        /// <summary>Linear base colour.</summary>
        public float g;

        /// <summary>Linear base colour.</summary>
        public float b;

        /// <summary>Blender roughness. URP wants smoothness, which is 1 − this.</summary>
        public float roughness = 0.7f;

        /// <summary>Metallic.</summary>
        public float metallic;

        /// <summary>Emission strength. Non-zero only on the lit bulb and the flare.</summary>
        public float emission;
    }

    /// <summary>One dressing piece and everything needed to place it.</summary>
    [Serializable]
    public sealed class DressingPiece
    {
        /// <summary>Piece name, matching the FBX and the GameObject the scatterer creates.</summary>
        public string name = string.Empty;

        /// <summary>File name inside <see cref="DressingManifest.KitRoot"/>.</summary>
        public string file = string.Empty;

        /// <summary>Placement family: Bulk · Debris · Decal · Wall · Ceiling · Corner · Sign.</summary>
        public string group = string.Empty;

        /// <summary>Mount convention the pivot follows: FLOOR · WALL · CEILING · CORNER.</summary>
        public string mount = "FLOOR";

        /// <summary>Zone palettes allowed to use this piece. A single "*" means any.</summary>
        public string[] palettes = Array.Empty<string>();

        /// <summary>Relative pick probability inside its group.</summary>
        public float weight = 1f;

        /// <summary>Measured bounding box, metres.</summary>
        public float size_x;

        /// <summary>Measured bounding box, metres.</summary>
        public float size_y;

        /// <summary>Measured bounding box, metres.</summary>
        public float size_z;

        /// <summary>How far the piece reaches off the surface it mounts to, metres.</summary>
        public float mount_depth;

        /// <summary>Whether the piece is tall enough to break a §12 sight line.</summary>
        public bool breaks_sightline;

        /// <summary>Whether the piece keeps a collider and is baked into the NavMesh.</summary>
        public bool solid = true;

        /// <summary>Whether the piece deliberately hangs below head height and must stay out of a route.</summary>
        public bool hangs_low;

        /// <summary>Triangle count, for the scatter report's budget line.</summary>
        public int triangles;

        /// <summary>How many of its materials are emissive.</summary>
        public int emissive_materials;

        /// <summary>Material slot names, in FBX order.</summary>
        public string[] materials = Array.Empty<string>();

        /// <summary>How many <c>Clue_Face</c> islands the piece carries (§03 · §13).</summary>
        public int clue_faces;

        /// <summary>The generator's one-line justification.</summary>
        public string note = string.Empty;

        /// <summary>Blender Y in Unity terms — the horizontal axis the piece's depth runs along.</summary>
        public Vector3 SizeUnity => new Vector3(size_x, size_z, size_y);

        /// <summary>Height in metres, in Unity's Y-up terms.</summary>
        public float Height => size_z;

        /// <summary>Whether this piece may be used in a zone with the given palette.</summary>
        public bool AllowsPalette(string palette)
        {
            for (var i = 0; i < palettes.Length; i++)
            {
                if (palettes[i] == "*" || string.Equals(palettes[i], palette, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
