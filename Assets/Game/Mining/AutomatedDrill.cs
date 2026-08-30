using System;
using System.Collections;
using UnityEngine;

namespace IndustryTycoon.Mining
{
    public enum AutomatedDrillState
    {
        Disabled,
        Idle,
        Producing,
        StorageFull,
        MissingStorage
    }

    public sealed class AutomatedDrill : MonoBehaviour
    {
        [SerializeField] private OreStorage storage;
        [SerializeField] private ParticleSystem productionParticles;
        [SerializeField, Min(0.1f)] private float cycleDuration = 1.8f;

        private WaitForSeconds _cycleWait;
        private Coroutine _productionCoroutine;
        private OreStorageReservation _storageReservation;
        private AutomatedDrillState _state = AutomatedDrillState.Disabled;
        private bool _isAcquiringReservation;
        private bool _isCompletingCycle;
        private bool _isShuttingDown;

        public event Action<AutomatedDrillState> StateChanged;
        public event Action<int> OreProduced;

        public OreStorage Storage => storage;
        public float CycleDuration => cycleDuration;
        public AutomatedDrillState State => _state;
        public bool IsProducing => _productionCoroutine != null
                                   && _storageReservation.IsValid;
        public bool IsPausedForFullStorage => isActiveAndEnabled
                                              && storage != null
                                              && storage.IsFull
                                              && !IsProducing;
        public bool HasStorageReservation => _storageReservation.IsValid;
        public int CompletedCycleCount { get; private set; }

        private void Awake()
        {
            RebuildCycleWait();
        }

        private void OnEnable()
        {
            _isShuttingDown = false;
            if (storage != null)
            {
                storage.StateChanged += HandleStorageChanged;
            }

            RefreshState();
            TryStartProduction();
        }

        private void OnDisable()
        {
            _isShuttingDown = true;
            if (storage != null)
            {
                storage.StateChanged -= HandleStorageChanged;
            }

            StopAndReleaseCycle();
            SetState(AutomatedDrillState.Disabled);
        }

        public bool TryStartProduction()
        {
            if (!Application.isPlaying
                || !isActiveAndEnabled
                || !gameObject.activeInHierarchy
                || _isShuttingDown
                || _isAcquiringReservation
                || _isCompletingCycle
                || _productionCoroutine != null
                || _storageReservation.IsValid
                || storage == null
                || !storage.isActiveAndEnabled)
            {
                RefreshState();
                return false;
            }

            _isAcquiringReservation = true;
            bool reserved;
            try
            {
                reserved = storage.TryReserveIncoming(out _storageReservation);
            }
            finally
            {
                _isAcquiringReservation = false;
            }

            if (!reserved)
            {
                _storageReservation = default;
                RefreshState();
                return false;
            }

            // Storage notifications are synchronous. Another listener may disable
            // this drill or invalidate the claim while the reservation is acquired.
            if (!isActiveAndEnabled
                || _isShuttingDown
                || !_storageReservation.IsValid)
            {
                ReleaseCurrentReservation();
                RefreshState();
                return false;
            }

            try
            {
                _productionCoroutine = StartCoroutine(ProductionCycle());
            }
            catch
            {
                ReleaseCurrentReservation();
                RefreshState();
                throw;
            }

            if (_productionCoroutine == null)
            {
                ReleaseCurrentReservation();
                RefreshState();
                return false;
            }

            RefreshState();
            return true;
        }

        public bool CompleteCycleImmediatelyForTests()
        {
            if (_productionCoroutine == null || !_storageReservation.IsValid)
            {
                return false;
            }

            StopCoroutine(_productionCoroutine);
            _productionCoroutine = null;
            return CompleteReservedCycle();
        }

        public void RestoreIdleState()
        {
            StopAndReleaseCycle();
            CompletedCycleCount = 0;
            RefreshState();
            TryStartProduction();
        }

        private IEnumerator ProductionCycle()
        {
            yield return _cycleWait;
            _productionCoroutine = null;
            CompleteReservedCycle();
        }

        private bool CompleteReservedCycle()
        {
            if (_isCompletingCycle || !_storageReservation.IsValid || storage == null)
            {
                ReleaseCurrentReservation();
                RefreshState();
                return false;
            }

            OreStorageReservation reservation = _storageReservation;
            _storageReservation = default;
            _isCompletingCycle = true;
            bool committed;
            try
            {
                committed = storage.TryDepositReserved(reservation);
                if (!committed && reservation.IsValid)
                {
                    storage.ReleaseIncoming(reservation);
                }
            }
            finally
            {
                _isCompletingCycle = false;
            }

            if (committed)
            {
                CompletedCycleCount++;
                productionParticles?.Emit(4);
                OreProduced?.Invoke(1);
            }

            RefreshState();
            TryStartProduction();
            return committed;
        }

        private void HandleStorageChanged(int storedOre, int incomingReservations)
        {
            if (_isShuttingDown || _isAcquiringReservation || _isCompletingCycle)
            {
                return;
            }

            if (_storageReservation.IsValid)
            {
                RefreshState();
                return;
            }

            if (_productionCoroutine != null)
            {
                StopCoroutine(_productionCoroutine);
                _productionCoroutine = null;
            }

            RefreshState();
            TryStartProduction();
        }

        private void StopAndReleaseCycle()
        {
            if (_productionCoroutine != null)
            {
                StopCoroutine(_productionCoroutine);
                _productionCoroutine = null;
            }

            ReleaseCurrentReservation();
        }

        private void ReleaseCurrentReservation()
        {
            OreStorageReservation reservation = _storageReservation;
            _storageReservation = default;
            if (reservation.IsValid && storage != null)
            {
                storage.ReleaseIncoming(reservation);
            }
        }

        private void RefreshState()
        {
            AutomatedDrillState nextState;
            if (!isActiveAndEnabled || _isShuttingDown)
            {
                nextState = AutomatedDrillState.Disabled;
            }
            else if (storage == null || !storage.isActiveAndEnabled)
            {
                nextState = AutomatedDrillState.MissingStorage;
            }
            else if (_productionCoroutine != null && _storageReservation.IsValid)
            {
                nextState = AutomatedDrillState.Producing;
            }
            else if (storage.IsFull)
            {
                nextState = AutomatedDrillState.StorageFull;
            }
            else
            {
                nextState = AutomatedDrillState.Idle;
            }

            SetState(nextState);
        }

        private void SetState(AutomatedDrillState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke(_state);
        }

        private void RebuildCycleWait()
        {
            _cycleWait = new WaitForSeconds(cycleDuration);
        }

        private void OnValidate()
        {
            cycleDuration = Mathf.Max(0.1f, cycleDuration);
        }
    }
}
