using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
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
    /// Focused M10 Play Mode integration smoke. Unlike the legacy milestone
    /// regressions, this keeps LumberCampProgressionService enabled and drives the
    /// accepted gameplay commit points rather than calling the progression model.
    /// </summary>
    [InitializeOnLoad]
    public static class LumberCampM10PlayModeSmokeTest
    {
        private const string ScenePath =
            "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey =
            "IndustryTycoon.M10.AuthoritativeSmoke.Running";
        private const string CommandLineKey =
            "IndustryTycoon.M10.AuthoritativeSmoke.CommandLine";
        private const string FinishPendingKey =
            "IndustryTycoon.M10.AuthoritativeSmoke.FinishPending";
        private const string SuccessKey =
            "IndustryTycoon.M10.AuthoritativeSmoke.Success";
        private const string ResultMessageKey =
            "IndustryTycoon.M10.AuthoritativeSmoke.ResultMessage";
        private const string ErrorCountKey =
            "IndustryTycoon.M10.AuthoritativeSmoke.ErrorCount";

        private static readonly Vector3 IsolatedPickupPosition =
            new Vector3(50f, 0.32f, 50f);
        private static readonly Vector3 IsolatedPlayerPosition =
            new Vector3(50f, 0f, 50f);
        private static readonly Vector3 ParkedPlayerPosition =
            new Vector3(60f, 0f, 60f);

        private enum Stage
        {
            Warmup,
            WaitForTimedProduction,
            WaitForPlayerPickup,
            CompleteSalesAndFirstContract,
            CompleteProductionPurchase,
            CompleteWorkerPurchase,
            CompleteProcessorPurchase,
            WaitForProcessorRecipes,
            CompleteAutoFeederPurchase,
            CompletePackingPurchase,
            WaitForPackingRecipe,
            CompleteCourierPurchase,
            WaitForCourierDelivery,
            VerifyStableCompletion,
            VerifyOfflineCompletionCollect
        }

        private static LocalPersistenceService _persistence;
        private static LumberCampProgressionService _progression;
        private static AchievementToastView _achievementToast;
        private static CharacterController _playerController;
        private static ResourceCollector _resourceCollector;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static CashPile _cashPile;
        private static CashPileCollector _cashCollector;
        private static SalePoint _salePoint;
        private static WoodSpawner _woodSpawner;
        private static LumberWorker _worker;
        private static WoodAutoFeeder _autoFeeder;
        private static WoodProcessor _processor;
        private static PackingStation _packingStation;
        private static CrateCourier _courier;
        private static LumberCampCompletion _completion;

        private static PurchasePad _productionPad;
        private static PurchasePad _workerPad;
        private static PurchasePad _processorPad;
        private static PurchasePad _autoFeederPad;
        private static PurchasePad _packingPad;
        private static PurchasePad _courierPad;

        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;
        private static int _initialLooseWood;
        private static int _targetProcessorRecipes;
        private static int _targetPackingRecipes;
        private static int _courierTripStart;
        private static long[] _offlineMetricSnapshot;
        private static int _offlineToastTarget;

        static LumberCampM10PlayModeSmokeTest()
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

            if (SessionState.GetBool(FinishPendingKey, false))
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
            }
        }

        [MenuItem("Industry Tycoon/Prototype/Run M10 Authoritative Progression Smoke Test")]
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
                    "Exit Play Mode before starting the M10 progression smoke test.");
            }

            if (!commandLine
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new InvalidOperationException(
                    $"Missing prototype scene at {ScenePath}.");
            }

            M9EditorSaveUtility.PrepareFreshSmokeTest();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(CommandLineKey, commandLine);
            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
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

                if (Now - _runStartedAt > 90d)
                {
                    throw new InvalidOperationException(
                        "M10 progression smoke exceeded its 90-second timeout.");
                }

                ValidateContinuousInvariants();
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

            _persistence = FindSingleIncludingInactive<LocalPersistenceService>();
            _progression = FindSingleIncludingInactive<LumberCampProgressionService>();
            _achievementToast = FindSingleIncludingInactive<AchievementToastView>();
            _playerController = FindSingleIncludingInactive<CharacterController>();
            _resourceCollector = FindSingleIncludingInactive<ResourceCollector>();
            _carryStack = FindSingleIncludingInactive<CarryStack>();
            _wallet = FindSingleIncludingInactive<Wallet>();
            _cashPile = FindSingleIncludingInactive<CashPile>();
            _cashCollector = FindSingleIncludingInactive<CashPileCollector>();
            _salePoint = FindSingleIncludingInactive<SalePoint>();
            _woodSpawner = FindSingleIncludingInactive<WoodSpawner>();
            _worker = FindSingleIncludingInactive<LumberWorker>();
            _autoFeeder = FindSingleIncludingInactive<WoodAutoFeeder>();
            _processor = FindSingleIncludingInactive<WoodProcessor>();
            _packingStation =
                FindSingleExactTypeIncludingInactive<PackingStation>();
            _courier = FindSingleIncludingInactive<CrateCourier>();
            _completion = FindSingleIncludingInactive<LumberCampCompletion>();

            _productionPad = _progression.ProductionUpgrade.PurchasePad;
            _workerPad = _progression.WorkerUnlock.WorkerPurchasePad;
            _processorPad = _progression.ProcessorUnlock.ProcessorPurchasePad;
            _autoFeederPad = _progression.AutoFeederUnlock.AutoFeederPurchasePad;
            _packingPad = _progression.PackingStationUnlock.PackingStationPurchasePad;
            _courierPad = _progression.CourierUnlock.CourierPurchasePad;

            Require(_persistence.ProgressionService == _progression
                    && _progression.enabled
                    && _progression.Wallet == _wallet
                    && _progression.WoodSpawner == _woodSpawner
                    && _progression.ResourceCollector == _resourceCollector
                    && _progression.SalePoint == _salePoint
                    && _progression.Processor == _processor
                    && _progression.PackingStation == _packingStation
                    && _progression.Courier == _courier,
                "M10 progression service is not wired to the accepted scene commits.");
            Require(_productionPad != null
                    && _workerPad != null
                    && _processorPad != null
                    && _autoFeederPad != null
                    && _packingPad != null
                    && _courierPad != null,
                "M10 progression smoke could not resolve all Purchase Pads.");
            Require(_carryStack.TotalAmount == 0
                    && _wallet.Balance == 0
                    && _cashPile.StoredCash == 0,
                "Fresh M10 smoke did not begin with an empty economy/CarryStack.");

            // Stop incidental automation while retaining the exact components and
            // authoritative event wiring under test.
            _resourceCollector.CancelTransientAttractions();
            _resourceCollector.enabled = false;
            _cashCollector.enabled = false;
            _worker.enabled = false;
            _autoFeeder.enabled = false;
            _woodSpawner.enabled = false;
            MovePlayerTo(ParkedPlayerPosition);

            _stage = Stage.Warmup;
            _stageStartedAt = Now;
            _runStartedAt = Now;
            _runtimeInitialized = true;
        }

        private static void TickCurrentStage()
        {
            switch (_stage)
            {
                case Stage.Warmup:
                    TickWarmup();
                    break;
                case Stage.WaitForTimedProduction:
                    TickWaitForTimedProduction();
                    break;
                case Stage.WaitForPlayerPickup:
                    TickWaitForPlayerPickup();
                    break;
                case Stage.CompleteSalesAndFirstContract:
                    TickCompleteSalesAndFirstContract();
                    break;
                case Stage.CompleteProductionPurchase:
                    TickCompleteProductionPurchase();
                    break;
                case Stage.CompleteWorkerPurchase:
                    TickCompleteWorkerPurchase();
                    break;
                case Stage.CompleteProcessorPurchase:
                    TickCompleteProcessorPurchase();
                    break;
                case Stage.WaitForProcessorRecipes:
                    TickWaitForProcessorRecipes();
                    break;
                case Stage.CompleteAutoFeederPurchase:
                    TickCompleteAutoFeederPurchase();
                    break;
                case Stage.CompletePackingPurchase:
                    TickCompletePackingPurchase();
                    break;
                case Stage.WaitForPackingRecipe:
                    TickWaitForPackingRecipe();
                    break;
                case Stage.CompleteCourierPurchase:
                    TickCompleteCourierPurchase();
                    break;
                case Stage.WaitForCourierDelivery:
                    TickWaitForCourierDelivery();
                    break;
                case Stage.VerifyStableCompletion:
                    TickVerifyStableCompletion();
                    break;
                case Stage.VerifyOfflineCompletionCollect:
                    TickVerifyOfflineCompletionCollect();
                    break;
            }
        }

        private static void TickWarmup()
        {
            EnsureStageTimeout(5d);
            if (!_persistence.IsInitialized
                || !_progression.IsRuntimeReady
                || !HasWaited(0.10d))
            {
                return;
            }

            Require(_persistence.LastLoadStatus == M9SaveLoadStatus.FreshNoSave,
                $"M10 smoke loaded unexpected state {_persistence.LastLoadStatus}.");
            Require(_progression.GetMetric(ProgressMetricId.WoodProduced) == 0L,
                "Bootstrap loose Wood fabricated WoodProduced progress.");
            _initialLooseWood = _woodSpawner.ActiveCount;
            Require(_initialLooseWood > 0,
                "Fresh scene did not create its bootstrap loose Wood inventory.");

            // Cycle twice before all commits. Every following exact metric assertion
            // also validates that gameplay subscriptions were not duplicated.
            _progression.enabled = false;
            _progression.enabled = true;
            _progression.enabled = false;
            _progression.enabled = true;
            Require(_progression.enabled && _progression.IsRuntimeReady,
                "M10 service did not recover from an enable/disable cycle.");

            long[] beforeFailedSale = CaptureMetrics();
            Require(!_salePoint.TryUnloadOne(),
                "An empty CarryStack unexpectedly completed a SalePoint transaction.");
            RequireMetricsEqual(beforeFailedSale,
                "Failed SalePoint transaction changed progression metrics.");

            _woodSpawner.enabled = true;
            AdvanceTo(Stage.WaitForTimedProduction);
        }

        private static void TickWaitForTimedProduction()
        {
            EnsureStageTimeout(_woodSpawner.EffectiveSpawnInterval + 4d);
            long produced = _progression.GetMetric(ProgressMetricId.WoodProduced);
            if (produced == 0L)
            {
                return;
            }

            Require(produced == 1L
                    && _woodSpawner.ActiveCount == _initialLooseWood + 1,
                "One timed Wood spawn did not create exactly one production metric.");
            _woodSpawner.enabled = false;

            ResourcePickup pickup = FindAvailableLooseWood();
            Require(pickup != null && pickup.Amount == 1,
                "M10 smoke could not isolate one real Wood pickup.");
            pickup.transform.position = IsolatedPickupPosition;
            MovePlayerTo(IsolatedPlayerPosition);
            _resourceCollector.enabled = true;
            AdvanceTo(Stage.WaitForPlayerPickup);
        }

        private static void TickWaitForPlayerPickup()
        {
            EnsureStageTimeout(4d);
            if (_carryStack.GetAmount(ResourceType.Wood) == 0)
            {
                return;
            }

            Require(_carryStack.GetAmount(ResourceType.Wood) == 1
                    && _carryStack.TotalAmount == 1
                    && _progression.GetMetric(ProgressMetricId.WoodCollected) == 1L
                    && _progression.GetMetric(ProgressMetricId.WoodProduced) == 1L,
                "Real ResourceCollector pickup did not commit exactly one Wood metric.");
            _resourceCollector.enabled = false;
            Require(_resourceCollector.ReservedCapacity == 0
                    && _carryStack.ReservedCapacity == 0,
                "Player pickup left transient CarryStack reservations.");
            MovePlayerTo(ParkedPlayerPosition);
            AdvanceTo(Stage.CompleteSalesAndFirstContract);
        }

        private static void TickCompleteSalesAndFirstContract()
        {
            EnsureStageTimeout(5d);
            int firstSaleReward = LumberCampProgressionCatalog.GetAchievement(
                (int)LumberCampAchievementId.FirstSale).RewardCash;
            Require(firstSaleReward == 50
                    && _wallet.Balance == 0
                    && _achievementToast.PresentationCount == 0,
                "First Sale fixture began with unexpected reward/toast state.");

            Require(_salePoint.TryUnloadOne(),
                "Accepted Wood sale failed at the SalePoint commit.");
            Require(_progression.GetMetric(ProgressMetricId.WoodSold) == 1L
                    && _progression.GetMetric(ProgressMetricId.TotalCashEarned)
                       == _salePoint.WoodValue
                    && _progression.GetMetric(ProgressMetricId.WoodCollected) == 1L
                    && _cashPile.StoredCash == _salePoint.WoodValue,
                "First Sale did not update sold quantity/gameplay Cash exactly once.");
            Require(_wallet.Balance == firstSaleReward
                    && _progression.IsAchievementUnlocked(
                        (int)LumberCampAchievementId.FirstSale)
                    && _progression.IsAchievementRewarded(
                        (int)LumberCampAchievementId.FirstSale)
                    && _achievementToast.PresentationCount == 1
                    && !_achievementToast.CanvasGroup.blocksRaycasts,
                "First Sale did not auto-reward/toast exactly once and nonblockingly.");
            Require(_progression.GetMetric(ProgressMetricId.TotalCashEarned)
                    == _salePoint.WoodValue,
                "Achievement reward leaked into gameplay-generated Cash.");

            for (int i = 1; i < 20; i++)
            {
                Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                        && _salePoint.TryUnloadOne(),
                    $"Authoritative Wood sale {i + 1} failed.");
            }

            int expectedSaleCash = 20 * _salePoint.WoodValue;
            Require(_carryStack.TotalAmount == 0
                    && _progression.GetMetric(ProgressMetricId.WoodSold) == 20L
                    && _progression.GetMetric(ProgressMetricId.TotalCashEarned)
                       == expectedSaleCash
                    && _cashPile.StoredCash == expectedSaleCash
                    && _wallet.Balance == firstSaleReward,
                "Twenty Wood sales or reward exclusion were not exact.");
            Require(_progression.ActiveContractIndex == 0
                    && _progression.ActiveContractState
                       == ContractProgressState.CompletedUnclaimed,
                "Sell 20 Wood contract did not complete at its exact baseline target.");

            long gameplayCashBeforeClaim = _progression.GetMetric(
                ProgressMetricId.TotalCashEarned);
            int walletBeforeClaim = _wallet.Balance;
            Require(_progression.TryClaimActiveContract(),
                "Completed Sell 20 Wood contract could not be claimed.");
            Require(_wallet.Balance == walletBeforeClaim + 150
                    && _progression.GetMetric(ProgressMetricId.TotalCashEarned)
                       == gameplayCashBeforeClaim
                    && _progression.IsContractClaimed(0)
                    && _progression.ActiveContractIndex == 1
                    && _progression.ActiveContractState == ContractProgressState.Active,
                "Contract reward, reward exclusion, or next activation was not exact.");
            _progression.GetActiveContractProgress(
                out long nextProgress,
                out long nextTarget);
            Require(nextProgress == 0L && nextTarget == 15L,
                "Produce 15 Planks contract did not capture a zero activation baseline.");

            int walletAfterClaim = _wallet.Balance;
            Require(!_progression.TryClaimActiveContract()
                    && _wallet.Balance == walletAfterClaim,
                "A second claim duplicated the first contract reward.");

            long[] beforeSecondFailedSale = CaptureMetrics();
            Require(!_salePoint.TryUnloadOne(),
                "Empty SalePoint transaction succeeded after the contract batch.");
            RequireMetricsEqual(beforeSecondFailedSale,
                "Failed post-contract sale changed progression metrics.");
            AdvanceTo(Stage.CompleteProductionPurchase);
        }

        private static void TickCompleteProductionPurchase()
        {
            CompletePurchase(_productionPad);
            Require(_progression.GetFlag(ProgressFlagId.ProductionUpgradeUnlocked)
                    && _progression.ProductionUpgrade.IsApplied,
                "Production PurchasePad commit did not set its exact M10 flag.");
            Require(_productionPad.ProcessPaymentStep() == 0,
                "Completed Production PurchasePad accepted a duplicate payment.");
            AdvanceTo(Stage.CompleteWorkerPurchase);
        }

        private static void TickCompleteWorkerPurchase()
        {
            CompletePurchase(_workerPad);
            Require(_progression.GetFlag(ProgressFlagId.WorkerUnlocked)
                    && _progression.WorkerUnlock.IsWorkerActivated,
                "Worker PurchasePad commit did not set its exact M10 flag.");
            _worker.enabled = false;
            Require(_workerPad.ProcessPaymentStep() == 0,
                "Completed Worker PurchasePad accepted a duplicate payment.");
            AdvanceTo(Stage.CompleteProcessorPurchase);
        }

        private static void TickCompleteProcessorPurchase()
        {
            CompletePurchase(_processorPad);
            Require(_progression.GetFlag(ProgressFlagId.ProcessorUnlocked)
                    && _progression.ProcessorUnlock.IsProcessorActivated
                    && _processor.isActiveAndEnabled,
                "Processor PurchasePad commit did not activate its exact M10 source.");
            Require(_processorPad.ProcessPaymentStep() == 0,
                "Completed Processor PurchasePad accepted a duplicate payment.");

            int requiredWood = _processor.RecipeInputWood * 2;
            Require(_carryStack.TryAdd(ResourceType.Wood, requiredWood),
                "M10 smoke could not stage two real Processor recipes.");
            _targetProcessorRecipes = _processor.CompletedRecipeCount + 2;
            for (int i = 0; i < requiredWood; i++)
            {
                Require(_processor.TryTransferInputFrom(_carryStack),
                    "Processor rejected valid carried Wood input.");
            }

            Require(_carryStack.TotalAmount == 0 && _processor.IsProcessing,
                "Processor recipes did not enter the authoritative processing lifecycle.");
            AdvanceTo(Stage.WaitForProcessorRecipes);
        }

        private static void TickWaitForProcessorRecipes()
        {
            EnsureStageTimeout((_processor.ProcessingDuration * 3d) + 3d);
            if (_processor.CompletedRecipeCount < _targetProcessorRecipes)
            {
                return;
            }

            Require(_processor.CompletedRecipeCount == _targetProcessorRecipes
                    && _processor.OutputPlanks == 2
                    && _progression.GetMetric(ProgressMetricId.PlanksProduced) == 2L,
                "Real Processor recipe commits did not increment PlanksProduced once each.");
            _progression.GetActiveContractProgress(
                out long contractProgress,
                out long contractTarget);
            Require(contractProgress == 2L && contractTarget == 15L,
                "New contract baseline included historical or duplicated Plank production.");

            Require(_processor.TryTransferOutputTo(_carryStack)
                    && _processor.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Plank) == 2,
                "Processor output could not enter the accepted CarryStack path.");
            AdvanceTo(Stage.CompleteAutoFeederPurchase);
        }

        private static void TickCompleteAutoFeederPurchase()
        {
            CompletePurchase(_autoFeederPad);
            Require(_progression.GetFlag(ProgressFlagId.AutoFeederUnlocked)
                    && _progression.AutoFeederUnlock.IsAutoFeederActivated,
                "Auto Feeder PurchasePad commit did not set its exact M10 flag.");
            _autoFeeder.enabled = false;
            Require(_autoFeederPad.ProcessPaymentStep() == 0,
                "Completed Auto Feeder PurchasePad accepted a duplicate payment.");
            AdvanceTo(Stage.CompletePackingPurchase);
        }

        private static void TickCompletePackingPurchase()
        {
            CompletePurchase(_packingPad);
            Require(_progression.GetFlag(ProgressFlagId.PackingStationUnlocked)
                    && _progression.PackingStationUnlock.IsPackingStationActivated
                    && _packingStation.isActiveAndEnabled,
                "Packing PurchasePad commit did not activate its exact M10 source.");
            Require(_packingPad.ProcessPaymentStep() == 0,
                "Completed Packing PurchasePad accepted a duplicate payment.");

            _targetPackingRecipes = _packingStation.CompletedRecipeCount + 1;
            int requiredPlanks = _packingStation.RecipeInputPlanks;
            Require(requiredPlanks == 2
                    && _carryStack.GetAmount(ResourceType.Plank) == requiredPlanks,
                "Packing recipe fixture does not hold the canonical two Planks.");
            for (int i = 0; i < requiredPlanks; i++)
            {
                Require(_packingStation.TryTransferInputFrom(_carryStack),
                    "Packing Station rejected a valid carried Plank.");
            }

            Require(_carryStack.TotalAmount == 0 && _packingStation.IsProcessing,
                "Packing recipe did not enter the authoritative processing lifecycle.");
            AdvanceTo(Stage.WaitForPackingRecipe);
        }

        private static void TickWaitForPackingRecipe()
        {
            EnsureStageTimeout((_packingStation.ProcessingDuration * 2d) + 3d);
            if (_packingStation.CompletedRecipeCount < _targetPackingRecipes)
            {
                return;
            }

            Require(_packingStation.CompletedRecipeCount == _targetPackingRecipes
                    && _packingStation.OutputCrates == 1
                    && _progression.GetMetric(ProgressMetricId.CratesProduced) == 1L,
                "Real Packing recipe did not increment CratesProduced exactly once.");
            AdvanceTo(Stage.CompleteCourierPurchase);
        }

        private static void TickCompleteCourierPurchase()
        {
            _courierTripStart = _courier.CompletedTripCount;
            CompletePurchase(_courierPad);
            Require(_progression.GetFlag(ProgressFlagId.CourierUnlocked)
                    && _progression.CourierUnlock.IsCourierActivated
                    && _courier.isActiveAndEnabled,
                "Courier PurchasePad commit did not activate its exact M10 source.");
            Require(_courierPad.ProcessPaymentStep() == 0,
                "Completed Courier PurchasePad accepted a duplicate payment.");
            AdvanceTo(Stage.WaitForCourierDelivery);
        }

        private static void TickWaitForCourierDelivery()
        {
            EnsureStageTimeout(25d);
            if (_courier.CompletedTripCount <= _courierTripStart)
            {
                return;
            }

            int expectedSaleCash = 20 * _salePoint.WoodValue;
            int expectedCourierCash = _courier.CashPerCrate;
            Require(_courier.CompletedTripCount == _courierTripStart + 1
                    && _courier.TotalDeliveredCrates == 1
                    && _progression.GetMetric(
                        ProgressMetricId.CourierTripsCompleted) == 1L
                    && _progression.GetMetric(ProgressMetricId.CratesDelivered) == 1L
                    && _progression.GetMetric(ProgressMetricId.TotalCashEarned)
                       == expectedSaleCash + expectedCourierCash
                    && _cashPile.StoredCash
                       == expectedSaleCash + expectedCourierCash,
                "Courier commit did not record one trip, one Crate, and exact gameplay Cash.");
            Require(_completion.IsCompleted
                    && _completion.CompletionCount == 1
                    && _progression.GetFlag(ProgressFlagId.LumberCampCompleted),
                "First real Courier delivery did not complete Lumber Camp once.");

            long[] beforeDuplicateDelivery = CaptureMetrics();
            Require(!_courier.TryCommitDelivery(),
                "Courier accepted a re-entered delivery after its committed generation.");
            RequireMetricsEqual(beforeDuplicateDelivery,
                "Rejected/re-entered Courier delivery duplicated M10 progress.");
            AdvanceTo(Stage.VerifyStableCompletion);
        }

        private static void TickVerifyStableCompletion()
        {
            EnsureStageTimeout(20d);
            const int expectedAchievementCount = 8;
            if (_achievementToast.PresentationCount < expectedAchievementCount
                || _achievementToast.IsPresenting
                || _achievementToast.QueuedCount > 0)
            {
                return;
            }

            Require(_progression.GetMetric(ProgressMetricId.WoodProduced) == 1L
                    && _progression.GetMetric(ProgressMetricId.WoodCollected) == 1L
                    && _progression.GetMetric(ProgressMetricId.WoodSold) == 20L
                    && _progression.GetMetric(ProgressMetricId.PlanksProduced) == 2L
                    && _progression.GetMetric(ProgressMetricId.CratesProduced) == 1L
                    && _progression.GetMetric(
                        ProgressMetricId.CourierTripsCompleted) == 1L
                    && _progression.GetMetric(ProgressMetricId.CratesDelivered) == 1L,
                "Final M10 authoritative metrics changed after their commit frames.");
            Require(_progression.GetFlag(ProgressFlagId.ProductionUpgradeUnlocked)
                    && _progression.GetFlag(ProgressFlagId.WorkerUnlocked)
                    && _progression.GetFlag(ProgressFlagId.ProcessorUnlocked)
                    && _progression.GetFlag(ProgressFlagId.AutoFeederUnlocked)
                    && _progression.GetFlag(ProgressFlagId.PackingStationUnlocked)
                    && _progression.GetFlag(ProgressFlagId.CourierUnlocked)
                    && _progression.GetFlag(ProgressFlagId.LumberCampCompleted),
                "Final PurchasePad/completion flag set is incomplete.");
            LumberCampAchievementId[] expectedAchievements =
            {
                LumberCampAchievementId.FirstSale,
                LumberCampAchievementId.FirstHire,
                LumberCampAchievementId.ProcessingBegins,
                LumberCampAchievementId.AutomationOnline,
                LumberCampAchievementId.PackedAndReady,
                LumberCampAchievementId.DeliveryService,
                LumberCampAchievementId.FullyAutomatedInput,
                LumberCampAchievementId.LumberCampComplete
            };
            Require(_achievementToast.PresentationCount
                    == expectedAchievements.Length,
                "Achievements did not present exactly one toast per real unlock.");
            for (int i = 0; i < LumberCampProgressionCatalog.AchievementCount; i++)
            {
                bool expected = Array.IndexOf(
                    expectedAchievements,
                    (LumberCampAchievementId)i) >= 0;
                Require(_progression.IsAchievementUnlocked(i) == expected
                        && _progression.IsAchievementRewarded(i) == expected,
                    $"Achievement {i} reward/unlock state was duplicated or fabricated.");
            }
            Require(_progression.IsContractClaimed(0)
                    && _progression.ActiveContractIndex == 1
                    && _progression.ActiveContractState == ContractProgressState.Active,
                "Claimed first contract or active second contract changed unexpectedly.");
            _progression.GetActiveContractProgress(
                out long contractProgress,
                out long contractTarget);
            Require(contractProgress == 2L && contractTarget == 15L,
                "Next contract baseline/progress changed after unrelated commits.");
            Require(SessionState.GetInt(ErrorCountKey, 0) == 0,
                "M10 progression smoke observed Console errors/assertions.");

            PrepareAndCollectOfflineCompletion();
            AdvanceTo(Stage.VerifyOfflineCompletionCollect);
        }

        private static void PrepareAndCollectOfflineCompletion()
        {
            _worker.enabled = false;
            _autoFeeder.enabled = false;
            _processor.enabled = false;
            _packingStation.enabled = false;
            _courier.enabled = false;
            _woodSpawner.enabled = false;

            // Recreate the exact edge case where legitimate offline Courier work is
            // the first source of Lumber Camp completion. No lifetime metric may be
            // fabricated, but the exact flag, objective, achievement, and reward must
            // reach a fixed point before Welcome Back cash is collected.
            _completion.RestoreCompleted(false);
            _persistence.MineUnlock.RestoreUnlocked(false);
            _courier.RestoreIdleState();
            int completionReward = LumberCampProgressionCatalog.GetAchievement(
                (int)LumberCampAchievementId.LumberCampComplete).RewardCash;
            Require(_wallet.SpendUpTo(completionReward) == completionReward,
                "Offline completion fixture could not remove the prior test reward.");
            M10ProgressionSaveData state = _progression.CapturePersistentState();
            state.flags[(int)ProgressFlagId.LumberCampCompleted].value = false;
            state.metrics[(int)ProgressMetricId.CourierTripsCompleted].value = 0L;
            state.metrics[(int)ProgressMetricId.CratesDelivered].value = 0L;
            state.metrics[(int)ProgressMetricId.MineUnlocked].value = 0L;
            M10AchievementSaveRecord campAchievement = state.FindAchievementRecord(
                (int)LumberCampAchievementId.LumberCampComplete);
            Require(campAchievement != null,
                "Offline completion fixture could not find its achievement record.");
            campAchievement.unlocked = false;
            campAchievement.rewarded = false;
            _progression.RestorePersistentState(state);

            Require(_packingStation.RestoreStableState(0, 1)
                    && !_completion.IsCompleted
                    && !_progression.GetFlag(ProgressFlagId.LumberCampCompleted),
                "Offline completion fixture could not stage one canonical Crate.");
            Require(_persistence.SaveNow(),
                "Offline completion fixture could not persist its pre-away state.");

            _offlineMetricSnapshot = CaptureMetrics();
            int walletBeforeSettlement = _wallet.Balance;
            int toastBeforeSettlement = _achievementToast.PresentationCount;
            Require(_persistence.SimulateAwayForDevelopment(10L * 60L),
                "Offline completion fixture could not evaluate its away interval.");
            Require(_persistence.LastOfflineResult != null
                    && _persistence.LastOfflineResult.CourierCratesDelivered > 0
                    && _persistence.HasPendingReturn
                    && _persistence.PendingOfflineCash > 0,
                "Offline Courier settlement did not create a pending return.");
            Require(_completion.IsCompleted
                    && _progression.GetFlag(ProgressFlagId.LumberCampCompleted)
                    && _progression.GetMetric(ProgressMetricId.MineUnlocked) == 1L
                    && _progression.IsAchievementUnlocked(
                        (int)LumberCampAchievementId.LumberCampComplete)
                    && _progression.IsAchievementRewarded(
                        (int)LumberCampAchievementId.LumberCampComplete)
                    && _wallet.Balance == walletBeforeSettlement + completionReward,
                "Offline completion did not resolve/reward M10 before COLLECT.");
            _offlineMetricSnapshot[(int)ProgressMetricId.MineUnlocked] = 1L;
            RequireMetricsEqual(_offlineMetricSnapshot,
                "Offline settlement fabricated an M10 lifetime metric.");

            int pendingCash = _persistence.PendingOfflineCash;
            int walletBeforeCollect = _wallet.Balance;
            Require(_persistence.TryCollectOfflineReward(1f)
                    && _wallet.Balance == walletBeforeCollect + pendingCash
                    && !_persistence.HasPendingReturn,
                "COLLECT was not atomic after the offline completion achievement.");
            int walletAfterCollect = _wallet.Balance;
            Require(!_persistence.TryCollectOfflineReward(1f)
                    && _wallet.Balance == walletAfterCollect,
                "Offline reward was collectable more than once.");
            RequireMetricsEqual(_offlineMetricSnapshot,
                "Achievement/offline reward changed gameplay Cash or production metrics.");
            _offlineToastTarget = toastBeforeSettlement + 1;
        }

        private static void TickVerifyOfflineCompletionCollect()
        {
            EnsureStageTimeout(10d);
            if (_achievementToast.IsPresenting
                || _achievementToast.QueuedCount > 0)
            {
                return;
            }

            Require(_achievementToast.PresentationCount == _offlineToastTarget,
                "Offline completion achievement did not toast exactly once.");
            Require(SessionState.GetInt(ErrorCountKey, 0) == 0,
                "Offline completion regression observed Console errors/assertions.");

            Pass(
                "M10 authoritative progression Play Mode smoke passed: silent bootstrap, "
                + "one timed production, real pickup/sales/recipes/delivery, stable "
                + "resubscription and duplicate guards, exact unlock flags, eight "
                + "achievement rewards/toasts, first-contract claim/next baseline, "
                + "and offline completion fixed-point/atomic COLLECT regression.");
        }

        private static void CompletePurchase(PurchasePad pad)
        {
            Require(pad != null && pad.IsAvailable && !pad.IsCompleted,
                "PurchasePad was not available in the canonical unlock order.");
            int walletBefore = _wallet.Balance;
            int remainingCost = pad.RemainingCost;
            int achievementCashBefore = GetRewardedAchievementCash();
            int requiredFunding = Mathf.Max(0, remainingCost - walletBefore);
            if (requiredFunding > 0)
            {
                Require(_wallet.Deposit(requiredFunding) == requiredFunding,
                    $"Could not fund {pad.PurchaseLabel} without overflow.");
            }

            int guard = 0;
            while (!pad.IsCompleted && guard++ < 1000)
            {
                Require(pad.ProcessPaymentStep() > 0,
                    $"{pad.PurchaseLabel} rejected an affordable authoritative payment.");
            }

            int achievementCashAfter = GetRewardedAchievementCash();
            int expectedWalletAfter = walletBefore
                                      + requiredFunding
                                      - remainingCost
                                      + achievementCashAfter
                                      - achievementCashBefore;
            Require(pad.IsCompleted
                    && pad.RemainingCost == 0
                    && _wallet.Balance == expectedWalletAfter,
                $"{pad.PurchaseLabel} did not commit exact completion/economy state.");
            Require(_progression.GetMetric(ProgressMetricId.TotalCashEarned)
                    == 20L * _salePoint.WoodValue,
                "Purchase funding/spend leaked into gameplay-generated Cash.");
        }

        private static int GetRewardedAchievementCash()
        {
            int total = 0;
            for (int i = 0; i < LumberCampProgressionCatalog.AchievementCount; i++)
            {
                if (_progression.IsAchievementRewarded(i))
                {
                    total += LumberCampProgressionCatalog
                        .GetAchievement(i)
                        .RewardCash;
                }
            }

            return total;
        }

        private static ResourcePickup FindAvailableLooseWood()
        {
            ResourcePickup[] pickups = Object.FindObjectsByType<ResourcePickup>(
                FindObjectsInactive.Exclude);
            for (int i = 0; i < pickups.Length; i++)
            {
                ResourcePickup pickup = pickups[i];
                if (pickup != null
                    && pickup.IsAvailable
                    && !pickup.IsClaimed
                    && pickup.ResourceType == ResourceType.Wood)
                {
                    return pickup;
                }
            }

            return null;
        }

        private static long[] CaptureMetrics()
        {
            var result = new long[LumberCampProgressionCatalog.MetricCount];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = _progression.GetMetric((ProgressMetricId)i);
            }

            return result;
        }

        private static void RequireMetricsEqual(long[] expected, string message)
        {
            Require(expected != null
                    && expected.Length == LumberCampProgressionCatalog.MetricCount,
                "M10 metric snapshot has the wrong size.");
            for (int i = 0; i < expected.Length; i++)
            {
                ProgressMetricId metric = (ProgressMetricId)i;
                Require(_progression.GetMetric(metric) == expected[i],
                    $"{message} Metric={metric}, expected={expected[i]}, "
                    + $"actual={_progression.GetMetric(metric)}.");
            }
        }

        private static void ValidateContinuousInvariants()
        {
            if (!_runtimeInitialized)
            {
                return;
            }

            Require(_progression != null
                    && _progression.enabled
                    && _progression.IsRuntimeReady,
                "M10 service became disabled or unready during its integration smoke.");
            Require(_wallet.Balance >= 0
                    && _cashPile.StoredCash >= 0
                    && _carryStack.TotalAmount >= 0
                    && _carryStack.ReservedCapacity >= 0
                    && _carryStack.TotalAmount + _carryStack.ReservedCapacity
                       <= _carryStack.Capacity,
                "M10 smoke economy/CarryStack invariant regressed.");
            for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
            {
                Require(_progression.GetMetric((ProgressMetricId)i) >= 0L,
                    $"M10 metric {(ProgressMetricId)i} became negative.");
            }
        }

        private static T FindSingleIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            Require(matches.Length == 1,
                $"M10 smoke expected one {typeof(T).Name}, found {matches.Length}.");
            return matches[0];
        }

        private static T FindSingleExactTypeIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            T result = null;
            int exactMatchCount = 0;
            for (int i = 0; i < matches.Length; i++)
            {
                if (matches[i].GetType() != typeof(T))
                {
                    continue;
                }

                result = matches[i];
                exactMatchCount++;
            }

            Require(exactMatchCount == 1,
                $"M10 smoke expected one concrete {typeof(T).Name}, found {exactMatchCount}.");
            return result;
        }

        private static void MovePlayerTo(Vector3 position)
        {
            bool wasEnabled = _playerController.enabled;
            _playerController.enabled = false;
            _playerController.transform.position = position;
            _playerController.enabled = wasEnabled;
            Physics.SyncTransforms();
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
                    $"M10 progression smoke timed out in stage {_stage}.");
            }
        }

        private static double Now => Time.realtimeSinceStartupAsDouble;

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
            string result = "M10 authoritative progression Play Mode smoke failed: "
                            + message;
            Debug.LogError(result);
            EndRun(false, result);
        }

        private static void EndRun(bool success, string message)
        {
            SessionState.SetBool(RunningKey, false);
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
                // Play Mode teardown performs a lifecycle save. Remove it only after
                // returning to Edit Mode so this smoke cannot contaminate later tests.
                M9EditorSaveUtility.PrepareFreshSmokeTest();
            }
            catch (Exception exception)
            {
                success = false;
                message = "M10 smoke could not clean its isolated save: "
                          + exception.Message;
                Debug.LogError(message);
            }

            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(CommandLineKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
            SessionState.SetInt(ErrorCountKey, 0);

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
