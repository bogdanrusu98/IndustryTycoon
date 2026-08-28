using System;
using IndustryTycoon.Interaction;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    public sealed class FirstCourierUnlock : MonoBehaviour
    {
        [SerializeField] private FirstPackingStationUnlock packingStationUnlock;
        [SerializeField] private PurchasePad courierPurchasePad;
        [SerializeField] private GameObject courierPurchasePadRoot;
        [SerializeField] private GameObject courierRoot;

        private bool _isPadUnlocked;
        private bool _isCourierActivated;

        public event Action PadUnlocked;
        public event Action CourierActivated;

        public FirstPackingStationUnlock PackingStationUnlock => packingStationUnlock;
        public PurchasePad CourierPurchasePad => courierPurchasePad;
        public GameObject CourierPurchasePadRoot => courierPurchasePadRoot;
        public GameObject CourierRoot => courierRoot;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsCourierActivated => _isCourierActivated;

        private void Awake()
        {
            courierPurchasePad?.SetAvailable(false);
            if (courierPurchasePadRoot != null)
            {
                courierPurchasePadRoot.SetActive(false);
            }

            if (courierRoot != null)
            {
                courierRoot.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (packingStationUnlock != null)
            {
                packingStationUnlock.PackingStationActivated +=
                    HandlePackingStationActivated;
            }

            if (courierPurchasePad != null)
            {
                courierPurchasePad.Completed += HandlePurchaseCompleted;
            }

            TryUnlockPad();
            TryActivateCourier();
        }

        private void OnDisable()
        {
            if (packingStationUnlock != null)
            {
                packingStationUnlock.PackingStationActivated -=
                    HandlePackingStationActivated;
            }

            if (courierPurchasePad != null)
            {
                courierPurchasePad.Completed -= HandlePurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || packingStationUnlock == null
                || !packingStationUnlock.IsPackingStationActivated
                || courierPurchasePad == null
                || courierPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            courierPurchasePadRoot.SetActive(true);
            courierPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivateCourier()
        {
            if (_isCourierActivated
                || !_isPadUnlocked
                || courierPurchasePad == null
                || !courierPurchasePad.IsCompleted
                || courierRoot == null)
            {
                return false;
            }

            _isCourierActivated = true;
            courierRoot.SetActive(true);
            CourierActivated?.Invoke();
            return true;
        }

        public void SynchronizeFromPurchaseState()
        {
            _isPadUnlocked = packingStationUnlock != null
                             && packingStationUnlock.IsPackingStationActivated
                             && courierPurchasePad != null
                             && courierPurchasePadRoot != null;
            bool shouldActivateCourier = _isPadUnlocked
                                         && courierPurchasePad.IsCompleted
                                         && courierRoot != null;

            if (courierPurchasePadRoot != null)
            {
                courierPurchasePadRoot.SetActive(_isPadUnlocked);
            }

            if (courierRoot != null)
            {
                _isCourierActivated = false;
                courierRoot.SetActive(shouldActivateCourier);
            }

            _isCourierActivated = shouldActivateCourier;
            courierPurchasePad?.SetAvailable(_isPadUnlocked && !_isCourierActivated);
        }

        private void HandlePackingStationActivated()
        {
            TryUnlockPad();
        }

        private void HandlePurchaseCompleted()
        {
            TryActivateCourier();
        }
    }
}
