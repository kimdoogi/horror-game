using System;
using System.Collections.Generic;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;

namespace HorrorGame.Core.Abilities
{
    /// <summary>
    /// A job handed to 정비공: what to do, where, and what the host already knows about
    /// who it will affect.
    /// <para>
    /// The consequence fields are the important part. §04's design note says the
    /// Engineer must be able to hurt the team — "문 안에 아군 · 조명 끔 · 함정에 주자" —
    /// and that this is the role's identity, not a defect: "버그로 취급해 없애지 말 것."
    /// So the host states who is on the far side of the door and the ability records it
    /// in the outcome. It never refuses on those grounds. Nothing in this file is a
    /// safety check; the fields exist so the accident is *legible* afterwards, which is
    /// what makes it funny instead of confusing.
    /// </para>
    /// </summary>
    public readonly struct EngineerRequest
    {
        /// <summary>What to do. §04.</summary>
        public readonly EngineerAction Action;

        /// <summary>Host-assigned id of the door, panel, safe or trap anchor.</summary>
        public readonly int SiteId;

        /// <summary>Where the work happens. The Engineer must stay within <see cref="GameConstants.EngineerReachDistance"/> of it.</summary>
        public readonly Vec3 Position;

        /// <summary>Zone the panel feeds. <see cref="EngineerAction.ZoneLight"/> only.</summary>
        public readonly int ZoneId;

        /// <summary>True to bring the zone up, false to cut it. <see cref="EngineerAction.ZoneLight"/> only. §04's "조명 끔" is the second listed accident.</summary>
        public readonly bool ZoneLightOn;

        /// <summary>
        /// Teammates the seal will shut on the far side, as the host counts them.
        /// Recorded, never vetoed — §04's first listed accident is "문 안에 아군".
        /// </summary>
        public readonly int TeammatesBeyond;

        /// <summary>True when sealing this site removes the team's only route. §10: "경로를 차단한다 / 아군도 막힌다."</summary>
        public readonly bool TeamOnlyRoute;

        /// <summary>Builds a request. Prefer the named factories.</summary>
        public EngineerRequest(
            EngineerAction action,
            int siteId,
            Vec3 position,
            int zoneId,
            bool zoneLightOn,
            int teammatesBeyond,
            bool teamOnlyRoute)
        {
            Action = action;
            SiteId = siteId;
            Position = position;
            ZoneId = zoneId;
            ZoneLightOn = zoneLightOn;
            TeammatesBeyond = teammatesBeyond;
            TeamOnlyRoute = teamOnlyRoute;
        }

        /// <summary>문 잠금 at a bottleneck (§12), with whoever is behind it.</summary>
        public static EngineerRequest LockDoor(int siteId, Vec3 position, int teammatesBeyond, bool teamOnlyRoute)
        {
            return new EngineerRequest(EngineerAction.LockDoor, siteId, position, -1, false, teammatesBeyond, teamOnlyRoute);
        }

        /// <summary>차단물 설치. §12 allows one or two per zone, so this is a commitment.</summary>
        public static EngineerRequest Barricade(int siteId, Vec3 position, int teammatesBeyond, bool teamOnlyRoute)
        {
            return new EngineerRequest(EngineerAction.Barricade, siteId, position, -1, false, teammatesBeyond, teamOnlyRoute);
        }

        /// <summary>구역 조명 on or off. §03: the light that lets a clue be read is the light that calls the monster.</summary>
        public static EngineerRequest ZoneLight(int siteId, Vec3 position, int zoneId, bool on)
        {
            return new EngineerRequest(EngineerAction.ZoneLight, siteId, position, zoneId, on, 0, false);
        }

        /// <summary>소음 함정. Who trips it is decided later, by whoever walks into it.</summary>
        public static EngineerRequest NoiseTrap(int siteId, Vec3 position)
        {
            return new EngineerRequest(EngineerAction.NoiseTrap, siteId, position, -1, false, 0, false);
        }

        /// <summary>금고. §08's 금고 속 문서 — 8 s of standing still in a dangerous room.</summary>
        public static EngineerRequest OpenSafe(int siteId, Vec3 position)
        {
            return new EngineerRequest(EngineerAction.OpenSafe, siteId, position, -1, false, 0, false);
        }
    }

