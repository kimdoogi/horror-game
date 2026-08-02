#nullable enable

using HorrorGame.Gameplay.Player;
using UnityEngine;

namespace HorrorGame.Net.PlayerBridge
{
    /// <summary>
    /// Plugs the player layer into the network layer, once, without either of them
    /// knowing.
    /// <para>
    /// <b>Why an initialiser and not a component.</b> <see cref="NetRunner"/> exposes two
    /// seams — the body and the local view source — and both were empty in the shipped
    /// build. The obvious fix is a component in the match scene that fills them, and it is
    /// the wrong one: the descent's scene is <em>generated</em>, so anything that has to
    /// be in it has to be added by the generator and re-added on every regeneration, and a
    /// scene saved before that change is a scene where twenty people are invisible to each
    /// other with a green suite. <c>RaceLobby.Install</c> already made this call for
    /// exactly this reason — "Doing it here rather than from a component means the
    /// bootstrap scene does not have to be regenerated to gain a lobby" — and this is the
    /// same seam one layer down.
    /// </para>
    /// <para>
    /// <c>AfterSceneLoad</c> rather than <c>BeforeSceneLoad</c>: nothing is looked up here,
    /// only delegates installed, so either would work — but the factories are resolved
    /// lazily against whatever scene is loaded at the time, and running after the first
    /// one keeps that true from the very first call.
    /// </para>
    /// </summary>
    public static class NetPlayerBridge
    {
        /// <summary>
        /// True once Unity's own start-up has run <see cref="InstallOnStartup"/> in this
        /// process.
        /// <para>
        /// This is the property a test asserts on, and it is the only one that means what
        /// the defect was about. <see cref="Install"/> being callable proves nothing —
        /// a fixture can call it, and a fixture calling it is precisely how the project
        /// arrived at a network layer that worked only in tests. Nothing but Unity's
        /// <c>[RuntimeInitializeOnLoadMethod]</c> can make this true, so a test that
        /// checks it is checking that the shipped build wires itself.
        /// </para>
        /// </summary>
        public static bool InstalledAtStartup { get; private set; }

        /// <summary>
        /// Fills <see cref="NetRunner.LocalViewSourceFactory"/> and
        /// <see cref="NetRunner.VisualFactory"/>.
        /// <para>
        /// Public and idempotent so a fixture that deliberately clears the seams — several
        /// do, to prove a bodiless runner still replicates — can put them back without
        /// restarting the editor.
        /// </para>
        /// </summary>
        public static void Install()
        {
            NetRunner.LocalViewSourceFactory = ResolveLocalView;
            NetRunner.VisualFactory = NetRunnerBody.Build;
        }

        /// <summary>
        /// Empties both seams and forgets the cached rig. For a fixture that wants a
        /// runner with no body and no driver.
        /// </summary>
        public static void Uninstall()
        {
            NetRunner.LocalViewSourceFactory = null;
            NetRunner.VisualFactory = null;
            NetRunnerBody.Forget();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallOnStartup()
        {
            Install();
            InstalledAtStartup = true;
        }

        /// <summary>
        /// The rig this machine's player is driving, wrapped as §05's five rows — or null
        /// while there is none.
        /// <para>
        /// <c>FindFirstObjectByType</c> is the same search <c>MatchDirector.ResolveWiring</c>
        /// uses to find the local player, so the two cannot disagree about which rig is
        /// the local one. It runs at most once per send interval per owned runner
        /// (<see cref="NetLocalRunner"/>) and stops entirely once a source is bound, so
        /// the cost is a handful of scene scans in the seconds between a runner being
        /// spawned in §11's lobby and the descent's scene arriving.
        /// </para>
        /// <para>
        /// Null, not an exception, when there is no rig: a headless host and a lobby are
        /// both legitimate states with a runner and no legs.
        /// </para>
        /// </summary>
        private static INetPlayerViewSource? ResolveLocalView()
        {
            var rig = Object.FindFirstObjectByType<PlayerMotor>();
            return rig == null ? null : PlayerRigNetView.Attach(rig);
        }
    }
}
