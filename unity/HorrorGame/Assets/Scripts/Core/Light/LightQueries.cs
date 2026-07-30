using HorrorGame.Core.Math;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// How much light is on one point, and where it came from.
    /// <para>
    /// This is the single answer the clue system consumes: <see cref="Quality"/> goes
    /// straight into <c>ClueReadContext.LightQuality</c>. §03's lock — "어둠 = 목표의
    /// 잠금장치" — is one threshold on one number, so a flashlight beam, a 구역 조명 and a
    /// 조명탄 are the same rule seen three ways rather than three rules that happen to
    /// agree today.
    /// </para>
    /// </summary>
    public readonly struct LightSample
    {
        /// <summary>Light on the point, 0–1, from the strongest source.</summary>
        public readonly float Quality;

        /// <summary>Which source won. <see cref="LightSourceKind.None"/> in the dark.</summary>
        public readonly LightSourceKind Source;

        /// <summary>Creates a sample. Quality is clamped; NaN reads as darkness.</summary>
        public LightSample(float quality, LightSourceKind source)
        {
            Quality = float.IsNaN(quality) ? 0f : MathX.Clamp01(quality);
            Source = Quality > 0f ? source : LightSourceKind.None;
        }

        /// <summary>Darkness. §03's lock, engaged.</summary>
        public static LightSample Dark
        {
            get { return default(LightSample); }
        }

        /// <summary>
        /// True when a clue here could be read at all — <see cref="Quality"/> at or above
        /// §03's <see cref="GameConstants.ClueMinReadableLightQuality"/>.
        /// <para>
        /// The same threshold <c>ClueReader</c> interrupts on, deliberately: this is how "a
        /// dead battery means you cannot read a clue" is one fact rather than two systems'
        /// opinions.
        /// </para>
        /// </summary>
        public bool IsReadable
        {
            get { return Quality >= GameConstants.ClueMinReadableLightQuality; }
        }
    }

    /// <summary>
    /// Everything <see cref="LightRules.Visibility"/> needs, as primitives.
    /// <para>
    /// Bundled into a struct rather than passed as six positional arguments because five of
    /// them are floats and vectors that would be trivially swappable at a call site — and a
    /// swapped pair here would silently mis-price §03's central dilemma.
    /// </para>
    /// </summary>
    public readonly struct LightVisibilityQuery
    {
        /// <summary>Where the player is.</summary>
        public readonly Vec3 PlayerPosition;

        /// <summary>Where the monster is. The host's truth, not the Listener's estimate.</summary>
        public readonly Vec3 MonsterPosition;

        /// <summary>The player's own beam, or <see cref="LightCone.None"/>.</summary>
        public readonly LightCone Beam;

        /// <summary>
        /// How far the beam can be noticed from, metres.
        /// <see cref="FlashlightState.NoticeDistance"/>; 0 when unlit.
        /// </summary>
        public readonly float BeamNoticeDistance;

        /// <summary>Area light on the player, 0–1 — a 구역 조명 or a 조명탄 lighting them from outside.</summary>
        public readonly float AreaLightQuality;

        /// <summary>Which area light that was, for <see cref="PlayerVisibility.DominantSource"/>.</summary>
        public readonly LightSourceKind AreaLightKind;

        /// <summary>
        /// Whether the monster has an unbroken sight line to the player, from
        /// <see cref="Session.IWorldProbe.HasLineOfSight"/>. §12's whole cover argument is
        /// that this is the term that saves you.
        /// </summary>
        public readonly bool HasLineOfSight;

        /// <summary>Builds a query.</summary>
        public LightVisibilityQuery(
            Vec3 playerPosition,
            Vec3 monsterPosition,
            LightCone beam,
            float beamNoticeDistance,
            float areaLightQuality,
            LightSourceKind areaLightKind,
            bool hasLineOfSight)
        {
            PlayerPosition = playerPosition;
            MonsterPosition = monsterPosition;
            Beam = beam;
            BeamNoticeDistance = float.IsNaN(beamNoticeDistance) || beamNoticeDistance < 0f
                ? 0f
                : beamNoticeDistance;
            AreaLightQuality = float.IsNaN(areaLightQuality) ? 0f : MathX.Clamp01(areaLightQuality);
            AreaLightKind = areaLightKind;
            HasLineOfSight = hasLineOfSight;
        }
    }

    /// <summary>
    /// How much light is giving one player away right now.
    /// <para>
    /// The other half of §03's switch, and the reason this system exists as one system:
    /// <see cref="LightSample"/> answers "can this be read" and this answers "who can see
    /// me", from the same sources with the same falloff. §03 says the two are the same
    /// switch — "목표와 위험이 같은 스위치에 걸린다" — so they are the same code.
    /// </para>
    /// <para>
    /// This is <b>only the light term</b>. §06 gives the monster its own
    /// <see cref="GameConstants.MonsterSightRange"/> cone for bodies, and a score of 0 here
    /// means light is not helping it — not that the player is invisible. Perception must add
    /// the two, and must not re-apply distance to this one: the falloff is already in it.
    /// </para>
    /// </summary>
    public readonly struct PlayerVisibility
    {
        /// <summary>
        /// How conspicuous the player's light makes them, 0–1, at the monster's current
        /// distance. 0 in the dark, out of range, or behind cover.
        /// </summary>
        public readonly float Score;

        /// <summary>
        /// Farthest distance any of the player's light can be noticed from, metres. 0 when
        /// they are carrying no light and standing in none.
        /// </summary>
        public readonly float NoticeDistance;

        /// <summary>Horizontal distance to the monster, metres — the house convention for a range check.</summary>
        public readonly float DistanceToMonster;

        /// <summary>Whether the sight line was clear. False zeroes <see cref="Score"/>.</summary>
        public readonly bool HasLineOfSight;

        /// <summary>Which source is doing the most damage, for the HUD and for telemetry.</summary>
        public readonly LightSourceKind DominantSource;

        /// <summary>Builds a result.</summary>
        public PlayerVisibility(
            float score,
            float noticeDistance,
            float distanceToMonster,
            bool hasLineOfSight,
            LightSourceKind dominantSource)
        {
            Score = float.IsNaN(score) ? 0f : MathX.Clamp01(score);
            NoticeDistance = noticeDistance;
            DistanceToMonster = distanceToMonster;
            HasLineOfSight = hasLineOfSight;
            DominantSource = dominantSource;
        }

        /// <summary>
        /// True when the monster can pick the player out by their light: close enough, and
        /// with a sight line. This is the flag §08's 강화 손전등 doubles the reach of.
        /// </summary>
        public bool IsNoticed
        {
            get { return HasLineOfSight && NoticeDistance > 0f && DistanceToMonster <= NoticeDistance; }
        }
    }

    /// <summary>
    /// A light the monster is drawn towards. §03 prices both area lights this way — 구역
    /// 조명's "괴물도 그쪽으로 온다" is the entire cost of the Engineer's best contribution to
    /// the objective, and §08's 조명탄 inherits it.
    /// <para>
    /// Primitives only, so the monster brain can walk to one without importing this
    /// namespace (ARCHITECTURE §3). It reads exactly like a sound cue with no expiry.
    /// </para>
    /// </summary>
    public readonly struct LightLure
    {
        /// <summary>Somewhere to walk to. A flare's own position; a zone light's breaker panel.</summary>
        public readonly Vec3 Position;

        /// <summary>How far the light reaches from there, metres.</summary>
        public readonly float Radius;

        /// <summary>Whether this is a 구역 조명 or a 조명탄 — the flare will go out, the breaker will not.</summary>
        public readonly LightSourceKind Kind;

        /// <summary>Zone id for a 구역 조명, flare id for a 조명탄.</summary>
        public readonly int SourceId;

        /// <summary>Builds a lure.</summary>
        public LightLure(Vec3 position, float radius, LightSourceKind kind, int sourceId)
        {
            Position = position;
            Radius = radius;
            Kind = kind;
            SourceId = sourceId;
        }
    }
}
