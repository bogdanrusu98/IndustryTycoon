using System;
using IndustryTycoon.Core;
using IndustryTycoon.ResourceSystem;
using UnityEngine;

namespace IndustryTycoon.Workers
{
    public enum LumberWorkerState
    {
        Idle,
        MoveToWood,
        MoveToStockpile
    }

    public sealed class LumberWorker : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WoodSpawner woodSpawner;
        [SerializeField] private WoodStockpile stockpile;
        [SerializeField] private Transform depositPoint;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 3.5f;
        [SerializeField, Min(1f)] private float rotationSpeed = 540f;
        [SerializeField, Min(0.05f)] private float stopDistance = 0.35f;

        [Header("Cadence")]
        [SerializeField, Min(0.05f)] private float searchInterval = 0.35f;
        [SerializeField, Min(0f)] private float pickupDelay = 0.12f;
        [SerializeField, Min(0f)] private float depositDelay = 0.15f;

        private ResourceClaimHandle _targetClaim;
        private WoodStockpileReservation _stockpileReservation;
        private LumberWorkerState _state;
        private float _searchCooldown;
        private float _actionDelayRemaining;
        private bool _pickupPauseStarted;
        private bool _depositPauseStarted;
        private bool _isCarrying;

        public event Action<LumberWorkerState> StateChanged;
        public event Action<bool> CargoChanged;
        public event Action WoodPickedUp;
        public event Action WoodDeposited;
        public event Action TargetClaimLost;

        public WoodSpawner WoodSpawner => woodSpawner;
        public WoodStockpile Stockpile => stockpile;
        public Transform DepositPoint => depositPoint;
        public LumberWorkerState State => _state;
        public ResourcePickup CurrentTarget => _targetClaim.Pickup;
        public bool HasValidTarget => _targetClaim.IsValid;
        public bool HasIncomingReservation => _stockpileReservation.IsValid;
        public bool IsCarrying => _isCarrying;
        public bool IsWaitingForStockpile => _isCarrying
            ? !_stockpileReservation.IsValid
            : _state == LumberWorkerState.Idle
              && (stockpile == null || !stockpile.isActiveAndEnabled || stockpile.IsFull);
        public float MoveSpeed => moveSpeed;
        public float RotationSpeed => rotationSpeed;
        public float StopDistance => stopDistance;
        public float SearchInterval => searchInterval;
        public float PickupDelay => pickupDelay;
        public float DepositDelay => depositDelay;
        public int CompletedPickupCount { get; private set; }
        public int CompletedDepositCount { get; private set; }
        public int RecoveryCount { get; private set; }

        private void OnEnable()
        {
            if (stockpile != null)
            {
                stockpile.StateChanged += HandleStockpileChanged;
            }

            _searchCooldown = 0f;
            _actionDelayRemaining = 0f;
            _pickupPauseStarted = false;
            _depositPauseStarted = false;
            SetState(_isCarrying
                ? LumberWorkerState.MoveToStockpile
                : LumberWorkerState.Idle);
        }

        private void OnDisable()
        {
            if (stockpile != null)
            {
                stockpile.StateChanged -= HandleStockpileChanged;
            }

            if (_targetClaim.IsValid)
            {
                _targetClaim.Pickup.TryReleaseClaim(_targetClaim);
            }

            _targetClaim = default;

            if (_stockpileReservation.IsValid && stockpile != null)
            {
                stockpile.ReleaseIncoming(_stockpileReservation);
            }

            _stockpileReservation = default;
            _state = LumberWorkerState.Idle;
            _actionDelayRemaining = 0f;
            _pickupPauseStarted = false;
            _depositPauseStarted = false;
        }

        private void Update()
        {
            if (woodSpawner == null || stockpile == null)
            {
                return;
            }

            if (_actionDelayRemaining > 0f)
            {
                _actionDelayRemaining = Mathf.Max(
                    0f,
                    _actionDelayRemaining - Time.deltaTime);
                if (_actionDelayRemaining > 0f)
                {
                    return;
                }
            }

            switch (_state)
            {
                case LumberWorkerState.Idle:
                    UpdateIdle();
                    break;
                case LumberWorkerState.MoveToWood:
                    UpdateMoveToWood();
                    break;
                case LumberWorkerState.MoveToStockpile:
                    UpdateMoveToStockpile();
                    break;
            }
        }

        private void UpdateIdle()
        {
            if (_isCarrying)
            {
                SetState(LumberWorkerState.MoveToStockpile);
                return;
            }

            _searchCooldown -= Time.deltaTime;
            if (_searchCooldown > 0f)
            {
                return;
            }

            SearchForWork();
            if (_state == LumberWorkerState.Idle)
            {
                _searchCooldown = searchInterval;
            }
        }

