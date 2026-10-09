using System;
using IndustryTycoon.Persistence;
using IndustryTycoon.Monetization;
using IndustryTycoon.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class WelcomeBackView : MonoBehaviour
    {
        [SerializeField] private LocalPersistenceService persistenceService;
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private Text awayText;
        [SerializeField] private Text earnedText;
        [SerializeField] private Button collectButton;
        [SerializeField] private Button doubleRewardButton;
        [SerializeField] private MonetizationService monetizationService;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerDragInput playerDragInput;

        private bool _controlsCaptured;
        private bool _movementWasEnabled;
        private bool _dragInputWasEnabled;
        private bool _collectionInProgress;

        public LocalPersistenceService PersistenceService => persistenceService;
        public GameObject OverlayRoot => overlayRoot;
        public Text AwayText => awayText;
        public Text EarnedText => earnedText;
        public Button CollectButton => collectButton;
        public bool IsVisible => overlayRoot != null && overlayRoot.activeSelf;

        private void Awake()
        {
            if (collectButton != null)
            {
                collectButton.onClick.AddListener(HandleCollectClicked);
            }
            if (doubleRewardButton != null) doubleRewardButton.onClick.AddListener(HandleDoubleClicked);

            SetVisible(false);
        }

        private void OnEnable()
        {
            if (persistenceService != null)
            {
                persistenceService.ReturnStateChanged += HandleReturnStateChanged;
            }

            Refresh();
        }

        private void Start()
        {
            Refresh();
        }

        private void OnDisable()
        {
            if (persistenceService != null)
            {
                persistenceService.ReturnStateChanged -= HandleReturnStateChanged;
            }

            _collectionInProgress = false;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (collectButton != null)
            {
                collectButton.onClick.RemoveListener(HandleCollectClicked);
            }
            if (doubleRewardButton != null) doubleRewardButton.onClick.RemoveListener(HandleDoubleClicked);
        }

        public void Refresh()
        {
            bool shouldShow = persistenceService != null
                              && persistenceService.IsInitialized
                              && persistenceService.HasPendingReturn;
            if (shouldShow)
            {
                if (awayText != null)
                {
                    awayText.text = $"Away: {FormatAwayDuration(persistenceService.PendingAwaySeconds)}";
                }

                if (earnedText != null)
                {
                    earnedText.text = $"Earned: ${persistenceService.PendingOfflineCash}";
                }
            }

            if (collectButton != null)
            {
                collectButton.interactable = shouldShow && !_collectionInProgress;
            }
            if (doubleRewardButton != null)
                doubleRewardButton.interactable = shouldShow && !_collectionInProgress
                    && monetizationService != null
                    && monetizationService.CanOfferReward(RewardedPlacement.OfflineDouble);
            if (doubleRewardButton != null && doubleRewardButton.interactable)
                monetizationService.TrackRewardedOfferVisible(RewardedPlacement.OfflineDouble);

            SetVisible(shouldShow);
        }

        public static string FormatAwayDuration(double awaySeconds)
        {
            long totalMinutes = Math.Max(0L, (long)Math.Floor(awaySeconds / 60d));
            long hours = totalMinutes / 60L;
            long minutes = totalMinutes % 60L;
            return hours > 0L ? $"{hours}h {minutes:00}m" : $"{minutes}m";
        }

        private void HandleReturnStateChanged()
        {
            _collectionInProgress = false;
            Refresh();
        }

        private void HandleCollectClicked()
        {
            if (_collectionInProgress
                || persistenceService == null
                || !persistenceService.HasPendingReturn)
            {
                return;
            }

            _collectionInProgress = true;
            if (!persistenceService.TryCollectOfflineReward(1f))
            {
                _collectionInProgress = false;
            }

            Refresh();
        }

        private void HandleDoubleClicked()
        {
            if (_collectionInProgress || monetizationService == null) return;
            _collectionInProgress = monetizationService.TryShowRewarded(RewardedPlacement.OfflineDouble);
            Refresh();
        }

        private void SetVisible(bool visible)
        {
            if (overlayRoot != null && overlayRoot.activeSelf != visible)
            {
                overlayRoot.SetActive(visible);
            }

            if (visible)
            {
                CaptureAndDisableControls();
            }
            else
            {
                RestoreControls();
            }
        }

        private void CaptureAndDisableControls()
        {
            if (!_controlsCaptured)
            {
                _movementWasEnabled = playerMovement != null && playerMovement.enabled;
                _dragInputWasEnabled = playerDragInput != null && playerDragInput.enabled;
                _controlsCaptured = true;
            }

            if (playerMovement != null)
            {
                playerMovement.enabled = false;
            }

            if (playerDragInput != null)
            {
                playerDragInput.enabled = false;
            }
        }

        private void RestoreControls()
        {
            if (!_controlsCaptured)
            {
                return;
            }

            if (playerMovement != null)
            {
                playerMovement.enabled = _movementWasEnabled;
            }

            if (playerDragInput != null)
            {
                playerDragInput.enabled = _dragInputWasEnabled;
            }

            _controlsCaptured = false;
        }
    }
}
