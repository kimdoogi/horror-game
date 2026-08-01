#nullable enable

namespace HorrorGame.Gameplay.Presence
{
    /// <summary>
    /// The names <c>tools/blender/gen_presence.py</c> and this code have agreed on.
    /// <para>
    /// Every one of these is a contract with a generator that runs outside Unity and
    /// cannot be told when a string here changes. They are gathered in one file so a
    /// rename is one diff rather than a search, and so the failure mode is a missing
    /// asset at build time rather than a figure that renders as untextured white in a
    /// store screenshot — which is defect 3.19's whole history.
    /// </para>
    /// </summary>
    public static class PresenceAssets
    {
        /// <summary>Where the generator writes its meshes and manifest.</summary>
        public const string ModelRoot = "Assets/Models/Presence";

        /// <summary>The generator's single description of the 그늘's surfaces.</summary>
        public const string ManifestPath = ModelRoot + "/Presence.manifest.json";

        /// <summary>The 형상 that stands at <c>PresenceStage.Close</c>.</summary>
        public const string FigureModelPath = ModelRoot + "/Presence_Figure.fbx";

        /// <summary>One flake, instanced around the player at <c>PresenceStage.Gathering</c>.</summary>
        public const string MoteModelPath = ModelRoot + "/Presence_Mote.fbx";

        /// <summary>Where <c>PresenceSkin</c> writes the built URP materials.</summary>
        public const string MaterialRoot = "Assets/Materials/Presence";

        /// <summary>
        /// The core. Albedo 0.013 linear — an order of magnitude under the darkest §12
        /// wall, so the figure subtracts rather than reflects.
        /// </summary>
        public const string VoidMaterialName = "Presence_Void";

        /// <summary>
        /// The flakes, and the only part of the figure a player at range actually sees.
        /// Emissive; see <c>PresenceSkin</c> for why the emission is the value that decides
        /// whether this entity exists on screen at all.
        /// </summary>
        public const string GrainMaterialName = "Presence_Grain";

        /// <summary>
        /// The same substance at a third the emission, for the free motes.
        /// <para>
        /// Two materials because the two things are seen at different distances and one
        /// exposure cannot serve both: the figure's flakes must carry a silhouette at 12 m,
        /// where a flake is under two pixels and only brightness survives downsampling,
        /// and the free motes sit at 1–4 m, where that same brightness renders as scraps of
        /// paper stuck to the brickwork. Measured, not assumed — see <c>PresenceShot</c>.
        /// </para>
        /// </summary>
        public const string DustMaterialName = "Presence_Dust";

        /// <summary>Non-positional clips. §04 — see the manifest's <c>audio_note</c>.</summary>
        public const string AudioRoot = "Assets/Audio/Presence";

        /// <summary>고임 — the bed that rises with the pool.</summary>
        public const string GatheringClipPath = AudioRoot + "/pre_gathering_loop.wav";

        /// <summary>임박 — the warning layer.</summary>
        public const string CloseClipPath = AudioRoot + "/pre_close_loop.wav";

        /// <summary>빼앗김 — the swallow, and the silence after it.</summary>
        public const string TakenClipPath = AudioRoot + "/pre_taken.wav";

        /// <summary>돌아옴 — the voice coming back, and the certainty not.</summary>
        public const string ReturnClipPath = AudioRoot + "/pre_return.wav";
    }
}
