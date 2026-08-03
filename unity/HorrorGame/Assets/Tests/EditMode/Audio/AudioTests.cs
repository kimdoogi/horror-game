#nullable enable

using HorrorGame.Audio;
using HorrorGame.Core;
using HorrorGame.Core.Map;
using HorrorGame.Core.Monster;
using NUnit.Framework;
using UnityEngine;

namespace HorrorGame.Tests.EditMode.Audio
{
    /// <summary>
    /// Assertions about the mix's reasoning, not about its numbers.
    /// <para>
    /// Every test here fails when a design argument stops holding — the occlusion floor
    /// stops protecting §12's alphabet, the rolloff stops reaching §04's range, §04's
    /// self-noise ordering inverts. A test that restated a constant would pass however
    /// wrong the constant became, which ARCHITECTURE §5 calls useless.
    /// </para>
    /// </summary>
    public sealed class AudioTests
    {
        // ====================================================================
        // F-003 — identity survives occlusion.
        // ====================================================================

        /// <summary>
        /// The measured floor. Sweeping the corner across the shipped monster
        /// footsteps through a model of Unity's own filter, 796 Hz is the lowest corner
        /// from which every surface pair clears F-003's 1.4× with 5% to spare. If this
        /// constant is lowered without re-running <c>tools/audio/verify_audio.py</c> and
        /// the sweep behind it, the 청음사 loses the ability to name a floor through a
        /// wall — which §04 says is the whole role.
        /// </summary>
        private const float MeasuredIdentityFloorHz = 796f;

        [Test]
        public void OcclusionFloor_StaysAboveTheMeasuredIdentityLimit()
        {
            Assert.That(AudioTuning.ListenerChannelOcclusionFloorHz,
                Is.GreaterThanOrEqualTo(MeasuredIdentityFloorHz),
                "F-003 / §12: below the measured floor the five surfaces stop separating by 1.4x, "
                + "and the Listener can no longer tell 나무 from 금속 through a wall.");
        }

        [Test]
        public void FullyOccludedCutoff_IsExactlyTheFloor()
        {
            var profile = OcclusionProfile.ListenerChannel;

            Assert.That(AudioOcclusion.CutoffHz(profile, 1f),
                Is.EqualTo(AudioTuning.ListenerChannelOcclusionFloorHz).Within(1f),
                "F-003: occlusion may take the cue's precision, never its identity — so the "
                + "corner has to stop at the floor rather than continue toward zero.");

            Assert.That(AudioOcclusion.CutoffHz(profile, 2f),
                Is.EqualTo(AudioTuning.ListenerChannelOcclusionFloorHz).Within(1f),
                "An out-of-range occlusion must clamp, not extrapolate past the floor.");
        }

        [Test]
        public void UnoccludedCutoff_IsTransparent()
        {
            Assert.That(AudioOcclusion.CutoffHz(OcclusionProfile.ListenerChannel, 0f),
                Is.GreaterThanOrEqualTo(20000f),
                "A clear path must not colour the cue at all: §12's dry separation is the "
                + "best case the alphabet ever gets, and the mix must not spend any of it.");
        }

        [Test]
        public void OcclusionCutoff_FallsMonotonicallyWithOcclusion()
        {
            var profile = OcclusionProfile.ListenerChannel;
            var previous = float.PositiveInfinity;

            for (var i = 0; i <= 20; i++)
            {
                var cutoff = AudioOcclusion.CutoffHz(profile, i / 20f);
                Assert.That(cutoff, Is.LessThanOrEqualTo(previous + 0.01f),
                    "More geometry in the way must never make a sound brighter.");
                previous = cutoff;
            }
        }

