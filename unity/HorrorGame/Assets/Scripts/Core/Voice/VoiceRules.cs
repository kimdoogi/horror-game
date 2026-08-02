using HorrorGame.Core.Map;

namespace HorrorGame.Core.Voice
{
    /// <summary>How hard someone is speaking. The only voice control the game has.</summary>
    public enum VoiceEffort
    {
        /// <summary>Not speaking.</summary>
        Silent = 0,

        /// <summary>속삭임. A few metres, and the creature will not come for it.</summary>
        Whisper = 1,

        /// <summary>보통. Talking distance. Enough to work with somebody beside you.</summary>
        Talk = 2,

        /// <summary>외침. Across a band — and the creature hears it from further than that.</summary>
        Shout = 3,
    }

    /// <summary>
    /// 근접 음성 — who hears whom, how loudly, and what it costs.
    /// <para>
    /// <b>Speaking is a game action, not a chat feature.</b> §06's creature leaves 순찰 on one
    /// transition and one only — 소리 감지 → 경계 — and a voice is a sound. So this type does
    /// two things at once: it decides what the other players hear, and it hands
    /// <c>MatchDirector</c> a range to report to the monster. Talking is how you coordinate a
    /// descent, and it is how the thing in the dark finds out where you are. That trade is the
    /// reason to have voice in this game at all; a chat channel that cost nothing would make
    /// the maze easier and the game less frightening in the same move.
    /// </para>
    /// <para>
    /// <b>Three efforts, and the middle one is the interesting one.</b> A whisper reaches
    /// somebody beside you and almost nothing else — the creature's threshold is above it, so
    /// whispering is genuinely safe and genuinely limited. A shout crosses a band and brings
    /// the creature from further than it carries, which makes it a real decision rather than a
    /// louder version of talking. Ordinary speech sits between: useful, and audible to
    /// something on the other side of a wall.
    /// </para>
    /// <para>
    /// <b>The wall matters, and it is the same wall the footsteps go through.</b> Voice is
    /// low-frequency and a wall is a low-pass, so speech survives occlusion far better than a
    /// footstep does — which is why you can hear someone on the far side of a gate and cannot
    /// tell where they are. That is <see cref="OccludedFraction"/>, and it is deliberately
    /// generous: hearing a voice you cannot place is the sound this game is made of.
    /// </para>
    /// <para>
    /// Engine-free, so the rule is one thing and the transport is another.
    /// </para>
    /// </summary>
    public static class VoiceRules
    {
        /// <summary>
        /// How much of a voice survives a wall, as a fraction of its clear-air range.
        /// <para>
        /// Speech is 100~300 Hz of fundamental and a wall passes low frequencies — measured on
        /// this project's own footstep set, the surfaces with energy under 300 Hz lose almost
        /// nothing through an occluder while the bright ones lose 20 dB. A voice behaves like
        /// the dark surfaces. So most of the range survives, and what is lost is the part that
        /// tells you WHERE it came from.
        /// </para>
        /// </summary>
        public const float OccludedFraction = 0.62f;

        /// <summary>Clear-air range of a whisper, metres. Somebody beside you, and nobody else.</summary>
        public const float WhisperRange = 4f;

        /// <summary>Clear-air range of ordinary speech, metres. A corridor.</summary>
        public const float TalkRange = 12f;

        /// <summary>Clear-air range of a shout, metres. §12-A's band is 5~7.5 m thick, so this crosses one.</summary>
        public const float ShoutRange = 30f;

        /// <summary>
        /// How far a voice carries to the other players.
        /// </summary>
        /// <param name="effort">How hard they are speaking.</param>
        /// <param name="occluded">True when a wall stands between speaker and listener.</param>
        public static float RangeMetres(VoiceEffort effort, bool occluded)
        {
            var clear = ClearRange(effort);
            return occluded ? clear * OccludedFraction : clear;
        }

