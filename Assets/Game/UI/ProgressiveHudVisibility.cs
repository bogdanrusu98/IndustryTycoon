using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.UI
{
    public sealed class ProgressiveHudVisibility : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private GameObject progressionTabs;
        [SerializeField] private GameObject industryButton;

        private void OnEnable()
        {
            if (progression != null) progression.StateChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (progression != null) progression.StateChanged -= Refresh;
        }

        public void Refresh()
        {
            bool firstSale = progression != null
                             && progression.GetMetric(ProgressMetricId.WoodSold) > 0L;
            bool warehouse = progression != null
                             && progression.GetMetric(ProgressMetricId.WarehouseUnlocked) > 0L;
            progressionTabs?.SetActive(firstSale);
            industryButton?.SetActive(warehouse);
        }
    }
}