        [Test]
        public void VoiceIsFilteredLessThanTheListenerChannel_AndWorldMore()
        {
            Assert.That(OcclusionProfile.Voice.FloorCutoffHz,
                Is.GreaterThan(OcclusionProfile.ListenerChannel.FloorCutoffHz),
                "§13 gives the race proximity voice and §11 puts up to twenty runners in one "
                + "building, so speech must keep its consonant band through a wall even though "
                + "footsteps need not. The rule outlived its original reason — §03's 단서 that had "
                + "to be spoken aloud, deleted 2026-08-03 — because what people now shout at each "
                + "other through a door (「거기 문 닫혔어」) has to arrive as words.");

            Assert.That(OcclusionProfile.World.FloorCutoffHz,
                Is.LessThan(OcclusionProfile.ListenerChannel.FloorCutoffHz),
                "wall penetration is a mechanic on the channel a player listens THROUGH a wall "
                + "with — footsteps and the creature — and not on ambience. A door hinge carries "
                + "no information and may be filtered realistically.");
        }

        [Test]
        public void OcclusionAttenuation_IsGentlestOnTheChannelTheListenerReads()
        {
            Assert.That(AudioTuning.ListenerChannelOcclusionAttenuationDb,
                Is.GreaterThan(AudioTuning.WorldOcclusionAttenuationDb),
                "F-002 measures the 800 Hz filter alone as removing 25.8 dB from 자갈. Adding "
                + "a realistic broadband wall loss on top of that would delete §04's role.");
        }

        // ====================================================================
        // F-002 — the inversion the mix reproduces and must not hide.
        // ====================================================================

        [Test]
        public void OccludedAudibility_InvertsTheClarityTable_AsF002Reports()
        {
            var gravelClarity = MapZone.ClarityOf(FloorMaterial.Gravel);
            var concreteClarity = MapZone.ClarityOf(FloorMaterial.Concrete);

            Assert.That(gravelClarity, Is.GreaterThan(concreteClarity),
                "GameConstants still ranks 자갈 above 콘크리트, which is what F-002 is about.");

            var gravelHeard = AudioOcclusion.OccludedLevelChangeDb(FloorMaterial.Gravel);
            var concreteHeard = AudioOcclusion.OccludedLevelChangeDb(FloorMaterial.Concrete);

            Assert.That(gravelHeard, Is.LessThan(concreteHeard - 20f),
                "F-002: through a wall the measured audibility inverts by more than 20 dB. "
                + "This test exists to fail loudly if the finding is ever 'fixed' by quietly "
                + "changing the mix — the resolution is a Core decision (F-002 option 1), and "
                + "the audio is not the bug.");
        }

        // ====================================================================
        // Rolloff — precision is what distance takes away.
        // ====================================================================

        [Test]
        public void Rolloff_ReachesSilenceAtTheAbilitysRangeLimit()
        {
            var curve = RolloffCurves.For(GameConstants.ListenerHearingRange);

            Assert.That(curve.Evaluate(1f), Is.EqualTo(0f).Within(0.001f),
                "Unity holds the last curve value past maxDistance, so a curve that ends "
                + "above zero is a source that is never silent anywhere on the map.");

            Assert.That(curve.Evaluate(0f), Is.EqualTo(1f).Within(0.001f),
                "Inside the reference distance a source plays at full level.");
        }

        [Test]
        public void Rolloff_NeverGetsLouderWithDistance()
        {
            var curve = RolloffCurves.For(GameConstants.ListenerHearingRange);
            var previous = float.PositiveInfinity;

            for (var i = 0; i <= 100; i++)
            {
                var value = curve.Evaluate(i / 100f);
                Assert.That(value, Is.LessThanOrEqualTo(previous + 0.0005f),
                    "A rolloff that rises anywhere would make backing away from a sound a way "
                    + "of hearing it better, which inverts every distance judgement §04 asks for.");
                previous = value;
            }
        }

