#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HorrorGame.Core;
using HorrorGame.Gameplay.Match;
using HorrorGame.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HorrorGame.Gameplay.Guidance
{
    /// <summary>
    /// The four things a first-time tester needs on screen: what to do next, which keys
    /// do it, §14's five questions, and what happened. §01 · §05 · §14.
    /// <para>
    /// <b>Why a fifth screen exists at all.</b> <c>MatchHud</c> is emphatic that it adds
    /// no widgets — the shipped screens already know what a horror game is allowed to
    /// show, and a second HUD would disagree with the first. Nothing here contradicts
    /// that: this is a <em>playtest</em> screen, built by the playtest scene builder,
    /// never installed by the bootstrap, and it draws only things the shipping HUD
    /// deliberately refuses to draw because a player who has read §01 does not need them.
    /// The person §14 is written for has not read §01, and §14's whole argument is
    /// "직접 만져봐야 나온다".
    /// </para>
    /// <para>
    /// <b>What it must never draw.</b> §03's constraint is that a clue exists only in a
    /// player's memory; §07's is that the hour is something you walk outside for. So this
    /// screen shows a clue <em>count</em> and never a mark, and it shows the monster's
    /// current speed — §14 Q2's margin is meaningless without it — but never the 시각 and
    /// never the distance to the monster, because §04's Listener finding it by ear is
    /// §14's fifth question and a number on screen would answer it for free.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaytestGuidanceScreen : UiScreen
    {
        /// <summary>Above §02's end screen, because the run summary is drawn on top of it.</summary>
        private const int SortOrderGuidance = UiStyle.SortOrderEnd + 100;

        /// <summary>
        /// Seconds the controls card stays up unasked at the start of a match. Long enough
        /// to read six rows, short enough that it is gone before the first corridor. A
        /// presentation value, not a §-anything — see <c>UiStyle</c>'s own note.
        /// </summary>
        private const float ControlsCardSeconds = 14f;

        // Ink. Brighter than UiStyle's, and deliberately: the shipping HUD is dim because
        // "the HUD is not the game", whereas this is instruction text that has to survive
        // being read over a lit concrete floor by somebody who does not yet know it is there.
        private static readonly Color Backing = new Color(0.02f, 0.02f, 0.03f, 0.72f);
        private static readonly Color PanelBacking = new Color(0.02f, 0.02f, 0.03f, 0.96f);
        private static readonly Color Bright = new Color(0.95f, 0.95f, 0.91f, 0.98f);
        private static readonly Color Dim = new Color(0.82f, 0.82f, 0.78f, 0.92f);

        /// <summary>
        /// Where the objective line sits normally: above §04's stamina bar and §09's
        /// ghost readout, below §03's clue overlay, clear of the crosshair.
        /// </summary>
        private static readonly Vector2 ObjectiveHome = new Vector2(0f, 132f);

        /// <summary>
        /// Where it goes while §08's shop is up. The shop's rows reach down to about 90
        /// reference pixels off the bottom, and those rows are the decision the player is
        /// making — so the line drops underneath them and covers §04's stamina bar
        /// instead, which is a readout about running away from something, at the van, on
        /// the surface, in §01's 안전 지대.
        /// </summary>
        private static readonly Vector2 ObjectiveUnderShop = new Vector2(0f, 2f);

        private MatchDirector? _director;
        private MatchGuidance? _guidance;

        private Text? _objective;
        private Text? _load;
        private RectTransform? _objectiveGroup;

        private RectTransform? _controlsGroup;
        private RectTransform? _questionsGroup;
        private RectTransform? _summaryGroup;

        private Text? _questionNumbers;
        private Text[]? _summaryValues;

        private float _controlsHideAt;
        private bool _controlsPinned;
        private bool _questionsShown;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return SortOrderGuidance; }
        }

        /// <summary>False. Every click belongs to §08's shop or to the world underneath.</summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>The reader this screen draws. Null before <c>Start</c>.</summary>
        public MatchGuidance? Guidance
        {
            get { return _guidance; }
        }

        /// <summary>Whether the controls card is up.</summary>
        public bool ControlsVisible
        {
            get { return _controlsGroup != null && _controlsGroup.gameObject.activeSelf; }
        }

        /// <summary>Whether §14's five questions are up.</summary>
        public bool QuestionsVisible
        {
            get { return _questionsShown; }
        }

        /// <summary>Points the screen at a match. Called by the scene builder; otherwise found in <c>Start</c>.</summary>
        public void Bind(MatchDirector director)
        {
            _director = director;
            _guidance = new MatchGuidance(director);
        }

        private void Start()
        {
            if (_director == null)
            {
                _director = FindFirstObjectByType<MatchDirector>();
            }

            if (_director == null)
            {
                Debug.LogWarning(
                    "[Guidance] No MatchDirector in the scene, so there is no §01 loop to describe. "
                    + "Build the playtest scene with HorrorGame ▸ Play ▸ ▶ START PLAYTEST.", this);
                enabled = false;
                return;
            }

            if (_guidance == null)
            {
                _guidance = new MatchGuidance(_director);
            }

            SetVisible(true);
            _controlsHideAt = Time.unscaledTime + ControlsCardSeconds;
        }

        private void Update()
        {
            ReadKeys();
            Redraw();
        }

        /// <summary>
        /// Re-reads the match and repaints, without touching the keyboard.
        /// <para>
        /// Public so the overlay can be photographed. §14's "is this legible over a dark
        /// corridor" is a question about pixels, and the only honest way to answer it is
        /// to render the real screen over the real map — which means something outside
        /// Play mode has to be able to make one frame happen.
        /// </para>
        /// </summary>
        public void Redraw()
        {
            var guidance = _guidance;
            if (guidance == null)
            {
                return;
            }

            guidance.Observe();
            DrawObjective(guidance);
            DrawControls();
            DrawQuestions(guidance);
            DrawSummary(guidance);
        }

        /// <summary>Forces the two optional panels open or shut. The key handler's other door.</summary>
        public void SetPanels(bool controls, bool questions)
        {
            _controlsPinned = controls;
            _controlsHideAt = 0f;
            _questionsShown = questions;
        }

        // ------------------------------------------------------------------ keys

        private void ReadKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                _controlsPinned = !ControlsVisible;
                _controlsHideAt = 0f;
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                _questionsShown = !_questionsShown;
            }
        }

        // ------------------------------------------------------------------ drawing

        private void DrawObjective(MatchGuidance guidance)
        {
            if (_objectiveGroup == null || _objective == null || _load == null)
            {
                return;
            }

            var line = guidance.Line;
            _objectiveGroup.gameObject.SetActive(line.Length > 0);

            var home = _director != null && _director.ShopOpen ? ObjectiveUnderShop : ObjectiveHome;
            if (_objectiveGroup.anchoredPosition != home)
            {
                _objectiveGroup.anchoredPosition = home;
            }

            Set(_objective, line);
            Set(_load, guidance.Note);
            _load.color = guidance.NoteIsPenalty ? UiStyle.Trade : Dim;
        }

        private void DrawControls()
        {
            if (_controlsGroup == null)
            {
                return;
            }

            var show = _controlsPinned || Time.unscaledTime < _controlsHideAt;
            if (_controlsGroup.gameObject.activeSelf != show)
            {
                _controlsGroup.gameObject.SetActive(show);
            }
        }

        private void DrawQuestions(MatchGuidance guidance)
        {
            if (_questionsGroup == null || _questionNumbers == null)
            {
                return;
            }

            if (_questionsGroup.gameObject.activeSelf != _questionsShown)
            {
                _questionsGroup.gameObject.SetActive(_questionsShown);
            }

            if (!_questionsShown)
            {
                return;
            }

            Set(_questionNumbers, LiveFeelNumbers(guidance));
        }

        /// <summary>
        /// §14 Q2's numbers, taken off <c>PlayerMotor</c> — the same members the §05 feel
        /// harness reads, because §05's table, the load multiplier and the margin all
        /// resolve inside <c>SpeedResolver</c> and <c>ChaseMargin</c> and a second
        /// arithmetic here could disagree with the one the player is actually moving at.
        /// </summary>
        private string LiveFeelNumbers(MatchGuidance guidance)
        {
            var motor = guidance.Motor;
            if (motor == null || _director == null)
            {
                return "   플레이어 리그가 없어 §05 수치를 읽을 수 없습니다";
            }

            var monsterSpeed = _director.Clock.Tier.MonsterSpeed;
            var margin = motor.MarginVersusMonster(monsterSpeed);
            var safePeek = motor.SafePeekAngleDegrees(monsterSpeed, motor.Stamina.SprintAvailable);

            var text = new StringBuilder();
            text.Append("   속도 ").Append(F(motor.ResolvedSpeed)).Append(" m/s")
                .Append("   방향 ×").Append(F(motor.DirectionalMultiplier))
                .Append(" (").Append(F(motor.HeadingOffsetDegrees)).Append("도)")
                .Append('\n');
            text.Append("   괴물 ").Append(F(monsterSpeed)).Append(" m/s")
                .Append("   여유 ").Append(Signed(margin.MetresPerSecond)).Append(" m/s")
                .Append(margin.IsLosingGround ? "  ← 좁혀지는 중" : "  ← 벌어지는 중")
                .Append('\n');
            text.Append("   안전 곁눈질 ").Append(F(safePeek)).Append("도")
                .Append("  (§05 45도 곁눈질 ×").Append(F(GameConstants.MulDiagonal))
                .Append(", 후진 ×").Append(F(GameConstants.MulBackward)).Append(")");

            return text.ToString();
        }

        /// <summary>The eight things §14 is answered by. Labels are built once; only the values are written.</summary>
        private static readonly string[] SummaryLabels =
        {
            "결과",
            "경과 시간",
            "왕복",
            "읽은 표식",
            "판매한 전리품",
            "크레딧",
            "추격",
            "사망",
        };

        private void DrawSummary(MatchGuidance guidance)
        {
            var values = _summaryValues;
            if (_summaryGroup == null || values == null)
            {
                return;
            }

            var resolution = guidance.Resolution;
            var show = resolution.IsFinal;

            if (_summaryGroup.gameObject.activeSelf != show)
            {
                _summaryGroup.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            Set(values[0], UiStrings.Outcome(resolution.Outcome));
            Set(values[1], Clock(guidance.ElapsedSeconds));
            Set(values[2], guidance.RoundTrips.ToString(CultureInfo.InvariantCulture) + "회");
            Set(values[3], guidance.CluesRead.ToString(CultureInfo.InvariantCulture)
                + "/" + guidance.CluesRequired.ToString(CultureInfo.InvariantCulture)
                + "  (맵에 " + guidance.CluesOnMap.ToString(CultureInfo.InvariantCulture) + "개)");
            Set(values[4], guidance.LootSold.ToString(CultureInfo.InvariantCulture) + "개");
            Set(values[5], guidance.Credits.ToString(CultureInfo.InvariantCulture));
            Set(values[6], guidance.Chases.ToString(CultureInfo.InvariantCulture) + "회  ·  따돌림 "
                + guidance.ChasesBroken.ToString(CultureInfo.InvariantCulture) + "회");
            Set(values[7], guidance.Deaths.ToString(CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ build

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            BuildObjectiveLine(root);
            BuildControlsCard(root);
            BuildQuestions(root);
            BuildSummary(root);
        }

        /// <summary>
        /// One line, bottom centre, above §04's stamina bar and clear of §12's crosshair.
        /// §01's loop has nine steps and a tester can only be told the next one.
        /// </summary>
        private void BuildObjectiveLine(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Objective", root),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                ObjectiveHome,
                new Vector2(1280f, 86f));

            UiFactory.Stretch((RectTransform)UiFactory.CreateImage("Backing", group, Backing).transform);

            _objective = UiFactory.CreateText("Line", group, Font, string.Empty, 26, Bright, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)_objective.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(1240f, 36f));

            _load = UiFactory.CreateText("Load", group, Font, string.Empty, 19, Dim, TextAnchor.MiddleCenter);
            UiFactory.Place((RectTransform)_load.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(1240f, 28f));

            _objectiveGroup = group;
        }

        private void BuildControlsCard(RectTransform root)
        {
            var lines = GuidanceBindings.Read();
            var height = 74f + (lines.Count * 31f) + 74f;

            var group = UiFactory.Place(
                UiFactory.CreateRect("Controls", root),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -28f),
                new Vector2(620f, height));

            UiFactory.Stretch((RectTransform)UiFactory.CreateImage("Backing", group, PanelBacking).transform);

            Label(group, "Title", "조작", new Vector2(22f, -16f), 24, Bright, TextAnchor.UpperLeft, 580f);

            for (var i = 0; i < lines.Count; i++)
            {
                var y = -64f - (i * 31f);
                Label(group, "Label" + i, lines[i].Label, new Vector2(22f, y), 19, Dim, TextAnchor.UpperLeft, 360f);
                Label(group, "Keys" + i, lines[i].Keys, new Vector2(392f, y), 19, Bright, TextAnchor.UpperLeft, 210f);
            }

            var footer = -76f - (lines.Count * 31f);
            Label(group, "Tools", "F1  이 카드 다시 보기        F2  §14 관찰 항목",
                new Vector2(22f, footer), 18, UiStyle.Trade, TextAnchor.UpperLeft, 580f);
            Label(group, "Loop",
                "§01  지상(상점) ⇄ 지하(단서 · 전리품 · 목표물)",
                new Vector2(22f, footer - 28f), 17, Dim, TextAnchor.UpperLeft, 580f);

            _controlsGroup = group;
            group.gameObject.SetActive(true);
        }

        /// <summary>
        /// §14's five, in the document's own words, with Q2's live numbers under it and
        /// Q3's honest caveat under that. A tester hunting for a pressure that cannot
        /// build in a 2.5-minute match concludes the design is wrong; the overlay says
        /// which one of those it actually is.
        /// </summary>
        private void BuildQuestions(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Questions", root),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-28f, -150f),
                new Vector2(790f, 404f));

            UiFactory.Stretch((RectTransform)UiFactory.CreateImage("Backing", group, PanelBacking).transform);

            Label(group, "Title", "§14 검증 질문 — 이 다섯 개만  (F2로 닫기)",
                new Vector2(22f, -16f), 22, Bright, TextAnchor.UpperLeft, 750f);

            Label(group, "Q1", "1.  추격이 재밌는가?  (거리를 벌리는 순간이 짜릿한가)",
                new Vector2(22f, -58f), 19, Bright, TextAnchor.UpperLeft, 750f);
            Label(group, "Q2", "2.  곁눈질 딜레마가 작동하는가?  (뒤를 볼지 고민하게 되는가)",
                new Vector2(22f, -90f), 19, Bright, TextAnchor.UpperLeft, 750f);

            _questionNumbers = Label(group, "Q2Numbers", string.Empty,
                new Vector2(22f, -120f), 18, UiStyle.Trade, TextAnchor.UpperLeft, 750f);

            Label(group, "Q3", "3.  \"지금 나갈까?\" 갈등이 생기는가?",
                new Vector2(22f, -206f), 19, Bright, TextAnchor.UpperLeft, 750f);
            Label(group, "Q3Caveat", Q3Caveat(),
                new Vector2(22f, -236f), 16, UiStyle.Spent, TextAnchor.UpperLeft, 750f);

            Label(group, "Q4", "4.  \"6이었나 9였나\" 대화가 나오는가?",
                new Vector2(22f, -284f), 19, Bright, TextAnchor.UpperLeft, 750f);
            Label(group, "Q5", "5.  청음사가 방향·거리를 구별할 수 있는가?  (헤드폰 필요)",
                new Vector2(22f, -316f), 19, Bright, TextAnchor.UpperLeft, 750f);
            Label(group, "Q5Note", "     괴물의 거리는 표시하지 않습니다 — Q5는 귀로만 답이 나옵니다",
                new Vector2(22f, -346f), 16, Dim, TextAnchor.UpperLeft, 750f);

            _questionsGroup = group;
            group.gameObject.SetActive(false);
        }

        /// <summary>
        /// F-006, stated on the overlay. The window comes from <c>GameConstants</c>; the
        /// 2.5-minute median is a measurement and is cited as one.
        /// </summary>
        private static string Q3Caveat()
        {
            var min = (GameConstants.TargetMatchSecondsMin / 60f).ToString("0", CultureInfo.InvariantCulture);
            var max = (GameConstants.TargetMatchSecondsMax / 60f).ToString("0", CultureInfo.InvariantCulture);

            return "     ※ 아직 물을 수 없습니다 — 한 판 중앙값 2.5분, §01 목표는 "
                + min + "~" + max + "분 (docs/BALANCE-FINDINGS.md F-006)";
        }

        /// <summary>
        /// The run, on §02's end screen. §14 is answered by what happened, and §02's own
        /// screen is deliberately a ledger of what survived rather than a scoreboard — so
        /// the numbers go beside it instead of into it.
        /// </summary>
        private void BuildSummary(RectTransform root)
        {
            var height = 66f + (SummaryLabels.Length * 33f) + 84f;

            var group = UiFactory.Place(
                UiFactory.CreateRect("RunSummary", root),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(40f, 0f),
                new Vector2(470f, height));

            UiFactory.Stretch((RectTransform)UiFactory.CreateImage("Backing", group, PanelBacking).transform);

            Label(group, "Title", "이번 판 기록", new Vector2(22f, -16f), 22, Bright, TextAnchor.UpperLeft, 430f);

            _summaryValues = new Text[SummaryLabels.Length];
            for (var i = 0; i < SummaryLabels.Length; i++)
            {
                var y = -62f - (i * 33f);
                Label(group, "Label" + i, SummaryLabels[i], new Vector2(22f, y), 19, Dim, TextAnchor.UpperLeft, 160f);
                _summaryValues[i] = Label(
                    group, "Value" + i, string.Empty, new Vector2(192f, y), 19, Bright, TextAnchor.UpperLeft, 260f);
            }

            var footer = -(78f + (SummaryLabels.Length * 33f));
            Label(group, "Footer", "§14  1·2번이 안 되면 나머지는 의미가 없습니다",
                new Vector2(22f, footer), 17, UiStyle.Trade, TextAnchor.UpperLeft, 430f);
            Label(group, "Next", "[나가기]  같은 맵, 다음 시드로 새 판",
                new Vector2(22f, footer - 26f), 16, Dim, TextAnchor.UpperLeft, 430f);

            _summaryGroup = group;
            group.gameObject.SetActive(false);
        }

        private Text Label(
            RectTransform parent, string name, string value, Vector2 position, int size, Color colour,
            TextAnchor anchor, float width)
        {
            var text = UiFactory.CreateText(name, parent, Font, value, size, colour, anchor);
            UiFactory.Place((RectTransform)text.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(width, size + 10f));
            return text;
        }

        /// <summary>Writes a label only when it changed — a <c>Text</c> assignment rebuilds the canvas.</summary>
        private static void Set(Text? target, string value)
        {
            if (target != null && !string.Equals(target.text, value, System.StringComparison.Ordinal))
            {
                target.text = value;
            }
        }

        private static string Clock(float seconds)
        {
            var whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (whole / 60).ToString("00", CultureInfo.InvariantCulture)
                + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string F(float value)
        {
            if (float.IsPositiveInfinity(value))
            {
                return "inf";
            }

            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Signed(float value)
        {
            return value.ToString("+0.00;-0.00", CultureInfo.InvariantCulture);
        }
    }
}
