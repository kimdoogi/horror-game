#nullable enable

using System.Collections;
using HorrorGame.Core;
using HorrorGame.Core.Presence;
using HorrorGame.Gameplay.Presence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HorrorGame.Tests.PlayMode.Presence
{
    /// <summary>
    /// The 그늘 in a running engine: whether the adapter feeds the rules the world they
    /// were written about.
    /// <para>
    /// <c>PresenceTests</c> in Core already asserts every rule without an engine, so
    /// repeating any of them here would be measuring the same thing twice and calling it
    /// coverage. What only a scene can answer is the seam — whether a real light on a real
    /// player produces the light quality §03's threshold is written in, whether a real
    /// monster transform clears the dark at the radius §06's sight range names, and whether
    /// the figure this entity is made of can find anywhere to stand in the 2.2 m corridor
    /// §12 specifies.
    /// </para>
    /// <para>
    /// The last of those is not hypothetical. The first render of this entity found the
    /// figure lying flat on the floor and every mote parked at the world origin, and both
    /// were seam bugs that no Core test could have seen: one was an FBX axis convention,
    /// the other was a placement rule that was correct in an open room and impossible in a
    /// corridor.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PresenceSessionTests
    {
        private GameObject? _world;

        [TearDown]
        public void TearDown()
        {
            if (_world != null)
            {
                Object.DestroyImmediate(_world);
                _world = null;
            }
        }

        /// <summary>
        /// Builds a §12 corridor section with colliders, and returns its centre at eye
        /// height. 2.2 × 3.0 m, the clear section §12 specifies, because the placement
        /// rules this suite exercises all turn on how little room that is.
        /// </summary>
        private Vector3 BuildCorridor()
        {
            _world = new GameObject("[PresenceSessionTests World]");

            Slab("Floor", new Vector3(0f, -0.1f, 8f), new Vector3(2.2f, 0.2f, 32f));
            Slab("Ceiling", new Vector3(0f, 3.1f, 8f), new Vector3(2.2f, 0.2f, 32f));
            Slab("Wall_L", new Vector3(-1.2f, 1.5f, 8f), new Vector3(0.2f, 3f, 32f));
            Slab("Wall_R", new Vector3(1.2f, 1.5f, 8f), new Vector3(0.2f, 3f, 32f));

            Physics.SyncTransforms();
            return new Vector3(0f, 1.63f, 0f);
        }

        private void Slab(string name, Vector3 centre, Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(_world!.transform, worldPositionStays: false);
            cube.transform.SetPositionAndRotation(centre, Quaternion.identity);
            cube.transform.localScale = size;
        }

        // ====================================================================
        // The seam: does the scene produce the numbers the rules are written in?
        // ====================================================================

        /// <summary>
        /// A player standing under a §12 practical reads as lit on §03's own scale, and the
        /// same player two rooms away reads as dark.
        /// <para>
        /// This is the conversion the whole mechanic rests on. <c>GameConstants</c> may not
        /// reference an engine, so §03's threshold is a 0–1 quality and something has to
        /// turn Unity's photometric intensity into it. If that conversion is wrong in the
        /// dark direction the 그늘 never fires; if it is wrong in the lit direction it
        /// fires in a lit room, which is the opposite of the mechanic.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator APracticalOverheadReadsAsLit_AndTwoRoomsAwayReadsAsDark()
        {
            var eye = BuildCorridor();

            var body = new GameObject("Player");
            body.transform.SetParent(_world!.transform, worldPositionStays: false);
            body.transform.position = new Vector3(0f, eye.y - PresenceSubject.SampleHeightMetres, 0f);
            var subject = body.AddComponent<PresenceSubject>();

            // ART.md §3.6: a §12 practical is intensity 1.1 over a 5.5 m range.
            var practical = new GameObject("Practical").AddComponent<Light>();
            practical.transform.SetParent(_world.transform, worldPositionStays: false);
            practical.type = LightType.Point;
            practical.range = 5.5f;
            practical.intensity = 1.1f;
            practical.transform.position = new Vector3(0f, 2.6f, 0f);

            yield return null;
            subject.RefreshSceneLights();

            var lit = subject.LightQuality();
            Assert.That(lit, Is.GreaterThanOrEqualTo(GameConstants.ClueMinReadableLightQuality),
                "standing under a §12 practical has to clear §03's readable threshold, or the 그늘 "
                + "pools in a lit room and the mechanic is inverted");

            practical.transform.position = new Vector3(0f, 2.6f, 24f);
            yield return null;

            var far = subject.LightQuality();
            Assert.That(far, Is.EqualTo(0f),
                "a lamp 24 m down the corridor must not count as light on this player");

            var reading = PresenceDensity.Sample(far, 1000f, 0f, onSurface: false);
            Assert.That(reading.Density, Is.GreaterThan(0f));
        }

        /// <summary>
        /// A lit torch in the player's own hand reads as fully lit, with no falloff — §03
        /// prices a lit beam as "괴물이 잘 본다", which is the same statement as "the holder
        /// is conspicuous". You cannot stand outside your own torch.
        /// </summary>
        [UnityTest]
        public IEnumerator ACarriedTorchReadsAsFullyLit_AndSwitchingItOffReadsAsDark()
        {
            var eye = BuildCorridor();

            var body = new GameObject("Player");
            body.transform.SetParent(_world!.transform, worldPositionStays: false);
            body.transform.position = new Vector3(0f, eye.y - PresenceSubject.SampleHeightMetres, 0f);
            var subject = body.AddComponent<PresenceSubject>();

            var torch = new GameObject("Torch").AddComponent<Light>();
            torch.transform.SetParent(body.transform, worldPositionStays: false);
            torch.type = LightType.Spot;
            torch.range = GameConstants.FlashlightRange;
            torch.spotAngle = GameConstants.FlashlightHalfAngle * 2f;
            torch.intensity = 2.6f;
            subject.AddCarriedLight(torch);

            yield return null;
            subject.RefreshSceneLights();

            Assert.That(subject.LightQuality(), Is.EqualTo(1f));

            torch.enabled = false;
            yield return null;

            Assert.That(subject.LightQuality(), Is.EqualTo(0f),
                "with the torch off and no other lamp, the player is in the dark — which is what "
                + "§03 never charged for and this entity does");
        }

        /// <summary>
        /// The guarantee, driven through the adapter rather than through the rules: a real
        /// monster transform inside <see cref="GameConstants.PresenceMonsterClearRadius"/>
        /// leaves nothing pooling, and the same player with the monster removed fills up.
        /// <para>
        /// §01 keeps its horror with one unkillable pursuer. <c>MonsterChaseTests</c> is
        /// 4/4 at 1% and this is the test that says the 그늘 cannot move those numbers,
        /// because it is not there while the monster is.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheMonsterStandingCloseLeavesNothingPooling()
        {
            var eye = BuildCorridor();

            var body = new GameObject("Player");
            body.transform.SetParent(_world!.transform, worldPositionStays: false);
            body.transform.position = new Vector3(0f, eye.y - PresenceSubject.SampleHeightMetres, 0f);
            var subject = body.AddComponent<PresenceSubject>();
            subject.OverrideLightQuality = 0f;

            var monster = new GameObject("Monster");
            monster.transform.SetParent(_world.transform, worldPositionStays: false);
            monster.transform.position = body.transform.position
                                         + new Vector3(0f, 0f, GameConstants.MonsterSightRange - 1f);

            var host = new GameObject("[PresenceDirector]");
            host.transform.SetParent(_world.transform, worldPositionStays: false);
            var director = host.AddComponent<PresenceDirector>();
            SetPrivate(director, "_selfDriven", false);
            SetPrivate(director, "_monster", monster.transform);
            director.Initialize();
            director.SetMatchElapsed(GameConstants.ThreatTierSeconds * (GameConstants.ThreatTierCount - 1));

            yield return null;

            Drive(director, GameConstants.PresenceSaturationSeconds * 2f);
            var state = director.StateOf(subject.PlayerIndex);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.Pooling01, Is.EqualTo(0f),
                "the 그늘 reached a player standing 19 m from the monster, inside §06's own sight range");

            // Take the monster away and the same unlit player fills up. Without this half
            // the assertion above would pass on a director that was not running at all.
            SetPrivate(director, "_monster", null);
            Drive(director, GameConstants.PresenceSaturationSeconds * 2f);

            Assert.That(director.StateOf(subject.PlayerIndex)!.TakenCount, Is.GreaterThanOrEqualTo(1),
                "with nothing clearing it, standing unlit at 동트기 전 has to cost something");
        }

        /// <summary>
        /// <b>The corridor test, and the reason it exists.</b> §12's clear section is 2.2 m,
        /// so from the middle of one every direction except along the corridor has a wall
        /// 1.1 m away. The first version of the placement rules kept the 그늘 out of the
        /// beam cone unconditionally and required 3.5 m of clear sight, which in that
        /// geometry is unsatisfiable — the figure had nowhere to stand in the shape the
        /// whole game is made of, and the only symptom was an empty frame.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFigureCanStandSomewhereInATwoPointTwoMetreCorridor()
        {
            var eye = BuildCorridor();

            var rig = new GameObject("Eye");
            rig.transform.SetParent(_world!.transform, worldPositionStays: false);
            rig.transform.SetPositionAndRotation(eye, Quaternion.identity);

            var host = new GameObject("[PresenceView]");
            host.transform.SetParent(_world.transform, worldPositionStays: false);
            var view = host.AddComponent<PresenceView>();
            view.Eye = rig.transform;
            view.BeamActive = false;

            yield return null;

            Assert.That(view.TryFindStandingSpot(rig.transform, out var spot), Is.True,
                "the 형상 could not be placed anywhere in a §12 corridor");

            var distance = Vector3.Distance(spot, rig.transform.position);
            Assert.That(distance, Is.GreaterThanOrEqualTo(PresenceView.FigureNearMetres - 0.5f));
            Assert.That(distance, Is.LessThanOrEqualTo(PresenceView.FigureFarMetres + 0.5f));
            Assert.That(spot.y, Is.EqualTo(0f).Within(0.05f), "the figure has to stand on the floor");
        }

        /// <summary>
        /// With the beam lit the 그늘 stays out of the cone, which is the mechanic stated in
        /// pixels: pointing the torch at it removes it.
        /// </summary>
        [UnityTest]
        public IEnumerator WithTheBeamLitTheFigureStaysOutOfTheCone()
        {
            var eye = BuildCorridor();

            var rig = new GameObject("Eye");
            rig.transform.SetParent(_world!.transform, worldPositionStays: false);
            rig.transform.SetPositionAndRotation(eye, Quaternion.identity);

            var host = new GameObject("[PresenceView]");
            host.transform.SetParent(_world.transform, worldPositionStays: false);
            var view = host.AddComponent<PresenceView>();
            view.Eye = rig.transform;
            view.BeamActive = true;

            yield return null;

            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (!view.TryFindStandingSpot(rig.transform, out var spot))
                {
                    continue;
                }

                var toSpot = spot - rig.transform.position;
                toSpot.y = 0f;
                Assert.That(Vector3.Angle(rig.transform.forward, toSpot.normalized),
                    Is.GreaterThanOrEqualTo(GameConstants.FlashlightHalfAngle),
                    "the 형상 was placed inside §03's beam, which is the one place it cannot be");
            }
        }

        /// <summary>
        /// Advances the director by <paramref name="seconds"/> the way a host does — one
        /// fixed step per call.
        /// <para>
        /// <b>Not <c>Simulate(seconds)</c>.</b> That is capped at
        /// <c>MaxStepsPerSimulate</c> whole steps and then drops the backlog, deliberately,
        /// so a host that hitches cannot fill the dark in one frame. Handing it ninety
        /// seconds therefore advances 0.16 s and silently answers a different question —
        /// which is exactly how the first version of this test passed its first assertion
        /// while measuring nothing at all.
        /// </para>
        /// </summary>
        private static void Drive(PresenceDirector director, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / GameConstants.FixedStep);
            for (var i = 0; i < steps; i++)
            {
                director.Simulate(GameConstants.FixedStep);
            }
        }

        private static void SetPrivate(object target, string field, object? value)
        {
            var info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, target.GetType().Name + " has no field " + field);
            info!.SetValue(target, value);
        }
    }
}
