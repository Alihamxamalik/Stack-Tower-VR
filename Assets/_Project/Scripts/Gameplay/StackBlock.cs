using System;
using System.Collections.Generic;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// A stackable block: wraps the Rigidbody and owns its physics state.
    /// Kinematic while waiting on the shelf, while held (the hand drives it, so it never jitters
    /// through the tower) and once frozen deep in the tower; dynamic otherwise.
    /// It knows nothing about scoring, UI or the XR SDK. Whatever does the grabbing calls
    /// <see cref="BeginHold"/> and <see cref="EndHold"/>; the tower listens to the events.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class StackBlock : MonoBehaviour
    {
        public enum BlockState
        {
            /// <summary>Inactive in the pool.</summary>
            Pooled,
            /// <summary>Hovering on the spawn shelf, never grabbed yet.</summary>
            Spawned,
            Held,
            /// <summary>Released and moving (or resting somewhere that isn't the tower).</summary>
            Free,
            /// <summary>Counted as part of the tower.</summary>
            Settled,
            /// <summary>Settled and made kinematic because it is deep in the tower.</summary>
            Frozen
        }

        private const int SampleCount = 6;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Dictionary<Collider, StackBlock> ByCollider = new Dictionary<Collider, StackBlock>(128);

        [SerializeField] private BlockDefinition definition;
        [SerializeField] private GameTuning tuning;
        [Tooltip("Child that holds the mesh. Rotation snapping turns this instead of the physics body while held.")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Renderer[] renderers;

        private readonly Vector3[] _samplePositions = new Vector3[SampleCount];
        private readonly Quaternion[] _sampleRotations = new Quaternion[SampleCount];
        private readonly float[] _sampleTimes = new float[SampleCount];
        private int _sampleHead;
        private int _sampleFill;

        private Rigidbody _body;
        private Collider[] _colliders;
        private MaterialPropertyBlock _props;
        private Color _baseColor = Color.white;
        private float _baseAngularDamping;
        private Vector3 _visualBaseScale = Vector3.one;

        private float _fallY = float.NegativeInfinity;
        private float _restTimer;
        private float _dampingTimer;
        private float _trackingHoldTimer;
        private bool _released;
        private bool _impactPending;
        private bool _fell;
        private bool _grabbable;
        private bool _hovered;
        private Color _tint;
        private float _tintAmount;

        private bool _gliding;
        private float _glideTime;
        private float _glideDuration;
        private Vector3 _glideFromPosition;
        private Vector3 _glideToPosition;
        private Quaternion _glideFromRotation;
        private Quaternion _glideToRotation;

        public event Action<StackBlock> OnGrabbed;
        public event Action<StackBlock> OnReleased;
        /// <summary>
        /// The block came to rest after being released. Whoever tracks the tower decides whether that
        /// counts and answers with <see cref="ConfirmSettled"/>.
        /// </summary>
        public event Action<StackBlock> OnSettled;
        public event Action<StackBlock> OnFell;
        /// <summary>First contact after a release, with the impact speed in m/s.</summary>
        public event Action<StackBlock, float> OnImpact;
        /// <summary>A placed block was hit hard enough to shake the tower.</summary>
        public event Action<StackBlock> OnBigHit;
        public event Action<StackBlock, bool> GrabbableChanged;

        public BlockDefinition Definition => definition;
        public BlockState State { get; private set; } = BlockState.Pooled;
        public Rigidbody Body => _body;
        public Transform VisualRoot => visualRoot;
        public bool IsHeld => State == BlockState.Held;
        public bool IsPlaced => State == BlockState.Settled || State == BlockState.Frozen;
        public bool IsGrabbable => _grabbable;
        /// <summary>Held, but the hand vanished and the block is waiting in place for it to return.</summary>
        public bool IsWaitingForTracking => State == BlockState.Held && _trackingHoldTimer > 0f;

        /// <summary>How long a block waits in place for a lost hand before it is dropped (s).</summary>
        public float TrackingLostHoldTime => tuning != null ? tuning.trackingLostHoldTime : 0.5f;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        /// <summary>Rotation the player sees: the snapped one while held, otherwise the body's.</summary>
        public Quaternion VisualRotation => visualRoot.rotation;
        public Vector3 HalfSize => definition != null ? definition.HalfSize : Vector3.one * 0.09f;
        public Vector3 WorldExtents => PlacementMath.WorldExtents(transform.rotation, HalfSize);
        public float TopY => transform.position.y + WorldExtents.y;
        public float BottomY => transform.position.y - WorldExtents.y;

        /// <summary>Speed of the hand moving this block, from recent frames (m/s). Zero unless held.</summary>
        public float HeldSpeed
        {
            get
            {
                if (State != BlockState.Held)
                {
                    return 0f;
                }

                EstimateVelocity(out Vector3 linear, out _);
                return linear.magnitude;
            }
        }

        public static bool TryGet(Collider collider, out StackBlock block)
        {
            if (collider == null)
            {
                block = null;
                return false;
            }

            return ByCollider.TryGetValue(collider, out block);
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < _colliders.Length; i++)
            {
                ByCollider[_colliders[i]] = this;
            }

            _props = new MaterialPropertyBlock();
            _baseAngularDamping = _body.angularDamping;

            if (visualRoot == null)
            {
                visualRoot = transform;
            }

            _visualBaseScale = visualRoot.localScale;

            if (definition != null)
            {
                _body.mass = definition.mass;
                if (definition.material != null && definition.material.HasProperty(BaseColorId))
                {
                    _baseColor = definition.material.GetColor(BaseColorId);
                }
            }

            if (tuning != null)
            {
                _body.maxDepenetrationVelocity = tuning.maxDepenetrationSpeed;
                _body.solverIterations = tuning.solverIterations;
                _body.solverVelocityIterations = tuning.solverVelocityIterations;
            }
        }

        private void OnDestroy()
        {
            if (_colliders == null)
            {
                return;
            }

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                {
                    ByCollider.Remove(_colliders[i]);
                }
            }
        }

        /// <summary>Takes the block out of the pool and parks it, kinematic, where it can be grabbed.</summary>
        /// <param name="fallY">World height below which the block counts as fallen.</param>
        public void Spawn(Vector3 position, Quaternion rotation, float fallY)
        {
            gameObject.SetActive(true);

            _fallY = fallY;
            _released = false;
            _impactPending = false;
            _fell = false;
            _gliding = false;
            _restTimer = 0f;
            _dampingTimer = 0f;
            _trackingHoldTimer = 0f;
            _sampleFill = 0;
            _hovered = false;
            _tintAmount = 0f;
            ApplyColor();

            ResetVisual();
            _body.angularDamping = _baseAngularDamping;
            SetKinematic(true);
            _body.interpolation = RigidbodyInterpolation.None;
            transform.SetPositionAndRotation(position, rotation);

            State = BlockState.Spawned;
            SetGrabbable(true);
        }

        /// <summary>Returns the block to the pool.</summary>
        public void Despawn()
        {
            // State first: turning grabbing off may force a release, which must not run the release logic.
            State = BlockState.Pooled;
            SetGrabbable(false);

            _gliding = false;
            _trackingHoldTimer = 0f;
            SetKinematic(true);
            gameObject.SetActive(false);
        }

        public void BeginHold()
        {
            if (State == BlockState.Pooled || IsPlaced)
            {
                return;
            }

            _trackingHoldTimer = 0f;
            _gliding = false;

            if (State == BlockState.Held)
            {
                // Tracking came back during the lost-tracking hold: carry on as if nothing happened.
                return;
            }

            State = BlockState.Held;
            SetKinematic(true);
            _body.interpolation = RigidbodyInterpolation.None;
            _sampleFill = 0;
            ResetVisual();
            OnGrabbed?.Invoke(this);
        }

        public void EndHold(HoldEndReason reason = HoldEndReason.Released)
        {
            if (State != BlockState.Held)
            {
                return;
            }

            if (reason == HoldEndReason.TrackingLost && tuning != null && tuning.trackingLostHoldTime > 0f)
            {
                // Lost-tracking safety: stay frozen where we are and give the hand a moment to return.
                // Forget the motion so far; the last frames before a tracking loss are the least reliable.
                _trackingHoldTimer = tuning.trackingLostHoldTime;
                _sampleFill = 0;
                return;
            }

            EstimateVelocity(out Vector3 linear, out Vector3 angular);
            if (tuning != null)
            {
                linear = Vector3.ClampMagnitude(linear * tuning.releaseVelocityScale, tuning.maxReleaseSpeed);
                angular = Vector3.ClampMagnitude(angular * tuning.releaseVelocityScale,
                    tuning.MaxReleaseAngularSpeedRad);
            }

            Release(linear, angular);
        }

        /// <summary>Lets go with no velocity at all, skipping the lost-tracking wait.</summary>
        public void Drop()
        {
            if (State == BlockState.Held)
            {
                Release(Vector3.zero, Vector3.zero);
            }
        }

        /// <summary>The tower accepted this block: it is now part of it and can't be picked up again.</summary>
        public void ConfirmSettled()
        {
            if (State != BlockState.Free)
            {
                return;
            }

            State = BlockState.Settled;
            SetGrabbable(false);
        }

        public void Freeze()
        {
            if (State != BlockState.Settled)
            {
                return;
            }

            State = BlockState.Frozen;
            SetKinematic(true);
        }

        public void Unfreeze()
        {
            if (State != BlockState.Frozen)
            {
                return;
            }

            State = BlockState.Settled;
            SetKinematic(false);
            _body.WakeUp();
        }

        /// <summary>
        /// Soft alignment: moves the just-released block to a pose over a short time, then hands it back
        /// to physics at rest. Ignored unless the block is free.
        /// </summary>
        public void BeginGlide(Vector3 position, Quaternion rotation, float duration)
        {
            if (State != BlockState.Free)
            {
                return;
            }

            _gliding = true;
            _glideTime = 0f;
            _glideDuration = duration;
            _glideFromPosition = transform.position;
            _glideFromRotation = transform.rotation;
            _glideToPosition = position;
            _glideToRotation = rotation;
            SetKinematic(true);
        }

        public void SetGrabbable(bool grabbable)
        {
            if (_grabbable == grabbable)
            {
                return;
            }

            _grabbable = grabbable;
            GrabbableChanged?.Invoke(this, grabbable);
        }

        /// <summary>Turns only the visible mesh (rotation snapping) and leaves the physics body alone.</summary>
        public void SetVisualRotation(Quaternion worldRotation)
        {
            if (visualRoot != transform)
            {
                visualRoot.rotation = worldRotation;
            }
        }

        public void SetVisualScale(float factor)
        {
            if (visualRoot != transform)
            {
                visualRoot.localScale = _visualBaseScale * factor;
            }
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            ApplyColor();
        }

        public void SetTint(Color tint, float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Approximately(amount, _tintAmount) && tint == _tint)
            {
                return;
            }

            _tint = tint;
            _tintAmount = amount;
            ApplyColor();
        }

        private void Update()
        {
            if (_trackingHoldTimer <= 0f)
            {
                return;
            }

            _trackingHoldTimer -= Time.deltaTime;
            if (_trackingHoldTimer <= 0f && State == BlockState.Held)
            {
                // The hand never came back: let go gently, straight down.
                Release(Vector3.zero, Vector3.zero);
            }
        }

        private void LateUpdate()
        {
            if (State != BlockState.Held || _trackingHoldTimer > 0f)
            {
                return;
            }

            // Runs after the interaction system has moved us this frame.
            _samplePositions[_sampleHead] = transform.position;
            _sampleRotations[_sampleHead] = transform.rotation;
            _sampleTimes[_sampleHead] = Time.time;
            _sampleHead = (_sampleHead + 1) % SampleCount;
            if (_sampleFill < SampleCount)
            {
                _sampleFill++;
            }
        }

        private void FixedUpdate()
        {
            if (State == BlockState.Free)
            {
                if (_gliding)
                {
                    TickGlide();
                    return;
                }

                TickSettleDamping();
                if (!CheckFell() && _released)
                {
                    TickRest();
                }
            }
            else if (State == BlockState.Settled)
            {
                TickSettleDamping();
                CheckFell();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;

            // A held block is moved by its transform, so physics sees no velocity. Use the hand's.
            Collider hit = collision.collider;
            if (hit != null && ByCollider.TryGetValue(hit, out StackBlock other) && other.State == BlockState.Held)
            {
                speed = Mathf.Max(speed, other.HeldSpeed);
            }

            if (_impactPending && State == BlockState.Free)
            {
                _impactPending = false;
                OnImpact?.Invoke(this, speed);
            }

            if (IsPlaced && tuning != null && speed > tuning.bigHitSpeed)
            {
                OnBigHit?.Invoke(this);
            }
        }

        private void Release(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            CommitVisualRotation();

            State = BlockState.Free;
            _trackingHoldTimer = 0f;
            _released = true;
            _impactPending = true;
            _restTimer = 0f;

            SetKinematic(false);
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.linearVelocity = linearVelocity;
            _body.angularVelocity = angularVelocity;
            StartSettleDamping();

            OnReleased?.Invoke(this);
        }

        private void TickGlide()
        {
            _glideTime += Time.fixedDeltaTime;
            float t = _glideDuration <= 0f ? 1f : Mathf.Clamp01(_glideTime / _glideDuration);

            if (t < 1f)
            {
                float eased = t * t * (3f - 2f * t);
                _body.MovePosition(Vector3.Lerp(_glideFromPosition, _glideToPosition, eased));
                _body.MoveRotation(Quaternion.Slerp(_glideFromRotation, _glideToRotation, eased));
                return;
            }

            _gliding = false;
            _body.position = _glideToPosition;
            _body.rotation = _glideToRotation;
            SetKinematic(false);
            _restTimer = 0f;
            StartSettleDamping();
        }

        private void StartSettleDamping()
        {
            if (tuning == null || tuning.settleDampingDuration <= 0f)
            {
                return;
            }

            _dampingTimer = tuning.settleDampingDuration;
            _body.angularDamping = tuning.settleAngularDamping;
        }

        private void TickSettleDamping()
        {
            if (_dampingTimer <= 0f)
            {
                return;
            }

            _dampingTimer -= Time.fixedDeltaTime;
            if (_dampingTimer <= 0f)
            {
                _body.angularDamping = _baseAngularDamping;
            }
        }

        private void TickRest()
        {
            if (tuning == null)
            {
                return;
            }

            float maxAngular = tuning.SettleAngularSpeedRad;
            bool resting = _body.linearVelocity.sqrMagnitude < tuning.settleSpeed * tuning.settleSpeed
                           && _body.angularVelocity.sqrMagnitude < maxAngular * maxAngular;
            if (!resting)
            {
                _restTimer = 0f;
                return;
            }

            _restTimer += Time.fixedDeltaTime;
            if (_restTimer >= tuning.settleTime)
            {
                _restTimer = 0f;
                OnSettled?.Invoke(this);
            }
        }

        private bool CheckFell()
        {
            if (_fell || transform.position.y >= _fallY)
            {
                return false;
            }

            _fell = true;
            OnFell?.Invoke(this);
            return true;
        }

        private void EstimateVelocity(out Vector3 linear, out Vector3 angular)
        {
            linear = Vector3.zero;
            angular = Vector3.zero;
            if (_sampleFill < 2)
            {
                return;
            }

            int newest = (_sampleHead - 1 + SampleCount) % SampleCount;
            int oldest = (_sampleHead - _sampleFill + SampleCount) % SampleCount;
            float dt = _sampleTimes[newest] - _sampleTimes[oldest];
            if (dt <= 1e-4f)
            {
                return;
            }

            linear = (_samplePositions[newest] - _samplePositions[oldest]) / dt;

            Quaternion delta = _sampleRotations[newest] * Quaternion.Inverse(_sampleRotations[oldest]);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }

            if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
            {
                angular = axis * (angle * Mathf.Deg2Rad / dt);
            }
        }

        private void SetKinematic(bool kinematic)
        {
            if (_body.isKinematic == kinematic)
            {
                return;
            }

            if (kinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            _body.isKinematic = kinematic;
        }

        private void ResetVisual()
        {
            if (visualRoot == transform)
            {
                return;
            }

            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = _visualBaseScale;
        }

        // The body takes over whatever rotation the player was looking at.
        private void CommitVisualRotation()
        {
            if (visualRoot == transform)
            {
                return;
            }

            Quaternion shown = visualRoot.rotation;
            transform.rotation = shown;
            visualRoot.localRotation = Quaternion.identity;
        }

        private void ApplyColor()
        {
            if (renderers == null)
            {
                return;
            }

            bool plain = _tintAmount <= 0.001f && !_hovered;
            if (!plain)
            {
                Color color = _hovered ? Color.Lerp(_baseColor, Color.white, 0.35f) : _baseColor;
                color = Color.Lerp(color, _tint, _tintAmount);
                color.a = _baseColor.a;
                _props.SetColor(BaseColorId, color);
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    // Clearing the block lets untinted blocks batch together again.
                    renderers[i].SetPropertyBlock(plain ? null : _props);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ByCollider.Clear();
        }
    }
}
