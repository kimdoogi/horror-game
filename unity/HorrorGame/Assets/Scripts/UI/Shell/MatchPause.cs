#nullable enable

using HorrorGame.Gameplay.Player;
using UnityEngine;

namespace HorrorGame.UI.Shell
{
    /// <summary>
    /// Stops the match, for real.
    /// <para>
    /// <b>§07's clock is the game's currency and it must actually stop.</b> The section
    /// makes elapsed time the thing every dilemma is priced in — the threat tier, the
    /// monster's speed, whether §10's "지금 나갈까?" is a question worth asking — and
    /// §03 is explicit that surfacing does not reset it: "나가는 것은 숨 돌리기이지
    /// 리셋이 아니다." A pause menu that leaves it running charges a player for the time
    /// they spent reading their own key bindings, and it is the kind of bug a player
    /// notices in one session and never forgives.
    /// </para>
    /// <para>
    /// <b>How it stops.</b> <c>Time.timeScale = 0</c>, which is enough because
    /// <c>MatchDirector</c> steps the whole match from <c>FixedUpdate</c> at
    /// <c>GameConstants.FixedStep</c> and Unity does not run <c>FixedUpdate</c> at zero
    /// scale. One switch stops the clock, the monster, the clue read and the battery
    /// together, and — importantly — stops them at the same instant, which a per-system
    /// pause flag would not guarantee.
    /// </para>
    /// <para>
    /// <b>Three things do not stop, deliberately.</b> The player's input router runs on
    /// <c>Update</c> and would keep reading the mouse, so it is suppressed rather than
    /// scaled; the mix runs on <c>Time.unscaledDeltaTime</c> and would keep the world
    /// audible behind a paused game, so the listener is paused; and the cursor comes
    /// back, because a pause menu you cannot click is not a pause menu.
    /// </para>
    /// <para>
    /// <b>This is single-player only and says so.</b> §13 gives the host authority and
    /// there is no host migration; when Mirror is carrying a real session, one client
    /// stopping its own <c>timeScale</c> would desynchronise it from a match that is
    /// still running. The menu that opens this is therefore the solo one, and the
    /// networked build will need a host-side vote instead — the same shape §02's
    /// "leave for good" already has.
    /// </para>
    /// </summary>
    public static class MatchPause
    {
        private static float _restoreTimeScale = 1f;
        private static bool _restoreCursorLocked;
        private static bool _paused;

        /// <summary>Whether the match is currently stopped.</summary>
        public static bool IsPaused
        {
            get { return _paused; }
        }

        /// <summary>
        /// Stops time, silences the world, frees the mouse. Idempotent.
        /// </summary>
        public static void Pause()
        {
            if (_paused)
            {
                return;
            }

            _paused = true;
            _restoreTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            _restoreCursorLocked = Cursor.lockState == CursorLockMode.Locked;

            Time.timeScale = 0f;
            AudioListener.pause = true;

            SetPlayerInput(suppressed: true, cursorLocked: false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Starts the match again exactly where it stopped. Idempotent.</summary>
        public static void Resume()
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            Time.timeScale = _restoreTimeScale;
            AudioListener.pause = false;

            SetPlayerInput(suppressed: false, cursorLocked: _restoreCursorLocked);

            Cursor.lockState = _restoreCursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_restoreCursorLocked;
        }

        /// <summary>
        /// Drops the paused state without restoring the match — for leaving to the menu,
        /// where time must run again but the player rig is about to be unloaded.
        /// </summary>
        public static void Clear()
        {
            _paused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// Suppresses or restores §05's input. Uses the router's own public switches, so
        /// nothing here reaches into the player layer's private state.
        /// </summary>
        private static void SetPlayerInput(bool suppressed, bool cursorLocked)
        {
            var routers = Object.FindObjectsByType<PlayerInputRouter>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (var i = 0; i < routers.Length; i++)
            {
                routers[i].InputSuppressed = suppressed;
                routers[i].LockCursor = cursorLocked;
            }

            var looks = Object.FindObjectsByType<PlayerLook>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (var i = 0; i < looks.Length; i++)
            {
                looks[i].LookLocked = suppressed;
            }
        }
    }
}
