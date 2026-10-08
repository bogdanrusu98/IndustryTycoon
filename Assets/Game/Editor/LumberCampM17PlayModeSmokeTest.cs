using System;
using IndustryTycoon.Logistics;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    [InitializeOnLoad]
    public static class LumberCampM17PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string Prefix = "IndustryTycoon.M17.Smoke.";
        private const string Running = Prefix + "Running";
        private const string CommandLine = Prefix + "CommandLine";
        private const string FinishPending = Prefix + "FinishPending";
        private const string Success = Prefix + "Success";
        private const string Message = Prefix + "Message";
        private const string ErrorCount = Prefix + "ErrorCount";
        private static bool _executed;

        static LumberCampM17PlayModeSmokeTest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= Update;
            if (SessionState.GetBool(Running, false)) EditorApplication.update += Update;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            if (SessionState.GetBool(FinishPending, false))
                EditorApplication.delayCall += Complete;
        }

        [MenuItem("Industry Tycoon/Prototype/Run M17 Full Progression Smoke Test")]
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
                throw new InvalidOperationException("Exit Play Mode before M17 smoke.");
            if (!commandLine && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            LocalPersistenceService persistence = Object.FindAnyObjectByType<LocalPersistenceService>(
                FindObjectsInactive.Include);
            if (persistence == null) throw new InvalidOperationException("Missing persistence service.");
            persistence.enabled = false;
            SessionState.SetBool(Running, true);
            SessionState.SetBool(CommandLine, commandLine);
            SessionState.SetBool(FinishPending, false);
            SessionState.SetBool(Success, false);
            SessionState.SetString(Message, string.Empty);
            SessionState.SetInt(ErrorCount, 0);
            _executed = false;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode
                && SessionState.GetBool(FinishPending, false))
                EditorApplication.delayCall += Complete;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(Running, false)
                || !EditorApplication.isPlaying
                || _executed) return;
            _executed = true;
            try
            {
                RunSmoke();
                Pass("fresh → Lumber → Mining → Tools → Logistics → Industrial → "
                     + "Machine Parts → Specialization → Market → Empire 25 PASS");
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private static void RunSmoke()
        {
            LocalPersistenceService persistence = Object.FindAnyObjectByType<LocalPersistenceService>(
                FindObjectsInactive.Include);
            WorldProgressionPresentation world = Object.FindAnyObjectByType<WorldProgressionPresentation>(
                FindObjectsInactive.Include);
            NextUnlockGuidance guidance = Object.FindAnyObjectByType<NextUnlockGuidance>(
                FindObjectsInactive.Include);
            Require(persistence != null && !persistence.enabled && world != null && guidance != null,
                "M17 runtime graph is incomplete or persistence was not isolated.");
            LumberCampProgressionService progression = persistence.ProgressionService;
            IndustrialDistrictService district = persistence.IndustrialDistrictService;
            CentralWarehouse warehouse = persistence.CentralWarehouse;
            Require(progression != null && district != null && warehouse != null,
                "M17 authoritative services are missing.");
            Require(progression.EmpireLevel == 1 && !warehouse.gameObject.activeSelf
                    && !district.IsGateOpen,
                "Fresh world did not begin with Logistics and Industrial content locked.");

            // Runtime auto-activation deterministically advances Warehouse 5→6
            // and Market 20→21, so checkpoint their settled presentation states.
            foreach (int level in new[] { 6, 10, 15, 21, 25 })
            {
                BuildCheckpoint(level, out M10ProgressionSaveData state,
                    out M15FactorySaveData factory);
                progression.RestorePersistentState(state);
                progression.SynchronizeIndustrialContext(
                    M14IndustrialSaveData.CreateFresh(), factory, false);
                world.Refresh();
                guidance.Refresh();
                Require(progression.EmpireLevel == level,
                    $"Checkpoint {level} restored as {progression.EmpireLevel}.");
                Require(!string.IsNullOrWhiteSpace(guidance.DisplayText),
                    $"Checkpoint {level} has no primary guidance.");
                int activeMarkers = 0;
                WorldGuidanceMarker[] markers = Object.FindObjectsByType<WorldGuidanceMarker>(
                    FindObjectsInactive.Include);
                for (int i = 0; i < markers.Length; i++)
                    if (markers[i].gameObject.activeInHierarchy) activeMarkers++;
                Require(markers.Length == 8 && activeMarkers == 1,
                    $"Checkpoint {level} exposed {activeMarkers} recommended markers.");
            }

            Require(warehouse.gameObject.activeSelf && district.IsGateOpen
                    && progression.EmpireLevel == 25
                    && guidance.DisplayText.Contains("LEVEL 25"),
                "Completed world did not restore open gates and completion guidance.");
        }

        private static void BuildCheckpoint(int level, out M10ProgressionSaveData state,
            out M15FactorySaveData factory)
        {
            state = M10ProgressionSaveData.CreateFresh();
            factory = M15FactorySaveData.CreateFresh();
            if (level >= 2)
            {
                foreach (ProgressFlagId flag in new[]
                         {
                             ProgressFlagId.ProductionUpgradeUnlocked,
                             ProgressFlagId.WorkerUnlocked,
                             ProgressFlagId.ProcessorUnlocked,
                             ProgressFlagId.AutoFeederUnlocked,
                             ProgressFlagId.PackingStationUnlocked,
                             ProgressFlagId.CourierUnlocked,
                             ProgressFlagId.LumberCampCompleted
                         })
                    state.flags[(int)flag].value = true;
                state.metrics[(int)ProgressMetricId.PlanksProduced].value = 10;
                state.metrics[(int)ProgressMetricId.CratesProduced].value = 5;
                state.metrics[(int)ProgressMetricId.CourierTripsCompleted].value = 5;
            }
            if (level >= 3) Set(state, ProgressMetricId.DrillUnlocked);
            if (level >= 3)
            {
                state.flags[(int)ProgressFlagId.SmelterUnlocked].value = true;
                state.metrics[(int)ProgressMetricId.IronOreMined].value = 10;
                state.metrics[(int)ProgressMetricId.IronBarsProduced].value = 5;
            }
            if (level >= 4) Set(state, ProgressMetricId.ToolWorkshopUnlocked);
            if (level >= 5) state.metrics[(int)ProgressMetricId.ToolsProduced].value = 5;
            if (level >= 6) Set(state, ProgressMetricId.WarehouseUnlocked);
            if (level >= 7) Set(state, ProgressMetricId.IndustrialRouteOperational);
            if (level >= 8) Set(state, ProgressMetricId.DailyGoalsCompleted);
            if (level >= 9) Set(state, ProgressMetricId.CrossIndustryLogisticsContractsCompleted);
            if (level >= 10) Set(state, ProgressMetricId.IndustrialDistrictReady);
            if (level >= 11) Set(state, ProgressMetricId.IndustrialDistrictEntered);
            if (level >= 12) Set(state, ProgressMetricId.AssemblyPlantUnlocked);
            if (level >= 13) Set(state, ProgressMetricId.MachinePartsProduced);
            if (level >= 14) Set(state, ProgressMetricId.ProductionUpgradesPurchased);
            if (level >= 15) Set(state, ProgressMetricId.AdvancedManufacturingContractsCompleted);
            if (level >= 16) M15FactoryRules.TryChoose(factory,
                "wood_processor", FactorySpecialization.Throughput);
            if (level >= 18) M15FactoryRules.TryChoose(factory,
                "iron_smelter", FactorySpecialization.Efficiency);
            if (level >= 19) factory.optimizationObjectiveCompleted = true;
            if (level >= 20)
            {
                M15FactoryRules.TryChoose(factory,
                    "tool_workshop", FactorySpecialization.Throughput);
                factory.GetFactory("wood_processor").completedSpecializedCycles = 25;
                factory.GetFactory("iron_smelter").completedSpecializedCycles = 25;
                factory.upgradedRouteUnitsTransported = 50;
                M15FactoryRules.RecalculateDistrictLevel(factory);
            }
            if (level >= 21) Set(state, ProgressMetricId.MarketUnlocked);
            if (level >= 22) Set(state, ProgressMetricId.ProductionOrdersCompleted);
            if (level >= 23) Set(state, ProgressMetricId.HighBoomSales);
            if (level >= 24) Set(state, ProgressMetricId.FlowBonusesTriggered);
            if (level >= 25) Set(state, ProgressMetricId.AdvancedMachinePartOrdersCompleted);
        }

        private static void Set(M10ProgressionSaveData state, ProgressMetricId metric)
        {
            state.metrics[(int)metric].value = 1;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (SessionState.GetBool(Running, false)
                && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                SessionState.SetInt(ErrorCount, SessionState.GetInt(ErrorCount, 0) + 1);
        }

        private static void Pass(string message)
        {
            SessionState.SetBool(Success, true);
            SessionState.SetString(Message, message);
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
            string message = SessionState.GetString(Message, "M17 smoke produced no result.");
            bool commandLine = SessionState.GetBool(CommandLine, false);
            SessionState.SetBool(FinishPending, false);
            if (success) Debug.Log("M17 PlayMode smoke PASS: " + message);
            else Debug.LogError("M17 PlayMode smoke FAILED: " + message);
            if (commandLine) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