        [Test]
        public void Rolloff_IsShallowerThanFreeField_SoTheListenersRangeIsUsable()
        {
            var curve = RolloffCurves.For(GameConstants.ListenerHearingRange);

            // Unity's Logarithmic mode is amplitude = reference / distance.
            var freeField = AudioTuning.RolloffReferenceDistance / 25f;
            var ours = curve.Evaluate(25f / GameConstants.ListenerHearingRange);

            Assert.That(ours, Is.GreaterThan(freeField),
                "§04 puts the Listener's range at 40 m and §12 caps a corridor at 20 m — a "
                + "free-field 1/d curve leaves a footstep 22 dB under the room tone at that "
                + "range, so the role's headline number would be silence.");
        }

        [Test]
        public void Rolloff_StillLosesMostOfItsLevelAcrossTheRange()
        {
            var curve = RolloffCurves.For(GameConstants.ListenerHearingRange);
            var near = curve.Evaluate(5f / GameConstants.ListenerHearingRange);
            var far = curve.Evaluate(30f / GameConstants.ListenerHearingRange);

            Assert.That(far, Is.LessThan(near * 0.6f),
                "Precision is the thing distance is allowed to take (F-003). A rolloff shallow "
                + "enough to keep the alphabet legible must still make a far sound obviously "
                + "far, or the Listener would be able to place the monster anywhere on the map.");
        }

        // ====================================================================
        // §04 — the Listener's own noise.
        // ====================================================================

        [Test]
        public void Walking_StaysUnderTheSelfNoiseThreshold_AndRunningClearsIt()
        {
            Assert.That(NoiseMeter.NoiseForSpeed(GameConstants.WalkSpeed),
                Is.LessThan(GameConstants.ListenerSelfNoiseThreshold),
                "§04 blinds the Listener for 뛰거나 문을 열면. Walking is neither, and a "
                + "Listener who cannot walk has no ability at all.");

            Assert.That(NoiseMeter.NoiseForSpeed(GameConstants.RunSpeed),
                Is.GreaterThan(GameConstants.ListenerSelfNoiseThreshold),
                "§04: 뛰거나 … 정보가 끊긴다. If running were quiet enough to hear through, "
                + "§10's price for the role — 자기가 조용해야 한다 — would cost nothing.");

            Assert.That(NoiseMeter.NoiseForSpeed(GameConstants.RunnerSprintSpeed),
                Is.GreaterThan(NoiseMeter.NoiseForSpeed(GameConstants.RunSpeed)),
                "§06's sprint is louder than running, so a 주자 who is also the 청음사 pays "
                + "the most for the escape.");
        }

        [Test]
        public void DoorNoise_ClearsTheThreshold_AndStaysUnderTheFlare()
        {
            Assert.That(AudioTuning.SelfNoiseDoor,
                Is.GreaterThan(GameConstants.ListenerSelfNoiseThreshold),
                "§04 names the door specifically: 문을 열면 정보가 끊긴다.");

            Assert.That(AudioTuning.SelfNoiseDoor,
                Is.LessThan(GameConstants.FlareIgniteNoiseLevel),
                "§08 prices a 조명탄's noise at 0.70 and §04's trap saturates the scale at 1.0. "
                + "A door must sit below both or those two numbers stop meaning anything.");

            Assert.That(GameConstants.FlareIgniteNoiseLevel,
                Is.LessThan(GameConstants.EngineerTrapNoiseLevel),
                "The 소음 함정's whole job is to be the loudest thing in the zone (§04).");
        }

        [Test]
        public void SelfNoise_IsMonotonicInSpeed()
        {
            var previous = -1f;
            for (var speed = 0f; speed <= GameConstants.RunnerSprintSpeed + 1f; speed += 0.1f)
            {
                var noise = NoiseMeter.NoiseForSpeed(speed);
                Assert.That(noise, Is.GreaterThanOrEqualTo(previous - 0.0001f),
                    "Moving faster must never make a player quieter, or §05's speed multipliers "
                    + "and §04's constraint would pull in opposite directions.");
                previous = noise;
            }
        }

