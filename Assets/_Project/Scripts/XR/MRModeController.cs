using System.Collections;
using UnityEngine;
using UnityEngine.XR;

namespace StackTower.XR
{
    /// <summary>
    /// Mixed reality or virtual room. In mixed reality the eye camera clears to transparent so the
    /// passthrough underlay shows the real room, and the virtual set dressing hides itself
    /// (<see cref="VirtualEnvironment"/>). Falls back to the virtual room wherever passthrough isn't
    /// available, which includes the editor without a headset.
    /// </summary>
    public sealed class MRModeController : MonoBehaviour
    {
        [SerializeField] private OVRManager manager;
        [SerializeField] private OVRPassthroughLayer passthroughLayer;
        [SerializeField] private Camera eyeCamera;
        [SerializeField] private Color virtualBackground = new Color(0.69f, 0.82f, 0.93f, 1f);
        [Tooltip("How long to wait for the headset before deciding passthrough isn't available (s).")]
        [SerializeField] private float startupTimeout = 3f;

        private bool _ready;

        private void OnEnable()
        {
            SaveService.SettingsChanged += Apply;
        }

        private void OnDisable()
        {
            SaveService.SettingsChanged -= Apply;
        }

        private IEnumerator Start()
        {
            // Start in the virtual room; passthrough support can only be asked once XR is running.
            ApplyMode(false);

            float waited = 0f;
            while (!XRSettings.isDeviceActive && waited < startupTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            _ready = true;
            Apply();
        }

        private void Apply()
        {
            if (!_ready)
            {
                return;
            }

            bool supported = XRSettings.isDeviceActive && OVRManager.IsInsightPassthroughSupported();
            ApplyMode(SaveService.Settings.mixedReality && supported);
        }

        private void ApplyMode(bool passthrough)
        {
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = passthrough;
                // A seated/standing game never needs the boundary wall drawn over the real room.
                manager.shouldBoundaryVisibilityBeSuppressed = passthrough;
            }

            if (passthroughLayer != null)
            {
                passthroughLayer.enabled = passthrough;
            }

            if (eyeCamera != null)
            {
                eyeCamera.clearFlags = CameraClearFlags.SolidColor;
                eyeCamera.backgroundColor = passthrough ? Color.clear : virtualBackground;
            }

            MixedReality.SetPassthroughActive(passthrough);
        }
    }
}
