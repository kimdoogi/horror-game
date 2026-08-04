#nullable enable

using HorrorGame.Audio;
using HorrorGame.Core.Race;
using HorrorGame.Gameplay.Audio;
using HorrorGame.UI;
using HorrorGame.UI.Screens;
using UnityEngine;

namespace HorrorGame.Gameplay.Match
{
    /// <summary>
    /// Makes being caught legible: half a second of black, one sting, and the name of
    /// what just happened. §01 · §02 · §06.
    /// <para>
    /// <b>The defect this closes.</b> <c>MatchDirector.CheckGrab</c> resolves a catch in
    /// one fixed step — <c>SendBackToTheStartLine</c> writes the transform,
    /// <c>RaceState.ReportCaught</c> zeroes the storey — and the only output was a
    /// <c>Debug.Log</c>. §06's grab clip is not the answer: it fires on the <em>lunge</em>,
    /// which means it plays identically on a strike that misses, so the loudest thing in
    /// the moment says nothing about whether the moment happened. A player who cannot tell
    /// "it nearly had me" from "I have lost eight storeys" reads the second one as a bug.
    /// </para>
    /// <para>
    /// <b>Why this watches the standings instead of being told.</b> The trigger is
    /// <c>Racer.TimesCaught</c> going up on <see cref="IRaceReadout.LocalRacer"/> — the
    /// same one-way window <see cref="RaceHud"/> draws from. §02 puts the catch on the
    /// host, and §13 makes every client render a standing it was told rather than one it
    /// computed; hanging the presentation off the same reading means the picture cannot
    /// disagree with the scoreboard about whether a runner was caught. A second trigger
    /// wired straight to <c>CheckGrab</c> would be a second source of truth, and this
    /// repo's own history is a list of what those cost — a race that recorded descents
    /// into an object nothing read, and nothing failed.
    /// </para>
    /// <para>
    /// <b>And the poll is the only trigger that works on a client.</b>
    /// <c>RaceDirector</c> raises a <c>Caught(seat, storey)</c> event, which looks like the
    /// obvious thing to subscribe to and is not: it is raised inside <c>ReportCaught</c>,
    /// which returns early when <c>Rules</c> is null, and <c>Rules</c> is null on every
    /// machine that is not judging — deliberately and structurally, so that a client cannot
    /// answer §02's questions on its own. A runner caught on a client would therefore see
    /// nothing. <see cref="IRaceReadout.LocalRacer"/> has no such hole: on the host it
    /// reads the <c>RaceState</c>, on a client it resolves through <c>NetRace.Standings</c>,
    /// which the host broadcasts after every accepted change. One trigger, both machines.
    /// </para>
    /// <para>
    /// <b>The storey has to be remembered, not asked for.</b> By the time
    /// <c>TimesCaught</c> increments, <c>Storey</c> is already 0 — that reset is what lets
    /// the runner descend again through a monotonic <c>ReportDescent</c>. So the last
    /// storey seen <em>before</em> the increment is the one that was lost, and it is the
    /// only interesting number in the sentence: "B1" is where they are, "B6 → B1" is what
    /// it cost.
    /// </para>
    /// <para>
    /// <b>It reports whether it actually made a sound.</b> <see cref="LastStingPlayed"/>
    /// is <c>AudioCuePlayer.Play</c>'s own return value — true only when a clip came out
    /// of a bank — and a false is logged as a warning rather than swallowed. An unwired
    /// cue is silent by construction (<see cref="AudioCuePlayer.PlayAt"/> returns false
    /// for an empty bank), which is exactly the shape of failure that ships looking fine.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Match/Caught Feedback")]
    public sealed class CaughtFeedback : MonoBehaviour
    {
        /// <summary>
        /// The one-shot this raises. §06.
        /// <para>
        /// Id 30, which <c>MatchAudioLibrary.asset</c> resolves today to
        /// <c>death_transition_01/02</c> — a cue that nothing in the game has raised since
        /// §09's spectator was deleted, sitting on the <c>Interface</c> bus, describing
        /// precisely the moment that has been redefined underneath it. Wiring it is
        /// therefore a smaller change than adding a cue, and it means this component makes
        /// a sound on the tree as it stands rather than on the tree as somebody intends it
        /// to be.
        /// </para>
        /// <para>
        /// <c>tools/audio/gen_caught.py</c> writes <c>caught_sent_home.wav</c>, which is
        /// what this cue should point at: one sting instead of two variants, 1.05 s
        /// instead of 3.45 s, and no ghost drone for a spectator state that no longer
        /// exists. That re-point is a one-line change to <c>AudioClipCatalog</c>'s
        /// <c>CueTable</c> plus a re-run of ▸ Audio ▸ Rebuild Audio Libraries; the
        /// generator prints whether it has happened yet.
        /// </para>
        /// </summary>
        public const AudioCueId Sting = AudioCueId.Caught;

