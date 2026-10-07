using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    /// <summary>Deterministic ownership, retention, progression, and v5 persistence coverage.</summary>
    public static class LumberCampM13Tests
    {
        private const long BaselineUtc = 2000000000L;
        private static int _tests;
        private static int _assertions;

        [MenuItem("Industry Tycoon/Prototype/Run M13 Industrial Logistics Tests")]
        private static void RunFromMenu() => RunAllForValidator();

        public static void RunFromCommandLine()
        {
            try
            {
                RunAllForValidator();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void RunAllForValidator()
        {
            _tests = 0;
            _assertions = 0;
            Run("Warehouse manual deposit, withdraw, capacity, and type", TestWarehouse);
            Run("Route atomic transfer, pause/resume, contention, and repetition", TestRoutes);
            Run("Route save/load resolves at a stable boundary", TestRouteSaveLoad);
            Run("Offline logistics uses persisted source budgets only", TestOfflineLogistics);
            Run("Daily/Weekly progress, claim-once, and reset", TestRetentionProgress);
            Run("Clock rollback and abnormal jump safety", TestClockSafety);
            Run("Goal generation excludes locked content", TestGoalEligibility);
            Run("Expanded contracts remain unlock-eligible and non-retroactive", TestContracts);
            Run("Empire levels 6 through 10 are ordered and once-only", TestEmpireLevels);
            Run("v4 to v5 migration is safe and idempotent", TestVersion4Migration);
            Run("v5 round-trip and fresh reset clear M13 state", TestVersion5RoundTripAndReset);

            Debug.Log($"M13 deterministic tests PASS: {_tests} tests, {_assertions} assertions.");
        }

        private static void TestWarehouse()
        {
            GameObject root = new GameObject("M13 Warehouse Test");
            try
            {
                CentralWarehouse warehouse = root.AddComponent<CentralWarehouse>();
                CarryStack stack = root.AddComponent<CarryStack>();
                int[] capacities = { 2, 2, 2, 2, 2, 2 };
                Require(warehouse.RestoreStableState(new int[6], capacities),
                    "Warehouse rejected canonical capacities.");
                Require(stack.TryAdd(ResourceType.Plank, 2)
                        && warehouse.TryDepositFrom(stack, ResourceType.Plank)
                        && warehouse.TryDepositFrom(stack, ResourceType.Plank),
                    "Warehouse did not accept valid physical Planks.");
                Require(warehouse.GetAmount(ResourceType.Plank) == 2
                        && stack.TotalAmount == 0
                        && !warehouse.TryDepositFrom(stack, ResourceType.Plank),
                    "Warehouse capacity or source ownership was not respected.");
                Require(warehouse.TryWithdrawTo(stack, ResourceType.Plank)
                        && warehouse.GetAmount(ResourceType.Plank) == 1
                        && stack.GetAmount(ResourceType.Plank) == 1,
                    "Warehouse withdrawal was not ownership preserving.");
                Require(!warehouse.TryWithdrawTo(stack, ResourceType.IronBar)
                        && warehouse.GetAmount(ResourceType.IronBar) == 0,
                    "Warehouse permitted a wrong-type withdrawal.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void TestRoutes()
        {
            GameObject sourceObject = new GameObject("M13 Route Source");
            GameObject destinationObject = new GameObject("M13 Route Destination");
            GameObject routeObject = new GameObject("M13 Route");
            try
            {
                CentralWarehouse source = sourceObject.AddComponent<CentralWarehouse>();
                CentralWarehouse destination = destinationObject.AddComponent<CentralWarehouse>();
                int[] capacities = { 10, 10, 10, 10, 10, 10 };
                int[] sourceAmounts = new int[6];
                sourceAmounts[(int)ResourceType.Plank] = 3;
                Require(source.RestoreStableState(sourceAmounts, capacities)
                        && destination.RestoreStableState(new int[6], capacities),
                    "Route fixtures could not be initialized.");
                IndustrialLogisticsRoute route = routeObject.AddComponent<IndustrialLogisticsRoute>();
                Require(route.TryConfigure(source, destination, ResourceType.Plank, 1, 0.1f, true),
                    "Valid route configuration was rejected.");

                Require(source.TryReserveOutgoing(ResourceType.Plank, 3, out var playerReservation),
                    "Contention fixture could not reserve source ownership.");
                route.Tick(0.1f);
                Require(route.State == IndustrialRouteState.PausedEmptySource
                        && source.GetAmount(ResourceType.Plank) == 3
                        && destination.GetAmount(ResourceType.Plank) == 0,
                    "Route ignored competing ownership or partially transferred.");
                Require(source.ReleaseReservation(playerReservation),
                    "Competing source reservation could not be released.");

                for (int i = 0; i < 3; i++) route.Tick(0.1f);
                Require(source.GetAmount(ResourceType.Plank) == 0
                        && destination.GetAmount(ResourceType.Plank) == 3
                        && route.State == IndustrialRouteState.PausedEmptySource,
                    "Repeated route transfers duplicated or lost inventory.");

                int[] full = new int[6];
                full[(int)ResourceType.Plank] = 10;
                sourceAmounts[(int)ResourceType.Plank] = 1;
                Require(source.RestoreStableState(sourceAmounts, capacities)
                        && destination.RestoreStableState(full, capacities),
                    "Full-destination fixture restore failed.");
                route.Tick(0.1f);
                Require(route.State == IndustrialRouteState.PausedFullDestination
                        && source.GetAmount(ResourceType.Plank) == 1,
                    "Full destination did not pause without consuming source inventory.");
                full[(int)ResourceType.Plank] = 9;
                Require(destination.RestoreStableState(full, capacities),
                    "Destination resume fixture failed.");
                route.Tick(0.1f);
                Require(source.GetAmount(ResourceType.Plank) == 0
                        && destination.GetAmount(ResourceType.Plank) == 10,
                    "Route did not resume atomically after capacity returned.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(routeObject);
                UnityEngine.Object.DestroyImmediate(destinationObject);
                UnityEngine.Object.DestroyImmediate(sourceObject);
            }
        }

        private static void TestRouteSaveLoad()
        {
            GameObject root = new GameObject("M13 Save Route");
            try
            {
                CentralWarehouse source = new GameObject("Source").AddComponent<CentralWarehouse>();
                source.transform.SetParent(root.transform);
                CentralWarehouse destination = new GameObject("Destination").AddComponent<CentralWarehouse>();
                destination.transform.SetParent(root.transform);
                IndustrialLogisticsRoute route = new GameObject("Route").AddComponent<IndustrialLogisticsRoute>();
                route.transform.SetParent(root.transform);
                int[] capacities = { 10, 10, 10, 10, 10, 10 };
                int[] amounts = new int[6];
                amounts[(int)ResourceType.Plank] = 1;
                source.RestoreStableState(amounts, capacities);
                destination.RestoreStableState(new int[6], capacities);
                Require(route.TryConfigure(source, destination, ResourceType.Plank, 1, 1f, true),
                    "Save/load route setup failed.");
                route.Tick(0.6f);
                route.RestoreRuntimeState(true);
                route.Tick(0.5f);
                Require(source.GetAmount(ResourceType.Plank) == 1
                        && destination.GetAmount(ResourceType.Plank) == 0,
                    "Load incorrectly persisted an in-flight timer/presentation.");
                route.Tick(0.5f);
                Require(source.GetAmount(ResourceType.Plank) == 0
                        && destination.GetAmount(ResourceType.Plank) == 1,
                    "Loaded route did not resume from a stable ownership boundary.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void TestOfflineLogistics()
        {
            M9SaveData data = M9SaveData.CreateFresh(BaselineUtc);
            data.processorOutputPlanks = 8;
            data.logistics.routes[0].enabled = true;
            data.logistics.routes[1].enabled = true;
            int transferred = M13OfflineLogisticsCalculator.Settle(data, 10L, 1d);
            Require(transferred == 5
                    && data.processorOutputPlanks == 3
                    && data.logistics.warehouse.Get(ResourceType.Plank).amount == 5
                    && data.packingInputPlanks == 0,
                "Offline settlement multi-hopped or failed to conserve persisted inventory.");

            data.processorOutputPlanks = 0;
            data.logistics.warehouse.Get(ResourceType.Plank).amount = 0;
            data.packingInputPlanks = 0;
            Require(M13OfflineLogisticsCalculator.Settle(data, 1000L, 1d) == 0
                    && data.processorOutputPlanks == 0
                    && data.packingInputPlanks == 0,
                "Offline logistics created inventory for an empty source.");
        }

        private static void TestRetentionProgress()
        {
            var metrics = new Dictionary<ProgressMetricId, long>();
            int granted = 0;
            int dailyCompletions = 0;
            var model = new RetentionGoalModel(
                M13RetentionSaveData.CreateFresh(BaselineUtc),
                metric => metrics.TryGetValue(metric, out long value) ? value : 0L,
                cash => { granted += cash; return true; },
                () => dailyCompletions++);
            GoalUnlockContext unlocks = new GoalUnlockContext(true, true, false, false, false, true);
            Require(model.Evaluate(BaselineUtc, unlocks)
                    && model.Daily.goals.Length == 3
                    && model.Weekly.goals.Length == 3,
                "Daily/Weekly goals were not deterministically generated.");
            CompleteGoals(model.Daily, metrics);
            CompleteGoals(model.Weekly, metrics);
            Require(model.Evaluate(BaselineUtc + 1L, unlocks)
                    && model.Daily.goals[0].completed
                    && model.Weekly.goals[0].completed
                    && dailyCompletions == 3,
                "Authoritative metric progress did not complete both periods.");
            Require(model.TryClaim(RetentionGoalPeriod.Daily, 0)
                    && !model.TryClaim(RetentionGoalPeriod.Daily, 0)
                    && granted == model.Daily.goals[0].rewardCash,
                "Goal reward was not claim-once.");
            long previousDaily = model.Daily.periodId;
            long previousWeekly = model.Weekly.periodId;
            Require(model.Evaluate(BaselineUtc + 86400L, unlocks)
                    && model.Daily.periodId == previousDaily + 1L
                    && model.Weekly.periodId >= previousWeekly,
                "UTC period advancement did not reset Daily deterministically.");
        }

        private static void TestClockSafety()
        {
            var model = new RetentionGoalModel(
                M13RetentionSaveData.CreateFresh(BaselineUtc),
                _ => 0L,
                _ => true,
                null);
            GoalUnlockContext unlocks = new GoalUnlockContext(false, false, false, false, false, false);
            model.Evaluate(BaselineUtc, unlocks);
            long daily = model.Daily.periodId;
            long weekly = model.Weekly.periodId;
            model.Evaluate(BaselineUtc - 86400L, unlocks);
            Require(model.ClockAnomalyDetected
                    && model.Daily.periodId == daily
                    && model.Weekly.periodId == weekly,
                "Clock rollback reset a reward period.");
            model.Evaluate(
                BaselineUtc + RetentionGoalModel.MaximumAcceptedForwardStepSeconds + 1L,
                unlocks);
            Require(model.Daily.periodId == daily && model.Weekly.periodId == weekly,
                "Abnormal forward clock jump reset a reward period.");
        }

        private static void TestGoalEligibility()
        {
            var model = new RetentionGoalModel(
                M13RetentionSaveData.CreateFresh(BaselineUtc),
                _ => 0L,
                _ => true,
                null);
            model.Evaluate(
                BaselineUtc,
                new GoalUnlockContext(false, false, false, false, false, false));
            for (int i = 0; i < model.Daily.goals.Length; i++)
            {
                M13GoalSaveRecord goal = model.Daily.goals[i];
                Require(goal.type != RetentionGoalType.Transport
                        && (goal.type == RetentionGoalType.EarnCash
                            || goal.resourceType == ResourceType.Wood),
                    "Locked or impossible content entered a fresh goal set.");
            }
        }

        private static void TestContracts()
        {
            M10ProgressionSaveData data = M10ProgressionSaveData.CreateFresh();
            LumberCampContractDefinition warehouse = LumberCampProgressionCatalog.GetContract(7);
            LumberCampContractDefinition mine = LumberCampProgressionCatalog.GetContract(8);
            LumberCampContractDefinition tools = LumberCampProgressionCatalog.GetContract(9);
            LumberCampContractDefinition logistics = LumberCampProgressionCatalog.GetContract(10);
            Require(!M10ProgressionRules.IsContractEligible(data, warehouse)
                    && !M10ProgressionRules.IsContractEligible(data, mine)
                    && !M10ProgressionRules.IsContractEligible(data, tools)
                    && !M10ProgressionRules.IsContractEligible(data, logistics),
                "Locked M13 contracts became active.");
            SetMetric(data, ProgressMetricId.WarehouseUnlocked, 1L);
            SetMetric(data, ProgressMetricId.MineUnlocked, 1L);
            SetMetric(data, ProgressMetricId.ToolWorkshopUnlocked, 1L);
            SetMetric(data, ProgressMetricId.IndustrialRouteOperational, 1L);
            Require(M10ProgressionRules.IsContractEligible(data, warehouse)
                    && M10ProgressionRules.IsContractEligible(data, mine)
                    && M10ProgressionRules.IsContractEligible(data, tools)
                    && M10ProgressionRules.IsContractEligible(data, logistics)
                    && logistics.IsCrossIndustryLogistics,
                "Unlocked M13 contract eligibility is incorrect.");
        }

        private static void TestEmpireLevels()
        {
            M10ProgressionSaveData data = M10ProgressionSaveData.CreateFresh();
            data.flags[(int)ProgressFlagId.LumberCampCompleted].value = true;
            data.flags[(int)ProgressFlagId.SmelterUnlocked].value = true;
            SetMetric(data, ProgressMetricId.DrillUnlocked, 1L);
            SetMetric(data, ProgressMetricId.ToolWorkshopUnlocked, 1L);
            SetMetric(data, ProgressMetricId.ToolsProduced, 1L);
            data.activeContractIndex = 10;
            data.activeContractState = ContractProgressState.Locked;
            for (int i = 0; i < 10; i++) data.claimedContracts[i] = true;
            var model = new LumberCampProgressionModel(data, _ => true);
            var levels = new List<int>();
            model.EmpireLevelAdvanced += levels.Add;
            Require(model.EmpireLevel == 5 && model.RecordWarehouseUnlocked(),
                "Empire did not enter level 6 from Warehouse unlock.");
            model.RecordIndustrialTransport(ResourceType.Plank, 1);
            model.RecordDailyGoalCompleted();
            model.RecordIndustrialTransport(ResourceType.IronBar, 10);
            Require(model.ActiveContractState == ContractProgressState.CompletedUnclaimed
                    && model.TryClaimActiveContract(),
                "Cross-industry logistics contract did not complete/claim.");
            Require(model.EmpireLevel == 10
                    && levels.Count == 5
                    && levels[0] == 6 && levels[1] == 7 && levels[2] == 8
                    && levels[3] == 9 && levels[4] == 10,
                $"Empire levels 6-10 were not emitted in strict order: "
                + $"level={model.EmpireLevel}, events={string.Join(",", levels)}.");
            model.RecordDailyGoalCompleted();
            Require(levels.Count == 5,
                "Empire milestones emitted more than once.");
        }

        private static void TestVersion4Migration()
        {
            M9SaveData legacy = M9SaveData.CreateFresh(BaselineUtc);
            legacy.version = M9SaveSchema.Version4;
            Array.Resize(ref legacy.progression.metrics, 20);
            Array.Resize(ref legacy.progression.claimedContracts, 7);
            legacy.logistics = null;
            legacy.retention = null;
            string json = JsonUtility.ToJson(legacy);
            M9SaveDecodeResult first = M9SaveCodec.Decode(
                json,
                M9SaveValidationSettings.CreateDefault(),
                BaselineUtc);
            Require(first.IsSuccess && first.WasMigrated
                    && first.Data.version == M9SaveSchema.Version5
                    && first.Data.logistics != null
                    && first.Data.retention != null,
                "Valid v4 did not migrate to canonical v5 M13 state.");
            Require(M9SaveCodec.TryEncode(
                        first.Data,
                        M9SaveValidationSettings.CreateDefault(),
                        BaselineUtc,
                        out string migratedJson,
                        out _,
                        out _)
                    && M9SaveCodec.Decode(
                        migratedJson,
                        M9SaveValidationSettings.CreateDefault(),
                        BaselineUtc).IsSuccess,
                "Migrated v5 state was not idempotently decodable.");
        }

        private static void TestVersion5RoundTripAndReset()
        {
            M9SaveData source = M9SaveData.CreateFresh(BaselineUtc);
            source.logistics.warehouse.Get(ResourceType.IronBar).amount = 7;
            source.logistics.routes[2].enabled = true;
            source.retention.clockAnomalyDetected = true;
            Require(M9SaveCodec.TryEncode(
                        source,
                        M9SaveValidationSettings.CreateDefault(),
                        BaselineUtc,
                        out string json,
                        out _,
                        out string failure),
                "v5 encode failed: " + failure);
            M9SaveDecodeResult decoded = M9SaveCodec.Decode(
                json,
                M9SaveValidationSettings.CreateDefault(),
                BaselineUtc);
            Require(decoded.IsSuccess && !decoded.WasMigrated
                    && decoded.Data.logistics.warehouse.Get(ResourceType.IronBar).amount == 7
                    && decoded.Data.logistics.routes[2].enabled
                    && decoded.Data.retention.clockAnomalyDetected,
                "v5 round-trip lost M13 state.");
            M9SaveData reset = M9SaveData.CreateFresh(BaselineUtc);
            Require(reset.logistics.warehouse.Get(ResourceType.IronBar).amount == 0
                    && !reset.logistics.routes[2].enabled
                    && reset.retention.daily.goals.Length == 0
                    && !reset.retention.clockAnomalyDetected,
                "Fresh/reset state retained M13 progress.");
        }

        private static void CompleteGoals(
            M13GoalPeriodSaveData period,
            IDictionary<ProgressMetricId, long> metrics)
        {
            for (int i = 0; i < period.goals.Length; i++)
            {
                M13GoalSaveRecord goal = period.goals[i];
                long required = goal.baseline + goal.target;
                if (!metrics.TryGetValue(goal.metric, out long current) || current < required)
                {
                    metrics[goal.metric] = required;
                }
            }
        }

        private static void SetMetric(
            M10ProgressionSaveData data,
            ProgressMetricId metric,
            long value)
        {
            data.metrics[(int)metric].value = value;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _tests++;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"M13 test failed: {name}", exception);
            }
        }

        private static void Require(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
