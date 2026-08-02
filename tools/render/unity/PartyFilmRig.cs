#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HorrorGame.Gameplay.Player;
using HorrorGame.Gameplay.PlayerEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HorrorGame.EditorTools.Film
{
    /// <summary>
    /// Films four players walking a corridor with the creature behind them, as a PNG
    /// sequence for <c>ffmpeg</c>.
    /// <para>
    /// §01 is a four-player co-op game and every picture of it so far has had one person
    /// in it, because <c>Map_FirstSketch_Solo.unity</c> spawns one. This stages four,
    /// gives each of them a §04 role colour, samples the real clips out of
    /// <c>Player.fbx</c> at a phase offset per person so they are not marching in lockstep,
    /// and walks a camera past them.
    /// </para>
    /// <para>
    /// <b>Everything here is edit-mode.</b> No play mode, no build, so no
    /// <c>MatchDirector</c> and no physics — the four are posed, not simulated. That is
    /// honest for a piece of footage and it is why the file says so: this shows what the
    /// game's assets look like in the game's map under the game's lighting, not what a
    /// match plays like.
    /// </para>
    /// <para>
    /// Two consequences of edit mode the rig has to answer for itself. <c>LateUpdate</c>
    /// never fires, so <see cref="PlayerWorldArms.Apply"/> is called by hand after each
    /// sample — otherwise the footage would show the raised first-person arms on all four
    /// bodies, which is the exact pose that component exists to remove. And
    /// <c>BuildRig</c> hangs a camera and an <c>AudioListener</c> off every player; three
    /// of the four are stripped, or Unity warns once per frame and the wrong camera may
    /// render.
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod \
    ///   HorrorGame.EditorTools.Film.PartyFilmRig.Film -filmSpec /tmp/party.json
    /// </code>
    /// </summary>
    public static class PartyFilmRig
    {
        private const string PlayerModelPath = "Assets/Models/Characters/Player.fbx";
        private const string MonsterModelPath = "Assets/Models/Characters/Monster.fbx";
        private const string RoleMaterialFolder = "Assets/Models/Characters/Materials/";

        [System.Serializable]
        public sealed class Vec3
        {
            public float x;
            public float y;
            public float z;

            public Vector3 V => new Vector3(x, y, z);
        }

        [System.Serializable]
        public sealed class Walker
        {
            /// <summary>§04 role, used for the slot-0 material: Listener/Observer/Runner/Engineer/Flasher.</summary>
            public string role = "Listener";

            /// <summary>Where they start.</summary>
            public Vec3 start = new Vec3();

            /// <summary>Metres travelled over the whole shot, along <see cref="Spec.walkDirection"/>.</summary>
            public float travel = 6f;

            /// <summary>Compass heading in degrees.</summary>
            public float yaw;

            /// <summary>Clip name out of Player.fbx — Walk, Idle, Carry…</summary>
            public string clip = "Walk";

            /// <summary>0~1 offset into the cycle, so four people are not one person copied.</summary>
            public float phase;
        }

        [System.Serializable]
        public sealed class Spec
        {
            /// <summary>
            /// Lay the shot out around the player the scene already spawns, instead of
            /// around world coordinates in this file.
            /// <para>
            /// Two takes were lost to coordinates: the first put three of the four
            /// off-screen, the second put all four behind a wall. The solo scene spawns a
            /// player at a spot the map generator cleared, so anchoring there means every
            /// offset below is measured from somewhere a body demonstrably fits.
            /// </para>
            /// </summary>
            public bool anchorOnScenePlayer = true;

            public string scene = "Assets/Scenes/Map_FirstSketch_Solo.unity";
            public string outputDir = "/tmp/party";
            public int width = 1280;
            public int height = 720;
            public int frames = 96;

            /// <summary>Camera at the first and last frame; it lerps between them.</summary>
            public Vec3 cameraFrom = new Vec3();
            public Vec3 cameraTo = new Vec3();
            public Vec3 cameraEulerFrom = new Vec3();
            public Vec3 cameraEulerTo = new Vec3();
            public float fov = 60f;

            /// <summary>Unit direction the party walks in.</summary>
            public Vec3 walkDirection = new Vec3 { z = 1f };

            public List<Walker> walkers = new List<Walker>();

            /// <summary>§03's beam, on. Without it the footage is a black rectangle.</summary>
            public bool torches = true;

            public bool monster = true;
            public Vec3 monsterStart = new Vec3();
            public float monsterTravel = 9f;
            public float monsterYaw;
        }

        /// <summary>Batch entry point. Writes <c>frame_0000.png</c>… and exits 0 or 1.</summary>
        public static void Film()
        {
            try
            {
                var path = ArgValue("-filmSpec")
                           ?? throw new System.InvalidOperationException("-filmSpec <json> is required.");
                var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(path));
                var written = Shoot(spec);
                Debug.Log("[PartyFilm] wrote " + written + " frame(s) to " + spec.outputDir);
                EditorApplication.Exit(0);
            }
            catch (System.Exception error)
            {
                Debug.LogError("[PartyFilm] " + error);
                EditorApplication.Exit(1);
            }
        }

        private static int Shoot(Spec spec)
        {
            EditorSceneManager.OpenScene(spec.scene, OpenSceneMode.Single);
            Directory.CreateDirectory(spec.outputDir);

            if (spec.anchorOnScenePlayer)
            {
                AnchorOnScenePlayer(spec);
            }

            HideScenePlayer();

            var camera = new GameObject("FilmCamera").AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = spec.fov;

            var clips = ClipsOf(PlayerModelPath);
            var party = new List<(GameObject rig, Walker walker, AnimationClip? clip,
                PlayerWorldArms? arms, float rate)>();

            foreach (var walker in spec.walkers)
            {
                var rig = PlayerFeelHarnessMenu.BuildRig();
                if (rig == null)
                {
                    throw new System.InvalidOperationException(PlayerModelPath + " is missing.");
                }

                StripOwnerOnlyParts(rig);
                Paint(rig, walker.role);

                var view = rig.GetComponent<PlayerFirstPersonView>();
                if (view != null)
                {
                    // Not the local player: draw the whole body, and let PlayerWorldArms
                    // lower the arms — which is the whole reason this footage is worth
                    // taking at all.
                    view.IsOwner = false;
                    view.Apply();
                }

                if (spec.torches)
                {
                    LightTheTorch(rig);
                }

                clips.TryGetValue(walker.clip, out var clip);
                // 1.0. The spec's travel is now driven by Player.clips.json's measured
                // ground speed, so the clip is already playing at the speed its own stance
                // was built for. Two earlier attempts derived a rate here from the ANKLE's
                // travel and both made it worse, because an ankle legitimately outruns the
                // ground while a foot rolls from heel to toe.
                const float rate = 1f;
                if (clip != null)
                {
                    var body = spec.frames > 0 ? walker.travel / (spec.frames / 24f) : 0f;
                    Debug.Log("[PartyFilm] " + walker.role + " " + walker.clip
                              + ": body " + body.ToString("F3") + " m/s from Player.clips.json");
                }

                party.Add((rig, walker, clip, rig.GetComponent<PlayerWorldArms>(), rate));
            }

            // Does the sample actually land on the skeleton? Two takes have now shipped
            // with the legs frozen, and looking at dark frames cannot tell "the clip did
            // nothing" from "the clip is subtle". This prints the thigh angle at three
            // points of the cycle: three identical numbers mean the sample is not landing
            // and nothing downstream is worth debugging.
            if (party.Count > 0 && party[0].clip != null)
            {
                var probe = party[0].rig;
                var thigh = HorrorGame.Gameplay.Player.PlayerRigBones.Find(probe.transform, "LeftUpperLeg");
                var animator = probe.GetComponentInChildren<Animator>();
                Debug.Log("[PartyFilm] probe rig: animator=" + (animator != null)
                          + " avatar=" + (animator != null && animator.avatar != null)
                          + " human=" + (animator != null && animator.avatar != null && animator.avatar.isHuman)
                          + " thighBone=" + (thigh != null));
                // The stride, measured in the rig's OWN space with the body pinned.
                // A planted foot must travel backward here at exactly the speed the body
                // travels forward in the world; if the local travel is short, the world
                // foot skates by the difference however good the pose looks.
                var clip0 = party[0].clip!;
                var foot = HorrorGame.Gameplay.Player.PlayerRigBones.Find(probe.transform, "LeftFoot");
                probe.transform.position = Vector3.zero;
                probe.transform.rotation = Quaternion.identity;
                float lo = 1e9f, hi = -1e9f;
                const int steps = 32;
                var contactFirst = -1;
                var contactLast = -1;
                var zs = new float[steps];
                for (var k = 0; k < steps; k++)
                {
                    var time = clip0.length * k / steps;
                    BeginFrame();
                    Sample(probe, clip0, time);
                    EndFrame();
                    var local = foot != null ? probe.transform.InverseTransformPoint(foot.position) : Vector3.zero;
                    zs[k] = local.z;
                    lo = Mathf.Min(lo, local.z);
                    hi = Mathf.Max(hi, local.z);
                    var down = local.y <= 0.14f;
                    if (down)
                    {
                        if (contactFirst < 0)
                        {
                            contactFirst = k;
                        }

                        contactLast = k;
                    }

                    Debug.Log("[PartyFilm] stride k=" + k.ToString("00")
                              + " localZ " + local.z.ToString("F3")
                              + " localY " + local.y.ToString("F3")
                              + (down ? "  CONTACT" : string.Empty));
                }

                if (contactFirst >= 0 && contactLast > contactFirst)
                {
                    var span = (contactLast - contactFirst) / (float)steps * clip0.length;
                    var travel = Mathf.Abs(zs[contactLast] - zs[contactFirst]);
                    Debug.Log("[PartyFilm] CONTACT PHASE k" + contactFirst + "~k" + contactLast
                              + ": foot travels " + travel.ToString("F3") + " m in "
                              + span.ToString("F3") + " s = " + (travel / span).ToString("F2")
                              + " m/s. The body must move forward at exactly this or the "
                              + "planted foot slides by the difference.");
                }

                Debug.Log("[PartyFilm] STRIDE local foot travel " + (hi - lo).ToString("F3")
                          + " m over a " + clip0.length.ToString("F3") + " s cycle."
                          + " A 4.53 m/s run needs about " + (4.53f * clip0.length).ToString("F3")
                          + " m of ground per cycle, and one foot covers a whole cycle's worth.");
            }

            var monster = spec.monster ? StageMonster() : null;
            var monsterClips = monster != null ? ClipsOf(MonsterModelPath) : new Dictionary<string, AnimationClip>();
            monsterClips.TryGetValue("Chase", out var chase);

            var dir = spec.walkDirection.V.normalized;
            var written = 0;
            var skate = new SkateMeter(1f / 24f);

            for (var f = 0; f < spec.frames; f++)
            {
                var t = spec.frames <= 1 ? 0f : f / (float)(spec.frames - 1);

                camera.transform.position = Vector3.Lerp(spec.cameraFrom.V, spec.cameraTo.V, t);
                camera.transform.rotation = Quaternion.Slerp(
                    Quaternion.Euler(spec.cameraEulerFrom.V), Quaternion.Euler(spec.cameraEulerTo.V), t);

                BeginFrame();
                foreach (var (rig, walker, clip, arms, rate) in party)
                {
                    rig.transform.position = walker.start.V + dir * (walker.travel * t);
                    rig.transform.rotation = Quaternion.Euler(0f, walker.yaw, 0f);
                    if (clip != null)
                    {
                        var cycle = clip.length <= 0f ? 1f : clip.length;
                        // Playback rate, not raw time: the clip's stance is authored for a
                        // speed of its own and the body moves at §05's, so the clip has to
                        // be played at the ratio or the planted foot slides by the gap.
                        Sample(rig, clip,
                               ((t * spec.frames / 24f * rate) + walker.phase * cycle) % cycle);
                    }

                }

                if (monster != null)
                {
                    monster.transform.position = spec.monsterStart.V + dir * (spec.monsterTravel * t);
                    monster.transform.rotation = Quaternion.Euler(0f, spec.monsterYaw, 0f);
                    if (chase != null)
                    {
                        var cycle = chase.length <= 0f ? 1f : chase.length;
                        Sample(monster, chase, (t * spec.frames / 24f) % cycle);
                    }
                }

                if (party.Count > 0)
                {
                    skate.Observe(party[0].rig, RUN_SPEED_HINT);
                }

                var file = Path.Combine(spec.outputDir,
                    "frame_" + f.ToString("0000", CultureInfo.InvariantCulture) + ".png");
                WritePng(file, camera, spec.width, spec.height);
                written++;
            }

            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            skate.Report();
            return written;
        }

        /// <summary>
        /// Poses one rig at one time in a clip, through the editor's animation mode.
        /// <para>
        /// <b>Not <c>AnimationClip.SampleAnimation</c>.</b> That call goes through the
        /// legacy path and does nothing useful to a <b>Humanoid</b> rig, which is what
        /// <c>Player.fbx</c> imports as. The first cut of this film used it and the
        /// footage showed exactly that: four bodies sliding down the corridor in their
        /// bind pose, legs still, while the creature — Generic, so it sampled fine —
        /// waddled along beside them at the wrong stride rate. The owner's words were
        /// "사람은 떠다니고 괴물은 뒤뚱뒤뚱", and both halves of that were one bug each.
        /// </para>
        /// <para>
        /// <c>AnimationMode.SampleAnimationClip</c> runs the retargeting the avatar
        /// describes, so a humanoid clip lands on the humanoid skeleton.
        /// </para>
        /// </summary>
        private static void Sample(GameObject rig, AnimationClip clip, float time)
        {
            var animator = rig.GetComponentInChildren<Animator>();
            var target = animator != null ? animator.gameObject : rig;
            AnimationMode.SampleAnimationClip(target, clip, time);
        }

        /// <summary>Run's measured ground speed, m/s. gen_player_ai's ANIM_REPORT.</summary>
        private const float RUN_SPEED_HINT = 4.53f;

        /// <summary>
        /// The speed the planted foot travels backward in the rig's own space, measured
        /// from the clip's longest unbroken ground-contact run.
        /// <para>
        /// This is the number a body has to move forward at, and it is not the number the
        /// generator publishes. gen_player_ai reports Run at 4.53 m/s from two chosen
        /// stance keys; sampled at 32 points and gated on foot height, the contiguous
        /// contact window k0~k7 travels 0.657 m in 0.1166 s — <b>5.63 m/s</b>. Translating
        /// at 4.5 against a stance authored for 5.63 slides the foot 1.13 m/s, which is
        /// most of the 2.0 m/s the skate meter was reading.
        /// </para>
        /// <para>
        /// Measuring it here rather than hard-coding it is the same decision
        /// MonsterAnimationDriver already made at runtime: playback rate is
        /// measured ÷ authored, and a clip that is re-authored moves this number with it.
        /// </para>
        /// </summary>
        private static float StanceSpeed(GameObject rig, AnimationClip clip)
        {
            var ankle = HorrorGame.Gameplay.Player.PlayerRigBones.Find(rig.transform, "LeftFoot");
            var toes = HorrorGame.Gameplay.Player.PlayerRigBones.Find(rig.transform, "LeftToes");
            if (ankle == null || clip.length <= 0f)
            {
                return 0f;
            }

            const int steps = 48;
            const float contact = 0.14f;
            var wasDown = false;
            int runStart = -1, bestStart = -1, bestEnd = -1;
            var z = new float[steps];

            for (var k = 0; k < steps; k++)
            {
                BeginFrame();
                AnimationMode.SampleAnimationClip(
                    rig.GetComponentInChildren<Animator>()?.gameObject ?? rig, clip,
                    clip.length * k / steps);
                EndFrame();

                // Whichever of the two is on the ground this frame is the contact point.
                var contactBone = toes != null && toes.position.y < ankle.position.y ? toes : ankle;
                var local = rig.transform.InverseTransformPoint(contactBone.position);
                z[k] = local.z;
                var down = local.y <= contact;
                if (down && !wasDown)
                {
                    runStart = k;
                }

                if (down && runStart >= 0 && k - runStart > bestEnd - bestStart)
                {
                    bestStart = runStart;
                    bestEnd = k;
                }

                wasDown = down;
            }

            if (bestStart < 0 || bestEnd <= bestStart)
            {
                return 0f;
            }

            var seconds = (bestEnd - bestStart) / (float)steps * clip.length;
            return Mathf.Abs(z[bestEnd] - z[bestStart]) / seconds;
        }

        /// <summary>
        /// Measures foot skate — the one number that says whether a run reads as running.
        /// <para>
        /// A planted foot has zero horizontal velocity in world space. Everything else is
        /// the character moonwalking, and it is the difference between a stride and a
        /// slide however good the pose is. <c>Monster.clips.json</c> already publishes
        /// <c>residual_slide_mps</c> per clip for exactly this reason; nothing measured it
        /// on the shipped rig, which is why three passes at "make it look like running"
        /// were argued from dark screenshots.
        /// </para>
        /// <para>
        /// Stance is identified by height rather than by a curve: whichever foot is lower
        /// this frame is the one taking the weight. That is crude for the instant both
        /// feet are level and exact everywhere else, and the mean over a whole shot is
        /// what matters.
        /// </para>
        /// </summary>
        private sealed class SkateMeter
        {
            private readonly float _dt;
            private float _slipTotal;
            private int _samples;
            private float _worst;
            private int _airborne;

            /// <summary>
            /// Metres above the rig's own floor that still counts as contact. The foot
            /// bone sits 0.095 m up at rest, and the clip lifts it to 0.554 m at the top
            /// of the stride, so anything under 0.14 m is a foot on the ground.
            /// </summary>
            private const float ContactHeight = 0.14f;

            public SkateMeter(float dt)
            {
                _dt = dt;
            }

            public void Observe(GameObject rig, float bodySpeed)
            {
                // Per foot, independently. "Whichever foot is lower" flips back and forth
                // in the double-contact frames where one foot lands before the other has
                // left, and each flip pairs two different feet's positions into one bogus
                // displacement — which is how a reading of 6.46 m/s appeared on a stance
                // that cannot exceed 5.5.
                Track(rig, "LeftFoot", ref _leftLast, ref _leftHas);
                Track(rig, "RightFoot", ref _rightLast, ref _rightHas);
            }

            private void Track(GameObject rig, string bone, ref Vector3 last, ref bool has)
            {
                // The contact point, not the ankle. A foot lands on its heel and leaves on
                // its toe, so the ankle legitimately travels FASTER than the ground — 5.49
                // against a 4.50 m/s body on this run, which is the foot rolling and not a
                // defect. Measuring the ankle counted that roll as skate and sent two
                // fixes after a number that was never wrong.
                var ankle = HorrorGame.Gameplay.Player.PlayerRigBones.Find(rig.transform, bone);
                var toes = HorrorGame.Gameplay.Player.PlayerRigBones.Find(
                    rig.transform, bone.Replace("Foot", "Toes"));
                if (ankle == null)
                {
                    return;
                }

                var foot = toes != null && toes.position.y < ankle.position.y ? toes : ankle;
                var here = foot.position;
                if (here.y - rig.transform.position.y > ContactHeight)
                {
                    has = false;
                    _airborne++;
                    return;
                }

                if (has)
                {
                    var slip = new Vector2(here.x - last.x, here.z - last.z).magnitude / _dt;
                    _slipTotal += slip;
                    _samples++;
                    if (slip > _worst)
                    {
                        _worst = slip;
                    }
                }

                last = here;
                has = true;
            }

            private Vector3 _leftLast;
            private Vector3 _rightLast;
            private bool _leftHas;
            private bool _rightHas;

            public void Report()
            {
                if (_samples == 0)
                {
                    Debug.Log("[PartyFilm] skate: no stance samples.");
                    return;
                }

                var mean = _slipTotal / _samples;
                Debug.Log("[PartyFilm] SKATE mean " + mean.ToString("F3") + " m/s, worst "
                          + _worst.ToString("F3") + " m/s, over " + _samples + " stance frames ("
                          + _airborne + " airborne frames skipped)"
                          + " — 0.000 is a planted foot; anything else is the character sliding.");
            }
        }

        /// <summary>
        /// Opens one sampling block for the whole frame.
        /// <para>
        /// <b>One block per frame, not one per rig.</b> <c>BeginSampling</c> reverts every
        /// property recorded since the last block before it starts — that is what stops a
        /// pose accumulating when the Animation window is scrubbed. Wrapping each rig in
        /// its own pair therefore meant sampling the second body undid the first, and the
        /// third undid the second: only the last rig sampled held a pose, and the trace
        /// said so exactly. The left foot's offset from its own hips was identical on
        /// every frame of an eight-frame run —
        /// </para>
        /// <code>
        ///   f0  foot (40.407, 0.095, 84.359)   rig (40.300, 0.000, 84.350)
        ///   f7  foot (40.407, 0.095, 82.859)   rig (40.300, 0.000, 82.850)
        /// </code>
        /// <para>
        /// — a body travelling 1.5 m with its legs welded to it. That is the floating the
        /// owner kept pointing at, and it survived two fixes aimed at the wrong thing.
        /// </para>
        /// </summary>
        private static void BeginFrame()
        {
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            AnimationMode.BeginSampling();
        }

        private static void EndFrame()
        {
            AnimationMode.EndSampling();
        }

        /// <summary>
        /// Takes the scene's own player out of the picture once its transform has been
        /// read. It is the anchor for the layout and it is also a fifth body standing in
        /// the middle of the shot with its first-person arms across the lens.
        /// </summary>
        private static void HideScenePlayer()
        {
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            if (motor != null)
            {
                motor.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Rewrites every position in the spec as an offset from the scene's own player.
        /// <para>
        /// The spec's coordinates are read as "metres ahead / metres right" of that
        /// player's own facing rather than as world space: X is right, Z is ahead. Nothing
        /// in this file then has to know where in the building the shot is.
        /// </para>
        /// </summary>
        private static void AnchorOnScenePlayer(Spec spec)
        {
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            if (motor == null)
            {
                Debug.LogWarning("[PartyFilm] no PlayerMotor in the scene; using world coordinates.");
                return;
            }

            var origin = motor.transform.position;
            var yaw = motor.transform.eulerAngles.y;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Debug.Log("[PartyFilm] anchored on the scene player at "
                      + origin.ToString("F2") + " facing " + yaw.ToString("F1") + " deg");

            Vec3 Place(Vec3 local)
            {
                var world = origin + rot * new Vector3(local.x, local.y, local.z);
                return new Vec3 { x = world.x, y = world.y, z = world.z };
            }

            spec.cameraFrom = Place(spec.cameraFrom);
            spec.cameraTo = Place(spec.cameraTo);
            spec.cameraEulerFrom.y += yaw;
            spec.cameraEulerTo.y += yaw;
            spec.monsterStart = Place(spec.monsterStart);
            spec.monsterYaw += yaw;

            var dir = rot * new Vector3(spec.walkDirection.x, 0f, spec.walkDirection.z);
            spec.walkDirection = new Vec3 { x = dir.x, y = 0f, z = dir.z };

            foreach (var walker in spec.walkers)
            {
                walker.start = Place(walker.start);
                walker.yaw += yaw;
            }
        }

        /// <summary>
        /// Switches §03's beam on and points it where the body faces.
        /// <para>
        /// <c>BuildRig</c> leaves the torch stowed on purpose — §10 makes switching it on
        /// a decision with a cost, and a rig that arrived lit would show the player they
        /// had made it before they had. In a piece of footage that reasoning inverts: a
        /// party walking a §03 corridor with no beams is a black rectangle, which is what
        /// the first take of this shot came back as.
        /// </para>
        /// </summary>
        private static void LightTheTorch(GameObject rig)
        {
            foreach (var light in rig.GetComponentsInChildren<Light>(true))
            {
                if (!light.gameObject.name.Contains("Flashlight"))
                {
                    continue;
                }

                light.enabled = true;
                light.gameObject.SetActive(true);
                // Chest height, angled a few degrees down, which is where a carried torch
                // points and what puts the pool of light on the floor ahead of the boots.
                light.transform.localPosition = new Vector3(0.16f, 1.28f, 0.22f);
                light.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
            }
        }

        /// <summary>
        /// Four rigs means four cameras and four AudioListeners unless something removes
        /// them. Unity renders whichever camera it likes and warns once a frame about the
        /// listeners; both are noise in a batch log that has to stay readable.
        /// </summary>
        private static void StripOwnerOnlyParts(GameObject rig)
        {
            foreach (var listener in rig.GetComponentsInChildren<AudioListener>(true))
            {
                Object.DestroyImmediate(listener);
            }

            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        /// <summary>Puts the §04 role colour on slot 0, which is where the model keeps it.</summary>
        private static void Paint(GameObject rig, string role)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(RoleMaterialFolder + "Role_" + role + ".mat");
            if (material == null)
            {
                Debug.LogWarning("[PartyFilm] no material for role " + role + "; leaving the default.");
                return;
            }

            foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var slots = renderer.sharedMaterials;
                if (slots.Length == 0 || slots[0] == null || !slots[0].name.StartsWith("Role_"))
                {
                    continue;
                }

                slots[0] = material;
                renderer.sharedMaterials = slots;
            }
        }

        private static GameObject? StageMonster()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterModelPath);
            if (model == null)
            {
                Debug.LogWarning("[PartyFilm] " + MonsterModelPath + " is missing; filming without it.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "FilmMonster";
            return instance;
        }

        private static Dictionary<string, AnimationClip> ClipsOf(string modelPath)
        {
            var found = new Dictionary<string, AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    // FBX clip names arrive as "Player_Rig|Walk".
                    var bar = clip.name.LastIndexOf('|');
                    found[bar >= 0 ? clip.name.Substring(bar + 1) : clip.name] = clip;
                }
            }

            return found;
        }

        private static void WritePng(string path, Camera camera, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;

                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        private static string? ArgValue(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
