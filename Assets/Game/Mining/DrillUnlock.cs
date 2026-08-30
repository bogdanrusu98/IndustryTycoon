using System;
using IndustryTycoon.Interaction;
using UnityEngine;

namespace IndustryTycoon.Mining
{
    public sealed class DrillUnlock : MonoBehaviour
    {
        [SerializeField] private SmelterUnlock smelterUnlock;
        [SerializeField] private Smelter smelter;
        [SerializeField] private PurchasePad drillPurchasePad;
        [SerializeField] private GameObject drillPurchasePadRoot;
        [SerializeField] private GameObject drillRoot;
        [SerializeField, Min(1)] private int requiredProducedBars = 5;

        private int _producedBarCount;
        private bool _isPadUnlocked;
        private bool _isDrillActivated;

        public event Action<int, int> ProductionProgressChanged;
        public event Action PadUnlocked;
        public event Action DrillActivated;

        public SmelterUnlock SmelterUnlock => smelterUnlock;
        public Smelter Smelter => smelter;
        public PurchasePad DrillPurchasePad => drillPurchasePad;
        public GameObject DrillPurchasePadRoot => drillPurchasePadRoot;
        public GameObject DrillRoot => drillRoot;
        public int RequiredProducedBars => requiredProducedBars;
        public int ProducedBarCount => _producedBarCount;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsDrillActivated => _isDrillActivated;
        public int ActivationCount { get; private set; }

        private void Awake()
        {
            drillPurchasePad?.SetAvailable(false);
            drillPurchasePadRoot?.SetActive(false);
            drillRoot?.SetActive(false);
        }

        private void OnEnable()
        {
            if (smelterUnlock != null)
            {
                smelterUnlock.SmelterActivated += HandleSmelterActivated;
            }

            if (smelter != null)
            {
                smelter.RecipeCompleted += HandleRecipeCompleted;
            }

            if (drillPurchasePad != null)
            {
                drillPurchasePad.Completed += HandlePurchaseCompleted;
            }

            TryUnlockPad();
            TryActivateDrill();
        }

        private void OnDisable()
        {
            if (smelterUnlock != null)
            {
                smelterUnlock.SmelterActivated -= HandleSmelterActivated;
            }

            if (smelter != null)
            {
                smelter.RecipeCompleted -= HandleRecipeCompleted;
            }

            if (drillPurchasePad != null)
            {
                drillPurchasePad.Completed -= HandlePurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || smelterUnlock == null
                || !smelterUnlock.IsSmelterActivated
                || _producedBarCount < requiredProducedBars
                || drillPurchasePad == null
                || drillPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            drillPurchasePadRoot.SetActive(true);
            drillPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivateDrill()
        {
            if (_isDrillActivated
                || !_isPadUnlocked
                || drillPurchasePad == null
                || !drillPurchasePad.IsCompleted
                || drillRoot == null)
            {
                return false;
            }

            _isDrillActivated = true;
            ActivationCount++;
            drillRoot.SetActive(true);
            DrillActivated?.Invoke();
            return true;
        }

        public void RestoreProducedBarCount(int producedBarCount)
        {
            _producedBarCount = Mathf.Max(0, producedBarCount);
            ProductionProgressChanged?.Invoke(_producedBarCount, requiredProducedBars);
        }

        public void SynchronizeFromPurchaseState()
        {
            _isPadUnlocked = smelterUnlock != null
                             && smelterUnlock.IsSmelterActivated
                             && _producedBarCount >= requiredProducedBars
                             && drillPurchasePad != null
                             && drillPurchasePadRoot != null;
            bool shouldActivate = _isPadUnlocked
                                  && drillPurchasePad.IsCompleted
                                  && drillRoot != null;

            drillPurchasePadRoot?.SetActive(_isPadUnlocked);
            drillRoot?.SetActive(shouldActivate);
            _isDrillActivated = shouldActivate;
            ActivationCount = shouldActivate ? 1 : 0;
            drillPurchasePad?.SetAvailable(_isPadUnlocked && !shouldActivate);
        }

        private void HandleSmelterActivated()
        {
            TryUnlockPad();
        }

        private void HandleRecipeCompleted(int inputOre, int outputBars)
        {
            int producedBars = smelter != null ? smelter.RecipeOutputBars : 1;
            _producedBarCount += Mathf.Max(1, producedBars);
            ProductionProgressChanged?.Invoke(_producedBarCount, requiredProducedBars);
            TryUnlockPad();
        }

        private void HandlePurchaseCompleted()
        {
            TryActivateDrill();
        }

        private void OnValidate()
        {
            requiredProducedBars = Mathf.Max(1, requiredProducedBars);
        }
    }
}