    /// <summary>
    /// Something the Engineer has left behind: a locked door, a barricade or an armed
    /// trap. Held so the ability can refuse to install the same thing twice, and so the
    /// host and the HUD can see what the team is now living with.
    /// </summary>
    public readonly struct EngineerInstallation
    {
        /// <summary>Which kind of install this is.</summary>
        public readonly EngineerAction Action;

        /// <summary>Host-assigned site id.</summary>
        public readonly int SiteId;

        /// <summary>Where it is.</summary>
        public readonly Vec3 Position;

        /// <summary>Teammates that were on the far side when it went in. §04.</summary>
        public readonly int TeammatesSealedIn;

        /// <summary>True when this install took the team's only route with it.</summary>
        public readonly bool TeamOnlyRoute;

        /// <summary>Builds an installation record.</summary>
        public EngineerInstallation(EngineerAction action, int siteId, Vec3 position, int teammatesSealedIn, bool teamOnlyRoute)
        {
            Action = action;
            SiteId = siteId;
            Position = position;
            TeammatesSealedIn = teammatesSealedIn;
            TeamOnlyRoute = teamOnlyRoute;
        }
    }

    /// <summary>
    /// What a finished Engineer job did — including the damage. §04's design note is
    /// explicit that the damage is content: "사고가 시스템에서 자동 생산되고, 그게 이
    /// 역할의 정체성이며 방송 콘텐츠의 핵심이다."
    /// </summary>
    public readonly struct EngineerOutcome
    {
        /// <summary>What was completed.</summary>
        public readonly EngineerAction Action;

        /// <summary>Which site.</summary>
        public readonly int SiteId;

        /// <summary>Where.</summary>
        public readonly Vec3 Position;

        /// <summary>Zone affected, for <see cref="EngineerAction.ZoneLight"/>; -1 otherwise.</summary>
        public readonly int ZoneId;

        /// <summary>Resulting state of that zone's light.</summary>
        public readonly bool ZoneLit;

        /// <summary>Teammates now shut on the far side. §04: "문 안에 아군."</summary>
        public readonly int TeammatesSealedIn;

        /// <summary>True when the team's only route is now closed. §10.</summary>
        public readonly bool TeamRouteBlocked;

        /// <summary>
        /// True when this job put a lit zone back into darkness. §04: "조명 끔" — if
        /// someone was mid-clue (§03 needs 2.5 s in the light), they just lost it.
        /// </summary>
        public readonly bool ZoneWentDark;

        /// <summary>정비 자재 spent. Charged on completion, so an abandoned job costs only time (§07).</summary>
        public readonly int MaterialsSpent;

        /// <summary>Builds an outcome record.</summary>
        public EngineerOutcome(
            EngineerAction action,
            int siteId,
            Vec3 position,
            int zoneId,
            bool zoneLit,
            int teammatesSealedIn,
            bool teamRouteBlocked,
            bool zoneWentDark,
            int materialsSpent)
        {
            Action = action;
            SiteId = siteId;
            Position = position;
            ZoneId = zoneId;
            ZoneLit = zoneLit;
            TeammatesSealedIn = teammatesSealedIn;
            TeamRouteBlocked = teamRouteBlocked;
            ZoneWentDark = zoneWentDark;
            MaterialsSpent = materialsSpent;
        }

        /// <summary>True when this job cost the team something. §04 counts that as working correctly.</summary>
        public bool HurtTheTeam
        {
            get { return TeammatesSealedIn > 0 || TeamRouteBlocked || ZoneWentDark; }
        }
    }

