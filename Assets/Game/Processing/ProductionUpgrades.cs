using System;

namespace IndustryTycoon.Processing
{
    public enum ProductionUpgradeCategory
    {
        Speed = 0,
        InputCapacity = 1,
        OutputCapacity = 2
    }

    public interface IProductionUpgradeable
    {
        string ProductionBuildingId { get; }
        int SpeedUpgradeLevel { get; }
        int InputCapacityUpgradeLevel { get; }
        int OutputCapacityUpgradeLevel { get; }
        bool ApplyUpgradeLevels(int speed, int input, int output);
    }

    public interface IFactorySpecializable
    {
        FactorySpecialization Specialization { get; }
        int IndustrialDistrictLevel { get; }
        int EfficiencyCompletedRemainder { get; }
        long CompletedSpecializedCycles { get; }
        void ApplyFactorySpecialization(FactorySpecialization specialization, int districtLevel);
        void RestoreFactoryProgress(int efficiencyRemainder, long completedCycles);
    }

    public static class M14ProductionUpgradeTuning
    {
        public const int MaximumLevel = 3;

        public static int GetCost(ProductionUpgradeCategory category, int nextLevel)
        {
            if (nextLevel < 1 || nextLevel > MaximumLevel)
            {
                return int.MaxValue;
            }

            int baseCost = category == ProductionUpgradeCategory.Speed
                ? 1800
                : category == ProductionUpgradeCategory.InputCapacity
                    ? 1400
                    : 1600;
            return baseCost * nextLevel * nextLevel;
        }

        public static float GetDurationMultiplier(int level)
        {
            level = ClampLevel(level);
            return 1f - (0.15f * level);
        }

        public static int GetCapacityBonus(int baseCapacity, int level)
        {
            level = ClampLevel(level);
            return (int)Math.Floor(baseCapacity * 0.5d * level);
        }

        public static int ClampLevel(int level)
        {
            return Math.Max(0, Math.Min(MaximumLevel, level));
        }
    }
}
