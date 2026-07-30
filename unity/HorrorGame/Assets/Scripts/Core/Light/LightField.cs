using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Light
{
    /// <summary>
    /// Everything that is lit, and the one place anything asks about it.
    /// <para>
    /// §03 makes darkness the lock on the objective — "어둠 = 목표의 잠금장치" — and makes
    /// light the thing that calls the monster, and then says the important part out loud:
    /// "목표와 위험이 같은 스위치에 걸린다." If the clue system and the perception system each
    /// worked out what was lit, that sentence would be true only for as long as two
    /// independently maintained answers happened to agree. So there is one field, it answers
    /// both questions, and the two answers come out of the same sources with the same
    /// falloff: <see cref="SampleAt"/> for reading, <see cref="VisibilityOf"/> for being
    /// found.
    /// </para>
    /// <para>
    /// It holds §03's three 조명 수단 and nothing else. A player's flashlight arrives per call
    /// as a <see cref="LightCone"/>, because a beam is a reading of where somebody is looking
    /// this frame; 구역 조명 and 조명탄 are placed and therefore owned here. Zone lights arrive
    /// as an int and a position rather than as an <c>EngineerOutcome</c>, so the light layer
    /// never imports the ability layer (ARCHITECTURE §3) and a test can light a zone in one
    /// line.
    /// </para>
    /// <para>
    /// Host-side (§13). What is lit decides what can be read, and clue contents exist only on
    /// the host — a client that could resolve reading light for itself would be halfway to
    /// resolving the clue.
    /// </para>
    /// </summary>
    public sealed class LightField
    {
        private readonly IWorldProbe _probe;
        private readonly List<Flare> _flares = new List<Flare>();
        private readonly List<Flare> _expiredFlares = new List<Flare>();
        private readonly List<int> _litZones = new List<int>();
        private readonly List<Vec3> _litZoneAnchors = new List<Vec3>();
        private readonly List<LightLure> _lures = new List<LightLure>();

        private bool _luresDirty = true;
        private int _nextFlareId;

        /// <summary>
        /// Creates the field over the world seam.
        /// </summary>
        /// <param name="probe">
        /// Used for three things: snapping a thrown flare onto the floor, naming the zone a
        /// position is in, and the sight line between a player and the monster.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="probe"/> is missing.</exception>
        public LightField(IWorldProbe probe)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            _probe = probe;
        }

        /// <summary>Flares still burning, in the order they were lit.</summary>
        public IReadOnlyList<Flare> BurningFlares
        {
            get { return _flares; }
        }

        /// <summary>Zones a 구역 조명 is currently up in. §03: 구역 전체가 밝다.</summary>
        public IReadOnlyList<int> LitZones
        {
            get { return _litZones; }
        }

        /// <summary>
        /// Every light the monster is drawn towards. §03 prices both area lights with
        /// "괴물도 그쪽으로 온다", and this is that price made available to the monster brain as
        /// plain positions and radii.
        /// <para>
        /// A player's flashlight is deliberately absent: §03 prices a beam as "괴물이 잘 본다",
        /// which is <see cref="VisibilityOf"/>, not a destination. The monster is drawn to
        /// rooms someone lit and left, and to people it can see.
        /// </para>
        /// </summary>
        public IReadOnlyList<LightLure> Lures
        {
            get
            {
                RebuildLuresIfNeeded();
                return _lures;
            }
        }

        /// <summary>
        /// Brings a 구역 조명 up or cuts it. Mirrors the Engineer's breaker (§04) and the
        /// 조명탄's zone-scale substitute (§11).
        /// <para>
        /// Cutting one is as easy as calling this with false, which is the point: §04's second
        /// listed accident is "조명 끔", and its design note forbids protecting the team from
        /// it. If somebody was 2.5 s into a clue (§03), they have just lost it.
        /// </para>
        /// </summary>
        /// <param name="zoneId">Zone from <see cref="IWorldProbe.ZoneIdAt"/>. Negative ids are ignored — -1 means "off the map".</param>
        /// <param name="anchor">The breaker panel, so <see cref="Lures"/> has somewhere to send the monster.</param>
        /// <param name="lit">True to light the zone.</param>
        public void SetZoneLit(int zoneId, Vec3 anchor, bool lit)
        {
            if (zoneId < 0)
            {
                return;
            }

            var index = _litZones.IndexOf(zoneId);
            if (lit)
            {
                if (index < 0)
                {
                    _litZones.Add(zoneId);
                    _litZoneAnchors.Add(anchor);
                }
                else
                {
                    _litZoneAnchors[index] = anchor;
                }
            }
            else if (index >= 0)
            {
                _litZones.RemoveAt(index);
                _litZoneAnchors.RemoveAt(index);
            }
            else
            {
                return;
            }

            _luresDirty = true;
        }

        /// <summary>True when that zone's 구역 조명 is up.</summary>
        public bool IsZoneLit(int zoneId)
        {
            return zoneId >= 0 && _litZones.Contains(zoneId);
        }

        /// <summary>
        /// Strikes a 조명탄. §08: 1회용 · 소리를 낸다.
        /// <para>
        /// Single use is structural rather than checked: this creates a flare that burns for
        /// <see cref="GameConstants.FlareSeconds"/> and is then gone, and there is no method
        /// that puts one back. Whether the player had a flare to strike is §08's business —
        /// the economy owns the item, this owns the fire.
        /// </para>
        /// <para>
        /// The landing point is snapped with <see cref="IWorldProbe.SnapToNavigable"/>: a
        /// flare lies on the floor, and a probe that knows nothing navigable simply returns
        /// the requested point, which lights the room the thrower is standing in rather than
        /// failing.
        /// </para>
        /// </summary>
        /// <param name="position">Where it was thrown.</param>
        /// <returns>The light and the noise, for the world and for the monster brain.</returns>
        public FlareIgnition Ignite(Vec3 position)
        {
            var landed = _probe.SnapToNavigable(position);
            var id = _nextFlareId++;

            _flares.Add(new Flare(id, landed, GameConstants.FlareSeconds));
            _luresDirty = true;

            return new FlareIgnition(
                id,
                landed,
                GameConstants.FlareIgniteNoiseLevel,
                GameConstants.ZoneLightRadius,
                GameConstants.FlareSeconds);
        }

        /// <summary>
        /// Burns the flares down.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Step length. Zero changes nothing. A frame spike longer than a flare's remaining
        /// burn expires it exactly once and queues it for
        /// <see cref="TryTakeExpiredFlare"/> — the burn cannot be tunnelled through, and
        /// several flares going out on the same step are all reported, in the order they were
        /// lit.
        /// </param>
        public void Tick(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f || _flares.Count == 0)
            {
                return;
            }

            // Compact in place: keeps ignition order, allocates nothing, and cannot skip an
            // element the way removing while iterating forwards would.
            var write = 0;
            for (var read = 0; read < _flares.Count; read++)
            {
                var flare = _flares[read];
                var remaining = flare.RemainingSeconds - deltaSeconds;

                if (remaining > 0f)
                {
                    _flares[write] = flare.WithRemaining(remaining);
                    write++;
                }
                else
                {
                    _expiredFlares.Add(flare.WithRemaining(0f));
                    _luresDirty = true;
                }
            }

            if (write < _flares.Count)
            {
                _flares.RemoveRange(write, _flares.Count - write);
            }
        }

        /// <summary>
        /// Takes one flare that has burned out, oldest first.
        /// <para>
        /// A one-shot handoff rather than a flag, for the same reason
        /// <c>EngineerAbility.TryTakeOutcome</c> is: the host may tick and read at different
        /// rates, and a room going dark must be applied once. §03 makes going dark the
        /// interesting event — it is when a read dies.
        /// </para>
        /// </summary>
        public bool TryTakeExpiredFlare(out Flare flare)
        {
            if (_expiredFlares.Count == 0)
            {
                flare = default(Flare);
                return false;
            }

            flare = _expiredFlares[0];
            _expiredFlares.RemoveAt(0);
            return true;
        }

        /// <summary>
        /// Light on a point from the area lights alone — everything except the asker's own
        /// beam. This is the term <see cref="VisibilityOf"/> needs: a 구역 조명 lights the
        /// player, a flashlight lights what the player is pointing at.
        /// </summary>
        public LightSample SampleArea(Vec3 position)
        {
            var best = 0f;
            var kind = LightSourceKind.None;

            // 구역 조명 first: §03 says "구역 전체가 밝다", so a lit zone is uniformly bright
            // with no falloff. That uniformity is what makes "여러 명이 동시에 읽는다" true and
            // what makes the Engineer the efficient answer to §03's lock rather than a
            // stronger flashlight.
            if (IsZoneLit(_probe.ZoneIdAt(position)))
            {
                best = 1f;
                kind = LightSourceKind.ZoneLight;
            }

            // The host may know about light this field does not track. Reported as its own
            // kind so telemetry can tell a designer's fixture apart from a player's doing.
            if (best < 1f && _probe.IsAreaLit(position))
            {
                best = 1f;
                kind = LightSourceKind.AreaLit;
            }

            for (var i = 0; i < _flares.Count; i++)
            {
                var flare = _flares[i];

                // Full 3D distance here, unlike every gameplay range check: a flare on the
                // floor below must not light the room above it. §12's map is multi-storey and
                // light is not a horizontal phenomenon.
                var quality = LightRules.RadialFalloff(
                    Vec3.Distance(position, flare.Position), flare.Radius);

                if (quality > best)
                {
                    best = quality;
                    kind = LightSourceKind.Flare;
                }
            }

            return new LightSample(best, kind);
        }

        /// <summary>
        /// Light on a point, from every source including a beam pointed at it. Feeds
        /// <c>ClueReadContext.LightQuality</c>.
        /// <para>
        /// The strongest source wins — reading by flashlight inside a lit zone is no harder
        /// than reading by the zone light alone. §03 lists the three 조명 수단 as alternatives,
        /// not as an additive budget, and the zone light's advantage is that it is uniform and
        /// hands-free, not that it stacks.
        /// </para>
        /// </summary>
        /// <param name="position">The mark being read.</param>
        /// <param name="beam">The reader's own beam, or <see cref="LightCone.None"/>.</param>
        public LightSample SampleAt(Vec3 position, in LightCone beam)
        {
            var area = SampleArea(position);
            var beamQuality = beam.QualityAt(position);

            if (beamQuality > area.Quality)
            {
                return new LightSample(beamQuality, LightSourceKind.Flashlight);
            }

            return area;
        }

        /// <summary>
        /// True when a clue at this position could be read right now. §03's lock, as one
        /// boolean.
        /// <para>
        /// This is where "배터리가 떨어지면 단서를 읽을 수 없다" is enforced: a flashlight with a
        /// flat cell produces <see cref="LightCone.None"/>, which lights nothing, and with no
        /// area light in reach the answer is false. No separate battery check exists anywhere
        /// in the clue path — there is only ever the light.
        /// </para>
        /// </summary>
        public bool CanReadAt(Vec3 position, in LightCone beam)
        {
            return SampleAt(position, beam).IsReadable;
        }

        /// <summary>
        /// How much light is giving a player away right now. §03's danger half.
        /// <para>
        /// Gathers the world state and hands it to <see cref="LightRules.Visibility"/>. The
        /// perception system should call this and nothing else, and must not re-derive a light
        /// term of its own — that is the whole reason this exists as one query.
        /// </para>
        /// </summary>
        /// <param name="playerPosition">Where the player is.</param>
        /// <param name="aimDirection">Where they are looking. §05 puts this on the wire because a beam's direction is information.</param>
        /// <param name="flashlight">Their flashlight. Its on/off, battery and §08 upgrade state are all read from here.</param>
        /// <param name="tierRangeMultiplier"><c>ThreatTier.FlashlightRangeMultiplier</c> — §07's 심야 −30%, as a float.</param>
        /// <param name="monsterPosition">Where the monster is.</param>
        /// <exception cref="ArgumentNullException"><paramref name="flashlight"/> is missing.</exception>
        public PlayerVisibility VisibilityOf(
            Vec3 playerPosition,
            Vec3 aimDirection,
            FlashlightState flashlight,
            float tierRangeMultiplier,
            Vec3 monsterPosition)
        {
            if (flashlight == null)
            {
                throw new ArgumentNullException(nameof(flashlight));
            }

            var beam = flashlight.ConeAt(playerPosition, aimDirection, tierRangeMultiplier);
            var area = SampleArea(playerPosition);

            return LightRules.Visibility(new LightVisibilityQuery(
                playerPosition,
                monsterPosition,
                beam,
                flashlight.NoticeDistance,
                area.Quality,
                area.Source,
                _probe.HasLineOfSight(playerPosition, monsterPosition)));
        }

        /// <summary>
        /// Empties the field for a new match.
        /// <para>
        /// Not for §03's 부분 리셋. That resets "괴물의 추격 상태 · 위치 · 어그로" and explicitly
        /// keeps 시간 — a flare left burning in the basement keeps burning while the team is at
        /// the van, and the Engineer's breaker stays where he left it. Calling this on a
        /// surface trip would hand the team a free blackout reset.
        /// </para>
        /// </summary>
        public void Clear()
        {
            _flares.Clear();
            _expiredFlares.Clear();
            _litZones.Clear();
            _litZoneAnchors.Clear();
            _lures.Clear();
            _luresDirty = true;
            _nextFlareId = 0;
        }

        private void RebuildLuresIfNeeded()
        {
            if (!_luresDirty)
            {
                return;
            }

            _lures.Clear();

            for (var i = 0; i < _litZones.Count; i++)
            {
                _lures.Add(new LightLure(
                    _litZoneAnchors[i],
                    GameConstants.ZoneLightRadius,
                    LightSourceKind.ZoneLight,
                    _litZones[i]));
            }

            for (var i = 0; i < _flares.Count; i++)
            {
                _lures.Add(new LightLure(
                    _flares[i].Position,
                    _flares[i].Radius,
                    LightSourceKind.Flare,
                    _flares[i].Id));
            }

            _luresDirty = false;
        }
    }
}
