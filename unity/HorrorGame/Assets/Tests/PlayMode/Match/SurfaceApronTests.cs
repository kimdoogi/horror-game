#if UNITY_INCLUDE_TESTS
#nullable enable

using System.Collections;
using System.Text;
using HorrorGame.Gameplay.Match;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Match
{
    /// <summary>
    /// §01's 지상 has a physical edge, and the edge is the rule rather than a picture of it.
    /// <para>
    /// <b>Why this exists.</b> The apron was a <c>sqrMagnitude</c> test against a light
    /// with no presence in the world at all — no line, no gate, no threshold, no change
    /// of light or sound — and the only thing that told a player which side of it they
    /// were on was one line of §14 guidance text. The owner played the build and asked
    /// <i>앞마당이 어디야</i>. The failure that question exposes is not "the paint looks
    /// wrong", it is "there is nothing to look at", so the assertion that matters most
    /// here is the count: something was painted, and it was painted somewhere a player
    /// can walk.
    /// </para>
    /// <para>
    /// <b>The radius assertion is the one that keeps this honest over time.</b>
    /// <c>SurfaceApron</c> lays every stripe at <c>MatchMap.SurfaceRadius</c> from
    /// <c>MatchMap.Entrance</c> — the same two values <c>MatchMap.IsOnSurface</c>
    /// measures against — so crossing the paint and the match changing phase are the
    /// same event. A future change that moved one without the other would put a line on
    /// the floor that lies about where the safe zone ends, which is worse than the
    /// invisible boundary this replaced.
    /// </para>
    /// <para>
    /// It lives in the predefined assembly because <c>MatchDirector</c> does, and an
    /// <c>.asmdef</c> cannot reference one. The file compiles out of a player build on
    /// <c>UNITY_INCLUDE_TESTS</c>.
    /// </para>
    /// </summary>
    public sealed class SurfaceApronTests
    {
        /// <summary>§13's seed for the layout under test — <c>SoloPlaytest.PlaytestSeed</c>, quoted because that class is editor-only.</summary>
        private const int Seed = 20260731;

        /// <summary>The scene <c>SoloPlaytest.BuildScene</c> writes, and Build Settings carries.</summary>
        private const string SoloScene = "Map_FirstSketch_Solo";

        /// <summary>
        /// How far a stripe may sit from the true radius, metres. Not a tolerance on the
        /// maths — the ring is computed exactly — but on the floor: each stripe is
        /// dropped onto whatever surface is under it, and §12's gravel and the kit's tile
        /// seams move the landing point by centimetres.
        /// </summary>
        private const float RadiusToleranceMetres = 0.35f;

        [SetUp]
        public void QuietenTheImporter()
        {
            // Loading the five-storey scene re-emits Mirror's packaging complaint about
            // an immutable folder nobody is allowed to fix, and the Test Framework fails
            // a test on any unexpected LogError. Safe only because every step below is
            // asserted explicitly.
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>
        /// Puts the building back. A five-storey map left in the active scene has failed
        /// audio-occlusion tests that ran after it — see <c>InteractionPickupTests</c>.
        /// </summary>
        [UnityTearDown]
        public IEnumerator PutTheWorldBack()
        {
            var solo = SceneManager.GetSceneByName(SoloScene);
            if (solo.IsValid() && solo.isLoaded)
            {
                var empty = SceneManager.CreateScene("SurfaceApronTests_Empty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(solo);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// The whole of the fix, asserted: §01's boundary is drawn, it is drawn at the
        /// radius the rule uses, at least one crossing is framed and lit, and none of it
        /// can be walked into.
        /// </summary>
        [UnityTest]
        public IEnumerator The_surface_boundary_is_painted_at_the_radius_the_rule_measures()
        {
            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var director = Object.FindFirstObjectByType<MatchDirector>();
            Assert.That(director, Is.Not.Null, "the solo scene has no MatchDirector");

            if (director!.Map == null)
            {
                Assert.That(director.BeginMatch(Seed), Is.True, "BeginMatch refused");
            }

            yield return null;

            var map = director.Map;
            Assert.That(map, Is.Not.Null, "the match started without a map");

            var apron = director.Apron;
            Assert.That(apron, Is.Not.Null,
                "§01's 지상 was not painted at all. That is the defect this test exists for: the safe "
                + "zone, the shop, the resupply point and §02's win condition were one radius test "
                + "with nothing in the world to see.");

            // ── It exists, and it is somewhere a player can be ──────────────────
            Assert.That(apron!.PaintedStripes, Is.GreaterThan(0),
                "no stripe found floor under it. §12 puts the 출입구 in the corner of B1 하역장, so most of "
                + "a 15 m ring is outside the building — but the arc across the 하역 베이 must land.");

            Assert.That(apron.Gates, Is.GreaterThan(0),
                "the ring was painted but crosses nowhere walkable, so no threshold was framed or lit. "
                + "A player would meet the line only where it meets a wall.");

            Assert.That(apron.WalkableMetres, Is.GreaterThan(1f),
                "less than a metre of the boundary is walkable, which cannot be §01's 출입구.");

            // ── The paint is the rule, not a picture of it ──────────────────────
            var entrance = map!.Entrance;
            var radius = MatchMap.SurfaceRadius;
            var stripes = 0;
            var worst = 0f;
            var worstName = string.Empty;

            foreach (var renderer in apron.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith("Apron_Stripe_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                stripes++;

                var flat = renderer.transform.position - entrance;
                flat.y = 0f;
                var error = Mathf.Abs(flat.magnitude - radius);
                if (error > worst)
                {
                    worst = error;
                    worstName = renderer.name;
                }
            }

            Assert.That(stripes, Is.EqualTo(apron.PaintedStripes),
                "the apron reports " + apron.PaintedStripes + " stripes and " + stripes + " are in the scene");

            Assert.That(worst, Is.LessThanOrEqualTo(RadiusToleranceMetres),
                "the painted line is not where the rule is. " + worstName + " sits " + worst.ToString("0.00")
                + " m off MatchMap.SurfaceRadius (" + radius.ToString("0.#")
                + " m), so a player crossing the paint and the match changing phase are no longer the same "
                + "event — which is worse than the invisible boundary this replaced.");

            // ── And it changes nothing about how the building is walked ─────────
            var solid = new StringBuilder();
            foreach (var collider in apron.GetComponentsInChildren<Collider>(true))
            {
                solid.Append(' ').Append(collider.name);
            }

            Assert.That(solid.Length, Is.Zero,
                "the apron put collision at the one doorway every round trip funnels through:"
                + solid + ". §12's corridor widths are computed for the character and the monster.");

            // ── Warm, and warmer than §12's cold zone fittings ──────────────────
            var lamps = 0;
            foreach (var light in apron.GetComponentsInChildren<Light>(true))
            {
                lamps++;
                Assert.That(light.color.r, Is.GreaterThan(light.color.b),
                    light.name + " is not a warm source. §07 cools the grade as the night advances and §12's "
                    + "zone fittings are blue; the apron reads as the opposite or it reads as nothing.");
            }

            Assert.That(lamps, Is.GreaterThan(0), "the apron lit nothing");

            Debug.Log(
                "[ApronTest] §01 지상 at " + radius.ToString("0.#") + " m from " + entrance
                + " — " + apron.PaintedStripes + " stripes (worst radial error "
                + worst.ToString("0.000") + " m), " + apron.Gates + " lit crossing(s) over "
                + apron.WalkableMetres.ToString("0.#") + " m, " + lamps + " warm source(s).");
        }

        /// <summary>
        /// §01's two crossings register as events. Descending is the section's commitment
        /// and surfacing is its relief, and neither used to move a pixel.
        /// </summary>
        [UnityTest]
        public IEnumerator Crossing_the_line_swells_the_threshold_on_the_way_in_and_dims_it_on_the_way_out()
        {
            SceneManager.LoadScene(SoloScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var director = Object.FindFirstObjectByType<MatchDirector>();
            Assert.That(director, Is.Not.Null, "the solo scene has no MatchDirector");

            if (director!.Map == null)
            {
                Assert.That(director.BeginMatch(Seed), Is.True, "BeginMatch refused");
            }

            yield return null;

            var apron = director.Apron;
            Assert.That(apron, Is.Not.Null, "§01's 지상 was not painted");

            var lamp = FindGateLamp(apron!);
            Assert.That(lamp, Is.Not.Null, "no gate lamp to read the crossing off");

            var rest = lamp!.intensity;
            Assert.That(rest, Is.GreaterThan(0f), "the threshold lamp is off at rest");

            apron!.SetOnSurface(true);
            yield return null;

            Assert.That(lamp.intensity, Is.GreaterThan(rest),
                "§01's 귀환 did not swell the threshold. Rest was " + rest.ToString("0.00")
                + ", after the crossing it was " + lamp.intensity.ToString("0.00") + ".");

            apron.SetOnSurface(false);
            yield return null;

            Assert.That(lamp.intensity, Is.LessThan(rest),
                "§01's 잠입 did not draw the warmth back. Rest was " + rest.ToString("0.00")
                + ", after the crossing it was " + lamp.intensity.ToString("0.00") + ".");

            // And it settles. A beat that never returned would be a permanent change of
            // grade wearing an event's clothes.
            var waited = 0f;
            while (waited < 3f && !Mathf.Approximately(lamp.intensity, rest))
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Assert.That(lamp.intensity, Is.EqualTo(rest).Within(0.001f),
                "the crossing beat never settled back to rest — after " + waited.ToString("0.0")
                + " s the lamp is still at " + lamp.intensity.ToString("0.00") + " against " + rest.ToString("0.00"));
        }

        private static Light? FindGateLamp(SurfaceApron apron)
        {
            foreach (var light in apron.GetComponentsInChildren<Light>(true))
            {
                if (light.name.StartsWith("Apron_GateLamp_", System.StringComparison.Ordinal))
                {
                    return light;
                }
            }

            return null;
        }
    }
}
#endif
