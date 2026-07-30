namespace HorrorGame.Core.Economy
{
    /// <summary>
    /// §08's 구매 목록, complete and in the document's own order.
    /// <para>
    /// The list is closed on purpose. §08 opens the table with "전부 §10 딜레마
    /// 원리를 따른다 — 얻는 게 있으면 대가가 있다", so an item with a benefit and no
    /// cost does not belong in this enum; adding one is a design change, not a
    /// content addition.
    /// </para>
    /// </summary>
    public enum ShopItemId
    {
        /// <summary>Nothing. Lookups that fail return this rather than a valid item.</summary>
        None = 0,

        /// <summary>배터리 — 손전등 지속. §08's only item with no drawback, because §03 made the flashlight universal issue.</summary>
        Battery = 1,

        /// <summary>정비 자재 — 문 · 함정 · 차단물. §04's Engineer cannot act without it.</summary>
        RepairMaterials = 2,

        /// <summary>응급킷 — 부상 회복. §03 restricts injury recovery to the surface, so this is what makes a survived grab survivable twice.</summary>
        FirstAidKit = 3,

        /// <summary>강화 손전등 — 반경 2배, 괴물이 2배 멀리서 본다. §08 calls it "이 목록의 대표작".</summary>
        UpgradedFlashlight = 4,

        /// <summary>조명탄 — lights a zone with no Engineer. 1회용, and it makes noise. §11's stand-in for a missing 정비공.</summary>
        Flare = 5,

        /// <summary>분필 — marks the way you came, and the monster follows the marks.</summary>
        Chalk = 6,

        /// <summary>밧줄 — a shortcut between floors, 편도만. §08 prices the five minutes it saves.</summary>
        Rope = 7,

        /// <summary>가방 — 전리품 수용 +5, 이동 속도 −10%. The purest §10 trade in the list.</summary>
        Bag = 8,

        /// <summary>건물 도면 — reveals part of the map. §08 marks it 비쌈, and §12's fixed layout means it is a crutch, not a strategy.</summary>
        Blueprint = 9,

        /// <summary>회중시계 — tells the time underground. §07 otherwise reveals the hour only on the surface.</summary>
        PocketWatch = 10,

        /// <summary>감지기 — points at the monster, and makes noise doing it. §11's stand-in for a missing 청음사.</summary>
        Detector = 11,

        /// <summary>미끼 — pulls the monster to a chosen spot. 1회용 · 비쌈. §11's stand-in for a missing 주자.</summary>
        Bait = 12,

        /// <summary>소음기 — quieter footsteps, and "자기도 못 듣게 됨 → 청음사 무효". The item that deletes a role.</summary>
        Suppressor = 13,
    }
}
