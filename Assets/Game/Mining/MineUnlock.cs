using System;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.Mining
{
    public sealed class MineUnlock : MonoBehaviour
    {
        [SerializeField] private LumberCampCompletion lumberCampCompletion;
        [SerializeField] private GameObject lockedTeaserRoot;
        [SerializeField] private GameObject mineAreaRoot;

        private bool _isUnlocked;

        public event Action Unlocked;

        public LumberCampCompletion LumberCampCompletion => lumberCampCompletion;
        public GameObject LockedTeaserRoot => lockedTeaserRoot;
        public GameObject MineAreaRoot => mineAreaRoot;
        public bool IsUnlocked => _isUnlocked;
        public int UnlockCount { get; private set; }

        private void Awake()
        {
            ApplyPresentationState();
        }

        private void OnEnable()
        {
            if (lumberCampCompletion != null)
            {
                lumberCampCompletion.Completed += HandleLumberCampCompleted;
            }

            TryUnlock();
        }

        private void OnDisable()
        {
            if (lumberCampCompletion != null)
            {
                lumberCampCompletion.Completed -= HandleLumberCampCompleted;
            }
        }

        public bool TryUnlock()
        {
            if (_isUnlocked
                || lumberCampCompletion == null
                || !lumberCampCompletion.IsCompleted)
            {
                return false;
            }

            _isUnlocked = true;
            UnlockCount++;
            ApplyPresentationState();
            Unlocked?.Invoke();
            return true;
        }

        public void RestoreUnlocked(bool unlocked)
        {
            _isUnlocked = unlocked;
            UnlockCount = unlocked ? 1 : 0;
            ApplyPresentationState();
        }

        public void SynchronizeFromCompletionState()
        {
            if (!_isUnlocked)
            {
                TryUnlock();
                return;
            }

            ApplyPresentationState();
        }

        private void HandleLumberCampCompleted()
        {
            TryUnlock();
        }

        private void ApplyPresentationState()
        {
            if (lockedTeaserRoot != null)
            {
                // The M8 gateway owns when the whole teaser becomes visible.
                // Within it, keep the locked presentation intact until the
                // authoritative completion event swaps it for the mine area.
                lockedTeaserRoot.SetActive(!_isUnlocked);
            }

            if (mineAreaRoot != null)
            {
                mineAreaRoot.SetActive(_isUnlocked);
            }
        }
    }
}
