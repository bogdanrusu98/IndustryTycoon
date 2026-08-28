using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.UI;
using IndustryTycoon.Workers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndustryTycoon.Persistence
{
    [DefaultExecutionOrder(-1000)]
    public sealed class LocalPersistenceService : MonoBehaviour
    {
        [Header("Authoritative Economy")]
        [SerializeField] private Wallet wallet;
        [SerializeField] private CashPile cashPile;
        [SerializeField] private CashPileCollector cashPileCollector;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private ResourceCollector resourceCollector;

        [Header("Authoritative Progression")]
        [SerializeField] private WoodProductionUpgrade productionUpgrade;
        [SerializeField] private FirstWorkerUnlock workerUnlock;
        [SerializeField] private FirstProcessorUnlock processorUnlock;
        [SerializeField] private FirstAutoFeederUnlock autoFeederUnlock;
        [SerializeField] private FirstPackingStationUnlock packingStationUnlock;
        [SerializeField] private FirstCourierUnlock courierUnlock;
        [SerializeField] private LumberCampCompletion lumberCampCompletion;
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private NextUnlockGuidance nextUnlockGuidance;

        [Header("Authoritative Production")]
        [SerializeField] private WoodSpawner woodSpawner;
        [SerializeField] private LumberWorker lumberWorker;
        [SerializeField] private WoodStockpile stockpile;
        [SerializeField] private WoodProcessor processor;
        [SerializeField] private WoodAutoFeeder autoFeeder;
        [SerializeField] private PackingStation packingStation;
        [SerializeField] private CrateCourier courier;

        [Header("Save Cadence")]
        [SerializeField, Min(0.25f)] private float saveDebounceSeconds = 3f;

        [Header("Offline Rules")]
        [SerializeField, Min(0f)] private float returnScreenThresholdSeconds = 300f;
        [SerializeField, Min(1f)] private float maximumCreditedAwaySeconds = 14400f;
        [SerializeField, Range(0f, 1f)] private float offlineEfficiency = 0.60f;
        [SerializeField, Min(0.1f)] private float workerSecondsPerWood = 6.50f;

        private IUtcClock _clock;
        private M9LocalSaveStore _saveStore;
        private M9SaveValidationSettings _validationSettings;
        private M9SaveData _state;
        private bool _isApplyingState;
        private bool _isSaving;
        private bool _isSubscribed;
        private bool _isDirty;
        private bool _immediateSaveRequested;
        private bool _wasPaused;
        private bool _suppressSaves;
        private double _saveDueAt;
        private string _lastWriteDiagnostic = string.Empty;

        public event Action ReturnStateChanged;

        public bool IsInitialized { get; private set; }
        public bool IsDirty => _isDirty;
        public bool HasPendingReturn => _state != null && _state.returnScreenPending;
        public int PendingOfflineCash => _state != null ? _state.pendingOfflineCash : 0;
        public long PendingAwaySeconds => _state != null
            ? _state.pendingOfflineAwaySeconds
            : 0L;
        public string SavePath => _saveStore != null ? _saveStore.PrimaryPath : string.Empty;
        public M9SaveLoadStatus LastLoadStatus { get; private set; }
        public OfflineProgressionResult LastOfflineResult { get; private set; }
        public int SuccessfulSaveCount { get; private set; }

        public Wallet Wallet => wallet;
        public CashPile CashPile => cashPile;
        public CarryStack CarryStack => carryStack;
        public WoodProductionUpgrade ProductionUpgrade => productionUpgrade;
        public FirstWorkerUnlock WorkerUnlock => workerUnlock;
        public FirstProcessorUnlock ProcessorUnlock => processorUnlock;
        public FirstAutoFeederUnlock AutoFeederUnlock => autoFeederUnlock;
        public FirstPackingStationUnlock PackingStationUnlock => packingStationUnlock;
        public FirstCourierUnlock CourierUnlock => courierUnlock;
        public LumberCampCompletion LumberCampCompletion => lumberCampCompletion;
        public LumberCampProgressionService ProgressionService => progressionService;
        public WoodStockpile Stockpile => stockpile;
        public WoodProcessor Processor => processor;
        public PackingStation PackingStation => packingStation;
        public LumberWorker LumberWorker => lumberWorker;
        public WoodAutoFeeder AutoFeeder => autoFeeder;
        public CrateCourier Courier => courier;
        public float SaveDebounceSeconds => saveDebounceSeconds;
        public float ReturnScreenThresholdSeconds => returnScreenThresholdSeconds;
        public float MaximumCreditedAwaySeconds => maximumCreditedAwaySeconds;
        public float OfflineEfficiency => offlineEfficiency;
        public float WorkerSecondsPerWood => workerSecondsPerWood;
        public double CourierSecondsPerTrip => ResolveCourierSecondsPerTrip();

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (!IsInitialized
                || _suppressSaves
                || !_isDirty
                || Time.realtimeSinceStartupAsDouble < _saveDueAt)
            {
                return;
            }

            SaveNow();
        }

        private void LateUpdate()
        {
            if (!IsInitialized || _suppressSaves || !_immediateSaveRequested)
            {
                return;
            }

            _immediateSaveRequested = false;
            SaveNow();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!IsInitialized || _suppressSaves)
            {
                return;
            }

            if (pauseStatus)
            {
                _wasPaused = true;
                MarkDirty();
                SaveNow();
            }
            else if (_wasPaused)
            {
                _wasPaused = false;
                EvaluateResumeInterval();
            }
        }

        private void OnApplicationQuit()
        {
            if (!IsInitialized || _suppressSaves)
            {
                return;
            }

            MarkDirty();
            SaveNow();
        }

        private void OnDestroy()
        {
            UnsubscribeDirtyTracking();
        }

        public bool SaveNow()
        {
            if (!IsInitialized
                || _suppressSaves
                || _isSaving
                || _isApplyingState
                || _saveStore == null)
            {
                return false;
            }

            long now = _clock.UtcNowUnixSeconds;
            if (!TryBuildStableSnapshot(now, out M9SaveData snapshot, out string failure))
            {
                KeepDirtyAfterFailure(failure);
                return false;
            }

            _state = snapshot;
            return PersistCurrentState();
        }

        public bool TryCollectOfflineReward(float multiplier)
        {
            if (!IsInitialized
                || _state == null
                || wallet == null)
            {
                return false;
            }

            int originalWalletBalance = wallet.Balance;
            int originalPendingCash = _state.pendingOfflineCash;
            long originalPendingAwaySeconds = _state.pendingOfflineAwaySeconds;
            bool originalReturnScreenPending = _state.returnScreenPending;
            if (!OfflineRewardCollection.TryCollect(
                    _state,
                    originalWalletBalance,
                    multiplier,
                    out int creditedCash,
                    out _))
            {
                return false;
            }

            if (creditedCash > 0
                && wallet.Deposit(creditedCash) != creditedCash)
            {
                wallet.RestoreBalance(originalWalletBalance);
                RestorePendingReturn(
                    originalPendingCash,
                    originalPendingAwaySeconds,
                    originalReturnScreenPending);
                return false;
            }

            MarkDirty();
            bool saved = SaveNow();
            if (!saved)
            {
                wallet.RestoreBalance(originalWalletBalance);
                RestorePendingReturn(
                    originalPendingCash,
                    originalPendingAwaySeconds,
                    originalReturnScreenPending);
                MarkDirty();
                return false;
            }

            ReturnStateChanged?.Invoke();
            return true;
        }

        private void RestorePendingReturn(
            int pendingCash,
            long pendingAwaySeconds,
            bool returnScreenPending)
        {
            _state.pendingOfflineCash = pendingCash;
            _state.pendingOfflineAwaySeconds = pendingAwaySeconds;
            _state.returnScreenPending = returnScreenPending;
        }

        public bool ResetSaveAndReload()
        {
            if (!CanUseDevelopmentUtilities() || _saveStore == null)
            {
                return false;
            }

            _suppressSaves = true;
            UnsubscribeDirtyTracking();
            bool deleted = _saveStore.TryDeleteSave(out string failureReason);
            if (!deleted)
            {
                _suppressSaves = false;
                Debug.LogError(failureReason, this);
                SubscribeDirtyTracking();
                return false;
            }

            _state = M9SaveData.CreateFresh(_clock.UtcNowUnixSeconds);
            ReturnStateChanged?.Invoke();
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex);
            return true;
        }

        [ContextMenu("QA/Simulate 10 Minutes Away")]
        public void SimulateTenMinutesAway()
        {
            SimulateAwayForDevelopment(10L * 60L);
        }

        [ContextMenu("QA/Simulate 4 Minutes Away (Below Threshold)")]
        public void SimulateFourMinutesAway()
        {
            SimulateAwayForDevelopment(4L * 60L);
        }

        [ContextMenu("QA/Simulate 5 Hours Away (4h Cap)")]
        public void SimulateFiveHoursAway()
        {
            SimulateAwayForDevelopment(5L * 60L * 60L);
        }

        [ContextMenu("QA/Simulate Backward Clock")]
        public void SimulateBackwardClock()
        {
            SimulateAwayForDevelopment(-60L * 60L);
        }

        [ContextMenu("QA/Simulate Invalid Timestamp")]
        public void SimulateInvalidTimestamp()
        {
            SimulateAwayForDevelopment(long.MinValue);
        }

        public bool SimulateAwayForDevelopment(long awaySeconds)
        {
            if (!CanUseDevelopmentUtilities()
                || !IsInitialized
                || _state == null
                || _state.returnScreenPending)
            {
                return false;
            }

            if (!SaveNow())
            {
                return false;
            }

            long now = _clock.UtcNowUnixSeconds;
            if (awaySeconds >= 0L)
            {
                _state.lastEvaluationUtcUnixSeconds = now - awaySeconds;
            }
            else
            {
                long futureOffset = awaySeconds == long.MinValue
                    ? long.MaxValue
                    : -awaySeconds;
                _state.lastEvaluationUtcUnixSeconds = now > long.MaxValue - futureOffset
                    ? long.MaxValue
                    : now + futureOffset;
            }

            EvaluateAndApply(_state, now);
            bool saved = PersistCurrentState();
            ReturnStateChanged?.Invoke();
            return saved;
        }

        private void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            _clock = SystemUtcClock.Instance;
            _validationSettings = BuildValidationSettings();
            long now = _clock.UtcNowUnixSeconds;
            try
            {
                _saveStore = M9LocalSaveStore.CreateForPersistentDataPath(
                    _clock,
                    _validationSettings,
                    Application.isEditor || Debug.isDebugBuild);
                M9SaveLoadResult loadResult = _saveStore.Load();
                LastLoadStatus = loadResult.Status;
                _state = loadResult.Data ?? M9SaveData.CreateFresh(now);
                if (loadResult.Status != M9SaveLoadStatus.LoadedPrimary
                    && loadResult.Status != M9SaveLoadStatus.FreshNoSave
                    && !string.IsNullOrEmpty(loadResult.Diagnostic))
                {
                    Debug.LogWarning(
                        $"Local save recovered safely ({loadResult.Status}): "
                        + loadResult.Diagnostic,
                        this);
                }
            }
            catch (Exception exception)
            {
                LastLoadStatus = M9SaveLoadStatus.FreshIoFailure;
                _state = M9SaveData.CreateFresh(now);
                Debug.LogError(
                    $"Local persistence is unavailable; using a fresh in-memory state. {exception.Message}",
                    this);
            }

            EvaluateAndApply(_state, now);
            IsInitialized = true;
            SubscribeDirtyTracking();
            PersistCurrentState();
            ReturnStateChanged?.Invoke();
        }

        private void EvaluateResumeInterval()
        {
            long now = _clock.UtcNowUnixSeconds;
            M9SaveData stableState = _state;
            if (TryBuildStableSnapshot(
                    _state != null ? _state.lastEvaluationUtcUnixSeconds : now,
                    out M9SaveData liveSnapshot,
                    out _))
            {
                // The pause flush already established the evaluation anchor. Preserve it
                // while reconciling any live transient ownership into stable buffers.
                liveSnapshot.lastEvaluationUtcUnixSeconds = _state.lastEvaluationUtcUnixSeconds;
                stableState = liveSnapshot;
            }

            EvaluateAndApply(stableState, now);
            MarkDirty();
            PersistCurrentState();
            ReturnStateChanged?.Invoke();
        }

        private void EvaluateAndApply(M9SaveData data, long now)
        {
            OfflineProgressionInput input = BuildOfflineInput(data, now);
            OfflineProgressionRules rules = BuildOfflineRules(data);
            LastOfflineResult = OfflineProgressionCalculator.Calculate(input, rules);
            ApplyOfflineResult(data, LastOfflineResult);
            ApplyStableState(data);

            // ApplyStableState restores M10 silently by design. Offline settlement
            // may nevertheless establish an exact canonical completion flag. Resolve
            // its objective/achievement transitions before the Welcome Back reward
            // can be collected, then fold all synchronous reward side effects back
            // into the state that is persisted. Offline production/delivery remains
            // excluded from the lifetime counters.
            _state = data;
            progressionService?.EvaluateCurrentState();
            if (progressionService != null)
            {
                data.progression = progressionService.CapturePersistentState();
            }

            if (wallet != null)
            {
                data.walletCash = wallet.Balance;
            }
        }

        private OfflineProgressionInput BuildOfflineInput(M9SaveData data, long now)
        {
            return new OfflineProgressionInput
            {
                LastEvaluationUtcUnixSeconds = data.lastEvaluationUtcUnixSeconds,
                NowUtcUnixSeconds = now,
                WorkerUnlocked = IsPadCompleted(data, M9PurchasePadIds.LumberWorker),
                ProcessorUnlocked = IsPadCompleted(data, M9PurchasePadIds.WoodProcessor),
                AutoFeederUnlocked = IsPadCompleted(data, M9PurchasePadIds.AutoFeeder),
                PackingUnlocked = IsPadCompleted(data, M9PurchasePadIds.PackingStation),
                CourierUnlocked = IsPadCompleted(data, M9PurchasePadIds.DeliveryCourier),
                StockpileWood = data.stockpileWood,
                StockpileCapacity = stockpile != null ? stockpile.Capacity : 30,
                ProcessorInputWood = data.processorInputWood,
                ProcessorInputCapacity = processor != null ? processor.InputCapacity : 24,
                ProcessorOutputPlanks = data.processorOutputPlanks,
                ProcessorOutputCapacity = processor != null ? processor.OutputCapacity : 12,
                PackingInputPlanks = data.packingInputPlanks,
                PackingInputCapacity = packingStation != null ? packingStation.InputCapacity : 24,
                PackingOutputCrates = data.packingOutputCrates,
                PackingOutputCapacity = packingStation != null ? packingStation.OutputCapacity : 12,
                PendingOfflineCash = data.pendingOfflineCash,
                PendingOfflineAwaySeconds = data.pendingOfflineAwaySeconds,
                ReturnScreenPending = data.returnScreenPending
            };
        }

        private OfflineProgressionRules BuildOfflineRules(M9SaveData data)
        {
            var rules = OfflineProgressionRules.CreateDefault();
            rules.MaximumCreditedAwaySeconds = Math.Max(
                0L,
                (long)Math.Floor(maximumCreditedAwaySeconds));
            rules.OfflineEfficiency = offlineEfficiency;
            rules.ReturnScreenThresholdSeconds = Math.Max(
                0L,
                (long)Math.Floor(returnScreenThresholdSeconds));
            rules.WorkerCollectionSecondsPerWood = workerSecondsPerWood;
            rules.WoodProductionSecondsPerWood = ResolveOfflineProductionSeconds(data);
            rules.FeederTransferSecondsPerWood = autoFeeder != null
                ? autoFeeder.LaunchInterval
                : rules.FeederTransferSecondsPerWood;
            rules.ProcessorSecondsPerRecipe = processor != null
                ? processor.ProcessingDuration
                : rules.ProcessorSecondsPerRecipe;
            rules.PackingSecondsPerRecipe = packingStation != null
                ? packingStation.ProcessingDuration
                : rules.PackingSecondsPerRecipe;
            rules.CourierSecondsPerTrip = ResolveCourierSecondsPerTrip();
            rules.CourierCratesPerTrip = courier != null ? courier.Capacity : 2;
            rules.CashPerDeliveredCrate = courier != null ? courier.CashPerCrate : 40;
            return rules;
        }

        private double ResolveOfflineProductionSeconds(M9SaveData data)
        {
            double baseSeconds = woodSpawner != null ? woodSpawner.BaseSpawnInterval : 1.25d;
            double multiplier = IsPadCompleted(data, M9PurchasePadIds.ProductionUpgrade)
                && productionUpgrade != null
                    ? productionUpgrade.ProductionMultiplier
                    : 1d;
            return baseSeconds / Math.Max(0.1d, multiplier);
        }

        private double ResolveCourierSecondsPerTrip()
        {
            if (courier == null)
            {
                return 11.40d;
            }

            double travelSeconds = 0d;
            if (courier.PickupPoint != null && courier.DeliveryPoint != null)
            {
                double routeDistance = Vector3.Distance(
                    courier.PickupPoint.position,
                    courier.DeliveryPoint.position);
                travelSeconds = (routeDistance * 2d) / Math.Max(0.1d, courier.MovementSpeed);
            }

            return Math.Max(
                0.1d,
                travelSeconds
                + courier.PickupDelay
                + courier.DeliveryDelay
                + courier.RetryInterval);
        }

        private static void ApplyOfflineResult(
            M9SaveData data,
            OfflineProgressionResult result)
        {
            data.stockpileWood = result.StockpileWood;
            data.processorInputWood = result.ProcessorInputWood;
            data.processorOutputPlanks = result.ProcessorOutputPlanks;
            data.packingInputPlanks = result.PackingInputPlanks;
            data.packingOutputCrates = result.PackingOutputCrates;
            data.pendingOfflineCash = result.PendingOfflineCash;
            data.pendingOfflineAwaySeconds = result.PendingOfflineAwaySeconds;
            data.returnScreenPending = result.ReturnScreenPending;
            data.lastEvaluationUtcUnixSeconds = result.NextEvaluationUtcUnixSeconds;
            if (result.CourierCratesDelivered > 0)
            {
                data.lumberCampCompleted = true;
                data.progression?.SetFlag(ProgressFlagId.LumberCampCompleted);
            }
        }

        private void ApplyStableState(M9SaveData data)
        {
            _isApplyingState = true;
            bool workerEnabled = lumberWorker != null && lumberWorker.enabled;
            bool processorEnabled = processor != null && processor.enabled;
            bool feederEnabled = autoFeeder != null && autoFeeder.enabled;
            bool packingEnabled = packingStation != null && packingStation.enabled;
            bool courierEnabled = courier != null && courier.enabled;
            bool cashPlayerInside = false;
            try
            {
                progressionService?.RestorePersistentState(data.progression);
                resourceCollector?.CancelTransientAttractions();
                if (cashPileCollector != null)
                {
                    cashPlayerInside = cashPileCollector.NormalizeForPersistenceRestore();
                }

                SetEnabled(lumberWorker, false);
                SetEnabled(autoFeeder, false);
                SetEnabled(courier, false);
                SetEnabled(processor, false);
                SetEnabled(packingStation, false);

                wallet?.RestoreBalance(data.walletCash);
                cashPile?.RestoreStoredCash(data.cashPileStoredCash);
                ResourceType? carryType = data.carry != null && data.carry.amount > 0
                    ? data.carry.resourceType
                    : (ResourceType?)null;
                carryStack?.RestoreStableState(
                    carryType,
                    data.carry != null ? data.carry.amount : 0);

                RestorePurchasePad(
                    productionUpgrade != null ? productionUpgrade.PurchasePad : null,
                    data,
                    M9PurchasePadIds.ProductionUpgrade);
                RestorePurchasePad(
                    workerUnlock != null ? workerUnlock.WorkerPurchasePad : null,
                    data,
                    M9PurchasePadIds.LumberWorker);
                RestorePurchasePad(
                    processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null,
                    data,
                    M9PurchasePadIds.WoodProcessor);
                RestorePurchasePad(
                    autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null,
                    data,
                    M9PurchasePadIds.AutoFeeder);
                RestorePurchasePad(
                    packingStationUnlock != null
                        ? packingStationUnlock.PackingStationPurchasePad
                        : null,
                    data,
                    M9PurchasePadIds.PackingStation);
                RestorePurchasePad(
                    courierUnlock != null ? courierUnlock.CourierPurchasePad : null,
                    data,
                    M9PurchasePadIds.DeliveryCourier);

                stockpile?.RestoreStableState(data.stockpileWood);
                processor?.RestoreStableState(
                    data.processorInputWood,
                    data.processorOutputPlanks);
                packingStation?.RestoreStableState(
                    data.packingInputPlanks,
                    data.packingOutputCrates);
                lumberWorker?.RestoreIdleState();
                courier?.RestoreIdleState();

                productionUpgrade?.SynchronizeFromPurchaseState();
                workerUnlock?.SynchronizeFromPurchaseState();
                processorUnlock?.SynchronizeFromPurchaseState();
                autoFeederUnlock?.SynchronizeFromPurchaseState();
                packingStationUnlock?.SynchronizeFromPurchaseState();
                courierUnlock?.SynchronizeFromPurchaseState();
                lumberCampCompletion?.RestoreCompleted(data.lumberCampCompleted);
                nextUnlockGuidance?.Refresh();
            }
            finally
            {
                SetEnabled(processor, processorEnabled);
                SetEnabled(packingStation, packingEnabled);
                SetEnabled(lumberWorker, workerEnabled);
                SetEnabled(autoFeeder, feederEnabled);
                SetEnabled(courier, courierEnabled);
                cashPileCollector?.ResumeAfterPersistenceRestore(cashPlayerInside);
                _isApplyingState = false;
            }
        }

        private bool TryBuildStableSnapshot(
            long evaluationTimestamp,
            out M9SaveData snapshot,
            out string failure)
        {
            snapshot = null;
            failure = null;
            if (_state == null
                || wallet == null
                || cashPile == null
                || carryStack == null
                || stockpile == null
                || processor == null
                || packingStation == null)
            {
                failure = "Persistence references are incomplete.";
                return false;
            }

            long normalizedWallet = wallet.Balance;
            long normalizedPile = cashPile.StoredCash;
            if (cashPileCollector != null)
            {
                normalizedPile += cashPileCollector.PendingCash;
            }

            int courierCargo = courier != null ? courier.CarriedCrates : 0;
            // M9 reconciles in-flight Courier cargo into stable Cash ownership so a
            // quit cannot lose Crates. This is save normalization, not a delivery
            // commit: it deliberately emits no M10 trip/delivery/gameplay-Cash metric
            // and must not complete the Lumber Camp.
            normalizedPile += (long)courierCargo
                              * (courier != null ? courier.CashPerCrate : 40);
            if (normalizedPile > int.MaxValue)
            {
                long overflow = normalizedPile - int.MaxValue;
                normalizedPile = int.MaxValue;
                normalizedWallet += overflow;
            }

            if (normalizedWallet > int.MaxValue)
            {
                failure = "Transient Cash ownership exceeds stable Wallet/CashPile capacity.";
                return false;
            }

            long stableStockpile = stockpile.TotalOwnedWood
                                   + (lumberWorker != null && lumberWorker.IsCarrying ? 1L : 0L);
            long stablePackingInput = (long)packingStation.InputPlanks
                                      + packingStation.ProcessingInputPlanks;
            if (stableStockpile > stockpile.Capacity
                || stablePackingInput > packingStation.InputCapacity)
            {
                failure = "Transient resource ownership cannot fit its stable authoritative buffer.";
                return false;
            }

            long now = _clock != null ? _clock.UtcNowUnixSeconds : evaluationTimestamp;
            snapshot = M9SaveData.CreateFresh(now);
            snapshot.walletCash = (int)normalizedWallet;
            snapshot.cashPileStoredCash = (int)normalizedPile;
            snapshot.carry.amount = carryStack.TotalAmount;
            snapshot.carry.resourceType = carryStack.ActiveResourceType ?? ResourceType.Wood;
            SnapshotPurchasePad(
                snapshot,
                0,
                productionUpgrade != null ? productionUpgrade.PurchasePad : null);
            SnapshotPurchasePad(
                snapshot,
                1,
                workerUnlock != null ? workerUnlock.WorkerPurchasePad : null);
            SnapshotPurchasePad(
                snapshot,
                2,
                processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null);
            SnapshotPurchasePad(
                snapshot,
                3,
                autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null);
            SnapshotPurchasePad(
                snapshot,
                4,
                packingStationUnlock != null
                    ? packingStationUnlock.PackingStationPurchasePad
                    : null);
            SnapshotPurchasePad(
                snapshot,
                5,
                courierUnlock != null ? courierUnlock.CourierPurchasePad : null);
            snapshot.lumberCampCompleted = lumberCampCompletion != null
                                            && lumberCampCompletion.IsCompleted;
            snapshot.progression = progressionService != null
                ? progressionService.CapturePersistentState()
                : _state.progression != null
                    ? _state.progression.DeepClone()
                    : M10ProgressionSaveData.CreateFresh();
            if (snapshot.lumberCampCompleted)
            {
                snapshot.progression.SetFlag(ProgressFlagId.LumberCampCompleted);
            }
            snapshot.stockpileWood = (int)stableStockpile;
            snapshot.processorInputWood = processor.InputWood;
            snapshot.processorOutputPlanks = processor.OutputPlanks;
            snapshot.packingInputPlanks = (int)stablePackingInput;
            snapshot.packingOutputCrates = packingStation.OutputCrates;
            snapshot.pendingOfflineCash = _state.pendingOfflineCash;
            snapshot.pendingOfflineAwaySeconds = _state.pendingOfflineAwaySeconds;
            snapshot.returnScreenPending = _state.returnScreenPending;
            snapshot.lastEvaluationUtcUnixSeconds = Math.Max(
                _state.lastEvaluationUtcUnixSeconds,
                evaluationTimestamp);
            snapshot.lastWriteUtcUnixSeconds = Math.Max(
                _state.lastWriteUtcUnixSeconds,
                now);
            return true;
        }

        private bool PersistCurrentState()
        {
            if (_saveStore == null || _state == null || _suppressSaves || _isSaving)
            {
                return false;
            }

            _isSaving = true;
            try
            {
                M9SaveWriteResult result = _saveStore.Save(_state);
                if (!result.IsSuccess)
                {
                    KeepDirtyAfterFailure(result.Diagnostic);
                    return false;
                }

                _state = result.PersistedData;
                _isDirty = false;
                _immediateSaveRequested = false;
                _lastWriteDiagnostic = string.Empty;
                SuccessfulSaveCount++;
                return true;
            }
            finally
            {
                _isSaving = false;
            }
        }

        private M9SaveValidationSettings BuildValidationSettings()
        {
            return new M9SaveValidationSettings
            {
                carryCapacity = carryStack != null ? carryStack.Capacity : 12,
                stockpileCapacity = stockpile != null ? stockpile.Capacity : 30,
                processorInputCapacity = processor != null ? processor.InputCapacity : 24,
                processorOutputCapacity = processor != null ? processor.OutputCapacity : 12,
                packingInputCapacity = packingStation != null ? packingStation.InputCapacity : 24,
                packingOutputCapacity = packingStation != null ? packingStation.OutputCapacity : 12
            };
        }

        private void MarkDirty()
        {
            if (!IsInitialized || _isApplyingState || _suppressSaves)
            {
                return;
            }

            if (_isDirty)
            {
                return;
            }

            _isDirty = true;
            _saveDueAt = Time.realtimeSinceStartupAsDouble + saveDebounceSeconds;
        }

        private void RequestCriticalSave()
        {
            MarkDirty();
            _immediateSaveRequested = true;
        }

        private void KeepDirtyAfterFailure(string diagnostic)
        {
            _isDirty = true;
            _saveDueAt = Time.realtimeSinceStartupAsDouble + saveDebounceSeconds;
            if (!string.IsNullOrEmpty(diagnostic)
                && !string.Equals(_lastWriteDiagnostic, diagnostic, StringComparison.Ordinal))
            {
                _lastWriteDiagnostic = diagnostic;
                Debug.LogError(diagnostic, this);
            }
        }

        private void SubscribeDirtyTracking()
        {
            if (_isSubscribed)
            {
                return;
            }

            if (wallet != null) wallet.BalanceChanged += HandleIntChanged;
            if (cashPile != null) cashPile.StoredCashChanged += HandleIntChanged;
            if (carryStack != null) carryStack.Changed += HandleChanged;
            if (stockpile != null) stockpile.StateChanged += HandleTwoIntsChanged;
            if (processor != null) processor.BufferChanged += HandleThreeIntsChanged;
            if (packingStation != null) packingStation.BufferChanged += HandleFourIntsChanged;
            if (lumberWorker != null) lumberWorker.CargoChanged += HandleBoolChanged;
            if (courier != null) courier.CargoChanged += HandleIntChanged;
            if (progressionService != null)
            {
                progressionService.StateChanged += HandleChanged;
            }
            SubscribePurchasePads(true);
            if (lumberCampCompletion != null)
            {
                lumberCampCompletion.Completed += RequestCriticalSave;
            }

            _isSubscribed = true;
        }

        private void UnsubscribeDirtyTracking()
        {
            if (!_isSubscribed)
            {
                return;
            }

            if (wallet != null) wallet.BalanceChanged -= HandleIntChanged;
            if (cashPile != null) cashPile.StoredCashChanged -= HandleIntChanged;
            if (carryStack != null) carryStack.Changed -= HandleChanged;
            if (stockpile != null) stockpile.StateChanged -= HandleTwoIntsChanged;
            if (processor != null) processor.BufferChanged -= HandleThreeIntsChanged;
            if (packingStation != null) packingStation.BufferChanged -= HandleFourIntsChanged;
            if (lumberWorker != null) lumberWorker.CargoChanged -= HandleBoolChanged;
            if (courier != null) courier.CargoChanged -= HandleIntChanged;
            if (progressionService != null)
            {
                progressionService.StateChanged -= HandleChanged;
            }
            SubscribePurchasePads(false);
            if (lumberCampCompletion != null)
            {
                lumberCampCompletion.Completed -= RequestCriticalSave;
            }

            _isSubscribed = false;
        }

        private void SubscribePurchasePads(bool subscribe)
        {
            SetPurchasePadSubscription(
                productionUpgrade != null ? productionUpgrade.PurchasePad : null,
                subscribe);
            SetPurchasePadSubscription(
                workerUnlock != null ? workerUnlock.WorkerPurchasePad : null,
                subscribe);
            SetPurchasePadSubscription(
                processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null,
                subscribe);
            SetPurchasePadSubscription(
                autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null,
                subscribe);
            SetPurchasePadSubscription(
                packingStationUnlock != null
                    ? packingStationUnlock.PackingStationPurchasePad
                    : null,
                subscribe);
            SetPurchasePadSubscription(
                courierUnlock != null ? courierUnlock.CourierPurchasePad : null,
                subscribe);
        }

        private void SetPurchasePadSubscription(PurchasePad pad, bool subscribe)
        {
            if (pad == null)
            {
                return;
            }

            if (subscribe)
            {
                pad.ProgressChanged += HandleIntChanged;
                pad.Completed += RequestCriticalSave;
            }
            else
            {
                pad.ProgressChanged -= HandleIntChanged;
                pad.Completed -= RequestCriticalSave;
            }
        }

        private void HandleChanged() => MarkDirty();
        private void HandleIntChanged(int value) => MarkDirty();
        private void HandleBoolChanged(bool value) => MarkDirty();
        private void HandleTwoIntsChanged(int first, int second) => MarkDirty();
        private void HandleThreeIntsChanged(int first, int second, int third) => MarkDirty();
        private void HandleFourIntsChanged(int first, int second, int third, int fourth) => MarkDirty();

        private static void SetEnabled(Behaviour behaviour, bool enabled)
        {
            if (behaviour != null && behaviour.enabled != enabled)
            {
                behaviour.enabled = enabled;
            }
        }

        private static bool IsPadCompleted(M9SaveData data, string id)
        {
            return data != null
                   && data.TryGetPurchasePad(id, out M9PurchasePadSaveRecord pad)
                   && pad.completed;
        }

        private static void RestorePurchasePad(
            PurchasePad purchasePad,
            M9SaveData data,
            string id)
        {
            if (purchasePad != null
                && data.TryGetPurchasePad(id, out M9PurchasePadSaveRecord record))
            {
                purchasePad.RestorePaidAmount(record.paidCash, record.completed);
            }
        }

        private static void SnapshotPurchasePad(
            M9SaveData snapshot,
            int index,
            PurchasePad purchasePad)
        {
            M9PurchasePadSaveRecord record = snapshot.purchasePads[index];
            if (purchasePad == null)
            {
                return;
            }

            record.paidCash = Mathf.Clamp(
                purchasePad.TotalCost - purchasePad.RemainingCost,
                0,
                purchasePad.TotalCost);
            record.completed = purchasePad.IsCompleted;
        }

        private static bool CanUseDevelopmentUtilities()
        {
            return Application.isEditor || Debug.isDebugBuild;
        }

        private void OnValidate()
        {
            saveDebounceSeconds = Mathf.Max(0.25f, saveDebounceSeconds);
            returnScreenThresholdSeconds = Mathf.Max(0f, returnScreenThresholdSeconds);
            maximumCreditedAwaySeconds = Mathf.Max(1f, maximumCreditedAwaySeconds);
            offlineEfficiency = Mathf.Clamp01(offlineEfficiency);
            workerSecondsPerWood = Mathf.Max(0.1f, workerSecondsPerWood);
        }
    }
}
