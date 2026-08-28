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
    public static class LumberCampM6PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey = "IndustryTycoon.M6.Smoke.Running";
        private const string CommandLineKey = "IndustryTycoon.M6.Smoke.CommandLine";
        private const string FinishPendingKey = "IndustryTycoon.M6.Smoke.FinishPending";
        private const string SuccessKey = "IndustryTycoon.M6.Smoke.Success";
        private const string ResultMessageKey = "IndustryTycoon.M6.Smoke.ResultMessage";
        private static readonly Vector3 NeutralPosition = new Vector3(0f, 0f, -9.5f);

        private enum Stage
        {
            Warmup,
            CompletePrerequisites,
            PartiallyFundPackingStation,
            CompletePackingStationPurchase,
            VerifyCarryIsolation,
            StartManualWoodChain,
            WaitForManualPlanks,
            WaitForPackingInputZone,
            WaitForFirstCrate,
            VerifyWoodOutputRejection,
            PreparePlankOutputRejection,
            VerifyPlankOutputRejection,
            PrepareFirstCrateCollection,
            WaitForPackingOutputZone,
            WaitForOddCycle,
            CancelInProgressCycle,
            WaitForCancellationRecovery,
            FillPackingInput,
            WaitForFullOutput,
            WaitForCapacityResume,
            CollectFullOutput,
            StartSimultaneousChains,
            WaitForSimultaneousChains
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
        private static PurchasePad _packingPad;
        private static WoodProductionUpgrade _productionUpgrade;
        private static FirstWorkerUnlock _workerUnlock;
        private static FirstProcessorUnlock _processorUnlock;
        private static FirstAutoFeederUnlock _autoFeederUnlock;
        private static FirstPackingStationUnlock _packingUnlock;
        private static LumberWorker _worker;
        private static WoodStockpile _stockpile;
        private static WoodProcessor _processor;
        private static ProcessorInputZone _processorInputZone;
        private static ProcessorOutputZone _processorOutputZone;
        private static WoodAutoFeeder _autoFeeder;
        private static PackingStation _packingStation;
        private static PackingStationInputZone _packingInputZone;
        private static PackingStationOutputZone _packingOutputZone;
        private static PackingStationFeedback _packingFeedback;
        private static PackingStationUnlockFeedback _packingUnlockFeedback;

        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;
        private static int _packingPadUnlockCount;
        private static int _packingCompletionCount;
        private static int _packingActivationCount;
        private static int _packingRecipeEventCount;
        private static int _crateSaleCount;
        private static int _woodSaleCount;
        private static int _plankSaleCount;
        private static int _processorRecipeTarget;
        private static int _packingRecipeTarget;
        private static int _capacityRecipeTarget;
        private static int _outputBeforeCancellation;
        private static int _workerDepositStart;
        private static int _feederTransferStart;
        private static int _processorRecipeStart;
        private static int _packingRecipeStart;

        static LumberCampM6PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/Prototype/Run M6 Finished Product Smoke Test")]
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
                    "Exit Play Mode before starting the M6 finished-product smoke test.");
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
                        "M6 finished-product smoke exceeded its 150-second timeout.");
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
            _packingPad = FindPurchasePad(900);
            _productionUpgrade = Object.FindAnyObjectByType<WoodProductionUpgrade>();
            _workerUnlock = Object.FindAnyObjectByType<FirstWorkerUnlock>();
            _processorUnlock = Object.FindAnyObjectByType<FirstProcessorUnlock>();
            _autoFeederUnlock = Object.FindAnyObjectByType<FirstAutoFeederUnlock>();
            _packingUnlock = Object.FindAnyObjectByType<FirstPackingStationUnlock>();
            _worker = FindSingleIncludingInactive<LumberWorker>();
            _stockpile = Object.FindAnyObjectByType<WoodStockpile>();
            _processor = FindSingleIncludingInactive<WoodProcessor>();
            _processorInputZone = FindSingleIncludingInactive<ProcessorInputZone>();
            _processorOutputZone = FindSingleIncludingInactive<ProcessorOutputZone>();
            _autoFeeder = FindSingleIncludingInactive<WoodAutoFeeder>();
            _packingStation = FindSingleIncludingInactive<PackingStation>();
            _packingInputZone = FindSingleIncludingInactive<PackingStationInputZone>();
            _packingOutputZone = FindSingleIncludingInactive<PackingStationOutputZone>();
            _packingFeedback = FindSingleIncludingInactive<PackingStationFeedback>();
            _packingUnlockFeedback =
                Object.FindAnyObjectByType<PackingStationUnlockFeedback>();

            Require(_playerController != null
                    && _resourceCollector != null
                    && _carryStack != null
                    && _carryStack.Capacity == 12,
                "M6 smoke could not find the accepted Player/CarryStack components.");
            Require(_wallet != null && _wallet.Balance == 0
                    && _cashPile != null && _cashPile.StoredCash == 0,
                "M6 smoke requires initially empty Wallet and Cash Pile state.");
            Require(_salePoint != null
                    && _salePoint.WoodValue == 5
                    && _salePoint.PlankValue == 15
                    && _salePoint.CrateValue == 40,
                "M6 smoke requires $5 Wood / $15 Plank / $40 Crate values.");
            Require(_productionPad != null
                    && _workerPad != null
                    && _processorPad != null
                    && _autoFeederPad != null
                    && _packingPad != null,
                "M6 smoke could not find all five progression Purchase Pads.");
            Require(_productionUpgrade != null
                    && _workerUnlock != null
                    && _processorUnlock != null
                    && _autoFeederUnlock != null
                    && _packingUnlock != null,
                "M6 smoke could not find the full progression chain.");
            Require(_worker != null
                    && _stockpile != null
                    && _processor != null
                    && _autoFeeder != null,
                "M6 smoke could not find the accepted automation chain.");
            Require(_packingStation != null
                    && _packingInputZone != null
                    && _packingOutputZone != null
                    && _packingFeedback != null
                    && _packingUnlockFeedback != null,
                "M6 smoke could not find Packing Station logic/presentation.");
            Require(_packingStation.InputCapacity == 24
                    && _packingStation.OutputCapacity == 12
                    && _packingStation.RecipeInputPlanks == 2
                    && _packingStation.RecipeOutputCrates == 1
                    && Mathf.Approximately(_packingStation.ProcessingDuration, 1.5f),
                "M6 smoke requires the configured 24/12, 2:1, 1.5-second recipe.");
            Require(_packingUnlock.AutoFeederUnlock == _autoFeederUnlock
                    && _packingUnlock.PackingStationPurchasePad == _packingPad
                    && _packingUnlock.PackingStationRoot == _packingStation.gameObject,
                "Packing Station progression references are incomplete.");

            _resourceCollector.enabled = false;
            _processorInputZone.enabled = false;
            _processorOutputZone.enabled = false;
            _packingInputZone.enabled = false;
            _packingOutputZone.enabled = false;
            MovePlayerTo(NeutralPosition);

            ResetCounters();
            SubscribeEvents();
            _runStartedAt = Now;
            AdvanceTo(Stage.Warmup);
            _runtimeInitialized = true;
        }

        private static void ResetCounters()
        {
            _packingPadUnlockCount = 0;
            _packingCompletionCount = 0;
            _packingActivationCount = 0;
            _packingRecipeEventCount = 0;
            _crateSaleCount = 0;
            _woodSaleCount = 0;
            _plankSaleCount = 0;
        }

        private static void SubscribeEvents()
        {
            _packingPad.Completed += HandlePackingPurchaseCompleted;
            _packingUnlock.PadUnlocked += HandlePackingPadUnlocked;
            _packingUnlock.PackingStationActivated += HandlePackingStationActivated;
            _packingStation.RecipeCompleted += HandlePackingRecipeCompleted;
            _salePoint.UnitSold += HandleUnitSold;
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
                case Stage.PartiallyFundPackingStation:
                    TickPartiallyFundPackingStation();
                    break;
                case Stage.CompletePackingStationPurchase:
                    TickCompletePackingStationPurchase();
                    break;
                case Stage.VerifyCarryIsolation:
                    TickVerifyCarryIsolation();
                    break;
                case Stage.StartManualWoodChain:
                    TickStartManualWoodChain();
                    break;
                case Stage.WaitForManualPlanks:
                    TickWaitForManualPlanks();
                    break;
                case Stage.WaitForPackingInputZone:
                    TickWaitForPackingInputZone();
                    break;
                case Stage.WaitForFirstCrate:
                    TickWaitForFirstCrate();
                    break;
                case Stage.VerifyWoodOutputRejection:
                    TickVerifyWoodOutputRejection();
                    break;
                case Stage.PreparePlankOutputRejection:
                    TickPreparePlankOutputRejection();
                    break;
                case Stage.VerifyPlankOutputRejection:
                    TickVerifyPlankOutputRejection();
                    break;
                case Stage.PrepareFirstCrateCollection:
                    TickPrepareFirstCrateCollection();
                    break;
                case Stage.WaitForPackingOutputZone:
                    TickWaitForPackingOutputZone();
                    break;
                case Stage.WaitForOddCycle:
                    TickWaitForOddCycle();
                    break;
                case Stage.CancelInProgressCycle:
                    TickCancelInProgressCycle();
                    break;
                case Stage.WaitForCancellationRecovery:
                    TickWaitForCancellationRecovery();
                    break;
                case Stage.FillPackingInput:
                    TickFillPackingInput();
                    break;
                case Stage.WaitForFullOutput:
                    TickWaitForFullOutput();
                    break;
                case Stage.WaitForCapacityResume:
                    TickWaitForCapacityResume();
                    break;
                case Stage.CollectFullOutput:
                    TickCollectFullOutput();
                    break;
                case Stage.StartSimultaneousChains:
                    TickStartSimultaneousChains();
                    break;
                case Stage.WaitForSimultaneousChains:
                    TickWaitForSimultaneousChains();
                    break;
            }
        }

        private static void TickWarmup()
        {
            if (!HasWaited(0.25d))
            {
                return;
            }

            Require(!_autoFeederUnlock.IsAutoFeederActivated
                    && !_packingUnlock.IsPadUnlocked
                    && !_packingUnlock.IsPackingStationActivated
                    && !_packingPad.IsAvailable
                    && !_packingPad.gameObject.activeSelf
                    && !_packingStation.gameObject.activeSelf,
                "Packing Station pad/station was available before Auto Feeder unlock.");
            AdvanceTo(Stage.CompletePrerequisites);
        }

        private static void TickCompletePrerequisites()
        {
            CompletePurchase(_productionPad);
            Require(_productionUpgrade.IsApplied, "Production prerequisite did not apply.");
            CompletePurchase(_workerPad);
            Require(_workerUnlock.IsWorkerActivated, "Worker prerequisite did not activate.");
            CompletePurchase(_processorPad);
            Require(_processorUnlock.IsProcessorActivated,
                "Processor prerequisite did not activate.");
            Require(!_packingUnlock.IsPadUnlocked
                    && !_packingPad.gameObject.activeSelf,
                "Packing pad unlocked before Auto Feeder purchase completed.");

            CompletePurchase(_autoFeederPad);
            Require(_autoFeederUnlock.IsAutoFeederActivated
                    && _packingUnlock.IsPadUnlocked
                    && !_packingUnlock.IsPackingStationActivated
                    && _packingPad.gameObject.activeSelf
                    && _packingPad.IsAvailable
                    && !_packingStation.gameObject.activeSelf
                    && _packingPadUnlockCount == 1
                    && !_packingUnlock.TryUnlockPad(),
                "Auto Feeder did not reveal the Packing pad exactly once.");

            _worker.enabled = false;
            _autoFeeder.enabled = false;
            AdvanceTo(Stage.PartiallyFundPackingStation);
        }

        private static void TickPartiallyFundPackingStation()
        {
            Require(_wallet.Deposit(65) == 65, "Could not fund the partial $65 M6 payment.");
            for (int i = 0; i < 13; i++)
            {
                Require(_packingPad.ProcessPaymentStep() == 5,
                    "Packing pad rejected a valid partial payment tick.");
            }

            Require(_packingPad.RemainingCost == 835
                    && _wallet.Balance == 0
                    && !_packingPad.IsCompleted
                    && _packingCompletionCount == 0,
                "Packing pad did not persist its exact $65 partial payment.");
            _packingPad.enabled = false;
            _packingPad.enabled = true;
            Require(_packingPad.RemainingCost == 835
                    && _packingPad.IsAvailable
                    && !_packingPad.IsCompleted,
                "Packing partial payment was lost after leave/re-entry lifecycle.");
            AdvanceTo(Stage.CompletePackingStationPurchase);
        }

        private static void TickCompletePackingStationPurchase()
        {
            CompletePurchase(_packingPad);
            Require(_packingPad.IsCompleted
                    && _packingPad.RemainingCost == 0
                    && _packingCompletionCount == 1
                    && _packingActivationCount == 1
                    && _packingUnlock.IsPackingStationActivated
                    && _packingStation.gameObject.activeSelf
                    && !_packingUnlock.TryActivatePackingStation()
                    && _packingCompletionCount == 1
                    && _packingActivationCount == 1,
                "Packing Station purchase/activation did not complete exactly once.");
            Require(_packingUnlockFeedback.PresentationCount == 1
                    && _packingFeedback.OutputVisualPoolCount == 6
                    && _packingFeedback.DisplayedState
                       == PackingStationFeedbackState.WaitingForInput,
                "Packing Station unlock or idle presentation did not initialize correctly.");
            AdvanceTo(Stage.VerifyCarryIsolation);
        }

        private static void TickVerifyCarryIsolation()
        {
            Require(_carryStack.TotalAmount == 0
                    && _carryStack.TryAdd(ResourceType.Crate, 1)
                    && !_carryStack.TryAdd(ResourceType.Wood, 1)
                    && !_carryStack.TryAdd(ResourceType.Plank, 1)
                    && _carryStack.TryRemove(ResourceType.Crate, 1)
                    && _carryStack.TryAdd(ResourceType.Wood, 1)
                    && !_carryStack.TryAdd(ResourceType.Plank, 1)
                    && !_carryStack.TryAdd(ResourceType.Crate, 1)
                    && _carryStack.TryRemove(ResourceType.Wood, 1)
                    && _carryStack.TryAdd(ResourceType.Plank, 1)
                    && !_carryStack.TryAdd(ResourceType.Wood, 1)
                    && !_carryStack.TryAdd(ResourceType.Crate, 1)
                    && _carryStack.TryRemove(ResourceType.Plank, 1),
                "CarryStack failed Wood/Plank/Crate type isolation.");
            Require(_carryStack.TryReserveCapacity(ResourceType.Crate, 1)
                    && !_carryStack.TryCommitReservedAdd(ResourceType.Plank, 1)
                    && _carryStack.TryCommitReservedAdd(ResourceType.Crate, 1)
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && _carryStack.TryRemove(ResourceType.Crate, 1)
                    && _carryStack.TotalAmount == 0,
                "CarryStack Crate reservation was mixed, lost, or duplicated.");
            Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                    && !_packingStation.TryTransferInputFrom(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Wood) == 1
                    && PackingInputOwnership == 0
                    && _carryStack.TryRemove(ResourceType.Wood, 1)
                    && _carryStack.TryAdd(ResourceType.Crate, 1)
                    && !_packingStation.TryTransferInputFrom(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && PackingInputOwnership == 0
                    && _carryStack.TryRemove(ResourceType.Crate, 1),
                "Packing input accepted Wood/Crate or changed authoritative ownership.");
            AdvanceTo(Stage.StartManualWoodChain);
        }

        private static void TickStartManualWoodChain()
        {
            Require(_packingStation.InputPlanks == 0
                    && _packingStation.ProcessingInputPlanks == 0
                    && _packingStation.OutputCrates == 0
                    && _packingStation.ReservedOutputCapacity == 0
                    && _carryStack.TryAdd(ResourceType.Wood, 4),
                "Manual M6 chain did not start from empty authoritative buffers.");
            for (int i = 0; i < 4; i++)
            {
                Require(_processor.TryTransferInputFrom(_carryStack),
                    "Wood Processor rejected one of four manual Wood inputs.");
            }

            Require(_carryStack.TotalAmount == 0 && _processor.InputWood == 4,
                "Four manual Wood inputs were lost or duplicated.");
            _processorRecipeTarget = _processor.CompletedRecipeCount + 2;
            AdvanceTo(Stage.WaitForManualPlanks);
        }

        private static void TickWaitForManualPlanks()
        {
            EnsureStageTimeout(5d);
            if (_processor.CompletedRecipeCount < _processorRecipeTarget
                || _processor.OutputPlanks < 2
                || _processor.IsProcessing)
            {
                return;
            }

            Require(_processor.InputWood == 0 && _processor.OutputPlanks == 2,
                "Exactly four Wood did not become exactly two Planks.");
            Require(_processor.TryTransferOutputTo(_carryStack)
                    && _processor.TryTransferOutputTo(_carryStack)
                    && _processor.OutputPlanks == 0
                    && _carryStack.GetAmount(ResourceType.Plank) == 2,
                "Manual Plank collection lost, duplicated, or mixed output.");
            _packingInputZone.enabled = true;
            MovePlayerTo(_packingInputZone.transform.position);
            AdvanceTo(Stage.WaitForPackingInputZone);
        }

        private static void TickWaitForPackingInputZone()
        {
            EnsureStageTimeout(2d);
            if (_carryStack.TotalAmount > 0 || !_packingStation.IsProcessing)
            {
                return;
            }

            Require(_packingInputZone.IsPlayerInside
                    && _packingStation.InputPlanks == 0
                    && _packingStation.ProcessingInputPlanks == 2
                    && _packingStation.ReservedOutputCapacity == 1
                    && _packingStation.OutputCrates == 0
                    && _packingFeedback.DisplayedState
                       == PackingStationFeedbackState.Working,
                "Actual Packing input trigger did not progressively transfer two Planks into one atomic cycle.");
            _packingInputZone.enabled = false;
            MovePlayerTo(NeutralPosition);
            _packingRecipeTarget = _packingStation.CompletedRecipeCount + 1;
            AdvanceTo(Stage.WaitForFirstCrate);
        }

        private static void TickWaitForFirstCrate()
        {
            EnsureStageTimeout(4d);
            if (_packingStation.CompletedRecipeCount < _packingRecipeTarget)
            {
                return;
            }

            Require(!_packingStation.IsProcessing
                    && _packingStation.InputPlanks == 0
                    && _packingStation.ProcessingInputPlanks == 0
                    && _packingStation.OutputCrates == 1
                    && _packingStation.ReservedOutputCapacity == 0,
                "Exactly two Planks did not become exactly one stored Crate.");
            Require(_carryStack.TryAdd(ResourceType.Wood, 1),
                "Could not prepare Wood for actual Packing output rejection.");
            _packingOutputZone.enabled = true;
            MovePlayerTo(_packingOutputZone.transform.position);
            AdvanceTo(Stage.VerifyWoodOutputRejection);
        }

        private static void TickVerifyWoodOutputRejection()
        {
            if (!HasWaited(0.35d))
            {
                return;
            }

            Require(_packingOutputZone.IsPlayerInside
                    && _packingStation.OutputCrates == 1
                    && _carryStack.GetAmount(ResourceType.Wood) == 1
                    && _carryStack.GetAmount(ResourceType.Crate) == 0,
                "Actual Packing output trigger changed state while CarryStack held Wood.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.PreparePlankOutputRejection);
        }

        private static void TickPreparePlankOutputRejection()
        {
            EnsureStageTimeout(1d);
            if (_packingOutputZone.IsPlayerInside)
            {
                return;
            }

            Require(_carryStack.TryRemove(ResourceType.Wood, 1)
                    && _carryStack.TryAdd(ResourceType.Plank, 1),
                "Could not replace carried Wood with Plank for output rejection.");
            MovePlayerTo(_packingOutputZone.transform.position);
            AdvanceTo(Stage.VerifyPlankOutputRejection);
        }

        private static void TickVerifyPlankOutputRejection()
        {
            if (!HasWaited(0.35d))
            {
                return;
            }

            Require(_packingOutputZone.IsPlayerInside
                    && _packingStation.OutputCrates == 1
                    && _carryStack.GetAmount(ResourceType.Plank) == 1
                    && _carryStack.GetAmount(ResourceType.Crate) == 0,
                "Actual Packing output trigger changed state while CarryStack held Planks.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.PrepareFirstCrateCollection);
        }

        private static void TickPrepareFirstCrateCollection()
        {
            EnsureStageTimeout(1d);
            if (_packingOutputZone.IsPlayerInside)
            {
                return;
            }

            Require(_carryStack.TryRemove(ResourceType.Plank, 1)
                    && _carryStack.TotalAmount == 0,
                "Could not clear Planks before actual Crate collection.");
            MovePlayerTo(_packingOutputZone.transform.position);
            AdvanceTo(Stage.WaitForPackingOutputZone);
        }

        private static void TickWaitForPackingOutputZone()
        {
            EnsureStageTimeout(1.5d);
            if (_carryStack.GetAmount(ResourceType.Crate) < 1)
            {
                return;
            }

            Require(_packingOutputZone.IsPlayerInside
                    && _packingStation.OutputCrates == 0
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && _carryStack.GetAmount(ResourceType.Wood) == 0
                    && _carryStack.GetAmount(ResourceType.Plank) == 0,
                "Empty CarryStack did not collect exactly one Crate from the actual output trigger.");
            _packingOutputZone.enabled = false;
            MovePlayerTo(NeutralPosition);

            int cashBeforeSales = _cashPile.StoredCash;
            Require(_salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBeforeSales + 40
                    && _carryStack.TryAdd(ResourceType.Wood, 1)
                    && _salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBeforeSales + 45
                    && _carryStack.TryAdd(ResourceType.Plank, 1)
                    && _salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBeforeSales + 60,
                "Generic Sale Point did not preserve Crate $40, Wood $5, and Plank $15.");
            Require((4 * _salePoint.WoodValue) == 20
                    && (2 * _salePoint.PlankValue) == 30
                    && _salePoint.CrateValue == 40,
                "M6 chain economics no longer resolve to $20 / $30 / $40.");

            Require(_carryStack.TryAdd(ResourceType.Plank, 3),
                "Could not prepare the odd Plank deposit.");
            for (int i = 0; i < 3; i++)
            {
                Require(_packingStation.TryTransferInputFrom(_carryStack),
                    "Odd Plank deposit lost a unit.");
            }

            Require(_carryStack.TotalAmount == 0
                    && _packingStation.InputPlanks == 1
                    && _packingStation.ProcessingInputPlanks == 2
                    && _packingStation.IsProcessing,
                "Odd Plank deposit did not leave exactly one waiting Plank.");
            _packingRecipeTarget = _packingStation.CompletedRecipeCount + 1;
            AdvanceTo(Stage.WaitForOddCycle);
        }

        private static void TickWaitForOddCycle()
        {
            EnsureStageTimeout(4d);
            if (_packingStation.CompletedRecipeCount < _packingRecipeTarget)
            {
                return;
            }

            Require(_packingStation.InputPlanks == 1
                    && !_packingStation.IsProcessing
                    && _packingStation.OutputCrates == 1
                    && _packingFeedback.DisplayedState
                       == PackingStationFeedbackState.WaitingForInput,
                "One odd Plank did not wait in the idle/no-input state.");
            Require(_packingStation.TryTransferOutputTo(_carryStack)
                    && _salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _carryStack.TryAdd(ResourceType.Plank, 1)
                    && _packingStation.TryTransferInputFrom(_carryStack)
                    && _packingStation.IsProcessing
                    && _packingStation.InputPlanks == 0
                    && _packingStation.ProcessingInputPlanks == 2
                    && _packingStation.ReservedOutputCapacity == 1,
                "Could not prepare a cancellable Packing cycle.");
            _outputBeforeCancellation = _packingStation.OutputCrates;
            AdvanceTo(Stage.CancelInProgressCycle);
        }

        private static void TickCancelInProgressCycle()
        {
            _packingStation.enabled = false;
            Require(!_packingStation.IsProcessing
                    && _packingStation.InputPlanks == 2
                    && _packingStation.ProcessingInputPlanks == 0
                    && _packingStation.ReservedOutputCapacity == 0
                    && _packingStation.OutputCrates == _outputBeforeCancellation,
                "Disabling Packing Station did not refund exactly two Planks and release output.");
            _packingStation.enabled = true;
            Require(_packingStation.IsProcessing
                    && _packingStation.InputPlanks == 0
                    && _packingStation.ProcessingInputPlanks == 2
                    && _packingStation.ReservedOutputCapacity == 1
                    && _packingStation.OutputCrates == _outputBeforeCancellation,
                "Re-enabled Packing Station did not resume its safely refunded cycle.");
            _packingRecipeTarget = _packingStation.CompletedRecipeCount + 1;
            AdvanceTo(Stage.WaitForCancellationRecovery);
        }

        private static void TickWaitForCancellationRecovery()
        {
            EnsureStageTimeout(4d);
            if (_packingStation.CompletedRecipeCount < _packingRecipeTarget)
            {
                return;
            }

            Require(_packingStation.OutputCrates == _outputBeforeCancellation + 1
                    && _packingStation.InputPlanks == 0
                    && _packingStation.ProcessingInputPlanks == 0
                    && _packingStation.ReservedOutputCapacity == 0
                    && _packingStation.TryTransferOutputTo(_carryStack)
                    && _salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _packingStation.OutputCrates == 0,
                "Cancellation recovery duplicated or lost its finished Crate.");
            AdvanceTo(Stage.FillPackingInput);
        }

        private static void TickFillPackingInput()
        {
            int ownedBefore = PackingInputOwnership;
            for (int batch = 0; batch < 2; batch++)
            {
                Require(_carryStack.TryAdd(ResourceType.Plank, _carryStack.Capacity),
                    "Could not prepare a full Plank CarryStack for input-capacity testing.");
                for (int unit = 0; unit < _carryStack.Capacity; unit++)
                {
                    Require(_packingStation.TryTransferInputFrom(_carryStack),
                        "Packing input rejected a Plank before reaching capacity.");
                }
            }

            Require(ownedBefore == 0
                    && PackingInputOwnership == _packingStation.InputCapacity
                    && _packingStation.AvailableInputCapacity == 0
                    && _packingStation.IsProcessing
                    && _packingStation.ProcessingInputPlanks == 2
                    && _packingStation.ReservedOutputCapacity == 1
                    && _carryStack.TotalAmount == 0,
                "Packing input did not reach exact 24-unit ownership without duplication.");
            Require(_carryStack.TryAdd(ResourceType.Plank, 1)
                    && !_packingStation.TryTransferInputFrom(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Plank) == 1
                    && PackingInputOwnership == _packingStation.InputCapacity,
                "Packing Station exceeded input capacity or consumed the rejected 25th Plank.");
            _capacityRecipeTarget = _packingStation.CompletedRecipeCount + 12;
            AdvanceTo(Stage.WaitForFullOutput);
        }

        private static void TickWaitForFullOutput()
        {
            EnsureStageTimeout(25d);
            if (_packingStation.CompletedRecipeCount < _capacityRecipeTarget
                || _packingStation.OutputCrates < _packingStation.OutputCapacity)
            {
                return;
            }

            Require(!_packingStation.IsProcessing
                    && PackingInputOwnership == 0
                    && _packingStation.OutputCrates == 12
                    && _packingStation.ReservedOutputCapacity == 0
                    && _packingFeedback.DisplayedState == PackingStationFeedbackState.OutputFull
                    && _carryStack.GetAmount(ResourceType.Plank) == 1,
                "Repeated production did not stop cleanly at full Crate output.");
            Require(_carryStack.TryAdd(ResourceType.Plank, 1)
                    && _packingStation.TryTransferInputFrom(_carryStack)
                    && _packingStation.TryTransferInputFrom(_carryStack)
                    && _carryStack.TotalAmount == 0
                    && _packingStation.InputPlanks == 2
                    && !_packingStation.IsProcessing
                    && _packingStation.OutputCrates == 12,
                "Output-full Packing Station did not retain two waiting Planks.");
            Require(_packingStation.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && _packingStation.OutputCrates == 11
                    && _packingStation.IsProcessing
                    && _packingStation.ProcessingInputPlanks == 2
                    && _packingStation.ReservedOutputCapacity == 1
                 && _packingStation.OutputCrates
                        + _packingStation.ReservedOutputCapacity == 12,
                "Removing one full-output Crate did not automatically resume with reservation.");
            _packingRecipeTarget = _packingStation.CompletedRecipeCount + 1;
            AdvanceTo(Stage.WaitForCapacityResume);
        }

        private static void TickWaitForCapacityResume()
        {
            EnsureStageTimeout(4d);
            if (_packingStation.CompletedRecipeCount < _packingRecipeTarget)
            {
                return;
            }

            Require(!_packingStation.IsProcessing
                    && PackingInputOwnership == 0
                    && _packingStation.OutputCrates == 12
                    && _packingStation.ReservedOutputCapacity == 0
                    && _carryStack.GetAmount(ResourceType.Crate) == 1,
                "Resumed full-output cycle did not finish as exactly one Crate.");
            AdvanceTo(Stage.CollectFullOutput);
        }

        private static void TickCollectFullOutput()
        {
            for (int i = 1; i < _carryStack.Capacity; i++)
            {
                Require(_packingStation.TryTransferOutputTo(_carryStack),
                    "Crate collection stopped before CarryStack capacity 12.");
            }

            Require(_carryStack.TotalAmount == 12
                    && _carryStack.GetAmount(ResourceType.Crate) == 12
                    && _carryStack.GetAmount(ResourceType.Wood) == 0
                    && _carryStack.GetAmount(ResourceType.Plank) == 0
                    && _packingStation.OutputCrates == 1
                    && !_packingStation.TryTransferOutputTo(_carryStack)
                    && _packingStation.OutputCrates == 1,
                "Full CarryStack did not reject a thirteenth Crate without changing output.");
            SellAllCarried();
            Require(_carryStack.TotalAmount == 0
                    && _packingStation.TryTransferOutputTo(_carryStack)
                    && _salePoint.TryUnloadOne()
                    && _carryStack.TotalAmount == 0
                    && _packingStation.OutputCrates == 0,
                "Progressive sale/collection did not resolve all full-output Crates exactly once.");
            AdvanceTo(Stage.StartSimultaneousChains);
        }

        private static void TickStartSimultaneousChains()
        {
            Require(_stockpile.TotalOwnedWood == 0
                    && _processor.InputWood == 0
                    && _processor.OutputPlanks == 0
                    && !_worker.IsCarrying
                    && !_worker.HasIncomingReservation
                    && !_autoFeeder.IsTransferInFlight,
                "Simultaneous regression did not begin from an empty causal automation chain.");
            _workerDepositStart = _worker.CompletedDepositCount;
            _feederTransferStart = _autoFeeder.CompletedTransferCount;
            _processorRecipeStart = _processor.CompletedRecipeCount;
            _packingRecipeStart = _packingStation.CompletedRecipeCount;

            Require(_carryStack.TryAdd(ResourceType.Plank, 2)
                    && _packingStation.TryTransferInputFrom(_carryStack)
                    && _packingStation.TryTransferInputFrom(_carryStack)
                    && _packingStation.IsProcessing,
                "Could not start the simultaneous manual Packing cycle.");
            _worker.enabled = true;
            _autoFeeder.enabled = true;
            AdvanceTo(Stage.WaitForSimultaneousChains);
        }

        private static void TickWaitForSimultaneousChains()
        {
            EnsureStageTimeout(50d);
            bool workerAdvanced = _worker.CompletedDepositCount >= _workerDepositStart + 2;
            bool feederAdvanced = _autoFeeder.CompletedTransferCount
                                  >= _feederTransferStart + 2;
            bool processorAdvanced = _processor.CompletedRecipeCount > _processorRecipeStart;
            bool packingAdvanced = _packingStation.CompletedRecipeCount > _packingRecipeStart;
            if (!workerAdvanced || !feederAdvanced || !processorAdvanced || !packingAdvanced)
            {
                return;
            }

            Require(_packingStation.InputPlanks == 0
                    && _packingStation.ProcessingInputPlanks == 0
                    && _packingStation.OutputCrates == 1
                    && _carryStack.TotalAmount == 0
                     && _stockpile.StoredWood >= 0
                     && _processor.InputWood >= 0
                     && _processor.OutputPlanks >= 1,
                "Simultaneous automation/manual chains crossed boundaries or lost ownership.");
            int cashBeforeFinalCrate = _cashPile.StoredCash;
            Require(_packingStation.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Crate) == 1
                    && _salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBeforeFinalCrate + 40
                    && _carryStack.TotalAmount == 0
                    && _packingStation.OutputCrates == 0,
                "Manual Crate collection/sale did not complete while automation remained active.");
            Require(_packingPadUnlockCount == 1
                    && _packingCompletionCount == 1
                    && _packingActivationCount == 1
                     && _packingUnlockFeedback.PresentationCount == 1
                     && _packingRecipeEventCount == _packingStation.CompletedRecipeCount
                     && _packingFeedback.CompletionFeedbackCount
                        == _packingStation.CompletedRecipeCount
                     && _woodSaleCount == 1
                     && _plankSaleCount == 1
                     && _crateSaleCount + _packingStation.OutputCrates
                        == _packingStation.CompletedRecipeCount,
                 "M6 completion/unlock/processing feedback counts were not exactly once per event.");
            Pass(
                $"M6 Finished Product Play Mode smoke passed: {_packingStation.CompletedRecipeCount} exact 2 Plank -> 1 Crate recipes, atomic cancellation recovery, 24/12 capacity pause-resume, three typed sales, and simultaneous Worker/Feeder/Processor/Packing operation.");
        }

        private static void ValidateContinuousInvariants()
        {
            int wood = _carryStack.GetAmount(ResourceType.Wood);
            int planks = _carryStack.GetAmount(ResourceType.Plank);
            int crates = _carryStack.GetAmount(ResourceType.Crate);
            int activeTypes = (wood > 0 ? 1 : 0)
                              + (planks > 0 ? 1 : 0)
                              + (crates > 0 ? 1 : 0);
            Require(wood >= 0 && planks >= 0 && crates >= 0
                    && wood + planks + crates == _carryStack.TotalAmount
                    && _carryStack.TotalAmount <= _carryStack.Capacity
                    && _carryStack.ReservedCapacity >= 0
                    && _carryStack.TotalAmount + _carryStack.ReservedCapacity
                       <= _carryStack.Capacity
                    && activeTypes <= 1,
                "CarryStack became negative, mixed, duplicated, or over capacity.");
            if (_carryStack.TotalAmount == 0)
            {
                Require(!_carryStack.ActiveResourceType.HasValue,
                    "Empty CarryStack retained an active resource type.");
            }
            else
            {
                Require(_carryStack.ActiveResourceType.HasValue
                        && ((wood > 0 && _carryStack.ActiveResourceType == ResourceType.Wood)
                            || (planks > 0
                                && _carryStack.ActiveResourceType == ResourceType.Plank)
                            || (crates > 0
                                && _carryStack.ActiveResourceType == ResourceType.Crate)),
                    "CarryStack active type disagreed with authoritative ownership.");
            }

            Require(_packingStation.InputPlanks >= 0
                    && _packingStation.ProcessingInputPlanks >= 0
                    && PackingInputOwnership <= _packingStation.InputCapacity
                    && _packingStation.OutputCrates >= 0
                    && _packingStation.ReservedOutputCapacity >= 0
                    && _packingStation.OutputCrates
                       + _packingStation.ReservedOutputCapacity
                       <= _packingStation.OutputCapacity,
                "Packing Station buffer became negative or exceeded capacity.");
            Require(_packingStation.IsProcessing
                    ? _packingStation.ProcessingInputPlanks == 2
                      && _packingStation.ReservedOutputCapacity == 1
                    : _packingStation.ProcessingInputPlanks == 0
                      && _packingStation.ReservedOutputCapacity == 0,
                "Packing Station processing ownership/reservation became inconsistent.");
            Require(_processor.InputWood >= 0
                    && _processor.ReservedInputCapacity >= 0
                    && _processor.InputWood + _processor.ReservedInputCapacity
                       <= _processor.InputCapacity
                    && _processor.OutputPlanks >= 0
                    && _processor.ReservedOutputCapacity >= 0
                    && _processor.OutputPlanks + _processor.ReservedOutputCapacity
                       <= _processor.OutputCapacity,
                "Wood Processor regressed to negative or over-capacity buffers.");
            Require(_stockpile.StoredWood >= 0
                    && _stockpile.IncomingReservations >= 0
                    && _stockpile.OutgoingReservations >= 0
                    && _stockpile.StoredWood
                       + _stockpile.IncomingReservations
                       + _stockpile.OutgoingReservations <= _stockpile.Capacity,
                "Stockpile ownership became negative or over capacity during M6.");
            Require(_autoFeeder.ActiveTransferCount >= 0
                    && _autoFeeder.ActiveTransferCount <= 1,
                "Auto Feeder exceeded its single-transfer boundary.");
        }

        private static int PackingInputOwnership =>
            _packingStation.InputPlanks + _packingStation.ProcessingInputPlanks;

        private static void CompletePurchase(PurchasePad pad)
        {
            Require(pad != null && !pad.IsCompleted && pad.IsAvailable,
                "M6 tried to fund an unavailable/completed prerequisite pad.");
            int expectedAmount = pad.RemainingCost;
            Require(_wallet.Deposit(expectedAmount) == expectedAmount,
                $"Could not fund {pad.PurchaseLabel}.");
            int totalPaid = 0;
            int guard = 0;
            while (!pad.IsCompleted && guard++ < 256)
            {
                totalPaid += pad.ProcessPaymentStep();
            }

            Require(pad.IsCompleted
                    && pad.RemainingCost == 0
                    && totalPaid == expectedAmount
                    && _wallet.Balance == 0,
                $"{pad.PurchaseLabel} paid ${totalPaid}; expected exactly ${expectedAmount}.");
        }

        private static void SellAllCarried()
        {
            int guard = 0;
            while (_carryStack.TotalAmount > 0 && guard++ < _carryStack.Capacity + 1)
            {
                Require(_salePoint.TryUnloadOne(),
                    "Sale Point stopped before progressive carried-resource unloading finished.");
            }
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
                    $"M6 smoke found more than one ${totalCost} Purchase Pad.");
                result = pads[i];
            }

            return result;
        }

        private static T FindSingleIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            Require(matches.Length == 1,
                $"M6 smoke expected exactly one {typeof(T).Name}, found {matches.Length}.");
            return matches[0];
        }

        private static void MovePlayerTo(Vector3 position)
        {
            Vector3 planarPosition = new Vector3(position.x, 0f, position.z);
            _playerController.Move(planarPosition - _playerController.transform.position);
            Physics.SyncTransforms();
        }

        private static void HandlePackingPadUnlocked()
        {
            _packingPadUnlockCount++;
        }

        private static void HandlePackingPurchaseCompleted()
        {
            _packingCompletionCount++;
        }

        private static void HandlePackingStationActivated()
        {
            _packingActivationCount++;
        }

        private static void HandlePackingRecipeCompleted(int inputPlanks, int outputCrates)
        {
            Require(inputPlanks == _packingStation.InputPlanks
                    && outputCrates == _packingStation.OutputCrates
                    && _packingStation.ProcessingInputPlanks == 0
                    && _packingStation.ReservedOutputCapacity == 0,
                "Packing recipe event preceded authoritative completion state.");
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
                    Require(feedback.CashValue == 40, "Crate sale value is not $40.");
                    _crateSaleCount++;
                    break;
                default:
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
                throw new InvalidOperationException($"M6 smoke timed out in stage {_stage}.");
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
            string result = $"M6 Finished Product Play Mode smoke failed: {message}";
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
            if (_packingPad != null)
            {
                _packingPad.Completed -= HandlePackingPurchaseCompleted;
            }

            if (_packingUnlock != null)
            {
                _packingUnlock.PadUnlocked -= HandlePackingPadUnlocked;
                _packingUnlock.PackingStationActivated -= HandlePackingStationActivated;
            }

            if (_packingStation != null)
            {
                _packingStation.RecipeCompleted -= HandlePackingRecipeCompleted;
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