        private void SearchForWork()
        {
            if (!stockpile.TryReserveIncoming(out WoodStockpileReservation reservation))
            {
                return;
            }

            if (!woodSpawner.TryClaimNearestAvailable(transform.position, this, out ResourceClaimHandle claim))
            {
                stockpile.ReleaseIncoming(reservation);
                return;
            }

            _stockpileReservation = reservation;
            _targetClaim = claim;
            _pickupPauseStarted = false;
            SetState(LumberWorkerState.MoveToWood);
        }

        private void UpdateMoveToWood()
        {
            if (!_targetClaim.IsValid || !_stockpileReservation.IsValid)
            {
                RecoverFromClaimLoss();
                return;
            }

            Transform target = _targetClaim.Pickup.transform;
            if (!MoveTowards(target.position))
            {
                _pickupPauseStarted = false;
                return;
            }

            if (!_pickupPauseStarted)
            {
                _pickupPauseStarted = true;
                _actionDelayRemaining = pickupDelay;
                return;
            }

            if (!_targetClaim.Pickup.TryConsumeClaim(
                    _targetClaim,
                    out ResourceType consumedType,
                    out int consumedAmount)
                || consumedType != ResourceType.Wood
                || consumedAmount != 1)
            {
                RecoverFromClaimLoss();
                return;
            }

            _targetClaim = default;
            _pickupPauseStarted = false;
            _isCarrying = true;
            CompletedPickupCount++;
            CargoChanged?.Invoke(true);
            WoodPickedUp?.Invoke();
            _depositPauseStarted = false;
            SetState(LumberWorkerState.MoveToStockpile);
        }

        private void UpdateMoveToStockpile()
        {
            if (!_isCarrying)
            {
                ReleaseIncomingReservation();
                SetState(LumberWorkerState.Idle);
                _searchCooldown = searchInterval;
                return;
            }

            if (!_stockpileReservation.IsValid)
            {
                _stockpileReservation = default;
                if (!stockpile.TryReserveIncoming(out _stockpileReservation))
                {
                    _actionDelayRemaining = searchInterval;
                    return;
                }
            }

            Vector3 destination = depositPoint != null
                ? depositPoint.position
                : stockpile.transform.position;
            if (!MoveTowards(destination))
            {
                _depositPauseStarted = false;
                return;
            }

            if (!_depositPauseStarted)
            {
                _depositPauseStarted = true;
                _actionDelayRemaining = depositDelay;
                return;
            }

            if (!stockpile.TryDepositReserved(_stockpileReservation))
            {
                if (!_stockpileReservation.IsValid)
                {
                    _stockpileReservation = default;
                }

                _depositPauseStarted = false;
                _actionDelayRemaining = searchInterval;
                return;
            }

            _stockpileReservation = default;
            _depositPauseStarted = false;
            _isCarrying = false;
            CompletedDepositCount++;
            CargoChanged?.Invoke(false);
            WoodDeposited?.Invoke();
            SetState(LumberWorkerState.Idle);
            _searchCooldown = searchInterval;
        }

        private bool MoveTowards(Vector3 destination)
        {
            Vector3 currentPosition = transform.position;
            destination.y = currentPosition.y;
            Vector3 offset = destination - currentPosition;
            float stopDistanceSquared = stopDistance * stopDistance;
            if (offset.sqrMagnitude <= stopDistanceSquared)
            {
                return true;
            }

            transform.position = Vector3.MoveTowards(
                currentPosition,
                destination,
                moveSpeed * Time.deltaTime);
            if (offset.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime);
            }

            return false;
        }

        private void RecoverFromClaimLoss()
        {
            if (_targetClaim.IsValid)
            {
                _targetClaim.Pickup.TryReleaseClaim(_targetClaim);
            }

            _targetClaim = default;
            ReleaseIncomingReservation();
            _pickupPauseStarted = false;
            _actionDelayRemaining = 0f;
            RecoveryCount++;
            TargetClaimLost?.Invoke();
            SetState(LumberWorkerState.Idle);
            _searchCooldown = searchInterval;
        }

        private void ReleaseIncomingReservation()
        {
            if (_stockpileReservation.IsValid && stockpile != null)
            {
                stockpile.ReleaseIncoming(_stockpileReservation);
            }

            _stockpileReservation = default;
        }

        private void HandleStockpileChanged(int storedWood, int incomingReservations)
        {
            if (_state == LumberWorkerState.Idle)
            {
                _searchCooldown = 0f;
            }
            else if (_isCarrying && !_stockpileReservation.IsValid)
            {
                _actionDelayRemaining = 0f;
            }
        }

        private void SetState(LumberWorkerState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke(_state);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.65f);
            Gizmos.DrawWireSphere(transform.position, stopDistance);
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            rotationSpeed = Mathf.Max(1f, rotationSpeed);
            stopDistance = Mathf.Max(0.05f, stopDistance);
            searchInterval = Mathf.Max(0.05f, searchInterval);
            pickupDelay = Mathf.Max(0f, pickupDelay);
            depositDelay = Mathf.Max(0f, depositDelay);
        }
    }
}
