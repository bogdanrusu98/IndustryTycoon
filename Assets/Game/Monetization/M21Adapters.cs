using System;
using System.Collections.Generic;

namespace IndustryTycoon.Monetization
{
    /// <summary>Deterministic Editor/test adapter. It is never selected in a non-development build.</summary>
    public sealed class TestRewardedAdService : IRewardedAdService
    {
        private RewardedResult _next = RewardedResult.Completed;
        private readonly HashSet<string> _delivered = new HashSet<string>(StringComparer.Ordinal);
        private Action<RewardedAdOutcome> _lastCallback;
        private string _lastRequestId;
        private RewardedPlacement _lastPlacement;
        public ServiceAvailability Availability { get; set; } = ServiceAvailability.Available;
        public bool IsReady(RewardedPlacement placement) => Availability == ServiceAvailability.Available;
        public void SetNextResult(RewardedResult result) => _next = result;
        public bool TryShow(string requestId, RewardedPlacement placement, Action<RewardedAdOutcome> completed)
        {
            if (!IsReady(placement) || string.IsNullOrWhiteSpace(requestId)) return false;
            RewardedResult result = _next;
            _next = RewardedResult.Completed;
            _lastCallback = completed;
            _lastRequestId = requestId;
            _lastPlacement = placement;
            if (_delivered.Add(requestId)) completed?.Invoke(new RewardedAdOutcome(requestId, placement, result));
            return true;
        }
        public void Replay(string requestId, RewardedPlacement placement, RewardedResult result,
            Action<RewardedAdOutcome> completed) => completed?.Invoke(new RewardedAdOutcome(requestId, placement, result));
        public void ReplayLast(RewardedResult result) => _lastCallback?.Invoke(
            new RewardedAdOutcome(_lastRequestId, _lastPlacement, result));
    }

    /// <summary>Store simulator with localized test labels; contains no real product/store identifiers.</summary>
    public sealed class TestPurchaseService : IPurchaseService
    {
        private PurchaseState _next = PurchaseState.Pending;
        private readonly Dictionary<string, string> _prices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, PurchaseOutcome> _pending = new Dictionary<string, PurchaseOutcome>(StringComparer.Ordinal);
        private int _sequence;
        public bool HoldConfirmations { get; set; }
        public ServiceAvailability Availability { get; private set; } = ServiceAvailability.Initializing;
        public PurchaseState State { get; private set; } = PurchaseState.Initializing;
        public event Action CatalogChanged;
        public event Action<PurchaseOutcome> PurchaseUpdated;
        public void Initialize(IReadOnlyList<M21ProductDefinition> catalog)
        {
            _prices.Clear();
            if (catalog != null)
                for (int i = 0; i < catalog.Count; i++) _prices[catalog[i].Key] = "TEST PRICE";
            Availability = ServiceAvailability.Available;
            State = PurchaseState.Available;
            CatalogChanged?.Invoke();
        }
        public void SetNextState(PurchaseState state) => _next = state;
        public bool TryGetLocalizedPrice(string productKey, out string localizedPrice) =>
            _prices.TryGetValue(productKey ?? string.Empty, out localizedPrice);
        public bool TryPurchase(string productKey)
        {
            if (Availability != ServiceAvailability.Available || !M21ProductCatalog.TryGet(productKey, out _)) return false;
            string transaction = "sandbox:" + (++_sequence).ToString();
            PurchaseState result = _next;
            _next = PurchaseState.Pending;
            State = result;
            var outcome = new PurchaseOutcome(productKey, transaction, result);
            if (result == PurchaseState.Pending) _pending[transaction] = outcome;
            PurchaseUpdated?.Invoke(outcome);
            return true;
        }
        public bool Confirm(string transactionId)
        {
            if (HoldConfirmations
                || !_pending.TryGetValue(transactionId ?? string.Empty, out PurchaseOutcome pending)) return false;
            _pending.Remove(transactionId);
            State = PurchaseState.Successful;
            PurchaseUpdated?.Invoke(new PurchaseOutcome(pending.ProductKey, transactionId, PurchaseState.Successful));
            return true;
        }
        public void RestoreEntitlements()
        {
            const string transactionId = "sandbox:restore:starter_pack";
            var outcome = new PurchaseOutcome(M21ProductCatalog.StarterPack,
                transactionId, PurchaseState.Pending);
            State = PurchaseState.Pending;
            _pending[transactionId] = outcome;
            PurchaseUpdated?.Invoke(outcome);
        }
    }
}
