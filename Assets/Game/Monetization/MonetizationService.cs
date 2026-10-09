using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Market;
using IndustryTycoon.Persistence;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.Monetization
{
    [DefaultExecutionOrder(-810)]
    public sealed class MonetizationService : MonoBehaviour
    {
        private const string ConsentKey = "industry_tycoon.privacy.consent";
        private const string StarterEntitlementKey = "industry_tycoon.entitlement.starter_pack";

        [SerializeField] private LocalPersistenceService persistence;
        [SerializeField] private MarketPlanningService market;
        [SerializeField] private Wallet wallet;
        [SerializeField] private LumberCampProgressionService progression;

        private M21MonetizationSaveData _state;
        private IPremiumCurrencyService _gems;
        private IRewardedAdService _ads;
        private IPurchaseService _purchases;
        private IAnalyticsService _analytics;
        private readonly HashSet<string> _requestsInFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<RewardedPlacement> _placementsInFlight = new HashSet<RewardedPlacement>();
        private readonly HashSet<string> _rewardedOffersTracked = new HashSet<string>(StringComparer.Ordinal);
        private bool _subscribed;
        private bool _lifecycleStarted;
        private bool _sessionOpen;
        private bool _sessionStartTracked;
        private string _lastPurchaseAvailabilityEvent;

        public event Action StateChanged;
        public event Action<string> UserMessage;
        public M21MonetizationSaveData State => _state;
        public IPremiumCurrencyService Gems => _gems;
        public IRewardedAdService RewardedAds => _ads;
        public IPurchaseService Purchases => _purchases;
        public IAnalyticsService Analytics => _analytics;
        public ConsentStatus Consent { get; private set; }
        public bool HasStarterPackEntitlement => PlayerPrefs.GetInt(StarterEntitlementKey, 0) != 0;
        public bool IsBoostActive => _state != null && M21MonetizationRules.IsBoostActive(
            _state, SystemUtcClock.Instance.UtcNowUnixSeconds);

        private void Awake()
        {
            _state = M21MonetizationSaveData.CreateFresh(SystemUtcClock.Instance.UtcNowUnixSeconds);
            RebindState();
            Consent = (ConsentStatus)Mathf.Clamp(PlayerPrefs.GetInt(ConsentKey, 0), 0, 2);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _ads = new TestRewardedAdService();
            _purchases = new TestPurchaseService();
#else
            _ads = new UnavailableRewardedAdService();
            _purchases = new UnavailablePurchaseService();
#endif
            _analytics = new SafeAnalyticsService();
            _analytics.SetConsent(Consent);
            Subscribe();
            _purchases.Initialize(M21ProductCatalog.Products);
            ProductionBoostRuntime.Bind(() => IsBoostActive);
        }

        private void Start()
        {
            Subscribe();
            _lifecycleStarted = true;
            BeginAnalyticsSession();
            TrackPurchaseAvailability();
            ResumePendingPurchases();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_lifecycleStarted) return;
            if (paused) EndAnalyticsSession();
            else BeginAnalyticsSession();
        }
        private void OnApplicationQuit() => EndAnalyticsSession();
        private void OnDestroy()
        {
            Unsubscribe();
            ProductionBoostRuntime.Reset();
        }

        public void ConfigureForTests(IRewardedAdService ads, IPurchaseService purchases,
            IAnalyticsService analytics, M21MonetizationSaveData state = null,
            Wallet testWallet = null)
        {
            Unsubscribe();
            _ads = ads ?? new UnavailableRewardedAdService();
            _purchases = purchases ?? new UnavailablePurchaseService();
            _analytics = analytics ?? new SafeAnalyticsService();
            if (testWallet != null) wallet = testWallet;
            if (state != null) _state = state;
            RebindState();
            Subscribe();
            _purchases.Initialize(M21ProductCatalog.Products);
        }

        public void SetConsent(ConsentStatus consent)
        {
            Consent = consent;
            PlayerPrefs.SetInt(ConsentKey, (int)consent);
            PlayerPrefs.Save();
            _analytics?.SetConsent(consent);
            if (consent == ConsentStatus.Granted)
            {
                TryTrackSessionStart();
                TrackPurchaseAvailability();
            }
            StateChanged?.Invoke();
        }

        public M21MonetizationSaveData CapturePersistentState()
        {
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            M21MonetizationRules.AcceptTime(_state, now);
            return M21MonetizationSaveValidator.TryNormalize(_state, now, out M21MonetizationSaveData copy, out _)
                ? copy : M21MonetizationSaveData.CreateFresh(now);
        }

        public void RestorePersistentState(M21MonetizationSaveData state)
        {
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            _state = M21MonetizationSaveValidator.TryNormalize(state, now, out M21MonetizationSaveData normalized, out _)
                ? normalized : M21MonetizationSaveData.CreateFresh(now);
            RebindState();
            StateChanged?.Invoke();
        }

        public bool CanOfferReward(RewardedPlacement placement)
        {
            if (Consent != ConsentStatus.Granted || _ads == null || !_ads.IsReady(placement) || _state == null) return false;
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            if (placement == RewardedPlacement.OfflineDouble)
                return persistence != null && persistence.HasPendingReturn && !string.IsNullOrEmpty(_state.offlineRewardSessionId);
            if (placement == RewardedPlacement.ProductionBoost)
                return now >= _state.lastAcceptedUtcUnixSeconds
                       && !M21MonetizationRules.IsBoostActive(_state, now);
            return market != null && market.IsMarketUnlocked && market.HasRefreshableOrder
                   && M21MonetizationRules.CanRefreshOrders(_state, now);
        }

        public bool TryShowRewarded(RewardedPlacement placement)
        {
            if (!CanOfferReward(placement)) { UserMessage?.Invoke("Rewarded ad unavailable."); return false; }
            if (!_placementsInFlight.Add(placement)) return false;
            TrackRewardedOfferVisible(placement);
            string requestId = placement == RewardedPlacement.OfflineDouble
                ? _state.offlineRewardSessionId : "ad:" + placement + ":" + Guid.NewGuid().ToString("N");
            if (!_requestsInFlight.Add(requestId))
            {
                _placementsInFlight.Remove(placement);
                return false;
            }
            Track("rewarded_start", P("placement", placement.ToString()));
            bool started = _ads.TryShow(requestId, placement, HandleRewardedOutcome);
            if (!started && _requestsInFlight.Remove(requestId))
            {
                _placementsInFlight.Remove(placement);
                Track("rewarded_fail", P("placement", placement.ToString()));
            }
            return started;
        }

        public void TrackRewardedOfferVisible(RewardedPlacement placement)
        {
            if (!CanOfferReward(placement)) return;
            string eligibility = placement == RewardedPlacement.OfflineDouble
                ? _state.offlineRewardSessionId
                : placement == RewardedPlacement.OrderRefresh
                    ? _state.orderRefreshWindowStartUtcUnixSeconds + ":" + _state.orderRefreshesInWindow
                    : "session";
            string key = placement + ":" + eligibility;
            if (_rewardedOffersTracked.Add(key))
                Track("rewarded_offer", P("placement", placement.ToString()));
        }

        public bool TryBuyProductionBoostWithGems()
        {
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            if (_state == null || now < _state.lastAcceptedUtcUnixSeconds
                || M21MonetizationRules.IsBoostActive(_state, now)) return false;
            string tx = "spend:boost:" + Guid.NewGuid().ToString("N");
            if (!_gems.TrySpend(tx, M21MonetizationRules.BoostGemCost, "production_boost")) return false;
            if (!M21MonetizationRules.TryStartBoost(_state, now)) return false;
            CommitState();
            return true;
        }

        public bool TryRefreshOrdersWithGems()
        {
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            if (!M21MonetizationRules.CanRefreshOrders(_state, now)
                || market == null || !market.HasRefreshableOrder) return false;
            string tx = "spend:refresh:" + Guid.NewGuid().ToString("N");
            if (!_gems.TrySpend(tx, M21MonetizationRules.OrderRefreshGemCost, "order_refresh")) return false;
            if (!M21MonetizationRules.TryConsumeOrderRefresh(_state, now) || !market.TryRefreshAvailableOrders()) return false;
            CommitState();
            return true;
        }

        public bool TryPurchase(string productKey)
        {
            Track("shop_purchase_start", P("product", productKey));
            return _purchases != null && _purchases.TryPurchase(productKey);
        }

        public void RestorePurchases() => _purchases?.RestoreEntitlements();

        public void EnsureOfflineSession(long evaluationUtc, int pendingCash, long awaySeconds)
        {
            if (_state == null || pendingCash <= 0 || awaySeconds <= 0) return;
            string id = "offline:" + evaluationUtc + ":" + pendingCash + ":" + awaySeconds;
            if (string.IsNullOrEmpty(_state.offlineRewardSessionId))
            {
                _state.offlineRewardSessionId = id;
                CommitState();
            }
        }

        private void HandleRewardedOutcome(RewardedAdOutcome outcome)
        {
            if (!_requestsInFlight.Remove(outcome.RequestId)) return;
            _placementsInFlight.Remove(outcome.Placement);
            if (outcome.Result != RewardedResult.Completed)
            {
                Track("rewarded_fail", P("placement", outcome.Placement.ToString()));
                UserMessage?.Invoke(outcome.Result == RewardedResult.Cancelled ? "Ad cancelled." : "Ad failed.");
                return;
            }
            if (Contains(_state.completedRewardTransactions, outcome.RequestId)) return;
            Track("rewarded_complete", P("placement", outcome.Placement.ToString()));
            bool granted = false;
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            if (outcome.Placement == RewardedPlacement.OfflineDouble)
                granted = persistence != null && persistence.TryCollectOfflineReward(2f);
            else if (outcome.Placement == RewardedPlacement.ProductionBoost)
                granted = M21MonetizationRules.TryStartBoost(_state, now);
            else if (M21MonetizationRules.TryConsumeOrderRefresh(_state, now))
                granted = market != null && market.TryRefreshAvailableOrders();
            if (!granted) { Track("rewarded_fail", P("placement", outcome.Placement.ToString())); return; }
            if (!M21MonetizationRules.TryRecordRewardCompletion(_state, outcome.RequestId)) return;
            if (outcome.Placement == RewardedPlacement.OfflineDouble) _state.offlineRewardSessionId = string.Empty;
            Track("rewarded_grant", P("placement", outcome.Placement.ToString()));
            CommitState();
        }

        private void HandlePurchase(PurchaseOutcome outcome)
        {
            Track("purchase_" + outcome.State.ToString().ToLowerInvariant(), P("product", outcome.ProductKey));
            if (outcome.State == PurchaseState.Successful)
            {
                if (!string.IsNullOrWhiteSpace(outcome.TransactionId))
                {
                    if (RemovePending(outcome.TransactionId)) CommitState();
                }
                UserMessage?.Invoke("PURCHASE SUCCESSFUL");
            }
            else if (outcome.State == PurchaseState.Cancelled) UserMessage?.Invoke("PURCHASE CANCELLED");
            else if (outcome.State == PurchaseState.Failed) UserMessage?.Invoke("PURCHASE FAILED");
            else if (outcome.State == PurchaseState.Pending) UserMessage?.Invoke("PURCHASE PENDING");
            if (outcome.State != PurchaseState.Pending || string.IsNullOrWhiteSpace(outcome.TransactionId))
            { StateChanged?.Invoke(); return; }
            if (!M21ProductCatalog.TryGet(outcome.ProductKey, out M21ProductDefinition product)) return;
            bool permanentAlreadyOwned = product.PermanentEntitlement && HasStarterPackEntitlement;
            bool granted = permanentAlreadyOwned || _gems.HasTransaction("iap:" + outcome.TransactionId);
            if (!granted)
            {
                if (product.Cash > 0 && (wallet == null || wallet.Balance > int.MaxValue - product.Cash))
                {
                    UpsertPending(outcome.ProductKey, outcome.TransactionId, false);
                    CommitState();
                    return;
                }
                granted = _gems.TryGrant("iap:" + outcome.TransactionId, product.Gems, "iap:" + product.Key);
                if (granted && product.Cash > 0 && wallet != null) granted = wallet.Deposit(product.Cash) == product.Cash;
                if (granted && product.PermanentEntitlement)
                {
                    PlayerPrefs.SetInt(StarterEntitlementKey, 1);
                    PlayerPrefs.Save();
                }
            }
            UpsertPending(outcome.ProductKey, outcome.TransactionId, granted);
            if (!granted || !CommitState()) return;
            if (_purchases.Confirm(outcome.TransactionId))
            {
                if (RemovePending(outcome.TransactionId)) CommitState();
            }
        }

        public void ResumePendingPurchases()
        {
            M21PendingPurchaseRecord[] pending = _state?.pendingPurchases;
            if (pending == null) return;
            for (int i = 0; i < pending.Length; i++)
                if (pending[i]?.granted == true && _purchases?.Confirm(pending[i].transactionId) == true)
                {
                    if (RemovePending(pending[i].transactionId)) CommitState();
                }
        }

        private void RebindState()
        {
            _gems = new PremiumCurrencyLedger(_state, () => SystemUtcClock.Instance.UtcNowUnixSeconds);
            _gems.BalanceChanged += _ => StateChanged?.Invoke();
        }

        private void Subscribe()
        {
            if (_subscribed || _purchases == null) return;
            _purchases.CatalogChanged += HandleCatalogChanged;
            _purchases.PurchaseUpdated += HandlePurchase;
            InternalAnalytics.EventPublished += HandleInternalAnalytics;
            if (progression != null) progression.AchievementUnlocked += HandleAchievementUnlocked;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_purchases != null)
            {
                _purchases.CatalogChanged -= HandleCatalogChanged;
                _purchases.PurchaseUpdated -= HandlePurchase;
            }
            InternalAnalytics.EventPublished -= HandleInternalAnalytics;
            if (progression != null) progression.AchievementUnlocked -= HandleAchievementUnlocked;
            _subscribed = false;
        }

        private void HandleCatalogChanged()
        {
            TrackPurchaseAvailability();
            StateChanged?.Invoke();
        }
        private void HandleInternalAnalytics(InternalAnalyticsEvent evt) => Track(evt.Name,
            string.IsNullOrEmpty(evt.Parameter) ? null : P("value", evt.Parameter));
        private void HandleAchievementUnlocked(int index)
        {
            int amount = index == 4 ? 5 : index == 10 ? 10 : index == 18 ? 20 : 0;
            if (amount <= 0 || !_gems.TryGrant("achievement:" + index, amount, "achievement")) return;
            CommitState();
        }
        public void Track(string name, IReadOnlyDictionary<string, string> values = null) => _analytics?.Track(name, values);
        private void BeginAnalyticsSession()
        {
            if (_sessionOpen) { TryTrackSessionStart(); return; }
            _sessionOpen = true;
            _sessionStartTracked = false;
            TryTrackSessionStart();
        }
        private void TryTrackSessionStart()
        {
            if (!_sessionOpen || _sessionStartTracked || _analytics?.IsCollectionEnabled != true) return;
            Track("session_start", P("empire_level",
                (progression != null ? progression.EmpireLevel : M10ProgressionRules.MinimumEmpireLevel).ToString()));
            _sessionStartTracked = true;
        }
        private void EndAnalyticsSession()
        {
            if (!_sessionOpen) return;
            if (_sessionStartTracked) Track("session_end");
            _analytics?.Flush();
            _sessionOpen = false;
            _sessionStartTracked = false;
        }
        private void TrackPurchaseAvailability()
        {
            if (_analytics?.IsCollectionEnabled != true || _purchases == null) return;
            string eventName = "purchase_" + _purchases.State.ToString().ToLowerInvariant();
            if (string.Equals(eventName, _lastPurchaseAvailabilityEvent, StringComparison.Ordinal)) return;
            _lastPurchaseAvailabilityEvent = eventName;
            Track(eventName);
        }
        private bool CommitState() { StateChanged?.Invoke(); return persistence == null || persistence.SaveNow(); }
        private static bool Contains(string[] values, string value)
        { if (values == null) return false; for (int i = 0; i < values.Length; i++) if (string.Equals(values[i], value, StringComparison.Ordinal)) return true; return false; }
        private void UpsertPending(string product, string tx, bool granted)
        {
            var list = new List<M21PendingPurchaseRecord>(_state.pendingPurchases ?? Array.Empty<M21PendingPurchaseRecord>());
            for (int i = 0; i < list.Count; i++) if (string.Equals(list[i].transactionId, tx, StringComparison.Ordinal))
            { list[i].granted |= granted; _state.pendingPurchases = list.ToArray(); return; }
            list.Add(new M21PendingPurchaseRecord { productKey = product, transactionId = tx, granted = granted });
            _state.pendingPurchases = list.ToArray();
        }
        private bool RemovePending(string tx)
        {
            var list = new List<M21PendingPurchaseRecord>(_state.pendingPurchases ?? Array.Empty<M21PendingPurchaseRecord>());
            int removed = list.RemoveAll(x => x != null && string.Equals(x.transactionId, tx, StringComparison.Ordinal));
            if (removed == 0) return false;
            _state.pendingPurchases = list.ToArray();
            return true;
        }
        private static IReadOnlyDictionary<string, string> P(string key, string value) =>
            new Dictionary<string, string>(1, StringComparer.Ordinal) { [key] = value ?? string.Empty };

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [ContextMenu("QA/Rewarded Next Success")] private void QaAdSuccess() => (_ads as TestRewardedAdService)?.SetNextResult(RewardedResult.Completed);
        [ContextMenu("QA/Rewarded Next Failure")] private void QaAdFailure() => (_ads as TestRewardedAdService)?.SetNextResult(RewardedResult.Failed);
        [ContextMenu("QA/Rewarded Next Cancel")] private void QaAdCancel() => (_ads as TestRewardedAdService)?.SetNextResult(RewardedResult.Cancelled);
        [ContextMenu("QA/Purchase Next Success")] private void QaPurchaseSuccess()
        { if (_purchases is TestPurchaseService test) { test.HoldConfirmations = false; test.SetNextState(PurchaseState.Pending); } }
        [ContextMenu("QA/Purchase Next Pending")] private void QaPurchasePending()
        { if (_purchases is TestPurchaseService test) { test.HoldConfirmations = true; test.SetNextState(PurchaseState.Pending); } }
        [ContextMenu("QA/Purchase Next Failure")] private void QaPurchaseFailure() => (_purchases as TestPurchaseService)?.SetNextState(PurchaseState.Failed);
        [ContextMenu("QA/Purchase Next Cancel")] private void QaPurchaseCancel() => (_purchases as TestPurchaseService)?.SetNextState(PurchaseState.Cancelled);
        [ContextMenu("QA/Restore Entitlements")] private void QaRestore() => RestorePurchases();
#endif
    }
}
