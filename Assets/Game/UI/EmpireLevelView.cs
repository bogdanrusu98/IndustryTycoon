using System;
using IndustryTycoon.Feedback;
using IndustryTycoon.Market;
using IndustryTycoon.Progression;
using IndustryTycoon.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class EmpireLevelView : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private Text levelText;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;
        [SerializeField] private bool compactLabel;
        [SerializeField] private MobileProgressBar progressBar;

        private int _displayedLevel = M10ProgressionRules.MinimumEmpireLevel;
        private string _displayText = string.Empty;

        public event Action<int> LevelPresented;

        public LumberCampProgressionService ProgressionService => progressionService;
        public Text LevelText => levelText;
        public AudioFeedback AudioFeedback => audioFeedback;
        public HapticFeedback HapticFeedback => hapticFeedback;
        public int DisplayedLevel => _displayedLevel;
        public string DisplayText => _displayText;
        public int PresentationCount { get; private set; }

        private void OnEnable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged += HandleStateChanged;
                progressionService.EmpireLevelAdvanced += HandleEmpireLevelAdvanced;
            }

            SynchronizeSilently();
        }

        private void OnDisable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged -= HandleStateChanged;
                progressionService.EmpireLevelAdvanced -= HandleEmpireLevelAdvanced;
            }
        }

        public void SynchronizeSilently()
        {
            int currentLevel = progressionService != null
                ? progressionService.EmpireLevel
                : M10ProgressionRules.MinimumEmpireLevel;
            SetDisplayedLevel(currentLevel);
        }

        public static string BuildDisplayText(int empireLevel)
        {
            int safeLevel = Mathf.Clamp(
                empireLevel,
                M10ProgressionRules.MinimumEmpireLevel,
                M16MarketSaveData.MaximumEmpireLevel);
            return $"EMPIRE LEVEL {safeLevel}";
        }

        private void HandleStateChanged()
        {
            // Save restore and generic state refreshes update the permanent label
            // without manufacturing a level-up presentation.
            SynchronizeSilently();
        }

        private void HandleEmpireLevelAdvanced(int empireLevel)
        {
            int safeLevel = Mathf.Clamp(
                empireLevel,
                M10ProgressionRules.MinimumEmpireLevel,
                M16MarketSaveData.MaximumEmpireLevel);
            if (safeLevel <= _displayedLevel)
            {
                return;
            }

            SetDisplayedLevel(safeLevel);
            PresentationCount++;
            audioFeedback?.PlayUnlock();
            hapticFeedback?.PlayImportant();
            LevelPresented?.Invoke(safeLevel);
        }

        private void SetDisplayedLevel(int empireLevel)
        {
            _displayedLevel = Mathf.Clamp(
                empireLevel,
                M10ProgressionRules.MinimumEmpireLevel,
                M16MarketSaveData.MaximumEmpireLevel);
            _displayText = BuildDisplayText(_displayedLevel);
            if (levelText != null)
            {
                levelText.text = compactLabel ? $"Lv {_displayedLevel}" : _displayText;
            }


            if (progressBar != null)
            {
                progressBar.SetProgress(
                    _displayedLevel - M10ProgressionRules.MinimumEmpireLevel,
                    M16MarketSaveData.MaximumEmpireLevel
                    - M10ProgressionRules.MinimumEmpireLevel,
                    false);
            }
        }
    }
}
