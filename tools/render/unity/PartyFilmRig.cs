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
            var party = new List<(GameObject rig, Walker walker, AnimationClip? clip, PlayerWorldArms? arms)>();

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
                party.Add((rig, walker, clip, rig.GetComponent<PlayerWorldArms>()));
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
                for (var k = 0; k < 3; k++)
                {
                    BeginFrame();
                    Sample(probe, party[0].clip!, party[0].clip!.length * k / 3f);
                    EndFrame();
                    Debug.Log("[PartyFilm] t=" + (k / 3f).ToString("0.00")
                              + " LeftUpperLeg local euler "
                              + (thigh != null ? thigh.localEulerAngles.ToString("F2") : "n/a"));
                }
            }

            var monster = spec.monster ? StageMonster() : null;
            var monsterClips = monster != null ? ClipsOf(MonsterModelPath) : new Dictionary<string, AnimationClip>();
            monsterClips.TryGetValue("Chase", out var chase);

            var dir = spec.walkDirection.V.normalized;
            var written = 0;

            for (var f = 0; f < spec.frames; f++)
            {
                var t = spec.frames <= 1 ? 0f : f / (float)(spec.frames - 1);

                camera.transform.position = Vector3.Lerp(spec.cameraFrom.V, spec.cameraTo.V, t);
                camera.transform.rotation = Quaternion.Slerp(
                    Quaternion.Euler(spec.cameraEulerFrom.V), Quaternion.Euler(spec.cameraEulerTo.V), t);

                BeginFrame();
                foreach (var (rig, walker, clip, arms) in party)
                {
                    rig.transform.position = walker.start.V + dir * (walker.travel * t);
                    rig.transform.rotation = Quaternion.Euler(0f, walker.yaw, 0f);
                    if (clip != null)
                    {
                        var cycle = clip.length <= 0f ? 1f : clip.length;
                        Sample(rig, clip, ((t * spec.frames / 24f) + walker.phase * cycle) % cycle);
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

                // Is the sampled pose still on the skeleton when the camera fires?
                // The probe above proves Sample() moves the thigh; it does not prove the
                // pose survives three more Sample() calls and a Render(). If this column
                // is constant the animation is being reverted and everything upstream is
                // a red herring.
                if (f < 8 && party.Count > 0)
                {
                    var foot = HorrorGame.Gameplay.Player.PlayerRigBones.Find(
                        party[0].rig.transform, "LeftFoot");
                    Debug.Log("[PartyFilm] f" + f
                              + " LeftFoot world " + (foot != null ? foot.position.ToString("F3") : "n/a")
                              + " rig " + party[0].rig.transform.position.ToString("F3"));
                }

                EndFrame();

                // After the sampling block closes, so animation mode does not record these
                // and revert them on the next frame. LateUpdate never runs in edit mode, so
                // without this the footage shows the raised first-person pose on all four.
                foreach (var (_, _, _, arms) in party)
                {
                    arms?.Apply();
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
