using System;
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

        public event Action StateChanged;
        public bool IsGateOpen => progression != null
                                  && progression.EmpireLevel >= IndustrialDistrictTease.ReadyEmpireLevel;
        public bool DistrictEntered => _districtEntered;
        public bool AssemblyPlantUnlocked => assemblyPurchasePad != null
                                             && assemblyPurchasePad.IsCompleted;
        public AssemblyPlant AssemblyPlant => assemblyPlant;

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
            StateChanged?.Invoke();
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
        }
    }
}
