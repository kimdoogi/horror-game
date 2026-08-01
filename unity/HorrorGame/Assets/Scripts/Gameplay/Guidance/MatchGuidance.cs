#nullable enable

using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Core;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Match;
using HorrorGame.Gameplay.Match;
using HorrorGame.Gameplay.Monster;
using HorrorGame.Gameplay.Player;
using UnityEngine;
using MonsterStateId = HorrorGame.Core.Monster.MonsterStateId;

namespace HorrorGame.Gameplay.Guidance
{
    /// <summary>
    /// What to do next, in Korean, read off the match rather than scripted. §01 · §03 · §08.
    /// <para>
    /// <b>Why this exists.</b> §14 asks five questions that only a person can answer,
    /// and the person answering them has to be able to play the game without reading
    /// the design document first. Everything §01's loop asks for is implemented and
    /// none of it is announced: a tester standing at the van has no way to know that
    /// walking out of the apron is a 잠입, that a 표식 needs the beam held on it, or
    /// that the thing in their hands is why the flashlight stopped working. This class
    /// turns the match's own state into one sentence saying which of those is next.
    /// </para>
    /// <para>
    /// <b>It is a reader, never a rule.</b> Every value comes off <c>MatchDirector</c>,
    /// <c>MatchClock</c>, <c>Inventory</c> or <c>MonsterAgent</c> — nothing here decides
    /// anything, keeps a parallel copy of match state, or hard-codes a tuned number
    /// (ARCHITECTURE §1 · §2). <see cref="Observe"/> is idempotent within a frame and
    /// safe to call from a headless test loop, which is how the phase table is pinned.
    /// </para>
    /// <para>
    /// <b>§03's constraint is the one thing this must not break.</b> "그 자리에서 보고,
    /// 기억해서, 말로 전달해야 한다" — so this counts marks and never records one. There
    /// is no field here that could hold a glyph, a floor number or a site label, and the
    /// progress figure below is a count of completed reads and nothing else. A guidance
    /// line that quoted what a clue said would be the clue log §03 forbids, arriving by
    /// the back door.
    /// </para>
    /// </summary>
    public sealed class MatchGuidance
    {
        private readonly MatchDirector _director;

        /// <summary>
        /// Clue ids this player has finished a read on. Ids only — see the class remarks.
        /// A set rather than a counter because <c>ClueReader</c> holds
        /// <see cref="ClueReadState.Complete"/> for as long as the beam stays on the
        /// mark, and re-reading a mark you already know is not progress.
        /// </summary>
        private readonly HashSet<int> _cluesRead = new HashSet<int>();

        private PlayerLoadout? _loadout;
        private PlayerMotor? _motor;
        private MonsterAgent? _monster;
        private MatchHud? _hud;

        /// <summary>
        /// The clock instance the counters below belong to. <c>MatchDirector.BeginMatch</c>
        /// assigns a fresh <c>MatchClock</c>, so a reference comparison is a free and
        /// exact "is this still the same match" — there is no match id to ask for, and a
        /// tally carried across the end screen's 계속 button would be a lie.
        /// </summary>
        private MatchClock? _generation;

        private bool _wasOnSurface = true;
        private bool _wasChasing;
        private int _ghostsSeen;
        private int _deathsAtChaseStart;
        private int _lootHeldBelow;
        private int _lineKey = int.MinValue;
        private int _loadKey = int.MinValue;

        /// <summary>Points the guidance at a match. The rig and the monster are found lazily.</summary>
        /// <param name="director">The match to read. Never written to.</param>
        public MatchGuidance(MatchDirector director)
        {
            _director = director;
        }

        /// <summary>Where the player is in §01's loop right now.</summary>
        public GuidancePhase Phase { get; private set; } = GuidancePhase.Idle;

        /// <summary>The one line a tester should be able to act on without reading anything.</summary>
        public string Line { get; private set; } = string.Empty;

        /// <summary>
        /// §08's live load, or empty when the hands are free. Separate from
        /// <see cref="Line"/> because it is true in every phase and §08 makes it a
        /// decision rather than a step — "이거 들고 갈까? 2명 필요해".
        /// </summary>
        public string Note { get; private set; } = string.Empty;

        /// <summary>Whether <see cref="Note"/> is reporting a speed the player is paying for. §08.</summary>
        public bool NoteIsPenalty { get; private set; }

        /// <summary>Marks this player has finished reading. A count — never what they said (§03).</summary>
        public int CluesRead
        {
            get { return _cluesRead.Count; }
        }

