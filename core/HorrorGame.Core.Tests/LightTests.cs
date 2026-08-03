using HorrorGame.Core;
using HorrorGame.Core.Light;
using HorrorGame.Core.Math;
using HorrorGame.Core.Threat;
using NUnit.Framework;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// The runner's light, after the light ECONOMY was deleted in the
    /// 상점/전리품/단서 제거 round.
    /// <para>
    /// <b>What this file used to be.</b> 42 tests over §03's light layer as a
    /// co-operative resource: <c>BatteryState</c> (charge in seconds, a per-switch-on
    /// cost, an idle drain, spare cells bought from §08, a 지상 발전기 recharge, and the
    /// latched "it just went dark" flag), <c>Flare</c> (조명탄 — a shop item that lit a
    /// zone and made noise), <c>LightField</c> (the 배전반: <c>SetZoneLit</c> with the
    /// breaker panel as the creature's lure anchor, plus the flare registry),
    /// <c>FlashlightOptics</c> (§08's 강화 손전등, which existed so the item's reward and
    /// its price were literally the same number), and <c>LightRules</c>/<c>LightQueries</c>
    /// behind them. All of it is deleted.
    /// </para>
    /// <para>
    /// <b>Why, and what did not go.</b> The owner's complaint was 「단서를 찾고 불을
    /// 밝히고 이러고있어」 — 하강 is 선착순 미로탈출 and a torch with a fuel gauge is
    /// paperwork bolted onto a footrace. Darkness itself is untouched and is the horror:
    /// the maze is dark and gets darker as you descend. What went is the chore of
    /// maintaining the light. So the surviving surface is small enough to state in one
    /// line — a switch, a cone, and one price — and every test below is a positive
    /// assertion that it still works, because the failure mode of deleting an economy is
    /// a half-deleted torch that is always dead.
    /// </para>
    /// </summary>
    [TestFixture]
    public class LightTests
    {
        /// <summary>§07's 심야 multiplier — the −30% reach penalty, read as a bare float exactly as <see cref="FlashlightState.RangeFor"/> receives it.</summary>
        private const float LateNightMultiplier = 0.7f;

        // ====================================================================
        // The switch. It always works — that is the whole change.
        // ====================================================================

        /// <summary>
        /// A new torch starts off, and one press lights it. There is no cell to be flat
        /// and no cost to fail to pay, so <see cref="FlashlightState.TryTurnOn"/> cannot
        /// return false.
        /// </summary>
        [Test]
        public void PressingTheKey_AlwaysGivesLight()
        {
            var light = new FlashlightState();
            Assert.That(light.IsOn, Is.False, "A torch starts in the pocket.");
            Assert.That(light.IsLit, Is.False);

            Assert.That(light.TryTurnOn(), Is.True,
                "TryTurnOn used to charge §03's switch-on cost against the cell and could come up "
                + "dark. There is nothing to spend now; the contract is press-and-see.");
            Assert.That(light.IsLit, Is.True);
        }

        /// <summary>
        /// Toggling is symmetric and repeatable. §03 deliberately made it asymmetric — off
        /// free, on charged — so flicking the beam was worse than committing to it; with
        /// the cell gone there is no charge to be asymmetric with.
        /// </summary>
        [Test]
        public void TogglingRepeatedly_NeverStrandsTheRunnerInTheDark()
        {
            var light = new FlashlightState();

            for (var press = 0; press < 200; press++)
            {
                var lit = light.Toggle();
                Assert.That(lit, Is.EqualTo(press % 2 == 0),
                    "Press " + press + " should have left the torch "
                    + (press % 2 == 0 ? "lit" : "dark") + ".");
                Assert.That(light.IsLit, Is.EqualTo(lit));
            }

            Assert.That(light.Toggle(), Is.True,
                "200 presses cannot exhaust anything, because there is nothing to exhaust.");
        }

        /// <summary>
        /// The specific regression this round could have introduced: a light that goes out
        /// on its own. <c>FlashlightState.Tick(float)</c> was DELETED rather than emptied,
        /// so no elapsed time can turn the beam off — asserted here through the type
        /// itself, because an empty Tick that a future edit refills would pass every
        /// behavioural test in this file.
        /// </summary>
        [Test]
        public void NothingCanTurnTheLightOffButTheRunner()
        {
            Assert.That(typeof(FlashlightState).GetMethod("Tick"), Is.Null,
                "A per-frame drain hook is how the battery comes back. If a future round wants a "
                + "resource on the light it must argue it from the race, not restore §03's.");

            var light = new FlashlightState();
            light.TryTurnOn();
            Assert.That(light.IsLit, Is.True);

            // Every public reading call, many times over. None of them is allowed to
            // consume anything, because there is nothing left to consume.
            for (var i = 0; i < 1000; i++)
            {
                light.RangeFor(1f);
                light.ConeAt(Vec3.Zero, Vec3.Forward, 1f);
                _ = light.NoticeDistance;
            }

            Assert.That(light.IsLit, Is.True, "Reading the beam must not spend it.");
            light.TurnOff();
            Assert.That(light.IsLit, Is.False, "The runner's own key is the only switch.");
        }

        // ====================================================================
        // The one price the light still charges — and it is a hazard, not an economy.
        // ====================================================================

        /// <summary>
        /// A lit runner is a runner the creature picks out further away, and a dark one is
        /// invisible to that test entirely. This is the whole cost of light now: not a
        /// resource, a risk.
        /// </summary>
        [Test]
        public void BeingLit_IsTheOnlyThingLightCosts()
        {
            var light = new FlashlightState();
            Assert.That(light.NoticeDistance, Is.EqualTo(0f),
                "An unlit torch cannot give its holder away.");

            light.TryTurnOn();
            Assert.That(light.NoticeDistance,
                Is.EqualTo(GameConstants.FlashlightNoticeDistance).Within(1e-4f));
            Assert.That(light.NoticeDistance, Is.GreaterThan(light.RangeFor(1f)),
                "§03's central bargain, and the half of it that survives the pivot: the creature "
                + "notices the beam from further away than the beam reaches. Turning the light on "
                + "buys sight at the price of being seen first.");
        }

        /// <summary>
        /// §07's 심야 penalty takes reach and leaves exposure alone. Read literally that is
        /// what §08 says — brightness and visibility are one dial — and the alternative
        /// reading would make 심야 partly a stealth *bonus*, contradicting §07's own thesis
        /// that the pressure only rises. docs/BALANCE-FINDINGS.md carries the argument.
        /// </summary>
        [Test]
        public void LateNight_TakesReach_NotExposure()
        {
            var light = new FlashlightState();
            light.TryTurnOn();

            Assert.That(light.RangeFor(LateNightMultiplier),
                Is.EqualTo(GameConstants.FlashlightRange * LateNightMultiplier).Within(0.01f),
                "§07: 12 m − 30% = 8.4 m.");
            Assert.That(light.RangeFor(LateNightMultiplier), Is.LessThan(light.RangeFor(1f)));

            Assert.That(light.NoticeDistance,
                Is.EqualTo(GameConstants.FlashlightNoticeDistance).Within(1e-4f),
                "If this ever changes, 심야 becomes partly a stealth buff and "
                + "docs/BALANCE-FINDINGS.md must be updated in the same commit.");
        }

        /// <summary>
        /// The penalty starts at §07's 16-minute mark, read through the threat system's own
        /// float rather than a number copied into the light system.
        /// </summary>
        [Test]
        public void LateNightPenalty_StartsAtSixteenMinutes()
        {
            var light = new FlashlightState();
            light.TryTurnOn();

            var before = ThreatCurve.At(GameConstants.ThreatTierSeconds * 2f - 1f);
            var lateNight = ThreatCurve.At(GameConstants.ThreatTierSeconds * 2f);

            Assert.That(light.RangeFor(before.FlashlightRangeMultiplier),
                Is.EqualTo(GameConstants.FlashlightRange).Within(1e-3f),
                "§07 takes nothing from the flashlight before 심야.");
            Assert.That(light.RangeFor(lateNight.FlashlightRangeMultiplier),
                Is.EqualTo(GameConstants.FlashlightRange * LateNightMultiplier).Within(0.01f),
                "§07: the −30% arrives at 16 min.");
        }

        /// <summary>
        /// A broken tier multiplier must put the runner in the dark rather than hand every
        /// later comparison a NaN. §07's clock is not readable during a descent, so a host
        /// that has lost track of it is a reachable state.
        /// </summary>
        [Test]
        public void BrokenTierMultiplier_ProducesDarkness_NotNaN()
        {
            var light = new FlashlightState();
            light.TryTurnOn();

            foreach (var bad in new[] { float.NaN, -1f, 0f })
            {
                Assert.That(light.RangeFor(bad), Is.EqualTo(0f),
                    "Multiplier " + bad + " must reach nowhere.");
                Assert.That(light.ConeAt(Vec3.Zero, Vec3.Forward, bad).IsLit, Is.False,
                    "Multiplier " + bad + " must not light anything.");
            }

            Assert.That(light.IsLit, Is.True,
                "A broken clock darkens the corridor; it does not flip the runner's switch.");
        }

        // ====================================================================
        // The beam as geometry.
        // ====================================================================

        /// <summary>
        /// An unlit torch produces no cone at all, and a lit one produces the cone the
        /// constants describe. §03: 빛이 좁다 — the narrowness is what makes aiming a skill
        /// and a junction a decision.
        /// </summary>
        [Test]
        public void TheConeMatchesTheSwitch()
        {
            var light = new FlashlightState();

            var dark = light.ConeAt(Vec3.Zero, Vec3.Forward, 1f);
            Assert.That(dark.IsLit, Is.False);
            Assert.That(dark.RangeMetres, Is.EqualTo(0f));

            light.TryTurnOn();
            var lit = light.ConeAt(Vec3.Zero, Vec3.Forward, 1f);
            Assert.That(lit.IsLit, Is.True);
            Assert.That(lit.RangeMetres, Is.EqualTo(GameConstants.FlashlightRange).Within(1e-4f));
            Assert.That(lit.HalfAngleDegrees,
                Is.EqualTo(GameConstants.FlashlightHalfAngle).Within(1e-4f));
            Assert.That(lit.HalfAngleDegrees, Is.LessThan(45f),
                "A torch that opens wider than a quarter turn stops being a torch and starts "
                + "being room lighting.");
        }

        /// <summary>
        /// <see cref="FlashlightState.IsOn"/> and <see cref="FlashlightState.IsLit"/> are
        /// now the same reading, and both names are kept on purpose: the first-person view
        /// asks whether the torch is in the fist, the spot light asks whether light is
        /// coming out. They could differ while a flat cell existed. Pinned so that a future
        /// tidy-up deleting one does not silently answer the other question.
        /// </summary>
        [Test]
        public void OnAndLit_CannotDisagree()
        {
            var light = new FlashlightState();
            Assert.That(light.IsLit, Is.EqualTo(light.IsOn));

            light.TryTurnOn();
            Assert.That(light.IsLit, Is.EqualTo(light.IsOn));

            light.TurnOff();
            Assert.That(light.IsLit, Is.EqualTo(light.IsOn));
        }

        // ====================================================================
        // The economy is gone and must stay gone.
        // ====================================================================

        /// <summary>
        /// An executable tombstone, in the spirit of the round's own lesson —
        /// 「막아 둔 것은 없어진 것이 아니다」. The light economy was gated out once before
        /// and leaked back, so the record is a failing test rather than a comment.
        /// Checked against the compiled assembly, not the source tree.
        /// </summary>
        [Test]
        public void TheLightEconomy_IsAbsentFromTheAssembly()
        {
            var assembly = typeof(FlashlightState).Assembly;

            foreach (var gone in new[]
            {
                "HorrorGame.Core.Light.BatteryState",
                "HorrorGame.Core.Light.Flare",
                "HorrorGame.Core.Light.LightField",
                "HorrorGame.Core.Light.LightRules",
                "HorrorGame.Core.Light.LightQueries",
                "HorrorGame.Core.Light.LightSourceKind",
                "HorrorGame.Core.Light.FlashlightOptics",
            })
            {
                Assert.That(assembly.GetType(gone), Is.Null,
                    gone + " is the light economy coming back. Batteries, 조명탄, 배전반 and §08's "
                    + "강화 손전등 were deleted because a light you maintain is a chore; a light you "
                    + "carry is a game.");
            }

            Assert.That(assembly.GetType("HorrorGame.Core.Light.FlashlightState"), Is.Not.Null,
                "A suite that only asserts absences passes just as happily on an empty assembly.");
            Assert.That(assembly.GetType("HorrorGame.Core.Light.LightCone"), Is.Not.Null);
        }
    }
}
