using IndustryTycoon.Logistics;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.UI
{
    public sealed class IndustrialDistrictView : MonoBehaviour
    {
        [SerializeField] private IndustrialDistrictService district;
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private IndustrialLogisticsNetwork logistics;
        private string _buildingId = AssemblyPlant.StableEndpointId;
        private static readonly string[] BuildingIds =
        {
            "wood_processor", "iron_smelter", "tool_workshop", "assembly_plant"
        };
        private int _buildingIndex = 3;

        private void OnGUI()
        {
            if (district == null || progression == null || !district.DistrictEntered) return;
            float width = Mathf.Min(340f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12f, Screen.height * 0.48f, width, Screen.height * 0.48f),
                GUI.skin.box);
            GUILayout.Label($"INDUSTRIAL DISTRICT L{district.DistrictLevel}  •  EMPIRE {district.EffectiveEmpireLevel}/20");
            GUILayout.Label($"{_buildingId}: {district.GetProductionStatus(_buildingId)}");
            AssemblyPlant plant = district.AssemblyPlant;
            if (plant == null || !district.AssemblyPlantUnlocked)
            {
                GUILayout.Label($"Assembly Plant locked — ${Persistence.M14IndustrialTuning.AssemblyPlantUnlockCost}");
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label($"Assembly: Bars {plant.InputIronBars}/{plant.IronBarInputCapacity}  "
                            + $"Tools {plant.InputTools}/{plant.ToolInputCapacity}  "
                            + $"Parts {plant.OutputMachineParts}/{plant.OutputCapacity}");
            GUILayout.Label("2 Iron Bars + 1 Tool → 1 Machine Part ($250)");
            if (GUILayout.Button("UPGRADE TARGET: " + _buildingId.Replace('_', ' ').ToUpperInvariant()))
            {
                _buildingIndex = (_buildingIndex + 1) % BuildingIds.Length;
                _buildingId = BuildingIds[_buildingIndex];
            }
            if (GUILayout.Button("Production Speed " + Cost(ProductionUpgradeCategory.Speed)))
                district.TryPurchaseProductionUpgrade(_buildingId, ProductionUpgradeCategory.Speed);
            if (GUILayout.Button("Input Capacity " + Cost(ProductionUpgradeCategory.InputCapacity)))
                district.TryPurchaseProductionUpgrade(_buildingId, ProductionUpgradeCategory.InputCapacity);
            if (GUILayout.Button("Output Capacity " + Cost(ProductionUpgradeCategory.OutputCapacity)))
                district.TryPurchaseProductionUpgrade(_buildingId, ProductionUpgradeCategory.OutputCapacity);
            GUILayout.Label("Specialization (permanent)");
            if (GUILayout.Button("Throughput  $" + district.GetSpecializationCost(_buildingId)))
                district.TryPurchaseSpecialization(_buildingId, FactorySpecialization.Throughput);
            if (GUILayout.Button("Efficiency  $" + district.GetSpecializationCost(_buildingId)))
                district.TryPurchaseSpecialization(_buildingId, FactorySpecialization.Efficiency);
            IndustrialLogisticsRoute route = FindFirstRoute();
            if (route != null)
            {
                if (GUILayout.Button("Route Transport Capacity"))
                    district.TryPurchaseRouteUpgrade(route.RouteId, true);
                if (GUILayout.Button("Route Transfer Speed"))
                    district.TryPurchaseRouteUpgrade(route.RouteId, false);
            }
            GUILayout.EndArea();
        }

        private IndustrialLogisticsRoute FindFirstRoute()
        {
            IndustrialLogisticsRoute[] routes = logistics?.Routes;
            if (routes == null) return null;
            for (int i = 0; i < routes.Length; i++)
                if (routes[i] != null && routes[i].RouteId.Contains("assembly")) return routes[i];
            return null;
        }

        private string Cost(ProductionUpgradeCategory category)
        {
            int cost = district.GetProductionUpgradeCost(_buildingId, category);
            return cost == int.MaxValue ? "MAX" : "$" + cost;
        }
    }
}