    /// <summary>
    /// A 소음 함정 going off. The noise is the effect — §06 moves the monster from 순찰
    /// to 경계 on a sound — so this is what the monster brain consumes.
    /// </summary>
    public readonly struct NoiseTrapTrigger
    {
        /// <summary>Which trap.</summary>
        public readonly int SiteId;

        /// <summary>Where the noise came from.</summary>
        public readonly Vec3 Position;

        /// <summary>Noise level on the same 0~1 scale the Listener is blinded by (§04).</summary>
        public readonly float NoiseLevel;

        /// <summary>Who tripped it, or <see cref="MonsterObservation.NoTarget"/> when the monster did.</summary>
        public readonly int TrippedByPlayerIndex;

        /// <summary>True when the monster tripped it — the intended case, and not the common one.</summary>
        public readonly bool TrippedByMonster;

        /// <summary>Builds a trigger record.</summary>
        public NoiseTrapTrigger(int siteId, Vec3 position, float noiseLevel, int trippedByPlayerIndex, bool trippedByMonster)
        {
            SiteId = siteId;
            Position = position;
            NoiseLevel = noiseLevel;
            TrippedByPlayerIndex = trippedByPlayerIndex;
            TrippedByMonster = trippedByMonster;
        }

        /// <summary>
        /// True when a teammate set it off. §04's third listed accident is "함정에 주자",
        /// and that is a reachable state on purpose.
        /// </summary>
        public bool CaughtTeammate
        {
            get { return !TrippedByMonster && TrippedByPlayerIndex != MonsterObservation.NoTarget; }
        }
    }

    /// <summary>
    /// 정비공 — locks, lights, traps, barricades and safes. §04.
    /// <para>
    /// The constraint that shapes everything here is "시간과 자재. 즉석 사용 불가 —
    /// 사전 준비형." So every action is a job: it is begun, it accrues §04's setup time
    /// tick by tick while the Engineer stays at the site, and only then does anything
    /// happen. There is no path through this object that produces an effect on the tick
    /// it was asked for.
    /// </para>
    /// <para>
    /// Materials are checked when a job starts and spent when it finishes, so walking
    /// away costs time and nothing else. §07 is the reason that is not a loophole:
    /// "시간이 유일한 통화다" — six seconds thrown away on a barricade the team then
    /// abandoned is already the full price.
    /// </para>
    /// <para>
    /// And nothing here protects the team from its Engineer. A door can be locked with
    /// teammates behind it, a trap can be armed where the Runner will come through, and
    /// the zone the others are reading in can be switched off. §04's design note
    /// forbids treating any of that as a bug.
    /// </para>
    /// </summary>
    public sealed class EngineerAbility
    {
        private readonly IWorldProbe _probe;
        private readonly List<EngineerInstallation> _installations = new List<EngineerInstallation>();
        private readonly List<int> _litZones = new List<int>();
        private readonly List<int> _openedSafes = new List<int>();

        private EngineerRequest _job;
        private float _elapsed;
        private float _required;
        private EngineerOutcome _outcome;
        private bool _hasOutcome;

        /// <summary>
        /// Creates the ability with the 정비 자재 the team bought (§08).
        /// </summary>
        /// <param name="probe">World seam. Used to name the zone a job happened in.</param>
        /// <param name="startingMaterials">Materials in hand. Negative is treated as none.</param>
        /// <exception cref="ArgumentNullException"><paramref name="probe"/> is missing.</exception>
        public EngineerAbility(IWorldProbe probe, int startingMaterials)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            _probe = probe;
            Materials = startingMaterials > 0 ? startingMaterials : 0;
            Failure = AbilityFailure.None;
        }

        /// <summary>정비 자재 in hand. §08 sells these at the 지상 차량.</summary>
        public int Materials { get; private set; }

        /// <summary>The job in progress, or <see cref="EngineerAction.None"/>.</summary>
        public EngineerAction CurrentAction
        {
            get { return IsBusy ? _job.Action : EngineerAction.None; }
        }

