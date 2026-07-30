namespace HorrorGame.Core.Light
{
    /// <summary>
    /// Which of §03's 조명 수단 is answering for a position.
    /// <para>
    /// §03 hangs the objective and the danger on one switch — "목표와 위험이 같은
    /// 스위치에 걸린다" — but the three switches do not cost the same thing: a
    /// flashlight gives away the person holding it, a zone light gives away the room,
    /// and a flare gives away both and then dies. The rules combine them into one
    /// number (<see cref="LightSample.Quality"/>); this says which one won, so a HUD
    /// can tell a player *why* they are visible and telemetry can tell a designer
    /// which of §03's three options the team actually used.
    /// </para>
    /// </summary>
    public enum LightSourceKind
    {
        /// <summary>Darkness. §03's lock is engaged: nothing here can be read.</summary>
        None = 0,

        /// <summary>A player's own beam. §03: 빛이 좁다 · 괴물이 잘 본다.</summary>
        Flashlight = 1,

        /// <summary>정비공's 구역 조명. §03: 구역 전체가 밝다 · 여러 명이 동시에 읽는다.</summary>
        ZoneLight = 2,

        /// <summary>A burning 조명탄. §08: 1회용 · 소리를 낸다.</summary>
        Flare = 3,

        /// <summary>
        /// The host's own answer, from <see cref="Session.IWorldProbe.IsAreaLit"/>.
        /// Light this system does not track — a lit generator room, a map-authored
        /// fixture — still has to gate clue reading the same way, or §03's lock would
        /// mean two different things depending on who lit the room.
        /// </summary>
        AreaLit = 4,
    }
}
