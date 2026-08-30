using System;

namespace IndustryTycoon.Progression
{
    // Values and stable IDs are append-only. New areas (for example Mining) can add
    // metrics without changing the store, save format, objectives, or reward logic.
    public enum ProgressMetricId
    {
        TotalCashEarned = 0,
        WoodCollected = 1,
        WoodProduced = 2,
        WoodSold = 3,
        PlanksProduced = 4,
        PlanksSold = 5,
        CratesProduced = 6,
        CratesSold = 7,
        CourierTripsCompleted = 8,
        CratesDelivered = 9,
        IronOreMined = 10,
        IronOreProduced = 11,
        IronOreSold = 12,
        IronBarsProduced = 13,
        IronBarsSold = 14,
        MineUnlocked = 15,
        DrillUnlocked = 16
    }

    public enum ProgressFlagId
    {
        ProductionUpgradeUnlocked = 0,
        WorkerUnlocked = 1,
        ProcessorUnlocked = 2,
        AutoFeederUnlocked = 3,
        PackingStationUnlocked = 4,
        CourierUnlocked = 5,
        LumberCampCompleted = 6,
        SmelterUnlocked = 7
    }

    public enum MainObjectiveId
    {
        UnlockWorker = 0,
        UnlockProcessor = 1,
        ProduceTenPlanks = 2,
        UnlockAutoFeeder = 3,
        UnlockPackingStation = 4,
        ProduceFiveCrates = 5,
        UnlockCourier = 6,
        CompleteFiveCourierDeliveries = 7,
        CompleteLumberCamp = 8,
        MineTenIronOre = 9,
        UnlockSmelter = 10,
        ProduceFiveIronBars = 11,
        UnlockAutomatedDrill = 12
    }

    public enum LumberCampContractId
    {
        SellTwentyWood = 0,
        ProduceFifteenPlanks = 1,
        SellTwentyPlanks = 2,
        ProduceTenCrates = 3,
        DeliverTenCrates = 4
    }

    public enum ContractProgressState
    {
        Active = 0,
        CompletedUnclaimed = 1,
        Claimed = 2
    }

    public enum LumberCampAchievementId
    {
        FirstSale = 0,
        FirstHire = 1,
        ProcessingBegins = 2,
        AutomationOnline = 3,
        PackedAndReady = 4,
        DeliveryService = 5,
        Lumberjack = 6,
        MassProduction = 7,
        PlankFactory = 8,
        CrateMaker = 9,
        OnTheRoad = 10,
        Merchant = 11,
        TycoonInTraining = 12,
        FullyAutomatedInput = 13,
        LumberCampComplete = 14
    }

    public enum ObjectiveConditionKind
    {
        Metric = 0,
        Flag = 1
    }

    public readonly struct MainObjectiveDefinition
    {
        public MainObjectiveDefinition(
            MainObjectiveId id,
            string label,
            ProgressMetricId metric,
            long target)
        {
            Id = id;
            Label = label;
            ConditionKind = ObjectiveConditionKind.Metric;
            Metric = metric;
            Flag = default;
            Target = Math.Max(1L, target);
        }

        public MainObjectiveDefinition(
            MainObjectiveId id,
            string label,
            ProgressFlagId flag)
        {
            Id = id;
            Label = label;
            ConditionKind = ObjectiveConditionKind.Flag;
            Metric = default;
            Flag = flag;
            Target = 1L;
        }

        public MainObjectiveId Id { get; }
        public string Label { get; }
        public ObjectiveConditionKind ConditionKind { get; }
        public ProgressMetricId Metric { get; }
        public ProgressFlagId Flag { get; }
        public long Target { get; }
    }

    public readonly struct LumberCampContractDefinition
    {
        public LumberCampContractDefinition(
            LumberCampContractId id,
            string stableId,
            string description,
            ProgressMetricId metric,
            long target,
            int rewardCash)
        {
            Id = id;
            StableId = stableId;
            Description = description;
            Metric = metric;
            Target = Math.Max(1L, target);
            RewardCash = Math.Max(1, rewardCash);
        }

        public LumberCampContractId Id { get; }
        public string StableId { get; }
        public string Description { get; }
        public ProgressMetricId Metric { get; }
        public long Target { get; }
        public int RewardCash { get; }
    }

