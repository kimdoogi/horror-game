namespace HorrorGame.Core.Race
{
    /// <summary>Why a shot did nothing.</summary>
    public enum ShotRefusal
    {
        /// <summary>It hit.</summary>
        None = 0,

        /// <summary>The shooter is not holding a gun, or the one they had is spent.</summary>
        NoGun = 1,

        /// <summary>Nobody shoots themselves back to the start line.</summary>
        SelfShot = 2,

        /// <summary>Further than <see cref="Gunplay.RangeMetres"/>.</summary>
        TooFar = 3,

        /// <summary>The target has finished or their seat is empty. There is nothing to send back.</summary>
        NotRacing = 4,

        /// <summary>One of the two ids is not a seat in this race.</summary>
        NotASeat = 5,
    }

    /// <summary>
    /// The gun, as a rule: what it can do and what it costs.
    /// <para>
    /// <b>What it is for.</b> §02 is a race down eight mazes and the only thing that
    /// currently interferes with another runner is a door you can shut behind you. A gun
    /// on the floor gives the field one more way to spend a detour: leave your route, arm
    /// yourself, and hold a thing you can use exactly once on exactly one person. It is the
    /// same shape of decision §12 already makes with the 관문 — a cost now against an
    /// advantage later — and it makes the corridor behind you worth looking at.
    /// </para>
    /// <para>
    /// <b>One shot, and the gun is gone.</b> Not a magazine, not a reload. Twenty players
    /// with repeating fire turns a maze into a shooting gallery and the descent stops being
    /// the game; one shot is a decision you make once and then have to live with, and it
    /// keeps the answer to "somebody has a gun" from being "stay away from everybody".
    /// </para>
    /// <para>
    /// <b>Being shot costs exactly what being caught costs.</b> Back to your own starting
    /// cell on B1, still racing, nothing else. Two ways to lose a descent and one price, so
    /// a player never has to learn a second rule about what a setback means — and so the gun
    /// cannot be worse for the game than the creature already is.
    /// </para>
    /// <para>
    /// This type decides; it does not act. <c>RaceState</c> is what records the setback and
    /// the Unity side is what moves a body, for the same reason every other rule in this
    /// folder is engine-free: the question "was that a legal shot" is the single most
    /// attractive thing in a race to lie about, and §13 answers it on the host.
    /// </para>
    /// </summary>
    public static class Gunplay
    {
        /// <summary>
        /// Shots a gun found on the floor carries. One.
        /// <para>
        /// See the type's own note. It is a constant rather than a literal because
        /// <c>GunPickup</c> and the HUD both have to agree with the rule about how many
        /// shots a runner has left, and two places saying "1" is two places to change.
        /// </para>
        /// </summary>
        public const int ShotsPerGun = 1;

        /// <summary>
        /// How far a shot reaches, metres.
        /// <para>
        /// <b>The dark is the balance.</b> This is <c>GameConstants.FlashlightRange</c> —
        /// you can shoot exactly as far as your own light shows you, and not one metre
        /// past. A longer range would let a runner kill something they can only hear, which
        /// is a coin flip rather than a decision; a shorter one would make the gun a melee
        /// weapon in a game where the other runner is always moving away from you.
        /// </para>
        /// <para>
        /// It is also under §06's <c>MonsterSightRange</c> of 20 m on purpose: the creature
        /// notices you from further away than you can shoot, so drawing on somebody in an
        /// open stretch is a decision that can be overheard.
        /// </para>
        /// </summary>
        public const float RangeMetres = GameConstants.FlashlightRange;

        /// <summary>
        /// Whether a shot is legal, and why not when it is not.
        /// <para>
        /// Range is compared in the plan, without the vertical: two runners on different
        /// storeys are 3.75 m apart and behind a concrete slab, and the caller has already
        /// established line of sight — a rule that measured the diagonal would let a shot
        /// through a floor be legal on a technicality.
        /// </para>
        /// </summary>
        /// <param name="race">The standings, which know who is still running.</param>
        /// <param name="shooterId">Seat firing.</param>
        /// <param name="targetId">Seat being fired at.</param>
        /// <param name="shotsLeft">Shots the shooter's gun has. 0 is no gun.</param>
        /// <param name="metresApart">Distance between them in the plan, metres.</param>
        /// <returns><see cref="ShotRefusal.None"/> when the shot lands.</returns>
        public static ShotRefusal Judge(RaceState race, int shooterId, int targetId, int shotsLeft, float metresApart)
        {
            if (race == null)
            {
                return ShotRefusal.NotASeat;
            }

            if (shooterId < 0 || shooterId >= race.Count || targetId < 0 || targetId >= race.Count)
            {
                return ShotRefusal.NotASeat;
            }

            if (shooterId == targetId)
            {
                return ShotRefusal.SelfShot;
            }

            if (shotsLeft <= 0)
            {
                return ShotRefusal.NoGun;
            }

            // A shooter who has already finished or left is checked too, and deliberately
            // not with a message of its own: what matters to the caller is that the shot did
            // not land, and "the target is not racing" is the true reason in both cases —
            // there is a seat in this exchange that the race has stopped caring about.
            if (race[shooterId].Status != RacerStatus.Running
                || race[targetId].Status != RacerStatus.Running)
            {
                return ShotRefusal.NotRacing;
            }

            if (metresApart > RangeMetres)
            {
                return ShotRefusal.TooFar;
            }

            return ShotRefusal.None;
        }

        /// <summary>
        /// Fires, if the shot is legal, and sends the target back to B1.
        /// <para>
        /// The same call the creature makes — <see cref="RaceState.ReportCaught"/> — so the
        /// setback is one rule with two causes. A gun that had its own kind of setback would
        /// be a second thing for a player to learn and a second thing for the standings to
        /// disagree about.
        /// </para>
        /// </summary>
        /// <param name="race">The standings.</param>
        /// <param name="shooterId">Seat firing.</param>
        /// <param name="targetId">Seat being fired at.</param>
        /// <param name="shotsLeft">Shots the shooter's gun has.</param>
        /// <param name="metresApart">Distance between them in the plan, metres.</param>
        /// <param name="elapsedSeconds">Seconds since the start.</param>
        /// <returns><see cref="ShotRefusal.None"/> when the target was sent back.</returns>
        public static ShotRefusal Fire(
            RaceState race,
            int shooterId,
            int targetId,
            int shotsLeft,
            float metresApart,
            float elapsedSeconds)
        {
            var refusal = Judge(race, shooterId, targetId, shotsLeft, metresApart);
            if (refusal != ShotRefusal.None)
            {
                return refusal;
            }

            // Judge has already established the target is Running, so this cannot refuse —
            // but it is the call that actually moves them, and letting its answer through
            // rather than discarding it means a future guard inside RaceState cannot be
            // silently defeated by this one.
            return race.ReportCaught(targetId, elapsedSeconds)
                ? ShotRefusal.None
                : ShotRefusal.NotRacing;
        }
    }
}
