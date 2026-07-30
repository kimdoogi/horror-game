#nullable enable

using HorrorGame.Core.Economy;
using HorrorGame.Core.Light;
using HorrorGame.Core.Match;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;

namespace HorrorGame.UI.Readouts
{
    /// <summary>
    /// §03's objective in the player's hands, and everything that costs them.
    /// <para>
    /// §03 spends three rules on the carry and every one of them is a subtraction:
    /// <em>"양손을 쓴다 → 손전등을 들 수 없다 → 누군가 비춰줘야 한다"</em>,
    /// <em>"전리품 동시 소지 불가"</em>, <em>"주자가 들면 질주 불가"</em>. The
    /// section closes by calling the result "사실상 2인 1조 호송".
    /// </para>
    /// <para>
    /// Those subtractions are the reason the carry is a decision rather than a
    /// pickup, so the HUD states them rather than letting the player discover them
    /// by pressing a key that no longer works. This is §10's principle applied to a
    /// readout: the cost has to be visible at the moment of choosing.
    /// </para>
    /// </summary>
    public readonly struct ObjectiveCarryReadout
    {
        /// <summary>Builds a readout directly.</summary>
        public ObjectiveCarryReadout(bool carrying, bool isRunner)
        {
            Carrying = carrying;
            IsRunner = isRunner;
        }

        /// <summary>Whether this player has the objective in both hands.</summary>
        public bool Carrying { get; }

        /// <summary>Whether the carrier is §04's 주자 — the case §03 calls out by name.</summary>
        public bool IsRunner { get; }

        /// <summary>Drawn only while carrying. There is nothing to say about not holding it.</summary>
        public bool IsVisible
        {
            get { return Carrying; }
        }

        /// <summary>§03: both hands are used, so there is no flashlight. Always true while carrying.</summary>
        public bool LightSurrendered
        {
            get { return Carrying; }
        }

        /// <summary>§03: no loot alongside it. Always true while carrying.</summary>
        public bool LootBlocked
        {
            get { return Carrying; }
        }

        /// <summary>§03: "주자가 들면 질주 불가". Only worth saying to the player it takes something from.</summary>
        public bool SprintSurrendered
        {
            get { return Carrying && IsRunner; }
        }
    }

    /// <summary>
    /// Everything the in-world HUD may draw, resolved once per frame from core state.
    /// <para>
    /// <b>The brief is subtraction.</b> §01 is a horror game and the flashlight beam
    /// is supposed to be where the player is looking, so each part of this readout
    /// owns an <c>IsVisible</c> and the HUD draws nothing for the parts that answer
    /// false. In the common case — walking a corridor, light off, empty hands, not
    /// the Runner — the screen is empty.
    /// </para>
    /// <para>
    /// <b>What is deliberately absent.</b> No compass, no map, no objective marker,
    /// no clue log, no monster indicator, no teammate healthbars, no timer. Each of
    /// those would answer a question the design charges for: §03 makes the objective
    /// something you deduce and §07 makes the hour something you go outside for or
    /// buy. §08 sells a 건물 도면 and a 감지기 for two of them, and a HUD that gave
    /// either away for free would be deleting inventory from the shop.
    /// </para>
    /// <para>
    /// Values only. No method here reaches back into a core object, so a screen can
    /// hold last frame's readout, compare, and redraw only what moved.
    /// </para>
    /// </summary>
    public readonly struct HudReadout
    {
        /// <summary>Builds a readout directly. Prefer <see cref="For"/>.</summary>
        public HudReadout(
            RoleId role,
            ClockReadout clock,
            LightReadout light,
            LoadReadout load,
            SprintReadout sprint,
            ObjectiveCarryReadout objective)
        {
            Role = role;
            Clock = clock;
            Light = light;
            Load = load;
            Sprint = sprint;
            Objective = objective;
        }

        /// <summary>This player's §04 role. Decides whether the sprint bar exists at all.</summary>
        public RoleId Role { get; }

        /// <summary>§07's gated clock. Usually invisible.</summary>
        public ClockReadout Clock { get; }

        /// <summary>§03's battery.</summary>
        public LightReadout Light { get; }

        /// <summary>§08's carry weight and the speed it costs.</summary>
        public LoadReadout Load { get; }

        /// <summary>§06's sprint bar, for the Runner only.</summary>
        public SprintReadout Sprint { get; }

        /// <summary>§03's objective carry, and the three things it takes away.</summary>
        public ObjectiveCarryReadout Objective { get; }

        /// <summary>True when the HUD would draw nothing at all — the state most of a descent should be in.</summary>
        public bool IsEmpty
        {
            get
            {
                return !Clock.IsVisible
                    && !Light.IsVisible
                    && !Load.IsVisible
                    && !Sprint.IsVisible
                    && !Objective.IsVisible;
            }
        }

        /// <summary>
        /// Gathers one frame's HUD from the core objects the host is stepping.
        /// <para>
        /// Every parameter is nullable because a HUD is alive before, between and
        /// after the things it describes: a player waiting in the lobby has no
        /// inventory, an objective carrier has no flashlight, and everybody but the
        /// Runner has no stamina. A missing part reads as "nothing to show", never as
        /// an exception — the alternative is a null reference thrown from a frame
        /// during a chase.
        /// </para>
        /// </summary>
        public static HudReadout For(
            RoleId role,
            MatchClock? clock,
            Inventory? inventory,
            FlashlightState? flashlight,
            StaminaState? stamina)
        {
            var load = LoadReadout.From(inventory);
            var carrying = inventory != null && inventory.CarryingObjective;

            return new HudReadout(
                role,
                ClockReadout.From(clock),
                LightReadout.From(flashlight),
                load,
                SprintReadout.From(role, stamina, load.CanSprint),
                new ObjectiveCarryReadout(carrying, role == RoleId.Runner));
        }
    }
}
