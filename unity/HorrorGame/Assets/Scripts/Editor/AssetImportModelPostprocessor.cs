using System;
using System.Collections.Generic;
using System.IO;
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
    /// <para>
    /// <b>The second job: rigged characters import as rigs, wherever they live.</b> A
    /// character's rig settings are not a preference somebody expressed in the Inspector —
    /// they are a property of the file. See <see cref="IsRiggedCharacterModel"/> for the
    /// class, <see cref="GeneratedCharacterRule"/> for what it forces, and
    /// <see cref="CharacterPolicyRevision"/> for why a code change alone is not enough to
    /// reach an asset that is already imported.
    /// </para>
    /// </summary>
    public sealed class AssetImportModelPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// Added to <see cref="AssetImportPolicy.PolicyVersion"/> so this file can force the
        /// reimport its own rule changes need without editing the shared policy's version.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unity reimports an asset when the hash of its importer's inputs moves, and a
        /// post-processor contributes to that hash through <see cref="GetVersion"/> and
        /// nothing else. Editing the code below without moving this number changes what
        /// <em>would</em> happen on the next import and changes nothing about the assets
        /// already in <c>Library/</c> — so the source reads correct, the reviewer agrees, and
        /// the artefact in the build is the old one. That is the failure this repository keeps
        /// shipping (a map generated into a scene the game does not load; a rig imported with
        /// animation switched off), and it is the same failure every time: the code was
        /// checked and the artefact was not.
        /// </para>
        /// <para>
        /// 1 — the rigged-character class below. Runner.fbx imported as a static prop for a
        /// week because it lives in <c>Assets/Models/Player/</c> rather than
        /// <c>Assets/Models/Characters/</c>, and the settings that were hand-corrected in its
        /// <c>.meta</c> were overwritten on the next import by this very post-processor.
        /// </para>
        /// </remarks>
        public const uint CharacterPolicyRevision = 1;

        /// <summary>
        /// How much of an FBX is read at a time when classifying it. 64 KiB is a comfortable
        /// multiple of a page and small enough that the buffer stays off the large object
        /// heap; the largest FBX in the project is 2.65 MB, so nothing here is sized by it.
        /// </summary>
        private const int FbxScanChunkBytes = 64 * 1024;

        /// <summary>
        /// The measured largest extent, in metres, of a rigged character this file has to
        /// grade itself — that is, one <see cref="AssetImportPolicy.ResolveModelCategory"/>
        /// does not already recognise. Keyed on file name, because a scale anchor is a
        /// measurement of one asset and not a property of a class.
        /// <para>
        /// A name with no entry gets no anchor and is checked only against the 1–3 m
        /// character band, which is deliberate: a wrong anchor is a red error line on a
        /// correct asset every time it imports, and the project has already spent real time
        /// on a message that accused a good FBX of a unit-scale error
        /// (<see cref="AssetImportPolicy.RequiredLightmapMarginMethod"/>).
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, float> CharacterScaleAnchors =
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                // 주자 — DESCENT-PIVOT §5, 「모델 하나. 20명이 똑같이 생긴다.」 Read out of the
                // FBX's own vertex data: the mesh measures 0.906719 × 0.394246 × 1.750000 in
                // file units, so its largest extent is its height and its height is exactly
                // AssetImportPolicy.PlayerHeightMetres. tools/blender/gen_runner.py holds
                // TARGET_HEIGHT = 1.750 and its verify_roundtrip re-reads the written file and
                // fails outside ±2 mm, so the ±2% band here has seventeen times the generator's
                // own tolerance and cannot fire on a correctly generated figure.
                { "Runner", AssetImportPolicy.PlayerHeightMetres },

                // 주자's viewmodel — the first-person arms, cut at the shoulder. It carries a
                // skin and animation stacks, so IsRiggedCharacterModel promotes it to
                // CharacterGeneric and it would be graded against that category's 1.0–3.0 m
                // band; two arms are 0.887 m and would be reported as a unit-scale error on a
                // correct import, which is the exact accusation this table exists to prevent.
                // Read out of the FBX: 0.887 × 0.516 × 0.223 m, largest extent the span.
                // gen_runner.py derives it from the body's own Fit rather than re-solving a
                // scale, so it moves only when the body's arm length does.
                { "RunnerArms", 0.887f },
            };

        public override uint GetVersion()
        {
            return AssetImportPolicy.PolicyVersion + CharacterPolicyRevision;
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

            var rule = ResolveGovernedRule(assetPath, out var policyMisgraded);
            if (rule.Category == ModelCategory.Unmanaged)
            {
                return;
            }

            // Snapshotted before anything is written, so the log below can say what the
            // .meta actually carried rather than what it carries now. Only taken for the
            // rigged-character class: the same reporting across all 91 files would be noise
            // on 87 of them, and noise is how the four that matter got missed.
            var wasCharacter = IsCharacterCategory(rule.Category);
            var before = wasCharacter
                ? CharacterImportSnapshot.Capture(importer)
                : default(CharacterImportSnapshot);

            ApplyScale(importer);
            ApplySceneHygiene(importer);
            ApplyMesh(importer, rule);
            ApplyRig(importer, rule);
            ApplyColliders(importer, rule);

            if (policyMisgraded)
            {
                ReportMisgradedCharacter(assetPath, rule);
            }

            if (wasCharacter)
            {
                ReportCharacterCorrections(assetPath, before, CharacterImportSnapshot.Capture(importer));
            }
        }

        // ── The rigged-character class ──────────────────────────────────────────────
        //
        // Everything from here to ReportCharacterCorrections exists to make one sentence
        // true: an FBX that contains a skinned skeleton and animation takes imports as a
        // rig, and no .meta edit, reimport, Library wipe or fresh clone can lose that.

        /// <summary>
        /// The rule this import is graded against: the shared policy's answer, corrected
        /// upward when the file's own contents say it is a rigged character and the policy
        /// does not know it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why a correction is needed at all.</b>
        /// <see cref="AssetImportPolicy.ResolveModelCategory"/> keys on the asset's immediate
        /// folder — <c>MapKit</c>, <c>Props</c>, <c>Characters</c> — and everything else falls
        /// through to <see cref="ModelCategory.Prop"/>. <c>Assets/Models/Player/Runner.fbx</c>
        /// is in none of those folders, so 주자 — a 13-bone rig carrying nine takes — was
        /// graded a static prop, and the Prop rule writes exactly the four settings that were
        /// reported as a mystery: <c>addColliders: 1</c>, <c>importAnimation: 0</c>,
        /// <c>animationType: 0</c>, <c>avatarSetup: 0</c>. Unity was never "normalising" the
        /// hand-edited <c>.meta</c>. This post-processor was overwriting it, every import,
        /// from a category that was wrong.
        /// </para>
        /// <para>
        /// <b>Why the correction only ever goes upward.</b> A file the policy already grades
        /// <see cref="ModelCategory.CharacterHumanoid"/> or
        /// <see cref="ModelCategory.CharacterGeneric"/> is returned untouched, so Player.fbx
        /// keeps Humanoid and Monster.fbx keeps Generic and neither can be regraded from
        /// here. A file with no skin and no takes is returned untouched, so all 22 kit pieces,
        /// 25 props, 38 dressing pieces and both Presence figures are out of reach. The only
        /// transition this can make is prop-or-kit → <see cref="ModelCategory.CharacterGeneric"/>,
        /// and Generic is the safe direction: it never invents a bone, whereas Humanoid maps
        /// onto a skeleton it may not have.
        /// </para>
        /// </remarks>
        /// <param name="path">Project-relative asset path.</param>
        /// <param name="policyMisgraded">
        /// True when the shared policy and the file disagree, so the caller can say so.
        /// </param>
        // Internal, not private: AssetImportValidator's model sweep must grade every
        // file against the SAME governed rule the import actually ran under. It once
        // graded Runner.fbx against the raw shared policy instead and reported four
        // failures (compression, lightmap UVs, rig, collider) for settings this class
        // had deliberately forced the other way — a validator red on a correct import.
        internal static ModelImportRule ResolveGovernedRule(string path, out bool policyMisgraded)
        {
            policyMisgraded = false;

            var rule = AssetImportPolicy.ResolveModel(path);
            if (IsCharacterCategory(rule.Category))
            {
                return rule;
            }

            if (!IsRiggedCharacterModel(path))
            {
                return rule;
            }

            policyMisgraded = true;
            return GeneratedCharacterRule(path);
        }

        private static bool IsCharacterCategory(ModelCategory category)
        {
            return category == ModelCategory.CharacterHumanoid
                || category == ModelCategory.CharacterGeneric;
        }

        /// <summary>
        /// What a rigged character imports as when this file has to decide for itself:
        /// Generic, animation on, an Avatar built from the model, no lightmap UVs, no mesh
        /// compression and no generated collider.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Generic and not Humanoid</b>, for the same reason
        /// <see cref="AssetImportPolicy.ResolveModelCategory"/> gives for Monster.fbx.
        /// Runner.fbx's skeleton, read out of the file, is 13 bones — Hips, Spine, Head, the
        /// two UpperArms and the two legs down to the toes — with <em>no elbow and no hand</em>
        /// on either arm. Unity's humanoid mapper requires LowerArm and Hand on both sides, so
        /// a Humanoid avatar would need four bones the rig does not have; a mapper that
        /// invents bones does not report it, it just retargets onto a body nobody authored.
        /// A rig that ships its own clips and never retargets loses nothing by being Generic.
        /// </para>
        /// <para>
        /// <b>No collider</b>, from ASSETS.md §90's "Generate Colliders | off". Not tidiness:
        /// the Prop grading was putting a <c>MeshCollider</c> around the player's own visual,
        /// which is a 3,754-triangle cook that has to be re-cooked as the mesh deforms, and
        /// which sits between §03's interact ray and everything the player is trying to point
        /// at. §05's capsule is authored on the prefab and is the only collider a character
        /// gets.
        /// </para>
        /// <para>
        /// <b>No lightmap UVs</b> because a skinned mesh never bakes, and <b>no mesh
        /// compression</b> because quantising positions on bone-weighted vertices reads as a
        /// wobble around the joints. Both match what
        /// <see cref="AssetImportPolicy.ResolveModel"/> already returns for the two known
        /// characters; the one field that is deliberately <em>not</em> copied from it is the
        /// scale anchor, which that method hard-codes to
        /// <see cref="AssetImportPolicy.MonsterHeightMetres"/> for every Generic rig. 주자 is
        /// 1.750 m, so inheriting the monster's 2.336 m would put a red error line on a
        /// perfectly good import every single time. <see cref="CharacterScaleAnchors"/> is the
        /// measurement instead.
        /// </para>
        /// </remarks>
        private static ModelImportRule GeneratedCharacterRule(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            CharacterScaleAnchors.TryGetValue(name, out var anchorMetres);

            return new ModelImportRule(
                category: ModelCategory.CharacterGeneric,
                importAnimation: true,
                generateLightmapUv: false,
                meshCompression: ModelImporterMeshCompression.Off,
                addCollider: false,
                convexCollider: false,
                expectedTallestExtentMetres: anchorMetres);
        }

        /// <summary>
        /// True when the FBX at this path contains both a skin deformer and at least one
        /// animation take — which is the definition of the thing, not a naming convention
        /// that describes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why the file and not the path.</b> The four settings at issue are not
        /// preferences: <c>animationType</c> needs a skeleton to be meaningful,
        /// <c>importAnimation</c> and <c>avatarSetup</c> need takes, and <c>addCollider</c>
        /// must be off precisely because the renderer is skinned. Every one of them is
        /// decided by what is inside the file, so the file is what they are keyed off.
        /// A folder key survives a re-export and a Library wipe but not a move, and moving
        /// 주자 out of <c>Assets/Models/Characters/</c> is the exact event that caused this
        /// defect. <see cref="AssetImportPolicy.TryResolveAudioFromSource"/> made the same
        /// choice for the same reason: an import setting that depends on content is read from
        /// the content, at the cost of one file handle.
        /// </para>
        /// <para>
        /// <b>The signatures.</b> FBX binary stores an object's name and class joined by the
        /// two bytes <c>00 01</c> — <c>Idle\0\1AnimStack</c>, <c>Skin\0\1Deformer</c> — and
        /// leaves both uncompressed even when the vertex arrays beside them are zlib'd. So
        /// the whole test is "do these two byte strings occur", with no parser to get wrong.
        /// </para>
        /// <para>
        /// <b>The separator is not decoration, and this was measured.</b> Searching for the
        /// bare word <c>AnimStack</c> matches all 91 FBX in the project, because the
        /// Definitions block near the top of every file declares a property template called
        /// <c>FbxAnimStack</c> — which contains <c>AnimStack</c> as a substring. Every static
        /// corridor piece would have been classed as a character. With the <c>00 01</c> prefix
        /// the match is 4 files: Player.fbx, Monster.fbx, Ghost.fbx and Runner.fbx. The bare
        /// word <c>Deformer</c> happens to match only those same 4 today, and is prefixed here
        /// anyway, because "happens to" is not a rule.
        /// </para>
        /// <para>
        /// <b>Both, not either.</b> A skin with no takes is a rigged mesh nobody animated and
        /// has no clips to import; takes with no skin is an object animation on a prop. Only
        /// the conjunction is a character. Requiring both is also what keeps a blend-shape
        /// deformer — same FBX class — from promoting a prop on its own.
        /// </para>
        /// <para>
        /// Unreadable, absent or not-binary-FBX answers false: the asset then keeps whatever
        /// the shared policy grades it, which is the answer that changes nothing. A
        /// classification that guesses when it cannot read is the fallback
        /// <see cref="AssetImportPolicy.TryResolveAudioFromSource"/> was written to delete.
        /// </para>
        /// </remarks>
        public static bool IsRiggedCharacterModel(string path)
        {
            var absolute = AssetImportPolicy.ToAbsolutePath(path);
            if (absolute == null)
            {
                return false;
            }

            return ContainsBothSignatures(absolute, SkinDeformerSignature, AnimationStackSignature);
        }

        /// <summary>An FBX object header's class marker: the two separator bytes then the class name.</summary>
        private static byte[] FbxClassSignature(string className)
        {
            var bytes = new byte[className.Length + 2];
            bytes[0] = 0x00;
            bytes[1] = 0x01;
            for (var i = 0; i < className.Length; i++)
            {
                bytes[i + 2] = (byte)className[i];
            }

            return bytes;
        }

        /// <summary>A skin cluster: the binding that makes the mesh follow the skeleton.</summary>
        private static readonly byte[] SkinDeformerSignature = FbxClassSignature("Deformer");

        /// <summary>One animation take. The abbreviated class name is what the object header carries.</summary>
        private static readonly byte[] AnimationStackSignature = FbxClassSignature("AnimStack");

        /// <summary>
        /// Streams the file once looking for both signatures, carrying enough of each chunk
        /// forward that a signature straddling a chunk boundary is still found.
        /// <para>
        /// Chunked rather than read whole so the cost is bounded by
        /// <see cref="FbxScanChunkBytes"/> and not by the largest FBX anybody adds later, and
        /// it stops the moment both are seen — on Runner.fbx that is 147 KB into an 806 KB
        /// file. No cache: the same reasoning as reading a WAVE header per clip, and an
        /// import is about to read the whole file anyway.
        /// </para>
        /// </summary>
        private static bool ContainsBothSignatures(string absolutePath, byte[] first, byte[] second)
        {
            // Carrying one byte less than the longest signature is exactly enough: any
            // occurrence beginning inside the carried tail is wholly contained in the next
            // window. Verified against all 91 FBX at chunk sizes from 7 bytes up, including
            // sizes below the signature length itself, against a whole-file search.
            var overlap = Mathf.Max(first.Length, second.Length) - 1;
            var buffer = new byte[FbxScanChunkBytes + overlap];
            var foundFirst = false;
            var foundSecond = false;
            var carried = 0;

            try
            {
                using (var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    while (true)
                    {
                        var read = stream.Read(buffer, carried, FbxScanChunkBytes);
                        if (read <= 0)
                        {
                            return false;
                        }

                        var filled = carried + read;
                        foundFirst = foundFirst || Contains(buffer, filled, first);
                        foundSecond = foundSecond || Contains(buffer, filled, second);
                        if (foundFirst && foundSecond)
                        {
                            return true;
                        }

                        // Array.Copy and not Buffer.BlockCopy: source and destination overlap
                        // here by design, and Array.Copy is the one whose contract says the
                        // source is read before it is overwritten.
                        carried = Mathf.Min(overlap, filled);
                        Array.Copy(buffer, filled - carried, buffer, 0, carried);
                    }
                }
            }
            // IOException covers the truncated and the mid-write cases; the asset then keeps
            // whatever the shared policy grades it.
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Whether <paramref name="needle"/> occurs in the first <paramref name="length"/> bytes.</summary>
        private static bool Contains(byte[] haystack, int length, byte[] needle)
        {
            var last = length - needle.Length;
            for (var i = 0; i <= last; i++)
            {
                var j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j])
                {
                    j++;
                }

                if (j == needle.Length)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Says, as an error, that the project's own policy and the file disagree — and gives
        /// the edit that ends the disagreement.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The import above is already correct, so this is not a report of a broken asset. It
        /// is a report of a broken <em>rule</em>, and it has to be loud for two reasons. The
        /// import is right and <see cref="AssetImportValidator"/> is not: it grades against
        /// <see cref="AssetImportPolicy.ResolveModel"/> directly, so until the edit below
        /// lands it will report this asset as a failing prop — missing its collider, missing
        /// its lightmap UVs, animating when the policy says not to. Somebody reading that
        /// output needs to find this line, or they will "fix" the asset back to broken. And a
        /// correction nobody is told about is how the original defect survived a week.
        /// </para>
        /// </remarks>
        private static void ReportMisgradedCharacter(string path, ModelImportRule applied)
        {
            Debug.LogError($"[AssetImport] {path} is a rigged character — its FBX carries a skin deformer and "
                + "animation takes — but AssetImportPolicy.ResolveModelCategory grades it "
                + $"{AssetImportPolicy.ResolveModelCategory(path)} because it is not under Models/Characters, "
                + "Models/MapKit or Models/Props. The import has been forced to "
                + $"{applied.Category} (animation on, Avatar from this model, no generated collider) so the "
                + "artefact is correct, but the policy is still wrong and AssetImportValidator will report this "
                + "asset as a failing prop until it is fixed. Two edits in AssetImportPolicy.cs:\n"
                + "  1. ResolveModelCategory — before the final 'return ModelCategory.Prop', add\n"
                + "         if (AssetImportModelPostprocessor.IsRiggedCharacterModel(assetPath))\n"
                + "         {\n"
                + "             return ModelCategory.CharacterGeneric;\n"
                + "         }\n"
                + "  2. ResolveModel — its CharacterGeneric branch hard-codes the scale anchor to\n"
                + "     MonsterHeightMetres (2.336 m). 주자 is 1.750 m, so landing edit 1 without this one\n"
                + "     replaces this error with a permanent scale-anchor error on a correct file. Make the\n"
                + "     anchor per asset, the way ExpectedAnimationClipCount already is.\n"
                + "  Also worth adding while there: ExpectedAnimationClipCount has no entry for this file, so "
                + "nothing notices if a regenerated model loses a take.");
        }

        /// <summary>
        /// Lists every rig setting the incoming <c>.meta</c> had wrong, so the repair is
        /// visible instead of silent.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A setting a reimport can undo is not a setting, it is a coin flip — and the tell
        /// that this is happening is a <c>.meta</c> that keeps arriving wrong. That is
        /// information, so it is printed. It is a warning and not an error because the asset
        /// that comes out the other side is correct; nothing downstream is broken, and a
        /// clean checkout with a stale <c>.meta</c> committed would otherwise fail a batch
        /// import over a file it is in the middle of repairing.
        /// </para>
        /// <para>
        /// Silence here is the good case and the common one: an unchanged import prints
        /// nothing at all.
        /// </para>
        /// </remarks>
        private static void ReportCharacterCorrections(string path,
            CharacterImportSnapshot before, CharacterImportSnapshot after)
        {
            var changes = new List<string>();
            before.AppendDifferences(after, changes);
            if (changes.Count == 0)
            {
                return;
            }

            Debug.LogWarning($"[AssetImport] {path} arrived with {changes.Count} rig setting(s) that this policy "
                + "had to correct:\n  " + string.Join("\n  ", changes.ToArray())
                + "\n  They are enforced here rather than in the .meta because a hand-edited .meta is undone by "
                + "the next import and the consequence is invisible — the body simply slides and no test fails. "
                + "If this keeps appearing on a clean checkout, the committed .meta is stale: reimport the asset "
                + "and commit the .meta the policy writes.");
        }

        /// <summary>
        /// The rig settings whose loss is invisible at runtime, sampled either side of the
        /// policy being applied. All six were wrong on Runner.fbx at once, and the only
        /// symptom was a player that slid.
        /// </summary>
        private readonly struct CharacterImportSnapshot
        {
            private readonly ModelImporterAnimationType _animationType;
            private readonly ModelImporterAvatarSetup _avatarSetup;
            private readonly bool _importAnimation;
            private readonly bool _addCollider;
            private readonly bool _optimizeGameObjects;
            private readonly bool _generateSecondaryUv;

            private CharacterImportSnapshot(ModelImporter importer)
            {
                _animationType = importer.animationType;
                _avatarSetup = importer.avatarSetup;
                _importAnimation = importer.importAnimation;
                _addCollider = importer.addCollider;
                _optimizeGameObjects = importer.optimizeGameObjects;
                _generateSecondaryUv = importer.generateSecondaryUV;
            }

            public static CharacterImportSnapshot Capture(ModelImporter importer)
            {
                return new CharacterImportSnapshot(importer);
            }

            /// <summary>
            /// Appends one line per field that moved, each naming what it was, what it is now,
            /// and what it costs — a diff nobody can read the consequence of is a diff nobody
            /// reads.
            /// </summary>
            public void AppendDifferences(CharacterImportSnapshot after, List<string> into)
            {
                if (_animationType != after._animationType)
                {
                    into.Add($"animationType {(int)_animationType} ({_animationType}) → "
                        + $"{(int)after._animationType} ({after._animationType}). "
                        + "None means no Animator and no clips: every take in the file is unreachable.");
                }

                if (_importAnimation != after._importAnimation)
                {
                    into.Add($"importAnimation {Flag(_importAnimation)} → {Flag(after._importAnimation)}. "
                        + "Off imports none of the takes, and an FBX full of animation that produced no clips "
                        + "looks exactly like an FBX with no animation in it.");
                }

                if (_avatarSetup != after._avatarSetup)
                {
                    into.Add($"avatarSetup {(int)_avatarSetup} ({_avatarSetup}) → "
                        + $"{(int)after._avatarSetup} ({after._avatarSetup}). "
                        + "NoAvatar next to a Generic animationType is the internally inconsistent pair that "
                        + "makes a hand-edited .meta look like it 'reverted' — the Animator gets a null avatar.");
                }

                if (_addCollider != after._addCollider)
                {
                    into.Add($"addColliders {Flag(_addCollider)} → {Flag(after._addCollider)}. "
                        + "ASSETS.md:90 records Generate Colliders off; a MeshCollider on a skinned character is "
                        + "a per-frame re-cook and it swallows §03's interact ray. §05's capsule is on the prefab.");
                }

                if (_optimizeGameObjects != after._optimizeGameObjects)
                {
                    into.Add($"optimizeGameObjects {Flag(_optimizeGameObjects)} → "
                        + $"{Flag(after._optimizeGameObjects)}. "
                        + "On, it strips the bones no Avatar protects — which is every mount transform §05's "
                        + "flashlight and §03's objective attach to.");
                }

                if (_generateSecondaryUv != after._generateSecondaryUv)
                {
                    into.Add($"generateSecondaryUV {Flag(_generateSecondaryUv)} → "
                        + $"{Flag(after._generateSecondaryUv)}. "
                        + "A skinned mesh is never lightmapped, so this is unwrap time spent on nothing.");
                }
            }

            private static string Flag(bool value)
            {
                return value ? "1" : "0";
            }
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

            // Through the same seam OnPreprocessModel used. Reading the policy directly here
            // would leave a character the policy misgrades with animation switched ON by the
            // pre-process and its clip list never written — nine takes importing on Unity's
            // defaults, which means every cycle arrives with Loop Time off and §06's 추격
            // freezes mid-stride. Two readers, one asset, opposite answers.
            var rule = ResolveGovernedRule(assetPath, out _);
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

            var rule = ResolveGovernedRule(assetPath, out _);
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

            // The category band is skipped when this file has its own anchor — see
            // CharacterScaleAnchors, and the same reasoning as AssetImportValidator's copy
            // of this decision: the anchor is the stronger statement, and running both lets
            // the vaguer one fail an asset the project has measured exactly. RunnerArms.fbx
            // is the case that forced it — a rigged 0.887 m viewmodel graded as
            // CharacterGeneric against a 1–3 m band.
            if (rule.ExpectedTallestExtentMetres <= 0f)
            {
                AssetImportPolicy.MetreScaleBand(rule.Category, out var min, out var max);
                if (largest < min || largest > max)
                {
                    Debug.LogError($"[AssetImport] {assetPath} imported with a largest extent of {largest:0.####} m, "
                        + $"outside the {min:0.###}–{max:0.###} m band for {rule.Category}. The exports are 1 unit = "
                        + "1 metre and §12's grid, corridor section and sightline distances only hold at that scale, "
                        + "so a scale error here makes every map rule wrong simultaneously. Scale factor is "
                        + $"{ScaleDiagnostic()}.");
                }
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