        /// <summary>True while a job is accruing its setup time.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>Progress on the current job, 0 to 1. The HUD's bar.</summary>
        public float Progress01
        {
            get { return IsBusy && _required > 0f ? MathX.Clamp01(_elapsed / _required) : 0f; }
        }

        /// <summary>Seconds of work left on the current job.</summary>
        public float RemainingSeconds
        {
            get
            {
                if (!IsBusy)
                {
                    return 0f;
                }

                var remaining = _required - _elapsed;
                return remaining > 0f ? remaining : 0f;
            }
        }

        /// <summary>
        /// Why the ability is not doing what was asked:
        /// <see cref="AbilityFailure.NoMaterials"/>, <see cref="AbilityFailure.Busy"/>,
        /// <see cref="AbilityFailure.OutOfRange"/>,
        /// <see cref="AbilityFailure.NothingToActOn"/>,
        /// <see cref="AbilityFailure.Moving"/> when a job was abandoned, or
        /// <see cref="AbilityFailure.None"/>.
        /// </summary>
        public AbilityFailure Failure { get; private set; }

        /// <summary>Everything the Engineer has installed and not yet spent: locked doors, barricades, armed traps.</summary>
        public IReadOnlyList<EngineerInstallation> Installations
        {
            get { return _installations; }
        }

        /// <summary>Zones currently lit by a breaker. §03 — several people can read a clue in one of these.</summary>
        public IReadOnlyList<int> LitZones
        {
            get { return _litZones; }
        }

        /// <summary>Safes opened this match. §08's high-value 문서 come out of these.</summary>
        public IReadOnlyList<int> OpenedSafes
        {
            get { return _openedSafes; }
        }

        /// <summary>True when a finished job is waiting to be picked up by the host.</summary>
        public bool HasPendingOutcome
        {
            get { return _hasOutcome; }
        }

        /// <summary>Restocks at the 지상 차량. §08 — this is one of §03's reasons to walk back out.</summary>
        public void AddMaterials(int count)
        {
            if (count > 0)
            {
                Materials += count;
            }
        }

        /// <summary>
        /// Starts a job. Nothing happens yet — that is the point of §04's 사전 준비형
        /// constraint, and <see cref="Tick"/> is what eventually completes it.
        /// </summary>
        /// <param name="request">What to do and where.</param>
        /// <param name="engineerPosition">Where the Engineer is standing right now.</param>
        /// <param name="failure">Why it could not be started.</param>
        /// <returns>True when the job is now in progress.</returns>
        public bool TryBegin(in EngineerRequest request, Vec3 engineerPosition, out AbilityFailure failure)
        {
            if (request.Action == EngineerAction.None)
            {
                failure = AbilityFailure.NothingToActOn;
                Failure = failure;
                return false;
            }

            if (IsBusy)
            {
                failure = AbilityFailure.Busy;
                Failure = failure;
                return false;
            }

            if (Vec3.Distance(engineerPosition, request.Position) > GameConstants.EngineerReachDistance)
            {
                failure = AbilityFailure.OutOfRange;
                Failure = failure;
                return false;
            }

            if (!IsSomethingToDo(request))
            {
                failure = AbilityFailure.NothingToActOn;
                Failure = failure;
                return false;
            }

            if (Materials < CostOf(request))
            {
                failure = AbilityFailure.NoMaterials;
                Failure = failure;
                return false;
            }

            _job = request;
            _elapsed = 0f;
            _required = EngineerActions.SetupSeconds(request.Action);
            IsBusy = true;
            failure = AbilityFailure.None;
            Failure = AbilityFailure.Busy;
            return true;
        }

