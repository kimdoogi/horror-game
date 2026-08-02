using System;

// The map authoring sources compiled in by HorrorGame.Core.Tests.csproj are engine-free
// with exactly one exception: MapSketch.RoomWouldBlockTheStoreyAbove reports B-003
// through UnityEngine.Debug.LogError. Unity resolves that call against the real engine;
// outside Unity it has to resolve against something, and this is it.
//
// A near-copy of core/HorrorGame.Sim/UnityDebugShim.cs on purpose. The alternative is a
// shared project that exists only to hold nine lines of stand-in, and sharing it would
// invite the thing both copies are written to prevent: a general-purpose fake engine
// that authoring code can quietly grow to depend on. If a second engine symbol ever
// shows up in the authoring code, move the call out of the authoring code rather than
// widening either file.
namespace UnityEngine
{
    /// <summary>
    /// The one engine symbol §12's map authoring reaches for, answered outside the
    /// editor so the test suite can build the shipped building.
    /// <para>
    /// Everything here goes to stderr. B-003 — a 개방 공간 whose roof rises into the
    /// corridor above, dropped rather than built — changes the graph a test is about to
    /// assert on, so a run that hits it must say so somewhere the operator will see it,
    /// not into a swallowed log that makes the assertion look like the whole story.
    /// </para>
    /// </summary>
    internal static class Debug
    {
        /// <summary>Reports a map defect the authoring code found. Verbatim, to stderr.</summary>
        /// <param name="message">The generator's own message.</param>
        internal static void LogError(object message) =>
            Console.Error.WriteLine("[MapSketch] " + message);

        /// <summary>As <see cref="LogError"/>, for a condition that does not change the graph.</summary>
        /// <param name="message">The generator's own message.</param>
        internal static void LogWarning(object message) =>
            Console.Error.WriteLine("[MapSketch] " + message);

        /// <summary>As <see cref="LogError"/>, for information the generator prints while working.</summary>
        /// <param name="message">The generator's own message.</param>
        internal static void Log(object message) =>
            Console.Error.WriteLine("[MapSketch] " + message);
    }
}
