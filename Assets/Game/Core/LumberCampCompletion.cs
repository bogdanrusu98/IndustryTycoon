using System;
using IndustryTycoon.Logistics;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    public sealed class LumberCampCompletion : MonoBehaviour
    {
        [SerializeField] private FirstCourierUnlock courierUnlock;
        [SerializeField] private CrateCourier courier;
        [SerializeField] private GameObject mineTeaserRoot;

        private bool _isCompleted;

        public event Action Completed;

        public FirstCourierUnlock CourierUnlock => courierUnlock;
        public CrateCourier Courier => courier;
        public GameObject MineTeaserRoot => mineTeaserRoot;
        public bool IsCompleted => _isCompleted;
        public int CompletionCount { get; private set; }

        private void Awake()
        {
            ApplyPresentationState();
        }

        private void OnEnable()
        {
            if (courierUnlock != null)
            {
                courierUnlock.CourierActivated += HandleCourierActivated;
            }

            if (courier != null)
            {
                courier.DeliveryCompleted += HandleDeliveryCompleted;
            }

            TryComplete();
        }

        private void OnDisable()
        {
            if (courierUnlock != null)
            {
                courierUnlock.CourierActivated -= HandleCourierActivated;
            }

            if (courier != null)
            {
                courier.DeliveryCompleted -= HandleDeliveryCompleted;
            }
        }

        public bool TryComplete()
        {
            if (_isCompleted
                || courierUnlock == null
                || !courierUnlock.IsCourierActivated
                || courier == null
                || courier.CompletedTripCount <= 0)
            {
                return false;
            }

            _isCompleted = true;
            CompletionCount++;
            ApplyPresentationState();
            Completed?.Invoke();
            return true;
        }

        private void HandleCourierActivated()
        {
            TryComplete();
        }

        private void HandleDeliveryCompleted(
            uint generation,
            int crateCount,
            int cashValue)
        {
            TryComplete();
        }

        private void ApplyPresentationState()
        {
            if (mineTeaserRoot != null)
            {
                mineTeaserRoot.SetActive(_isCompleted);
            }
        }
    }
}
