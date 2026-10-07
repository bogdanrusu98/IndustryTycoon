using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;
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
    /// <summary>Real-scene M13 producer-to-sale smoke with retention and empire checks.</summary>
    [InitializeOnLoad]
    public static class LumberCampM13PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string Prefix = "IndustryTycoon.M13.Smoke.";
        private const string Running = Prefix + "Running";
        private const string CommandLine = Prefix + "CommandLine";
        private const string FinishPending = Prefix + "FinishPending";
        private const string Success = Prefix + "Success";
        private const string Message = Prefix + "Message";
        private const string ErrorCount = Prefix + "ErrorCount";

        private enum Stage { Resolve, Transfer, WaitForCrate, Sell, RetentionAndEmpire }

        private static LocalPersistenceService _persistence;
        private static CentralWarehouse _warehouse;
        private static IndustrialLogisticsRoute[] _routes;
        private static WoodProcessor _processor;
        private static PackingStation _packing;
        private static CarryStack _carry;
        private static LumberCampProgressionService _progression;
        private static Stage _stage;
        private static double _started;

        static LumberCampM13PlayModeSmokeTest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= Update;
            if (SessionState.GetBool(Running, false)) EditorApplication.update += Update;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            if (SessionState.GetBool(FinishPending, false))
            {
                EditorApplication.delayCall += Complete;
            }
        }

        [MenuItem("Industry Tycoon/Prototype/Run M13 Industrial Smoke Test")]
        private static void RunFromMenu() => Start(false);

        public static void RunFromCommandLine()
        {
            try { Start(true); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Start(bool commandLine)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before M13 smoke.");
            if (!commandLine && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new InvalidOperationException("Missing rebuilt prototype scene.");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            LocalPersistenceService[] services = Object.FindObjectsByType<LocalPersistenceService>(
                FindObjectsInactive.Include);
            if (services.Length != 1) throw new InvalidOperationException("Expected one persistence service.");
            services[0].enabled = false; // Never touch the user's persistent save.
            SessionState.SetBool(Running, true);
            SessionState.SetBool(CommandLine, commandLine);
            SessionState.SetBool(FinishPending, false);
            SessionState.SetBool(Success, false);
            SessionState.SetString(Message, string.Empty);
            SessionState.SetInt(ErrorCount, 0);
            _stage = Stage.Resolve;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode
                && SessionState.GetBool(Running, false))
            {
                _started = EditorApplication.timeSinceStartup;
            }
            else if (state == PlayModeStateChange.EnteredEditMode
                     && SessionState.GetBool(FinishPending, false))
            {
                EditorApplication.delayCall += Complete;
            }
        }

        private static void Update()
        {
            if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 25d)
                    throw new InvalidOperationException("M13 smoke exceeded 25 seconds.");

                switch (_stage)
                {
                    case Stage.Resolve:
                        ResolveAndSeed();
                        _stage = Stage.Transfer;
                        break;
                    case Stage.Transfer:
                        TransferToConsumer();
                        _stage = Stage.WaitForCrate;
                        break;
                    case Stage.WaitForCrate:
                        if (_packing.OutputCrates > 0) _stage = Stage.Sell;
                        break;
                    case Stage.Sell:
                        Require(_packing.TryTransferOutputTo(_carry), "Consumer output could not enter CarryStack.");
                        Require(_progression.SalePoint.TryUnloadOne(), "Produced Crate could not be sold.");
                        Require(_progression.GetMetric(ProgressMetricId.CratesSold) >= 1L,
                            "Authoritative sale metric was not updated.");
                        _stage = Stage.RetentionAndEmpire;
                        break;
                    case Stage.RetentionAndEmpire:
                        RunRetentionContractEmpireSmoke();
                        Pass();
                        break;
                }
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private static void ResolveAndSeed()
        {
            LocalPersistenceService[] services = Object.FindObjectsByType<LocalPersistenceService>(
                FindObjectsInactive.Include);
            if (services.Length != 1) throw new InvalidOperationException("Runtime service graph is missing.");
            _persistence = services[0];
            _warehouse = _persistence.CentralWarehouse;
            _routes = _persistence.LogisticsNetwork?.Routes;
            _processor = _persistence.Processor;
            _packing = _persistence.PackingStation;
            _carry = _persistence.CarryStack;
            _progression = _persistence.ProgressionService;
            Require(!_persistence.enabled && _warehouse != null && _routes?.Length >= 3
                    && _processor != null && _packing != null && _carry != null
                    && _progression?.SalePoint != null,
                "M13 runtime references are incomplete.");
            M10ProgressionSaveData progressionState =
                M10ProgressionSaveData.CreateFresh();
            progressionState.flags[(int)ProgressFlagId.LumberCampCompleted].value = true;
            progressionState.metrics[(int)ProgressMetricId.DrillUnlocked].value = 1L;
            progressionState.metrics[(int)ProgressMetricId.ToolWorkshopUnlocked].value = 1L;
            progressionState.metrics[(int)ProgressMetricId.ToolsProduced].value = 1L;
            _progression.RestorePersistentState(progressionState);
            _processor.gameObject.SetActive(true);
            _packing.gameObject.SetActive(true);
            Require(_warehouse.gameObject.activeSelf
                    && _progression.EmpireLevel >= 6,
                $"Warehouse did not unlock from the level-5 prerequisite state: "
                + $"active={_warehouse.gameObject.activeSelf}, "
                + $"level={_progression.EmpireLevel}, "
                + $"metric={_progression.GetMetric(ProgressMetricId.WarehouseUnlocked)}.");
            _carry.RestoreStableState(null, 0);
            _processor.RestoreStableState(0, 2);
            _packing.RestoreStableState(0, 0);
            _warehouse.RestoreStableState(
                new int[7], new[] { 60, 60, 60, 60, 60, 60, 60 });
            for (int i = 0; i < _routes.Length; i++) _routes[i].SetRouteEnabled(false);
        }

        private static void TransferToConsumer()
        {
            _routes[0].SetRouteEnabled(true);
            _routes[0].Tick(2f);
            _routes[0].Tick(2f);
            Require(_processor.OutputPlanks == 0
                    && _warehouse.GetAmount(ResourceType.Plank) == 2,
                $"Producer-to-Warehouse route ownership mismatch: "
                + $"processor={_processor.OutputPlanks}, "
                + $"warehouse={_warehouse.GetAmount(ResourceType.Plank)}, "
                + $"state={_routes[0].State}.");
            _routes[1].SetRouteEnabled(true);
            _routes[1].Tick(2f);
            _routes[1].Tick(2f);
            Require(_warehouse.GetAmount(ResourceType.Plank) == 0
                    && _packing.InputPlanks + _packing.ProcessingInputPlanks == 2,
                "Warehouse-to-consumer route lost or duplicated Planks.");
            Require(_progression.GetMetric(ProgressMetricId.PlanksTransported) >= 4L
                    && _progression.GetMetric(ProgressMetricId.IndustrialRouteOperational) == 1L,
                "Route commits did not reach authoritative M10 metrics.");
        }

        private static void RunRetentionContractEmpireSmoke()
        {
            const long now = 2000000000L;
            var metrics = new Dictionary<ProgressMetricId, long>();
            int dailyCompleted = 0;
            int reward = 0;
            var goals = new RetentionGoalModel(
                M13RetentionSaveData.CreateFresh(now),
                id => metrics.TryGetValue(id, out long value) ? value : 0L,
                cash => { reward += cash; return true; },
                () => dailyCompleted++);
            goals.Evaluate(now, new GoalUnlockContext(true, true, false, false, false, true));
            M13GoalSaveRecord daily = goals.Daily.goals[0];
            metrics[daily.metric] = daily.baseline + daily.target;
            goals.Evaluate(now + 1L, new GoalUnlockContext(true, true, false, false, false, true));
            Require(daily.completed && goals.TryClaim(RetentionGoalPeriod.Daily, 0)
                    && !goals.TryClaim(RetentionGoalPeriod.Daily, 0)
                    && dailyCompleted == 1 && reward == daily.rewardCash,
                "Daily goal did not progress and claim exactly once.");

            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            state.flags[(int)ProgressFlagId.LumberCampCompleted].value = true;
            state.flags[(int)ProgressFlagId.SmelterUnlocked].value = true;
            state.metrics[(int)ProgressMetricId.DrillUnlocked].value = 1;
            state.metrics[(int)ProgressMetricId.ToolWorkshopUnlocked].value = 1;
            state.metrics[(int)ProgressMetricId.ToolsProduced].value = 1;
            state.activeContractIndex = 10;
            state.activeContractState = ContractProgressState.Locked;
            for (int i = 0; i < 10; i++) state.claimedContracts[i] = true;
            var progression = new LumberCampProgressionModel(state, _ => true);
            var levels = new List<int>();
            progression.EmpireLevelAdvanced += levels.Add;
            progression.RecordWarehouseUnlocked();
            progression.RecordIndustrialTransport(ResourceType.Plank, 1);
            progression.RecordDailyGoalCompleted();
            progression.RecordIndustrialTransport(ResourceType.IronBar, 10);
            Require(progression.TryClaimActiveContract()
                    && progression.EmpireLevel == 10
                    && levels.Count == 5,
                "Logistics contract and Empire 6-10 smoke did not complete.");
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (SessionState.GetBool(Running, false)
                && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            {
                SessionState.SetInt(ErrorCount, SessionState.GetInt(ErrorCount, 0) + 1);
            }
        }

        private static void Pass()
        {
            SessionState.SetBool(Success, true);
            SessionState.SetString(Message,
                "Producer → Warehouse → route → consumer → sale + Daily Goal + logistics contract + Empire PASS.");
            Stop();
        }

        private static void Fail(string message)
        {
            SessionState.SetBool(Success, false);
            SessionState.SetString(Message, message);
            Stop();
        }

        private static void Stop()
        {
            SessionState.SetBool(Running, false);
            SessionState.SetBool(FinishPending, true);
            EditorApplication.update -= Update;
            EditorApplication.ExitPlaymode();
        }

        private static void Complete()
        {
            EditorApplication.delayCall -= Complete;
            if (!SessionState.GetBool(FinishPending, false)) return;
            bool success = SessionState.GetBool(Success, false)
                           && SessionState.GetInt(ErrorCount, 0) == 0;
            string message = SessionState.GetString(Message, "M13 smoke produced no result.");
            bool commandLine = SessionState.GetBool(CommandLine, false);
            SessionState.SetBool(FinishPending, false);
            if (success) Debug.Log("M13 full PlayMode smoke PASS: " + message);
            else Debug.LogError("M13 full PlayMode smoke FAILED: " + message);
            if (commandLine) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
