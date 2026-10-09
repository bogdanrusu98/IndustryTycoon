using System;
using System.Collections.Generic;
using IndustryTycoon.Market;
using IndustryTycoon.Persistence;

namespace IndustryTycoon.Monetization
{
    public enum ServiceAvailability { Unavailable = 0, Initializing = 1, Available = 2 }
    public enum RewardedPlacement { OfflineDouble = 0, ProductionBoost = 1, OrderRefresh = 2 }
    public enum RewardedResult { Completed = 0, Cancelled = 1, Failed = 2, Unavailable = 3 }
    public enum PurchaseState { Initializing = 0, Available = 1, Pending = 2, Successful = 3, Cancelled = 4, Failed = 5, Unavailable = 6 }
    public enum ConsentStatus { Unknown = 0, Granted = 1, Denied = 2 }
    public enum PremiumTransactionKind { Grant = 0, Spend = 1 }

    public readonly struct RewardedAdOutcome
    {
        public RewardedAdOutcome(string requestId, RewardedPlacement placement,
            RewardedResult result, string diagnostic = "")
        {
            RequestId = requestId ?? string.Empty;
            Placement = placement;
            Result = result;
            Diagnostic = diagnostic ?? string.Empty;
        }
        public string RequestId { get; }
        public RewardedPlacement Placement { get; }
        public RewardedResult Result { get; }
        public string Diagnostic { get; }
    }

    public interface IRewardedAdService
    {
        ServiceAvailability Availability { get; }
        bool IsReady(RewardedPlacement placement);
        bool TryShow(string requestId, RewardedPlacement placement,
            Action<RewardedAdOutcome> completed);
    }

    public readonly struct PurchaseOutcome
    {
        public PurchaseOutcome(string productKey, string transactionId,
            PurchaseState state, string diagnostic = "")
        {
            ProductKey = productKey ?? string.Empty;
            TransactionId = transactionId ?? string.Empty;
            State = state;
            Diagnostic = diagnostic ?? string.Empty;
        }
        public string ProductKey { get; }
        public string TransactionId { get; }
        public PurchaseState State { get; }
        public string Diagnostic { get; }
    }

    public interface IPurchaseService
    {
        ServiceAvailability Availability { get; }
        PurchaseState State { get; }
        event Action CatalogChanged;
        event Action<PurchaseOutcome> PurchaseUpdated;
        void Initialize(IReadOnlyList<M21ProductDefinition> catalog);
        bool TryGetLocalizedPrice(string productKey, out string localizedPrice);
        bool TryPurchase(string productKey);
        bool Confirm(string transactionId);
        void RestoreEntitlements();
    }

    public interface IPremiumCurrencyService
    {
        int Balance { get; }
        event Action<int> BalanceChanged;
        bool TryGrant(string transactionId, int amount, string source);
        bool TrySpend(string transactionId, int amount, string sink);
        bool HasTransaction(string transactionId);
    }

    public interface IAnalyticsService
    {
        ServiceAvailability Availability { get; }
        bool IsCollectionEnabled { get; }
        void SetConsent(ConsentStatus consent);
        void Track(string eventName, IReadOnlyDictionary<string, string> parameters = null);
        void Flush();
    }

    [Serializable]
    public sealed class M21PremiumLedgerRecord
    {
        public string transactionId;
        public PremiumTransactionKind kind;
        public int amount;
        public string reason;
        public long utcUnixSeconds;
    }

    [Serializable]
    public sealed class M21PendingPurchaseRecord
    {
        public string productKey;
        public string transactionId;
        public bool granted;
    }

    [Serializable]
    public sealed class M21MonetizationSaveData
    {
        public int gemBalance;
        public M21PremiumLedgerRecord[] gemLedger;
        public string[] completedRewardTransactions;
        public M21PendingPurchaseRecord[] pendingPurchases;
        public string offlineRewardSessionId;
        public long productionBoostExpiresUtcUnixSeconds;
        public long orderRefreshWindowStartUtcUnixSeconds;
        public long orderRefreshReadyUtcUnixSeconds;
        public int orderRefreshesInWindow;
        public long lastAcceptedUtcUnixSeconds;

