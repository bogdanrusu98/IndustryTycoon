using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IndustryTycoon.Monetization;
using IndustryTycoon.Market;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using IndustryTycoon.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM22Tests
    {
        private const long Now = 2000000000L;

        [Test] public void Economy_FreshToEmpire25ProfilesAreViableAndBounded() => RunEconomyAudit();
        [Test] public void Monetization_AcceleratesWithoutTrivializingEmpire25() => RunMonetizationAudit();
        [Test] public void SaveValidator_RepairsRecoverableFieldsWithoutReset() => TestRecoverableSaveRepair();
        [Test] public void SaveStore_RecoversAtomicBackupAfterPrimaryCorruption() => TestAtomicRecovery();
        [Test] public void Offline_MinutesDaysExtremeRollbackAndJumpAreBounded() => TestOfflineStress();
        [Test] public void Monetization_DuplicateHistoriesAreSanitizedAndBounded() => TestMonetizationSanitization();
        [Test] public void Analytics_ConsentPayloadAndProviderFailureStaySafe() => TestAnalyticsHardening();
        [Test] public void ProductionWaits_AreReusedAcrossNormalGameplayCycles() => TestProductionWaitCache();
        [Test] public void UiLifecycle_ReenableDoesNotDuplicateButtonListeners() => TestUiListenerLifecycle();
        [Test] public void Soak_TenThousandRotationsAndSettlementsStayBounded() => TestAcceleratedSoak();

        public static void RunAllForValidator()
        {
            RunEconomyAudit();
            RunMonetizationAudit();
            TestRecoverableSaveRepair();
            TestAtomicRecovery();
            TestOfflineStress();
            TestMonetizationSanitization();
            TestAnalyticsHardening();
            TestProductionWaitCache();
            TestUiListenerLifecycle();
            TestAcceleratedSoak();
        }

        private static void RunEconomyAudit()
        {
            SimResult manual = Simulate(Profile.Manual);
            SimResult automation = Simulate(Profile.AutomationFocused);
            SimResult offline = Simulate(Profile.NormalOffline);
            SimResult rewarded = Simulate(Profile.RewardedNormal);
            SimResult free = Simulate(Profile.ZeroAds);

            foreach (SimResult result in new[] { manual, automation, offline, rewarded, free })
            {
                Require(result.EmpireLevel == 25, result.Name + " deadlocked before Empire 25.");
                Require(result.ActiveSeconds <= 8 * 60 * 60,
                    result.Name + " exceeded eight active hours.");
                Require(result.WallSeconds <= 36 * 60 * 60,
                    result.Name + " exceeded the bounded wall-clock audit window.");
                Require(result.MinimumUnlockGapSeconds >= 25,
                    result.Name + " produced an instant unlock chain.");
                Require(result.MaximumUnlockGapSeconds <= 3 * 60 * 60,
                    result.Name + " produced a grind spike over three hours.");
                Require(result.PeakCash < 1000000d,
                    result.Name + " produced runaway Cash.");
                Require(result.UsedLegacyPhaseCount == 9,
                    result.Name + " bypassed a legacy production chain.");
                Debug.Log(result.ToLogLine("M22_ECONOMY"));
            }

            Require(Math.Abs(manual.WallSeconds - free.WallSeconds) < 0.01d,
                "Zero-ads/free pacing diverged from the active free baseline.");
            Require(rewarded.WallSeconds >= manual.WallSeconds * 0.55d,
                "Normal rewarded usage trivialized progression by more than 45%. ");
        }

        private static void RunMonetizationAudit()
        {
            SimResult free = Simulate(Profile.ZeroAds);
            SimResult gameplayGems = Simulate(Profile.GameplayGems);
            SimResult starter = Simulate(Profile.StarterPack);
            SimResult small = Simulate(Profile.SmallGems);
            SimResult medium = Simulate(Profile.MediumGems);
            SimResult large = Simulate(Profile.LargeGems);
            SimResult maximumAds = Simulate(Profile.RewardedMaximum);
            SimResult combination = Simulate(Profile.GemsAndAds);

            foreach (SimResult result in new[]
                     { gameplayGems, starter, small, medium, large, maximumAds, combination })
            {
                Require(result.EmpireLevel == 25, result.Name + " failed to reach Empire 25.");
                Require(result.WallSeconds <= free.WallSeconds,
                    result.Name + " monetization was slower than free progression.");
                Require(result.WallSeconds >= free.WallSeconds * 0.42d,
                    result.Name + " trivialized Empire 25 below 42% of free time.");
                Debug.Log(result.ToLogLine("M22_MONETIZATION"));
            }

            Require(starter.UnlockSeconds[2] < free.UnlockSeconds[2],
                "Starter Pack did not accelerate the early game.");
            Require(small.WallSeconds >= medium.WallSeconds
                    && medium.WallSeconds >= large.WallSeconds,
                "Gem pack acceleration was not monotonic.");
            Require(gameplayGems.WallSeconds < free.WallSeconds,
                "Gameplay-only Gems had no useful acceleration path.");
            Require(maximumAds.WallSeconds >= free.WallSeconds * 0.48d,
                "Maximum legitimate rewarded use exceeded the intended ~2x ceiling.");
        }

        private static void TestRecoverableSaveRepair()
        {
            M9SaveData save = M9SaveData.CreateFresh(Now);
            save.walletCash = 1234;
            save.stockpileWood = -50;
            save.processorInputWood = int.MaxValue;
            save.pendingOfflineCash = -1;
            save.pendingOfflineAwaySeconds = long.MaxValue;
            M14BuildingUpgradeSaveRecord upgrade = save.industrial.GetBuildingUpgrade("wood_processor");
            upgrade.speedLevel = -20;
            upgrade.inputCapacityLevel = 999;
            upgrade.outputCapacityLevel = int.MaxValue;

            save.monetization.gemLedger = new[]
            {
                new M21PremiumLedgerRecord { transactionId = "grant", kind = PremiumTransactionKind.Grant, amount = 10 },
                new M21PremiumLedgerRecord { transactionId = "grant", kind = PremiumTransactionKind.Grant, amount = 10 },
                null
            };
            save.monetization.gemBalance = -100;
            save.monetization.completedRewardTransactions = new[] { "reward", "reward", "", null };
            save.monetization.pendingPurchases = new[]
            {
                new M21PendingPurchaseRecord { productKey = M21ProductCatalog.SmallGems, transactionId = "pending", granted = false },
                new M21PendingPurchaseRecord { productKey = M21ProductCatalog.SmallGems, transactionId = "pending", granted = true },
                new M21PendingPurchaseRecord { productKey = "invalid", transactionId = "bad" }
            };

            M9SaveValidationSettings settings = M9SaveValidationSettings.CreateDefault();
            bool valid = M9SaveValidator.TryNormalize(save,
                settings, Now,
                out M9SaveData repaired, out string failure);
            Require(valid, failure);
            Require(repaired.walletCash == 1234, "Valid fields were wiped during repair.");
            Require(repaired.stockpileWood == 0 && repaired.processorInputWood == 24,
                "Negative/overflow inventory was not clamped to valid capacity.");
            Require(repaired.pendingOfflineCash == 0
                    && repaired.pendingOfflineAwaySeconds == settings.maximumPendingAwaySeconds,
                "Offline pending values were not clamped.");
            M14BuildingUpgradeSaveRecord repairedUpgrade =
                repaired.industrial.GetBuildingUpgrade("wood_processor");
            Require(repairedUpgrade.speedLevel == 0
                    && repairedUpgrade.inputCapacityLevel == 3
                    && repairedUpgrade.outputCapacityLevel == 3,
                "Invalid upgrade levels were not clamped.");
            Require(repaired.monetization.gemBalance == 10
                    && repaired.monetization.gemLedger.Length == 1
                    && repaired.monetization.completedRewardTransactions.Length == 1
                    && repaired.monetization.pendingPurchases.Length == 1
                    && repaired.monetization.pendingPurchases[0].granted,
                "Duplicate monetization payout/event state was not safely normalized.");
        }

        private static void TestAtomicRecovery()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "IndustryTycoon-M22-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new M9LocalSaveStore(directory, new FixedClock(Now),
                    M9SaveValidationSettings.CreateDefault(), preserveInvalidFiles: false);
                M9SaveData first = M9SaveData.CreateFresh(Now);
                first.walletCash = 111;
                Require(store.Save(first).IsSuccess, "Initial atomic save failed.");
                M9SaveData second = M9SaveData.CreateFresh(Now);
                second.walletCash = 222;
                Require(store.Save(second).IsSuccess && File.Exists(store.BackupPath),
                    "Atomic replacement did not retain a last-valid backup.");
                File.WriteAllText(store.PrimaryPath, "{corrupt");
                M9SaveLoadResult recovered = store.Load();
                Require(recovered.Status == M9SaveLoadStatus.RecoveredBackup
                        && recovered.Data.walletCash == 111,
                    "Corrupt primary did not recover the last-valid save.");
                Require(store.Load().Status == M9SaveLoadStatus.LoadedPrimary,
                    "Backup recovery did not repair the primary atomically.");
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void TestOfflineStress()
        {
            long[] absences = { 60, 10 * 60, 3 * 60 * 60, 24 * 60 * 60,
                7 * 24 * 60 * 60, 3650L * 24L * 60L * 60L };
            foreach (long absence in absences)
            {
                long credited = OfflineProgressionCalculator.CalculateCreditedAwaySeconds(
                    Now, Now + absence);
                Require(credited == Math.Min(absence, 14400L),
                    "Offline cap failed for " + absence + " seconds.");
            }
            Require(OfflineProgressionCalculator.CalculateCreditedAwaySeconds(Now, Now - 1) == 0,
                "Clock rollback awarded offline progress.");
            Require(OfflineProgressionCalculator.CalculateCreditedAwaySeconds(0, long.MaxValue) == 0,
                "Invalid/extreme timestamps awarded offline progress.");

            var input = new OfflineProgressionInput
            {
                LastEvaluationUtcUnixSeconds = Now,
                NowUtcUnixSeconds = Now + 30L * 24L * 60L * 60L,
                WorkerUnlocked = true,
                ProcessorUnlocked = true,
                AutoFeederUnlocked = true,
                PackingUnlocked = true,
                CourierUnlocked = true,
                StockpileWood = int.MaxValue,
                ProcessorInputWood = int.MaxValue,
                ProcessorOutputPlanks = int.MaxValue,
                PackingInputPlanks = int.MaxValue,
                PackingOutputCrates = int.MaxValue,
                PendingOfflineCash = int.MaxValue - 40
            };
            OfflineProgressionResult result = OfflineProgressionCalculator.Calculate(input);
            Require(result.CreditedAwaySeconds == 14400
                    && result.StockpileWood <= input.StockpileCapacity
                    && result.ProcessorInputWood <= input.ProcessorInputCapacity
                    && result.ProcessorOutputPlanks <= input.ProcessorOutputCapacity
                    && result.PackingInputPlanks <= input.PackingInputCapacity
                    && result.PackingOutputCrates <= input.PackingOutputCapacity
                    && result.PendingOfflineCash <= int.MaxValue,
                "Offline numeric stress escaped a capacity or integer bound.");

            input.ReturnScreenPending = true;
            input.PendingOfflineCash = 500;
            input.PendingOfflineAwaySeconds = 600;
            OfflineProgressionResult pending = OfflineProgressionCalculator.Calculate(input);
            Require(pending.SkippedBecauseReturnPending
                    && pending.PendingOfflineCash == 500
                    && pending.PendingOfflineAwaySeconds == 600,
                "Outstanding offline settlement was evaluated twice.");
        }

        private static void TestMonetizationSanitization()
        {
            var state = M21MonetizationSaveData.CreateFresh(Now);
            state.productionBoostExpiresUtcUnixSeconds = long.MaxValue;
            state.orderRefreshReadyUtcUnixSeconds = long.MaxValue;
            state.orderRefreshWindowStartUtcUnixSeconds = long.MaxValue;
            state.completedRewardTransactions = Enumerable.Range(0, 400)
                .Select(i => "reward:" + i).Concat(new[] { "reward:399" }).ToArray();
            Require(M21MonetizationSaveValidator.TryNormalize(state, Now,
                out M21MonetizationSaveData normalized, out string failure), failure);
            Require(normalized.completedRewardTransactions.Length > 0
                    && normalized.completedRewardTransactions.Length
                    <= M21MonetizationRules.MaximumRewardHistoryEntries
                    && normalized.productionBoostExpiresUtcUnixSeconds
                    == Now + M21MonetizationRules.BoostDurationSeconds
                    && normalized.orderRefreshReadyUtcUnixSeconds
                    == Now + M21MonetizationRules.BoostDurationSeconds
                    && normalized.orderRefreshWindowStartUtcUnixSeconds == Now,
                "Monetization histories or timers were not bounded.");
        }

        private static void TestAnalyticsHardening()
        {
            int calls = 0;
            IReadOnlyDictionary<string, string> captured = null;
            var analytics = new SafeAnalyticsService((_, values) =>
            {
                calls++;
                captured = values;
            });
            analytics.Track("before_consent");
            analytics.SetConsent(ConsentStatus.Denied);
            analytics.Track("denied");
            Require(calls == 0, "Analytics emitted without granted consent.");
            analytics.SetConsent(ConsentStatus.Granted);
            var payload = new Dictionary<string, string>();
            for (int i = 0; i < 20; i++) payload["key" + i] = new string('x', 200);
            analytics.Track("bounded_event", payload);
            Require(calls == 1 && captured != null
                    && captured.Count == SafeAnalyticsService.MaximumParameters
                    && captured.All(x => x.Key.Length <= SafeAnalyticsService.MaximumParameterKeyLength
                                         && x.Value.Length <= SafeAnalyticsService.MaximumParameterValueLength),
                "Analytics payload bounds were not enforced.");
            analytics.Track(new string('e', SafeAnalyticsService.MaximumEventNameLength + 1));
            Require(calls == 1, "Oversized analytics event was transported.");

            var failing = new SafeAnalyticsService((_, __) => throw new InvalidOperationException());
            failing.SetConsent(ConsentStatus.Granted);
            RequireNoThrow(() => failing.Track("provider_failure"),
                "Analytics provider failure escaped into gameplay.");
        }

        private static void TestProductionWaitCache()
        {
            var cache = new ProductionWaitCache();
            try
            {
                ProductionBoostRuntime.Bind(() => false);
                object normalA = cache.Get(2f);
                object normalB = cache.Get(2f);
                Require(ReferenceEquals(normalA, normalB),
                    "Normal production wait allocated once per cycle.");
                ProductionBoostRuntime.Bind(() => true);
                object boostedA = cache.Get(2f);
                object boostedB = cache.Get(2f);
                Require(ReferenceEquals(boostedA, boostedB) && !ReferenceEquals(normalA, boostedA),
                    "Boosted production wait was not independently cached.");
                ProductionBoostRuntime.Bind(() => false);
                Require(ReferenceEquals(normalA, cache.Get(2f)),
                    "Returning to normal production allocated another wait.");
            }
            finally { ProductionBoostRuntime.Reset(); }
        }

        private static void TestUiListenerLifecycle()
        {
            GameObject root = new GameObject("M22 UI lifecycle");
            root.SetActive(false);
            try
            {
                GameObject main = new GameObject("Main");
                GameObject content = new GameObject("Content");
                main.transform.SetParent(root.transform);
                content.transform.SetParent(root.transform);
                Button button = new GameObject("Content Button").AddComponent<Button>();
                button.transform.SetParent(root.transform);
                MobilePanelNavigation navigation = root.AddComponent<MobilePanelNavigation>();
                SetField(navigation, "mainMenu", main);
                SetField(navigation, "contentPanels", new[] { content });
                SetField(navigation, "contentButtons", new[] { button });

                root.SetActive(true);
                button.onClick.Invoke();
                Require(content.activeSelf, "First UI listener did not open its panel.");
                root.SetActive(false);
                root.SetActive(true);
                button.onClick.Invoke();
                Require(content.activeSelf,
                    "Re-enabled UI invoked a duplicated listener and toggled twice.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void TestAcceleratedSoak()
        {
            var timer = Stopwatch.StartNew();
            var market = M16MarketSaveData.CreateFresh(Now);
            M16MarketRules.RefillOrders(market, MarketUnlockContext.All);
            var monetization = M21MonetizationSaveData.CreateFresh(Now);
            long now = Now;
            for (int i = 0; i < 10000; i++)
            {
                now += M16MarketRules.DefaultRotationSeconds;
                Require(M16MarketRules.Advance(market, now,
                    M16MarketRules.DefaultRotationSeconds, MarketUnlockContext.All),
                    "Market soak rotation stalled at " + i);
                Require(market.orders.Length == M16MarketSaveData.VisibleOrderCount,
                    "Market order collection grew during soak.");
                M21MonetizationRules.TryRecordRewardCompletion(
                    monetization, "soak:" + i);

                var input = new OfflineProgressionInput
                {
                    LastEvaluationUtcUnixSeconds = now - 14400,
                    NowUtcUnixSeconds = now,
                    WorkerUnlocked = true,
                    ProcessorUnlocked = true,
                    AutoFeederUnlocked = true,
                    PackingUnlocked = true,
                    CourierUnlocked = true,
                    StockpileWood = 30,
                    ProcessorInputWood = 24,
                    PackingInputPlanks = 24
                };
                OfflineProgressionResult offline = OfflineProgressionCalculator.Calculate(input);
                Require(offline.CreditedAwaySeconds == 14400
                        && offline.PendingOfflineCash >= 0,
                    "Offline soak settlement escaped bounds.");
            }
            timer.Stop();
            Require(market.rotationIndex == 10000
                    && monetization.completedRewardTransactions.Length
                    == M21MonetizationRules.MaximumRewardHistoryEntries,
                "Accelerated soak produced timer drift or unbounded history.");
            Debug.Log($"M22_SOAK 10000 market/offline/reward cycles PASS in "
                      + $"{timer.Elapsed.TotalMilliseconds:F1}ms; reward history="
                      + monetization.completedRewardTransactions.Length);
        }

        private enum Profile
        {
            Manual, AutomationFocused, NormalOffline, RewardedNormal, ZeroAds,
            GameplayGems, StarterPack, SmallGems, MediumGems, LargeGems,
            RewardedMaximum, GemsAndAds
        }

        private readonly struct Phase
        {
            public Phase(int level, int cashCost, double workSeconds)
            { Level = level; CashCost = cashCost; WorkSeconds = workSeconds; }
            public int Level { get; }
            public int CashCost { get; }
            public double WorkSeconds { get; }
        }

        private sealed class SimResult
        {
            public string Name;
            public int EmpireLevel;
            public double WallSeconds;
            public double ActiveSeconds;
            public double PeakCash;
            public double MinimumUnlockGapSeconds = double.MaxValue;
            public double MaximumUnlockGapSeconds;
            public int UsedLegacyPhaseCount;
            public readonly Dictionary<int, double> UnlockSeconds = new Dictionary<int, double>();

            public string ToLogLine(string prefix) =>
                $"{prefix} {Name}: Empire {EmpireLevel}, wall={WallSeconds / 60d:F1}m, "
                + $"active={ActiveSeconds / 60d:F1}m, unlock-gap={MinimumUnlockGapSeconds:F0}.."
                + $"{MaximumUnlockGapSeconds:F0}s, peak-cash=${PeakCash:F0}";
        }

        private static readonly Phase[] Phases =
        {
            new Phase(2, 3720, 600), new Phase(3, 3600, 300),
            new Phase(4, 3200, 300), new Phase(5, 0, 120),
            new Phase(6, 0, 30), new Phase(7, 0, 60),
            new Phase(8, 0, 900), new Phase(9, 0, 600),
            new Phase(10, 0, 300), new Phase(11, 0, 30),
            new Phase(12, 7500, 120), new Phase(13, 0, 180),
            new Phase(14, 1400, 30), new Phase(15, 0, 600),
            new Phase(16, 3000, 60), new Phase(17, 4500, 60),
            new Phase(18, 0, 900), new Phase(19, 1600, 600),
            new Phase(20, 8700, 1800), new Phase(21, 0, 30),
            new Phase(22, 0, 300), new Phase(23, 0, 180),
            new Phase(24, 0, 180), new Phase(25, 0, 600)
        };

        private static SimResult Simulate(Profile profile)
        {
            const double tick = 5d;
            double cash = profile == Profile.StarterPack || profile == Profile.GemsAndAds
                ? M21MonetizationRules.StarterPackCash : 0d;
            int gems = profile == Profile.SmallGems ? 80
                : profile == Profile.MediumGems ? 250
                : profile == Profile.LargeGems || profile == Profile.GemsAndAds ? 650 : 0;
            double boostRemaining = 0d;
            double wall = 0d;
            double active = 0d;
            double lastUnlock = 0d;
            int level = 1;
            int phaseIndex = 0;
            double work = 0d;
            var result = new SimResult { Name = profile.ToString(), EmpireLevel = 1 };

            while (phaseIndex < Phases.Length && wall < 48d * 60d * 60d)
            {
                Phase phase = Phases[phaseIndex];
                bool online = profile != Profile.NormalOffline || wall % 8100d < 900d;
                if (online) active += tick;

                if (level == 4 && !result.UnlockSeconds.ContainsKey(-4))
                { gems += 5; result.UnlockSeconds[-4] = wall; }
                if (level == 10 && !result.UnlockSeconds.ContainsKey(-10))
                { gems += 10; result.UnlockSeconds[-10] = wall; }
                if (level == 18 && !result.UnlockSeconds.ContainsKey(-18))
                { gems += 20; result.UnlockSeconds[-18] = wall; }

                bool rewardedMaximum = profile == Profile.RewardedMaximum
                                       || profile == Profile.GemsAndAds;
                bool rewardedNormal = profile == Profile.RewardedNormal
                                      && ((int)(wall / 900d) % 2 == 0)
                                      && wall % 900d < M21MonetizationRules.BoostDurationSeconds;
                bool maySpendGems = profile == Profile.GameplayGems
                                    || profile == Profile.SmallGems
                                    || profile == Profile.MediumGems
                                    || profile == Profile.LargeGems;
                if (online && maySpendGems && boostRemaining <= 0d
                    && gems >= M21MonetizationRules.BoostGemCost)
                {
                    gems -= M21MonetizationRules.BoostGemCost;
                    boostRemaining = M21MonetizationRules.BoostDurationSeconds;
                }
                bool boosted = online && (rewardedMaximum || rewardedNormal || boostRemaining > 0d);
                if (boostRemaining > 0d) boostRemaining = Math.Max(0d, boostRemaining - tick);

                double incomeRate = 4d + Math.Min(42d, level * 1.7d);
                double workRate = 1d;
                if (profile == Profile.AutomationFocused)
                {
                    incomeRate *= level < 7 ? 0.78d : 1.16d;
                    workRate *= level < 7 ? 0.86d : 1.12d;
                }
                if (!online)
                {
                    incomeRate *= level >= 2 ? 0.15d : 0d;
                    workRate *= level >= 2 ? 0.10d : 0d;
                }
                if (boosted)
                {
                    incomeRate *= 2d;
                    workRate *= 2d;
                }
                if (!online && (profile == Profile.RewardedNormal || rewardedMaximum))
                    incomeRate *= 2d;

                cash += incomeRate * tick;
                work += workRate * tick;
                result.PeakCash = Math.Max(result.PeakCash, cash);
                wall += tick;

                if (cash + 0.0001d < phase.CashCost || work + 0.0001d < phase.WorkSeconds)
                    continue;

                cash -= phase.CashCost;
                work = 0d;
                level = phase.Level;
                double gap = wall - lastUnlock;
                lastUnlock = wall;
                result.MinimumUnlockGapSeconds = Math.Min(result.MinimumUnlockGapSeconds, gap);
                result.MaximumUnlockGapSeconds = Math.Max(result.MaximumUnlockGapSeconds, gap);
                result.UnlockSeconds[level] = wall;
                if (level <= 10) result.UsedLegacyPhaseCount++;
                phaseIndex++;
            }

            result.EmpireLevel = level;
            result.WallSeconds = wall;
            result.ActiveSeconds = active;
            return result;
        }

        private sealed class FixedClock : IUtcClock
        {
            public FixedClock(long value) { UtcNowUnixSeconds = value; }
            public long UtcNowUnixSeconds { get; }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing field " + name);
            field.SetValue(target, value);
        }

        private static void RequireNoThrow(Action action, string message)
        {
            try { action(); }
            catch (Exception exception) { throw new InvalidOperationException(message, exception); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
