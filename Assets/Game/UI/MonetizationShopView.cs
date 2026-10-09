using System;
using IndustryTycoon.Monetization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class MonetizationShopView : MonoBehaviour
    {
        [SerializeField] private MonetizationService monetization;
        [SerializeField] private Text gemsText;
        [SerializeField] private Text statusText;
        [SerializeField] private Text boostStatusText;
        [SerializeField] private Button[] productButtons;
        [SerializeField] private Text[] productLabels;
        [SerializeField] private Button rewardedBoostButton;
        [SerializeField] private Button gemBoostButton;
        [SerializeField] private Button rewardedRefreshButton;
        [SerializeField] private Button gemRefreshButton;
        [SerializeField] private Button restoreButton;
        private UnityAction[] _productActions;

        private static readonly string[] ProductKeys =
        {
            M21ProductCatalog.SmallGems, M21ProductCatalog.MediumGems,
            M21ProductCatalog.LargeGems, M21ProductCatalog.StarterPack
        };

        private void OnEnable()
        {
            if (monetization != null)
            {
                monetization.StateChanged += Refresh;
                monetization.UserMessage += SetStatus;
            }
            if (productButtons != null)
            {
                _productActions = new UnityAction[productButtons.Length];
                for (int i = 0; i < productButtons.Length && i < ProductKeys.Length; i++)
                {
                    int index = i;
                    _productActions[i] = () => Buy(ProductKeys[index]);
                    productButtons[i]?.onClick.AddListener(_productActions[i]);
                }
            }
            rewardedBoostButton?.onClick.AddListener(WatchForBoost);
            gemBoostButton?.onClick.AddListener(BuyBoost);
            rewardedRefreshButton?.onClick.AddListener(WatchForRefresh);
            gemRefreshButton?.onClick.AddListener(BuyRefresh);
            restoreButton?.onClick.AddListener(Restore);
            monetization?.Track("shop_open");
            Refresh();
        }

        private void OnDisable()
        {
            if (monetization != null)
            {
                monetization.StateChanged -= Refresh;
                monetization.UserMessage -= SetStatus;
            }
            if (productButtons != null && _productActions != null)
                for (int i = 0; i < productButtons.Length && i < _productActions.Length; i++)
                    if (_productActions[i] != null) productButtons[i]?.onClick.RemoveListener(_productActions[i]);
            _productActions = null;
            rewardedBoostButton?.onClick.RemoveListener(WatchForBoost);
            gemBoostButton?.onClick.RemoveListener(BuyBoost);
            rewardedRefreshButton?.onClick.RemoveListener(WatchForRefresh);
            gemRefreshButton?.onClick.RemoveListener(BuyRefresh);
            restoreButton?.onClick.RemoveListener(Restore);
            monetization?.Track("shop_close");
        }

        public void Refresh()
        {
            int gems = monetization?.Gems?.Balance ?? 0;
            if (gemsText != null) gemsText.text = "GEMS  " + gems;
            bool storeReady = monetization?.Purchases?.Availability == ServiceAvailability.Available;
            for (int i = 0; i < ProductKeys.Length; i++)
            {
                M21ProductCatalog.TryGet(ProductKeys[i], out M21ProductDefinition product);
                string title = ProductKeys[i] == M21ProductCatalog.StarterPack
                    ? $"STARTER PACK  •  {product.Gems} Gems + ${product.Cash}"
                    : $"{product.Gems} GEMS";
                string price = monetization != null && monetization.Purchases.TryGetLocalizedPrice(ProductKeys[i], out string localized)
                    ? localized : "UNAVAILABLE";
                if (productLabels != null && i < productLabels.Length && productLabels[i] != null)
                    productLabels[i].text = title + "\n" + price;
                if (productButtons != null && i < productButtons.Length && productButtons[i] != null)
                    productButtons[i].interactable = storeReady
                        && !(ProductKeys[i] == M21ProductCatalog.StarterPack && monetization.HasStarterPackEntitlement);
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long remaining = monetization?.State == null ? 0L
                : Math.Max(0L, monetization.State.productionBoostExpiresUtcUnixSeconds - now);
            if (boostStatusText != null) boostStatusText.text = remaining > 0
                ? $"PRODUCTION ×2  {remaining / 60}:{remaining % 60:00}" : "PRODUCTION BOOST READY";
            bool canBoost = monetization != null && !monetization.IsBoostActive;
            if (rewardedBoostButton != null) rewardedBoostButton.interactable = canBoost
                && monetization.CanOfferReward(RewardedPlacement.ProductionBoost);
            if (gemBoostButton != null) gemBoostButton.interactable = canBoost && gems >= M21MonetizationRules.BoostGemCost;
            if (rewardedRefreshButton != null) rewardedRefreshButton.interactable = monetization != null
                && monetization.CanOfferReward(RewardedPlacement.OrderRefresh);
            if (gemRefreshButton != null) gemRefreshButton.interactable = gems >= M21MonetizationRules.OrderRefreshGemCost;
            if (restoreButton != null) restoreButton.interactable = storeReady;
            if (statusText != null && string.IsNullOrEmpty(statusText.text))
                statusText.text = storeReady ? "STORE READY" : "STORE UNAVAILABLE";
            if (rewardedBoostButton != null && rewardedBoostButton.interactable)
                monetization.TrackRewardedOfferVisible(RewardedPlacement.ProductionBoost);
            if (rewardedRefreshButton != null && rewardedRefreshButton.interactable)
                monetization.TrackRewardedOfferVisible(RewardedPlacement.OrderRefresh);
        }

        private void Buy(string key)
        {
            if (monetization == null || !monetization.TryPurchase(key)) SetStatus("PURCHASE UNAVAILABLE");
        }
        private void WatchForBoost() => monetization?.TryShowRewarded(RewardedPlacement.ProductionBoost);
        private void BuyBoost() => SetStatus(monetization != null && monetization.TryBuyProductionBoostWithGems() ? "BOOST ACTIVE" : "BOOST UNAVAILABLE");
        private void WatchForRefresh() => monetization?.TryShowRewarded(RewardedPlacement.OrderRefresh);
        private void BuyRefresh() => SetStatus(monetization != null && monetization.TryRefreshOrdersWithGems() ? "ORDERS REFRESHED" : "REFRESH UNAVAILABLE");
        private void Restore() { monetization?.RestorePurchases(); SetStatus("RESTORE REQUESTED"); }
        private void SetStatus(string message) { if (statusText != null) statusText.text = message ?? string.Empty; }
    }
}
