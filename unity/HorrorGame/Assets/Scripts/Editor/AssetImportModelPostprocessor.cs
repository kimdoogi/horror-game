using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Applies <see cref="AssetImportPolicy"/> to every FBX under <c>Assets/Models</c> as it
    /// imports.
    /// <para>
    /// The setting that matters most here is scale, and it is worth being explicit about why.
    /// §12 opens with "맵은 아트가 아니라 시스템이다" and then derives every dimension from a
    /// numbered rule: a 2.5 m grid, a 3.75 m storey, a 2.2 × 3.0 m corridor clear section, a
    /// 20 m cap on straight runs, sightline blockers every 15–25 m. §06 layers speeds on top —
    /// <c>MonsterBaseSpeed</c> 4.8 m/s against <c>RunnerSprintSpeed</c> 5.6 m/s, an
    /// <c>AggroReleaseDistance</c> of 12 m, a 60 m sprint budget. Every one of those is a
    /// number of metres. Import a model at the wrong scale and none of them is wrong
    /// individually — they are all wrong simultaneously, and the game reads as badly tuned
    /// rather than as misconfigured. That is why the scale check below fails loudly instead of
    /// warning.
    /// </para>
    /// <para>
    /// <b>Why <c>useFileScale</c> is on.</b> Read from the FBX files directly: they declare
    /// <c>UnitScaleFactor 1.0</c> (one file unit = one centimetre) and put the unit conversion
    /// on the root node as <c>Lcl Scaling 100</c>, which is what Blender's
    /// <c>FBX_SCALE_NONE</c> export mode does. Unity therefore reports a file scale of 0.01,
    /// and 100 × 0.01 = 1: the two cancel and a vertex at 1.75 arrives at 1.75 m. Turning
    /// unit conversion <em>off</em> would leave the node's ×100 uncancelled and every asset
    /// would import a hundred times too large. The scale factor is 1.0 and the effective
    /// scale is 1.0, which is the invariant the validator asserts — the arithmetic that gets
    /// there is the exporter's business and may change, so the assertion is on the outcome.
    /// </para>
    /// </summary>
    public sealed class AssetImportModelPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion()
        {
            return AssetImportPolicy.PolicyVersion;
        }

        public override int GetPostprocessOrder()
        {
            return AssetImportPolicy.PostprocessOrder;
        }

        private void OnPreprocessModel()
        {
            var importer = assetImporter as ModelImporter;
            if (importer == null || !AssetImportPolicy.IsManagedModel(assetPath))
            {
                return;
            }

            if (AssetImportPolicy.IsExcluded(assetPath, importer))
            {
                return;
            }

            var rule = AssetImportPolicy.ResolveModel(assetPath);
            if (rule.Category == ModelCategory.Unmanaged)
            {
                return;
            }

            ApplyScale(importer);
            ApplySceneHygiene(importer);
            ApplyMesh(importer, rule);
            ApplyRig(importer, rule);
            ApplyColliders(importer, rule);
        }

        /// <summary>
        /// Scale factor 1.0 with the file's own unit conversion applied, so the effective
        /// import scale is exactly 1.0 and one Unity unit is one metre.
        /// </summary>
        private void ApplyScale(ModelImporter importer)
        {
            if (!Mathf.Approximately(importer.globalScale, AssetImportPolicy.RequiredScaleFactor))
            {
                importer.globalScale = AssetImportPolicy.RequiredScaleFactor;
            }

            if (importer.useFileScale != AssetImportPolicy.RequiredUseFileScale)
            {
                importer.useFileScale = AssetImportPolicy.RequiredUseFileScale;
            }

            // Left off deliberately. The Blender export puts the Z-up to Y-up conversion on
            // the root node as a −90° X rotation, which is the ordinary, well-travelled path
            // through Unity's FBX importer. Baking it into the vertices would give a tidier
            // hierarchy, but it rewrites mesh and skin data on a rig whose Avatar has to map
            // afterwards, and that is not a change to make on a pipeline nobody has run yet.
            if (importer.bakeAxisConversion)
            {
                importer.bakeAxisConversion = false;
            }

            // No arithmetic assertion here on purpose. It is tempting to require
            // globalScale × fileScale == 1, but that is wrong for these files and would fire on
            // all 47: fileScale reads 0.01 because the FBX declares centimetre units, and the
            // 0.01 is exactly what cancels the ×100 the exporter parked on the root node. The
            // scale multiplier and the node scaling only mean anything together, and the only
            // place they can be read together is the imported result — so the assertion lives
            // in VerifyMetreScale, against measured bounds.
        }

        /// <summary>
        /// Turns off everything the FBX files do not contain. None of the 47 exports carries a
        /// camera, a light, a blend shape, a constraint or a visibility track, so importing
        /// them can only add empty GameObjects to the hierarchy the map assembler has to walk.
        /// </summary>
        private static void ApplySceneHygiene(ModelImporter importer)
        {
            if (importer.importCameras)
            {
                importer.importCameras = false;
            }

            if (importer.importLights)
            {
                importer.importLights = false;
            }

            if (importer.importVisibility)
            {
                importer.importVisibility = false;
            }

            if (importer.importBlendShapes)
            {
                importer.importBlendShapes = false;
            }

            if (importer.importConstraints)
            {
                importer.importConstraints = false;
            }

            if (importer.importAnimatedCustomProperties)
            {
                importer.importAnimatedCustomProperties = false;
            }
        }

        private static void ApplyMesh(ModelImporter importer, ModelImportRule rule)
        {
            if (importer.meshCompression != rule.MeshCompression)
            {
                importer.meshCompression = rule.MeshCompression;
            }

            // Read/write off everywhere. Nothing in the project reads mesh data at runtime:
            // NavMesh is baked in the editor, lightmap UVs are generated at import, and mesh
            // colliders are cooked once. Leaving it on doubles every mesh's memory by keeping
            // a CPU copy alive for nobody.
            if (importer.isReadable)
            {
                importer.isReadable = false;
            }

            if (importer.generateSecondaryUV != rule.GenerateLightmapUv)
            {
                importer.generateSecondaryUV = rule.GenerateLightmapUv;
            }

            // Pinned, not derived. Unity's Calculate margin assumes an object roughly one unit
            // across; mesh-space here is a hundred times below metres (the exporter's ×100 is
            // cancelled on the Transform, not in the vertex data), so on the smallest props
            // Calculate asks for a margin wider than the UV square, the unwrapper aborts, and
            // Unity imports the model with no vertices at all — silently, as a plain log line.
            // AssetImportPolicy.RequiredLightmapMarginMethod carries the measurements.
            if (importer.secondaryUVMarginMethod != AssetImportPolicy.RequiredLightmapMarginMethod)
            {
                importer.secondaryUVMarginMethod = AssetImportPolicy.RequiredLightmapMarginMethod;
            }

            if (importer.secondaryUVPackMargin != AssetImportPolicy.LightmapUvPackMargin)
            {
                importer.secondaryUVPackMargin = AssetImportPolicy.LightmapUvPackMargin;
            }

            // Normals are imported, not recalculated. The exporter writes face smoothing, so
            // the hard edges on §12's kit are authored; recalculating would smooth the
            // corridor corners that make a piece read as architecture.
            if (importer.importNormals != ModelImporterNormals.Import)
            {
                importer.importNormals = ModelImporterNormals.Import;
            }

            // Tangents are calculated even though no normal map exists yet. Assets/Materials
            // is empty today; the first normal-mapped material added later would otherwise
            // light incorrectly with no error to explain why, and §03 makes lighting the
            // central mechanic rather than a look.
            if (importer.importTangents != ModelImporterTangents.CalculateMikk)
            {
                importer.importTangents = ModelImporterTangents.CalculateMikk;
            }

            // Auto rather than a pinned width: the largest mesh here is 1,416 vertices, so
            // 16-bit indices are correct today, but pinning it would silently truncate a
            // regenerated model that grew past 65,535.
            if (importer.indexFormat != ModelImporterIndexFormat.Auto)
            {
                importer.indexFormat = ModelImporterIndexFormat.Auto;
            }

            if (!importer.weldVertices)
            {
                importer.weldVertices = true;
            }

            // Reorders vertices and indices for GPU cache locality without changing geometry.
            // Note the namespace: in Unity 6 this enum is UnityEditor.MeshOptimizationFlags,
            // not the UnityEngine.Rendering one older code refers to.
            if (importer.meshOptimizationFlags != MeshOptimizationFlags.Everything)
            {
                importer.meshOptimizationFlags = MeshOptimizationFlags.Everything;
            }
        }

        private static void ApplyRig(ModelImporter importer, ModelImportRule rule)
        {
            switch (rule.Category)
            {
                case ModelCategory.CharacterHumanoid:
                    SetAnimationType(importer, ModelImporterAnimationType.Human);
                    if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    }

                    break;

                case ModelCategory.CharacterGeneric:
                    SetAnimationType(importer, ModelImporterAnimationType.Generic);
                    if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    }

                    break;

                default:
                    // A rig on a corridor piece would give it an Animator and an Avatar to
                    // serialise for nothing.
                    SetAnimationType(importer, ModelImporterAnimationType.None);
                    break;
            }

            if (importer.importAnimation != rule.ImportAnimation)
            {
                importer.importAnimation = rule.ImportAnimation;
            }

            if (!rule.ImportAnimation)
            {
                return;
            }

            // Keyframe reduction only. The generated clips are already sparse — 16 to 92
            // frames each — so compressing the curves as well trades a few kilobytes for
            // drift on the mount bones §05's flashlight-as-pointer aims from.
            if (importer.animationCompression != ModelImporterAnimationCompression.KeyframeReduction)
            {
                importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            }

            // The hierarchy stays unoptimised so all 26 player bones remain real transforms.
            // Four of them — HeadCameraAnchor, FlashlightMount, ObjectiveMount, BackpackMount —
            // are not humanoid bones, so the Avatar does not protect them; optimising the
            // hierarchy would strip exactly the transforms §05's flashlight, §03's objective
            // carry and §08's 가방 attach to. The validator asserts all four survive.
            if (importer.optimizeGameObjects)
            {
                importer.optimizeGameObjects = false;
            }

            if (importer.skinWeights != ModelImporterSkinWeights.Standard)
            {
                importer.skinWeights = ModelImporterSkinWeights.Standard;
            }

            // Root motion is deliberately not sourced from any node. §13 and ARCHITECTURE.md
            // §4 make the host authoritative over position: the host moves the monster and
            // replicates the transform. Root motion would have the animation fight that,
            // producing exactly the sliding-and-snapping that host authority exists to avoid.
        }

        private static void SetAnimationType(ModelImporter importer, ModelImporterAnimationType type)
        {
            if (importer.animationType != type)
            {
                importer.animationType = type;
            }
        }

        private static void ApplyColliders(ModelImporter importer, ModelImportRule rule)
        {
            if (importer.addCollider != rule.AddCollider)
            {
                importer.addCollider = rule.AddCollider;
            }
        }

        /// <summary>
        /// Sets loop time on cycles and clears it on events, matched by clip name.
        /// <para>
        /// The names are read from the file rather than assumed. Both FBX files carry their
        /// takes under bare action names — <c>Idle</c>, <c>Walk</c>, <c>Chase</c> — nine for
        /// the player and seven for the monster, so no <c>Rig|</c> prefix has to be stripped;
        /// the matcher strips one anyway, because a re-export that changes the convention
        /// should not quietly stop matching.
        /// </para>
        /// <para>
        /// Getting this backwards is visible in both directions. A <c>Death</c> that loops
        /// replays for the rest of the match, and §09 makes death a persistent ghost state
        /// rather than an ending, so it would replay for a long time. A <c>Chase</c> that does
        /// not loop freezes the monster's legs mid-stride while it keeps closing at
        /// <c>MonsterBaseSpeed</c>.
        /// </para>
        /// </summary>
        private void OnPreprocessAnimation()
        {
            var importer = assetImporter as ModelImporter;
            if (importer == null || !AssetImportPolicy.IsManagedModel(assetPath))
            {
                return;
            }

            if (AssetImportPolicy.IsExcluded(assetPath, importer))
            {
                return;
            }

            var rule = AssetImportPolicy.ResolveModel(assetPath);
            if (!rule.ImportAnimation)
            {
                return;
            }

            var takes = importer.defaultClipAnimations;
            if (takes == null || takes.Length == 0)
            {
                return;
            }

            var kept = new List<ModelImporterClipAnimation>(takes.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var take in takes)
            {
                var shortName = ShortClipName(take.name);
                if (!seen.Add(shortName))
                {
                    // Two takes for one action would produce two clips with the same name,
                    // and an Animator referencing "Chase" would bind to whichever won.
                    Debug.LogWarning($"[AssetImport] {assetPath} contains more than one take for '{shortName}'. "
                        + "Keeping the first and dropping the rest so the clip name stays unambiguous.");
                    continue;
                }

                var loops = AssetImportPolicy.LoopingAnimationClips.Contains(shortName);
                if (!loops && !AssetImportPolicy.OneShotAnimationClips.Contains(shortName))
                {
                    Debug.LogWarning($"[AssetImport] {assetPath} has a clip named '{shortName}' that is in neither "
                        + "the cycle list nor the one-shot list in AssetImportPolicy. Importing it as a one-shot, "
                        + "which is the wrong-but-visible choice; add it to the policy.");
                }

                take.name = shortName;
                take.loopTime = loops;

                // Loop pose matches the last frame to the first. The generated cycles are
                // periodic by construction, so this is a no-op on correct data and a repair
                // on a re-export whose end frame drifted.
                take.loopPose = loops;
                take.cycleOffset = 0f;
                kept.Add(take);
            }

            var desired = kept.ToArray();
            if (!ClipsMatch(importer.clipAnimations, desired))
            {
                importer.clipAnimations = desired;
            }
        }

        /// <summary>
        /// Finishes the two jobs that need the imported hierarchy: convex flags on prop
        /// colliders, and the bounds check that catches a unit-scale error at the moment it
        /// happens rather than the first time someone notices the map feels wrong.
        /// </summary>
        private void OnPostprocessModel(GameObject root)
        {
            if (root == null || !AssetImportPolicy.IsManagedModel(assetPath))
            {
                return;
            }

            var importer = assetImporter as ModelImporter;
            if (importer == null || AssetImportPolicy.IsExcluded(assetPath, importer))
            {
                return;
            }

            var rule = AssetImportPolicy.ResolveModel(assetPath);
            if (rule.Category == ModelCategory.Unmanaged)
            {
                return;
            }

            ApplyColliderConvexity(root, rule);
            VerifyMetreScale(root, rule);
        }

        /// <summary>
        /// Marks prop colliders convex except where the shape's interior is the point.
        /// <see cref="AssetImportPolicy.ConcaveProps"/> carries a reason per exception, and
        /// convex is the default because PhysX will not accept a concave mesh collider on a
        /// non-kinematic <c>Rigidbody</c> — which is what every <c>Loot_*</c> piece becomes
        /// the moment §08's carry-and-drop loop touches it.
        /// </summary>
        private static void ApplyColliderConvexity(GameObject root, ModelImportRule rule)
        {
            if (!rule.AddCollider)
            {
                return;
            }

            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.convex != rule.ConvexCollider)
                {
                    collider.convex = rule.ConvexCollider;
                }
            }
        }

        /// <summary>
        /// Measures what actually arrived and fails when it is not metre-scale.
        /// <para>
        /// The extents are measured through each mesh's own transform scale, so a residual
        /// factor parked on a node is included rather than hidden — this is the check that
        /// catches the 100× and the 0.01× error outright, and it is an error rather than a
        /// warning because §12's every distance is wrong the moment it fires.
        /// </para>
        /// <para>
        /// A non-unit local scale is reported separately and only as a warning. It is a
        /// hierarchy-hygiene problem rather than a scale problem: the export puts the unit
        /// conversion on the root node as <c>Lcl Scaling 100</c> against a file scale of 0.01,
        /// and whether Unity cancels those into the mesh or leaves 100 on the transform is the
        /// importer's business. Either way the bounds above are right, so this cannot be
        /// allowed to fail an import — it is here because anything the map assembler parents
        /// under a scaled node inherits the factor.
        /// </para>
        /// </summary>
        private void VerifyMetreScale(GameObject root, ModelImportRule rule)
        {
            var largest = 0f;
            var meshCount = 0;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                largest = Mathf.Max(largest, LargestExtent(filter.sharedMesh, filter.transform));
                meshCount += filter.sharedMesh != null ? 1 : 0;
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                largest = Mathf.Max(largest, LargestExtent(skinned.sharedMesh, skinned.transform));
                meshCount += skinned.sharedMesh != null ? 1 : 0;
            }

            if (meshCount == 0)
            {
                Debug.LogWarning($"[AssetImport] {assetPath} imported with no mesh, so its scale cannot be checked.");
                return;
            }

            AssetImportPolicy.MetreScaleBand(rule.Category, out var min, out var max);
            if (largest < min || largest > max)
            {
                Debug.LogError($"[AssetImport] {assetPath} imported with a largest extent of {largest:0.####} m, "
                    + $"outside the {min:0.###}–{max:0.###} m band for {rule.Category}. The exports are 1 unit = "
                    + "1 metre and §12's grid, corridor section and sightline distances only hold at that scale, "
                    + "so a scale error here makes every map rule wrong simultaneously. Scale factor is "
                    + $"{ScaleDiagnostic()}.");
            }

            if (rule.ExpectedTallestExtentMetres > 0f)
            {
                var expected = rule.ExpectedTallestExtentMetres;
                var slack = expected * AssetImportPolicy.ScaleTolerance;
                if (Mathf.Abs(largest - expected) > slack)
                {
                    Debug.LogError($"[AssetImport] {assetPath} measures {largest:0.####} m at its longest, but this "
                        + $"model is a scale anchor at {expected:0.###} m ±{AssetImportPolicy.ScaleTolerance:P0}. "
                        + "§12's 2.2 × 3.0 m corridor section and §06's 12 m aggro release are only meaningful "
                        + "relative to a character of the authored size.");
                }
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var scale = transform.localScale;
                if (Mathf.Abs(scale.x - 1f) > 0.001f
                    || Mathf.Abs(scale.y - 1f) > 0.001f
                    || Mathf.Abs(scale.z - 1f) > 0.001f)
                {
                    Debug.LogWarning($"[AssetImport] {assetPath} imported with a non-unit local scale "
                        + $"{scale.ToString("0.####")} on '{transform.name}'. The measured bounds above are still "
                        + "metre-correct, so this is not a scale failure — but anything §12's map assembler "
                        + "parents under that node inherits the factor.");
                    return;
                }
            }
        }

        /// <summary>
        /// The three scale inputs, for a message that has to be actionable without the reader
        /// opening the Inspector.
        /// </summary>
        private string ScaleDiagnostic()
        {
            var importer = assetImporter as ModelImporter;
            if (importer == null)
            {
                return "unavailable";
            }

            return $"{importer.globalScale:0.######}, Convert Units is "
                + $"{(importer.useFileScale ? "on" : "off")}, and the file reports a scale of "
                + $"{importer.fileScale:0.######}";
        }

        private static float LargestExtent(Mesh mesh, Transform owner)
        {
            if (mesh == null)
            {
                return 0f;
            }

            var size = mesh.bounds.size;
            var scale = owner != null ? owner.lossyScale : Vector3.one;
            return Mathf.Max(
                Mathf.Abs(size.x * scale.x),
                Mathf.Abs(size.y * scale.y),
                Mathf.Abs(size.z * scale.z));
        }

        /// <summary>Strips a <c>Rig|</c> prefix if one is present, leaving the action name.</summary>
        public static string ShortClipName(string takeName)
        {
            if (string.IsNullOrEmpty(takeName))
            {
                return string.Empty;
            }

            var bar = takeName.LastIndexOf('|');
            return bar >= 0 && bar < takeName.Length - 1 ? takeName.Substring(bar + 1) : takeName;
        }

        /// <summary>
        /// Compares the clip lists field by field so an unchanged import does not reassign
        /// <c>clipAnimations</c> and dirty the importer, which is what would turn a reimport
        /// into a loop.
        /// </summary>
        private static bool ClipsMatch(ModelImporterClipAnimation[] a, ModelImporterClipAnimation[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i].name, b[i].name, StringComparison.Ordinal)
                    || !string.Equals(a[i].takeName, b[i].takeName, StringComparison.Ordinal)
                    || a[i].loopTime != b[i].loopTime
                    || a[i].loopPose != b[i].loopPose
                    || !Mathf.Approximately(a[i].firstFrame, b[i].firstFrame)
                    || !Mathf.Approximately(a[i].lastFrame, b[i].lastFrame))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
