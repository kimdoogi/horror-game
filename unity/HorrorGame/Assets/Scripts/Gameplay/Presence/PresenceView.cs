#nullable enable

using System.Collections.Generic;
using HorrorGame.Core;
using HorrorGame.Core.Presence;
using UnityEngine;

namespace HorrorGame.Gameplay.Presence
{
    /// <summary>
    /// What the 그늘 looks like from inside it: grain gathering at the edge of the beam,
    /// and — past <see cref="PresenceStage.Close"/> — a figure standing where the light is
    /// not.
    /// <para>
    /// <b>Everything here is presentation and none of it is a rule.</b> The figure has no
    /// collider, no agent, no path and never moves once placed; it is removed and put
    /// somewhere else, which is a cut rather than an approach. Walking into it does
    /// nothing at all. That is the visual half of the same guarantee
    /// <see cref="PresenceField"/> makes structurally — §01 keeps its horror with one
    /// unkillable pursuer, and the first thing that would make this read as a second one
    /// is it coming towards you.
    /// </para>
    /// <para>
    /// <b>Why the grain sits at the fringe of the beam and not in it.</b> §05 gives the
    /// player a 22° cone and ART.md's first target is that the beam is the source of
    /// information and outside it there is shape only. The 그늘 is the shape. Motes are
    /// rejected inside <see cref="GameConstants.FlashlightHalfAngle"/> of where the player
    /// is looking, so pointing the torch at them removes them — which is the mechanic
    /// stated in pixels: light is the answer, and it only answers where it is pointed.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PresenceView : MonoBehaviour
    {
        /// <summary>
        /// Motes at a full pool. Chosen against §05's 90° FOV: enough that a slow turn
        /// always has some in frame, few enough that they never read as fog. Fog is a
        /// weather effect and this is not weather.
        /// </summary>
        public const int MaxMotes = 160;

        /// <summary>
        /// Nearest a mote may sit to the eye, metres. Closer than this it is a smear on the
        /// lens rather than a thing in the room.
        /// <para>
        /// 0.6 rather than the 1.1 this started at, and the corridor decided it: §12's
        /// clear section is 2.2 m, so from the middle of one the wall is 1.1 m away in
        /// every direction that is not along the corridor — and along the corridor is where
        /// the beam points. At 1.1 m the first render placed nothing at all.
        /// </para>
        /// </summary>
        public const float MoteNearMetres = 0.6f;

        /// <summary>
        /// Furthest a mote may sit, metres. Half §03's
        /// <see cref="GameConstants.FlashlightRange"/>, so the grain lives in the near band
        /// the beam could have reached and did not, rather than at a distance where a
        /// 3 cm flake is one pixel.
        /// </summary>
        public const float MoteFarMetres = 4.5f;

        /// <summary>
        /// How far a mote is held off a surface it would otherwise be placed inside,
        /// metres.
        /// <para>
        /// The second render round put this at 12 cm and photographed the result: the motes
        /// sat flush on the brick and read as scraps of paper glued to the wall rather than
        /// as anything in the air. 40 cm is enough that the parallax between a mote and the
        /// wall behind it is visible from a walking player.
        /// </para>
        /// </summary>
        public const float MoteWallStandoffMetres = 0.40f;

        /// <summary>
        /// Distance at which a mote is drawn at its authored size, metres. Everything
        /// nearer is shrunk and everything further is grown in proportion, so apparent size
        /// stays constant — see the note in <c>Reposition</c>.
        /// </summary>
        public const float MoteReferenceMetres = 3.0f;

        /// <summary>
        /// Widest a mote may sit off the player's heading, degrees.
        /// <para>
        /// The floor is <see cref="GameConstants.FlashlightHalfAngle"/> — inside the beam
        /// there is no 그늘, which is the mechanic stated in pixels. The ceiling is a
        /// little inside §05's 90° half-frame so most of the grain lands in the annulus
        /// between the edge of the beam and the edge of the screen. That annulus is the
        /// whole idea: the dark you can see is the dark you are not pointing at.
        /// </para>
        /// </summary>
        public const float MoteFringeMaxDegrees = 62f;

        /// <summary>
        /// Share of motes placed in the fringe annulus rather than anywhere around the
        /// player. The rest go behind, where they are never seen directly and are only ever
        /// caught by a turn.
        /// </summary>
        public const float MoteFringeShare = 0.75f;

        /// <summary>Fraction of the mote pool repositioned per second. Slow — the dark settles, it does not swarm.</summary>
        public const float MoteChurnPerSecond = 0.55f;

        /// <summary>Nearest the 형상 may stand, metres. Inside this it stops being a silhouette and becomes a wall.</summary>
        public const float FigureNearMetres = 3.5f;

        /// <summary>
        /// Furthest the 형상 may stand, metres — §03's beam range. Past it the figure would
        /// be pure grain against pure black with no room in between, which reads as a bug
        /// rather than as a thing.
        /// </summary>
        public const float FigureFarMetres = GameConstants.FlashlightRange;

        /// <summary>
        /// Seconds one placement lasts before the figure is moved. It is never seen to
        /// move: it is somewhere, and then it is somewhere else.
        /// </summary>
        public const float FigureDwellSeconds = 6.5f;

        [Header("Assets")]
        [SerializeField]
        [Tooltip("Presence_Figure prefab, built by PresenceRig.")]
        private GameObject? _figurePrefab;

        [SerializeField]
        [Tooltip("Presence_Mote prefab, built by PresenceRig.")]
        private GameObject? _motePrefab;

        [Header("Rig")]
        [SerializeField]
        [Tooltip("The player's camera. Left empty, the main camera is used.")]
        private Transform? _eye;

        [SerializeField]
        [Tooltip("Layers the placement raycasts may hit. Level geometry, not characters.")]
        private LayerMask _geometry = ~0;

        private readonly List<Transform> _motes = new List<Transform>();
        private Transform? _moteRoot;
        private Transform? _figure;
        private PresenceState? _state;

        private float _pooling;
        private PresenceStage _stage = PresenceStage.Clear;
        private float _figureAge;
        private bool _overridden;
        private int _churnCursor;
        private uint _rng = 0x9E3779B9u;

        /// <summary>Where the 그늘 is measured and drawn from.</summary>
        public Transform? Eye
        {
            get { return _eye; }
            set { _eye = value; }
        }

        /// <summary>The 형상's transform once one has been made, for a shot rig to inspect.</summary>
        public Transform? Figure => _figure;

        /// <summary>
        /// Whether the player's beam is lit. Placement keeps the 그늘 out of the cone only
        /// while it is.
        /// <para>
        /// <b>This is not a refinement, it is a correction.</b> The first version excluded
        /// <see cref="GameConstants.FlashlightHalfAngle"/> around the heading
        /// unconditionally, which sounds right — the 그늘 is what the light is not on — and
        /// in a §12 corridor it means nothing can ever be placed. A 2.2 m clear section
        /// has 3.5–12 m of clear sight in exactly two directions, both of them along the
        /// corridor, both inside the cone. The figure would have had nowhere to stand in
        /// the geometry the game is made of.
        /// </para>
        /// <para>
        /// And the exclusion is only meaningful for a few seconds at a time anyway: a
        /// player with a lit torch reads as fully lit, so the pool drains and the 그늘 goes.
        /// The window where a beam and a full pool coexist is the
        /// <see cref="GameConstants.PresenceDispersalSeconds"/> after switching on — which
        /// is precisely when "it was there and the light took it away" is worth showing.
        /// </para>
        /// </summary>
        public bool BeamActive { get; set; }

        /// <summary>Motes currently switched on. The shot rig quotes this in its caption.</summary>
        public int VisibleMotes { get; private set; }

        /// <summary>
        /// Motes actually inside a camera's frustum right now.
        /// <para>
        /// The instrument the second render round was missing. "88 motes on" and "two motes
        /// on screen" are both true at once — most of the pool is behind the player by
        /// construction — and without this number the only way to tell a placement bug from
        /// a legibility problem is to squint at a PNG. It counts placement; the shot rig's
        /// contrast reading counts legibility. Two different failures, two numbers.
        /// </para>
        /// </summary>
        public int MotesInFrame(Camera camera)
        {
            if (camera == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < _motes.Count; i++)
            {
                if (!_motes[i].gameObject.activeSelf)
                {
                    continue;
                }

                var viewport = camera.WorldToViewportPoint(_motes[i].position);
                if (viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f
                    && viewport.y >= 0f && viewport.y <= 1f)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Binds the state this view draws. Passing null returns it to whatever
        /// <see cref="SetStageOverride"/> last said, which is how the shot rig drives it
        /// with no match running.
        /// </summary>
        public void Bind(PresenceState? state)
        {
            _state = state;
            _overridden = false;
        }

        /// <summary>
        /// Drives the view directly, for a rig that needs a named stage rather than a
        /// match. Overrides any bound state until <see cref="Bind"/> is called again.
        /// </summary>
        public void SetStageOverride(PresenceStage stage, float pooling01)
        {
            _overridden = true;
            _stage = stage;
            _pooling = Mathf.Clamp01(pooling01);
        }

        /// <summary>
        /// Stands the 형상 on a floor point, facing the eye. Deterministic, so the shot rig
        /// photographs the same frame twice.
        /// </summary>
        public void PlaceFigureAt(Vector3 floorPoint)
        {
            var figure = EnsureFigure();
            if (figure == null)
            {
                return;
            }

            figure.position = floorPoint;

            var eye = ResolveEye();
            if (eye != null)
            {
                var toEye = eye.position - floorPoint;
                toEye.y = 0f;
                if (toEye.sqrMagnitude > 0.0001f)
                {
                    // Facing the player, because the one thing this shape has to do is be
                    // recognised as a person shape before it is recognised as anything else.
                    figure.rotation = Quaternion.LookRotation(toEye.normalized, Vector3.up);
                }
            }

            figure.gameObject.SetActive(true);
            _figureAge = 0f;
        }

        /// <summary>Hides the 형상. Called when the pool falls back under the warning, and on a taking.</summary>
        public void HideFigure()
        {
            if (_figure != null)
            {
                _figure.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Rebuilds the mote pool at a given fill and returns how many are switched on.
        /// Public so a rig can drive one frame and measure it rather than waiting for
        /// <c>LateUpdate</c> to converge.
        /// </summary>
        public int LayOutMotes(float pooling01)
        {
            EnsureMotes();

            var eye = ResolveEye();
            if (eye == null)
            {
                return 0;
            }

            var wanted = Mathf.RoundToInt(MaxMotes * Mathf.Clamp01(pooling01));
            for (var i = 0; i < _motes.Count; i++)
            {
                var on = i < wanted;
                _motes[i].gameObject.SetActive(on);
                if (on)
                {
                    Reposition(_motes[i], eye);
                }
            }

            VisibleMotes = wanted;
            return wanted;
        }

        private void LateUpdate()
        {
            if (!_overridden && _state != null)
            {
                _pooling = _state.Pooling01;
                _stage = _state.Stage;
            }

            var eye = ResolveEye();
            if (eye == null)
            {
                return;
            }

            DriveMotes(eye);
            DriveFigure(eye);
        }

        private void DriveMotes(Transform eye)
        {
            EnsureMotes();

            // Nothing is drawn while the player is Taken: the frame is supposed to be
            // emptier than it was, not busier. §06 already argues that "침묵이 가장 무서운
            // 소리다" for the monster's 정지; the taking is that argument applied to the
            // picture as well as to the mix.
            var fill = _stage == PresenceStage.Taken ? 0f : _pooling;
            var wanted = Mathf.RoundToInt(MaxMotes * Mathf.Clamp01(fill));
            VisibleMotes = wanted;

            for (var i = 0; i < _motes.Count; i++)
            {
                var shouldBeOn = i < wanted;
                var mote = _motes[i];
                if (mote.gameObject.activeSelf != shouldBeOn)
                {
                    mote.gameObject.SetActive(shouldBeOn);
                    if (shouldBeOn)
                    {
                        Reposition(mote, eye);
                    }
                }
            }

            if (wanted == 0)
            {
                return;
            }

            var churn = Mathf.Max(1, Mathf.RoundToInt(wanted * MoteChurnPerSecond * Time.deltaTime));
            for (var n = 0; n < churn; n++)
            {
                _churnCursor = (_churnCursor + 1) % wanted;
                Reposition(_motes[_churnCursor], eye);
            }
        }

        private void DriveFigure(Transform eye)
        {
            if (_stage != PresenceStage.Close)
            {
                HideFigure();
                return;
            }

            _figureAge += Time.deltaTime;

            var figure = EnsureFigure();
            if (figure == null)
            {
                return;
            }

            if (!figure.gameObject.activeSelf || _figureAge >= FigureDwellSeconds)
            {
                if (TryFindStandingSpot(eye, out var spot))
                {
                    PlaceFigureAt(spot);
                }
                else
                {
                    // No legal spot this frame: hide rather than leave it where it was. A
                    // figure that stays put while the player walks past it becomes an
                    // object, and an object can be inspected.
                    HideFigure();
                    _figureAge = 0f;
                }
            }
        }

        /// <summary>
        /// Looks for somewhere the 형상 can stand: outside the beam, on a floor, in sight,
        /// between <see cref="FigureNearMetres"/> and <see cref="FigureFarMetres"/>.
        /// <para>
        /// Outside the beam first and everything else second. §03's whole switch is that
        /// the beam decides what you know, so a figure that could be placed inside the cone
        /// would be a thing the light shows you — and this is a thing the light removes.
        /// </para>
        /// </summary>
        public bool TryFindStandingSpot(Transform eye, out Vector3 floorPoint)
        {
            floorPoint = default;

            for (var attempt = 0; attempt < 24; attempt++)
            {
                var yaw = NextFloat() * 360f;
                var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

                var flat = eye.forward;
                flat.y = 0f;
                if (BeamActive && flat.sqrMagnitude > 0.0001f
                    && Vector3.Angle(flat.normalized, direction) < GameConstants.FlashlightHalfAngle)
                {
                    continue;
                }

                var distance = Mathf.Lerp(FigureNearMetres, FigureFarMetres, NextFloat());
                var target = eye.position + (direction * distance);

                // Sight line, from the eye. A figure behind a wall is not frightening, it
                // is absent — and the player has to be able to have seen it.
                if (Physics.Linecast(eye.position, target, _geometry, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                if (!Physics.Raycast(target, Vector3.down, out var hit, 4f, _geometry,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                floorPoint = hit.point;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Puts one mote somewhere in the dark around the eye.
        /// <para>
        /// <b>The first version rejected any direction whose wall was closer than
        /// <see cref="MoteNearMetres"/> and placed nothing at all in a §12 corridor.</b>
        /// From the middle of a 2.2 m clear section every direction except along the
        /// corridor has a wall 1.1 m away, and along the corridor is where the beam points.
        /// So a near wall now pulls the mote in rather than discarding the direction, and
        /// the whole placement band moved forward to sit inside a corridor's width.
        /// </para>
        /// </summary>
        private void Reposition(Transform mote, Transform eye)
        {
            var heading = eye.forward;
            heading.y = 0f;
            var yawBase = heading.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(heading.normalized, Vector3.up).eulerAngles.y
                : 0f;

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var inFringe = NextFloat() < MoteFringeShare;

                // With the beam off there is no cone to stay out of, and in a §12 corridor
                // the only directions with any room in them are the ones the cone would
                // have covered. See BeamActive.
                var floorDegrees = BeamActive ? GameConstants.FlashlightHalfAngle + 3f : 5f;
                var offAxis = Mathf.Lerp(
                    floorDegrees,
                    inFringe ? MoteFringeMaxDegrees : 180f,
                    NextFloat());
                var side = NextFloat() < 0.5f ? -1f : 1f;

                var pitch = Mathf.Lerp(-26f, 30f, NextFloat());
                var direction = Quaternion.Euler(pitch, yawBase + (side * offAxis), 0f) * Vector3.forward;

                var distance = Mathf.Lerp(MoteNearMetres, MoteFarMetres, Mathf.Sqrt(NextFloat()));

                // Pull a mote out of the wall it would otherwise be inside. Grain floating
                // through brickwork is the single cheapest way to make a careful effect
                // look like a particle system somebody forgot to mask.
                if (Physics.Raycast(eye.position, direction, out var hit, distance, _geometry,
                        QueryTriggerInteraction.Ignore))
                {
                    distance = hit.distance - MoteWallStandoffMetres;
                }

                if (distance < MoteNearMetres * 0.5f)
                {
                    continue;
                }

                mote.position = eye.position + (direction * distance);
                mote.rotation = Quaternion.Euler(
                    NextFloat() * 360f, NextFloat() * 360f, NextFloat() * 360f);

                // Scaled in proportion to distance, so every mote subtends roughly the
                // same few pixels wherever it is. The third render round is the argument:
                // with a fixed world size the near motes were 30-pixel white triangles and
                // the far ones were invisible, in the same frame — one effect reading as
                // two mistakes. Grain is defined by its apparent size, not its real one.
                var reference = MoteReferenceMetres;
                mote.localScale = Vector3.one
                                  * Mathf.Clamp(distance / reference, 0.55f, 1.9f)
                                  * Mathf.Lerp(0.80f, 1.25f, NextFloat());
                return;
            }

            // Nowhere legal in eight tries — a cupboard, or a face against a wall. Park it
            // out of the frustum rather than leaving it wherever it last was, which on the
            // first frame is the world origin and on this map is 400 m below the stage.
            mote.position = eye.position - (Vector3.up * 100f);
        }

        private Transform? ResolveEye()
        {
            if (_eye != null)
            {
                return _eye;
            }

            var camera = Camera.main;
            _eye = camera != null ? camera.transform : null;
            return _eye;
        }

        private void EnsureMotes()
        {
            if (_motes.Count >= MaxMotes || _motePrefab == null)
            {
                return;
            }

            if (_moteRoot == null)
            {
                _moteRoot = new GameObject("[Presence Motes]").transform;
                _moteRoot.SetParent(transform, worldPositionStays: false);
            }

            while (_motes.Count < MaxMotes)
            {
                var mote = Instantiate(_motePrefab, _moteRoot);
                mote.name = "Mote_" + _motes.Count.ToString("00");
                Strip(mote);
                mote.SetActive(false);
                _motes.Add(mote.transform);
            }
        }

        private Transform? EnsureFigure()
        {
            if (_figure != null)
            {
                return _figure;
            }

            if (_figurePrefab == null)
            {
                return null;
            }

            var instance = Instantiate(_figurePrefab, transform.parent);
            instance.name = "[Presence 형상]";
            Strip(instance);
            instance.SetActive(false);
            _figure = instance.transform;
            return _figure;
        }

        /// <summary>
        /// Removes every collider and rigidbody from an instance.
        /// <para>
        /// <c>AssetImportPolicy</c> grades anything in a new folder under
        /// <c>Assets/Models</c> as a Prop and props import with a generated mesh collider.
        /// That is right for a 전리품 and wrong for this: a 그늘 you can bump into is a
        /// piece of furniture, and the moment a player learns they can walk through it the
        /// figure stops being anything at all.
        /// </para>
        /// </summary>
        private static void Strip(GameObject instance)
        {
            foreach (var collider in instance.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                DestroyImmediate(collider);
            }

            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                DestroyImmediate(body);
            }
        }

        /// <summary>
        /// A small xorshift, so a placement sequence replays. §13's invariant 4 is that a
        /// seed replays a match exactly, and a view that reached for <c>UnityEngine.Random</c>
        /// would put a global stream in the middle of it.
        /// </summary>
        private float NextFloat()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng >> 8) * (1f / 16777216f);
        }
    }
}
