using IndustryTycoon.Market;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;

namespace IndustryTycoon.Progression
{
    public enum WorldDistrictId
    {
        Lumber = 0,
        Mining = 1,
        CentralLogistics = 2,
        Industrial = 3
    }

    public enum WorldLandmarkId
    {
        LumberCamp = 0,
        MineEntrance = 1,
        Smelter = 2,
        ToolWorkshop = 3,
        Warehouse = 4,
        IndustrialEntrance = 5,
        AssemblyPlant = 6,
        Market = 7
    }

    public readonly struct WorldGuidanceState
    {
        public WorldGuidanceState(string text, WorldLandmarkId landmark)
        {
            Text = text ?? string.Empty;
            Landmark = landmark;
        }

        public string Text { get; }
        public WorldLandmarkId Landmark { get; }
    }

    /// <summary>
    /// Selects one next action from the existing authoritative progression state.
    /// This is presentation policy only; it does not own or persist progression.
    /// </summary>
    public static class WorldProgressionGuidance
    {
        public static bool IsDistrictUnlocked(
            WorldDistrictId district,
            M10ProgressionSaveData progression,
            int empireLevel)
        {
            switch (district)
            {
                case WorldDistrictId.Lumber:
                    return true;
                case WorldDistrictId.Mining:
                    return progression != null
                           && progression.GetFlag(ProgressFlagId.LumberCampCompleted);
                case WorldDistrictId.CentralLogistics:
                    return progression != null
                           && progression.GetMetric(ProgressMetricId.WarehouseUnlocked) > 0L;
                case WorldDistrictId.Industrial:
                    return empireLevel >= IndustrialDistrictTease.ReadyEmpireLevel;
                default:
                    return false;
            }
        }

