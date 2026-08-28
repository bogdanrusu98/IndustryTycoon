using System;
using IndustryTycoon.Interaction;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    public sealed class FirstAutoFeederUnlock : MonoBehaviour
    {
        [SerializeField] private FirstProcessorUnlock processorUnlock;
        [SerializeField] private PurchasePad autoFeederPurchasePad;
        [SerializeField] private GameObject autoFeederPurchasePadRoot;
        [SerializeField] private GameObject autoFeederRoot;

        private bool _isPadUnlocked;
        private bool _isAutoFeederActivated;

        public event Action PadUnlocked;
        public event Action AutoFeederActivated;

        public FirstProcessorUnlock ProcessorUnlock => processorUnlock;
        public PurchasePad AutoFeederPurchasePad => autoFeederPurchasePad;
        public GameObject AutoFeederPurchasePadRoot => autoFeederPurchasePadRoot;
        public GameObject AutoFeederRoot => autoFeederRoot;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsAutoFeederActivated => _isAutoFeederActivated;

        private void Awake()
        {
            autoFeederPurchasePad?.SetAvailable(false);
            if (autoFeederPurchasePadRoot != null)
            {
                autoFeederPurchasePadRoot.SetActive(false);
            }

            if (autoFeederRoot != null)
            {
                autoFeederRoot.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (processorUnlock != null)
            {
                processorUnlock.ProcessorActivated += HandleProcessorActivated;
            }

            if (autoFeederPurchasePad != null)
            {
                autoFeederPurchasePad.Completed += HandlePurchaseCompleted;
            }

            TryUnlockPad();
            TryActivateAutoFeeder();
        }

        private void OnDisable()
        {
            if (processorUnlock != null)
            {
                processorUnlock.ProcessorActivated -= HandleProcessorActivated;
            }

            if (autoFeederPurchasePad != null)
            {
                autoFeederPurchasePad.Completed -= HandlePurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || processorUnlock == null
                || !processorUnlock.IsProcessorActivated
                || autoFeederPurchasePad == null
                || autoFeederPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            autoFeederPurchasePadRoot.SetActive(true);
            autoFeederPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivateAutoFeeder()
        {
            if (_isAutoFeederActivated
                || !_isPadUnlocked
                || autoFeederPurchasePad == null
                || !autoFeederPurchasePad.IsCompleted
                || autoFeederRoot == null)
            {
                return false;
            }

            _isAutoFeederActivated = true;
            autoFeederRoot.SetActive(true);
            AutoFeederActivated?.Invoke();
            return true;
        }

        public void SynchronizeFromPurchaseState()
        {
            _isPadUnlocked = processorUnlock != null
                             && processorUnlock.IsProcessorActivated
                             && autoFeederPurchasePad != null
                             && autoFeederPurchasePadRoot != null;
            bool shouldActivateAutoFeeder = _isPadUnlocked
                                            && autoFeederPurchasePad.IsCompleted
                                            && autoFeederRoot != null;

            if (autoFeederPurchasePadRoot != null)
            {
                autoFeederPurchasePadRoot.SetActive(_isPadUnlocked);
            }

            if (autoFeederRoot != null)
            {
                _isAutoFeederActivated = false;
                autoFeederRoot.SetActive(shouldActivateAutoFeeder);
            }

            _isAutoFeederActivated = shouldActivateAutoFeeder;
            autoFeederPurchasePad?.SetAvailable(
                _isPadUnlocked && !_isAutoFeederActivated);
        }

        private void HandleProcessorActivated()
        {
            TryUnlockPad();
        }

        private void HandlePurchaseCompleted()
        {
            TryActivateAutoFeeder();
        }
    }
}