        public static M21MonetizationSaveData CreateFresh(long utcNow)
        {
            long safeNow = M9UnixTime.IsPlausible(utcNow) ? utcNow : 0L;
            return new M21MonetizationSaveData
            {
                gemLedger = Array.Empty<M21PremiumLedgerRecord>(),
                completedRewardTransactions = Array.Empty<string>(),
                pendingPurchases = Array.Empty<M21PendingPurchaseRecord>(),
                offlineRewardSessionId = string.Empty,
                orderRefreshWindowStartUtcUnixSeconds = safeNow,
                lastAcceptedUtcUnixSeconds = safeNow
            };
        }
    }

    public static class M21MonetizationRules
    {
        public const int BoostDurationSeconds = 5 * 60;
        public const int BoostGemCost = 20;
        public const int OrderRefreshGemCost = 5;
        public const int OrderRefreshCooldownSeconds = 5 * 60;
        public const int OrderRefreshWindowSeconds = 24 * 60 * 60;
        public const int MaximumOrderRefreshesPerWindow = 3;
        public const int StarterPackCash = 750;
        public const int MaximumGemLedgerEntries = 4096;
        public const int MaximumRewardHistoryEntries = 256;
        public const int MaximumPendingPurchases = 64;
        public const int MaximumTransactionIdLength = 128;
        public const int MaximumReasonLength = 64;

        public static bool IsBoostActive(M21MonetizationSaveData state, long utcNow) =>
            state != null && utcNow >= state.lastAcceptedUtcUnixSeconds
            && utcNow < state.productionBoostExpiresUtcUnixSeconds;

        public static bool TryStartBoost(M21MonetizationSaveData state, long utcNow)
        {
            if (state == null || !AcceptTime(state, utcNow)
                || utcNow < state.productionBoostExpiresUtcUnixSeconds) return false;
            state.productionBoostExpiresUtcUnixSeconds = utcNow + BoostDurationSeconds;
            return true;
        }

        public static bool CanRefreshOrders(M21MonetizationSaveData state, long utcNow)
        {
            if (state == null || !M9UnixTime.IsPlausible(utcNow)
                || utcNow < state.lastAcceptedUtcUnixSeconds
                || utcNow < state.orderRefreshReadyUtcUnixSeconds) return false;
            int count = state.orderRefreshesInWindow;
            if (utcNow - state.orderRefreshWindowStartUtcUnixSeconds >= OrderRefreshWindowSeconds)
                count = 0;
            return count < MaximumOrderRefreshesPerWindow;
        }

        public static bool TryConsumeOrderRefresh(M21MonetizationSaveData state, long utcNow)
        {
            if (!CanRefreshOrders(state, utcNow) || !AcceptTime(state, utcNow)) return false;
            if (utcNow - state.orderRefreshWindowStartUtcUnixSeconds >= OrderRefreshWindowSeconds)
            {
                state.orderRefreshWindowStartUtcUnixSeconds = utcNow;
                state.orderRefreshesInWindow = 0;
            }
            state.orderRefreshesInWindow++;
            state.orderRefreshReadyUtcUnixSeconds = utcNow + OrderRefreshCooldownSeconds;
            return true;
        }

        public static bool AcceptTime(M21MonetizationSaveData state, long utcNow)
        {
            if (state == null || !M9UnixTime.IsPlausible(utcNow)
                || utcNow < state.lastAcceptedUtcUnixSeconds) return false;
            state.lastAcceptedUtcUnixSeconds = utcNow;
            return true;
        }

