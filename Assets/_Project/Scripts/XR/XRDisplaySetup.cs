using System.Collections;
using UnityEngine;

namespace StackTower.XR
{
    /// <summary>
    /// Headset-level settings applied once at startup (90 Hz, fixed foveated rendering with dynamic
    /// foveation) and pausing the run when the headset comes off or the system menu opens.
    /// </summary>
    public sealed class XRDisplaySetup : MonoBehaviour
    {
        [SerializeField] private float targetRefreshRate = 90f;
        [Tooltip("Used only if the target rate isn't offered.")]
        [SerializeField] private float fallbackRefreshRate = 72f;
        [SerializeField] private OVRManager.FoveatedRenderingLevel foveation = OVRManager.FoveatedRenderingLevel.Medium;
        [SerializeField] private bool dynamicFoveation = true;
        [SerializeField] private float startupTimeout = 5f;

        private void OnEnable()
        {
            OVRManager.InputFocusLost += HandleInputFocusLost;
            OVRManager.InputFocusAcquired += HandleInputFocusAcquired;
            OVRManager.HMDUnmounted += HandleUnmounted;
            OVRManager.HMDMounted += HandleMounted;
        }

        private void OnDisable()
        {
            OVRManager.InputFocusLost -= HandleInputFocusLost;
            OVRManager.InputFocusAcquired -= HandleInputFocusAcquired;
            OVRManager.HMDUnmounted -= HandleUnmounted;
            OVRManager.HMDMounted -= HandleMounted;
        }

        private IEnumerator Start()
        {
            float waited = 0f;
            while (!OVRManager.isHmdPresent && waited < startupTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!OVRManager.isHmdPresent)
            {
                yield break;
            }

            ApplyRefreshRate();

            if (OVRManager.fixedFoveatedRenderingSupported)
            {
                OVRManager.foveatedRenderingLevel = foveation;
                OVRManager.useDynamicFoveatedRendering = dynamicFoveation;
            }
        }

        private void ApplyRefreshRate()
        {
            OVRDisplay display = OVRManager.display;
            if (display == null)
            {
                return;
            }

            float[] available = display.displayFrequenciesAvailable;
            float chosen = 0f;
            if (available != null)
            {
                for (int i = 0; i < available.Length; i++)
                {
                    if (Mathf.Abs(available[i] - targetRefreshRate) < 0.5f)
                    {
                        chosen = available[i];
                        break;
                    }

                    if (Mathf.Abs(available[i] - fallbackRefreshRate) < 0.5f)
                    {
                        chosen = available[i];
                    }
                }
            }

            if (chosen > 0f)
            {
                display.displayFrequency = chosen;
                Debug.Log($"[XRDisplaySetup] Display refresh rate set to {chosen} Hz.");
            }
        }

        private static void HandleInputFocusLost()
        {
            PauseService.Set(PauseService.Reason.InputFocusLost, true);
        }

        private static void HandleInputFocusAcquired()
        {
            PauseService.Set(PauseService.Reason.InputFocusLost, false);
        }

        private static void HandleUnmounted()
        {
            PauseService.Set(PauseService.Reason.HeadsetRemoved, true);
        }

        private static void HandleMounted()
        {
            PauseService.Set(PauseService.Reason.HeadsetRemoved, false);
        }
    }
}
