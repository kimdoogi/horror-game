using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HorrorGame.Core;
using HorrorGame.Core.Race;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// Guards the arrangement the whole project rests on: the core stays
    /// engine-free, and the tuned numbers stay internally consistent.
    /// <para>
    /// If these fail, nothing else in the suite is trustworthy — the assembly
    /// under test is either not the assembly Unity compiles, or it encodes a
    /// balance the design document contradicts.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FoundationTests
    {
        // ====================================================================
        // The core must never reference the engine.
        // ====================================================================

        /// <summary>
        /// Scans every core source file for engine references.
        /// <para>
        /// A single <c>using UnityEngine;</c> would still compile inside Unity, so
        /// the mistake is invisible there — it only shows up as the .NET build
        /// collapsing. Catching it as a readable test failure beats reading a wall
        /// of CS0246 errors.
        /// </para>
        /// </summary>
        [Test]
        public void CoreSources_DoNotReferenceUnityEngine()
        {
            // Comments are stripped first: several core files explain in prose
            // that they exist precisely so the engine types are not needed, and
            // that explanation must not read as a violation.
            var offenders = CoreSourceRootAttribute.EnumerateCoreSources()
                .Select(f => (File: f, Code: CSharpSource.StripCommentsAndLiterals(File.ReadAllText(f.FullName))))
                .Where(x => Regex.IsMatch(x.Code, @"\bUnityEngine\b|\bUnityEditor\b"))
                .Select(x => x.File.Name)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "These core files reference the engine, which breaks the whole point of a testable "
                + "rules layer. Move the engine-facing part into Assets/Scripts/Gameplay and keep the "
                + "rule in Core: " + string.Join(", ", offenders));
        }

        /// <summary>The glob in HorrorGame.Core.csproj must actually be finding sources.</summary>
        [Test]
        public void CoreSources_AreDiscoverable()
        {
            var files = CoreSourceRootAttribute.EnumerateCoreSources();
            Assert.That(files.Length, Is.GreaterThan(0),
                "No core sources found. HorrorGame.Core would be compiling as an empty assembly.");
        }

        /// <summary>
        /// Core code must not reach for ambient randomness or wall-clock time.
        /// Both would make a seeded match unreproducible, and §13 needs replayable
        /// seeds to diagnose balance from a bug report.
        /// </summary>
        [Test]
        public void CoreSources_DoNotUseAmbientRandomnessOrClock()
        {
            var offenders = CoreSourceRootAttribute.EnumerateCoreSources()
                .Select(f => (f.Name, Code: CSharpSource.StripCommentsAndLiterals(File.ReadAllText(f.FullName))))
                .Where(x => Regex.IsMatch(x.Code, @"new\s+Random\s*\(|DateTime\.(Now|UtcNow)|Environment\.TickCount"))
                .Select(x => x.Name)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "These core files use ambient randomness or the wall clock, so replaying a seed would "
                + "not reproduce the match. Take an IRandomSource and an explicit delta time instead: "
                + string.Join(", ", offenders));
        }

        // ====================================================================
        // §05–§12 — the design's own reasoning, as assertions.
        // ====================================================================

        /// <summary>The relationship checks in <see cref="GameConstants.Validate"/> must hold.</summary>
        [Test]
        public void GameConstants_Validate_Passes() => Assert.DoesNotThrow(GameConstants.Validate);

        /// <summary>§06: 걷기 2.0 &lt; 달리기 4.5 &lt; 괴물 4.8 &lt; 주자 질주 5.6.</summary>
        [Test]
        public void SpeedLadder_MatchesSection06()
        {
            Assert.That(GameConstants.WalkSpeed, Is.EqualTo(2.0f).Within(1e-4f));
            Assert.That(GameConstants.RunSpeed, Is.EqualTo(4.5f).Within(1e-4f));
            Assert.That(GameConstants.MonsterBaseSpeed, Is.EqualTo(4.8f).Within(1e-4f));
            Assert.That(GameConstants.RunnerSprintSpeed, Is.EqualTo(5.6f).Within(1e-4f));
        }

        /// <summary>
        /// §05's headline conclusion: "뒷걸음은 괴물보다 느리다. 뒤를 보면 잡힌다."
        /// Even the Runner, sprinting, must lose ground while backpedalling.
        /// </summary>
        [Test]
        public void Backpedalling_IsSlowerThanTheMonster()
        {
            Assert.That(GameConstants.RunSpeed * GameConstants.MulBackward,
                Is.LessThan(GameConstants.MonsterBaseSpeed),
                "A running player backpedalling must lose ground.");
            Assert.That(GameConstants.RunnerSprintSpeed * GameConstants.MulBackward,
                Is.LessThan(GameConstants.MonsterBaseSpeed),
                "§05: even a sprinting Runner must lose ground while facing backwards.");
        }

        /// <summary>
        /// §05: the 45° peek is the skill expression — it must stay faster than
        /// the monster, or checking the distance behind you becomes strictly wrong
        /// and the analogue dilemma collapses into a binary.
        /// </summary>
        [Test]
        public void DiagonalPeek_StillOutrunsTheMonster()
        {
            var peek = GameConstants.RunnerSprintSpeed * GameConstants.MulDiagonal;
            Assert.That(peek, Is.GreaterThan(GameConstants.MonsterBaseSpeed));
            Assert.That(peek - GameConstants.MonsterBaseSpeed, Is.EqualTo(0.52f).Within(0.01f),
                "§05 computes the peek margin at +0.52 m/s. If this moved, the 60 m sprint distance "
                + "and §12's cover spacing both need recomputing.");
        }

        /// <summary>
        /// §06: "어그로 해제는 거리가 아니라 맵을 쓰는 것이다." One full sprint must
        /// not open the release distance on its own.
        /// </summary>
        [Test]
        public void OneSprint_CannotOpenTheReleaseDistance()
        {
            var gain = (GameConstants.RunnerSprintSpeed - GameConstants.MonsterBaseSpeed)
                       * GameConstants.SprintStaminaSeconds;

            Assert.That(gain, Is.EqualTo(9.6f).Within(0.01f), "§06 computes the gain at 9.6 m.");
            Assert.That(gain, Is.LessThan(GameConstants.AggroReleaseDistance),
                "If a sprint alone could reach 12 m, the map would stop mattering and §12's whole "
                + "S-corridor argument would be dead weight.");
        }

        /// <summary>§12: two 10 m legs must take the monster longer to clear than the 3 s cover requirement.</summary>
        [Test]
        public void SCorridor_BreaksLineOfSightLongEnough()
        {
            var transit = GameConstants.SCorridorLegLength * 2f / GameConstants.MonsterBaseSpeed;
            Assert.That(transit, Is.EqualTo(4.1667f).Within(0.01f), "§12 computes 4.2 s.");
            Assert.That(transit, Is.GreaterThan(GameConstants.AggroReleaseLineOfSightBreak));
        }

        /// <summary>§12: a single corner only works from 14.4 m out — 3 s of cover at monster speed.</summary>
        [Test]
        public void SingleCorner_RequiresFourteenPointFourMetres()
        {
            var needed = GameConstants.AggroReleaseLineOfSightBreak * GameConstants.MonsterBaseSpeed;
            Assert.That(needed, Is.EqualTo(GameConstants.SingleCornerMinDistance).Within(0.01f));
        }

        // ====================================================================
        // DELETED with §08's carry weight. Two tests stood here:
        //   HeavyLoad_CostsTheRunnerItsEscape
        //   WeightBands_AreACliffForTheRunner_NotAGradient
        //
        // The second was a genuine FINDING and it is worth recording why it can go
        // rather than just that it did. It pinned that §08's four weight bands were
        // not the gradual slowdown the table implied: 5.6 × 0.85 = 4.76 m/s is
        // already under the creature's 4.8, so the moment a runner passed weight 5
        // the remaining three bands were identical from their point of view. Three
        // quarters of the table described one outcome — "drop it or die".
        //
        // The pivot resolved the contradiction by deleting the table. Nobody picks
        // anything up, so there is no weight, no cliff and no gradient. The margin
        // that decides whether a runner lives is now direction and stance alone,
        // and it is asserted in MovementTests against MarginVersusMonster.
        // ====================================================================


        /// <summary>§07: a match must be able to reach the late threat tiers.</summary>
        [Test]
        public void ThreatCurve_FitsInsideAMatch()
        {
            var tiersInShortestMatch = GameConstants.TargetMatchSecondsMin / GameConstants.ThreatTierSeconds;
            Assert.That(tiersInShortestMatch, Is.GreaterThanOrEqualTo(3f),
                "Even a fast match should reach 심야, or §07's pressure never lands.");
        }

        // ====================================================================
        // Math primitives.
        // ====================================================================

        /// <summary>Flat distance must ignore height — a stairwell is not extra range.</summary>
        [Test]
        public void DistanceFlat_IgnoresHeight()
        {
            var a = new Vec3(0f, 0f, 0f);
            var b = new Vec3(3f, 100f, 4f);
            Assert.That(Vec3.DistanceFlat(a, b), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(Vec3.Distance(a, b), Is.GreaterThan(99f));
        }

        /// <summary>Yaw and direction must round-trip.</summary>
        [Test]
        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(90f)]
        [TestCase(-135f)]
        [TestCase(179f)]
        public void YawAndDirection_RoundTrip(float yaw)
        {
            var back = MathX.YawOf(MathX.DirectionFromYaw(yaw));
            Assert.That(MathX.DeltaAngle(yaw, back), Is.EqualTo(0f).Within(0.01f));
        }

        /// <summary>Degenerate vectors must normalise to zero rather than produce NaN.</summary>
        [Test]
        public void Normalized_OfZero_IsZeroNotNaN()
        {
            var n = Vec3.Zero.Normalized;
            Assert.That(n, Is.EqualTo(Vec3.Zero));
            Assert.That(float.IsNaN(n.X), Is.False);
        }

        /// <summary>The cone test drives the flashlight, the flash stun and monster vision.</summary>
        [Test]
        public void InCone_RespectsRangeAndAngle()
        {
            var origin = Vec3.Zero;
            var forward = Vec3.Forward;

            Assert.That(MathX.InCone(origin, forward, new Vec3(0f, 0f, 5f), 10f, 30f), Is.True,
                "Straight ahead and in range.");
            Assert.That(MathX.InCone(origin, forward, new Vec3(0f, 0f, 15f), 10f, 30f), Is.False,
                "Out of range.");
            Assert.That(MathX.InCone(origin, forward, new Vec3(5f, 0f, 1f), 10f, 30f), Is.False,
                "Inside range but far outside the cone.");
        }

        // ====================================================================
        // Determinism — §13 needs a seed to replay a match exactly.
        // ====================================================================

        /// <summary>The same seed must produce the same stream.</summary>
        [Test]
        public void DeterministicRandom_SameSeed_SameSequence()
        {
            var a = new DeterministicRandom(20260730);
            var b = new DeterministicRandom(20260730);

            for (var i = 0; i < 512; i++)
            {
                Assert.That(b.NextFloat(), Is.EqualTo(a.NextFloat()).Within(0f), $"Diverged at draw {i}.");
            }
        }

        /// <summary>Different seeds must not collapse onto the same stream.</summary>
        [Test]
        public void DeterministicRandom_DifferentSeeds_Diverge()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            var same = 0;
            for (var i = 0; i < 64; i++)
            {
                if (MathX.Approximately(a.NextFloat(), b.NextFloat(), 1e-7f))
                {
                    same++;
                }
            }

            Assert.That(same, Is.LessThan(4), "Seeds 1 and 2 are producing near-identical streams.");
        }

        /// <summary>
        /// Adjacent seeds must not correlate on their first draws. Those draws
        /// place the objective, so a correlation there would make consecutive
        /// matches feel repetitive in exactly the way §03 is trying to avoid.
        /// </summary>
        [Test]
        public void DeterministicRandom_AdjacentSeeds_DoNotCorrelateEarly()
        {
            var firstDraws = Enumerable.Range(1000, 200)
                .Select(s => new DeterministicRandom(s).NextFloat())
                .ToArray();

            var mean = firstDraws.Average();
            Assert.That(mean, Is.EqualTo(0.5f).Within(0.08f),
                "First draws across adjacent seeds are not uniformly distributed.");

            var buckets = new int[10];
            foreach (var v in firstDraws)
            {
                buckets[System.Math.Min(9, (int)(v * 10f))]++;
            }

            Assert.That(buckets.All(b => b > 0), Is.True,
                "First draws across adjacent seeds leave whole deciles empty: "
                + string.Join(",", buckets));
        }

        /// <summary>Range draws must stay inside their bounds.</summary>
        [Test]
        public void DeterministicRandom_RespectsBounds()
        {
            var rng = new DeterministicRandom(7);
            for (var i = 0; i < 4096; i++)
            {
                var f = rng.NextFloat(GameConstants.StandstillIntervalMin, GameConstants.StandstillIntervalMax);
                Assert.That(f, Is.InRange(GameConstants.StandstillIntervalMin, GameConstants.StandstillIntervalMax));

                // Was GameConstants.RoleCount (§04's five 직업). Any small closed
                // range exercises the bound; the race's own is the storey count.
                var n = rng.NextInt(0, RaceState.Storeys);
                Assert.That(n, Is.InRange(0, RaceState.Storeys - 1));
            }
        }

        /// <summary>A degenerate integer range must not throw or loop.</summary>
        [Test]
        public void DeterministicRandom_EmptyIntRange_ReturnsMin()
        {
            var rng = new DeterministicRandom(3);
            Assert.That(rng.NextInt(5, 5), Is.EqualTo(5));
            Assert.That(rng.NextInt(9, 2), Is.EqualTo(9));
        }

        /// <summary>Shuffle must be a permutation, not a resample.</summary>
        [Test]
        public void Shuffle_PreservesElements()
        {
            var rng = new DeterministicRandom(42);
            var items = Enumerable.Range(0, 64).ToList();
            rng.Shuffle(items);

            Assert.That(items.Count, Is.EqualTo(64));
            Assert.That(items.Distinct().Count(), Is.EqualTo(64));
            Assert.That(items.OrderBy(x => x), Is.EqualTo(Enumerable.Range(0, 64)));
            Assert.That(items, Is.Not.EqualTo(Enumerable.Range(0, 64).ToList()),
                "A 64-element shuffle returning the original order suggests it is not shuffling.");
        }
    }
}
