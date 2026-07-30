#nullable enable

using System;
using HorrorGame.Core.Clues;
using HorrorGame.Core.Math;
using HorrorGame.Core.Session;
using UnityEngine;

namespace HorrorGame.Net.Host
{
    /// <summary>
    /// The host's side of §03, and the only place in the Net assembly that is
    /// allowed to name a clue type.
    /// <para>
    /// <b>The one output is a sentence.</b> <see cref="TryRenderRead"/> takes a
    /// reader's conditions and returns a <c>string</c> — already rendered, already
    /// filtered through <c>MisreadModel</c>, describing one clue. There is no
    /// overload that returns a <c>ClueReport</c>, a <c>SiteLabel</c> or a
    /// <c>ClueGlyph</c>, so the Net layer has no structured form of the answer to
    /// accidentally put in a SyncVar. ARCHITECTURE §4 asked for the host to reply
    /// "with the rendered glyph for <em>that</em> clue only"; this is that reply, and
    /// deliberately nothing that could become a second one.
    /// </para>
    /// <para>
    /// <b>The objective's position is a push, never a pull.</b>
    /// <see cref="TryPlaceObjective"/> forwards <c>ObjectiveResolver</c>'s one-shot
    /// callback and keeps nothing. No property on this class returns it. The core
    /// went to the trouble of having no getter for it; storing the value here would
    /// undo that in one field.
    /// </para>
    /// <para>
    /// §13: "단서 내용 · 목표물 위치 — 호스트만 보유. 클라이언트에 보내면 메모리에서
    /// 읽힌다."
    /// </para>
    /// </summary>
    [HostOnly]
    public sealed class HostClueAuthority
    {
        private readonly ObjectiveResolver _resolver;
        private readonly IRandomSource _rng;

        /// <summary>
        /// Wraps the match's resolver.
        /// </summary>
        /// <param name="resolver">Built once per match, on the host, from a seed clients never see.</param>
        /// <param name="rng">
        /// Drives <c>MisreadModel</c>. Separate from the layout's stream on purpose:
        /// §03 says two reads of the same clue may differ, so misreads must not
        /// consume draws that the layout's reproducibility depends on.
        /// </param>
        public HostClueAuthority(ObjectiveResolver resolver, IRandomSource rng)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>How many clues this match has. A count, not a content.</summary>
        public int ClueCount => _resolver.ClueCount;

        /// <summary>Clues legibly read so far. §13's match summary.</summary>
        public int CluesRead => _resolver.CluesRead;

        /// <summary>
        /// Reads that produced at least one wrong mark. §03 wants this non-zero —
        /// "이 게임의 주된 웃음이자 사망 원인" — and it must stay host-side, because
        /// telling a reader they misread would delete the mechanic.
        /// </summary>
        public int Misreads => _resolver.Misreads;

        /// <summary>
        /// Whether the layout the host built is winnable: a team that reads every
        /// clue correctly must end at exactly one site, and it must be the right one.
        /// The host refuses to start a match that fails this.
        /// </summary>
        public bool VerifyChainConverges() => _resolver.VerifyChainConverges();

