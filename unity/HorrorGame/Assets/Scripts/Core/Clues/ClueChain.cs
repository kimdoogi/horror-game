using System;

namespace HorrorGame.Core.Clues
{
    /// <summary>How far a team's beliefs have narrowed the search. §03.</summary>
    public enum NarrowingStatus
    {
        /// <summary>Nothing has been said out loud yet. Every candidate is still live.</summary>
        NoInformation = 0,

        /// <summary>The search is smaller but still has more than one answer.</summary>
        Narrowed = 1,

        /// <summary>Exactly one site fits. §03's chain has converged.</summary>
        Pinned = 2,

        /// <summary>
        /// No site fits. Someone misremembered a mark, and the building itself has
        /// just told them so — the one misread that costs a walk instead of a match.
        /// </summary>
        Contradiction = 3,
    }

    /// <summary>The result of combining what a team believes with what the building is.</summary>
    public readonly struct NarrowingResult
    {
        /// <summary>How far the beliefs got.</summary>
        public readonly NarrowingStatus Status;

        /// <summary>How many candidate sites still fit.</summary>
        public readonly int CandidateCount;

        /// <summary>The site, when <see cref="Status"/> is <see cref="NarrowingStatus.Pinned"/>; otherwise -1.</summary>
        public readonly int SiteId;

        /// <summary>True when the beliefs resolve to a floor.</summary>
        public readonly bool FloorKnown;

        /// <summary>The resolved floor, meaningful only when <see cref="FloorKnown"/>.</summary>
        public readonly int FloorIndex;

        /// <summary>Creates a result.</summary>
        public NarrowingResult(
            NarrowingStatus status, int candidateCount, int siteId, bool floorKnown, int floorIndex)
        {
            Status = status;
            CandidateCount = candidateCount;
            SiteId = siteId;
            FloorKnown = floorKnown;
            FloorIndex = floorIndex;
        }
    }

    /// <summary>
    /// What a team is holding in its collective head.
    /// <para>
    /// <b>This is a model of memory, not a game object.</b> §03's hardest constraint
    /// is "단서를 가지고 나올 수 없다 — 그 자리에서 보고, 기억해서, 말로 전달해야 한다",
    /// so nothing in the shipped game may store, sync, save or draw one of these.
    /// It exists because the rules layer has to be able to answer "does this chain
    /// converge?" without a room full of players, and because the headless simulator
    /// needs to model a team that got one mark wrong. A HUD panel showing a
    /// <see cref="ClueBelief"/> would delete the design.
    /// </para>
    /// <para>
    /// Notice the second slot does not care where the mapping came from. A team can
    /// fill it from the second clue or from remembering last match — §03 fixes the
    /// map so that "학습 가능해야 실력이 성장한다", and the property-to-floor mapping is
    /// part of the fixed map. See docs/BALANCE-FINDINGS.md: this makes the middle
    /// link optional for a team that has played the building before.
    /// </para>
    /// </summary>
    public readonly struct ClueBelief
    {
        /// <summary>True once someone has reported the objective's floor property.</summary>
        public readonly bool HasFeature;

        /// <summary>The property the objective's floor is believed to have.</summary>
        public readonly FloorFeature Feature;

        /// <summary>True once someone holds a property-to-floor mapping, from a clue or from memory.</summary>
        public readonly bool HasMapping;

        /// <summary>The property the mapping is about. Only useful when it matches <see cref="Feature"/>.</summary>
        public readonly FloorFeature MappedFeature;

        /// <summary>The floor sign the mapping names, as remembered — misreads included.</summary>
        public readonly ClueGlyph MappedFloorNumber;

        /// <summary>True once someone has reported a site sign.</summary>
        public readonly bool HasLabel;

        /// <summary>The site sign, as remembered — misreads included.</summary>
        public readonly SiteLabel Label;

        private ClueBelief(
            bool hasFeature,
            FloorFeature feature,
            bool hasMapping,
            FloorFeature mappedFeature,
            ClueGlyph mappedFloorNumber,
            bool hasLabel,
            SiteLabel label)
        {
            HasFeature = hasFeature;
            Feature = feature;
            HasMapping = hasMapping;
            MappedFeature = mappedFeature;
            MappedFloorNumber = mappedFloorNumber;
            HasLabel = hasLabel;
            Label = label;
        }

        /// <summary>A team that has just gone in.</summary>
        public static ClueBelief Empty =>
            new ClueBelief(
                false, FloorFeature.None, false, FloorFeature.None, ClueGlyph.Unreadable, false, SiteLabel.Unreadable);

        /// <summary>The belief with §03's first link filled in.</summary>
        public ClueBelief WithFeature(FloorFeature feature) =>
            new ClueBelief(true, feature, HasMapping, MappedFeature, MappedFloorNumber, HasLabel, Label);

        /// <summary>
        /// The belief with a property-to-floor mapping filled in. Also the entry
        /// point for map knowledge: a veteran calling this from memory is playing the
        /// game §03 asked for.
        /// </summary>
        public ClueBelief WithMapping(FloorFeature feature, ClueGlyph floorNumber) =>
            new ClueBelief(HasFeature, Feature, true, feature, floorNumber, HasLabel, Label);

        /// <summary>The belief with §03's third link filled in.</summary>
        public ClueBelief WithLabel(SiteLabel label) =>
            new ClueBelief(HasFeature, Feature, HasMapping, MappedFeature, MappedFloorNumber, true, label);

        /// <summary>
        /// The belief after somebody says a report out loud. An illegible report
        /// changes nothing, which is the point of failing a read rather than
        /// guessing: the team is no worse off than before they walked in.
        /// </summary>
        public ClueBelief Absorb(ClueReport report)
        {
            if (!report.Legible)
            {
                return this;
            }

            switch (report.Layer)
            {
                case ClueLayer.Feature:
                    return WithFeature(report.Feature);
                case ClueLayer.FloorMapping:
                    return WithMapping(report.Feature, report.FloorNumber);
                case ClueLayer.SitePin:
                    return WithLabel(report.Label);
                default:
                    return this;
            }
        }
    }

