#nullable enable

using HorrorGame.Core;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// One mark on a wall. §03.
    /// <para>
    /// <b>It has no key.</b> §03 does not describe reading as an action: the gate is
    /// proximity, stillness and a beam held on the mark for
    /// <see cref="GameConstants.ClueReadSeconds"/> — "오래 비춰야 읽힌다" — and losing
    /// any one of them throws the progress away rather than pausing it. Binding that
    /// to a key would let a player start a read from cover, and the point of §03 is
    /// that reading puts you in the dangerous room with a light on.
    /// </para>
    /// <para>
    /// So this component carries an id and nothing else. <see cref="PlayerInteractor"/>
    /// measures distance, speed and light every frame; <c>MatchDirector</c> steps
    /// <c>ClueReader</c> with it; the host resolves the finished read through
    /// <c>ObjectiveResolver.TryRead</c>. <b>No clue content is on this object</b> —
    /// ARCHITECTURE §4 keeps it on the host, and a prop that knew what it said would be
    /// the first place a client could read it out of memory.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CluePropInteractable : Interactable
    {
        private int _clueId = -1;

        /// <summary>Which clue this is, as <c>ObjectiveResolver</c> numbers them. Not what it says.</summary>
        public int ClueId
        {
            get { return _clueId; }
        }

        /// <summary>Stands a mark at a §12 후보 지점.</summary>
        public static CluePropInteractable Spawn(int clueId, Vector3 position, Transform? parent)
        {
            // A free-standing lectern rather than the wall board. §03's markers carry a
            // position and no surface normal, so there is nothing to orient a wall
            // fitting against, and a board floating a hand's width off nothing is worse
            // than a stand that needs no wall. Clue_WallBoard and Clue_EngravedPlate
            // stay generated and unused until a marker carries the wall it belongs to.
            var prop = CreateProp("Clue_" + clueId, PropModels.Clue, position);

            if (parent != null)
            {
                prop.transform.SetParent(parent, worldPositionStays: true);
            }

            var interactable = prop.AddComponent<CluePropInteractable>();
            interactable._clueId = clueId;
            return interactable;
        }

        /// <inheritdoc />
        public override string Title
        {
            get { return "표식"; }
        }

        /// <inheritdoc />
        public override string Detail
        {
            get
            {
                // The three conditions, stated. §03 builds the whole information economy
                // on the player standing here in the light, so the prompt names the price
                // rather than letting a failed read look like a bug.
                return "§03 가까이 · 멈춰서 · 계속 비춰야 읽힌다 ("
                       + GameConstants.ClueReadSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                       + "초). 빛이 끊기면 처음부터.";
            }
        }

        /// <summary>§03's own range, which is longer than a hands-on reach — a mark is read, not touched.</summary>
        public override float ReachMetres
        {
            get { return GameConstants.ClueReadRange; }
        }

        /// <summary>False. See the class remarks: §03's gate is not a key press.</summary>
        public override bool AcceptsKey
        {
            get { return false; }
        }
    }
}
