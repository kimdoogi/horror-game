#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using HorrorGame.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.UI.Settings
{
    /// <summary>
    /// Key rebinding, mouse sensitivity and invert-Y, all expressed as overrides on the
    /// shipped <see cref="InputActionAsset"/>.
    /// <para>
    /// <b>Why the asset and not the player rig.</b> §05's scheme already lives in
    /// <c>PlayerControls.inputactions</c>, and <c>PlayerInputRouter</c> is explicit that
    /// it is "the only place in the game that knows a key is involved". It clones that
    /// asset in <c>Awake</c> — deliberately, so a host and a client in one editor
    /// session do not share action state.
    /// </para>
    /// <para>
    /// <b>That clone is the whole problem, and it is why this class listens.</b>
    /// Binding overrides are runtime state on the asset object and
    /// <c>Object.Instantiate</c> does <em>not</em> copy them — measured, and pinned by
    /// <c>UiTests</c>. Writing a rebind onto the shipped asset therefore reaches the
    /// settings screen, which reads that asset, and reaches the player's hands not at
    /// all: the screen shows the new key and the game keeps the old one, with nothing
    /// logged. So the overrides are re-applied to any copy of the scheme at the moment
    /// it is enabled, through <see cref="InputSystem.onActionChange"/>. The player rig
    /// is not touched and does not have to know.
    /// </para>
    /// <para>
    /// <b>Sensitivity and invert-Y are processor overrides on the same asset</b> rather
    /// than fields poked into the rig. §05 makes 마우스 방향 = 이동 방향, so the look
    /// axis is the one input every other input is relative to, and scaling it at the
    /// binding means the value survives the same clone, saves in the same blob and
    /// composes with a rebind instead of racing it. <c>PlayerInputRouter</c> multiplies
    /// by its own authored degrees-per-count afterwards, which is why this is a
    /// multiplier and not an absolute.
    /// </para>
    /// <para>
    /// <b>What is not rebindable.</b> The mouse look axis and the gamepad sticks. A
    /// player who binds "look" to a key has a game that cannot be played, and §05's
    /// 45° peek — the skill the whole chase is built on — is an analogue rotation
    /// ("이산적 선택이 아니라 아날로그 조절"). Everything on the keyboard scheme is
    /// rebindable, because that is where the actual conflicts are.
    /// </para>
    /// </summary>
    public static class InputBindings
    {
        /// <summary>The action map §05's scheme lives in.</summary>
        public const string PlayerMap = "Player";

        /// <summary>The look action carrying the mouse delta. Not rebindable; scaled instead.</summary>
        public const string LookAction = "Look";

        /// <summary>Binding group for the keyboard scheme, as authored in the asset.</summary>
        public const string KeyboardScheme = "Keyboard&Mouse";

        private static InputActionAsset? _asset;
        private static string _overridesJson = string.Empty;
        private static float _sensitivity = SettingsLimits.MouseSensitivityDefault;
        private static bool _invertY;
        private static bool _listening;
        private static bool _applying;

        /// <summary>
        /// The shipped asset — the same object <c>PlayerInputRouter</c> loads. Null only
        /// when the Resources copy is missing, which is a broken build rather than a
        /// state to recover from.
        /// </summary>
        public static InputActionAsset? Asset
        {
            get
            {
                if (_asset == null)
                {
                    _asset = Resources.Load<InputActionAsset>(PlayerInputRouter.DefaultAssetResourcePath);
                }

                return _asset;
            }
        }

        /// <summary>One rebindable control, as the settings screen needs to draw it.</summary>
        public readonly struct Entry
        {
            /// <summary>Action this binding belongs to.</summary>
            public readonly InputAction Action;

            /// <summary>Index of the binding inside <see cref="Action"/>'s own binding list.</summary>
            public readonly int BindingIndex;

            /// <summary>Korean label, from §05's table.</summary>
            public readonly string Label;

            /// <summary>Builds one entry.</summary>
            public Entry(InputAction action, int bindingIndex, string label)
            {
                Action = action;
                BindingIndex = bindingIndex;
                Label = label;
            }

            /// <summary>The key currently bound, as a person would say it. "W", "Left Shift".</summary>
            public string KeyText
            {
                get
                {
                    var path = Action.bindings[BindingIndex].effectivePath;
                    if (string.IsNullOrEmpty(path))
                    {
                        return "—";
                    }

                    return InputControlPath.ToHumanReadableString(
                        path, InputControlPath.HumanReadableStringOptions.OmitDevice);
                }
            }
        }

        /// <summary>
        /// Every keyboard binding a player may move, in §05's reading order.
        /// Empty when the asset is missing — the screen then draws nothing rather than
        /// a row that cannot work.
        /// </summary>
        public static IReadOnlyList<Entry> Rebindable()
        {
            var entries = new List<Entry>();
            var map = Asset != null ? Asset.FindActionMap(PlayerMap, false) : null;
            if (map == null)
            {
                return entries;
            }

            foreach (var action in map.actions)
            {
                if (string.Equals(action.name, LookAction, StringComparison.Ordinal))
                {
                    continue;
                }

                for (var i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (binding.isComposite)
                    {
                        continue;
                    }

                    if (!binding.groups.Contains(KeyboardScheme))
                    {
                        continue;
                    }

                    entries.Add(new Entry(action, i, Label(action.name, binding.name)));
                }
            }

            return entries;
        }

        /// <summary>
        /// Applies the saved overrides, then the look scaling, in that order.
        /// <para>
        /// Order matters and is not interchangeable. The scaling is itself a binding
        /// override, so it lives in the same saved blob — loading the blob restores
        /// whatever scaling was in force when it was written, and re-applying the
        /// current settings afterwards is what makes the live value win.
        /// </para>
        /// </summary>
        public static void Apply(GameSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _overridesJson = settings.BindingOverridesJson;
            _sensitivity = settings.MouseSensitivity;
            _invertY = settings.InvertLookY;

            Listen();
            ApplyTo(Asset);
            ApplyToLiveCopies();
        }

        /// <summary>
        /// Scales the look axis on every live copy of the scheme.
        /// <paramref name="sensitivity"/> multiplies the rig's authored
        /// degrees-per-count; <paramref name="invertY"/> flips pitch only.
        /// </summary>
        public static void ApplyLookScaling(float sensitivity, bool invertY)
        {
            _sensitivity = sensitivity;
            _invertY = invertY;

            Listen();
            ScaleLook(Asset);
            ApplyToLiveCopies();
        }

        /// <summary>
        /// Subscribes to the Input System's own change feed, once.
        /// <para>
        /// The alternative — polling every loaded <see cref="InputActionAsset"/> each
        /// scene load — would still miss a rig spawned mid-match, which is exactly what
        /// §13's host does when a client joins.
        /// </para>
        /// </summary>
        private static void Listen()
        {
            if (_listening)
            {
                return;
            }

            _listening = true;
            InputSystem.onActionChange += OnActionChange;
        }

        private static void OnActionChange(object subject, InputActionChange change)
        {
            if (_applying)
            {
                return;
            }

            if (change != InputActionChange.ActionMapEnabled && change != InputActionChange.ActionEnabled)
            {
                return;
            }

            var asset = AssetOf(subject);
            if (asset == null || ReferenceEquals(asset, Asset) || !IsPlayerScheme(asset))
            {
                return;
            }

            ApplyTo(asset);
        }

        /// <summary>Writes the saved rebinds and the current look scaling onto one asset.</summary>
        private static void ApplyTo(InputActionAsset? asset)
        {
            if (asset == null)
            {
                return;
            }

            _applying = true;
            try
            {
                if (!string.IsNullOrEmpty(_overridesJson))
                {
                    asset.LoadBindingOverridesFromJson(_overridesJson);
                }

                ScaleLook(asset);
            }
            catch (Exception error)
            {
                Debug.LogWarning(
                    "[Settings] Saved key bindings could not be restored (" + error.Message
                    + "); §05's defaults are in force.");
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>
        /// Every copy of the scheme currently in memory, including the clone
        /// <c>PlayerInputRouter</c> made. Used when the settings change while a match is
        /// already running — the change feed only fires on enable.
        /// </summary>
        private static void ApplyToLiveCopies()
        {
            var all = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            for (var i = 0; i < all.Length; i++)
            {
                if (ReferenceEquals(all[i], Asset) || !IsPlayerScheme(all[i]))
                {
                    continue;
                }

                ApplyTo(all[i]);
            }
        }

        private static void ScaleLook(InputActionAsset? asset)
        {
            var map = asset != null ? asset.FindActionMap(PlayerMap, false) : null;
            var look = map != null ? map.FindAction(LookAction, false) : null;
            if (look == null)
            {
                return;
            }

            var scale = Mathf.Clamp(
                _sensitivity, SettingsLimits.MouseSensitivityMin, SettingsLimits.MouseSensitivityMax);

            // A player on the shipped defaults gets no override at all rather than a
            // ×1.00 one. Two reasons: SaveBindingOverridesAsJson would otherwise write a
            // no-op into every settings file ever created, and this runs against the
            // asset in Resources at editor start-up — leaving a stray override on a
            // source asset is how a scheme gets a preference committed into it.
            var identity = Mathf.Approximately(scale, SettingsLimits.MouseSensitivityDefault) && !_invertY;
            var processors = "scaleVector2(x=" + Format(scale) + ",y=" + Format(_invertY ? -scale : scale) + ")";

            for (var i = 0; i < look.bindings.Count; i++)
            {
                if (!look.bindings[i].groups.Contains(KeyboardScheme))
                {
                    continue;
                }

                if (identity)
                {
                    // Safe to remove outright: the look axis is not rebindable, so the
                    // only override this binding can be carrying is one of ours.
                    look.RemoveBindingOverride(i);
                    continue;
                }

                // The path is carried through deliberately: ApplyBindingOverride clears
                // any field the override leaves null, and the look binding must keep
                // pointing at the mouse delta.
                var existing = look.bindings[i];
                look.ApplyBindingOverride(i, new InputBinding
                {
                    overridePath = existing.effectivePath,
                    overrideProcessors = processors,
                });
            }
        }

        private static InputActionAsset? AssetOf(object subject)
        {
            if (subject is InputActionAsset asset)
            {
                return asset;
            }

            if (subject is InputActionMap map)
            {
                return map.asset;
            }

            if (subject is InputAction action)
            {
                return action.actionMap != null ? action.actionMap.asset : null;
            }

            return null;
        }

        /// <summary>
        /// Whether an asset is §05's scheme. Matched on the action map rather than on the
        /// object's name: <c>Instantiate</c> appends "(Clone)" unless the caller renames
        /// it, and relying on a caller in another layer to keep doing that is how this
        /// silently stops working.
        /// </summary>
        private static bool IsPlayerScheme(InputActionAsset? asset)
        {
            if (asset == null)
            {
                return false;
            }

            var map = asset.FindActionMap(PlayerMap, false);
            return map != null && map.FindAction(LookAction, false) != null;
        }

        /// <summary>
        /// Starts an interactive rebind and calls <paramref name="onDone"/> when the
        /// player presses something or cancels.
        /// <para>
        /// The action is disabled for the duration, which is what stops the keypress
        /// that finishes the rebind from also firing the action underneath it. Mouse
        /// position and the escape key are excluded: escape is how a player backs out
        /// of a rebind they opened by mistake, and binding movement to the pointer would
        /// leave a rig that walks whenever the mouse does.
        /// </para>
        /// </summary>
        /// <returns>The running operation, so a screen can cancel it; null when the entry is unusable.</returns>
        public static InputActionRebindingExtensions.RebindingOperation? StartRebind(Entry entry, Action<bool> onDone)
        {
            var action = entry.Action;
            if (action == null)
            {
                onDone?.Invoke(false);
                return null;
            }

            var wasEnabled = action.enabled;
            action.Disable();

            var operation = action.PerformInteractiveRebinding(entry.BindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(op =>
                {
                    op.Dispose();
                    if (wasEnabled)
                    {
                        action.Enable();
                    }

                    onDone?.Invoke(false);
                })
                .OnComplete(op =>
                {
                    op.Dispose();
                    if (wasEnabled)
                    {
                        action.Enable();
                    }

                    onDone?.Invoke(true);
                });

            operation.Start();
            return operation;
        }

        /// <summary>
        /// Every override currently in force, as the Input System's own blob. Stored
        /// verbatim in the settings file — parsing it here would make a package upgrade
        /// a settings migration.
        /// </summary>
        public static string CurrentOverridesJson()
        {
            var asset = Asset;
            return asset != null ? asset.SaveBindingOverridesAsJson() : string.Empty;
        }

        /// <summary>
        /// Puts §05's authored keys back — on the shipped asset and on every live copy,
        /// because a reset that only reached the settings screen would leave the player's
        /// hands on the bindings they just cleared.
        /// </summary>
        public static void ResetToDefaults()
        {
            _overridesJson = string.Empty;

            Asset?.RemoveAllBindingOverrides();

            var all = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            for (var i = 0; i < all.Length; i++)
            {
                if (IsPlayerScheme(all[i]))
                {
                    all[i].RemoveAllBindingOverrides();
                }
            }
        }

        /// <summary>Whether any binding differs from what shipped.</summary>
        public static bool HasAnyOverride()
        {
            return !string.IsNullOrEmpty(CurrentOverridesJson());
        }

        /// <summary>
        /// §05's own words for each control. The composite parts are named for the
        /// direction rather than for the key, because the key is the thing about to
        /// change.
        /// </summary>
        private static string Label(string actionName, string partName)
        {
            if (string.Equals(actionName, "Move", StringComparison.Ordinal))
            {
                switch (partName)
                {
                    case "up": return "전진";
                    case "down": return "후진  (§05 65% — 뒤를 보면 잡힌다)";
                    case "left": return "좌 스트레이프";
                    case "right": return "우 스트레이프";
                    default: return "이동";
                }
            }

            if (string.Equals(actionName, "Sprint", StringComparison.Ordinal))
            {
                return "달리기 / 질주";
            }

            if (string.Equals(actionName, "Flashlight", StringComparison.Ordinal))
            {
                return "손전등";
            }

            return string.IsNullOrEmpty(partName) ? actionName : actionName + " · " + partName;
        }

        /// <summary>Invariant formatting: a processor string parsed with a Korean locale must not read "1,50".</summary>
        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
