#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core;

namespace HorrorGame.Gameplay.Race
{
    /// <summary>
    /// The facts about the party that do not survive the descent's scene load on
    /// their own. §11 · §13.
    /// <para>
    /// <b>Why a static and not a component.</b> Both are decided in <c>Bootstrap</c> and
    /// wanted in the descent scene, and the load in between is a
    /// <c>LoadSceneMode.Single</c> — every object that is not explicitly kept is destroyed
    /// halfway through the handover. <c>RaceLobby.AgreedSeed</c> already carries the seed
    /// this way and for this reason; this is the rest of the same envelope, kept separate
    /// so that <see cref="RaceRunners"/> does not have to reach into the lobby's private
    /// state to read it.
    /// </para>
    /// <para>
    /// <b>Deliberately a few fields and not a roster.</b> An earlier draft of this carried
    /// the seed, the field size and every name, and nothing read any of them: the seed is
    /// already static on the lobby, the names are already replicated in
    /// <see cref="RaceLobbyRosterMessage"/>, and the field size has no consumer until
    /// <c>MatchDirector.PlayersInMatch</c> stops being a constant. An object nothing reads
    /// is exactly the shape of this project's most expensive bug, so what is here is only
    /// what something downstream actually asks for. Each field below names its reader.
    /// </para>
    /// <para>
    /// <b>Consumed, not remembered.</b> <see cref="Clear"/> runs when a session ends, so a
    /// solo playtest started afterwards does not silently inherit a seat number from a
    /// race that is over — the same argument <c>RaceLobby.OnSceneLoaded</c> makes about
    /// the seed.
    /// </para>
    /// </summary>
    public static class RaceParty
    {
        private static int[] _seatConnectionIds = Array.Empty<int>();
        private static string[] _seatNames = Array.Empty<string>();

        /// <summary>True once a lobby has started a descent, and until the session ends.</summary>
        public static bool Settled { get; private set; }

        /// <summary>
        /// The building §13 chose for this race, or the zero value on a build with no
        /// roster.
        /// <para>
        /// <b>Its reader is the descent itself.</b> <c>RaceLobby.OnSceneLoaded</c> compares
        /// this against the generation stamp inside the scene that actually came up, which
        /// is the only check in the game that reads the ARTEFACT rather than a name two
        /// machines agreed on. Two builds holding different geometry under one scene name
        /// — one of them made before a rebake — are told apart by that comparison and by
        /// nothing else, and the alternative is a runner racing down a maze nobody else is
        /// in with no symptom to notice.
        /// </para>
        /// <para>
        /// It is here rather than only on <c>RaceLobby.AgreedBuilding</c> for the reason
        /// every other field here exists: the load in between is a
        /// <c>LoadSceneMode.Single</c>, and something after it has to be able to ask what
        /// was agreed before it. <c>AgreedBuilding</c> is consumed by the check the way the
        /// seed is consumed by <c>BeginMatch</c>; this copy outlives the session so a
        /// second descent or a §02 results screen can still name the building.
        /// </para>
        /// </summary>
        public static DescentBuilding Building { get; private set; }

        /// <summary>
        /// This machine's row in the lobby, or −1.
        /// <para>
        /// Used for one thing: choosing a fallback starting marker when §13's own placement
        /// never arrives, so a session that failed to replicate is a spread-out race rather
        /// than twenty people in one cell. The real placement comes from the host — see
        /// <see cref="RaceRunners"/> for why a locally derived one would be a second
        /// opinion about the same question.
        /// </para>
        /// </summary>
        public static int LocalSeat { get; private set; } = -1;

        /// <summary>
        /// Mirror's connection id for each seat, in the lobby's arrival order. Empty on a
        /// client.
        /// <para>
        /// The host is the only machine that has connection ids and the only one that
        /// needs them: it is what lets <see cref="RaceRunners"/> count how many of §11's
        /// seats still have a body after the scene load, which is the check that would
        /// have caught the whole party arriving in the building with nothing in it.
        /// Carried as ids rather than as connections because a connection that drops
        /// between the lobby and the descent must resolve to nothing rather than to a
        /// stale object.
        /// </para>
        /// </summary>
        public static int[] SeatConnectionIds
        {
            get { return _seatConnectionIds; }
        }