        [Tooltip("The curtain. Left empty, one is found in the scene or built here.")]
        [SerializeField]
        private CaughtScreen? screen;

        private IRaceReadout? _race;
        private AudioCuePlayer? _cues;

        /// <summary>
        /// <c>TimesCaught</c> as of the last frame this looked, or −1 before the first
        /// look. The −1 matters: a client that binds to a match already in progress must
        /// adopt whatever count it finds without playing a curtain for somebody else's
        /// history.
        /// </summary>
        private int _seenTimesCaught = -1;

        /// <summary>The storey seen last frame. See the class remarks.</summary>
        private int _lastStorey;

        /// <summary>
        /// Cached fallback HUD. <c>FindFirstObjectByType</c> walks the scene, and a
        /// per-frame walk for an object that either exists or does not is a cost paid by
        /// every unbound frame of every match. Re-looked at <see cref="SearchInterval"/>
        /// so a HUD created after this component still gets picked up.
        /// </summary>
        private RaceHud? _hud;

        private float _nextSearch;

        /// <summary>
        /// Seconds between scene searches for a HUD when this is not bound. One second:
        /// the fallback is for a scene assembled in a different order, and a second of
        /// latency on the first frame of a match is a second before anybody can be caught.
        /// </summary>
        private const float SearchInterval = 1f;

        /// <summary>How many catches this has drawn. A test's proof the wire is live.</summary>
        public int Played { get; private set; }

        /// <summary>The storey the last curtain said was lost, 0-based. −1 before the first.</summary>
        public int LastFromStorey { get; private set; } = -1;

        /// <summary>
        /// Whether the last sting produced an actual clip. False means the cue resolved to
        /// an empty bank — the moment was drawn in silence.
        /// </summary>
        public bool LastStingPlayed { get; private set; }

        /// <summary>
        /// True when there is a readout to watch. If this is false during a match, nothing
        /// will ever fire and the defect is back.
        /// </summary>
        public bool IsWatching
        {
            get { return Resolve() != null; }
        }

        /// <summary>The curtain this drives, building one if the scene has none.</summary>
        public CaughtScreen Screen
        {
            get
            {
                var found = screen;
                if (found == null)
                {
                    found = FindFirstObjectByType<CaughtScreen>();
                }

                if (found == null)
                {
                    var go = new GameObject("CaughtScreen");
                    go.transform.SetParent(transform, false);
                    found = go.AddComponent<CaughtScreen>();
                }

                screen = found;
                return found;
            }
        }

        /// <summary>
        /// Points this at the race and at the mix.
        /// <para>
        /// Both arguments are optional, and both have a fallback that is deliberately
        /// narrow rather than clever: the readout falls back to whatever
        /// <see cref="RaceHud"/> is already drawing, and the cue player to
        /// <c>MatchAudioRig.Cues</c>. Neither fallback invents a source — if the HUD is
        /// unbound there is no race, and this stays quiet rather than guessing at one.
        /// </para>
        /// </summary>
        /// <param name="race">The standings to watch. Null falls back to the HUD's.</param>
        /// <param name="cues">Where the sting is raised. Null falls back to the audio rig's.</param>
        public void Bind(IRaceReadout? race, AudioCuePlayer? cues)
        {
            _race = race;
            _cues = cues;

            // Re-arm rather than reset to the current count: a fresh bind is a fresh
            // match, and the next look adopts whatever it finds.
            _seenTimesCaught = -1;
            _lastStorey = 0;
        }