        /// <summary>
        /// Where the clue props go, for the host's level builder. Positions only —
        /// <c>ClueMarker</c> carries no content by construction, and §03 is fine with
        /// a client seeing that there is a piece of paper on the desk, because a
        /// player standing in the room can see that too.
        /// </summary>
        public bool TryGetMarkerPosition(int clueId, out Vector3 position)
        {
            var markers = _resolver.Markers;
            for (var i = 0; i < markers.Count; i++)
            {
                if (markers[i].ClueId != clueId)
                {
                    continue;
                }

                position = ToUnity(markers[i].Position);
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>Every clue's id, so the host can spawn one prop per clue.</summary>
        public int[] ClueIds()
        {
            var markers = _resolver.Markers;
            var ids = new int[markers.Count];
            for (var i = 0; i < markers.Count; i++)
            {
                ids[i] = markers[i].ClueId;
            }

            return ids;
        }

        /// <summary>
        /// Hands the objective's position to the host's level builder exactly once,
        /// and forgets it.
        /// <para>
        /// A pass-through, on purpose. The value arrives in a callback and leaves in
        /// the same expression; there is no field, no property and no second call.
        /// Anything that needs the position later must keep what it built, which is
        /// the same discipline <c>ObjectiveResolver.TryPlaceObjective</c> imposes and
        /// for the same reason.
        /// </para>
        /// </summary>
        public bool TryPlaceObjective(Action<Vector3> spawn)
        {
            if (spawn == null)
            {
                throw new ArgumentNullException(nameof(spawn));
            }

            return _resolver.TryPlaceObjective(position => spawn(ToUnity(position)));
        }

        /// <summary>Whether a site the host is already asking about is the objective's. A predicate, never an accessor.</summary>
        public bool IsObjectiveSite(int siteId) => _resolver.IsObjectiveSite(siteId);

        /// <summary>
        /// Answers one read with one sentence.
        /// <para>
        /// The parameters are a reader's circumstances, all measured by the host or
        /// reported as raw sensory conditions: how long the beam was held, how well
        /// lit the mark was, from what angle, how worn it is. §03's obstacles —
        /// "손전등의 좁은 빛 · 낡아서 지워진 부분 · 급하게 봐야 하는 상황" — are all
        /// expressed in exactly these four numbers, which is why nothing about the
        /// clue itself needs to come in.
        /// </para>
        /// <para>
        /// Returns false for an illegible read, with an empty line. §03: a failed
        /// read leaves the team with nothing rather than with something wrong.
        /// </para>
        /// </summary>
        public bool TryRenderRead(
            int clueId,
            float secondsHeld,
            float worstLightQuality,
            float viewAngleDegrees,
            float blur,
            out string line)
        {
            var observation = new ClueObservation(secondsHeld, worstLightQuality, viewAngleDegrees, blur);

            if (!_resolver.TryRead(clueId, observation, _rng, out var report) || !report.Legible)
            {
                line = string.Empty;
                return false;
            }

            line = Render(report);
            return line.Length > 0;
        }

        /// <summary>
        /// Turns one reader's belief into the sentence they read off the wall.
        /// <para>
        /// The phrasings are §03's own worked examples — "그것은 물이 있는 층에 있다",
        /// "물이 있는 층은 지하 3층이다" — and the site pin is rendered as the sign
        /// itself, "ㅁ-6 좌". Rendering here rather than on the client is the point of
        /// the whole class: a client that received the marks would hold the answer in
        /// a form it could recombine, and §03's constraint is that the only durable
        /// copy is in a player's head.
        /// </para>
        /// </summary>
        private static string Render(ClueReport report)
        {
            switch (report.Layer)
            {
                case ClueLayer.Feature:
                    return "그것은 " + FeaturePhrase(report.Feature) + " 층에 있다";

                case ClueLayer.FloorMapping:
                    return FeaturePhrase(report.Feature) + " 층은 지하 "
                           + ClueGlyphs.Render(report.FloorNumber) + "층이다";

                case ClueLayer.SitePin:
                    return report.Label.ToString();

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// §03's floor properties in the words the design document uses. Not a
        /// localisation table — these are the strings the design names, and a
        /// localiser replacing them has to keep §03's four confusion pairs intact,
        /// which is a decision, not a translation.
        /// </summary>
        private static string FeaturePhrase(FloorFeature feature)
        {
            switch (feature)
            {
                case FloorFeature.Water: return "물이 있는";
                case FloorFeature.Machinery: return "기계가 도는";
                case FloorFeature.Cold: return "냉기가 도는";
                case FloorFeature.Rust: return "녹이 슨";
                case FloorFeature.Collapse: return "무너진";
                default: return "알 수 없는";
            }
        }

        private static Vector3 ToUnity(Vec3 value) => new Vector3(value.X, value.Y, value.Z);
    }

    /// <summary>
    /// Where the host keeps the match's answers, and where a client keeps nothing.
    /// <para>
    /// Static because there is one match per process and because the level builder,
    /// the clue terminals and the match summary all need the same instance without
    /// threading a reference through four layers of prefab wiring.
    /// </para>
    /// <para>
    /// <see cref="Install"/> refuses to run on a client. That check is cheap and it
    /// is the last line of defence: if a future refactor ever tried to construct the
    /// answers on a client — from a seed, say, which is the classic way this rule
    /// gets broken by accident — it fails loudly instead of quietly working.
    /// </para>
    /// </summary>
    [HostOnly]
    public static class HostSecrets
    {
        /// <summary>The match's clue authority, or null when this process is not hosting.</summary>
        public static HostClueAuthority? Clues { get; private set; }

        /// <summary>True when this process holds the answers.</summary>
        public static bool Installed => Clues != null;

        /// <summary>
        /// Installs the match's answers. Refused, with an error, on a machine that is
        /// a client and not a server.
        /// </summary>
        /// <returns>False when refused.</returns>
        public static bool Install(HostClueAuthority authority, bool isServer)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            if (!isServer)
            {
                Debug.LogError(
                    "[Net] Refused to install clue answers on a machine that is not the server. "
                    + "§13: 단서 내용 · 목표물 위치 — 호스트만 보유.");
                return false;
            }

            Clues = authority;
            return true;
        }

        /// <summary>Forgets the answers. Called when a session ends, and by tests.</summary>
        public static void Clear()
        {
            Clues = null;
        }
    }
}
