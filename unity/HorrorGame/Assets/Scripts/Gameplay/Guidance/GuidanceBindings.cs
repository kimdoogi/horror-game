#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using HorrorGame.Gameplay.Interaction;
using HorrorGame.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorGame.Gameplay.Guidance
{
    /// <summary>One row of the controls card: what it does, and the key that does it.</summary>
    public readonly struct ControlLine
    {
        /// <summary>What the binding is for, in the design document's language.</summary>
        public readonly string Label;

        /// <summary>The key or keys, as the Input System renders them.</summary>
        public readonly string Keys;

        /// <summary>Builds a row.</summary>
        public ControlLine(string label, string keys)
        {
            Label = label;
            Keys = keys;
        }
    }

    /// <summary>
    /// The controls card's contents, read out of the bindings that are actually live.
    /// <para>
    /// <b>Nothing here is typed out by hand.</b> §05's scheme is an
    /// <c>InputActionAsset</c> — the same one <c>PlayerInputRouter</c> clones at
    /// <c>Awake</c> from <see cref="PlayerInputRouter.DefaultAssetResourcePath"/> — and a
    /// card that restated it would be a second copy that drifts silently the first time
    /// somebody rebinds a key. A tester who is told the wrong key concludes the game is
    /// broken, which is the exact failure this whole guidance layer exists to prevent.
    /// So every row below is produced from a binding, and the one key §05 never
    /// documented (the interact key) is read off the component that owns it.
    /// </para>
    /// </summary>
    public static class GuidanceBindings
    {
        /// <summary>The control scheme a solo tester at a keyboard is using.</summary>
        private const string KeyboardScheme = "Keyboard&Mouse";

        private const string PlayerMap = "Player";

        /// <summary>
        /// Every keyboard-and-mouse binding in §05's scheme, plus the interact key.
        /// Gamepad-only actions are dropped rather than shown empty — §14's tester is at
        /// a mouse, and <c>LookStick</c> exists only so a pad does not integrate a delta.
        /// </summary>
        public static IReadOnlyList<ControlLine> Read()
        {
            var lines = new List<ControlLine>();
            var asset = Resources.Load<InputActionAsset>(PlayerInputRouter.DefaultAssetResourcePath);

            if (asset == null)
            {
                lines.Add(new ControlLine("조작 정의", "없음 — Resources/"
                    + PlayerInputRouter.DefaultAssetResourcePath + " 를 찾지 못했습니다"));
                return lines;
            }

            var map = asset.FindActionMap(PlayerMap, false);
            if (map == null)
            {
                lines.Add(new ControlLine("조작 정의", "'" + PlayerMap + "' 액션 맵이 없습니다"));
                return lines;
            }

            foreach (var action in map.actions)
            {
                var keys = KeysOf(action);
                if (keys.Length == 0)
                {
                    continue;
                }

                lines.Add(new ControlLine(LabelOf(action.name), keys));
            }

            lines.Add(new ControlLine("상호작용 · 줍기 · 상점", InteractKeyName()));
            return lines;
        }

        /// <summary>
        /// Joins one action's keyboard-and-mouse bindings, composite parts included and in
        /// the order the asset declares them — which for §05's Move is up, down, left,
        /// right, so the card reads W S A D exactly as the asset does.
        /// </summary>
        private static string KeysOf(InputAction action)
        {
            var text = new StringBuilder();

            for (var i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];

                // The composite itself carries no path — only its parts do.
                if (binding.isComposite)
                {
                    continue;
                }

                if (!IsKeyboardOrMouse(binding))
                {
                    continue;
                }

                var name = InputControlPath.ToHumanReadableString(
                    binding.effectivePath, InputControlPath.HumanReadableStringOptions.OmitDevice);

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // The device is dropped for keys, because "W [Keyboard] S [Keyboard]" is
                // four words of noise, and kept for everything else, because §05's Look
                // binding renders as the bare word "Delta" and a tester reading that
                // learns nothing. Both come out of the binding path either way.
                var device = InputControlPath.TryGetDeviceLayout(binding.effectivePath);
                if (!string.IsNullOrEmpty(device)
                    && !string.Equals(device, "Keyboard", StringComparison.Ordinal))
                {
                    name = device + " " + name;
                }

                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(name);
            }

            return text.ToString();
        }

        /// <summary>
        /// Whether a binding belongs to the scheme a person at a keyboard is using. An
        /// ungrouped binding counts: a scheme-less asset is still the asset in force.
        /// </summary>
        private static bool IsKeyboardOrMouse(InputBinding binding)
        {
            return string.IsNullOrEmpty(binding.groups)
                || binding.groups.Contains(KeyboardScheme);
        }

        /// <summary>
        /// §05's own words for each action. An action the asset gains later falls through
        /// to its raw name, so a new binding appears on the card unlabelled rather than
        /// not at all.
        /// </summary>
        private static string LabelOf(string actionName)
        {
            switch (actionName)
            {
                case "Move": return "이동 (시점 기준)";
                case "Look": return "시점 — 이것이 \"앞\"을 정합니다";
                case "Sprint": return "달리기 · 주자는 질주";
                case "Flashlight": return "손전등 on/off";
                default: return actionName;
            }
        }

        /// <summary>
        /// The interact key, read off <c>PlayerInteractor</c> rather than repeated.
        /// <para>
        /// §05 names 마우스 · WASD · Shift · F and nothing else, so the interact key is a
        /// constant in the component that binds it — and it is the key a tester needs
        /// most, because §03's objective, §08's loot and §08's shop all go through it.
        /// This used to reach for it with reflection because it was private; it is now
        /// public, so the compiler holds the two ends together and a rename cannot
        /// silently turn this line into "읽지 못함".
        /// </para>
        /// </summary>
        private static string InteractKeyName()
        {
            return PlayerInteractor.InteractKeyLabel;
        }
    }
}