        /// <summary>Stops watching and takes any curtain down.</summary>
        public void Unbind()
        {
            _race = null;
            _seenTimesCaught = -1;

            if (screen != null)
            {
                screen.Clear();
            }
        }

        /// <summary>
        /// Draws the moment. Public so a test can put a known catch on the screen without
        /// running a match, and so a call site that already holds both numbers can skip
        /// the watch.
        /// </summary>
        /// <param name="fromStorey">The storey that was lost, 0-based.</param>
        /// <param name="timesCaught">How many times this runner has now been caught.</param>
        public void Play(int fromStorey, int timesCaught)
        {
            LastFromStorey = fromStorey;
            Played++;

            // Keeps the watcher from drawing this same catch a second time when a call
            // site got here first. Max rather than assign, so an explicit Play cannot wind
            // the watch backwards and re-arm it against a count it has already seen.
            if (timesCaught > _seenTimesCaught)
            {
                _seenTimesCaught = timesCaught;
            }

            Screen.PlayCaught(fromStorey, timesCaught);

            var cues = _cues;
            if (cues == null)
            {
                var rig = FindFirstObjectByType<MatchAudioRig>();
                cues = rig != null ? rig.Cues : null;
                _cues = cues;
            }

            LastStingPlayed = cues != null && cues.Play(Sting);

            Debug.Log(
                "[Match] §06 잡힘 연출 — B" + (fromStorey + 1) + " → B1 · " + timesCaught
                + "회 · 암전 " + CaughtScreen.TotalSeconds.ToString("0.00") + "초"
                + (LastStingPlayed ? " · 스팅 재생" : " · 스팅 없음"), this);

            if (!LastStingPlayed)
            {
                // Not a Debug.Log. A silent catch is the defect this component exists to
                // close, and it can come back by the cue's bank being empty rather than by
                // anything here being wrong — so it says so at a level somebody reads.
                Debug.LogWarning(
                    "[Match] 잡힘 스팅이 소리를 내지 못했다 — " + Sting
                    + " 큐가 빈 뱅크로 해결됐거나 AudioCuePlayer가 없다. "
                    + "▸ Audio ▸ Rebuild Audio Libraries 를 다시 돌려야 한다.", this);
            }
        }

        /// <summary>
        /// Watches <c>TimesCaught</c>, and remembers the storey it will need when it moves.
        /// <para>
        /// <c>LateUpdate</c>, so the reading is the one the HUD drew this frame rather
        /// than one from before the fixed step that produced it.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            var race = Resolve();
            if (race == null || race.LocalRacerId < 0)
            {
                _seenTimesCaught = -1;
                return;
            }

            var local = race.LocalRacer;

            if (_seenTimesCaught < 0)
            {
                _seenTimesCaught = local.TimesCaught;
                _lastStorey = local.Storey;
                return;
            }

            if (local.TimesCaught > _seenTimesCaught)
            {
                // _lastStorey, not local.Storey — ReportCaught has already set the latter
                // to 0. See the class remarks.
                Play(_lastStorey, local.TimesCaught);
                _seenTimesCaught = local.TimesCaught;
            }

            _lastStorey = local.Storey;
        }

        /// <summary>The readout in use: the bound one, or whatever the HUD is drawing.</summary>
        private IRaceReadout? Resolve()
        {
            if (_race != null)
            {
                return _race;
            }

            if (_hud == null && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + SearchInterval;
                _hud = FindFirstObjectByType<RaceHud>();
            }

            return _hud != null ? _hud.Race : null;
        }
    }
}
