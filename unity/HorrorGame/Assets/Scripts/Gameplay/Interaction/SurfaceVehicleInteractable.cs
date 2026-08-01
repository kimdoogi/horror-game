#nullable enable

using System.Globalization;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// The 지상 차량. §08: "지상 차량 = 안전 지대 + 상점 + 보급소."
    /// <para>
    /// <b>Some of what the vehicle does needs no key.</b> Walking into its apron is what
    /// surfaces the team, and surfacing is what sells the 전리품 and tops the cell up at
    /// §03's 지상 발전기 — <c>MatchDirector.Surfaced</c> does both on the rising edge,
    /// because §08 describes loading loot into the vehicle as the consequence of
    /// arriving rather than as an errand. Neither takes anything away from the player.
    /// </para>
    /// <para>
    /// <b>The shop is not in that list, and the difference is the mouse.</b> §08's shop
    /// is the one screen operated with a cursor, so opening it unlocks the pointer and
    /// pins §05's aim; done on arrival it seizes the camera from someone who only walked
    /// past the van. §01 makes surfacing a deliberate act, and this key is where that
    /// deliberateness lives: look at the 차량, read the line, press it. The same key
    /// closes it from anywhere, which is what gives the camera back.
    /// </para>
    /// <para>
    /// Ending the match is deliberately <em>not</em> on this key — see
    /// <c>MatchDirector.TryLeaveForGood</c> for why §02's 생존 row does not belong on a
    /// single press.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceVehicleInteractable : Interactable
    {
        /// <summary>Parks the vehicle at the 출입구.</summary>
        public static SurfaceVehicleInteractable Spawn(Vector3 position, Transform? parent)
        {
            // 2.81 x 2.94 x 6.69 m, which is what §08's "안전 지대 + 상점 + 보급소" has to
            // look like from the far end of the apron. The team returns to it 2.94 times
            // a match; it was a 2 m box.
            var prop = CreateProp("SurfaceVehicle", PropModels.Vehicle, position);

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
        public override string Action
        {
            get { return "상점 열기"; }
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
    }
}