        /// <summary>§03's 3층위: how many marks the chain needs before it converges.</summary>
        public int CluesRequired
        {
            get { return GameConstants.CluesRequiredToLocate; }
        }

        /// <summary>Marks placed on this map — §03's three plus any decoys. The denominator a tester sees is <see cref="CluesRequired"/>, not this.</summary>
        public int CluesOnMap
        {
            get { return _director.ClueCount; }
        }

        /// <summary>Times §06's monster entered 추격 this match. §14 Q1's raw material.</summary>
        public int Chases { get; private set; }

        /// <summary>Chases that ended with the player still alive. §14 Q1: "거리를 벌리는 순간".</summary>
        public int ChasesBroken { get; private set; }

        /// <summary>§09 deaths this match.</summary>
        public int Deaths { get; private set; }

        /// <summary>Pieces of §08 전리품 carried up and sold into the shared wallet.</summary>
        public int LootSold { get; private set; }

        /// <summary>§08's 공용 지갑 right now.</summary>
        public int Credits
        {
            get { return _director.Shop.Wallet.Credits; }
        }

        /// <summary>§01 round trips completed — the count §03's 왕복 구조 is about.</summary>
        public int RoundTrips
        {
            get { return _director.Clock.SurfacingCount; }
        }

        /// <summary>Seconds since the match began. §07's clock, which never pauses.</summary>
        public float ElapsedSeconds
        {
            get { return _director.Clock.ElapsedSeconds; }
        }

        /// <summary>§02's reading of the match. Final only once <c>IsFinal</c>.</summary>
        public MatchResolution Resolution
        {
            get { return _director.Resolution; }
        }

        /// <summary>The player rig's motor, resolved lazily. Null on a scene with no rig.</summary>
        public PlayerMotor? Motor
        {
            get { return _motor; }
        }

        /// <summary>§06's monster, resolved lazily. Null on a scene with none.</summary>
        public MonsterAgent? Monster
        {
            get { return _monster; }
        }

        /// <summary>
        /// Re-reads the match and rewrites <see cref="Line"/>. Call once per frame — or,
        /// in a headless test, once after each step.
        /// </summary>
        public void Observe()
        {
            ResolveWiring();
            NoteNewMatchIfAny();

            var state = _director.State;
            if (state == null)
            {
                Phase = GuidancePhase.Idle;
                Line = string.Empty;
                Note = string.Empty;
                NoteIsPenalty = false;
                _lineKey = int.MinValue;
                _loadKey = int.MinValue;
                return;
            }

            CountClueReads();
            CountChases();
            CountDeaths(state);
            CountSales();

            // The two strings are rebuilt only when something in them changed. Not a
            // micro-optimisation: §14's first question is whether a chase is fun, and a
            // line that concatenated four strings every frame would put an allocation on
            // every frame of that chase. The one moment a stutter is least affordable is
            // the exact moment this layer exists to let somebody judge.
            var phase = ResolvePhase(state);
            var lineKey = (_cluesRead.Count * 397) ^ Credits ^ (_director.ShopOpen ? 1 << 30 : 0);
            if (phase != Phase || lineKey != _lineKey)
            {
                Line = Describe(phase);
                _lineKey = lineKey;
            }

            Phase = phase;

            var loadKey = LoadKey();
            if (loadKey != _loadKey)
            {
                Note = DescribeLoad(out var penalty);
                NoteIsPenalty = penalty;
                _loadKey = loadKey;
            }
        }

        /// <summary>
        /// Everything <see cref="DescribeLoad"/> reads, in one integer. §08's multiplier
        /// is a pure function of the weight and the bag, so those two settle it.
        /// </summary>
        private int LoadKey()
        {
            var inventory = _loadout != null ? _loadout.Inventory : null;
            if (inventory == null)
            {
                return 0;
            }

            return (inventory.TotalWeight * 397) ^ (inventory.Capacity * 7);
        }