    /// <summary>
    /// §03's 단서 조합 방식 — the one part of the clue system that never varies.
    /// <para>
    /// "맵 구조 · 단서 조합 방식 — 고정. 학습 가능해야 실력이 성장한다." So the combination
    /// is a pure function of beliefs and geography, with no seed and no state: a
    /// property plus a mapping gives a floor, a floor plus a sign gives a site. A
    /// team improves by getting better at the marks, never by learning a new recipe.
    /// </para>
    /// <para>
    /// It is also the honest place to see how a misread hurts. Feed in a label with
    /// one mark wrong and the chain still converges — on the wrong room. Feed in a
    /// floor sign that names no floor and it reports
    /// <see cref="NarrowingStatus.Contradiction"/>, which is the team's only free
    /// warning. §03 wants far more of the first kind than the second.
    /// </para>
    /// </summary>
    public static class ClueChain
    {
        /// <summary>
        /// Resolves a floor from the first two links.
        /// <para>
        /// The mapping is only worth anything when it is about the property the first
        /// clue named. A team holding a decoy mapping is holding a true sentence
        /// about the wrong floor, and this returns false for it without complaint —
        /// they have to go back in, which is where §03's extra round trips come from.
        /// </para>
        /// </summary>
        /// <param name="catalog">The building.</param>
        /// <param name="belief">What the team believes.</param>
        /// <param name="floorIndex">The resolved floor, when this returns true.</param>
        /// <param name="impossible">
        /// True when the remembered sign names no floor in this building — the belief
        /// is not merely incomplete, it is wrong.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
        public static bool TryResolveFloor(
            SiteCatalog catalog, ClueBelief belief, out int floorIndex, out bool impossible)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            floorIndex = 0;
            impossible = false;

            if (!belief.HasFeature || !belief.HasMapping || belief.MappedFeature != belief.Feature)
            {
                return false;
            }

            if (catalog.TryGetFloorByNumber(belief.MappedFloorNumber, out var floor))
            {
                floorIndex = floor.FloorIndex;
                return true;
            }

            impossible = true;
            return false;
        }

        /// <summary>
        /// Narrows the candidate sites by everything the team believes.
        /// <para>
        /// Deliberately takes no <see cref="Session.IRandomSource"/> and no objective:
        /// this is the players' reasoning, and it must be possible to run it — in a
        /// test, in the simulator, at a whiteboard — without the answer being
        /// anywhere nearby.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
        public static NarrowingResult Narrow(SiteCatalog catalog, ClueBelief belief)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var floorKnown = TryResolveFloor(catalog, belief, out var floorIndex, out var impossible);

            var count = 0;
            var lastSiteId = -1;
            var sites = catalog.Sites;

            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];

                if (floorKnown && site.FloorIndex != floorIndex)
                {
                    continue;
                }

                if (belief.HasLabel && !site.Label.Equals(belief.Label))
                {
                    continue;
                }

                count++;
                lastSiteId = site.SiteId;
            }

            if (impossible || count == 0)
            {
                return new NarrowingResult(NarrowingStatus.Contradiction, 0, -1, floorKnown, floorIndex);
            }

            // A single survivor only counts as pinned if a belief did the narrowing.
            // A building with one candidate site would otherwise report Pinned to a
            // team that has read nothing, and §03's chain would be decoration.
            var narrowedBySomething = floorKnown || belief.HasLabel;

            if (count == 1 && narrowedBySomething)
            {
                return new NarrowingResult(NarrowingStatus.Pinned, 1, lastSiteId, floorKnown, floorIndex);
            }

            if (!belief.HasFeature && !belief.HasMapping && !belief.HasLabel)
            {
                return new NarrowingResult(NarrowingStatus.NoInformation, count, -1, false, 0);
            }

            return new NarrowingResult(NarrowingStatus.Narrowed, count, -1, floorKnown, floorIndex);
        }
    }
}
