using System;
using IndustryTycoon.Interaction;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    public sealed class FirstProcessorUnlock : MonoBehaviour
    {
        [SerializeField] private FirstWorkerUnlock workerUnlock;
        [SerializeField] private PurchasePad processorPurchasePad;
        [SerializeField] private GameObject processorPurchasePadRoot;
        [SerializeField] private GameObject processorRoot;

        private bool _isPadUnlocked;
        private bool _isProcessorActivated;

        public event Action PadUnlocked;
        public event Action ProcessorActivated;

        public FirstWorkerUnlock WorkerUnlock => workerUnlock;
        public PurchasePad ProcessorPurchasePad => processorPurchasePad;
        public GameObject ProcessorPurchasePadRoot => processorPurchasePadRoot;
        public GameObject ProcessorRoot => processorRoot;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsProcessorActivated => _isProcessorActivated;

        private void Awake()
        {
            processorPurchasePad?.SetAvailable(false);
            if (processorPurchasePadRoot != null)
            {
                processorPurchasePadRoot.SetActive(false);
            }

            if (processorRoot != null)
            {
                processorRoot.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (workerUnlock != null)
            {
                workerUnlock.WorkerActivated += HandleWorkerActivated;
            }

            if (processorPurchasePad != null)
            {
                processorPurchasePad.Completed += HandleProcessorPurchaseCompleted;
            }

            TryUnlockPad();
            TryActivateProcessor();
        }

        private void OnDisable()
        {
            if (workerUnlock != null)
            {
                workerUnlock.WorkerActivated -= HandleWorkerActivated;
            }

            if (processorPurchasePad != null)
            {
                processorPurchasePad.Completed -= HandleProcessorPurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || workerUnlock == null
                || !workerUnlock.IsWorkerActivated
                || processorPurchasePad == null
                || processorPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            processorPurchasePadRoot.SetActive(true);
            processorPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivateProcessor()
        {
            if (_isProcessorActivated
                || !_isPadUnlocked
                || processorPurchasePad == null
                || !processorPurchasePad.IsCompleted
                || processorRoot == null)
            {
                return false;
            }

            _isProcessorActivated = true;
            processorRoot.SetActive(true);
            ProcessorActivated?.Invoke();
            return true;
        }

        private void HandleWorkerActivated()
        {
            TryUnlockPad();
        }

        private void HandleProcessorPurchaseCompleted()
        {
            TryActivateProcessor();
        }
    }
}