        public static bool TryRecordRewardCompletion(M21MonetizationSaveData state,
            string transactionId)
        {
            if (state == null || string.IsNullOrWhiteSpace(transactionId)
                || transactionId.Length > MaximumTransactionIdLength) return false;
            string[] existing = state.completedRewardTransactions ?? Array.Empty<string>();
            for (int i = 0; i < existing.Length; i++)
                if (string.Equals(existing[i], transactionId, StringComparison.Ordinal)) return false;
            int retained = Math.Min(existing.Length, MaximumRewardHistoryEntries - 1);
            var copy = new string[retained + 1];
            if (retained > 0)
                Array.Copy(existing, existing.Length - retained, copy, 0, retained);
            copy[copy.Length - 1] = transactionId;
            state.completedRewardTransactions = copy;
            return true;
        }
    }

    public static class M21MonetizationSaveValidator
    {
        public static bool TryNormalize(M21MonetizationSaveData source, long fallbackUtc,
            out M21MonetizationSaveData normalized, out string failure)
        {
            normalized = null;
            failure = null;
            if (source == null) { failure = "M21 monetization state is missing."; return false; }

            var transactionIds = new HashSet<string>(StringComparer.Ordinal);
            var ledger = source.gemLedger ?? Array.Empty<M21PremiumLedgerRecord>();
            long derivedBalance = 0;
            var ledgerCopy = new List<M21PremiumLedgerRecord>(Math.Min(
                ledger.Length, M21MonetizationRules.MaximumGemLedgerEntries));
            for (int i = 0; i < ledger.Length
                            && ledgerCopy.Count < M21MonetizationRules.MaximumGemLedgerEntries; i++)
            {
                M21PremiumLedgerRecord entry = ledger[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.transactionId)
                    || entry.transactionId.Length > M21MonetizationRules.MaximumTransactionIdLength
                    || entry.amount <= 0
                    || !Enum.IsDefined(typeof(PremiumTransactionKind), entry.kind)
                    || !transactionIds.Add(entry.transactionId))
                    continue;
                long nextBalance = derivedBalance
                                   + (entry.kind == PremiumTransactionKind.Grant
                                       ? entry.amount : -entry.amount);
                if (nextBalance < 0 || nextBalance > int.MaxValue)
                    continue;
                derivedBalance = nextBalance;
                string reason = entry.reason ?? string.Empty;
                if (reason.Length > M21MonetizationRules.MaximumReasonLength)
                    reason = reason.Substring(0, M21MonetizationRules.MaximumReasonLength);
                ledgerCopy.Add(new M21PremiumLedgerRecord
                {
                    transactionId = entry.transactionId,
                    kind = entry.kind,
                    amount = entry.amount,
                    reason = reason,
                    utcUnixSeconds = Math.Max(0L, entry.utcUnixSeconds)
                });
            }

            var rewardIds = new HashSet<string>(StringComparer.Ordinal);
            string[] rewards = source.completedRewardTransactions ?? Array.Empty<string>();
            var rewardCopy = new List<string>(Math.Min(
                rewards.Length, M21MonetizationRules.MaximumRewardHistoryEntries));
            int rewardStart = Math.Max(0,
                rewards.Length - M21MonetizationRules.MaximumRewardHistoryEntries);
            for (int i = rewardStart; i < rewards.Length; i++)
            {
                string reward = rewards[i];
                if (string.IsNullOrWhiteSpace(reward)
                    || reward.Length > M21MonetizationRules.MaximumTransactionIdLength
                    || !rewardIds.Add(reward))
                    continue;
                rewardCopy.Add(reward);
            }

