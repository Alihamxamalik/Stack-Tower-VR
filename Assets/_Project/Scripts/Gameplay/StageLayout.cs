using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Height calibration and layout. At the start of a run it reads where the player is standing
    /// (or sitting) and puts the pedestal, spawn shelf and side panel around them, so everything is
    /// within arm's reach and inside the headset cameras' view. Nothing follows the head afterwards.
    /// </summary>
    public sealed class StageLayout : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private Pedestal pedestal;
        [SerializeField] private Transform spawnShelf;
        [Tooltip("World-space panel that carries the HUD and the end card.")]
        [SerializeField] private Transform sidePanel;

        [Header("Layout (metres from the player; X is toward the dominant hand, Y is forward)")]
        [SerializeField] private float pedestalDistance = 0.55f;
        [SerializeField] private Vector2 shelfOffset = new Vector2(0.36f, 0.4f);
        [SerializeField] private Vector2 panelOffset = new Vector2(-0.4f, 0.52f);

        [Header("Heights as a fraction of eye height")]
        [SerializeField] private float pedestalHeightRatio = 0.45f;
        [SerializeField] private float shelfHeightRatio = 0.72f;
        [SerializeField] private float panelHeightRatio = 0.74f;

        [Header("Seated mode")]
        [SerializeField] private float seatedPedestalHeightRatio = 0.52f;
        [Tooltip("Pulls everything closer when seated.")]
        [SerializeField] private float seatedDistanceScale = 0.85f;

        [Header("Eye height")]
        [Tooltip("Used when no headset is tracked (editor without a device).")]
        [SerializeField] private float defaultEyeHeight = 1.6f;
        [SerializeField] private Vector2 eyeHeightRange = new Vector2(0.9f, 2.1f);

        private Transform _head;

        public Pedestal Pedestal => pedestal;
        public float EyeHeight { get; private set; } = 1.6f;

        /// <summary>True once the headset reports a believable height above the floor.</summary>
        public bool HasTrackedHead
        {
            get
            {
                Transform head = Head;
                return head != null && head.position.y - FloorY > 0.3f;
            }
        }

        // Floor-level tracking: the rig's origin is on the floor, and the stage is authored at that height.
        private float FloorY => transform.position.y;

        private Transform Head
        {
            get
            {
                if (_head == null)
                {
                    Camera main = Camera.main;
                    _head = main != null ? main.transform : null;
                }

                return _head;
            }
        }

        public void Calibrate()
        {
            Transform head = Head;
            bool tracked = HasTrackedHead;
            float floorY = FloorY;

            Vector3 headPosition = head != null
                ? head.position
                : new Vector3(transform.position.x, floorY + defaultEyeHeight, transform.position.z);

            Vector3 forward = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : transform.forward;
            if (forward.sqrMagnitude < 0.01f && head != null)
            {
                // Looking straight down or up: the top of the head still points the right way.
                forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            }

            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;

            EyeHeight = tracked
                ? Mathf.Clamp(headPosition.y - floorY, eyeHeightRange.x, eyeHeightRange.y)
                : defaultEyeHeight;

            SettingsData settings = SaveService.Settings;
            float side = settings.leftHanded ? -1f : 1f;
            float reach = settings.seated ? seatedDistanceScale : 1f;
            float pedestalRatio = settings.seated ? seatedPedestalHeightRatio : pedestalHeightRatio;

            transform.SetPositionAndRotation(
                new Vector3(headPosition.x, floorY, headPosition.z),
                Quaternion.LookRotation(forward, Vector3.up));

            if (pedestal != null)
            {
                pedestal.transform.localPosition = new Vector3(0f, 0f, pedestalDistance * reach);
                pedestal.transform.localRotation = Quaternion.identity;
                pedestal.SetHeight(EyeHeight * pedestalRatio);
            }

            if (spawnShelf != null)
            {
                spawnShelf.localPosition = new Vector3(
                    side * shelfOffset.x * reach, EyeHeight * shelfHeightRatio, shelfOffset.y * reach);
                spawnShelf.localRotation = Quaternion.identity;
            }

            if (sidePanel != null)
            {
                sidePanel.localPosition = new Vector3(
                    side * panelOffset.x * reach, EyeHeight * panelHeightRatio, panelOffset.y * reach);

                // A canvas is read from its -Z side, so point +Z away from the eyes.
                Vector3 eyes = new Vector3(headPosition.x, floorY + EyeHeight, headPosition.z);
                Vector3 away = sidePanel.position - eyes;
                if (away.sqrMagnitude > 0.0001f)
                {
                    sidePanel.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                }
            }
        }

        public void PlayIntro(float duration)
        {
            if (pedestal != null)
            {
                pedestal.PlayRise(duration);
            }
        }
    }
}
