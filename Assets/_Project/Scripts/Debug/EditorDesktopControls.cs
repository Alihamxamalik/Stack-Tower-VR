using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR;
#endif

namespace StackTower
{
    /// <summary>
    /// Editor-only stand-in for a headset, so the game loop can be played with mouse and keyboard
    /// when no XR device (or simulator) is running. It lifts the rig to standing height and lets you:
    ///   left mouse    grab a block / click a button
    ///   mouse wheel   move the held block nearer or further
    ///   Q / E         turn the held block, R / F tip it
    ///   right mouse   look around
    /// It does nothing in builds, and nothing in the editor once a real XR device is active.
    /// </summary>
    public sealed class EditorDesktopControls : MonoBehaviour
    {
        // The fields stay in builds so the serialized layout matches the editor's; only the code is stripped.
#pragma warning disable 0414
        [Tooltip("The camera rig. Lifted to eye height when no headset is driving it.")]
        [SerializeField] private Transform rigRoot;
        [SerializeField] private float eyeHeight = 1.6f;
        [Tooltip("Initial look-down angle so the pedestal, shelf and side panel are in view (deg).")]
        [SerializeField] private float lookDown = 38f;
#pragma warning restore 0414

#if UNITY_EDITOR
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private Camera _camera;
        private StackBlock _held;
        private float _depth;
        private float _yaw;
        private float _pitch;
        private Quaternion _heldRotation;

        private void Start()
        {
            if (XRSettings.isDeviceActive || rigRoot == null)
            {
                enabled = false;
                return;
            }

            _pitch = lookDown;
            rigRoot.position += Vector3.up * eyeHeight;
            rigRoot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Debug.Log("[EditorDesktopControls] No XR device: mouse and keyboard controls are on " +
                      "(LMB grab/click, wheel depth, Q/E/R/F rotate, RMB look).");
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * 0.15f;
                _pitch = Mathf.Clamp(_pitch - delta.y * 0.15f, -60f, 75f);
                rigRoot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (!TryGrab(ray))
                {
                    ClickUi(mouse.position.ReadValue());
                }
            }

            if (_held != null)
            {
                DriveHeld(ray, mouse, keyboard);
            }
        }

        private bool TryGrab(Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, 5f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                || !StackBlock.TryGet(hit.collider, out StackBlock block) || !block.IsGrabbable)
            {
                return false;
            }

            _held = block;
            _depth = Vector3.Dot(block.Position - ray.origin, ray.direction);
            _heldRotation = block.Rotation;
            block.BeginHold();
            return true;
        }

        private void DriveHeld(Ray ray, Mouse mouse, Keyboard keyboard)
        {
            if (!_held.IsHeld)
            {
                // Something else ended the hold (run over, block despawned).
                _held = null;
                return;
            }

            if (!mouse.leftButton.isPressed)
            {
                _held.EndHold();
                _held = null;
                return;
            }

            // Wheel units differ wildly between mice, trackpads and platforms, so cap the step per frame.
            float scroll = Mathf.Clamp(mouse.scroll.ReadValue().y * 0.01f, -0.03f, 0.03f);
            _depth = Mathf.Clamp(_depth + scroll, 0.2f, 3f);

            if (keyboard != null)
            {
                float turn = 120f * Time.unscaledDeltaTime;
                if (keyboard.qKey.isPressed)
                {
                    _heldRotation = Quaternion.AngleAxis(-turn, Vector3.up) * _heldRotation;
                }

                if (keyboard.eKey.isPressed)
                {
                    _heldRotation = Quaternion.AngleAxis(turn, Vector3.up) * _heldRotation;
                }

                if (keyboard.rKey.isPressed)
                {
                    _heldRotation = Quaternion.AngleAxis(turn, _camera.transform.right) * _heldRotation;
                }

                if (keyboard.fKey.isPressed)
                {
                    _heldRotation = Quaternion.AngleAxis(-turn, _camera.transform.right) * _heldRotation;
                }
            }

            _held.transform.SetPositionAndRotation(ray.GetPoint(_depth), _heldRotation);
        }

        // World-space canvases normally get their clicks from the XR pointer module. Without a headset,
        // raycast the mouse into them and deliver the click by hand.
        private void ClickUi(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    canvas.worldCamera = _camera;
                }
            }

            var pointer = new PointerEventData(eventSystem) { position = screenPosition };
            _uiHits.Clear();
            eventSystem.RaycastAll(pointer, _uiHits);
            if (_uiHits.Count > 0)
            {
                ExecuteEvents.ExecuteHierarchy(_uiHits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            }
        }
#endif
    }
}
