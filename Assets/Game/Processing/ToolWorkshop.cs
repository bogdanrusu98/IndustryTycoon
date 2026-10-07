using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    public sealed class ToolWorkshop : MonoBehaviour, IProductionUpgradeable, IFactorySpecializable
    {
        private const int PlankInputIndex = 0;
        private const int IronBarInputIndex = 1;
        private const int PlanksRequiredPerRecipe = 2;
        private const int IronBarsRequiredPerRecipe = 1;
        private const int ToolsProducedPerRecipe = 1;

        [Header("Buffers")]
        [SerializeField, Min(PlanksRequiredPerRecipe)]
        private int plankInputCapacity = 24;
        [SerializeField, Min(IronBarsRequiredPerRecipe)]
        private int ironBarInputCapacity = 12;
        [SerializeField, Min(ToolsProducedPerRecipe)]
        private int outputCapacity = 12;

        [Header("Recipe")]
        [SerializeField, Min(0.02f)] private float processingDuration = 2f;

        private readonly int[] _storedInputs = new int[2];
        private readonly int[] _inputCapacities = new int[2];
        private BufferedRecipeTransaction _recipeTransaction;
        private WaitForSeconds _processingWait;
        private Coroutine _processingCoroutine;
        private int _outputTools;
        private int _completedRecipeCount;
        private bool _isProcessing;
        private bool _isStartingProcessing;
        private bool _isInputTransferInProgress;
        private bool _isOutputTransferInProgress;
        private int _basePlankInputCapacity;
        private int _baseIronBarInputCapacity;
        private int _baseOutputCapacity;
        private float _baseProcessingDuration;
        private int _speedUpgradeLevel;
        private int _inputUpgradeLevel;
        private int _outputUpgradeLevel;
        private FactorySpecialization _specialization;
        private int _districtLevel = 1;
        private readonly Persistence.M15FactoryRecord _factoryProgress = new Persistence.M15FactoryRecord { buildingId = "tool_workshop" };

        public event Action<int, int, int, int, int, int> BufferChanged;
        public event Action<int, int, int> RecipeCompleted;

        public int InputPlanks => _storedInputs[PlankInputIndex];
        public int ProcessingInputPlanks => GetOwnedInput(PlankInputIndex);
        public int InputIronBars => _storedInputs[IronBarInputIndex];
        public int ProcessingInputIronBars => GetOwnedInput(IronBarInputIndex);
        public int OutputTools => _outputTools;
        public int ReservedOutputCapacity => _recipeTransaction != null
            ? _recipeTransaction.ReservedOutputCapacity
            : 0;
        public int AvailablePlankInputCapacity => Mathf.Max(
            0,
            plankInputCapacity - InputPlanks - ProcessingInputPlanks);
        public int AvailableIronBarInputCapacity => Mathf.Max(
            0,
            ironBarInputCapacity - InputIronBars - ProcessingInputIronBars);
        public int AvailableOutputCapacity => Mathf.Max(
            0,
            outputCapacity - _outputTools - ReservedOutputCapacity);
        public int PlankInputCapacity => plankInputCapacity;
        public int IronBarInputCapacity => ironBarInputCapacity;
        public int OutputCapacity => outputCapacity;
        public int RecipeInputPlanks => PlanksRequiredPerRecipe;
        public int RecipeInputIronBars => IronBarsRequiredPerRecipe;
        public int RecipeOutputTools => ToolsProducedPerRecipe;
        public float ProcessingDuration => processingDuration;
        public bool IsProcessing => _isProcessing;
        public bool IsStarvedForPlanks => !_isProcessing
                                          && InputPlanks < PlanksRequiredPerRecipe;
        public bool IsStarvedForIronBars => !_isProcessing
                                            && InputIronBars < IronBarsRequiredPerRecipe;
        public bool IsStarved => IsStarvedForPlanks || IsStarvedForIronBars;
        public bool IsOutputFull => !_isProcessing
                                    && _outputTools + ToolsProducedPerRecipe
                                    > outputCapacity;
        public int CompletedRecipeCount => _completedRecipeCount;
        public string ProductionBuildingId => "tool_workshop";
        public int SpeedUpgradeLevel => _speedUpgradeLevel;
        public int InputCapacityUpgradeLevel => _inputUpgradeLevel;
        public int OutputCapacityUpgradeLevel => _outputUpgradeLevel;
        public FactorySpecialization Specialization => _specialization;
        public int IndustrialDistrictLevel => _districtLevel;
        public int EfficiencyCompletedRemainder => _factoryProgress.efficiencyCompletedRemainder;
        public long CompletedSpecializedCycles => _factoryProgress.completedSpecializedCycles;
        public void ApplyFactorySpecialization(FactorySpecialization specialization, int districtLevel)
        { _specialization=specialization;_factoryProgress.specialization=specialization;_districtLevel=Mathf.Clamp(districtLevel,1,3);ApplyUpgradeLevels(_speedUpgradeLevel,_inputUpgradeLevel,_outputUpgradeLevel); }
        public void RestoreFactoryProgress(int remainder,long cycles){_factoryProgress.efficiencyCompletedRemainder=Mathf.Clamp(remainder,0,4);_factoryProgress.completedSpecializedCycles=Math.Max(0,cycles);}

        private void Awake()
        {
            CaptureBaseTuning();
            EnsureRuntimeState();
            AssertInvariants();
        }

        public bool ApplyUpgradeLevels(int speed, int input, int output)
        {
            CaptureBaseTuning();
            _speedUpgradeLevel = M14ProductionUpgradeTuning.ClampLevel(speed);
            _inputUpgradeLevel = M14ProductionUpgradeTuning.ClampLevel(input);
            _outputUpgradeLevel = M14ProductionUpgradeTuning.ClampLevel(output);
            plankInputCapacity = _basePlankInputCapacity
                + M14ProductionUpgradeTuning.GetCapacityBonus(_basePlankInputCapacity, _inputUpgradeLevel);
            ironBarInputCapacity = _baseIronBarInputCapacity
                + M14ProductionUpgradeTuning.GetCapacityBonus(_baseIronBarInputCapacity, _inputUpgradeLevel);
            outputCapacity = _baseOutputCapacity
                + M14ProductionUpgradeTuning.GetCapacityBonus(_baseOutputCapacity, _outputUpgradeLevel);
            processingDuration = M15FactoryTuning.CalculateDuration(_baseProcessingDuration,
                _speedUpgradeLevel, _specialization, ProductionBuildingId, _districtLevel);
            _processingWait = new WaitForSeconds(processingDuration);
            EnsureRuntimeState();
            NotifyBufferChanged();
            return true;
        }

        private void CaptureBaseTuning()
        {
            if (_basePlankInputCapacity > 0) return;
            _basePlankInputCapacity = plankInputCapacity;
            _baseIronBarInputCapacity = ironBarInputCapacity;
            _baseOutputCapacity = outputCapacity;
            _baseProcessingDuration = processingDuration;
        }

        private void OnEnable()
        {
            EnsureRuntimeState();
            if (Application.isPlaying && !TryStartProcessing())
            {
                NotifyBufferChanged();
            }
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

        public int GetAvailableInputCapacity(ResourceType resourceType)
        {
            switch (resourceType)
            {
                case ResourceType.Plank:
                    return AvailablePlankInputCapacity;
                case ResourceType.IronBar:
                    return AvailableIronBarInputCapacity;
                default:
                    return 0;
            }
        }

        public bool TryTransferInputFrom(
            CarryStack carryStack,
            ResourceType resourceType)
        {
            int inputIndex = GetInputIndex(resourceType);
            if (!isActiveAndEnabled
                || _isInputTransferInProgress
                || carryStack == null
                || inputIndex < 0
                || GetAvailableInputCapacity(resourceType) <= 0
                || !carryStack.CanRemove(resourceType, 1))
            {
                return false;
            }

            _isInputTransferInProgress = true;
            bool transferred = false;
            try
            {
                if (!carryStack.TryRemove(resourceType, 1))
                {
                    return false;
                }

                _storedInputs[inputIndex]++;
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
                || _outputTools <= 0
                || !carryStack.TryReserveCapacity(ResourceType.Tool, 1))
            {
                return false;
            }

            _isOutputTransferInProgress = true;
            bool carryReservationOwned = true;
            bool outputRemoved = false;
            bool transferred = false;
            try
            {
                _outputTools--;
                outputRemoved = true;

                if (!carryStack.TryCommitReservedAdd(ResourceType.Tool, 1))
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
                        _outputTools++;
                    }

                    if (carryReservationOwned)
                    {
                        bool released = carryStack.ReleaseReservedCapacity(1);
                        Debug.Assert(
                            released,
                            "Tool Workshop failed to release a rejected Tool carry reservation.");
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
            EnsureRuntimeState();
            if (!Application.isPlaying
                || !isActiveAndEnabled
                || _processingCoroutine != null
                || _isStartingProcessing
                || _isProcessing
                || _recipeTransaction.HasReservation)
            {
                return false;
            }

            _isStartingProcessing = true;
            if (!_recipeTransaction.TryBegin(
                    _storedInputs,
                    _outputTools,
                    outputCapacity))
            {
                _isStartingProcessing = false;
                return false;
            }

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
            return true;
        }

        public bool RestoreStableState(
            int inputPlanks,
            int inputIronBars,
            int outputTools)
        {
            if (inputPlanks < 0
                || inputPlanks > plankInputCapacity
                || inputIronBars < 0
                || inputIronBars > ironBarInputCapacity
                || outputTools < 0
                || outputTools > outputCapacity)
            {
                return false;
            }

            EnsureRuntimeState();
            StopProcessingAndResolveOwnership();
            _isInputTransferInProgress = false;
            _isOutputTransferInProgress = false;
            _storedInputs[PlankInputIndex] = inputPlanks;
            _storedInputs[IronBarInputIndex] = inputIronBars;
            _outputTools = outputTools;
            _completedRecipeCount = 0;
            AssertInvariants();
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
            bool committed = _isProcessing
                             && _recipeTransaction != null
                             && _recipeTransaction.TryCommit(
                                 ref _outputTools,
                                 outputCapacity);
            Debug.Assert(committed, "Tool Workshop recipe ownership became invalid.");
            if (!committed)
            {
                ResolveReservedRecipeOwnership();
                NotifyBufferChanged();
                AssertInvariants();
                TryStartProcessing();
                return false;
            }

            _isProcessing = false;
            Persistence.M15FactoryRules.CompleteRecipe(_factoryProgress, out bool conserve);
            if (conserve) _storedInputs[PlankInputIndex] = Mathf.Min(
                plankInputCapacity, _storedInputs[PlankInputIndex] + 1);
            _completedRecipeCount++;
            AssertInvariants();
            RecipeCompleted?.Invoke(
                PlanksRequiredPerRecipe,
                IronBarsRequiredPerRecipe,
                ToolsProducedPerRecipe);
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

            bool hadOwnedState = _isProcessing
                                 || (_recipeTransaction != null
                                     && _recipeTransaction.HasReservation);
            ResolveReservedRecipeOwnership();
            return hadOwnedState;
        }

        private void ResolveReservedRecipeOwnership()
        {
            if (_recipeTransaction != null && _recipeTransaction.HasReservation)
            {
                bool rolledBack = _recipeTransaction.TryRollback(
                    _storedInputs,
                    _inputCapacities);
                Debug.Assert(
                    rolledBack,
                    "Tool Workshop could not refund its in-flight recipe inputs.");
            }

            _isProcessing = false;
        }

        private int GetOwnedInput(int inputIndex)
        {
            return _recipeTransaction != null
                ? _recipeTransaction.GetOwnedInput(inputIndex)
                : 0;
        }

        private static int GetInputIndex(ResourceType resourceType)
        {
            switch (resourceType)
            {
                case ResourceType.Plank:
                    return PlankInputIndex;
                case ResourceType.IronBar:
                    return IronBarInputIndex;
                default:
                    return -1;
            }
        }

        private void NotifyBufferChanged()
        {
            BufferChanged?.Invoke(
                InputPlanks,
                ProcessingInputPlanks,
                InputIronBars,
                ProcessingInputIronBars,
                _outputTools,
                ReservedOutputCapacity);
        }

        private void EnsureRuntimeState()
        {
            _inputCapacities[PlankInputIndex] = plankInputCapacity;
            _inputCapacities[IronBarInputIndex] = ironBarInputCapacity;
            if (_recipeTransaction == null)
            {
                _recipeTransaction = new BufferedRecipeTransaction(
                    new[]
                    {
                        PlanksRequiredPerRecipe,
                        IronBarsRequiredPerRecipe
                    },
                    ToolsProducedPerRecipe);
            }

            if (_processingWait == null)
            {
                _processingWait = new WaitForSeconds(processingDuration);
            }
        }

        private void AssertInvariants()
        {
            Debug.Assert(InputPlanks >= 0, "Tool Workshop Plank input became negative.");
            Debug.Assert(InputIronBars >= 0, "Tool Workshop Iron Bar input became negative.");
            Debug.Assert(
                ProcessingInputPlanks == 0
                || ProcessingInputPlanks == PlanksRequiredPerRecipe,
                "Tool Workshop has invalid in-flight Plank ownership.");
            Debug.Assert(
                ProcessingInputIronBars == 0
                || ProcessingInputIronBars == IronBarsRequiredPerRecipe,
                "Tool Workshop has invalid in-flight Iron Bar ownership.");
            Debug.Assert(
                InputPlanks + ProcessingInputPlanks <= plankInputCapacity,
                "Tool Workshop Plank ownership exceeded capacity.");
            Debug.Assert(
                InputIronBars + ProcessingInputIronBars <= ironBarInputCapacity,
                "Tool Workshop Iron Bar ownership exceeded capacity.");
            Debug.Assert(_outputTools >= 0, "Tool Workshop output became negative.");
            Debug.Assert(
                ReservedOutputCapacity == 0
                || ReservedOutputCapacity == ToolsProducedPerRecipe,
                "Tool Workshop has an invalid Tool output reservation.");
            Debug.Assert(
                _outputTools + ReservedOutputCapacity <= outputCapacity,
                "Tool Workshop stored and reserved output exceeded capacity.");
            Debug.Assert(
                _isProcessing
                    ? ProcessingInputPlanks == PlanksRequiredPerRecipe
                      && ProcessingInputIronBars == IronBarsRequiredPerRecipe
                      && ReservedOutputCapacity == ToolsProducedPerRecipe
                    : ProcessingInputPlanks == 0
                      && ProcessingInputIronBars == 0
                      && ReservedOutputCapacity == 0,
                "Tool Workshop processing state does not match its owned resources.");
        }

        private void OnValidate()
        {
            plankInputCapacity = Mathf.Max(
                PlanksRequiredPerRecipe,
                plankInputCapacity);
            ironBarInputCapacity = Mathf.Max(
                IronBarsRequiredPerRecipe,
                ironBarInputCapacity);
            outputCapacity = Mathf.Max(ToolsProducedPerRecipe, outputCapacity);
            processingDuration = Mathf.Max(0.02f, processingDuration);
        }
    }
}
