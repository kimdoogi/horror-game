#nullable enable

using System.Collections.Generic;
using HorrorGame.Core.Economy;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// Which generated model stands for which §-object, and nothing else.
    /// <para>
    /// <b>§08's sizes are gameplay.</b> The table gives four rows and the weights 1, 1,
    /// 2 and 5, and the two 5s exist "2인 운반 or 극심한 감속" — the section says outright
    /// that the 궤짝 exists to create the scene where two people carry it down a narrow
    /// corridor. A player has to be able to price that decision <em>before</em> lifting,
    /// and the only channel that carries it before the prompt is read is how big the
    /// thing looks. On identical cubes the whole read is gone. So the 궤짝 is 1.28 m
    /// across and the 반지 is 2.2 cm, as <c>gen_props.py</c> built them.
    /// </para>
    /// <para>
    /// <b>Two models per row, chosen from the piece's own position.</b> §08 names each
    /// row as a pair — 은수저 · 잡동사니, 회중시계 · 반지, 대형 초상화 · 궤짝 — so both
    /// exist and the pick is made from the position rather than from a random source.
    /// §13 replays a match from its seed; the layout is drawn from that seed, so a
    /// function of the position is a function of the seed and the replay is unaffected.
    /// A <c>Random.Range</c> here would have broken it silently.
    /// </para>
    /// </summary>
    public static class PropModels
    {
        /// <summary>§03's mark. The lectern rather than the wall board: no clue marker carries a wall normal.</summary>
        public const string Clue = "Clue_LedgerStand";

        /// <summary>§03's 목표물.</summary>
        public const string Objective = "Objective";

        /// <summary>§08's 금고, shut.</summary>
        public const string SafeClosed = "Safe_Closed";

        /// <summary>§08's 금고 once §04's 정비공 has drilled it.</summary>
        public const string SafeOpen = "Safe_Open";

        /// <summary>§08's 지상 차량 — the shop, the wallet and the safe zone.</summary>
        public const string Vehicle = "Vehicle";

        /// <summary>§08's 문서, which is what a drilled safe hands over.</summary>
        public const string SafeDocument = "Loot_SafeDocument";

        /// <summary>Every key an interactable can ask for. The build step validates against this.</summary>
        public static IReadOnlyList<string> Required
        {
            get { return RequiredKeys; }
        }

        /// <summary>The model for one §08 row, picked from where the piece lies.</summary>
        public static string ForLoot(LootId loot, Vector3 at)
        {
            switch (loot)
            {
                case LootId.Trinket:
                    return Pick(at, TrinketModels);
                case LootId.Timepiece:
                    return Pick(at, TimepieceModels);
                case LootId.SafeDocument:
                    return SafeDocument;
                case LootId.LargePiece:
                    return Pick(at, LargePieceModels);
                default:
                    return Pick(at, TrinketModels);
            }
        }

        /// <summary>
        /// A stable yaw for a piece, so a corridor of loot is not a parade of objects
        /// all facing the same way. Same input, same answer, so §13's replay holds.
        /// </summary>
        public static Quaternion YawFor(Vector3 at)
        {
            return Quaternion.Euler(0f, Hash(at) % 360, 0f);
        }

        private static string Pick(Vector3 at, string[] options)
        {
            return options[(int)(Hash(at) % (uint)options.Length)];
        }

        /// <summary>
        /// A deterministic hash of a world position. Quantised to a centimetre first:
        /// the raw bits of a float are stable within one run but a position that came
        /// back from a NavMesh sample on a different platform can differ in the last
        /// bit, and that would swap a model between two replays of the same seed.
        /// </summary>
        private static uint Hash(Vector3 at)
        {
            unchecked
            {
                var x = (uint)Mathf.RoundToInt(at.x * 100f);
                var y = (uint)Mathf.RoundToInt(at.y * 100f);
                var z = (uint)Mathf.RoundToInt(at.z * 100f);
                var h = 2166136261u;
                h = (h ^ x) * 16777619u;
                h = (h ^ y) * 16777619u;
                h = (h ^ z) * 16777619u;
                return h;
            }
        }

        // §08's row names are pairs; both halves are modelled.
        private static readonly string[] TrinketModels =
        {
            "Loot_Trinket_SilverSpoons", "Loot_Trinket_Junk",
        };

        private static readonly string[] TimepieceModels =
        {
            "Loot_Timepiece_PocketWatch", "Loot_Timepiece_Ring",
        };

        private static readonly string[] LargePieceModels =
        {
            "Loot_LargePiece_Chest", "Loot_LargePiece_Portrait",
        };

        private static readonly string[] RequiredKeys =
        {
            "Loot_Trinket_SilverSpoons",
            "Loot_Trinket_Junk",
            "Loot_Timepiece_PocketWatch",
            "Loot_Timepiece_Ring",
            "Loot_SafeDocument",
            "Loot_LargePiece_Chest",
            "Loot_LargePiece_Portrait",
            Clue,
            Objective,
            SafeClosed,
            SafeOpen,
            Vehicle,
        };
    }
}
