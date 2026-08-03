#nullable enable

using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// What the owning client's controller hands to the network each tick.
    /// <para>
    /// The seam is deliberately primitives only (ARCHITECTURE §3): the Net layer
    /// must not import the movement controller to learn where a player is looking,
    /// and the controller must not import Mirror to be sendable. Every member here
    /// is a float, a bool or a Unity vector.
    /// </para>
    /// <para>
    /// Implemented by the first-person controller in <c>Assets/Scripts/Gameplay/</c>.
    /// When nothing implements it — a test scene, a spectator, a headless host —
    /// <see cref="NetPlayer"/> simply sends nothing, which is the correct behaviour
    /// for an avatar nobody is driving.
    /// </para>
    /// </summary>
    public interface INetPlayerViewSource
    {
        /// <summary>World position of the character's feet. §05's 위치 row.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// Camera yaw in degrees. §05: "마우스 방향 = 이동 방향" — this is the same
        /// angle the speed table (§05) resolves movement against, so a mismatch here
        /// would show a teammate running one way and facing another.
        /// </summary>
        float YawDegrees { get; }

        /// <summary>
        /// Camera pitch in degrees, negative looking down. §05 needs it because
        /// "바닥·천장을 비추는 것도 신호" — where the beam lands is the message.
        /// </summary>
        float PitchDegrees { get; }

        /// <summary>
        /// Whether the flashlight is lit. §05 lists it because it "괴물 인지에 영향" —
        /// the monster's notice distance (§06) depends on it, so it is a rules input
        /// on the host, not just a light on a client.
        /// </summary>
        bool FlashlightOn { get; }


        /// <summary>
        /// Sprint stamina, 0–1, exact. §05: "스태미나 (주자) — 본인만 정확히,
        /// 남은 대략." This value never crosses the wire at this precision; see
        /// <see cref="NetPlayer.ApproximateStaminaFraction"/>.
        /// </summary>
        float StaminaFraction { get; }
    }
}
