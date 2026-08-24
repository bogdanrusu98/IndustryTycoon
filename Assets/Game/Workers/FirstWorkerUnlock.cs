using System;
using IndustryTycoon.Interaction;
using IndustryTycoon.ResourceSystem;
using UnityEngine;

namespace IndustryTycoon.Workers
{
    public sealed class FirstWorkerUnlock : MonoBehaviour
    {
        [SerializeField] private WoodProductionUpgrade productionUpgrade;
        [SerializeField] private PurchasePad workerPurchasePad;
        [SerializeField] private GameObject workerPurchasePadRoot;
        [SerializeField] private GameObject workerRoot;

        private bool _isPadUnlocked;
        private bool _isWorkerActivated;

        public event Action PadUnlocked;
        public event Action WorkerActivated;

        public WoodProductionUpgrade ProductionUpgrade => productionUpgrade;
        public PurchasePad WorkerPurchasePad => workerPurchasePad;
        public GameObject WorkerPurchasePadRoot => workerPurchasePadRoot;
        public GameObject WorkerRoot => workerRoot;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsWorkerActivated => _isWorkerActivated;

        private void Awake()
        {
            workerPurchasePad?.SetAvailable(false);
            if (workerPurchasePadRoot != null)
            {
                workerPurchasePadRoot.SetActive(false);
            }

            if (workerRoot != null)
            {
                workerRoot.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (productionUpgrade != null)
            {
                productionUpgrade.Applied += HandleProductionUpgradeApplied;
            }

            if (workerPurchasePad != null)
            {
                workerPurchasePad.Completed += HandleWorkerPurchaseCompleted;
            }

            TryUnlockPad();
            TryActivateWorker();
        }

        private void OnDisable()
        {
            if (productionUpgrade != null)
            {
                productionUpgrade.Applied -= HandleProductionUpgradeApplied;
            }

            if (workerPurchasePad != null)
            {
                workerPurchasePad.Completed -= HandleWorkerPurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || productionUpgrade == null
                || !productionUpgrade.IsApplied
                || workerPurchasePad == null
                || workerPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            workerPurchasePadRoot.SetActive(true);
            workerPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivateWorker()
        {
            if (_isWorkerActivated
                || !_isPadUnlocked
                || workerPurchasePad == null
                || !workerPurchasePad.IsCompleted
                || workerRoot == null)
            {
                return false;
            }

            _isWorkerActivated = true;
            workerRoot.SetActive(true);
            WorkerActivated?.Invoke();
            return true;
        }

        private void HandleProductionUpgradeApplied()
        {
            TryUnlockPad();
        }

        private void HandleWorkerPurchaseCompleted()
        {
            TryActivateWorker();
        }
    }
}
