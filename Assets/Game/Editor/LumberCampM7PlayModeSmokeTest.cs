using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Workers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    [InitializeOnLoad]
    public static class LumberCampM7PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey = "IndustryTycoon.M7.Smoke.Running";
        private const string CommandLineKey = "IndustryTycoon.M7.Smoke.CommandLine";
        private const string FinishPendingKey = "IndustryTycoon.M7.Smoke.FinishPending";
        private const string SuccessKey = "IndustryTycoon.M7.Smoke.Success";
        private const string ResultMessageKey = "IndustryTycoon.M7.Smoke.ResultMessage";
        private static readonly Vector3 NeutralPosition = new Vector3(0f, 0f, -11f);

        private enum Stage
        {
            WarmupLocked,
            CompletePrerequisites,
            PartiallyFundCourier,
            CompleteCourierPurchase,
            VerifyEmptyWait,
            StartOneCrateTrip,
            WaitForOneCrateDelivery,
            CollectOneCrateCash,
            PrepareCapacityOutput,
            WaitForCapacityOutput,
            WaitForTwoCrateDelivery,
            WaitForCapacityRemainder,
            PreparePartialPreemption,
            WaitForPartialPreemptionOutput,
            WaitForPartialPreemptionDelivery,
            PrepareFullPreemption,
            WaitForFullPreemptionOutput,
            WaitForFullPreemptionRecovery,
            VerifyEmptyAfterPreemption,
            RefillAfterEmpty,
            WaitForRefillDelivery,
            PrepareStationLifecycleOutput,
            WaitForStationLifecycleOutput,
            WaitForStationLifecycleRetry,
            WaitForStationLifecycleDelivery,
            VerifyTypedManualSales,
            StartSimultaneousChains,
            WaitForSimultaneousChains
        }

        private static readonly HashSet<uint> StartedGenerations = new HashSet<uint>();
        private static readonly HashSet<uint> DeliveredGenerations = new HashSet<uint>();

        private static CharacterController _playerController;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static CashPile _cashPile;
        private static CashPileCollector _cashCollector;
        private static SalePoint _salePoint;
        private static PurchasePad _productionPad;
        private static PurchasePad _workerPad;
        private static PurchasePad _processorPad;
        private static PurchasePad _autoFeederPad;
        private static PurchasePad _packingPad;
        private static PurchasePad _courierPad;
        private static FirstWorkerUnlock _workerUnlock;
        private static FirstProcessorUnlock _processorUnlock;
        private static FirstAutoFeederUnlock _autoFeederUnlock;
        private static FirstPackingStationUnlock _packingUnlock;
        private static FirstCourierUnlock _courierUnlock;
        private static LumberWorker _worker;
        private static WoodStockpile _stockpile;
        private static WoodProcessor _processor;
        private static WoodAutoFeeder _autoFeeder;
        private static PackingStation _packingStation;
        private static CrateCourier _courier;
        private static CrateCourierFeedback _courierFeedback;
        private static CourierUnlockFeedback _courierUnlockFeedback;

        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;
        private static int _courierPadUnlockCount;
        private static int _courierCompletionCount;
        private static int _courierActivationCount;
        private static int _tripStartedEventCount;
        private static int _pickupEventCount;
        private static int _deliveryEventCount;
        private static int _cancelEventCount;
        private static int _packingRecipeEventCount;
        private static int _woodSaleCount;
        private static int _plankSaleCount;
        private static int _crateSaleCount;
        private static int _collectedCash;
        private static int _lastDeliveredCrates;
        private static int _lastDeliveredCash;
        private static int _targetPackingRecipes;
        private static int _targetCompletedTrips;
        private static int _targetCancelledTrips;
        private static int _stageCashStart;
        private static int _stageDeliveredCratesStart;
        private static int _emptyTripStartCount;
        private static int _workerDepositStart;
        private static int _feederTransferStart;
        private static int _processorRecipeStart;
        private static int _packingRecipeStart;
        private static int _courierTripStart;
        private static int _deliveryLifecycleCancelStart;
        private static bool _disableCourierDuringNextDeliveryEvent;
        private static bool _didDisableCourierDuringDeliveryEvent;
        private static int _deliveryCycleCancelStart;
        private static bool _cycleCourierDuringNextDeliveryEvent;
        private static bool _deliveryCallbackCycleStayedClean;
        private static bool _cycleCourierDuringNextCancellationEvent;
        private static bool _didCycleCourierDuringCancellationEvent;
        private static bool _disableCourierDuringNextReservationEvent;
        private static bool _didDisableCourierDuringReservationEvent;
        private static bool _cycleCourierDuringNextTripStartedEvent;
        private static bool _didCycleCourierDuringTripStartedEvent;

        static LumberCampM7PlayModeSmokeTest()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

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

        [MenuItem("Industry Tycoon/Prototype/Run M7 Automated Delivery Smoke Test")]
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
                    "Exit Play Mode before starting the M7 automated-delivery smoke test.");
            }

            if (!commandLine && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new InvalidOperationException($"Missing prototype scene at {ScenePath}.");
            }

            M9EditorSaveUtility.PrepareFreshSmokeTest();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(CommandLineKey, commandLine);
            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
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

                if (Now - _runStartedAt > 180d)
                {
                    throw new InvalidOperationException(
                        "M7 automated-delivery smoke exceeded its 180-second timeout.");
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
            IndustryTycoon.Progression.LumberCampProgressionService m10 =
                Object.FindAnyObjectByType<
                    IndustryTycoon.Progression.LumberCampProgressionService>();
            if (m10 != null) m10.enabled = false;

            if (_runtimeInitialized || !EditorApplication.isPlaying)
            {
                return;
            }

            _playerController = Object.FindAnyObjectByType<CharacterController>();
            _carryStack = Object.FindAnyObjectByType<CarryStack>();
            _wallet = Object.FindAnyObjectByType<Wallet>();
            _cashPile = Object.FindAnyObjectByType<CashPile>();
            _cashCollector = Object.FindAnyObjectByType<CashPileCollector>();
            _salePoint = Object.FindAnyObjectByType<SalePoint>();
            _productionPad = FindPurchasePad(120);
            _workerPad = FindPurchasePad(240);
            _processorPad = FindPurchasePad(360);
            _autoFeederPad = FindPurchasePad(600);
            _packingPad = FindPurchasePad(900);
            _courierPad = FindPurchasePad(1500);
            _workerUnlock = Object.FindAnyObjectByType<FirstWorkerUnlock>();
            _processorUnlock = Object.FindAnyObjectByType<FirstProcessorUnlock>();
            _autoFeederUnlock = Object.FindAnyObjectByType<FirstAutoFeederUnlock>();
            _packingUnlock = Object.FindAnyObjectByType<FirstPackingStationUnlock>();
            _courierUnlock = Object.FindAnyObjectByType<FirstCourierUnlock>();
            _worker = FindSingleIncludingInactive<LumberWorker>();
            _stockpile = Object.FindAnyObjectByType<WoodStockpile>();
            _processor = FindSingleIncludingInactive<WoodProcessor>();
            _autoFeeder = FindSingleIncludingInactive<WoodAutoFeeder>();
            _packingStation =
                FindSingleExactTypeIncludingInactive<PackingStation>();
            _courier = FindSingleIncludingInactive<CrateCourier>();
            _courierFeedback = FindSingleIncludingInactive<CrateCourierFeedback>();
            _courierUnlockFeedback =
                Object.FindAnyObjectByType<CourierUnlockFeedback>();

            Require(_playerController != null
                    && _carryStack != null
                    && _wallet != null
                    && _cashPile != null
                    && _cashCollector != null
                    && _salePoint != null,
                "M7 smoke could not find the accepted player/economy loop.");
            ResourceCollector resourceCollector =
                _carryStack.GetComponent<ResourceCollector>();
            Require(resourceCollector != null,
                "M7 smoke requires the accepted player ResourceCollector.");
            resourceCollector.CancelTransientAttractions();
            resourceCollector.enabled = false;
            Require(_wallet.Balance == 0
                    && _cashPile.StoredCash == 0
                    && _cashCollector.PendingCash == 0
                    && _carryStack.TotalAmount == 0,
                "M7 smoke requires empty Wallet, CashPile, and CarryStack state.");
            Require(_productionPad != null
                    && _workerPad != null
                    && _processorPad != null
                    && _autoFeederPad != null
                    && _packingPad != null
                    && _courierPad != null,
                "M7 smoke could not find all six progression Purchase Pads.");
            Require(_workerUnlock != null
                    && _processorUnlock != null
                    && _autoFeederUnlock != null
                    && _packingUnlock != null
                    && _courierUnlock != null,
                "M7 smoke could not find the full progression unlock chain.");
            Require(_worker != null
                    && _stockpile != null
                    && _processor != null
                    && _autoFeeder != null
                    && _packingStation != null,
                "M7 smoke could not find the accepted factory chain.");
            Require(_courier != null
                    && _courierFeedback != null
                    && _courierUnlockFeedback != null,
                "M7 smoke could not find Courier logic/presentation.");
            Require(_courier.PackingStation == _packingStation
                    && _courier.CashPile == _cashPile
                    && _courier.AcceptedResourceType == ResourceType.Crate
                    && _courier.Capacity == 2
                    && _courier.CashPerCrate == 40,
                "Courier route/product/capacity/cash configuration is incorrect.");
            Require(_salePoint.WoodValue == 5
                    && _salePoint.PlankValue == 15
                    && _salePoint.CrateValue == 40,
                "M7 smoke requires $5 Wood / $15 Plank / $40 Crate manual sales.");
            Require(!_courierPad.StartsAvailable
                    && !_courierPad.IsAvailable
                    && !_courierPad.gameObject.activeSelf
                    && !_courier.gameObject.activeInHierarchy
                    && !_courierUnlock.IsPadUnlocked
                    && !_courierUnlock.IsCourierActivated,
                "Courier must remain fully locked before Packing Station activation.");

            StartedGenerations.Clear();
            DeliveredGenerations.Clear();
            _courierPadUnlockCount = 0;
            _courierCompletionCount = 0;
            _courierActivationCount = 0;
            _tripStartedEventCount = 0;
            _pickupEventCount = 0;
            _deliveryEventCount = 0;
            _cancelEventCount = 0;
            _packingRecipeEventCount = 0;
            _woodSaleCount = 0;
            _plankSaleCount = 0;
            _crateSaleCount = 0;
            _collectedCash = 0;
            _lastDeliveredCrates = 0;
            _lastDeliveredCash = 0;
            _disableCourierDuringNextDeliveryEvent = false;
            _didDisableCourierDuringDeliveryEvent = false;
            _cycleCourierDuringNextDeliveryEvent = false;
            _deliveryCallbackCycleStayedClean = false;
            _cycleCourierDuringNextCancellationEvent = false;
            _didCycleCourierDuringCancellationEvent = false;
            _disableCourierDuringNextReservationEvent = false;
            _didDisableCourierDuringReservationEvent = false;
            _cycleCourierDuringNextTripStartedEvent = false;
            _didCycleCourierDuringTripStartedEvent = false;

            _courierPad.Completed += HandleCourierPurchaseCompleted;
            _courierUnlock.PadUnlocked += HandleCourierPadUnlocked;
            _courierUnlock.CourierActivated += HandleCourierActivated;
            _courier.TripStarted += HandleTripStarted;
            _courier.PickupCompleted += HandlePickupCompleted;
            _courier.DeliveryCompleted += HandleDeliveryCompleted;
            _courier.TripCancelled += HandleTripCancelled;
            _packingStation.RecipeCompleted += HandlePackingRecipeCompleted;
            _packingStation.CourierOutputReservationChanged +=
                HandleCourierOutputReservationChanged;
            _salePoint.UnitSold += HandleUnitSold;
            _cashCollector.CollectionCompleted += HandleCashCollectionCompleted;

            MovePlayerTo(NeutralPosition);
            _stage = Stage.WarmupLocked;
            _stageStartedAt = Now;
            _runStartedAt = Now;
            _runtimeInitialized = true;
        }

        private static void TickCurrentStage()
        {
            switch (_stage)
            {
                case Stage.WarmupLocked:
                    TickWarmupLocked();
                    break;
                case Stage.CompletePrerequisites:
                    TickCompletePrerequisites();
                    break;
                case Stage.PartiallyFundCourier:
                    TickPartiallyFundCourier();
                    break;
                case Stage.CompleteCourierPurchase:
                    TickCompleteCourierPurchase();
                    break;
                case Stage.VerifyEmptyWait:
                    TickVerifyEmptyWait();
                    break;
                case Stage.StartOneCrateTrip:
                    TickStartOneCrateTrip();
                    break;
                case Stage.WaitForOneCrateDelivery:
                    TickWaitForOneCrateDelivery();
                    break;
                case Stage.CollectOneCrateCash:
                    TickCollectOneCrateCash();
                    break;
                case Stage.PrepareCapacityOutput:
                    TickPrepareCapacityOutput();
                    break;
                case Stage.WaitForCapacityOutput:
                    TickWaitForCapacityOutput();
                    break;
                case Stage.WaitForTwoCrateDelivery:
                    TickWaitForTwoCrateDelivery();
                    break;
                case Stage.WaitForCapacityRemainder:
                    TickWaitForCapacityRemainder();
                    break;
                case Stage.PreparePartialPreemption:
                    TickPreparePartialPreemption();
                    break;
                case Stage.WaitForPartialPreemptionOutput:
                    TickWaitForPartialPreemptionOutput();
                    break;
                case Stage.WaitForPartialPreemptionDelivery:
                    TickWaitForPartialPreemptionDelivery();
                    break;
                case Stage.PrepareFullPreemption:
                    TickPrepareFullPreemption();
                    break;
                case Stage.WaitForFullPreemptionOutput:
                    TickWaitForFullPreemptionOutput();
                    break;
                case Stage.WaitForFullPreemptionRecovery:
                    TickWaitForFullPreemptionRecovery();
                    break;
                case Stage.VerifyEmptyAfterPreemption:
                    TickVerifyEmptyAfterPreemption();
                    break;
                case Stage.RefillAfterEmpty:
                    TickRefillAfterEmpty();
                    break;
                case Stage.WaitForRefillDelivery:
                    TickWaitForRefillDelivery();
                    break;
                case Stage.PrepareStationLifecycleOutput:
                    TickPrepareStationLifecycleOutput();
                    break;
                case Stage.WaitForStationLifecycleOutput:
                    TickWaitForStationLifecycleOutput();
                    break;
                case Stage.WaitForStationLifecycleRetry:
                    TickWaitForStationLifecycleRetry();
                    break;
                case Stage.WaitForStationLifecycleDelivery:
                    TickWaitForStationLifecycleDelivery();
                    break;
                case Stage.VerifyTypedManualSales:
                    TickVerifyTypedManualSales();
                    break;
                case Stage.StartSimultaneousChains:
                    TickStartSimultaneousChains();
                    break;
                case Stage.WaitForSimultaneousChains:
                    TickWaitForSimultaneousChains();
                    break;
            }
        }

        private static void TickWarmupLocked()
        {
            if (!HasWaited(0.25d))
            {
                return;
            }

            Require(!_packingUnlock.IsPackingStationActivated
                    && !_courierUnlock.IsPadUnlocked
                    && !_courierUnlock.IsCourierActivated
                    && !_courierPad.IsAvailable
                    && !_courierPad.gameObject.activeSelf
                    && !_courier.gameObject.activeInHierarchy,
                "Courier became available before Packing Station unlock.");
            AdvanceTo(Stage.CompletePrerequisites);
        }

        private static void TickCompletePrerequisites()
        {
            CompletePurchase(_productionPad);
            Require(_workerPad.IsAvailable,
                "Worker pad did not unlock after the production upgrade.");
            CompletePurchase(_workerPad);
            Require(_processorPad.IsAvailable,
                "Processor pad did not unlock after Worker activation.");
            CompletePurchase(_processorPad);
            Require(_autoFeederPad.IsAvailable,
                "Auto Feeder pad did not unlock after Processor activation.");
            CompletePurchase(_autoFeederPad);
            Require(_packingPad.IsAvailable,
                "Packing pad did not unlock after Auto Feeder activation.");
            Require(!_courierUnlock.IsPadUnlocked
                    && !_courierPad.IsAvailable
                    && !_courierPad.gameObject.activeSelf,
                "Courier pad unlocked before Packing Station activation.");

            CompletePurchase(_packingPad);
            Require(_packingUnlock.IsPackingStationActivated
                    && _courierUnlock.IsPadUnlocked
                    && _courierPad.IsAvailable
                    && _courierPad.gameObject.activeSelf
                    && !_courierUnlock.IsCourierActivated
                    && !_courier.gameObject.activeInHierarchy
                    && _courierPadUnlockCount == 1,
                "Packing Station did not reveal exactly one locked Courier purchase.");
            AdvanceTo(Stage.PartiallyFundCourier);
        }

        private static void TickPartiallyFundCourier()
        {
            Require(_wallet.Deposit(65) == 65, "Could not fund Courier partial payment.");
            for (int i = 0; i < 13; i++)
            {
                Require(_courierPad.ProcessPaymentStep() == 5,
                    "Courier pad rejected a valid partial payment tick.");
            }

            Require(_courierPad.RemainingCost == 1435
                    && _wallet.Balance == 0
                    && !_courierPad.IsCompleted,
                "Courier pad did not persist the exact $65 partial payment.");
            _courierPad.enabled = false;
            _courierPad.enabled = true;
            Require(_courierPad.RemainingCost == 1435
                    && _courierPad.IsAvailable,
                "Courier partial funding was lost after leave/re-enter lifecycle.");
            AdvanceTo(Stage.CompleteCourierPurchase);
        }

        private static void TickCompleteCourierPurchase()
        {
            CompletePurchase(_courierPad);
            Require(_courierPad.IsCompleted
                    && _courierCompletionCount == 1
                    && _courierActivationCount == 1
                    && _courierUnlock.IsCourierActivated
                    && _courier.gameObject.activeInHierarchy
                    && _courier.State == CrateCourierState.Wait
                    && _courierFeedback.CargoVisualPoolCount == 2
                    && _courierUnlockFeedback.PresentationCount == 1,
                "Courier purchase/activation/presentation did not complete exactly once.");
            Require(!_courierUnlock.TryUnlockPad()
                    && !_courierUnlock.TryActivateCourier()
                    && _courierPad.ProcessPaymentStep() == 0,
                "Courier unlock or purchase could complete more than once.");
            _emptyTripStartCount = _courier.CompletedTripCount
                                   + _courier.CancelledTripCount;
            AdvanceTo(Stage.VerifyEmptyWait);
        }

        private static void TickVerifyEmptyWait()
        {
            EnsureStageTimeout(3d);
            if (!HasWaited(1.25d))
            {
                return;
            }

            Require(_packingStation.OutputCrates == 0
                    && _packingStation.ReservedCourierOutputCrates == 0
                    && _courier.State == CrateCourierState.Wait
                    && !_courier.HasActiveReservation
                    && !_courier.IsRetryScheduled
                    && _courier.CompletedTripCount + _courier.CancelledTripCount
                       == _emptyTripStartCount,
                "Empty Packing output did not leave Courier cleanly asleep.");
            AdvanceTo(Stage.StartOneCrateTrip);
        }

        private static void TickStartOneCrateTrip()
        {
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            _deliveryLifecycleCancelStart = _courier.CancelledTripCount;
            _disableCourierDuringNextDeliveryEvent = true;
            _stageCashStart = _cashPile.StoredCash;
            BeginPackingRecipes(1);
            AdvanceTo(Stage.WaitForOneCrateDelivery);
        }

        private static void TickWaitForOneCrateDelivery()
        {
            EnsureStageTimeout(20d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_didDisableCourierDuringDeliveryEvent
                    && !_courier.enabled
                    && _courier.CancelledTripCount == _deliveryLifecycleCancelStart
                    && _courier.ActiveTripGeneration == 0,
                "Delivery callback lifecycle interruption did not remain exactly once.");
            _courier.enabled = true;
            Require(_lastDeliveredCrates == 1
                    && _lastDeliveredCash == 40
                    && _cashPile.StoredCash == _stageCashStart + 40
                    && _wallet.Balance == 0
                    && _packingStation.OutputCrates == 0
                    && _courier.CarriedCrates == 0,
                "One-Crate trip did not add exactly $40 to CashPile only.");
            int cashAfterDelivery = _cashPile.StoredCash;
            Require(!_courier.TryCommitDelivery()
                    && _cashPile.StoredCash == cashAfterDelivery,
                "Completed Courier trip credited CashPile twice.");
            AdvanceTo(Stage.CollectOneCrateCash);
        }

        private static void TickCollectOneCrateCash()
        {
            EnsureStageTimeout(4d);
            if (!_cashCollector.IsCollecting && _cashPile.StoredCash > 0)
            {
                MovePlayerTo(_cashPile.transform.position);
                Require(_cashCollector.TryStartCollection(),
                    "Manual CashPile collection did not start.");
                return;
            }

            if (_cashCollector.IsCollecting || _cashCollector.PendingCash > 0)
            {
                return;
            }

            Require(_cashPile.StoredCash == 0
                    && _wallet.Balance == 40
                    && _collectedCash == 40,
                "Wallet changed incorrectly before/after manual $40 CashPile collection.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.PrepareCapacityOutput);
        }

        private static void TickPrepareCapacityOutput()
        {
            _courier.enabled = false;
            Require(_courier.State == CrateCourierState.Disabled
                    && _courier.CarriedCrates == 0
                    && _packingStation.ReservedCourierOutputCrates == 0,
                "Courier did not release idle state before capacity setup.");
            BeginPackingRecipes(3);
            AdvanceTo(Stage.WaitForCapacityOutput);
        }

        private static void TickWaitForCapacityOutput()
        {
            EnsureStageTimeout(8d);
            if (_packingStation.CompletedRecipeCount < _targetPackingRecipes)
            {
                return;
            }

            Require(_packingStation.OutputCrates == 3,
                "Capacity setup did not produce exactly three stored Crates.");
            _stageCashStart = _cashPile.StoredCash;
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            _deliveryCycleCancelStart = _courier.CancelledTripCount;
            _cycleCourierDuringNextDeliveryEvent = true;
            _courier.enabled = true;
            Require(_courier.State == CrateCourierState.MoveToPickup
                    && _courier.ReservedCrates == 2
                    && _packingStation.ReservedCourierOutputCrates == 2,
                "Courier did not cap a 3+ Crate output claim at exactly two.");
            AdvanceTo(Stage.WaitForTwoCrateDelivery);
        }

        private static void TickWaitForTwoCrateDelivery()
        {
            EnsureStageTimeout(20d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_deliveryCallbackCycleStayedClean
                    && _courier.CancelledTripCount == _deliveryCycleCancelStart,
                "Delivery callback disable/enable corrupted the next trip generation.");
            Require(_lastDeliveredCrates == 2
                    && _lastDeliveredCash == 80
                    && _cashPile.StoredCash == _stageCashStart + 80
                    && _packingStation.OutputCrates == 1
                    && _courier.CarriedCrates == 0,
                "Two-Crate trip did not deliver exactly two for $80 or preserve remainder.");
            _stageCashStart = _cashPile.StoredCash;
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            AdvanceTo(Stage.WaitForCapacityRemainder);
        }

        private static void TickWaitForCapacityRemainder()
        {
            EnsureStageTimeout(20d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_lastDeliveredCrates == 1
                    && _lastDeliveredCash == 40
                    && _cashPile.StoredCash == _stageCashStart + 40
                    && _packingStation.OutputCrates == 0,
                "Courier did not repeat cleanly for the one-Crate capacity remainder.");
            AdvanceTo(Stage.PreparePartialPreemption);
        }

        private static void TickPreparePartialPreemption()
        {
            _courier.enabled = false;
            BeginPackingRecipes(2);
            AdvanceTo(Stage.WaitForPartialPreemptionOutput);
        }

        private static void TickWaitForPartialPreemptionOutput()
        {
            EnsureStageTimeout(7d);
            if (_packingStation.CompletedRecipeCount < _targetPackingRecipes)
            {
                return;
            }

            Require(_packingStation.OutputCrates == 2 && _carryStack.TotalAmount == 0,
                "Partial-preemption setup did not preserve two Crates.");
            int preemptionsBefore = _courier.PreemptedCrateCount;
            _stageCashStart = _cashPile.StoredCash;
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            _courier.enabled = true;
            Require(_courier.ReservedCrates == 2
                    && _packingStation.ReservedCourierOutputCrates == 2,
                "Courier did not soft-reserve both contention Crates.");
            Require(_packingStation.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && _packingStation.OutputCrates == 1
                    && _packingStation.ReservedCourierOutputCrates == 1
                    && _courier.ReservedCrates == 1
                    && _courier.PreemptedCrateCount == preemptionsBefore + 1,
                "Player did not preempt one soft-reserved Crate safely.");
            AdvanceTo(Stage.WaitForPartialPreemptionDelivery);
        }

        private static void TickWaitForPartialPreemptionDelivery()
        {
            EnsureStageTimeout(20d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_lastDeliveredCrates == 1
                    && _cashPile.StoredCash == _stageCashStart + 40
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && _packingStation.OutputCrates == 0,
                "Partial preemption duplicated/lost a Crate or changed delivery value.");
            int cashBeforeManualSale = _cashPile.StoredCash;
            Require(_salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _cashPile.StoredCash == cashBeforeManualSale + 40
                    && _crateSaleCount == 1,
                "Player-preempted Crate did not remain manually sellable for $40.");
            AdvanceTo(Stage.PrepareFullPreemption);
        }

        private static void TickPrepareFullPreemption()
        {
            _courier.enabled = false;
            BeginPackingRecipes(1);
            AdvanceTo(Stage.WaitForFullPreemptionOutput);
        }

        private static void TickWaitForFullPreemptionOutput()
        {
            EnsureStageTimeout(5d);
            if (_packingStation.CompletedRecipeCount < _targetPackingRecipes)
            {
                return;
            }

            _stageCashStart = _cashPile.StoredCash;
            _stageDeliveredCratesStart = _courier.TotalDeliveredCrates;
            _targetCancelledTrips = _courier.CancelledTripCount + 1;
            _cycleCourierDuringNextCancellationEvent = true;
            _courier.enabled = true;
            Require(_courier.ReservedCrates == 1
                    && _packingStation.ReservedCourierOutputCrates == 1,
                "Full-preemption setup did not acquire one soft claim.");
            Require(_packingStation.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && _packingStation.OutputCrates == 0
                    && _packingStation.ReservedCourierOutputCrates == 0
                    && _courier.ReservedCrates == 0,
                "Player could not fully preempt Courier before pickup commit.");
            AdvanceTo(Stage.WaitForFullPreemptionRecovery);
        }

        private static void TickWaitForFullPreemptionRecovery()
        {
            EnsureStageTimeout(12d);
            if (_courier.CancelledTripCount < _targetCancelledTrips
                || _courier.State != CrateCourierState.Wait)
            {
                return;
            }

            Require(_didCycleCourierDuringCancellationEvent
                    && _courier.CancelledTripCount == _targetCancelledTrips,
                "Cancellation callback lifecycle resolved one generation more than once.");
            Require(_cashPile.StoredCash == _stageCashStart
                    && _courier.TotalDeliveredCrates == _stageDeliveredCratesStart
                    && _courier.CarriedCrates == 0
                    && !_courier.HasActiveReservation
                    && _carryStack.GetAmount(ResourceType.Crate) == 1,
                "Full player preemption credited cash or lost/duplicated ownership.");
            int cashBeforeManualSale = _cashPile.StoredCash;
            Require(_salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBeforeManualSale + 40
                    && _carryStack.TotalAmount == 0
                    && _crateSaleCount == 2,
                "Fully preempted Crate was not manually sellable for $40.");
            _emptyTripStartCount = _tripStartedEventCount;
            AdvanceTo(Stage.VerifyEmptyAfterPreemption);
        }

        private static void TickVerifyEmptyAfterPreemption()
        {
            EnsureStageTimeout(3d);
            if (!HasWaited(1.25d))
            {
                return;
            }

            Require(_courier.State == CrateCourierState.Wait
                    && _tripStartedEventCount == _emptyTripStartCount
                    && _packingStation.OutputCrates == 0
                    && _packingStation.ReservedCourierOutputCrates == 0,
                "Courier retried/spammed work while Packing output remained empty.");
            AdvanceTo(Stage.RefillAfterEmpty);
        }

        private static void TickRefillAfterEmpty()
        {
            _stageCashStart = _cashPile.StoredCash;
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            BeginPackingRecipes(1);
            AdvanceTo(Stage.WaitForRefillDelivery);
        }

        private static void TickWaitForRefillDelivery()
        {
            EnsureStageTimeout(15d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_lastDeliveredCrates == 1
                    && _cashPile.StoredCash == _stageCashStart + 40
                    && _packingStation.OutputCrates == 0,
                "Courier did not resume automatically after empty-output refill.");
            AdvanceTo(Stage.PrepareStationLifecycleOutput);
        }

        private static void TickPrepareStationLifecycleOutput()
        {
            _courier.enabled = false;
            Require(_courier.State == CrateCourierState.Disabled
                    && _courier.CarriedCrates == 0
                    && _packingStation.ReservedCourierOutputCrates == 0,
                "Courier did not become idle for Packing Station lifecycle setup.");
            BeginPackingRecipes(1);
            AdvanceTo(Stage.WaitForStationLifecycleOutput);
        }

        private static void TickWaitForStationLifecycleOutput()
        {
            EnsureStageTimeout(6d);
            if (_packingStation.CompletedRecipeCount < _targetPackingRecipes)
            {
                return;
            }

            Require(_packingStation.OutputCrates == 1,
                "Packing Station lifecycle setup did not preserve one output Crate.");
            _stageCashStart = _cashPile.StoredCash;
            int tripStartsBeforeReservationCallback = _tripStartedEventCount;
            _disableCourierDuringNextReservationEvent = true;
            _courier.enabled = true;
            Require(_didDisableCourierDuringReservationEvent
                    && !_courier.enabled
                    && _tripStartedEventCount == tripStartsBeforeReservationCallback
                    && _packingStation.OutputCrates == 1
                    && _packingStation.ReservedCourierOutputCrates == 0,
                "Reservation callback disable leaked or adopted a claim while disabled.");

            _cycleCourierDuringNextTripStartedEvent = true;
            _courier.enabled = true;
            Require(_didCycleCourierDuringTripStartedEvent
                    && _courier.enabled
                    && _courier.ActiveTripGeneration != 0
                    && _courier.ReservedCrates == 1
                    && _packingStation.ReservedCourierOutputCrates == 1,
                "Trip-start callback lifecycle did not recover a fresh claim.");
            _targetCancelledTrips = _courier.CancelledTripCount + 1;
            _packingStation.enabled = false;
            Require(!_packingStation.isActiveAndEnabled
                    && _packingStation.OutputCrates == 1
                    && _packingStation.ReservedCourierOutputCrates == 0
                    && _courier.ReservedCrates == 0,
                "Packing Station disable did not invalidate only the soft claim.");
            AdvanceTo(Stage.WaitForStationLifecycleRetry);
        }

        private static void TickWaitForStationLifecycleRetry()
        {
            EnsureStageTimeout(18d);
            if (_courier.CancelledTripCount < _targetCancelledTrips
                || _courier.State != CrateCourierState.Wait
                || _courier.IsRetryScheduled)
            {
                return;
            }

            Require(!_packingStation.isActiveAndEnabled
                    && _packingStation.OutputCrates == 1
                    && _courier.CarriedCrates == 0
                    && !_courier.HasActiveReservation
                    && _cashPile.StoredCash == _stageCashStart,
                "Disabled Packing Station lost output or credited a cancelled pickup.");
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            _packingStation.enabled = true;
            Require(_courier.State == CrateCourierState.MoveToPickup
                    && _courier.ReservedCrates == 1
                    && _packingStation.ReservedCourierOutputCrates == 1,
                "Packing Station re-enable did not wake Courier for existing output.");
            AdvanceTo(Stage.WaitForStationLifecycleDelivery);
        }

        private static void TickWaitForStationLifecycleDelivery()
        {
            EnsureStageTimeout(18d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_lastDeliveredCrates == 1
                    && _lastDeliveredCash == 40
                    && _cashPile.StoredCash == _stageCashStart + 40
                    && _packingStation.OutputCrates == 0
                    && _courier.CarriedCrates == 0,
                "Courier did not recover delivery after Packing Station re-enable.");
            AdvanceTo(Stage.VerifyTypedManualSales);
        }

        private static void TickVerifyTypedManualSales()
        {
            Require(_carryStack.TotalAmount == 0
                    && _courier.CarriedCrates == 0,
                "Typed-sale test requires empty player/Courier cargo.");
            int cashBefore = _cashPile.StoredCash;
            Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                    && _salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBefore + 5
                    && _woodSaleCount == 1,
                "Wood manual selling regressed from $5 while Courier was active.");
            cashBefore = _cashPile.StoredCash;
            Require(_carryStack.TryAdd(ResourceType.Plank, 1)
                    && _salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBefore + 15
                    && _plankSaleCount == 1,
                "Plank manual selling regressed from $15 while Courier was active.");
            Require(_courier.AcceptedResourceType == ResourceType.Crate
                    && _courier.CarriedCrates == 0
                    && _carryStack.TotalAmount == 0,
                "Courier touched player Wood/Plank cargo or accepted another product.");
            AdvanceTo(Stage.StartSimultaneousChains);
        }

        private static void TickStartSimultaneousChains()
        {
            _workerDepositStart = _worker.CompletedDepositCount;
            _feederTransferStart = _autoFeeder.CompletedTransferCount;
            _processorRecipeStart = _processor.CompletedRecipeCount;
            _packingRecipeStart = _packingStation.CompletedRecipeCount;
            _courierTripStart = _courier.CompletedTripCount;
            _targetPackingRecipes = _packingRecipeStart + 2;
            _worker.enabled = true;
            _autoFeeder.enabled = true;
            AdvanceTo(Stage.WaitForSimultaneousChains);
        }

        private static void TickWaitForSimultaneousChains()
        {
            EnsureStageTimeout(50d);
            RelayAvailablePlanksToPacking();

            bool workerAdvanced = _worker.CompletedDepositCount
                                  >= _workerDepositStart + 2;
            bool feederAdvanced = _autoFeeder.CompletedTransferCount
                                  >= _feederTransferStart + 2;
            bool processorAdvanced = _processor.CompletedRecipeCount
                                     >= _processorRecipeStart + 2;
            bool packingAdvanced = _packingStation.CompletedRecipeCount
                                   >= _targetPackingRecipes;
            bool courierAdvanced = _courier.CompletedTripCount
                                   >= _courierTripStart + 2;
            if (!workerAdvanced
                || !feederAdvanced
                || !processorAdvanced
                || !packingAdvanced
                || !courierAdvanced)
            {
                return;
            }

            Require(_courier.CompletedTripCount >= 7
                    && _courier.CancelledTripCount >= 1
                    && _courier.CompletedPickupCount == _courier.CompletedTripCount
                    && _courier.TotalDeliveredCash
                       == _courier.TotalDeliveredCrates * 40
                    && _courierFeedback.PickupFeedbackCount
                       == _courier.CompletedPickupCount
                    && _courierFeedback.DeliveryFeedbackCount
                       == _courier.CompletedTripCount,
                "Repeated Courier cycles or feedback counters were unstable.");
            Require(_courierPadUnlockCount == 1
                    && _courierCompletionCount == 1
                    && _courierActivationCount == 1
                    && _courierUnlockFeedback.PresentationCount == 1
                    && _packingRecipeEventCount
                       == _packingStation.CompletedRecipeCount
                    && _woodSaleCount == 1
                    && _plankSaleCount == 1
                    && _crateSaleCount == 2,
                "M7 unlock/recipe/manual-sale events were not exactly once.");
            ValidateContinuousInvariants();
            Pass(
                $"M7 Automated Delivery Play Mode smoke passed: {_courier.CompletedTripCount} completed trips, {_courier.CancelledTripCount} safe cancellation recoveries, {_courier.TotalDeliveredCrates} Crates delivered for ${_courier.TotalDeliveredCash}, exact $5/$15/$40 manual sales, and simultaneous Worker/Feeder/Processor/Packing/Courier operation.");
        }

        private static void BeginPackingRecipes(int crateCount)
        {
            Require(crateCount > 0
                    && crateCount * _packingStation.RecipeInputPlanks
                       <= _carryStack.Capacity
                    && _carryStack.TotalAmount == 0,
                $"M7 could not prepare an isolated manual Packing batch in {_stage}: "
                + $"requested={crateCount}, capacity={_carryStack.Capacity}, "
                + $"carried={_carryStack.TotalAmount}.");
            int plankCount = crateCount * _packingStation.RecipeInputPlanks;
            _targetPackingRecipes = _packingStation.CompletedRecipeCount + crateCount;
            Require(_carryStack.TryAdd(ResourceType.Plank, plankCount),
                "M7 could not prepare manual Planks for Packing.");
            for (int i = 0; i < plankCount; i++)
            {
                Require(_packingStation.TryTransferInputFrom(_carryStack),
                    "Packing Station rejected a valid manual Plank during M7.");
            }

            Require(_carryStack.TotalAmount == 0,
                "Manual Packing setup retained transferred Planks in CarryStack.");
        }

        private static void RelayAvailablePlanksToPacking()
        {
            if (_packingStation.CompletedRecipeCount >= _targetPackingRecipes
                || _carryStack.TotalAmount != 0)
            {
                return;
            }

            int recipesStillNeeded = _targetPackingRecipes
                                     - _packingStation.CompletedRecipeCount;
            int ownedPackingPlanks = _packingStation.InputPlanks
                                     + _packingStation.ProcessingInputPlanks;
            int planksStillNeeded = Mathf.Max(
                0,
                recipesStillNeeded * _packingStation.RecipeInputPlanks
                - ownedPackingPlanks);
            int guard = 0;
            while (planksStillNeeded > 0
                   && _processor.OutputPlanks > 0
                   && guard++ < _carryStack.Capacity)
            {
                Require(_processor.TryTransferOutputTo(_carryStack)
                        && _packingStation.TryTransferInputFrom(_carryStack),
                    "Manual Processor -> Packing Plank relay failed during simultaneous chains.");
                planksStillNeeded--;
            }
        }

        private static void ValidateContinuousInvariants()
        {
            if (_carryStack == null || _packingStation == null || _courier == null)
            {
                return;
            }

            int wood = _carryStack.GetAmount(ResourceType.Wood);
            int planks = _carryStack.GetAmount(ResourceType.Plank);
            int crates = _carryStack.GetAmount(ResourceType.Crate);
            int activeTypes = (wood > 0 ? 1 : 0)
                              + (planks > 0 ? 1 : 0)
                              + (crates > 0 ? 1 : 0);
            Require(wood >= 0
                    && planks >= 0
                    && crates >= 0
                    && wood + planks + crates == _carryStack.TotalAmount
                    && _carryStack.TotalAmount <= _carryStack.Capacity
                    && activeTypes <= 1,
                "CarryStack became negative, mixed, duplicated, or over capacity in M7.");
            Require(_packingStation.OutputCrates >= 0
                    && _packingStation.OutputCrates
                       + _packingStation.ReservedOutputCapacity
                       <= _packingStation.OutputCapacity
                    && _packingStation.ReservedCourierOutputCrates >= 0
                    && _packingStation.ReservedCourierOutputCrates <= 2
                    && _packingStation.ReservedCourierOutputCrates
                       <= _packingStation.OutputCrates,
                "Packing output/soft reservation became negative or exceeded ownership.");
            Require(_courier.ReservedCrates >= 0
                    && _courier.ReservedCrates <= _courier.Capacity
                    && _courier.CarriedCrates >= 0
                    && _courier.CarriedCrates <= _courier.Capacity
                    && (_courier.ReservedCrates == 0 || _courier.CarriedCrates == 0),
                "Courier exceeded capacity or owned claim and cargo simultaneously.");

            int activeTrips = _courier.ActiveTripGeneration != 0 ? 1 : 0;
            Require(_tripStartedEventCount
                    == _courier.CompletedTripCount
                       + _courier.CancelledTripCount
                       + activeTrips,
                "Courier trip generations were lost, duplicated, or resolved twice.");
            Require(_pickupEventCount == _courier.CompletedPickupCount
                    && _deliveryEventCount == _courier.CompletedTripCount
                    && _cancelEventCount == _courier.CancelledTripCount
                    && _courier.CompletedPickupCount
                       == _courier.CompletedTripCount
                          + (_courier.CarriedCrates > 0 ? 1 : 0),
                "Courier pickup/delivery/cancellation counters diverged.");

            int accountedCrates = _packingStation.OutputCrates
                                  + _courier.CarriedCrates
                                  + crates
                                  + _crateSaleCount
                                  + _courier.TotalDeliveredCrates;
            Require(accountedCrates == _packingStation.CompletedRecipeCount,
                "Packing -> player/Courier ownership lost or duplicated Crates.");

            int generatedCash = _courier.TotalDeliveredCash
                                + (_woodSaleCount * 5)
                                + (_plankSaleCount * 15)
                                + (_crateSaleCount * 40);
            int ownedCash = _wallet.Balance
                            + _cashPile.StoredCash
                            + _cashCollector.PendingCash;
            Require(generatedCash == ownedCash
                    && _wallet.Balance == _collectedCash,
                "Delivery/manual-sale Cash bypassed CashPile or changed Wallet implicitly.");

            Require(_stockpile.StoredWood >= 0
                    && _stockpile.IncomingReservations >= 0
                    && _stockpile.OutgoingReservations >= 0
                    && _stockpile.StoredWood
                       + _stockpile.IncomingReservations
                       + _stockpile.OutgoingReservations <= _stockpile.Capacity,
                "M1-M5 Stockpile ownership regressed during M7.");
            Require(_processor.InputWood >= 0
                    && _processor.ReservedInputCapacity >= 0
                    && _processor.InputWood + _processor.ReservedInputCapacity
                       <= _processor.InputCapacity
                    && _processor.OutputPlanks >= 0
                    && _processor.OutputPlanks + _processor.ReservedOutputCapacity
                       <= _processor.OutputCapacity
                    && _autoFeeder.ActiveTransferCount <= 1,
                "M4-M5 Processor/Feeder ownership regressed during M7.");
        }

        private static void CompletePurchase(PurchasePad pad)
        {
            Require(pad != null && !pad.IsCompleted && pad.IsAvailable,
                "M7 tried to fund an unavailable/completed progression pad.");
            int expectedAmount = pad.RemainingCost;
            Require(_wallet.Deposit(expectedAmount) == expectedAmount,
                $"Could not fund {pad.PurchaseLabel}.");
            int totalPaid = 0;
            int guard = 0;
            while (!pad.IsCompleted && guard++ < 400)
            {
                totalPaid += pad.ProcessPaymentStep();
            }

            Require(pad.IsCompleted
                    && pad.RemainingCost == 0
                    && totalPaid == expectedAmount
                    && _wallet.Balance == 0,
                $"{pad.PurchaseLabel} paid ${totalPaid}; expected ${expectedAmount}.");
        }

        private static PurchasePad FindPurchasePad(int totalCost)
        {
            PurchasePad[] pads = Object.FindObjectsByType<PurchasePad>(
                FindObjectsInactive.Include);
            PurchasePad result = null;
            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i].TotalCost != totalCost)
                {
                    continue;
                }

                Require(result == null,
                    $"M7 smoke found more than one ${totalCost} Purchase Pad.");
                result = pads[i];
            }

            return result;
        }

        private static T FindSingleIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(
                FindObjectsInactive.Include);
            Require(matches.Length == 1,
                $"M7 smoke expected one {typeof(T).Name}, found {matches.Length}.");
            return matches[0];
        }

        private static T FindSingleExactTypeIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(
                FindObjectsInactive.Include);
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
                $"M7 smoke expected one concrete {typeof(T).Name}, found {exactMatchCount}.");
            return result;
        }

        private static void MovePlayerTo(Vector3 position)
        {
            Vector3 planarPosition = new Vector3(position.x, 0f, position.z);
            _playerController.Move(planarPosition - _playerController.transform.position);
            Physics.SyncTransforms();
        }

        private static void HandleCourierPadUnlocked()
        {
            _courierPadUnlockCount++;
        }

        private static void HandleCourierPurchaseCompleted()
        {
            _courierCompletionCount++;
        }

        private static void HandleCourierActivated()
        {
            _courierActivationCount++;
        }

        private static void HandleTripStarted(uint generation, int reservedCrates)
        {
            Require(generation != 0
                    && reservedCrates >= 1
                    && reservedCrates <= 2
                    && StartedGenerations.Add(generation),
                "Courier started a duplicate generation or exceeded two-Crate capacity.");
            _tripStartedEventCount++;
            if (_cycleCourierDuringNextTripStartedEvent)
            {
                _cycleCourierDuringNextTripStartedEvent = false;
                _didCycleCourierDuringTripStartedEvent = true;
                _courier.enabled = false;
                _courier.enabled = true;
            }
        }

        private static void HandlePickupCompleted(uint generation, int crateCount)
        {
            Require(StartedGenerations.Contains(generation)
                    && crateCount >= 1
                    && crateCount <= 2
                    && _courier.CarriedCrates == crateCount,
                "Courier pickup event preceded valid authoritative cargo ownership.");
            _pickupEventCount++;
        }

        private static void HandleDeliveryCompleted(
            uint generation,
            int crateCount,
            int cashValue)
        {
            Require(StartedGenerations.Contains(generation)
                    && DeliveredGenerations.Add(generation)
                    && crateCount >= 1
                    && crateCount <= 2
                    && cashValue == crateCount * 40
                    && _cashPile.StoredCash >= cashValue
                    && _courier.CarriedCrates == 0,
                "Courier delivery was duplicated, mispriced, or published before commit.");
            _lastDeliveredCrates = crateCount;
            _lastDeliveredCash = cashValue;
            _deliveryEventCount++;
            if (_disableCourierDuringNextDeliveryEvent)
            {
                _disableCourierDuringNextDeliveryEvent = false;
                _didDisableCourierDuringDeliveryEvent = true;
                _courier.enabled = false;
            }

            if (_cycleCourierDuringNextDeliveryEvent)
            {
                _cycleCourierDuringNextDeliveryEvent = false;
                _courier.enabled = false;
                _courier.enabled = true;
                _deliveryCallbackCycleStayedClean = _courier.enabled
                                                     && _courier.ActiveTripGeneration == 0
                                                     && _courier.ReservedCrates == 0
                                                     && _courier.CarriedCrates == 0;
            }
        }

        private static void HandleTripCancelled(uint generation)
        {
            Require(StartedGenerations.Contains(generation)
                    && !DeliveredGenerations.Contains(generation),
                "Courier cancelled an unknown or already-delivered generation.");
            _cancelEventCount++;
            if (_cycleCourierDuringNextCancellationEvent)
            {
                _cycleCourierDuringNextCancellationEvent = false;
                _didCycleCourierDuringCancellationEvent = true;
                _courier.enabled = false;
                _courier.enabled = true;
            }
        }

        private static void HandleCourierOutputReservationChanged(int reservedCrates)
        {
            if (!_disableCourierDuringNextReservationEvent || reservedCrates <= 0)
            {
                return;
            }

            _disableCourierDuringNextReservationEvent = false;
            _didDisableCourierDuringReservationEvent = true;
            _courier.enabled = false;
        }

        private static void HandlePackingRecipeCompleted(int inputPlanks, int outputCrates)
        {
            Require(inputPlanks == _packingStation.InputPlanks
                    && outputCrates == _packingStation.OutputCrates,
                "Packing recipe event preceded authoritative output state.");
            _packingRecipeEventCount++;
        }

        private static void HandleUnitSold(SaleFeedbackData feedback)
        {
            Require(feedback.RemainingAmount
                    == _carryStack.GetAmount(feedback.ResourceType),
                "Sale event preceded authoritative CarryStack removal.");
            switch (feedback.ResourceType)
            {
                case ResourceType.Wood:
                    Require(feedback.CashValue == 5, "Wood sale value regressed from $5.");
                    _woodSaleCount++;
                    break;
                case ResourceType.Plank:
                    Require(feedback.CashValue == 15, "Plank sale value regressed from $15.");
                    _plankSaleCount++;
                    break;
                case ResourceType.Crate:
                    Require(feedback.CashValue == 40, "Crate sale value regressed from $40.");
                    _crateSaleCount++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Sale Point sold unsupported resource {feedback.ResourceType}.");
            }
        }

        private static void HandleCashCollectionCompleted(int collectedCash)
        {
            _collectedCash += collectedCash;
            Require(_wallet.Balance == _collectedCash,
                "Cash collection event preceded Wallet authoritative state.");
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
                throw new InvalidOperationException($"M7 smoke timed out in stage {_stage}.");
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
            string result = $"M7 Automated Delivery Play Mode smoke failed: {message}";
            Debug.LogError(result);
            EndRun(false, result);
        }

        private static void EndRun(bool success, string message)
        {
            UnsubscribeEvents();
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(FinishPendingKey, true);
            SessionState.SetBool(SuccessKey, success);
            SessionState.SetString(ResultMessageKey, message);
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

        private static void UnsubscribeEvents()
        {
            if (_courierPad != null)
            {
                _courierPad.Completed -= HandleCourierPurchaseCompleted;
            }

            if (_courierUnlock != null)
            {
                _courierUnlock.PadUnlocked -= HandleCourierPadUnlocked;
                _courierUnlock.CourierActivated -= HandleCourierActivated;
            }

            if (_courier != null)
            {
                _courier.TripStarted -= HandleTripStarted;
                _courier.PickupCompleted -= HandlePickupCompleted;
                _courier.DeliveryCompleted -= HandleDeliveryCompleted;
                _courier.TripCancelled -= HandleTripCancelled;
            }

            if (_packingStation != null)
            {
                _packingStation.RecipeCompleted -= HandlePackingRecipeCompleted;
                _packingStation.CourierOutputReservationChanged -=
                    HandleCourierOutputReservationChanged;
            }

            if (_salePoint != null)
            {
                _salePoint.UnitSold -= HandleUnitSold;
            }

            if (_cashCollector != null)
            {
                _cashCollector.CollectionCompleted -= HandleCashCollectionCompleted;
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
