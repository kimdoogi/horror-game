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
    /// 2인 1조 호송". A carry that looks like a walk turns that into a guess.
    /// </para>
    /// </summary>
    public enum PlayerAnimationState
    {
        /// <summary>Standing. §04's Observer spends three seconds here on purpose.</summary>
        Idle = 0,

        /// <summary>Walking — §06's 2.0 m/s. The speed a Listener can still hear over (§04).</summary>
        Walk = 1,

        /// <summary>Running — §06's 4.5 m/s, or the Runner's 5.6. Loud enough to blind the Listener.</summary>
        Run = 2,

        /// <summary>Crouched and still. §05 defines no crouch key; see <see cref="PlayerAnimatorDriver.Crouching"/>.</summary>
        Crouch = 3,

        /// <summary>Crouched and moving.</summary>
        CrouchWalk = 4,

        /// <summary>Moving with §03's objective in both hands. Must not be mistakable for <see cref="Walk"/>.</summary>
        Carry = 5,

        /// <summary>Standing with §03's objective. The pose an escort looks for.</summary>
        CarryIdle = 6,

        /// <summary>Visibly overloaded — §08's 대형 전리품, or a bag past the heavy band.</summary>
        CarryHeavy = 7,

        /// <summary>Killed. §09 turns the player into a ghost; the body plays this once.</summary>
        Death = 8,
    }
}
