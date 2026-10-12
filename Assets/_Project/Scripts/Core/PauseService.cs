using System;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Freezes the run (physics, timers, audio) while the app can't be played: paused by the OS,
    /// headset taken off, or the system menu open. Reasons stack, so the game only resumes when
    /// every one of them has cleared.
    /// </summary>
    public static class PauseService
    {
        [Flags]
        public enum Reason
        {
            None = 0,
            ApplicationPaused = 1 << 0,
            InputFocusLost = 1 << 1,
            HeadsetRemoved = 1 << 2
        }

        private static Reason _reasons;

        public static event Action<bool> PauseChanged;

        public static bool IsPaused => _reasons != Reason.None;

        public static void Set(Reason reason, bool paused)
        {
            bool wasPaused = IsPaused;
            _reasons = paused ? _reasons | reason : _reasons & ~reason;

            if (IsPaused == wasPaused)
            {
                return;
            }

            Time.timeScale = IsPaused ? 0f : 1f;
            AudioListener.pause = IsPaused;
            PauseChanged?.Invoke(IsPaused);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _reasons = Reason.None;
            PauseChanged = null;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }
}
