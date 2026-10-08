using System;
using IndustryTycoon.Persistence;

namespace IndustryTycoon.Progression
{
    public enum FirstSessionStep
    {
        MoveToWood = 0,
        CollectWood = 1,
        SellWood = 2,
        UnlockProduction = 3,
        UnlockWorker = 4,
        UseStockpile = 5,
        UnlockProcessor = 6,
        ProducePlank = 7,
        UnlockAutomation = 8,
        ProduceCrate = 9,
        UnlockCourier = 10,
        CompleteLumberMilestone = 11,
        Complete = 12
    }

    public enum FirstTimeExplanation
    {
        Mining = 0,
        Smelter = 1,
        ToolWorkshop = 2,
        Warehouse = 3,
        LogisticsRoutes = 4,
        DailyWeeklyGoals = 5,
        IndustrialDistrict = 6,
        AssemblyPlant = 7,
        BuildingUpgrades = 8,
        Specializations = 9,
        MarketDemand = 10,
        ProductionOrders = 11
    }

    [Serializable]
    public sealed class M18OnboardingSaveData
    {
        public int currentStep;
        public int completedStepsMask;
        public int explanationsSeenMask;
        public bool tutorialStarted;
        public bool tutorialSkipped;

        public static M18OnboardingSaveData CreateFresh()
        {
            return new M18OnboardingSaveData
            {
                currentStep = (int)FirstSessionStep.MoveToWood
            };
        }

        public M18OnboardingSaveData DeepClone()
        {
            return new M18OnboardingSaveData
            {
                currentStep = currentStep,
                completedStepsMask = completedStepsMask,
                explanationsSeenMask = explanationsSeenMask,
                tutorialStarted = tutorialStarted,
                tutorialSkipped = tutorialSkipped
            };
        }
    }

    public static class M18OnboardingRules
    {
        public const int StepCount = (int)FirstSessionStep.Complete;
        public const int ExplanationCount = 12;
        public const int AllStepsMask = (1 << StepCount) - 1;
        public const int AllExplanationsMask = (1 << ExplanationCount) - 1;

        public static M18OnboardingSaveData Normalize(M18OnboardingSaveData source)
        {
            M18OnboardingSaveData result = source?.DeepClone()
                                             ?? M18OnboardingSaveData.CreateFresh();
            result.completedStepsMask &= AllStepsMask;
            result.explanationsSeenMask &= AllExplanationsMask;

            int contiguousStep = 0;
            while (contiguousStep < StepCount
                   && (result.completedStepsMask & (1 << contiguousStep)) != 0)
            {
                contiguousStep++;
            }

            // Never accept holes or a current step beyond the authoritative chain.
            result.completedStepsMask = contiguousStep == 0
                ? 0
                : (1 << contiguousStep) - 1;
            result.currentStep = contiguousStep;
            if (result.tutorialSkipped)
            {
                result.completedStepsMask = AllStepsMask;
                result.currentStep = StepCount;
                result.tutorialStarted = true;
            }
            else if (result.currentStep >= StepCount)
            {
                result.currentStep = StepCount;
                result.completedStepsMask = AllStepsMask;
            }

            return result;
        }

        public static bool IsCompleted(M18OnboardingSaveData state, FirstSessionStep step)
        {
            int index = (int)step;
            return state != null
                   && index >= 0
                   && index < StepCount
                   && (state.completedStepsMask & (1 << index)) != 0;
        }

        public static string GetInstruction(FirstSessionStep step)
        {
            switch (step)
            {
                case FirstSessionStep.MoveToWood:
                    return "DRAG TO MOVE — GO TO THE WOOD";
                case FirstSessionStep.CollectWood:
                    return "WALK OVER WOOD TO PICK IT UP";
                case FirstSessionStep.SellWood:
                    return "TAKE WOOD TO SELL — EARN CASH";
                case FirstSessionStep.UnlockProduction:
                    return "BUY THE PRODUCTION UPGRADE";
                case FirstSessionStep.UnlockWorker:
                    return "SELL WOOD, THEN UNLOCK THE WORKER";
                case FirstSessionStep.UseStockpile:
                    return "WORKER STORES WOOD — COLLECT AT STOCKPILE";
                case FirstSessionStep.UnlockProcessor:
                    return "UNLOCK THE PROCESSOR";
                case FirstSessionStep.ProducePlank:
                    return "WOOD IN → COLLECT YOUR FIRST PLANK OUT";
                case FirstSessionStep.UnlockAutomation:
                    return "UNLOCK AUTO FEEDER";
                case FirstSessionStep.ProduceCrate:
                    return "UNLOCK PACKING, PLANKS IN → FIRST CRATE OUT";
                case FirstSessionStep.UnlockCourier:
                    return "UNLOCK COURIER FOR CRATE DELIVERY";
                case FirstSessionStep.CompleteLumberMilestone:
                    return "LET THE COURIER DELIVER THE FIRST CRATE";
                default:
                    return string.Empty;
            }
        }

