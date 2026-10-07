using System;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.UI
{
    [Serializable]
    public sealed class WorldDistrictPresentationBinding
    {
        [SerializeField] private WorldDistrictId district;
        [SerializeField] private TextMesh stateLabel;
        [SerializeField] private GameObject lockedVisual;
        [SerializeField] private GameObject unlockedVisual;

        public WorldDistrictPresentationBinding(
            WorldDistrictId district,
            TextMesh stateLabel,
            GameObject lockedVisual,
            GameObject unlockedVisual)
        {
            this.district = district;
            this.stateLabel = stateLabel;
            this.lockedVisual = lockedVisual;
            this.unlockedVisual = unlockedVisual;
        }

        public WorldDistrictId District => district;
        public TextMesh StateLabel => stateLabel;
        public GameObject LockedVisual => lockedVisual;
        public GameObject UnlockedVisual => unlockedVisual;
    }

    [Serializable]
    public sealed class WorldLandmarkMarkerBinding
    {
        [SerializeField] private WorldLandmarkId landmark;
        [SerializeField] private GameObject marker;

        public WorldLandmarkMarkerBinding(WorldLandmarkId landmark, GameObject marker)
        {
            this.landmark = landmark;
            this.marker = marker;
        }

        public WorldLandmarkId Landmark => landmark;
        public GameObject Marker => marker;
    }

    /// <summary>
    /// Event-driven world presentation derived from progression. It never stores
    /// independent unlock flags and never replaces authoritative gameplay gates.
    /// </summary>
    public sealed class WorldProgressionPresentation : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private WorldDistrictPresentationBinding[] districts;
        [SerializeField] private WorldLandmarkMarkerBinding[] landmarks;

        public LumberCampProgressionService Progression => progression;
        public int RefreshCount { get; private set; }

        public void Configure(
            LumberCampProgressionService progressionService,
            WorldDistrictPresentationBinding[] districtBindings,
            WorldLandmarkMarkerBinding[] landmarkBindings)
        {
            progression = progressionService;
            districts = districtBindings;
            landmarks = landmarkBindings;
        }

        private void OnEnable()
        {
            if (progression != null)
            {
                progression.StateChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (progression != null)
            {
                progression.StateChanged -= Refresh;
            }
        }

        public void Refresh()
        {
            int empireLevel = progression != null ? progression.EmpireLevel : 1;
            M10ProgressionSaveData state = progression != null
                ? progression.CapturePersistentState()
                : M10ProgressionSaveData.CreateFresh();

            if (districts != null)
            {
                for (int i = 0; i < districts.Length; i++)
                {
                    WorldDistrictPresentationBinding binding = districts[i];
                    if (binding == null) continue;
                    bool unlocked = WorldProgressionGuidance.IsDistrictUnlocked(
                        binding.District,
                        state,
                        empireLevel);
                    binding.LockedVisual?.SetActive(!unlocked);
                    binding.UnlockedVisual?.SetActive(unlocked);
                    if (binding.StateLabel != null)
                    {
                        binding.StateLabel.text = GetDistrictName(binding.District)
                                                  + (unlocked ? " — OPEN" : " — LOCKED");
                        binding.StateLabel.color = unlocked
                            ? new Color(0.35f, 1f, 0.58f)
                            : new Color(1f, 0.72f, 0.24f);
                    }
                }
            }

            WorldLandmarkId recommended = progression != null
                ? progression.RecommendedLandmark
                : WorldLandmarkId.LumberCamp;
            if (landmarks != null)
            {
                for (int i = 0; i < landmarks.Length; i++)
                {
                    WorldLandmarkMarkerBinding binding = landmarks[i];
                    if (binding?.Marker != null)
                        binding.Marker.SetActive(binding.Landmark == recommended);
                }
            }

            RefreshCount++;
        }

        public static string GetDistrictName(WorldDistrictId district)
        {
            switch (district)
            {
                case WorldDistrictId.Lumber: return "LUMBER AREA";
                case WorldDistrictId.Mining: return "MINING AREA";
                case WorldDistrictId.CentralLogistics: return "CENTRAL LOGISTICS";
                case WorldDistrictId.Industrial: return "INDUSTRIAL DISTRICT";
                default: return "DISTRICT";
            }
        }
    }

}
