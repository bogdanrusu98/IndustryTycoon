using System;
using System.IO;
using IndustryTycoon.Core;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.UI;
using IndustryTycoon.Workers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Two-session Play Mode smoke for the actual M9 scene composition. It verifies
    /// that an on-disk save survives a real Play Mode teardown/reload, that transient
    /// ownership is normalized, and that the pending return reward is collected once.
    /// </summary>
    [InitializeOnLoad]
    public static class LumberCampM9PersistencePlayModeSmokeTest
    {
        private const string ScenePath = M9EditorSaveUtility.PrototypeScenePath;
        private const string KeyPrefix = "IndustryTycoon.M9.PersistenceLifecycle.";
        private const string RunningKey = KeyPrefix + "Running";
        private const string CommandLineKey = KeyPrefix + "CommandLine";
        private const string RestartPendingKey = KeyPrefix + "RestartPending";
        private const string FinishPendingKey = KeyPrefix + "FinishPending";
        private const string SuccessKey = KeyPrefix + "Success";
        private const string ResultMessageKey = KeyPrefix + "ResultMessage";
        private const string SessionNumberKey = KeyPrefix + "SessionNumber";
        private const string ErrorCountKey = KeyPrefix + "ErrorCount";
        private const string ExpectedWalletKey = KeyPrefix + "ExpectedWallet";
        private const string ExpectedPileKey = KeyPrefix + "ExpectedPile";
        private const string ExpectedCarryKey = KeyPrefix + "ExpectedCarry";
        private const string ExpectedStockpileKey = KeyPrefix + "ExpectedStockpile";
        private const string ExpectedProcessorInputKey = KeyPrefix + "ExpectedProcessorInput";
        private const string ExpectedProcessorOutputKey = KeyPrefix + "ExpectedProcessorOutput";
        private const string ExpectedPackingInputKey = KeyPrefix + "ExpectedPackingInput";
        private const string ExpectedPackingOutputKey = KeyPrefix + "ExpectedPackingOutput";
        private const string ExpectedPendingCashKey = KeyPrefix + "ExpectedPendingCash";
        private const string ExpectedPendingAwayKey = KeyPrefix + "ExpectedPendingAway";

        private const int SeedWalletCash = 777;
        private const int SeedPileCash = 123;
        private const int SeedCarryCrates = 3;
        private const int SeedStockpileWood = 8;
        private const int SeedProcessorInputWood = 4;
        private const int SeedProcessorOutputPlanks = 2;
        private const int SeedPackingInputPlanks = 6;
        private const int SeedPackingOutputCrates = 2;
        private const long SimulatedAwaySeconds = 10L * 60L;

        private enum Stage
        {
            FirstSessionWarmup,
            FirstSessionSeedAndNormalize,
            FirstSessionOfflineSettlement,
            SecondSessionWarmup,
            SecondSessionVerifyLoad,
            SecondSessionCollect
        }

        private static LocalPersistenceService _service;
        private static WelcomeBackView _welcomeBackView;
        private static LumberCampProgressionService _progression;
        private static AchievementToastView _achievementToast;
        private static ResourceCollector _resourceCollector;
        private static CashPileCollector _cashPileCollector;
        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;

        static LumberCampM9PersistencePlayModeSmokeTest()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            Application.logMessageReceived -= HandleLogMessage;
            Application.logMessageReceived += HandleLogMessage;

            if (SessionState.GetBool(RunningKey, false))
            {
                EditorApplication.update -= UpdateSmokeTest;
                EditorApplication.update += UpdateSmokeTest;
            }

            if (SessionState.GetBool(RestartPendingKey, false))
            {
                EditorApplication.delayCall += EnterSecondPlayModeSession;
            }
            else if (SessionState.GetBool(FinishPendingKey, false))
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
            }
        }

        [MenuItem("Industry Tycoon/Prototype/Run M9 Persistence Lifecycle Smoke Test")]
        private static void RunFromMenu()
        {
            StartSmokeTest(false);
        }

        public static void RunFromCommandLine()
        {
            StartSmokeTest(true);
        }

        private static void StartSmokeTest(bool commandLine)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Exit Play Mode before starting the M9 persistence lifecycle smoke test.");
            }

            if (!commandLine && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new FileNotFoundException(
                    "The Lumber Camp prototype scene is missing.",
                    ScenePath);
            }

            M9EditorSaveUtility.PrepareFreshSmokeTest();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(CommandLineKey, commandLine);
            SessionState.SetBool(RestartPendingKey, false);
            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
            SessionState.SetInt(SessionNumberKey, 1);
            SessionState.SetInt(ErrorCountKey, 0);
            _runtimeInitialized = false;
            EditorApplication.update -= UpdateSmokeTest;
            EditorApplication.update += UpdateSmokeTest;
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode
                && SessionState.GetBool(RunningKey, false))
            {
                try
                {
                    InitializeRuntimeState();
                }
                catch (Exception exception)
                {
                    Fail(exception.Message);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode
                     && SessionState.GetBool(RestartPendingKey, false))
            {
                EditorApplication.delayCall += EnterSecondPlayModeSession;
            }
            else if (state == PlayModeStateChange.EnteredEditMode
                     && SessionState.GetBool(FinishPendingKey, false))
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
            }
        }

        private static void UpdateSmokeTest()
        {
            if (!SessionState.GetBool(RunningKey, false))
            {
                EditorApplication.update -= UpdateSmokeTest;
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            try
            {
                if (!_runtimeInitialized)
                {
                    InitializeRuntimeState();
                }

                if (Now - _runStartedAt > 30d)
                {
                    throw new InvalidOperationException(
                        "M9 persistence lifecycle smoke exceeded its 30-second session timeout.");
                }

                TickCurrentStage();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void InitializeRuntimeState()
        {
            if (_runtimeInitialized || !EditorApplication.isPlaying)
            {
                return;
            }

            _service = FindSingleIncludingInactive<LocalPersistenceService>();
            _welcomeBackView = FindSingleIncludingInactive<WelcomeBackView>();
            _progression = FindSingleIncludingInactive<LumberCampProgressionService>();
            _achievementToast = FindSingleIncludingInactive<AchievementToastView>();
            Require(_service != null,
                "M9 lifecycle smoke could not find the LocalPersistenceService.");
            Require(_welcomeBackView != null,
                "M9 lifecycle smoke could not find the Welcome Back view.");
            Require(_progression != null,
                "M10 lifecycle smoke could not find the progression service.");
            Require(_achievementToast != null,
                "M10 lifecycle smoke could not find the achievement toast.");

            // This regression seeds M9 state through restore-only APIs. Keep M10's
            // gameplay subscriptions disabled so those test fixtures cannot masquerade
            // as real authoritative commits; the M10 assertions below verify persistence
            // and offline/load silence independently.
            _progression.enabled = false;

            _resourceCollector = _service.CarryStack != null
                ? _service.CarryStack.GetComponent<ResourceCollector>()
                : null;
            _cashPileCollector = FindSingleIncludingInactive<CashPileCollector>();
            if (_resourceCollector != null)
            {
                _resourceCollector.enabled = false;
            }

            if (_cashPileCollector != null)
            {
                _cashPileCollector.enabled = false;
            }

            // Freeze automation before LocalPersistenceService.Start where possible.
            // This makes the assertions independent from frame cadence while leaving
            // the real load/reconstruction path intact.
            FreezeAutomation();

            int sessionNumber = SessionState.GetInt(SessionNumberKey, 1);
            _stage = sessionNumber == 1
                ? Stage.FirstSessionWarmup
                : Stage.SecondSessionWarmup;
            _stageStartedAt = Now;
            _runStartedAt = Now;
            _runtimeInitialized = true;
        }

        private static void TickCurrentStage()
        {
            switch (_stage)
            {
                case Stage.FirstSessionWarmup:
                    TickFirstSessionWarmup();
                    break;
                case Stage.FirstSessionSeedAndNormalize:
                    TickFirstSessionSeedAndNormalize();
                    break;
                case Stage.FirstSessionOfflineSettlement:
                    TickFirstSessionOfflineSettlement();
                    break;
                case Stage.SecondSessionWarmup:
                    TickSecondSessionWarmup();
                    break;
                case Stage.SecondSessionVerifyLoad:
                    TickSecondSessionVerifyLoad();
                    break;
                case Stage.SecondSessionCollect:
                    TickSecondSessionCollect();
                    break;
            }
        }

        private static void TickFirstSessionWarmup()
        {
            EnsureStageTimeout(5d);
            if (!_service.IsInitialized || !HasWaited(0.15d))
            {
                return;
            }

            Require(_service.LastLoadStatus == M9SaveLoadStatus.FreshNoSave,
                $"Fresh session loaded with unexpected status {_service.LastLoadStatus}.");
            Require(_service.Wallet.Balance == 0
                    && _service.CashPile.StoredCash == 0
                    && _service.CarryStack.TotalAmount == 0,
                "Fresh M8 economy did not begin at zero.");
            Require(_service.Stockpile.StoredWood == 0
                    && _service.Processor.InputWood == 0
                    && _service.Processor.OutputPlanks == 0
                    && _service.PackingStation.InputPlanks == 0
                    && _service.PackingStation.OutputCrates == 0,
                "Fresh M8 production buffers did not begin empty.");
            Require(!_service.HasPendingReturn
                    && _service.PendingOfflineCash == 0
                    && _service.PendingAwaySeconds == 0,
                "Fresh M8 state unexpectedly contained a pending offline settlement.");
            Require(!_service.LumberCampCompletion.IsCompleted
                    && !_service.LumberCampCompletion.MineTeaserRoot.activeSelf,
                "Fresh M8 completion/Mine teaser state was not reset.");
            AssertNoFakeM10Metrics();
            Require(_progression.ObjectiveIndex == 0
                    && _progression.ActiveContractIndex == 0
                    && _progression.ActiveContractState == ContractProgressState.Active
                    && _achievementToast.PresentationCount == 0,
                "Fresh M10 objective, contract, or toast state was not reset.");

            PurchasePad[] pads = GetPurchasePads();
            for (int i = 0; i < pads.Length; i++)
            {
                Require(!pads[i].IsCompleted
                        && pads[i].RemainingCost == pads[i].TotalCost,
                    $"Fresh PurchasePad {i} retained progress.");
            }

            Require(pads[0].IsAvailable
                    && !_service.WorkerUnlock.IsPadUnlocked
                    && !_service.ProcessorUnlock.IsPadUnlocked
                    && !_service.AutoFeederUnlock.IsPadUnlocked
                    && !_service.PackingStationUnlock.IsPadUnlocked
                    && !_service.CourierUnlock.IsPadUnlocked,
                "Fresh M8 unlock chain did not begin at the production upgrade.");
            AdvanceTo(Stage.FirstSessionSeedAndNormalize);
        }

        private static void TickFirstSessionSeedAndNormalize()
        {
            EnsureStageTimeout(5d);
            SeedCompletedFactoryState();
            // This M9 conservation fixture starts from an already-completed factory.
            // Mark the exact flag-based M10 rewards as previously handled so the
            // settlement can continue asserting that pending Courier cash is the
            // only economy delta. The focused M10 smoke covers first completion
            // during offline settlement and its legitimate achievement reward.
            PrepareExactM10StateForReload();

            // Exercise an actual feeder transfer whose Wood remains owned by the
            // Stockpile until commit. The serialized snapshot must retain it exactly
            // once at the source and omit both source/destination reservations.
            Require(_service.Processor.RestoreStableState(
                        SeedProcessorInputWood,
                        _service.Processor.OutputCapacity),
                "Could not prepare the Processor for feeder reconciliation.");
            _service.Processor.enabled = true;
            _service.AutoFeeder.enabled = true;
            Require(_service.AutoFeeder.IsTransferInFlight
                    || _service.AutoFeeder.TryStartTransfer(),
                "Could not start a deterministic feeder transfer for snapshot reconciliation.");
            Require(_service.AutoFeeder.IsTransferInFlight
                    && _service.Stockpile.OutgoingReservations == 1
                    && _service.Processor.ReservedInputCapacity == 1,
                "Feeder did not establish the expected transient ownership pair.");
            Require(_service.SaveNow(),
                "Persistence service rejected the in-flight feeder snapshot.");

            M9SaveData feederSnapshot = LoadPrimarySave();
            Require(feederSnapshot.stockpileWood == SeedStockpileWood
                    && feederSnapshot.processorInputWood == SeedProcessorInputWood
                    && feederSnapshot.processorOutputPlanks
                       == _service.Processor.OutputCapacity,
                "In-flight feeder snapshot lost or duplicated Wood ownership.");
            AssertNoTransientFieldsInJson();

            _service.AutoFeeder.enabled = false;
            _service.Processor.enabled = false;
            Require(_service.Stockpile.OutgoingReservations == 0
                    && _service.Processor.ReservedInputCapacity == 0
                    && !_service.AutoFeeder.IsTransferInFlight
                    && _service.Stockpile.StoredWood == SeedStockpileWood,
                "Feeder cancellation did not return to stable source ownership.");

            // Exercise both machine recipe ownership and a Courier output claim.
            Require(_service.Processor.RestoreStableState(
                        SeedProcessorInputWood,
                        3),
                "Could not restore Processor buffers for recipe snapshot.");
            Require(_service.PackingStation.RestoreStableState(
                        SeedPackingInputPlanks,
                        SeedPackingOutputCrates),
                "Could not restore Packing buffers for recipe snapshot.");
            _service.Processor.enabled = true;
            _service.PackingStation.enabled = true;
            _service.Courier.enabled = true;
            Require(_service.Processor.IsProcessing
                    && _service.Processor.ReservedOutputCapacity == 1,
                "Processor did not establish an in-progress recipe.");
            Require(_service.PackingStation.IsProcessing
                    && _service.PackingStation.ProcessingInputPlanks == 2,
                "Packing Station did not establish stable in-progress recipe ownership.");
            Require(_service.Courier.HasActiveReservation
                    && _service.Courier.ReservedCrates == SeedPackingOutputCrates,
                "Courier did not establish its expected uncommitted output claim.");
            Require(_service.Stockpile.TryReserveOutgoing(out WoodStockpileOutgoingReservation source),
                "Could not create a Worker-like outgoing Wood reservation.");
            Require(_service.Processor.TryReserveInput(out _),
                "Could not create a matching transient Processor capacity claim.");

            Require(_service.SaveNow(),
                "Persistence service rejected the active machine/Courier snapshot.");
            M9SaveData recipeSnapshot = LoadPrimarySave();
            Require(recipeSnapshot.stockpileWood == SeedStockpileWood
                    && recipeSnapshot.processorInputWood == SeedProcessorInputWood
                    && recipeSnapshot.processorOutputPlanks == 3
                    && recipeSnapshot.packingInputPlanks == SeedPackingInputPlanks
                    && recipeSnapshot.packingOutputCrates == SeedPackingOutputCrates,
                "Active recipe/Courier snapshot did not conserve stable resources.");
            AssertNoTransientFieldsInJson();

            _service.Courier.enabled = false;
            _service.PackingStation.enabled = false;
            _service.Processor.enabled = false;
            Require(_service.Stockpile.ReleaseOutgoing(source),
                "Could not reconcile the Worker-like outgoing reservation to its source.");
            Require(_service.Stockpile.IncomingReservations == 0
                    && _service.Stockpile.OutgoingReservations == 0
                    && _service.Stockpile.StoredWood == SeedStockpileWood
                    && _service.Processor.ReservedInputCapacity == 0
                    && _service.Processor.ReservedOutputCapacity == 0
                    && _service.PackingStation.ProcessingInputPlanks == 0
                    && _service.PackingStation.ReservedOutputCapacity == 0
                    && _service.PackingStation.ReservedCourierOutputCrates == 0
                    && _service.Courier.ReservedCrates == 0
                    && _service.Courier.CarriedCrates == 0,
                "Transient reservations did not return to clean stable ownership.");

            // Restore the final deterministic state used by the offline settlement.
            Require(_service.Stockpile.RestoreStableState(SeedStockpileWood)
                    && _service.Processor.RestoreStableState(
                        SeedProcessorInputWood,
                        SeedProcessorOutputPlanks)
                    && _service.PackingStation.RestoreStableState(
                        SeedPackingInputPlanks,
                        SeedPackingOutputCrates),
                "Could not restore the final pre-away production buffers.");
            FreezeAutomation();
            Require(_service.SaveNow(),
                "Could not persist the deterministic pre-away state.");
            AdvanceTo(Stage.FirstSessionOfflineSettlement);
        }

        private static void TickFirstSessionOfflineSettlement()
        {
            EnsureStageTimeout(5d);
            Require(_service.SimulateAwayForDevelopment(SimulatedAwaySeconds),
                "Development time injection could not evaluate the 10-minute interval.");
            FreezeAutomation();

            OfflineProgressionResult result = _service.LastOfflineResult;
            Require(result != null
                    && result.ObservedAwaySeconds == SimulatedAwaySeconds
                    && result.CreditedAwaySeconds == SimulatedAwaySeconds
                    && Math.Abs(result.EffectiveAutomationSeconds - 360d) < 0.001d,
                "Offline interval/60% efficiency was not evaluated deterministically.");
            Require(result.WorkerWoodCollected == 22
                    && result.FeederWoodTransferred == 20
                    && result.ProcessorRecipesCompleted == 10
                    && result.ProcessorPlanksProduced == 10,
                "Worker/Feeder/Processor aggregate settlement did not match scene rates/capacities.");
            Require(result.PackingRecipesCompleted == 3
                    && result.PackingCratesProduced == 3
                    && result.CourierCratesDelivered == 5
                    && result.OfflineCashEarned == 5 * 40,
                "Packing/Courier aggregate settlement did not preserve legitimate Crate ancestry.");
            Require(_service.Wallet.Balance == SeedWalletCash
                    && _service.CashPile.StoredCash == SeedPileCash,
                "Offline Courier cash was credited directly instead of remaining pending.");
            Require(_service.HasPendingReturn
                    && _service.PendingOfflineCash == 200
                    && _service.PendingAwaySeconds == SimulatedAwaySeconds,
                "Meaningful away interval did not create the expected pending return reward.");
            Require(_service.Stockpile.StoredWood == 10
                    && _service.Processor.InputWood == 4
                    && _service.Processor.OutputPlanks == 12
                    && _service.PackingStation.InputPlanks == 0
                    && _service.PackingStation.OutputCrates == 0,
                "Offline settlement ended with unexpected authoritative buffers.");
            Require(_service.CarryStack.GetAmount(ResourceType.Crate) == SeedCarryCrates,
                "Offline settlement modified the player's persisted CarryStack.");
            Require(_service.LumberCampCompletion.IsCompleted,
                "Lumber Camp completion was not retained through offline settlement.");
            AssertNoFakeM10Metrics();

            _welcomeBackView.Refresh();
            Require(_welcomeBackView.IsVisible
                    && _welcomeBackView.AwayText.text == "Away: 10m"
                    && _welcomeBackView.EarnedText.text == "Earned: $200",
                "Welcome Back presentation did not show the pending settlement once.");

            PrepareExactM10StateForReload();
            Require(_service.SaveNow(),
                "Pending offline return could not be flushed before Play Mode teardown.");
            M9SaveData persisted = LoadPrimarySave();
            Require(persisted.pendingOfflineCash == _service.PendingOfflineCash
                    && persisted.pendingOfflineAwaySeconds == _service.PendingAwaySeconds
                    && persisted.returnScreenPending,
                "Pending return was not immediately persisted after evaluation.");

            RecordExpectedLoadedState();
            BeginSecondSession();
        }

        private static void TickSecondSessionWarmup()
        {
            EnsureStageTimeout(5d);
            if (!_service.IsInitialized || !HasWaited(0.15d))
            {
                return;
            }

            FreezeAutomation();
            AdvanceTo(Stage.SecondSessionVerifyLoad);
        }

        private static void TickSecondSessionVerifyLoad()
        {
            EnsureStageTimeout(5d);
            Require(_service.LastLoadStatus == M9SaveLoadStatus.LoadedPrimary,
                $"Second session did not load the primary save: {_service.LastLoadStatus}.");
            Require(_service.Wallet.Balance == Expected(ExpectedWalletKey)
                    && _service.CashPile.StoredCash == Expected(ExpectedPileKey)
                    && _service.CarryStack.GetAmount(ResourceType.Crate)
                       == Expected(ExpectedCarryKey),
                "Wallet, CashPile, or typed CarryStack failed the real session round-trip.");
            Require(_service.Stockpile.StoredWood == Expected(ExpectedStockpileKey)
                    && _service.Processor.InputWood == Expected(ExpectedProcessorInputKey)
                    && _service.Processor.OutputPlanks == Expected(ExpectedProcessorOutputKey)
                    && _service.PackingStation.InputPlanks == Expected(ExpectedPackingInputKey)
                    && _service.PackingStation.OutputCrates == Expected(ExpectedPackingOutputKey),
                "Production buffers changed across the real Play Mode reload.");

            PurchasePad[] pads = GetPurchasePads();
            for (int i = 0; i < pads.Length; i++)
            {
                Require(pads[i].IsCompleted
                        && pads[i].RemainingCost == 0
                        && !pads[i].IsAvailable,
                    $"Completed PurchasePad {i} was not reconstructed exactly.");
            }

            Require(_service.ProductionUpgrade.IsApplied
                    && _service.WorkerUnlock.IsWorkerActivated
                    && _service.ProcessorUnlock.IsProcessorActivated
                    && _service.AutoFeederUnlock.IsAutoFeederActivated
                    && _service.PackingStationUnlock.IsPackingStationActivated
                    && _service.CourierUnlock.IsCourierActivated,
                "Derived unlock chain was not reconstructed from canonical PurchasePads.");
            Require(_service.LumberCampCompletion.IsCompleted
                    && _service.LumberCampCompletion.CompletionCount == 1
                    && _service.LumberCampCompletion.MineTeaserRoot.activeSelf,
                "Lumber Camp completion/Mine teaser was not reconstructed.");
            AssertNoFakeM10Metrics();
            Require(_progression.GetFlag(ProgressFlagId.WorkerUnlocked)
                    && _progression.GetFlag(ProgressFlagId.ProcessorUnlocked)
                    && _progression.GetFlag(ProgressFlagId.AutoFeederUnlocked)
                    && _progression.GetFlag(ProgressFlagId.PackingStationUnlocked)
                    && _progression.GetFlag(ProgressFlagId.CourierUnlocked)
                    && _progression.GetFlag(ProgressFlagId.LumberCampCompleted),
                "M10 exact unlock/completion flags failed the real session round-trip.");
            Require(_achievementToast.PresentationCount == 0,
                "Save restore retriggered an M10 achievement toast.");

            int pendingCash = Expected(ExpectedPendingCashKey);
            Require(_service.HasPendingReturn
                    && _service.PendingOfflineCash == pendingCash
                    && _service.PendingAwaySeconds == Expected(ExpectedPendingAwayKey),
                "Pending reward changed or duplicated during the second load interval.");
            Require(_service.LastOfflineResult != null
                    && _service.LastOfflineResult.SkippedBecauseReturnPending
                    && _service.LastOfflineResult.OfflineCashEarned == 0,
                "Outstanding pending return was evaluated a second time.");

            Require(_service.Stockpile.IncomingReservations == 0
                    && _service.Stockpile.OutgoingReservations == 0
                    && _service.Processor.ReservedInputCapacity == 0
                    && _service.Processor.ReservedOutputCapacity == 0
                    && !_service.Processor.IsProcessing
                    && _service.PackingStation.ProcessingInputPlanks == 0
                    && _service.PackingStation.ReservedOutputCapacity == 0
                    && _service.PackingStation.ReservedCourierOutputCrates == 0
                    && !_service.PackingStation.IsProcessing
                    && !_service.AutoFeeder.IsTransferInFlight
                    && !_service.LumberWorker.HasValidTarget
                    && !_service.LumberWorker.HasIncomingReservation
                    && !_service.LumberWorker.IsCarrying
                    && !_service.Courier.HasActiveReservation
                    && _service.Courier.ReservedCrates == 0
                    && _service.Courier.CarriedCrates == 0,
                "Load retained transient claims, reservations, recipe ownership, or cargo.");

            _welcomeBackView.Refresh();
            Require(_welcomeBackView.IsVisible,
                "Persisted pending return did not reopen the single Welcome Back view.");
            AdvanceTo(Stage.SecondSessionCollect);
        }

        private static void TickSecondSessionCollect()
        {
            EnsureStageTimeout(5d);
            int walletBefore = _service.Wallet.Balance;
            int pendingCash = _service.PendingOfflineCash;
            Require(_service.TryCollectOfflineReward(1f),
                "First 1x offline reward collection was rejected.");
            Require(_service.Wallet.Balance == walletBefore + pendingCash
                    && !_service.HasPendingReturn
                    && _service.PendingOfflineCash == 0
                    && _service.PendingAwaySeconds == 0,
                "COLLECT did not transfer and clear the pending reward exactly once.");

            int walletAfterFirstCollection = _service.Wallet.Balance;
            Require(!_service.TryCollectOfflineReward(1f)
                    && _service.Wallet.Balance == walletAfterFirstCollection,
                "A second COLLECT duplicated the offline reward.");
            AssertNoFakeM10Metrics();
            _welcomeBackView.Refresh();
            Require(!_welcomeBackView.IsVisible,
                "Welcome Back view remained visible after successful collection.");

            M9SaveData collectedSave = LoadPrimarySave();
            Require(collectedSave.walletCash == walletAfterFirstCollection
                    && collectedSave.pendingOfflineCash == 0
                    && collectedSave.pendingOfflineAwaySeconds == 0
                    && !collectedSave.returnScreenPending,
                "Collected reward was not persisted atomically with the cleared pending state.");
            Require(SessionState.GetInt(ErrorCountKey, 0) == 0,
                "M9 lifecycle smoke observed one or more Console errors/assertions.");

            Pass(
                "M9/M10 persistence lifecycle Play Mode smoke passed: fresh state, "
                + "transient conservation, two-session disk reload, deterministic 10-minute "
                + "offline settlement with zero fake metrics, silent M10 restore, pending "
                + "survival, and exactly-once COLLECT.");
        }

        private static void PrepareExactM10StateForReload()
        {
            M10ProgressionSaveData state = _progression.CapturePersistentState();
            const int m10FlagCount = 7;
            for (int i = 0; i < m10FlagCount; i++)
            {
                state.flags[i].value = true;
            }

            MarkAchievementHandled(state, LumberCampAchievementId.FirstHire);
            MarkAchievementHandled(state, LumberCampAchievementId.ProcessingBegins);
            MarkAchievementHandled(state, LumberCampAchievementId.AutomationOnline);
            MarkAchievementHandled(state, LumberCampAchievementId.DeliveryService);
            MarkAchievementHandled(state, LumberCampAchievementId.FullyAutomatedInput);
            MarkAchievementHandled(state, LumberCampAchievementId.LumberCampComplete);
            _progression.RestorePersistentState(state);
        }

        private static void MarkAchievementHandled(
            M10ProgressionSaveData state,
            LumberCampAchievementId achievement)
        {
            M10AchievementSaveRecord record = state.FindAchievementRecord(
                (int)achievement);
            Require(record != null,
                $"M10 test fixture could not find achievement {achievement}.");
            record.unlocked = true;
            record.rewarded = true;
        }

        private static void AssertNoFakeM10Metrics()
        {
            for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
            {
                ProgressMetricId metric = (ProgressMetricId)i;
                long expected = metric == ProgressMetricId.MineUnlocked
                                && _service.LumberCampCompletion.IsCompleted
                    ? 1L
                    : 0L;
                Require(_progression.GetMetric(metric) == expected,
                    $"Restore/offline flow fabricated M10 metric {metric}.");
            }
        }

        private static void SeedCompletedFactoryState()
        {
            FreezeAutomation();
            Require(_service.Wallet.RestoreBalance(SeedWalletCash)
                    && _service.CashPile.RestoreStoredCash(SeedPileCash)
                    && _service.CarryStack.RestoreStableState(
                        ResourceType.Crate,
                        SeedCarryCrates),
                "Could not seed the authoritative economy/CarryStack.");

            PurchasePad[] pads = GetPurchasePads();
            for (int i = 0; i < pads.Length; i++)
            {
                Require(pads[i].RestorePaidAmount(pads[i].TotalCost, true),
                    $"Could not complete PurchasePad {i} for the persisted unlock chain.");
            }

            _service.ProductionUpgrade.SynchronizeFromPurchaseState();
            _service.WorkerUnlock.SynchronizeFromPurchaseState();
            _service.ProcessorUnlock.SynchronizeFromPurchaseState();
            _service.AutoFeederUnlock.SynchronizeFromPurchaseState();
            _service.PackingStationUnlock.SynchronizeFromPurchaseState();
            _service.CourierUnlock.SynchronizeFromPurchaseState();
            FreezeAutomation();
            _service.LumberCampCompletion.RestoreCompleted(true);

            Require(_service.Stockpile.RestoreStableState(SeedStockpileWood)
                    && _service.Processor.RestoreStableState(
                        SeedProcessorInputWood,
                        SeedProcessorOutputPlanks)
                    && _service.PackingStation.RestoreStableState(
                        SeedPackingInputPlanks,
                        SeedPackingOutputCrates),
                "Could not seed authoritative factory buffers.");
        }

        private static PurchasePad[] GetPurchasePads()
        {
            var pads = new[]
            {
                _service.ProductionUpgrade != null
                    ? _service.ProductionUpgrade.PurchasePad
                    : null,
                _service.WorkerUnlock != null
                    ? _service.WorkerUnlock.WorkerPurchasePad
                    : null,
                _service.ProcessorUnlock != null
                    ? _service.ProcessorUnlock.ProcessorPurchasePad
                    : null,
                _service.AutoFeederUnlock != null
                    ? _service.AutoFeederUnlock.AutoFeederPurchasePad
                    : null,
                _service.PackingStationUnlock != null
                    ? _service.PackingStationUnlock.PackingStationPurchasePad
                    : null,
                _service.CourierUnlock != null
                    ? _service.CourierUnlock.CourierPurchasePad
                    : null
            };

            for (int i = 0; i < pads.Length; i++)
            {
                Require(pads[i] != null,
                    $"Persistence service is missing PurchasePad reference {i}.");
            }

            return pads;
        }

        private static void FreezeAutomation()
        {
            if (_service == null)
            {
                return;
            }

            SetEnabled(_service.LumberWorker, false);
            SetEnabled(_service.AutoFeeder, false);
            SetEnabled(_service.Courier, false);
            SetEnabled(_service.Processor, false);
            SetEnabled(_service.PackingStation, false);
        }

        private static void SetEnabled(Behaviour behaviour, bool enabled)
        {
            if (behaviour != null && behaviour.enabled != enabled)
            {
                behaviour.enabled = enabled;
            }
        }

        private static M9SaveData LoadPrimarySave()
        {
            var settings = new M9SaveValidationSettings
            {
                carryCapacity = _service.CarryStack.Capacity,
                stockpileCapacity = _service.Stockpile.Capacity,
                processorInputCapacity = _service.Processor.InputCapacity,
                processorOutputCapacity = _service.Processor.OutputCapacity,
                packingInputCapacity = _service.PackingStation.InputCapacity,
                packingOutputCapacity = _service.PackingStation.OutputCapacity
            };
            M9LocalSaveStore store = M9LocalSaveStore.CreateForPersistentDataPath(
                SystemUtcClock.Instance,
                settings,
                false);
            M9SaveLoadResult load = store.Load();
            Require(load.Status == M9SaveLoadStatus.LoadedPrimary && load.Data != null,
                $"Could not read the primary M9 save after flush: {load.Status} "
                + load.Diagnostic);
            return load.Data;
        }

        private static void AssertNoTransientFieldsInJson()
        {
            Require(!string.IsNullOrEmpty(_service.SavePath)
                    && File.Exists(_service.SavePath),
                "Persistence service did not expose a written primary save path.");
            string json = File.ReadAllText(_service.SavePath);
            Require(json.IndexOf("reservation", StringComparison.OrdinalIgnoreCase) < 0
                    && json.IndexOf("processingInput", StringComparison.OrdinalIgnoreCase) < 0
                    && json.IndexOf("carriedCrates", StringComparison.OrdinalIgnoreCase) < 0
                    && json.IndexOf("workerTarget", StringComparison.OrdinalIgnoreCase) < 0,
                "Serialized JSON contains transient claims, reservations, recipe state, or cargo.");
        }

        private static void RecordExpectedLoadedState()
        {
            SessionState.SetInt(ExpectedWalletKey, _service.Wallet.Balance);
            SessionState.SetInt(ExpectedPileKey, _service.CashPile.StoredCash);
            SessionState.SetInt(ExpectedCarryKey, _service.CarryStack.TotalAmount);
            SessionState.SetInt(ExpectedStockpileKey, _service.Stockpile.StoredWood);
            SessionState.SetInt(ExpectedProcessorInputKey, _service.Processor.InputWood);
            SessionState.SetInt(ExpectedProcessorOutputKey, _service.Processor.OutputPlanks);
            SessionState.SetInt(ExpectedPackingInputKey, _service.PackingStation.InputPlanks);
            SessionState.SetInt(ExpectedPackingOutputKey, _service.PackingStation.OutputCrates);
            SessionState.SetInt(ExpectedPendingCashKey, _service.PendingOfflineCash);
            SessionState.SetInt(ExpectedPendingAwayKey, checked((int)_service.PendingAwaySeconds));
        }

        private static int Expected(string key)
        {
            return SessionState.GetInt(key, int.MinValue);
        }

        private static void BeginSecondSession()
        {
            SessionState.SetInt(SessionNumberKey, 2);
            SessionState.SetBool(RestartPendingKey, true);
            _runtimeInitialized = false;
            EditorApplication.update -= UpdateSmokeTest;
            EditorApplication.ExitPlaymode();
        }

        private static void EnterSecondPlayModeSession()
        {
            if (!SessionState.GetBool(RunningKey, false)
                || !SessionState.GetBool(RestartPendingKey, false))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += EnterSecondPlayModeSession;
                return;
            }

            SessionState.SetBool(RestartPendingKey, false);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            _runtimeInitialized = false;
            EditorApplication.update -= UpdateSmokeTest;
            EditorApplication.update += UpdateSmokeTest;
            EditorApplication.EnterPlaymode();
        }

        private static void AdvanceTo(Stage nextStage)
        {
            _stage = nextStage;
            _stageStartedAt = Now;
        }

        private static bool HasWaited(double seconds)
        {
            return Now - _stageStartedAt >= seconds;
        }

        private static void EnsureStageTimeout(double seconds)
        {
            if (Now - _stageStartedAt > seconds)
            {
                throw new InvalidOperationException(
                    $"M9 persistence lifecycle smoke timed out in stage {_stage}.");
            }
        }

        private static double Now => Time.realtimeSinceStartupAsDouble;

        private static T FindSingleIncludingInactive<T>() where T : Object
        {
            T[] candidates = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            Require(candidates.Length == 1,
                $"Expected exactly one {typeof(T).Name}, found {candidates.Length}.");
            return candidates[0];
        }

        private static void HandleLogMessage(
            string condition,
            string stackTrace,
            LogType type)
        {
            if (!SessionState.GetBool(RunningKey, false)
                || (type != LogType.Error
                    && type != LogType.Exception
                    && type != LogType.Assert))
            {
                return;
            }

            SessionState.SetInt(
                ErrorCountKey,
                SessionState.GetInt(ErrorCountKey, 0) + 1);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Pass(string message)
        {
            Debug.Log(message);
            EndRun(true, message);
        }

        private static void Fail(string message)
        {
            string result = "M9 persistence lifecycle Play Mode smoke failed: " + message;
            Debug.LogError(result);
            EndRun(false, result);
        }

        private static void EndRun(bool success, string message)
        {
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(RestartPendingKey, false);
            SessionState.SetBool(FinishPendingKey, true);
            SessionState.SetBool(SuccessKey, success);
            SessionState.SetString(ResultMessageKey, message ?? string.Empty);
            EditorApplication.update -= UpdateSmokeTest;
            _runtimeInitialized = false;

            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
            }
        }

        private static void CompleteAfterPlayMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
                return;
            }

            if (!SessionState.GetBool(FinishPendingKey, false))
            {
                return;
            }

            bool commandLine = SessionState.GetBool(CommandLineKey, false);
            bool success = SessionState.GetBool(SuccessKey, false);
            string message = SessionState.GetString(ResultMessageKey, string.Empty);
            try
            {
                // Exiting Play Mode invokes the service's lifecycle save. Delete only
                // after teardown so this regression leaves no progression behind.
                M9EditorSaveUtility.PrepareFreshSmokeTest();
            }
            catch (Exception exception)
            {
                success = false;
                message = "M9 lifecycle smoke could not clean its isolated save: "
                          + exception.Message;
                Debug.LogError(message);
            }

            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(CommandLineKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);

            if (commandLine)
            {
                if (!success && !string.IsNullOrEmpty(message))
                {
                    Debug.LogError(message);
                }

                EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}
