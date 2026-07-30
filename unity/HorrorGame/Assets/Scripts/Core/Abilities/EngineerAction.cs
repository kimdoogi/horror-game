using System;

namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// The five things 정비공 can do. §04: 문 잠금 · 구역 조명 · 소음 함정 · 차단물 설치,
    /// plus 금고를 연다 from the same row's 목표 관여.
    /// </summary>
    public enum EngineerAction
    {
        /// <summary>No action. Rejected by the ability rather than treated as instant.</summary>
        None = 0,

        /// <summary>문 잠금. §12 puts the lockable doors on the bottlenecks, so this is what cuts a 순환로.</summary>
        LockDoor = 1,

        /// <summary>
        /// 구역 조명 — a breaker, so it turns a zone on *or* off. §03: a lit zone lets
        /// several people read a clue at once, and "괴물도 그쪽으로 온다."
        /// </summary>
        ZoneLight = 2,

        /// <summary>소음 함정. §04 — it makes noise, and noise is what moves the monster (§06 순찰 → 경계).</summary>
        NoiseTrap = 3,

        /// <summary>차단물 설치. §10 states the price plainly: "경로를 차단한다 / 아군도 막힌다."</summary>
        Barricade = 4,

        /// <summary>금고를 연다. §08's 금고 속 문서 is weight 2 and high value, and only this role reaches it.</summary>
        OpenSafe = 5,
    }

    /// <summary>
    /// The §04 cost of each Engineer action, in the two currencies the section names:
    /// "시간과 자재."
    /// <para>
    /// Kept separate from <see cref="EngineerAbility"/> so a shop screen or a HUD can
    /// quote a price without owning an ability instance, and so both quote the same
    /// numbers as the thing that charges them.
    /// </para>
    /// </summary>
    public static class EngineerActions
    {
        /// <summary>
        /// Setup time in seconds. §04's constraint is that none of these is instant —
        /// "즉석 사용 불가 — 사전 준비형" — so every value here is strictly positive and
        /// the ability charges the whole of it before anything happens.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="action"/> is <see cref="EngineerAction.None"/> or unknown.</exception>
        public static float SetupSeconds(EngineerAction action)
        {
            switch (action)
            {
                case EngineerAction.LockDoor:
                    return GameConstants.EngineerDoorLockSeconds;
                case EngineerAction.ZoneLight:
                    return GameConstants.EngineerZoneLightSeconds;
                case EngineerAction.NoiseTrap:
                    return GameConstants.EngineerTrapSeconds;
                case EngineerAction.Barricade:
                    return GameConstants.EngineerBarricadeSeconds;
                case EngineerAction.OpenSafe:
                    return GameConstants.EngineerSafeSeconds;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(action),
                        action,
                        "No §04 setup time exists for this action. Returning zero here would make it instant, which §04 forbids.");
            }
        }

        /// <summary>
        /// 정비 자재 consumed. §16-4 lists the material types and quantities as still
        /// open, so these are the provisional values the simulator sweeps.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="action"/> is <see cref="EngineerAction.None"/> or unknown.</exception>
        public static int MaterialCost(EngineerAction action)
        {
            switch (action)
            {
                case EngineerAction.LockDoor:
                    return GameConstants.EngineerDoorLockMaterialCost;
                case EngineerAction.ZoneLight:
                    return GameConstants.EngineerZoneLightMaterialCost;
                case EngineerAction.NoiseTrap:
                    return GameConstants.EngineerTrapMaterialCost;
                case EngineerAction.Barricade:
                    return GameConstants.EngineerBarricadeMaterialCost;
                case EngineerAction.OpenSafe:
                    return GameConstants.EngineerSafeMaterialCost;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(action),
                        action,
                        "No §04 material cost exists for this action.");
            }
        }
    }
}
