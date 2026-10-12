using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Picks the next block, pulls it from the pool and presents it on the spawn shelf, one at a time.
    /// Blocks are created up front; nothing is instantiated or destroyed during a run.
    /// </summary>
    public sealed class BlockSpawner : MonoBehaviour
    {
        // Far below the floor: where pooled blocks are warmed up for one frame, out of sight and reach.
        private static readonly Vector3 WarmUpPosition = new Vector3(0f, -50f, 0f);

        [Tooltip("Where a new block appears: chest height, toward the dominant hand.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Parent for pooled block instances.")]
        [SerializeField] private Transform poolRoot;

        [Header("Presentation")]
        [Tooltip("The new block floats up this far while it appears (m).")]
        [SerializeField] private float floatInDistance = 0.08f;
        [SerializeField] private float floatInTime = 0.3f;
        [Tooltip("Gentle bob while the block waits to be grabbed (m).")]
        [SerializeField] private float bobAmplitude = 0.008f;
        [SerializeField] private float bobFrequency = 0.7f;
        [SerializeField] private int warmUpPerFrame = 4;

        private readonly Dictionary<BlockDefinition, Stack<StackBlock>> _pool =
            new Dictionary<BlockDefinition, Stack<StackBlock>>();
        private readonly List<StackBlock> _active = new List<StackBlock>(64);
        private readonly List<StackBlock> _lingering = new List<StackBlock>(4);
        private readonly List<float> _lingerTimers = new List<float>(4);
        private readonly List<StackBlock> _warmUpBatch = new List<StackBlock>(8);

        private LevelConfig _level;
        private System.Random _random;
        private int _sequenceIndex;
        private float _fallY;
        private bool _spawning;
        private float _spawnTimer = -1f;
        private float _presentTime;
        private Coroutine _warmUp;

        /// <summary>Raised when a block is taken out of the pool, before it is shown.</summary>
        public event Action<StackBlock> BlockSpawned;
        public event Action<StackBlock> BlockDespawned;

        /// <summary>The block waiting on the shelf, if any.</summary>
        public StackBlock Waiting { get; private set; }
        public IReadOnlyList<StackBlock> ActiveBlocks => _active;
        public Transform SpawnPoint => spawnPoint;

        /// <summary>Seconds since the run started. Decides which blocks have unlocked.</summary>
        public float RunTime { get; set; }
        public RunPhase Phase { get; set; }

        /// <summary>Prepares for a run: resets the sequence and makes sure the pool is filled.</summary>
        public void BeginRun(LevelConfig level, float fallY)
        {
            _level = level;
            _fallY = fallY;
            _sequenceIndex = 0;
            _spawnTimer = -1f;
            _spawning = true;
            RunTime = 0f;
            _random = new System.Random(level.useFixedSeed ? level.seed : Environment.TickCount);

            if (_warmUp != null)
            {
                // A restart interrupted the previous warm-up: put its half-finished batch away first.
                StopCoroutine(_warmUp);
                Shelve(_warmUpBatch);
            }

            _warmUp = StartCoroutine(WarmUpPool(level));
        }

        public void StopSpawning()
        {
            _spawning = false;
            _spawnTimer = -1f;
        }

        /// <summary>Spawns the next block after a delay. Ignored while one is already on its way.</summary>
        public void QueueSpawn(float delay)
        {
            if (!_spawning || Waiting != null || _spawnTimer >= 0f)
            {
                return;
            }

            _spawnTimer = Mathf.Max(0f, delay);
        }

        public StackBlock SpawnNow()
        {
            if (!_spawning || _level == null || Waiting != null)
            {
                return null;
            }

            _spawnTimer = -1f;
            BlockDefinition definition = PickDefinition();
            if (definition == null)
            {
                return null;
            }

            StackBlock block = Take(definition);
            _active.Add(block);
            BlockSpawned?.Invoke(block);

            block.Spawn(spawnPoint.position - Vector3.up * floatInDistance, spawnPoint.rotation, _fallY);
            block.SetVisualScale(0.4f);
            block.OnGrabbed += HandleGrabbed;

            Waiting = block;
            _presentTime = 0f;
            GameEvents.RaiseBlockSpawned(block);
            return block;
        }

        /// <summary>Recycles a block that was dropped without being placed, after it has had time to be seen falling.</summary>
        public void DespawnAfter(StackBlock block, float delay)
        {
            if (block == null || _lingering.Contains(block))
            {
                return;
            }

            block.SetGrabbable(false);
            _lingering.Add(block);
            _lingerTimers.Add(delay);
        }

        public void Despawn(StackBlock block)
        {
            if (block == null || !_active.Remove(block))
            {
                return;
            }

            int lingerIndex = _lingering.IndexOf(block);
            if (lingerIndex >= 0)
            {
                _lingering.RemoveAt(lingerIndex);
                _lingerTimers.RemoveAt(lingerIndex);
            }

            if (Waiting == block)
            {
                Waiting = null;
            }

            block.OnGrabbed -= HandleGrabbed;
            BlockDespawned?.Invoke(block);
            block.Despawn();
            _pool[block.Definition].Push(block);
        }

        public void DespawnAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Despawn(_active[i]);
            }
        }

        /// <summary>Run over: nothing can be picked up any more.</summary>
        public void LockAllBlocks()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                _active[i].SetGrabbable(false);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_spawnTimer >= 0f)
            {
                _spawnTimer -= dt;
                if (_spawnTimer < 0f)
                {
                    SpawnNow();
                }
            }

            PresentWaitingBlock(dt);

            for (int i = _lingering.Count - 1; i >= 0; i--)
            {
                _lingerTimers[i] -= dt;
                if (_lingerTimers[i] <= 0f)
                {
                    Despawn(_lingering[i]);
                }
            }
        }

        // The waiting block floats up into place, then bobs gently until it is grabbed.
        private void PresentWaitingBlock(float dt)
        {
            StackBlock block = Waiting;
            if (block == null || block.State != StackBlock.BlockState.Spawned)
            {
                return;
            }

            _presentTime += dt;
            float t = floatInTime <= 0f ? 1f : Mathf.Clamp01(_presentTime / floatInTime);
            float eased = 1f - (1f - t) * (1f - t);
            float bob = Mathf.Sin(_presentTime * bobFrequency * Mathf.PI * 2f) * bobAmplitude * t;

            block.SetVisualScale(Mathf.Lerp(0.4f, 1f, eased));
            block.transform.SetPositionAndRotation(
                spawnPoint.position + Vector3.up * (-floatInDistance * (1f - eased) + bob),
                spawnPoint.rotation);
        }

        private void HandleGrabbed(StackBlock block)
        {
            if (Waiting == block)
            {
                Waiting = null;
            }
        }

        private BlockDefinition PickDefinition()
        {
            BlockDefinition[] opening = _level.openingSequence;
            if (opening != null && _sequenceIndex < opening.Length)
            {
                return opening[_sequenceIndex++];
            }

            LevelConfig.BlockEntry[] entries = _level.blocks;
            float total = 0f;
            for (int i = 0; i < entries.Length; i++)
            {
                total += Weight(entries[i]);
            }

            if (total <= 0f)
            {
                return entries.Length > 0 ? entries[0].definition : null;
            }

            float roll = (float)_random.NextDouble() * total;
            for (int i = 0; i < entries.Length; i++)
            {
                float weight = Weight(entries[i]);
                if (weight <= 0f)
                {
                    continue;
                }

                roll -= weight;
                if (roll <= 0f)
                {
                    return entries[i].definition;
                }
            }

            return entries[entries.Length - 1].definition;
        }

        private float Weight(LevelConfig.BlockEntry entry)
        {
            if (entry.definition == null || entry.unlockTime > RunTime)
            {
                return 0f;
            }

            float weight = entry.definition.spawnWeight * (entry.weightScale > 0f ? entry.weightScale : 1f);
            if (entry.definition.isGolden && Phase == RunPhase.FinalRush)
            {
                weight *= _level.goldenFinalRushScale;
            }

            return weight;
        }

        private StackBlock Take(BlockDefinition definition)
        {
            if (_pool.TryGetValue(definition, out Stack<StackBlock> stack) && stack.Count > 0)
            {
                return stack.Pop();
            }

            // Pool ran dry mid-run. Raise the definition's pool size if this shows up.
            Debug.LogWarning($"[BlockSpawner] Pool for '{definition.name}' is empty; creating one more.");
            return Create(definition);
        }

        private StackBlock Create(BlockDefinition definition)
        {
            if (!_pool.ContainsKey(definition))
            {
                _pool.Add(definition, new Stack<StackBlock>(definition.poolSize));
            }

            StackBlock block = Instantiate(definition.prefab, WarmUpPosition, Quaternion.identity, poolRoot);
            block.name = definition.name;
            return block;
        }

        // Creates the pooled instances a few per frame during the intro. Each one stays active for a
        // frame so its components finish initialising now rather than at its first spawn.
        private IEnumerator WarmUpPool(LevelConfig level)
        {
            List<StackBlock> batch = _warmUpBatch;

            for (int e = 0; e < level.blocks.Length; e++)
            {
                BlockDefinition definition = level.blocks[e].definition;
                if (definition == null || definition.prefab == null)
                {
                    continue;
                }

                if (!_pool.TryGetValue(definition, out Stack<StackBlock> stack))
                {
                    stack = new Stack<StackBlock>(definition.poolSize);
                    _pool.Add(definition, stack);
                }

                int inUse = 0;
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].Definition == definition)
                    {
                        inUse++;
                    }
                }

                for (int missing = definition.poolSize - stack.Count - inUse; missing > 0; missing--)
                {
                    StackBlock block = Create(definition);
                    block.Spawn(WarmUpPosition, Quaternion.identity, float.NegativeInfinity);
                    batch.Add(block);

                    if (batch.Count >= warmUpPerFrame)
                    {
                        yield return null;
                        Shelve(batch);
                    }
                }
            }

            yield return null;
            Shelve(batch);
            _warmUp = null;
        }

        private void Shelve(List<StackBlock> batch)
        {
            for (int i = 0; i < batch.Count; i++)
            {
                StackBlock block = batch[i];
                block.Despawn();
                _pool[block.Definition].Push(block);
            }

            batch.Clear();
        }
    }
}