            M21PendingPurchaseRecord[] pending = source.pendingPurchases ?? Array.Empty<M21PendingPurchaseRecord>();
            var pendingIds = new HashSet<string>(StringComparer.Ordinal);
            var pendingCopy = new List<M21PendingPurchaseRecord>(Math.Min(
                pending.Length, M21MonetizationRules.MaximumPendingPurchases));
            for (int i = 0; i < pending.Length
                            && pendingCopy.Count < M21MonetizationRules.MaximumPendingPurchases; i++)
            {
                M21PendingPurchaseRecord item = pending[i];
                if (item == null || string.IsNullOrWhiteSpace(item.productKey)
                    || string.IsNullOrWhiteSpace(item.transactionId)
                    || item.transactionId.Length > M21MonetizationRules.MaximumTransactionIdLength
                    || !M21ProductCatalog.TryGet(item.productKey, out _))
                    continue;
                if (!pendingIds.Add(item.transactionId))
                {
                    for (int existing = 0; existing < pendingCopy.Count; existing++)
                        if (string.Equals(pendingCopy[existing].transactionId,
                                item.transactionId, StringComparison.Ordinal))
                            pendingCopy[existing].granted |= item.granted;
                    continue;
                }
                pendingCopy.Add(new M21PendingPurchaseRecord
                    { productKey = item.productKey, transactionId = item.transactionId, granted = item.granted });
            }

