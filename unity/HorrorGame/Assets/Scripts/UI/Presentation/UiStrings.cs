#nullable enable

using HorrorGame.Core.Clues;
using HorrorGame.Core.Economy;
using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;
using HorrorGame.Core.Threat;

namespace HorrorGame.UI
{
    /// <summary>
    /// Every word the interface says, in one place.
    /// <para>
    /// The design document is written in Korean and its wording is load-bearing —
    /// §08's 대가 column is the thing that makes the shop a §10 dilemma rather than
    /// a list of upgrades, so the shop quotes it rather than paraphrasing. Keeping
    /// the strings together is also what makes a later localisation pass a change to
    /// one file instead of a change to every screen.
    /// </para>
    /// <para>
    /// Nothing here is a game rule. Every method is a total function from a core
    /// enum to a label, so a value the design adds later shows up as a legible
    /// fallback instead of an exception in the middle of a match.
    /// </para>
    /// </summary>
    public static class UiStrings
    {
        /// <summary>Shown wherever a value exists but must not be displayed — see <see cref="Readouts.ClockReadout"/> for the §07 case.</summary>
        public const string Unknown = "—";

        // ------------------------------------------------------------------
        // §04 / §11 — roles.
        // ------------------------------------------------------------------

        /// <summary>§04's name for a role.</summary>
        public static string Role(RoleId role)
        {
            switch (role)
            {
                case RoleId.Listener: return "청음사";
                case RoleId.Observer: return "관측자";
                case RoleId.Runner: return "주자";
                case RoleId.Engineer: return "정비공";
                case RoleId.Flasher: return "섬광수";
                default: return "미정";
            }
        }

