using System;
using IndustryTycoon.Interaction;
using UnityEngine;

namespace IndustryTycoon.Mining
{
    public sealed class SmelterUnlock : MonoBehaviour
    {
        [SerializeField] private MineUnlock mineUnlock;
        [SerializeField] private IronVein ironVein;
        [SerializeField] private PurchasePad smelterPurchasePad;
        [SerializeField] private GameObject smelterPurchasePadRoot;
        [SerializeField] private GameObject smelterRoot;
        [SerializeField, Min(1)] private int requiredMinedOre = 10;

        private int _minedOreCount;
        private bool _isPadUnlocked;
        private bool _isSmelterActivated;

        public event Action<int, int> MiningProgressChanged;
        public event Action PadUnlocked;
        public event Action SmelterActivated;

        public MineUnlock MineUnlock => mineUnlock;
        public IronVein IronVein => ironVein;
        public PurchasePad SmelterPurchasePad => smelterPurchasePad;
        public GameObject SmelterPurchasePadRoot => smelterPurchasePadRoot;
        public GameObject SmelterRoot => smelterRoot;
        public int RequiredMinedOre => requiredMinedOre;
        public int MinedOreCount => _minedOreCount;
        public bool IsPadUnlocked => _isPadUnlocked;
        public bool IsSmelterActivated => _isSmelterActivated;
        public int ActivationCount { get; private set; }

        private void Awake()
        {
            smelterPurchasePad?.SetAvailable(false);
            smelterPurchasePadRoot?.SetActive(false);
            smelterRoot?.SetActive(false);
        }

        private void OnEnable()
        {
            if (mineUnlock != null)
            {
                mineUnlock.Unlocked += HandleMineUnlocked;
            }

            if (ironVein != null)
            {
                ironVein.OreMined += HandleOreMined;
            }

            if (smelterPurchasePad != null)
            {
                smelterPurchasePad.Completed += HandlePurchaseCompleted;
            }

            TryUnlockPad();
            TryActivateSmelter();
        }

        private void OnDisable()
        {
            if (mineUnlock != null)
            {
                mineUnlock.Unlocked -= HandleMineUnlocked;
            }

            if (ironVein != null)
            {
                ironVein.OreMined -= HandleOreMined;
            }

            if (smelterPurchasePad != null)
            {
                smelterPurchasePad.Completed -= HandlePurchaseCompleted;
            }
        }

        public bool TryUnlockPad()
        {
            if (_isPadUnlocked
                || mineUnlock == null
                || !mineUnlock.IsUnlocked
                || _minedOreCount < requiredMinedOre
                || smelterPurchasePad == null
                || smelterPurchasePadRoot == null)
            {
                return false;
            }

            _isPadUnlocked = true;
            smelterPurchasePadRoot.SetActive(true);
            smelterPurchasePad.SetAvailable(true);
            PadUnlocked?.Invoke();
            return true;
        }

        public bool TryActivateSmelter()
        {
            if (_isSmelterActivated
                || !_isPadUnlocked
                || smelterPurchasePad == null
                || !smelterPurchasePad.IsCompleted
                || smelterRoot == null)
            {
                return false;
            }

            _isSmelterActivated = true;
            ActivationCount++;
            smelterRoot.SetActive(true);
            SmelterActivated?.Invoke();
            return true;
        }

        public void RestoreMinedOreCount(int minedOreCount)
        {
            _minedOreCount = Mathf.Max(0, minedOreCount);
            MiningProgressChanged?.Invoke(_minedOreCount, requiredMinedOre);
        }

        public void SynchronizeFromPurchaseState()
        {
            _isPadUnlocked = mineUnlock != null
                             && mineUnlock.IsUnlocked
                             && _minedOreCount >= requiredMinedOre
                             && smelterPurchasePad != null
                             && smelterPurchasePadRoot != null;
            bool shouldActivate = _isPadUnlocked
                                  && smelterPurchasePad.IsCompleted
                                  && smelterRoot != null;

            smelterPurchasePadRoot?.SetActive(_isPadUnlocked);
            smelterRoot?.SetActive(shouldActivate);
            _isSmelterActivated = shouldActivate;
            ActivationCount = shouldActivate ? 1 : 0;
            smelterPurchasePad?.SetAvailable(_isPadUnlocked && !shouldActivate);
        }

        private void HandleMineUnlocked()
        {
            TryUnlockPad();
        }

        private void HandleOreMined(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _minedOreCount += amount;
            MiningProgressChanged?.Invoke(_minedOreCount, requiredMinedOre);
            TryUnlockPad();
        }

        private void HandlePurchaseCompleted()
        {
            TryActivateSmelter();
        }

        private void OnValidate()
        {
            requiredMinedOre = Mathf.Max(1, requiredMinedOre);
        }
    }
}