        /// <summary>Clear-air range of an effort, metres.</summary>
        /// <param name="effort">How hard they are speaking.</param>
        public static float ClearRange(VoiceEffort effort)
        {
            switch (effort)
            {
                case VoiceEffort.Whisper:
                    return WhisperRange;
                case VoiceEffort.Talk:
                    return TalkRange;
                case VoiceEffort.Shout:
                    return ShoutRange;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// How far the same voice carries to §06's creature, metres. Zero means it never hears.
        /// <para>
        /// <b>Not the same number as <see cref="RangeMetres"/>, and the gap is the design.</b>
        /// A shout reaches other players 30 m and the creature further than that, because the
        /// point of shouting is that it is reckless — if it only carried as far as it was
        /// useful, it would be a free upgrade to talking. A whisper reaches the creature not at
        /// all, so there is always a way to say something. Ordinary speech sits between and is
        /// the one you have to think about.
        /// </para>
        /// </summary>
        /// <param name="effort">How hard they are speaking.</param>
        /// <param name="floor">§12's surface underfoot. A room's own acoustics carry a voice the way they carry a step.</param>
        public static float MonsterHearingRangeMetres(VoiceEffort effort, FloorMaterial floor)
        {
            switch (effort)
            {
                case VoiceEffort.Whisper:
                    // Below the creature's threshold by construction. §01 has to leave one way
                    // to speak that is not also a way to die, or players stop speaking at all
                    // and the game loses the thing this system is for.
                    return 0f;

                case VoiceEffort.Talk:
                    return TalkRange * MapZone.ClarityOf(floor);

                case VoiceEffort.Shout:
                    // Further than it is useful. That asymmetry is what makes a shout a
                    // decision rather than a better microphone.
                    return ShoutRange * 1.4f * MapZone.ClarityOf(floor);

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// How loudly a listener hears a speaker, 0~1, at <paramref name="distanceMetres"/>.
        /// <para>
        /// Linear roll-off rather than inverse-square. Inverse-square is physically right and
        /// wrong here: it puts almost all the audible range in the first couple of metres, so a
        /// voice is either shouting-loud or gone, and the band in between — where you can hear
        /// that somebody is near without knowing where — is the whole point.
        /// </para>
        /// </summary>
        /// <param name="effort">How hard they are speaking.</param>
        /// <param name="distanceMetres">Between the two of them.</param>
        /// <param name="occluded">True when a wall stands between them.</param>
        public static float Gain(VoiceEffort effort, float distanceMetres, bool occluded)
        {
            var range = RangeMetres(effort, occluded);
            if (range <= 0f || distanceMetres >= range)
            {
                return 0f;
            }

            var gain = 1f - (System.Math.Max(0f, distanceMetres) / range);

            // A wall takes level as well as range. Not much — see OccludedFraction — but
            // enough that "through a wall" and "round the corner" do not sound identical.
            return occluded ? gain * 0.7f : gain;
        }

        /// <summary>
        /// True when the listener hears the speaker at all.
        /// </summary>
        /// <param name="effort">How hard they are speaking.</param>
        /// <param name="distanceMetres">Between the two of them.</param>
        /// <param name="occluded">True when a wall stands between them.</param>
        public static bool CanHear(VoiceEffort effort, float distanceMetres, bool occluded) =>
            Gain(effort, distanceMetres, occluded) > 0f;

        /// <summary>
        /// What speaking costs the speaker's own ears, 0~1, to be folded into the same
        /// self-noise <c>NoiseMeter</c> already computes from movement.
        /// <para>
        /// §10's dilemma was written for the 청음사 — 자기가 소리를 내면 못 듣는다 — and the
        /// role is gone but the rule was never about the role. It is about the fact that a
        /// player who is talking cannot hear the corridor. Keeping it means the race's most
        /// useful action has a cost paid in the sense the race is most afraid of losing.
        /// </para>
        /// </summary>
        /// <param name="effort">How hard they are speaking.</param>
        public static float SelfNoise(VoiceEffort effort)
        {
            switch (effort)
            {
                case VoiceEffort.Whisper:
                    return 0.10f;
                case VoiceEffort.Talk:
                    return 0.40f;
                case VoiceEffort.Shout:
                    return 0.95f;
                default:
                    return 0f;
            }
        }
    }
}
