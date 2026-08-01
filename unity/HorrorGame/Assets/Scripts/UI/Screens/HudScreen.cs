#nullable enable

using System.Globalization;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Light;
using HorrorGame.Core.Match;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.UI.Readouts;
using UnityEngine;
using UnityEngine.UI;

namespace HorrorGame.UI.Screens
{
    /// <summary>
    /// The in-world HUD. §01 · §03 · §06 · §07 · §08 · §10.
    /// <para>
    /// <b>Its job is to make one §10 trade legible at the moment it is being made,
    /// and to be blank the rest of the time.</b> §10's map is a list of things a
    /// player gains by accepting a cost, and every element here is one of those
    /// costs while it is being paid: the cell draining while the beam is on, the
    /// weight band that just took 15% of the player's speed, the twelve seconds a
    /// Runner is deciding when to spend, the hands that are full of the objective and
    /// therefore empty of a flashlight. When none of those is happening the screen is
    /// empty, because §01 is a horror game and an idle HUD is a light source in the
    /// corner of the player's eye.
    /// </para>
    /// <para>
    /// Nothing here decides anything. Each frame it turns core state into a
    /// <see cref="HudReadout"/> and pushes strings, colours and fill fractions into
    /// widgets — no rule, no threshold with gameplay meaning, and no number that is
    /// not already in <c>GameConstants</c> (ARCHITECTURE §1 · §2).
    /// </para>
    /// <para>
    /// Two ways to drive it. <see cref="Apply"/> takes a finished readout, which is
    /// what the networked client uses — it is handed what the host says is true.
    /// <see cref="Bind"/> takes the core objects directly and pulls from them in
    /// <c>LateUpdate</c>, which is what the host and offline play use. Both end in the
    /// same place, so there is one HUD and not two.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudScreen : UiScreen
    {
        private RectTransform? _clockGroup;
        private Text? _clockPhase;
        private Text? _clockSource;
        private Text? _clockWarning;

        private RectTransform? _lightGroup;
        private UiBar? _lightBar;
        private Text? _lightText;

        private RectTransform? _loadGroup;
        private UiBar? _loadBar;
        private Text? _loadText;
        private Text? _loadHint;

        private RectTransform? _sprintGroup;
        private UiBar? _sprintBar;
        private Text? _sprintText;

        private RectTransform? _objectiveGroup;
        private Text? _objectiveText;
        private Text? _objectiveCost;

        private RoleId _role = RoleId.None;
        private MatchClock? _clock;
        private Inventory? _inventory;
        private FlashlightState? _flashlight;
        private StaminaState? _stamina;
        private bool _bound;

        /// <inheritdoc />
        protected override int SortOrder
        {
            get { return UiStyle.SortOrderHud; }
        }

        /// <summary>
        /// False. The HUD must never eat a click: the player is looking through it at
        /// a corridor, and the mouse belongs to the camera (§05 — "마우스 방향 = 이동
        /// 방향").
        /// </summary>
        protected override bool Interactive
        {
            get { return false; }
        }

        /// <summary>
        /// The clock this HUD was bound to, or null before <see cref="Bind"/> and after
        /// <see cref="Unbind"/>.
        /// <para>
        /// Exposed for <see cref="ShopScreen"/>, which is built as this screen's sibling
        /// and is bound later than it. §08's shop covers the display, so it hides the
        /// clock this screen draws — and §07 charges for the time spent standing in it,
        /// so the shop has to redraw the same night rather than leave the team without
        /// one. Reading it from here rather than from the scene keeps two instances of
        /// the game on one machine (§14) reading two different matches.
        /// </para>
        /// <para>
        /// Read-only on purpose: this is a window onto what the match owns, not a
        /// second place a clock can be set.
        /// </para>
        /// </summary>
        public MatchClock? Clock
        {
            get { return _clock; }
        }

        /// <summary>
        /// Points the HUD at the core objects the host is stepping, for host and
        /// offline play. Any of them may be null — an objective carrier has no
        /// flashlight, everybody but the Runner has no stamina.
        /// </summary>
        public void Bind(
            RoleId role,
            MatchClock? clock,
            Inventory? inventory,
            FlashlightState? flashlight,
            StaminaState? stamina)
        {
            _role = role;
            _clock = clock;
            _inventory = inventory;
            _flashlight = flashlight;
            _stamina = stamina;
            _bound = true;
            SetVisible(true);
        }

        /// <summary>Stops pulling from core objects. Used when the local player dies and the ghost overlay takes over (§09).</summary>
        public void Unbind()
        {
            _bound = false;
            _clock = null;
            _inventory = null;
            _flashlight = null;
            _stamina = null;
            Apply(new HudReadout(
                RoleId.None,
                ClockReadout.From(null),
                LightReadout.From(null),
                LoadReadout.From(null),
                SprintReadout.From(RoleId.None, null, true),
                new ObjectiveCarryReadout(false, false)));
        }

        /// <summary>
        /// Draws one frame's readout. The client's entry point — it is given what the
        /// host says rather than computing it.
        /// </summary>
        public void Apply(HudReadout readout)
        {
            EnsureBuilt();
            DrawClock(readout.Clock);
            DrawLight(readout.Light);
            DrawLoad(readout.Load);
            DrawSprint(readout.Sprint);
            DrawObjective(readout.Objective);
        }

        /// <summary>
        /// Pulls from the bound core objects after everything else has moved.
        /// <para>
        /// <c>LateUpdate</c> rather than <c>Update</c> so the HUD reports the state the
        /// player is about to see rather than the one they saw last frame — a stamina
        /// bar one frame behind is a bar that empties after the sprint has already
        /// stopped.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            if (!_bound)
            {
                return;
            }

            Apply(HudReadout.For(_role, _clock, _inventory, _flashlight, _stamina));
        }

