#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI
{
    /// <summary>
    /// Where a lobby can be. Four states, because each one shows a different set of
    /// controls and a player who cannot tell them apart presses 참가 twice.
    /// </summary>
    public enum LobbyStage
    {
        /// <summary>Nothing running. 호스트 and 참가 are the only live controls.</summary>
        Offline = 0,

        /// <summary>A join is in flight. Everything is disabled — a second 참가 would start a second client.</summary>
        Connecting = 1,

        /// <summary>Connected, roster filling, waiting for the host to start.</summary>
        Waiting = 2,

        /// <summary>The host has started. The seed is fixed and the descent is loading.</summary>
        Starting = 3,
    }

    /// <summary>
    /// One line of the lobby list.
    /// <para>
    /// A name, a flag for the host and a flag for you, and nothing else. §04 deleted
    /// the roles and §11 makes all twenty runners identical, so there is nothing left
    /// to choose in a lobby and therefore nothing else worth replicating: the whole
    /// screen exists to answer "who is here yet".
    /// </para>
    /// </summary>
    public readonly struct LobbyRunner
    {
        /// <summary>Builds a line.</summary>
        /// <param name="name">What to show. Never empty — see <c>IUserIdentity.NameOf</c>.</param>
        /// <param name="isHost">True for the machine that owns the seed and the start button.</param>
        /// <param name="isLocal">True for the row that is you.</param>
        public LobbyRunner(string name, bool isHost, bool isLocal)
        {
            Name = name;
            IsHost = isHost;
            IsLocal = isLocal;
        }

        /// <summary>Display name, as the host resolved it.</summary>
        public string Name { get; }

        /// <summary>Whether this row is the host. §13 — the one machine that may press 시작.</summary>
        public bool IsHost { get; }

        /// <summary>Whether this row is the local player.</summary>
        public bool IsLocal { get; }
    }

    /// <summary>
    /// What a player in the lobby can ask for. §11 · §13.
    /// <para>
    /// Every one of these is a request rather than an action: §13 makes the host
    /// authoritative and the seed is the sharpest case of it in the game. (The other
    /// two request interfaces this layer had — <c>IShopRequests</c> and
    /// <c>IRoleClaimRequests</c> — went with the shop and the role picker in
    /// DESCENT-PIVOT §7 step 7; this is the last one, because starting a race is the
    /// last thing left that a client may ask for and must not do.)
    /// A client that picked its own seed
    /// would build a different building from everybody else and would be racing down
    /// a maze nobody else can see. So the screen asks, and redraws whatever it is
    /// told; a refusal is a screen that comes back unchanged.
    /// </para>
    /// </summary>
    public interface ILobbyRequests
    {
        /// <summary>Starts a session on this machine. This player becomes §13's authority.</summary>
        void RequestHost();

        /// <summary>Joins a session. <paramref name="address"/> is whatever the transport understands.</summary>
        /// <param name="address">Host address. Empty means the transport's own default.</param>
        void RequestJoin(string address);

        /// <summary>Asks the host to start. Refused below <see cref="GameConstants.RaceRunnersMin"/> runners.</summary>
        void RequestStart();

        /// <summary>Leaves the session. On the host that ends it for everybody — §13 does not migrate.</summary>
        void RequestLeave();

        /// <summary>Closes the lobby without connecting, back to whatever opened it.</summary>
        void RequestBack();
    }

    /// <summary>
    /// The seam the shell's 시작 goes through on its way to the lobby.
    /// <para>
    /// <b>Why a seam and not a call.</b> <c>GameShell</c> lives in this assembly, which
    /// deliberately does not reference Mirror (see <see cref="UiScreen"/>), and the
    /// thing that actually hosts and joins has to. The dependency therefore only runs
    /// one way — the gameplay layer installs itself here at start-up and the shell asks
    /// a question it does not know the answer to. A build with no networking in it
    /// leaves <see cref="Intercept"/> null and 시작 keeps its old behaviour, which is
    /// also exactly what an editor solo playtest wants.
    /// </para>
    /// </summary>
    public static class LobbyEntry
    {
        private static bool _suppressed;

        /// <summary>
        /// Installed by the gameplay layer. Receives the callback to run once the race
        /// is agreed, and returns true when it has taken the flow over.
        /// </summary>
        public static System.Func<System.Action, bool>? Intercept { get; set; }

        /// <summary>
        /// Opens the lobby, if anything is listening.
        /// </summary>
        /// <param name="onBegin">What to run once the host has started and the seed is settled.</param>
        /// <returns>True when the lobby took over and the caller should stop.</returns>
        public static bool TryOpen(System.Action onBegin)
        {
            // The latch is here rather than in the shell because the lobby's own
            // "go down now" callback is the shell's 시작 — without it, agreeing to start
            // would reopen the lobby the agreement just came out of.
            if (_suppressed)
            {
                _suppressed = false;
                return false;
            }

            return Intercept != null && Intercept(onBegin);
        }

        /// <summary>Lets the next <see cref="TryOpen"/> through untouched. Called by the lobby as it hands the flow back.</summary>
        public static void PassNextThrough()
        {
            _suppressed = true;
        }

        /// <summary>Drops the installed hook and clears the latch. Tests need this — both outlive a PlayMode run.</summary>
        public static void ResetForTests()
        {
            Intercept = null;
            _suppressed = false;
        }
    }

    /// <summary>
    /// §11's lobby: host or join, watch the field fill up to twenty, and go down
    /// together on one seed.
    /// <para>
    /// <b>Why there is a lobby at all.</b> The build used to drop straight into the
    /// match scene from 시작, which is playable exactly once — alone. A race needs
    /// everybody in the same building before anybody starts running, and §13 needs
    /// one machine to be the authority over the map, so there has to be a moment
    /// between the menu and the descent where that is settled. This is it.
    /// </para>
    /// <para>
    /// <b>The seed is shown, and it is not editable.</b> §13 gives the host authority
    /// and <c>DescentMap.Build</c> is deterministic, so the seed *is* the building:
    /// twenty players holding the same number are in the same maze and twenty players
    /// holding different numbers are in twenty different games that happen to share a
    /// scoreboard. It is displayed because a race that can be replayed is a race that
    /// can be argued about, and it is read-only everywhere because a client that could
    /// choose it could choose an easier building.
    /// </para>
    /// <para>
    /// <b>Twenty rows, always drawn.</b> The empty ones stay visible rather than
    /// appearing as people arrive — §11 makes the field size the thing that changes
    /// the game ("11~20 · 설계 기준"), so how many seats are still empty is the single
    /// most useful fact on this screen and it should not have to be counted.
    /// </para>
    /// <para>
    /// This class knows nothing about Mirror; the UI assembly deliberately does not
    /// reference it (see <see cref="UiScreen"/>). It is handed a list and an
    /// <see cref="ILobbyRequests"/>, which is what lets it be opened by a test with no
    /// socket anywhere in the process.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyScreen : UiScreen
    {
        /// <summary>Panel width in reference pixels. Two columns of ten rows, side by side.</summary>
        private const float PanelWidth = 1300f;

        /// <summary>Panel height. Ten rows plus the header block, the connect bar and the footer.</summary>
        private const float PanelHeight = 840f;

        /// <summary>Inset from the panel edge to its content.</summary>
        private const float Padding = 34f;

        /// <summary>
        /// Rows per column. Twenty runners in two columns of ten rather than one column
        /// of twenty: a 20-row list at a legible row height is taller than the panel,
        /// and a lobby that scrolls hides the fact §11 cares about most — how full it is.
        /// </summary>
        private const int RowsPerColumn = 10;

        /// <summary>Height of one runner row. Shorter than <see cref="UiStyle.RowHeight"/> — a row here is one line, not three.</summary>
        private const float RunnerRowHeight = 44f;

        /// <summary>Gap between runner rows.</summary>
        private const float RunnerRowGap = 4f;

        /// <summary>Distance from the panel's top edge to the first runner row.</summary>
        private const float RosterTop = 224f;

        private ILobbyRequests? _requests;

        private Text? _count;
        private Text? _seed;
        private Text? _note;

        private RectTransform? _connectBar;
        private InputField? _address;
        private Button? _host;
        private Button? _join;

        private Button? _start;
        private Text? _startLabel;
        private Button? _leave;
        private Text? _leaveLabel;

        private readonly Image[] _rowBacks = new Image[GameConstants.RaceRunnersMax];
        private readonly Text[] _rowNames = new Text[GameConstants.RaceRunnersMax];
        private readonly Text[] _rowTags = new Text[GameConstants.RaceRunnersMax];

        /// <summary>
        /// Above <see cref="UiStyle.SortOrderPanel"/> and below
        /// <see cref="UiStyle.SortOrderEnd"/>.
        /// <para>
        /// The lobby is opened <em>over</em> the main menu, which is still standing
        /// behind it on the panel layer — the shell hides the menu when the match scene
        /// starts loading, which is minutes later. Two canvases on the same sorting order
        /// resolve by hierarchy, and the one that would win is whichever happened to be
        /// created first. Nothing outranks an ending, so this sits between the two.
        /// </para>
        /// </summary>
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderPanel + 20; }
        }

        /// <inheritdoc />
        protected override bool Interactive
        {
            get { return true; }
        }

        /// <summary>
        /// Shows the lobby and wires its buttons.
        /// </summary>
        /// <param name="requests">Who to ask. §13's host answers; a client is told.</param>
        /// <param name="address">Pre-filled join address — normally the transport's own default.</param>
        public void Open(ILobbyRequests requests, string address)
        {
            _requests = requests;
            SetVisible(true);

            if (_address != null && !string.IsNullOrEmpty(address))
            {
                _address.text = address;
            }

            Refresh(LobbyStage.Offline, System.Array.Empty<LobbyRunner>(), localIsHost: false, seed: 0, note: string.Empty);
        }

        /// <summary>Hides the lobby without dropping its wiring.</summary>
        public void Close()
        {
            SetVisible(false);
        }

        /// <summary>The join address the player typed, or whatever it was opened with.</summary>
        public string Address
        {
            get { return _address != null ? _address.text : string.Empty; }
        }

        /// <summary>
        /// Redraws the whole screen from the caller's state.
        /// <para>
        /// One method rather than a property per widget, because every one of these
        /// values changes at the same moment — a runner arriving changes the list, the
        /// count and whether 시작 is pressable — and a screen updated in pieces is a
        /// screen that can show four out of twenty beside a live start button.
        /// </para>
        /// </summary>
        /// <param name="stage">Where the session is.</param>
        /// <param name="runners">Everyone connected, host first. Longer than the cap is clamped.</param>
        /// <param name="localIsHost">Whether this machine holds §13's authority.</param>
        /// <param name="seed">The host's seed, or 0 before there is one.</param>
        /// <param name="note">One line explaining the current state, or why 시작 is refused.</param>
        public void Refresh(LobbyStage stage, IReadOnlyList<LobbyRunner> runners, bool localIsHost, int seed, string note)
        {
            EnsureBuilt();

            var filled = Mathf.Min(runners.Count, GameConstants.RaceRunnersMax);

            if (_count != null)
            {
                _count.text = filled + " / " + GameConstants.RaceRunnersMax;

                // Amber below §11's floor, calm once the race is legal. The count is the
                // one number on this screen that decides whether anything can happen.
                _count.color = filled >= GameConstants.RaceRunnersMin ? UiStyle.Calm : UiStyle.Trade;
            }

            if (_seed != null)
            {
                _seed.text = seed != 0 ? "씨앗 " + seed.ToString(System.Globalization.CultureInfo.InvariantCulture) : "씨앗 " + UiStrings.Unknown;
            }

            if (_note != null)
            {
                _note.text = note ?? string.Empty;
            }

            for (var i = 0; i < GameConstants.RaceRunnersMax; i++)
            {
                var occupied = i < filled;
                var back = _rowBacks[i];
                var name = _rowNames[i];
                var tag = _rowTags[i];

                if (back == null || name == null || tag == null)
                {
                    continue;
                }

                if (!occupied)
                {
                    back.color = UiStyle.RowDisabled;
                    name.text = UiStrings.Unknown;
                    name.color = UiStyle.InkFaint;
                    tag.text = string.Empty;
                    continue;
                }

                var runner = runners[i];
                back.color = runner.IsLocal ? UiStyle.RowAffordable : UiStyle.Row;
                name.text = runner.Name;
                name.color = UiStyle.InkStrong;

                // 호스트 wins over 나 when they are the same person: which machine holds
                // §13's authority is the fact that changes what the screen can do.
                tag.text = runner.IsHost ? "호스트" : runner.IsLocal ? "나" : string.Empty;
                tag.color = runner.IsHost ? UiStyle.Trade : UiStyle.InkQuiet;
            }

            var offline = stage == LobbyStage.Offline;

            _connectBar?.gameObject.SetActive(offline);

            if (_host != null)
            {
                _host.interactable = offline;
            }

            if (_join != null)
            {
                _join.interactable = offline;
            }

            if (_address != null)
            {
                _address.interactable = offline;
            }

            if (_start != null)
            {
                // Only the host has a start button at all — §13 keeps the decision on the
                // machine that owns the seed — and it stays dark below §11's two.
                _start.gameObject.SetActive(localIsHost && !offline);
                _start.interactable = stage == LobbyStage.Waiting && filled >= GameConstants.RaceRunnersMin;
            }

            if (_startLabel != null)
            {
                _startLabel.text = stage == LobbyStage.Starting ? "내려간다" : "출발";
            }

            if (_leave != null)
            {
                _leave.interactable = stage != LobbyStage.Starting;
            }

            if (_leaveLabel != null)
            {
                _leaveLabel.text = offline ? "뒤로" : "나가기";
            }
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            var scrim = UiFactory.CreateImage("Scrim", root, new Color(0.01f, 0.01f, 0.015f, 0.72f));
            UiFactory.Stretch((RectTransform)scrim.transform);

            var panel = UiFactory.CreateImage("Panel", root, UiStyle.Panel);
            var panelRect = UiFactory.Place(
                (RectTransform)panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidth, PanelHeight));

            BuildHeader(panelRect);
            BuildConnectBar(panelRect);
            BuildRoster(panelRect);
            BuildFooter(panelRect);

            // Complete the moment it is built, so a review render — which never calls
            // Open — photographs twenty empty rows rather than a blank rectangle.
            Refresh(LobbyStage.Offline, System.Array.Empty<LobbyRunner>(), localIsHost: false, seed: 0, note: string.Empty);
        }

        private void BuildHeader(RectTransform panel)
        {
            var inner = PanelWidth - (Padding * 2f);

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Title", panel, Font, "로비", UiStyle.TextSizeTitle, UiStyle.InkStrong, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Padding, -Padding), new Vector2(560f, 44f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "Subtitle", panel, Font,
                    "§11 · " + GameConstants.RaceRunnersMin + "~" + GameConstants.RaceRunnersMax
                    + "인이 B1 외곽 고리에서 동시에 출발한다. 전원 같은 몸, 같은 속도.",
                    UiControls.NoteSize, UiStyle.InkQuiet, TextAnchor.UpperLeft).transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Padding, -Padding - 48f), new Vector2(900f, 26f));

            _count = UiFactory.CreateText("Count", panel, Font, "0 / " + GameConstants.RaceRunnersMax, 40, UiStyle.Trade, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_count.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Padding, -Padding), new Vector2(320f, 46f));

            _seed = UiFactory.CreateText("Seed", panel, Font, "씨앗 " + UiStrings.Unknown, UiStyle.TextSizeSmall, UiStyle.InkQuiet, TextAnchor.UpperRight);
            UiFactory.Place((RectTransform)_seed.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Padding, -Padding - 50f), new Vector2(320f, 22f));

            var rule = UiFactory.CreateImage("Rule", panel, UiStyle.InkFaint);
            UiFactory.Place((RectTransform)rule.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Padding, -Padding - 84f), new Vector2(inner, 1f));
        }

        /// <summary>
        /// 주소 · 호스트 · 참가.
        /// <para>
        /// The address is a text field and not a stepper because §14 step 2 is "같은 PC
        /// 2인스턴스" and step 3 is two machines on one desk: the value that has to be
        /// typed is a LAN address nobody can enumerate in advance. It is pre-filled with
        /// whatever the transport already chose, so the common case is pressing 참가
        /// without touching it.
        /// </para>
        /// </summary>
        private void BuildConnectBar(RectTransform panel)
        {
            _connectBar = UiFactory.CreateRect("ConnectBar", panel);
            UiFactory.Place(_connectBar,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Padding, -Padding - 100f),
                new Vector2(PanelWidth - (Padding * 2f), 56f));

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "AddressLabel", _connectBar, Font, "주소", UiStyle.TextSize, UiStyle.InkQuiet, TextAnchor.MiddleLeft).transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(70f, 26f));

            _address = CreateAddressField(_connectBar, 78f, 460f);

            _host = UiFactory.CreateButton("Host", _connectBar, UiStyle.Row, delegate { _requests?.RequestHost(); });
            UiFactory.Place((RectTransform)_host.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(566f, 0f), new Vector2(180f, 44f));
            CentreLabel(_host, "호스트", UiStyle.TextSize + 2);

            _join = UiFactory.CreateButton("Join", _connectBar, UiStyle.Row,
                delegate { _requests?.RequestJoin(_address != null ? _address.text : string.Empty); });
            UiFactory.Place((RectTransform)_join.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(758f, 0f), new Vector2(180f, 44f));
            CentreLabel(_join, "참가", UiStyle.TextSize + 2);

            UiFactory.Place(
                (RectTransform)UiFactory.CreateText(
                    "HostNote", _connectBar, Font,
                    "§13 · 호스트가 씨앗과 순위를 정한다. 호스트가 나가면 판은 끝난다 — 이관하지 않는다.",
                    UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.MiddleLeft).transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(958f, 0f), new Vector2(268f, 40f));
        }

        private void BuildRoster(RectTransform panel)
        {
            var inner = PanelWidth - (Padding * 2f);
            var columnWidth = (inner - UiStyle.ColumnGap) * 0.5f;

            for (var i = 0; i < GameConstants.RaceRunnersMax; i++)
            {
                var column = i / RowsPerColumn;
                var slot = i % RowsPerColumn;

                var x = Padding + (column * (columnWidth + UiStyle.ColumnGap));
                var y = -RosterTop - (slot * (RunnerRowHeight + RunnerRowGap));

                var back = UiFactory.CreateImage("Runner_" + i, panel, UiStyle.RowDisabled);
                UiFactory.Place((RectTransform)back.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(columnWidth, RunnerRowHeight));
                _rowBacks[i] = back;

                // 01..20 rather than 0..19: the number beside a name is read as a position
                // in a queue, and queues do not start at zero.
                var ordinal = UiFactory.CreateText(
                    "Ordinal", back.transform, Font, (i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                    UiStyle.TextSizeSmall, UiStyle.InkFaint, TextAnchor.MiddleLeft);
                UiFactory.Place((RectTransform)ordinal.transform,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(40f, 22f));

                var name = UiFactory.CreateText(
                    "Name", back.transform, Font, UiStrings.Unknown, UiStyle.TextSize, UiStyle.InkFaint, TextAnchor.MiddleLeft);
                UiFactory.Place((RectTransform)name.transform,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(columnWidth - 190f, 26f));
                _rowNames[i] = name;

                var tag = UiFactory.CreateText(
                    "Tag", back.transform, Font, string.Empty, UiStyle.TextSizeSmall, UiStyle.InkQuiet, TextAnchor.MiddleRight);
                UiFactory.Place((RectTransform)tag.transform,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(96f, 22f));
                _rowTags[i] = tag;
            }
        }

        private void BuildFooter(RectTransform panel)
        {
            var inner = PanelWidth - (Padding * 2f);

            _note = UiFactory.CreateText("Note", panel, Font, string.Empty, UiControls.NoteSize, UiStyle.InkQuiet, TextAnchor.LowerLeft);
            UiFactory.Place((RectTransform)_note.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Padding, Padding + 64f), new Vector2(inner, 24f));

            _leave = UiFactory.CreateButton("Leave", panel, UiStyle.Row, delegate { OnLeave(); });
            UiFactory.Place((RectTransform)_leave.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Padding, Padding), new Vector2(220f, 56f));
            _leaveLabel = CentreLabel(_leave, "뒤로", UiStyle.TextSize + 4);

            _start = UiFactory.CreateButton("Start", panel, UiStyle.RowAffordable, delegate { _requests?.RequestStart(); });
            UiFactory.Place((RectTransform)_start.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Padding, Padding), new Vector2(300f, 56f));
            _startLabel = CentreLabel(_start, "출발", UiStyle.TextSize + 4);
        }

        private void OnLeave()
        {
            if (_requests == null)
            {
                return;
            }

            // One button, two meanings, decided by the label the last Refresh wrote. A
            // separate 뒤로 and 나가기 would put a dead control on screen in both states.
            if (_leaveLabel != null && _leaveLabel.text == "뒤로")
            {
                _requests.RequestBack();
                return;
            }

            _requests.RequestLeave();
        }

        private Text CentreLabel(Button button, string label, int size)
        {
            var text = UiFactory.CreateText("Label", button.transform, Font, label, size, UiStyle.Ink, TextAnchor.MiddleCenter);
            var rect = (RectTransform)button.transform;
            UiFactory.Place((RectTransform)text.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(rect.sizeDelta.x - 20f, rect.sizeDelta.y - 10f));
            return text;
        }

        /// <summary>
        /// A single-line text field, assembled the way <c>DefaultControls</c> does it.
        /// <para>
        /// The legacy <see cref="InputField"/> rather than TextMeshPro's, because this
        /// layer draws every other glyph with <see cref="UiFactory.ResolveFont"/>'s
        /// built-in face and dragging in a second text stack for one address box would
        /// mean two font pipelines to keep Korean working in. It types because the
        /// project's Active Input Handling is <em>Both</em> — the field reads
        /// <c>Input.inputString</c> through <c>BaseInput</c>, which the legacy backend
        /// supplies even though the game itself is on the Input System.
        /// </para>
        /// </summary>
        private InputField CreateAddressField(RectTransform parent, float x, float width)
        {
            var background = UiFactory.CreateImage("Address", parent, UiStyle.BarBed, raycastTarget: true);
            var rect = UiFactory.Place((RectTransform)background.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(width, 44f));

            var text = UiFactory.CreateText("Text", rect, Font, string.Empty, UiStyle.TextSize, UiStyle.InkStrong, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            var textRect = UiFactory.Stretch((RectTransform)text.transform);
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);

            var placeholder = UiFactory.CreateText("Placeholder", rect, Font, "localhost", UiStyle.TextSize, UiStyle.InkFaint, TextAnchor.MiddleLeft);
            placeholder.supportRichText = false;
            var placeholderRect = UiFactory.Stretch((RectTransform)placeholder.transform);
            placeholderRect.offsetMin = new Vector2(12f, 4f);
            placeholderRect.offsetMax = new Vector2(-12f, -4f);

            var field = background.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.targetGraphic = background;
            field.characterLimit = 128;
            return field;
        }
    }
}
