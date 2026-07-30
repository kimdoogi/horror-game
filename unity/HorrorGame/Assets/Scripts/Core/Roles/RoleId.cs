namespace HorrorGame.Core.Roles
{
    /// <summary>
    /// The five roles of §04. Four are picked per match, so exactly one is always
    /// absent — §11 treats that absence as the match's character rather than a
    /// gap to be patched.
    /// </summary>
    public enum RoleId
    {
        /// <summary>No role assigned yet (lobby default).</summary>
        None = 0,

        /// <summary>청음사 — reads the monster by sound, and only while silent itself. §04.</summary>
        Listener = 1,

        /// <summary>관측자 — sees who the monster is targeting, at 15 m and standing still. §04.</summary>
        Observer = 2,

        /// <summary>주자 — the only role that outruns the monster. §04.</summary>
        Runner = 3,

        /// <summary>정비공 — doors, zone lights, traps, safes. The most dangerous ally. §04.</summary>
        Engineer = 4,

        /// <summary>섬광수 — a weak but reusable stun. §04.</summary>
        Flasher = 5,
    }
}