        /// <summary>
        /// §01's arc, most committing state first.
        /// <para>
        /// The order is the whole correctness argument. A tester who stumbles onto the
        /// objective before reading anything is carrying it, and being told to go read
        /// 표식 would be wrong — so the hands are asked before the clue count, the clue
        /// count before the phase of the loop, and "where am I standing" last. Every
        /// branch is a state the match can actually be in, including the ones §01's
        /// diagram does not draw.
        /// </para>
        /// </summary>
        private GuidancePhase ResolvePhase(MatchState state)
        {
            if (_director.Resolution.IsFinal || !_director.IsRunning)
            {
                return GuidancePhase.MatchOver;
            }

            var player = state.PlayerAt(_director.LocalPlayerIndex);

            if (player.IsCarryingObjective)
            {
                return GuidancePhase.CarryObjectiveOut;
            }

            if (_director.LocalPlayerOnSurface)
            {
                if (HoldsLoot())
                {
                    return GuidancePhase.SellLoot;
                }

                // §07's clock only accrues 지하 seconds while the team is below, so this
                // is "has this player ever actually descended" without keeping a second
                // copy of a fact the clock already owns.
                return _director.Clock.BasementSeconds > 0f
                    ? GuidancePhase.ShopAndDescendAgain
                    : GuidancePhase.Descend;
            }

            return _cluesRead.Count >= GameConstants.CluesRequiredToLocate
                ? GuidancePhase.FindObjective
                : GuidancePhase.ReadClues;
        }

