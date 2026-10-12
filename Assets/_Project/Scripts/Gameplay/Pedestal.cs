using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// The base the tower stands on: a slab on a column, resized to suit the player's height.
    /// The top face of the slab is the surface every height is measured from.
    /// </summary>
    public sealed class Pedestal : MonoBehaviour
    {
        [Tooltip("Moved up out of the floor during the intro. Parent of the slab and column.")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform slab;
        [SerializeField] private Transform column;
        [SerializeField] private Collider[] colliders;

        [Header("Size (metres)")]
        [SerializeField] private float width = 0.42f;
        [SerializeField] private float slabThickness = 0.05f;
        [SerializeField] private float columnWidth = 0.28f;
        [SerializeField] private float height = 0.75f;

        private float _riseDuration;
        private float _riseTime;
        private bool _rising;

        /// <summary>World height of the play surface.</summary>
        public float SurfaceY => transform.position.y + height;
        public Vector3 SurfaceCentre => new Vector3(transform.position.x, SurfaceY, transform.position.z);
        public Quaternion Rotation => transform.rotation;
        public float Width => width;
        public float Height => height;

        public bool Owns(Collider candidate)
        {
            if (colliders == null)
            {
                return false;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Sets how far the play surface sits above the pedestal's root (the floor).</summary>
        public void SetHeight(float surfaceHeight)
        {
            height = Mathf.Max(slabThickness + 0.01f, surfaceHeight);
            ApplySize();
        }

        /// <summary>Intro: the pedestal comes up out of the floor.</summary>
        public void PlayRise(float duration)
        {
            if (body == null || duration <= 0f)
            {
                SetRise(1f);
                _rising = false;
                return;
            }

            _riseDuration = duration;
            _riseTime = 0f;
            _rising = true;
            SetRise(0f);
        }

        private void Awake()
        {
            ApplySize();
        }

        private void Update()
        {
            if (!_rising)
            {
                return;
            }

            _riseTime += Time.deltaTime;
            float t = Mathf.Clamp01(_riseTime / _riseDuration);
            // Ease out: quick start, soft landing.
            SetRise(1f - (1f - t) * (1f - t) * (1f - t));
            if (t >= 1f)
            {
                _rising = false;
            }
        }

        private void SetRise(float amount)
        {
            if (body != null)
            {
                body.localPosition = new Vector3(0f, -height * (1f - amount), 0f);
            }
        }

        private void ApplySize()
        {
            float columnHeight = height - slabThickness;

            if (slab != null)
            {
                slab.localScale = new Vector3(width, slabThickness, width);
                slab.localPosition = new Vector3(0f, height - slabThickness * 0.5f, 0f);
            }

            if (column != null)
            {
                column.localScale = new Vector3(columnWidth, columnHeight, columnWidth);
                column.localPosition = new Vector3(0f, columnHeight * 0.5f, 0f);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplySize();
        }
#endif
    }
}