    public readonly struct LumberCampAchievementDefinition
    {
        public LumberCampAchievementDefinition(
            LumberCampAchievementId id,
            string stableId,
            string name,
            string requirement,
            int rewardCash)
        {
            Id = id;
            StableId = stableId;
            Name = name;
            Requirement = requirement;
            RewardCash = Math.Max(1, rewardCash);
        }

        public LumberCampAchievementId Id { get; }
        public string StableId { get; }
        public string Name { get; }
        public string Requirement { get; }
        public int RewardCash { get; }
    }

    public static class LumberCampProgressionCatalog
    {
        private static readonly string[] MetricStableIds =
        {
            "total_cash_earned",
            "wood_collected",
            "wood_produced",
            "wood_sold",
            "planks_produced",
            "planks_sold",
            "crates_produced",
            "crates_sold",
            "courier_trips_completed",
            "crates_delivered",
            "iron_ore_mined",
            "iron_ore_produced",
            "iron_ore_sold",
            "iron_bars_produced",
            "iron_bars_sold",
            "mine_unlocked",
            "drill_unlocked"
        };

        private static readonly string[] FlagStableIds =
        {
            "production_upgrade_unlocked",
            "worker_unlocked",
            "processor_unlocked",
            "auto_feeder_unlocked",
            "packing_station_unlocked",
            "courier_unlocked",
            "lumber_camp_completed",
            "smelter_unlocked"
        };

        private static readonly MainObjectiveDefinition[] Objectives =
        {
            new MainObjectiveDefinition(
                MainObjectiveId.UnlockWorker,
                "UNLOCK WORKER",
                ProgressFlagId.WorkerUnlocked),
            new MainObjectiveDefinition(
                MainObjectiveId.UnlockProcessor,
                "UNLOCK PROCESSOR",
                ProgressFlagId.ProcessorUnlocked),
            new MainObjectiveDefinition(
                MainObjectiveId.ProduceTenPlanks,
                "PRODUCE PLANKS",
                ProgressMetricId.PlanksProduced,
                10L),
            new MainObjectiveDefinition(
                MainObjectiveId.UnlockAutoFeeder,
                "UNLOCK AUTO FEEDER",
                ProgressFlagId.AutoFeederUnlocked),
            new MainObjectiveDefinition(
                MainObjectiveId.UnlockPackingStation,
                "UNLOCK PACKING STATION",
                ProgressFlagId.PackingStationUnlocked),
            new MainObjectiveDefinition(
                MainObjectiveId.ProduceFiveCrates,
                "PRODUCE CRATES",
                ProgressMetricId.CratesProduced,
                5L),
            new MainObjectiveDefinition(
                MainObjectiveId.UnlockCourier,
                "UNLOCK COURIER",
                ProgressFlagId.CourierUnlocked),
            new MainObjectiveDefinition(
                MainObjectiveId.CompleteLumberCamp,
                "COMPLETE LUMBER CAMP",
                ProgressFlagId.LumberCampCompleted),
            new MainObjectiveDefinition(
                MainObjectiveId.CompleteFiveCourierDeliveries,
                "COMPLETE COURIER DELIVERIES",
                ProgressMetricId.CourierTripsCompleted,
                5L),
            new MainObjectiveDefinition(
                MainObjectiveId.MineTenIronOre,
                "MINE IRON ORE",
                ProgressMetricId.IronOreMined,
                10L),
            new MainObjectiveDefinition(
                MainObjectiveId.UnlockSmelter,
                "UNLOCK SMELTER",
                ProgressFlagId.SmelterUnlocked),
            new MainObjectiveDefinition(
                MainObjectiveId.ProduceFiveIronBars,
                "PRODUCE IRON BARS",
                ProgressMetricId.IronBarsProduced,
                5L),
            new MainObjectiveDefinition(
                MainObjectiveId.UnlockAutomatedDrill,
                "UNLOCK AUTOMATED DRILL",
                ProgressMetricId.DrillUnlocked,
                1L)
        };

        private static readonly LumberCampContractDefinition[] Contracts =
        {
            new LumberCampContractDefinition(
                LumberCampContractId.SellTwentyWood,
                "sell_20_wood",
                "SELL 20 WOOD",
                ProgressMetricId.WoodSold,
                20L,
                150),
            new LumberCampContractDefinition(
                LumberCampContractId.ProduceFifteenPlanks,
                "produce_15_planks",
                "PRODUCE 15 PLANKS",
                ProgressMetricId.PlanksProduced,
                15L,
                250),
            new LumberCampContractDefinition(
                LumberCampContractId.SellTwentyPlanks,
                "sell_20_planks",
                "SELL 20 PLANKS",
                ProgressMetricId.PlanksSold,
                20L,
                300),
            new LumberCampContractDefinition(
                LumberCampContractId.ProduceTenCrates,
                "produce_10_crates",
                "PRODUCE 10 CRATES",
                ProgressMetricId.CratesProduced,
                10L,
                400),
            new LumberCampContractDefinition(
                LumberCampContractId.DeliverTenCrates,
                "deliver_10_crates",
                "DELIVER 10 CRATES",
                ProgressMetricId.CratesDelivered,
                10L,
                600)
        };