        /// <summary>§04's 능력 row, one line. The lobby shows it beside <see cref="RoleLimit"/> so the pick reads as a trade.</summary>
        public static string RoleAbility(RoleId role)
        {
            switch (role)
            {
                case RoleId.Listener: return "소리로 괴물의 위치 · 거리 · 이동 방향을 파악";
                case RoleId.Observer: return "괴물의 시야를 본다 — 누가 표적인지";
                case RoleId.Runner: return "질주로 괴물의 어그로를 끌어낸다";
                case RoleId.Engineer: return "문 잠금 · 구역 조명 · 소음 함정 · 차단물";
                case RoleId.Flasher: return "괴물을 짧게 기절시킨다 (재사용 가능)";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// §04's 제약 row. Displayed with the same weight as the ability, because
        /// §10's whole map is "얻으려면 위험을 만들어야 한다" and a lobby that showed
        /// only the upside would be advertising, not information.
        /// </summary>
        public static string RoleLimit(RoleId role)
        {
            switch (role)
            {
                case RoleId.Listener: return "자기가 소리를 내면 못 듣는다";
                case RoleId.Observer: return "괴물 15m 이내에서 3초간 이동 정지";
                case RoleId.Runner: return "스태미나 12초 — 어그로를 풀 지점을 골라야 한다";
                case RoleId.Engineer: return "시간과 자재 — 즉석 사용 불가";
                case RoleId.Flasher: return "효과가 약하다 — 쿨타임을 견뎌야 한다";
                default: return string.Empty;
            }
        }

        /// <summary>§11's 그 판의 난제 column — what this match is short of.</summary>
        public static string RoleAbsenceProblem(RoleId missingRole)
        {
            switch (missingRole)
            {
                case RoleId.Listener: return "괴물의 위치를 모른다 — 공포 최대";
                case RoleId.Observer: return "누가 표적인지 모른다";
                case RoleId.Runner: return "괴물을 끌 수 없다";
                case RoleId.Engineer: return "단서 읽기가 훨씬 위험해진다";
                case RoleId.Flasher: return "저지 수단이 없다";
                default: return string.Empty;
            }
        }

        /// <summary>§11's 돈으로 메우기 column, as a name.</summary>
        public static string Substitute(RoleSubstituteItem substitute)
        {
            switch (substitute)
            {
                case RoleSubstituteItem.Detector: return "감지기";
                case RoleSubstituteItem.Bait: return "미끼";
                case RoleSubstituteItem.Flare: return "조명탄";
                case RoleSubstituteItem.Flashbang: return "섬광탄";
                default: return "불가능";
            }
        }

        /// <summary>Why §11's substitute is worse than the role. It always is — that is how §11 keeps roles worth picking.</summary>
        public static string SubstituteLimit(RoleSubstituteItem substitute)
        {
            switch (substitute)
            {
                case RoleSubstituteItem.Detector: return "작동 시 소리를 낸다";
                case RoleSubstituteItem.Bait: return "1회용 — 위치를 미리 정해야 한다";
                case RoleSubstituteItem.Flare: return "1회용 · 구역 하나";
                case RoleSubstituteItem.Flashbang: return "소모품 · 수량 제한";
                default: return "살 수 없는 정보다";
            }
        }

        /// <summary>§11's completability rows, for the lobby's "이 조합으로 끝낼 수 있는가".</summary>
        public static string Capability(MatchCapability capability)
        {
            switch (capability)
            {
                case MatchCapability.ClueLighting: return "단서에 빛을 대기";
                case MatchCapability.ObjectiveEscort: return "목표물 호송";
                case MatchCapability.MonsterLocation: return "괴물의 위치";
                case MatchCapability.TargetIdentity: return "표적 식별";
                case MatchCapability.AggroPull: return "어그로 유인";
                case MatchCapability.TemporaryDenial: return "일시적 저지";
                case MatchCapability.SafeAccess: return "금고 개방";
                default: return Unknown;
            }
        }

        /// <summary>How a capability is being met — §11's difference between a role, a fallback and a purchase.</summary>
        public static string Coverage(CapabilityCoverage coverage)
        {
            switch (coverage)
            {
                case CapabilityCoverage.Role: return "직업";
                case CapabilityCoverage.UniversalIssue: return "전원 기본";
                case CapabilityCoverage.PurchasedSubstitute: return "구매 대체";
                default: return "없음";
            }
        }

        // ------------------------------------------------------------------
        // §07 — the clock.
        // ------------------------------------------------------------------

        /// <summary>§07's 시각 column. The HUD never shows a number — see <see cref="Readouts.ClockReadout"/>.</summary>
        public static string Phase(NightPhase phase)
        {
            switch (phase)
            {
                case NightPhase.EarlyEvening: return "초저녁";
                case NightPhase.Night: return "밤";
                case NightPhase.LateNight: return "심야";
                case NightPhase.PreDawn: return "새벽";
                case NightPhase.BeforeSunrise: return "동트기 전";
                default: return Unknown;
            }
        }

        /// <summary>What the phase means for the people underground. §07's 추가 column, where it has one.</summary>
        public static string PhaseWarning(NightPhase phase)
        {
            switch (phase)
            {
                case NightPhase.LateNight: return "손전등 반경이 좁아졌다";
                case NightPhase.PreDawn: return "괴물이 출입구를 안다";
                case NightPhase.BeforeSunrise: return "생존 불가 수준";
                default: return string.Empty;
            }
        }

        /// <summary>Why the time is legible right now. Shown so the 회중시계 reads as the thing that bought it (§08).</summary>
        public static string ClockSource(bool onSurface, bool pocketWatch)
        {
            if (onSurface)
            {
                return "지상";
            }

            return pocketWatch ? "회중시계" : string.Empty;
        }

        // ------------------------------------------------------------------
        // §08 — the shop.
        // ------------------------------------------------------------------

        /// <summary>§08's 아이템 column.</summary>
        public static string Item(ShopItemId item)
        {
            switch (item)
            {
                case ShopItemId.Battery: return "배터리";
                case ShopItemId.RepairMaterials: return "정비 자재";
                case ShopItemId.FirstAidKit: return "응급킷";
                case ShopItemId.UpgradedFlashlight: return "강화 손전등";
                case ShopItemId.Flare: return "조명탄";
                case ShopItemId.Chalk: return "분필";
                case ShopItemId.Rope: return "밧줄";
                case ShopItemId.Bag: return "가방";
                case ShopItemId.Blueprint: return "건물 도면";
                case ShopItemId.PocketWatch: return "회중시계";
                case ShopItemId.Detector: return "감지기";
                case ShopItemId.Bait: return "미끼";
                case ShopItemId.Suppressor: return "소음기";
                default: return Unknown;
            }
        }

        /// <summary>§08's 효과 column, verbatim.</summary>
        public static string ItemEffect(ShopItemId item)
        {
            switch (item)
            {
                case ShopItemId.Battery: return "손전등 지속";
                case ShopItemId.RepairMaterials: return "문 · 함정 · 차단물";
                case ShopItemId.FirstAidKit: return "부상 회복";
                case ShopItemId.UpgradedFlashlight: return "반경 2배";
                case ShopItemId.Flare: return "구역을 밝힌다 (정비공 없이)";
                case ShopItemId.Chalk: return "지나온 길 표시";
                case ShopItemId.Rope: return "층 사이 지름길";
                case ShopItemId.Bag: return "전리품 수용 +5";
                case ShopItemId.Blueprint: return "맵 일부 공개";
                case ShopItemId.PocketWatch: return "안에서 시각 확인";
                case ShopItemId.Detector: return "괴물 방향 표시";
                case ShopItemId.Bait: return "괴물을 특정 지점으로 유인";
                case ShopItemId.Suppressor: return "발소리 감소";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// §08's 대가 column, verbatim. Empty only where §08 itself writes "—".
        /// <para>
        /// This is the string the shop exists to show. §08 opens its list with "전부
        /// §10 딜레마 원리를 따른다 — 얻는 게 있으면 대가가 있다", so a row rendered
        /// without its price is a row that lies about what the item is.
        /// </para>
        /// </summary>
        public static string ItemPrice(ShopItemId item)
        {
            switch (item)
            {
                case ShopItemId.UpgradedFlashlight: return "괴물이 2배 멀리서 본다";
                case ShopItemId.Flare: return "1회용 · 소리를 낸다";
                case ShopItemId.Chalk: return "괴물도 흔적을 따라온다";
                case ShopItemId.Rope: return "편도만";
                case ShopItemId.Bag: return "이동 속도 −10%";
                case ShopItemId.Blueprint: return "비쌈";
                case ShopItemId.Detector: return "작동 시 소리를 낸다";
                case ShopItemId.Bait: return "1회용 · 비쌈";
                case ShopItemId.Suppressor: return "자기도 못 듣게 됨 → 청음사 무효";
                default: return string.Empty;
            }
        }

        /// <summary>§08's 분류 column.</summary>
        public static string Category(ShopCategory category)
        {
            switch (category)
            {
                case ShopCategory.Consumable: return "소모품";
                case ShopCategory.Exploration: return "탐험";
                case ShopCategory.Information: return "정보";
                case ShopCategory.Danger: return "위험";
                default: return Unknown;
            }
        }

        /// <summary>Why a purchase did not happen, in the words the team will use about it.</summary>
        public static string Purchase(PurchaseOutcome outcome)
        {
            switch (outcome)
            {
                case PurchaseOutcome.Purchased: return "구매";
                case PurchaseOutcome.NotEnoughCredits: return "크레딧 부족";
                case PurchaseOutcome.UnknownItem: return "없는 물건";
                default: return "잘못된 수량";
            }
        }

        // ------------------------------------------------------------------
        // §03 — clues. Marks and interruptions only; never content.
        // ------------------------------------------------------------------

        /// <summary>Which link of §03's chain the mark belongs to. A player may see the kind of note; the note itself is read, not stored.</summary>
        public static string ClueLayerName(ClueLayer layer)
        {
            switch (layer)
            {
                case ClueLayer.Feature: return "층의 성질";
                case ClueLayer.FloorMapping: return "성질 → 층";
                case ClueLayer.SitePin: return "지점 표식";
                default: return Unknown;
            }
        }

        /// <summary>§03's 층의 성질 vocabulary — words, so the four confusion pairs cannot touch them.</summary>
        public static string Feature(FloorFeature feature)
        {
            switch (feature)
            {
                case FloorFeature.Water: return "물";
                case FloorFeature.Machinery: return "기계";
                case FloorFeature.Cold: return "냉기";
                case FloorFeature.Rust: return "녹";
                case FloorFeature.Collapse: return "붕괴";
                default: return Unknown;
            }
        }

        /// <summary>
        /// Why the read stopped. §03 makes darkness the lock, so
        /// <see cref="ClueReadInterrupt.LightLost"/> is worded as a loss rather than
        /// as a hint to try again — the progress is gone, not paused.
        /// </summary>
        public static string Interrupt(ClueReadInterrupt interrupt)
        {
            switch (interrupt)
            {
                case ClueReadInterrupt.LightLost: return "빛을 잃었다";
                case ClueReadInterrupt.Moved: return "움직였다";
                case ClueReadInterrupt.OutOfRange: return "너무 멀다";
                case ClueReadInterrupt.NoTarget: return "표식에서 눈을 뗐다";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // §09 — the ghost.
        // ------------------------------------------------------------------

        /// <summary>Why the rattle did not land. §09 distinguishes the two, because only one of them is worth moving to fix.</summary>
        public static string RattleFailure(HorrorGame.Core.Ghost.GhostSignalFailure failure)
        {
            switch (failure)
            {
                case HorrorGame.Core.Ghost.GhostSignalFailure.OnCooldown: return "아직 흔들 수 없다";
                case HorrorGame.Core.Ghost.GhostSignalFailure.OutOfRange: return "너무 멀다";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // §02 — the four endings.
        // ------------------------------------------------------------------

        /// <summary>§02's 결과 column.</summary>
        public static string Outcome(MatchOutcome outcome)
        {
            switch (outcome)
            {
                case MatchOutcome.FullVictory: return "완전 승리";
                case MatchOutcome.PartialVictory: return "부분 승리";
                case MatchOutcome.Survived: return "생존";
                case MatchOutcome.Wiped: return "패배";
                case MatchOutcome.Abandoned: return "세션 종료";
                default: return "진행 중";
            }
        }

        /// <summary>§02's 조건 column — the sentence that distinguishes this ending from the one above it.</summary>
        public static string OutcomeCondition(MatchOutcome outcome)
        {
            switch (outcome)
            {
                case MatchOutcome.FullVictory: return "목표물 회수 + 전원 탈출";
                case MatchOutcome.PartialVictory: return "목표물 회수 + 일부 탈출";
                case MatchOutcome.Survived: return "목표물 없이 탈출";
                case MatchOutcome.Wiped: return "전멸";
                case MatchOutcome.Abandoned: return "호스트 이탈 — 마이그레이션 없음";
                default: return string.Empty;
            }
        }
    }
}
