using System;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    public sealed class IndustrialDistrictTease : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private GameObject teaseRoot;

        private bool _isRevealed;

        public event Action Revealed;

        public LumberCampProgressionService ProgressionService => progressionService;
        public GameObject TeaseRoot => teaseRoot;
        public bool IsRevealed => _isRevealed;
        public int RevealCount { get; private set; }

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
            bool shouldReveal = progressionService != null
                                && progressionService.EmpireLevel
                                >= M10ProgressionRules.MaximumEmpireLevel;
            SetPresentationState(shouldReveal, false);
        }

        private void HandleStateChanged()
        {
            SynchronizeSilently();
        }

        private void HandleEmpireLevelAdvanced(int empireLevel)
        {
            if (empireLevel < M10ProgressionRules.MaximumEmpireLevel)
            {
                return;
            }

            SetPresentationState(true, true);
        }

        private void SetPresentationState(bool revealed, bool publishTransition)
        {
            bool changed = _isRevealed != revealed;
            _isRevealed = revealed;
            teaseRoot?.SetActive(revealed);
            if (!changed || !revealed || !publishTransition)
            {
                return;
            }

            RevealCount++;
            Revealed?.Invoke();
        }
    }
}
