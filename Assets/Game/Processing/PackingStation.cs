using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    public readonly struct PackingStationOutputReservation
    {
        private readonly PackingStation _station;
        private readonly uint _token;

        internal PackingStationOutputReservation(PackingStation station, uint token)
        {
            _station = station;
            _token = token;
        }

        public bool IsValid => _station != null
                               && _station.IsCourierOutputReservationValid(this);
        public int ReservedCrates => _station != null
            ? _station.GetCourierOutputReservationCount(this)
            : 0;

        internal PackingStation Station => _station;
        internal uint Token => _token;
    }

    public class PackingStation : MonoBehaviour
    {
        private const int PlanksRequiredPerRecipe = 2;
        private const int CratesProducedPerRecipe = 1;
        private const int MaximumCourierReservation = 2;

        [Header("Buffers")]
        [SerializeField, Min(PlanksRequiredPerRecipe)] private int inputCapacity = 24;
        [SerializeField, Min(CratesProducedPerRecipe)] private int outputCapacity = 12;

        [Header("Recipe")]
        [SerializeField, Min(0.02f)] private float processingDuration = 1.5f;

        private WaitForSeconds _processingWait;
        private Coroutine _processingCoroutine;
        private int _inputPlanks;
        private int _processingInputPlanks;
        private int _outputCrates;
        private int _reservedOutputCapacity;
        private int _reservedCourierOutputCrates;
        private uint _activeCourierOutputReservationToken;
        private uint _nextCourierOutputReservationToken;
        private int _completedRecipeCount;
        private bool _isProcessing;
        private bool _isStartingProcessing;
        private bool _isInputTransferInProgress;
        private bool _isOutputTransferInProgress;

        public event Action<int, int, int, int> BufferChanged;
        public event Action<int> CourierOutputReservationChanged;
        public event Action<bool> ProcessingChanged;
        public event Action<int, int> RecipeCompleted;

        public int InputPlanks => _inputPlanks;
        public int ProcessingInputPlanks => _processingInputPlanks;
        public int AvailableInputCapacity => Mathf.Max(
            0,
            inputCapacity - _inputPlanks - _processingInputPlanks);
        public int OutputCrates => _outputCrates;
        public int ReservedOutputCapacity => _reservedOutputCapacity;
        public int ReservedCourierOutputCrates => _reservedCourierOutputCrates;
        public int MaximumCourierReservedCrates => MaximumCourierReservation;
        public int AvailableOutputCapacity => Mathf.Max(
            0,
            outputCapacity - _outputCrates - _reservedOutputCapacity);
        public int InputCapacity => inputCapacity;
        public int OutputCapacity => outputCapacity;
        public int RecipeInputPlanks => PlanksRequiredPerRecipe;
        public int RecipeOutputCrates => CratesProducedPerRecipe;
        public float ProcessingDuration => processingDuration;
        public bool IsProcessing => _isProcessing;
        public bool IsStarved => !_isProcessing
                                 && _inputPlanks < PlanksRequiredPerRecipe;
        public bool IsOutputFull => !_isProcessing
                                    && _outputCrates + _reservedOutputCapacity
                                    + CratesProducedPerRecipe > outputCapacity;
        public int CompletedRecipeCount => _completedRecipeCount;

        protected virtual ResourceType InputResourceType => ResourceType.Plank;
        protected virtual ResourceType OutputResourceType => ResourceType.Crate;

        protected virtual void Awake()
        {
            RebuildProcessingWait();
            AssertInvariants();
        }

        protected virtual void OnEnable()
        {
            if (Application.isPlaying && !TryStartProcessing())
            {
                NotifyBufferChanged();
            }
        }

        protected virtual void OnDisable()
        {
            bool reservationChanged = _reservedCourierOutputCrates > 0;
            InvalidateCourierOutputReservation();
            bool stateChanged = StopProcessingAndResolveOwnership();
            if (reservationChanged)
            {
                CourierOutputReservationChanged?.Invoke(0);
            }

            if (stateChanged)
            {
                NotifyBufferChanged();
            }

            AssertInvariants();
        }

        public bool TryTransferInputFrom(CarryStack carryStack)
        {
            if (!isActiveAndEnabled
                || _isInputTransferInProgress
                || carryStack == null
                || AvailableInputCapacity <= 0
                || !carryStack.CanRemove(InputResourceType, 1))
            {
                return false;
            }

            _isInputTransferInProgress = true;
            bool transferred = false;
            try
            {
                if (!carryStack.TryRemove(InputResourceType, 1))
                {
                    return false;
                }

                _inputPlanks++;
                transferred = true;
                AssertInvariants();
            }
            finally
            {
                _isInputTransferInProgress = false;
            }

            if (!transferred)
            {
                return false;
            }

            NotifyBufferChanged();
            TryStartProcessing();
            return true;
        }

        public bool TryTransferOutputTo(CarryStack carryStack)
        {
            if (!isActiveAndEnabled
                || _isOutputTransferInProgress
                || carryStack == null
                || _outputCrates <= 0
                || !carryStack.TryReserveCapacity(OutputResourceType, 1))
            {
                return false;
            }

            _isOutputTransferInProgress = true;
            bool carryReservationOwned = true;
            bool outputRemoved = false;
            bool transferred = false;
            bool courierReservationChanged = false;
            try
            {
                _outputCrates--;
                outputRemoved = true;

                if (!carryStack.TryCommitReservedAdd(OutputResourceType, 1))
                {
                    return false;
                }

                carryReservationOwned = false;
                outputRemoved = false;
                courierReservationChanged = TrimCourierReservationToOutput();
                transferred = true;
            }
            finally
            {
                if (!transferred)
                {
                    if (outputRemoved)
                    {
                        _outputCrates++;
                    }

                    if (carryReservationOwned)
                    {
                        bool released = carryStack.ReleaseReservedCapacity(1);
                        Debug.Assert(
                            released,
                            "Packing Station failed to release a rejected Crate carry reservation.");
                    }
                }

                _isOutputTransferInProgress = false;
                AssertInvariants();
            }

            if (!transferred)
            {
                return false;
            }

            if (courierReservationChanged)
            {
                CourierOutputReservationChanged?.Invoke(_reservedCourierOutputCrates);
            }

            NotifyBufferChanged();
            TryStartProcessing();
            return true;
        }

        public bool TryReserveCourierOutput(
            int maximumCrates,
            out PackingStationOutputReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || maximumCrates <= 0
                || _reservedCourierOutputCrates != 0
                || _outputCrates <= 0)
            {
                return false;
            }

            int reservedCrates = Mathf.Min(
                MaximumCourierReservation,
                Mathf.Min(maximumCrates, _outputCrates));
            if (reservedCrates <= 0)
            {
                return false;
            }

            _nextCourierOutputReservationToken++;
            if (_nextCourierOutputReservationToken == 0)
            {
                _nextCourierOutputReservationToken = 1;
            }

            _activeCourierOutputReservationToken = _nextCourierOutputReservationToken;
            _reservedCourierOutputCrates = reservedCrates;
            reservation = new PackingStationOutputReservation(
                this,
                _activeCourierOutputReservationToken);

            AssertInvariants();
            CourierOutputReservationChanged?.Invoke(_reservedCourierOutputCrates);
            return true;
        }

        public bool IsCourierOutputReservationValid(
            PackingStationOutputReservation reservation)
        {
            return isActiveAndEnabled
                   && reservation.Station == this
                   && reservation.Token != 0
                   && reservation.Token == _activeCourierOutputReservationToken
                   && _reservedCourierOutputCrates > 0
                   && _reservedCourierOutputCrates <= _outputCrates;
        }

        public int GetCourierOutputReservationCount(
            PackingStationOutputReservation reservation)
        {
            return IsCourierOutputReservationValid(reservation)
                ? _reservedCourierOutputCrates
                : 0;
        }

        public bool ReleaseCourierOutputReservation(
            PackingStationOutputReservation reservation)
        {
            if (!IsCourierOutputReservationValid(reservation))
            {
                return false;
            }

            InvalidateCourierOutputReservation();
            AssertInvariants();
            CourierOutputReservationChanged?.Invoke(0);
            return true;
        }

        internal bool CanCommitCourierOutputReservation(
            PackingStationOutputReservation reservation)
        {
            return IsCourierOutputReservationValid(reservation)
                   && _reservedCourierOutputCrates <= _outputCrates;
        }

        internal int FinalizeCourierOutputReservation(
            PackingStationOutputReservation reservation)
        {
            Debug.Assert(CanCommitCourierOutputReservation(reservation));
            int committedCrates = _reservedCourierOutputCrates;
            _outputCrates -= committedCrates;
            InvalidateCourierOutputReservation();
            AssertInvariants();
            return committedCrates;
        }

        internal void PublishCourierOutputCommitted()
        {
            CourierOutputReservationChanged?.Invoke(0);
            NotifyBufferChanged();
            TryStartProcessing();
        }

        public bool TryStartProcessing()
        {
            if (!Application.isPlaying
                || !isActiveAndEnabled
                || _processingCoroutine != null
                || _isStartingProcessing
                || _isProcessing
                || _processingInputPlanks != 0
                || _reservedOutputCapacity != 0
                || _inputPlanks < PlanksRequiredPerRecipe
                || _outputCrates + CratesProducedPerRecipe > outputCapacity)
            {
                return false;
            }

            _isStartingProcessing = true;
            _reservedOutputCapacity = CratesProducedPerRecipe;
            _inputPlanks -= PlanksRequiredPerRecipe;
            _processingInputPlanks = PlanksRequiredPerRecipe;
            _isProcessing = true;

            try
            {
                _processingCoroutine = StartCoroutine(ProcessReservedRecipe());
            }
            catch
            {
                ResolveReservedRecipeOwnership();
                throw;
            }
            finally
            {
                _isStartingProcessing = false;
            }

            if (_processingCoroutine == null)
            {
                ResolveReservedRecipeOwnership();
                AssertInvariants();
                return false;
            }

            AssertInvariants();
            NotifyBufferChanged();
            ProcessingChanged?.Invoke(true);
            return true;
        }

        public bool RestoreStableState(int inputPlanks, int outputCrates)
        {
            if (inputPlanks < 0
                || inputPlanks > inputCapacity
                || outputCrates < 0
                || outputCrates > outputCapacity)
            {
                return false;
            }

            InvalidateCourierOutputReservation();
            StopProcessingAndResolveOwnership();
            _isInputTransferInProgress = false;
            _isOutputTransferInProgress = false;
            _inputPlanks = inputPlanks;
            _processingInputPlanks = 0;
            _outputCrates = outputCrates;
            _reservedOutputCapacity = 0;
            _completedRecipeCount = 0;
            AssertInvariants();
            CourierOutputReservationChanged?.Invoke(0);
            NotifyBufferChanged();
            return true;
        }

        public bool CompleteProcessingImmediatelyForTests()
        {
            if (!isActiveAndEnabled
                || (!_isProcessing && !TryStartProcessing()))
            {
                return false;
            }

            if (_processingCoroutine != null)
            {
                StopCoroutine(_processingCoroutine);
                _processingCoroutine = null;
            }

            return CompleteReservedRecipe();
        }

        private IEnumerator ProcessReservedRecipe()
        {
            yield return _processingWait;

            _processingCoroutine = null;
            CompleteReservedRecipe();
        }

        private bool CompleteReservedRecipe()
        {
            bool hasValidOwnership = _isProcessing
                                     && _processingInputPlanks == PlanksRequiredPerRecipe
                                     && _reservedOutputCapacity == CratesProducedPerRecipe
                                     && _outputCrates + _reservedOutputCapacity <= outputCapacity;
            Debug.Assert(hasValidOwnership, "Packing Station recipe ownership became invalid.");
            if (!hasValidOwnership)
            {
                bool wasProcessing = _isProcessing;
                ResolveReservedRecipeOwnership();
                if (wasProcessing)
                {
                    ProcessingChanged?.Invoke(false);
                }

                NotifyBufferChanged();
                AssertInvariants();
                TryStartProcessing();
                return false;
            }

            _processingInputPlanks = 0;
            _reservedOutputCapacity = 0;
            _outputCrates += CratesProducedPerRecipe;
            _isProcessing = false;
            _completedRecipeCount++;

            AssertInvariants();
            ProcessingChanged?.Invoke(false);
            RecipeCompleted?.Invoke(_inputPlanks, _outputCrates);
            NotifyBufferChanged();
            TryStartProcessing();
            return true;
        }

        private bool StopProcessingAndResolveOwnership()
        {
            _isStartingProcessing = false;
            if (_processingCoroutine != null)
            {
                StopCoroutine(_processingCoroutine);
                _processingCoroutine = null;
            }

            bool wasProcessing = _isProcessing;
            bool hadOwnedState = _processingInputPlanks > 0
                                 || _reservedOutputCapacity > 0;
            ResolveReservedRecipeOwnership();
            if (wasProcessing)
            {
                ProcessingChanged?.Invoke(false);
            }

            return hadOwnedState;
        }

        private void ResolveReservedRecipeOwnership()
        {
            if (_processingInputPlanks > 0)
            {
                Debug.Assert(
                    _inputPlanks + _processingInputPlanks <= inputCapacity,
                    "Packing Station cannot refund its in-flight Planks without exceeding input capacity.");
                _inputPlanks += _processingInputPlanks;
            }

            _processingInputPlanks = 0;
            _reservedOutputCapacity = 0;
            _isProcessing = false;
        }

        private bool TrimCourierReservationToOutput()
        {
            if (_reservedCourierOutputCrates <= _outputCrates)
            {
                return false;
            }

            _reservedCourierOutputCrates = _outputCrates;
            if (_reservedCourierOutputCrates == 0)
            {
                _activeCourierOutputReservationToken = 0;
            }

            return true;
        }

        private void InvalidateCourierOutputReservation()
        {
            _reservedCourierOutputCrates = 0;
            _activeCourierOutputReservationToken = 0;
        }

        private void NotifyBufferChanged()
        {
            BufferChanged?.Invoke(
                _inputPlanks,
                _processingInputPlanks,
                _outputCrates,
                _reservedOutputCapacity);
        }

        private void AssertInvariants()
        {
            Debug.Assert(_inputPlanks >= 0, "Packing Station input became negative.");
            Debug.Assert(
                _processingInputPlanks == 0
                || _processingInputPlanks == PlanksRequiredPerRecipe,
                "Packing Station has invalid in-flight Plank ownership.");
            Debug.Assert(
                _inputPlanks + _processingInputPlanks <= inputCapacity,
                "Packing Station input ownership exceeded capacity.");
            Debug.Assert(_outputCrates >= 0, "Packing Station output became negative.");
            Debug.Assert(
                _reservedCourierOutputCrates >= 0
                && _reservedCourierOutputCrates <= MaximumCourierReservation
                && _reservedCourierOutputCrates <= _outputCrates,
                "Packing Station courier reservation exceeded stored Crates.");
            Debug.Assert(
                _reservedCourierOutputCrates > 0
                    ? _activeCourierOutputReservationToken != 0
                    : _activeCourierOutputReservationToken == 0,
                "Packing Station courier reservation token does not match its claim.");
            Debug.Assert(
                _reservedOutputCapacity == 0
                || _reservedOutputCapacity == CratesProducedPerRecipe,
                "Packing Station has an invalid Crate output reservation.");
            Debug.Assert(
                _outputCrates + _reservedOutputCapacity <= outputCapacity,
                "Packing Station stored and reserved output exceeded capacity.");
            Debug.Assert(
                _isProcessing
                    ? _processingInputPlanks == PlanksRequiredPerRecipe
                      && _reservedOutputCapacity == CratesProducedPerRecipe
                    : _processingInputPlanks == 0 && _reservedOutputCapacity == 0,
                "Packing Station processing state does not match its owned resources.");
        }

        private void RebuildProcessingWait()
        {
            _processingWait = new WaitForSeconds(processingDuration);
        }

        protected virtual void OnValidate()
        {
            inputCapacity = Mathf.Max(PlanksRequiredPerRecipe, inputCapacity);
            outputCapacity = Mathf.Max(CratesProducedPerRecipe, outputCapacity);
            processingDuration = Mathf.Max(0.02f, processingDuration);
        }
    }
}