        private static readonly LumberCampAchievementDefinition[] Achievements =
        {
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.FirstSale,
                "first_sale",
                "First Sale",
                "Sell any resource once",
                50),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.FirstHire,
                "first_hire",
                "First Hire",
                "Unlock Worker",
                75),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.ProcessingBegins,
                "processing_begins",
                "Processing Begins",
                "Unlock Processor",
                100),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.AutomationOnline,
                "automation_online",
                "Automation Online",
                "Unlock Auto Feeder",
                125),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.PackedAndReady,
                "packed_and_ready",
                "Packed & Ready",
                "Produce the first Crate",
                150),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.DeliveryService,
                "delivery_service",
                "Delivery Service",
                "Unlock Courier",
                200),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.Lumberjack,
                "lumberjack",
                "Lumberjack",
                "Collect 100 Wood",
                150),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.MassProduction,
                "mass_production",
                "Mass Production",
                "Produce 100 Wood",
                150),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.PlankFactory,
                "plank_factory",
                "Plank Factory",
                "Produce 50 Planks",
                200),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.CrateMaker,
                "crate_maker",
                "Crate Maker",
                "Produce 25 Crates",
                250),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.OnTheRoad,
                "on_the_road",
                "On The Road",
                "Complete 10 Courier trips",
                250),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.Merchant,
                "merchant",
                "Merchant",
                "Earn $2,500 gameplay Cash",
                300),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.TycoonInTraining,
                "tycoon_in_training",
                "Tycoon in Training",
                "Earn $10,000 gameplay Cash",
                500),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.FullyAutomatedInput,
                "fully_automated_input",
                "Fully Automated Input",
                "Unlock Worker and Auto Feeder",
                250),
            new LumberCampAchievementDefinition(
                LumberCampAchievementId.LumberCampComplete,
                "lumber_camp_complete",
                "Lumber Camp Complete",
                "Complete Lumber Camp",
                500)
        };

        public static int MetricCount => MetricStableIds.Length;
        public static int FlagCount => FlagStableIds.Length;
        public static int ObjectiveCount => Objectives.Length;
        public static int ContractCount => Contracts.Length;
        public static int AchievementCount => Achievements.Length;

        public static string GetMetricStableId(ProgressMetricId id)
        {
            int index = (int)id;
            if (index < 0 || index >= MetricStableIds.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            return MetricStableIds[index];
        }

        public static bool TryGetMetricId(string stableId, out ProgressMetricId id)
        {
            for (int i = 0; i < MetricStableIds.Length; i++)
            {
                if (string.Equals(MetricStableIds[i], stableId, StringComparison.Ordinal))
                {
                    id = (ProgressMetricId)i;
                    return true;
                }
            }

            id = default;
            return false;
        }

        public static string GetFlagStableId(ProgressFlagId id)
        {
            int index = (int)id;
            if (index < 0 || index >= FlagStableIds.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            return FlagStableIds[index];
        }

        public static bool TryGetFlagId(string stableId, out ProgressFlagId id)
        {
            for (int i = 0; i < FlagStableIds.Length; i++)
            {
                if (string.Equals(FlagStableIds[i], stableId, StringComparison.Ordinal))
                {
                    id = (ProgressFlagId)i;
                    return true;
                }
            }

            id = default;
            return false;
        }

        public static MainObjectiveDefinition GetObjective(int index)
        {
            if (index < 0 || index >= Objectives.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Objectives[index];
        }

        public static LumberCampContractDefinition GetContract(int index)
        {
            if (index < 0 || index >= Contracts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Contracts[index];
        }

        public static LumberCampAchievementDefinition GetAchievement(int index)
        {
            if (index < 0 || index >= Achievements.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Achievements[index];
        }

        public static bool TryGetAchievementIndex(string stableId, out int index)
        {
            for (int i = 0; i < Achievements.Length; i++)
            {
                if (string.Equals(
                        Achievements[i].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }
    }
}