        private string Describe(GuidancePhase phase)
        {
            switch (phase)
            {
                case GuidancePhase.Descend:
                    // The line names what is under the player's feet. It used to say
                    // 앞마당, which is a word for a place the game did not draw — the
                    // owner played the build and asked where it was, which is how
                    // SurfaceApron came to exist. Now that §01's 지상 is painted at
                    // MatchMap.SurfaceRadius, the text points at the paint instead of
                    // carrying the whole boundary on its own.
                    return "아래로 내려가세요 — 바닥의 노란 선을 넘으면 잠입입니다";

                case GuidancePhase.ReadClues:
                    return "표식을 찾아 손전등을 고정하세요 ("
                        + _cluesRead.Count.ToString(CultureInfo.InvariantCulture) + "/"
                        + GameConstants.CluesRequiredToLocate.ToString(CultureInfo.InvariantCulture) + ")"
                        + " — 가까이서 멈춰 "
                        + GameConstants.ClueReadSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                        + "초, 불이 꺼지면 처음부터";

                case GuidancePhase.FindObjective:
                    return "목표물을 찾으세요 — 표식 "
                        + _cluesRead.Count.ToString(CultureInfo.InvariantCulture) + "/"
                        + GameConstants.CluesRequiredToLocate.ToString(CultureInfo.InvariantCulture)
                        + ", 위치는 기억에만 남습니다";

                case GuidancePhase.CarryObjectiveOut:
                    // §02's terminal row, named by something visible: the match ends when
                    // the objective crosses the painted line, so that is what to say.
                    return "출입구로 운반하세요 — 노란 선 안이 지상입니다, 손전등을 들 수 없습니다";

                case GuidancePhase.SellLoot:
                    return "차량에 팔고 상점을 여세요";

                case GuidancePhase.ShopAndDescendAgain:
                    // §08 loads the loot the instant the player reaches the apron, so by
                    // the time anybody reads this the selling is done and the panel is
                    // already up — and a first-timer's actual question is how to make it
                    // go away. The shop's own state answers it.
                    return _director.ShopOpen
                        ? "전리품은 팔렸습니다 — 크레딧 " + Credits.ToString(CultureInfo.InvariantCulture)
                            + " · 사고 나면 E로 상점을 닫고 다시 내려가세요"
                        : "차량에서 E로 상점을 열거나, 다시 내려가세요 — 크레딧 "
                            + Credits.ToString(CultureInfo.InvariantCulture);

                case GuidancePhase.MatchOver:
                    // Nothing. §02's end screen and the run summary beside it are already
                    // saying everything there is to say, and the bottom of that screen
                    // belongs to §02's own 나가기 button — a guidance line there would sit
                    // on top of the one control the ending has.
                    return string.Empty;

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// §08's weight band, live. The multiplier is <c>Inventory</c>'s own — §08 says
        /// the penalty is multiplicative into §05's table, and recomputing it here would
        /// put a second, disagreeing economy on the screen.
        /// </summary>
        private string DescribeLoad(out bool penalty)
        {
            penalty = false;

            var inventory = _loadout != null ? _loadout.Inventory : null;
            if (inventory == null || inventory.TotalWeight <= 0)
            {
                return string.Empty;
            }

            var multiplier = inventory.SpeedMultiplier;
            penalty = multiplier < 1f;

            var text = "무게 " + inventory.TotalWeight.ToString(CultureInfo.InvariantCulture)
                + "/" + inventory.Capacity.ToString(CultureInfo.InvariantCulture)
                + " · 속도 ×" + multiplier.ToString("0.00", CultureInfo.InvariantCulture);

            if (!inventory.CanSprint)
            {
                // §08's fourth band, and it says "주자도" — the Runner is not exempt.
                text += " · 질주 불가";
                penalty = true;
            }

            return text;
        }

        private bool HoldsLoot()
        {
            if (_loadout == null)
            {
                return false;
            }

            return _loadout.Inventory.LootCount > 0 || _loadout.CarryingOversizePiece;
        }

        /// <summary>
        /// Adds a mark to the read set the moment §03's reader completes a <em>legible</em>
        /// one. Distinct ids, because <c>ClueReader</c> stays complete while the beam
        /// stays on the mark and re-reading something you already know is not progress.
        /// <para>
        /// Legibility is asked of the overlay rather than of the resolver, and that is
        /// the only place it can be asked from: §13 keeps clue content on the host and
        /// <c>MatchDirector</c> exposes no reader for it, so the one thing that crosses
        /// out of the host is the rendered view. What is taken from it is a
        /// <c>bool</c> — whether this look produced anything at all. §03 says a failed
        /// read leaves the team with nothing, so a count that ticked anyway would be
        /// telling a tester they had learned something they had not.
        /// </para>
        /// </summary>
        private void CountClueReads()
        {
            var reader = _director.ClueReader;
            if (reader.State != ClueReadState.Complete || reader.ClueId < 0)
            {
                return;
            }

            var overlay = _hud != null ? _hud.Clue : null;
            if (overlay != null && !overlay.Current.Legible)
            {
                return;
            }

            _cluesRead.Add(reader.ClueId);
        }

        /// <summary>
        /// §06's 추격, counted on its edges. A chase that ends without a death is one the
        /// player got out of, which is the event §14's first question is asking about —
        /// including the one §03's partial reset ends by putting the monster back at its
        /// spawn, because walking out of the building is a way of losing it.
        /// </summary>
        private void CountChases()
        {
            var chasing = _monster != null && _monster.State == MonsterStateId.Chase;

            if (chasing && !_wasChasing)
            {
                Chases++;
                _deathsAtChaseStart = Deaths;
            }
            else if (!chasing && _wasChasing && Deaths == _deathsAtChaseStart)
            {
                ChasesBroken++;
            }

            _wasChasing = chasing;
        }

        private void CountDeaths(MatchState state)
        {
            var ghosts = state.GhostCount;
            if (ghosts > _ghostsSeen)
            {
                Deaths += ghosts - _ghostsSeen;
            }

            _ghostsSeen = ghosts;
        }

        /// <summary>
        /// §08's 판매, which happens on arrival rather than as an errand — so the count is
        /// taken from what was in the hands on the last tick below ground, and banked when
        /// the player crosses into the apron.
        /// </summary>
        private void CountSales()
        {
            var onSurface = _director.LocalPlayerOnSurface;

            if (!onSurface && _loadout != null)
            {
                _lootHeldBelow = _loadout.Inventory.LootCount + (_loadout.CarryingOversizePiece ? 1 : 0);
            }

            if (onSurface && !_wasOnSurface)
            {
                LootSold += _lootHeldBelow;
                _lootHeldBelow = 0;
            }

            _wasOnSurface = onSurface;
        }

        private void NoteNewMatchIfAny()
        {
            var clock = _director.Clock;
            if (ReferenceEquals(clock, _generation))
            {
                return;
            }

            _generation = clock;
            _cluesRead.Clear();
            Chases = 0;
            ChasesBroken = 0;
            Deaths = 0;
            LootSold = 0;
            _ghostsSeen = 0;
            _deathsAtChaseStart = 0;
            _lootHeldBelow = 0;
            _wasChasing = false;
            _wasOnSurface = _director.LocalPlayerOnSurface;
            _lineKey = int.MinValue;
            _loadKey = int.MinValue;
        }

        private void ResolveWiring()
        {
            if (_motor == null)
            {
                _motor = Object.FindFirstObjectByType<PlayerMotor>();
            }

            if (_loadout == null)
            {
                _loadout = _motor != null
                    ? _motor.GetComponentInChildren<PlayerLoadout>()
                    : Object.FindFirstObjectByType<PlayerLoadout>();
            }

            if (_monster == null)
            {
                _monster = Object.FindFirstObjectByType<MonsterAgent>();
            }

            if (_hud == null)
            {
                _hud = Object.FindFirstObjectByType<MatchHud>();
            }
        }
    }
}