        /// <summary>
        /// Records what the lobby agreed on, immediately before the descent's scene load.
        /// </summary>
        /// <param name="localSeat">This machine's row. Anything outside the field becomes 0.</param>
        /// <param name="connectionIds">Connection id per seat, host side. Null or empty on a client.</param>
        public static void Settle(int localSeat, IReadOnlyList<int>? connectionIds)
        {
            // Clamped rather than refused: this is called at the one moment the race is
            // already committed to — the begin message has gone out — and a throw here
            // would strand everybody in a lobby that has just told them to leave.
            LocalSeat = localSeat < 0 || localSeat >= GameConstants.RaceRunnersMax ? 0 : localSeat;

            if (connectionIds == null)
            {
                _seatConnectionIds = Array.Empty<int>();
            }
            else
            {
                var kept = connectionIds.Count < GameConstants.RaceRunnersMax
                    ? connectionIds.Count
                    : GameConstants.RaceRunnersMax;

                _seatConnectionIds = new int[kept];
                for (var i = 0; i < kept; i++)
                {
                    _seatConnectionIds[i] = connectionIds[i];
                }
            }

            Settled = true;
        }

        /// <summary>
        /// Records which building the party agreed to descend into.
        /// <para>
        /// A separate call from <see cref="Settle"/> for the same reason
        /// <see cref="SettleNames"/> is: it is a different KIND of fact and it must be
        /// allowed to be absent. A build whose map pipeline has never baked a roster passes
        /// the zero value here, which means "the one building this build has" and is not an
        /// error — <c>RaceLobby.VerifyLoadedBuilding</c> simply has nothing to compare and
        /// says so instead of failing a match.
        /// </para>
        /// </summary>
        public static void SettleBuilding(DescentBuilding building)
        {
            Building = building;
        }

        /// <summary>
        /// Who is in each seat, by name, as the lobby's roster had them.
        /// <para>
        /// A separate call from <see cref="Settle"/> because it is a different KIND of
        /// fact: the seat and the connection ids are what §13 needs to run a race, and a
        /// name is only ever drawn. <see cref="Settle"/> must succeed on a machine that
        /// has no roster; this may quietly do nothing there.
        /// </para>
        /// <para>
        /// <b>Why it exists at all.</b> <c>RaceDirector.NameOf</c> falls back to "N번"
        /// whenever <c>_names</c> is empty, and on a client nothing ever filled it —
        /// <c>SetName</c> has no caller outside the host's own path. So every client drew
        /// a standings board reading 1번 … 20번 while the host drew people's names, and
        /// §02's results screen is names.
        /// </para>
        /// </summary>
        /// <param name="names">One per seat, in the same order as <see cref="SeatConnectionIds"/>.</param>
        public static void SettleNames(IReadOnlyList<string>? names)
        {
            if (names == null)
            {
                _seatNames = Array.Empty<string>();
                return;
            }

            var kept = names.Count < GameConstants.RaceRunnersMax
                ? names.Count
                : GameConstants.RaceRunnersMax;

            _seatNames = new string[kept];
            for (var i = 0; i < kept; i++)
            {
                _seatNames[i] = names[i] ?? string.Empty;
            }
        }

        /// <summary>
        /// The lobby's names, or empty. Read by <c>RaceDirector.Begin</c>; empty means
        /// "nobody told us", which <c>NameOf</c> already answers with 번.
        /// </summary>
        public static string[] SeatNames
        {
            get { return _seatNames; }
        }

        /// <summary>Forgets the race. Called when the session ends, and by tests.</summary>
        public static void Clear()
        {
            Settled = false;
            LocalSeat = -1;
            Building = default;
            _seatConnectionIds = Array.Empty<int>();
            _seatNames = Array.Empty<string>();
        }
    }
}
