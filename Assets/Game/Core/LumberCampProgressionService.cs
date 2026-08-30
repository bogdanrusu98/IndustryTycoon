using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Mining;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    [DefaultExecutionOrder(-900)]
    public sealed class LumberCampProgressionService : MonoBehaviour
    {
        [Header("Economy Reward Target")]
        [SerializeField] private Wallet wallet;

        [Header("Authoritative Gameplay Commits")]
        [SerializeField] private WoodSpawner woodSpawner;
        [SerializeField] private ResourceCollector resourceCollector;
        [SerializeField] private SalePoint salePoint;
        [SerializeField] private WoodProcessor processor;
        [SerializeField] private PackingStation packingStation;
        [SerializeField] private CrateCourier courier;
        [SerializeField] private IronVein ironVein;
        [SerializeField] private Smelter smelter;
        [SerializeField] private AutomatedDrill automatedDrill;

        [Header("Authoritative Unlocks")]
        [SerializeField] private WoodProductionUpgrade productionUpgrade;
        [SerializeField] private FirstWorkerUnlock workerUnlock;
        [SerializeField] private FirstProcessorUnlock processorUnlock;
        [SerializeField] private FirstAutoFeederUnlock autoFeederUnlock;
        [SerializeField] private FirstPackingStationUnlock packingStationUnlock;
        [SerializeField] private FirstCourierUnlock courierUnlock;
        [SerializeField] private LumberCampCompletion lumberCampCompletion;
        [SerializeField] private MineUnlock mineUnlock;
        [SerializeField] private SmelterUnlock smelterUnlock;
        [SerializeField] private DrillUnlock drillUnlock;

        private LumberCampProgressionModel _model;
        private bool _hasStarted;
        private bool _isSubscribed;

        public event Action StateChanged;
        public event Action<int> AchievementUnlocked;

        public Wallet Wallet => wallet;
        public WoodSpawner WoodSpawner => woodSpawner;
        public ResourceCollector ResourceCollector => resourceCollector;
        public SalePoint SalePoint => salePoint;
        public WoodProcessor Processor => processor;
        public PackingStation PackingStation => packingStation;
        public CrateCourier Courier => courier;
        public IronVein IronVein => ironVein;
        public Smelter Smelter => smelter;
        public AutomatedDrill AutomatedDrill => automatedDrill;
        public WoodProductionUpgrade ProductionUpgrade => productionUpgrade;
        public FirstWorkerUnlock WorkerUnlock => workerUnlock;
        public FirstProcessorUnlock ProcessorUnlock => processorUnlock;
        public FirstAutoFeederUnlock AutoFeederUnlock => autoFeederUnlock;
        public FirstPackingStationUnlock PackingStationUnlock => packingStationUnlock;
        public FirstCourierUnlock CourierUnlock => courierUnlock;
        public LumberCampCompletion LumberCampCompletion => lumberCampCompletion;
        public MineUnlock MineUnlock => mineUnlock;
        public SmelterUnlock SmelterUnlock => smelterUnlock;
        public DrillUnlock DrillUnlock => drillUnlock;
        public bool IsRuntimeReady => _hasStarted;
        public int ObjectiveIndex => _model != null ? _model.ObjectiveIndex : 0;
        public bool AreAllObjectivesCompleted => _model != null
                                                 && _model.AreAllObjectivesCompleted;
        public int ActiveContractIndex => _model != null
            ? _model.ActiveContractIndex
            : 0;
        public bool HasActiveContract => _model != null && _model.HasActiveContract;
        public ContractProgressState ActiveContractState => _model != null
            ? _model.ActiveContractState
            : ContractProgressState.Active;
        public string ObjectiveDisplayText => _model != null
            ? _model.BuildObjectiveDisplayText()
            : "OBJECTIVE: UNLOCK WORKER";

        private void Awake()
        {
            _model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                TryGrantRewardCash);
            _model.StateChanged += HandleModelStateChanged;
            _model.AchievementUnlocked += HandleAchievementUnlocked;
        }

        private void Start()
        {
            _hasStarted = true;
            SubscribeGameplayCommits();
            SynchronizeExactRuntimeFlags();
            _model.EvaluateAll();
        }

        private void OnEnable()
        {
            if (_hasStarted)
            {
                SubscribeGameplayCommits();
                SynchronizeExactRuntimeFlags();
                _model.EvaluateAll();
            }
        }

        private void OnDisable()
        {
            UnsubscribeGameplayCommits();
        }

        private void OnDestroy()
        {
            UnsubscribeGameplayCommits();
            if (_model != null)
            {
                _model.StateChanged -= HandleModelStateChanged;
                _model.AchievementUnlocked -= HandleAchievementUnlocked;
            }
        }

        public long GetMetric(ProgressMetricId metric)
        {
            return _model != null ? _model.GetMetric(metric) : 0L;
        }

        public bool GetFlag(ProgressFlagId flag)
        {
            return _model != null && _model.GetFlag(flag);
        }

        public void GetObjectiveProgress(out long current, out long target)
        {
            if (_model == null)
            {
                current = 0L;
                target = 1L;
                return;
            }

            _model.GetObjectiveProgress(out current, out target);
        }

        public void GetActiveContractProgress(out long current, out long target)
        {
            if (_model == null)
            {
                current = 0L;
                target = 0L;
                return;
            }

            _model.GetActiveContractProgress(out current, out target);
        }

        public void GetAchievementProgress(
            int achievementIndex,
            out long current,
            out long target)
        {
            if (_model == null)
            {
                current = 0L;
                target = 1L;
                return;
            }

            _model.GetAchievementProgress(achievementIndex, out current, out target);
        }

        public bool IsAchievementUnlocked(int achievementIndex)
        {
            return _model != null && _model.IsAchievementUnlocked(achievementIndex);
        }

        public bool IsAchievementRewarded(int achievementIndex)
        {
            return _model != null && _model.IsAchievementRewarded(achievementIndex);
        }

        public bool IsContractClaimed(int contractIndex)
        {
            return _model != null && _model.IsContractClaimed(contractIndex);
        }

        public bool TryClaimActiveContract()
        {
            return _model != null && _model.TryClaimActiveContract();
        }

        public M10ProgressionSaveData CapturePersistentState()
        {
            return _model != null
                ? _model.CapturePersistentState()
                : M10ProgressionSaveData.CreateFresh();
        }

        public void RestorePersistentState(M10ProgressionSaveData state)
        {
            if (_model == null)
            {
                _model = new LumberCampProgressionModel(
                    M10ProgressionSaveData.CreateFresh(),
                    TryGrantRewardCash);
                _model.StateChanged += HandleModelStateChanged;
                _model.AchievementUnlocked += HandleAchievementUnlocked;
            }

            // Restore is intentionally silent: no evaluation, rewards, metric events,
            // or achievement toast can originate from loading a save.
            _model.Restore(state ?? M10ProgressionSaveData.CreateFresh());
            StateChanged?.Invoke();
        }

        public bool EvaluateCurrentState()
        {
            // Persistence uses this only after applying a legitimate offline state
            // transition. It resolves objectives/achievements from the restored
            // shared truth without fabricating any gameplay metric.
            return _model != null && _model.EvaluateAll();
        }

        private bool TryGrantRewardCash(int amount)
        {
            if (amount <= 0
                || wallet == null
                || wallet.Balance > int.MaxValue - amount)
            {
                return false;
            }

            return wallet.Deposit(amount) == amount;
        }

        private void SubscribeGameplayCommits()
        {
            if (_isSubscribed)
            {
                return;
            }

            if (woodSpawner != null)
            {
                woodSpawner.WoodProduced += HandleWoodProduced;
            }

            if (resourceCollector != null)
            {
                resourceCollector.CollectionCommitted += HandlePlayerCollection;
            }

            if (salePoint != null)
            {
                salePoint.UnitSold += HandleUnitSold;
            }

            if (processor != null)
            {
                processor.RecipeCompleted += HandleProcessorRecipeCompleted;
            }

            if (packingStation != null)
            {
                packingStation.RecipeCompleted += HandlePackingRecipeCompleted;
            }

            if (courier != null)
            {
                courier.DeliveryCompleted += HandleCourierDeliveryCompleted;
            }

            if (ironVein != null)
            {
                ironVein.OreMined += HandleIronOreMined;
            }

            if (smelter != null)
            {
                smelter.RecipeCompleted += HandleSmelterRecipeCompleted;
            }

            if (automatedDrill != null)
            {
                automatedDrill.OreProduced += HandleAutomatedDrillOreProduced;
            }

            SetPurchaseSubscription(
                productionUpgrade != null ? productionUpgrade.PurchasePad : null,
                HandleProductionUpgradeCompleted,
                true);
            SetPurchaseSubscription(
                workerUnlock != null ? workerUnlock.WorkerPurchasePad : null,
                HandleWorkerCompleted,
                true);
            SetPurchaseSubscription(
                processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null,
                HandleProcessorCompleted,
                true);
            SetPurchaseSubscription(
                autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null,
                HandleAutoFeederCompleted,
                true);
            SetPurchaseSubscription(
                packingStationUnlock != null
                    ? packingStationUnlock.PackingStationPurchasePad
                    : null,
                HandlePackingStationCompleted,
                true);
            SetPurchaseSubscription(
                courierUnlock != null ? courierUnlock.CourierPurchasePad : null,
                HandleCourierCompleted,
                true);
            SetPurchaseSubscription(
                smelterUnlock != null ? smelterUnlock.SmelterPurchasePad : null,
                HandleSmelterCompleted,
                true);
            SetPurchaseSubscription(
                drillUnlock != null ? drillUnlock.DrillPurchasePad : null,
                HandleDrillCompleted,
                true);

            if (lumberCampCompletion != null)
            {
                lumberCampCompletion.Completed += HandleLumberCampCompleted;
            }

            if (mineUnlock != null)
            {
                mineUnlock.Unlocked += HandleMineUnlocked;
            }

            if (wallet != null)
            {
                wallet.BalanceChanged += HandleWalletBalanceChanged;
            }

            _isSubscribed = true;
        }

        private void UnsubscribeGameplayCommits()
        {
            if (!_isSubscribed)
            {
                return;
            }

            if (woodSpawner != null)
            {
                woodSpawner.WoodProduced -= HandleWoodProduced;
            }

            if (resourceCollector != null)
            {
                resourceCollector.CollectionCommitted -= HandlePlayerCollection;
            }

            if (salePoint != null)
            {
                salePoint.UnitSold -= HandleUnitSold;
            }

            if (processor != null)
            {
                processor.RecipeCompleted -= HandleProcessorRecipeCompleted;
            }

            if (packingStation != null)
            {
                packingStation.RecipeCompleted -= HandlePackingRecipeCompleted;
            }

            if (courier != null)
            {
                courier.DeliveryCompleted -= HandleCourierDeliveryCompleted;
            }


            if (ironVein != null)
            {
                ironVein.OreMined -= HandleIronOreMined;
            }

            if (smelter != null)
            {
                smelter.RecipeCompleted -= HandleSmelterRecipeCompleted;
            }

            if (automatedDrill != null)
            {
                automatedDrill.OreProduced -= HandleAutomatedDrillOreProduced;
            }

            SetPurchaseSubscription(
                productionUpgrade != null ? productionUpgrade.PurchasePad : null,
                HandleProductionUpgradeCompleted,
                false);
            SetPurchaseSubscription(
                workerUnlock != null ? workerUnlock.WorkerPurchasePad : null,
                HandleWorkerCompleted,
                false);
            SetPurchaseSubscription(
                processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null,
                HandleProcessorCompleted,
                false);
            SetPurchaseSubscription(
                autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null,
                HandleAutoFeederCompleted,
                false);
            SetPurchaseSubscription(
                packingStationUnlock != null
                    ? packingStationUnlock.PackingStationPurchasePad
                    : null,
                HandlePackingStationCompleted,
                false);
            SetPurchaseSubscription(
                courierUnlock != null ? courierUnlock.CourierPurchasePad : null,
                HandleCourierCompleted,
                false);
            SetPurchaseSubscription(
                smelterUnlock != null ? smelterUnlock.SmelterPurchasePad : null,
                HandleSmelterCompleted,
                false);
            SetPurchaseSubscription(
                drillUnlock != null ? drillUnlock.DrillPurchasePad : null,
                HandleDrillCompleted,
                false);

            if (lumberCampCompletion != null)
            {
                lumberCampCompletion.Completed -= HandleLumberCampCompleted;
            }

            if (mineUnlock != null)
            {
                mineUnlock.Unlocked -= HandleMineUnlocked;
            }

            if (wallet != null)
            {
                wallet.BalanceChanged -= HandleWalletBalanceChanged;
            }

            _isSubscribed = false;
        }

        private void SynchronizeExactRuntimeFlags()
        {
            if (_model == null)
            {
                return;
            }

            if (productionUpgrade != null && productionUpgrade.IsApplied)
            {
                _model.RecordFlag(ProgressFlagId.ProductionUpgradeUnlocked);
            }

            if (workerUnlock != null && workerUnlock.IsWorkerActivated)
            {
                _model.RecordFlag(ProgressFlagId.WorkerUnlocked);
            }

            if (processorUnlock != null && processorUnlock.IsProcessorActivated)
            {
                _model.RecordFlag(ProgressFlagId.ProcessorUnlocked);
            }

            if (autoFeederUnlock != null && autoFeederUnlock.IsAutoFeederActivated)
            {
                _model.RecordFlag(ProgressFlagId.AutoFeederUnlocked);
            }

            if (packingStationUnlock != null
                && packingStationUnlock.IsPackingStationActivated)
            {
                _model.RecordFlag(ProgressFlagId.PackingStationUnlocked);
            }

            if (courierUnlock != null && courierUnlock.IsCourierActivated)
            {
                _model.RecordFlag(ProgressFlagId.CourierUnlocked);
            }

            if (lumberCampCompletion != null && lumberCampCompletion.IsCompleted)
            {
                _model.RecordFlag(ProgressFlagId.LumberCampCompleted);
            }

            if (mineUnlock != null && mineUnlock.IsUnlocked)
            {
                _model.RecordMineUnlocked();
            }

            if (smelterUnlock != null && smelterUnlock.IsSmelterActivated)
            {
                _model.RecordFlag(ProgressFlagId.SmelterUnlocked);
            }

            if (drillUnlock != null && drillUnlock.IsDrillActivated)
            {
                _model.RecordDrillUnlocked();
            }
        }

        private static void SetPurchaseSubscription(
            PurchasePad purchasePad,
            Action handler,
            bool subscribe)
        {
            if (purchasePad == null)
            {
                return;
            }

            if (subscribe)
            {
                purchasePad.Completed += handler;
            }
            else
            {
                purchasePad.Completed -= handler;
            }
        }

        private void HandleWoodProduced(int amount)
        {
            _model.RecordWoodProduced(amount);
        }

        private void HandlePlayerCollection(ResourceType resourceType, int amount)
        {
            _model.RecordPlayerCollection(resourceType, amount);
        }

        private void HandleUnitSold(SaleFeedbackData feedback)
        {
            _model.RecordSale(feedback.ResourceType, 1, feedback.CashValue);
        }

        private void HandleProcessorRecipeCompleted(int inputWood, int outputPlanks)
        {
            _model.RecordPlanksProduced(
                processor != null ? processor.RecipeOutputPlanks : 1);
        }

        private void HandlePackingRecipeCompleted(int inputPlanks, int outputCrates)
        {
            _model.RecordCratesProduced(
                packingStation != null ? packingStation.RecipeOutputCrates : 1);
        }

        private void HandleCourierDeliveryCompleted(
            uint generation,
            int crateCount,
            int cashValue)
        {
            _model.RecordCourierDelivery(crateCount, cashValue);
        }

        private void HandleIronOreMined(int amount)
        {
            _model.RecordIronOreMined(amount);
        }

        private void HandleSmelterRecipeCompleted(
            int resultingInputOre,
            int resultingOutputBars)
        {
            _model.RecordIronBarsProduced(
                smelter != null ? smelter.RecipeOutputBars : 1);
        }

        private void HandleAutomatedDrillOreProduced(int amount)
        {
            _model.RecordIronOreProduced(amount);
        }

        private void HandleProductionUpgradeCompleted()
        {
            _model.RecordFlag(ProgressFlagId.ProductionUpgradeUnlocked);
        }

        private void HandleWorkerCompleted()
        {
            _model.RecordFlag(ProgressFlagId.WorkerUnlocked);
        }

        private void HandleProcessorCompleted()
        {
            _model.RecordFlag(ProgressFlagId.ProcessorUnlocked);
        }

        private void HandleAutoFeederCompleted()
        {
            _model.RecordFlag(ProgressFlagId.AutoFeederUnlocked);
        }

        private void HandlePackingStationCompleted()
        {
            _model.RecordFlag(ProgressFlagId.PackingStationUnlocked);
        }

        private void HandleCourierCompleted()
        {
            _model.RecordFlag(ProgressFlagId.CourierUnlocked);
        }

        private void HandleLumberCampCompleted()
        {
            _model.RecordFlag(ProgressFlagId.LumberCampCompleted);
        }

        private void HandleMineUnlocked()
        {
            _model.RecordMineUnlocked();
        }

        private void HandleSmelterCompleted()
        {
            _model.RecordFlag(ProgressFlagId.SmelterUnlocked);
        }

        private void HandleDrillCompleted()
        {
            _model.RecordDrillUnlocked();
        }

        private void HandleWalletBalanceChanged(int balance)
        {
            // Only retries an already-unlocked reward that could not fit previously.
            // Wallet changes are never translated into gameplay Cash metrics.
            _model.EvaluateAll();
        }

        private void HandleModelStateChanged()
        {
            StateChanged?.Invoke();
        }

        private void HandleAchievementUnlocked(int achievementIndex)
        {
            AchievementUnlocked?.Invoke(achievementIndex);
        }
    }
}