        [Test]
        public void StandingStill_IsSilent()
        {
            Assert.That(NoiseMeter.NoiseForSpeed(0f), Is.EqualTo(0f),
                "§04's Listener works by being quiet, and there is no quieter thing to be "
                + "than stopped.");
        }

        // ====================================================================
        // §06 — danger, and the state that must stay frightening.
        // ====================================================================

        [Test]
        public void Chase_IsTheMostDangerousState_AtEveryDistance()
        {
            foreach (var distance in new[] { 0f, 5f, 15f, 25f, 40f })
            {
                var chase = DangerSense.Evaluate(MonsterStateId.Chase, distance);

                Assert.That(chase, Is.GreaterThanOrEqualTo(DangerSense.Evaluate(MonsterStateId.Alert, distance)));
                Assert.That(chase, Is.GreaterThanOrEqualTo(DangerSense.Evaluate(MonsterStateId.Search, distance)));
                Assert.That(chase, Is.GreaterThanOrEqualTo(DangerSense.Evaluate(MonsterStateId.Patrol, distance)),
                    "§06: 추격 is the state the whole speed table exists to make terrifying.");
            }
        }

        [Test]
        public void Standstill_KeepsSomeDanger_BecauseSilenceIsTheWeapon()
        {
            var standstill = DangerSense.Evaluate(MonsterStateId.Standstill, 5f);

            Assert.That(standstill, Is.GreaterThan(0f),
                "§06: 침묵이 가장 무서운 소리다. A heartbeat that dropped to nothing the moment "
                + "the monster stopped would hand the player exactly the information 정지 exists "
                + "to take away — the Listener loses the fix, and the mix must not restore it.");

            Assert.That(standstill, Is.LessThan(DangerSense.Evaluate(MonsterStateId.Chase, 5f)),
                "It is still the quiet state; it must not feel like being hunted.");
        }

        [Test]
        public void Danger_RisesAsTheMonsterCloses()
        {
            var far = DangerSense.Evaluate(MonsterStateId.Alert, AudioTuning.DangerProximityRange);
            var near = DangerSense.Evaluate(MonsterStateId.Alert, 0f);

            Assert.That(near, Is.GreaterThan(far));
            Assert.That(DangerSense.Evaluate(MonsterStateId.Alert, AudioTuning.DangerProximityRange * 4f),
                Is.EqualTo(far).Within(0.001f),
                "Beyond the proximity range distance stops mattering; the state alone carries it.");
        }

        [Test]
        public void HeartbeatThresholds_AreOrdered()
        {
            Assert.That(AudioTuning.HeartbeatLowThreshold, Is.LessThan(AudioTuning.HeartbeatMidThreshold));
            Assert.That(AudioTuning.HeartbeatMidThreshold, Is.LessThan(AudioTuning.HeartbeatHighThreshold));
            Assert.That(AudioTuning.HeartbeatHighThreshold, Is.LessThanOrEqualTo(1f),
                "The top layer has to be reachable, or two thirds of the heartbeat never plays.");
        }

        // ====================================================================
        // Mix staging.
        // ====================================================================

        [Test]
        public void TheListenersChannel_IsTheLoudestFamilyInTheMix()
        {
            Assert.That(AudioTuning.TrimFootstepsDb, Is.GreaterThan(AudioTuning.TrimAmbienceDb),
                "§12's floor alphabet is the 청음사's only input. A footstep that loses to the "
                + "room tone is a role that does not work.");

            Assert.That(AudioTuning.TrimFootstepsDb, Is.GreaterThan(AudioTuning.TrimMonsterDb));
            Assert.That(AudioTuning.TrimFootstepsDb, Is.GreaterThan(AudioTuning.TrimItemsDb));
        }

