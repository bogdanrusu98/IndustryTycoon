using System;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    public sealed class FirstPackingStationUnlock : MonoBehaviour
    {
        [SerializeField] private FirstAutoFeederUnlock autoFeederUnlock;
        [SerializeField] private PurchasePad packingStationPurchasePad;
        [SerializeField] private GameObject packingStationPurchasePadRoot;
        [SerializeField] private GameObject packingStationRoot;

        private bool _isPadUnlocked;
        private bool _isPackingStationActivated;

        public event Action PadUnlocked;
        public event Action PackingStationActivated;

        public FirstAutoFeederUnlock AutoFeederUnlock => autoFeederUnlock;
        public PurchasePad PackingStationPurchasePad => packingStationPurchasePad;
        public GameObject PackingStationPurchasePadRoot => packingStationPurchasePadRoot;
        public GameObject PackingStationRoot => packingStationRoot;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsPackingStationActivated => _isPackingStationActivated;

        private void Awake()
        {
            packingStationPurchasePad?.SetAvailable(false);
            if (packingStationPurchasePadRoot != null)
            {
                packingStationPurchasePadRoot.SetActive(false);
            }

            if (packingStationRoot != null)
            {
                packingStationRoot.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (autoFeederUnlock != null)
            {
                autoFeederUnlock.AutoFeederActivated += HandleAutoFeederActivated;
            }

            if (packingStationPurchasePad != null)
            {
                packingStationPurchasePad.Completed += HandlePurchaseCompleted;
            }

            TryUnlockPad();
            TryActivatePackingStation();
        }

        private void OnDisable()
        {
            if (autoFeederUnlock != null)
            {
                autoFeederUnlock.AutoFeederActivated -= HandleAutoFeederActivated;
            }

            if (packingStationPurchasePad != null)
            {
                packingStationPurchasePad.Completed -= HandlePurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || autoFeederUnlock == null
                || !autoFeederUnlock.IsAutoFeederActivated
                || packingStationPurchasePad == null
                || packingStationPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            packingStationPurchasePadRoot.SetActive(true);
            packingStationPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivatePackingStation()
        {
            if (_isPackingStationActivated
                || !_isPadUnlocked
                || packingStationPurchasePad == null
                || !packingStationPurchasePad.IsCompleted
                || packingStationRoot == null)
            {
                return false;
            }

            _isPackingStationActivated = true;
            packingStationRoot.SetActive(true);
            PackingStationActivated?.Invoke();
            return true;
        }

        private void HandleAutoFeederActivated()
        {
            TryUnlockPad();
        }

        private void HandlePurchaseCompleted()
        {
            TryActivatePackingStation();
        }
    }
}
