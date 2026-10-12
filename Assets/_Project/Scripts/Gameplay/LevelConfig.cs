using System;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// One playable setup: how long it runs, what it takes to win or lose, and which blocks appear when.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "Stack Tower/Level Config")]
    public sealed class LevelConfig : ScriptableObject
    {
        [Serializable]
        public struct BlockEntry
        {
            public BlockDefinition definition;
            [Tooltip("Seconds into the run before this block can appear.")]
            public float unlockTime;
            [Tooltip("Multiplies the block's own spawn weight for this level. 0 is treated as 1.")]
            public float weightScale;
        }

        public GameMode mode = GameMode.TimedTower;
        public string displayName = "Timed Tower";

        [Header("Time (seconds from run start)")]
        [Tooltip("Total run length. 0 means no timer (Zen).")]
        public float duration = 90f;
        [Tooltip("Hook: pedestal rises and the first block floats in.")]
        public float introDuration = 5f;
        public float firstSpawnTime = 1.5f;
        [Tooltip("Learn phase ends here: only the opening blocks, slow spawns, no timer pressure.")]
        public float learnEnd = 20f;
        [Tooltip("Final rush starts here.")]
        public float finalRushStart = 70f;
        [Tooltip("Audible countdown over the last seconds.")]
        public int countdownSeconds = 10;

        [Header("Goal")]
        [Tooltip("Run ends in success when the tower reaches this height (m). 0 means no target.")]
        public float targetHeight;
        [Tooltip("Off for Zen: a falling tower doesn't end the run.")]
        public bool canFail = true;

        [Header("Blocks")]
        public BlockEntry[] blocks = Array.Empty<BlockEntry>();
        [Tooltip("Optional fixed blocks handed out first, before the weighted random pick takes over.")]
        public BlockDefinition[] openingSequence = Array.Empty<BlockDefinition>();
        [Tooltip("Golden blocks are this much more likely during the final rush.")]
        public float goldenFinalRushScale = 4f;

        [Header("Spawn pacing (delay after a block is placed)")]
        public float spawnDelayLearn = 0.6f;
        public float spawnDelayRamp = 0.3f;
        public float spawnDelayFinalRush = 0.1f;

        [Header("Random")]
        [Tooltip("On: same block order every run (Daily Challenge). Off: a new order each run.")]
        public bool useFixedSeed;
        public int seed = 12345;

        public bool HasTimer => duration > 0f;

        public float SpawnDelay(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.Hook:
                case RunPhase.Learn:
                    return spawnDelayLearn;
                case RunPhase.FinalRush:
                    return spawnDelayFinalRush;
                default:
                    return spawnDelayRamp;
            }
        }
    }
}
