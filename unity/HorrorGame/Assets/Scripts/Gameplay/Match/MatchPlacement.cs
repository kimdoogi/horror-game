#nullable enable

using System.Collections.Generic;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Session;
using UnityEngine;

namespace HorrorGame.Gameplay.Match
{
    /// <summary>Where one piece of §08's 전리품 goes, and whether it is locked in a 금고.</summary>
    public readonly struct LootPlacement
    {
        /// <summary>Builds a placement.</summary>
        public LootPlacement(Vector3 position, LootId loot, bool inSafe)
        {
            Position = position;
            Loot = loot;
            InSafe = inSafe;
        }

        /// <summary>Where the piece sits.</summary>
        public Vector3 Position { get; }

        /// <summary>Which row of §08's table it is.</summary>
        public LootId Loot { get; }

        /// <summary>Whether it is behind §04's eight seconds of Engineer work (§08 금고 속 문서).</summary>
        public bool InSafe { get; }
    }

    /// <summary>
    /// Draws one match's 전리품 layout over the map's loot spawns. §08 · §03.
    /// <para>
    /// <b>Random placement, fixed table.</b> §03's randomisation table puts 전리품 배치
    /// in the random column, so which spawn holds what is drawn from the match seed;
    /// the four rows themselves are §08's and are not invented here — every weight and
    /// value comes from <c>LootCatalogue</c>.
    /// </para>
    /// <para>
    /// <b>Where the mix comes from.</b> §08 gives no drop rates, so none are made up.
    /// The mix is read off the two places the document does commit to numbers:
    /// </para>
    /// <list type="bullet">
    /// <item><description>§08's own credit derivation (quoted in <c>GameConstants</c>)
    /// values a mixed haul at "(10 + 25) / 2", which is an even split of 은수저 and
    /// 회중시계 and nothing else. So the small loot alternates one for one.</description></item>
    /// <item><description>§01's flow names the oversize piece once — "2차 잠입 → …
    /// 대형 전리품 발견 · 이거 들고 갈까? 2명 필요해" — and the 금고 문서 once, on the
    /// third descent. Both are singular, and both are set pieces: a building with four
    /// chests in it would make "2명 필요해" a chore rather than a decision.</description></item>
    /// </list>
    /// </summary>
    public static class MatchPlacement
    {
        /// <summary>
        /// Lays §08's table over the spawn points.
        /// </summary>
        /// <param name="spawns">§12's 전리품 drops — one at every 막힌 길.</param>
        /// <param name="rng">The match's seeded stream. §03: the placement varies, the map does not.</param>
        /// <returns>One placement per spawn, in spawn order.</returns>
        public static LootPlacement[] DrawLoot(IReadOnlyList<Transform> spawns, IRandomSource rng)
        {
            if (spawns == null || rng == null || spawns.Count == 0)
            {
                return System.Array.Empty<LootPlacement>();
            }

            // Which spawn gets the set pieces is the whole of §03's "막힌 길에 좋은
            // 것을 둔다" as far as this map can express it: every spawn already sits at a
            // dead end (§12), so the only thing left to vary is which one.
            var order = new int[spawns.Count];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = rng.NextInt(0, i + 1);
                var swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            var placed = new LootPlacement[spawns.Count];
            var smallLootDrawn = 0;

            for (var slot = 0; slot < order.Length; slot++)
            {
                var spawnIndex = order[slot];
                var position = spawns[spawnIndex].position;

                if (slot == 0)
                {
                    // §01's 궤짝, once.
                    placed[spawnIndex] = new LootPlacement(position, LootId.LargePiece, inSafe: false);
                    continue;
                }

                if (slot == 1 && order.Length > 2)
                {
                    // §08's 금고 속 문서, once, behind the Engineer.
                    placed[spawnIndex] = new LootPlacement(position, LootId.SafeDocument, inSafe: true);
                    continue;
                }

                // §08's mixed haul, one for one.
                var loot = smallLootDrawn % 2 == 0 ? LootId.Trinket : LootId.Timepiece;
                smallLootDrawn++;
                placed[spawnIndex] = new LootPlacement(position, loot, inSafe: false);
            }

            return placed;
        }
    }
}
