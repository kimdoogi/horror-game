using System;

// The map authoring sources compiled in by HorrorGame.Sim.csproj are engine-free
// with exactly one exception: MapSketch.RoomWouldBlockTheStoreyAbove reports B-003
// through UnityEngine.Debug.LogError. Unity resolves that call against the real
// engine; outside Unity it has to resolve against something, and this is it.
//
// This namespace is deliberately tiny and deliberately loud. It is not an engine
// stand-in and nothing else in the simulator may use it — if a second engine call
// ever appears in the authoring code, the right response is to move that call out
// of the authoring code, not to widen this file.
namespace UnityEngine
{
    /// <summary>
    /// The one engine symbol §12's map authoring reaches for, answered outside the
    /// editor so the simulator can build the shipped building.
    /// <para>
    /// Everything here goes to stderr rather than to a swallowed log. B-003 — a 개방
    /// 공간 whose roof rises into the corridor above, dropped rather than built — is a
    /// map defect that changes the graph the simulator is about to measure, so a run
    /// that hits it must say so where the operator will see it (docs/BLOCKERS.md
    /// B-003, docs/STATUS.md §3.2).
    /// </para>
    /// </summary>
    internal static class Debug
    {
        /// <summary>
        /// Reports a map defect the authoring code found. Written to stderr, because
        /// the simulator's stdout is the balance report and a defect that scrolled
        /// past inside it would be read as data.
        /// </summary>
        /// <param name="message">The generator's own message, verbatim.</param>
        internal static void LogError(object message) =>
            Console.Error.WriteLine("[MapSketch] " + message);

        /// <summary>As <see cref="LogError"/>, for a condition that does not change the graph.</summary>
        /// <param name="message">The generator's own message, verbatim.</param>
        internal static void LogWarning(object message) =>
            Console.Error.WriteLine("[MapSketch] " + message);

        /// <summary>As <see cref="LogError"/>, for information the generator prints while working.</summary>
        /// <param name="message">The generator's own message, verbatim.</param>
        internal static void Log(object message) =>
            Console.Error.WriteLine("[MapSketch] " + message);
    }
}
