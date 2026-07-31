#nullable enable

using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// §03's 목표물 — the thing the match is for.
    /// <para>
    /// Picking it up and putting it down go through <c>MatchDirector</c>, because
    /// §03's three subtractions are enforced in two different core objects at once:
    /// <c>Inventory.TryPickUpObjective</c> refuses while there is 전리품 in the pockets
    /// ("전리품 동시 소지 불가"), and <c>MatchState.TryTakeObjective</c> refuses a second
    /// carrier and records who has it. Both have to move together or the match and the
    /// player's hands disagree, so neither is called from here.
    /// </para>
    /// <para>
    /// The rest of §03's carry — no flashlight, no sprint, 80% speed — is already
    /// implemented where it belongs: <c>PlayerFlashlight</c> switches the beam off
    /// while the loadout says the hands are full, and <c>SpeedResolver</c> reads
    /// <c>MovementContext.CarryingObjective</c>. Nothing is re-implemented here.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectivePropInteractable : Interactable
    {
        private bool _carried;

        /// <summary>Whether it is in a player's hands right now.</summary>
        public bool IsCarried
        {
            get { return _carried; }
        }

        /// <summary>Places the objective at the site the host drew for this match.</summary>
        public static ObjectivePropInteractable Spawn(Vector3 position, Transform? parent)
        {
            var prop = CreateProp(
                "Objective",
                PrimitiveType.Capsule,
                position + (Vector3.up * (PropSize.y * 0.5f)),
                PropSize,
                PropColour);

            if (parent != null)
            {
                prop.transform.SetParent(parent, worldPositionStays: true);
            }

            return prop.AddComponent<ObjectivePropInteractable>();
        }

        /// <inheritdoc />
        public override string Title
        {
            get { return "목표물"; }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                return _carried
                    ? "§03 양손 사용 — 손전등 불가 · 전리품 불가.   다시 누르면 내려놓는다."
                    : "§03 양손을 쓴다 — 손전등을 들 수 없고, 전리품도 들 수 없다.";
            }
        }

        /// <inheritdoc />
        public override void OnPressed(PlayerInteractor by)
        {
            var director = by.Director;
            if (director == null)
            {
                return;
            }

            if (_carried)
            {
                if (director.TryDropObjective(by.transform.position, out var dropRefusal))
                {
                    Detach(by);
                    Refusal = string.Empty;
                    return;
                }

                Refusal = dropRefusal;
                return;
            }

            if (!director.TryTakeObjective(out var refusal))
            {
                Refusal = refusal;
                return;
            }

            Attach(by);
            Refusal = string.Empty;
        }

        /// <summary>Puts it back on the floor without a key press — a death, or the match ending.</summary>
        public void ForceDrop(PlayerInteractor by, Vector3 where)
        {
            if (!_carried)
            {
                return;
            }

            Detach(by);
            transform.position = where + (Vector3.up * (PropSize.y * 0.5f));
        }

        /// <summary>Takes it out of the world — it left the building with somebody (§02 목표물 회수).</summary>
        public void Recovered()
        {
            _carried = false;
            gameObject.SetActive(false);
        }

        private void Attach(PlayerInteractor by)
        {
            _carried = true;
            by.SetCarriedFocus(this);

            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                // Off while it is in the hands, or it would fill the crosshair and the
                // player could never look at anything else.
                collider.enabled = false;
            }

            var anchor = by.CarryAnchor;
            if (anchor != null)
            {
                transform.SetParent(anchor, worldPositionStays: false);
                transform.localPosition = Vector3.forward * CarryOffset;
                transform.localRotation = Quaternion.identity;
            }
        }

        private void Detach(PlayerInteractor by)
        {
            _carried = false;
            by.ClearCarriedFocus(this);

            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = true;
            }

            transform.SetParent(null, worldPositionStays: true);
        }

        // Rig geometry: something two hands are full of.
        private static readonly Vector3 PropSize = new Vector3(0.45f, 0.45f, 0.45f);
        private static readonly Color PropColour = new Color(0.55f, 0.78f, 0.72f);
        private const float CarryOffset = 0.7f;
    }
}