            long safeNow = M9UnixTime.IsPlausible(fallbackUtc) ? fallbackUtc : 0L;
            long lastAccepted = M9UnixTime.IsPlausible(source.lastAcceptedUtcUnixSeconds)
                ? source.lastAcceptedUtcUnixSeconds : safeNow;
            long maximumShortTimer = lastAccepted > long.MaxValue - M21MonetizationRules.BoostDurationSeconds
                ? long.MaxValue : lastAccepted + M21MonetizationRules.BoostDurationSeconds;
            long boostExpiry = Math.Max(0L, Math.Min(source.productionBoostExpiresUtcUnixSeconds,
                maximumShortTimer));
            long refreshReady = Math.Max(0L, Math.Min(source.orderRefreshReadyUtcUnixSeconds,
                maximumShortTimer));
            long refreshWindowStart = Math.Max(0L, Math.Min(
                source.orderRefreshWindowStartUtcUnixSeconds, lastAccepted));
            string offlineSession = source.offlineRewardSessionId ?? string.Empty;
            if (offlineSession.Length > M21MonetizationRules.MaximumTransactionIdLength)
                offlineSession = string.Empty;
            normalized = new M21MonetizationSaveData
            {
                gemBalance = (int)derivedBalance,
                gemLedger = ledgerCopy.ToArray(),
                completedRewardTransactions = rewardCopy.ToArray(),
                pendingPurchases = pendingCopy.ToArray(),
                offlineRewardSessionId = offlineSession,
                productionBoostExpiresUtcUnixSeconds = boostExpiry,
                orderRefreshWindowStartUtcUnixSeconds = refreshWindowStart,
                orderRefreshReadyUtcUnixSeconds = refreshReady,
                orderRefreshesInWindow = Math.Max(0, Math.Min(M21MonetizationRules.MaximumOrderRefreshesPerWindow,
                    source.orderRefreshesInWindow)),
                lastAcceptedUtcUnixSeconds = lastAccepted
            };
            return true;
        }
    }

    public sealed class PremiumCurrencyLedger : IPremiumCurrencyService
    {
        private readonly M21MonetizationSaveData _state;
        private readonly Func<long> _clock;
        public PremiumCurrencyLedger(M21MonetizationSaveData state, Func<long> clock)
        { _state = state ?? throw new ArgumentNullException(nameof(state)); _clock = clock ?? throw new ArgumentNullException(nameof(clock)); }
        public int Balance => _state.gemBalance;
        public event Action<int> BalanceChanged;
        public bool HasTransaction(string transactionId)
        {
            if (string.IsNullOrWhiteSpace(transactionId) || _state.gemLedger == null) return false;
            for (int i = 0; i < _state.gemLedger.Length; i++)
                if (string.Equals(_state.gemLedger[i]?.transactionId, transactionId, StringComparison.Ordinal)) return true;
            return false;
        }
        public bool TryGrant(string transactionId, int amount, string source) =>
            TryAppend(transactionId, amount, source, PremiumTransactionKind.Grant);
        public bool TrySpend(string transactionId, int amount, string sink) =>
            TryAppend(transactionId, amount, sink, PremiumTransactionKind.Spend);

        private bool TryAppend(string id, int amount, string reason, PremiumTransactionKind kind)
        {
            if (string.IsNullOrWhiteSpace(id)
                || id.Length > M21MonetizationRules.MaximumTransactionIdLength
                || amount <= 0 || HasTransaction(id)) return false;
            if ((_state.gemLedger?.Length ?? 0)
                >= M21MonetizationRules.MaximumGemLedgerEntries) return false;
            if (kind == PremiumTransactionKind.Spend && _state.gemBalance < amount) return false;
            if (kind == PremiumTransactionKind.Grant && _state.gemBalance > int.MaxValue - amount) return false;
            int newBalance = kind == PremiumTransactionKind.Grant ? _state.gemBalance + amount : _state.gemBalance - amount;
            var list = new List<M21PremiumLedgerRecord>(_state.gemLedger ?? Array.Empty<M21PremiumLedgerRecord>())
            {
                new M21PremiumLedgerRecord { transactionId = id, kind = kind, amount = amount,
                    reason = NormalizeReason(reason), utcUnixSeconds = Math.Max(0L, _clock()) }
            };
            _state.gemLedger = list.ToArray();
            _state.gemBalance = newBalance;
            BalanceChanged?.Invoke(newBalance);
            return true;
        }

        private static string NormalizeReason(string reason)
        {
            reason = reason ?? string.Empty;
            return reason.Length <= M21MonetizationRules.MaximumReasonLength
                ? reason
                : reason.Substring(0, M21MonetizationRules.MaximumReasonLength);
        }
    }

    public sealed class M21ProductDefinition
    {
        public M21ProductDefinition(string key, bool consumable, int gems, int cash = 0,
            bool permanentEntitlement = false)
        { Key = key; Consumable = consumable; Gems = gems; Cash = cash; PermanentEntitlement = permanentEntitlement; }
        public string Key { get; }
        public bool Consumable { get; }
        public int Gems { get; }
        public int Cash { get; }
        public bool PermanentEntitlement { get; }
        // Intentionally no store product ID: configure store mappings externally.
        public string ExternalStoreId => string.Empty;
    }

    public static class M21ProductCatalog
    {
        public const string SmallGems = "small_gems";
        public const string MediumGems = "medium_gems";
        public const string LargeGems = "large_gems";
        public const string StarterPack = "starter_pack";
        public static readonly IReadOnlyList<M21ProductDefinition> Products = new[]
        {
            new M21ProductDefinition(SmallGems, true, 80),
            new M21ProductDefinition(MediumGems, true, 250),
            new M21ProductDefinition(LargeGems, true, 650),
            new M21ProductDefinition(StarterPack, false, 120, M21MonetizationRules.StarterPackCash, true)
        };
        public static bool TryGet(string key, out M21ProductDefinition product)
        {
            for (int i = 0; i < Products.Count; i++)
                if (string.Equals(Products[i].Key, key, StringComparison.Ordinal)) { product = Products[i]; return true; }
            product = null; return false;
        }
    }

    public static class ProductionBoostRuntime
    {
        private static Func<bool> _isActive = () => false;
        public static bool IsActive => _isActive();
        public static float ScaleDuration(float duration) => Math.Max(0.02f, duration / (IsActive ? 2f : 1f));
        public static float ScaleDelta(float delta) => delta * (IsActive ? 2f : 1f);
        public static void Bind(Func<bool> isActive) => _isActive = isActive ?? (() => false);
        public static void Reset() => _isActive = () => false;
    }

    /// <summary>
    /// Reuses the two yield instructions required by normal and boosted production.
    /// Runtime production loops call this at cycle boundaries, so boost changes still
    /// take effect on the next cycle without allocating a WaitForSeconds every cycle.
    /// </summary>
    public sealed class ProductionWaitCache
    {
        private float _duration = -1f;
        private UnityEngine.WaitForSeconds _normal;
        private UnityEngine.WaitForSeconds _boosted;

        public UnityEngine.WaitForSeconds Get(float duration)
        {
            duration = Math.Max(0.02f, duration);
            if (_normal == null || Math.Abs(_duration - duration) > 0.0001f)
            {
                _duration = duration;
                _normal = new UnityEngine.WaitForSeconds(duration);
                _boosted = new UnityEngine.WaitForSeconds(duration * 0.5f);
            }

            return ProductionBoostRuntime.IsActive ? _boosted : _normal;
        }
    }

    public sealed class UnavailableRewardedAdService : IRewardedAdService
    {
        public ServiceAvailability Availability => ServiceAvailability.Unavailable;
        public bool IsReady(RewardedPlacement placement) => false;
        public bool TryShow(string requestId, RewardedPlacement placement, Action<RewardedAdOutcome> completed)
        { completed?.Invoke(new RewardedAdOutcome(requestId, placement, RewardedResult.Unavailable)); return false; }
    }

    public sealed class UnavailablePurchaseService : IPurchaseService
    {
        public ServiceAvailability Availability => ServiceAvailability.Unavailable;
        public PurchaseState State => PurchaseState.Unavailable;
        public event Action CatalogChanged { add { } remove { } }
        public event Action<PurchaseOutcome> PurchaseUpdated { add { } remove { } }
        public void Initialize(IReadOnlyList<M21ProductDefinition> catalog) { }
        public bool TryGetLocalizedPrice(string productKey, out string localizedPrice) { localizedPrice = string.Empty; return false; }
        public bool TryPurchase(string productKey) => false;
        public bool Confirm(string transactionId) => false;
        public void RestoreEntitlements() { }
    }

    public sealed class SafeAnalyticsService : IAnalyticsService
    {
        public const int MaximumEventNameLength = 48;
        public const int MaximumParameters = 8;
        public const int MaximumParameterKeyLength = 32;
        public const int MaximumParameterValueLength = 64;

        private readonly Action<string, IReadOnlyDictionary<string, string>> _transport;
        private ConsentStatus _consent;
        public SafeAnalyticsService(Action<string, IReadOnlyDictionary<string, string>> transport = null)
        { _transport = transport; }
        public ServiceAvailability Availability => _transport == null ? ServiceAvailability.Unavailable : ServiceAvailability.Available;
        public bool IsCollectionEnabled => _consent == ConsentStatus.Granted && _transport != null;
        public void SetConsent(ConsentStatus consent) => _consent = consent;
        public void Track(string eventName, IReadOnlyDictionary<string, string> parameters = null)
        {
            if (!IsCollectionEnabled || string.IsNullOrWhiteSpace(eventName)
                || eventName.Length > MaximumEventNameLength) return;
            IReadOnlyDictionary<string, string> safeParameters = Sanitize(parameters);
            try { _transport(eventName, safeParameters); }
            catch { /* Analytics never affects gameplay. */ }
        }
        public void Flush() { }

        private static IReadOnlyDictionary<string, string> Sanitize(
            IReadOnlyDictionary<string, string> parameters)
        {
            if (parameters == null || parameters.Count == 0) return null;
            var safe = new Dictionary<string, string>(
                Math.Min(parameters.Count, MaximumParameters), StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                if (safe.Count >= MaximumParameters) break;
                if (string.IsNullOrWhiteSpace(pair.Key)
                    || pair.Key.Length > MaximumParameterKeyLength) continue;
                string value = pair.Value ?? string.Empty;
                if (value.Length > MaximumParameterValueLength)
                    value = value.Substring(0, MaximumParameterValueLength);
                safe[pair.Key] = value;
            }
            return safe.Count == 0 ? null : safe;
        }
    }
}
