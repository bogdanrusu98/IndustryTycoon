using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.Workers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    [InitializeOnLoad]
    public static class LumberCampM5PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey = "IndustryTycoon.M5.Smoke.Running";
        private const string CommandLineKey = "IndustryTycoon.M5.Smoke.CommandLine";
        private const string FinishPendingKey = "IndustryTycoon.M5.Smoke.FinishPending";
        private const string SuccessKey = "IndustryTycoon.M5.Smoke.Success";
        private const string ResultMessageKey = "IndustryTycoon.M5.Smoke.ResultMessage";

        private const int RepeatedTransferCycles = 20;
        private static readonly Vector3 NeutralPosition = new Vector3(0f, 0f, -9.5f);

        private enum Stage
        {
            Warmup,
            CompletePrerequisites,
            PartiallyFundAutoFeeder,
            VerifyPartialAutoFeederProgress,
            CompleteAutoFeederPurchase,
            VerifySourceEmptyPause,
            StartFirstTransfer,
            WaitForFirstTransfer,
            VerifyPostTransferEmptyPause,
            StartPlayerContention,
            WaitForContentionTransfer,
            WaitForInitialRecipe,
            PrepareDestinationReservation,
            WaitForDestinationFull,
            WaitForCapacityResume,
            CancelTransferVisual,
            StartFeederDisableCancellation,
            VerifyFeederDisableCancellation,
            PrepareRepeatedCycles,
            WaitForRepeatedCycles,
            WaitForFullManualOutput,
            CollectManualOutput,
            SellManualPlank,
            VerifyManualInputAndDirectSale,
            PrepareWorkerIsolation,
            WaitForWorkerDeposit
        }

        private static CharacterController _playerController;
        private static ResourceCollector _resourceCollector;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static CashPile _cashPile;
        private static SalePoint _salePoint;
        private static PurchasePad _productionPad;
        private static PurchasePad _workerPad;
        private static PurchasePad _processorPad;
        private static PurchasePad _autoFeederPad;
        private static WoodProductionUpgrade _productionUpgrade;
        private static FirstWorkerUnlock _workerUnlock;
        private static FirstProcessorUnlock _processorUnlock;
        private static FirstAutoFeederUnlock _autoFeederUnlock;
        private static LumberWorker _worker;
        private static WoodStockpile _stockpile;
        private static WoodProcessor _processor;
        private static ProcessorInputZone _inputZone;
        private static ProcessorOutputZone _outputZone;
        private static WoodAutoFeeder _autoFeeder;
        private static WoodAutoFeederFeedback _autoFeederFeedback;
        private static AutoFeederUnlockFeedback _autoFeederUnlockFeedback;

        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;

        private static int _productionCompletionCount;
        private static int _productionAppliedCount;
        private static int _workerCompletionCount;
        private static int _workerPadUnlockCount;
        private static int _workerActivationCount;
        private static int _processorCompletionCount;
        private static int _processorPadUnlockCount;
        private static int _processorActivationCount;
        private static int _autoFeederCompletionCount;
        private static int _autoFeederPadUnlockCount;
        private static int _autoFeederActivationCount;
        private static int _transferStartedEventCount;
        private static int _transferCompletedEventCount;
        private static int _transferCancelledEventCount;
        private static int _recipeCompletionEventCount;
        private static int _workerDepositEventCount;
        private static int _woodSaleEventCount;
        private static int _plankSaleEventCount;
        private static int _woodSaleCash;
        private static int _plankSaleCash;

        private static int _firstTransferTarget;
        private static int _contentionTransferTarget;
        private static int _fullInputTransferTarget;
        private static int _recipeCountBeforeCapacityResume;
        private static int _repeatedTransferTarget;
        private static int _repeatedWoodEquivalent;
        private static int _cashBeforeManualOutput;
        private static int _workerDepositStart;
        private static int _workerStockpileOwnedStart;
        private static int _workerProcessorInputStart;
        private static int _workerProcessorOutputStart;
        private static int _workerCashStart;

        static LumberCampM5PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/Prototype/Run M5 Auto Feeder Smoke Test")]
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
                    "Exit Play Mode before starting the M5 auto-feeder smoke test.");
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

                if (Now - _runStartedAt > 150d)
                {
                    throw new InvalidOperationException(
                        "M5 auto-feeder smoke test exceeded its 150-second timeout.");
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
            _resourceCollector = Object.FindAnyObjectByType<ResourceCollector>();
            _carryStack = Object.FindAnyObjectByType<CarryStack>();
            _wallet = Object.FindAnyObjectByType<Wallet>();
            _cashPile = Object.FindAnyObjectByType<CashPile>();
            _salePoint = Object.FindAnyObjectByType<SalePoint>();
            _productionPad = FindPurchasePad(120);
            _workerPad = FindPurchasePad(240);
            _processorPad = FindPurchasePad(360);
            _autoFeederPad = FindPurchasePad(600);
            _productionUpgrade = Object.FindAnyObjectByType<WoodProductionUpgrade>();
            _workerUnlock = Object.FindAnyObjectByType<FirstWorkerUnlock>();
            _processorUnlock = Object.FindAnyObjectByType<FirstProcessorUnlock>();
            _autoFeederUnlock = Object.FindAnyObjectByType<FirstAutoFeederUnlock>();
            _worker = FindSingleIncludingInactive<LumberWorker>();
            _stockpile = Object.FindAnyObjectByType<WoodStockpile>();
            _processor = FindSingleIncludingInactive<WoodProcessor>();
            _inputZone = FindSingleIncludingInactive<ProcessorInputZone>();
            _outputZone = FindSingleIncludingInactive<ProcessorOutputZone>();
            _autoFeeder = FindSingleIncludingInactive<WoodAutoFeeder>();
            _autoFeederFeedback = FindSingleIncludingInactive<WoodAutoFeederFeedback>();
            _autoFeederUnlockFeedback =
                Object.FindAnyObjectByType<AutoFeederUnlockFeedback>();

            Require(_playerController != null && _resourceCollector != null,
                "M5 smoke could not find the accepted Player gameplay components.");
            Require(_carryStack != null && _carryStack.Capacity == 12,
                "M5 smoke requires the accepted 12-capacity CarryStack.");
            Require(_wallet != null && _wallet.Balance == 0,
                "M5 smoke requires an initially empty Wallet.");
            Require(_cashPile != null && _cashPile.StoredCash == 0,
                "M5 smoke requires an initially empty Cash Pile.");
            Require(_salePoint != null
                    && _salePoint.WoodValue == 5
                    && _salePoint.PlankValue == 15,
                "M5 smoke requires the accepted $5 Wood / $15 Plank Sale Point.");
            Require(_productionPad != null
                    && _workerPad != null
                    && _processorPad != null
                    && _autoFeederPad != null,
                "M5 smoke could not find all four progression Purchase Pads.");
            Require(_productionUpgrade != null
                    && _workerUnlock != null
                    && _processorUnlock != null
                    && _autoFeederUnlock != null,
                "M5 smoke could not find the accepted progression chain.");
            Require(_worker != null && _stockpile != null && _stockpile.Capacity == 30,
                "M5 smoke could not find the worker and 30-capacity Stockpile.");
            Require(_processor != null
                    && _inputZone != null
                    && _outputZone != null
                    && _processor.InputCapacity == 24
                    && _processor.OutputCapacity == 12
                    && _processor.RecipeInputWood == 2
                    && _processor.RecipeOutputPlanks == 1,
                "M5 smoke requires the accepted 24/12 Processor and 2:1 recipe.");
            Require(_autoFeeder != null
                    && _autoFeederFeedback != null
                    && _autoFeederUnlockFeedback != null,
                "M5 smoke could not find the auto-feeder logic and presentation.");
            Require(_autoFeeder.Stockpile == _stockpile
                    && _autoFeeder.Processor == _processor
                    && _autoFeeder.Presentation == _autoFeederFeedback,
                "Auto Feeder is not wired to the fixed Stockpile -> Processor route.");
            Require(Mathf.Approximately(_autoFeeder.LaunchInterval, 0.75f)
                    && Mathf.Approximately(_autoFeeder.TravelDuration, 0.55f),
                "M5 smoke requires the configured 0.75s cadence / 0.55s travel.");
            Require(_autoFeederUnlock.ProcessorUnlock == _processorUnlock
                    && _autoFeederUnlock.AutoFeederPurchasePad == _autoFeederPad
                    && _autoFeederUnlock.AutoFeederRoot == _autoFeeder.gameObject,
                "Auto Feeder progression references are incomplete.");

            // This smoke drives the same authoritative transfer APIs directly. Keep
            // trigger-driven player automation out of the deterministic test harness.
            _resourceCollector.enabled = false;
            _inputZone.enabled = false;
            _outputZone.enabled = false;
            MovePlayerTo(NeutralPosition);

            ResetCounters();
            SubscribeEvents();
            _runStartedAt = Now;
            AdvanceTo(Stage.Warmup);
            _runtimeInitialized = true;
        }

        private static void ResetCounters()
        {
            _productionCompletionCount = 0;
            _productionAppliedCount = 0;
            _workerCompletionCount = 0;
            _workerPadUnlockCount = 0;
            _workerActivationCount = 0;
            _processorCompletionCount = 0;
            _processorPadUnlockCount = 0;
            _processorActivationCount = 0;
            _autoFeederCompletionCount = 0;
            _autoFeederPadUnlockCount = 0;
            _autoFeederActivationCount = 0;
            _transferStartedEventCount = 0;
            _transferCompletedEventCount = 0;
            _transferCancelledEventCount = 0;
            _recipeCompletionEventCount = 0;
            _workerDepositEventCount = 0;
            _woodSaleEventCount = 0;
            _plankSaleEventCount = 0;
            _woodSaleCash = 0;
            _plankSaleCash = 0;
        }

        private static void SubscribeEvents()
        {
            _productionPad.Completed += HandleProductionCompleted;
            _productionUpgrade.Applied += HandleProductionApplied;
            _workerPad.Completed += HandleWorkerCompleted;
            _workerUnlock.PadUnlocked += HandleWorkerPadUnlocked;
            _workerUnlock.WorkerActivated += HandleWorkerActivated;
            _processorPad.Completed += HandleProcessorCompleted;
            _processorUnlock.PadUnlocked += HandleProcessorPadUnlocked;
            _processorUnlock.ProcessorActivated += HandleProcessorActivated;
            _autoFeederPad.Completed += HandleAutoFeederCompleted;
            _autoFeederUnlock.PadUnlocked += HandleAutoFeederPadUnlocked;
            _autoFeederUnlock.AutoFeederActivated += HandleAutoFeederActivated;
            _autoFeeder.TransferStarted += HandleTransferStarted;
            _autoFeeder.TransferCompleted += HandleTransferCompleted;
            _autoFeeder.TransferCancelled += HandleTransferCancelled;
            _processor.RecipeCompleted += HandleRecipeCompleted;
            _worker.WoodDeposited += HandleWorkerDeposited;
            _salePoint.UnitSold += HandleUnitSold;
        }

        private static void ValidateContinuousInvariants()
        {
            Require(_wallet.Balance >= 0 && _cashPile.StoredCash >= 0,
                "Wallet or Cash Pile became negative during M5 smoke.");
            Require(_carryStack.TotalAmount >= 0
                    && _carryStack.ReservedCapacity >= 0
                    && _carryStack.TotalAmount + _carryStack.ReservedCapacity
                       <= _carryStack.Capacity,
                "CarryStack amount plus reservations exceeded capacity.");
            Require(_carryStack.GetAmount(ResourceType.Wood) == 0
                    || _carryStack.GetAmount(ResourceType.Plank) == 0,
                "CarryStack mixed Wood and Plank during M5 smoke.");

            Require(_stockpile.StoredWood >= 0
                    && _stockpile.IncomingReservations >= 0
                    && _stockpile.OutgoingReservations >= 0
                    && _stockpile.StoredWood
                       + _stockpile.IncomingReservations
                       + _stockpile.OutgoingReservations <= _stockpile.Capacity,
                "Stockpile stored/incoming/outgoing ownership exceeded its bounds.");
            Require(_stockpile.TotalOwnedWood
                    == _stockpile.StoredWood + _stockpile.OutgoingReservations,
                "Stockpile total ownership diverged from stored plus escrowed Wood.");

            Require(_processor.InputWood >= 0
                    && _processor.ReservedInputCapacity >= 0
                    && _processor.InputWood + _processor.ReservedInputCapacity
                       <= _processor.InputCapacity,
                "Processor stored plus reserved input exceeded capacity 24.");
            Require(_processor.ReservedInputCapacity <= 1,
                "Processor reserved more than one feeder input slot.");
            Require(_processor.OutputPlanks >= 0
                    && _processor.ReservedOutputCapacity >= 0
                    && _processor.OutputPlanks + _processor.ReservedOutputCapacity
                       <= _processor.OutputCapacity,
                "Processor output plus recipe reservation exceeded capacity 12.");

            if (_autoFeeder.IsTransferInFlight)
            {
                Require(_autoFeeder.ActiveTransferCount == 1
                        && _stockpile.OutgoingReservations == 1
                        && _processor.ReservedInputCapacity == 1
                        && _autoFeederFeedback.ActiveVisualCount == 1
                        && _autoFeeder.ActiveTransferGeneration
                           == _autoFeederFeedback.ActiveVisualGeneration,
                    "An in-flight feeder transfer did not have exactly one source, destination, and visual owner.");
            }
            else
            {
                Require(_autoFeeder.ActiveTransferCount == 0
                        && _autoFeederFeedback.ActiveVisualCount == 0,
                    "Idle feeder retained logical or visual in-flight ownership.");
            }

            Require(_transferStartedEventCount
                    == _autoFeeder.CompletedTransferCount
                       + _autoFeeder.CancelledTransferCount
                       + _autoFeeder.ActiveTransferCount,
                "Feeder starts did not resolve to exactly one completion, cancellation, or active transfer.");
            Require(_transferCompletedEventCount == _autoFeeder.CompletedTransferCount
                    && _transferCancelledEventCount == _autoFeeder.CancelledTransferCount,
                "Feeder event counters diverged from authoritative counters.");
            Require(_worker.CompletedDepositCount <= _worker.CompletedPickupCount
                    && _worker.CompletedPickupCount - _worker.CompletedDepositCount <= 1,
                "Worker one-item cargo invariant regressed during M5 smoke.");

            if (_stage == Stage.WaitForRepeatedCycles)
            {
                Require(GetWoodEquivalent() == _repeatedWoodEquivalent,
                    "Wood equivalent changed during repeated feeder cycles (duplication or loss).");
            }
        }

        private static void TickCurrentStage()
        {
            switch (_stage)
            {
                case Stage.Warmup:
                    TickWarmup();
                    break;
                case Stage.CompletePrerequisites:
                    TickCompletePrerequisites();
                    break;
                case Stage.PartiallyFundAutoFeeder:
                    TickPartiallyFundAutoFeeder();
                    break;
                case Stage.VerifyPartialAutoFeederProgress:
                    TickVerifyPartialAutoFeederProgress();
                    break;
                case Stage.CompleteAutoFeederPurchase:
                    TickCompleteAutoFeederPurchase();
                    break;
                case Stage.VerifySourceEmptyPause:
                    TickVerifySourceEmptyPause();
                    break;
                case Stage.StartFirstTransfer:
                    TickStartFirstTransfer();
                    break;
                case Stage.WaitForFirstTransfer:
                    TickWaitForFirstTransfer();
                    break;
                case Stage.VerifyPostTransferEmptyPause:
                    TickVerifyPostTransferEmptyPause();
                    break;
                case Stage.StartPlayerContention:
                    TickStartPlayerContention();
                    break;
                case Stage.WaitForContentionTransfer:
                    TickWaitForContentionTransfer();
                    break;
                case Stage.WaitForInitialRecipe:
                    TickWaitForInitialRecipe();
                    break;
                case Stage.PrepareDestinationReservation:
                    TickPrepareDestinationReservation();
                    break;
                case Stage.WaitForDestinationFull:
                    TickWaitForDestinationFull();
                    break;
                case Stage.WaitForCapacityResume:
                    TickWaitForCapacityResume();
                    break;
                case Stage.CancelTransferVisual:
                    TickCancelTransferVisual();
                    break;
                case Stage.StartFeederDisableCancellation:
                    TickStartFeederDisableCancellation();
                    break;
                case Stage.VerifyFeederDisableCancellation:
                    TickVerifyFeederDisableCancellation();
                    break;
                case Stage.PrepareRepeatedCycles:
                    TickPrepareRepeatedCycles();
                    break;
                case Stage.WaitForRepeatedCycles:
                    TickWaitForRepeatedCycles();
                    break;
                case Stage.WaitForFullManualOutput:
                    TickWaitForFullManualOutput();
                    break;
                case Stage.CollectManualOutput:
                    TickCollectManualOutput();
                    break;
                case Stage.SellManualPlank:
                    TickSellManualPlank();
                    break;
                case Stage.VerifyManualInputAndDirectSale:
                    TickVerifyManualInputAndDirectSale();
                    break;
                case Stage.PrepareWorkerIsolation:
                    TickPrepareWorkerIsolation();
                    break;
                case Stage.WaitForWorkerDeposit:
                    TickWaitForWorkerDeposit();
                    break;
            }
        }

        private static void TickWarmup()
        {
            if (!HasWaited(0.5d))
            {
                return;
            }

            Require(_carryStack.TotalAmount == 0
                    && _stockpile.TotalOwnedWood == 0
                    && _processor.InputWood == 0
                    && _processor.OutputPlanks == 0,
                "M5 authoritative storage did not start empty.");
            Require(!_processorUnlock.IsProcessorActivated
                    && !_autoFeederUnlock.IsPadUnlocked
                    && !_autoFeederUnlock.IsAutoFeederActivated
                    && !_autoFeederPad.IsAvailable
                    && !_autoFeederPad.IsCompleted
                    && !_autoFeederUnlock.AutoFeederPurchasePadRoot.activeSelf
                    && !_autoFeederUnlock.AutoFeederRoot.activeSelf,
                "Auto Feeder pad or route was available before Processor unlock.");
            Require(_wallet.Deposit(5) == 5
                    && _autoFeederPad.ProcessPaymentStep() == 0
                    && _autoFeederPad.RemainingCost == 600
                    && _wallet.Balance == 5,
                "Locked Auto Feeder pad accepted payment or changed progress.");

            AdvanceTo(Stage.CompletePrerequisites);
        }

        private static void TickCompletePrerequisites()
        {
            Require(_wallet.Deposit(115) == 115,
                "Could not seed the production-upgrade remainder.");
            ProcessExactPayment(_productionPad, 120);
            Require(_productionCompletionCount == 1
                    && _productionAppliedCount == 1
                    && _productionUpgrade.IsApplied,
                "Accepted production purchase did not complete exactly once.");

            Require(_wallet.Deposit(240) == 240,
                "Could not seed the worker cost.");
            ProcessExactPayment(_workerPad, 240);
            Require(_workerCompletionCount == 1
                    && _workerPadUnlockCount == 1
                    && _workerActivationCount == 1
                    && _workerUnlock.IsWorkerActivated,
                "Accepted worker purchase did not complete exactly once.");
            _worker.enabled = false;

            Require(!_autoFeederUnlock.IsPadUnlocked
                    && !_autoFeederPad.IsAvailable
                    && !_autoFeederUnlock.AutoFeederPurchasePadRoot.activeSelf,
                "Auto Feeder pad unlocked before Processor purchase completion.");
            Require(_wallet.Deposit(360) == 360,
                "Could not seed the Processor cost.");
            ProcessExactPayment(_processorPad, 360);
            Require(_processorCompletionCount == 1
                    && _processorPadUnlockCount == 1
                    && _processorActivationCount == 1
                    && _processorUnlock.IsProcessorActivated
                    && _processor.isActiveAndEnabled,
                "Accepted Processor purchase did not complete exactly once.");
            Require(_autoFeederUnlock.IsPadUnlocked
                    && _autoFeederPadUnlockCount == 1
                    && _autoFeederUnlock.AutoFeederPurchasePadRoot.activeSelf
                    && _autoFeederPad.IsAvailable
                    && !_autoFeederUnlock.IsAutoFeederActivated
                    && !_autoFeederUnlock.AutoFeederRoot.activeSelf,
                "Auto Feeder pad did not reveal exactly once after Processor activation.");

            AdvanceTo(Stage.PartiallyFundAutoFeeder);
        }

        private static void TickPartiallyFundAutoFeeder()
        {
            Require(_wallet.Deposit(65) == 65,
                "Could not seed the Auto Feeder partial payment.");
            ProcessExactPayment(_autoFeederPad, 65);
            Require(_autoFeederPad.RemainingCost == 535
                    && _wallet.Balance == 0
                    && !_autoFeederPad.IsCompleted
                    && !_autoFeederUnlock.IsAutoFeederActivated,
                "Auto Feeder partial payment did not settle at $535 remaining.");

            _autoFeederPad.enabled = false;
            _autoFeederPad.enabled = true;
            AdvanceTo(Stage.VerifyPartialAutoFeederProgress);
        }

        private static void TickVerifyPartialAutoFeederProgress()
        {
            if (!HasWaited(0.1d))
            {
                return;
            }

            Require(_autoFeederPad.RemainingCost == 535
                    && _autoFeederPad.IsAvailable
                    && !_autoFeederPad.IsCompleted
                    && _autoFeederCompletionCount == 0
                    && _autoFeederActivationCount == 0,
                "Auto Feeder pad lost persistent partial progress across disable/enable.");
            AdvanceTo(Stage.CompleteAutoFeederPurchase);
        }

        private static void TickCompleteAutoFeederPurchase()
        {
            Require(_wallet.Deposit(535) == 535,
                "Could not seed the exact Auto Feeder remainder.");
            ProcessExactPayment(_autoFeederPad, 535);
            Require(_autoFeederPad.IsCompleted
                    && _autoFeederPad.RemainingCost == 0
                    && _wallet.Balance == 0
                    && _autoFeederCompletionCount == 1
                    && _autoFeederActivationCount == 1
                    && _autoFeederUnlock.IsAutoFeederActivated
                    && _autoFeederUnlock.AutoFeederRoot.activeSelf
                    && _autoFeeder.isActiveAndEnabled,
                "Auto Feeder purchase/activation did not complete exactly once.");
            Require(_autoFeederPad.ProcessPaymentStep() == 0
                    && !_autoFeederUnlock.TryUnlockPad()
                    && !_autoFeederUnlock.TryActivateAutoFeeder()
                    && _autoFeederCompletionCount == 1
                    && _autoFeederActivationCount == 1,
                "Completed Auto Feeder accepted duplicate payment or activation.");
            Require(_autoFeederUnlockFeedback.PresentationCount == 1
                    && _autoFeederFeedback.ConfiguredVisualPoolSize == 2
                    && _autoFeederFeedback.VisualPoolCount == 2,
                "Auto Feeder unlock feedback or capped two-visual pool was not ready.");

            AdvanceTo(Stage.VerifySourceEmptyPause);
        }

        private static void TickVerifySourceEmptyPause()
        {
            if (!HasWaited(0.25d))
            {
                return;
            }

            Require(_stockpile.AvailableWood == 0
                    && !_autoFeeder.IsTransferInFlight
                    && _autoFeeder.State == WoodAutoFeederState.WaitingForWood
                    && _autoFeederFeedback.DisplayedState
                       == WoodAutoFeederState.WaitingForWood,
                "Auto Feeder did not wait cleanly on an empty Stockpile.");
            AdvanceTo(Stage.StartFirstTransfer);
        }

        private static void TickStartFirstTransfer()
        {
            _firstTransferTarget = _autoFeeder.CompletedTransferCount + 1;
            DepositWoodToStockpile(1);
            Require(_autoFeeder.IsTransferInFlight
                    && _stockpile.StoredWood == 0
                    && _stockpile.OutgoingReservations == 1
                    && _stockpile.TotalOwnedWood == 1
                    && _processor.InputWood == 0
                    && _processor.ReservedInputCapacity == 1,
                "Stockpile refill did not automatically launch one escrowed transfer.");
            AdvanceTo(Stage.WaitForFirstTransfer);
        }

        private static void TickWaitForFirstTransfer()
        {
            EnsureStageTimeout(2d);
            if (_autoFeeder.CompletedTransferCount < _firstTransferTarget)
            {
                return;
            }

            Require(_autoFeeder.CompletedTransferCount == _firstTransferTarget
                    && _processor.InputWood == 1
                    && _processor.ReservedInputCapacity == 0
                    && _stockpile.TotalOwnedWood == 0
                    && !_autoFeeder.IsTransferInFlight,
                "First Auto Feeder cycle did not commit exactly one Wood.");
            AdvanceTo(Stage.VerifyPostTransferEmptyPause);
        }

        private static void TickVerifyPostTransferEmptyPause()
        {
            if (!HasWaited(0.3d))
            {
                return;
            }

            Require(_autoFeeder.State == WoodAutoFeederState.WaitingForWood
                    && _processor.InputWood == 1
                    && _stockpile.TotalOwnedWood == 0,
                "Auto Feeder did not return to source-empty wait after one cycle.");
            AdvanceTo(Stage.StartPlayerContention);
        }

        private static void TickStartPlayerContention()
        {
            _contentionTransferTarget = _autoFeeder.CompletedTransferCount + 1;
            DepositWoodToStockpile(1);
            DepositWoodToStockpile(1);
            Require(_autoFeeder.IsTransferInFlight
                    && _stockpile.StoredWood == 1
                    && _stockpile.OutgoingReservations == 1
                    && _stockpile.TotalOwnedWood == 2,
                "Two source Wood did not split into one escrowed and one available item.");
            Require(_stockpile.TryTransferOneTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Wood) == 1
                    && _stockpile.StoredWood == 0
                    && _stockpile.OutgoingReservations == 1
                    && !_stockpile.TryTransferOneTo(_carryStack),
                "Player/conveyor contention did not preserve exactly one owner per Wood.");
            Require(GetWoodEquivalent() == 3,
                "Wood ownership changed during simultaneous player/conveyor contention.");
            AdvanceTo(Stage.WaitForContentionTransfer);
        }

        private static void TickWaitForContentionTransfer()
        {
            EnsureStageTimeout(2d);
            if (_autoFeeder.CompletedTransferCount < _contentionTransferTarget)
            {
                return;
            }

            Require(_processor.InputWood == 2
                    && _processor.ReservedInputCapacity == 0
                    && _stockpile.TotalOwnedWood == 0
                    && _carryStack.GetAmount(ResourceType.Wood) == 1
                    && GetWoodEquivalent() == 3,
                "Contention transfer duplicated or lost Wood at arrival.");
            Require(_salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _cashPile.StoredCash == 5
                    && _woodSaleEventCount == 1,
                "Direct Wood sale did not remain intact while automation was enabled.");
            AdvanceTo(Stage.WaitForInitialRecipe);
        }

        private static void TickWaitForInitialRecipe()
        {
            EnsureStageTimeout(2d);
            if (_processor.CompletedRecipeCount < 1)
            {
                return;
            }

            Require(_processor.CompletedRecipeCount == 1
                    && _processor.InputWood == 0
                    && _processor.OutputPlanks == 1
                    && _processor.ReservedOutputCapacity == 0
                    && !_processor.IsProcessing,
                "Initial automated pair did not retain the accepted 2 Wood -> 1 Plank recipe.");
            AdvanceTo(Stage.PrepareDestinationReservation);
        }

        private static void TickPrepareDestinationReservation()
        {
            _fullInputTransferTarget = _autoFeeder.CompletedTransferCount + 1;
            DepositWoodToStockpile(1);
            DepositWoodToStockpile(1);
            Require(_autoFeeder.IsTransferInFlight
                    && _stockpile.StoredWood == 1
                    && _stockpile.OutgoingReservations == 1
                    && _processor.ReservedInputCapacity == 1,
                "Destination-capacity setup did not launch one reserved transfer.");

            for (int i = 0; i < 23; i++)
            {
                Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                        && _processor.TryTransferInputFrom(_carryStack),
                    $"Manual input setup rejected in-capacity Wood {i + 1}.");
            }

            Require(_processor.InputWood == 23
                    && _processor.ReservedInputCapacity == 1
                    && _processor.AvailableInputCapacity == 0
                    && _carryStack.TryAdd(ResourceType.Wood, 1)
                    && !_processor.TryTransferInputFrom(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Wood) == 1,
                "Manual feeding consumed the destination slot reserved by in-flight Wood.");
            _recipeCountBeforeCapacityResume = _processor.CompletedRecipeCount;
            AdvanceTo(Stage.WaitForDestinationFull);
        }

        private static void TickWaitForDestinationFull()
        {
            EnsureStageTimeout(2d);
            if (_autoFeeder.CompletedTransferCount < _fullInputTransferTarget)
            {
                return;
            }

            Require(_processor.InputWood == _processor.InputCapacity
                    && _processor.ReservedInputCapacity == 0
                    && _processor.AvailableInputCapacity == 0
                    && _stockpile.StoredWood == 1
                    && _stockpile.OutgoingReservations == 0
                    && _autoFeeder.State == WoodAutoFeederState.DestinationFull
                    && !_processor.TryTransferInputFrom(_carryStack),
                "Completed arrival did not fill exactly the last Processor slot and pause the route.");
            AdvanceTo(Stage.WaitForCapacityResume);
        }

        private static void TickWaitForCapacityResume()
        {
            EnsureStageTimeout(3d);
            if (_processor.CompletedRecipeCount <= _recipeCountBeforeCapacityResume
                || !_autoFeeder.IsTransferInFlight)
            {
                return;
            }

            Require(_processor.InputWood == 22
                    && _processor.ReservedInputCapacity == 1
                    && _stockpile.StoredWood == 0
                    && _stockpile.OutgoingReservations == 1
                    && _autoFeeder.State == WoodAutoFeederState.Moving,
                "Auto Feeder did not resume automatically when Processor consumption freed capacity.");
            AdvanceTo(Stage.CancelTransferVisual);
        }

        private static void TickCancelTransferVisual()
        {
            uint generation = _autoFeeder.ActiveTransferGeneration;
            WoodAutoFeederTransferVisual activeVisual = FindActiveTransferVisual(generation);
            Require(activeVisual != null,
                "Could not find the visual corresponding to the active logical transfer.");
            activeVisual.gameObject.SetActive(false);
            Require(!_autoFeeder.IsTransferInFlight
                    && _autoFeeder.CancelledTransferCount == 1
                    && _stockpile.StoredWood == 1
                    && _stockpile.OutgoingReservations == 0
                    && _processor.ReservedInputCapacity == 0
                    && _autoFeederFeedback.ActiveVisualCount == 0,
                "Disabling an in-flight visual did not refund source and release destination exactly once.");
            AdvanceTo(Stage.StartFeederDisableCancellation);
        }

        private static void TickStartFeederDisableCancellation()
        {
            EnsureStageTimeout(1d);
            if (!_autoFeeder.IsTransferInFlight)
            {
                return;
            }

            Require(_stockpile.OutgoingReservations == 1
                    && _processor.ReservedInputCapacity == 1,
                "Auto Feeder did not resume automatically after visual cancellation.");
            _autoFeeder.enabled = false;
            Require(!_autoFeeder.IsTransferInFlight
                    && _autoFeeder.State == WoodAutoFeederState.Disabled
                    && _autoFeeder.CancelledTransferCount == 2
                    && _stockpile.StoredWood == 1
                    && _stockpile.OutgoingReservations == 0
                    && _processor.ReservedInputCapacity == 0,
                "Disabling the Auto Feeder leaked or lost in-flight ownership.");
            Require(_stockpile.TryTransferOneTo(_carryStack)
                    && _stockpile.TotalOwnedWood == 0
                    && _carryStack.GetAmount(ResourceType.Wood) == 2,
                "Player could not withdraw refunded Wood after feeder cancellation.");
            _autoFeeder.enabled = true;
            AdvanceTo(Stage.VerifyFeederDisableCancellation);
        }

        private static void TickVerifyFeederDisableCancellation()
        {
            if (!HasWaited(0.2d))
            {
                return;
            }

            Require(_autoFeeder.State == WoodAutoFeederState.WaitingForWood
                    && _autoFeeder.CancelledTransferCount == 2
                    && _transferCancelledEventCount == 2,
                "Re-enabled Auto Feeder did not settle cleanly after cancellation.");
            Require(_salePoint.TryUnloadOne()
                    && _salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _cashPile.StoredCash == 15
                    && _woodSaleEventCount == 3
                    && _woodSaleCash == 15,
                "Direct Wood selling regressed after conveyor cancellation/refund.");
            AdvanceTo(Stage.PrepareRepeatedCycles);
        }

        private static void TickPrepareRepeatedCycles()
        {
            _repeatedTransferTarget =
                _autoFeeder.CompletedTransferCount + RepeatedTransferCycles;
            DepositWoodToStockpile(RepeatedTransferCycles);
            Require(_autoFeeder.IsTransferInFlight
                    && _stockpile.TotalOwnedWood == RepeatedTransferCycles,
                "Repeated-cycle setup did not retain ownership of all staged Wood.");
            _repeatedWoodEquivalent = GetWoodEquivalent();
            AdvanceTo(Stage.WaitForRepeatedCycles);
        }

        private static void TickWaitForRepeatedCycles()
        {
            EnsureStageTimeout(35d);
            if (_autoFeeder.CompletedTransferCount < _repeatedTransferTarget)
            {
                return;
            }

            Require(_autoFeeder.CompletedTransferCount == _repeatedTransferTarget
                    && _transferCompletedEventCount == _repeatedTransferTarget
                    && _stockpile.TotalOwnedWood == 0
                    && _stockpile.OutgoingReservations == 0
                    && _processor.ReservedInputCapacity == 0
                    && GetWoodEquivalent() == _repeatedWoodEquivalent,
                "Twenty consecutive Auto Feeder cycles were not exact and stable.");
            _cashBeforeManualOutput = _cashPile.StoredCash;
            AdvanceTo(Stage.WaitForFullManualOutput);
        }

        private static void TickWaitForFullManualOutput()
        {
            EnsureStageTimeout(18d);
            if (_processor.OutputPlanks < _processor.OutputCapacity
                || _processor.IsProcessing)
            {
                return;
            }

            Require(_processor.OutputPlanks == 12
                    && _cashPile.StoredCash == _cashBeforeManualOutput
                    && _carryStack.TotalAmount == 0,
                "Plank output was collected or sold without the player.");
            AdvanceTo(Stage.CollectManualOutput);
        }

        private static void TickCollectManualOutput()
        {
            if (!HasWaited(0.3d))
            {
                return;
            }

            Require(_processor.OutputPlanks == 12
                    && _processor.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Plank) == 1
                    && _processor.OutputPlanks == 11
                    && _cashPile.StoredCash == _cashBeforeManualOutput,
                "Plank output did not remain manual or collect exactly one item.");
            AdvanceTo(Stage.SellManualPlank);
        }

        private static void TickSellManualPlank()
        {
            Require(_salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _cashPile.StoredCash == _cashBeforeManualOutput + 15
                    && _plankSaleEventCount == 1
                    && _plankSaleCash == 15,
                "Manually collected Plank did not sell for exactly $15.");
            AdvanceTo(Stage.VerifyManualInputAndDirectSale);
        }

        private static void TickVerifyManualInputAndDirectSale()
        {
            int inputBefore = _processor.InputWood;
            Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                    && _processor.TryTransferInputFrom(_carryStack)
                    && _processor.InputWood == inputBefore + 1
                    && _carryStack.TotalAmount == 0,
                "Manual Processor feeding regressed after automation cycles.");
            Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                    && _salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == _cashBeforeManualOutput + 20
                    && _woodSaleEventCount == 4
                    && _woodSaleCash == 20,
                "Direct $5 Wood sale regressed after automation cycles.");
            AdvanceTo(Stage.PrepareWorkerIsolation);
        }

        private static void TickPrepareWorkerIsolation()
        {
            EnsureStageTimeout(3d);
            if (_processor.OutputPlanks < _processor.OutputCapacity
                || _processor.IsProcessing)
            {
                return;
            }

            _autoFeeder.enabled = false;
            _workerDepositStart = _worker.CompletedDepositCount;
            _workerStockpileOwnedStart = _stockpile.TotalOwnedWood;
            _workerProcessorInputStart = _processor.InputWood;
            _workerProcessorOutputStart = _processor.OutputPlanks;
            _workerCashStart = _cashPile.StoredCash;
            Require(!_worker.IsCarrying && !_worker.HasIncomingReservation,
                "Worker retained cargo or reservation before Stockpile-only regression.");
            _worker.enabled = true;
            AdvanceTo(Stage.WaitForWorkerDeposit);
        }

        private static void TickWaitForWorkerDeposit()
        {
            EnsureStageTimeout(18d);
            if (_worker.CompletedDepositCount <= _workerDepositStart)
            {
                return;
            }

            _worker.enabled = false;
            Require(_worker.CompletedDepositCount == _workerDepositStart + 1
                    && _stockpile.TotalOwnedWood == _workerStockpileOwnedStart + 1
                    && _stockpile.StoredWood == _workerStockpileOwnedStart + 1
                    && _stockpile.OutgoingReservations == 0
                    && _processor.InputWood == _workerProcessorInputStart
                    && _processor.OutputPlanks == _workerProcessorOutputStart
                    && _cashPile.StoredCash == _workerCashStart
                    && _carryStack.TotalAmount == 0,
                "Worker did not remain Stockpile-only when input automation was disabled.");
            Require(_productionCompletionCount == 1
                    && _productionAppliedCount == 1
                    && _workerCompletionCount == 1
                    && _workerPadUnlockCount == 1
                    && _workerActivationCount == 1
                    && _processorCompletionCount == 1
                    && _processorPadUnlockCount == 1
                    && _processorActivationCount == 1
                    && _autoFeederCompletionCount == 1
                    && _autoFeederPadUnlockCount == 1
                    && _autoFeederActivationCount == 1
                    && _autoFeederUnlockFeedback.PresentationCount == 1,
                "A progression purchase, unlock, activation, or feedback completed more than once.");

            Pass(
                $"M5 Auto Feeder Play Mode smoke passed: {_autoFeeder.CompletedTransferCount} exact transfers ({RepeatedTransferCycles} consecutive), two safe cancellation paths, capacity/source pause-resume, player contention, and manual worker/input/output/sale regressions.");
        }

        private static void DepositWoodToStockpile(int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                Require(_stockpile.TryReserveIncoming(out WoodStockpileReservation reservation)
                        && reservation.IsValid
                        && _stockpile.TryDepositReserved(reservation),
                    $"Could not stage Stockpile Wood {i + 1} of {amount}.");
            }
        }

        private static int GetWoodEquivalent()
        {
            return _stockpile.TotalOwnedWood
                   + _processor.InputWood
                   + _carryStack.GetAmount(ResourceType.Wood)
                   + (_processor.OutputPlanks * _processor.RecipeInputWood);
        }

        private static void ProcessExactPayment(PurchasePad pad, int expectedAmount)
        {
            int totalPaid = 0;
            int guard = 0;
            while (totalPaid < expectedAmount && guard++ < 128)
            {
                int paid = pad.ProcessPaymentStep();
                Require(paid == pad.SpendPerTick,
                    $"{pad.PurchaseLabel} payment tick spent ${paid}; expected ${pad.SpendPerTick}.");
                totalPaid += paid;
            }

            Require(totalPaid == expectedAmount,
                $"{pad.PurchaseLabel} paid ${totalPaid}; expected exactly ${expectedAmount}.");
        }

        private static PurchasePad FindPurchasePad(int totalCost)
        {
            PurchasePad[] pads = Object.FindObjectsByType<PurchasePad>(FindObjectsInactive.Include);
            PurchasePad result = null;
            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i].TotalCost != totalCost)
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"M5 smoke found more than one ${totalCost} Purchase Pad.");
                }

                result = pads[i];
            }

            return result;
        }

        private static T FindSingleIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"M5 smoke expected exactly one {typeof(T).Name}, found {matches.Length}.");
            }

            return matches[0];
        }

        private static void MovePlayerTo(Vector3 destination)
        {
            Vector3 planarDestination = new Vector3(destination.x, 0f, destination.z);
            _playerController.Move(planarDestination - _playerController.transform.position);
            Physics.SyncTransforms();
        }

        private static WoodAutoFeederTransferVisual FindActiveTransferVisual(uint generation)
        {
            WoodAutoFeederTransferVisual[] visuals =
                Object.FindObjectsByType<WoodAutoFeederTransferVisual>(
                    FindObjectsInactive.Include);
            WoodAutoFeederTransferVisual result = null;
            for (int i = 0; i < visuals.Length; i++)
            {
                if (!visuals[i].IsLeased || visuals[i].Generation != generation)
                {
                    continue;
                }

                Require(result == null,
                    "More than one pooled visual owned the same feeder transfer.");
                result = visuals[i];
            }

            return result;
        }

        private static void HandleProductionCompleted()
        {
            _productionCompletionCount++;
        }

        private static void HandleProductionApplied()
        {
            _productionAppliedCount++;
        }

        private static void HandleWorkerCompleted()
        {
            _workerCompletionCount++;
        }

        private static void HandleWorkerPadUnlocked()
        {
            _workerPadUnlockCount++;
        }

        private static void HandleWorkerActivated()
        {
            _workerActivationCount++;
        }

        private static void HandleProcessorCompleted()
        {
            _processorCompletionCount++;
        }

        private static void HandleProcessorPadUnlocked()
        {
            _processorPadUnlockCount++;
        }

        private static void HandleProcessorActivated()
        {
            _processorActivationCount++;
        }

        private static void HandleAutoFeederCompleted()
        {
            _autoFeederCompletionCount++;
        }

        private static void HandleAutoFeederPadUnlocked()
        {
            _autoFeederPadUnlockCount++;
            Require(_autoFeederUnlock.IsPadUnlocked
                    && _autoFeederUnlock.AutoFeederPurchasePadRoot.activeSelf
                    && _autoFeederPad.IsAvailable,
                "Auto Feeder pad unlock event preceded authoritative state.");
        }

        private static void HandleAutoFeederActivated()
        {
            _autoFeederActivationCount++;
            Require(_autoFeederUnlock.IsAutoFeederActivated
                    && _autoFeederUnlock.AutoFeederRoot.activeSelf
                    && _autoFeeder.isActiveAndEnabled,
                "Auto Feeder activation event preceded authoritative state.");
        }

        private static void HandleTransferStarted(uint generation)
        {
            Require(generation != 0
                    && _autoFeeder.IsTransferInFlight
                    && _stockpile.OutgoingReservations == 1
                    && _processor.ReservedInputCapacity == 1
                    && _autoFeederFeedback.ActiveVisualGeneration == generation,
                "Transfer-start event preceded complete logical/visual reservation state.");
            _transferStartedEventCount++;
        }

        private static void HandleTransferCompleted(uint generation)
        {
            Require(generation != 0
                    && !_autoFeeder.IsTransferInFlight
                    && _stockpile.OutgoingReservations == 0
                    && _processor.ReservedInputCapacity == 0,
                "Transfer-completed event preceded atomic ownership commit.");
            _transferCompletedEventCount++;
        }

        private static void HandleTransferCancelled(uint generation)
        {
            Require(generation != 0
                    && !_autoFeeder.IsTransferInFlight
                    && _stockpile.OutgoingReservations == 0
                    && _processor.ReservedInputCapacity == 0,
                "Transfer-cancelled event preceded source refund/destination release.");
            _transferCancelledEventCount++;
        }

        private static void HandleRecipeCompleted(int inputWood, int outputPlanks)
        {
            Require(inputWood == _processor.InputWood
                    && outputPlanks == _processor.OutputPlanks,
                "Processor recipe event preceded authoritative buffer state.");
            _recipeCompletionEventCount++;
        }

        private static void HandleWorkerDeposited()
        {
            _workerDepositEventCount++;
        }

        private static void HandleUnitSold(SaleFeedbackData feedback)
        {
            Require(feedback.RemainingAmount
                    == _carryStack.GetAmount(feedback.ResourceType),
                "Sale event preceded authoritative CarryStack removal.");
            if (feedback.ResourceType == ResourceType.Wood)
            {
                Require(feedback.CashValue == 5,
                    "Sale Point reported a Wood value other than $5.");
                _woodSaleEventCount++;
                _woodSaleCash += feedback.CashValue;
            }
            else if (feedback.ResourceType == ResourceType.Plank)
            {
                Require(feedback.CashValue == 15,
                    "Sale Point reported a Plank value other than $15.");
                _plankSaleEventCount++;
                _plankSaleCash += feedback.CashValue;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Sale Point sold unsupported resource {feedback.ResourceType}.");
            }
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
                throw new InvalidOperationException($"M5 smoke timed out in stage {_stage}.");
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
            string result = $"M5 Auto Feeder Play Mode smoke failed: {message}";
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
            if (_productionPad != null)
            {
                _productionPad.Completed -= HandleProductionCompleted;
            }

            if (_productionUpgrade != null)
            {
                _productionUpgrade.Applied -= HandleProductionApplied;
            }

            if (_workerPad != null)
            {
                _workerPad.Completed -= HandleWorkerCompleted;
            }

            if (_workerUnlock != null)
            {
                _workerUnlock.PadUnlocked -= HandleWorkerPadUnlocked;
                _workerUnlock.WorkerActivated -= HandleWorkerActivated;
            }

            if (_processorPad != null)
            {
                _processorPad.Completed -= HandleProcessorCompleted;
            }

            if (_processorUnlock != null)
            {
                _processorUnlock.PadUnlocked -= HandleProcessorPadUnlocked;
                _processorUnlock.ProcessorActivated -= HandleProcessorActivated;
            }

            if (_autoFeederPad != null)
            {
                _autoFeederPad.Completed -= HandleAutoFeederCompleted;
            }

            if (_autoFeederUnlock != null)
            {
                _autoFeederUnlock.PadUnlocked -= HandleAutoFeederPadUnlocked;
                _autoFeederUnlock.AutoFeederActivated -= HandleAutoFeederActivated;
            }

            if (_autoFeeder != null)
            {
                _autoFeeder.TransferStarted -= HandleTransferStarted;
                _autoFeeder.TransferCompleted -= HandleTransferCompleted;
                _autoFeeder.TransferCancelled -= HandleTransferCancelled;
            }

            if (_processor != null)
            {
                _processor.RecipeCompleted -= HandleRecipeCompleted;
            }

            if (_worker != null)
            {
                _worker.WoodDeposited -= HandleWorkerDeposited;
            }

            if (_salePoint != null)
            {
                _salePoint.UnitSold -= HandleUnitSold;
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
