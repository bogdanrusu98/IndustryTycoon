using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
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
        private int _outputPlanks;
        private int _reservedOutputCapacity;
        private int _completedRecipeCount;
        private bool _isProcessing;
        private bool _isStartingProcessing;
        private bool _isInputTransferInProgress;
        private bool _isOutputTransferInProgress;

        public event Action<int, int, int> BufferChanged;
        public event Action<bool> ProcessingChanged;
        public event Action<int, int> RecipeCompleted;

        public int InputWood => _inputWood;
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
            StopProcessingAndReleaseReservation();
        }

        public bool TryTransferInputFrom(CarryStack carryStack)
        {
            if (!isActiveAndEnabled
                || _isInputTransferInProgress
                || carryStack == null
                || _inputWood >= inputCapacity
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
