#nullable enable

using System;
using System.Collections.Generic;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Light;
using HorrorGame.Core.Match;
using HorrorGame.Core.Movement;
using HorrorGame.Core.Roles;
using HorrorGame.UI;
using HorrorGame.UI.Screens;
using UnityEngine;

namespace HorrorGame.Gameplay.Match
{
    /// <summary>
    /// Puts the finished screens on the screen. §03 · §07 · §08 · §02.
    /// <para>
    /// <b>Nothing is drawn here.</b> <c>HudScreen</c>, <c>ClueReadingOverlay</c>,
    /// <c>ShopScreen</c> and <c>EndScreen</c> already exist and already know what §01's
    /// horror game is allowed to show; this class instantiates them and points them at
    /// the core objects the match is stepping. Adding a widget here would put a second,
    /// disagreeing HUD in the project.
    /// </para>
    /// <para>
    /// <b>Two absences are the design, not an omission.</b> §07 makes the hour
    /// something you walk outside for or buy a 회중시계 to learn, and <c>HudScreen</c>
    /// enforces that by reading <c>MatchClock.IsTimeReadable</c> — so this class hands
    /// it the clock rather than a formatted time, and there is no path here that could
    /// leak the hour. And there is no clue log: §03 forbids carrying a clue out, so the
    /// only screen that ever shows a mark is the overlay, which holds one value and
    /// overwrites it. Neither absence can be fixed by wiring; both would need a new
    /// screen, which is the point.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchHud : MonoBehaviour
    {
        private HudScreen? _hud;
        private ClueReadingOverlay? _clue;
        private ShopScreen? _shop;
        private EndScreen? _end;

        /// <summary>The §01 HUD.</summary>
        public HudScreen? Hud
        {
            get { return _hud; }
        }

        /// <summary>§03's reading overlay — the only place a mark is ever drawn.</summary>
        public ClueReadingOverlay? Clue
        {
            get { return _clue; }
        }

        /// <summary>§08's shop.</summary>
        public ShopScreen? Shop
        {
            get { return _shop; }
        }

        /// <summary>§02's verdict.</summary>
        public EndScreen? End
        {
            get { return _end; }
        }

        /// <summary>Whether §08's shop is up.</summary>
        public bool ShopOpen
        {
            get { return _shop != null && _shop.IsVisible; }
        }

        /// <summary>Builds the four screens, once.</summary>
        public void EnsureScreens()
        {
            EnsureEventSystem();
            _hud = Attach(_hud, "Hud");
            _clue = Attach(_clue, "ClueReading");
            _shop = Attach(_shop, "Shop");
            _end = Attach(_end, "End");
        }

        /// <summary>
        /// Gives §08's shop and §02's end screen something to route a click through.
        /// <para>
        /// <c>UiFactory.EnsureEventSystem</c> deliberately does nothing under the new
        /// input backend — which module reads the mouse depends on how the project is
        /// configured, and that is a gameplay-layer decision rather than a UI-layer one
        /// (the UI assembly does not reference the input package). §05 puts the project
        /// on the new backend, so this is where the module comes from.
        /// </para>
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (existing != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        /// <summary>
        /// Points the HUD at the match's own objects, so it pulls rather than being
        /// pushed. Any of them may be null — an objective carrier has no flashlight,
        /// and only §04's 주자 has a stamina bar.
        /// </summary>
        public void BindHud(
            RoleId role,
            MatchClock? clock,
            Inventory? inventory,
            FlashlightState? flashlight,
            StaminaState? stamina)
        {
            EnsureScreens();
            _hud?.Bind(role, clock, inventory, flashlight, stamina);
        }

        /// <summary>Stops the HUD pulling. Used when the match ends.</summary>
        public void UnbindHud()
        {
            _hud?.Unbind();
        }

        /// <summary>
        /// Draws §03's beam-holding progress. The layer is passed as
        /// <see cref="ClueLayer.None"/> on purpose: which link of the chain a mark is
        /// belongs to the host until the read finishes (ARCHITECTURE §4), and telling
        /// the reader up front which one they are grinding through would let them skip
        /// the ones they already have.
        /// </summary>
        public void ShowClueProgress(ClueReader reader)
        {
            EnsureScreens();
            _clue?.ShowProgress(reader, ClueLayer.None);
        }

        /// <summary>Draws the mark the host resolved. It is not stored anywhere.</summary>
        public void ShowClueRevealed(ClueReport report)
        {
            EnsureScreens();
            _clue?.ShowRevealed(report);
        }

        /// <summary>Clears the overlay. §03: nothing survives looking away.</summary>
        public void DismissClue()
        {
            _clue?.Dismiss();
        }

        /// <summary>Opens §08's shop over the one team wallet.</summary>
        public void OpenShop(Shop shop, IShopRequests requests, IReadOnlyList<RoleId>? roster, RoleId missingRole)
        {
            EnsureScreens();
            _shop?.Open(shop, requests, roster, missingRole);
        }

        /// <summary>Redraws the shop after anything is bought or sold.</summary>
        public void RefreshShop()
        {
            if (_shop != null && _shop.IsVisible)
            {
                _shop.Refresh();
            }
        }

        /// <summary>Closes the shop — the team is going back down.</summary>
        public void CloseShop()
        {
            _shop?.Close();
        }

        /// <summary>Shows §02's verdict.</summary>
        public void ShowEnd(MatchResolution resolution, Action? onContinue)
        {
            EnsureScreens();
            _end?.Show(resolution, onContinue);
        }

        /// <summary>Hides the end screen.</summary>
        public void HideEnd()
        {
            _end?.Hide();
        }

        private T Attach<T>(T? existing, string childName)
            where T : UiScreen
        {
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(childName);
            child.transform.SetParent(transform, worldPositionStays: false);
            return child.AddComponent<T>();
        }
    }
}
