#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Net
{
    /// <summary>
    /// How far away a networked object stays worth replicating.
    /// <para>
    /// Two classes, because §03 and §12 give two genuinely different answers and a
    /// single radius would have to be wrong for one of them.
    /// </para>
    /// </summary>
    public enum NetInterestClass
    {
        /// <summary>
        /// Players and the monster: replicated out to the furthest distance any
        /// player can perceive anything. The default.
        /// </summary>
        Perception,

        /// <summary>
        /// The objective and the clue props: replicated only inside the radius a
        /// light can actually reach. §03 — "어둠 = 목표의 잠금장치".
        /// </summary>
        Secret,
    }

    /// <summary>
    /// Declares which interest class a networked object belongs to. Absent, an
    /// object is <see cref="NetInterestClass.Perception"/>.
    /// <para>
    /// A plain MonoBehaviour, not a <c>NetworkBehaviour</c>: it exists on the host to
    /// answer a question about the object, and it has nothing to say to a client.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HorrorGame/Net/Net Interest Scope")]
    public sealed class NetInterestScope : MonoBehaviour
    {
        [Tooltip("Secret for the objective and clue props; Perception for players and the monster.")]
        [SerializeField]
        private NetInterestClass _interestClass = NetInterestClass.Perception;

        /// <summary>Which class this object belongs to.</summary>
        public NetInterestClass InterestClass
        {
            get => _interestClass;
            set => _interestClass = value;
        }

        /// <summary>
        /// How far a player can perceive anything at all, and therefore the furthest
        /// another player or the monster is worth sending.
        /// <para>
        /// Derived, not chosen. The candidates are §04's 청음사 hearing range
        /// (<see cref="GameConstants.ListenerHearingRange"/>), §13's voice cutoff
        /// (<see cref="GameConstants.VoiceCutoffDistance"/>) and §12's largest zone
        /// diagonal (<see cref="GameConstants.ZoneDiagonalMax"/>) — the point past
        /// which two players are not even in the same room. The largest of the three
        /// wins, because culling below any of them would delete something a player is
        /// entitled to perceive: a footstep the Listener should have heard, a voice
        /// that should still have carried, a teammate across the same hall.
        /// </para>
        /// </summary>
        public static float PerceptionRange
        {
            get
            {
                var range = GameConstants.ListenerHearingRange;
                if (GameConstants.VoiceCutoffDistance > range)
                {
                    range = GameConstants.VoiceCutoffDistance;
                }

                if (GameConstants.ZoneDiagonalMax > range)
                {
                    range = GameConstants.ZoneDiagonalMax;
                }

                return range;
            }
        }

        /// <summary>
        /// How far a secret is worth sending: the furthest a player can light
        /// something up.
        /// <para>
        /// §03 makes darkness the lock on the objective — "목표와 위험이 같은 스위치에
        /// 걸린다" — so the honest network answer is that an unlit object is not
        /// there. The radius is the best light in the game: an upgraded flashlight
        /// (§08) at <see cref="GameConstants.FlashlightRange"/> ×
        /// <see cref="GameConstants.UpgradedFlashlightRangeMultiplier"/>, or a
        /// zone light at <see cref="GameConstants.ZoneLightRadius"/>, whichever
        /// reaches further. Anything shorter would let a team buy the upgrade and
        /// find that it does not work.
        /// </para>
        /// <para>
        /// This is also the answer to ARCHITECTURE §4's warning that "sending it but
        /// only showing it when close is the same as sending it": the object is not
        /// sent and then hidden — it is not sent. A client that logs every spawn it
        /// receives learns nothing until its own player is standing in the light.
        /// </para>
        /// </summary>
        public static float SecretRange
        {
            get
            {
                var lit = GameConstants.FlashlightRange * GameConstants.UpgradedFlashlightRangeMultiplier;
                return lit > GameConstants.ZoneLightRadius ? lit : GameConstants.ZoneLightRadius;
            }
        }

        /// <summary>The radius for a class.</summary>
        public static float RangeFor(NetInterestClass interestClass) =>
            interestClass == NetInterestClass.Secret ? SecretRange : PerceptionRange;

        /// <summary>
        /// Stamps a class onto an object the host has just built, adding the
        /// component if it is missing.
        /// <para>
        /// Exists so a level builder spawning the objective writes one line and
        /// cannot forget the component. Forgetting it on a player is a visible bug —
        /// they pop in late. Forgetting it on the objective would be silent, and
        /// silent is the failure mode §13 cannot afford, so the host should never
        /// spawn a secret without going through here.
        /// </para>
        /// </summary>
        public static NetInterestScope Apply(GameObject target, NetInterestClass interestClass)
        {
            if (target == null)
            {
                throw new System.ArgumentNullException(nameof(target));
            }

            if (!target.TryGetComponent(out NetInterestScope scope))
            {
                scope = target.AddComponent<NetInterestScope>();
            }

            scope.InterestClass = interestClass;
            return scope;
        }
    }
}
