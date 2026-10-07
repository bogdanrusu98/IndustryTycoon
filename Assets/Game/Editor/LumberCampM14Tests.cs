using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    public static class LumberCampM14Tests
    {
        private const long Now = 2000000000L;
        private static int _tests;
        private static int _assertions;

        [MenuItem("Industry Tycoon/Prototype/Run M14 Advanced Manufacturing Tests")]
        private static void RunFromMenu() => RunAllForValidator();

        public static void RunFromCommandLine()
        {
            try { RunAllForValidator(); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void RunM9ThroughM14FromCommandLine()
        {
            try
            {
                LumberCampM9PersistenceTests.RunAllForValidator();
                LumberCampM10ProgressionTests.RunAllForValidator();
                LumberCampM11Tests.RunAllForValidator();
                LumberCampM12Tests.RunAllForValidator();
                LumberCampM13Tests.RunAllForValidator();
                RunAllForValidator();
                Debug.Log("M9-M14 deterministic regression PASS.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        public static void RunAllForValidator()
        {
            _tests = _assertions = 0;
            Run("atomic 2 Bars + 1 Tool recipe and full buffers", TestAtomicRecipe);
            Run("manual Assembly transport", TestManualTransport);
            Run("Warehouse routes into Assembly", TestWarehouseRoutes);
            Run("Machine Part sale value and ownership", TestMachinePartSale);
            Run("production upgrades and persistence", TestProductionUpgrades);
            Run("route upgrades reset active transfer safely", TestRouteUpgradeSafety);
            Run("offline production and logistics use upgrades", TestOffline);
            Run("locked contract and Daily/Weekly filtering", TestFiltering);
            Run("Empire levels 11 through 15 are ordered", TestEmpireLevels);
            Run("v5 to v6 migration preserves M13 data", TestMigration);
            Run("v6 save/load/reset clears M14", TestRoundTripAndReset);
            Debug.Log($"M14 deterministic tests PASS: {_tests} tests, {_assertions} assertions.");
        }

        private static void TestAtomicRecipe()
        {
            var transaction = new BufferedRecipeTransaction(new[] { 2, 1 }, 1);
            int[] insufficient = { 2, 0 };
            Require(!transaction.TryBegin(insufficient, 0, 12)
                    && insufficient[0] == 2 && insufficient[1] == 0,
                "An insufficient recipe partially consumed inputs.");
            int[] fullOutput = { 2, 1 };
            Require(!transaction.TryBegin(fullOutput, 12, 12)
                    && fullOutput[0] == 2 && fullOutput[1] == 1,
                "A full output buffer consumed recipe inputs.");
            int[] valid = { 2, 1 };
            int output = 11;
            Require(transaction.TryBegin(valid, output, 12)
                    && valid[0] == 0 && valid[1] == 0
                    && transaction.TryCommit(ref output, 12) && output == 12,
                "The valid multi-input recipe did not commit atomically.");
        }

        private static void TestManualTransport()
        {
            GameObject root = new GameObject("M14 Manual");
            try
            {
                AssemblyPlant plant = root.AddComponent<AssemblyPlant>();
                CarryStack carry = root.AddComponent<CarryStack>();
                Require(carry.TryAdd(ResourceType.IronBar, 2)
                        && plant.TryTransferInputFrom(carry, ResourceType.IronBar)
                        && plant.TryTransferInputFrom(carry, ResourceType.IronBar),
                    "Manual Iron Bar input failed.");
                Require(carry.TryAdd(ResourceType.Tool, 1)
                        && plant.TryTransferInputFrom(carry, ResourceType.Tool)
                        && plant.InputIronBars == 2 && plant.InputTools == 1,
                    "Manual Tool input failed or crossed buffers.");
                Require(plant.RestoreStableState(0, 0, 1)
                        && plant.TryTransferOutputTo(carry)
                        && carry.GetAmount(ResourceType.MachinePart) == 1,
                    "Manual Machine Part output lost ownership.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestWarehouseRoutes()
        {
            GameObject root = new GameObject("M14 Warehouse Routes");
            try
            {
                CentralWarehouse warehouse = root.AddComponent<CentralWarehouse>();
                AssemblyPlant plant = new GameObject("Plant").AddComponent<AssemblyPlant>();
                plant.transform.SetParent(root.transform);
                int[] amounts = new int[7];
                int[] capacities = { 60, 60, 60, 60, 60, 60, 60 };
                amounts[(int)ResourceType.IronBar] = 2;
                amounts[(int)ResourceType.Tool] = 1;
                Require(warehouse.RestoreStableState(amounts, capacities), "Warehouse setup failed.");
                IndustrialLogisticsRoute bars = NewRoute(root, warehouse, plant,
                    ResourceType.IronBar, "bars");
                IndustrialLogisticsRoute tools = NewRoute(root, warehouse, plant,
                    ResourceType.Tool, "tools");
                bars.Tick(2f); bars.Tick(2f); tools.Tick(2f);
                Require(warehouse.GetAmount(ResourceType.IronBar) == 0
                        && warehouse.GetAmount(ResourceType.Tool) == 0
                        && plant.InputIronBars == 2 && plant.InputTools == 1,
                    "Warehouse logistics did not conserve Assembly inputs.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestMachinePartSale()
        {
            GameObject root = new GameObject("M14 Sale");
            try
            {
                CarryStack carry = root.AddComponent<CarryStack>();
                CashPile pile = root.AddComponent<CashPile>();
                GameObject saleObject = new GameObject("Sale");
                saleObject.transform.SetParent(root.transform);
                saleObject.AddComponent<BoxCollider>().isTrigger = true;
                SalePoint sale = saleObject.AddComponent<SalePoint>();
                SetReference(sale, "carryStack", carry);
                SetReference(sale, "cashPile", pile);
                Require(sale.GetUnitValue(ResourceType.MachinePart) == 250
                        && carry.TryAdd(ResourceType.MachinePart, 1)
                        && sale.TryUnloadOne()
                        && pile.StoredCash == 250 && carry.TotalAmount == 0,
                    "Machine Part sale did not use the existing economy transaction.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestProductionUpgrades()
        {
            GameObject root = new GameObject("M14 Upgrade");
            try
            {
                WoodProcessor processor = root.AddComponent<WoodProcessor>();
                Require(processor.ApplyUpgradeLevels(2, 1, 3)
                        && processor.ProcessingDuration < 1.1f
                        && processor.InputCapacity == 36
                        && processor.OutputCapacity == 30,
                    "Local Wood Processor upgrade tuning was not applied.");
                M9SaveData data = M9SaveData.CreateFresh(Now);
                M14BuildingUpgradeSaveRecord record = data.industrial.GetBuildingUpgrade(
                    "wood_processor");
                record.speedLevel = 2; record.inputCapacityLevel = 1;
                record.outputCapacityLevel = 3;
                Require(RoundTrip(data, out M9SaveData loaded)
                        && loaded.industrial.GetBuildingUpgrade("wood_processor").speedLevel == 2
                        && loaded.industrial.GetBuildingUpgrade("wood_processor").outputCapacityLevel == 3,
                    "Production upgrade levels were not persisted.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestRouteUpgradeSafety()
        {
            GameObject root = new GameObject("M14 Route Upgrade");
            try
            {
                CentralWarehouse source = root.AddComponent<CentralWarehouse>();
                CentralWarehouse destination = new GameObject("Destination")
                    .AddComponent<CentralWarehouse>();
                destination.transform.SetParent(root.transform);
                int[] capacities = { 10, 10, 10, 10, 10, 10, 10 };
                int[] amounts = new int[7]; amounts[(int)ResourceType.Plank] = 4;
                source.RestoreStableState(amounts, capacities);
                destination.RestoreStableState(new int[7], capacities);
                IndustrialLogisticsRoute route = NewRoute(root, source, destination,
                    ResourceType.Plank, "upgrade", 1f);
                route.Tick(0.6f);
                Require(route.TryApplyNextUpgrade(false), "Speed upgrade was rejected.");
                route.Tick(0.79f);
                Require(source.GetAmount(ResourceType.Plank) == 4,
                    "Upgrade preserved an unsafe partial timer.");
                route.Tick(0.01f);
                Require(source.GetAmount(ResourceType.Plank) == 3
                        && destination.GetAmount(ResourceType.Plank) == 1,
                    "Upgraded route duplicated or lost its atomic transfer.");
                Require(route.TryApplyNextUpgrade(true) && route.BatchSize == 2,
                    "Transport Capacity upgrade was not local to the route.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestOffline()
        {
            M9SaveData data = M9SaveData.CreateFresh(Now);
            data.industrial.districtEntered = true;
            data.industrial.assemblyPlantPurchase.completed = true;
            data.industrial.assemblyPlantPurchase.paidCash = M14IndustrialTuning.AssemblyPlantUnlockCost;
            data.industrial.assemblyInputIronBars = 10;
            data.industrial.assemblyInputTools = 5;
            M14BuildingUpgradeSaveRecord plant = data.industrial.GetBuildingUpgrade("assembly_plant");
            plant.speedLevel = 3; plant.outputCapacityLevel = 1;
            Require(M14OfflineProductionCalculator.Settle(data, 12L, 1d) == 5
                    && data.industrial.assemblyInputIronBars == 0
                    && data.industrial.assemblyInputTools == 0
                    && data.industrial.assemblyOutputMachineParts == 5,
                "Offline Assembly production ignored upgraded time or legitimate inputs.");

            data.logistics.warehouse.Get(ResourceType.Tool).amount = 6;
            M13RouteSaveRecord route = data.logistics.routes[4];
            route.enabled = true;
            route.batchSize = 2;
            route.capacityUpgradeLevel = 1;
            route.transferIntervalSeconds = 1.6f;
            route.speedUpgradeLevel = 1;
            data.industrial.assemblyInputTools = 0;
            Require(M13OfflineLogisticsCalculator.Settle(data, 4L, 1d) == 4
                    && data.logistics.warehouse.Get(ResourceType.Tool).amount == 2
                    && data.industrial.assemblyInputTools == 4,
                "Offline logistics ignored effective upgraded route values.");
        }

        private static void TestFiltering()
        {
            M10ProgressionSaveData data = M10ProgressionSaveData.CreateFresh();
            LumberCampContractDefinition advanced = LumberCampProgressionCatalog.GetContract(11);
            Require(!M10ProgressionRules.IsContractEligible(data, advanced),
                "Advanced contract became eligible while Assembly was locked.");
            var model = new RetentionGoalModel(M13RetentionSaveData.CreateFresh(Now),
                _ => 0L, _ => true, null);
            model.Evaluate(Now, new GoalUnlockContext(true, true, true, true, true, true));
            Require(!ContainsMachinePart(model.Daily) && !ContainsMachinePart(model.Weekly),
                "Locked Machine Part content entered retention goals.");
            bool generated = false;
            for (int day = 0; day < 20 && !generated; day++)
            {
                long timestamp = Now + day * 86400L;
                var unlocked = new RetentionGoalModel(
                    M13RetentionSaveData.CreateFresh(timestamp), _ => 0L, _ => true, null);
                unlocked.Evaluate(timestamp,
                    new GoalUnlockContext(true, true, true, true, true, true, true));
                generated = HasCandidateAcrossPeriods(unlocked);
            }
            Require(generated,
                "Unlocked M14 systems never entered Daily/Weekly candidates.");
        }

        private static void TestEmpireLevels()
        {
            M10ProgressionSaveData data = CreateLevelTenProgression();
            var model = new LumberCampProgressionModel(data, _ => true);
            var levels = new List<int>(); model.EmpireLevelAdvanced += levels.Add;
            model.RecordIndustrialDistrictEntered();
            model.RecordAssemblyPlantUnlocked();
            model.RecordMachinePartsProduced(10);
            model.RecordProductionUpgradePurchased();
            Require(model.ActiveContractIndex == 11
                    && model.ActiveContractState == ContractProgressState.CompletedUnclaimed
                    && model.TryClaimActiveContract(),
                "Advanced manufacturing contract did not complete and claim.");
            Require(model.EmpireLevel == 15
                    && string.Join(",", levels) == "11,12,13,14,15",
                "Empire 11-15 ordering is not deterministic: " + string.Join(",", levels));
        }

        private static void TestMigration()
        {
            M9SaveData legacy = M9SaveData.CreateFresh(Now);
            legacy.version = M9SaveSchema.Version5;
            Array.Resize(ref legacy.progression.metrics, 31);
            Array.Resize(ref legacy.progression.flags, 8);
            Array.Resize(ref legacy.progression.claimedContracts, 11);
            Array.Resize(ref legacy.progression.achievements, 19);
            Array.Resize(ref legacy.logistics.warehouse.resources, 6);
            Array.Resize(ref legacy.logistics.routes, 3);
            legacy.logistics.warehouse.Get(ResourceType.IronBar).amount = 7;
            M9SaveDecodeResult result = M9SaveCodec.Decode(JsonUtility.ToJson(legacy),
                M9SaveValidationSettings.CreateDefault(), Now);
            Require(result.IsSuccess && result.WasMigrated
                    && result.Data.version == M9SaveSchema.Version6
                    && result.Data.logistics.warehouse.Get(ResourceType.IronBar).amount == 7
                    && result.Data.industrial != null
                    && !result.Data.industrial.districtEntered,
                "M13 v5 save did not migrate to canonical v6 without reset: " + result.Diagnostic);
        }

        private static void TestRoundTripAndReset()
        {
            M9SaveData data = M9SaveData.CreateFresh(Now);
            data.industrial.districtEntered = true;
            SetMetric(data.progression, ProgressMetricId.IndustrialDistrictEntered, 1);
            data.industrial.assemblyPlantPurchase.completed = true;
            data.industrial.assemblyPlantPurchase.paidCash = M14IndustrialTuning.AssemblyPlantUnlockCost;
            SetMetric(data.progression, ProgressMetricId.AssemblyPlantUnlocked, 1);
            data.industrial.assemblyInputIronBars = 4;
            data.industrial.assemblyInputTools = 2;
            data.industrial.assemblyOutputMachineParts = 3;
            SetMetric(data.progression, ProgressMetricId.MachinePartsProduced, 3);
            data.logistics.routes[3].capacityUpgradeLevel = 1;
            data.logistics.routes[3].batchSize = 2;
            Require(RoundTrip(data, out M9SaveData loaded)
                    && loaded.industrial.assemblyOutputMachineParts == 3
                    && loaded.logistics.routes[3].capacityUpgradeLevel == 1,
                "v6 round-trip lost M14 state.");
            M9SaveData reset = M9SaveData.CreateFresh(Now);
            Require(!reset.industrial.districtEntered
                    && !reset.industrial.assemblyPlantPurchase.completed
                    && reset.industrial.assemblyOutputMachineParts == 0
                    && reset.logistics.routes[3].capacityUpgradeLevel == 0,
                "Fresh/reset retained M14 state.");
        }

        private static M10ProgressionSaveData CreateLevelTenProgression()
        {
            M10ProgressionSaveData data = M10ProgressionSaveData.CreateFresh();
            data.flags[(int)ProgressFlagId.LumberCampCompleted].value = true;
            data.flags[(int)ProgressFlagId.SmelterUnlocked].value = true;
            SetMetric(data, ProgressMetricId.DrillUnlocked, 1);
            SetMetric(data, ProgressMetricId.ToolWorkshopUnlocked, 1);
            SetMetric(data, ProgressMetricId.ToolsProduced, 1);
            SetMetric(data, ProgressMetricId.WarehouseUnlocked, 1);
            SetMetric(data, ProgressMetricId.IndustrialRouteOperational, 1);
            SetMetric(data, ProgressMetricId.DailyGoalsCompleted, 1);
            SetMetric(data, ProgressMetricId.CrossIndustryLogisticsContractsCompleted, 1);
            SetMetric(data, ProgressMetricId.IndustrialDistrictReady, 1);
            for (int i = 0; i < 11; i++) data.claimedContracts[i] = true;
            data.activeContractIndex = 11;
            data.activeContractBaseline = 0;
            data.activeContractState = ContractProgressState.Locked;
            return data;
        }

        private static IndustrialLogisticsRoute NewRoute(GameObject root,
            MonoBehaviour source, MonoBehaviour destination, ResourceType type,
            string id, float interval = 2f)
        {
            IndustrialLogisticsRoute route = new GameObject(id).AddComponent<IndustrialLogisticsRoute>();
            route.transform.SetParent(root.transform);
            Require(route.TryConfigure(source, destination, type, 1, interval, true),
                "Route fixture configuration failed.");
            return route;
        }

        private static bool RoundTrip(M9SaveData data, out M9SaveData loaded)
        {
            loaded = null;
            if (!M9SaveCodec.TryEncode(data, M9SaveValidationSettings.CreateDefault(),
                    Now, out string json, out _, out _)) return false;
            M9SaveDecodeResult result = M9SaveCodec.Decode(json,
                M9SaveValidationSettings.CreateDefault(), Now);
            loaded = result.Data;
            return result.IsSuccess && !result.WasMigrated;
        }

        private static bool ContainsMachinePart(M13GoalPeriodSaveData period)
        {
            if (period?.goals == null) return false;
            for (int i = 0; i < period.goals.Length; i++)
                if (period.goals[i].resourceType == ResourceType.MachinePart) return true;
            return false;
        }

        private static bool HasCandidateAcrossPeriods(RetentionGoalModel model)
        {
            return ContainsMachinePart(model.Daily) || ContainsMachinePart(model.Weekly)
                   || ContainsType(model.Daily, RetentionGoalType.UpgradeBuilding)
                   || ContainsType(model.Weekly, RetentionGoalType.UpgradeRoute);
        }

        private static bool ContainsType(M13GoalPeriodSaveData period, RetentionGoalType type)
        {
            if (period?.goals == null) return false;
            for (int i = 0; i < period.goals.Length; i++)
                if (period.goals[i].type == type) return true;
            return false;
        }

        private static void SetMetric(M10ProgressionSaveData data,
            ProgressMetricId metric, long value) => data.metrics[(int)metric].value = value;

        private static void SetReference(UnityEngine.Object target, string property,
            UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Run(string name, Action test)
        {
            try { test(); _tests++; }
            catch (Exception ex) { throw new InvalidOperationException("M14 test failed: " + name, ex); }
        }

        private static void Require(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
