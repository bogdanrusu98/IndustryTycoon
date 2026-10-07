using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;
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

    public sealed class WoodProcessor : MonoBehaviour, IIndustrialResourceEndpoint,
        IProductionUpgradeable, IFactorySpecializable
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
        private int _reservedIndustrialIncoming;
        private int _reservedIndustrialOutgoing;
        private uint _nextIndustrialToken;
        private IndustrialResourceReservation _industrialIncoming;
        private IndustrialResourceReservation _industrialOutgoing;
        private int _baseInputCapacity;
        private int _baseOutputCapacity;
        private float _baseProcessingDuration;
        private int _speedUpgradeLevel;
        private int _inputUpgradeLevel;
        private int _outputUpgradeLevel;
        private FactorySpecialization _specialization;
        private int _districtLevel = 1;
        private readonly Persistence.M15FactoryRecord _factoryProgress = new Persistence.M15FactoryRecord { buildingId = "wood_processor" };

        public event Action<int, int, int> BufferChanged;
        public event Action<bool> ProcessingChanged;
        public event Action<int, int> RecipeCompleted;

        public int InputWood => _inputWood;
        public int ReservedInputCapacity => _reservedInputCapacity;
        public int AvailableInputCapacity => Mathf.Max(
            0,
            inputCapacity - _inputWood - _reservedInputCapacity
            - _reservedIndustrialIncoming);
        public int OutputPlanks => _outputPlanks;
        public int ReservedOutputCapacity => _reservedOutputCapacity;
        public int InputCapacity => inputCapacity;
        public int OutputCapacity => outputCapacity;
        public int RecipeInputWood => WoodRequiredPerRecipe;
        public int RecipeOutputPlanks => PlanksProducedPerRecipe;
        public float ProcessingDuration => processingDuration;
        public bool IsProcessing => _isProcessing;
        public int CompletedRecipeCount => _completedRecipeCount;
        public string EndpointId => "wood_processor";
        public bool IsEndpointOperational => isActiveAndEnabled;
        public string ProductionBuildingId => EndpointId;
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
            RebuildProcessingWait();
        }

        public bool ApplyUpgradeLevels(int speed, int input, int output)
        {
            CaptureBaseTuning();
            _speedUpgradeLevel = M14ProductionUpgradeTuning.ClampLevel(speed);
            _inputUpgradeLevel = M14ProductionUpgradeTuning.ClampLevel(input);
            _outputUpgradeLevel = M14ProductionUpgradeTuning.ClampLevel(output);
            inputCapacity = _baseInputCapacity
                + M14ProductionUpgradeTuning.GetCapacityBonus(_baseInputCapacity, _inputUpgradeLevel);
            outputCapacity = _baseOutputCapacity
                + M14ProductionUpgradeTuning.GetCapacityBonus(_baseOutputCapacity, _outputUpgradeLevel);
            processingDuration = M15FactoryTuning.CalculateDuration(_baseProcessingDuration,
                _speedUpgradeLevel, _specialization, ProductionBuildingId, _districtLevel);
            RebuildProcessingWait();
            NotifyBufferChanged();
            return true;
        }

        private void CaptureBaseTuning()
        {
            if (_baseInputCapacity > 0) return;
            _baseInputCapacity = inputCapacity;
            _baseOutputCapacity = outputCapacity;
            _baseProcessingDuration = processingDuration;
        }

        private void OnEnable()
        {
            TryStartProcessing();
        }

        private void OnDisable()
        {
            bool hadInputReservation = _reservedInputCapacity > 0;
            InvalidateInputReservation();
            InvalidateIndustrialReservations();
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
                || _outputPlanks - _reservedIndustrialOutgoing <= 0
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

        public bool RestoreStableState(int inputWood, int outputPlanks)
        {
            if (inputWood < 0
                || inputWood > inputCapacity
                || outputPlanks < 0
                || outputPlanks > outputCapacity)
            {
                return false;
            }

            InvalidateInputReservation();
            InvalidateIndustrialReservations();
            StopProcessingAndReleaseReservation();
            _isInputTransferInProgress = false;
            _isOutputTransferInProgress = false;
            _inputWood = inputWood;
            _outputPlanks = outputPlanks;
            _completedRecipeCount = 0;
            NotifyBufferChanged();
            return true;
        }

        public int GetAvailable(ResourceType resourceType)
        {
            return resourceType == ResourceType.Plank
                ? Mathf.Max(0, _outputPlanks - _reservedIndustrialOutgoing)
                : 0;
        }

        public int GetAvailableCapacity(ResourceType resourceType)
        {
            return resourceType == ResourceType.Wood ? AvailableInputCapacity : 0;
        }

        public bool TryReserveOutgoing(
            ResourceType resourceType,
            int amount,
            out IndustrialResourceReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || resourceType != ResourceType.Plank
                || amount <= 0
                || _industrialOutgoing.IsValid
                || GetAvailable(resourceType) < amount)
            {
                return false;
            }

            _reservedIndustrialOutgoing = amount;
            _industrialOutgoing = CreateIndustrialReservation(
                IndustrialReservationKind.Outgoing,
                resourceType,
                amount);
            reservation = _industrialOutgoing;
            return true;
        }

        public bool TryReserveIncoming(
            ResourceType resourceType,
            int amount,
            out IndustrialResourceReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || resourceType != ResourceType.Wood
                || amount <= 0
                || _industrialIncoming.IsValid
                || AvailableInputCapacity < amount)
            {
                return false;
            }

            _reservedIndustrialIncoming = amount;
            _industrialIncoming = CreateIndustrialReservation(
                IndustrialReservationKind.Incoming,
                resourceType,
                amount);
            reservation = _industrialIncoming;
            return true;
        }

        public bool IsReservationValid(IndustrialResourceReservation reservation)
        {
            if (!isActiveAndEnabled
                || !ReferenceEquals(reservation.Endpoint, this)
                || reservation.Token == 0)
            {
                return false;
            }

            IndustrialResourceReservation active =
                reservation.Kind == IndustrialReservationKind.Incoming
                    ? _industrialIncoming
                    : _industrialOutgoing;
            return active.Token == reservation.Token
                   && active.ResourceType == reservation.ResourceType
                   && active.Amount == reservation.Amount;
        }

        public bool ReleaseReservation(IndustrialResourceReservation reservation)
        {
            if (!IsReservationValid(reservation))
            {
                return false;
            }

            ClearIndustrialReservation(reservation.Kind);
            return true;
        }

        public void FinalizeReservationForTransfer(
            IndustrialResourceReservation reservation)
        {
            if (!IsReservationValid(reservation))
            {
                throw new InvalidOperationException(
                    "Wood Processor logistics reservation is invalid.");
            }

            if (reservation.Kind == IndustrialReservationKind.Incoming)
            {
                _inputWood += reservation.Amount;
            }
            else
            {
                _outputPlanks -= reservation.Amount;
            }

            ClearIndustrialReservation(reservation.Kind);
        }

        public void PublishTransferCommitted(
            IndustrialResourceReservation reservation)
        {
            NotifyBufferChanged();
            TryStartProcessing();
        }

        private IndustrialResourceReservation CreateIndustrialReservation(
            IndustrialReservationKind kind,
            ResourceType resourceType,
            int amount)
        {
            _nextIndustrialToken++;
            if (_nextIndustrialToken == 0)
            {
                _nextIndustrialToken = 1;
            }

            return new IndustrialResourceReservation(
                this,
                _nextIndustrialToken,
                kind,
                resourceType,
                amount);
        }

        private void ClearIndustrialReservation(IndustrialReservationKind kind)
        {
            if (kind == IndustrialReservationKind.Incoming)
            {
                _reservedIndustrialIncoming = 0;
                _industrialIncoming = default;
            }
            else
            {
                _reservedIndustrialOutgoing = 0;
                _industrialOutgoing = default;
            }
        }

        private void InvalidateIndustrialReservations()
        {
            _reservedIndustrialIncoming = 0;
            _reservedIndustrialOutgoing = 0;
            _industrialIncoming = default;
            _industrialOutgoing = default;
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

            Persistence.M15FactoryRules.CompleteRecipe(_factoryProgress, out bool conserve);
            _inputWood -= WoodRequiredPerRecipe - (conserve ? 1 : 0);
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
