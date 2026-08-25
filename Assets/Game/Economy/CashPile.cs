using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndustryTycoon.Economy
{
    public sealed class CashPile : MonoBehaviour
    {
        [Header("Visuals")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject cashVisualPrefab;
        [SerializeField] private TextMesh amountText;
        [SerializeField, Min(1)] private int maximumVisualItems = 8;
        [SerializeField, Min(1)] private int cashPerVisual = 5;

        private readonly List<GameObject> _visualItems = new List<GameObject>();
        private int _storedCash;

        public event Action<int> StoredCashChanged;

        public int StoredCash => _storedCash;
        public int MaximumVisualItems => maximumVisualItems;
        public int CashPerVisual => cashPerVisual;
        public Transform VisualRoot => visualRoot != null ? visualRoot : transform;

        private void Awake()
        {
            EnsureVisualPool();
            RefreshVisuals();
        }

        public bool CanDeposit(int amount)
        {
            return amount > 0 && amount <= int.MaxValue - _storedCash;
        }

        public int Deposit(int amount)
        {
            if (amount <= 0 || _storedCash >= int.MaxValue)
            {
                return 0;
            }

            int acceptedAmount = (int)Math.Min((long)amount, (long)int.MaxValue - _storedCash);
            if (acceptedAmount <= 0)
            {
                return 0;
            }

            FinalizeDepositForTransfer(acceptedAmount);
            PublishDepositCommitted();
            return acceptedAmount;
        }

        internal void FinalizeDepositForTransfer(int amount)
        {
            Debug.Assert(CanDeposit(amount));
            _storedCash += amount;
            RefreshVisuals();
        }

        internal void PublishDepositCommitted()
        {
            StoredCashChanged?.Invoke(_storedCash);
        }

        public bool TryWithdrawAll(out int amount)
        {
            amount = _storedCash;
            if (amount <= 0)
            {
                return false;
            }

            _storedCash = 0;
            RefreshVisuals();
            StoredCashChanged?.Invoke(_storedCash);
            return true;
        }

        private void EnsureVisualPool()
        {
            if (cashVisualPrefab == null)
            {
                return;
            }

            Transform parent = VisualRoot;
            while (_visualItems.Count < maximumVisualItems)
            {
                GameObject visual = Instantiate(cashVisualPrefab, parent);
                visual.name = $"Cash Bundle {_visualItems.Count + 1:00}";
                visual.SetActive(false);
                _visualItems.Add(visual);
            }
        }

        private void RefreshVisuals()
        {
            EnsureVisualPool();

            long requiredVisuals = _storedCash > 0
                ? ((long)_storedCash + cashPerVisual - 1L) / cashPerVisual
                : 0L;
            int visibleCount = (int)Math.Min(maximumVisualItems, requiredVisuals);

            for (int i = 0; i < _visualItems.Count; i++)
            {
                GameObject visual = _visualItems[i];
                bool shouldBeVisible = i < visibleCount;
                visual.SetActive(shouldBeVisible);
                if (!shouldBeVisible)
                {
                    continue;
                }

                int row = i / 4;
                int column = i % 4;
                visual.transform.localPosition = new Vector3(
                    (column - 1.5f) * 0.34f,
                    row * 0.10f,
                    (column % 2 == 0 ? -1f : 1f) * 0.12f);
                visual.transform.localRotation = Quaternion.Euler(0f, (i % 2 == 0 ? -8f : 8f), 0f);
            }

            if (amountText != null)
            {
                amountText.text = $"${_storedCash}";
            }
        }

        private void OnValidate()
        {
            maximumVisualItems = Mathf.Max(1, maximumVisualItems);
            cashPerVisual = Mathf.Max(1, cashPerVisual);
        }
    }
}
