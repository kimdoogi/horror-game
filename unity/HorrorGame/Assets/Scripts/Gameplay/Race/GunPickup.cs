#nullable enable

using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Interaction;
using UnityEngine;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// A gun lying on the floor of a 막힌 길, and the detour that is worth taking for it.
    /// <para>
    /// <b>Why a dead end and not a corridor.</b> §12 puts a 막힌 길 at the end of every
    /// alcove and the map already marks all 152 of them. A gun there is a real decision in
    /// the shape §01 keeps making: leave the route you were running, spend the seconds,
    /// come back armed — or read the corridor, decide nobody is close enough to matter, and
    /// keep descending. Lying in a through-corridor it would be a thing you pick up because
    /// you walked over it, which is not a decision at all.
    /// </para>
    /// <para>
    /// <b>It is not networked, and that is the design.</b> The pickup is a scene object and
    /// taking it is a local claim the host confirms — see <see cref="RunnerGun"/>. A
    /// networked item with an owner would be one more thing that can desync in a race whose
    /// whole authority model (§13) is "the host decides and everybody renders what they are
    /// told". What crosses the wire is the CONSEQUENCE — somebody is holding a gun, and
    /// somebody has been shot — not the object.
    /// </para>
    /// <para>
    /// <b>Nothing in the scene carries this component and nothing should.</b>
    /// <c>MapSceneBuilder.BuildGuns</c> lays down the mesh, a trigger the crosshair can find
    /// and a name; <see cref="AttachAll"/> puts the behaviour on top at match start. That is
    /// the same split §12's 문 uses — the generator is an editor assembly and this is
    /// Assembly-CSharp, so the reference only ever runs one way — and it means a gun needs
    /// no scene authoring, survives a regeneration, and cannot be half-wired by a prefab
    /// that drifted.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GunPickup : Interactable
    {
        /// <summary>
        /// Group the generator hangs the guns under, inside <see cref="MarkerRootName"/>.
        /// Mirrors <c>MapSceneBuilder.GunRootName</c>.
        /// <para>
        /// Restated rather than referenced, exactly as <c>MatchMap</c>'s marker names and
        /// <c>Chute.DropHeightMetres</c> are: the generator lives in an editor assembly that
        /// a player build does not contain. <c>MatchMap</c>'s own remark is the standing
        /// rule — the scene is a contract between two assemblies that cannot see each other,
        /// and the <c>const string</c>s are the contract itself.
        /// </para>
        /// </summary>
        public const string GroupName = "Guns";

        /// <summary>
        /// Name the generator gives one gun, minus the storey number — <c>Gun_B3</c> is the
        /// gun on B3. Mirrors <c>MapSceneBuilder.GunNamePrefix</c>.
        /// <para>
        /// ASCII, and deliberately: Unity escapes Korean in <c>m_Name</c> as
        /// <c>\uXXXX</c>, so a scene object called 총 cannot be counted with a grep of the
        /// written <c>.unity</c> file — and this repository has already once believed a
        /// zero that meant "the grep was wrong". The player is shown 총; the file spells it
        /// in letters.
        /// </para>
        /// </summary>
        public const string NamePrefix = "Gun_B";

        /// <summary>Root of the generated map. Mirrors <c>MapSceneBuilder.MapRootName</c>.</summary>
        public const string MapRootName = "Map";

        /// <summary>Child of the root holding every marker group. Mirrors <c>MapSceneBuilder.MarkerRootName</c>.</summary>
        public const string MarkerRootName = "Markers";

        /// <inheritdoc />
        public override string Title
        {
            get { return "총"; }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                return "한 발. " + Gunplay.RangeMetres.ToString("0") + " m 안의 주자를 맞히면 "
                       + "그 사람은 출발선으로 돌아간다.";
            }
        }

        /// <summary>
        /// 줍기, until somebody has it.
        /// <para>
        /// It overrides <see cref="Interactable.Action"/> — the base class has no member
        /// called <c>Verb</c>, and this property was written against one. That is worth a
        /// line rather than a silent fix: the file was committed with the rule and the
        /// asset, and the pair of them compiled nowhere, because nothing in the project
        /// referenced this type and Unity had never imported it. A component that has never
        /// been in a build is exactly the failure this repository keeps finding.
        /// </para>
        /// </summary>
        public override string Action
        {
            get { return _taken ? string.Empty : "줍기"; }
        }

        /// <summary>
        /// The key does nothing once it has been taken. Without this the prompt would keep
        /// offering E on an invisible object, which teaches a runner that E does not work.
        /// </summary>
        public override bool AcceptsKey
        {
            get { return !_taken; }
        }

        private bool _taken;

        /// <summary>Whether somebody has already taken this one.</summary>
        public bool Taken
        {
            get { return _taken; }
        }

        /// <summary>
        /// Finds every gun the generator laid down and gives it this component.
        /// <para>
        /// The same shape as <c>MatchDirector.AttachChutes</c> and <c>AttachDoors</c>: the
        /// scene is searched by name and the behaviour is added on top of geometry that was
        /// written by something which cannot reference it. Idempotent, because more than one
        /// thing may reasonably want to be sure the guns are live — a match starting, a test
        /// loading the scene by hand — and a second component on one gun would let it be
        /// taken twice.
        /// </para>
        /// </summary>
        /// <returns>How many guns carry the component afterwards, which is how many are in the map.</returns>
        public static int AttachAll()
        {
            return AttachAllInternal();
        }

        /// <summary>
        /// Runs <see cref="AttachAll"/> on every scene load, for the whole life of the
        /// process.
        /// <para>
        /// <b>Why this and not a line in <c>MatchDirector.BeginMatch</c>.</b> That IS the
        /// better home — it is where <c>AttachChutes</c> and <c>AttachDoors</c> live — and
        /// the one-line diff for it is in this round's report. It is not taken here because
        /// <c>MatchDirector</c> is not this task's file, and a feature that only works once
        /// somebody else applies a diff is a feature that is not in the build. The whole
        /// standing lesson of this repository is that things pass review and turn out not to
        /// be in it.
        /// </para>
        /// <para>
        /// The descent scene is loaded by <c>RaceLobby</c> long after startup, so the
        /// startup call alone would find an empty scene; the subscription is what makes it
        /// work. Unsubscribe-then-subscribe because a domain reload that is disabled — Unity
        /// 6's default for entering play mode — leaves the previous run's handler attached.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            AttachAllInternal();
        }

        private static void OnSceneLoaded(
            UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            AttachAllInternal();
        }

        private static int AttachAllInternal()
        {
            var attached = 0;
            foreach (var group in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (group.name != GroupName || group.parent == null || group.parent.name != MarkerRootName)
                {
                    continue;
                }

                foreach (Transform child in group)
                {
                    if (!child.name.StartsWith(NamePrefix, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (child.GetComponent<GunPickup>() == null)
                    {
                        child.gameObject.AddComponent<GunPickup>();
                    }

                    attached++;
                }
            }

            return attached;
        }

        /// <summary>
        /// The crosshair was on this and the key went down. Hands it to the runner who
        /// pressed, and says why if they cannot have it.
        /// <para>
        /// <see cref="RunnerGun"/> is asked rather than told, because "can this runner hold
        /// a gun" is its rule and not this object's: one gun at a time, and a spent one is
        /// still a gun. A refusal is shown rather than swallowed —
        /// <see cref="Interactable"/>'s standing rule, and the reason it has a
        /// <see cref="Interactable.Refusal"/> at all.
        /// </para>
        /// </summary>
        public override void OnPressed(PlayerInteractor by)
        {
            if (_taken)
            {
                return;
            }

            var gun = by != null ? by.GetComponentInParent<RunnerGun>() : null;
            if (gun == null)
            {
                gun = RunnerGun.Local;
            }

            if (gun == null && by != null)
            {
                // The runner who pressed has no hands yet. Given to them here rather than
                // required on the rig, because the rig is assembled in three different
                // places — SoloPlaytest.BuildScene, RaceLobby's spawn and the PlayMode
                // harnesses — and a component that has to be added in all three is a
                // component that is missing in one of them. Same reasoning as
                // PlayerInteractor adding its own prompt screen in Awake.
                gun = by.gameObject.AddComponent<RunnerGun>();
            }

            if (gun == null)
            {
                Refusal = "총을 들 수 없다.";
                return;
            }

            if (!gun.TryTake(this))
            {
                Refusal = "이미 총을 들고 있다.";
                return;
            }

            Refusal = string.Empty;
        }

        /// <summary>
        /// Takes the gun, if it is still there.
        /// <para>
        /// The object is hidden rather than destroyed. A destroyed pickup cannot be put
        /// back, and §02's next round runs on the same scene — and a renderer switched off
        /// is one line to undo against a Destroy that needs the generator run again.
        /// </para>
        /// </summary>
        /// <returns>True if this call is the one that got it.</returns>
        public bool Take()
        {
            if (_taken)
            {
                return false;
            }

            _taken = true;

            var renderers = GetComponentsInChildren<Renderer>();
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }

            var colliders = GetComponentsInChildren<Collider>();
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            return true;
        }

        /// <summary>Puts it back. §02's next round on the same scene.</summary>
        public void Restore()
        {
            _taken = false;

            var renderers = GetComponentsInChildren<Renderer>();
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
            }

            // Only the crosshair's own trigger comes back on. The generator disabled the
            // importer's solid MeshCollider on purpose — a solid 0.13 m object in an alcove
            // is a wall to every reach audit in this project, which sweeps a capsule with
            // QueryTriggerInteraction.Ignore — so restoring every collider indiscriminately
            // would re-arm the exact obstruction the placement pass took out, and it would
            // only appear on the second round of a match.
            var colliders = GetComponentsInChildren<Collider>();
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger)
                {
                    colliders[i].enabled = true;
                }
            }
        }
    }
}