        /// <inheritdoc />
        protected override void Build(RectTransform root)
        {
            BuildClock(root);
            BuildLight(root);
            BuildLoad(root);
            BuildSprint(root);
            BuildObjective(root);
        }

        // ------------------------------------------------------------------
        // §07 — the clock. Top centre, and usually not there at all.
        // ------------------------------------------------------------------

        private void BuildClock(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Clock", root),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -UiStyle.ScreenMargin),
                new Vector2(640f, 96f));
            _clockGroup = group;

            _clockPhase = AddText(group, "Phase", TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, 0f), UiStyle.TextSizeTitle, UiStyle.Ink);
            _clockSource = AddText(group, "Source", TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -42f), UiStyle.TextSizeSmall, UiStyle.InkFaint);
            _clockWarning = AddText(group, "Warning", TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -64f), UiStyle.TextSizeSmall, UiStyle.Trade);
        }

        private void DrawClock(ClockReadout clock)
        {
            if (_clockGroup == null || _clockPhase == null || _clockSource == null || _clockWarning == null)
            {
                return;
            }

            // §07: "안에서는 시간 감각이 없다." The group is switched off entirely rather
            // than showing a placeholder — a persistent "시각 불명" line is still a clock
            // in the corner of the screen, and it would blunt the 회중시계's whole reason
            // to exist.
            _clockGroup.gameObject.SetActive(clock.IsVisible);
            if (!clock.IsVisible)
            {
                return;
            }

            _clockPhase.text = clock.PhaseLabel;
            _clockPhase.color = clock.IsLate ? UiStyle.Trade : UiStyle.Ink;
            _clockSource.text = clock.SourceLabel;
            _clockWarning.text = clock.WarningLabel;
        }

        // ------------------------------------------------------------------
        // §03 — the battery. Bottom left.
        // ------------------------------------------------------------------

        private void BuildLight(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Light", root),
                Vector2.zero,
                Vector2.zero,
                new Vector2(UiStyle.ScreenMargin, UiStyle.ScreenMargin),
                new Vector2(UiStyle.BarWidth, 40f));
            _lightGroup = group;

            var bar = UiFactory.CreateBar("LightBar", group);
            UiFactory.Place(bar.Root, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(UiStyle.BarWidth, UiStyle.BarHeight));
            _lightBar = bar;

            _lightText = AddText(group, "LightText", TextAnchor.LowerLeft,
                Vector2.zero, new Vector2(0f, UiStyle.BarHeight + 4f), UiStyle.TextSizeSmall, UiStyle.InkFaint);
        }

        private void DrawLight(LightReadout light)
        {
            if (_lightGroup == null || _lightBar == null || _lightText == null)
            {
                return;
            }

            _lightGroup.gameObject.SetActive(light.IsVisible);
            if (!light.IsVisible)
            {
                return;
            }

            _lightBar.SetFill(light.ChargeFraction);

            // §03 ties the cell to the lock ("배터리가 떨어지면 단서를 읽을 수 없다"), so
            // the readout answers the two questions a player actually has: how long can
            // I keep looking, and is there another cell in the bag.
            var text = light.RemainingMinutes.ToString(CultureInfo.InvariantCulture)
                + ":" + light.RemainingSecondsPart.ToString("00", CultureInfo.InvariantCulture);

            if (light.SpareCells > 0)
            {
                text += "  여분 " + light.SpareCells.ToString(CultureInfo.InvariantCulture);
            }

            if (light.IsDead)
            {
                text = "배터리 없음";
            }
            else if (light.Upgraded && light.DrawingAttention)
            {
                // §08's 대가 for the flagship item, said while it is being paid.
                text += "  괴물이 2배 멀리서 본다";
            }

            _lightText.text = text;
            _lightText.color = light.IsDead ? UiStyle.Spent : UiStyle.InkFaint;
        }

        // ------------------------------------------------------------------
        // §08 — carry weight and the speed it costs. Bottom left, above the light.
        // ------------------------------------------------------------------

        private void BuildLoad(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Load", root),
                Vector2.zero,
                Vector2.zero,
                new Vector2(UiStyle.ScreenMargin, UiStyle.ScreenMargin + (UiStyle.LineGap * 2f)),
                new Vector2(UiStyle.BarWidth, 44f));
            _loadGroup = group;

            var bar = UiFactory.CreateBar("LoadBar", group);
            UiFactory.Place(bar.Root, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(UiStyle.BarWidth, UiStyle.BarHeight));
            _loadBar = bar;

            _loadText = AddText(group, "LoadText", TextAnchor.LowerLeft,
                Vector2.zero, new Vector2(0f, UiStyle.BarHeight + 4f), UiStyle.TextSizeSmall, UiStyle.InkFaint);
            _loadHint = AddText(group, "LoadHint", TextAnchor.LowerLeft,
                Vector2.zero, new Vector2(0f, UiStyle.BarHeight + 22f), UiStyle.TextSizeSmall, UiStyle.Trade);
        }

        private void DrawLoad(LoadReadout load)
        {
            if (_loadGroup == null || _loadBar == null || _loadText == null || _loadHint == null)
            {
                return;
            }

            _loadGroup.gameObject.SetActive(load.IsVisible);
            if (!load.IsVisible)
            {
                return;
            }

            // Weight fills toward the band that hurts, so the bar reads as "how close am
            // I to the next penalty" rather than as a score. Colour comes from the band,
            // not from the fraction: at band 3 §08 takes the sprint away from everybody,
            // "주자도".
            var color = load.CanSprint
                ? (load.IsPenalised ? UiStyle.Trade : UiStyle.Calm)
                : UiStyle.Spent;
            _loadBar.SetFill(load.Fill01, color);

            var text = load.TotalWeight.ToString(CultureInfo.InvariantCulture)
                + " / " + load.Capacity.ToString(CultureInfo.InvariantCulture);

            if (load.IsPenalised)
            {
                text += "   −" + load.PenaltyPercent.ToString(CultureInfo.InvariantCulture) + "%";
            }

            _loadText.text = text;
            _loadText.color = load.IsPenalised ? UiStyle.Trade : UiStyle.InkFaint;

            // §10 wants the trade legible at the moment of choosing, which is standing
            // over the next piece of loot — not after picking it up.
            if (!load.CanSprint)
            {
                _loadHint.text = "질주 불가";
                _loadHint.color = UiStyle.Spent;
            }
            else if (load.OneMoreCosts)
            {
                _loadHint.text = "하나 더 = −" + load.PenaltyPercentAfterOneMore.ToString(CultureInfo.InvariantCulture) + "%";
                _loadHint.color = UiStyle.Trade;
            }
            else
            {
                _loadHint.text = string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // §06 — the Runner's twelve seconds. Bottom centre.
        // ------------------------------------------------------------------

        private void BuildSprint(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Sprint", root),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, UiStyle.ScreenMargin),
                new Vector2(280f, 30f));
            _sprintGroup = group;

            var bar = UiFactory.CreateBar("SprintBar", group, 280f, UiStyle.BarHeight);
            UiFactory.Place(bar.Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero,
                new Vector2(280f, UiStyle.BarHeight));
            _sprintBar = bar;

            _sprintText = AddText(group, "SprintText", TextAnchor.LowerCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, UiStyle.BarHeight + 4f), UiStyle.TextSizeSmall, UiStyle.InkFaint);
        }

        private void DrawSprint(SprintReadout sprint)
        {
            if (_sprintGroup == null || _sprintBar == null || _sprintText == null)
            {
                return;
            }

            _sprintGroup.gameObject.SetActive(sprint.IsVisible);
            if (!sprint.IsVisible)
            {
                return;
            }

            _sprintBar.SetFill(sprint.Fraction);

            if (sprint.WeightBlocked)
            {
                // §08's band 3, which the section says applies to "주자도". A different
                // problem from an empty bar, and it has a different fix: drop something.
                _sprintText.text = "무게 때문에 질주 불가";
                _sprintText.color = UiStyle.Spent;
            }
            else if (sprint.IsLockedOut)
            {
                // §06 promises "주자도 스태미나가 끝나면 잡힌다"; the bar refusing to
                // re-engage is that promise, so it is said rather than left to be felt.
                _sprintText.text = sprint.SecondsUntilAvailable.ToString("0.0", CultureInfo.InvariantCulture) + "초 후";
                _sprintText.color = UiStyle.Spent;
            }
            else
            {
                _sprintText.text = string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // §03 — the objective in both hands. Bottom centre, above the sprint bar.
        // ------------------------------------------------------------------

        private void BuildObjective(RectTransform root)
        {
            var group = UiFactory.Place(
                UiFactory.CreateRect("Objective", root),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, UiStyle.ScreenMargin + (UiStyle.LineGap * 2f)),
                new Vector2(640f, 48f));
            _objectiveGroup = group;

            _objectiveText = AddText(group, "ObjectiveText", TextAnchor.LowerCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, 20f), UiStyle.TextSize, UiStyle.Trade);
            _objectiveCost = AddText(group, "ObjectiveCost", TextAnchor.LowerCenter,
                new Vector2(0.5f, 0f), Vector2.zero, UiStyle.TextSizeSmall, UiStyle.InkFaint);
        }

        private void DrawObjective(ObjectiveCarryReadout objective)
        {
            if (_objectiveGroup == null || _objectiveText == null || _objectiveCost == null)
            {
                return;
            }

            _objectiveGroup.gameObject.SetActive(objective.IsVisible);
            if (!objective.IsVisible)
            {
                return;
            }

            _objectiveText.text = "목표물 운반 중";

            // §03's three subtractions, said rather than discovered. The carrier cannot
            // see ahead ("운반자는 앞을 보지 못하므로 사실상 2인 1조 호송"), so the line
            // doubles as the instruction to ask somebody for light.
            var cost = "양손 사용 — 손전등 불가 · 전리품 불가";
            if (objective.SprintSurrendered)
            {
                cost += " · 질주 불가";
            }

            _objectiveCost.text = cost;
        }

        // ------------------------------------------------------------------

        private Text AddText(
            RectTransform parent, string name, TextAnchor alignment, Vector2 anchor, Vector2 position, int size, Color color)
        {
            var text = UiFactory.CreateText(name, parent, Font, string.Empty, size, color, alignment);
            UiFactory.Place((RectTransform)text.transform, anchor, anchor, position, new Vector2(640f, size + 8f));
            return text;
        }
    }
}
