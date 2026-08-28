using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
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
    [InitializeOnLoad]
    public static class LumberCampM8PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey = "IndustryTycoon.M8.Smoke.Running";
        private const string CommandLineKey = "IndustryTycoon.M8.Smoke.CommandLine";
        private const string FinishPendingKey = "IndustryTycoon.M8.Smoke.FinishPending";
        private const string SuccessKey = "IndustryTycoon.M8.Smoke.Success";
        private const string ResultMessageKey = "IndustryTycoon.M8.Smoke.ResultMessage";
        private static readonly Vector3 NeutralPosition = new Vector3(0f, 0f, -11f);

        private static readonly LumberCampProgressStage[] PurchaseStages =
        {
            LumberCampProgressStage.ProductionUpgrade,
            LumberCampProgressStage.Worker,
            LumberCampProgressStage.Processor,
            LumberCampProgressStage.AutoFeeder,
            LumberCampProgressStage.PackingStation,
            LumberCampProgressStage.Courier
        };

        private static readonly int[] PurchaseCosts =
        {
            120,
            240,
            360,
            600,
            900,
            1500
        };

        private enum Stage
        {
            Warmup,
            VerifyFirstPickupAndSale,
            CompleteProductionUpgrade,
            CompleteWorker,
            CompleteProcessor,
            CompleteAutoFeeder,
            CompletePackingStation,
            CompleteCourier,
            VerifyCourierPurchaseOnly,
            StartFirstDelivery,
            WaitForFirstDelivery,
            StartLaterDelivery,
            WaitForLaterDelivery,
            VerifyPostCompletionPlay,
            VerifyProbeReset
        }

        private static CharacterController _playerController;
        private static CarryStack _carryStack;
        private static ResourceCollector _resourceCollector;
        private static Wallet _wallet;
        private static CashPile _cashPile;
        private static SalePoint _salePoint;
        private static PurchasePad[] _purchasePads;
        private static WoodProductionUpgrade _productionUpgrade;
        private static FirstWorkerUnlock _workerUnlock;
        private static FirstProcessorUnlock _processorUnlock;
        private static FirstAutoFeederUnlock _autoFeederUnlock;
        private static FirstPackingStationUnlock _packingUnlock;
        private static FirstCourierUnlock _courierUnlock;
        private static LumberWorker _worker;
        private static WoodProcessor _processor;
        private static WoodAutoFeeder _autoFeeder;
        private static PackingStation _packingStation;
        private static CrateCourier _courier;
        private static NextUnlockGuidance _guidance;
        private static LumberCampCompletion _completion;
        private static LumberCampCompletionFeedback _completionFeedback;
        private static LumberCampPacingProbe _pacingProbe;

        private static readonly HashSet<uint> DeliveredGenerations = new HashSet<uint>();
        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;
        private static int _completionEventCount;
        private static int _deliveryEventCount;
        private static int _lastDeliveredCrates;
        private static int _lastDeliveredCash;
        private static int _targetCompletedTrips;
        private static int _targetPackingRecipes;
        private static int _cashBeforePostCompletionSale;
        private static Vector3 _positionBeforePostCompletionMove;

        static LumberCampM8PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/Prototype/Run M8 Completion Pacing Smoke Test")]
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
                    "Exit Play Mode before starting the M8 completion/pacing smoke test.");
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

                if (Now - _runStartedAt > 90d)
                {
                    throw new InvalidOperationException(
                        "M8 completion/pacing smoke exceeded its 90-second timeout.");
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

            _playerController = Object.FindAnyObjectByType<CharacterController>();
            _carryStack = Object.FindAnyObjectByType<CarryStack>();
            _resourceCollector = _carryStack != null
                ? _carryStack.GetComponent<ResourceCollector>()
                : null;
            _wallet = Object.FindAnyObjectByType<Wallet>();
            _cashPile = Object.FindAnyObjectByType<CashPile>();
            _salePoint = Object.FindAnyObjectByType<SalePoint>();
            _productionUpgrade = Object.FindAnyObjectByType<WoodProductionUpgrade>();
            _workerUnlock = Object.FindAnyObjectByType<FirstWorkerUnlock>();
            _processorUnlock = Object.FindAnyObjectByType<FirstProcessorUnlock>();
            _autoFeederUnlock = Object.FindAnyObjectByType<FirstAutoFeederUnlock>();
            _packingUnlock = Object.FindAnyObjectByType<FirstPackingStationUnlock>();
            _courierUnlock = Object.FindAnyObjectByType<FirstCourierUnlock>();
            _worker = FindSingleIncludingInactive<LumberWorker>();
            _processor = FindSingleIncludingInactive<WoodProcessor>();
            _autoFeeder = FindSingleIncludingInactive<WoodAutoFeeder>();
            _packingStation = FindSingleIncludingInactive<PackingStation>();
            _courier = FindSingleIncludingInactive<CrateCourier>();
            _guidance = Object.FindAnyObjectByType<NextUnlockGuidance>();
            _completion = Object.FindAnyObjectByType<LumberCampCompletion>();
            _completionFeedback = Object.FindAnyObjectByType<LumberCampCompletionFeedback>();
            _pacingProbe = Object.FindAnyObjectByType<LumberCampPacingProbe>();

            _purchasePads = new PurchasePad[PurchaseCosts.Length];
            for (int i = 0; i < PurchaseCosts.Length; i++)
            {
                _purchasePads[i] = FindPurchasePad(PurchaseCosts[i]);
            }

            Require(_playerController != null
                    && _carryStack != null
                    && _resourceCollector != null
                    && _wallet != null
                    && _cashPile != null
                    && _salePoint != null,
                "M8 smoke could not find the accepted player/economy loop.");
            Require(_productionUpgrade != null
                    && _workerUnlock != null
                    && _processorUnlock != null
                    && _autoFeederUnlock != null
                    && _packingUnlock != null
                    && _courierUnlock != null,
                "M8 smoke could not find the complete authoritative unlock chain.");
            Require(_worker != null
                    && _processor != null
                    && _autoFeeder != null
                    && _packingStation != null
                    && _courier != null,
                "M8 smoke could not find the complete factory chain.");
            Require(_guidance != null
                    && _completion != null
                    && _completionFeedback != null
                    && _pacingProbe != null,
                "M8 smoke could not find guidance, completion, feedback, or pacing probe.");
            Require(_completion.CourierUnlock == _courierUnlock
                    && _completion.Courier == _courier
                    && _completion.MineTeaserRoot != null,
                "M8 completion is not wired to the authoritative Courier and Mine teaser.");
            Require(_guidance.ProductionUpgrade == _productionUpgrade
                    && _guidance.WorkerUnlock == _workerUnlock
                    && _guidance.ProcessorUnlock == _processorUnlock
                    && _guidance.AutoFeederUnlock == _autoFeederUnlock
                    && _guidance.PackingStationUnlock == _packingUnlock
                    && _guidance.CourierUnlock == _courierUnlock
                    && _guidance.Completion == _completion,
                "M8 guidance does not derive from the accepted progression chain.");
            Require(_pacingProbe.CarryStack == _carryStack
                    && _pacingProbe.SalePoint == _salePoint
                    && _pacingProbe.ProductionUpgrade == _productionUpgrade
                    && _pacingProbe.WorkerUnlock == _workerUnlock
                    && _pacingProbe.ProcessorUnlock == _processorUnlock
                    && _pacingProbe.AutoFeederUnlock == _autoFeederUnlock
                    && _pacingProbe.PackingStationUnlock == _packingUnlock
                    && _pacingProbe.CourierUnlock == _courierUnlock
                    && _pacingProbe.Courier == _courier
                    && _pacingProbe.Completion == _completion,
                "M8 pacing probe does not use the authoritative gameplay events.");

            DeliveredGenerations.Clear();
            _completionEventCount = 0;
            _deliveryEventCount = 0;
            _lastDeliveredCrates = 0;
            _lastDeliveredCash = 0;
            _completion.Completed += HandleLumberCampCompleted;
            _courier.DeliveryCompleted += HandleCourierDeliveryCompleted;

            MovePlayerTo(NeutralPosition);
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
                case Stage.VerifyFirstPickupAndSale:
                    TickFirstPickupAndSale();
                    break;
                case Stage.CompleteProductionUpgrade:
                    TickPurchaseStage(0, Stage.CompleteWorker);
                    break;
                case Stage.CompleteWorker:
                    TickPurchaseStage(1, Stage.CompleteProcessor);
                    break;
                case Stage.CompleteProcessor:
                    TickPurchaseStage(2, Stage.CompleteAutoFeeder);
                    break;
                case Stage.CompleteAutoFeeder:
                    TickPurchaseStage(3, Stage.CompletePackingStation);
                    break;
                case Stage.CompletePackingStation:
                    TickPurchaseStage(4, Stage.CompleteCourier);
                    break;
                case Stage.CompleteCourier:
                    TickPurchaseStage(5, Stage.VerifyCourierPurchaseOnly);
                    break;
                case Stage.VerifyCourierPurchaseOnly:
                    TickVerifyCourierPurchaseOnly();
                    break;
                case Stage.StartFirstDelivery:
                    TickStartFirstDelivery();
                    break;
                case Stage.WaitForFirstDelivery:
                    TickWaitForFirstDelivery();
                    break;
                case Stage.StartLaterDelivery:
                    TickStartLaterDelivery();
                    break;
                case Stage.WaitForLaterDelivery:
                    TickWaitForLaterDelivery();
                    break;
                case Stage.VerifyPostCompletionPlay:
                    TickPostCompletionPlay();
                    break;
                case Stage.VerifyProbeReset:
                    TickProbeReset();
                    break;
            }
        }

        private static void TickWarmup()
        {
            if (!HasWaited(0.25d))
            {
                return;
            }

            Require(_wallet.Balance == 0
                    && _cashPile.StoredCash == 0
                    && _carryStack.TotalAmount == 0,
                "M8 smoke requires an empty fresh-session economy.");
            Require(!_completion.IsCompleted
                    && _completion.CompletionCount == 0
                    && _completionEventCount == 0
                    && !_completion.MineTeaserRoot.activeSelf
                    && _completionFeedback.PresentationCount == 0
                    && !_completionFeedback.BannerRoot.activeSelf,
                "Lumber Camp completion/Mine presentation did not begin hidden and idle.");
            Require(_pacingProbe.RecordedMilestoneCount == 1
                    && _pacingProbe.AutomaticReportCount == 0
                    && _pacingProbe.HasTimestamp(LumberCampPacingMilestone.SessionStart)
                    && Math.Abs(_pacingProbe.GetElapsedSeconds(
                        LumberCampPacingMilestone.SessionStart)) < 0.0001d,
                "Pacing probe did not begin with exactly one zero-time session milestone.");
            Require(!_pacingProbe.HasTimestamp(LumberCampPacingMilestone.FirstWoodPickup)
                    && !_pacingProbe.HasTimestamp(LumberCampPacingMilestone.FirstSale),
                "Pacing probe retained pickup/sale state before the fresh run.");
            ValidateGuidance(0, 0);
            AssertFuturePadsLocked(0, false);
            Require(!_completion.TryComplete(),
                "Lumber Camp completed without a Courier purchase and delivery.");
            AdvanceTo(Stage.VerifyFirstPickupAndSale);
        }

        private static void TickFirstPickupAndSale()
        {
            int cashBeforeSale = _cashPile.StoredCash;
            Require(_carryStack.TryAdd(ResourceType.Wood, 1),
                "M8 smoke could not put one Wood into the authoritative CarryStack.");
            Require(_pacingProbe.HasTimestamp(LumberCampPacingMilestone.FirstWoodPickup)
                    && !_pacingProbe.HasTimestamp(LumberCampPacingMilestone.FirstSale),
                "Pacing probe did not capture first Wood pickup before first sale.");
            Require(_salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _cashPile.StoredCash == cashBeforeSale + _salePoint.WoodValue,
                "Fresh-session Wood could not be sold through the accepted SalePoint.");
            Require(_pacingProbe.HasTimestamp(LumberCampPacingMilestone.FirstSale)
                    && _pacingProbe.AreRecordedTimestampsOrdered(),
                "Pacing probe did not capture a valid ordered first sale.");

            // The remaining M8 stages inject only the exact typed resources needed
            // for their focused assertions. Cancel any real pickup that was reserved
            // during scene startup so random spawn placement cannot contaminate them.
            _resourceCollector.enabled = false;
            Require(_resourceCollector.ReservedCapacity == 0
                    && _carryStack.ReservedCapacity == 0,
                "M8 smoke could not isolate later typed-resource assertions.");
            AdvanceTo(Stage.CompleteProductionUpgrade);
        }

        private static void TickPurchaseStage(int purchaseIndex, Stage nextTestStage)
        {
            PurchasePad pad = _purchasePads[purchaseIndex];
            ValidateGuidance(purchaseIndex, 0);
            AssertFuturePadsLocked(purchaseIndex, true);
            Require(_wallet.Balance == 0,
                $"Wallet was not empty before testing {pad.PurchaseLabel}.");

            int partialPayment = Mathf.Min(pad.SpendPerTick, pad.TotalCost - 1);
            Require(_wallet.Deposit(partialPayment) == partialPayment,
                $"Could not fund the partial {pad.PurchaseLabel} payment.");
            AssertFuturePadsRejectPayment(purchaseIndex, partialPayment);
            Require(pad.ProcessPaymentStep() == partialPayment
                    && _wallet.Balance == 0
                    && pad.RemainingCost == pad.TotalCost - partialPayment
                    && !pad.IsCompleted,
                $"{pad.PurchaseLabel} did not retain authoritative partial progress.");
            ValidateGuidance(purchaseIndex, partialPayment);

            bool verifyProbeCatchUp = purchaseIndex == 2;
            if (verifyProbeCatchUp)
            {
                Require(!_pacingProbe.HasTimestamp(LumberCampPacingMilestone.Processor),
                    "Pacing probe recorded Processor before its authoritative activation.");
                _pacingProbe.enabled = false;
                Require(!_pacingProbe.enabled,
                    "M8 smoke could not pause the pacing probe for lifecycle catch-up.");
            }

            int remainingCost = pad.RemainingCost;
            Require(_wallet.Deposit(remainingCost) == remainingCost,
                $"Could not fund the remaining {pad.PurchaseLabel} cost.");
            int paid = 0;
            int guard = 0;
            while (!pad.IsCompleted && guard++ < 400)
            {
                paid += pad.ProcessPaymentStep();
            }

            Require(pad.IsCompleted
                    && pad.RemainingCost == 0
                    && paid == remainingCost
                    && _wallet.Balance == 0
                    && pad.ProcessPaymentStep() == 0,
                $"{pad.PurchaseLabel} did not complete exactly once at ${pad.TotalCost}.");
            Require(IsPurchaseActivationAuthoritative(purchaseIndex),
                $"{pad.PurchaseLabel} completed without activating its authoritative stage.");
            if (verifyProbeCatchUp)
            {
                Require(!_pacingProbe.HasTimestamp(LumberCampPacingMilestone.Processor),
                    "Disabled pacing probe continued receiving Processor events.");
                _pacingProbe.enabled = true;
                Require(_pacingProbe.HasTimestamp(LumberCampPacingMilestone.Processor)
                        && _pacingProbe.AreRecordedTimestampsOrdered()
                        && _pacingProbe.AutomaticReportCount == 0,
                    "Re-enabled pacing probe did not catch up from authoritative Processor state.");
            }

            LumberCampProgressStage expectedNextStage = purchaseIndex + 1 < PurchaseStages.Length
                ? PurchaseStages[purchaseIndex + 1]
                : LumberCampProgressStage.FirstCourierDelivery;
            Require(_guidance.CurrentStage == expectedNextStage
                    && _guidance.ResolveCurrentStage() == expectedNextStage,
                $"Guidance skipped or failed to advance after {pad.PurchaseLabel}.");
            if (purchaseIndex + 1 < _purchasePads.Length)
            {
                Require(_purchasePads[purchaseIndex + 1].IsAvailable
                        && _purchasePads[purchaseIndex + 1].gameObject.activeSelf,
                    $"{pad.PurchaseLabel} did not reveal only the next real purchase.");
                AssertFuturePadsLocked(purchaseIndex + 1, false);
            }

            Require(!_completion.IsCompleted
                    && _completion.CompletionCount == 0
                    && !_completion.MineTeaserRoot.activeSelf
                    && _completionFeedback.PresentationCount == 0,
                $"Lumber Camp completed early while advancing {pad.PurchaseLabel}.");
            AdvanceTo(nextTestStage);
        }

        private static void TickVerifyCourierPurchaseOnly()
        {
            EnsureStageTimeout(3d);
            if (!HasWaited(0.75d))
            {
                return;
            }

            Require(_courierUnlock.IsCourierActivated
                    && _courier.gameObject.activeInHierarchy
                    && _courier.CompletedTripCount == 0
                    && _courier.State == CrateCourierState.Wait,
                "Courier purchase did not produce one empty, waiting Courier.");
            Require(!_completion.IsCompleted
                    && _completion.CompletionCount == 0
                    && _completionEventCount == 0
                    && !_completion.MineTeaserRoot.activeSelf
                    && _completionFeedback.PresentationCount == 0
                    && !_completion.TryComplete(),
                "Courier purchase alone completed the Lumber Camp.");
            Require(_guidance.CurrentStage == LumberCampProgressStage.FirstCourierDelivery
                    && _guidance.DisplayText == "NEXT: FIRST COURIER DELIVERY"
                    && _pacingProbe.HasTimestamp(LumberCampPacingMilestone.Courier)
                    && !_pacingProbe.HasTimestamp(
                        LumberCampPacingMilestone.FirstCourierDelivery),
                "Post-purchase guidance/probe did not wait for the first real delivery.");
            AdvanceTo(Stage.StartFirstDelivery);
        }

        private static void TickStartFirstDelivery()
        {
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            BeginPackingRecipe();
            AdvanceTo(Stage.WaitForFirstDelivery);
        }

        private static void TickWaitForFirstDelivery()
        {
            EnsureStageTimeout(30d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_deliveryEventCount == 1
                    && _lastDeliveredCrates == 1
                    && _lastDeliveredCash == _courier.CashPerCrate
                    && _courier.CarriedCrates == 0
                    && _packingStation.OutputCrates == 0,
                "First real Courier delivery did not commit exactly one Crate.");
            Require(_completion.IsCompleted
                    && _completion.CompletionCount == 1
                    && _completionEventCount == 1
                    && _completion.MineTeaserRoot.activeSelf
                    && _completionFeedback.PresentationCount == 1
                    && !_completion.TryComplete(),
                "First valid Courier delivery did not complete Lumber Camp exactly once.");
            Require(_guidance.CurrentStage == LumberCampProgressStage.Complete
                    && _guidance.DisplayText == "LUMBER CAMP COMPLETE",
                "Guidance did not switch to its completed state.");
            Require(_pacingProbe.HasCompleteOrderedSequence()
                    && _pacingProbe.AutomaticReportCount == 1
                    && !_pacingProbe.BuildReport().Contains("--:--"),
                "Pacing probe did not publish one complete ordered first-session report.");
            AdvanceTo(Stage.StartLaterDelivery);
        }

        private static void TickStartLaterDelivery()
        {
            _targetCompletedTrips = _courier.CompletedTripCount + 1;
            BeginPackingRecipe();
            AdvanceTo(Stage.WaitForLaterDelivery);
        }

        private static void TickWaitForLaterDelivery()
        {
            EnsureStageTimeout(30d);
            if (_courier.CompletedTripCount < _targetCompletedTrips)
            {
                return;
            }

            Require(_deliveryEventCount == 2
                    && _lastDeliveredCrates == 1
                    && _lastDeliveredCash == _courier.CashPerCrate,
                "Later Courier delivery did not use the real delivery path.");
            Require(_completion.IsCompleted
                    && _completion.CompletionCount == 1
                    && _completionEventCount == 1
                    && _completion.MineTeaserRoot.activeSelf
                    && _completionFeedback.PresentationCount == 1
                    && _pacingProbe.AutomaticReportCount == 1
                    && _pacingProbe.HasCompleteOrderedSequence(),
                "Later delivery retriggered completion, Mine, feedback, or pacing report.");
            AdvanceTo(Stage.VerifyPostCompletionPlay);
        }

        private static void TickPostCompletionPlay()
        {
            Require(_worker.isActiveAndEnabled
                    && _processor.isActiveAndEnabled
                    && _autoFeeder.isActiveAndEnabled
                    && _packingStation.isActiveAndEnabled
                    && _courier.isActiveAndEnabled,
                "A factory system stopped after Lumber Camp completion.");
            Require(_salePoint.WoodValue == 5
                    && _salePoint.PlankValue == 15
                    && _salePoint.CrateValue == 40,
                "Manual resource values changed during M8 completion.");

            _cashBeforePostCompletionSale = _cashPile.StoredCash;
            Require(_carryStack.TotalAmount == 0
                    && _carryStack.TryAdd(ResourceType.Crate, 1)
                    && _salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _cashPile.StoredCash
                       == _cashBeforePostCompletionSale + _salePoint.CrateValue,
                "Manual Crate selling stopped working after completion.");

            _positionBeforePostCompletionMove = _playerController.transform.position;
            _playerController.Move(new Vector3(0.45f, 0f, 0f));
            Physics.SyncTransforms();
            Vector3 movedOffset = _playerController.transform.position
                                  - _positionBeforePostCompletionMove;
            movedOffset.y = 0f;
            Require(movedOffset.sqrMagnitude >= 0.15f * 0.15f,
                "Player movement stopped working after completion.");
            Require(_purchasePads[0].IsCompleted
                    && _purchasePads[1].IsCompleted
                    && _purchasePads[2].IsCompleted
                    && _purchasePads[3].IsCompleted
                    && _purchasePads[4].IsCompleted
                    && _purchasePads[5].IsCompleted
                    && _completion.IsCompleted
                    && _guidance.CurrentStage == LumberCampProgressStage.Complete,
                "Completed progression became unusable after continued play.");
            Require(_pacingProbe.AutomaticReportCount == 1
                    && _pacingProbe.HasCompleteOrderedSequence(),
                "Post-completion play mutated the first-session pacing report.");
            _resourceCollector.enabled = true;
            Require(_resourceCollector.isActiveAndEnabled,
                "Player pickup could not resume after Lumber Camp completion.");
            AdvanceTo(Stage.VerifyProbeReset);
        }

        private static void TickProbeReset()
        {
            _pacingProbe.ResetProbe();
            Require(_pacingProbe.RecordedMilestoneCount == 1
                    && _pacingProbe.AutomaticReportCount == 0
                    && _pacingProbe.HasTimestamp(LumberCampPacingMilestone.SessionStart)
                    && Math.Abs(_pacingProbe.GetElapsedSeconds(
                        LumberCampPacingMilestone.SessionStart)) < 0.0001d
                    && !_pacingProbe.HasTimestamp(
                        LumberCampPacingMilestone.FirstWoodPickup)
                    && !_pacingProbe.HasTimestamp(LumberCampPacingMilestone.FirstSale)
                    && !_pacingProbe.HasTimestamp(
                        LumberCampPacingMilestone.ProductionUpgrade)
                    && !_pacingProbe.HasTimestamp(LumberCampPacingMilestone.Worker)
                    && !_pacingProbe.HasTimestamp(LumberCampPacingMilestone.Processor)
                    && !_pacingProbe.HasTimestamp(LumberCampPacingMilestone.AutoFeeder)
                    && !_pacingProbe.HasTimestamp(
                        LumberCampPacingMilestone.PackingStation)
                    && !_pacingProbe.HasTimestamp(LumberCampPacingMilestone.Courier)
                    && !_pacingProbe.HasTimestamp(
                        LumberCampPacingMilestone.FirstCourierDelivery)
                    && !_pacingProbe.HasTimestamp(
                        LumberCampPacingMilestone.LumberCampCompletion)
                    && _pacingProbe.AreRecordedTimestampsOrdered()
                    && !_pacingProbe.HasCompleteOrderedSequence()
                    && _pacingProbe.BuildReport().Contains("--:--"),
                "Pacing probe reset retained stale timestamps or report state.");
            Require(_completion.IsCompleted
                    && _completion.CompletionCount == 1
                    && _completionEventCount == 1
                    && _completion.MineTeaserRoot.activeSelf
                    && _completionFeedback.PresentationCount == 1,
                "Resetting the development probe changed gameplay completion state.");

            Pass(
                "M8 Completion/Pacing Play Mode smoke passed: all six guidance stages used authoritative paid progress, first real delivery completed once, later delivery did not retrigger, Mine/presentation remained one-shot, post-completion play stayed usable, and the pacing probe reported once then reset cleanly.");
        }

        private static void BeginPackingRecipe()
        {
            Require(_carryStack.TotalAmount == 0,
                "M8 packing setup requires an empty CarryStack.");
            _targetPackingRecipes = _packingStation.CompletedRecipeCount + 1;
            int requiredPlanks = _packingStation.RecipeInputPlanks;
            Require(requiredPlanks > 0
                    && _carryStack.TryAdd(ResourceType.Plank, requiredPlanks),
                "M8 smoke could not carry the Planks needed for a real Packing recipe.");
            for (int i = 0; i < requiredPlanks; i++)
            {
                Require(_packingStation.TryTransferInputFrom(_carryStack),
                    "M8 smoke could not transfer a carried Plank into Packing.");
            }

            Require(_carryStack.TotalAmount == 0
                    && _packingStation.CompletedRecipeCount < _targetPackingRecipes,
                "Packing input bypassed the accepted processing lifecycle.");
        }

        private static void ValidateGuidance(int purchaseIndex, int paidAmount)
        {
            PurchasePad pad = _purchasePads[purchaseIndex];
            LumberCampProgressStage expectedStage = PurchaseStages[purchaseIndex];
            string expectedText = NextUnlockGuidance.BuildDisplayText(
                expectedStage,
                paidAmount,
                pad.TotalCost);
            Require(_guidance.CurrentStage == expectedStage
                    && _guidance.ResolveCurrentStage() == expectedStage
                    && _guidance.PaidAmount == paidAmount
                    && _guidance.TotalCost == pad.TotalCost
                    && _guidance.DisplayText == expectedText
                    && _guidance.GuidanceText != null
                    && _guidance.GuidanceText.text == expectedText,
                $"Guidance did not show authoritative {expectedStage} progress "
                + $"${paidAmount} / ${pad.TotalCost}.");
        }

        private static void AssertFuturePadsLocked(int currentIndex, bool includePaymentCheck)
        {
            for (int i = currentIndex + 1; i < _purchasePads.Length; i++)
            {
                PurchasePad futurePad = _purchasePads[i];
                Require(!futurePad.IsAvailable
                        && !futurePad.IsCompleted
                        && !futurePad.gameObject.activeSelf,
                    $"{futurePad.PurchaseLabel} skipped an earlier progression stage.");
                if (includePaymentCheck)
                {
                    Require(futurePad.ProcessPaymentStep() == 0,
                        $"Locked {futurePad.PurchaseLabel} accepted payment.");
                }
            }
        }

        private static void AssertFuturePadsRejectPayment(int currentIndex, int walletBalance)
        {
            for (int i = currentIndex + 1; i < _purchasePads.Length; i++)
            {
                Require(_purchasePads[i].ProcessPaymentStep() == 0
                        && !_purchasePads[i].IsCompleted,
                    $"Locked {_purchasePads[i].PurchaseLabel} accepted out-of-order payment.");
            }

            Require(_wallet.Balance == walletBalance,
                "A future Purchase Pad consumed Wallet cash out of order.");
        }

        private static bool IsPurchaseActivationAuthoritative(int purchaseIndex)
        {
            switch (purchaseIndex)
            {
                case 0:
                    return _productionUpgrade.IsApplied;
                case 1:
                    return _workerUnlock.IsWorkerActivated;
                case 2:
                    return _processorUnlock.IsProcessorActivated;
                case 3:
                    return _autoFeederUnlock.IsAutoFeederActivated;
                case 4:
                    return _packingUnlock.IsPackingStationActivated;
                case 5:
                    return _courierUnlock.IsCourierActivated;
                default:
                    return false;
            }
        }

        private static void ValidateContinuousInvariants()
        {
            Require(_wallet.Balance >= 0
                    && _cashPile.StoredCash >= 0
                    && _carryStack.TotalAmount >= 0
                    && _carryStack.ReservedCapacity >= 0,
                "M8 economy or CarryStack ownership became negative.");
            Require(_packingStation.InputPlanks >= 0
                    && _packingStation.ProcessingInputPlanks >= 0
                    && _packingStation.OutputCrates >= 0
                    && _packingStation.ReservedCourierOutputCrates >= 0,
                "M8 Packing/Courier ownership became negative.");
            Require(_courier.ReservedCrates >= 0
                    && _courier.CarriedCrates >= 0
                    && _courier.CompletedTripCount == _deliveryEventCount,
                "M8 Courier delivery events diverged from committed trips.");
            Require(_completion.CompletionCount <= 1
                    && _completionEventCount <= 1
                    && _completionFeedback.PresentationCount <= 1
                    && _pacingProbe.AutomaticReportCount <= 1,
                "M8 one-shot completion/presentation/report invariant regressed.");
            Require(_pacingProbe.AreRecordedTimestampsOrdered(),
                "M8 pacing timestamps became negative, invalid, or out of order.");
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
                    $"M8 smoke found more than one ${totalCost} Purchase Pad.");
                result = pads[i];
            }

            Require(result != null,
                $"M8 smoke could not find the ${totalCost} Purchase Pad.");
            return result;
        }

        private static T FindSingleIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(
                FindObjectsInactive.Include);
            Require(matches.Length == 1,
                $"M8 smoke expected one {typeof(T).Name}, found {matches.Length}.");
            return matches[0];
        }

        private static void MovePlayerTo(Vector3 position)
        {
            Vector3 planarPosition = new Vector3(position.x, 0f, position.z);
            _playerController.Move(planarPosition - _playerController.transform.position);
            Physics.SyncTransforms();
        }

        private static void HandleLumberCampCompleted()
        {
            Require(_completion.IsCompleted
                    && _completion.CompletionCount == 1
                    && _courier.CompletedTripCount > 0,
                "Completion event preceded authoritative Courier delivery state.");
            _completionEventCount++;
        }

        private static void HandleCourierDeliveryCompleted(
            uint generation,
            int crateCount,
            int cashValue)
        {
            Require(generation != 0
                    && DeliveredGenerations.Add(generation)
                    && crateCount > 0
                    && crateCount <= _courier.Capacity
                    && cashValue == crateCount * _courier.CashPerCrate
                    && _courier.CarriedCrates == 0
                    && _cashPile.StoredCash >= cashValue,
                "Courier published a duplicate, empty, mispriced, or uncommitted delivery.");
            _lastDeliveredCrates = crateCount;
            _lastDeliveredCash = cashValue;
            _deliveryEventCount++;
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
                throw new InvalidOperationException($"M8 smoke timed out in stage {_stage}.");
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
            string result = $"M8 Completion/Pacing Play Mode smoke failed: {message}";
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
            if (_completion != null)
            {
                _completion.Completed -= HandleLumberCampCompleted;
            }

            if (_courier != null)
            {
                _courier.DeliveryCompleted -= HandleCourierDeliveryCompleted;
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
