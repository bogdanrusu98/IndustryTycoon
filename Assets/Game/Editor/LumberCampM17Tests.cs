using System;
using System.Collections.Generic;
using IndustryTycoon.Market;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using IndustryTycoon.UI;
using NUnit.Framework;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM17Tests
    {
        [Test] public void Guidance_CoversTheCompleteLegitimatePathOneActionAtATime() => TestGuidance();
        [Test] public void DistrictGates_AreDerivedAndOrdered() => TestDistrictGates();
        [Test] public void Empire15To25_IsSequentialWithoutDuplicateEvents() => TestLateEmpireSequence();
        [Test] public void RepresentativeCheckpoints_RoundTripOnSchema8() => TestCheckpointReloads();
        [Test] public void UnlockFeedback_IsMajorOnlyAndDeterministic() => TestUnlockFeedback();
        [Test] public void AcceleratedFreshToEmpire25_CompletesWithoutDeadState() => TestAcceleratedSmoke();

        public static void RunAllForValidator()
        {
            TestGuidance();
            TestDistrictGates();
            TestLateEmpireSequence();
            TestCheckpointReloads();
            TestUnlockFeedback();
            TestAcceleratedSmoke();
        }

        private static void TestGuidance()
        {
            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            M15FactorySaveData factories = M15FactorySaveData.CreateFresh();
            WorldGuidanceState guidance = WorldProgressionGuidance.Resolve(state, factories, 1);
            Require(guidance.Landmark == WorldLandmarkId.LumberCamp
                    && guidance.Text.Contains("UNLOCK WORKER"),
                "Fresh guidance did not select the Lumber action.");

            CompleteLegacyObjectives(state);
            RequireGuidance(state, factories, 5, WorldLandmarkId.Warehouse, "WAREHOUSE");
            SetMetric(state, ProgressMetricId.WarehouseUnlocked, 1);
            RequireGuidance(state, factories, 6, WorldLandmarkId.Warehouse, "ROUTE");
            SetMetric(state, ProgressMetricId.IndustrialRouteOperational, 1);
            SetMetric(state, ProgressMetricId.DailyGoalsCompleted, 1);
            SetMetric(state, ProgressMetricId.CrossIndustryLogisticsContractsCompleted, 1);
            SetMetric(state, ProgressMetricId.IndustrialDistrictReady, 1);
            RequireGuidance(state, factories, 10, WorldLandmarkId.IndustrialEntrance, "ENTER");
            SetMetric(state, ProgressMetricId.IndustrialDistrictEntered, 1);
            RequireGuidance(state, factories, 11, WorldLandmarkId.AssemblyPlant, "ASSEMBLY");
            SetMetric(state, ProgressMetricId.AssemblyPlantUnlocked, 1);
            SetMetric(state, ProgressMetricId.MachinePartsProduced, 1);
            SetMetric(state, ProgressMetricId.ProductionUpgradesPurchased, 1);
            SetMetric(state, ProgressMetricId.AdvancedManufacturingContractsCompleted, 1);
            RequireGuidance(state, factories, 15, WorldLandmarkId.AssemblyPlant, "SPECIALIZE");

            CompleteFactoryScaling(factories);
            RequireGuidance(state, factories, 20, WorldLandmarkId.Market, "MARKET");
            SetMetric(state, ProgressMetricId.MarketUnlocked, 1);
            RequireGuidance(state, factories, 21, WorldLandmarkId.Market, "PRODUCTION ORDER");
            SetMetric(state, ProgressMetricId.ProductionOrdersCompleted, 1);
            SetMetric(state, ProgressMetricId.HighBoomSales, 1);
            SetMetric(state, ProgressMetricId.FlowBonusesTriggered, 1);
            RequireGuidance(state, factories, 24, WorldLandmarkId.Market, "MACHINE PART ORDER");
            SetMetric(state, ProgressMetricId.AdvancedMachinePartOrdersCompleted, 1);
            guidance = WorldProgressionGuidance.Resolve(state, factories, 25);
            Require(guidance.Text.Contains("LEVEL 25") && guidance.Landmark == WorldLandmarkId.Market,
                "Completed guidance did not converge on Empire 25.");
        }

        private static void TestDistrictGates()
        {
            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            Require(WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.Lumber, state, 1)
                    && !WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.Mining, state, 1)
                    && !WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.CentralLogistics, state, 1)
                    && !WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.Industrial, state, 1),
                "Fresh district gates are not deterministic.");
            SetFlag(state, ProgressFlagId.LumberCampCompleted);
            SetMetric(state, ProgressMetricId.WarehouseUnlocked, 1);
            Require(WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.Mining, state, 2)
                    && WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.CentralLogistics, state, 6)
                    && !WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.Industrial, state, 9)
                    && WorldProgressionGuidance.IsDistrictUnlocked(WorldDistrictId.Industrial, state, 10),
                "District gates did not follow authoritative progression.");
            string display = DistrictStatusView.BuildDisplayText(state, 10);
            Require(display.Contains("MINING OPEN") && display.Contains("INDUSTRIAL OPEN"),
                "District HUD did not expose the derived state.");
        }

        private static void TestLateEmpireSequence()
        {
            M10ProgressionSaveData state = CreateLevel15Progression();
            var model = new LumberCampProgressionModel(state, _ => true);
            var factory = M15FactorySaveData.CreateFresh();
            model.UpdateIndustrialContext(M14IndustrialSaveData.CreateFresh(), factory, false);
            var levels = new List<int>();
            model.EmpireLevelAdvanced += levels.Add;

            M15FactoryRules.TryChoose(factory, "wood_processor", FactorySpecialization.Throughput);
            model.UpdateIndustrialContext(M14IndustrialSaveData.CreateFresh(), factory, true);
            M15FactoryRules.TryChoose(factory, "iron_smelter", FactorySpecialization.Efficiency);
            model.UpdateIndustrialContext(M14IndustrialSaveData.CreateFresh(), factory, true);
            factory.optimizationObjectiveCompleted = true;
            model.UpdateIndustrialContext(M14IndustrialSaveData.CreateFresh(), factory, true);
            M15FactoryRules.TryChoose(factory, "tool_workshop", FactorySpecialization.Throughput);
            factory.GetFactory("wood_processor").completedSpecializedCycles = 25;
            factory.GetFactory("iron_smelter").completedSpecializedCycles = 25;
            factory.upgradedRouteUnitsTransported = 50;
            M15FactoryRules.RecalculateDistrictLevel(factory);
            model.UpdateIndustrialContext(M14IndustrialSaveData.CreateFresh(), factory, true);
            model.RecordMarketUnlocked();
            model.RecordProductionOrderCompleted(false);
            model.RecordMarketSale(DemandTier.High, 1);
            model.RecordFlowBonusTriggered();
            model.RecordProductionOrderCompleted(true);

            Require(model.EmpireLevel == 25
                    && string.Join(",", levels) == "16,17,18,19,20,21,22,23,24,25",
                "Empire 15-25 was skipped, duplicated, or unordered: " + string.Join(",", levels));
            int count = levels.Count;
            model.EvaluateAll();
            model.UpdateIndustrialContext(M14IndustrialSaveData.CreateFresh(), factory, true);
            Require(levels.Count == count, "Repeated evaluation duplicated a level event.");

            var restored = new LumberCampProgressionModel(model.CapturePersistentState(), _ => true);
            restored.UpdateIndustrialContext(M14IndustrialSaveData.CreateFresh(), factory, false);
            var replay = new List<int>();
            restored.EmpireLevelAdvanced += replay.Add;
            restored.EvaluateAll();
            Require(restored.EmpireLevel == 25 && replay.Count == 0,
                "Empire events replayed after silent restoration.");
        }

        private static void TestCheckpointReloads()
        {
            foreach (int level in new[] { 1, 5, 10, 15, 20, 25 })
            {
                const long now = 1800000000L;
                M9SaveData save = M9SaveData.CreateFresh(now);
                PopulateCheckpoint(save, level);
                Require(M9SaveCodec.TryEncode(save, M9SaveValidationSettings.CreateDefault(), now,
                        out string json, out _, out string failure),
                    $"Level {level} checkpoint did not encode: {failure}");
                M9SaveDecodeResult decoded = M9SaveCodec.Decode(
                    json, M9SaveValidationSettings.CreateDefault(), now);
                Require(decoded.IsSuccess && !decoded.WasMigrated
                        && decoded.Data.version == M9SaveSchema.CurrentVersion,
                    $"Level {level} checkpoint did not round-trip on the current schema.");
                var model = new LumberCampProgressionModel(decoded.Data.progression, _ => true);
                model.UpdateIndustrialContext(decoded.Data.industrial, decoded.Data.factoryScaling, false);
                Require(model.EmpireLevel == level,
                    $"Level {level} restored as {model.EmpireLevel}.");
            }
        }

        private static void TestUnlockFeedback()
        {
            int count = 0;
            for (int level = 1; level <= 25; level++)
                if (!string.IsNullOrEmpty(MajorUnlockToastView.GetMajorUnlockMessage(level))) count++;
            Require(count == 7
                    && MajorUnlockToastView.GetMajorUnlockMessage(10).Contains("INDUSTRIAL")
                    && MajorUnlockToastView.GetMajorUnlockMessage(25).Contains("25"),
                "Major unlock feedback became noisy or non-deterministic.");
        }

        private static void TestAcceleratedSmoke()
        {
            M9SaveData save = M9SaveData.CreateFresh(1800000000L);
            for (int level = 1; level <= 25; level++)
            {
                if (level == 17) continue; // Level 17 is an ordered transition into district level 2.
                PopulateCheckpoint(save, level);
                var model = new LumberCampProgressionModel(save.progression, _ => true);
                model.UpdateIndustrialContext(save.industrial, save.factoryScaling, false);
                Require(model.EmpireLevel == level,
                    $"Accelerated full progression reached {model.EmpireLevel}, expected {level}.");
            }
        }

        private static void PopulateCheckpoint(M9SaveData save, int level)
        {
            save.progression = M10ProgressionSaveData.CreateFresh();
            save.factoryScaling = M15FactorySaveData.CreateFresh();
            save.industrial = M14IndustrialSaveData.CreateFresh();
            if (level >= 2)
            {
                save.lumberCampCompleted = true;
                for (int i = 0; i < save.purchasePads.Length; i++)
                    CompletePad(save.purchasePads[i], M9PurchasePadIds.GetTotalCost(i));
                SetFlag(save.progression, ProgressFlagId.ProductionUpgradeUnlocked);
                SetFlag(save.progression, ProgressFlagId.WorkerUnlocked);
                SetFlag(save.progression, ProgressFlagId.ProcessorUnlocked);
                SetFlag(save.progression, ProgressFlagId.AutoFeederUnlocked);
                SetFlag(save.progression, ProgressFlagId.PackingStationUnlocked);
                SetFlag(save.progression, ProgressFlagId.CourierUnlocked);
                SetFlag(save.progression, ProgressFlagId.LumberCampCompleted);
                SetMetric(save.progression, ProgressMetricId.PlanksProduced, 10);
                SetMetric(save.progression, ProgressMetricId.CratesProduced, 5);
                SetMetric(save.progression, ProgressMetricId.CourierTripsCompleted, 5);
            }
            if (level >= 3)
            {
                save.mining.mineUnlocked = true;
                CompletePad(save.mining.purchasePads[0],
                    M11MiningPurchasePadIds.GetTotalCost(0));
                CompletePad(save.mining.purchasePads[1],
                    M11MiningPurchasePadIds.GetTotalCost(1));
                SetMetric(save.progression, ProgressMetricId.MineUnlocked, 1);
                SetFlag(save.progression, ProgressFlagId.SmelterUnlocked);
                SetMetric(save.progression, ProgressMetricId.IronOreMined, 10);
                SetMetric(save.progression, ProgressMetricId.IronBarsProduced, 5);
                SetMetric(save.progression, ProgressMetricId.DrillUnlocked, 1);
            }
            if (level >= 4)
            {
                CompletePad(save.workshop.purchasePad, M12WorkshopPurchasePadIds.ToolWorkshopCost);
                SetMetric(save.progression, ProgressMetricId.ToolWorkshopUnlocked, 1);
            }
            if (level >= 5) SetMetric(save.progression, ProgressMetricId.ToolsProduced, 1);
            if (level >= 6) SetMetric(save.progression, ProgressMetricId.WarehouseUnlocked, 1);
            if (level >= 7) SetMetric(save.progression, ProgressMetricId.IndustrialRouteOperational, 1);
            if (level >= 8) SetMetric(save.progression, ProgressMetricId.DailyGoalsCompleted, 1);
            if (level >= 9) SetMetric(save.progression, ProgressMetricId.CrossIndustryLogisticsContractsCompleted, 1);
            if (level >= 10) SetMetric(save.progression, ProgressMetricId.IndustrialDistrictReady, 1);
            if (level >= 11)
            {
                save.industrial.districtEntered = true;
                SetMetric(save.progression, ProgressMetricId.IndustrialDistrictEntered, 1);
            }
            if (level >= 12)
            {
                CompletePad(save.industrial.assemblyPlantPurchase,
                    M14IndustrialTuning.AssemblyPlantUnlockCost);
                SetMetric(save.progression, ProgressMetricId.AssemblyPlantUnlocked, 1);
            }
            if (level >= 13) SetMetric(save.progression, ProgressMetricId.MachinePartsProduced, 1);
            if (level >= 14) SetMetric(save.progression, ProgressMetricId.ProductionUpgradesPurchased, 1);
            if (level >= 15) SetMetric(save.progression, ProgressMetricId.AdvancedManufacturingContractsCompleted, 1);
            if (level >= 16) M15FactoryRules.TryChoose(save.factoryScaling,
                "wood_processor", FactorySpecialization.Throughput);
            if (level >= 18) M15FactoryRules.TryChoose(save.factoryScaling,
                "iron_smelter", FactorySpecialization.Efficiency);
            if (level >= 19) save.factoryScaling.optimizationObjectiveCompleted = true;
            if (level >= 20) CompleteFactoryScaling(save.factoryScaling);
            if (level >= 21) SetMetric(save.progression, ProgressMetricId.MarketUnlocked, 1);
            if (level >= 22) SetMetric(save.progression, ProgressMetricId.ProductionOrdersCompleted, 1);
            if (level >= 23) SetMetric(save.progression, ProgressMetricId.HighBoomSales, 1);
            if (level >= 24) SetMetric(save.progression, ProgressMetricId.FlowBonusesTriggered, 1);
            if (level >= 25) SetMetric(save.progression,
                ProgressMetricId.AdvancedMachinePartOrdersCompleted, 1);
        }

        private static M10ProgressionSaveData CreateLevel15Progression()
        {
            M9SaveData save = M9SaveData.CreateFresh(1800000000L);
            PopulateCheckpoint(save, 15);
            return save.progression;
        }

        private static void CompleteLegacyObjectives(M10ProgressionSaveData state)
        {
            SetFlag(state, ProgressFlagId.WorkerUnlocked);
            SetFlag(state, ProgressFlagId.ProcessorUnlocked);
            SetFlag(state, ProgressFlagId.AutoFeederUnlocked);
            SetFlag(state, ProgressFlagId.PackingStationUnlocked);
            SetFlag(state, ProgressFlagId.CourierUnlocked);
            SetFlag(state, ProgressFlagId.LumberCampCompleted);
            SetFlag(state, ProgressFlagId.SmelterUnlocked);
            SetMetric(state, ProgressMetricId.PlanksProduced, 10);
            SetMetric(state, ProgressMetricId.CratesProduced, 5);
            SetMetric(state, ProgressMetricId.CourierTripsCompleted, 5);
            SetMetric(state, ProgressMetricId.IronOreMined, 10);
            SetMetric(state, ProgressMetricId.IronBarsProduced, 5);
            SetMetric(state, ProgressMetricId.DrillUnlocked, 1);
            SetMetric(state, ProgressMetricId.ToolWorkshopUnlocked, 1);
            SetMetric(state, ProgressMetricId.ToolsProduced, 5);
        }

        private static void CompleteFactoryScaling(M15FactorySaveData factory)
        {
            if (factory.GetFactory("wood_processor").specialization == FactorySpecialization.None)
                M15FactoryRules.TryChoose(factory, "wood_processor", FactorySpecialization.Throughput);
            if (factory.GetFactory("iron_smelter").specialization == FactorySpecialization.None)
                M15FactoryRules.TryChoose(factory, "iron_smelter", FactorySpecialization.Efficiency);
            if (factory.GetFactory("tool_workshop").specialization == FactorySpecialization.None)
                M15FactoryRules.TryChoose(factory, "tool_workshop", FactorySpecialization.Throughput);
            factory.optimizationObjectiveCompleted = true;
            factory.upgradedRouteUnitsTransported = 50;
            factory.GetFactory("wood_processor").completedSpecializedCycles = 25;
            factory.GetFactory("iron_smelter").completedSpecializedCycles = 25;
            M15FactoryRules.RecalculateDistrictLevel(factory);
        }

        private static void RequireGuidance(M10ProgressionSaveData state,
            M15FactorySaveData factory, int level, WorldLandmarkId landmark, string fragment)
        {
            WorldGuidanceState guidance = WorldProgressionGuidance.Resolve(state, factory, level);
            Require(guidance.Landmark == landmark && guidance.Text.Contains(fragment),
                $"Guidance selected {guidance.Landmark}: {guidance.Text}");
        }

        private static void SetMetric(M10ProgressionSaveData state,
            ProgressMetricId metric, long value)
        {
            state.metrics[(int)metric].value = value;
        }

        private static void CompletePad(M9PurchasePadSaveRecord pad, int totalCost)
        {
            pad.paidCash = totalCost;
            pad.completed = true;
        }

        private static void SetFlag(M10ProgressionSaveData state, ProgressFlagId flag)
        {
            state.flags[(int)flag].value = true;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
