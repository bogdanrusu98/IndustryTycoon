using System;
using System.IO;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Mining;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using IndustryTycoon.ResourceSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// M11 Play Mode vertical-slice smoke. It drives accepted gameplay commit
    /// points from Lumber completion through Mine, Smelter, sale, Drill, and Ore
    /// Storage, then verifies canonical save/reload/reset behavior.
    /// </summary>
    [InitializeOnLoad]
    public static class LumberCampM11PlayModeSmokeTest
    {
        private const string ScenePath =
            "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey =
            "IndustryTycoon.M11.VerticalSlice.Running";
        private const string CommandLineKey =
            "IndustryTycoon.M11.VerticalSlice.CommandLine";
        private const string FinishPendingKey =
            "IndustryTycoon.M11.VerticalSlice.FinishPending";
        private const string SuccessKey =
            "IndustryTycoon.M11.VerticalSlice.Success";
        private const string ResultMessageKey =
            "IndustryTycoon.M11.VerticalSlice.ResultMessage";
        private const string ErrorCountKey =
            "IndustryTycoon.M11.VerticalSlice.ErrorCount";

        private enum Stage
        {
            Warmup,
            WaitForLumberCompletion,
            VerifyWrongTypePause,
            WaitForTimedManualMining,
            VerifyFullCarryPause,
            BuyAndExerciseSmelter,
            WaitForTimedSmelterCycle,
            BuyAndExerciseDrill,
            WaitForTimedDrillCycle,
            WaitForStorageCollectorTransfer,
            FinishDrillCoverage,
            WaitForPersistenceReload,
            WaitForFreshReset
        }

        private static LocalPersistenceService _persistence;
        private static LumberCampProgressionService _progression;
        private static CharacterController _playerController;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static CashPile _cashPile;
        private static SalePoint _salePoint;
        private static PackingStation _packingStation;
        private static CrateCourier _courier;
        private static LumberCampCompletion _completion;
        private static MineUnlock _mineUnlock;
        private static IronVein _ironVein;
        private static SmelterUnlock _smelterUnlock;
        private static Smelter _smelter;
        private static DrillUnlock _drillUnlock;
        private static AutomatedDrill _drill;
        private static OreStorage _oreStorage;
        private static OreStorageCollector _oreStorageCollector;
        private static SmoothFollowCamera _followCamera;
        private static UnityEngine.Camera _camera;

        private static Stage _stage;
        private static double _runStartedAt;
        private static double _stageStartedAt;
        private static double _wrongTypeObservedAt;
        private static double _manualCycleStartedAt;
        private static double _smelterCycleStartedAt;
        private static double _drillCycleStartedAt;
        private static long _pauseMetricSnapshot;
        private static long _barsProducedBeforeTimedCycle;
        private static long _oreProducedBeforeTimedCycle;
        private static int _smelterCyclesBeforeTimedCycle;
        private static int _drillCyclesBeforeTimedCycle;
        private static int _collectorStorageBefore;
        private static int _collectorCarryBefore;
        private static bool _runtimeInitialized;
        private static bool _initialScenePrepared;

        private static long _expectedIronOreMined;
        private static long _expectedIronOreProduced;
        private static long _expectedIronOreSold;
        private static long _expectedIronBarsProduced;
        private static long _expectedIronBarsSold;
        private static long _expectedMineUnlocked;
        private static long _expectedDrillUnlocked;

        static LumberCampM11PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/Prototype/Run M11 Vertical Slice Smoke Test")]
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
                    "Exit Play Mode before starting the M11 vertical-slice smoke.");
            }

            if (!commandLine
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new InvalidOperationException(
                    "Missing prototype scene at " + ScenePath + ".");
            }

            M9EditorSaveUtility.PrepareFreshSmokeTest();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(CommandLineKey, commandLine);
            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
            SessionState.SetInt(ErrorCountKey, 0);

            _stage = Stage.Warmup;
            _runtimeInitialized = false;
            _initialScenePrepared = false;
            _wrongTypeObservedAt = -1d;
            _smelterCycleStartedAt = 0d;
            _drillCycleStartedAt = 0d;
            _runStartedAt = 0d;
            _stageStartedAt = 0d;
            EditorApplication.update -= UpdateSmokeTest;
            EditorApplication.update += UpdateSmokeTest;
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode
                && SessionState.GetBool(RunningKey, false))
            {
                _runStartedAt = Now;
                _stageStartedAt = Now;
                TryResolveRuntimeReferences();
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
                if (!_runtimeInitialized || _persistence == null)
                {
                    _runtimeInitialized = false;
                    if (!TryResolveRuntimeReferences())
                    {
                        if (Now - _stageStartedAt > 8d)
                        {
                            throw new InvalidOperationException(
                                "M11 smoke could not resolve the reloaded scene graph in stage "
                                + _stage + ".");
                        }

                        return;
                    }
                }

                if (Now - _runStartedAt > 120d)
                {
                    throw new InvalidOperationException(
                        "M11 vertical-slice smoke exceeded its 120-second timeout.");
                }

                ValidateContinuousInvariants();
                TickCurrentStage();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static bool TryResolveRuntimeReferences()
        {
            LocalPersistenceService[] persistenceMatches =
                Object.FindObjectsByType<LocalPersistenceService>(
                    FindObjectsInactive.Include);
            if (persistenceMatches.Length != 1)
            {
                return false;
            }

            LocalPersistenceService persistence = persistenceMatches[0];
            LumberCampProgressionService progression =
                persistence.ProgressionService;
            if (progression == null
                || persistence.Wallet == null
                || persistence.CashPile == null
                || persistence.CarryStack == null
                || persistence.PackingStation == null
                || persistence.Courier == null
                || persistence.LumberCampCompletion == null
                || persistence.MineUnlock == null
                || persistence.IronVein == null
                || persistence.SmelterUnlock == null
                || persistence.Smelter == null
                || persistence.DrillUnlock == null
                || persistence.AutomatedDrill == null
                || persistence.OreStorage == null
                || progression.SalePoint == null)
            {
                return false;
            }

            SmoothFollowCamera[] followMatches =
                Object.FindObjectsByType<SmoothFollowCamera>(
                    FindObjectsInactive.Include);
            if (followMatches.Length != 1)
            {
                return false;
            }

            OreStorageCollector[] storageCollectorMatches =
                Object.FindObjectsByType<OreStorageCollector>(
                    FindObjectsInactive.Include);
            if (storageCollectorMatches.Length != 1)
            {
                return false;
            }

            CharacterController controller =
                persistence.CarryStack.GetComponent<CharacterController>();
            UnityEngine.Camera camera = followMatches[0].GetComponent<UnityEngine.Camera>();
            if (controller == null || camera == null)
            {
                return false;
            }

            _persistence = persistence;
            _progression = progression;
            _playerController = controller;
            _carryStack = persistence.CarryStack;
            _wallet = persistence.Wallet;
            _cashPile = persistence.CashPile;
            _salePoint = progression.SalePoint;
            _packingStation = persistence.PackingStation;
            _courier = persistence.Courier;
            _completion = persistence.LumberCampCompletion;
            _mineUnlock = persistence.MineUnlock;
            _ironVein = persistence.IronVein;
            _smelterUnlock = persistence.SmelterUnlock;
            _smelter = persistence.Smelter;
            _drillUnlock = persistence.DrillUnlock;
            _drill = persistence.AutomatedDrill;
            _oreStorage = persistence.OreStorage;
            _oreStorageCollector = storageCollectorMatches[0];
            _followCamera = followMatches[0];
            _camera = camera;

            Require(_progression.MineUnlock == _mineUnlock
                    && _progression.IronVein == _ironVein
                    && _progression.SmelterUnlock == _smelterUnlock
                    && _progression.Smelter == _smelter
                    && _progression.DrillUnlock == _drillUnlock
                    && _progression.AutomatedDrill == _drill,
                "M11 progression is not wired to the authoritative Mining graph.");
            Require(_ironVein.CarryStack == _carryStack
                    && _ironVein.PlayerCollider == _playerController
                    && _smelterUnlock.SmelterPurchasePad != null
                    && _drillUnlock.DrillPurchasePad != null
                    && _drill.Storage == _oreStorage
                    && _oreStorageCollector.Storage == _oreStorage
                    && _oreStorageCollector.CarryStack == _carryStack
                    && _oreStorageCollector.PlayerCollider == _playerController,
                "M11 Mining interaction references are incomplete.");
            _runtimeInitialized = true;
            return true;
        }

        private static void TickCurrentStage()
        {
            switch (_stage)
            {
                case Stage.Warmup:
                    TickWarmup();
                    break;
                case Stage.WaitForLumberCompletion:
                    TickWaitForLumberCompletion();
                    break;
                case Stage.VerifyWrongTypePause:
                    TickVerifyWrongTypePause();
                    break;
                case Stage.WaitForTimedManualMining:
                    TickWaitForTimedManualMining();
                    break;
                case Stage.VerifyFullCarryPause:
                    TickVerifyFullCarryPause();
                    break;
                case Stage.BuyAndExerciseSmelter:
                    TickBuyAndExerciseSmelter();
                    break;
                case Stage.WaitForTimedSmelterCycle:
                    TickWaitForTimedSmelterCycle();
                    break;
                case Stage.BuyAndExerciseDrill:
                    TickBuyAndExerciseDrill();
                    break;
                case Stage.WaitForTimedDrillCycle:
                    TickWaitForTimedDrillCycle();
                    break;
                case Stage.WaitForStorageCollectorTransfer:
                    TickWaitForStorageCollectorTransfer();
                    break;
                case Stage.FinishDrillCoverage:
                    TickFinishDrillCoverage();
                    break;
                case Stage.WaitForPersistenceReload:
                    TickWaitForPersistenceReload();
                    break;
                case Stage.WaitForFreshReset:
                    TickWaitForFreshReset();
                    break;
            }
        }

        private static void TickWarmup()
        {
            EnsureStageTimeout(8d);
            if (!_persistence.IsInitialized
                || !_progression.IsRuntimeReady
                || !HasWaited(0.15d))
            {
                return;
            }

            Require(_persistence.LastLoadStatus == M9SaveLoadStatus.FreshNoSave
                    && !_completion.IsCompleted
                    && !_mineUnlock.IsUnlocked
                    && !_mineUnlock.MineAreaRoot.activeSelf
                    && _carryStack.TotalAmount == 0,
                "Fresh M11 smoke did not begin at the exact locked Mining state.");
            ValidateCameraExpansion();

            // Prove the service can resubscribe without duplicating later commits.
            _progression.enabled = false;
            _progression.enabled = true;
            _progression.enabled = false;
            _progression.enabled = true;
            Require(_progression.IsRuntimeReady && _progression.enabled,
                "M11 progression did not recover from enable/disable cycles.");

            // Keep unrelated Lumber automation quiet while using a real Courier
            // delivery as the authoritative Lumber-completion commit.
            _progression.ResourceCollector.CancelTransientAttractions();
            _progression.ResourceCollector.enabled = false;
            _progression.WoodSpawner.enabled = false;
            _persistence.LumberWorker.enabled = false;
            _persistence.AutoFeeder.enabled = false;
            _persistence.Processor.enabled = false;

            CompletePurchase(_progression.ProductionUpgrade.PurchasePad);
            CompletePurchase(_progression.WorkerUnlock.WorkerPurchasePad);
            CompletePurchase(_progression.ProcessorUnlock.ProcessorPurchasePad);
            CompletePurchase(_progression.AutoFeederUnlock.AutoFeederPurchasePad);
            CompletePurchase(
                _progression.PackingStationUnlock.PackingStationPurchasePad);
            CompletePurchase(_progression.CourierUnlock.CourierPurchasePad);
            Require(_progression.GetFlag(ProgressFlagId.CourierUnlocked)
                    && _progression.CourierUnlock.IsCourierActivated
                    && _courier.isActiveAndEnabled,
                "Canonical Lumber purchase chain did not activate Courier.");
            Require(_packingStation.RestoreStableState(0, 1),
                "Could not stage one real Crate for Lumber completion.");
            _courier.TryBeginTrip();
            _initialScenePrepared = true;
            AdvanceTo(Stage.WaitForLumberCompletion);
        }

        private static void TickWaitForLumberCompletion()
        {
            EnsureStageTimeout(30d);
            if (!_completion.IsCompleted || !_mineUnlock.IsUnlocked)
            {
                return;
            }

            Require(_initialScenePrepared
                    && _completion.CompletionCount == 1
                    && _courier.CompletedTripCount == 1
                    && _mineUnlock.UnlockCount == 1
                    && _mineUnlock.MineAreaRoot.activeSelf
                    && !_mineUnlock.LockedTeaserRoot.activeSelf
                    && _progression.GetFlag(
                        ProgressFlagId.LumberCampCompleted)
                    && _progression.GetMetric(
                        ProgressMetricId.MineUnlocked) == 1L,
                "Courier completion did not unlock the Mine exactly once.");
            Require(!_mineUnlock.TryUnlock()
                    && _mineUnlock.UnlockCount == 1
                    && _progression.GetMetric(
                        ProgressMetricId.MineUnlocked) == 1L,
                "Repeated Mine unlock duplicated persistent progress.");

            float areaSeparation = HorizontalDistance(
                _persistence.Stockpile.transform.position,
                _mineUnlock.MineAreaRoot.transform.position);
            Require(areaSeparation >= 12f,
                "Mine is not physically separated from the Lumber production area.");

            Require(_carryStack.RestoreStableState(ResourceType.Wood, 1),
                "Could not stage wrong-resource CarryStack eligibility.");
            MovePlayerToCollider(_ironVein.GetComponent<Collider>());
            _wrongTypeObservedAt = -1d;
            AdvanceTo(Stage.VerifyWrongTypePause);
        }

        private static void TickVerifyWrongTypePause()
        {
            EnsureStageTimeout(5d);
            if (!_ironVein.IsPlayerInside)
            {
                return;
            }

            if (_wrongTypeObservedAt < 0d)
            {
                Require(!_ironVein.IsEligible
                        && _ironVein.IsPausedByCarry
                        && !_ironVein.IsMining
                        && _carryStack.GetAmount(ResourceType.Wood) == 1,
                    "Wrong CarryStack resource did not pause manual Mining.");
                _pauseMetricSnapshot = _progression.GetMetric(
                    ProgressMetricId.IronOreMined);
                _wrongTypeObservedAt = Now;
                return;
            }

            if (Now - _wrongTypeObservedAt < 0.25d)
            {
                return;
            }

            Require(_progression.GetMetric(ProgressMetricId.IronOreMined)
                    == _pauseMetricSnapshot
                    && _ironVein.Progress01 <= 0.001f,
                "Wrong-type pause advanced or committed a Mining cycle.");
            Require(_carryStack.RestoreStableState(null, 0),
                "Could not clear the wrong-resource Mining fixture.");
            _manualCycleStartedAt = Now;
            AdvanceTo(Stage.WaitForTimedManualMining);
        }

        private static void TickWaitForTimedManualMining()
        {
            EnsureStageTimeout(_ironVein.MiningDuration + 3d);
            if (_carryStack.GetAmount(ResourceType.IronOre) == 0)
            {
                return;
            }

            Require(_carryStack.GetAmount(ResourceType.IronOre) == 1
                    && _carryStack.TotalAmount == 1
                    && _ironVein.CompletedCycleCount == 1
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreMined) == 1L
                    && Now - _manualCycleStartedAt
                       >= _ironVein.MiningDuration - 0.15d,
                "The 1.25-second trigger Mining cycle did not commit exactly one Ore.");
            Require(_carryStack.RestoreStableState(
                    ResourceType.IronOre,
                    _carryStack.Capacity),
                "Could not fill CarryStack for Mining full-pause coverage.");
            _pauseMetricSnapshot = _progression.GetMetric(
                ProgressMetricId.IronOreMined);
            AdvanceTo(Stage.VerifyFullCarryPause);
        }

        private static void TickVerifyFullCarryPause()
        {
            EnsureStageTimeout(4d);
            if (HasWaited(0.20d) && _ironVein.IsMining)
            {
                return;
            }

            if (!HasWaited(0.20d))
            {
                return;
            }

            Require(_ironVein.IsPausedByCarry
                    && !_ironVein.IsEligible
                    && !_ironVein.IsMining
                    && _carryStack.TotalAmount == _carryStack.Capacity
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreMined) == _pauseMetricSnapshot,
                "Full CarryStack did not pause Mining without a commit.");

            Require(_carryStack.RestoreStableState(ResourceType.IronOre, 1),
                "Could not resume the manual Mining fixture.");
            for (int i = 0; i < 10; i++)
            {
                Require(_ironVein.TryMineOne(),
                    "Eligible deterministic Mining commit failed at Ore "
                    + (i + 2) + ".");
            }

            Require(_carryStack.GetAmount(ResourceType.IronOre) == 11
                    && _ironVein.CompletedCycleCount == 11
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreMined) == 11L
                    && _smelterUnlock.MinedOreCount == 11
                    && _smelterUnlock.IsPadUnlocked
                    && _smelterUnlock.SmelterPurchasePad.IsAvailable,
                "Eleven real manual Ore commits did not unlock the Smelter pad.");
            MovePlayerAwayFrom(_ironVein.transform.position);
            AdvanceTo(Stage.BuyAndExerciseSmelter);
        }

        private static void TickBuyAndExerciseSmelter()
        {
            EnsureStageTimeout(8d);
            if (_ironVein.IsPlayerInside)
            {
                return;
            }

            int cashBeforeOreSale = _cashPile.StoredCash;
            Require(_salePoint.TryUnloadOne()
                    && _carryStack.GetAmount(ResourceType.IronOre) == 10
                    && _cashPile.StoredCash == cashBeforeOreSale + 10
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreSold) == 1L,
                "Iron Ore sale did not commit one Ore at $10.");

            Require(_smelterUnlock.SmelterPurchasePad.TotalCost == 1200,
                "Scene Smelter purchase cost is not $1200.");
            CompletePurchase(_smelterUnlock.SmelterPurchasePad);
            Require(_smelterUnlock.IsSmelterActivated
                    && _smelterUnlock.ActivationCount == 1
                    && _smelter.isActiveAndEnabled
                    && _progression.GetFlag(ProgressFlagId.SmelterUnlocked),
                "Smelter purchase did not activate its authoritative state once.");
            Require(_smelter.InputCapacity == 24
                    && _smelter.OutputCapacity == 12
                    && _smelter.RecipeInputOre == 2
                    && _smelter.RecipeOutputBars == 1
                    && Mathf.Approximately(_smelter.ProcessingDuration, 1.5f),
                "Smelter recipe/capacity tuning is not 2→1, 1.5s, 24/12.");

            Require(_smelter.RestoreStableState(0, 0)
                    && !_smelter.TryStartProcessing()
                    && _smelter.IsStarved,
                "Empty Smelter did not remain safely starved.");
            _barsProducedBeforeTimedCycle = _progression.GetMetric(
                ProgressMetricId.IronBarsProduced);
            _smelterCyclesBeforeTimedCycle = _smelter.CompletedRecipeCount;
            Require(_smelter.TryTransferInputFrom(_carryStack),
                "Smelter rejected the first Ore of the timed recipe.");
            Require(_smelter.InputOre == 1
                    && _smelter.ProcessingInputOre == 0
                    && !_smelter.IsProcessing,
                "Smelter consumed a partial timed recipe before two Ore existed.");
            _smelterCycleStartedAt = Now;
            Require(_smelter.TryTransferInputFrom(_carryStack)
                    && _smelter.IsProcessing
                    && _smelter.InputOre == 0
                    && _smelter.ProcessingInputOre == 2
                    && _smelter.ReservedOutputCapacity == 1,
                "Smelter did not atomically start its real timed recipe.");
            AdvanceTo(Stage.WaitForTimedSmelterCycle);
        }

        private static void TickWaitForTimedSmelterCycle()
        {
            EnsureStageTimeout(_smelter.ProcessingDuration + 3d);
            if (_smelter.CompletedRecipeCount == _smelterCyclesBeforeTimedCycle)
            {
                return;
            }

            Require(_smelter.CompletedRecipeCount
                    == _smelterCyclesBeforeTimedCycle + 1
                    && _smelter.InputOre == 0
                    && _smelter.ProcessingInputOre == 0
                    && _smelter.ReservedOutputCapacity == 0
                    && _smelter.OutputBars == 1
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsProduced)
                       == _barsProducedBeforeTimedCycle + 1L
                    && Now - _smelterCycleStartedAt
                       >= _smelter.ProcessingDuration - 0.15d,
                "The real 1.5-second Smelter cycle did not commit exactly one Bar.");

            for (int recipe = 1; recipe < 5; recipe++)
            {
                Require(_smelter.TryTransferInputFrom(_carryStack),
                    "Smelter rejected the first Ore of recipe " + recipe + ".");
                Require(_smelter.InputOre == 1
                        && _smelter.ProcessingInputOre == 0
                        && !_smelter.IsProcessing,
                    "Smelter consumed a partial recipe before two Ore existed.");
                Require(_smelter.TryTransferInputFrom(_carryStack)
                        && _smelter.IsProcessing
                        && _smelter.InputOre == 0
                        && _smelter.ProcessingInputOre == 2
                        && _smelter.ReservedOutputCapacity == 1,
                    "Smelter did not atomically own 2 Ore and reserve 1 Bar.");
                Require(_smelter.CompleteProcessingImmediatelyForTests()
                        && _smelter.InputOre == 0
                        && _smelter.ProcessingInputOre == 0
                        && _smelter.ReservedOutputCapacity == 0
                        && _smelter.OutputBars == recipe + 1
                        && _progression.GetMetric(
                            ProgressMetricId.IronBarsProduced)
                           == _barsProducedBeforeTimedCycle + recipe + 1L,
                    "Smelter recipe completion duplicated or lost resources.");
            }

            Require(_carryStack.TotalAmount == 0
                    && _smelter.OutputBars == 5
                    && (_smelter.InputOre + _smelter.ProcessingInputOre
                        + (2 * _smelter.OutputBars)) == 10
                    && _drillUnlock.ProducedBarCount == 5
                    && _drillUnlock.IsPadUnlocked,
                "Five Smelter recipes violated conservation or Drill unlock gating.");

            Require(_smelter.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.IronBar) == 1
                    && _smelter.OutputBars == 4,
                "Player could not collect a Smelter Iron Bar.");
            int cashBeforeBarSale = _cashPile.StoredCash;
            Require(_salePoint.TryUnloadOne()
                    && _cashPile.StoredCash == cashBeforeBarSale + 30
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsSold) == 1L,
                "Iron Bar sale did not commit one Bar at $30.");

            Require(_smelter.RestoreStableState(2, 12)
                    && _smelter.IsOutputFull
                    && !_smelter.TryStartProcessing()
                    && !_smelter.IsProcessing,
                "Full Smelter output did not pause with both Ore still owned.");
            Require(_smelter.TryTransferOutputTo(_carryStack)
                    && _smelter.OutputBars == 11
                    && _smelter.IsProcessing
                    && _smelter.ProcessingInputOre == 2
                    && _smelter.ReservedOutputCapacity == 1,
                "One Bar withdrawal did not resume a fully blocked Smelter.");
            Require(_smelter.CompleteProcessingImmediatelyForTests()
                    && _smelter.OutputBars == 12
                    && _smelter.InputOre == 0
                    && _smelter.ProcessingInputOre == 0
                    && _smelter.ReservedOutputCapacity == 0,
                "Resumed full-output recipe did not conserve its owned resources.");
            Require(_salePoint.TryUnloadOne()
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsSold) == 2L,
                "Second Bar sale was not recorded exactly once.");

            long beforeCancellation = _progression.GetMetric(
                ProgressMetricId.IronBarsProduced);
            Require(_smelter.RestoreStableState(2, 0)
                    && _smelter.TryStartProcessing(),
                "Could not stage Smelter cancellation ownership.");
            _smelter.enabled = false;
            Require(_smelter.InputOre == 2
                    && _smelter.ProcessingInputOre == 0
                    && _smelter.ReservedOutputCapacity == 0
                    && _smelter.OutputBars == 0
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsProduced) == beforeCancellation,
                "Disabling Smelter lost input, leaked a reservation, or faked a Bar.");
            _smelter.enabled = true;
            _smelter.enabled = false;
            Require(_smelter.RestoreStableState(0, 0),
                "Could not clear the cancellation fixture.");
            _smelter.enabled = true;
            Require(!_smelter.IsProcessing
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsProduced) == beforeCancellation,
                "Re-enable fabricated a cancelled Smelter recipe.");

            AdvanceTo(Stage.BuyAndExerciseDrill);
        }

        private static void TickBuyAndExerciseDrill()
        {
            EnsureStageTimeout(10d);
            Require(_drillUnlock.DrillPurchasePad.TotalCost == 2400,
                "Scene Automated Drill purchase cost is not $2400.");
            _drillCyclesBeforeTimedCycle = _drill.CompletedCycleCount;
            _oreProducedBeforeTimedCycle = _progression.GetMetric(
                ProgressMetricId.IronOreProduced);
            _drillCycleStartedAt = Now;
            CompletePurchase(_drillUnlock.DrillPurchasePad);
            Require(_drillUnlock.IsDrillActivated
                    && _drillUnlock.ActivationCount == 1
                    && _drill.isActiveAndEnabled
                    && _progression.GetMetric(
                        ProgressMetricId.DrillUnlocked) == 1L
                    && _oreStorage.Capacity == 30
                    && Mathf.Approximately(_drill.CycleDuration, 1.8f),
                "Drill purchase/tuning did not commit as $2400, 1.8s, capacity 30.");
            Require(_drill.IsProducing
                    && _drill.HasStorageReservation
                    && _oreStorage.StoredOre == 0
                    && _oreStorage.IncomingReservations == 1,
                "Activated Drill did not reserve one atomic storage slot.");

            AdvanceTo(Stage.WaitForTimedDrillCycle);
        }

        private static void TickWaitForTimedDrillCycle()
        {
            EnsureStageTimeout(_drill.CycleDuration + 3d);
            if (_drill.CompletedCycleCount == _drillCyclesBeforeTimedCycle)
            {
                return;
            }

            Require(_drill.CompletedCycleCount
                    == _drillCyclesBeforeTimedCycle + 1
                    && _oreStorage.StoredOre == 1
                    && _oreStorage.IncomingReservations == 1
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreProduced)
                       == _oreProducedBeforeTimedCycle + 1L
                    && Now - _drillCycleStartedAt
                       >= _drill.CycleDuration - 0.15d,
                "The real 1.8-second Drill cycle did not commit exactly one Ore.");

            _drill.enabled = false;
            Require(_oreStorage.StoredOre == 1
                    && _oreStorage.IncomingReservations == 0
                    && !_drill.HasStorageReservation,
                "Disabling Drill did not release its uncommitted storage claim.");
            _drill.enabled = true;
            Require(_drill.IsProducing
                    && _oreStorage.IncomingReservations == 1,
                "Drill did not resume after a released reservation.");
            _drill.enabled = false;
            Require(_oreStorage.StoredOre == 1
                    && _oreStorage.IncomingReservations == 0
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreProduced)
                       == _oreProducedBeforeTimedCycle + 1L,
                "Cancelled Drill cycle deposited or counted phantom Ore.");

            Require(_oreStorage.RestoreStableState(30),
                "Could not fill Ore Storage for pause coverage.");
            _drill.enabled = true;
            Require(_drill.State == AutomatedDrillState.StorageFull
                    && _drill.IsPausedForFullStorage
                    && !_drill.IsProducing
                    && _oreStorage.IncomingReservations == 0,
                "Full Ore Storage did not pause Drill without a reservation.");

            _collectorStorageBefore = _oreStorage.StoredOre;
            _collectorCarryBefore = _carryStack.GetAmount(ResourceType.IronOre);
            Require(_collectorCarryBefore == 0,
                "Ore Storage collector fixture requires an empty CarryStack.");
            MovePlayerToCollider(_oreStorageCollector.GetComponent<Collider>());
            AdvanceTo(Stage.WaitForStorageCollectorTransfer);
        }

        private static void TickWaitForStorageCollectorTransfer()
        {
            EnsureStageTimeout(5d);
            if (_carryStack.GetAmount(ResourceType.IronOre)
                == _collectorCarryBefore)
            {
                return;
            }

            Require(_oreStorageCollector.IsPlayerInside
                    && _oreStorageCollector.IsTransferring,
                "Ore Storage collector did not enter its real player-trigger flow.");
            MovePlayerAwayFrom(_oreStorageCollector.transform.position);
            AdvanceTo(Stage.FinishDrillCoverage);
        }

        private static void TickFinishDrillCoverage()
        {
            EnsureStageTimeout(5d);
            if (_oreStorageCollector.IsPlayerInside
                || _oreStorageCollector.IsTransferring)
            {
                return;
            }

            int collectedOre = _carryStack.GetAmount(ResourceType.IronOre)
                               - _collectorCarryBefore;
            Require(collectedOre > 0
                    && _collectorStorageBefore - _oreStorage.StoredOre
                       == collectedOre
                    && _drill.IsProducing
                    && _oreStorage.IncomingReservations == 1,
                "Player-trigger Ore Storage collection lost Ore or failed to resume Drill.");

            _drill.enabled = false;
            Require(_oreStorage.IncomingReservations == 0
                    && _carryStack.RestoreStableState(ResourceType.IronOre, 1)
                    && _oreStorage.RestoreStableState(29),
                "Could not normalize the real collector fixture for Drill coverage.");
            _drill.enabled = true;
            Require(_drill.IsProducing
                    && _oreStorage.StoredOre == 29
                    && _oreStorage.IncomingReservations == 1,
                "Collector withdrawal did not leave Drill able to reserve its next cycle.");
            Require(_oreStorage.TryTransferOneTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.IronOre) == 2
                    && _oreStorage.StoredOre == 28
                    && _oreStorage.IncomingReservations == 1,
                "Player could not manually withdraw a second stored Ore.");
            Require(_drill.CompleteCycleImmediatelyForTests()
                    && _oreStorage.StoredOre == 29
                    && _oreStorage.IncomingReservations == 1
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreProduced)
                       == _oreProducedBeforeTimedCycle + 2L,
                "Resumed Drill cycle duplicated/lost Ore or its metric.");
            _drill.enabled = false;
            Require(_oreStorage.StoredOre == 29
                    && _oreStorage.IncomingReservations == 0,
                "Stopping the resumed Drill leaked its next reservation.");

            Require(_oreStorage.TryReserveIncoming(
                        out OreStorageReservation reservation)
                    && reservation.IsValid
                    && _oreStorage.IncomingReservations == 1
                    && !_oreStorage.TryReserveIncoming(out _)
                    && _oreStorage.ReleaseIncoming(reservation)
                    && !_oreStorage.ReleaseIncoming(reservation)
                    && _oreStorage.StoredOre == 29
                    && _oreStorage.IncomingReservations == 0,
                "Ore Storage reservation token was not exclusive and once-only.");

            Require(_smelter.RestoreStableState(0, 0)
                    && _smelter.TryTransferInputFrom(_carryStack)
                    && _smelter.TryTransferInputFrom(_carryStack)
                    && _carryStack.TotalAmount == 0
                    && _smelter.IsProcessing
                    && _smelter.CompleteProcessingImmediatelyForTests()
                    && _smelter.OutputBars == 1
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsProduced) == 7L,
                "Storage → player → Smelter flow did not create one conserved Bar.");

            // Save while both machines own transient reservations. The stable
            // snapshot must refund Smelter input ownership and exclude the Drill
            // claim, without fabricating either output metric.
            Require(_smelter.RestoreStableState(3, 4)
                    && _smelter.TryStartProcessing()
                    && _smelter.InputOre + _smelter.ProcessingInputOre == 3
                    && _smelter.ReservedOutputCapacity == 1,
                "Could not stage in-flight Smelter persistence ownership.");
            Require(_oreStorage.RestoreStableState(7),
                "Could not stage Ore Storage persistence state.");
            _drill.enabled = true;
            Require(_drill.IsProducing
                    && _oreStorage.StoredOre == 7
                    && _oreStorage.IncomingReservations == 1,
                "Could not stage in-flight Drill persistence ownership.");

            CaptureExpectedMiningMetrics();
            int saveCountBefore = _persistence.SuccessfulSaveCount;
            Require(_persistence.SaveNow()
                    && _persistence.SuccessfulSaveCount == saveCountBefore + 1
                    && File.Exists(_persistence.SavePath),
                "M11 canonical state did not persist before reload.");
            string savedJson = File.ReadAllText(_persistence.SavePath);
            Require(savedJson.Contains("\"version\":3")
                    && savedJson.Contains("\"mining\"")
                    && !savedJson.Contains("processingInputOre")
                    && !savedJson.Contains("reservedOutputCapacity")
                    && !savedJson.Contains("incomingReservations")
                    && !savedJson.Contains("isProcessing")
                    && !savedJson.Contains("isProducing"),
                "Runtime save omitted v3 Mining state or leaked transient ownership.");

            AdvanceTo(Stage.WaitForPersistenceReload);
            _runtimeInitialized = false;
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex);
        }

        private static void TickWaitForPersistenceReload()
        {
            EnsureStageTimeout(10d);
            if (!_persistence.IsInitialized || !_progression.IsRuntimeReady)
            {
                return;
            }

            Require(_persistence.LastLoadStatus == M9SaveLoadStatus.LoadedPrimary
                    && _completion.IsCompleted
                    && _mineUnlock.IsUnlocked
                    && _smelterUnlock.IsSmelterActivated
                    && _drillUnlock.IsDrillActivated,
                "Reload did not restore the canonical M11 unlock chain.");
            Require(_smelter.InputOre + _smelter.ProcessingInputOre == 3
                    && _smelter.OutputBars == 4
                    && _smelter.InputOre >= 0
                    && _smelter.ProcessingInputOre >= 0
                    && _smelter.ReservedOutputCapacity >= 0
                    && _smelter.OutputBars + _smelter.ReservedOutputCapacity
                       <= _smelter.OutputCapacity,
                "Reload did not normalize in-flight Smelter ownership to 3 Ore / 4 Bars.");
            Require(_oreStorage.StoredOre == 7
                    && _oreStorage.IncomingReservations >= 0
                    && _oreStorage.StoredOre + _oreStorage.IncomingReservations
                       <= _oreStorage.Capacity,
                "Reload committed or lost the in-flight Drill storage claim.");
            RequireExpectedMiningMetrics(
                "Persistence reload fabricated or lost a Mining metric.");
            Require(_carryStack.TotalAmount == 0,
                "Persistence reload changed the empty player CarryStack.");

            AdvanceTo(Stage.WaitForFreshReset);
            _runtimeInitialized = false;
            Require(_persistence.ResetSaveAndReload(),
                "Runtime Reset Save could not delete and reload the M11 state.");
        }

        private static void TickWaitForFreshReset()
        {
            EnsureStageTimeout(10d);
            if (!_persistence.IsInitialized || !_progression.IsRuntimeReady)
            {
                return;
            }

            Require(_persistence.LastLoadStatus == M9SaveLoadStatus.FreshNoSave
                    && !_completion.IsCompleted
                    && !_mineUnlock.IsUnlocked
                    && !_mineUnlock.MineAreaRoot.activeSelf
                    && !_smelterUnlock.SmelterPurchasePad.IsCompleted
                    && !_drillUnlock.DrillPurchasePad.IsCompleted
                    && _smelter.InputOre == 0
                    && _smelter.ProcessingInputOre == 0
                    && _smelter.OutputBars == 0
                    && _smelter.ReservedOutputCapacity == 0
                    && _oreStorage.StoredOre == 0
                    && _oreStorage.IncomingReservations == 0
                    && _carryStack.TotalAmount == 0,
                "Reset Save did not restore exact fresh M11 gameplay state.");
            Require(_progression.GetMetric(ProgressMetricId.IronOreMined) == 0L
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreProduced) == 0L
                    && _progression.GetMetric(
                        ProgressMetricId.IronOreSold) == 0L
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsProduced) == 0L
                    && _progression.GetMetric(
                        ProgressMetricId.IronBarsSold) == 0L
                    && _progression.GetMetric(
                        ProgressMetricId.MineUnlocked) == 0L
                    && _progression.GetMetric(
                        ProgressMetricId.DrillUnlocked) == 0L
                    && !_progression.GetFlag(ProgressFlagId.SmelterUnlocked),
                "Reset Save retained Mining metrics or unlock flags.");
            Require(SessionState.GetInt(ErrorCountKey, 0) == 0,
                "M11 vertical slice observed Console errors/assertions.");

            Pass(
                "M11 vertical-slice Play Mode smoke passed: expanded portrait camera, "
                + "Courier completion → Mine unlock, timed/type/full-safe manual Mining, "
                + "$10 Ore, atomic 2→1 Smelter with full/cancel resume, $30 Bar, "
                + "$2400 Drill with exclusive storage reservation/full resume, manual "
                + "storage transfer, exact Mining metrics, v3 reload, and Reset Save.");
        }

        private static void ValidateCameraExpansion()
        {
            Vector3 expectedOffset = new Vector3(0f, 14f, -10.6f);
            Require((_followCamera.Offset - expectedOffset).sqrMagnitude < 0.0001f
                    && Mathf.Approximately(_camera.fieldOfView, 43f),
                "M11 camera must use offset (0,14,-10.6) and vertical FOV 43.");
            float beforeArea = ComputePortraitGroundArea(
                new Vector3(0f, 12f, -9f),
                new Vector3(0f, 1f, 0f),
                43f);
            float afterArea = ComputePortraitGroundArea(
                _followCamera.Offset,
                _followCamera.LookAtOffset,
                _camera.fieldOfView);
            float areaRatio = afterArea / beforeArea;
            float distanceRatio =
                (_followCamera.Offset - _followCamera.LookAtOffset).magnitude
                / (new Vector3(0f, 12f, -9f)
                   - new Vector3(0f, 1f, 0f)).magnitude;
            Require(areaRatio >= 1.30f
                    && areaRatio <= 1.50f
                    && distanceRatio <= 1.25f,
                $"Portrait ground area/readability ratio is outside M11 bounds: "
                + $"area={areaRatio:F3}, distance={distanceRatio:F3}.");
        }

        private static float ComputePortraitGroundArea(
            Vector3 cameraOffset,
            Vector3 lookAtOffset,
            float verticalFieldOfView)
        {
            const float portraitAspect = 9f / 16f;
            float tangent = Mathf.Tan(verticalFieldOfView * Mathf.Deg2Rad * 0.5f);
            Quaternion rotation = Quaternion.LookRotation(
                lookAtOffset - cameraOffset,
                Vector3.up);
            Vector2[] footprint = new Vector2[4];
            Vector2[] corners =
            {
                new Vector2(-1f, -1f),
                new Vector2(1f, -1f),
                new Vector2(1f, 1f),
                new Vector2(-1f, 1f)
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 localDirection = new Vector3(
                    corners[i].x * tangent * portraitAspect,
                    corners[i].y * tangent,
                    1f).normalized;
                Vector3 worldDirection = rotation * localDirection;
                Require(worldDirection.y < -0.0001f,
                    "Camera frustum corner does not intersect the gameplay ground.");
                float rayDistance = -cameraOffset.y / worldDirection.y;
                Vector3 hit = cameraOffset + worldDirection * rayDistance;
                footprint[i] = new Vector2(hit.x, hit.z);
            }

            float doubleArea = 0f;
            for (int i = 0; i < footprint.Length; i++)
            {
                Vector2 current = footprint[i];
                Vector2 next = footprint[(i + 1) % footprint.Length];
                doubleArea += current.x * next.y - next.x * current.y;
            }

            return Mathf.Abs(doubleArea) * 0.5f;
        }

        private static void CompletePurchase(PurchasePad pad)
        {
            Require(pad != null && pad.IsAvailable && !pad.IsCompleted,
                "PurchasePad was unavailable in the canonical progression order.");
            int requiredFunding = Mathf.Max(0, pad.RemainingCost - _wallet.Balance);
            if (requiredFunding > 0)
            {
                Require(_wallet.Deposit(requiredFunding) == requiredFunding,
                    "Could not fund " + pad.PurchaseLabel + ".");
            }

            int guard = 0;
            while (!pad.IsCompleted && guard++ < 2000)
            {
                Require(pad.ProcessPaymentStep() > 0,
                    pad.PurchaseLabel + " rejected an affordable payment.");
            }

            Require(pad.IsCompleted
                    && pad.RemainingCost == 0
                    && pad.ProcessPaymentStep() == 0,
                pad.PurchaseLabel + " did not complete exactly once.");
        }

        private static void CaptureExpectedMiningMetrics()
        {
            _expectedIronOreMined = _progression.GetMetric(
                ProgressMetricId.IronOreMined);
            _expectedIronOreProduced = _progression.GetMetric(
                ProgressMetricId.IronOreProduced);
            _expectedIronOreSold = _progression.GetMetric(
                ProgressMetricId.IronOreSold);
            _expectedIronBarsProduced = _progression.GetMetric(
                ProgressMetricId.IronBarsProduced);
            _expectedIronBarsSold = _progression.GetMetric(
                ProgressMetricId.IronBarsSold);
            _expectedMineUnlocked = _progression.GetMetric(
                ProgressMetricId.MineUnlocked);
            _expectedDrillUnlocked = _progression.GetMetric(
                ProgressMetricId.DrillUnlocked);
            Require(_expectedIronOreMined == 11L
                    && _expectedIronOreProduced == 2L
                    && _expectedIronOreSold == 1L
                    && _expectedIronBarsProduced == 7L
                    && _expectedIronBarsSold == 2L
                    && _expectedMineUnlocked == 1L
                    && _expectedDrillUnlocked == 1L,
                "Authoritative vertical-slice Mining metrics have unexpected totals.");
        }

        private static void RequireExpectedMiningMetrics(string message)
        {
            Require(_progression.GetMetric(ProgressMetricId.IronOreMined)
                    == _expectedIronOreMined
                    && _progression.GetMetric(ProgressMetricId.IronOreProduced)
                    == _expectedIronOreProduced
                    && _progression.GetMetric(ProgressMetricId.IronOreSold)
                    == _expectedIronOreSold
                    && _progression.GetMetric(ProgressMetricId.IronBarsProduced)
                    == _expectedIronBarsProduced
                    && _progression.GetMetric(ProgressMetricId.IronBarsSold)
                    == _expectedIronBarsSold
                    && _progression.GetMetric(ProgressMetricId.MineUnlocked)
                    == _expectedMineUnlocked
                    && _progression.GetMetric(ProgressMetricId.DrillUnlocked)
                    == _expectedDrillUnlocked,
                message);
        }

        private static void ValidateContinuousInvariants()
        {
            if (!_runtimeInitialized)
            {
                return;
            }

            Require(_carryStack.TotalAmount >= 0
                    && _carryStack.ReservedCapacity >= 0
                    && _carryStack.TotalAmount + _carryStack.ReservedCapacity
                       <= _carryStack.Capacity,
                "M11 CarryStack ownership invariant regressed.");
            Require(_smelter.InputOre >= 0
                    && _smelter.ProcessingInputOre >= 0
                    && _smelter.InputOre + _smelter.ProcessingInputOre
                       <= _smelter.InputCapacity
                    && _smelter.OutputBars >= 0
                    && _smelter.ReservedOutputCapacity >= 0
                    && _smelter.OutputBars + _smelter.ReservedOutputCapacity
                       <= _smelter.OutputCapacity,
                "M11 Smelter ownership invariant regressed.");
            Require(_oreStorage.StoredOre >= 0
                    && _oreStorage.IncomingReservations >= 0
                    && _oreStorage.StoredOre + _oreStorage.IncomingReservations
                       <= _oreStorage.Capacity,
                "M11 Ore Storage ownership invariant regressed.");
            for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
            {
                Require(_progression.GetMetric((ProgressMetricId)i) >= 0L,
                    "M11 metric became negative: " + (ProgressMetricId)i + ".");
            }
        }

        private static void MovePlayerToCollider(Collider target)
        {
            Require(target != null, "M11 trigger target is missing a Collider.");
            Vector3 controllerCenterOffset =
                _playerController.transform.TransformVector(_playerController.center);
            Vector3 targetCenter = target.bounds.center;
            Vector3 position = targetCenter - controllerCenterOffset;
            MovePlayerTo(position);
        }

        private static void MovePlayerAwayFrom(Vector3 origin)
        {
            Vector3 away = origin + new Vector3(-10f, 0f, -10f);
            away.y = _playerController.transform.position.y;
            if (_playerController.enabled)
            {
                _playerController.Move(
                    away - _playerController.transform.position);
                Physics.SyncTransforms();
                return;
            }

            MovePlayerTo(away);
        }

        private static void MovePlayerTo(Vector3 position)
        {
            bool wasEnabled = _playerController.enabled;
            _playerController.enabled = false;
            _playerController.transform.position = position;
            _playerController.enabled = wasEnabled;
            Physics.SyncTransforms();
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(second.x, second.z));
        }

        private static void HandleLogMessage(
            string condition,
            string stackTrace,
            LogType type)
        {
            bool isUnitySearchStartupException =
                type == LogType.Exception
                && condition.StartsWith(
                    "ArgumentOutOfRangeException",
                    StringComparison.Ordinal)
                && stackTrace.Contains(
                    "UnityEditor.Search.SearchDatabase");
            if (!SessionState.GetBool(RunningKey, false)
                || !EditorApplication.isPlaying
                || isUnitySearchStartupException
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
                    "M11 vertical slice timed out in stage " + _stage + ".");
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
            string result = "M11 vertical-slice Play Mode smoke failed: " + message;
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
                M9EditorSaveUtility.PrepareFreshSmokeTest();
            }
            catch (Exception exception)
            {
                success = false;
                message = "M11 smoke could not clean its isolated save: "
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