        public static string GetCompletionMessage(FirstSessionStep step)
        {
            switch (step)
            {
                case FirstSessionStep.MoveToWood: return "MOVEMENT READY";
                case FirstSessionStep.CollectWood: return "WOOD COLLECTED";
                case FirstSessionStep.SellWood: return "FIRST CASH EARNED";
                case FirstSessionStep.UnlockProduction: return "PRODUCTION BOOSTED";
                case FirstSessionStep.UnlockWorker: return "WORKER HIRED";
                case FirstSessionStep.UseStockpile: return "STOCKPILE CONNECTED";
                case FirstSessionStep.UnlockProcessor: return "PROCESSOR ONLINE";
                case FirstSessionStep.ProducePlank: return "FIRST PLANK READY";
                case FirstSessionStep.UnlockAutomation: return "AUTOMATION ONLINE";
                case FirstSessionStep.ProduceCrate: return "FIRST CRATE READY";
                case FirstSessionStep.UnlockCourier: return "COURIER HIRED";
                case FirstSessionStep.CompleteLumberMilestone: return "LUMBER MILESTONE COMPLETE";
                default: return string.Empty;
            }
        }

        public static string GetExplanation(FirstTimeExplanation explanation)
        {
            switch (explanation)
            {
                case FirstTimeExplanation.Mining: return "MINING — COLLECT IRON ORE IN THE NEW DISTRICT";
                case FirstTimeExplanation.Smelter: return "SMELTER — IRON ORE IN, IRON BARS OUT";
                case FirstTimeExplanation.ToolWorkshop: return "TOOL WORKSHOP — COMBINE PLANKS AND IRON BARS";
                case FirstTimeExplanation.Warehouse: return "WAREHOUSE — SHARED STORAGE FOR EVERY INDUSTRY";
                case FirstTimeExplanation.LogisticsRoutes: return "LOGISTICS ROUTES — MOVE GOODS BETWEEN BUILDINGS";
                case FirstTimeExplanation.DailyWeeklyGoals: return "GOALS — OPTIONAL DAILY AND WEEKLY CASH TARGETS";
                case FirstTimeExplanation.IndustrialDistrict: return "INDUSTRIAL DISTRICT — ADVANCED PRODUCTION IS NOW OPEN";
                case FirstTimeExplanation.AssemblyPlant: return "ASSEMBLY PLANT — COMBINE INDUSTRIAL INPUTS";
                case FirstTimeExplanation.BuildingUpgrades: return "BUILDING UPGRADES — IMPROVE SPEED OR CAPACITY";
                case FirstTimeExplanation.Specializations: return "SPECIALIZATIONS — PERMANENT FACTORY TRADE-OFFS";
                case FirstTimeExplanation.MarketDemand: return "MARKET DEMAND — PRICES CHANGE BY RESOURCE";
                case FirstTimeExplanation.ProductionOrders: return "PRODUCTION ORDERS — ACCEPT, PRODUCE, THEN CLAIM";
                default: return string.Empty;
            }
        }

        public static bool IsExplanationUnlocked(
            FirstTimeExplanation explanation,
            M10ProgressionSaveData progression,
            int empireLevel)
        {
            if (progression == null) return false;
            switch (explanation)
            {
                case FirstTimeExplanation.Mining:
                    return progression.GetFlag(ProgressFlagId.LumberCampCompleted);
                case FirstTimeExplanation.Smelter:
                    return progression.GetFlag(ProgressFlagId.SmelterUnlocked);
                case FirstTimeExplanation.ToolWorkshop:
                    return progression.GetMetric(ProgressMetricId.ToolWorkshopUnlocked) > 0L;
                case FirstTimeExplanation.Warehouse:
                case FirstTimeExplanation.DailyWeeklyGoals:
                    return progression.GetMetric(ProgressMetricId.WarehouseUnlocked) > 0L;
                case FirstTimeExplanation.LogisticsRoutes:
                    return progression.GetMetric(ProgressMetricId.IndustrialRouteOperational) > 0L;
                case FirstTimeExplanation.IndustrialDistrict:
                    return progression.GetMetric(ProgressMetricId.IndustrialDistrictEntered) > 0L;
                case FirstTimeExplanation.AssemblyPlant:
                    return progression.GetMetric(ProgressMetricId.AssemblyPlantUnlocked) > 0L;
                case FirstTimeExplanation.BuildingUpgrades:
                    return progression.GetMetric(ProgressMetricId.ProductionUpgradesPurchased) > 0L
                           || progression.GetMetric(ProgressMetricId.LogisticsUpgradesPurchased) > 0L;
                case FirstTimeExplanation.Specializations:
                    return empireLevel >= 16;
                case FirstTimeExplanation.MarketDemand:
                case FirstTimeExplanation.ProductionOrders:
                    return progression.GetMetric(ProgressMetricId.MarketUnlocked) > 0L;
                default:
                    return false;
            }
        }
    }

    public readonly struct InternalAnalyticsEvent
    {
        public InternalAnalyticsEvent(string name, string parameter = "")
        {
            Name = name ?? string.Empty;
            Parameter = parameter ?? string.Empty;
        }

        public string Name { get; }
        public string Parameter { get; }
    }

    /// <summary>Internal-only analytics seam. No SDK, persistence, or network transport.</summary>
    public static class InternalAnalytics
    {
        public const string TutorialStarted = "tutorial_started";
        public const string TutorialStepCompleted = "tutorial_step_completed";
        public const string TutorialCompleted = "tutorial_completed";
        public const string FirstWorker = "first_worker";
        public const string FirstProcessor = "first_processor";
        public const string FirstAutomation = "first_automation";
        public const string FirstCrate = "first_crate";
        public const string FirstCourier = "first_courier";
        public const string FirstSessionProgression = "first_session_progression";

        public static event Action<InternalAnalyticsEvent> EventPublished;

        public static void Publish(string name, string parameter = "")
        {
            EventPublished?.Invoke(new InternalAnalyticsEvent(name, parameter));
        }
    }
}
