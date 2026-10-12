using System;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Broadcast hub the systems talk through. Gameplay systems raise, presentation (HUD, audio,
    /// haptics) listens; nobody needs a direct reference to anybody else.
    /// Subscribe in OnEnable and unsubscribe in OnDisable.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<GameState> StateChanged;
        public static event Action<RunPhase> PhaseChanged;
        public static event Action<int> CountdownTick;
        public static event Action<RunResult> RunEnded;

        public static event Action<StackBlock> BlockSpawned;
        public static event Action<StackBlock> BlockGrabbed;
        public static event Action<StackBlock> BlockReleased;
        /// <summary>First contact after a release. The float is the impact speed in m/s.</summary>
        public static event Action<StackBlock, float> BlockImpact;
        public static event Action<PlacementInfo> BlockPlaced;
        /// <summary>A block that was never placed fell off the play area.</summary>
        public static event Action<StackBlock> BlockLost;

        /// <summary>Current height, max height (metres above the pedestal).</summary>
        public static event Action<float, float> HeightChanged;
        public static event Action<bool> WobbleChanged;
        public static event Action TowerFell;

        public static event Action<ScoreAward> ScoreAwarded;
        public static event Action<int> ScoreChanged;
        /// <summary>Multiplier and combo step (0 = no combo).</summary>
        public static event Action<float, int> ComboChanged;

        public static event Action<InputMode> InputModeChanged;

        public static InputMode CurrentInputMode { get; private set; } = InputMode.Hands;

        public static void RaiseStateChanged(GameState state) => StateChanged?.Invoke(state);
        public static void RaisePhaseChanged(RunPhase phase) => PhaseChanged?.Invoke(phase);
        public static void RaiseCountdownTick(int secondsLeft) => CountdownTick?.Invoke(secondsLeft);
        public static void RaiseRunEnded(RunResult result) => RunEnded?.Invoke(result);

        public static void RaiseBlockSpawned(StackBlock block) => BlockSpawned?.Invoke(block);
        public static void RaiseBlockGrabbed(StackBlock block) => BlockGrabbed?.Invoke(block);
        public static void RaiseBlockReleased(StackBlock block) => BlockReleased?.Invoke(block);
        public static void RaiseBlockImpact(StackBlock block, float speed) => BlockImpact?.Invoke(block, speed);
        public static void RaiseBlockPlaced(PlacementInfo info) => BlockPlaced?.Invoke(info);
        public static void RaiseBlockLost(StackBlock block) => BlockLost?.Invoke(block);

        public static void RaiseHeightChanged(float current, float max) => HeightChanged?.Invoke(current, max);
        public static void RaiseWobbleChanged(bool wobbling) => WobbleChanged?.Invoke(wobbling);
        public static void RaiseTowerFell() => TowerFell?.Invoke();

        public static void RaiseScoreAwarded(ScoreAward award) => ScoreAwarded?.Invoke(award);
        public static void RaiseScoreChanged(int score) => ScoreChanged?.Invoke(score);
        public static void RaiseComboChanged(float multiplier, int step) => ComboChanged?.Invoke(multiplier, step);

        public static void RaiseInputModeChanged(InputMode mode)
        {
            if (mode == CurrentInputMode)
            {
                return;
            }

            CurrentInputMode = mode;
            InputModeChanged?.Invoke(mode);
        }

        // Statics survive play sessions when domain reload is disabled, so start each one clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            StateChanged = null;
            PhaseChanged = null;
            CountdownTick = null;
            RunEnded = null;
            BlockSpawned = null;
            BlockGrabbed = null;
            BlockReleased = null;
            BlockImpact = null;
            BlockPlaced = null;
            BlockLost = null;
            HeightChanged = null;
            WobbleChanged = null;
            TowerFell = null;
            ScoreAwarded = null;
            ScoreChanged = null;
            ComboChanged = null;
            InputModeChanged = null;
            CurrentInputMode = InputMode.Hands;
        }
    }
}
