using System;
using System.Collections.Generic;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Watches the tower: which blocks count as placed, how tall it is and was, whether it is
    /// wobbling, whether it has fallen, and which deep blocks can be frozen to save physics work.
    /// </summary>
    public sealed class TowerMonitor : MonoBehaviour
    {
        // How often height, wobble and failure are re-checked. Placement itself is event driven.
        private const float CheckInterval = 0.1f;
        // A block rests on another when its bottom is within this of the other's top (m).
        private const float SupportTolerance = 0.02f;

        private static readonly Comparison<StackBlock> ByBottom = (a, b) => a.BottomY.CompareTo(b.BottomY);

        [SerializeField] private GameTuning tuning;
        [SerializeField] private Pedestal pedestal;
        [SerializeField] private BlockSpawner spawner;
        [SerializeField] private LayerMask supportMask = Physics.DefaultRaycastLayers;

        private readonly List<StackBlock> _placed = new List<StackBlock>(64);
        private readonly List<int> _levels = new List<int>(64);
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private bool _running;
        private bool _canFail = true;
        private float _checkTimer;
        private float _wobbleTime;
        private float _calmTime;
        private float _freezeAllowedAt;
        private bool _freezeDirty;

        public float CurrentHeight { get; private set; }
        public float MaxHeight { get; private set; }
        public int PlacedCount => _placed.Count;
        public bool IsWobbling { get; private set; }
        public bool HasFallen { get; private set; }
        public IReadOnlyList<StackBlock> PlacedBlocks => _placed;

        /// <summary>World height below which a block has fallen off the pedestal.</summary>
        public float FallY => pedestal.SurfaceY - tuning.fallMargin;

        public void BeginRun(bool canFail)
        {
            _placed.Clear();
            _running = true;
            _canFail = canFail;
            _checkTimer = 0f;
            _wobbleTime = 0f;
            _calmTime = 0f;
            _freezeAllowedAt = 0f;
            _freezeDirty = false;
            CurrentHeight = 0f;
            MaxHeight = 0f;
            HasFallen = false;
            SetWobbling(false);
            GameEvents.RaiseHeightChanged(0f, 0f);
        }

        /// <summary>Stops counting placements. The blocks keep simulating.</summary>
        public void EndRun()
        {
            _running = false;
            SetWobbling(false);
        }

        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.BlockSpawned += Track;
                spawner.BlockDespawned += Untrack;
            }
        }

        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.BlockSpawned -= Track;
                spawner.BlockDespawned -= Untrack;
            }
        }

        private void Track(StackBlock block)
        {
            block.OnSettled += HandleCameToRest;
            block.OnFell += HandleFell;
            block.OnBigHit += HandleBigHit;
            block.OnImpact += HandleImpact;
        }

        private void Untrack(StackBlock block)
        {
            block.OnSettled -= HandleCameToRest;
            block.OnFell -= HandleFell;
            block.OnBigHit -= HandleBigHit;
            block.OnImpact -= HandleImpact;
            block.SetTint(tuning.wobbleTint, 0f);
            _placed.Remove(block);
        }

        private void FixedUpdate()
        {
            if (!_running || HasFallen)
            {
                return;
            }

            _checkTimer += Time.fixedDeltaTime;
            if (_checkTimer < CheckInterval)
            {
                return;
            }

            float elapsed = _checkTimer;
            _checkTimer = 0f;

            RecomputeHeight();
            UpdateWobble(elapsed);

            if (_canFail && MaxHeight >= tuning.failMinHeight
                         && CurrentHeight < MaxHeight * (1f - tuning.failTopDropFraction))
            {
                Fail();
                return;
            }

            if (_freezeDirty && !IsWobbling && Time.time >= _freezeAllowedAt)
            {
                _freezeDirty = false;
                UpdateFrozen();
            }
        }

        private void HandleImpact(StackBlock block, float speed)
        {
            GameEvents.RaiseBlockImpact(block, speed);
        }

        // A released block stopped moving. It only counts if it is resting on the pedestal or the tower.
        private void HandleCameToRest(StackBlock block)
        {
            if (!_running || HasFallen)
            {
                return;
            }

            if (block.BottomY < pedestal.SurfaceY - SupportTolerance)
            {
                return;
            }

            if (!FindSupport(block, out StackBlock below, out Vector3 supportCentre))
            {
                return;
            }

            block.ConfirmSettled();
            if (!block.IsPlaced)
            {
                return;
            }

            _placed.Add(block);

            float offset = PlacementMath.HorizontalDistance(block.Position, supportCentre);
            float previousMax = MaxHeight;
            RecomputeHeight();
            _freezeDirty = true;

            GameEvents.RaiseBlockPlaced(new PlacementInfo(
                block, below, offset, offset <= tuning.precisionRadius, previousMax, MaxHeight, CurrentHeight));
        }

        private void HandleFell(StackBlock block)
        {
            if (block.IsPlaced)
            {
                // Part of the tower went over the edge.
                if (_running && !HasFallen && _canFail)
                {
                    Fail();
                }

                return;
            }

            // A block that was never placed just missed. Recycle it and hand out another.
            if (spawner != null)
            {
                spawner.DespawnAfter(block, tuning.lostBlockLinger);
            }

            if (_running)
            {
                GameEvents.RaiseBlockLost(block);
            }
        }

        private void HandleBigHit(StackBlock block)
        {
            // The tower took a real knock: let the frozen part react too.
            _freezeAllowedAt = Time.time + tuning.refreezeDelay;
            _freezeDirty = true;
            for (int i = 0; i < _placed.Count; i++)
            {
                _placed[i].Unfreeze();
            }
        }

        private void Fail()
        {
            HasFallen = true;
            SetWobbling(false);
            for (int i = 0; i < _placed.Count; i++)
            {
                _placed[i].Unfreeze();
            }

            GameEvents.RaiseTowerFell();
        }

        private void RecomputeHeight()
        {
            float surface = pedestal.SurfaceY;
            float top = surface;
            for (int i = 0; i < _placed.Count; i++)
            {
                top = Mathf.Max(top, _placed[i].TopY);
            }

            float current = Mathf.Max(0f, top - surface);
            float max = Mathf.Max(MaxHeight, current);
            if (Mathf.Approximately(current, CurrentHeight) && Mathf.Approximately(max, MaxHeight))
            {
                return;
            }

            CurrentHeight = current;
            MaxHeight = max;
            GameEvents.RaiseHeightChanged(current, max);
        }

        // Wobble = the unfrozen part of the tower swaying sideways for more than a moment.
        // A fresh block landing shakes things briefly and mostly vertically, which this ignores.
        private void UpdateWobble(float elapsed)
        {
            float fastest = 0f;
            for (int i = 0; i < _placed.Count; i++)
            {
                StackBlock block = _placed[i];
                if (block.State != StackBlock.BlockState.Settled)
                {
                    continue;
                }

                Vector3 velocity = block.Body.linearVelocity;
                float sideways = velocity.x * velocity.x + velocity.z * velocity.z;
                if (sideways > fastest)
                {
                    fastest = sideways;
                }
            }

            fastest = Mathf.Sqrt(fastest);

            if (fastest > tuning.wobbleSpeed)
            {
                _wobbleTime += elapsed;
                _calmTime = 0f;
                if (!IsWobbling && _wobbleTime >= tuning.wobbleMinDuration)
                {
                    SetWobbling(true);
                }
            }
            else
            {
                _wobbleTime = 0f;
                _calmTime += elapsed;
                if (IsWobbling && _calmTime >= tuning.wobbleCalmTime)
                {
                    SetWobbling(false);
                }
            }

            if (IsWobbling)
            {
                float amount = Mathf.InverseLerp(tuning.wobbleSpeed, tuning.wobbleSpeed * 4f, fastest);
                TintTower(Mathf.Lerp(0.35f, 0.85f, amount));
            }
        }

        private void SetWobbling(bool wobbling)
        {
            if (IsWobbling == wobbling)
            {
                return;
            }

            IsWobbling = wobbling;
            if (!wobbling)
            {
                TintTower(0f);
            }

            GameEvents.RaiseWobbleChanged(wobbling);
        }

        private void TintTower(float amount)
        {
            for (int i = 0; i < _placed.Count; i++)
            {
                _placed[i].SetTint(tuning.wobbleTint, amount);
            }
        }

        // Layer = longest chain of blocks underneath. Anything more than freezeLayers below the top
        // layer stops simulating; it wakes again on a big hit.
        private void UpdateFrozen()
        {
            int count = _placed.Count;
            if (count <= tuning.freezeLayers)
            {
                return;
            }

            _placed.Sort(ByBottom);
            _levels.Clear();

            int topLevel = 0;
            for (int i = 0; i < count; i++)
            {
                StackBlock block = _placed[i];
                Vector3 position = block.Position;
                Vector3 extents = block.WorldExtents;
                float bottom = position.y - extents.y;

                int level = 1;
                for (int j = 0; j < i; j++)
                {
                    StackBlock other = _placed[j];
                    Vector3 otherPosition = other.Position;
                    Vector3 otherExtents = other.WorldExtents;

                    bool underneath = otherPosition.y + otherExtents.y <= bottom + SupportTolerance;
                    bool overlapping = Mathf.Abs(position.x - otherPosition.x) < extents.x + otherExtents.x
                                       && Mathf.Abs(position.z - otherPosition.z) < extents.z + otherExtents.z;
                    if (underneath && overlapping && _levels[j] + 1 > level)
                    {
                        level = _levels[j] + 1;
                    }
                }

                _levels.Add(level);
                if (level > topLevel)
                {
                    topLevel = level;
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (topLevel - _levels[i] > tuning.freezeLayers)
                {
                    _placed[i].Freeze();
                }
            }
        }

        // Looks straight down from the block's footprint for the pedestal or a placed block.
        // With several candidates, the one whose centre is closest is "the block below".
        private bool FindSupport(StackBlock block, out StackBlock below, out Vector3 supportCentre)
        {
            below = null;
            supportCentre = default;

            Vector3 half = block.HalfSize * 0.95f;
            int hitCount = Physics.BoxCastNonAlloc(block.Position, half, Vector3.down, _hits, block.Rotation,
                SupportTolerance + 0.03f, supportMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float best = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _hits[i].collider;
                Vector3 centre;
                StackBlock candidate = null;

                if (pedestal.Owns(hit))
                {
                    centre = pedestal.SurfaceCentre;
                }
                else if (StackBlock.TryGet(hit, out candidate) && candidate != block && candidate.IsPlaced)
                {
                    centre = candidate.Position;
                }
                else
                {
                    continue;
                }

                float distance = PlacementMath.HorizontalDistance(block.Position, centre);
                if (distance < best)
                {
                    best = distance;
                    below = candidate;
                    supportCentre = centre;
                    found = true;
                }
            }

            return found;
        }
    }
}
