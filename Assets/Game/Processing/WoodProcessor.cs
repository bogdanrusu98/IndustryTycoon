using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    public readonly struct ProcessorInputReservation
    {
        private readonly WoodProcessor _processor;
        private readonly uint _token;

        internal ProcessorInputReservation(WoodProcessor processor, uint token)
        {
            _processor = processor;
            _token = token;
        }

        public bool IsValid => _processor != null
                               && _processor.IsInputReservationValid(this);

        internal WoodProcessor Processor => _processor;
        internal uint Token => _token;
    }

    public sealed class WoodProcessor : MonoBehaviour
    {
        private const int WoodRequiredPerRecipe = 2;
        private const int PlanksProducedPerRecipe = 1;

        [Header("Buffers")]
        [SerializeField, Min(1)] private int inputCapacity = 24;
        [SerializeField, Min(1)] private int outputCapacity = 12;

        [Header("Recipe")]
        [SerializeField, Min(0.02f)] private float processingDuration = 1.1f;

        private WaitForSeconds _processingWait;
        private Coroutine _processingCoroutine;
        private int _inputWood;
        private int _reservedInputCapacity;
        private int _outputPlanks;
        private int _reservedOutputCapacity;
        private uint _activeInputReservationToken;
        private uint _nextInputReservationToken;
        private int _completedRecipeCount;
        private bool _isProcessing;
        private bool _isStartingProcessing;
        private bool _isInputTransferInProgress;
        private bool _isOutputTransferInProgress;

        public event Action<int, int, int> BufferChanged;
        public event Action<bool> ProcessingChanged;
        public event Action<int, int> RecipeCompleted;

        public int InputWood => _inputWood;
        public int ReservedInputCapacity => _reservedInputCapacity;
        public int AvailableInputCapacity => Mathf.Max(
            0,
            inputCapacity - _inputWood - _reservedInputCapacity);
        public int OutputPlanks => _outputPlanks;
        public int ReservedOutputCapacity => _reservedOutputCapacity;
        public int InputCapacity => inputCapacity;
        public int OutputCapacity => outputCapacity;
        public int RecipeInputWood => WoodRequiredPerRecipe;
        public int RecipeOutputPlanks => PlanksProducedPerRecipe;
        public float ProcessingDuration => processingDuration;
        public bool IsProcessing => _isProcessing;
        public int CompletedRecipeCount => _completedRecipeCount;

        private void Awake()
        {
            RebuildProcessingWait();
        }

        private void OnEnable()
        {
            TryStartProcessing();
        }

        private void OnDisable()
        {
            bool hadInputReservation = _reservedInputCapacity > 0;
            InvalidateInputReservation();
            StopProcessingAndReleaseReservation();
            if (hadInputReservation)
            {
                NotifyBufferChanged();
            }
        }

        public bool TryReserveInput(out ProcessorInputReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || _reservedInputCapacity != 0
                || AvailableInputCapacity <= 0)
            {
                return false;
            }

            _nextInputReservationToken++;
            if (_nextInputReservationToken == 0)
            {
                _nextInputReservationToken = 1;
            }

            _activeInputReservationToken = _nextInputReservationToken;
            _reservedInputCapacity = 1;
            reservation = new ProcessorInputReservation(
                this,
                _activeInputReservationToken);
            NotifyBufferChanged();
            return true;
        }

        public bool IsInputReservationValid(ProcessorInputReservation reservation)
        {
            return isActiveAndEnabled
                   && reservation.Processor == this
                   && reservation.Token != 0
                   && reservation.Token == _activeInputReservationToken
                   && _reservedInputCapacity == 1
                   && _inputWood + _reservedInputCapacity <= inputCapacity;
        }

        public bool ReleaseReservedInput(ProcessorInputReservation reservation)
        {
            if (!IsInputReservationValid(reservation))
            {
                return false;
            }

            InvalidateInputReservation();
            NotifyBufferChanged();
            return true;
        }

        public bool TryCommitReservedInput(ProcessorInputReservation reservation)
        {
            if (!IsInputReservationValid(reservation))
            {
                return false;
            }

            CommitReservedInputForTransfer(reservation);
            PublishInputTransferCommitted();
            return true;
        }

        internal void CommitReservedInputForTransfer(
            ProcessorInputReservation reservation)
        {
            Debug.Assert(IsInputReservationValid(reservation));
            _inputWood++;
            InvalidateInputReservation();
        }

        internal void PublishInputTransferCommitted()
        {
            NotifyBufferChanged();
            TryStartProcessing();
        }

        public bool TryTransferInputFrom(CarryStack carryStack)
        {
            if (!isActiveAndEnabled
                || _isInputTransferInProgress
                || carryStack == null
                || AvailableInputCapacity <= 0
                || carryStack.GetAmount(ResourceType.Wood) <= 0)
            {
                return false;
            }

            _isInputTransferInProgress = true;
            bool transferred = false;
            try
            {
                if (!carryStack.TryRemove(ResourceType.Wood, 1))
                {
                    return false;
                }

                _inputWood++;
                transferred = true;
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
                || _outputPlanks <= 0
                || !carryStack.TryReserveCapacity(ResourceType.Plank, 1))
            {
                return false;
            }

            _isOutputTransferInProgress = true;
            bool transferred = false;
            try
            {
                _outputPlanks--;
                if (!carryStack.TryCommitReservedAdd(ResourceType.Plank, 1))
                {
                    _outputPlanks++;
                    carryStack.ReleaseReservedCapacity(1);
                    return false;
                }

                transferred = true;
            }
            finally
            {
                _isOutputTransferInProgress = false;
            }

            if (!transferred)
            {
                return false;
            }

            NotifyBufferChanged();
            TryStartProcessing();
            return true;
        }

        public bool TryStartProcessing()
        {
            if (!Application.isPlaying
                || !isActiveAndEnabled
                || _processingCoroutine != null
                || _isStartingProcessing
                || _isProcessing
                || _inputWood < WoodRequiredPerRecipe
                || _outputPlanks + _reservedOutputCapacity + PlanksProducedPerRecipe > outputCapacity)
            {
                return false;
            }

            _isStartingProcessing = true;
            _reservedOutputCapacity = PlanksProducedPerRecipe;
            _isProcessing = true;
            try
            {
                _processingCoroutine = StartCoroutine(ProcessReservedRecipe());
            }
            finally
            {
                _isStartingProcessing = false;
            }

            NotifyBufferChanged();
            ProcessingChanged?.Invoke(true);
            return true;
        }

        private IEnumerator ProcessReservedRecipe()
        {
            yield return _processingWait;

            _processingCoroutine = null;
            CompleteReservedRecipe();
        }

        private void CompleteReservedRecipe()
        {
            if (!_isProcessing
                || _reservedOutputCapacity != PlanksProducedPerRecipe
                || _inputWood < WoodRequiredPerRecipe
                || _outputPlanks + PlanksProducedPerRecipe > outputCapacity)
            {
                ReleaseOutputReservation();
                TryStartProcessing();
                return;
            }

            _inputWood -= WoodRequiredPerRecipe;
            _outputPlanks += PlanksProducedPerRecipe;
            _reservedOutputCapacity = 0;
            _isProcessing = false;
            _completedRecipeCount++;
            int resultingInputWood = _inputWood;
            int resultingOutputPlanks = _outputPlanks;

            ProcessingChanged?.Invoke(false);
            RecipeCompleted?.Invoke(resultingInputWood, resultingOutputPlanks);
            NotifyBufferChanged();
            TryStartProcessing();
        }

        private void StopProcessingAndReleaseReservation()
        {
            _isStartingProcessing = false;
            if (_processingCoroutine != null)
            {
                StopCoroutine(_processingCoroutine);
                _processingCoroutine = null;
            }

            ReleaseOutputReservation();
        }

        private void ReleaseOutputReservation()
        {
            bool wasProcessing = _isProcessing;
            bool hadReservation = _reservedOutputCapacity > 0;
            _reservedOutputCapacity = 0;
            _isProcessing = false;

            if (wasProcessing)
            {
                ProcessingChanged?.Invoke(false);
            }

            if (hadReservation)
            {
                NotifyBufferChanged();
            }
        }

        private void InvalidateInputReservation()
        {
            _reservedInputCapacity = 0;
            _activeInputReservationToken = 0;
        }

        private void NotifyBufferChanged()
        {
            BufferChanged?.Invoke(_inputWood, _outputPlanks, _reservedOutputCapacity);
        }

        private void RebuildProcessingWait()
        {
            _processingWait = new WaitForSeconds(processingDuration);
        }

        private void OnValidate()
        {
            inputCapacity = Mathf.Max(1, inputCapacity);
            outputCapacity = Mathf.Max(1, outputCapacity);
            processingDuration = Mathf.Max(0.02f, processingDuration);
        }
    }
}
