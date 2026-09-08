using System;
using IndustryTycoon.Interaction;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    public sealed class ToolWorkshopUnlock : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private PurchasePad workshopPurchasePad;
        [SerializeField] private GameObject workshopPurchasePadRoot;
        [SerializeField] private GameObject workshopRoot;

        private bool _isPadUnlocked;
        private bool _isWorkshopActivated;

        public event Action PadUnlocked;
        public event Action WorkshopActivated;

        public LumberCampProgressionService ProgressionService => progressionService;
        public PurchasePad WorkshopPurchasePad => workshopPurchasePad;
        public GameObject WorkshopPurchasePadRoot => workshopPurchasePadRoot;
        public GameObject WorkshopRoot => workshopRoot;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsWorkshopActivated => _isWorkshopActivated;
        public int ActivationCount { get; private set; }

        private void Awake()
        {
            workshopPurchasePad?.SetAvailable(false);
            workshopPurchasePadRoot?.SetActive(false);
            workshopRoot?.SetActive(false);
        }

        private void OnEnable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged += HandleProgressionStateChanged;
            }

            if (workshopPurchasePad != null)
            {
                workshopPurchasePad.Completed += HandlePurchaseCompleted;
            }

            TryUnlockPad();
            TryActivateWorkshop();
        }

        private void OnDisable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged -= HandleProgressionStateChanged;
            }

            if (workshopPurchasePad != null)
            {
                workshopPurchasePad.Completed -= HandlePurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || !HasRequiredProductionAccess()
                || workshopPurchasePad == null
                || workshopPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            workshopPurchasePadRoot.SetActive(true);
            workshopPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivateWorkshop()
        {
            if (_isWorkshopActivated
                || !_isPadUnlocked
                || workshopPurchasePad == null
                || !workshopPurchasePad.IsCompleted
                || workshopRoot == null)
            {
                return false;
            }

            _isWorkshopActivated = true;
            ActivationCount++;
            workshopRoot.SetActive(true);
            WorkshopActivated?.Invoke();
            return true;
        }

        public void SynchronizeFromPurchaseState()
        {
            _isPadUnlocked = HasRequiredProductionAccess()
                             && workshopPurchasePad != null
                             && workshopPurchasePadRoot != null;
            bool shouldActivate = _isPadUnlocked
                                  && workshopPurchasePad.IsCompleted
                                  && workshopRoot != null;

            workshopPurchasePadRoot?.SetActive(_isPadUnlocked);
            workshopRoot?.SetActive(shouldActivate);
            _isWorkshopActivated = shouldActivate;
            ActivationCount = shouldActivate ? 1 : 0;
            workshopPurchasePad?.SetAvailable(_isPadUnlocked && !shouldActivate);
        }

        private bool HasRequiredProductionAccess()
        {
            return progressionService != null
                   && progressionService.GetMetric(ProgressMetricId.PlanksProduced) > 0L
                   && progressionService.GetMetric(ProgressMetricId.IronBarsProduced) > 0L;
        }

        private void HandleProgressionStateChanged()
        {
            TryUnlockPad();
            TryActivateWorkshop();
        }

        private void HandlePurchaseCompleted()
        {
            TryActivateWorkshop();
        }
    }
}
