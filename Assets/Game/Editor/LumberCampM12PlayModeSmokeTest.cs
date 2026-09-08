using System;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Lean real-scene M12 smoke. Persistence is disabled in the opened scene
    /// before Play Mode, so this test never reads or writes the player's save.
    /// </summary>
    [InitializeOnLoad]
    public static class LumberCampM12PlayModeSmokeTest
    {
        private const string ScenePath =
            "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string KeyPrefix = "IndustryTycoon.M12.WorkshopSmoke.";
        private const string RunningKey = KeyPrefix + "Running";
        private const string CommandLineKey = KeyPrefix + "CommandLine";
        private const string FinishPendingKey = KeyPrefix + "FinishPending";
        private const string SuccessKey = KeyPrefix + "Success";
        private const string ResultMessageKey = KeyPrefix + "ResultMessage";
        private const string ErrorCountKey = KeyPrefix + "ErrorCount";
        private const string OriginalScenePathKey = KeyPrefix + "OriginalScenePath";

        private enum Stage
        {
            Warmup,
            UnlockAndPurchase,
            DepositPlanksOnly,
            ForgeFirstTool,
            CollectAndSell,
            OutputFullPauseResume,
            StableRestore
        }

        private static LocalPersistenceService _persistence;
        private static LumberCampProgressionService _progression;
        private static ToolWorkshopUnlock _unlock;
        private static ToolWorkshop _workshop;
        private static PurchasePad _purchasePad;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static CashPile _cashPile;
        private static SalePoint _salePoint;

        private static Stage _stage;
        private static double _runStartedAt;
        private static double _stageStartedAt;
        private static bool _runtimeInitialized;
        private static int _recipeEventCount;
        private static int _lastConsumedPlanks;
        private static int _lastConsumedIronBars;
        private static int _lastProducedTools;
        private static int _empireEventCount;
        private static int _lastEmpireLevel;
        private static int _saleEventCount;
        private static SaleFeedbackData _lastSale;

        static LumberCampM12PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/Prototype/Run M12 Workshop Smoke Test")]
        private static void RunFromMenu()
        {
            StartSmokeTest(false);
        }

        public static void RunFromCommandLine()
        {
            try
            {
                StartSmokeTest(true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void StartSmokeTest(bool commandLine)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Exit Play Mode before starting the M12 Workshop smoke.");
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

            string originalScenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrWhiteSpace(originalScenePath))
            {
                originalScenePath = ScenePath;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            LocalPersistenceService[] persistenceServices =
                Object.FindObjectsByType<LocalPersistenceService>(
                    FindObjectsInactive.Include);
            if (persistenceServices.Length != 1)
            {
                throw new InvalidOperationException(
                    "M12 smoke requires exactly one LocalPersistenceService.");
            }

            // This in-memory scene change is discarded after Play Mode. Keeping the
            // service disabled guarantees that no real persistent path is touched.
            persistenceServices[0].enabled = false;

            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(CommandLineKey, commandLine);
            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(SuccessKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
            SessionState.SetInt(ErrorCountKey, 0);
            SessionState.SetString(OriginalScenePathKey, originalScenePath);

            _stage = Stage.Warmup;
            _runtimeInitialized = false;
            _recipeEventCount = 0;
            _empireEventCount = 0;
            _saleEventCount = 0;
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
                if (!_runtimeInitialized && !TryResolveRuntimeReferences())
                {
                    if (Now - _stageStartedAt > 8d)
                    {
                        throw new InvalidOperationException(
                            "M12 smoke could not resolve the rebuilt scene graph.");
                    }

                    return;
                }

                if (Now - _runStartedAt > 45d)
                {
                    throw new InvalidOperationException(
                        "M12 Workshop smoke exceeded its 45-second timeout.");
                }

                ValidateOwnershipInvariants();
                TickCurrentStage();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static bool TryResolveRuntimeReferences()
        {
            LocalPersistenceService[] matches =
                Object.FindObjectsByType<LocalPersistenceService>(
                    FindObjectsInactive.Include);
            if (matches.Length != 1)
            {
                return false;
            }

            LocalPersistenceService persistence = matches[0];
            LumberCampProgressionService progression =
                persistence.ProgressionService;
            ToolWorkshop workshop = persistence.ToolWorkshop;
            ToolWorkshopUnlock unlock = persistence.ToolWorkshopUnlock;
            if (persistence.enabled
                || progression == null
                || workshop == null
                || unlock == null
                || unlock.WorkshopPurchasePad == null
                || persistence.CarryStack == null
                || persistence.Wallet == null
                || persistence.CashPile == null
                || progression.SalePoint == null
                || progression.ToolWorkshop != workshop
                || progression.ToolWorkshopUnlock != unlock)
            {
                return false;
            }

            _persistence = persistence;
            _progression = progression;
            _unlock = unlock;
            _workshop = workshop;
            _purchasePad = unlock.WorkshopPurchasePad;
            _carryStack = persistence.CarryStack;
            _wallet = persistence.Wallet;
            _cashPile = persistence.CashPile;
            _salePoint = progression.SalePoint;

            _workshop.RecipeCompleted -= HandleRecipeCompleted;
            _workshop.RecipeCompleted += HandleRecipeCompleted;
            _progression.EmpireLevelAdvanced -= HandleEmpireLevelAdvanced;
            _progression.EmpireLevelAdvanced += HandleEmpireLevelAdvanced;
            _salePoint.UnitSold -= HandleUnitSold;
            _salePoint.UnitSold += HandleUnitSold;
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
                case Stage.UnlockAndPurchase:
                    TickUnlockAndPurchase();
                    break;
                case Stage.DepositPlanksOnly:
                    TickDepositPlanksOnly();
                    break;
                case Stage.ForgeFirstTool:
                    TickForgeFirstTool();
                    break;
                case Stage.CollectAndSell:
                    TickCollectAndSell();
                    break;
                case Stage.OutputFullPauseResume:
                    TickOutputFullPauseResume();
                    break;
                case Stage.StableRestore:
                    TickStableRestore();
                    break;
            }
        }

        private static void TickWarmup()
        {
            EnsureStageTimeout(8d);
            if (!_progression.IsRuntimeReady || !HasWaited(0.15d))
            {
                return;
            }

            Require(!_persistence.IsInitialized
                    && string.IsNullOrEmpty(_persistence.SavePath)
                    && _persistence.SuccessfulSaveCount == 0,
                "Disabled smoke persistence unexpectedly initialized or opened a save.");

            StopUnrelatedRuntimeActivity();
            Require(_carryStack.RestoreStableState(null, 0)
                    && _wallet.RestoreBalance(0)
                    && _cashPile.RestoreStoredCash(0)
                    && _workshop.RestoreStableState(0, 0, 0),
                "M12 smoke could not establish empty runtime buffers.");

            Require(!_unlock.IsPadUnlocked
                    && !_unlock.IsWorkshopActivated
                    && !_purchasePad.IsCompleted,
                "Tool Workshop was available before cross-industry access.");

            _progression.RestorePersistentState(CreateEstablishedM11State());
            Require(_progression.EmpireLevel == 3
                    && _empireEventCount == 0,
                "Restoring established M11 state replayed Empire feedback.");
            Require(_unlock.IsPadUnlocked
                    && !_unlock.IsWorkshopActivated
                    && _purchasePad.IsAvailable
                    && _purchasePad.gameObject.activeSelf
                    && !_workshop.gameObject.activeSelf,
                "Plank + Iron Bar access did not reveal only the Workshop opportunity.");
            AdvanceTo(Stage.UnlockAndPurchase);
        }

        private static void TickUnlockAndPurchase()
        {
            EnsureStageTimeout(5d);
            int funding = Mathf.Max(0, _purchasePad.RemainingCost - _wallet.Balance);
            Require(funding == 0 || _wallet.Deposit(funding) == funding,
                "Could not fund the $3,200 Tool Workshop purchase.");

            int guard = 0;
            while (!_purchasePad.IsCompleted && guard++ < 1000)
            {
                Require(_purchasePad.ProcessPaymentStep() > 0,
                    "Affordable Tool Workshop payment was rejected.");
            }

            Require(_purchasePad.IsCompleted
                    && _purchasePad.RemainingCost == 0
                    && _purchasePad.ProcessPaymentStep() == 0
                    && _unlock.IsWorkshopActivated
                    && _unlock.ActivationCount == 1
                    && _workshop.gameObject.activeSelf,
                "Tool Workshop purchase did not activate exactly once.");
            Require(_progression.GetMetric(
                        ProgressMetricId.ToolWorkshopUnlocked) == 1L
                    && _progression.EmpireLevel == 4
                    && _empireEventCount == 1
                    && _lastEmpireLevel == 4,
                "Workshop purchase did not commit its metric and Empire Level 4 once.");

            _progression.enabled = false;
            _progression.enabled = true;
            _progression.enabled = false;
            _progression.enabled = true;
            Require(_progression.GetMetric(
                        ProgressMetricId.ToolWorkshopUnlocked) == 1L
                    && _empireEventCount == 1,
                "Progression resubscription duplicated Workshop unlock state.");
            AdvanceTo(Stage.DepositPlanksOnly);
        }

        private static void TickDepositPlanksOnly()
        {
            EnsureStageTimeout(3d);
            Require(_carryStack.TryAdd(ResourceType.Plank, 2)
                    && !_carryStack.TryAdd(ResourceType.IronBar, 1),
                "CarryStack did not isolate its Plank transport trip.");
            Require(_workshop.TryTransferInputFrom(
                        _carryStack,
                        ResourceType.Plank)
                    && _workshop.TryTransferInputFrom(
                        _carryStack,
                        ResourceType.Plank),
                "Separate Plank delivery did not deposit exactly two inputs.");
            Require(_carryStack.TotalAmount == 0
                    && _workshop.InputPlanks == 2
                    && _workshop.ProcessingInputPlanks == 0
                    && _workshop.InputIronBars == 0
                    && _workshop.ProcessingInputIronBars == 0
                    && _workshop.OutputTools == 0
                    && _workshop.ReservedOutputCapacity == 0
                    && !_workshop.IsProcessing
                    && _recipeEventCount == 0
                    && _progression.GetMetric(ProgressMetricId.ToolsProduced) == 0L,
                "Planks alone started or mutated the multi-input recipe.");
            AdvanceTo(Stage.ForgeFirstTool);
        }

        private static void TickForgeFirstTool()
        {
            EnsureStageTimeout(3d);
            Require(_carryStack.TryAdd(ResourceType.IronBar, 1)
                    && !_carryStack.TryAdd(ResourceType.Tool, 1),
                "CarryStack did not isolate the separate Iron Bar trip.");
            Require(_workshop.TryTransferInputFrom(
                        _carryStack,
                        ResourceType.IronBar)
                    && _workshop.IsProcessing
                    && _workshop.InputPlanks == 0
                    && _workshop.ProcessingInputPlanks == 2
                    && _workshop.InputIronBars == 0
                    && _workshop.ProcessingInputIronBars == 1
                    && _workshop.OutputTools == 0
                    && _workshop.ReservedOutputCapacity == 1,
                "Adding the missing Iron Bar did not reserve exact 2 + 1 ownership.");
            Require(_workshop.CompleteProcessingImmediatelyForTests()
                    && !_workshop.IsProcessing
                    && _workshop.ProcessingInputPlanks == 0
                    && _workshop.ProcessingInputIronBars == 0
                    && _workshop.ReservedOutputCapacity == 0
                    && _workshop.OutputTools == 1
                    && _recipeEventCount == 1
                    && _lastConsumedPlanks == 2
                    && _lastConsumedIronBars == 1
                    && _lastProducedTools == 1,
                "Exact Workshop recipe did not commit one Tool once.");
            Require(_progression.GetMetric(ProgressMetricId.ToolsProduced) == 1L
                    && _progression.EmpireLevel == 5
                    && _empireEventCount == 2
                    && _lastEmpireLevel == 5,
                "First Tool did not commit one metric and Empire Level 5.");
            Require(!_workshop.CompleteProcessingImmediatelyForTests()
                    && _recipeEventCount == 1
                    && _progression.GetMetric(ProgressMetricId.ToolsProduced) == 1L,
                "An empty repeated completion duplicated the first Tool.");
            AdvanceTo(Stage.CollectAndSell);
        }

        private static void TickCollectAndSell()
        {
            EnsureStageTimeout(3d);
            Require(_workshop.TryTransferOutputTo(_carryStack)
                    && _workshop.OutputTools == 0
                    && _carryStack.GetAmount(ResourceType.Tool) == 1
                    && _carryStack.ActiveResourceType == ResourceType.Tool
                    && !_carryStack.TryAdd(ResourceType.Plank, 1),
                "Manual Tool collection was not type-isolated.");

            int cashBefore = _cashPile.StoredCash;
            long earnedBefore = _progression.GetMetric(
                ProgressMetricId.TotalCashEarned);
            Require(_salePoint.TryUnloadOne()
                    && _cashPile.StoredCash - cashBefore == 100
                    && _carryStack.TotalAmount == 0
                    && _saleEventCount == 1
                    && _lastSale.ResourceType == ResourceType.Tool
                    && _lastSale.CashValue == 100
                    && _progression.GetMetric(ProgressMetricId.ToolsSold) == 1L
                    && _progression.GetMetric(ProgressMetricId.TotalCashEarned)
                       - earnedBefore == 100L,
                "Authoritative SalePoint did not sell one Tool for exactly $100.");
            AdvanceTo(Stage.OutputFullPauseResume);
        }

        private static void TickOutputFullPauseResume()
        {
            EnsureStageTimeout(3d);
            long toolsBefore = _progression.GetMetric(
                ProgressMetricId.ToolsProduced);
            Require(_workshop.RestoreStableState(2, 1, 12),
                "Could not stage a full Workshop output.");
            Require(!_workshop.CompleteProcessingImmediatelyForTests()
                    && _workshop.InputPlanks == 2
                    && _workshop.InputIronBars == 1
                    && _workshop.OutputTools == 12
                    && !_workshop.IsProcessing
                    && _workshop.ReservedOutputCapacity == 0
                    && _progression.GetMetric(ProgressMetricId.ToolsProduced)
                       == toolsBefore,
                "Output-full Workshop consumed inputs or fabricated a metric.");

            Require(_workshop.TryTransferOutputTo(_carryStack)
                    && _carryStack.GetAmount(ResourceType.Tool) == 1
                    && _workshop.IsProcessing
                    && _workshop.OutputTools == 11
                    && _workshop.ProcessingInputPlanks == 2
                    && _workshop.ProcessingInputIronBars == 1
                    && _workshop.ReservedOutputCapacity == 1,
                "Collecting from full output did not resume the reserved recipe.");
            Require(_workshop.CompleteProcessingImmediatelyForTests()
                    && _workshop.OutputTools == 12
                    && _workshop.InputPlanks == 0
                    && _workshop.InputIronBars == 0
                    && !_workshop.IsProcessing
                    && _recipeEventCount == 2
                    && _progression.GetMetric(ProgressMetricId.ToolsProduced)
                       == toolsBefore + 1L,
                "Resumed Workshop cycle lost or duplicated resources.");
            AdvanceTo(Stage.StableRestore);
        }

        private static void TickStableRestore()
        {
            EnsureStageTimeout(3d);
            Require(_workshop.RestoreStableState(7, 4, 5)
                    && _workshop.TryStartProcessing(),
                "Could not stage in-flight Workshop ownership for stable capture.");
            int stablePlanks = _workshop.InputPlanks
                               + _workshop.ProcessingInputPlanks;
            int stableIronBars = _workshop.InputIronBars
                                 + _workshop.ProcessingInputIronBars;
            int stableTools = _workshop.OutputTools;
            Require(stablePlanks == 7
                    && stableIronBars == 4
                    && stableTools == 5
                    && _workshop.ReservedOutputCapacity == 1,
                "In-flight Workshop ownership was not conservation-safe.");
            Require(_workshop.RestoreStableState(
                        stablePlanks,
                        stableIronBars,
                        stableTools)
                    && _workshop.InputPlanks == 7
                    && _workshop.ProcessingInputPlanks == 0
                    && _workshop.InputIronBars == 4
                    && _workshop.ProcessingInputIronBars == 0
                    && _workshop.OutputTools == 5
                    && _workshop.ReservedOutputCapacity == 0
                    && !_workshop.IsProcessing,
                "Stable Workshop restore did not refund transient ownership.");
            Require(!_persistence.IsInitialized
                    && string.IsNullOrEmpty(_persistence.SavePath)
                    && _persistence.SuccessfulSaveCount == 0,
                "M12 smoke touched the real persistence service.");
            Require(SessionState.GetInt(ErrorCountKey, 0) == 0,
                "M12 Workshop smoke observed Console errors/assertions.");

            Pass(
                "M12 Workshop Play Mode smoke passed: gated $3,200 activation, "
                + "separate typed inputs, exact 2 + 1 → 1 commit, full-output "
                + "pause/resume, Tool isolation, $100 sale, authoritative metrics, "
                + "Empire Level 5, and stable transient-ownership restore.");
        }

        private static M10ProgressionSaveData CreateEstablishedM11State()
        {
            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            for (int i = 0; i < LumberCampProgressionCatalog.FlagCount; i++)
            {
                state.flags[i].value = true;
            }

            SetMetric(state, ProgressMetricId.PlanksProduced, 10L);
            SetMetric(state, ProgressMetricId.CratesProduced, 5L);
            SetMetric(state, ProgressMetricId.CourierTripsCompleted, 5L);
            SetMetric(state, ProgressMetricId.IronOreMined, 10L);
            SetMetric(state, ProgressMetricId.IronBarsProduced, 5L);
            SetMetric(state, ProgressMetricId.MineUnlocked, 1L);
            SetMetric(state, ProgressMetricId.DrillUnlocked, 1L);
            state.objectiveIndex = (int)MainObjectiveId.UnlockToolWorkshop;
            return state;
        }

        private static void SetMetric(
            M10ProgressionSaveData state,
            ProgressMetricId metric,
            long value)
        {
            state.metrics[(int)metric].value = value;
        }

        private static void StopUnrelatedRuntimeActivity()
        {
            _progression.ResourceCollector?.CancelTransientAttractions();
            SetEnabled(_progression.ResourceCollector, false);
            SetEnabled(_progression.WoodSpawner, false);
            SetEnabled(_persistence.LumberWorker, false);
            SetEnabled(_persistence.AutoFeeder, false);
            SetEnabled(_persistence.Processor, false);
            SetEnabled(_persistence.PackingStation, false);
            SetEnabled(_persistence.Courier, false);
            SetEnabled(_persistence.IronVein, false);
            SetEnabled(_persistence.Smelter, false);
            SetEnabled(_persistence.AutomatedDrill, false);
        }

        private static void SetEnabled(Behaviour behaviour, bool enabled)
        {
            if (behaviour != null)
            {
                behaviour.enabled = enabled;
            }
        }

        private static void ValidateOwnershipInvariants()
        {
            if (!_runtimeInitialized)
            {
                return;
            }

            Require(_carryStack.TotalAmount >= 0
                    && _carryStack.ReservedCapacity >= 0
                    && _carryStack.TotalAmount + _carryStack.ReservedCapacity
                       <= _carryStack.Capacity,
                "Tool CarryStack ownership invariant regressed.");
            Require(_workshop.InputPlanks >= 0
                    && _workshop.ProcessingInputPlanks >= 0
                    && _workshop.InputPlanks + _workshop.ProcessingInputPlanks
                       <= _workshop.PlankInputCapacity
                    && _workshop.InputIronBars >= 0
                    && _workshop.ProcessingInputIronBars >= 0
                    && _workshop.InputIronBars
                       + _workshop.ProcessingInputIronBars
                       <= _workshop.IronBarInputCapacity
                    && _workshop.OutputTools >= 0
                    && _workshop.ReservedOutputCapacity >= 0
                    && _workshop.OutputTools + _workshop.ReservedOutputCapacity
                       <= _workshop.OutputCapacity,
                "Tool Workshop ownership invariant regressed.");
        }

        private static void HandleRecipeCompleted(
            int consumedPlanks,
            int consumedIronBars,
            int producedTools)
        {
            _recipeEventCount++;
            _lastConsumedPlanks = consumedPlanks;
            _lastConsumedIronBars = consumedIronBars;
            _lastProducedTools = producedTools;
        }

        private static void HandleEmpireLevelAdvanced(int empireLevel)
        {
            _empireEventCount++;
            _lastEmpireLevel = empireLevel;
        }

        private static void HandleUnitSold(SaleFeedbackData feedback)
        {
            _saleEventCount++;
            _lastSale = feedback;
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
                && stackTrace.Contains("UnityEditor.Search.SearchDatabase");
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
                    "M12 Workshop smoke timed out in stage " + _stage + ".");
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
            string result = "M12 Workshop Play Mode smoke failed: " + message;
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
            string originalScenePath = SessionState.GetString(
                OriginalScenePathKey,
                ScenePath);
            try
            {
                if (!string.IsNullOrWhiteSpace(originalScenePath)
                    && AssetDatabase.LoadAssetAtPath<SceneAsset>(originalScenePath)
                    != null)
                {
                    EditorSceneManager.OpenScene(
                        originalScenePath,
                        OpenSceneMode.Single);
                }
            }
            catch (Exception exception)
            {
                success = false;
                message = "M12 smoke could not restore the original scene: "
                          + exception.Message;
                Debug.LogError(message);
            }

            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(CommandLineKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
            SessionState.SetString(OriginalScenePathKey, string.Empty);
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
