using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;

namespace StackTower.XR
{
    /// <summary>
    /// Short controller pulses on grab, placement and wobble. Does nothing with bare hands, where
    /// the audio carries that feedback instead.
    /// </summary>
    public sealed class XRHaptics : MonoBehaviour
    {
        [SerializeField] private float grabAmplitude = 0.35f;
        [SerializeField] private float grabDuration = 0.04f;
        [SerializeField] private float placeAmplitude = 0.6f;
        [SerializeField] private float placeDuration = 0.06f;
        [SerializeField] private float wobbleAmplitude = 0.3f;
        [SerializeField] private float wobbleDuration = 0.18f;

        private static OVRInput.Controller _lastGrabController = OVRInput.Controller.RTouch;

        private float _leftTimer;
        private float _rightTimer;

        /// <summary>Remembers which side grabbed last so the placement pulse goes to the same hand.</summary>
        public static void NoteGrab(object interactor)
        {
            Handedness? side = null;
            if (interactor is HandGrabInteractor handGrab && handGrab.Hand != null)
            {
                side = handGrab.Hand.Handedness;
            }
            else if (interactor is Component component)
            {
                IController controller = component.GetComponentInParent<IController>();
                if (controller != null)
                {
                    side = controller.Handedness;
                }
                else
                {
                    IHand hand = component.GetComponentInParent<IHand>();
                    if (hand != null)
                    {
                        side = hand.Handedness;
                    }
                }
            }

            if (side.HasValue)
            {
                _lastGrabController = side.Value == Handedness.Left
                    ? OVRInput.Controller.LTouch
                    : OVRInput.Controller.RTouch;
            }
        }

        private void OnEnable()
        {
            GameEvents.BlockGrabbed += HandleGrabbed;
            GameEvents.BlockImpact += HandleImpact;
            GameEvents.WobbleChanged += HandleWobble;
        }

        private void OnDisable()
        {
            GameEvents.BlockGrabbed -= HandleGrabbed;
            GameEvents.BlockImpact -= HandleImpact;
            GameEvents.WobbleChanged -= HandleWobble;
            Stop(OVRInput.Controller.LTouch);
            Stop(OVRInput.Controller.RTouch);
        }

        private void Update()
        {
            Tick(ref _leftTimer, OVRInput.Controller.LTouch);
            Tick(ref _rightTimer, OVRInput.Controller.RTouch);
        }

        private void HandleGrabbed(StackBlock block)
        {
            Pulse(_lastGrabController, grabAmplitude, grabDuration);
        }

        private void HandleImpact(StackBlock block, float speed)
        {
            Pulse(_lastGrabController, placeAmplitude, placeDuration);
        }

        private void HandleWobble(bool wobbling)
        {
            if (wobbling)
            {
                Pulse(OVRInput.Controller.LTouch, wobbleAmplitude, wobbleDuration);
                Pulse(OVRInput.Controller.RTouch, wobbleAmplitude, wobbleDuration);
            }
        }

        private void Pulse(OVRInput.Controller controller, float amplitude, float duration)
        {
            if (GameEvents.CurrentInputMode != InputMode.Controllers)
            {
                return;
            }

            OVRInput.SetControllerVibration(1f, amplitude, controller);
            if (controller == OVRInput.Controller.LTouch)
            {
                _leftTimer = duration;
            }
            else
            {
                _rightTimer = duration;
            }
        }

        // Vibration keeps going until told to stop, so every pulse needs its own stop.
        private static void Tick(ref float timer, OVRInput.Controller controller)
        {
            if (timer <= 0f)
            {
                return;
            }

            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f)
            {
                Stop(controller);
            }
        }

        private static void Stop(OVRInput.Controller controller)
        {
            OVRInput.SetControllerVibration(0f, 0f, controller);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _lastGrabController = OVRInput.Controller.RTouch;
        }
    }
}