        public static WorldGuidanceState Resolve(
            M10ProgressionSaveData progression,
            M15FactorySaveData factoryScaling,
            int empireLevel)
        {
            if (progression == null)
            {
                return Next("UNLOCK WORKER", WorldLandmarkId.LumberCamp);
            }

            int objectiveIndex = M10ProgressionRules.ResolveObjectiveIndex(progression);
            if (objectiveIndex < LumberCampProgressionCatalog.ObjectiveCount)
            {
                MainObjectiveDefinition objective =
                    LumberCampProgressionCatalog.GetObjective(objectiveIndex);
                string text;
                if (objective.ConditionKind == ObjectiveConditionKind.Flag)
                {
                    text = "OBJECTIVE: " + objective.Label;
                }
                else
                {
                    long current = objective.ConditionKind == ObjectiveConditionKind.EmpireLevel
                        ? empireLevel
                        : progression.GetMetric(objective.Metric);
                    text = $"OBJECTIVE: {objective.Label} — "
                           + $"{System.Math.Min(objective.Target, current)} / {objective.Target}";
                }

                return new WorldGuidanceState(text, ResolveLegacyLandmark(objective.Id));
            }

            if (progression.GetMetric(ProgressMetricId.WarehouseUnlocked) < 1L)
                return Next("OPEN CENTRAL WAREHOUSE", WorldLandmarkId.Warehouse);
            if (progression.GetMetric(ProgressMetricId.IndustrialRouteOperational) < 1L)
                return Next("ACTIVATE A WAREHOUSE ROUTE", WorldLandmarkId.Warehouse);
            if (progression.GetMetric(ProgressMetricId.DailyGoalsCompleted) < 1L)
                return Next("COMPLETE A DAILY GOAL", WorldLandmarkId.Warehouse);
            if (progression.GetMetric(ProgressMetricId.CrossIndustryLogisticsContractsCompleted) < 1L)
                return Next("COMPLETE THE LOGISTICS CONTRACT", WorldLandmarkId.Warehouse);
            if (progression.GetMetric(ProgressMetricId.IndustrialDistrictEntered) < 1L)
                return Next("ENTER THE INDUSTRIAL DISTRICT", WorldLandmarkId.IndustrialEntrance);
            if (progression.GetMetric(ProgressMetricId.AssemblyPlantUnlocked) < 1L)
                return Next("UNLOCK ASSEMBLY PLANT", WorldLandmarkId.AssemblyPlant);
            if (progression.GetMetric(ProgressMetricId.MachinePartsProduced) < 1L)
                return Next("PRODUCE A MACHINE PART", WorldLandmarkId.AssemblyPlant);
            if (progression.GetMetric(ProgressMetricId.ProductionUpgradesPurchased) < 1L
                && progression.GetMetric(ProgressMetricId.LogisticsUpgradesPurchased) < 1L)
                return Next("PURCHASE AN INDUSTRIAL UPGRADE", WorldLandmarkId.AssemblyPlant);
            if (progression.GetMetric(ProgressMetricId.AdvancedManufacturingContractsCompleted) < 1L)
                return Next("COMPLETE AN ADVANCED CONTRACT", WorldLandmarkId.AssemblyPlant);

            M15FactorySaveData factories = factoryScaling ?? M15FactorySaveData.CreateFresh();
            int specialized = M15FactoryRules.GetSpecializedFactoryCount(factories);
            if (specialized < 1)
                return Next("SPECIALIZE A FACTORY", WorldLandmarkId.AssemblyPlant);
            if (specialized < 2)
                return Next("SPECIALIZE A SECOND FACTORY", WorldLandmarkId.AssemblyPlant);
            if (!factories.optimizationObjectiveCompleted)
                return Next("COMPLETE THE OPTIMIZATION OBJECTIVE", WorldLandmarkId.AssemblyPlant);
            if (specialized < 3)
                return Next("SPECIALIZE A THIRD FACTORY", WorldLandmarkId.AssemblyPlant);
            if (factories.industrialDistrictLevel < 3)
                return Next("COMPLETE 25 CYCLES IN TWO FACTORIES", WorldLandmarkId.AssemblyPlant);
            if (factories.upgradedRouteUnitsTransported < 50L)
                return new WorldGuidanceState(
                    $"OBJECTIVE: TRANSPORT ON UPGRADED ROUTES — "
                    + $"{factories.upgradedRouteUnitsTransported} / 50",
                    WorldLandmarkId.Warehouse);

            if (progression.GetMetric(ProgressMetricId.MarketUnlocked) < 1L)
                return Next("OPEN THE MARKET", WorldLandmarkId.Market);
            if (progression.GetMetric(ProgressMetricId.ProductionOrdersCompleted) < 1L)
                return Next("COMPLETE A PRODUCTION ORDER", WorldLandmarkId.Market);
            if (progression.GetMetric(ProgressMetricId.HighBoomSales) < 1L)
                return Next("SELL DURING HIGH OR BOOM DEMAND", WorldLandmarkId.Market);
            if (progression.GetMetric(ProgressMetricId.FlowBonusesTriggered) < 1L)
                return Next("TRIGGER INDUSTRIAL FLOW", WorldLandmarkId.AssemblyPlant);
            if (progression.GetMetric(ProgressMetricId.AdvancedMachinePartOrdersCompleted) < 1L)
                return Next("COMPLETE A MACHINE PART ORDER", WorldLandmarkId.Market);

            return new WorldGuidanceState(
                "EMPIRE LEVEL 25 — WORLD PROGRESSION COMPLETE",
                WorldLandmarkId.Market);
        }

        private static WorldGuidanceState Next(string action, WorldLandmarkId landmark)
        {
            return new WorldGuidanceState("OBJECTIVE: " + action, landmark);
        }

        private static WorldLandmarkId ResolveLegacyLandmark(MainObjectiveId objective)
        {
            switch (objective)
            {
                case MainObjectiveId.MineTenIronOre:
                    return WorldLandmarkId.MineEntrance;
                case MainObjectiveId.UnlockSmelter:
                case MainObjectiveId.ProduceFiveIronBars:
                case MainObjectiveId.UnlockAutomatedDrill:
                    return WorldLandmarkId.Smelter;
                case MainObjectiveId.UnlockToolWorkshop:
                case MainObjectiveId.ProduceFiveTools:
                case MainObjectiveId.ReachEmpireLevelFive:
                    return WorldLandmarkId.ToolWorkshop;
                default:
                    return WorldLandmarkId.LumberCamp;
            }
        }
    }
}
