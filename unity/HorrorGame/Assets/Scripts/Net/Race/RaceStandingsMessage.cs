#nullable enable

using HorrorGame.Core;
using HorrorGame.Core.Race;
using Mirror;

namespace HorrorGame.Net
{
    /// <summary>
    /// One seat's standing, as it crosses the wire. §02 · §13.
    /// <para>
    /// <b>Public fields, on purpose.</b> Mirror's weaver generates the serialiser from a
    /// type's fields; properties are not written. <c>RaceLobbyRosterMessage</c> already
    /// carries the same exception for the same reason, so every field here carries its own
    /// line instead of a property.
    /// </para>
    /// <para>
    /// <b>There is no <c>Id</c> field, and that is a guarantee rather than a saving.</b>
    /// <see cref="RaceStandingsMessage.Rows"/> is indexed by seat, so a row's seat is where
    /// it sits in the array. A row that carried its own id could disagree with its position
    /// — two rows claiming seat 4, or a row for a seat outside the field — and every one of
    /// those is a way a client draws a standing the host did not send. The array index
    /// cannot be wrong.
    /// </para>
    /// <para>
    /// <b>Why four bytes and not four ints.</b> §11 caps the field at
    /// <see cref="GameConstants.RaceRunnersMax"/> = 20, <c>RaceState.Storeys</c> is 8,
    /// <see cref="RacerStatus"/> has three values and a place is at most the field size. All
    /// four fit a byte with two orders of magnitude to spare, and the whole table is sent
    /// on every change — see <see cref="RaceStandingsMessage"/> for the arithmetic that
    /// makes that affordable.
    /// </para>
    /// </summary>
    public struct NetRacerRow
    {
        /// <summary><see cref="RacerStatus"/> as a byte. 0 Running, 1 Finished, 2 Eliminated.</summary>
        public byte Status;

        /// <summary>Deepest storey reached. 0 is B1; <c>RaceState.Storeys</c> − 1 is the bottom.</summary>
        public byte Storey;

        /// <summary>§02's 완주 순위 — 1 for the winner, 0 while still running.</summary>
        public byte Place;

        /// <summary>
        /// How many times §06 has sent this runner back to B1. Saturates at 255 rather than
        /// wrapping: a runner caught 256 times has a longer story than the standings can tell,
        /// and a count that wrapped to 0 would read as a clean run.
        /// </summary>
        public byte TimesCaught;

        /// <summary>Seconds from the start to whatever last moved this row.</summary>
        public float ElapsedSeconds;
    }