        [Test]
        public void OccludedFootstepAtTwentyFiveMetres_StillStandsAboveTheZoneBed()
        {
            // The measured chain, in dBFS. See AudioTuning's class remarks for the
            // A-weighted RMS figures these come from.
            const float MonsterStepDba = -28.1f;
            const float ZoneBedDba = -38.9f;
            const float OcclusionLossDb = -7.3f;

            var curve = RolloffCurves.For(GameConstants.ListenerHearingRange);
            var rolloffDb = AudioOcclusion.LinearToDecibels(
                curve.Evaluate(25f / GameConstants.ListenerHearingRange));

            var step = MonsterStepDba + AudioTuning.TrimFootstepsDb + OcclusionLossDb + rolloffDb;
            var bed = ZoneBedDba + AudioTuning.TrimAmbienceDb;

            Assert.That(step, Is.GreaterThan(bed),
                "F-003's brief: the Listener should lose precision with distance, not identity. "
                + "At §12's widest legal cover spacing the cue has to still be above the room "
                + "it is heard in, or the alphabet is legible and inaudible at the same time.");
        }

        [Test]
        public void DecibelConversion_RoundTrips()
        {
            foreach (var db in new[] { -40f, -12f, -6f, 0f, 6f, 16f })
            {
                var linear = AudioOcclusion.DecibelsToLinear(db);
                Assert.That(AudioOcclusion.LinearToDecibels(linear), Is.EqualTo(db).Within(0.01f));
            }

            Assert.That(AudioOcclusion.LinearToDecibels(0f), Is.EqualTo(-80f),
                "Silence must have a finite decibel value an AudioMixer parameter can be set to.");
        }

        // ====================================================================
        // Occlusion estimation.
        // ====================================================================

        [Test]
        public void ADoorway_ReadsAsHalfOccluded_NotAsAWall()
        {
            var wall = AudioOcclusion.FromRays(true, 4, 4);
            var doorway = AudioOcclusion.FromRays(false, 4, 4);
            var clear = AudioOcclusion.FromRays(false, 0, 4);

            Assert.That(wall, Is.EqualTo(1f).Within(0.001f));
            Assert.That(clear, Is.EqualTo(0f).Within(0.001f));
            Assert.That(doorway, Is.EqualTo(0.5f).Within(0.001f),
                "§12 connects every pair of zones through two or three entry points. A clear "
                + "line of sight through a doorway must not be filtered like a wall, or most of "
                + "the map would sound solid.");
        }

        [Test]
        public void ClipBank_NeverRepeatsTheSameVariantTwiceRunning()
        {
            var bank = new AudioClipBank();
            bank.SetClips(new[]
            {
                AudioClip.Create("a", 16, 1, 8000, false),
                AudioClip.Create("b", 16, 1, 8000, false),
                AudioClip.Create("c", 16, 1, 8000, false),
                AudioClip.Create("d", 16, 1, 8000, false),
            });

            var previous = bank.Next();
            for (var i = 0; i < 200; i++)
            {
                var next = bank.Next();
                Assert.That(next, Is.Not.SameAs(previous),
                    "Four variants picked uniformly repeat about a quarter of the time, which at "
                    + "§06's sprint cadence is audible within a second and reads as a broken loop "
                    + "rather than as a floor.");
                previous = next;
            }
        }

        [Test]
        public void ClipBank_WithOneVariant_StillPlaysIt()
        {
            var bank = new AudioClipBank();
            bank.SetClips(new[] { AudioClip.Create("only", 16, 1, 8000, false) });

            Assert.That(bank.Next(), Is.Not.Null,
                "A bank of one has nothing else to offer; refusing to repeat would be silence, "
                + "which is worse.");
        }

        [Test]
        public void EmptyClipBank_IsSilentRatherThanBroken()
        {
            var bank = new AudioClipBank();

            Assert.That(bank.IsEmpty, Is.True);
            Assert.That(bank.Next(), Is.Null,
                "An unauthored surface plays nothing. §12 already fails a map that has one, so "
                + "the mix makes the omission audible instead of substituting a wrong floor.");
        }
    }
}