        /// <summary>
        /// Steps the current job. The Engineer has to still be there: §04 makes the work
        /// hands-on, so leaving <see cref="GameConstants.EngineerReachDistance"/>
        /// abandons it and the accrued time is gone.
        /// </summary>
        /// <param name="deltaSeconds">Step length. Zero cannot complete a job, because every §04 setup time is positive.</param>
        /// <param name="engineerPosition">Where the Engineer is standing.</param>
        public void Tick(float deltaSeconds, Vec3 engineerPosition)
        {
            var dt = deltaSeconds > 0f ? deltaSeconds : 0f;

            if (!IsBusy)
            {
                return;
            }

            if (Vec3.Distance(engineerPosition, _job.Position) > GameConstants.EngineerReachDistance)
            {
                Abandon(AbilityFailure.Moving);
                return;
            }

            _elapsed += dt;
            if (_elapsed < _required)
            {
                Failure = AbilityFailure.Busy;
                return;
            }

            // Re-check the pool: the shop, a death or another install may have moved it
            // between the start and now. Time already spent is not refunded (§07).
            var cost = CostOf(_job);
            if (Materials < cost)
            {
                Abandon(AbilityFailure.NoMaterials);
                return;
            }

            Complete(cost);
        }

        /// <summary>
        /// Gives up the current job. The time is lost; the materials were never taken.
        /// </summary>
        public void Cancel()
        {
            if (IsBusy)
            {
                Abandon(AbilityFailure.None);
            }
        }

        /// <summary>
        /// Takes the finished job's outcome, once. Modelled as a one-shot handoff so a
        /// host that ticks and reads at different rates cannot apply the same lock, or
        /// the same blackout, twice.
        /// </summary>
        public bool TryTakeOutcome(out EngineerOutcome outcome)
        {
            if (!_hasOutcome)
            {
                outcome = default(EngineerOutcome);
                return false;
            }

            outcome = _outcome;
            _hasOutcome = false;
            return true;
        }

        /// <summary>
        /// Sets off an armed trap. Deliberately indifferent to who walked into it:
        /// §04's third accident is "함정에 주자", and the design note forbids guarding
        /// against it. The trap is consumed either way.
        /// </summary>
        /// <param name="siteId">Which trap.</param>
        /// <param name="playerIndex">Who tripped it. Ignored when <paramref name="byMonster"/> is true.</param>
        /// <param name="byMonster">True when the monster tripped it.</param>
        /// <param name="trigger">The noise event for the monster brain to consume.</param>
        /// <returns>False when no trap is armed at that site.</returns>
        public bool TryTriggerTrap(int siteId, int playerIndex, bool byMonster, out NoiseTrapTrigger trigger)
        {
            var index = IndexOf(EngineerAction.NoiseTrap, siteId);
            if (index < 0)
            {
                trigger = default(NoiseTrapTrigger);
                return false;
            }

            var trap = _installations[index];
            _installations.RemoveAt(index);

            trigger = new NoiseTrapTrigger(
                siteId,
                trap.Position,
                GameConstants.EngineerTrapNoiseLevel,
                byMonster ? MonsterObservation.NoTarget : playerIndex,
                byMonster);
            return true;
        }

        /// <summary>True when that door is locked. §12 puts these on the bottlenecks, so this answers "is the loop cut?".</summary>
        public bool IsDoorLocked(int siteId)
        {
            return IndexOf(EngineerAction.LockDoor, siteId) >= 0;
        }

        /// <summary>True when a barricade stands at that site.</summary>
        public bool IsBarricaded(int siteId)
        {
            return IndexOf(EngineerAction.Barricade, siteId) >= 0;
        }

        /// <summary>True when a trap is armed at that site.</summary>
        public bool IsTrapArmed(int siteId)
        {
            return IndexOf(EngineerAction.NoiseTrap, siteId) >= 0;
        }

        /// <summary>True when that zone's light is on. §03: bright enough for several people to read at once.</summary>
        public bool IsZoneLit(int zoneId)
        {
            return _litZones.Contains(zoneId);
        }

        /// <summary>True when that safe has been opened.</summary>
        public bool IsSafeOpen(int siteId)
        {
            return _openedSafes.Contains(siteId);
        }

