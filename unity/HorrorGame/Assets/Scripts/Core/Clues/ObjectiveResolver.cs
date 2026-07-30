using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// The match's answer, and the only thing in the codebase allowed to know it.
    /// <para>
    /// <b>Host-only.</b> §13: "단서 내용 · 목표물 위치 — 호스트만 보유. 클라이언트에 보내면
    /// 메모리에서 읽힌다." ARCHITECTURE §4 adds the part that is easy to get wrong —
    /// sending the answer and only showing it when the player is close is the same as
    /// sending the answer. So this type is built so that there is nothing to send:
    /// </para>
    /// <list type="bullet">
    /// <item>No property returns the objective's site, position, floor or label. Not
    /// one. The answer leaves through <see cref="TryPlaceObjective"/>, which pushes a
    /// position into a callback exactly once and then refuses forever, and through
    /// <see cref="IsObjectiveSite"/>, which answers a guess the host already made.</item>
    /// <item>Clue content lives in <c>ClueDef</c>, which is <c>internal</c> — the Net
    /// assembly cannot name the type, so it cannot serialise it even by accident.
    /// <c>ClueDef</c> is also a class, so nothing blittable-generic will sweep it
    /// up.</item>
    /// <item>What crosses the wire is a <see cref="ClueReport"/>: what one reader
    /// believed, after <see cref="MisreadModel"/> has had its say. A client that logs
    /// every report it ever receives has a transcript of its own player's guesses.</item>
    /// <item>There is no method that produces an item, a note or a journal entry.
    /// §03: "단서를 가지고 나올 수 없다." The only channel out of the basement is a
    /// player's memory and their voice, and voice is cut at the sender (§13).</item>
    /// </list>
    /// <para>
    /// Randomisation follows §03's table exactly: the objective's site, the clues'
    /// positions and the clues' contents vary per match; the building and the way the
    /// clues combine do not, because "학습 가능해야 실력이 성장한다".
    /// </para>
    /// </summary>
    public sealed class ObjectiveResolver
    {
        private readonly SiteCatalog _catalog;
        private readonly ClueDef[] _clues;
        private readonly ClueMarker[] _markers;
        private readonly int _objectiveSiteId;
        private readonly Vec3 _objectivePosition;

        private bool _objectiveReleased;

        /// <summary>
        /// Lays out one match.
        /// <para>
        /// Draws in a fixed order — objective site, round-trip target, clue hosts,
        /// then one inscription per clue — so a seed reproduces a layout exactly. §13
        /// needs that: a balance report from a player is a seed, and a seed that
        /// replayed differently would be worthless.
        /// </para>
        /// </summary>
        /// <param name="catalog">The building. Fixed across matches. §03.</param>
        /// <param name="probe">
        /// Used to keep the layout on navigable ground and to prefer sites the team
        /// can actually reach from <paramref name="entrance"/>. A probe that reports
        /// nothing reachable does not fail the match — the layout falls back to the
        /// level's own declared positions and raises
        /// <see cref="UsedUnreachableFallback"/>, because a match that refuses to
        /// start is worse than one laid out optimistically.
        /// </param>
        /// <param name="entrance">Where the team comes in from. §03's 왕복 starts here.</param>
        /// <param name="rng">Seeded. Never a clock, never ambient randomness.</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// No floor in the catalog has a <see cref="FloorFeature"/>, so §03's first
        /// clue could not be written about anywhere and the chain has no first link.
        /// </exception>
        public ObjectiveResolver(SiteCatalog catalog, IWorldProbe probe, Vec3 entrance, IRandomSource rng)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            _catalog = catalog;

            var eligible = CollectEligibleSites(catalog);
            if (eligible.Count == 0)
            {
                throw new ArgumentException(
                    "§03's first clue names a property of the objective's floor ('물이 있는 층'), so the "
                    + "objective can only be placed on a floor that has one. No floor in this catalog does.",
                    nameof(catalog));
            }

            var reachable = FilterReachable(catalog, probe, entrance, eligible);
            if (reachable.Count == 0)
            {
                UsedUnreachableFallback = true;
                reachable = eligible;
            }

            var objectiveIndex = reachable[rng.NextInt(0, reachable.Count)];
            var objective = catalog.Sites[objectiveIndex];
            _objectiveSiteId = objective.SiteId;
            _objectivePosition = Snap(probe, objective.Position);

            catalog.TryGetFloor(objective.FloorIndex, out var objectiveFloor);

            // §03: "운이 좋으면 2번, 나쁘면 5번." The target is drawn first so the
            // layout can be built to realise it, rather than measured afterwards.
            var targetTrips = rng.NextInt(
                GameConstants.ExpectedRoundTripsMin, GameConstants.ExpectedRoundTripsMax + 1);

            var decoyBudget = System.Math.Max(0, targetTrips - GameConstants.CluesRequiredToLocate);
            var wantCoLocation = targetTrips < GameConstants.CluesRequiredToLocate;

            var hosts = new List<int>();
            for (var i = 0; i < catalog.Sites.Count; i++)
            {
                if (catalog.Sites[i].SiteId != objective.SiteId)
                {
                    hosts.Add(i);
                }
            }

            Shuffle(hosts, rng);

            // The site pin goes on the objective's own floor: §03's last descent is
            // "지하 3층으로 직행 → 회수", and a pin anywhere else would let a team read
            // it before they had any reason to be there.
            var pinHost = TakeFirstOnFloor(catalog, hosts, objective.FloorIndex);
            if (pinHost < 0)
            {
                // A floor with a single candidate site. The pin ends up at the
                // objective itself, which is a lucky map rather than a broken one.
                pinHost = objectiveIndex;
            }

            var featureHost = TakeAny(hosts, objectiveIndex);

            var mappingHost = -1;
            if (wantCoLocation)
            {
                mappingHost = TakeFirstInZone(catalog, hosts, catalog.Sites[featureHost].ZoneId);
            }

            if (mappingHost < 0)
            {
                mappingHost = TakeFirstOutsideZone(catalog, hosts, catalog.Sites[featureHost].ZoneId);
            }

            if (mappingHost < 0)
            {
                mappingHost = TakeAny(hosts, objectiveIndex);
            }

            var coLocated = catalog.Sites[mappingHost].ZoneId == catalog.Sites[featureHost].ZoneId;

            var clues = new List<ClueDef>();
            var nextClueId = 0;

            clues.Add(new ClueDef(
                nextClueId++,
                ClueLayer.Feature,
                catalog.Sites[featureHost].ZoneId,
                catalog.Sites[featureHost].FloorIndex,
                Snap(probe, catalog.Sites[featureHost].Position),
                RollInscription(rng),
                objectiveFloor.Feature,
                ClueGlyph.Unreadable,
                SiteLabel.Unreadable,
                false));

            clues.Add(new ClueDef(
                nextClueId++,
                ClueLayer.FloorMapping,
                catalog.Sites[mappingHost].ZoneId,
                catalog.Sites[mappingHost].FloorIndex,
                Snap(probe, catalog.Sites[mappingHost].Position),
                RollInscription(rng),
                objectiveFloor.Feature,
                objectiveFloor.NumberGlyph,
                SiteLabel.Unreadable,
                false));

            clues.Add(new ClueDef(
                nextClueId++,
                ClueLayer.SitePin,
                catalog.Sites[pinHost].ZoneId,
                catalog.Sites[pinHost].FloorIndex,
                Snap(probe, catalog.Sites[pinHost].Position),
                RollInscription(rng),
                FloorFeature.None,
                ClueGlyph.Unreadable,
                objective.Label,
                false));

            // Decoys are true mappings about other floors' properties. They are the
            // mechanism behind §03's unlucky matches: a team cannot tell one from the
            // real mapping until it already holds the first clue.
            var decoysPlaced = 0;
            for (var i = 0; i < catalog.Floors.Count && decoysPlaced < decoyBudget; i++)
            {
                var floor = catalog.Floors[i];
                if (floor.Feature == FloorFeature.None || floor.Feature == objectiveFloor.Feature)
                {
                    continue;
                }

                var decoyHost = TakeFirstOutsideZone(catalog, hosts, catalog.Sites[mappingHost].ZoneId);
                if (decoyHost < 0)
                {
                    break;
                }

                clues.Add(new ClueDef(
                    nextClueId++,
                    ClueLayer.FloorMapping,
                    catalog.Sites[decoyHost].ZoneId,
                    catalog.Sites[decoyHost].FloorIndex,
                    Snap(probe, catalog.Sites[decoyHost].Position),
                    RollInscription(rng),
                    floor.Feature,
                    floor.NumberGlyph,
                    SiteLabel.Unreadable,
                    true));

                decoysPlaced++;
            }

            _clues = clues.ToArray();

            _markers = new ClueMarker[_clues.Length];
            for (var i = 0; i < _clues.Length; i++)
            {
                _markers[i] = new ClueMarker(
                    _clues[i].ClueId, _clues[i].ZoneId, _clues[i].FloorIndex, _clues[i].Position);
            }

            // Reported from what was actually built, not from the draw: a building
            // that could not host the co-location or the decoys must not claim a
            // round-trip count it cannot deliver.
            var realised = (coLocated ? GameConstants.ExpectedRoundTripsMin : GameConstants.CluesRequiredToLocate)
                           + decoysPlaced;

            PlannedRoundTrips = MathX.Clamp(
                realised, GameConstants.ExpectedRoundTripsMin, GameConstants.ExpectedRoundTripsMax);
        }

        /// <summary>
        /// Round trips this layout is built to cost, inside §03's 2–5.
        /// <para>
        /// Safe to send and safe to log — it is a difficulty, not a location, and
        /// §13's match summary wants it. Note the word: <em>planned</em>. §03 says the
        /// count varies per match but not how, and the mechanisms here — putting two
        /// clues in one zone for a lucky match, planting true-but-irrelevant mappings
        /// for an unlucky one — only bind a team that reads what it finds in the order
        /// it finds it. A team that walks past both decoys beats the plan. See
        /// docs/BALANCE-FINDINGS.md.
        /// </para>
        /// </summary>
        public int PlannedRoundTrips { get; }

        /// <summary>
        /// True when no candidate site was reachable from the entrance and the layout
        /// fell back to the level's declared positions. Always false on a valid map;
        /// worth logging loudly, because it means the NavMesh and the level data
        /// disagree about the building.
        /// </summary>
        public bool UsedUnreachableFallback { get; }

        /// <summary>How many clues this match has: §03's three, plus any decoy mappings.</summary>
        public int ClueCount => _clues.Length;

        /// <summary>
        /// Where the clue props go. Positions and zones only — no content. The host
        /// may spawn these for everyone; a player standing in the room can see the
        /// paper on the desk too.
        /// </summary>
        public IReadOnlyList<ClueMarker> Markers => _markers;

        /// <summary>Clues legibly read this match. §13's match summary. §03 tunes against it.</summary>
        public int CluesRead { get; private set; }

        /// <summary>
        /// Reads where at least one mark came out wrong. §13's <c>ClueMisreads</c>,
        /// which §03 wants to be non-zero: "이 게임의 주된 웃음이자 사망 원인". Host-side
        /// only — telling the reader they misread would remove the entire mechanic.
        /// </summary>
        public int Misreads { get; private set; }

        /// <summary>
        /// The one channel content takes out of the host: what this reader, in these
        /// conditions, believes this clue says.
        /// <para>
        /// The host calls this when a <see cref="ClueReader"/> reaches
        /// <see cref="ClueReadState.Complete"/>, passing that reader's
        /// <see cref="ClueReader.Observation"/>. Returns false for an unknown clue and
        /// for a read the conditions did not support, with an illegible report either
        /// way — §03's failed read leaves the team with nothing rather than with
        /// something wrong.
        /// </para>
        /// <para>
        /// Not idempotent, and deliberately so. Two reads of the same clue can differ,
        /// because nothing is stored: §03 keeps no record, and a stable answer would be
        /// one — a record the team could compare against, which is precisely the
        /// "스크린샷도, 아이템 전달도 없다" this rests on.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
        public bool TryRead(int clueId, ClueObservation observation, IRandomSource rng, out ClueReport report)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var clue = FindClue(clueId);
            if (clue == null)
            {
                report = ClueReport.Illegible(clueId, ClueLayer.None);
                return false;
            }

            var viewing = clue.Inscription.ViewedBy(observation);

            if (!MisreadModel.IsLegible(viewing))
            {
                report = ClueReport.Illegible(clue.ClueId, clue.Layer);
                return false;
            }

            CluesRead++;

            switch (clue.Layer)
            {
                case ClueLayer.Feature:
                    // A word, not a mark. §03's four pairs are all glyph-level, so
                    // this is the one link a reader cannot get wrong — see
                    // docs/BALANCE-FINDINGS.md.
                    report = new ClueReport(
                        clue.ClueId, clue.Layer, true, clue.Feature, ClueGlyph.Unreadable, SiteLabel.Unreadable);
                    return true;

                case ClueLayer.FloorMapping:
                    var number = MisreadModel.Perceive(clue.FloorNumber, viewing, rng);
                    if (number.Misread)
                    {
                        Misreads++;
                    }

                    report = new ClueReport(
                        clue.ClueId, clue.Layer, true, clue.Feature, number.Perceived, SiteLabel.Unreadable);
                    return true;

                case ClueLayer.SitePin:
                    var label = MisreadModel.PerceiveLabel(clue.Label, viewing, rng, out var anyMisread);
                    if (anyMisread)
                    {
                        Misreads++;
                    }

                    report = new ClueReport(
                        clue.ClueId, clue.Layer, true, FloorFeature.None, ClueGlyph.Unreadable, label);
                    return true;

                default:
                    report = ClueReport.Illegible(clue.ClueId, clue.Layer);
                    return false;
            }
        }

        /// <summary>
        /// Whether a site the host is already asking about is the one.
        /// <para>
        /// A predicate, not an accessor: it confirms a guess and cannot be asked to
        /// produce one. The host calls it when a player interacts with a candidate
        /// site. It must never be reachable from a network message — answering it for
        /// a client, even one guess at a time, is answering "where is it".
        /// </para>
        /// </summary>
        public bool IsObjectiveSite(int siteId) => siteId == _objectiveSiteId;

        /// <summary>
        /// Hands the objective's position to the world builder, once.
        /// <para>
        /// A push rather than a getter, on purpose. There is no property on this class
        /// that returns the position, so there is nothing for a serialiser, a debug
        /// inspector or a careless <c>SyncVar</c> to find; the value flows outward
        /// through a callback the host supplies while it is building the level, and
        /// the second call returns false without invoking anything. If some later code
        /// needs the position again, the right answer is to keep it where the first
        /// call put it — not to widen this.
        /// </para>
        /// </summary>
        /// <param name="spawn">Receives the position, snapped onto navigable ground.</param>
        /// <returns>True on the first call, false on every one after it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="spawn"/> is null.</exception>
        public bool TryPlaceObjective(Action<Vec3> spawn)
        {
            if (spawn == null)
            {
                throw new ArgumentNullException(nameof(spawn));
            }

            if (_objectiveReleased)
            {
                return false;
            }

            _objectiveReleased = true;
            spawn(_objectivePosition);
            return true;
        }

        /// <summary>
        /// Whether the objective's position has already been handed out. Lets a host
        /// assert it built the level exactly once.
        /// </summary>
        public bool ObjectivePlaced => _objectiveReleased;

        /// <summary>
        /// Host-side invariant check: a team that reads every clue correctly must end
        /// up at exactly one site, and it must be this match's site.
        /// <para>
        /// The same role <see cref="GameConstants.Validate"/> plays for the numbers. A
        /// layout that fails this is unwinnable — §03's chain would either leave two
        /// candidates standing or point somewhere the objective is not — and a match
        /// is far better off refusing to start than sending four players down for
        /// something that is not there. Returns a bool rather than the site, so it
        /// stays a check and cannot become a back door.
        /// </para>
        /// </summary>
        public bool VerifyChainConverges()
        {
            var belief = ClueBelief.Empty;

            for (var i = 0; i < _clues.Length; i++)
            {
                var clue = _clues[i];

                switch (clue.Layer)
                {
                    case ClueLayer.Feature:
                        belief = belief.WithFeature(clue.Feature);
                        break;
                    case ClueLayer.FloorMapping:
                        if (!clue.IsDecoy)
                        {
                            belief = belief.WithMapping(clue.Feature, clue.FloorNumber);
                        }

                        break;
                    case ClueLayer.SitePin:
                        belief = belief.WithLabel(clue.Label);
                        break;
                }
            }

            var result = ClueChain.Narrow(_catalog, belief);
            return result.Status == NarrowingStatus.Pinned && result.SiteId == _objectiveSiteId;
        }

        private ClueDef? FindClue(int clueId)
        {
            for (var i = 0; i < _clues.Length; i++)
            {
                if (_clues[i].ClueId == clueId)
                {
                    return _clues[i];
                }
            }

            return null;
        }

        private static List<int> CollectEligibleSites(SiteCatalog catalog)
        {
            var eligible = new List<int>();

            for (var i = 0; i < catalog.Sites.Count; i++)
            {
                if (catalog.TryGetFloor(catalog.Sites[i].FloorIndex, out var floor)
                    && floor.Feature != FloorFeature.None)
                {
                    eligible.Add(i);
                }
            }

            return eligible;
        }

        private static List<int> FilterReachable(
            SiteCatalog catalog, IWorldProbe probe, Vec3 entrance, List<int> eligible)
        {
            var reachable = new List<int>();

            for (var i = 0; i < eligible.Count; i++)
            {
                var distance = probe.NavigableDistance(entrance, catalog.Sites[eligible[i]].Position);
                if (distance >= 0f && !float.IsNaN(distance) && !float.IsInfinity(distance))
                {
                    reachable.Add(eligible[i]);
                }
            }

            return reachable;
        }

        private static Vec3 Snap(IWorldProbe probe, Vec3 desired)
        {
            var snapped = probe.SnapToNavigable(desired);

            // A probe with no navigation data can answer with a degenerate point.
            // The level's own position is a better answer than the origin.
            if (float.IsNaN(snapped.X) || float.IsNaN(snapped.Y) || float.IsNaN(snapped.Z))
            {
                return desired;
            }

            return snapped;
        }

        private static void Shuffle(List<int> items, IRandomSource rng)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = rng.NextInt(0, i + 1);
                var swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }

        private static int TakeAny(List<int> hosts, int fallbackIndex)
        {
            if (hosts.Count == 0)
            {
                return fallbackIndex;
            }

            var index = hosts[0];
            hosts.RemoveAt(0);
            return index;
        }

        private static int TakeFirstOnFloor(SiteCatalog catalog, List<int> hosts, int floorIndex)
        {
            for (var i = 0; i < hosts.Count; i++)
            {
                if (catalog.Sites[hosts[i]].FloorIndex == floorIndex)
                {
                    var index = hosts[i];
                    hosts.RemoveAt(i);
                    return index;
                }
            }

            return -1;
        }

        private static int TakeFirstInZone(SiteCatalog catalog, List<int> hosts, int zoneId)
        {
            for (var i = 0; i < hosts.Count; i++)
            {
                if (catalog.Sites[hosts[i]].ZoneId == zoneId)
                {
                    var index = hosts[i];
                    hosts.RemoveAt(i);
                    return index;
                }
            }

            return -1;
        }

        private static int TakeFirstOutsideZone(SiteCatalog catalog, List<int> hosts, int zoneId)
        {
            for (var i = 0; i < hosts.Count; i++)
            {
                if (catalog.Sites[hosts[i]].ZoneId != zoneId)
                {
                    var index = hosts[i];
                    hosts.RemoveAt(i);
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Plants exactly one of §03's four conditions on a mark.
        /// <para>
        /// §03: "혼동쌍 — 의도적으로 심는다." Uniform over the table, so which clue is
        /// hung upside down varies per match while the set of obstacles never does —
        /// and no probability is invented here, because §03 supplies none. One
        /// condition per mark rather than several: a clue carrying all four would be
        /// unreadable rather than ambiguous, and ambiguity is the point.
        /// </para>
        /// <para>
        /// A planted condition only bites when the mark on that clue happens to belong
        /// to the matching pair. An upside-down sign that reads "ㅁ-2 좌" is a
        /// perfectly clear upside-down sign.
        /// </para>
        /// </summary>
        private static ClueInscription RollInscription(IRandomSource rng)
        {
            var pairs = ClueGlyphs.Pairs;
            var condition = pairs[rng.NextInt(0, pairs.Count)].Condition;

            switch (condition)
            {
                case MisreadCondition.InvertedAngle:
                    return new ClueInscription(false, false, 0f, 180f);

                case MisreadCondition.Handwriting:
                    return new ClueInscription(true, false, 0f, 0f);

                case MisreadCondition.Blur:
                    // Midway between "흐릿하다" and gone entirely: worn enough to
                    // confuse ㅁ and ㅇ, never worn enough to stop being a clue.
                    var worn = MathX.Lerp(
                        GameConstants.ClueBlurConfusionThreshold, GameConstants.ClueIllegibleBlur, 0.5f);
                    return new ClueInscription(false, false, worn, 0f);

                default:
                    return new ClueInscription(false, true, 0f, 0f);
            }
        }
    }
}