    /// <summary>
    /// §02's standings, as the host decided them, sent to everybody. §13's 순위 · 도착 판정
    /// — 호스트만.
    /// <para>
    /// <b>This message is the whole of what a client knows about the race.</b> Before it
    /// existed each machine's own <c>MatchDirector</c> reported descents, finishes and
    /// catches into its own <c>RaceState</c>, so two people in one match held two different
    /// scoreboards and neither was authoritative. §13 puts 순위 · 도착 판정 on the host and
    /// names it "경주에서 가장 먼저 조작되는 값"; this is that sentence as bytes.
    /// </para>
    /// <para>
    /// <b>A broadcast message and not a <c>SyncList</c>, for four reasons and one cost.</b>
    /// </para>
    /// <list type="number">
    /// <item><description><b>There is no object to hang a SyncList on.</b> A sync collection
    /// lives on a <c>NetworkBehaviour</c>, which needs a <c>NetworkIdentity</c> on a spawned
    /// object. This project has no prefab asset with an identity on it —
    /// <see cref="NetRunner"/> spends a page explaining why the runner has to be built from
    /// code and registered by asset id — so a standings object means a second spawnable, a
    /// second spawn handler and a construction path only the host ever takes.</description></item>
    /// <item><description><b>Interest management would cull it, and it would cull it from the
    /// leaders first.</b> <see cref="HorrorInterestManagement.OnRebuildObservers"/> sends a
    /// spawned identity only to connections whose own body is within
    /// <see cref="NetInterestScope.PerceptionRange"/> of it. A standings object standing
    /// anywhere in a 57.5 m × 8-storey column is out of range of most of the field, and the
    /// runner furthest from it is the one who is winning. A message has no observer set:
    /// <c>NetworkServer.SendToReady</c> reaches every ready connection wherever it
    /// is.</description></item>
    /// <item><description><b>The order comes off the wire.</b> A SyncList replicates the
    /// rows; the client would still have to sort them into
    /// <c>RaceState.Standings()</c>'s order to draw a board, which is a client computing a
    /// §02 fact. <see cref="LiveOrder"/> is the host's own ordering, so the client renders
    /// a list rather than deriving one — and the comparator cannot drift out of step
    /// between the two ends, because there is only one of it.</description></item>
    /// <item><description><b>The direction of every byte is visible in one file.</b>
    /// <c>RaceLobby</c> chose messages over a spawned <c>NetLobby</c> on exactly this
    /// argument, and it is worth more here than it was there.</description></item>
    /// </list>
    /// <para>
    /// <b>The cost, stated.</b> A SyncList sends a delta per operation; this sends the whole
    /// table. At §11's ceiling that is 20 × 8 bytes of rows + 20 bytes of order + the clock,
    /// the winner and the flag ≈ <b>190 bytes a frame</b>. Frames go out on every accepted
    /// descent, finish, catch and withdrawal — about 20 × 7 + 20 + a handful ≈ 180 in a full
    /// match — plus a 1 Hz heartbeat so the HUD's clock is the host's clock and not a local
    /// one (<see cref="NetRace.HeartbeatSeconds"/>). At twenty observers the heartbeat is
    /// ≈ 3.8 kB/s off the host, against §05's five rows at
    /// <see cref="GameConstants.NetworkSendRate"/> = 30 Hz × 20 players, which is an order of
    /// magnitude more. §13's budget argument survives it.
    /// </para>
    /// </summary>
    public struct RaceStandingsMessage : NetworkMessage
    {
        /// <summary>
        /// Counts up from 1 on the host. A client keeps the highest it has seen and drops
        /// anything older.
        /// <para>
        /// Not paranoia about ordering: Mirror's default channel is reliable and ordered, but
        /// a frame sent to one connection on join (<see cref="NetRace.SendTo"/>) races the
        /// broadcast that a descent triggers in the same tick, and the two are not one
        /// stream. Dropping the older one is cheaper than reasoning about which arrives
        /// first, and it is what makes the join-time frame safe to send at all.
        /// </para>
        /// </summary>
        public uint Frame;

        /// <summary>
        /// Seconds since the twenty left the rim of B1, on the host's clock.
        /// <para>
        /// Sent rather than counted locally because it is the number <c>RaceHud</c> prints,
        /// and a client counting its own would drift from everybody else's screen over a
        /// thirty-two minute match for no reason at all.
        /// </para>
        /// </summary>
        public float ElapsedSeconds;

        /// <summary>§02 승리 — the winner's seat, or −1 while nobody has arrived.</summary>
        public int WinnerId;

        /// <summary>True once nobody is still running. <c>RaceState.Over</c>.</summary>
        public bool Over;

        /// <summary>
        /// One row per seat, indexed by seat. Length is the field §11 sized, so it is also
        /// how a client learns how many started.
        /// </summary>
        public NetRacerRow[] Rows;

        /// <summary>
        /// The seats that are still running, in the host's own <c>RaceState.Standings()</c>
        /// order — deepest first, then by how long they have been on that storey.
        /// <para>
        /// This is the field that makes the client a renderer. §02 says the HUD reads and
        /// never decides; handing it the rows alone would still leave it deciding who is
        /// second.
        /// </para>
        /// </summary>
        public byte[] LiveOrder;
    }
}
