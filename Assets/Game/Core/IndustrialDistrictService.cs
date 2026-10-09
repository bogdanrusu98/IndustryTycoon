using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    public sealed class IndustrialDistrictService : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private Wallet wallet;
        [SerializeField] private Collider playerCollider;
        [SerializeField] private Collider entryTrigger;
        [SerializeField] private GameObject gateBlocker;
        [SerializeField] private PurchasePad assemblyPurchasePad;
        [SerializeField] private AssemblyPlant assemblyPlant;
        [SerializeField] private MonoBehaviour[] upgradeableBuildings;
        [SerializeField] private IndustrialLogisticsNetwork logisticsNetwork;

        private bool _districtEntered;
        private bool _subscribed;
        private M15FactorySaveData _factoryScaling = M15FactorySaveData.CreateFresh();

        public event Action StateChanged;
        public bool IsGateOpen => progression != null
                                  && progression.EmpireLevel >= IndustrialDistrictTease.ReadyEmpireLevel;
        public bool DistrictEntered => _districtEntered;
        public bool AssemblyPlantUnlocked => assemblyPurchasePad != null
                                             && assemblyPurchasePad.IsCompleted;
        public AssemblyPlant AssemblyPlant => assemblyPlant;
        public int DistrictLevel => _factoryScaling.industrialDistrictLevel;
        public int EffectiveEmpireLevel => Math.Max(progression?.EmpireLevel ?? 1,
            M15FactoryRules.GetEmpireLevel(_factoryScaling));
        public M15FactorySaveData CaptureFactoryScalingState()
        {
            if (upgradeableBuildings != null) for(int i=0;i<upgradeableBuildings.Length;i++)
                if(upgradeableBuildings[i] is IProductionUpgradeable building && upgradeableBuildings[i] is IFactorySpecializable specialized)
                { var record=_factoryScaling.GetFactory(building.ProductionBuildingId); if(record!=null){record.efficiencyCompletedRemainder=specialized.EfficiencyCompletedRemainder;record.completedSpecializedCycles=specialized.CompletedSpecializedCycles;} }
            M15FactoryRules.RecalculateDistrictLevel(_factoryScaling);
            M15FactorySaveValidator.TryNormalize(_factoryScaling, out var copy, out _);
            return copy ?? M15FactorySaveData.CreateFresh();
        }

        public bool RestoreFactoryScalingState(M15FactorySaveData data)
        {
            if (!M15FactorySaveValidator.TryNormalize(data, out var normalized, out _)) return false;
            _factoryScaling = normalized; ApplyFactoryModifiers();
            SynchronizeProgression(false);
            StateChanged?.Invoke(); return true;
        }

        public int GetSpecializationCost(string buildingId)
        {
            return M15FactoryTuning.TryGet(buildingId, out var definition)
                ? definition.Cost : int.MaxValue;
        }

        public ProductionStatus GetProductionStatus(string buildingId)
        {
            IProductionUpgradeable building = FindBuilding(buildingId);
            bool logisticsExpected = false;
            IndustrialLogisticsRoute[] routes = logisticsNetwork?.Routes;
            if (routes != null) for (int i=0;i<routes.Length;i++)
                logisticsExpected |= routes[i] != null && routes[i].RouteEnabled
                    && string.Equals(routes[i].SourceEndpointId, buildingId, StringComparison.Ordinal);
            if (building is WoodProcessor wood)
                return ProductionStatusRules.Derive(IsBuildingUnlocked(buildingId), wood.isActiveAndEnabled,
                    wood.IsProcessing, wood.InputWood >= wood.RecipeInputWood,
                    wood.OutputPlanks + wood.RecipeOutputPlanks <= wood.OutputCapacity, logisticsExpected);
            if (building is PackingStation packing)
                return ProductionStatusRules.Derive(IsBuildingUnlocked(buildingId), packing.isActiveAndEnabled,
                    packing.IsProcessing, packing.InputPlanks >= packing.RecipeInputPlanks,
                    packing.OutputCrates + packing.RecipeOutputCrates <= packing.OutputCapacity, logisticsExpected);
            if (building is ToolWorkshop workshop)
                return ProductionStatusRules.Derive(IsBuildingUnlocked(buildingId), workshop.isActiveAndEnabled,
                    workshop.IsProcessing, !workshop.IsStarved, !workshop.IsOutputFull, logisticsExpected);
            if (building is AssemblyPlant plant)
                return ProductionStatusRules.Derive(IsBuildingUnlocked(buildingId), plant.isActiveAndEnabled,
                    plant.IsProcessing, plant.InputIronBars >= 2 && plant.InputTools >= 1,
                    plant.OutputMachineParts < plant.OutputCapacity, logisticsExpected);
            return ProductionStatus.LockedInactive;
        }

        public bool TryPurchaseSpecialization(string buildingId, FactorySpecialization specialization)
        {
            if (wallet == null || !IsBuildingUnlocked(buildingId)
                || !M15FactoryTuning.TryGet(buildingId, out var definition)
                || specialization == FactorySpecialization.None
                || _factoryScaling.GetFactory(buildingId)?.specialization != FactorySpecialization.None
                || wallet.Balance < definition.Cost || wallet.SpendUpTo(definition.Cost) != definition.Cost)
                return false;
            if (!M15FactoryRules.TryChoose(_factoryScaling, buildingId, specialization))
            { wallet.Deposit(definition.Cost); return false; }
            ApplyFactoryModifiers();
            SynchronizeProgression(true);
            StateChanged?.Invoke(); return true;
        }

        public void RecordUpgradedRouteTransport(int units)
        {
            if (units <= 0) return;
            _factoryScaling.upgradedRouteUnitsTransported = Math.Min(long.MaxValue,
                _factoryScaling.upgradedRouteUnitsTransported + units);
            SynchronizeProgression(true);
            StateChanged?.Invoke();
        }

        public bool MarkOptimizationObjectiveCompleted()
        {
            if (_factoryScaling.optimizationObjectiveCompleted)
            {
                return false;
            }

            _factoryScaling.optimizationObjectiveCompleted = true;
            SynchronizeProgression(true);
            StateChanged?.Invoke();
            return true;
        }

        private void ApplyFactoryModifiers()
        {
            if (upgradeableBuildings == null) return;
            for (int i=0;i<upgradeableBuildings.Length;i++)
                if (upgradeableBuildings[i] is IProductionUpgradeable building
                    && upgradeableBuildings[i] is IFactorySpecializable specialized)
                    specialized.ApplyFactorySpecialization(
                        _factoryScaling.GetFactory(building.ProductionBuildingId)?.specialization
                        ?? FactorySpecialization.None, _factoryScaling.industrialDistrictLevel);
            for (int i=0;upgradeableBuildings!=null&&i<upgradeableBuildings.Length;i++)
                if(upgradeableBuildings[i] is IProductionUpgradeable building && upgradeableBuildings[i] is IFactorySpecializable specialized)
                {var r=_factoryScaling.GetFactory(building.ProductionBuildingId);specialized.RestoreFactoryProgress(r?.efficiencyCompletedRemainder??0,r?.completedSpecializedCycles??0);}
            IndustrialLogisticsRoute[] routes = logisticsNetwork?.Routes;
            if (routes != null) for (int i=0;i<routes.Length;i++)
                routes[i]?.ApplyIndustrialDistrictLevel(_factoryScaling.industrialDistrictLevel);
        }

        public int GetProductionUpgradeCost(string buildingId,
            ProductionUpgradeCategory category)
        {
            IProductionUpgradeable building = FindBuilding(buildingId);
            if (building == null) return int.MaxValue;
            int level = category == ProductionUpgradeCategory.Speed
                ? building.SpeedUpgradeLevel
                : category == ProductionUpgradeCategory.InputCapacity
                    ? building.InputCapacityUpgradeLevel
                    : building.OutputCapacityUpgradeLevel;
            return M14ProductionUpgradeTuning.GetCost(category, level + 1);
        }

        private void OnEnable()
        {
            Subscribe(true);
            SynchronizeAccess();
        }

        private void OnDisable() => Subscribe(false);

        private void OnTriggerEnter(Collider other)
        {
            if (other != playerCollider || !IsGateOpen || _districtEntered) return;
            _districtEntered = true;
            progression?.RecordIndustrialDistrictEntered();
            SynchronizeAccess();
            StateChanged?.Invoke();
        }

        public bool TryPurchaseProductionUpgrade(string buildingId,
            ProductionUpgradeCategory category)
        {
            IProductionUpgradeable building = FindBuilding(buildingId);
            if (building == null || wallet == null || !IsBuildingUnlocked(buildingId)) return false;
            int current = category == ProductionUpgradeCategory.Speed
                ? building.SpeedUpgradeLevel
                : category == ProductionUpgradeCategory.InputCapacity
                    ? building.InputCapacityUpgradeLevel
                    : building.OutputCapacityUpgradeLevel;
            int cost = M14ProductionUpgradeTuning.GetCost(category, current + 1);
            if (cost == int.MaxValue || wallet.Balance < cost || wallet.SpendUpTo(cost) != cost)
                return false;
            int speed = building.SpeedUpgradeLevel;
            int input = building.InputCapacityUpgradeLevel;
            int output = building.OutputCapacityUpgradeLevel;
            if (category == ProductionUpgradeCategory.Speed) speed++;
            else if (category == ProductionUpgradeCategory.InputCapacity) input++;
            else output++;
            if (!building.ApplyUpgradeLevels(speed, input, output))
            {
                wallet.Deposit(cost);
                return false;
            }
            progression?.RecordProductionUpgradePurchased();
            InternalAnalytics.Publish("production_upgrade_purchased", buildingId + ":" + category);
            SynchronizeProgression(true);
            StateChanged?.Invoke();
            return true;
        }

        public bool TryPurchaseRouteUpgrade(string routeId, bool capacity)
        {
            IndustrialLogisticsRoute route = FindRoute(routeId);
            if (route == null || wallet == null) return false;
            int cost = route.GetNextUpgradeCost(capacity);
            if (cost == int.MaxValue || wallet.Balance < cost || wallet.SpendUpTo(cost) != cost)
                return false;
            if (!route.TryApplyNextUpgrade(capacity))
            {
                wallet.Deposit(cost);
                return false;
            }
            progression?.RecordLogisticsUpgradePurchased();
            InternalAnalytics.Publish("logistics_upgrade_purchased", routeId + ":" + (capacity ? "capacity" : "speed"));
            SynchronizeProgression(true);
            StateChanged?.Invoke();
            return true;
        }

        public M14IndustrialSaveData CapturePersistentState()
        {
            M14IndustrialSaveData data = M14IndustrialSaveData.CreateFresh();
            data.districtEntered = _districtEntered;
            if (assemblyPurchasePad != null)
            {
                data.assemblyPlantPurchase.paidCash = assemblyPurchasePad.TotalCost
                                                      - assemblyPurchasePad.RemainingCost;
                data.assemblyPlantPurchase.completed = assemblyPurchasePad.IsCompleted;
            }
            if (assemblyPlant != null)
            {
                data.assemblyInputIronBars = assemblyPlant.InputIronBars
                                               + assemblyPlant.ProcessingIronBars;
                data.assemblyInputTools = assemblyPlant.InputTools
                                           + assemblyPlant.ProcessingTools;
                data.assemblyOutputMachineParts = assemblyPlant.OutputMachineParts;
            }
            if (upgradeableBuildings != null)
            {
                for (int i = 0; i < upgradeableBuildings.Length; i++)
                {
                    if (!(upgradeableBuildings[i] is IProductionUpgradeable building)) continue;
                    M14BuildingUpgradeSaveRecord record = data.GetBuildingUpgrade(
                        building.ProductionBuildingId);
                    if (record == null) continue;
                    record.speedLevel = building.SpeedUpgradeLevel;
                    record.inputCapacityLevel = building.InputCapacityUpgradeLevel;
                    record.outputCapacityLevel = building.OutputCapacityUpgradeLevel;
                }
            }
            return data;
        }

        public bool RestorePersistentState(M14IndustrialSaveData data)
        {
            if (!M14IndustrialSaveValidator.TryNormalize(data, out data, out _)) return false;
            _districtEntered = data.districtEntered;
            // Reconcile in-flight Assembly ownership into an empty stable boundary
            // before capacities can legitimately decrease during reset/load.
            assemblyPlant?.RestoreStableState(0, 0, 0);
            if (upgradeableBuildings != null)
            {
                for (int i = 0; i < upgradeableBuildings.Length; i++)
                {
                    if (!(upgradeableBuildings[i] is IProductionUpgradeable building)) continue;
                    M14BuildingUpgradeSaveRecord record = data.GetBuildingUpgrade(
                        building.ProductionBuildingId);
                    building.ApplyUpgradeLevels(record?.speedLevel ?? 0,
                        record?.inputCapacityLevel ?? 0, record?.outputCapacityLevel ?? 0);
                }
            }
            assemblyPurchasePad?.RestorePaidAmount(
                data.assemblyPlantPurchase.paidCash,
                data.assemblyPlantPurchase.completed);
            if (assemblyPlant != null)
            {
                assemblyPlant.gameObject.SetActive(data.assemblyPlantPurchase.completed);
                assemblyPlant.RestoreStableState(data.assemblyInputIronBars,
                    data.assemblyInputTools, data.assemblyOutputMachineParts);
            }
            SynchronizeAccess();
            SynchronizeProgression(false);
            StateChanged?.Invoke();
            return true;
        }

        private void HandleProgressionChanged() => SynchronizeAccess();

        private void HandleAssemblyPurchased()
        {
            if (!_districtEntered) return;
            assemblyPlant?.gameObject.SetActive(true);
            progression?.RecordAssemblyPlantUnlocked();
            StateChanged?.Invoke();
        }

        private void HandleRecipeCompleted(int amount)
        {
            progression?.RecordMachinePartsProduced(amount);
            SynchronizeProgression(true);
            StateChanged?.Invoke();
        }

        private void SynchronizeProgression(bool publishTransitions)
        {
            progression?.SynchronizeIndustrialContext(
                CapturePersistentState(),
                CaptureFactoryScalingState(),
                publishTransitions);
        }

        private void HandleRouteTransfer(IndustrialLogisticsRoute route,
            ResourceType resourceType, int amount)
        {
            if (route != null && (route.SpeedUpgradeLevel > 0 || route.CapacityUpgradeLevel > 0))
                RecordUpgradedRouteTransport(amount);
        }

        private void SynchronizeAccess()
        {
            if (gateBlocker != null) gateBlocker.SetActive(!IsGateOpen);
            if (entryTrigger != null) entryTrigger.enabled = IsGateOpen;
            if (assemblyPurchasePad != null)
                assemblyPurchasePad.SetAvailable(_districtEntered);
        }

        private bool IsBuildingUnlocked(string id)
        {
            if (id == AssemblyPlant.StableEndpointId) return AssemblyPlantUnlocked;
            if (progression == null) return false;
            if (id == "wood_processor")
                return progression.GetFlag(ProgressFlagId.ProcessorUnlocked);
            if (id == "iron_smelter")
                return progression.GetFlag(ProgressFlagId.SmelterUnlocked);
            if (id == "tool_workshop")
                return progression.GetMetric(ProgressMetricId.ToolWorkshopUnlocked) > 0L;
            return false;
        }

        private IProductionUpgradeable FindBuilding(string id)
        {
            if (upgradeableBuildings == null) return null;
            for (int i = 0; i < upgradeableBuildings.Length; i++)
                if (upgradeableBuildings[i] is IProductionUpgradeable building
                    && string.Equals(building.ProductionBuildingId, id,
                        StringComparison.Ordinal)) return building;
            return null;
        }

        private IndustrialLogisticsRoute FindRoute(string id)
        {
            IndustrialLogisticsRoute[] routes = logisticsNetwork?.Routes;
            if (routes == null) return null;
            for (int i = 0; i < routes.Length; i++)
                if (routes[i] != null && string.Equals(routes[i].RouteId, id,
                    StringComparison.Ordinal)) return routes[i];
            return null;
        }

        private void Subscribe(bool subscribe)
        {
            if (_subscribed == subscribe) return;
            _subscribed = subscribe;
            if (progression != null)
            {
                if (subscribe) progression.StateChanged += HandleProgressionChanged;
                else progression.StateChanged -= HandleProgressionChanged;
            }
            if (assemblyPurchasePad != null)
            {
                if (subscribe) assemblyPurchasePad.Completed += HandleAssemblyPurchased;
                else assemblyPurchasePad.Completed -= HandleAssemblyPurchased;
            }
            if (assemblyPlant != null)
            {
                if (subscribe) assemblyPlant.RecipeCompleted += HandleRecipeCompleted;
                else assemblyPlant.RecipeCompleted -= HandleRecipeCompleted;
            }
            IndustrialLogisticsRoute[] routes = logisticsNetwork?.Routes;
            if (routes != null) for (int i=0;i<routes.Length;i++) if(routes[i]!=null)
            {
                if(subscribe) routes[i].TransferCommitted += HandleRouteTransfer;
                else routes[i].TransferCommitted -= HandleRouteTransfer;
            }
        }
    }
}
