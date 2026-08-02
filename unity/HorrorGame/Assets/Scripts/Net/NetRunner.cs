#nullable enable

using System;
using Mirror;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// §01's 주자 as a thing Mirror can spawn: the one place a networked runner is
    /// assembled, used by the host to build the authoritative copy and by every
    /// client to build its view of somebody else's.
    /// <para>
    /// <b>Why this is a factory and not a prefab.</b> Mirror's usual answer is a
    /// prefab asset dropped into <c>NetworkManager.spawnPrefabs</c>. This project
    /// cannot use that answer yet, and pretending otherwise would be the exact
    /// failure this repo keeps repeating. The player rig is assembled in code by
    /// <c>PlayerFeelHarnessMenu.BuildRig</c>, which lives in an
    /// <c>includePlatforms: ["Editor"]</c> assembly and reads <c>Runner.fbx</c>
    /// through <c>AssetDatabase</c> — none of which exists in a player. There is no
    /// runner prefab on disk to register, and an asset that only an editor menu can
    /// produce is an asset that drifts from the code that produces it. So the runner
    /// is built the same way on both ends, from this one function, and Mirror is told
    /// about it through the mechanism its own documentation names for exactly this
    /// case: "when no prefab exists for the spawned objects — such as when they are
    /// constructed dynamically at runtime" (<c>NetworkClient.RegisterSpawnHandler</c>).
    /// </para>
    /// <para>
    /// <b>The body is supplied, never invented.</b> <see cref="VisualFactory"/> is
    /// the seam a scene builder plugs the real rig into. When nothing is plugged in
    /// there is no renderer at all, and that is deliberate:
    /// <c>GameObject.CreatePrimitive</c> would give every runner a visible capsule in
    /// the editor and Unity's error magenta in a URP player, because URP only answers
    /// <c>RenderPipelineAsset.defaultMaterial</c> in the editor —
    /// <c>Interactable.Build</c> already carries the scar from shipping that once.
    /// A runner with no body replicates correctly and is invisible, which is an
    /// honest bug; a runner that is magenta in the build and grey in the editor is a
    /// green number.
    /// </para>
    /// <para>
    /// <b>Authority.</b> §13 puts the host in charge, and <see cref="NetPlayer"/>
    /// already implements that split — the owner reports, the host clamps, every
    /// observer receives the host's value. This type adds no authority of its own; it
    /// only guarantees that the object those rules run on exists on both machines and
    /// is the same shape on both.
    /// </para>
    /// </summary>
    public static class NetRunner
    {
        /// <summary>
        /// The string the asset id is derived from. A namespace-qualified type name
        /// because it is already unique in this project and cannot silently collide
        /// with a future spawnable.
        /// </summary>
        private const string AssetIdSeed = "HorrorGame.Net.NetRunner";

        /// <summary>
        /// Name given to the child the visual is parented under, when there is one.
        /// <para>
        /// Public because the owner has to be able to find it again and switch it off:
        /// §01's local player already has a rig of their own with the camera on it, so a
        /// second body standing at their spawn marker would be a duplicate of themselves
        /// that never moves. <see cref="NetLocalRunner"/> is the one that does it, and it
        /// finds the child by this name rather than by index.
        /// </para>
        /// </summary>
        public const string VisualChildName = "Visual";

        /// <summary>
        /// Where a runner visual may be found at run time if nothing set
        /// <see cref="VisualFactory"/>.
        /// <para>
        /// <c>Resources</c> rather than a serialized field, because the object that
        /// would hold the field — a <see cref="HorrorGameNetworkManager"/> in a scene
        /// — does not exist in this project either (RaceLobby builds one at runtime).
        /// A path that resolves to nothing simply yields a bodiless runner; see the
        /// class remarks for why that is preferred to a primitive.
        /// </para>
        /// </summary>
        public const string VisualResourcePath = "Net/NetRunnerVisual";

        /// <summary>
        /// Mirror's id for "this object is a runner", identical on every machine.
        /// <para>
        /// Not a hand-picked number and not <c>Random</c>: it is the FNV-1a hash
        /// Mirror itself uses to turn names into wire ids
        /// (<c>Extensions.GetStableHashCode</c>, the same function that gives every
        /// <c>NetworkMessage</c> its id), applied to <see cref="AssetIdSeed"/>. That
        /// makes it stable across processes, platforms, builds and recompiles — which
        /// is the entire requirement, because the host puts this number in the spawn
        /// message and the client looks its handler up by it. It evaluates to
        /// 0x6AC0590D; Mirror rejects a zero asset id, and this is not zero.
        /// </para>
        /// </summary>
        public static readonly uint AssetId = unchecked((uint)AssetIdSeed.GetStableHashCode());

        /// <summary>
        /// Builds the body a runner is drawn with, or null for a runner with no
        /// renderer.
        /// <para>
        /// Set by whatever assembles the world — the solo scene builder, a playtest
        /// harness — so that the Net assembly never has to reference the editor-only
        /// code that knows where <c>Runner.fbx</c> is. ARCHITECTURE §3: the seam is a
        /// delegate returning a <c>GameObject</c>, not an import of the player layer.
        /// </para>
        /// </summary>
        public static Func<GameObject?>? VisualFactory { get; set; }

        /// <summary>
        /// Produces the thing that drives §05's five rows on the machine that owns a
        /// runner — the mouse, the keys and the legs — or null when this machine has no
        /// player rig to drive them with.
        /// <para>
        /// <b>Why this seam exists at all.</b> <c>NetPlayer.BindLocalSource</c> had zero
        /// callers outside tests for the whole life of the network layer, which meant the
        /// wire worked and nobody was on it: two peers connected, twenty sockets were
        /// accepted, and a human pressing W sent nothing. The interface was there and the
        /// implementation was not, and no test failed — the fixtures supplied their own
        /// <c>INetPlayerViewSource</c>, which is exactly the shape of a green number that
        /// is not in the build.
        /// </para>
        /// <para>
        /// A delegate rather than a direct reference, for the reason
        /// <see cref="INetPlayerViewSource"/> states outright: ARCHITECTURE §3 forbids
        /// this assembly from importing the movement controller to learn where a player
        /// is looking. The implementation lives in <c>HorrorGame.Net.PlayerBridge</c> —
        /// a satellite assembly that may see both halves, the same arrangement
        /// <c>HorrorGame.Net.SteamTransport</c> already uses to keep Steamworks out of
        /// this one — and installs itself from a
        /// <c>[RuntimeInitializeOnLoadMethod]</c> so no scene has to remember to.
        /// </para>
        /// <para>
        /// It returns null rather than throwing while there is no rig yet: a runner is
        /// spawned when a connection reports ready, which is one phase before the match
        /// scene exists (see <c>HorrorGameNetworkManager.OnServerReady</c>).
        /// <see cref="NetLocalRunner"/> keeps asking.
        /// </para>
        /// </summary>
        public static Func<INetPlayerViewSource?>? LocalViewSourceFactory { get; set; }

        /// <summary>
        /// Assembles a runner, ready to be handed to
        /// <c>NetworkServer.Spawn(runner, NetRunner.AssetId, owner)</c> on the host or
        /// returned from the client's spawn handler.
        /// <para>
        /// The object is created inactive and activated last. That is not tidiness:
        /// <c>NetworkIdentity.Awake</c> caches the <c>NetworkBehaviour</c>s it can see
        /// at the instant it runs, and on a live object <c>AddComponent</c> runs
        /// <c>Awake</c> immediately — so a <see cref="NetPlayer"/> added after the
        /// identity would compile, tick, and carry a null <c>netIdentity</c>. A
        /// prefab never has this problem; anything built in code does, which is one
        /// more honest cost of not having the prefab.
        /// </para>
        /// </summary>
        /// <param name="name">Scene name for the object. Only ever seen in a hierarchy or a log.</param>
        public static GameObject Build(string name)
        {
            var runner = new GameObject(string.IsNullOrEmpty(name) ? "NetRunner" : name);
            runner.SetActive(false);

            // NetPlayer carries [RequireComponent(typeof(NetworkIdentity))], so this
            // one line brings the identity with it. Asking for the identity a second
            // time would trip Unity's [DisallowMultipleComponent] and log a line that
            // reads like a defect, so it is only added if it is genuinely absent.
            runner.AddComponent<NetPlayer>();
            if (!runner.TryGetComponent(out NetworkIdentity _))
            {
                runner.AddComponent<NetworkIdentity>();
            }

            // §13's interest rules treat a player as Perception, not Secret: a runner
            // is exactly the thing other runners are entitled to perceive, and
            // HorrorInterestManagement defaults to Perception anyway — stamped
            // explicitly so the default is a decision rather than an omission.
            NetInterestScope.Apply(runner, NetInterestClass.Perception);

            // Added on both ends and on every copy, because neither end knows yet which
            // copy this is: ownership arrives with the spawn message, after this function
            // has returned. The component is inert on a copy this machine does not own —
            // see NetLocalRunner — so building it unconditionally costs one MonoBehaviour
            // per runner and removes the only alternative, which is a second construction
            // path that only the owner takes and that therefore only the owner can break.
            runner.AddComponent<NetLocalRunner>();

            AttachVisual(runner);

            runner.SetActive(true);
            return runner;
        }

        /// <summary>
        /// Teaches this machine's client how to build a runner it is told about.
        /// <para>
        /// Called from <c>HorrorGameNetworkManager.OnStartClient</c>. Without
        /// it a spawn message arrives, finds neither a registered prefab nor a
        /// handler for <see cref="AssetId"/>, and Mirror logs "Failed to spawn server
        /// object, did you forget to add it to the NetworkManager?" — which is
        /// precisely the state this project was in when it had zero
        /// <c>NetworkIdentity</c> prefabs: two peers connected, and nothing to say.
        /// </para>
        /// </summary>
        public static void RegisterClientSpawnHandler()
        {
            NetworkClient.RegisterSpawnHandler(AssetId, SpawnOnClient, UnspawnOnClient);
        }

        /// <summary>Forgets the handler. <c>NetworkClient.Shutdown</c> also clears it; this is for tests and for a client that stops without shutting down.</summary>
        public static void UnregisterClientSpawnHandler()
        {
            NetworkClient.UnregisterSpawnHandler(AssetId);
        }

        /// <summary>
        /// The client's side of a spawn. Mirror re-applies position, rotation and
        /// scale from the message immediately afterwards, so the placement here only
        /// matters for the frame the object is born in — but a runner that appears at
        /// the origin for one frame is a teammate blinking across the map, which
        /// <see cref="NetPlayer"/> already goes out of its way to avoid.
        /// </summary>
        private static GameObject SpawnOnClient(SpawnMessage message)
        {
            var runner = Build("NetRunner [netId=" + message.netId + "]");
            runner.transform.SetPositionAndRotation(message.position, message.rotation);
            return runner;
        }

        private static void UnspawnOnClient(GameObject spawned)
        {
            if (spawned != null)
            {
                UnityEngine.Object.Destroy(spawned);
            }
        }

        /// <summary>
        /// Parents a body under the runner if the game supplied one. Silent when it
        /// did not — see the class remarks; a warning per spawn would be twenty lines
        /// per match for a condition that is the same on every one of them.
        /// </summary>
        private static void AttachVisual(GameObject runner)
        {
            var visual = VisualFactory?.Invoke();

            if (visual == null)
            {
                var prefab = Resources.Load<GameObject>(VisualResourcePath);
                if (prefab != null)
                {
                    visual = UnityEngine.Object.Instantiate(prefab);
                }
            }

            if (visual == null)
            {
                return;
            }

            visual.name = VisualChildName;

            // worldPositionStays: false — the body belongs at the runner's feet, and a
            // factory that happened to build it at the origin must not drag it back
            // there once it is parented.
            visual.transform.SetParent(runner.transform, false);
        }
    }
}
