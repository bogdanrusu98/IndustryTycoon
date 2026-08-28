using System;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
using IndustryTycoon.Player;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.Workers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    [InitializeOnLoad]
    public static class LumberCampM3PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey = "IndustryTycoon.M3.Smoke.Running";
        private const string CommandLineKey = "IndustryTycoon.M3.Smoke.CommandLine";
        private const string FinishPendingKey = "IndustryTycoon.M3.Smoke.FinishPending";
        private const string SuccessKey = "IndustryTycoon.M3.Smoke.Success";
        private const string ResultMessageKey = "IndustryTycoon.M3.Smoke.ResultMessage";

        private static readonly Vector3 NeutralPosition = new Vector3(0f, 0f, -9f);

        private enum Stage
        {
            Warmup,
            FillStockpile,
            CompleteProductionUpgrade,
            PartiallyFundWorker,
            VerifyPartialWorkerProgress,
            CompleteWorkerPurchase,
            VerifyFullStockpileIdle,
            WaitForSingleSlotWithdrawal,
            WaitForWorkerResumeDeposit,
            LeaveAfterSingleSlotResume,
            WaitForStockpileWithdrawal,
            VerifyWithdrawalStoppedAtCapacity,
            LeaveStockpile,
            WaitForReservationInvalidationClaim,
            VerifyReservationInvalidationRecovery,
            WaitForRetryClaim,
            WaitForPlayerAttraction,
            WaitForPlayerPickupAndRecovery,
            WaitForAutonomousCycles
        }

        private static CharacterController _playerController;
        private static ResourceCollector _resourceCollector;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static WoodSpawner _woodSpawner;
        private static SalePoint _salePoint;
        private static CashPile _cashPile;
        private static PurchasePad _productionPad;
        private static PurchasePad _workerPad;
        private static WoodProductionUpgrade _productionUpgrade;
        private static WoodStockpile _stockpile;
        private static WoodStockpileCollector _stockpileCollector;
        private static LumberWorker _worker;
        private static FirstWorkerUnlock _workerUnlock;
        private static WoodStockpileFeedback _stockpileFeedback;
        private static LumberWorkerFeedback _workerFeedback;
        private static WorkerUnlockFeedback _workerUnlockFeedback;
        private static SmoothFollowCamera _followCamera;

        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;
        private static int _productionCompletionCount;
        private static int _productionAppliedCount;
        private static int _workerCompletionCount;
        private static int _padUnlockCount;
        private static int _workerActivationCount;
        private static int _claimLossEventCount;
        private static int _saleEventCount;
        private static int _workerDepositEventCount;
        private static int _recoveryCountBeforePreemption;
        private static int _pickupCountBeforeFullResume;
        private static int _depositCountBeforeFullResume;
        private static int _recoveryCountBeforeReservationInvalidation;
        private static int _pickupCountBeforeReservationInvalidation;
        private static int _depositCountBeforeReservationInvalidation;
        private static int _depositCountBeforeCycles;
        private static int _stockpileAmountBeforeCycles;
        private static int _playerCollectedWood;
        private static bool _reservationRecoveryReleasedTargetObserved;
        private static ResourcePickup _reservationInvalidatedTarget;
        private static ResourcePickup _playerPreemptedTarget;

        static LumberCampM3PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/Prototype/Run M3 Worker Smoke Test")]
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
                throw new InvalidOperationException("Exit Play Mode before starting the M3 worker smoke test.");
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
                        "M3 worker smoke test exceeded its 90-second timeout.");
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
            _woodSpawner = Object.FindAnyObjectByType<WoodSpawner>();
            _salePoint = Object.FindAnyObjectByType<SalePoint>();
            _cashPile = Object.FindAnyObjectByType<CashPile>();
            _productionPad = FindPurchasePad(120);
            _workerPad = FindPurchasePad(240);
            _productionUpgrade = Object.FindAnyObjectByType<WoodProductionUpgrade>();
            _stockpile = Object.FindAnyObjectByType<WoodStockpile>();
            _stockpileCollector = Object.FindAnyObjectByType<WoodStockpileCollector>();
            _worker = FindIncludingInactive<LumberWorker>();
            _workerUnlock = Object.FindAnyObjectByType<FirstWorkerUnlock>();
            _stockpileFeedback = Object.FindAnyObjectByType<WoodStockpileFeedback>();
            _workerFeedback = FindIncludingInactive<LumberWorkerFeedback>();
            _workerUnlockFeedback = Object.FindAnyObjectByType<WorkerUnlockFeedback>();
            _followCamera = Object.FindAnyObjectByType<SmoothFollowCamera>();

            Require(_playerController != null, "M3 smoke could not find the Player CharacterController.");
            Require(_resourceCollector != null, "M3 smoke could not find the ResourceCollector.");
            Require(_carryStack != null && _carryStack.Capacity == 12,
                "M3 smoke requires the existing 12-capacity CarryStack.");
            Require(_wallet != null, "M3 smoke could not find the Wallet.");
            Require(_woodSpawner != null, "M3 smoke could not find the WoodSpawner.");
            Require(_salePoint != null && _cashPile != null,
                "M3 smoke could not find the existing sale/cash loop.");
            Require(_productionPad != null && _productionPad.TotalCost == 120,
                "M3 smoke could not find the $120 production Purchase Pad.");
            Require(_workerPad != null && _workerPad.TotalCost == 240,
                "M3 smoke could not find the $240 worker Purchase Pad.");
            Require(_productionUpgrade != null, "M3 smoke could not find the production upgrade.");
            Require(_stockpile != null && _stockpile.Capacity == 30,
                "M3 smoke could not find the 30-capacity Wood Stockpile.");
            Require(_stockpileCollector != null,
                "M3 smoke could not find the Wood Stockpile collection trigger.");
            Require(_worker != null, "M3 smoke could not find the inactive Lumber Worker.");
            Require(_workerUnlock != null, "M3 smoke could not find the first-worker unlock gate.");
            Require(_stockpileFeedback != null
                    && _workerFeedback != null
                    && _workerUnlockFeedback != null,
                "M3 smoke could not find all M3 presentation components.");
            Require(_followCamera != null, "M3 smoke could not find the existing follow camera.");
            Require(_workerUnlock.ProductionUpgrade == _productionUpgrade
                    && _workerUnlock.WorkerPurchasePad == _workerPad
                    && _workerUnlock.WorkerRoot != null,
                "First-worker unlock references are not wired to the expected M3 objects.");
            Require(_worker.WoodSpawner == _woodSpawner && _worker.Stockpile == _stockpile,
                "Lumber Worker references are not wired to the scene spawner and stockpile.");
            Require(_stockpileCollector.Stockpile == _stockpile
                    && _stockpileCollector.CarryStack == _carryStack,
                "Wood Stockpile collector references are incomplete.");

            _resourceCollector.enabled = false;
            MovePlayerTo(NeutralPosition);

            _productionCompletionCount = 0;
            _productionAppliedCount = 0;
            _workerCompletionCount = 0;
            _padUnlockCount = 0;
            _workerActivationCount = 0;
            _claimLossEventCount = 0;
            _saleEventCount = 0;
            _workerDepositEventCount = 0;
            _recoveryCountBeforePreemption = 0;
            _pickupCountBeforeFullResume = 0;
            _depositCountBeforeFullResume = 0;
            _recoveryCountBeforeReservationInvalidation = 0;
            _pickupCountBeforeReservationInvalidation = 0;
            _depositCountBeforeReservationInvalidation = 0;
            _depositCountBeforeCycles = 0;
            _stockpileAmountBeforeCycles = 0;
            _playerCollectedWood = 0;
            _reservationRecoveryReleasedTargetObserved = false;
            _reservationInvalidatedTarget = null;
            _playerPreemptedTarget = null;

            _productionPad.Completed += HandleProductionCompleted;
            _productionUpgrade.Applied += HandleProductionApplied;
            _workerPad.Completed += HandleWorkerCompleted;
            _workerUnlock.PadUnlocked += HandlePadUnlocked;
            _workerUnlock.WorkerActivated += HandleWorkerActivated;
            _worker.TargetClaimLost += HandleTargetClaimLost;
            _worker.WoodDeposited += HandleWorkerDeposited;
            _salePoint.UnitSold += HandleUnitSold;

            _runStartedAt = Now;
            AdvanceTo(Stage.Warmup);
            _runtimeInitialized = true;
        }

        private static void ValidateContinuousInvariants()
        {
            Require(_wallet.Balance >= 0, "Wallet became negative during the M3 smoke test.");
            Require(_carryStack.TotalAmount >= 0
                    && _carryStack.ReservedCapacity >= 0
                    && _carryStack.TotalAmount + _carryStack.ReservedCapacity <= _carryStack.Capacity,
                "CarryStack amount plus reservations exceeded its capacity.");
            Require(_stockpile.StoredWood >= 0
                    && _stockpile.IncomingReservations >= 0
                    && _stockpile.StoredWood + _stockpile.IncomingReservations <= _stockpile.Capacity,
                "Stockpile stored wood plus incoming reservations exceeded capacity.");
            Require(_productionPad.RemainingCost >= 0 && _workerPad.RemainingCost >= 0,
                "A Purchase Pad remaining cost became negative.");
            Require(_worker.CompletedDepositCount <= _worker.CompletedPickupCount
                    && _worker.CompletedPickupCount - _worker.CompletedDepositCount <= 1,
                "The one-item worker cargo invariant was violated.");
            Require(_woodSpawner.ActiveCount == _woodSpawner.ActiveRegistryCount,
                "WoodSpawner active count diverged from its allocation-free registry.");
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
                case Stage.CompleteProductionUpgrade:
                    TickCompleteProductionUpgrade();
                    break;
                case Stage.PartiallyFundWorker:
                    TickPartiallyFundWorker();
                    break;
                case Stage.VerifyPartialWorkerProgress:
                    TickVerifyPartialWorkerProgress();
                    break;
                case Stage.CompleteWorkerPurchase:
                    TickCompleteWorkerPurchase();
                    break;
                case Stage.VerifyFullStockpileIdle:
                    TickVerifyFullStockpileIdle();
                    break;
                case Stage.WaitForSingleSlotWithdrawal:
                    TickWaitForSingleSlotWithdrawal();
                    break;
                case Stage.WaitForWorkerResumeDeposit:
                    TickWaitForWorkerResumeDeposit();
                    break;
                case Stage.LeaveAfterSingleSlotResume:
                    TickLeaveAfterSingleSlotResume();
                    break;
                case Stage.WaitForStockpileWithdrawal:
                    TickWaitForStockpileWithdrawal();
                    break;
                case Stage.VerifyWithdrawalStoppedAtCapacity:
                    TickVerifyWithdrawalStoppedAtCapacity();
                    break;
                case Stage.LeaveStockpile:
                    TickLeaveStockpile();
                    break;
                case Stage.WaitForReservationInvalidationClaim:
                    TickWaitForReservationInvalidationClaim();
                    break;
                case Stage.VerifyReservationInvalidationRecovery:
                    TickVerifyReservationInvalidationRecovery();
                    break;
                case Stage.WaitForRetryClaim:
                    TickWaitForRetryClaim();
                    break;
                case Stage.WaitForPlayerAttraction:
                    TickWaitForPlayerAttraction();
                    break;
                case Stage.WaitForPlayerPickupAndRecovery:
                    TickWaitForPlayerPickupAndRecovery();
                    break;
                case Stage.WaitForAutonomousCycles:
                    TickWaitForAutonomousCycles();
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
            Require(_wallet.Balance == 0 && _cashPile.StoredCash == 0,
                "Wallet and Cash Pile did not start empty.");
            Require(_stockpile.StoredWood == 0 && _stockpile.IncomingReservations == 0,
                "Wood Stockpile did not start empty and unreserved.");
            Require(_productionPad.IsAvailable && !_productionPad.IsCompleted,
                "Production Purchase Pad did not start available.");
            Require(!_productionUpgrade.IsApplied,
                "Production upgrade was already applied in a fresh run.");
            Require(!_workerPad.IsAvailable && !_workerPad.IsCompleted,
                "Worker Purchase Pad was usable before the production upgrade.");
            Require(!_workerUnlock.IsPadUnlocked && !_workerUnlock.IsWorkerActivated,
                "Worker progression gate did not start locked.");
            Require(!_workerUnlock.WorkerPurchasePadRoot.activeSelf
                    && !_workerUnlock.WorkerRoot.activeSelf
                    && !_worker.isActiveAndEnabled,
                "Worker pad or worker was active before the production upgrade.");
            Require(_woodSpawner.ActiveRegistryCount > 0,
                "WoodSpawner did not register any loose wood during warmup.");
            Require(_stockpileFeedback.VisualPoolCount == 10,
                "Stockpile presentation pool was not prewarmed to 10 visuals.");

            Require(_wallet.Deposit(5) == 5, "Could not seed wallet for locked-pad verification.");
            Require(_workerPad.ProcessPaymentStep() == 0,
                "Locked worker Purchase Pad accepted a direct payment.");
            Require(_wallet.Balance == 5
                    && _workerPad.RemainingCost == 240
                    && _workerCompletionCount == 0,
                "Locked worker pad changed wallet, progress, or completion state.");

            AdvanceTo(Stage.FillStockpile);
        }

        private static void TickFillStockpile()
        {
            for (int i = 0; i < _stockpile.Capacity; i++)
            {
                Require(_stockpile.TryReserveIncoming(out WoodStockpileReservation reservation),
                    $"Stockpile rejected incoming reservation {i + 1} before capacity.");
                Require(reservation.IsValid
                        && _stockpile.IncomingReservations == 1
                        && _stockpile.StoredWood + _stockpile.IncomingReservations <= _stockpile.Capacity,
                    "Stockpile incoming reservation was invalid or exceeded capacity.");
                Require(_stockpile.TryDepositReserved(reservation),
                    $"Stockpile rejected reserved deposit {i + 1} before capacity.");
                Require(!reservation.IsValid,
                    "Committed stockpile reservation remained valid after deposit.");
            }

            Require(_stockpile.StoredWood == 30
                    && _stockpile.IncomingReservations == 0
                    && _stockpile.IsFull,
                "Stockpile did not settle exactly at 30 / 30.");
            Require(!_stockpile.TryReserveIncoming(out _),
                "Full stockpile accepted an additional incoming reservation.");
            Require(_stockpileFeedback.DepositFeedbackCount == 30,
                "Stockpile presentation did not react once per successful logical deposit.");

            AdvanceTo(Stage.CompleteProductionUpgrade);
        }

        private static void TickCompleteProductionUpgrade()
        {
            Require(_wallet.Deposit(115) == 115 && _wallet.Balance == 120,
                "Could not seed exactly $120 for the production upgrade.");
            ProcessExactPayment(_productionPad, 120);

            Require(_wallet.Balance == 0,
                "Production purchase did not spend exactly the seeded $120.");
            Require(_productionPad.IsCompleted
                    && !_productionPad.IsAvailable
                    && _productionPad.RemainingCost == 0,
                "Production Purchase Pad did not complete cleanly.");
            Require(_productionCompletionCount == 1
                    && _productionAppliedCount == 1
                    && _productionUpgrade.IsApplied,
                "Production purchase or upgrade did not complete exactly once.");
            Require(Mathf.Approximately(_woodSpawner.ProductionRateMultiplier, 2f),
                "Production upgrade did not preserve the expected 2x multiplier.");
            Require(_workerUnlock.IsPadUnlocked
                    && _workerUnlock.WorkerPurchasePadRoot.activeSelf
                    && _workerPad.IsAvailable,
                "Worker Purchase Pad was not revealed after the production upgrade.");
            Require(_padUnlockCount == 1
                    && !_workerUnlock.IsWorkerActivated
                    && !_workerUnlock.WorkerRoot.activeSelf,
                "Worker pad unlock count or pre-purchase worker state was incorrect.");
            Require(_productionPad.ProcessPaymentStep() == 0
                    && _productionCompletionCount == 1
                    && _productionAppliedCount == 1,
                "Completed production Purchase Pad accepted duplicate payment or completion.");

            AdvanceTo(Stage.PartiallyFundWorker);
        }

        private static void TickPartiallyFundWorker()
        {
            Require(_wallet.Deposit(65) == 65, "Could not seed partial worker payment.");
            ProcessExactPayment(_workerPad, 65);
            Require(_workerPad.RemainingCost == 175
                    && _wallet.Balance == 0
                    && !_workerPad.IsCompleted,
                "Worker Purchase Pad partial progress did not settle at $175 remaining.");
            Require(!_workerUnlock.IsWorkerActivated && !_workerUnlock.WorkerRoot.activeSelf,
                "Worker activated from partial payment.");

            _workerPad.enabled = false;
            _workerPad.enabled = true;
            AdvanceTo(Stage.VerifyPartialWorkerProgress);
        }

        private static void TickVerifyPartialWorkerProgress()
        {
            if (!HasWaited(0.15d))
            {
                return;
            }

            Require(_workerPad.RemainingCost == 175
                    && _workerPad.IsAvailable
                    && !_workerPad.IsCompleted,
                "Worker Purchase Pad lost partial progress after disable/enable.");
            Require(_workerCompletionCount == 0
                    && _workerActivationCount == 0
                    && !_worker.isActiveAndEnabled,
                "Worker completed or activated during the partial-progress persistence check.");

            AdvanceTo(Stage.CompleteWorkerPurchase);
        }

        private static void TickCompleteWorkerPurchase()
        {
            Require(_wallet.Deposit(175) == 175, "Could not seed final worker payment.");
            ProcessExactPayment(_workerPad, 175);

            Require(_wallet.Balance == 0,
                "Worker purchase did not spend exactly the seeded $175 remainder.");
            Require(_workerPad.IsCompleted
                    && !_workerPad.IsAvailable
                    && _workerPad.RemainingCost == 0,
                "Worker Purchase Pad did not complete cleanly.");
            Require(_workerCompletionCount == 1
                    && _workerActivationCount == 1
                    && _workerUnlock.IsWorkerActivated,
                "Worker purchase or activation did not occur exactly once.");
            Require(_workerUnlock.WorkerRoot.activeSelf && _worker.isActiveAndEnabled,
                "Worker root was not activated after purchase completion.");
            Require(_workerUnlockFeedback.PresentationCount == 1,
                "Worker unlock presentation did not trigger exactly once.");
            Require(_workerFeedback.VisualPoolCount == 2,
                "Worker presentation did not prewarm its two reusable wood visuals.");
            Require(_workerPad.ProcessPaymentStep() == 0
                    && !_workerUnlock.TryActivateWorker()
                    && !_workerUnlock.TryUnlockPad()
                    && _workerCompletionCount == 1
                    && _workerActivationCount == 1,
                "Completed worker progression accepted a duplicate completion or activation.");

            AdvanceTo(Stage.VerifyFullStockpileIdle);
        }

        private static void TickVerifyFullStockpileIdle()
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.85d))
            {
                return;
            }

            Require(_stockpile.StoredWood == 30
                    && _stockpile.IncomingReservations == 0
                    && _stockpile.IsFull,
                "Full stockpile changed while the worker should have been waiting.");
            Require(_worker.State == LumberWorkerState.Idle
                    && _worker.IsWaitingForStockpile
                    && !_worker.HasValidTarget
                    && !_worker.IsCarrying
                    && _worker.CompletedPickupCount == 0
                    && _worker.CompletedDepositCount == 0,
                "Worker did not idle safely at a full stockpile.");
            Require(!_workerUnlockFeedback.IsPresenting
                    && _workerUnlockFeedback.PresentationCount == 1,
                "Worker unlock presentation did not finish once within its bounded duration.");

            for (int i = 0; i < 11; i++)
            {
                Require(_carryStack.TryAdd(ResourceType.Wood, 1),
                    "Could not prefill CarryStack to 11 for the one-slot resume check.");
            }

            Require(_carryStack.TotalAmount == 11 && _worker.isActiveAndEnabled,
                "One-slot resume setup did not preserve an enabled worker and 11 / 12 CarryStack.");
            _pickupCountBeforeFullResume = _worker.CompletedPickupCount;
            _depositCountBeforeFullResume = _worker.CompletedDepositCount;
            MovePlayerTo(_stockpileCollector.transform.position);
            AdvanceTo(Stage.WaitForSingleSlotWithdrawal);
        }

        private static void TickWaitForSingleSlotWithdrawal()
        {
            EnsureStageTimeout(3d);
            if (_carryStack.TotalAmount < _carryStack.Capacity)
            {
                return;
            }

            Require(_stockpileCollector.IsPlayerInside,
                "Stockpile collector did not register the player for the one-slot withdrawal.");
            Require(_carryStack.GetAmount(ResourceType.Wood) == 12
                    && _carryStack.ReservedCapacity == 0
                    && _stockpile.StoredWood == 29,
                "One free CarryStack slot did not withdraw exactly one stockpile wood.");
            Require(_worker.isActiveAndEnabled,
                "Worker was not enabled when stockpile capacity became available.");

            AdvanceTo(Stage.WaitForWorkerResumeDeposit);
        }

        private static void TickWaitForWorkerResumeDeposit()
        {
            EnsureStageTimeout(12d);
            if (_worker.CompletedDepositCount <= _depositCountBeforeFullResume)
            {
                return;
            }

            Require(_worker.CompletedPickupCount == _pickupCountBeforeFullResume + 1
                    && _worker.CompletedDepositCount == _depositCountBeforeFullResume + 1,
                "Worker did not resume with exactly one pickup/deposit after one slot was freed.");
            Require(_stockpile.StoredWood == 30
                    && _stockpile.IncomingReservations == 0
                    && _stockpile.IsFull,
                "Worker did not return the stockpile exactly to 30 / 30.");
            Require(_carryStack.TotalAmount == 12
                    && !_worker.IsCarrying
                    && !_worker.HasValidTarget,
                "One-slot resume changed player carry or left worker ownership/cargo behind.");

            _worker.enabled = false;
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.LeaveAfterSingleSlotResume);
        }

        private static void TickLeaveAfterSingleSlotResume()
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.3d))
            {
                return;
            }

            Require(!_stockpileCollector.IsPlayerInside,
                "Stockpile collector did not register exit after the one-slot resume check.");
            Require(_carryStack.TryRemove(ResourceType.Wood, 12)
                    && _carryStack.TotalAmount == 0,
                "Could not clear CarryStack outside the stockpile trigger.");

            MovePlayerTo(_stockpileCollector.transform.position);
            AdvanceTo(Stage.WaitForStockpileWithdrawal);
        }

        private static void TickWaitForStockpileWithdrawal()
        {
            EnsureStageTimeout(4d);
            if (_carryStack.TotalAmount < _carryStack.Capacity)
            {
                return;
            }

            Require(_stockpileCollector.IsPlayerInside,
                "Stockpile collector did not register the player in its trigger.");
            Require(_carryStack.GetAmount(ResourceType.Wood) == 12
                    && _carryStack.TotalAmount == 12
                    && _carryStack.ReservedCapacity == 0,
                "Stockpile withdrawal did not stop at the 12-item CarryStack limit.");
            Require(_stockpile.StoredWood == 18 && _stockpile.IncomingReservations == 0,
                "Stockpile did not atomically decrease from 30 to 18 during withdrawal.");

            AdvanceTo(Stage.VerifyWithdrawalStoppedAtCapacity);
        }

        private static void TickVerifyWithdrawalStoppedAtCapacity()
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.35d))
            {
                return;
            }

            Require(_carryStack.TotalAmount == 12
                    && _stockpile.StoredWood == 18
                    && !_stockpile.TryTransferOneTo(_carryStack),
                "Stockpile withdrawal exceeded CarryStack capacity while the player remained inside.");

            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.LeaveStockpile);
        }

        private static void TickLeaveStockpile()
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.3d))
            {
                return;
            }

            Require(!_stockpileCollector.IsPlayerInside,
                "Stockpile collector still considered the player inside after leaving.");
            Require(_carryStack.TryRemove(ResourceType.Wood, 12)
                    && _carryStack.TotalAmount == 0,
                "Could not clear the test CarryStack after validating stockpile withdrawal.");

            _stockpileCollector.enabled = false;
            _worker.enabled = true;
            AdvanceTo(Stage.WaitForReservationInvalidationClaim);
        }

        private static void TickWaitForReservationInvalidationClaim()
        {
            EnsureStageTimeout(7d);
            if (!_worker.HasValidTarget
                || _worker.CurrentTarget == null
                || _worker.State != LumberWorkerState.MoveToWood)
            {
                return;
            }

            _reservationInvalidatedTarget = _worker.CurrentTarget;
            Require(_reservationInvalidatedTarget.IsClaimedBy(_worker)
                    && _stockpile.IncomingReservations == 1,
                "Worker did not hold its target and incoming reservation before invalidation.");
            _recoveryCountBeforeReservationInvalidation = _worker.RecoveryCount;
            _pickupCountBeforeReservationInvalidation = _worker.CompletedPickupCount;
            _depositCountBeforeReservationInvalidation = _worker.CompletedDepositCount;

            _stockpile.enabled = false;
            Require(_stockpile.IncomingReservations == 0,
                "Disabling Wood Stockpile did not invalidate its incoming reservation.");
            _stockpile.enabled = true;

            AdvanceTo(Stage.VerifyReservationInvalidationRecovery);
        }

        private static void TickVerifyReservationInvalidationRecovery()
        {
            EnsureStageTimeout(3d);
            if (_worker.RecoveryCount <= _recoveryCountBeforeReservationInvalidation)
            {
                return;
            }

            Require(_claimLossEventCount == 1,
                "Worker did not publish exactly one recovery event for the invalidated reservation.");
            Require(_reservationInvalidatedTarget != null
                    && _reservationRecoveryReleasedTargetObserved,
                "Worker did not release target, reservation, and cargo state during recovery.");
            Require(_carryStack.TotalAmount == 0 && _carryStack.ReservedCapacity == 0,
                "Reservation invalidation unexpectedly changed player CarryStack state.");

            AdvanceTo(Stage.WaitForRetryClaim);
        }

        private static void TickWaitForRetryClaim()
        {
            EnsureStageTimeout(4d);
            if (!_worker.HasValidTarget
                || _worker.CurrentTarget == null
                || _worker.State != LumberWorkerState.MoveToWood)
            {
                return;
            }

            _playerPreemptedTarget = _worker.CurrentTarget;
            Require(_playerPreemptedTarget.IsClaimedBy(_worker)
                    && _stockpile.IncomingReservations == 1,
                "Worker did not reacquire bounded work after reservation invalidation.");
            Require(!_playerPreemptedTarget.TryClaim(
                    _stockpile,
                    ResourceClaimPriority.Worker,
                    out _),
                "A second worker-priority owner claimed the worker's loose wood.");

            _recoveryCountBeforePreemption = _worker.RecoveryCount;
            _resourceCollector.enabled = true;
            MovePlayerTo(_playerPreemptedTarget.transform.position);
            AdvanceTo(Stage.WaitForPlayerAttraction);
        }

        private static void TickWaitForPlayerAttraction()
        {
            EnsureStageTimeout(2d);
            if (_playerPreemptedTarget == null
                || !_playerPreemptedTarget.IsAttracted
                || _playerPreemptedTarget.ClaimOwner != _resourceCollector)
            {
                return;
            }

            Require(!_worker.HasValidTarget,
                "Actual ResourceCollector attraction did not preempt the worker's soft claim.");
            Require(_resourceCollector.ReservedCapacity > 0
                    && _carryStack.ReservedCapacity > 0,
                "Actual ResourceCollector path did not reserve CarryStack capacity.");

            AdvanceTo(Stage.WaitForPlayerPickupAndRecovery);
        }

        private static void TickWaitForPlayerPickupAndRecovery()
        {
            EnsureStageTimeout(4d);
            if (_worker.RecoveryCount <= _recoveryCountBeforePreemption)
            {
                return;
            }

            Require(!_worker.IsCarrying,
                "Worker gained cargo from the target taken by ResourceCollector.");
            if (_worker.enabled)
            {
                _worker.enabled = false;
            }

            if (_carryStack.GetAmount(ResourceType.Wood) <= 0
                || _playerPreemptedTarget.IsAttracted)
            {
                return;
            }

            Require(_claimLossEventCount == 2,
                "Worker did not recover exactly once from each invalidated target condition.");
            _playerCollectedWood = _carryStack.GetAmount(ResourceType.Wood);
            Require(_playerCollectedWood > 0 && _playerCollectedWood <= _carryStack.Capacity,
                "Actual ResourceCollector pickup did not settle within CarryStack capacity.");

            _resourceCollector.enabled = false;
            MovePlayerTo(NeutralPosition);
            Require(_resourceCollector.ReservedCapacity == 0
                    && _carryStack.ReservedCapacity == 0,
                "Disabling ResourceCollector after preemption leaked capacity reservations.");
            Require(_carryStack.TryRemove(ResourceType.Wood, _playerCollectedWood)
                    && _carryStack.TotalAmount == 0,
                "Could not clear wood collected through the actual player pickup path.");

            _worker.enabled = true;
            _depositCountBeforeCycles = _worker.CompletedDepositCount;
            _stockpileAmountBeforeCycles = _stockpile.StoredWood;
            AdvanceTo(Stage.WaitForAutonomousCycles);
        }

        private static void TickWaitForAutonomousCycles()
        {
            EnsureStageTimeout(55d);
            int completedCycles = _worker.CompletedDepositCount - _depositCountBeforeCycles;
            if (completedCycles < 5)
            {
                return;
            }

            Require(_worker.CompletedPickupCount >= _worker.CompletedDepositCount,
                "Worker deposited more wood than it picked up.");
            Require(_stockpile.StoredWood
                    == _stockpileAmountBeforeCycles + completedCycles,
                "Autonomous deposits did not increase stockpile by exactly one wood per cycle.");
            Require(_workerDepositEventCount == _worker.CompletedDepositCount,
                "Worker logical deposit event count diverged from completed deposits.");
            Require(_workerFeedback.DepositPresentationCount == _worker.CompletedDepositCount,
                "Worker deposit presentation did not react once per successful logical deposit.");
            Require(_stockpileFeedback.DepositFeedbackCount
                    == 30 + _worker.CompletedDepositCount,
                "Stockpile feedback count diverged from successful deposits.");
            Require(_wallet.Balance == 0
                    && _cashPile.StoredCash == 0
                    && _saleEventCount == 0
                    && _carryStack.TotalAmount == 0,
                "Worker mutated wallet, Cash Pile, Sale Point, or player CarryStack.");
            Require(_productionCompletionCount == 1
                    && _productionAppliedCount == 1
                    && _workerCompletionCount == 1
                    && _padUnlockCount == 1
                    && _workerActivationCount == 1
                    && _workerUnlockFeedback.PresentationCount == 1,
                "A purchase, upgrade, unlock, or activation completed more than once.");
            Require(Mathf.Approximately(_woodSpawner.ProductionRateMultiplier, 2f),
                "Production multiplier regressed during autonomous worker operation.");
            Require(_claimLossEventCount == 2
                    && _playerCollectedWood > 0
                    && !_resourceCollector.enabled,
                "Reservation recovery or actual ResourceCollector preemption did not remain isolated.");

            Pass(
                $"M3 worker Play Mode smoke passed: full-stockpile resume, reservation invalidation recovery, actual player preemption, and {completedCycles} autonomous cycles completed without duplication.");
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
                        $"M3 smoke found more than one ${totalCost} Purchase Pad.");
                }

                result = pads[i];
            }

            return result;
        }

        private static T FindIncludingInactive<T>() where T : Object
        {
            T[] matches = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            return matches.Length > 0 ? matches[0] : null;
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

        private static void HandlePadUnlocked()
        {
            _padUnlockCount++;
        }

        private static void HandleWorkerActivated()
        {
            _workerActivationCount++;
        }

        private static void HandleTargetClaimLost()
        {
            _claimLossEventCount++;
            if (_stage == Stage.VerifyReservationInvalidationRecovery
                && _reservationInvalidatedTarget != null)
            {
                _reservationRecoveryReleasedTargetObserved =
                    !_reservationInvalidatedTarget.IsClaimedBy(_worker)
                    && !_worker.HasValidTarget
                    && !_worker.IsCarrying
                    && _stockpile.IncomingReservations == 0
                    && _worker.CompletedPickupCount == _pickupCountBeforeReservationInvalidation
                    && _worker.CompletedDepositCount == _depositCountBeforeReservationInvalidation;
            }
        }

        private static void HandleWorkerDeposited()
        {
            _workerDepositEventCount++;
        }

        private static void HandleUnitSold(SaleFeedbackData feedback)
        {
            _saleEventCount++;
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
                throw new InvalidOperationException($"M3 smoke timed out in stage {_stage}.");
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
            string result = $"M3 worker Play Mode smoke failed: {message}";
            Debug.LogError(result);
            EndRun(false, result);
        }

        private static void EndRun(bool success, string message)
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
                _workerUnlock.PadUnlocked -= HandlePadUnlocked;
                _workerUnlock.WorkerActivated -= HandleWorkerActivated;
            }

            if (_worker != null)
            {
                _worker.TargetClaimLost -= HandleTargetClaimLost;
                _worker.WoodDeposited -= HandleWorkerDeposited;
            }

            if (_salePoint != null)
            {
                _salePoint.UnitSold -= HandleUnitSold;
            }

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
