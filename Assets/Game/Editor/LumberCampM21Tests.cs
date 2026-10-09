using System;
using System.Collections.Generic;
using IndustryTycoon.Economy;
using IndustryTycoon.Market;
using IndustryTycoon.Monetization;
using IndustryTycoon.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    public sealed class LumberCampM21Tests
    {
        private const long Now = 2000000000L;

        [Test] public void Gems_AtomicGrantSpendAndInsufficientFunds() => TestGems();
        [Test] public void Gems_DuplicateTransactionIsExactlyOnce() => TestGemDedupe();
        [Test] public void Gems_PersistenceAndResetSemantics() => TestGemPersistence();
        [Test] public void Rewarded_SuccessCancelFailureAndDuplicateCallback() => TestRewardedAdapter();
        [Test] public void Rewarded_GameplayGrantIsCompletionOnlyAndIdempotent() => TestRewardedGameplay();
        [Test] public void Rewarded_RepeatedTapAllowsOnlyOneDisplay() => TestRepeatedRewardTap();
        [Test] public void OfflineDouble_IsOncePerPersistentSession() => TestOfflineOnce();
        [Test] public void Boost_IsPersistentNonStackingAndRollbackSafe() => TestBoost();
        [Test] public void OrderRefresh_PreservesActiveAndPersistsLimits() => TestRefresh();
        [Test] public void Purchase_InitPendingConfirmCancelAndFailure() => TestPurchaseStates();
        [Test] public void Purchase_GrantedPendingResumesWithoutDuplicateGrant() => TestPendingRestart();
        [Test] public void Purchase_RestorationUsesPermanentLogicalProduct() => TestRestore();
        [Test] public void Analytics_ConsentAndTransportFailureAreSafe() => TestAnalytics();
        [Test] public void Catalog_HasNoFabricatedExternalIdsOrDiscounts() => TestCatalog();
        [Test] public void Schema_V8MigratesOnceToV9() => TestV8Migration();

        private static void TestGems()
        {
            M21MonetizationSaveData state = M21MonetizationSaveData.CreateFresh(Now);
            var gems = new PremiumCurrencyLedger(state, () => Now);
            Assert.IsTrue(gems.TryGrant("grant:1", 25, "test"));
            Assert.AreEqual(25, gems.Balance);
            Assert.IsFalse(gems.TrySpend("spend:too_much", 26, "test"));
            Assert.AreEqual(25, gems.Balance);
            Assert.IsTrue(gems.TrySpend("spend:1", 5, "test"));
            Assert.AreEqual(20, gems.Balance);
            Assert.IsFalse(gems.TryGrant("invalid", -1, "test"));
        }

        private static void TestGemDedupe()
        {
            var state = M21MonetizationSaveData.CreateFresh(Now);
            var gems = new PremiumCurrencyLedger(state, () => Now);
            Assert.IsTrue(gems.TryGrant("same", 10, "test"));
            Assert.IsFalse(gems.TryGrant("same", 10, "test"));
            Assert.AreEqual(10, gems.Balance);
            Assert.AreEqual(1, state.gemLedger.Length);
        }

        private static void TestGemPersistence()
        {
            M9SaveData save = M9SaveData.CreateFresh(Now);
            var gems = new PremiumCurrencyLedger(save.monetization, () => Now);
            Assert.IsTrue(gems.TryGrant("persist", 50, "test"));
            Assert.IsTrue(M9SaveCodec.TryEncode(save, M9SaveValidationSettings.CreateDefault(), Now,
                out string json, out _, out string failure), failure);
            M9SaveDecodeResult decode = M9SaveCodec.Decode(json, M9SaveValidationSettings.CreateDefault(), Now);
            Assert.IsTrue(decode.IsSuccess, decode.Diagnostic);
            Assert.AreEqual(50, decode.Data.monetization.gemBalance);
            Assert.AreEqual(0, M9SaveData.CreateFresh(Now).monetization.gemBalance,
                "Reset Save must reset Gems and consumable state.");
            Assert.IsNull(typeof(M21MonetizationSaveData).GetField("starterPackEntitlement"),
                "Permanent store-restorable entitlements must not depend solely on gameplay save.");
        }

        private static void TestRewardedAdapter()
        {
            var ads = new TestRewardedAdService();
            int callbacks = 0;
            ads.SetNextResult(RewardedResult.Completed);
            Assert.IsTrue(ads.TryShow("r1", RewardedPlacement.ProductionBoost, x =>
            { Assert.AreEqual(RewardedResult.Completed, x.Result); callbacks++; }));
            ads.Replay("r1", RewardedPlacement.ProductionBoost, RewardedResult.Completed, _ => callbacks++);
            Assert.AreEqual(2, callbacks, "Provider may duplicate; gameplay dedupe must use request ID.");
            ads.SetNextResult(RewardedResult.Cancelled);
            ads.TryShow("r2", RewardedPlacement.OrderRefresh, x => Assert.AreEqual(RewardedResult.Cancelled, x.Result));
            ads.SetNextResult(RewardedResult.Failed);
            ads.TryShow("r3", RewardedPlacement.OrderRefresh, x => Assert.AreEqual(RewardedResult.Failed, x.Result));
        }

        private static void TestRewardedGameplay()
        {
            const string consentKey = "industry_tycoon.privacy.consent";
            bool hadConsent = PlayerPrefs.HasKey(consentKey);
            int previousConsent = PlayerPrefs.GetInt(consentKey, 0);
            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var ads = new TestRewardedAdService();
                var eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                var analytics = new SafeAnalyticsService((name, _) =>
                    eventCounts[name] = eventCounts.TryGetValue(name, out int count) ? count + 1 : 1);
                GameObject root = new GameObject("M21 Reward Test");
                MonetizationService service = root.AddComponent<MonetizationService>();
                service.ConfigureForTests(ads, new UnavailablePurchaseService(),
                    analytics, M21MonetizationSaveData.CreateFresh(now));
                service.SetConsent(ConsentStatus.Granted);
                service.TrackRewardedOfferVisible(RewardedPlacement.ProductionBoost);
                service.TrackRewardedOfferVisible(RewardedPlacement.ProductionBoost);
                Assert.AreEqual(1, eventCounts["rewarded_offer"]);
                Assert.IsTrue(service.TryShowRewarded(RewardedPlacement.ProductionBoost));
                long expiry = service.State.productionBoostExpiresUtcUnixSeconds;
                Assert.Greater(expiry, now);
                Assert.AreEqual(1, service.State.completedRewardTransactions.Length);
                ads.ReplayLast(RewardedResult.Completed);
                Assert.AreEqual(expiry, service.State.productionBoostExpiresUtcUnixSeconds);
                Assert.AreEqual(1, service.State.completedRewardTransactions.Length,
                    "A duplicate provider callback must never grant twice.");
                Assert.AreEqual(1, eventCounts["rewarded_start"]);
                Assert.AreEqual(1, eventCounts["rewarded_complete"]);
                Assert.AreEqual(1, eventCounts["rewarded_grant"]);
                UnityEngine.Object.DestroyImmediate(root);

                AssertRewardDoesNotGrant(RewardedResult.Cancelled, now);
                AssertRewardDoesNotGrant(RewardedResult.Failed, now);
            }
            finally
            {
                if (hadConsent) PlayerPrefs.SetInt(consentKey, previousConsent);
                else PlayerPrefs.DeleteKey(consentKey);
                PlayerPrefs.Save();
            }
        }

        private static void AssertRewardDoesNotGrant(RewardedResult result, long now)
        {
            var ads = new TestRewardedAdService();
            ads.SetNextResult(result);
            GameObject root = new GameObject("M21 Reward No Grant Test");
            MonetizationService service = root.AddComponent<MonetizationService>();
            service.ConfigureForTests(ads, new UnavailablePurchaseService(),
                new SafeAnalyticsService(), M21MonetizationSaveData.CreateFresh(now));
            service.SetConsent(ConsentStatus.Granted);
            Assert.IsTrue(service.TryShowRewarded(RewardedPlacement.ProductionBoost));
            Assert.AreEqual(0L, service.State.productionBoostExpiresUtcUnixSeconds);
            Assert.AreEqual(0, service.State.completedRewardTransactions.Length);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void TestRepeatedRewardTap()
        {
            const string consentKey = "industry_tycoon.privacy.consent";
            bool hadConsent = PlayerPrefs.HasKey(consentKey);
            int previousConsent = PlayerPrefs.GetInt(consentKey, 0);
            GameObject root = null;
            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var ads = new DeferredRewardedAdService();
                root = new GameObject("M21 Repeated Tap Test");
                MonetizationService service = root.AddComponent<MonetizationService>();
                service.ConfigureForTests(ads, new UnavailablePurchaseService(),
                    new SafeAnalyticsService(), M21MonetizationSaveData.CreateFresh(now));
                service.SetConsent(ConsentStatus.Granted);
                Assert.IsTrue(service.TryShowRewarded(RewardedPlacement.ProductionBoost));
                Assert.IsFalse(service.TryShowRewarded(RewardedPlacement.ProductionBoost));
                Assert.AreEqual(1, ads.DisplayCount);
                ads.Complete(RewardedResult.Cancelled);
                Assert.AreEqual(0L, service.State.productionBoostExpiresUtcUnixSeconds);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (hadConsent) PlayerPrefs.SetInt(consentKey, previousConsent);
                else PlayerPrefs.DeleteKey(consentKey);
                PlayerPrefs.Save();
            }
        }

        private static void TestBoost()
        {
            var state = M21MonetizationSaveData.CreateFresh(Now);
            Assert.IsTrue(M21MonetizationRules.TryStartBoost(state, Now));
            Assert.AreEqual(Now + 300, state.productionBoostExpiresUtcUnixSeconds);
            Assert.IsFalse(M21MonetizationRules.TryStartBoost(state, Now + 1));
            Assert.IsTrue(M21MonetizationRules.IsBoostActive(state, Now + 299));
            Assert.IsFalse(M21MonetizationRules.IsBoostActive(state, Now - 1));
            Assert.IsFalse(M21MonetizationRules.AcceptTime(state, Now - 1));
            Assert.IsTrue(M21MonetizationSaveValidator.TryNormalize(state, Now, out var copy, out string failure), failure);
            Assert.AreEqual(state.productionBoostExpiresUtcUnixSeconds, copy.productionBoostExpiresUtcUnixSeconds);
            try
            {
                ProductionBoostRuntime.Bind(() => true);
                Assert.AreEqual(1f, ProductionBoostRuntime.ScaleDuration(2f));
                Assert.AreEqual(2f, ProductionBoostRuntime.ScaleDelta(1f));
            }
            finally
            {
                ProductionBoostRuntime.Reset();
            }
        }

        private static void TestOfflineOnce()
        {
            var state = M21MonetizationSaveData.CreateFresh(Now);
            state.offlineRewardSessionId = "offline:session";
            Assert.IsTrue(M21MonetizationRules.TryRecordRewardCompletion(state, state.offlineRewardSessionId));
            Assert.IsFalse(M21MonetizationRules.TryRecordRewardCompletion(state, state.offlineRewardSessionId));
            Assert.IsTrue(M21MonetizationSaveValidator.TryNormalize(state, Now, out var restored, out string failure), failure);
            Assert.IsFalse(M21MonetizationRules.TryRecordRewardCompletion(restored, "offline:session"));
        }

        private static void TestRefresh()
        {
            var market = M16MarketSaveData.CreateFresh(Now);
            M16MarketRules.RefillOrders(market, MarketUnlockContext.All);
            Assert.IsTrue(M16MarketRules.TryActivateOrder(market, 1));
            long activeId = market.orders[1].id;
            Assert.IsTrue(M16MarketRules.RefreshAvailableOrders(market, MarketUnlockContext.All));
            Assert.AreEqual(activeId, market.orders[1].id);
            Assert.AreEqual(ProductionOrderState.Active, market.orders[1].state);

            var state = M21MonetizationSaveData.CreateFresh(Now);
            for (int i = 0; i < M21MonetizationRules.MaximumOrderRefreshesPerWindow; i++)
            {
                long time = Now + i * M21MonetizationRules.OrderRefreshCooldownSeconds;
                Assert.IsTrue(M21MonetizationRules.TryConsumeOrderRefresh(state, time));
            }
            Assert.IsFalse(M21MonetizationRules.CanRefreshOrders(state,
                Now + M21MonetizationRules.MaximumOrderRefreshesPerWindow * M21MonetizationRules.OrderRefreshCooldownSeconds));
            Assert.IsFalse(M21MonetizationRules.CanRefreshOrders(state, Now - 1));
        }

        private static void TestPurchaseStates()
        {
            var store = new TestPurchaseService();
            var states = new List<PurchaseState>();
            string pendingTransaction = null;
            store.PurchaseUpdated += x =>
            {
                states.Add(x.State);
                if (x.State == PurchaseState.Pending) pendingTransaction = x.TransactionId;
            };
            store.Initialize(M21ProductCatalog.Products);
            Assert.AreEqual(ServiceAvailability.Available, store.Availability);
            store.SetNextState(PurchaseState.Pending);
            Assert.IsTrue(store.TryPurchase(M21ProductCatalog.SmallGems));
            Assert.AreEqual(PurchaseState.Pending, states[0]);
            Assert.IsTrue(store.Confirm(pendingTransaction));
            CollectionAssert.Contains(states, PurchaseState.Successful);
            store.SetNextState(PurchaseState.Cancelled);
            store.TryPurchase(M21ProductCatalog.MediumGems);
            store.SetNextState(PurchaseState.Failed);
            store.TryPurchase(M21ProductCatalog.LargeGems);
            CollectionAssert.Contains(states, PurchaseState.Cancelled);
            CollectionAssert.Contains(states, PurchaseState.Failed);
        }

        private static void TestPendingRestart()
        {
            var store = new TestPurchaseService { HoldConfirmations = true };
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            GameObject firstRoot = new GameObject("M21 Purchase First Session");
            MonetizationService first = firstRoot.AddComponent<MonetizationService>();
            first.ConfigureForTests(new UnavailableRewardedAdService(), store,
                new SafeAnalyticsService(), M21MonetizationSaveData.CreateFresh(now));
            Assert.IsTrue(first.TryPurchase(M21ProductCatalog.SmallGems));
            Assert.AreEqual(80, first.Gems.Balance);
            Assert.AreEqual(1, first.State.pendingPurchases.Length);
            Assert.IsTrue(first.State.pendingPurchases[0].granted);
            M21MonetizationSaveData restored = first.CapturePersistentState();
            UnityEngine.Object.DestroyImmediate(firstRoot);

            store.HoldConfirmations = false;
            GameObject secondRoot = new GameObject("M21 Purchase Second Session");
            MonetizationService second = secondRoot.AddComponent<MonetizationService>();
            second.ConfigureForTests(new UnavailableRewardedAdService(), store,
                new SafeAnalyticsService(), restored);
            second.ResumePendingPurchases();
            Assert.AreEqual(80, second.Gems.Balance);
            Assert.AreEqual(1, second.State.gemLedger.Length);
            Assert.AreEqual(0, second.State.pendingPurchases.Length);
            second.ResumePendingPurchases();
            Assert.AreEqual(80, second.Gems.Balance);
            UnityEngine.Object.DestroyImmediate(secondRoot);
        }

        private static void TestRestore()
        {
            const string entitlementKey = "industry_tycoon.entitlement.starter_pack";
            bool hadEntitlement = PlayerPrefs.HasKey(entitlementKey);
            int previousEntitlement = PlayerPrefs.GetInt(entitlementKey, 0);
            GameObject root = null;
            try
            {
                PlayerPrefs.DeleteKey(entitlementKey);
                var store = new TestPurchaseService();
                root = new GameObject("M21 Restore Test");
                Wallet wallet = root.AddComponent<Wallet>();
                MonetizationService service = root.AddComponent<MonetizationService>();
                service.ConfigureForTests(new UnavailableRewardedAdService(), store,
                    new SafeAnalyticsService(),
                    M21MonetizationSaveData.CreateFresh(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                    wallet);
                store.RestoreEntitlements();
                Assert.IsTrue(service.HasStarterPackEntitlement);
                Assert.AreEqual(120, service.Gems.Balance);
                Assert.AreEqual(M21MonetizationRules.StarterPackCash, wallet.Balance);
                Assert.AreEqual(0, service.State.pendingPurchases.Length);

                store.RestoreEntitlements();
                Assert.AreEqual(120, service.Gems.Balance,
                    "Restoring the same permanent entitlement must not grant its bundle twice.");
                Assert.AreEqual(M21MonetizationRules.StarterPackCash, wallet.Balance);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (hadEntitlement) PlayerPrefs.SetInt(entitlementKey, previousEntitlement);
                else PlayerPrefs.DeleteKey(entitlementKey);
                PlayerPrefs.Save();
            }
        }

        private static void TestAnalytics()
        {
            int attempts = 0;
            var analytics = new SafeAnalyticsService((name, values) => { attempts++; throw new InvalidOperationException(); });
            analytics.Track("before_consent");
            Assert.AreEqual(0, attempts);
            analytics.SetConsent(ConsentStatus.Denied);
            analytics.Track("denied");
            Assert.AreEqual(0, attempts);
            analytics.SetConsent(ConsentStatus.Granted);
            Assert.DoesNotThrow(() => analytics.Track("tutorial_completed",
                new Dictionary<string, string> { ["value"] = "complete" }));
            Assert.AreEqual(1, attempts);
        }

        private static void TestCatalog()
        {
            Assert.AreEqual(4, M21ProductCatalog.Products.Count);
            for (int i = 0; i < M21ProductCatalog.Products.Count; i++)
            {
                M21ProductDefinition product = M21ProductCatalog.Products[i];
                Assert.IsEmpty(product.ExternalStoreId);
                Assert.Greater(product.Gems, 0);
            }
            Assert.IsTrue(M21ProductCatalog.TryGet(M21ProductCatalog.StarterPack, out var starter));
            Assert.IsFalse(starter.Consumable);
            Assert.IsTrue(starter.PermanentEntitlement);
        }

        private static void TestV8Migration()
        {
            M9SaveData save = M9SaveData.CreateFresh(Now);
            save.version = M9SaveSchema.Version8;
            string json = JsonUtility.ToJson(save);
            M9SaveDecodeResult first = M9SaveCodec.Decode(json, M9SaveValidationSettings.CreateDefault(), Now);
            Assert.IsTrue(first.IsSuccess, first.Diagnostic);
            Assert.IsTrue(first.WasMigrated);
            Assert.AreEqual(M9SaveSchema.Version9, first.Data.version);
            Assert.AreEqual(0, first.Data.monetization.gemBalance);
            Assert.IsTrue(M9SaveCodec.TryEncode(first.Data, M9SaveValidationSettings.CreateDefault(), Now,
                out string currentJson, out _, out string failure), failure);
            M9SaveDecodeResult second = M9SaveCodec.Decode(currentJson, M9SaveValidationSettings.CreateDefault(), Now);
            Assert.IsTrue(second.IsSuccess, second.Diagnostic);
            Assert.IsFalse(second.WasMigrated);
        }

        private sealed class DeferredRewardedAdService : IRewardedAdService
        {
            private string _requestId;
            private RewardedPlacement _placement;
            private Action<RewardedAdOutcome> _callback;
            public int DisplayCount { get; private set; }
            public ServiceAvailability Availability => ServiceAvailability.Available;
            public bool IsReady(RewardedPlacement placement) => true;
            public bool TryShow(string requestId, RewardedPlacement placement,
                Action<RewardedAdOutcome> completed)
            {
                DisplayCount++;
                _requestId = requestId;
                _placement = placement;
                _callback = completed;
                return true;
            }
            public void Complete(RewardedResult result) => _callback?.Invoke(
                new RewardedAdOutcome(_requestId, _placement, result));
        }
    }
}
