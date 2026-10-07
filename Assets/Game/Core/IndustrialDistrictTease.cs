using System;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    public sealed class IndustrialDistrictTease : MonoBehaviour
    {
        public const int ReadyEmpireLevel = 10;
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private GameObject teaseRoot;
        [SerializeField] private GameObject readyRoot;

        private bool _isRevealed;
        private bool _isReady;

        public event Action Revealed;
        public event Action Ready;

        public LumberCampProgressionService ProgressionService => progressionService;
        public GameObject TeaseRoot => teaseRoot;
        public bool IsRevealed => _isRevealed;
        public bool IsReady => _isReady;
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
                                >= 5;
            bool shouldBeReady = progressionService != null
                                 && progressionService.EmpireLevel
                                 >= ReadyEmpireLevel;
            SetPresentationState(shouldReveal, shouldBeReady, false);
        }

        private void HandleStateChanged()
        {
            SynchronizeSilently();
        }

        private void HandleEmpireLevelAdvanced(int empireLevel)
        {
            if (empireLevel < 5)
            {
                return;
            }

            SetPresentationState(
                true,
                empireLevel >= ReadyEmpireLevel,
                true);
        }

        private void SetPresentationState(
            bool revealed,
            bool ready,
            bool publishTransition)
        {
            bool changed = _isRevealed != revealed;
            bool readyChanged = _isReady != ready;
            _isRevealed = revealed;
            _isReady = ready;
            teaseRoot?.SetActive(revealed);
            readyRoot?.SetActive(ready);
            if (!publishTransition)
            {
                return;
            }

            if (changed && revealed)
            {
                RevealCount++;
                Revealed?.Invoke();
            }

            if (readyChanged && ready && publishTransition)
            {
                Ready?.Invoke();
            }
        }
    }
}
