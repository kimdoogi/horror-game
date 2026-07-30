using HorrorGame.Core.Math;

namespace HorrorGame.Core.Clues
{
    /// <summary>
    /// Which link of §03's chain a clue is.
    /// <para>
    /// The three layers are the chain, and §03 fixes the way they combine
    /// ("단서 조합 방식 — 고정"): a property, a mapping from that property to a floor,
    /// and a sign that pins a site on it. Only one of the three can be acted on
    /// alone, and it is the last one — which is why the layout puts it on the
    /// objective's own floor, where it is no use until the first two are in hand.
    /// </para>
    /// </summary>
    public enum ClueLayer
    {
        /// <summary>Not a clue.</summary>
        None = 0,

        /// <summary>"그것은 물이 있는 층에 있다" — names a property, no location. §03.</summary>
        Feature = 1,

        /// <summary>"물이 있는 층은 지하 3층이다" — maps a property onto a floor sign. §03.</summary>
        FloorMapping = 2,

        /// <summary>"ㅁ-6 좌" — pins one candidate site on a floor. §03.</summary>
        SitePin = 3,
    }

    /// <summary>
    /// How a mark is written and mounted — the half of a misread that belongs to the
    /// world rather than to the reader.
    /// <para>
    /// Internal on purpose. This is clue content: knowing that a given clue is hung
    /// upside down tells a client which glyph on it is worth a second guess, and
    /// ARCHITECTURE §4 puts clue content on the host. Nothing outside the core
    /// assembly can name this type, so no <c>NetworkBehaviour</c> can serialise it
    /// even by accident.
    /// </para>
    /// <para>
    /// §03: "혼동쌍 — 의도적으로 심는다." Every clue is planted with exactly one of the
    /// four conditions, drawn uniformly, so the obstacles are as fixed and learnable
    /// as the pairs themselves while which clue carries which varies per match.
    /// </para>
    /// </summary>
    internal readonly struct ClueInscription
    {
        /// <summary>The mark is handwritten. Opens §03's 1↔7 pair.</summary>
        internal readonly bool Handwritten;

        /// <summary>The mark is only readable as a reflection. Opens §03's 좌↔우 pair.</summary>
        internal readonly bool OnReflectiveSurface;

        /// <summary>Wear the mark already carries, 0–1. §03's "낡아서 지워진 부분". Opens ㅁ↔ㅇ.</summary>
        internal readonly float WornBlur;

        /// <summary>How the mark hangs, degrees out of upright. 180 opens §03's 6↔9 pair.</summary>
        internal readonly float MountAngleDegrees;

        /// <summary>Creates an inscription.</summary>
        internal ClueInscription(
            bool handwritten, bool onReflectiveSurface, float wornBlur, float mountAngleDegrees)
        {
            Handwritten = handwritten;
            OnReflectiveSurface = onReflectiveSurface;
            WornBlur = MathX.Clamp01(wornBlur);
            MountAngleDegrees = MathX.NormalizeAngle(mountAngleDegrees);
        }

        /// <summary>
        /// Combines the mark with one reader's look at it.
        /// <para>
        /// The angles add, so a reader who tilts their view to match an upside-down
        /// sign cancels the 6↔9 condition — the one counter-play §03's table implies
        /// and never states. The blurs do not add: the worse of wear and haze
        /// decides, because a clean mark seen through smoke is exactly as hard to
        /// read as a worn one seen clearly, and adding them would let two mild
        /// obstacles make a clue illegible that neither could.
        /// </para>
        /// </summary>
        internal GlyphViewing ViewedBy(ClueObservation observation)
        {
            var blur = System.Math.Max(WornBlur, observation.Blur);

            return new GlyphViewing(
                MountAngleDegrees + observation.ViewAngleDegrees,
                blur,
                observation.SecondsHeld,
                observation.WorstLightQuality,
                Handwritten,
                OnReflectiveSurface);
        }
    }

    /// <summary>
    /// One clue as it exists in the basement: where it is, which link of the chain
    /// it is, how it is written, and what it says.
    /// <para>
    /// Internal, and a class rather than a struct, both for the same reason. §13:
    /// "단서 내용 · 목표물 위치 — 호스트만 보유. 클라이언트에 보내면 메모리에서 읽힌다."
    /// An internal type cannot be named by the Net assembly, so it cannot appear in
    /// a <c>SyncVar</c>, a message struct or a serialiser; and a reference type is
    /// not blittable, so no amount of generic marshalling will pick it up either.
    /// The only way content leaves the host is
    /// <see cref="ObjectiveResolver.TryRead"/>, which returns what a specific reader
    /// perceived rather than what is written.
    /// </para>
    /// <para>
    /// There is deliberately no method here that produces an item, a note, a
    /// journal entry or any other lasting record. §03: "단서를 가지고 나올 수 없다.
    /// 그 자리에서 보고, 기억해서, 말로 전달해야 한다." The absence is the feature.
    /// </para>
    /// </summary>
    internal sealed class ClueDef
    {
        internal ClueDef(
            int clueId,
            ClueLayer layer,
            int zoneId,
            int floorIndex,
            Vec3 position,
            ClueInscription inscription,
            FloorFeature feature,
            ClueGlyph floorNumber,
            SiteLabel label,
            bool isDecoy)
        {
            ClueId = clueId;
            Layer = layer;
            ZoneId = zoneId;
            FloorIndex = floorIndex;
            Position = position;
            Inscription = inscription;
            Feature = feature;
            FloorNumber = floorNumber;
            Label = label;
            IsDecoy = isDecoy;
        }

        /// <summary>Stable per-match identifier. Safe to send — it names a prop, not its content.</summary>
        internal int ClueId { get; }

        /// <summary>Which link of §03's chain this is.</summary>
        internal ClueLayer Layer { get; }

        /// <summary>Zone the clue is in. §12.</summary>
        internal int ZoneId { get; }

        /// <summary>Floor the clue is on.</summary>
        internal int FloorIndex { get; }

        /// <summary>Where the clue is.</summary>
        internal Vec3 Position { get; }

        /// <summary>How the mark is written and hung.</summary>
        internal ClueInscription Inscription { get; }

        /// <summary>
        /// The property named. On <see cref="ClueLayer.Feature"/> it means "the
        /// objective is on the floor with this property"; on
        /// <see cref="ClueLayer.FloorMapping"/> it means "the floor with this
        /// property is the one signed <see cref="FloorNumber"/>". One field, two
        /// grammars — which is exactly why a team needs both clues and why holding
        /// only the second one feels like holding nothing.
        /// </summary>
        internal FloorFeature Feature { get; }

        /// <summary>The floor sign named, on <see cref="ClueLayer.FloorMapping"/>.</summary>
        internal ClueGlyph FloorNumber { get; }

        /// <summary>The site sign named, on <see cref="ClueLayer.SitePin"/>.</summary>
        internal SiteLabel Label { get; }

        /// <summary>
        /// True for a mapping clue about a property the objective's floor does not
        /// have. It is a true statement and a dead end, and it is where §03's
        /// "운이 나쁘면 5번" comes from — a team can only tell a decoy from the real
        /// mapping by already holding the first clue.
        /// </summary>
        internal bool IsDecoy { get; }
    }
}
