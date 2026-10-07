using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Mining;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.Market
{
    [DefaultExecutionOrder(-825)]
    public sealed class MarketPlanningService : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private IndustrialDistrictService industrialDistrictService;
        [SerializeField] private CentralWarehouse warehouse;
        [SerializeField] private Wallet wallet;
        [SerializeField] private SalePoint salePoint;
        [SerializeField] private CrateCourier courier;
        [SerializeField] private Smelter smelter;
        [SerializeField] private ToolWorkshop toolWorkshop;
        [SerializeField, Min(30)] private int rotationSeconds = M16MarketRules.DefaultRotationSeconds;
        [SerializeField, Min(10)] private int flowWindowSeconds = M16MarketRules.DefaultFlowWindowSeconds;
        [SerializeField, Min(10)] private int flowBonusSeconds = M16MarketRules.DefaultFlowBonusSeconds;

        private M16MarketSaveData _state;
        private bool _subscribed;
        private double _nextEvaluation;

        public event Action StateChanged;
        public M16MarketSaveData State => _state;
        public int RotationSeconds => rotationSeconds;
        public bool IsMarketUnlocked => progressionService != null
                                        && progressionService.GetMetric(ProgressMetricId.MarketUnlocked) > 0;
        public bool IsFlowBonusActive
        {
            get
            {
                long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
                return _state != null && now >= _state.lastAcceptedUtcUnixSeconds
                       && now < _state.flowBonusExpiresUtc;
            }
        }

        private void Awake() => _state = M16MarketSaveData.CreateFresh(SystemUtcClock.Instance.UtcNowUnixSeconds);
        private void Start()
        {
            Subscribe();
            EnsureMarketUnlocked();
            if (IsMarketUnlocked)
            {
                M16MarketRules.RotateDemand(_state, BuildUnlockContext());
                M16MarketRules.RefillOrders(_state, BuildUnlockContext());
                StateChanged?.Invoke();
            }
        }

        private void OnEnable() { if (_state != null) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void Update()
        {
            if (Time.realtimeSinceStartupAsDouble < _nextEvaluation) return;
            _nextEvaluation = Time.realtimeSinceStartupAsDouble + 1d;
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            if (now >= _state.lastAcceptedUtcUnixSeconds) _state.lastAcceptedUtcUnixSeconds = now;
            EnsureMarketUnlocked();
            if (IsMarketUnlocked && M16MarketRules.Advance(_state,
                    now, rotationSeconds, BuildUnlockContext()))
                StateChanged?.Invoke();
        }

        public DemandTier GetDemandTier(ResourceType type) => IsMarketUnlocked
            ? M16MarketRules.GetDemand(_state, type) : DemandTier.Normal;

        public int GetNormalSaleValue(ResourceType type) => M16MarketRules.CalculateSaleValue(
            type, GetDemandTier(type), IsFlowBonusActive);

        public int GetAvailableStock(ResourceType type) => warehouse != null ? warehouse.GetAvailable(type) : 0;

        public bool TryActivateOrder(int index)
        {
            if (!IsMarketUnlocked || !M16MarketRules.TryActivateOrder(_state, index)) return false;
            StateChanged?.Invoke();
            return true;
        }

        public bool TryDeliverActiveOrder()
        {
            var order = FindActiveOrder();
            if (order == null || warehouse == null
                || !warehouse.TryReserveOutgoing(order.resourceType, order.quantity, out var reservation)) return false;
            try
            {
                warehouse.FinalizeReservationForTransfer(reservation);
                warehouse.PublishTransferCommitted(reservation);
                M16MarketRules.TryTransitionOrder(order, ProductionOrderState.Active, ProductionOrderState.Completed);
                progressionService?.RecordProductionOrderCompleted(order.resourceType == ResourceType.MachinePart);
                StateChanged?.Invoke();
                return true;
            }
            catch
            {
                warehouse.ReleaseReservation(reservation);
                throw;
            }
        }

        public bool TryClaimOrder(int index)
        {
            var order = GetOrder(index);
            if (order == null || order.state != ProductionOrderState.Completed || wallet == null
                || wallet.Balance > int.MaxValue - order.rewardCash || wallet.Deposit(order.rewardCash) != order.rewardCash) return false;
            M16MarketRules.TryTransitionOrder(order, ProductionOrderState.Completed, ProductionOrderState.Claimed);
            M16MarketRules.RefillOrders(_state, BuildUnlockContext());
            StateChanged?.Invoke();
            return true;
        }

        public M16MarketSaveData CapturePersistentState()
        {
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            if (_state != null && now >= _state.lastAcceptedUtcUnixSeconds) _state.lastAcceptedUtcUnixSeconds = now;
            M16MarketSaveValidator.TryNormalize(_state, SystemUtcClock.Instance.UtcNowUnixSeconds, out var copy, out _);
            return copy ?? M16MarketSaveData.CreateFresh(SystemUtcClock.Instance.UtcNowUnixSeconds);
        }

        public void RestorePersistentState(M16MarketSaveData state)
        {
            if (M16MarketSaveValidator.TryNormalize(state, SystemUtcClock.Instance.UtcNowUnixSeconds, out var normalized, out _))
                _state = normalized;
            else
                _state = M16MarketSaveData.CreateFresh(SystemUtcClock.Instance.UtcNowUnixSeconds);
            // Deliberately silent: load cannot replay demand, order, Flow or Empire events.
        }

        public MarketUnlockContext BuildUnlockContext()
        {
            int mask = 1 << (int)ResourceType.Wood;
            if (progressionService == null) return new MarketUnlockContext(mask);
            if (progressionService.GetFlag(ProgressFlagId.ProcessorUnlocked)) mask |= 1 << (int)ResourceType.Plank;
            if (progressionService.GetFlag(ProgressFlagId.PackingStationUnlocked)) mask |= 1 << (int)ResourceType.Crate;
            if (progressionService.GetMetric(ProgressMetricId.MineUnlocked) > 0) mask |= 1 << (int)ResourceType.IronOre;
            if (progressionService.GetFlag(ProgressFlagId.SmelterUnlocked)) mask |= 1 << (int)ResourceType.IronBar;
            if (progressionService.GetMetric(ProgressMetricId.ToolWorkshopUnlocked) > 0) mask |= 1 << (int)ResourceType.Tool;
            if (progressionService.GetMetric(ProgressMetricId.AssemblyPlantUnlocked) > 0) mask |= 1 << (int)ResourceType.MachinePart;
            return new MarketUnlockContext(mask);
        }

        private void EnsureMarketUnlocked()
        {
            if (progressionService != null && progressionService.EmpireLevel >= 20
                && progressionService.GetMetric(ProgressMetricId.MarketUnlocked) == 0)
            {
                progressionService.RecordMarketUnlocked();
                M16MarketRules.RotateDemand(_state, BuildUnlockContext());
                M16MarketRules.RefillOrders(_state, BuildUnlockContext());
                StateChanged?.Invoke();
            }
        }

        private void HandleSale(SaleFeedbackData sale)
        {
            if (!IsMarketUnlocked) return;
            DemandTier tier = GetDemandTier(sale.ResourceType);
            int bonusRevenue = Math.Max(0, sale.CashValue - M16MarketRules.GetBaseValue(sale.ResourceType));
            progressionService?.RecordMarketSale(tier, bonusRevenue);
        }

        private void HandleCourierSale(uint _, int crateCount, int cashValue)
        {
            if (!IsMarketUnlocked || crateCount <= 0) return;
            DemandTier tier = GetDemandTier(ResourceType.Crate);
            int baseTotal = M16MarketRules.GetBaseValue(ResourceType.Crate) * crateCount;
            progressionService?.RecordMarketSale(tier, Math.Max(0, cashValue - baseTotal));
        }

        private void HandleSmelterRecipe(int _, int __)
        {
            if (!IsMarketUnlocked) return;
            if (SystemUtcClock.Instance.UtcNowUnixSeconds < _state.lastAcceptedUtcUnixSeconds) return;
            M16MarketRules.RecordFlowProduction(_state, IndustrialFlowStep.IronBar,
                SystemUtcClock.Instance.UtcNowUnixSeconds, flowWindowSeconds, flowBonusSeconds, out bool ignored);
            StateChanged?.Invoke();
        }

        private void HandleToolRecipe(int _, int __, int produced)
        {
            if (!IsMarketUnlocked || produced <= 0) return;
            if (SystemUtcClock.Instance.UtcNowUnixSeconds < _state.lastAcceptedUtcUnixSeconds) return;
            M16MarketRules.RecordFlowProduction(_state, IndustrialFlowStep.Tool,
                SystemUtcClock.Instance.UtcNowUnixSeconds, flowWindowSeconds, flowBonusSeconds, out bool ignored);
            StateChanged?.Invoke();
        }

        private void HandleMachinePartRecipe(int produced)
        {
            if (!IsMarketUnlocked || produced <= 0) return;
            long now = SystemUtcClock.Instance.UtcNowUnixSeconds;
            if (now < _state.lastAcceptedUtcUnixSeconds) return;
            if (!M16MarketRules.RecordFlowProduction(_state, IndustrialFlowStep.MachinePart,
                    now, flowWindowSeconds, flowBonusSeconds, out bool newlyTriggered)) return;
            if (newlyTriggered) progressionService?.RecordFlowBonusTriggered();
            StateChanged?.Invoke();
        }

        private M16ProductionOrderSaveRecord GetOrder(int index) => _state?.orders != null && index >= 0 && index < _state.orders.Length ? _state.orders[index] : null;
        private M16ProductionOrderSaveRecord FindActiveOrder()
        {
            if (_state?.orders == null) return null;
            for (int i = 0; i < _state.orders.Length; i++)
                if (_state.orders[i]?.state == ProductionOrderState.Active) return _state.orders[i];
            return null;
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (salePoint != null) salePoint.UnitSold += HandleSale;
            if (courier != null) courier.DeliveryCompleted += HandleCourierSale;
            if (smelter != null) smelter.RecipeCompleted += HandleSmelterRecipe;
            if (toolWorkshop != null) toolWorkshop.RecipeCompleted += HandleToolRecipe;
            if (industrialDistrictService?.AssemblyPlant != null) industrialDistrictService.AssemblyPlant.RecipeCompleted += HandleMachinePartRecipe;
            if (progressionService != null) progressionService.StateChanged += EnsureMarketUnlocked;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (salePoint != null) salePoint.UnitSold -= HandleSale;
            if (courier != null) courier.DeliveryCompleted -= HandleCourierSale;
            if (smelter != null) smelter.RecipeCompleted -= HandleSmelterRecipe;
            if (toolWorkshop != null) toolWorkshop.RecipeCompleted -= HandleToolRecipe;
            if (industrialDistrictService?.AssemblyPlant != null) industrialDistrictService.AssemblyPlant.RecipeCompleted -= HandleMachinePartRecipe;
            if (progressionService != null) progressionService.StateChanged -= EnsureMarketUnlocked;
            _subscribed = false;
        }

        private void OnValidate()
        {
            rotationSeconds = Mathf.Max(30, rotationSeconds);
            flowWindowSeconds = Mathf.Max(10, flowWindowSeconds);
            flowBonusSeconds = Mathf.Max(10, flowBonusSeconds);
        }
    }
}
