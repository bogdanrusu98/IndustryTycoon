using System;
using System.Collections.Generic;
using IndustryTycoon.Processing;

namespace IndustryTycoon.Persistence
{
    [Serializable]
    public sealed class M14BuildingUpgradeSaveRecord
    {
        public string buildingId;
        public int speedLevel;
        public int inputCapacityLevel;
        public int outputCapacityLevel;
    }

    [Serializable]
    public sealed class M14IndustrialSaveData
    {
        public bool districtEntered;
        public M9PurchasePadSaveRecord assemblyPlantPurchase;
        public int assemblyInputIronBars;
        public int assemblyInputTools;
        public int assemblyOutputMachineParts;
        public M14BuildingUpgradeSaveRecord[] buildingUpgrades;

        public static M14IndustrialSaveData CreateFresh()
        {
            return new M14IndustrialSaveData
            {
                assemblyPlantPurchase = new M9PurchasePadSaveRecord
                {
                    id = M14IndustrialTuning.AssemblyPlantPurchaseId
                },
                buildingUpgrades = new[]
                {
                    NewUpgrade("wood_processor"),
                    NewUpgrade("iron_smelter"),
                    NewUpgrade("tool_workshop"),
                    NewUpgrade(AssemblyPlant.StableEndpointId)
                }
            };
        }

        public M14BuildingUpgradeSaveRecord GetBuildingUpgrade(string id)
        {
            if (buildingUpgrades == null) return null;
            for (int i = 0; i < buildingUpgrades.Length; i++)
                if (buildingUpgrades[i] != null
                    && string.Equals(buildingUpgrades[i].buildingId, id,
                        StringComparison.Ordinal)) return buildingUpgrades[i];
            return null;
        }

        private static M14BuildingUpgradeSaveRecord NewUpgrade(string id) =>
            new M14BuildingUpgradeSaveRecord { buildingId = id };
    }

    public static class M14IndustrialTuning
    {
        public const string AssemblyPlantPurchaseId = "assembly_plant";
        public const int AssemblyPlantUnlockCost = 7500;
    }

    public static class M14IndustrialSaveValidator
    {
        private static readonly string[] BuildingIds =
        {
            "wood_processor", "iron_smelter", "tool_workshop", "assembly_plant"
        };

        public static bool TryNormalize(M14IndustrialSaveData source,
            out M14IndustrialSaveData normalized, out string failure)
        {
            normalized = null;
            failure = null;
            if (source?.assemblyPlantPurchase == null
                || source.buildingUpgrades == null
                || source.buildingUpgrades.Length != BuildingIds.Length
                || !string.Equals(source.assemblyPlantPurchase.id,
                    M14IndustrialTuning.AssemblyPlantPurchaseId,
                    StringComparison.Ordinal))
            {
                failure = "M14 industrial state has an invalid canonical shape.";
                return false;
            }

            bool completed = source.assemblyPlantPurchase.completed;
            int paid = completed ? M14IndustrialTuning.AssemblyPlantUnlockCost
                : Clamp(source.assemblyPlantPurchase.paidCash, 0,
                    M14IndustrialTuning.AssemblyPlantUnlockCost - 1);
            if (completed && !source.districtEntered)
            {
                failure = "Assembly Plant ownership requires district entry.";
                return false;
            }

            normalized = M14IndustrialSaveData.CreateFresh();
            normalized.districtEntered = source.districtEntered;
            normalized.assemblyPlantPurchase.paidCash = paid;
            normalized.assemblyPlantPurchase.completed = completed;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.buildingUpgrades.Length; i++)
            {
                M14BuildingUpgradeSaveRecord record = source.buildingUpgrades[i];
                if (record == null || Array.IndexOf(BuildingIds, record.buildingId) < 0
                    || !seen.Add(record.buildingId))
                {
                    failure = "M14 building upgrade IDs are missing or duplicated.";
                    normalized = null;
                    return false;
                }
                M14BuildingUpgradeSaveRecord target = normalized.GetBuildingUpgrade(record.buildingId);
                target.speedLevel = M14ProductionUpgradeTuning.ClampLevel(record.speedLevel);
                target.inputCapacityLevel = M14ProductionUpgradeTuning.ClampLevel(record.inputCapacityLevel);
                target.outputCapacityLevel = M14ProductionUpgradeTuning.ClampLevel(record.outputCapacityLevel);
            }

            M14BuildingUpgradeSaveRecord assembly = normalized.GetBuildingUpgrade("assembly_plant");
            int barCapacity = 24 + M14ProductionUpgradeTuning.GetCapacityBonus(
                24, assembly.inputCapacityLevel);
            int toolCapacity = 12 + M14ProductionUpgradeTuning.GetCapacityBonus(
                12, assembly.inputCapacityLevel);
            int outputCapacity = 12 + M14ProductionUpgradeTuning.GetCapacityBonus(
                12, assembly.outputCapacityLevel);
            normalized.assemblyInputIronBars = Clamp(source.assemblyInputIronBars, 0, barCapacity);
            normalized.assemblyInputTools = Clamp(source.assemblyInputTools, 0, toolCapacity);
            normalized.assemblyOutputMachineParts = Clamp(
                source.assemblyOutputMachineParts, 0, outputCapacity);
            if (!completed && (normalized.assemblyInputIronBars > 0
                               || normalized.assemblyInputTools > 0
                               || normalized.assemblyOutputMachineParts > 0))
            {
                failure = "Locked Assembly Plant cannot own resources.";
                normalized = null;
                return false;
            }
            return true;
        }

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;
    }

    public static class M14OfflineProductionCalculator
    {
        public static int Settle(M9SaveData data, long creditedAwaySeconds, double efficiency)
        {
            if (data?.industrial?.assemblyPlantPurchase == null
                || !data.industrial.assemblyPlantPurchase.completed
                || data.returnScreenPending || creditedAwaySeconds <= 0L || efficiency <= 0d)
                return 0;
            M14BuildingUpgradeSaveRecord upgrade = data.industrial.GetBuildingUpgrade(
                AssemblyPlant.StableEndpointId);
            double duration = 3d * M14ProductionUpgradeTuning.GetDurationMultiplier(
                upgrade?.speedLevel ?? 0);
            M15FactoryRecord factory = data.factoryScaling?.GetFactory(AssemblyPlant.StableEndpointId);
            duration = M15FactoryTuning.CalculateDuration(3f, upgrade?.speedLevel ?? 0,
                factory?.specialization ?? FactorySpecialization.None,
                AssemblyPlant.StableEndpointId,
                data.factoryScaling?.industrialDistrictLevel ?? 1);
            long timeBudget = (long)Math.Floor(
                creditedAwaySeconds * Math.Min(1d, Math.Max(0d, efficiency)) / duration);
            int outputCapacity = 12 + M14ProductionUpgradeTuning.GetCapacityBonus(
                12, upgrade?.outputCapacityLevel ?? 0);
            int recipes = (int)Math.Min(int.MaxValue, Math.Min(timeBudget,
                Math.Min(data.industrial.assemblyInputIronBars / 2,
                    Math.Min(data.industrial.assemblyInputTools,
                        outputCapacity - data.industrial.assemblyOutputMachineParts))));
            if (recipes <= 0) return 0;
            int conserved = 0;
            for (int i = 0; i < recipes; i++)
                if (M15FactoryRules.CompleteRecipe(factory, out bool conserve) && conserve) conserved++;
            data.industrial.assemblyInputIronBars -= recipes * 2 - conserved;
            data.industrial.assemblyInputTools -= recipes;
            data.industrial.assemblyOutputMachineParts += recipes;
            return recipes;
        }
    }
}
