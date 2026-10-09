using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.UI
{
    public sealed class AdvancedUiVisibility : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private GameObject routesButton;
        [SerializeField] private GameObject productionButton;
        [SerializeField] private GameObject marketButton;

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
            int level = progression != null ? progression.EmpireLevel : 1;
            routesButton?.SetActive(level >= 10);
            productionButton?.SetActive(level >= 14);
            marketButton?.SetActive(level >= 20);
        }
    }
}
