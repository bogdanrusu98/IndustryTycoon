using System;
using IndustryTycoon.Core;

namespace IndustryTycoon.Processing
{
    public enum FactorySpecialization { None = 0, Throughput = 1, Efficiency = 2 }
    public enum ProductionStatus { LockedInactive = 0, Running = 1, MissingInput = 2, OutputFull = 3, WaitingForLogistics = 4 }
    public readonly struct FactorySpecializationDefinition
    {
        public FactorySpecializationDefinition(string id, float throughput, ResourceType conserved, int interval, int cost)
        { BuildingId = id; ThroughputMultiplier = throughput; ConservedInput = conserved; ConservationInterval = interval; Cost = cost; }
        public string BuildingId { get; } public float ThroughputMultiplier { get; }
        public ResourceType ConservedInput { get; } public int ConservationInterval { get; } public int Cost { get; }
    }
    public static class M15FactoryTuning
    {
        public const float DistrictLevel2DurationMultiplier = .95f;
        public const float DistrictLevel3RouteIntervalMultiplier = .90f;
        public const float MinimumDuration = .02f;
        private static readonly FactorySpecializationDefinition[] Definitions = {
            new FactorySpecializationDefinition("wood_processor", .80f, ResourceType.Wood, 5, 3000),
            new FactorySpecializationDefinition("iron_smelter", .80f, ResourceType.IronOre, 5, 4500),
            new FactorySpecializationDefinition("tool_workshop", .82f, ResourceType.Plank, 5, 7500),
            new FactorySpecializationDefinition("assembly_plant", .85f, ResourceType.IronBar, 5, 12000) };
        public static bool TryGet(string id, out FactorySpecializationDefinition definition)
        { for (int i=0;i<Definitions.Length;i++) if (string.Equals(Definitions[i].BuildingId,id,StringComparison.Ordinal)) { definition=Definitions[i]; return true; } definition=default; return false; }
        public static float CalculateDuration(float baseSeconds, int speedLevel, FactorySpecialization specialization, string id, int districtLevel)
        { float r=Math.Max(MinimumDuration,baseSeconds)*M14ProductionUpgradeTuning.GetDurationMultiplier(speedLevel); if(specialization==FactorySpecialization.Throughput&&TryGet(id,out var d))r*=d.ThroughputMultiplier;if(districtLevel>=2)r*=DistrictLevel2DurationMultiplier;return Math.Max(MinimumDuration,r); }
        public static float CalculateRouteInterval(float baseSeconds,int speedLevel,int districtLevel)
        { float r=Math.Max(.1f,baseSeconds);for(int i=0;i<Math.Max(0,Math.Min(3,speedLevel));i++)r*=.8f;if(districtLevel>=3)r*=DistrictLevel3RouteIntervalMultiplier;return Math.Max(.1f,r); }
    }
    public static class ProductionStatusRules
    {
        public static ProductionStatus Derive(bool unlocked,bool active,bool processing,bool hasInputs,bool hasOutputSpace,bool logisticsExpected)
        { if(!unlocked||!active)return ProductionStatus.LockedInactive;if(processing)return ProductionStatus.Running;if(!hasOutputSpace)return logisticsExpected?ProductionStatus.WaitingForLogistics:ProductionStatus.OutputFull;if(!hasInputs)return ProductionStatus.MissingInput;return ProductionStatus.Running; }
    }
}
