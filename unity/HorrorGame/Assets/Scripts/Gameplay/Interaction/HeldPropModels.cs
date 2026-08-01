#nullable enable

using HorrorGame.Gameplay.Player;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// Teaches <see cref="PlayerHeldProp"/> how to turn a model key into an instance.
    /// <para>
    /// <b>This exists because of one assembly boundary.</b>
    /// <c>Assets/Scripts/Gameplay/Interaction</c> has no <c>.asmdef</c>, so it compiles
    /// into <c>Assembly-CSharp</c> — the predefined assembly, which references every
    /// asmdef in the project and is referenced by none of them. <see cref="PlayerHeldProp"/>
    /// belongs in <c>HorrorGame.Gameplay.Player</c>, beside the loadout whose two booleans
    /// it reads and the rig bones it hangs off, and from there
    /// <see cref="InteractablePropLibrary"/> is unreachable.
    /// </para>
    /// <para>
    /// So the dependency points the only way it can: the lower layer declares a hole and
    /// this fills it, at load, in both a player and the editor. Inverting it instead —
    /// moving the component up here — would put the thing that reads
    /// <c>PlayerLoadout</c> every LateUpdate in the assembly that cannot be referenced by
    /// the shot tools that photograph it.
    /// </para>
    /// </summary>
    public static class HeldPropModels
    {
        /// <summary>Installs the resolver. Runs before any scene loads.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        public static void Install()
        {
            // Asserted here rather than trusted, because the constant it mirrors is on
            // this side of the boundary and the copy is on the other: a rename of
            // PropModels.Objective would otherwise show up as an objective that renders
            // as a grey MISSING_ box in a player's hands and nowhere else.
            if (PlayerHeldProp.ObjectiveKey != PropModels.Objective)
            {
                Debug.LogError("[HeldPropModels] PlayerHeldProp.ObjectiveKey is '"
                    + PlayerHeldProp.ObjectiveKey + "' and PropModels.Objective is '"
                    + PropModels.Objective + "'. §03's 목표물 would be looked up under a key "
                    + "the library does not have.");
            }

            PlayerHeldProp.ModelSource = InteractablePropLibrary.Instantiate;
        }
    }
}
