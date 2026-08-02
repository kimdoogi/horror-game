using HorrorGame.Core.Math;

namespace HorrorGame.Core.Map
{
    /// <summary>What a door is doing.</summary>
    public enum DoorPhase
    {
        /// <summary>Open. Anything walks through, including the creature.</summary>
        Open,

        /// <summary>Shut. It blocks, and the creature has to break it.</summary>
        Shut,

        /// <summary>Shut, and something on the far side is working on it.</summary>
        Breaking,

        /// <summary>Broken open for the rest of the match. It cannot be shut again.</summary>
        Broken,
    }

    /// <summary>
    /// §12's 잠글 수 있는 문, as a thing a player can actually shut.
    /// <para>
    /// The map has placed one or two of these on every storey's bottleneck since the
    /// first sketch — <c>MapSketch.Door()</c> marks the edge and <c>MapValidator</c>
    /// checks the detour it forces is worth more than §06's release time — and none of
    /// them did anything. They were a validation fact. A door that cannot be shut is a
    /// doorway.
    /// </para>
    /// <para>
    /// <b>What shutting one buys, and what it costs.</b> §06's whole shape is that the
    /// creature is faster and never stops, so the only currency a player has is time.
    /// A shut door is <see cref="GameConstants.DoorBreakSeconds"/> of it — deliberately
    /// longer than <see cref="GameConstants.AggroReleaseLineOfSightBreak"/>, so a door is
    /// enough to break a chase if you are already round a corner, and deliberately less
    /// than a §07 patrol sweep, so it never makes a room safe. Shutting it costs
    /// <see cref="GameConstants.DoorShutSeconds"/> standing still with your back to
    /// whatever is coming, which is the only reason not to shut every door you pass.
    /// </para>
    /// <para>
    /// <b>A broken door stays broken.</b> The building degrades over a match: every door
    /// the creature has come through is a route that no longer costs it anything, so the
    /// same corridor is a different problem at 심야 than it was at 초저녁. That is §07's
    /// escalation expressed as geometry rather than as a speed.
    /// </para>
    /// </summary>
    public struct DoorState
    {
        private float _breakProgress;
        private float _shutProgress;

        /// <summary>What the door is doing. A default-constructed door is open.</summary>
        public DoorPhase Phase { get; private set; }

        /// <summary>0~1 of the way through being broken. 0 unless <see cref="DoorPhase.Breaking"/>.</summary>
        public float BreakProgress01
        {
            get
            {
                return Phase == DoorPhase.Breaking
                    ? MathX.Clamp01(_breakProgress / GameConstants.DoorBreakSeconds)
                    : 0f;
            }
        }

        /// <summary>0~1 of the way through being pulled shut by a player.</summary>
        public float ShutProgress01
        {
            get { return MathX.Clamp01(_shutProgress / GameConstants.DoorShutSeconds); }
        }

        /// <summary>True while nothing can pass — shut or being broken, but not yet broken.</summary>
        public bool Blocks
        {
            get { return Phase == DoorPhase.Shut || Phase == DoorPhase.Breaking; }
        }

        /// <summary>True when a player may still act on it. A broken door is scenery.</summary>
        public bool CanBeUsed
        {
            get { return Phase != DoorPhase.Broken; }
        }

        /// <summary>
        /// A player pulling the door shut, one tick at a time.
        /// </summary>
        /// <param name="deltaSeconds">Tick length.</param>
        /// <returns>True on the tick it closes.</returns>
        public bool Shut(float deltaSeconds)
        {
            if (Phase != DoorPhase.Open || deltaSeconds <= 0f)
            {
                return false;
            }

            _shutProgress += deltaSeconds;
            if (_shutProgress < GameConstants.DoorShutSeconds)
            {
                return false;
            }

            _shutProgress = 0f;
            _breakProgress = 0f;
            Phase = DoorPhase.Shut;
            return true;
        }

        /// <summary>
        /// A player letting go before it closed. The effort is lost, which is what makes
        /// shutting a door a commitment rather than a tap.
        /// </summary>
        public void AbandonShut()
        {
            _shutProgress = 0f;
        }

        /// <summary>Opening one is instant. Nobody has ever struggled to open a door away from themselves.</summary>
        /// <returns>True if it opened.</returns>
        public bool Open()
        {
            if (Phase != DoorPhase.Shut)
            {
                return false;
            }

            Phase = DoorPhase.Open;
            _breakProgress = 0f;
            return true;
        }

        /// <summary>
        /// The creature working on a shut door.
        /// </summary>
        /// <param name="deltaSeconds">Tick length. Zero or negative lets it recover.</param>
        /// <returns>True on the tick it breaks through.</returns>
        public bool Break(float deltaSeconds)
        {
            if (!Blocks)
            {
                return false;
            }

            if (deltaSeconds <= 0f)
            {
                return false;
            }

            Phase = DoorPhase.Breaking;
            _breakProgress += deltaSeconds;
            if (_breakProgress < GameConstants.DoorBreakSeconds)
            {
                return false;
            }

            Phase = DoorPhase.Broken;
            _breakProgress = 0f;
            return true;
        }

        /// <summary>
        /// Nothing is working on it this tick. Progress decays rather than resetting: a
        /// creature that was driven off by a §04 섬광수 has still weakened the door, and a
        /// player who bought thirty seconds with a flash does not get the whole door back.
        /// </summary>
        /// <param name="deltaSeconds">Tick length.</param>
        public void Relax(float deltaSeconds)
        {
            if (Phase != DoorPhase.Breaking || deltaSeconds <= 0f)
            {
                return;
            }

            _breakProgress -= deltaSeconds * GameConstants.DoorRepairFraction;
            if (_breakProgress <= 0f)
            {
                _breakProgress = 0f;
                Phase = DoorPhase.Shut;
            }
        }
    }
}
