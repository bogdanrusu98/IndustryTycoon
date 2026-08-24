using System;
using System.Collections;
using IndustryTycoon.Economy;
using UnityEngine;

namespace IndustryTycoon.Interaction
{
    [RequireComponent(typeof(Collider))]
    public sealed class PurchasePad : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Wallet wallet;
        [SerializeField] private Collider playerCollider;
        [SerializeField] private Collider interactionCollider;
        [SerializeField] private Renderer padRenderer;
        [SerializeField] private Material availableMaterial;
        [SerializeField] private Material completedMaterial;
        [SerializeField] private TextMesh statusText;

        [Header("Purchase")]
        [SerializeField] private string purchaseLabel = "SECOND SAW";
        [SerializeField] private bool startsAvailable = true;
        [SerializeField, Min(1)] private int totalCost = 120;
        [SerializeField, Min(1)] private int spendPerTick = 5;
        [SerializeField, Min(0.02f)] private float spendInterval = 0.1f;

        private WaitForSeconds _spendWait;
        private Coroutine _spendCoroutine;
        private int _remainingCost;
        private bool _isInitialized;
        private bool _isStartingSpend;
        private bool _isPlayerInside;
        private bool _isAvailable;
        private bool _isCompleted;
        private bool _fundingPauseNotified;

        public event Action<int> ProgressChanged;
        public event Action<int, int> PaymentProcessed;
        public event Action FundingPaused;
        public event Action Completed;

        public Wallet Wallet => wallet;
        public Collider PlayerCollider => playerCollider;
        public Collider InteractionCollider => interactionCollider;
        public int TotalCost => totalCost;
        public int SpendPerTick => spendPerTick;
        public float SpendInterval => spendInterval;
        public int RemainingCost => _isInitialized ? _remainingCost : totalCost;
        public string PurchaseLabel => purchaseLabel;
        public bool StartsAvailable => startsAvailable;
        public bool IsAvailable => _isInitialized ? _isAvailable : startsAvailable;
        public bool IsPlayerInside => _isPlayerInside;
        public bool IsCompleted => _isCompleted;

        private void Awake()
        {
            EnsureInitialized();
            ApplyVisualState();
        }

        private void OnEnable()
        {
            if (wallet != null)
            {
                wallet.BalanceChanged += HandleWalletBalanceChanged;
            }
        }

        private void OnDisable()
        {
            if (wallet != null)
            {
                wallet.BalanceChanged -= HandleWalletBalanceChanged;
            }

            _isPlayerInside = false;
            _fundingPauseNotified = false;
            StopSpending();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != playerCollider || !_isAvailable || _isCompleted)
            {
                return;
            }

            _isPlayerInside = true;
            _fundingPauseNotified = false;
            if (wallet == null || wallet.Balance <= 0)
            {
                NotifyFundingPaused();
            }

            TryStartSpending();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other != playerCollider)
            {
                return;
            }

            _isPlayerInside = false;
            _fundingPauseNotified = false;
            StopSpending();
        }

        public int ProcessPaymentStep()
        {
            EnsureInitialized();
            if (!_isAvailable || _isCompleted || wallet == null || RemainingCost <= 0)
            {
                return 0;
            }

            int requestedAmount = Mathf.Min(spendPerTick, _remainingCost);
            int spentAmount = wallet.SpendUpTo(requestedAmount);
            if (spentAmount <= 0)
            {
                return 0;
            }

            _remainingCost -= spentAmount;
            ProgressChanged?.Invoke(_remainingCost);
            PaymentProcessed?.Invoke(spentAmount, _remainingCost);
            RefreshStatusText();

            if (_remainingCost == 0)
            {
                CompletePurchase();
            }
            else if (wallet.Balance <= 0)
            {
                NotifyFundingPaused();
            }

            return spentAmount;
        }

        public bool SetAvailable(bool isAvailable)
        {
            EnsureInitialized();
            bool nextAvailability = isAvailable && !_isCompleted;
            bool changed = _isAvailable != nextAvailability;
            _isAvailable = nextAvailability;

            if (!_isAvailable)
            {
                _isPlayerInside = false;
                _fundingPauseNotified = false;
                StopSpending();
            }

            ApplyVisualState();
            return changed;
        }

        private void HandleWalletBalanceChanged(int newBalance)
        {
            if (_isAvailable && _isPlayerInside && newBalance > 0)
            {
                _fundingPauseNotified = false;
                TryStartSpending();
            }
        }

        private void TryStartSpending()
        {
            if (_spendCoroutine != null
                || _isStartingSpend
                || !_isPlayerInside
                || !_isAvailable
                || _isCompleted
                || wallet == null
                || wallet.Balance <= 0
                || RemainingCost <= 0)
            {
                return;
            }

            _isStartingSpend = true;
            try
            {
                _spendCoroutine = StartCoroutine(SpendRoutine());
            }
            finally
            {
                _isStartingSpend = false;
            }
        }

        private IEnumerator SpendRoutine()
        {
            while (_isPlayerInside && !_isCompleted && wallet != null && wallet.Balance > 0)
            {
                if (ProcessPaymentStep() <= 0)
                {
                    break;
                }

                yield return _spendWait;
            }

            _spendCoroutine = null;
        }

        private void StopSpending()
        {
            _isStartingSpend = false;
            if (_spendCoroutine == null)
            {
                return;
            }

            StopCoroutine(_spendCoroutine);
            _spendCoroutine = null;
        }

        private void NotifyFundingPaused()
        {
            if (_fundingPauseNotified || !_isPlayerInside || _isCompleted || RemainingCost <= 0)
            {
                return;
            }

            _fundingPauseNotified = true;
            FundingPaused?.Invoke();
        }

        private void CompletePurchase()
        {
            if (_isCompleted)
            {
                return;
            }

            _isCompleted = true;
            _isAvailable = false;
            _remainingCost = 0;
            _isPlayerInside = false;
            _fundingPauseNotified = false;

            ApplyVisualState();
            Completed?.Invoke();
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            _remainingCost = totalCost;
            _spendWait = new WaitForSeconds(spendInterval);
            _isAvailable = startsAvailable;
            _isInitialized = true;
        }

        private void ApplyVisualState()
        {
            if (padRenderer != null)
            {
                Material stateMaterial = _isCompleted ? completedMaterial : availableMaterial;
                if (stateMaterial != null)
                {
                    padRenderer.sharedMaterial = stateMaterial;
                }
            }

            if (interactionCollider != null)
            {
                interactionCollider.enabled = _isAvailable && !_isCompleted;
            }

            RefreshStatusText();
        }

        private void RefreshStatusText()
        {
            if (statusText == null)
            {
                return;
            }

            string label = string.IsNullOrWhiteSpace(purchaseLabel) ? "PURCHASE" : purchaseLabel;
            statusText.text = _isCompleted
                ? $"{label}\nUNLOCKED"
                : !_isAvailable
                    ? $"{label}\nLOCKED"
                    : $"{label}\n${RemainingCost} / ${totalCost}";
        }

        private void OnValidate()
        {
            totalCost = Mathf.Max(1, totalCost);
            spendPerTick = Mathf.Max(1, spendPerTick);
            spendInterval = Mathf.Max(0.02f, spendInterval);
        }
    }
}
