using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// One floor of the building, as the building itself describes it.
    /// <para>
    /// §12 requires candidate sites to be built into the level; §03 requires the map
    /// structure to be fixed so that "학습 가능해야 실력이 성장한다" holds. Both mean
    /// this record is level data, not match data: the same every match, and no
    /// secret. What varies per match is only which site the objective is at.
    /// </para>
    /// </summary>
    public readonly struct FloorDescriptor
    {
        /// <summary>Signed floor index: 0 is ground, -3 is 지하 3층. §03's example floor.</summary>
        public readonly int FloorIndex;

        /// <summary>
        /// The mark this floor is signed with in the stairwells. Kept separate from
        /// <see cref="FloorIndex"/> because the sign is what a player reads and
        /// misreads — §03's chain is a chain of marks, and "지하 3층" reaches the team
        /// as the glyph 3, not as the number -3.
        /// </summary>
        public readonly ClueGlyph NumberGlyph;

        /// <summary>
        /// What makes this floor recognisable from the inside. §03's first clue names
        /// one of these and nothing else.
        /// </summary>
        public readonly FloorFeature Feature;

        /// <summary>Creates a floor descriptor.</summary>
        public FloorDescriptor(int floorIndex, ClueGlyph numberGlyph, FloorFeature feature)
        {
            FloorIndex = floorIndex;
            NumberGlyph = numberGlyph;
            Feature = feature;
        }
    }

    /// <summary>
    /// One place the objective or a clue can be, per §12's 후보 지점 rules (two exits,
    /// no sight line from the entrances, cover nearby, reachable electrical panel).
    /// <para>
    /// The catalog holds three per zone because §12 says so, and exactly one of them
    /// holds the objective each match. Everything in this struct is public level
    /// data: a client is welcome to know that a room is a candidate — §03 only
    /// requires that it not know <em>which</em> candidate.
    /// </para>
    /// </summary>
    public readonly struct CandidateSite
    {
        /// <summary>Stable identifier, unique across the map.</summary>
        public readonly int SiteId;

        /// <summary>Zone this site belongs to, matching <see cref="Session.IWorldProbe.ZoneIdAt"/>. §12.</summary>
        public readonly int ZoneId;

        /// <summary>Floor this site is on, matching a <see cref="FloorDescriptor.FloorIndex"/>.</summary>
        public readonly int FloorIndex;

        /// <summary>Where it is.</summary>
        public readonly Vec3 Position;

        /// <summary>How the building signs it — the third clue's content. §03.</summary>
        public readonly SiteLabel Label;

        /// <summary>Creates a candidate site.</summary>
        public CandidateSite(int siteId, int zoneId, int floorIndex, Vec3 position, SiteLabel label)
        {
            SiteId = siteId;
            ZoneId = zoneId;
            FloorIndex = floorIndex;
            Position = position;
            Label = label;
        }
    }

    /// <summary>
    /// The building's fixed geography of floors and candidate sites.
    /// <para>
    /// This type deliberately contains no answer. §03 fixes the map so skill can
    /// grow and randomises only placement, so the catalog is shareable, cacheable
    /// and safe to hand to a client — while <see cref="ObjectiveResolver"/>, which
    /// holds the per-match choice, is not.
    /// </para>
    /// <para>
    /// The constructor rejects a map that cannot carry §03's chain, rather than
    /// letting the chain fail silently at runtime. Two of the checks are the whole
    /// reason this class exists:
    /// </para>
    /// <list type="bullet">
    /// <item>A <see cref="FloorFeature"/> may name at most one floor, or the second
    /// clue ("물이 있는 층은 지하 3층이다") would be a statement with two answers.</item>
    /// <item>Two sites on the same floor may not share a label, or the third clue
    /// could not pin a site and §03's chain would never converge.</item>
    /// </list>
    /// </summary>
    public sealed class SiteCatalog
    {
        private readonly FloorDescriptor[] _floors;
        private readonly CandidateSite[] _sites;

        /// <summary>
        /// Builds and validates a catalog. The inputs are copied, so a level
        /// generator may reuse its working lists.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either list is null.</exception>
        /// <exception cref="ArgumentException">
        /// The map cannot carry §03's narrowing chain. The message names the rule.
        /// </exception>
        public SiteCatalog(IReadOnlyList<FloorDescriptor> floors, IReadOnlyList<CandidateSite> sites)
        {
            if (floors == null)
            {
                throw new ArgumentNullException(nameof(floors));
            }

            if (sites == null)
            {
                throw new ArgumentNullException(nameof(sites));
            }

            if (floors.Count == 0)
            {
                throw new ArgumentException("§03: a building with no floors cannot hold a clue chain.", nameof(floors));
            }

            if (sites.Count == 0)
            {
                throw new ArgumentException(
                    "§12 requires candidate sites per zone; with none, §03 has nothing to narrow to.", nameof(sites));
            }

            _floors = new FloorDescriptor[floors.Count];
            var seenFloor = new HashSet<int>();
            var seenGlyph = new HashSet<ClueGlyph>();
            var seenFeature = new HashSet<FloorFeature>();

            for (var i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                _floors[i] = floor;

                if (!seenFloor.Add(floor.FloorIndex))
                {
                    throw new ArgumentException(
                        "Two floors share index " + floor.FloorIndex + ".", nameof(floors));
                }

                if (!ClueGlyphs.IsDigit(floor.NumberGlyph))
                {
                    throw new ArgumentException(
                        "Floor " + floor.FloorIndex + " is signed with a mark that is not a digit, so §03's "
                        + "second clue could not be written on it.", nameof(floors));
                }

                if (!seenGlyph.Add(floor.NumberGlyph))
                {
                    throw new ArgumentException(
                        "Two floors are signed " + ClueGlyphs.Render(floor.NumberGlyph)
                        + ". §03's second clue names a floor by its sign, so signs must identify floors.",
                        nameof(floors));
                }

                if (floor.Feature != FloorFeature.None && !seenFeature.Add(floor.Feature))
                {
                    throw new ArgumentException(
                        "Two floors have the property " + floor.Feature + ". §03's second clue is a mapping "
                        + "('물이 있는 층은 지하 3층이다') and a mapping with two answers is not a clue.",
                        nameof(floors));
                }
            }

            _sites = new CandidateSite[sites.Count];
            var seenSite = new HashSet<int>();
            var seenLabelPerFloor = new HashSet<long>();

            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                _sites[i] = site;

                if (!seenSite.Add(site.SiteId))
                {
                    throw new ArgumentException("Two candidate sites share id " + site.SiteId + ".", nameof(sites));
                }

                if (!seenFloor.Contains(site.FloorIndex))
                {
                    throw new ArgumentException(
                        "Site " + site.SiteId + " is on floor " + site.FloorIndex + ", which is not in the catalog.",
                        nameof(sites));
                }

                if (!site.Label.IsWellFormed)
                {
                    throw new ArgumentException(
                        "Site " + site.SiteId + " is signed '" + site.Label + "', which is outside §03's glyph "
                        + "alphabet — it would be the one site no confusion pair could touch.", nameof(sites));
                }

                // Floor index is offset before packing so that negative basement
                // levels stay distinct from positive ones in the key.
                var key = (((long)site.FloorIndex + 1024L) << 24)
                          | ((long)(int)site.Label.Wing << 16)
                          | ((long)(int)site.Label.Number << 8)
                          | (long)(int)site.Label.Side;

                if (!seenLabelPerFloor.Add(key))
                {
                    throw new ArgumentException(
                        "Floor " + site.FloorIndex + " signs two sites '" + site.Label + "'. §03's third clue "
                        + "pins a site by its sign, so the chain would never converge.", nameof(sites));
                }
            }
        }

        /// <summary>Every floor, in the order the level declared them.</summary>
        public IReadOnlyList<FloorDescriptor> Floors => _floors;

        /// <summary>Every candidate site. §12: three per zone, one of them chosen per match.</summary>
        public IReadOnlyList<CandidateSite> Sites => _sites;

        /// <summary>Looks a floor up by its signed index.</summary>
        public bool TryGetFloor(int floorIndex, out FloorDescriptor floor)
        {
            for (var i = 0; i < _floors.Length; i++)
            {
                if (_floors[i].FloorIndex == floorIndex)
                {
                    floor = _floors[i];
                    return true;
                }
            }

            floor = default;
            return false;
        }

        /// <summary>
        /// Looks a floor up by the mark on its sign. This is the step a misread
        /// breaks: a team that remembers the wrong digit either lands on a different
        /// real floor or on no floor at all, and only the second case tells them
        /// anything.
        /// </summary>
        public bool TryGetFloorByNumber(ClueGlyph numberGlyph, out FloorDescriptor floor)
        {
            for (var i = 0; i < _floors.Length; i++)
            {
                if (_floors[i].NumberGlyph == numberGlyph)
                {
                    floor = _floors[i];
                    return true;
                }
            }

            floor = default;
            return false;
        }

        /// <summary>Looks a floor up by its distinguishing property. §03's first clue.</summary>
        public bool TryGetFloorOfFeature(FloorFeature feature, out FloorDescriptor floor)
        {
            if (feature != FloorFeature.None)
            {
                for (var i = 0; i < _floors.Length; i++)
                {
                    if (_floors[i].Feature == feature)
                    {
                        floor = _floors[i];
                        return true;
                    }
                }
            }

            floor = default;
            return false;
        }

        /// <summary>Looks a candidate site up by id.</summary>
        public bool TryGetSite(int siteId, out CandidateSite site)
        {
            for (var i = 0; i < _sites.Length; i++)
            {
                if (_sites[i].SiteId == siteId)
                {
                    site = _sites[i];
                    return true;
                }
            }

            site = default;
            return false;
        }

        /// <summary>How many candidate sites a floor holds. Used to check a floor can host the third clue.</summary>
        public int CountSitesOnFloor(int floorIndex)
        {
            var count = 0;
            for (var i = 0; i < _sites.Length; i++)
            {
                if (_sites[i].FloorIndex == floorIndex)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
