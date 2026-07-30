#nullable enable

using HorrorGame.Core.Match;
using HorrorGame.Core.Roles;

namespace HorrorGame.UI
{
    /// <summary>
    /// What a player in the lobby can ask for. §11 · §13.
    /// <para>
    /// Claiming is a contested write — four people picking from five roles, and §11's
    /// invariant is that no two of them end up with the same one — so the host owns
    /// it for the same reason it owns the wallet. The screen asks; the host answers by
    /// pushing a new <c>RoleSelection</c>; a refusal is simply a board that comes back
    /// unchanged, which is also what losing a race to another player looks like.
    /// </para>
    /// </summary>
    public interface IRoleClaimRequests
    {
        /// <summary>Asks for a role. The host refuses if somebody else already has it.</summary>
        void RequestClaim(RoleId role);

        /// <summary>Gives up whatever this player holds, so somebody else can take it.</summary>
        void RequestRelease();

        /// <summary>Asks to start. The host refuses until every slot has picked (§11's four-of-five).</summary>
        void RequestStart();
    }

    /// <summary>
    /// An <see cref="IRoleClaimRequests"/> that writes straight to the selection.
    /// <para>
    /// For the host's own lobby screen and for offline play. A remote client gets a
    /// networked implementation from the Net layer; the screen is written against the
    /// interface and cannot tell which it has.
    /// </para>
    /// </summary>
    public sealed class LocalRoleClaimRequests : IRoleClaimRequests
    {
        private readonly RoleSelection _selection;
        private readonly int _playerIndex;
        private readonly System.Action? _onStart;

        /// <summary>Wires a lobby screen to the selection it is showing.</summary>
        /// <param name="selection">The lobby's picks.</param>
        /// <param name="playerIndex">Which slot this screen speaks for.</param>
        /// <param name="onStart">Invoked when the lineup is complete and start is requested. Null means "start is somebody else's job".</param>
        public LocalRoleClaimRequests(RoleSelection selection, int playerIndex, System.Action? onStart)
        {
            _selection = selection;
            _playerIndex = playerIndex;
            _onStart = onStart;
        }

        /// <inheritdoc />
        public void RequestClaim(RoleId role)
        {
            _selection.TryClaim(_playerIndex, role);
        }

        /// <inheritdoc />
        public void RequestRelease()
        {
            _selection.Release(_playerIndex);
        }

        /// <summary>
        /// Starts the match if §11's lineup is complete. Silently does nothing
        /// otherwise — the button is already disabled in that state, and a lobby is not
        /// a place to throw.
        /// </summary>
        public void RequestStart()
        {
            if (_selection.IsComplete && _onStart != null)
            {
                _onStart();
            }
        }
    }
}
