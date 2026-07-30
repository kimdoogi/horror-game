using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// A burning 조명탄. §08 sells it as "구역을 밝힌다 (정비공 없이) · 1회용 · 소리를 낸다",
    /// and §11 names it the paid stand-in for a missing 정비공.
    /// <para>
    /// It is a value, not an object with behaviour: <see cref="LightField"/> owns the list
    /// and counts them down, so a flare cannot be ticked twice or ticked by a client. §13
    /// puts the host in charge of what is lit, because what is lit decides what can be
    /// read.
    /// </para>
    /// </summary>
    public readonly struct Flare
    {
        /// <summary>Host-assigned id, unique within a match. -1 is never a real flare.</summary>
        public readonly int Id;

        /// <summary>Where it landed, snapped onto navigable ground — a flare lies on the floor.</summary>
        public readonly Vec3 Position;

        /// <summary>Burn time left, seconds. Reaches exactly 0 and no lower.</summary>
        public readonly float RemainingSeconds;

        /// <summary>Creates a flare record.</summary>
        public Flare(int id, Vec3 position, float remainingSeconds)
        {
            Id = id;
            Position = position;
            RemainingSeconds = float.IsNaN(remainingSeconds) || remainingSeconds < 0f ? 0f : remainingSeconds;
        }

        /// <summary>Burn time left as a fraction of <see cref="GameConstants.FlareSeconds"/>. The HUD's fuse.</summary>
        public float BurnFraction
        {
            get { return MathX.Clamp01(RemainingSeconds / GameConstants.FlareSeconds); }
        }

        /// <summary>
        /// True while it still lights anything. §08's "1회용" is enforced by the flare being
        /// dropped from <see cref="LightField.BurningFlares"/> when this goes false — there
        /// is no path that relights one.
        /// </summary>
        public bool IsBurning
        {
            get { return RemainingSeconds > 0f; }
        }

        /// <summary>Radius it illuminates, metres. §03 — the same radius as a 구역 조명, which is what makes it a substitute at all.</summary>
        public float Radius
        {
            get { return GameConstants.ZoneLightRadius; }
        }

        /// <summary>This flare with a new burn time, for the host's countdown.</summary>
        public Flare WithRemaining(float remainingSeconds)
        {
            return new Flare(Id, Position, remainingSeconds);
        }
    }

    /// <summary>
    /// The moment a 조명탄 is struck: a light appears and a noise happens.
    /// <para>
    /// The noise is the half that has to leave this system. §08 prices the flare with
    /// "소리를 낸다" and §06 moves the monster from 순찰 to 경계 on a sound, so the monster
    /// brain needs this — as primitives, never as a light type (ARCHITECTURE §3). §04's
    /// Listener is caught by it too: at
    /// <see cref="GameConstants.FlareIgniteNoiseLevel"/> the crack is above
    /// <see cref="GameConstants.ListenerSelfNoiseThreshold"/>, so a team that lights its
    /// own flare goes deaf for the instant it most wants to hear.
    /// </para>
    /// </summary>
    public readonly struct FlareIgnition
    {
        /// <summary>Which flare. Matches <see cref="Flare.Id"/>.</summary>
        public readonly int FlareId;

        /// <summary>Where the light and the noise are.</summary>
        public readonly Vec3 Position;

        /// <summary>Noise level, 0–1, on §04's scale. <see cref="GameConstants.FlareIgniteNoiseLevel"/>.</summary>
        public readonly float NoiseLevel;

        /// <summary>Radius the flare will light, metres. §03.</summary>
        public readonly float Radius;

        /// <summary>Seconds it will burn. §08's fixed 1회용 duration.</summary>
        public readonly float BurnSeconds;

        /// <summary>Creates an ignition record.</summary>
        public FlareIgnition(int flareId, Vec3 position, float noiseLevel, float radius, float burnSeconds)
        {
            FlareId = flareId;
            Position = position;
            NoiseLevel = noiseLevel;
            Radius = radius;
            BurnSeconds = burnSeconds;
        }
    }
}
