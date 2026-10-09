using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;
using IndustryTycoon.Player;
using UnityEngine;
using IndustryTycoon.Monetization;

namespace IndustryTycoon.Processing
{
    public sealed class AssemblyPlant : MonoBehaviour, IIndustrialResourceEndpoint,
        IProductionUpgradeable, IFactorySpecializable
    {
        public const string StableEndpointId = "assembly_plant";
        private const int IronBarIndex = 0;
        private const int ToolIndex = 1;
        private const int IronBarsPerRecipe = 2;
        private const int ToolsPerRecipe = 1;
        private const int OutputPerRecipe = 1;

        [SerializeField, Min(2)] private int ironBarInputCapacity = 24;
        [SerializeField, Min(1)] private int toolInputCapacity = 12;
        [SerializeField, Min(1)] private int outputCapacity = 12;
        [SerializeField, Min(0.02f)] private float processingDuration = 3f;

        private readonly int[] _inputs = new int[2];
        private readonly int[] _capacities = new int[2];
        private BufferedRecipeTransaction _transaction;
        private Coroutine _coroutine;
        private readonly ProductionWaitCache _processingWaitCache = new ProductionWaitCache();
        private int _output;
        private bool _processing;
        private bool _transferring;
        private int _speedLevel;
        private int _inputLevel;
        private int _outputLevel;
        private FactorySpecialization _specialization;
        private int _districtLevel = 1;
        private readonly Persistence.M15FactoryRecord _factoryProgress = new Persistence.M15FactoryRecord { buildingId = StableEndpointId };
        private int _reservedIncomingBars;
        private int _reservedIncomingTools;
        private int _reservedOutgoing;
        private uint _nextToken;
        private IndustrialResourceReservation _incomingBars;
        private IndustrialResourceReservation _incomingTools;
        private IndustrialResourceReservation _outgoing;

        public event Action StateChanged;
        public event Action<int> RecipeCompleted;

        public string EndpointId => StableEndpointId;
        public bool IsEndpointOperational => isActiveAndEnabled;
        public string ProductionBuildingId => StableEndpointId;
        public int SpeedUpgradeLevel => _speedLevel;
        public int InputCapacityUpgradeLevel => _inputLevel;
        public int OutputCapacityUpgradeLevel => _outputLevel;
        public FactorySpecialization Specialization => _specialization;
        public int IndustrialDistrictLevel => _districtLevel;
        public int EfficiencyCompletedRemainder => _factoryProgress.efficiencyCompletedRemainder;
        public long CompletedSpecializedCycles => _factoryProgress.completedSpecializedCycles;
        public void ApplyFactorySpecialization(FactorySpecialization specialization, int districtLevel)
        { _specialization=specialization;_factoryProgress.specialization=specialization;_districtLevel=Mathf.Clamp(districtLevel,1,3);StateChanged?.Invoke(); }
        public void RestoreFactoryProgress(int remainder,long cycles){_factoryProgress.efficiencyCompletedRemainder=Mathf.Clamp(remainder,0,4);_factoryProgress.completedSpecializedCycles=Math.Max(0,cycles);}
        public int InputIronBars => _inputs[IronBarIndex];
        public int InputTools => _inputs[ToolIndex];
        public int OutputMachineParts => _output;
        public int IronBarInputCapacity => EffectiveCapacity(ironBarInputCapacity, _inputLevel);
        public int ToolInputCapacity => EffectiveCapacity(toolInputCapacity, _inputLevel);
        public int OutputCapacity => EffectiveCapacity(outputCapacity, _outputLevel);
        public float ProcessingDuration => M15FactoryTuning.CalculateDuration(processingDuration,
            _speedLevel, _specialization, ProductionBuildingId, _districtLevel);
        public bool IsProcessing => _processing;
        public int ProcessingIronBars => Owned(IronBarIndex);
        public int ProcessingTools => Owned(ToolIndex);
        public int AvailableIronBarInputCapacity => Mathf.Max(0,
            IronBarInputCapacity - InputIronBars - ProcessingIronBars - _reservedIncomingBars);
        public int AvailableToolInputCapacity => Mathf.Max(0,
            ToolInputCapacity - InputTools - ProcessingTools - _reservedIncomingTools);

        private void Awake() => EnsureRuntime();
        private void OnEnable() { EnsureRuntime(); TryStartProcessing(); }
        private void OnDisable()
        {
            StopAndRefund();
            ClearReservations();
        }

        public bool ApplyUpgradeLevels(int speed, int input, int output)
        {
            speed = M14ProductionUpgradeTuning.ClampLevel(speed);
            input = M14ProductionUpgradeTuning.ClampLevel(input);
            output = M14ProductionUpgradeTuning.ClampLevel(output);
            int nextBars = EffectiveCapacity(ironBarInputCapacity, input);
            int nextTools = EffectiveCapacity(toolInputCapacity, input);
            int nextOutput = EffectiveCapacity(outputCapacity, output);
            if (InputIronBars + ProcessingIronBars > nextBars
                || InputTools + ProcessingTools > nextTools
                || _output + (_transaction?.ReservedOutputCapacity ?? 0) > nextOutput)
            {
                return false;
            }

            _speedLevel = speed;
            _inputLevel = input;
            _outputLevel = output;
            EnsureRuntime();
            StateChanged?.Invoke();
            TryStartProcessing();
            return true;
        }

        public int GetAvailableInputCapacity(ResourceType type)
        {
            return type == ResourceType.IronBar
                ? AvailableIronBarInputCapacity
                : type == ResourceType.Tool ? AvailableToolInputCapacity : 0;
        }

        public bool TryTransferInputFrom(CarryStack carry, ResourceType type)
        {
            int index = type == ResourceType.IronBar ? IronBarIndex
                : type == ResourceType.Tool ? ToolIndex : -1;
            if (!isActiveAndEnabled || _transferring || carry == null || index < 0
                || GetAvailableInputCapacity(type) <= 0 || !carry.CanRemove(type, 1))
            {
                return false;
            }

            _transferring = true;
            bool committed = false;
            try
            {
                if (!carry.TryRemove(type, 1)) return false;
                _inputs[index]++;
                committed = true;
            }
            finally { _transferring = false; }
            if (!committed) return false;
            StateChanged?.Invoke();
            TryStartProcessing();
            return true;
        }

        public bool TryTransferOutputTo(CarryStack carry)
        {
            if (!isActiveAndEnabled || _transferring || carry == null
                || _output - _reservedOutgoing <= 0
                || !carry.TryReserveCapacity(ResourceType.MachinePart, 1))
            {
                return false;
            }

            _transferring = true;
            _output--;
            bool committed = carry.TryCommitReservedAdd(ResourceType.MachinePart, 1);
            if (!committed)
            {
                _output++;
                carry.ReleaseReservedCapacity(1);
            }
            _transferring = false;
            if (!committed) return false;
            StateChanged?.Invoke();
            TryStartProcessing();
            return true;
        }

        public bool RestoreStableState(int bars, int tools, int parts)
        {
            if (bars < 0 || bars > IronBarInputCapacity || tools < 0
                || tools > ToolInputCapacity || parts < 0 || parts > OutputCapacity)
            {
                return false;
            }

            EnsureRuntime();
            StopAndRefund();
            ClearReservations();
            _inputs[IronBarIndex] = bars;
            _inputs[ToolIndex] = tools;
            _output = parts;
            StateChanged?.Invoke();
            return true;
        }

        public bool CompleteProcessingImmediatelyForTests()
        {
            if (!_processing && !TryStartProcessing()) return false;
            if (_coroutine != null) { StopCoroutine(_coroutine); _coroutine = null; }
            return CompleteRecipe();
        }

        public bool TryStartProcessing()
        {
            EnsureRuntime();
            if (!Application.isPlaying || !isActiveAndEnabled || _processing
                || _transaction.HasReservation) return false;
            if (!_transaction.TryBegin(_inputs, _output, OutputCapacity)) return false;
            _processing = true;
            _coroutine = StartCoroutine(Process());
            StateChanged?.Invoke();
            return true;
        }

        private IEnumerator Process()
        {
            yield return _processingWaitCache.Get(ProcessingDuration);
            _coroutine = null;
            CompleteRecipe();
        }

        private bool CompleteRecipe()
        {
            if (!_processing || !_transaction.TryCommit(ref _output, OutputCapacity))
            {
                StopAndRefund();
                return false;
            }
            _processing = false;
            Persistence.M15FactoryRules.CompleteRecipe(_factoryProgress, out bool conserve);
            if (conserve) _inputs[IronBarIndex] = Mathf.Min(IronBarInputCapacity, _inputs[IronBarIndex] + 1);
            RecipeCompleted?.Invoke(OutputPerRecipe);
            StateChanged?.Invoke();
            TryStartProcessing();
            return true;
        }

        public int GetAvailable(ResourceType type) => type == ResourceType.MachinePart
            ? Mathf.Max(0, _output - _reservedOutgoing) : 0;

        public int GetAvailableCapacity(ResourceType type) => GetAvailableInputCapacity(type);

        public bool TryReserveOutgoing(ResourceType type, int amount,
            out IndustrialResourceReservation reservation)
        {
            reservation = default;
            if (type != ResourceType.MachinePart || amount <= 0 || _outgoing.IsValid
                || GetAvailable(type) < amount) return false;
            _reservedOutgoing = amount;
            _outgoing = NewReservation(IndustrialReservationKind.Outgoing, type, amount);
            reservation = _outgoing;
            return true;
        }

        public bool TryReserveIncoming(ResourceType type, int amount,
            out IndustrialResourceReservation reservation)
        {
            reservation = default;
            if ((type != ResourceType.IronBar && type != ResourceType.Tool)
                || amount <= 0 || GetAvailableCapacity(type) < amount) return false;
            if (type == ResourceType.IronBar)
            {
                if (_incomingBars.IsValid) return false;
                _reservedIncomingBars = amount;
                _incomingBars = NewReservation(IndustrialReservationKind.Incoming, type, amount);
                reservation = _incomingBars;
            }
            else
            {
                if (_incomingTools.IsValid) return false;
                _reservedIncomingTools = amount;
                _incomingTools = NewReservation(IndustrialReservationKind.Incoming, type, amount);
                reservation = _incomingTools;
            }
            return true;
        }

        public bool IsReservationValid(IndustrialResourceReservation r)
        {
            if (!isActiveAndEnabled || !ReferenceEquals(r.Endpoint, this) || r.Token == 0)
                return false;
            IndustrialResourceReservation active = r.Kind == IndustrialReservationKind.Outgoing
                ? _outgoing : r.ResourceType == ResourceType.IronBar ? _incomingBars : _incomingTools;
            return active.Token == r.Token && active.Amount == r.Amount
                   && active.ResourceType == r.ResourceType;
        }

        public bool ReleaseReservation(IndustrialResourceReservation r)
        {
            if (!IsReservationValid(r)) return false;
            ClearReservation(r);
            return true;
        }

        public void FinalizeReservationForTransfer(IndustrialResourceReservation r)
        {
            if (!IsReservationValid(r)) throw new InvalidOperationException("Invalid Assembly Plant reservation.");
            if (r.Kind == IndustrialReservationKind.Outgoing) _output -= r.Amount;
            else _inputs[r.ResourceType == ResourceType.IronBar ? IronBarIndex : ToolIndex] += r.Amount;
            ClearReservation(r);
        }

        public void PublishTransferCommitted(IndustrialResourceReservation r)
        {
            StateChanged?.Invoke();
            TryStartProcessing();
        }

        private IndustrialResourceReservation NewReservation(IndustrialReservationKind kind,
            ResourceType type, int amount)
        {
            if (++_nextToken == 0) _nextToken = 1;
            return new IndustrialResourceReservation(this, _nextToken, kind, type, amount);
        }

        private void ClearReservation(IndustrialResourceReservation r)
        {
            if (r.Kind == IndustrialReservationKind.Outgoing)
            { _outgoing = default; _reservedOutgoing = 0; }
            else if (r.ResourceType == ResourceType.IronBar)
            { _incomingBars = default; _reservedIncomingBars = 0; }
            else { _incomingTools = default; _reservedIncomingTools = 0; }
        }

        private void ClearReservations()
        {
            _incomingBars = default; _incomingTools = default; _outgoing = default;
            _reservedIncomingBars = 0; _reservedIncomingTools = 0; _reservedOutgoing = 0;
        }

        private void StopAndRefund()
        {
            if (_coroutine != null) { StopCoroutine(_coroutine); _coroutine = null; }
            if (_transaction != null && _transaction.HasReservation)
                _transaction.TryRollback(_inputs, _capacities);
            _processing = false;
        }

        private void EnsureRuntime()
        {
            _capacities[IronBarIndex] = IronBarInputCapacity;
            _capacities[ToolIndex] = ToolInputCapacity;
            if (_transaction == null)
                _transaction = new BufferedRecipeTransaction(
                    new[] { IronBarsPerRecipe, ToolsPerRecipe }, OutputPerRecipe);
        }

        private int Owned(int index) => _transaction?.GetOwnedInput(index) ?? 0;
        private static int EffectiveCapacity(int baseValue, int level) => baseValue
            + M14ProductionUpgradeTuning.GetCapacityBonus(baseValue, level);

        private void OnValidate()
        {
            ironBarInputCapacity = Mathf.Max(IronBarsPerRecipe, ironBarInputCapacity);
            toolInputCapacity = Mathf.Max(ToolsPerRecipe, toolInputCapacity);
            outputCapacity = Mathf.Max(OutputPerRecipe, outputCapacity);
            processingDuration = Mathf.Max(0.02f, processingDuration);
        }
    }
}
