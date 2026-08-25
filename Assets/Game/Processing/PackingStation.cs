using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    public sealed class PackingStation : MonoBehaviour
    {
        private const int PlanksRequiredPerRecipe = 2;
        private const int CratesProducedPerRecipe = 1;

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
        private int _completedRecipeCount;
        private bool _isProcessing;
        private bool _isStartingProcessing;
        private bool _isInputTransferInProgress;
        private bool _isOutputTransferInProgress;

        public event Action<int, int, int, int> BufferChanged;
        public event Action<bool> ProcessingChanged;
        public event Action<int, int> RecipeCompleted;

        public int InputPlanks => _inputPlanks;
        public int ProcessingInputPlanks => _processingInputPlanks;
        public int AvailableInputCapacity => Mathf.Max(
            0,
            inputCapacity - _inputPlanks - _processingInputPlanks);
        public int OutputCrates => _outputCrates;
        public int ReservedOutputCapacity => _reservedOutputCapacity;
        public int AvailableOutputCapacity => Mathf.Max(
            0,
            outputCapacity - _outputCrates - _reservedOutputCapacity);
        public int InputCapacity => inputCapacity;
        public int OutputCapacity => outputCapacity;
        public int RecipeInputPlanks => PlanksRequiredPerRecipe;
        public int RecipeOutputCrates => CratesProducedPerRecipe;
        public float ProcessingDuration => processingDuration;
        public bool IsProcessing => _isProcessing;
        public int CompletedRecipeCount => _completedRecipeCount;

        private void Awake()
        {
            RebuildProcessingWait();
            AssertInvariants();
        }

        private void OnEnable()
        {
            TryStartProcessing();
        }

        private void OnDisable()
        {
            bool stateChanged = StopProcessingAndResolveOwnership();
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
                || !carryStack.CanRemove(ResourceType.Plank, 1))
            {
                return false;
            }

            _isInputTransferInProgress = true;
            bool transferred = false;
            try
            {
                if (!carryStack.TryRemove(ResourceType.Plank, 1))
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
                || !carryStack.TryReserveCapacity(ResourceType.Crate, 1))
            {
                return false;
            }

            _isOutputTransferInProgress = true;
            bool carryReservationOwned = true;
            bool outputRemoved = false;
            bool transferred = false;
            try
            {
                _outputCrates--;
                outputRemoved = true;

                if (!carryStack.TryCommitReservedAdd(ResourceType.Crate, 1))
                {
                    return false;
                }

                carryReservationOwned = false;
                outputRemoved = false;
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

        private IEnumerator ProcessReservedRecipe()
        {
            yield return _processingWait;

            _processingCoroutine = null;
            CompleteReservedRecipe();
        }

        private void CompleteReservedRecipe()
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
                return;
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

        private void OnValidate()
        {
            inputCapacity = Mathf.Max(PlanksRequiredPerRecipe, inputCapacity);
            outputCapacity = Mathf.Max(CratesProducedPerRecipe, outputCapacity);
            processingDuration = Mathf.Max(0.02f, processingDuration);
        }
    }
}
