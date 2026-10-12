using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// The assists that make bare-hand stacking feel fair:
    /// a ghost that shows where the held block would land, rotation snapping while held, and a
    /// gentle pull into alignment on release. (Settle damping, velocity softening and the
    /// lost-tracking hold live on <see cref="StackBlock"/> itself.)
    /// It hears about grabs and releases through the block's events, which the XR layer drives.
    /// </summary>
    public sealed class PlacementAssist : MonoBehaviour
    {
        // The cast box is shrunk by this much so it doesn't catch on surfaces it is merely touching (m).
        private const float Skin = 0.002f;
        // The cast starts this far above the block, so a block pushed slightly into the surface (very
        // common when placing by hand) still finds its resting place instead of counting as "inside" (m).
        private const float Lift = 0.04f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public struct Landing
        {
            public bool Valid;
            /// <summary>The landing pose includes the soft-alignment correction.</summary>
            public bool Aligned;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        [SerializeField] private GameTuning tuning;
        [SerializeField] private BlockSpawner spawner;
        [SerializeField] private Pedestal pedestal;
        [SerializeField] private LayerMask castMask = Physics.DefaultRaycastLayers;

        [Header("Ghost preview")]
        [SerializeField] private Transform ghost;
        [SerializeField] private MeshFilter ghostFilter;
        [SerializeField] private MeshRenderer ghostRenderer;

        // Sized for a sweep through the whole tower: results are unordered, so none may be dropped.
        private readonly RaycastHit[] _hits = new RaycastHit[96];
        private MaterialPropertyBlock _ghostProps;
        private StackBlock _held;
        private bool _ghostAligned;
        private bool _ghostColorSet;

        /// <summary>Rotation snapping while a block is held. The game switches it by phase and setting.</summary>
        public bool RotationSnapEnabled { get; set; }

        /// <summary>
        /// Where the block would come to rest if released at this pose, straight down, including the
        /// alignment pull. Not valid unless there is tower or pedestal underneath.
        /// </summary>
        public bool Predict(StackBlock block, Vector3 position, Quaternion rotation, out Landing landing)
        {
            landing = default;

            if (!CastDown(block, position, rotation, out float distance, out Collider support))
            {
                return false;
            }

            Vector3 supportCentre;
            Quaternion supportRotation;
            bool canAlign = tuning.softAlignment;

            if (pedestal != null && pedestal.Owns(support))
            {
                supportCentre = pedestal.SurfaceCentre;
                supportRotation = pedestal.Rotation;
                canAlign &= tuning.alignToPedestal;
            }
            else if (StackBlock.TryGet(support, out StackBlock below) && below.IsPlaced)
            {
                supportCentre = below.Position;
                supportRotation = below.Rotation;
            }
            else
            {
                // Floor or anything else: not the tower.
                return false;
            }

            landing.Valid = true;
            landing.Position = position + Vector3.down * distance;
            landing.Rotation = rotation;

            if (!canAlign || PlacementMath.HorizontalDistance(position, supportCentre) > tuning.alignRadius)
            {
                return true;
            }

            Quaternion aligned = PlacementMath.NearestAligned(block.Definition.shape, rotation, supportRotation);
            if (Quaternion.Angle(rotation, aligned) > tuning.alignAngle)
            {
                return true;
            }

            // Centred and squared up, the block may land at a slightly different height. Cast again.
            Vector3 centred = new Vector3(supportCentre.x, position.y, supportCentre.z);
            if (CastDown(block, centred, aligned, out float alignedDistance, out _))
            {
                landing.Aligned = true;
                landing.Position = centred + Vector3.down * alignedDistance;
                landing.Rotation = aligned;
            }

            return true;
        }

        private void Awake()
        {
            _ghostProps = new MaterialPropertyBlock();
            HideGhost();
        }

        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.BlockSpawned += Track;
                spawner.BlockDespawned += Untrack;
            }
        }

        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.BlockSpawned -= Track;
                spawner.BlockDespawned -= Untrack;
            }

            HideGhost();
        }

        private void Track(StackBlock block)
        {
            block.OnGrabbed += HandleGrabbed;
            block.OnReleased += HandleReleased;
        }

        private void Untrack(StackBlock block)
        {
            block.OnGrabbed -= HandleGrabbed;
            block.OnReleased -= HandleReleased;
            if (_held == block)
            {
                _held = null;
                HideGhost();
            }
        }

        private void HandleGrabbed(StackBlock block)
        {
            _held = block;
            if (ghostFilter != null)
            {
                ghostFilter.sharedMesh = block.Definition.mesh;
            }

            if (ghost != null)
            {
                ghost.localScale = block.VisualRoot.lossyScale;
            }

            GameEvents.RaiseBlockGrabbed(block);
        }

        private void HandleReleased(StackBlock block)
        {
            if (_held == block)
            {
                _held = null;
            }

            HideGhost();
            GameEvents.RaiseBlockReleased(block);

            if (!Predict(block, block.Position, block.Rotation, out Landing landing))
            {
                return;
            }

            float drop = block.Position.y - landing.Position.y;

            if (landing.Aligned)
            {
                // Close above its resting place: set it down. Higher up: square it up and let it drop.
                Vector3 target = drop <= tuning.alignSnapDownDistance
                    ? landing.Position
                    : new Vector3(landing.Position.x, block.Position.y, landing.Position.z);
                block.BeginGlide(target, landing.Rotation, tuning.alignDuration);
            }
            else if (drop < 0f)
            {
                // Let go a little inside the surface: lift it out rather than let physics pop it out.
                block.BeginGlide(landing.Position, landing.Rotation, tuning.alignDuration * 0.5f);
            }
        }

        // After the interaction system has moved the held block for this frame.
        private void LateUpdate()
        {
            StackBlock block = _held;
            if (block == null || !block.IsHeld)
            {
                return;
            }

            Quaternion shown = block.Rotation;
            if (RotationSnapEnabled)
            {
                shown = PlacementMath.SnapRotation(shown, tuning.rotationSnapStep);
                block.SetVisualRotation(shown);
            }
            else
            {
                block.SetVisualRotation(block.Rotation);
            }

            if (ghost == null)
            {
                return;
            }

            if (Predict(block, block.Position, shown, out Landing landing))
            {
                ShowGhost(landing);
            }
            else
            {
                HideGhost();
            }
        }

        private void ShowGhost(Landing landing)
        {
            ghost.SetPositionAndRotation(landing.Position, landing.Rotation);

            if (ghostRenderer != null && (!_ghostColorSet || landing.Aligned != _ghostAligned))
            {
                _ghostAligned = landing.Aligned;
                _ghostColorSet = true;
                _ghostProps.SetColor(BaseColorId, landing.Aligned ? tuning.ghostAlignedColor : tuning.ghostColor);
                ghostRenderer.SetPropertyBlock(_ghostProps);
            }

            if (!ghost.gameObject.activeSelf)
            {
                ghost.gameObject.SetActive(true);
            }
        }

        private void HideGhost()
        {
            if (ghost != null && ghost.gameObject.activeSelf)
            {
                ghost.gameObject.SetActive(false);
            }
        }

        // Sweeps the block's bounding box straight down and reports the first thing that isn't the block
        // itself. The distance is measured from the given position and is negative when the resting place
        // is above it. Fails when nothing is underneath or the block is deep inside something.
        private bool CastDown(StackBlock block, Vector3 position, Quaternion rotation, out float distance,
            out Collider support)
        {
            distance = 0f;
            support = null;

            Vector3 half = block.HalfSize - new Vector3(Skin, Skin, Skin);
            int count = Physics.BoxCastNonAlloc(position + Vector3.up * Lift, half, Vector3.down, _hits, rotation,
                tuning.ghostMaxDrop + Lift, castMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (StackBlock.TryGet(hit.collider, out StackBlock other) && other == block)
                {
                    continue;
                }

                // Unity reports "already overlapping at the start" as distance 0 with a zero point.
                if (hit.distance <= 0f && hit.point == Vector3.zero)
                {
                    return false;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    support = hit.collider;
                }
            }

            if (support == null)
            {
                return false;
            }

            // The box was shrunk, so the real surfaces touch a little earlier. Stop a hair short rather
            // than a hair inside; physics closes the last millimetre.
            distance = nearest - Skin * 1.75f - Lift;
            return true;
        }
    }
}
