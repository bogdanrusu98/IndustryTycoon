using System;
using System.Collections;
using IndustryTycoon.Feedback;
using IndustryTycoon.Processing;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    public enum WoodAutoFeederState
    {
        Disabled,
        Idle,
        Moving,
        WaitingForWood,
        DestinationFull
    }

    public sealed class WoodAutoFeeder : MonoBehaviour
    {
        [Header("Fixed Route")]
        [SerializeField] private WoodStockpile stockpile;
        [SerializeField] private WoodProcessor processor;
        [SerializeField] private WoodAutoFeederFeedback presentation;

        [Header("Cadence")]
        [SerializeField, Min(0.1f)] private float launchInterval = 0.75f;
        [SerializeField, Min(0.1f)] private float travelDuration = 0.55f;

        private WaitForSeconds _postTransferWait;
        private Coroutine _cycleCoroutine;
        private WoodStockpileOutgoingReservation _sourceReservation;
        private ProcessorInputReservation _destinationReservation;
        private WoodAutoFeederState _state = WoodAutoFeederState.Disabled;
        private uint _activeTransferGeneration;
        private uint _nextTransferGeneration;
        private float _transferProgress;
        private bool _hasInFlightTransfer;
        private bool _isAcquiring;
        private bool _isCancelling;
        private bool _isCompleting;
        private bool _isShuttingDown;

        public event Action<WoodAutoFeederState> StateChanged;
        public event Action<uint> TransferStarted;
        public event Action<uint> TransferCompleted;
        public event Action<uint> TransferCancelled;

        public WoodStockpile Stockpile => stockpile;
        public WoodProcessor Processor => processor;
        public WoodAutoFeederFeedback Presentation => presentation;
        public float LaunchInterval => launchInterval;
        public float TravelDuration => travelDuration;
        public WoodAutoFeederState State => _state;
        public bool IsTransferInFlight => _hasInFlightTransfer;
        public bool IsCoolingDown => _cycleCoroutine != null && !_hasInFlightTransfer;
        public int ActiveTransferCount => _hasInFlightTransfer ? 1 : 0;
        public float TransferProgress => _transferProgress;
        public uint ActiveTransferGeneration => _activeTransferGeneration;
        public int CompletedTransferCount { get; private set; }
        public int CancelledTransferCount { get; private set; }

        private void Awake()
        {
            RebuildCadenceWait();
        }

        private void OnEnable()
        {
            _isShuttingDown = false;
            if (stockpile != null)
            {
                stockpile.StateChanged += HandleSourceStateChanged;
            }

            if (processor != null)
            {
                processor.BufferChanged += HandleDestinationStateChanged;
            }

            RefreshState();
            TryStartTransfer();
        }

        private void OnDisable()
        {
            _isShuttingDown = true;
            if (stockpile != null)
            {
                stockpile.StateChanged -= HandleSourceStateChanged;
            }

            if (processor != null)
            {
                processor.BufferChanged -= HandleDestinationStateChanged;
            }

            StopCycleCoroutine();
            CancelInFlightTransferInternal();
            SetState(WoodAutoFeederState.Disabled);
        }

        public bool TryStartTransfer()
        {
            if (!Application.isPlaying
                || !isActiveAndEnabled
                || !gameObject.activeInHierarchy
                || _isShuttingDown
                || _isAcquiring
                || _isCompleting
                || _cycleCoroutine != null
                || _hasInFlightTransfer
                || stockpile == null
                || processor == null
                || presentation == null
                || !presentation.isActiveAndEnabled
                || stockpile.AvailableWood <= 0
                || processor.AvailableInputCapacity <= 0)
            {
                RefreshState();
                return false;
            }

            _isAcquiring = true;
            bool started = false;
            try
            {
                if (!processor.TryReserveInput(out ProcessorInputReservation destination))
                {
                    return false;
                }

                if (!stockpile.TryReserveOutgoing(
                        out WoodStockpileOutgoingReservation source))
                {
                    processor.ReleaseReservedInput(destination);
                    return false;
                }

                uint generation = NextTransferGeneration();
                _sourceReservation = source;
                _destinationReservation = destination;
                _activeTransferGeneration = generation;
                _transferProgress = 0f;
                _hasInFlightTransfer = true;

                if (!presentation.TryBeginTransfer(generation))
                {
                    CancelInFlightTransferInternal();
                    return false;
                }

                started = true;
            }
            finally
            {
                _isAcquiring = false;
            }

            if (!started || !_hasInFlightTransfer)
            {
                RefreshState();
                return false;
            }

            uint startedGeneration = _activeTransferGeneration;
            RefreshState();
            TransferStarted?.Invoke(startedGeneration);
            if (!_hasInFlightTransfer
                || _activeTransferGeneration != startedGeneration
                || !isActiveAndEnabled
                || !gameObject.activeInHierarchy
                || _isShuttingDown)
            {
                return false;
            }

            _cycleCoroutine = StartCoroutine(RunTransferCycle(startedGeneration));
            return true;
        }

        public bool CancelInFlightTransfer()
        {
            if (!_hasInFlightTransfer)
            {
                return false;
            }

            StopCycleCoroutine();
            bool cancelled = CancelInFlightTransferInternal();
            if (cancelled)
            {
                ScheduleRestartAfterCancellation();
            }

            return cancelled;
        }

        public void HandleTransferVisualDisabled(uint generation)
        {
            if (!_hasInFlightTransfer
                || generation == 0
                || generation != _activeTransferGeneration)
            {
                return;
            }

            CancelInFlightTransfer();
        }

        private IEnumerator RunTransferCycle(uint generation)
        {
            float elapsed = 0f;
            while (elapsed < travelDuration)
            {
                if (!IsCurrentTransferValid(generation))
                {
                    _cycleCoroutine = null;
                    CancelInFlightTransferInternal();
                    yield break;
                }

                elapsed = Mathf.Min(travelDuration, elapsed + Time.deltaTime);
                _transferProgress = Mathf.Clamp01(elapsed / travelDuration);
                presentation.SetTransferProgress(generation, _transferProgress);
                yield return null;
            }

            if (!IsCurrentTransferValid(generation))
            {
                _cycleCoroutine = null;
                CancelInFlightTransferInternal();
                yield break;
            }

            CompleteInFlightTransfer(generation);
            if (_isShuttingDown || !isActiveAndEnabled)
            {
                _cycleCoroutine = null;
                yield break;
            }

            if (_postTransferWait != null)
            {
                yield return _postTransferWait;
            }

            _cycleCoroutine = null;
            TryStartTransfer();
        }

        private IEnumerator RestartAfterCancellation()
        {
            if (_postTransferWait != null)
            {
                yield return _postTransferWait;
            }
            else
            {
                yield return null;
            }

            _cycleCoroutine = null;
            TryStartTransfer();
        }

        private void CompleteInFlightTransfer(uint generation)
        {
            if (!_hasInFlightTransfer || generation != _activeTransferGeneration)
            {
                return;
            }

            WoodStockpileOutgoingReservation source = _sourceReservation;
            ProcessorInputReservation destination = _destinationReservation;
            ClearInFlightState();
            _isCompleting = true;

            bool committed = WoodInputTransferTransaction.TryCommit(
                stockpile,
                source,
                processor,
                destination);
            if (!committed)
            {
                if (source.IsValid)
                {
                    stockpile.ReleaseOutgoing(source);
                }

                if (destination.IsValid)
                {
                    processor.ReleaseReservedInput(destination);
                }
            }

            presentation.ReleaseTransferVisual(generation);
            _isCompleting = false;

            if (committed)
            {
                CompletedTransferCount++;
                TransferCompleted?.Invoke(generation);
            }
            else
            {
                CancelledTransferCount++;
                TransferCancelled?.Invoke(generation);
            }

            RefreshState();
        }

        private bool CancelInFlightTransferInternal()
        {
            if (!_hasInFlightTransfer)
            {
                return false;
            }

            uint generation = _activeTransferGeneration;
            WoodStockpileOutgoingReservation source = _sourceReservation;
            ProcessorInputReservation destination = _destinationReservation;
            ClearInFlightState();
            _isCancelling = true;

            presentation?.ReleaseTransferVisual(generation);
            if (source.IsValid && stockpile != null)
            {
                stockpile.ReleaseOutgoing(source);
            }

            if (destination.IsValid && processor != null)
            {
                processor.ReleaseReservedInput(destination);
            }

            _isCancelling = false;
            CancelledTransferCount++;
            TransferCancelled?.Invoke(generation);
            RefreshState();
            return true;
        }

        private void ClearInFlightState()
        {
            _sourceReservation = default;
            _destinationReservation = default;
            _activeTransferGeneration = 0;
            _transferProgress = 0f;
            _hasInFlightTransfer = false;
        }

        private bool IsCurrentTransferValid(uint generation)
        {
            return !_isShuttingDown
                   && isActiveAndEnabled
                   && _hasInFlightTransfer
                   && generation != 0
                   && generation == _activeTransferGeneration
                   && _sourceReservation.IsValid
                   && _destinationReservation.IsValid;
        }

        private uint NextTransferGeneration()
        {
            _nextTransferGeneration++;
            if (_nextTransferGeneration == 0)
            {
                _nextTransferGeneration = 1;
            }

            return _nextTransferGeneration;
        }

        private void HandleSourceStateChanged(int storedWood, int incomingReservations)
        {
            HandleAuthoritativeStateChanged();
        }

        private void HandleDestinationStateChanged(
            int inputWood,
            int outputPlanks,
            int reservedOutputCapacity)
        {
            HandleAuthoritativeStateChanged();
        }

        private void HandleAuthoritativeStateChanged()
        {
            if (_isShuttingDown || _isAcquiring || _isCancelling || _isCompleting)
            {
                return;
            }

            if (_hasInFlightTransfer)
            {
                if (!_sourceReservation.IsValid || !_destinationReservation.IsValid)
                {
                    CancelInFlightTransfer();
                }

                return;
            }

            if (_cycleCoroutine == null)
            {
                TryStartTransfer();
            }
            else
            {
                RefreshState();
            }
        }

        private void StopCycleCoroutine()
        {
            if (_cycleCoroutine == null)
            {
                return;
            }

            StopCoroutine(_cycleCoroutine);
            _cycleCoroutine = null;
        }

        private void ScheduleRestartAfterCancellation()
        {
            if (_cycleCoroutine != null
                || _isShuttingDown
                || !isActiveAndEnabled
                || !gameObject.activeInHierarchy
                || presentation == null
                || !presentation.isActiveAndEnabled
                || !presentation.gameObject.activeInHierarchy)
            {
                return;
            }

            _cycleCoroutine = StartCoroutine(RestartAfterCancellation());
        }

        private void RefreshState()
        {
            WoodAutoFeederState nextState;
            if (!isActiveAndEnabled || _isShuttingDown)
            {
                nextState = WoodAutoFeederState.Disabled;
            }
            else if (_hasInFlightTransfer)
            {
                nextState = WoodAutoFeederState.Moving;
            }
            else if (processor == null
                     || !processor.isActiveAndEnabled
                     || processor.AvailableInputCapacity <= 0)
            {
                nextState = WoodAutoFeederState.DestinationFull;
            }
            else if (stockpile == null
                     || !stockpile.isActiveAndEnabled
                     || stockpile.AvailableWood <= 0)
            {
                nextState = WoodAutoFeederState.WaitingForWood;
            }
            else
            {
                nextState = WoodAutoFeederState.Idle;
            }

            SetState(nextState);
        }

        private void SetState(WoodAutoFeederState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke(_state);
        }

        private void RebuildCadenceWait()
        {
            float postTransferDelay = Mathf.Max(0f, launchInterval - travelDuration);
            _postTransferWait = postTransferDelay > 0f
                ? new WaitForSeconds(postTransferDelay)
                : null;
        }

        private void OnValidate()
        {
            launchInterval = Mathf.Max(0.1f, launchInterval);
            travelDuration = Mathf.Clamp(travelDuration, 0.1f, launchInterval);
        }
    }
}
