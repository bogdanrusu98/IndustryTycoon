using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class DistrictStatusView : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private Text statusText;

        public Text StatusText => statusText;

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
            M10ProgressionSaveData state = progression != null
                ? progression.CapturePersistentState()
                : M10ProgressionSaveData.CreateFresh();
            int level = progression != null ? progression.EmpireLevel : 1;
            if (statusText != null) statusText.text = BuildDisplayText(state, level);
        }

        public static string BuildDisplayText(M10ProgressionSaveData state, int level)
        {
            return "LUMBER OPEN  •  MINING " + State(WorldDistrictId.Mining, state, level)
                   + "  •  LOGISTICS " + State(WorldDistrictId.CentralLogistics, state, level)
                   + "  •  INDUSTRIAL " + State(WorldDistrictId.Industrial, state, level);
        }

        private static string State(WorldDistrictId district, M10ProgressionSaveData state, int level)
        {
            return WorldProgressionGuidance.IsDistrictUnlocked(district, state, level)
                ? "OPEN"
                : "LOCKED";
        }
    }
}
