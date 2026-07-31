#nullable enable

using System.Globalization;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// The 지상 차량. §08: "지상 차량 = 안전 지대 + 상점 + 보급소."
    /// <para>
    /// <b>Most of what the vehicle does needs no key.</b> Walking into its apron is
    /// what surfaces the team, and surfacing is what sells the 전리품, tops the cell up
    /// at §03's 지상 발전기 and opens the shop — <c>MatchDirector</c> does all of that on
    /// the rising edge, because §08 describes loading loot into the vehicle as the
    /// consequence of arriving rather than as an errand.
    /// </para>
    /// <para>
    /// <b>The key opens the shelf, and nothing else.</b> §08's shop is the one screen
    /// operated with a mouse, so it is put up and taken down deliberately; the same key
    /// closes it from anywhere, which is what gives the camera back. Ending the match
    /// is deliberately <em>not</em> on this key — see <c>MatchDirector.TryLeaveForGood</c>
    /// for why §02's 생존 row does not belong on a single press.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceVehicleInteractable : Interactable
    {
        /// <summary>Parks the vehicle at the 출입구.</summary>
        public static SurfaceVehicleInteractable Spawn(Vector3 position, Transform? parent)
        {
            var prop = CreateProp(
                "SurfaceVehicle",
                PrimitiveType.Cube,
                position + (Vector3.up * (PropSize.y * 0.5f)),
                PropSize,
                PropColour);

            if (parent != null)
            {
                prop.transform.SetParent(parent, worldPositionStays: true);
            }

            return prop.AddComponent<SurfaceVehicleInteractable>();
        }

        /// <inheritdoc />
        public override string Title
        {
            get { return "차량"; }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                var director = PlayerInteractor.Active != null ? PlayerInteractor.Active.Director : null;
                if (director == null)
                {
                    return string.Empty;
                }

                return "§08 팀 크레딧 "
                       + director.Shop.Wallet.Credits.ToString(CultureInfo.InvariantCulture)
                       + "   ·   전리품은 도착하는 순간 실렸다. 상점을 열려면 누른다 — §07 시계는 멈추지 않는다.";
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

            if (!director.LocalPlayerOnSurface)
            {
                Refusal = "§08 차량은 지상에 있다.";
                return;
            }

            director.OpenShopScreen();
            Refusal = string.Empty;
        }

        // Rig geometry: a van at the door.
        private static readonly Vector3 PropSize = new Vector3(2.0f, 1.8f, 4.2f);
        private static readonly Color PropColour = new Color(0.32f, 0.34f, 0.30f);
    }
}