        /// <summary>
        /// Drops the job in progress but keeps installations and materials. §03 resets
        /// the monster's state when the team leaves the building — locked doors are not
        /// on that list, and a barricade the team walked away from is still there when
        /// they come back.
        /// </summary>
        public void Reset()
        {
            IsBusy = false;
            _elapsed = 0f;
            _required = 0f;
            _hasOutcome = false;
            _outcome = default(EngineerOutcome);
            Failure = AbilityFailure.None;
        }

        /// <summary>
        /// What a request actually costs. Bringing a zone up costs 정비 자재; cutting one
        /// back to darkness costs nothing but the time to throw the breaker — which is
        /// exactly why §04's "조명 끔" accident is so cheap to have.
        /// </summary>
        public static int CostOf(in EngineerRequest request)
        {
            if (request.Action == EngineerAction.ZoneLight && !request.ZoneLightOn)
            {
                return 0;
            }

            return EngineerActions.MaterialCost(request.Action);
        }

        private bool IsSomethingToDo(in EngineerRequest request)
        {
            switch (request.Action)
            {
                case EngineerAction.LockDoor:
                    return !IsDoorLocked(request.SiteId);
                case EngineerAction.Barricade:
                    return !IsBarricaded(request.SiteId);
                case EngineerAction.NoiseTrap:
                    return !IsTrapArmed(request.SiteId);
                case EngineerAction.OpenSafe:
                    return !IsSafeOpen(request.SiteId);
                case EngineerAction.ZoneLight:
                    return IsZoneLit(request.ZoneId) != request.ZoneLightOn;
                default:
                    return false;
            }
        }

        private void Complete(int cost)
        {
            Materials -= cost;
            IsBusy = false;
            _elapsed = 0f;

            var zoneWentDark = false;
            var zoneLit = false;
            var zoneId = _job.ZoneId;

            switch (_job.Action)
            {
                case EngineerAction.LockDoor:
                case EngineerAction.Barricade:
                    _installations.Add(new EngineerInstallation(
                        _job.Action, _job.SiteId, _job.Position, _job.TeammatesBeyond, _job.TeamOnlyRoute));
                    zoneId = _probe.ZoneIdAt(_job.Position);
                    zoneLit = IsZoneLit(zoneId);
                    break;

                case EngineerAction.NoiseTrap:
                    _installations.Add(new EngineerInstallation(
                        _job.Action, _job.SiteId, _job.Position, 0, false));
                    zoneId = _probe.ZoneIdAt(_job.Position);
                    zoneLit = IsZoneLit(zoneId);
                    break;

                case EngineerAction.ZoneLight:
                    if (_job.ZoneLightOn)
                    {
                        if (!_litZones.Contains(zoneId))
                        {
                            _litZones.Add(zoneId);
                        }

                        zoneLit = true;
                    }
                    else
                    {
                        _litZones.Remove(zoneId);
                        zoneWentDark = true;
                    }

                    break;

                case EngineerAction.OpenSafe:
                    if (!_openedSafes.Contains(_job.SiteId))
                    {
                        _openedSafes.Add(_job.SiteId);
                    }

                    zoneId = _probe.ZoneIdAt(_job.Position);
                    zoneLit = IsZoneLit(zoneId);
                    break;
            }

            _outcome = new EngineerOutcome(
                _job.Action,
                _job.SiteId,
                _job.Position,
                zoneId,
                zoneLit,
                _job.TeammatesBeyond,
                _job.TeamOnlyRoute,
                zoneWentDark,
                cost);
            _hasOutcome = true;
            Failure = AbilityFailure.None;
        }

        private void Abandon(AbilityFailure why)
        {
            IsBusy = false;
            _elapsed = 0f;
            _required = 0f;
            Failure = why;
        }

        private int IndexOf(EngineerAction action, int siteId)
        {
            for (var i = 0; i < _installations.Count; i++)
            {
                if (_installations[i].Action == action && _installations[i].SiteId == siteId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
