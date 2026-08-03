#nullable enable

namespace HorrorGame.Gameplay.Player
{
    /// <summary>
    /// The nine poses <c>Player.fbx</c> ships, which is also the whole vocabulary other
    /// players have for reading what someone is doing.
    /// <para>
    /// §05 concedes that a first-person game still needs a body — "1인칭이어도 캐릭터
    /// 모델이 필요하다. 협동 게임에서는 다른 3명이 보여야 한다" — and §03 raises the stake:
    /// the last trip is 목표물 1명 + 호송 3명, and the escort has to be able to tell at a
    /// glance who is holding the objective, because the carrier "앞을 보지 못하므로 사실상
    /// A pose that looks like the wrong verb turns reading another runner into a guess.
    /// </para>
    /// </summary>
    public enum PlayerAnimationState
    {
        /// <summary>Standing.</summary>
        Idle = 0,

        /// <summary>Walking — §06's 2.0 m/s, the speed a runner can still hear over.</summary>
        Walk = 1,

        /// <summary>Running — §06's 4.5 m/s, or 5.6 sprinting. Loud enough to mask what you can hear.</summary>
        Run = 2,

        /// <summary>Crouched and still. §05 defines no crouch key; see <see cref="PlayerAnimatorDriver.Crouching"/>.</summary>
        Crouch = 3,

        /// <summary>Crouched and moving.</summary>
        CrouchWalk = 4,

        // DELETED with §03's 목표물 and §08's 대형 전리품: Carry = 5, CarryIdle = 6 and
        // CarryHeavy = 7. Nobody carries anything, so no runner can ever be in one of
        // these poses. The numbering is left sparse on purpose — Death stays 8, so the
        // serialised scenes and the animator's mixer indices do not silently re-point.

        /// <summary>Killed. §09 turns the player into a ghost; the body plays this once.</summary>
        Death = 8,
    }
}
