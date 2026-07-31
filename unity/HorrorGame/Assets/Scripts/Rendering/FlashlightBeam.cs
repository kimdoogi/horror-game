#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Rendering
{
    /// <summary>
    /// The one description of §03's flashlight beam, shared by the thing the player
    /// holds and every tool that photographs it.
    /// <para>
    /// This exists because the two disagreed. <c>PlayerFlashlight</c> ran the beam
    /// at intensity 6 with hard shadows; the review rig in <c>SceneShot</c> built
    /// its own at 3.5 with soft ones. Every screenshot the art passes were judged
    /// against was therefore a picture of a light that does not exist in the game,
    /// and it was the dimmer of the two — so the beam looked better in review than
    /// in play, which is the direction of error that gets a look signed off and
    /// then shipped wrong.
    /// </para>
    /// <para>
    /// §03 makes darkness the lock on the objective and the beam the only key, so
    /// this is not a cosmetic constant. It is the exposure key for the whole game:
    /// every ambient value in <see cref="NightAtmosphere"/> is chosen as a ratio
    /// against it.
    /// </para>
    /// </summary>
    public static class FlashlightBeam
    {
        /// <summary>
        /// Beam intensity.
        /// <para>
        /// Lowered from the 6 the player rig used. At 6, a wall inside about two
        /// metres — which is most of a §12 corridor, whose clear section is 2.2 m —
        /// clipped to pure white through the Neutral tonemap at
        /// <c>+1.15 EV</c>. A clipped hotspot is worse than a dim one: it throws
        /// away the texture detail in the one part of the frame the player is
        /// actually looking at, so the expensive floor materials read as white
        /// paper exactly when they are closest to the camera.
        /// </para>
        /// <para>
        /// 2.6 puts a wall at 1 m near the top of the range without clipping and
        /// still lands visibly at <see cref="GameConstants.FlashlightRange"/>.
        /// Verified by rendering, not by theory — see the blown-highlight
        /// percentages in docs/ART.md.
        /// </para>
        /// </summary>
        public const float Intensity = 2.6f;

        /// <summary>
        /// Slightly warm white. A neutral beam in a scene graded this cold reads as
        /// a second moon rather than as something the player is carrying; the warmth
        /// is what separates "my torch" from "the environment".
        /// </summary>
        public static readonly Color Colour = new Color(1f, 0.96f, 0.87f);

        /// <summary>
        /// Hard shadows, matching the player rig.
        /// <para>
        /// Not a performance choice. §03's beam has to be blocked by crates, doors
        /// and other players for hiding to mean anything, and a soft edge on a spot
        /// this narrow reads as fog rather than as an object's shadow — the
        /// silhouette of the monster in a doorway is the shot the whole game is
        /// selling.
        /// </para>
        /// </summary>
        public const LightShadows Shadows = LightShadows.Hard;

        /// <summary>Configures <paramref name="light"/> as §03's beam. Leaves the transform alone.</summary>
        public static void Apply(Light light)
        {
            light.type = LightType.Spot;
            light.range = GameConstants.FlashlightRange;
            light.spotAngle = GameConstants.FlashlightHalfAngle * 2f;
            light.intensity = Intensity;
            light.color = Colour;
            light.shadows = Shadows;
        }
    }
}
