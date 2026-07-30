#nullable enable

using System;
using HorrorGame.Core.Map;
using UnityEngine;

namespace HorrorGame.Audio
{
    /// <summary>
    /// The five surfaces of §12, as clips.
    /// <para>
    /// This asset <em>is</em> the 청음사's alphabet. §12 is explicit that the floor
    /// layout is "아트 결정이 아니라 시스템 결정" — changing which clip a zone plays
    /// changes what the role can do — so it is one authored asset with one owner
    /// rather than a field on every prop. <c>MapValidator</c> rejects a map with
    /// <see cref="FloorMaterial.None"/> in it for the same reason.
    /// </para>
    /// <para>
    /// Also carries the per-zone ambience bed, because the bed and the footstep are
    /// the same claim about a room made twice — a Listener who hears 자갈 underfoot
    /// and a 나무 room tone would have no idea where they were standing.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "HorrorGame/Audio/Surface Library", fileName = "SurfaceAudioLibrary")]
    public sealed class SurfaceAudioLibrary : ScriptableObject
    {
        /// <summary>Everything one surface sounds like.</summary>
        [Serializable]
        public sealed class SurfaceEntry
        {
            [Tooltip("Which of §12's five surfaces this describes.")]
            public FloorMaterial material = FloorMaterial.None;

            [Tooltip("step_<surface>_player_walk_*.wav")]
            public AudioClipBank playerWalk = new AudioClipBank();

            [Tooltip("step_<surface>_player_run_*.wav")]
            public AudioClipBank playerRun = new AudioClipBank();

            [Tooltip("step_<surface>_monster_step_*.wav — the Listener's actual input.")]
            public AudioClipBank monsterStep = new AudioClipBank();

            [Tooltip("amb_zone_*_loop.wav for the zone this surface belongs to. Optional; " +
                     "the stairwell surface has its own bed rather than a zone.")]
            public AudioClip? ambienceBed;
        }

        [SerializeField]
        private SurfaceEntry[] surfaces = Array.Empty<SurfaceEntry>();

        [Tooltip("Played when a surface has no entry. Leave empty to play nothing — " +
                 "silence is a louder bug report than a wrong floor.")]
        [SerializeField]
        private SurfaceEntry? fallback;

        /// <summary>Every authored surface. For tooling and validation.</summary>
        public SurfaceEntry[] Surfaces => surfaces;

        /// <summary>
        /// The entry for a surface, or the fallback, or null.
        /// <para>
        /// Returning null rather than substituting a neighbouring surface is
        /// deliberate: §12 makes the material the Listener's only location cue, so
        /// playing the wrong floor would be worse than playing nothing. A silent zone
        /// is immediately obvious in playtest; a zone that lies is not.
        /// </para>
        /// </summary>
        public SurfaceEntry? For(FloorMaterial material)
        {
            for (var i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null && surfaces[i].material == material)
                {
                    return surfaces[i];
                }
            }

            return fallback;
        }

        /// <summary>Footsteps for a surface at a given effort. Null when the surface is unauthored.</summary>
        public AudioClipBank? Steps(FloorMaterial material, FootstepActor actor)
        {
            var entry = For(material);
            if (entry == null)
            {
                return null;
            }

            switch (actor)
            {
                case FootstepActor.MonsterStep:
                    return entry.monsterStep;
                case FootstepActor.PlayerRun:
                    return entry.playerRun;
                default:
                    return entry.playerWalk;
            }
        }

        /// <summary>The room tone for a surface, or null.</summary>
        public AudioClip? Ambience(FloorMaterial material)
        {
            var entry = For(material);
            return entry != null ? entry.ambienceBed : null;
        }
    }

    /// <summary>
    /// Who is making the footstep. The three sets the clip library actually ships, and
    /// the distinction matters to more than volume: §04 gives the Listener the
    /// monster's steps as information and the player's own as interference.
    /// </summary>
    public enum FootstepActor
    {
        /// <summary>A player at <c>GameConstants.WalkSpeed</c>. Below §04's self-noise threshold.</summary>
        PlayerWalk = 0,

        /// <summary>A player running or sprinting. Above the threshold — §04: "뛰거나 … 정보가 끊긴다".</summary>
        PlayerRun = 1,

        /// <summary>The monster. §06's four audible states all produce these; 정지 produces none.</summary>
        MonsterStep = 2,
    }
}
