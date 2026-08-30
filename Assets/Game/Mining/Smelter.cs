using IndustryTycoon.Core;
using IndustryTycoon.Processing;

namespace IndustryTycoon.Mining
{
    /// <summary>
    /// Mining-facing facade over the proven atomic buffered-recipe core used by
    /// <see cref="PackingStation"/>. The inherited serialized capacities and
    /// duration intentionally keep the same scene wiring contract.
    /// </summary>
    public sealed class Smelter : PackingStation
    {
        public int InputOre => InputPlanks;
        public int ProcessingInputOre => ProcessingInputPlanks;
        public int OutputBars => OutputCrates;
        public int RecipeInputOre => RecipeInputPlanks;
        public int RecipeOutputBars => RecipeOutputCrates;

        protected override ResourceType InputResourceType => ResourceType.IronOre;
        protected override ResourceType OutputResourceType => ResourceType.IronBar;
    }
}
