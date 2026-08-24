using System;
using IndustryTycoon.Interaction;
using UnityEngine;

namespace IndustryTycoon.ResourceSystem
{
    public sealed class WoodProductionUpgrade : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PurchasePad purchasePad;
        [SerializeField] private WoodSpawner woodSpawner;
        [SerializeField] private GameObject secondCutterVisual;
        [SerializeField] private TextMesh statusText;

        [Header("Production")]
        [SerializeField, Min(0.1f)] private float productionMultiplier = 2f;

        private bool _isApplied;

        public event Action Applied;

        public PurchasePad PurchasePad => purchasePad;
        public WoodSpawner WoodSpawner => woodSpawner;
        public GameObject SecondCutterVisual => secondCutterVisual;
        public float ProductionMultiplier => productionMultiplier;
        public bool IsApplied => _isApplied;

        private void Awake()
        {
            if (secondCutterVisual != null)
            {
                secondCutterVisual.SetActive(false);
            }

            RefreshStatusText();
        }

        private void OnEnable()
        {
            if (purchasePad == null)
            {
                return;
            }

            purchasePad.Completed += HandlePurchaseCompleted;
            if (purchasePad.IsCompleted)
            {
                TryApply();
            }
        }

        private void OnDisable()
        {
            if (purchasePad != null)
            {
                purchasePad.Completed -= HandlePurchaseCompleted;
            }
        }

        public bool TryApply()
        {
            if (_isApplied
                || purchasePad == null
                || !purchasePad.IsCompleted
                || woodSpawner == null)
            {
                return false;
            }

            _isApplied = true;
            woodSpawner.SetProductionRateMultiplier(productionMultiplier);
            if (secondCutterVisual != null)
            {
                secondCutterVisual.SetActive(true);
            }

            RefreshStatusText();
            Applied?.Invoke();
            return true;
        }

        private void HandlePurchaseCompleted()
        {
            TryApply();
        }

        private void RefreshStatusText()
        {
            if (statusText != null)
            {
                statusText.text = _isApplied ? "2X PRODUCTION" : "SECOND CUTTER\nLOCKED";
            }
        }

        private void OnValidate()
        {
            productionMultiplier = Mathf.Max(0.1f, productionMultiplier);
        }
    }
}
