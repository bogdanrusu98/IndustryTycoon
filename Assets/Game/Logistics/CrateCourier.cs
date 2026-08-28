using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    public enum CrateCourierState
    {
        Disabled,
        Wait,
        MoveToPickup,
        Pickup,
        MoveToDelivery,
        Deliver
    }

    public sealed class CrateCourier : MonoBehaviour
    {
        private const int MaximumCratesPerTrip = 2;
        private const int CashPerDeliveredCrate = 40;

        [Header("Fixed Route")]
        [SerializeField] private PackingStation packingStation;
        [SerializeField] private CashPile cashPile;
        [SerializeField] private Transform pickupPoint;
        [SerializeField] private Transform deliveryPoint;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float movementSpeed = 3.5f;
        [SerializeField, Min(1f)] private float rotationSpeed = 540f;
        [SerializeField, Min(0.02f)] private float stopDistance = 0.08f;

        [Header("Cadence")]
        [SerializeField, Min(0f)] private float pickupDelay = 0.60f;
        [SerializeField, Min(0f)] private float deliveryDelay = 0.45f;
        [SerializeField, Min(0.1f)] private float retryInterval = 0.75f;

        private WaitForSeconds _retryWait;
        private Coroutine _retryCoroutine;
        private PackingStationOutputReservation _outputReservation;
        private CrateCourierState _state = CrateCourierState.Disabled;
        private uint _activeTripGeneration;
        private uint _nextTripGeneration;
        private uint _deliveryCommittedGeneration;
        private int _reservedCrates;
        private int _carriedCrates;
        private float _actionDelayRemaining;
        private bool _isAcquiringReservation;
        private bool _isCommittingPickup;
        private bool _isCommittingDelivery;
        private bool _isResolvingCancellation;
        private bool _isAwaitingCashCapacity;
        private bool _isShuttingDown;
        private uint _lifecycleVersion;

        public event Action<CrateCourierState> StateChanged;
        public event Action<int> ReservationChanged;
        public event Action<int> CargoChanged;
        public event Action<uint, int> TripStarted;
        public event Action<uint, int> PickupCompleted;
        public event Action<uint, int, int> DeliveryCompleted;
        public event Action<uint> TripCancelled;

        public PackingStation PackingStation => packingStation;
        public CashPile CashPile => cashPile;
        public Transform PickupPoint => pickupPoint;
        public Transform DeliveryPoint => deliveryPoint;
        public ResourceType AcceptedResourceType => ResourceType.Crate;
        public CrateCourierState State => _state;
        public int Capacity => MaximumCratesPerTrip;
        public int CashPerCrate => CashPerDeliveredCrate;
        public int ReservedCrates => _reservedCrates;
        public int CarriedCrates => _carriedCrates;
        public uint ActiveTripGeneration => _activeTripGeneration;
        public float MovementSpeed => movementSpeed;
        public float RotationSpeed => rotationSpeed;
        public float StopDistance => stopDistance;
        public float PickupDelay => pickupDelay;
        public float DeliveryDelay => deliveryDelay;
        public float RetryInterval => retryInterval;
        public bool IsWaitingForCashCapacity => _isAwaitingCashCapacity;
        public bool HasActiveReservation => _outputReservation.IsValid;
        public bool IsRetryScheduled => _retryCoroutine != null;
        public int CompletedPickupCount { get; private set; }
        public int CompletedTripCount { get; private set; }
        public int CancelledTripCount { get; private set; }
        public int PreemptedCrateCount { get; private set; }
        public int TotalDeliveredCrates { get; private set; }
        public int TotalDeliveredCash { get; private set; }

        private void Awake()
        {
            RebuildRetryWait();
            AssertInvariants();
        }

        private void OnEnable()
        {
            _lifecycleVersion++;
            _isShuttingDown = false;
            if (packingStation != null)
            {
                packingStation.BufferChanged += HandlePackingBufferChanged;
                packingStation.CourierOutputReservationChanged +=
                    HandleCourierReservationChanged;
            }

            if (cashPile != null)
            {
                cashPile.StoredCashChanged += HandleCashPileChanged;
            }

            if (_carriedCrates > 0)
            {
                SetState(CrateCourierState.MoveToDelivery);
            }
            else
            {
                ClearInactiveTripState();
                SetState(CrateCourierState.Wait);
                TryBeginTrip();
            }

            AssertInvariants();
        }

        private void OnDisable()
        {
            _lifecycleVersion++;
            uint disablingLifecycle = _lifecycleVersion;
            _isShuttingDown = true;
            if (packingStation != null)
            {
                packingStation.BufferChanged -= HandlePackingBufferChanged;
                packingStation.CourierOutputReservationChanged -=
                    HandleCourierReservationChanged;
            }

            if (cashPile != null)
            {
                cashPile.StoredCashChanged -= HandleCashPileChanged;
            }

            StopRetryCoroutine();
            CancelUnpickedTrip();
            _actionDelayRemaining = 0f;
            _isAwaitingCashCapacity = false;
            if (_lifecycleVersion != disablingLifecycle)
            {
                if (isActiveAndEnabled
                    && !_isShuttingDown
                    && _state == CrateCourierState.Wait
                    && _activeTripGeneration == 0
                    && _carriedCrates == 0)
                {
                    TryBeginTrip();
                }

                AssertInvariants();
                return;
            }

            SetState(CrateCourierState.Disabled);
            AssertInvariants();
        }

        private void Update()
        {
            switch (_state)
            {
                case CrateCourierState.MoveToPickup:
                    if (MoveTowards(ResolvePickupPosition()))
                    {
                        _actionDelayRemaining = pickupDelay;
                        SetState(CrateCourierState.Pickup);
                    }

                    break;
                case CrateCourierState.Pickup:
                    if (TickActionDelay())
                    {
                        CompletePickup();
                    }

                    break;
                case CrateCourierState.MoveToDelivery:
                    if (MoveTowards(ResolveDeliveryPosition()))
                    {
                        _actionDelayRemaining = deliveryDelay;
                        SetState(CrateCourierState.Deliver);
                    }

                    break;
                case CrateCourierState.Deliver:
                    if (!_isAwaitingCashCapacity && TickActionDelay())
                    {
                        TryCommitDelivery();
                    }

                    break;
            }
        }

        public bool TryBeginTrip()
        {
            if (!Application.isPlaying
                || !isActiveAndEnabled
                || !gameObject.activeInHierarchy
                || _isShuttingDown
                || _isAcquiringReservation
                || _isCommittingPickup
                || _isCommittingDelivery
                || _isResolvingCancellation
                || _retryCoroutine != null
                || _state != CrateCourierState.Wait
                || _carriedCrates != 0
                || _activeTripGeneration != 0
                || packingStation == null
                || !packingStation.isActiveAndEnabled)
            {
                return false;
            }

            _isAcquiringReservation = true;
            PackingStationOutputReservation reservation = default;
            bool reserved;
            try
            {
                reserved = packingStation.TryReserveCourierOutput(
                    MaximumCratesPerTrip,
                    out reservation);
            }
            finally
            {
                _isAcquiringReservation = false;
            }

            int reservedCrates = reservation.ReservedCrates;
            bool canAdoptReservation = reserved
                                       && reservedCrates > 0
                                       && reservation.IsValid
                                       && isActiveAndEnabled
                                       && gameObject.activeInHierarchy
                                       && !_isShuttingDown
                                       && !_isCommittingPickup
                                       && !_isCommittingDelivery
                                       && !_isResolvingCancellation
                                       && _state == CrateCourierState.Wait
                                       && _carriedCrates == 0
                                       && _activeTripGeneration == 0;
            if (!canAdoptReservation)
            {
                if (reservation.IsValid)
                {
                    packingStation.ReleaseCourierOutputReservation(reservation);
                }

                return false;
            }

            _outputReservation = reservation;
            _reservedCrates = reservedCrates;
            _activeTripGeneration = NextTripGeneration();
            _deliveryCommittedGeneration = 0;
            _actionDelayRemaining = 0f;
            _isAwaitingCashCapacity = false;

            uint generation = _activeTripGeneration;
            _state = CrateCourierState.MoveToPickup;
            TripStarted?.Invoke(generation, reservedCrates);
            if (!IsCurrentTrip(generation))
            {
                AssertInvariants();
                return true;
            }

            StateChanged?.Invoke(CrateCourierState.MoveToPickup);
            if (!IsCurrentTrip(generation))
            {
                AssertInvariants();
                return true;
            }

            ReservationChanged?.Invoke(_reservedCrates);
            AssertInvariants();
            return true;
        }

        public bool TryCommitDelivery()
        {
            if (_isCommittingDelivery
                || _state != CrateCourierState.Deliver
                || _activeTripGeneration == 0
                || _carriedCrates <= 0
                || cashPile == null)
            {
                return false;
            }

            uint generation = _activeTripGeneration;
            _isCommittingDelivery = true;
            bool committed;
            try
            {
                committed = CrateDeliveryTransaction.TryCommit(
                    this,
                    generation,
                    cashPile);
            }
            finally
            {
                _isCommittingDelivery = false;
            }

            if (!committed)
            {
                _isAwaitingCashCapacity = true;
                return false;
            }

            _isAwaitingCashCapacity = false;
            _actionDelayRemaining = 0f;
            if (isActiveAndEnabled
                && !_isShuttingDown
                && _activeTripGeneration == 0
                && _carriedCrates == 0)
            {
                SetState(CrateCourierState.Wait);
                if (_state == CrateCourierState.Wait
                    && _activeTripGeneration == 0)
                {
                    ScheduleRetry();
                }
            }

            AssertInvariants();
            return true;
        }

        internal bool CanAcceptPickup(
            uint generation,
            PackingStationOutputReservation reservation,
            int crateCount)
        {
            return !_isShuttingDown
                   && isActiveAndEnabled
                   && _state == CrateCourierState.Pickup
                   && generation != 0
                   && generation == _activeTripGeneration
                   && reservation.Station == packingStation
                   && reservation.Token == _outputReservation.Token
                   && crateCount > 0
                   && crateCount <= MaximumCratesPerTrip
                   && _carriedCrates == 0;
        }

        internal void FinalizePickupForTransfer(
            uint generation,
            PackingStationOutputReservation reservation,
            int crateCount)
        {
            Debug.Assert(CanAcceptPickup(generation, reservation, crateCount));
            _outputReservation = default;
            _reservedCrates = 0;
            _carriedCrates = crateCount;
            CompletedPickupCount++;
            AssertInvariants();
        }

        internal void PublishPickupCommitted(uint generation, int crateCount)
        {
            ReservationChanged?.Invoke(0);
            CargoChanged?.Invoke(_carriedCrates);
            PickupCompleted?.Invoke(generation, crateCount);
        }

        internal bool TryGetPendingDelivery(
            uint generation,
            out int crateCount,
            out int cashValue)
        {
            crateCount = 0;
            cashValue = 0;
            if (_state != CrateCourierState.Deliver
                || generation == 0
                || generation != _activeTripGeneration
                || generation == _deliveryCommittedGeneration
                || _carriedCrates <= 0
                || _carriedCrates > MaximumCratesPerTrip)
            {
                return false;
            }

            crateCount = _carriedCrates;
            cashValue = crateCount * CashPerDeliveredCrate;
            return true;
        }

        internal void FinalizeDeliveryForTransfer(
            uint generation,
            int crateCount,
            int cashValue)
        {
            Debug.Assert(TryGetPendingDelivery(generation, out int pendingCrates, out int pendingCash)
                         && pendingCrates == crateCount
                         && pendingCash == cashValue);
            _deliveryCommittedGeneration = generation;
            _activeTripGeneration = 0;
            _carriedCrates = 0;
            CompletedTripCount++;
            TotalDeliveredCrates += crateCount;
            TotalDeliveredCash += cashValue;
        }

        internal void PublishDeliveryCommitted(
            uint generation,
            int crateCount,
            int cashValue)
        {
            CargoChanged?.Invoke(0);
            DeliveryCompleted?.Invoke(generation, crateCount, cashValue);
        }

        private void CompletePickup()
        {
            uint generation = _activeTripGeneration;
            _isCommittingPickup = true;
            bool committed;
            try
            {
                committed = CratePickupTransaction.TryCommit(
                    packingStation,
                    _outputReservation,
                    this,
                    generation);
            }
            finally
            {
                _isCommittingPickup = false;
            }

            if (committed)
            {
                _actionDelayRemaining = 0f;
                if (isActiveAndEnabled && !_isShuttingDown)
                {
                    SetState(CrateCourierState.MoveToDelivery);
                }

                AssertInvariants();
                return;
            }

            ResolveCancelledTrip(
                generation,
                CrateCourierState.Wait,
                true);
            AssertInvariants();
        }

        private void HandlePackingBufferChanged(
            int inputPlanks,
            int processingInputPlanks,
            int outputCrates,
            int reservedOutputCapacity)
        {
            if (_isShuttingDown
                || _isAcquiringReservation
                || _isCommittingPickup
                || _isCommittingDelivery
                || _isResolvingCancellation)
            {
                return;
            }

            SynchronizeReservationClaim();

            if (_state == CrateCourierState.Wait && _retryCoroutine == null)
            {
                TryBeginTrip();
            }
        }

        private void HandleCourierReservationChanged(int reservedCrates)
        {
            if (_isShuttingDown
                || _isAcquiringReservation
                || _isCommittingPickup
                || _isCommittingDelivery
                || _isResolvingCancellation)
            {
                return;
            }

            SynchronizeReservationClaim();
        }

        private void SynchronizeReservationClaim()
        {
            if (_activeTripGeneration != 0 && _carriedCrates == 0)
            {
                int currentReservation = _outputReservation.ReservedCrates;
                if (currentReservation != _reservedCrates)
                {
                    if (currentReservation < _reservedCrates)
                    {
                        PreemptedCrateCount += _reservedCrates - currentReservation;
                    }

                    _reservedCrates = currentReservation;
                    if (_reservedCrates == 0)
                    {
                        _outputReservation = default;
                    }

                    ReservationChanged?.Invoke(_reservedCrates);
                }
            }
        }

        private void HandleCashPileChanged(int storedCash)
        {
            if (_state != CrateCourierState.Deliver
                || !_isAwaitingCashCapacity
                || cashPile == null
                || !TryGetPendingDelivery(
                    _activeTripGeneration,
                    out _,
                    out int pendingCash)
                || !cashPile.CanDeposit(pendingCash))
            {
                return;
            }

            _isAwaitingCashCapacity = false;
            _actionDelayRemaining = 0f;
            TryCommitDelivery();
        }

        private bool MoveTowards(Vector3 destination)
        {
            Vector3 currentPosition = transform.position;
            destination.y = currentPosition.y;
            Vector3 offset = destination - currentPosition;
            if (offset.sqrMagnitude <= stopDistance * stopDistance)
            {
                transform.position = destination;
                return true;
            }

            transform.position = Vector3.MoveTowards(
                currentPosition,
                destination,
                movementSpeed * Time.deltaTime);
            if (offset.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(
                    offset.normalized,
                    Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime);
            }

            return false;
        }

        private bool TickActionDelay()
        {
            if (_actionDelayRemaining <= 0f)
            {
                return true;
            }

            _actionDelayRemaining = Mathf.Max(
                0f,
                _actionDelayRemaining - Time.deltaTime);
            return _actionDelayRemaining <= 0f;
        }

        private Vector3 ResolvePickupPosition()
        {
            return pickupPoint != null
                ? pickupPoint.position
                : packingStation != null
                    ? packingStation.transform.position
                    : transform.position;
        }

        private Vector3 ResolveDeliveryPosition()
        {
            return deliveryPoint != null
                ? deliveryPoint.position
                : cashPile != null
                    ? cashPile.transform.position
                    : transform.position;
        }

        private void ScheduleRetry()
        {
            if (_retryCoroutine != null
                || _isShuttingDown
                || !isActiveAndEnabled
                || !gameObject.activeInHierarchy)
            {
                return;
            }

            _retryCoroutine = StartCoroutine(RetryAfterCadence());
        }

        private IEnumerator RetryAfterCadence()
        {
            yield return _retryWait;
            _retryCoroutine = null;
            if (_state == CrateCourierState.Wait)
            {
                TryBeginTrip();
            }
        }

        private void StopRetryCoroutine()
        {
            if (_retryCoroutine == null)
            {
                return;
            }

            StopCoroutine(_retryCoroutine);
            _retryCoroutine = null;
        }

        private void CancelUnpickedTrip()
        {
            if (_carriedCrates > 0)
            {
                return;
            }

            uint generation = _activeTripGeneration;
            if (generation != 0)
            {
                ResolveCancelledTrip(
                    generation,
                    CrateCourierState.Disabled,
                    false);
                return;
            }

            PackingStationOutputReservation reservation = _outputReservation;
            _outputReservation = default;
            _reservedCrates = 0;
            _activeTripGeneration = 0;
            _deliveryCommittedGeneration = 0;
            bool wasResolvingCancellation = _isResolvingCancellation;
            _isResolvingCancellation = true;
            try
            {
                if (reservation.IsValid && packingStation != null)
                {
                    packingStation.ReleaseCourierOutputReservation(reservation);
                }
            }
            finally
            {
                _isResolvingCancellation = wasResolvingCancellation;
            }
        }

        private void ResolveCancelledTrip(
            uint generation,
            CrateCourierState settledState,
            bool scheduleRetry)
        {
            if (generation == 0 || generation != _activeTripGeneration)
            {
                return;
            }

            PackingStationOutputReservation reservation = _outputReservation;
            _outputReservation = default;
            _reservedCrates = 0;
            _activeTripGeneration = 0;
            _deliveryCommittedGeneration = 0;
            _actionDelayRemaining = 0f;
            _isAwaitingCashCapacity = false;
            CancelledTripCount++;

            bool wasResolvingCancellation = _isResolvingCancellation;
            _isResolvingCancellation = true;
            try
            {
                SetState(settledState);
                if (reservation.IsValid && packingStation != null)
                {
                    packingStation.ReleaseCourierOutputReservation(reservation);
                }

                ReservationChanged?.Invoke(0);
                TripCancelled?.Invoke(generation);
            }
            finally
            {
                _isResolvingCancellation = wasResolvingCancellation;
            }

            if (scheduleRetry
                && isActiveAndEnabled
                && !_isShuttingDown
                && _state == CrateCourierState.Wait
                && _activeTripGeneration == 0
                && _carriedCrates == 0)
            {
                ScheduleRetry();
            }
        }

        private void ClearInactiveTripState()
        {
            _outputReservation = default;
            _reservedCrates = 0;
            _activeTripGeneration = 0;
            _deliveryCommittedGeneration = 0;
            _isAwaitingCashCapacity = false;
        }

        public void RestoreIdleState()
        {
            StopRetryCoroutine();
            PackingStationOutputReservation reservation = _outputReservation;
            _outputReservation = default;
            _reservedCrates = 0;
            _carriedCrates = 0;
            _activeTripGeneration = 0;
            _deliveryCommittedGeneration = 0;
            _actionDelayRemaining = 0f;
            _isAwaitingCashCapacity = false;
            _isAcquiringReservation = false;
            _isCommittingPickup = false;
            _isCommittingDelivery = false;
            _isResolvingCancellation = false;
            CompletedPickupCount = 0;
            CompletedTripCount = 0;
            CancelledTripCount = 0;
            PreemptedCrateCount = 0;
            TotalDeliveredCrates = 0;
            TotalDeliveredCash = 0;

            if (reservation.IsValid && packingStation != null)
            {
                packingStation.ReleaseCourierOutputReservation(reservation);
            }

            SetState(isActiveAndEnabled
                ? CrateCourierState.Wait
                : CrateCourierState.Disabled);
            ReservationChanged?.Invoke(0);
            CargoChanged?.Invoke(0);
            AssertInvariants();
        }

        private uint NextTripGeneration()
        {
            _nextTripGeneration++;
            if (_nextTripGeneration == 0)
            {
                _nextTripGeneration = 1;
            }

            return _nextTripGeneration;
        }

        private bool IsCurrentTrip(uint generation)
        {
            return generation != 0 && _activeTripGeneration == generation;
        }

        private void SetState(CrateCourierState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke(_state);
        }

        private void AssertInvariants()
        {
            Debug.Assert(
                _reservedCrates >= 0 && _reservedCrates <= MaximumCratesPerTrip,
                "Courier reservation exceeded its two-Crate capacity.");
            Debug.Assert(
                _carriedCrates >= 0 && _carriedCrates <= MaximumCratesPerTrip,
                "Courier cargo exceeded its two-Crate capacity.");
            Debug.Assert(
                _carriedCrates == 0 || _reservedCrates == 0,
                "Courier cannot own reserved and picked-up Crates simultaneously.");
            Debug.Assert(
                _reservedCrates == 0 || _activeTripGeneration != 0,
                "Courier reservation has no active trip generation.");
            Debug.Assert(
                _carriedCrates == 0 || _activeTripGeneration != 0,
                "Courier cargo has no active trip generation.");
        }

        private void RebuildRetryWait()
        {
            _retryWait = new WaitForSeconds(retryInterval);
        }

        private void OnValidate()
        {
            movementSpeed = Mathf.Max(0.1f, movementSpeed);
            rotationSpeed = Mathf.Max(1f, rotationSpeed);
            stopDistance = Mathf.Max(0.02f, stopDistance);
            pickupDelay = Mathf.Max(0f, pickupDelay);
            deliveryDelay = Mathf.Max(0f, deliveryDelay);
            retryInterval = Mathf.Max(0.1f, retryInterval);
        }
    }
}
