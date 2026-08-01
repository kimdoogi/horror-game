#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HorrorGame.Gameplay.Interaction
{
    /// <summary>
    /// Every model an interactable is built from, in one asset the build is guaranteed
    /// to contain.
    /// <para>
    /// <b>Why an asset and not <c>Resources.Load</c> per model.</b> The models live in
    /// <c>Assets/Models/Props</c>, which is where <c>gen_props.py</c> writes them and
    /// where <c>AssetImportPolicy</c> governs them; moving them under
    /// <c>Resources/</c> to make them loadable would break both. A single
    /// <c>ScriptableObject</c> under <c>Resources/</c> that <em>references</em> them
    /// pulls the whole set into the player build as dependencies and leaves the folder
    /// layout alone. <c>MatchAudioLibrary</c> reached the same arrangement for the
    /// 166 clips and for the same reason.
    /// </para>
    /// <para>
    /// <b>It is built, not dragged.</b> <c>SoloPlaytest.BuildScene</c> rewrites the solo
    /// scene from scratch on every invocation, so anything wired by hand into a scene
    /// is wiring that will be silently lost. The editor step that fills this in is
    /// <c>HorrorGame ▸ Props ▸ Build Interactable Prop Library</c>, and it fails rather
    /// than half-filling the asset.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "HorrorGame/Props/Interactable Prop Library", fileName = "InteractableProps")]
    public sealed class InteractablePropLibrary : ScriptableObject
    {
        /// <summary><see cref="Resources"/> name the editor build step writes this asset under.</summary>
        public const string ResourceName = "InteractableProps";

        /// <summary>One model, under the file name <c>gen_props.py</c> gave it.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("File name in Assets/Models/Props, without the extension.")]
            public string key = string.Empty;

            [Tooltip("The imported FBX. Referencing it here is what puts it in the build.")]
            public GameObject? model;
        }

        [SerializeField]
        [Tooltip("Filled by HorrorGame ▸ Props ▸ Build Interactable Prop Library.")]
        private Entry[] _entries = Array.Empty<Entry>();

        [NonSerialized]
        private Dictionary<string, GameObject>? _index;

        private static InteractablePropLibrary? _loaded;
        private static bool _loadAttempted;

        /// <summary>Loads the built library, or null when the build step has never been run.</summary>
        public static InteractablePropLibrary? LoadDefault()
        {
            if (_loadAttempted && _loaded != null)
            {
                return _loaded;
            }

            _loadAttempted = true;
            _loaded = Resources.Load<InteractablePropLibrary>(ResourceName);
            return _loaded;
        }

        /// <summary>The models this library knows about, in build order.</summary>
        public IReadOnlyList<Entry> Entries
        {
            get { return _entries; }
        }

        /// <summary>Replaces the whole roster. Editor build step only.</summary>
        public void SetEntries(Entry[] entries)
        {
            _entries = entries;
            _index = null;
        }

        /// <summary>The model for a key, or null when the library has no such entry.</summary>
        public GameObject? Model(string key)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                foreach (var entry in _entries)
                {
                    if (!string.IsNullOrEmpty(entry.key) && entry.model != null)
                    {
                        _index[entry.key] = entry.model;
                    }
                }
            }

            return _index.TryGetValue(key, out var model) ? model : null;
        }

        /// <summary>Everything wrong with this library, or empty when it is complete.</summary>
        public string Validate(IReadOnlyList<string> required)
        {
            var problems = new StringBuilder();
            foreach (var entry in _entries)
            {
                if (entry.model == null)
                {
                    problems.AppendLine("  " + entry.key + ": no model bound");
                }
            }

            foreach (var key in required)
            {
                if (Model(key) == null)
                {
                    problems.AppendLine("  " + key + ": required by an interactable and missing");
                }
            }

            return problems.ToString();
        }

        /// <summary>
        /// Builds one instance of a model, or a marked-up stand-in with an error in the
        /// log.
        /// <para>
        /// The stand-in is deliberately not a <c>CreatePrimitive</c> default material —
        /// that is the exact path that made every interactable magenta in a player build
        /// (see <see cref="Interactable"/>). It resolves URP's Lit shader, which a URP
        /// build always contains because the map's own materials reference it, and says
        /// so loudly if even that is gone. A grey box with an error beside it is
        /// recoverable; a magenta box with a clean log is what shipped.
        /// </para>
        /// </summary>
        public static GameObject Instantiate(string key)
        {
            var library = LoadDefault();
            var model = library != null ? library.Model(key) : null;

            if (model != null)
            {
                // The instance keeps the prefab's own scale, and that is load-bearing.
                // ASSETS.md's scale policy leaves the FBX unit conversion on the root
                // transform (Lcl Scaling 100 against a file scale of 0.01) and the mesh
                // data a hundred times below metres. Normalising the root to 1 — which
                // looks like harmless defensiveness — shrinks every prop to a centimetre
                // and renders a match with nothing in it.
                return UnityEngine.Object.Instantiate(model);
            }

            Debug.LogError(
                "[Props] No model for '" + key + "'"
                + (library == null
                    ? " — Resources/" + ResourceName + " does not exist. Run HorrorGame ▸ Props ▸ "
                      + "Build Interactable Prop Library."
                    : " — the library is built but has no entry for it. Re-run HorrorGame ▸ Props ▸ "
                      + "Build Interactable Prop Library.")
                + " Falling back to an untextured box.");

            return Stand(key);
        }

        private static GameObject Stand(string key)
        {
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stand.name = "MISSING_" + key;
            stand.transform.localScale = Vector3.one * StandInMetres;

            var renderer = stand.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError(
                    "[Props] URP's Lit shader is not in this build either, so the stand-in for '"
                    + key + "' will render as Unity's error magenta. The pipeline is not set up: run "
                    + "Horror ▸ Setup ▸ Configure URP Render Pipeline.");
                return stand;
            }

            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(shader) { name = "MissingProp" };
            }

            return stand;
        }

        /// <summary>Side of the stand-in box, metres. Big enough to be obvious in a screenshot.</summary>
        private const float StandInMetres = 0.4f;
    }
}
