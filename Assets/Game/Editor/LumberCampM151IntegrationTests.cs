using System;
using System.Collections.Generic;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using NUnit.Framework;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM151IntegrationTests
    {
        [Test]
        public void StrategyContracts_RotateOnlyAcrossEligiblePersistentContracts() =>
            TestStrategyContracts();

        [Test]
        public void EmpireLevels16To20_UseGlobalSequentialSilentRestorePipeline() =>
            TestEmpireLevels();

        [Test]
        public void SmelterOffline_RespectsPersistedBudgetsAndAllModifiers() =>
            TestSmelterOffline();

        public static void RunAllForValidator()
        {
            TestStrategyContracts();
            TestEmpireLevels();
            TestSmelterOffline();
        }

        private static void TestStrategyContracts()
        {
            M10ProgressionSaveData progression = CreateLevelFifteenProgression();
            var model = new LumberCampProgressionModel(progression, _ => true);
            M14IndustrialSaveData industrial = CreateUnlockedIndustrial();
            M15FactorySaveData factory = M15FactorySaveData.CreateFresh();
            Require(M15FactoryRules.TryChoose(factory, "wood_processor",
                    FactorySpecialization.Efficiency)
                    && M15FactoryRules.TryChoose(factory, "iron_smelter",
                        FactorySpecialization.Throughput),
                "Strategy fixture could not establish eligible specializations.");

            model.UpdateIndustrialContext(industrial, factory, true);
            Require(model.ActiveContractIndex
                    == (int)LumberCampContractId.CompleteEfficiencyCycles,
                "Rotation did not select the first legitimately eligible strategy contract.");
            M15FactoryRules.CompleteRecipes(factory.GetFactory("wood_processor"),
                10, out _);
            model.UpdateIndustrialContext(industrial, factory, true);
            Require(model.ActiveContractState == ContractProgressState.CompletedUnclaimed
                    && model.TryClaimActiveContract(),
                "Efficiency strategy contract did not complete/claim exactly once.");
            Require(model.ActiveContractIndex
                    == (int)LumberCampContractId.TransportOnUpgradedRoutes,
                "Rotation did not preserve deterministic eligible order.");

            factory.upgradedRouteUnitsTransported = 25;
            model.UpdateIndustrialContext(industrial, factory, true);
            Require(model.TryClaimActiveContract()
                    && model.ActiveContractIndex
                    == (int)LumberCampContractId.SpecializeAnotherFactory,
                "Upgraded-route strategy contract did not rotate after claim.");
            Require(M15FactoryRules.TryChoose(factory, "tool_workshop",
                    FactorySpecialization.Throughput),
                "Strategy fixture could not specialize the third factory.");
            model.UpdateIndustrialContext(industrial, factory, true);
            Require(model.TryClaimActiveContract()
                    && model.ActiveContractIndex
                    == (int)LumberCampContractId.ClearOutputBottleneck,
                "Additional-specialization contract did not use its activation baseline.");

            industrial.GetBuildingUpgrade("wood_processor").outputCapacityLevel = 1;
            model.UpdateIndustrialContext(industrial, factory, true);
            Require(model.ActiveContractState == ContractProgressState.CompletedUnclaimed
                    && model.TryClaimActiveContract()
                    && !model.HasPendingContract,
                "Output bottleneck contract did not claim once or retained a phantom contract.");

            M10ProgressionSaveData saved = model.CapturePersistentState();
            var restored = new LumberCampProgressionModel(saved, _ => true);
            restored.UpdateIndustrialContext(industrial, factory, false);
            Require(!restored.HasPendingContract
                    && restored.IsContractClaimed(
                        (int)LumberCampContractId.ClearOutputBottleneck),
                "Strategy rotation/claim state did not persist.");
        }

        private static void TestEmpireLevels()
        {
            var model = new LumberCampProgressionModel(
                CreateLevelFifteenProgression(), _ => true);
            M14IndustrialSaveData industrial = CreateUnlockedIndustrial();
            M15FactorySaveData factory = M15FactorySaveData.CreateFresh();
            var events = new List<int>();
            model.EmpireLevelAdvanced += events.Add;

            M15FactoryRules.TryChoose(factory, "wood_processor",
                FactorySpecialization.Throughput);
            model.UpdateIndustrialContext(industrial, factory, true);
            M15FactoryRules.TryChoose(factory, "iron_smelter",
                FactorySpecialization.Efficiency);
            model.UpdateIndustrialContext(industrial, factory, true);
            factory.optimizationObjectiveCompleted = true;
            model.UpdateIndustrialContext(industrial, factory, true);
            M15FactoryRules.TryChoose(factory, "tool_workshop",
                FactorySpecialization.Throughput);
            factory.GetFactory("wood_processor").completedSpecializedCycles = 25;
            factory.GetFactory("iron_smelter").completedSpecializedCycles = 25;
            factory.upgradedRouteUnitsTransported = 50;
            M15FactoryRules.RecalculateDistrictLevel(factory);
            model.UpdateIndustrialContext(industrial, factory, true);

            Require(model.EmpireLevel == 20
                    && string.Join(",", events) == "16,17,18,19,20",
                "Global Empire pipeline skipped or duplicated M15 level events: "
                + string.Join(",", events));
            model.UpdateIndustrialContext(industrial, factory, true);
            Require(events.Count == 5,
                "An unchanged industrial state replayed Empire level events.");

            var restored = new LumberCampProgressionModel(
                model.CapturePersistentState(), _ => true);
            var replayed = new List<int>();
            restored.EmpireLevelAdvanced += replayed.Add;
            restored.UpdateIndustrialContext(industrial, factory, false);
            restored.EvaluateAll();
            Require(restored.EmpireLevel == 20 && replayed.Count == 0,
                "Save/load replayed already-earned M15 Empire levels.");
        }

        private static void TestSmelterOffline()
        {
            M9SaveData noInput = CreateSmelterData(0, 0);
            Require(M15SmelterOfflineProductionCalculator.Settle(
                    noInput, 100, 1d) == 0,
                "Offline Smelter invented Iron Ore.");

            M9SaveData limited = CreateSmelterData(3, 0);
            Require(M15SmelterOfflineProductionCalculator.Settle(
                        limited, 100, 1d) == 1
                    && limited.mining.smelterInputIronOre == 1
                    && limited.mining.smelterOutputIronBars == 1,
                "Offline Smelter violated its limited persisted input budget.");

            M9SaveData full = CreateSmelterData(24, 12);
            Require(M15SmelterOfflineProductionCalculator.Settle(
                    full, 100, 1d) == 0,
                "Offline Smelter exceeded output capacity.");

            M9SaveData baseSpeed = CreateSmelterData(2, 0);
            M9SaveData upgradedSpeed = CreateSmelterData(2, 0);
            upgradedSpeed.industrial.GetBuildingUpgrade("iron_smelter").speedLevel = 3;
            Require(M15SmelterOfflineProductionCalculator.Settle(
                        baseSpeed, 1, 1d) == 0
                    && M15SmelterOfflineProductionCalculator.Settle(
                        upgradedSpeed, 1, 1d) == 1,
                "Offline Smelter ignored the M14 speed upgrade.");

            M9SaveData modified = CreateSmelterData(10, 0);
            M15FactoryRules.TryChoose(modified.factoryScaling, "iron_smelter",
                FactorySpecialization.Efficiency);
            M15FactoryRules.TryChoose(modified.factoryScaling, "wood_processor",
                FactorySpecialization.Throughput);
            Require(M15SmelterOfflineProductionCalculator.Settle(
                        modified, 100, 1d) == 5
                    && modified.mining.smelterInputIronOre == 1
                    && modified.factoryScaling.GetFactory("iron_smelter")
                        .completedSpecializedCycles == 5,
                "Offline Smelter ignored specialization/district modifiers or conservation.");

            M9SaveData boundary = CreateSmelterData(4, 0);
            Require(M15SmelterOfflineProductionCalculator.Settle(
                    boundary, 3, 1d) == 2,
                "Offline Smelter failed the first deterministic save boundary.");
            string json = UnityEngine.JsonUtility.ToJson(boundary);
            M9SaveData loaded = UnityEngine.JsonUtility.FromJson<M9SaveData>(json);
            Require(M15SmelterOfflineProductionCalculator.Settle(
                        loaded, 0, 1d) == 0
                    && loaded.mining.smelterOutputIronBars == 2,
                "Save/load boundary duplicated offline Smelter output.");
        }

        private static M9SaveData CreateSmelterData(int input, int output)
        {
            M9SaveData data = M9SaveData.CreateFresh(1000);
            data.mining.mineUnlocked = true;
            M9PurchasePadSaveRecord pad =
                data.mining.purchasePads[M11MiningPurchasePadIds.SmelterIndex];
            pad.completed = true;
            pad.paidCash = M11MiningPurchasePadIds.GetTotalCost(
                M11MiningPurchasePadIds.SmelterIndex);
            data.mining.smelterInputIronOre = input;
            data.mining.smelterOutputIronBars = output;
            return data;
        }

        private static M14IndustrialSaveData CreateUnlockedIndustrial()
        {
            M14IndustrialSaveData data = M14IndustrialSaveData.CreateFresh();
            data.districtEntered = true;
            data.assemblyPlantPurchase.completed = true;
            data.assemblyPlantPurchase.paidCash = M14IndustrialTuning.AssemblyPlantUnlockCost;
            return data;
        }

        private static M10ProgressionSaveData CreateLevelFifteenProgression()
        {
            M10ProgressionSaveData data = M10ProgressionSaveData.CreateFresh();
            data.flags[(int)ProgressFlagId.LumberCampCompleted].value = true;
            SetMetric(data, ProgressMetricId.DrillUnlocked, 1);
            SetMetric(data, ProgressMetricId.ToolWorkshopUnlocked, 1);
            SetMetric(data, ProgressMetricId.ToolsProduced, 1);
            SetMetric(data, ProgressMetricId.WarehouseUnlocked, 1);
            SetMetric(data, ProgressMetricId.IndustrialRouteOperational, 1);
            SetMetric(data, ProgressMetricId.DailyGoalsCompleted, 1);
            SetMetric(data, ProgressMetricId.CrossIndustryLogisticsContractsCompleted, 1);
            SetMetric(data, ProgressMetricId.IndustrialDistrictReady, 1);
            SetMetric(data, ProgressMetricId.IndustrialDistrictEntered, 1);
            SetMetric(data, ProgressMetricId.AssemblyPlantUnlocked, 1);
            SetMetric(data, ProgressMetricId.MachinePartsProduced, 1);
            SetMetric(data, ProgressMetricId.ProductionUpgradesPurchased, 1);
            SetMetric(data, ProgressMetricId.AdvancedManufacturingContractsCompleted, 1);
            for (int i = 0; i < LumberCampProgressionCatalog.LegacyContractCount; i++)
            {
                data.claimedContracts[i] = true;
            }
            data.activeContractIndex = -1;
            data.activeContractState = ContractProgressState.Claimed;
            return data;
        }

        private static void SetMetric(
            M10ProgressionSaveData data,
            ProgressMetricId metric,
            long value) => data.metrics[(int)metric].value = value;

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
