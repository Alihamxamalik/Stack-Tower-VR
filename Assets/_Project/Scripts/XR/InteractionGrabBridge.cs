using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;

namespace StackTower.XR
{
    /// <summary>
    /// Connects a block to the Meta Interaction SDK. The SDK moves the block while it is grabbed
    /// (pinch, palm, two hands or controller); this turns its pointer events into
    /// <see cref="StackBlock.BeginHold"/> / <see cref="StackBlock.EndHold"/> so the block, not the
    /// SDK, decides its physics. The Grabbable's own "kinematic while selected" and "throw when
    /// unselected" options are switched off for the same reason.
    /// </summary>
    [RequireComponent(typeof(StackBlock))]
    public sealed class InteractionGrabBridge : MonoBehaviour
    {
        [SerializeField] private StackBlock block;
        [SerializeField] private Grabbable grabbable;
        [Tooltip("Object holding the HandGrabInteractable and GrabInteractable. Switched off when the block can't be picked up.")]
        [SerializeField] private GameObject interactables;

        // Who is holding us, by interactor identifier. Null hand = a controller without a tracked hand.
        private readonly Dictionary<int, IHand> _holders = new Dictionary<int, IHand>(2);
        private bool _holding;
        private bool _dropGently;
        private float _lostTime;

        private void Reset()
        {
            block = GetComponent<StackBlock>();
            grabbable = GetComponent<Grabbable>();
        }

        private void OnEnable()
        {
            block.GrabbableChanged += HandleGrabbableChanged;
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
            Apply(block.IsGrabbable);
        }

        private void OnDisable()
        {
            block.GrabbableChanged -= HandleGrabbableChanged;
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            _holders.Clear();
            _holding = false;
        }

        private void Update()
        {
            if (!_holding)
            {
                return;
            }

            // Lost-tracking safety. If every hand holding the block has lost tracking, the SDK simply
            // stops moving it, which already holds it frozen in place. Give the hand a moment to come
            // back; if it doesn't, let go gently instead of leaving the block hanging in the air.
            if (AnyHolderTracked())
            {
                _lostTime = 0f;
                return;
            }

            _lostTime += Time.deltaTime;
            if (_lostTime >= block.TrackingLostHoldTime)
            {
                _dropGently = true;
                CancelGrabs();
                _dropGently = false;
            }
        }

        private void HandleGrabbableChanged(StackBlock changed, bool canGrab)
        {
            Apply(canGrab);
        }

        private void Apply(bool canGrab)
        {
            if (canGrab)
            {
                grabbable.enabled = true;
                if (interactables != null)
                {
                    interactables.SetActive(true);
                }

                return;
            }

            // Disabling the Grabbable cancels every pointer on it, which makes the interactors let go.
            grabbable.enabled = false;
            if (interactables != null)
            {
                interactables.SetActive(false);
            }

            block.SetHovered(false);
        }

        private void CancelGrabs()
        {
            grabbable.enabled = false;
            grabbable.enabled = true;
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    _holders[evt.Identifier] = FindHand(evt.Data);
                    XRHaptics.NoteGrab(evt.Data);
                    if (!_holding)
                    {
                        _holding = true;
                        _lostTime = 0f;
                        block.SetHovered(false);
                        block.BeginHold();
                    }

                    break;

                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    bool lostTracking = _holders.TryGetValue(evt.Identifier, out IHand hand) && !IsTracked(hand);
                    _holders.Remove(evt.Identifier);

                    if (_holding && grabbable.SelectingPointsCount == 0)
                    {
                        _holding = false;
                        _holders.Clear();

                        if (_dropGently)
                        {
                            block.Drop();
                        }
                        else
                        {
                            // The SDK let go because the hand vanished: the block waits in place for it.
                            block.EndHold(lostTracking ? HoldEndReason.TrackingLost : HoldEndReason.Released);
                        }
                    }

                    UpdateHover();
                    break;

                case PointerEventType.Hover:
                case PointerEventType.Unhover:
                    UpdateHover();
                    break;
            }
        }

        // Grab hover: brighten the block while a hand is in grab range.
        private void UpdateHover()
        {
            block.SetHovered(!_holding && block.IsGrabbable && grabbable.PointsCount > 0);
        }

        private bool AnyHolderTracked()
        {
            foreach (KeyValuePair<int, IHand> holder in _holders)
            {
                if (IsTracked(holder.Value))
                {
                    return true;
                }
            }

            return _holders.Count == 0;
        }

        private static bool IsTracked(IHand hand)
        {
            // No hand means a plain controller grab, which doesn't drop out the way hand tracking does.
            return hand == null || (hand.IsConnected && hand.IsTrackedDataValid);
        }

        // Only hand-grab interactors can lose a hand. A plain controller grab reports no hand at all, and
        // must not be mistaken for one that has stopped tracking.
        private static IHand FindHand(object interactor)
        {
            switch (interactor)
            {
                case HandGrabInteractor handGrab:
                    return handGrab.Hand;
                case DistanceHandGrabInteractor distanceGrab:
                    return distanceGrab.Hand;
                default:
                    return null;
            }
        }
    }
}
