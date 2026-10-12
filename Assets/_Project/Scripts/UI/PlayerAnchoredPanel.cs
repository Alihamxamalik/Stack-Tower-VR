using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Puts a world-space panel in front of the player once, at a height that suits a poke
    /// (waist to chest), tilted to face their eyes. It does not follow the head afterwards.
    /// </summary>
    public sealed class PlayerAnchoredPanel : MonoBehaviour
    {
        [SerializeField] private float distance = 0.55f;
        [Tooltip("Panel centre height as a fraction of eye height.")]
        [SerializeField] private float heightRatio = 0.78f;
        [Tooltip("Place anyway if the headset hasn't reported a pose by then (s).")]
        [SerializeField] private float timeout = 2.5f;
        [SerializeField] private float defaultEyeHeight = 1.6f;

        private Transform _head;
        private float _waited;
        private bool _placed;

        private void OnEnable()
        {
            _placed = false;
            _waited = 0f;
        }

        private void Update()
        {
            if (_placed)
            {
                return;
            }

            if (_head == null)
            {
                Camera main = Camera.main;
                _head = main != null ? main.transform : null;
            }

            _waited += Time.unscaledDeltaTime;
            bool tracked = _head != null && _head.position.y > 0.3f;
            if (tracked || _waited >= timeout)
            {
                Place(tracked);
            }
        }

        private void Place(bool tracked)
        {
            _placed = true;

            Vector3 eyes = tracked ? _head.position : new Vector3(0f, defaultEyeHeight, 0f);
            Vector3 forward = _head != null ? Vector3.ProjectOnPlane(_head.forward, Vector3.up) : Vector3.forward;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;

            Vector3 position = eyes + forward * distance;
            position.y = eyes.y * heightRatio;

            // A canvas is read from its -Z side, so point +Z away from the eyes.
            transform.SetPositionAndRotation(position, Quaternion.LookRotation((position - eyes).normalized, Vector3.up));
        }
    }
}
