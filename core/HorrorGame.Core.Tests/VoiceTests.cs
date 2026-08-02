using HorrorGame.Core.Map;
using HorrorGame.Core.Voice;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// 근접 음성, and the one thing that makes it a game system rather than a chat feature:
    /// the creature is listening.
    /// </summary>
    public sealed class VoiceTests
    {
        [Test]
        public void A_whisper_reaches_the_person_beside_you_and_nobody_else()
        {
            Assert.That(VoiceRules.CanHear(VoiceEffort.Whisper, 2f, false), Is.True);
            Assert.That(VoiceRules.CanHear(VoiceEffort.Whisper, 8f, false), Is.False);
        }

        [Test]
        public void A_whisper_is_the_one_way_to_speak_that_cannot_kill_you()
        {
            foreach (FloorMaterial floor in System.Enum.GetValues(typeof(FloorMaterial)))
            {
                Assert.That(VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Whisper, floor), Is.EqualTo(0f),
                    "§01 has to leave one way to say something that is not also a way to die. "
                    + "Without it players stop speaking, and a race down a maze where nobody "
                    + "talks is not the game this system exists for. Surface " + floor);
            }
        }

        [Test]
        public void A_shout_carries_further_to_the_creature_than_to_the_people_you_are_shouting_at()
        {
            var toPlayers = VoiceRules.RangeMetres(VoiceEffort.Shout, false);
            var toMonster = VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Shout, FloorMaterial.Metal);

            Assert.That(toMonster, Is.GreaterThan(toPlayers),
                "That asymmetry is the whole reason a shout is a decision. If it only carried "
                + "as far as it was useful it would be a free upgrade to talking, and nobody "
                + "would ever choose to talk.");
        }

        [Test]
        public void Louder_is_further_in_both_directions()
        {
            Assert.That(VoiceRules.RangeMetres(VoiceEffort.Whisper, false),
                Is.LessThan(VoiceRules.RangeMetres(VoiceEffort.Talk, false)));
            Assert.That(VoiceRules.RangeMetres(VoiceEffort.Talk, false),
                Is.LessThan(VoiceRules.RangeMetres(VoiceEffort.Shout, false)));

            Assert.That(VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Talk, FloorMaterial.Metal),
                Is.LessThan(VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Shout, FloorMaterial.Metal)));
        }

        [Test]
        public void A_voice_survives_a_wall_far_better_than_a_footstep_does()
        {
            var clear = VoiceRules.RangeMetres(VoiceEffort.Talk, false);
            var through = VoiceRules.RangeMetres(VoiceEffort.Talk, true);

            Assert.That(through, Is.GreaterThan(clear * 0.5f),
                "Speech is 100~300 Hz and a wall is a low-pass — measured on this project's own "
                + "footstep set, the surfaces with their energy under 300 Hz lose almost nothing "
                + "through an occluder. Hearing a voice you cannot place is the sound this game "
                + "is made of, so most of the range has to survive.");
            Assert.That(through, Is.LessThan(clear));
        }

        [Test]
        public void The_middle_of_the_range_is_audible_rather_than_a_cliff()
        {
            // Linear roll-off on purpose. Inverse-square would put nearly all the audible
            // range in the first two metres, and the band where you can hear that somebody is
            // near without knowing where is the point.
            var half = VoiceRules.Gain(VoiceEffort.Talk, VoiceRules.TalkRange * 0.5f, false);

            Assert.That(half, Is.GreaterThan(0.35f).And.LessThan(0.65f));
        }

        [Test]
        public void Speaking_costs_you_the_sense_the_race_is_most_afraid_of_losing()
        {
            Assert.That(VoiceRules.SelfNoise(VoiceEffort.Silent), Is.EqualTo(0f));
            Assert.That(VoiceRules.SelfNoise(VoiceEffort.Whisper),
                Is.LessThan(GameConstants.ListenerSelfNoiseThreshold),
                "a whisper must not blind you, or there is no safe way to speak at all");
            Assert.That(VoiceRules.SelfNoise(VoiceEffort.Shout),
                Is.GreaterThan(GameConstants.ListenerSelfNoiseThreshold),
                "§10: 자기가 소리를 내면 못 듣는다. The role that rule was written for is gone; "
                + "the rule was never about the role.");
        }

        [Test]
        public void Carpet_swallows_a_voice_the_way_it_swallows_a_step()
        {
            var onCarpet = VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Talk, FloorMaterial.Carpet);
            var onMetal = VoiceRules.MonsterHearingRangeMetres(VoiceEffort.Talk, FloorMaterial.Metal);

            Assert.That(onCarpet, Is.LessThan(onMetal * 0.5f),
                "§12's surface alphabet decides where it is safe to talk, which gives the "
                + "eight floors a second axis of character beyond how they sound underfoot.");
        }
    }
}
