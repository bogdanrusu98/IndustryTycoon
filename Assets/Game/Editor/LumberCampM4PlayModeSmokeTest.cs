using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
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
    public static class LumberCampM4PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey = "IndustryTycoon.M4.Smoke.Running";
        private const string CommandLineKey = "IndustryTycoon.M4.Smoke.CommandLine";
        private const string FinishPendingKey = "IndustryTycoon.M4.Smoke.FinishPending";
        private const string SuccessKey = "IndustryTycoon.M4.Smoke.Success";
        private const string ResultMessageKey = "IndustryTycoon.M4.Smoke.ResultMessage";

        private static readonly Vector3 NeutralPosition = new Vector3(0f, 0f, -9.5f);

        private enum Stage
        {
            Warmup,
            FillStockpile,
            CompleteProductionAndWorker,
            PartiallyFundProcessor,
            VerifyPartialProcessorProgress,
            CompleteProcessorPurchase,
            VerifyCarryTypeIsolation,
            EnterProcessorInput,
            WaitForInputTransfer,
            WaitForOddRecipe,
            EnterBlockedOutput,
            VerifyBlockedOutput,
            LeaveBlockedOutput,
            WaitForPlankCollection,
            SellPlank,
            WaitForPlankSale,
            PrepareDirectWoodSale,
            WaitForDirectWoodSale,
            FillProcessorInput,
            WaitForFullOutput,
            VerifyFullOutputPause,
            WaitForSingleOutputCollection,
            WaitForProcessingResume,
            DrainFullOutput,
            StartRepeatedCycles,
            WaitForRepeatedCycles,
            PrepareWorkerCycles,
            WaitForWorkerCycles
        }

        private static CharacterController _playerController;
        private static ResourceCollector _resourceCollector;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static WoodSpawner _woodSpawner;
        private static CashPile _cashPile;
        private static SalePoint _salePoint;
        private static PurchasePad _productionPad;
        private static PurchasePad _workerPad;
        private static PurchasePad _processorPad;
        private static WoodProductionUpgrade _productionUpgrade;
        private static FirstWorkerUnlock _workerUnlock;
        private static LumberWorker _worker;
        private static WoodStockpile _stockpile;
        private static FirstProcessorUnlock _processorUnlock;
        private static WoodProcessor _processor;
        private static ProcessorInputZone _inputZone;
        private static ProcessorOutputZone _outputZone;
        private static WoodProcessorFeedback _processorFeedback;
        private static ProcessorUnlockFeedback _processorUnlockFeedback;

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
        private static int _recipeCompletionEventCount;
        private static int _workerDepositEventCount;
        private static int _woodSaleEventCount;
        private static int _plankSaleEventCount;
        private static int _woodSaleCash;
        private static int _plankSaleCash;

        private static int _fullOutputRecipeStart;
        private static int _fullOutputCompletionTarget;
        private static int _resumeCompletionTarget;
        private static int _repeatCompletionTarget;
        private static int _workerDepositStart;
        private static int _processorRecipesBeforeWorkerCycles;
        private static int _processorInputBeforeWorkerCycles;
        private static int _processorOutputBeforeWorkerCycles;
        private static int _cashBeforeWorkerCycles;
        private static int _walletBeforeWorkerCycles;
        private static int _saleEventsBeforeWorkerCycles;

        static LumberCampM4PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/Prototype/Run M4 Processor Smoke Test")]
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
                    "Exit Play Mode before starting the M4 processor smoke test.");
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

                if (Now - _runStartedAt > 120d)
                {
                    throw new InvalidOperationException(
                        "M4 processor smoke test exceeded its 120-second timeout.");
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
            _resourceCollector = Object.FindAnyObjectByType<ResourceCollector>();
            _carryStack = Object.FindAnyObjectByType<CarryStack>();
            _wallet = Object.FindAnyObjectByType<Wallet>();
            _woodSpawner = Object.FindAnyObjectByType<WoodSpawner>();
            _cashPile = Object.FindAnyObjectByType<CashPile>();
            _salePoint = Object.FindAnyObjectByType<SalePoint>();
            _productionPad = FindPurchasePad(120);
            _workerPad = FindPurchasePad(240);
            _processorPad = FindPurchasePad(360);
            _productionUpgrade = Object.FindAnyObjectByType<WoodProductionUpgrade>();
            _workerUnlock = Object.FindAnyObjectByType<FirstWorkerUnlock>();
            _worker = FindSingleIncludingInactive<LumberWorker>();
            _stockpile = Object.FindAnyObjectByType<WoodStockpile>();
            _processorUnlock = Object.FindAnyObjectByType<FirstProcessorUnlock>();
            _processor = FindSingleIncludingInactive<WoodProcessor>();
            _inputZone = FindSingleIncludingInactive<ProcessorInputZone>();
            _outputZone = FindSingleIncludingInactive<ProcessorOutputZone>();
            _processorFeedback = FindSingleIncludingInactive<WoodProcessorFeedback>();
            _processorUnlockFeedback = Object.FindAnyObjectByType<ProcessorUnlockFeedback>();

            Require(_playerController != null,
                "M4 smoke could not find the Player CharacterController.");
            Require(_resourceCollector != null,
                "M4 smoke could not find the ResourceCollector.");
            Require(_carryStack != null && _carryStack.Capacity == 12,
                "M4 smoke requires the existing 12-capacity CarryStack.");
            Require(_wallet != null && _wallet.Balance == 0,
                "M4 smoke requires an initially empty Wallet.");
            Require(_woodSpawner != null,
                "M4 smoke could not find the WoodSpawner.");
            Require(_cashPile != null && _cashPile.StoredCash == 0,
                "M4 smoke requires an initially empty Cash Pile.");
            Require(_salePoint != null
                    && _salePoint.WoodValue == 5
                    && _salePoint.PlankValue == 15,
                "M4 smoke requires the shared $5 Wood / $15 Plank Sale Point.");
            Require(_productionPad != null && _productionPad.TotalCost == 120,
                "M4 smoke could not find the $120 production Purchase Pad.");
            Require(_workerPad != null && _workerPad.TotalCost == 240,
                "M4 smoke could not find the $240 worker Purchase Pad.");
            Require(_processorPad != null && _processorPad.TotalCost == 360,
                "M4 smoke could not find the $360 processor Purchase Pad.");
            Require(_productionUpgrade != null && _workerUnlock != null && _worker != null,
                "M4 smoke could not find the accepted production/worker unlock chain.");
            Require(_stockpile != null && _stockpile.Capacity == 30,
                "M4 smoke could not find the 30-capacity Wood Stockpile.");
            Require(_processorUnlock != null
                    && _processor != null
                    && _inputZone != null
                    && _outputZone != null,
                "M4 smoke could not find the processor gate, machine, and transfer zones.");
            Require(_processorFeedback != null && _processorUnlockFeedback != null,
                "M4 smoke could not find the processor presentation components.");
            Require(_processor.InputCapacity == 24
                    && _processor.OutputCapacity == 12
                    && _processor.RecipeInputWood == 2
                    && _processor.RecipeOutputPlanks == 1
                    && Mathf.Approximately(_processor.ProcessingDuration, 1.1f),
                "M4 smoke requires the 24/12, 2 Wood -> 1 Plank, 1.1-second recipe.");
            Require(_inputZone.Processor == _processor
                    && _inputZone.CarryStack == _carryStack
                    && _outputZone.Processor == _processor
                    && _outputZone.CarryStack == _carryStack,
                "Processor input/output references are incomplete.");
            Require(_processorUnlock.WorkerUnlock == _workerUnlock
                    && _processorUnlock.ProcessorPurchasePad == _processorPad
                    && _processorUnlock.ProcessorRoot == _processor.gameObject,
                "Processor progression references are incomplete.");

            _resourceCollector.enabled = false;
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
            _processor.RecipeCompleted += HandleRecipeCompleted;
            _worker.WoodDeposited += HandleWorkerDeposited;
            _salePoint.UnitSold += HandleUnitSold;
        }

        private static void ValidateContinuousInvariants()
        {
            Require(_wallet.Balance >= 0,
                "Wallet became negative during the M4 smoke test.");
            Require(_cashPile.StoredCash >= 0,
                "Cash Pile became negative during the M4 smoke test.");

            int carriedWood = _carryStack.GetAmount(ResourceType.Wood);
            int carriedPlanks = _carryStack.GetAmount(ResourceType.Plank);
            Require(carriedWood >= 0
                    && carriedPlanks >= 0
                    && carriedWood + carriedPlanks == _carryStack.TotalAmount,
                "CarryStack resource amounts diverged from its authoritative total.");
            Require(carriedWood == 0 || carriedPlanks == 0,
                "CarryStack mixed Wood and Plank.");
            Require(_carryStack.TotalAmount >= 0
                    && _carryStack.ReservedCapacity >= 0
                    && _carryStack.TotalAmount + _carryStack.ReservedCapacity
                       <= _carryStack.Capacity,
                "CarryStack amount plus reservations exceeded capacity 12.");

            if (_carryStack.TotalAmount == 0)
            {
                Require(!_carryStack.HasActiveResource
                        && !_carryStack.ActiveResourceType.HasValue,
                    "An empty CarryStack retained an active resource type.");
            }
            else
            {
                Require(_carryStack.HasActiveResource
                        && _carryStack.ActiveResourceType.HasValue,
                    "A non-empty CarryStack has no active resource type.");
                ResourceType expectedType = carriedWood > 0
                    ? ResourceType.Wood
                    : ResourceType.Plank;
                Require(_carryStack.ActiveResourceType.Value == expectedType,
                    "CarryStack active type does not match its logical contents.");
            }

            if (_carryStack.ReservedCapacity > 0)
            {
                Require(_carryStack.ReservedResourceType.HasValue,
                    "CarryStack has capacity reserved without a reserved resource type.");
                if (_carryStack.HasActiveResource)
                {
                    Require(_carryStack.ReservedResourceType == _carryStack.ActiveResourceType,
                        "CarryStack reservation type conflicts with its active type.");
                }
            }
            else
            {
                Require(!_carryStack.ReservedResourceType.HasValue,
                    "CarryStack retained a reservation type after releasing all capacity.");
            }

            Require(_stockpile.StoredWood >= 0
                    && _stockpile.IncomingReservations >= 0
                    && _stockpile.StoredWood + _stockpile.IncomingReservations
                       <= _stockpile.Capacity,
                "Wood Stockpile stored plus incoming Wood exceeded capacity.");
            Require(_processor.InputWood >= 0
                    && _processor.InputWood <= _processor.InputCapacity,
                "Processor input buffer exceeded its 0..24 bounds.");
            Require(_processor.OutputPlanks >= 0
                    && _processor.ReservedOutputCapacity >= 0
                    && _processor.OutputPlanks + _processor.ReservedOutputCapacity
                       <= _processor.OutputCapacity,
                "Processor output plus its reservation exceeded capacity 12.");
            Require(_processor.ReservedOutputCapacity <= _processor.RecipeOutputPlanks,
                "Processor reserved more than one recipe output.");
            Require(!_processor.IsProcessing
                    || _processor.ReservedOutputCapacity == _processor.RecipeOutputPlanks,
                "Processor is working without a valid output reservation.");
            Require(_processor.IsProcessing || _processor.ReservedOutputCapacity == 0,
                "Processor retained output capacity while idle.");

            Require(_productionPad.RemainingCost >= 0
                    && _workerPad.RemainingCost >= 0
                    && _processorPad.RemainingCost >= 0,
                "A Purchase Pad remaining cost became negative.");
            Require(_worker.CompletedDepositCount <= _worker.CompletedPickupCount
                    && _worker.CompletedPickupCount - _worker.CompletedDepositCount <= 1,
                "The one-item worker cargo invariant was violated.");
            Require(_woodSpawner.ActiveCount == _woodSpawner.ActiveRegistryCount,
                "WoodSpawner active count diverged from its loose-resource registry.");
        }

        private static void TickCurrentStage()
        {
            switch (_stage)
            {
                case Stage.Warmup:
                    TickWarmup();
                    break;
                case Stage.FillStockpile:
                    TickFillStockpile();
                    break;
                case Stage.CompleteProductionAndWorker:
                    TickCompleteProductionAndWorker();
                    break;
                case Stage.PartiallyFundProcessor:
                    TickPartiallyFundProcessor();
                    break;
                case Stage.VerifyPartialProcessorProgress:
                    TickVerifyPartialProcessorProgress();
                    break;
                case Stage.CompleteProcessorPurchase:
                    TickCompleteProcessorPurchase();
                    break;
                case Stage.VerifyCarryTypeIsolation:
                    TickVerifyCarryTypeIsolation();
                    break;
                case Stage.EnterProcessorInput:
                    TickEnterProcessorInput();
                    break;
                case Stage.WaitForInputTransfer:
                    TickWaitForInputTransfer();
                    break;
                case Stage.WaitForOddRecipe:
                    TickWaitForOddRecipe();
                    break;
                case Stage.EnterBlockedOutput:
                    TickEnterBlockedOutput();
                    break;
                case Stage.VerifyBlockedOutput:
                    TickVerifyBlockedOutput();
                    break;
                case Stage.LeaveBlockedOutput:
                    TickLeaveBlockedOutput();
                    break;
                case Stage.WaitForPlankCollection:
                    TickWaitForPlankCollection();
                    break;
                case Stage.SellPlank:
                    TickSellPlank();
                    break;
                case Stage.WaitForPlankSale:
                    TickWaitForPlankSale();
                    break;
                case Stage.PrepareDirectWoodSale:
                    TickPrepareDirectWoodSale();
                    break;
                case Stage.WaitForDirectWoodSale:
                    TickWaitForDirectWoodSale();
                    break;
                case Stage.FillProcessorInput:
                    TickFillProcessorInput();
                    break;
                case Stage.WaitForFullOutput:
                    TickWaitForFullOutput();
                    break;
                case Stage.VerifyFullOutputPause:
                    TickVerifyFullOutputPause();
                    break;
                case Stage.WaitForSingleOutputCollection:
                    TickWaitForSingleOutputCollection();
                    break;
                case Stage.WaitForProcessingResume:
                    TickWaitForProcessingResume();
                    break;
                case Stage.DrainFullOutput:
                    TickDrainFullOutput();
                    break;
                case Stage.StartRepeatedCycles:
                    TickStartRepeatedCycles();
                    break;
                case Stage.WaitForRepeatedCycles:
                    TickWaitForRepeatedCycles();
                    break;
                case Stage.PrepareWorkerCycles:
                    TickPrepareWorkerCycles();
                    break;
                case Stage.WaitForWorkerCycles:
                    TickWaitForWorkerCycles();
                    break;
            }
        }

        private static void TickWarmup()
        {
            if (!HasWaited(0.6d))
            {
                return;
            }

            Require(_carryStack.TotalAmount == 0 && _carryStack.ReservedCapacity == 0,
                "CarryStack did not start empty and unreserved.");
            Require(!_productionUpgrade.IsApplied
                    && !_workerUnlock.IsPadUnlocked
                    && !_workerUnlock.IsWorkerActivated,
                "Accepted production/worker progression did not start locked.");
            Require(!_processorUnlock.IsPadUnlocked
                    && !_processorUnlock.IsProcessorActivated
                    && !_processorPad.IsAvailable
                    && !_processorPad.IsCompleted,
                "Processor progression did not start locked.");
            Require(!_processorUnlock.ProcessorPurchasePadRoot.activeSelf
                    && !_processorUnlock.ProcessorRoot.activeSelf
                    && !_processor.isActiveAndEnabled,
                "Processor pad or machine was visible/active before worker unlock.");

            Require(_wallet.Deposit(5) == 5,
                "Could not seed Wallet for locked processor-pad verification.");
            Require(_processorPad.ProcessPaymentStep() == 0
                    && _processorPad.RemainingCost == 360
                    && _wallet.Balance == 5,
                "Locked processor Purchase Pad accepted payment or changed progress.");

            AdvanceTo(Stage.FillStockpile);
        }

        private static void TickFillStockpile()
        {
            for (int i = 0; i < _stockpile.Capacity; i++)
            {
                Require(_stockpile.TryReserveIncoming(out WoodStockpileReservation reservation)
                        && reservation.IsValid,
                    $"Stockpile rejected setup reservation {i + 1} before capacity.");
                Require(_stockpile.TryDepositReserved(reservation),
                    $"Stockpile rejected setup deposit {i + 1} before capacity.");
            }

            Require(_stockpile.StoredWood == 30
                    && _stockpile.IncomingReservations == 0
                    && _stockpile.IsFull,
                "Stockpile did not settle exactly at 30 / 30 for worker isolation.");
            AdvanceTo(Stage.CompleteProductionAndWorker);
        }

        private static void TickCompleteProductionAndWorker()
        {
            Require(_wallet.Deposit(115) == 115 && _wallet.Balance == 120,
                "Could not seed the exact production-upgrade cost.");
            ProcessExactPayment(_productionPad, 120);
            Require(_productionPad.IsCompleted
                    && _productionCompletionCount == 1
                    && _productionAppliedCount == 1
                    && _productionUpgrade.IsApplied,
                "Production purchase/upgrade did not complete exactly once.");
            Require(_workerUnlock.IsPadUnlocked
                    && _workerPad.IsAvailable
                    && !_workerUnlock.IsWorkerActivated,
                "Worker pad did not unlock after production or worker activated early.");
            Require(!_processorUnlock.IsPadUnlocked
                    && !_processorUnlock.ProcessorPurchasePadRoot.activeSelf
                    && !_processorPad.IsAvailable,
                "Processor pad unlocked before the worker purchase completed.");

            Require(_wallet.Deposit(240) == 240,
                "Could not seed the exact worker cost.");
            ProcessExactPayment(_workerPad, 240);
            Require(_workerPad.IsCompleted
                    && _workerCompletionCount == 1
                    && _workerPadUnlockCount == 1
                    && _workerActivationCount == 1
                    && _workerUnlock.IsWorkerActivated
                    && _worker.isActiveAndEnabled,
                "Worker purchase/activation did not occur exactly once.");
            Require(_processorUnlock.IsPadUnlocked
                    && _processorPadUnlockCount == 1
                    && _processorUnlock.ProcessorPurchasePadRoot.activeSelf
                    && _processorPad.IsAvailable
                    && !_processorUnlock.IsProcessorActivated
                    && !_processorUnlock.ProcessorRoot.activeSelf,
                "Processor pad was not revealed exactly once after worker activation.");
            Require(!_workerUnlock.TryActivateWorker()
                    && !_processorUnlock.TryUnlockPad(),
                "An accepted unlock gate repeated an already-completed transition.");

            AdvanceTo(Stage.PartiallyFundProcessor);
        }

        private static void TickPartiallyFundProcessor()
        {
            Require(_wallet.Deposit(65) == 65,
                "Could not seed the processor partial payment.");
            ProcessExactPayment(_processorPad, 65);
            Require(_processorPad.RemainingCost == 295
                    && _wallet.Balance == 0
                    && !_processorPad.IsCompleted
                    && !_processorUnlock.IsProcessorActivated,
                "Processor partial payment did not settle at $295 remaining.");

            _processorPad.enabled = false;
            _processorPad.enabled = true;
            AdvanceTo(Stage.VerifyPartialProcessorProgress);
        }

        private static void TickVerifyPartialProcessorProgress()
        {
            if (!HasWaited(0.1d))
            {
                return;
            }

            Require(_processorPad.RemainingCost == 295
                    && _processorPad.IsAvailable
                    && !_processorPad.IsCompleted
                    && _processorCompletionCount == 0
                    && _processorActivationCount == 0,
                "Processor pad lost partial progress across disable/enable.");
            AdvanceTo(Stage.CompleteProcessorPurchase);
        }

        private static void TickCompleteProcessorPurchase()
        {
            Require(_wallet.Deposit(295) == 295,
                "Could not seed the exact processor remainder.");
            ProcessExactPayment(_processorPad, 295);
            Require(_processorPad.IsCompleted
                    && _processorPad.RemainingCost == 0
                    && _wallet.Balance == 0
                    && _processorCompletionCount == 1
                    && _processorActivationCount == 1
                    && _processorUnlock.IsProcessorActivated
                    && _processorUnlock.ProcessorRoot.activeSelf
                    && _processor.isActiveAndEnabled,
                "Processor purchase/activation did not complete exactly once.");
            Require(_processorPad.ProcessPaymentStep() == 0
                    && !_processorUnlock.TryActivateProcessor()
                    && _processorCompletionCount == 1
                    && _processorActivationCount == 1,
                "Completed processor purchase accepted duplicate payment or activation.");
            Require(_processorUnlockFeedback.PresentationCount == 1,
                "Processor unlock presentation did not react exactly once.");
            Require(_processorFeedback.OutputVisualPoolCount == 6,
                "Processor output presentation did not prewarm six capped visuals.");

            AdvanceTo(Stage.VerifyCarryTypeIsolation);
        }

        private static void TickVerifyCarryTypeIsolation()
        {
            Require(_carryStack.TotalAmount == 0
                    && !_carryStack.HasActiveResource
                    && !_carryStack.ActiveResourceType.HasValue,
                "CarryStack was not empty before mixed-resource validation.");

            Require(_carryStack.TryReserveCapacity(ResourceType.Wood, 1)
                    && _carryStack.ReservedResourceType == ResourceType.Wood,
                "CarryStack could not reserve capacity for Wood.");
            Require(!_carryStack.TryReserveCapacity(ResourceType.Plank, 1)
                    && !_carryStack.TryAdd(ResourceType.Plank, 1),
                "An in-flight Wood reservation allowed Plank into an empty CarryStack.");
            Require(_carryStack.TryCommitReservedAdd(ResourceType.Wood, 1)
                    && _carryStack.GetAmount(ResourceType.Wood) == 1
                    && _carryStack.ActiveResourceType == ResourceType.Wood,
                "CarryStack could not commit its matching Wood reservation.");
            Require(!_carryStack.TryAdd(ResourceType.Plank, 1),
                "Non-empty Wood CarryStack accepted Plank.");
            Require(_carryStack.TryRemove(ResourceType.Wood, 1)
                    && !_carryStack.HasActiveResource,
                "Removing the last Wood did not clear the CarryStack active type.");

            Require(_carryStack.TryReserveCapacity(ResourceType.Plank, 1)
                    && !_carryStack.TryCommitReservedAdd(ResourceType.Wood, 1)
                    && _carryStack.ReservedCapacity == 1
                    && _carryStack.TryCommitReservedAdd(ResourceType.Plank, 1),
                "CarryStack accepted the wrong reservation type or rejected matching Plank.");
            Require(_carryStack.ActiveResourceType == ResourceType.Plank
                    && !_carryStack.TryAdd(ResourceType.Wood, 1),
                "Non-empty Plank CarryStack accepted Wood.");
            Require(_carryStack.TryRemove(ResourceType.Plank, 1)
                    && _carryStack.TotalAmount == 0
                    && !_carryStack.ActiveResourceType.HasValue,
                "Removing the last Plank did not restore an untyped empty CarryStack.");

            AdvanceTo(Stage.EnterProcessorInput);
        }

        private static void TickEnterProcessorInput()
        {
            Require(_processor.InputWood == 0
                    && _processor.OutputPlanks == 0
                    && _processor.CompletedRecipeCount == 0,
                "Processor did not begin empty before the actual input-zone test.");
            Require(_carryStack.TryAdd(ResourceType.Wood, 3),
                "Could not seed three carried Wood for processor input.");
            MovePlayerTo(_inputZone.transform.position);
            AdvanceTo(Stage.WaitForInputTransfer);
        }

        private static void TickWaitForInputTransfer()
        {
            EnsureStageTimeout(2d);
            if (_carryStack.GetAmount(ResourceType.Wood) > 0 || _processor.InputWood < 3)
            {
                return;
            }

            Require(_inputZone.IsPlayerInside
                    && _carryStack.TotalAmount == 0
                    && _processor.InputWood == 3,
                "Actual processor input trigger did not transfer exactly three Wood.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.WaitForOddRecipe);
        }

        private static void TickWaitForOddRecipe()
        {
            EnsureStageTimeout(2d);
            if (_processor.CompletedRecipeCount < 1)
            {
                return;
            }

            Require(_processor.CompletedRecipeCount == 1
                    && _recipeCompletionEventCount == 1
                    && _processor.InputWood == 1
                    && _processor.OutputPlanks == 1
                    && _processor.ReservedOutputCapacity == 0
                    && !_processor.IsProcessing,
                "Odd input did not produce exactly one Plank and retain one Wood.");
            AdvanceTo(Stage.EnterBlockedOutput);
        }

        private static void TickEnterBlockedOutput()
        {
            Require(_carryStack.TryAdd(ResourceType.Wood, 1),
                "Could not seed carried Wood for output type rejection.");
            MovePlayerTo(_outputZone.transform.position);
            AdvanceTo(Stage.VerifyBlockedOutput);
        }

        private static void TickVerifyBlockedOutput()
        {
            if (!HasWaited(0.35d))
            {
                return;
            }

            Require(_outputZone.IsPlayerInside
                    && _processor.OutputPlanks == 1
                    && _carryStack.GetAmount(ResourceType.Wood) == 1
                    && _carryStack.GetAmount(ResourceType.Plank) == 0,
                "Carrying Wood did not block actual processor output collection.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.LeaveBlockedOutput);
        }

        private static void TickLeaveBlockedOutput()
        {
            if (!HasWaited(0.15d))
            {
                return;
            }

            Require(!_outputZone.IsPlayerInside,
                "Processor output trigger did not register the player leaving.");
            Require(_carryStack.TryRemove(ResourceType.Wood, 1)
                    && _carryStack.TotalAmount == 0,
                "Could not clear Wood before collecting processor output.");
            MovePlayerTo(_outputZone.transform.position);
            AdvanceTo(Stage.WaitForPlankCollection);
        }

        private static void TickWaitForPlankCollection()
        {
            EnsureStageTimeout(1.5d);
            if (_carryStack.GetAmount(ResourceType.Plank) < 1)
            {
                return;
            }

            _outputZone.enabled = false;
            MovePlayerTo(NeutralPosition);
            Require(_carryStack.GetAmount(ResourceType.Plank) == 1
                    && _carryStack.GetAmount(ResourceType.Wood) == 0
                    && _carryStack.ActiveResourceType == ResourceType.Plank
                    && _processor.OutputPlanks == 0,
                "Empty CarryStack did not collect exactly one Plank from the actual output trigger.");
            AdvanceTo(Stage.SellPlank);
        }

        private static void TickSellPlank()
        {
            if (!HasWaited(0.15d))
            {
                return;
            }

            _outputZone.enabled = true;
            Require(_cashPile.StoredCash == 0,
                "Cash Pile changed before the first M4 sale.");
            MovePlayerTo(_salePoint.transform.position);
            AdvanceTo(Stage.WaitForPlankSale);
        }

        private static void TickWaitForPlankSale()
        {
            EnsureStageTimeout(1.5d);
            if (_carryStack.TotalAmount > 0)
            {
                return;
            }

            Require(_cashPile.StoredCash == 15
                    && _plankSaleEventCount == 1
                    && _plankSaleCash == 15
                    && _woodSaleEventCount == 0,
                "One Plank did not sell for exactly $15 through the shared Sale Point.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.PrepareDirectWoodSale);
        }

        private static void TickPrepareDirectWoodSale()
        {
            if (!HasWaited(0.15d))
            {
                return;
            }

            Require(!_salePoint.IsPlayerInside,
                "Sale Point did not register exit between Plank and Wood sales.");
            Require(_carryStack.TryAdd(ResourceType.Wood, 12),
                "Could not seed 12 Wood for the direct-sale regression.");
            MovePlayerTo(_salePoint.transform.position);
            AdvanceTo(Stage.WaitForDirectWoodSale);
        }

        private static void TickWaitForDirectWoodSale()
        {
            EnsureStageTimeout(3.5d);
            if (_carryStack.TotalAmount > 0)
            {
                return;
            }

            Require(_cashPile.StoredCash == 75
                    && _woodSaleEventCount == 12
                    && _woodSaleCash == 60
                    && _plankSaleEventCount == 1,
                "Direct sale did not convert 12 Wood into exactly $60.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.FillProcessorInput);
        }

        private static void TickFillProcessorInput()
        {
            if (!HasWaited(0.2d))
            {
                return;
            }

            Require(!_salePoint.IsPlayerInside
                    && _processor.InputWood == 1
                    && _processor.OutputPlanks == 0,
                "Processor/carry state was not ready for the full-output test.");

            for (int i = 0; i < 23; i++)
            {
                Require(_carryStack.TryAdd(ResourceType.Wood, 1),
                    $"Could not stage Wood {i + 1} while filling processor input.");
                Require(_processor.TryTransferInputFrom(_carryStack),
                    $"Processor rejected in-capacity Wood {i + 1}.");
            }

            Require(_processor.InputWood == 24
                    && _carryStack.TotalAmount == 0
                    && !_processor.TryTransferInputFrom(_carryStack),
                "Processor input did not settle exactly at capacity 24.");
            _fullOutputRecipeStart = _processor.CompletedRecipeCount;
            _fullOutputCompletionTarget = _fullOutputRecipeStart + 12;
            AdvanceTo(Stage.WaitForFullOutput);
        }

        private static void TickWaitForFullOutput()
        {
            EnsureStageTimeout(17.5d);
            if (_processor.CompletedRecipeCount < _fullOutputCompletionTarget)
            {
                return;
            }

            Require(_processor.CompletedRecipeCount == _fullOutputCompletionTarget
                    && _recipeCompletionEventCount == _processor.CompletedRecipeCount
                    && _processor.InputWood == 0
                    && _processor.OutputPlanks == 12
                    && _processor.ReservedOutputCapacity == 0
                    && !_processor.IsProcessing,
                "Twelve bounded recipes did not fill output exactly to 12.");

            for (int i = 0; i < 2; i++)
            {
                Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                        && _processor.TryTransferInputFrom(_carryStack),
                    "Could not add the two-Wood full-output pause input.");
            }

            Require(_processor.InputWood == 2
                    && _processor.OutputPlanks == 12
                    && !_processor.IsProcessing
                    && _processor.ReservedOutputCapacity == 0,
                "Full output did not prevent a new recipe reservation.");
            AdvanceTo(Stage.VerifyFullOutputPause);
        }

        private static void TickVerifyFullOutputPause()
        {
            if (!HasWaited(1.4d))
            {
                return;
            }

            Require(_processor.CompletedRecipeCount == _fullOutputCompletionTarget
                    && _processor.InputWood == 2
                    && _processor.OutputPlanks == 12
                    && !_processor.IsProcessing,
                "Processor did not remain paused while output was full.");
            Require(_carryStack.TryAdd(ResourceType.Plank, 11),
                "Could not prefill 11 Planks for a capacity-bounded actual output collection.");
            _outputZone.enabled = true;
            MovePlayerTo(_outputZone.transform.position);
            AdvanceTo(Stage.WaitForSingleOutputCollection);
        }

        private static void TickWaitForSingleOutputCollection()
        {
            EnsureStageTimeout(1.5d);
            if (_carryStack.GetAmount(ResourceType.Plank) < 12)
            {
                return;
            }

            _outputZone.enabled = false;
            MovePlayerTo(NeutralPosition);
            _resumeCompletionTarget = _processor.CompletedRecipeCount + 1;
            Require(_carryStack.GetAmount(ResourceType.Plank) == 12
                    && _carryStack.TotalAmount == _carryStack.Capacity
                    && _processor.OutputPlanks == 11
                    && _processor.InputWood == 2
                    && _processor.IsProcessing
                    && _processor.ReservedOutputCapacity == 1,
                "Freeing one output slot did not collect exactly one Plank and resume processing.");
            AdvanceTo(Stage.WaitForProcessingResume);
        }

        private static void TickWaitForProcessingResume()
        {
            EnsureStageTimeout(2d);
            if (_processor.CompletedRecipeCount < _resumeCompletionTarget)
            {
                return;
            }

            Require(_processor.CompletedRecipeCount == _resumeCompletionTarget
                    && _processor.InputWood == 0
                    && _processor.OutputPlanks == 12
                    && !_processor.IsProcessing
                    && _processor.ReservedOutputCapacity == 0,
                "Freeing output space did not resume one exact recipe.");
            Require(_carryStack.TryRemove(ResourceType.Plank, 12),
                "Could not clear the capacity-test Planks.");
            _outputZone.enabled = true;
            AdvanceTo(Stage.DrainFullOutput);
        }

        private static void TickDrainFullOutput()
        {
            for (int i = 0; i < 12; i++)
            {
                Require(_processor.TryTransferOutputTo(_carryStack),
                    $"Processor rejected output Plank {i + 1} before CarryStack capacity.");
            }

            Require(_processor.OutputPlanks == 0
                    && _carryStack.GetAmount(ResourceType.Plank) == 12
                    && !_processor.TryTransferOutputTo(_carryStack),
                "Processor output drain lost resources or exceeded CarryStack capacity.");
            Require(_carryStack.TryRemove(ResourceType.Plank, 12),
                "Could not clear the drained full processor output.");
            AdvanceTo(Stage.StartRepeatedCycles);
        }

        private static void TickStartRepeatedCycles()
        {
            Require(_processor.InputWood == 0 && _processor.OutputPlanks == 0,
                "Processor was not empty before repeated-cycle validation.");
            for (int i = 0; i < 10; i++)
            {
                Require(_carryStack.TryAdd(ResourceType.Wood, 1)
                        && _processor.TryTransferInputFrom(_carryStack),
                    $"Repeated-cycle input transfer {i + 1} failed.");
            }

            _repeatCompletionTarget = _processor.CompletedRecipeCount + 5;
            AdvanceTo(Stage.WaitForRepeatedCycles);
        }

        private static void TickWaitForRepeatedCycles()
        {
            EnsureStageTimeout(7.5d);
            if (_processor.CompletedRecipeCount < _repeatCompletionTarget)
            {
                return;
            }

            Require(_processor.CompletedRecipeCount == _repeatCompletionTarget
                    && _processor.InputWood == 0
                    && _processor.OutputPlanks == 5
                    && !_processor.IsProcessing,
                "Five repeated processor cycles deadlocked or produced the wrong result.");
            for (int i = 0; i < 5; i++)
            {
                Require(_processor.TryTransferOutputTo(_carryStack),
                    $"Repeated-cycle Plank collection {i + 1} failed.");
            }

            Require(_carryStack.GetAmount(ResourceType.Plank) == 5
                    && _processor.OutputPlanks == 0
                    && _carryStack.TryRemove(ResourceType.Plank, 5),
                "Repeated-cycle output could not be collected without loss.");
            AdvanceTo(Stage.PrepareWorkerCycles);
        }

        private static void TickPrepareWorkerCycles()
        {
            _workerDepositStart = _worker.CompletedDepositCount;
            _processorRecipesBeforeWorkerCycles = _processor.CompletedRecipeCount;
            _processorInputBeforeWorkerCycles = _processor.InputWood;
            _processorOutputBeforeWorkerCycles = _processor.OutputPlanks;
            _cashBeforeWorkerCycles = _cashPile.StoredCash;
            _walletBeforeWorkerCycles = _wallet.Balance;
            _saleEventsBeforeWorkerCycles = _woodSaleEventCount + _plankSaleEventCount;

            for (int i = 0; i < 5; i++)
            {
                Require(_stockpile.TryTransferOneTo(_carryStack),
                    $"Could not free stockpile slot {i + 1} for worker-cycle validation.");
            }

            Require(_stockpile.StoredWood == 25
                    && _carryStack.GetAmount(ResourceType.Wood) == 5
                    && _carryStack.TryRemove(ResourceType.Wood, 5),
                "Freeing five stockpile slots did not conserve Wood.");
            AdvanceTo(Stage.WaitForWorkerCycles);
        }

        private static void TickWaitForWorkerCycles()
        {
            EnsureStageTimeout(45d);
            int completedWorkerCycles = _worker.CompletedDepositCount - _workerDepositStart;
            if (completedWorkerCycles < 3)
            {
                return;
            }

            Require(_workerDepositEventCount >= completedWorkerCycles,
                "Worker deposit events did not track completed Wood-to-stockpile cycles.");
            Require(_stockpile.StoredWood >= 28
                    && _stockpile.StoredWood + _stockpile.IncomingReservations
                       <= _stockpile.Capacity,
                "Worker cycles did not refill the stockpile without exceeding capacity.");
            Require(_processor.CompletedRecipeCount == _processorRecipesBeforeWorkerCycles
                    && _processor.InputWood == _processorInputBeforeWorkerCycles
                    && _processor.OutputPlanks == _processorOutputBeforeWorkerCycles
                    && _processor.ReservedOutputCapacity == 0,
                "Worker touched processor input, output, or recipe state.");
            Require(_cashPile.StoredCash == _cashBeforeWorkerCycles
                    && _wallet.Balance == _walletBeforeWorkerCycles
                    && _woodSaleEventCount + _plankSaleEventCount
                       == _saleEventsBeforeWorkerCycles,
                "Worker sold resources or changed cash/wallet state.");
            Require(_productionCompletionCount == 1
                    && _productionAppliedCount == 1
                    && _workerCompletionCount == 1
                    && _workerActivationCount == 1
                    && _processorCompletionCount == 1
                    && _processorActivationCount == 1,
                "A purchase or unlock completed more than once during M4 processing.");
            Require(_recipeCompletionEventCount == _processor.CompletedRecipeCount
                    && _processorFeedback.CompletionFeedbackCount
                       == _processor.CompletedRecipeCount,
                "Processor logic and completion presentation counts diverged.");

            Pass(
                $"M4 processor Play Mode smoke passed: type isolation, $15 Plank / $5 Wood sales, full-output pause/resume, {_processor.CompletedRecipeCount} recipes, and {completedWorkerCycles} isolated worker cycles.");
        }

        private static void ProcessExactPayment(PurchasePad pad, int expectedAmount)
        {
            int totalPaid = 0;
            int guard = 0;
            while (totalPaid < expectedAmount && guard++ < 100)
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
                        $"M4 smoke found more than one ${totalCost} Purchase Pad.");
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
                    $"M4 smoke expected exactly one {typeof(T).Name}, found {matches.Length}.");
            }

            return matches[0];
        }

        private static void MovePlayerTo(Vector3 destination)
        {
            Vector3 planarDestination = new Vector3(destination.x, 0f, destination.z);
            _playerController.Move(planarDestination - _playerController.transform.position);
            Physics.SyncTransforms();
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
            Require(_workerUnlock.IsPadUnlocked
                    && _workerUnlock.WorkerPurchasePadRoot.activeSelf
                    && _workerPad.IsAvailable,
                "Worker pad unlock event preceded authoritative state.");
        }

        private static void HandleWorkerActivated()
        {
            _workerActivationCount++;
            Require(_workerUnlock.IsWorkerActivated
                    && _workerUnlock.WorkerRoot.activeSelf,
                "Worker activation event preceded authoritative state.");
        }

        private static void HandleProcessorCompleted()
        {
            _processorCompletionCount++;
        }

        private static void HandleProcessorPadUnlocked()
        {
            _processorPadUnlockCount++;
            Require(_processorUnlock.IsPadUnlocked
                    && _processorUnlock.ProcessorPurchasePadRoot.activeSelf
                    && _processorPad.IsAvailable,
                "Processor pad unlock event preceded authoritative state.");
        }

        private static void HandleProcessorActivated()
        {
            _processorActivationCount++;
            Require(_processorUnlock.IsProcessorActivated
                    && _processorUnlock.ProcessorRoot.activeSelf
                    && _processor.isActiveAndEnabled,
                "Processor activation event preceded authoritative state.");
        }

        private static void HandleRecipeCompleted(int inputWood, int outputPlanks)
        {
            _recipeCompletionEventCount++;
            Require(inputWood == _processor.InputWood
                    && outputPlanks == _processor.OutputPlanks
                    && _processor.CompletedRecipeCount == _recipeCompletionEventCount,
                "Processor recipe event preceded authoritative buffer state.");
        }

        private static void HandleWorkerDeposited()
        {
            _workerDepositEventCount++;
        }

        private static void HandleUnitSold(SaleFeedbackData feedback)
        {
            Require(feedback.RemainingAmount
                    == _carryStack.GetAmount(feedback.ResourceType),
                "Sale feedback preceded authoritative CarryStack removal.");
            switch (feedback.ResourceType)
            {
                case ResourceType.Wood:
                    Require(feedback.CashValue == 5,
                        "Sale Point reported a Wood value other than $5.");
                    _woodSaleEventCount++;
                    _woodSaleCash += feedback.CashValue;
                    break;
                case ResourceType.Plank:
                    Require(feedback.CashValue == 15,
                        "Sale Point reported a Plank value other than $15.");
                    _plankSaleEventCount++;
                    _plankSaleCash += feedback.CashValue;
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
                throw new InvalidOperationException($"M4 smoke timed out in stage {_stage}.");
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
            string result = $"M4 processor Play Mode smoke failed: {message}";
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
