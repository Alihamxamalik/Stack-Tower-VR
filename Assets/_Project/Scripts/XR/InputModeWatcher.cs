using UnityEngine;

namespace StackTower.XR
{
    /// <summary>
    /// Notices whether the player is using bare hands or controllers (the headset switches on its own
    /// when controllers are picked up or put down) and tells the rest of the game, so hints and
    /// haptics follow.
    /// </summary>
    public sealed class InputModeWatcher : MonoBehaviour
    {
        [SerializeField] private float pollInterval = 0.2f;

        private float _timer;

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = pollInterval;

            OVRInput.Controller active = OVRInput.GetActiveController();
            if ((active & OVRInput.Controller.Touch) != 0)
            {
                GameEvents.RaiseInputModeChanged(InputMode.Controllers);
            }
            else if ((active & OVRInput.Controller.Hands) != 0)
            {
                GameEvents.RaiseInputModeChanged(InputMode.Hands);
            }
        }
    }
}
