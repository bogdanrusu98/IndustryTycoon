using System;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Progression;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    [InitializeOnLoad]
    public static class LumberCampM18PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string Prefix = "IndustryTycoon.M18.Smoke.";
        private const string Running = Prefix + "Running";
        private const string CommandLine = Prefix + "CommandLine";
        private const string Pending = Prefix + "Pending";
        private const string Success = Prefix + "Success";
        private const string Message = Prefix + "Message";
        private const string Errors = Prefix + "Errors";
        private static bool _executed;

        static LumberCampM18PlayModeSmokeTest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= Update;
            if (SessionState.GetBool(Running, false)) EditorApplication.update += Update;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            if (SessionState.GetBool(Pending, false)) EditorApplication.delayCall += Complete;
        }

        [MenuItem("Industry Tycoon/Prototype/Run M18 Onboarding Smoke Test")]
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
                throw new InvalidOperationException("Exit Play Mode before M18 smoke.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            LocalPersistenceService persistence = Object.FindAnyObjectByType<LocalPersistenceService>(
                FindObjectsInactive.Include);
            Require(persistence != null, "Missing persistence service.");
            persistence.enabled = false;
            SessionState.SetBool(Running, true);
            SessionState.SetBool(CommandLine, commandLine);
            SessionState.SetBool(Pending, false);
            SessionState.SetBool(Success, false);
            SessionState.SetString(Message, string.Empty);
            SessionState.SetInt(Errors, 0);
            _executed = false;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.EnterPlaymode();
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
                Stop(true, "fresh/resume/no-replay/M17 handoff/input/marker cleanup PASS");
            }
            catch (Exception exception)
            {
                Stop(false, exception.Message);
            }
        }

        private static void RunSmoke()
        {
            FirstSessionOnboardingService onboarding =
                Object.FindAnyObjectByType<FirstSessionOnboardingService>();
            NextUnlockGuidance guidance = Object.FindAnyObjectByType<NextUnlockGuidance>();
            LocalPersistenceService persistence = Object.FindAnyObjectByType<LocalPersistenceService>(
                FindObjectsInactive.Include);
            PlayerDragInput[] inputs = Object.FindObjectsByType<PlayerDragInput>(
                FindObjectsInactive.Include);
            Require(onboarding != null && guidance != null && persistence != null,
                "M18 runtime graph is incomplete.");
            Require(onboarding.IsTutorialActive
                    && onboarding.CurrentStep == FirstSessionStep.MoveToWood
                    && onboarding.TargetMarker.activeInHierarchy
                    && guidance.DisplayText.Contains("DRAG TO MOVE"),
                "Fresh save did not start with one contextual movement instruction.");
            Require(inputs.Length == 1 && inputs[0].isActiveAndEnabled,
                "One-finger PlayerDragInput changed or duplicated.");

            var checkpoint = M18OnboardingSaveData.CreateFresh();
            checkpoint.tutorialStarted = true;
            checkpoint.completedStepsMask = 0b111;
            checkpoint.currentStep = 3;
            onboarding.RestorePersistentState(checkpoint);
            guidance.Refresh();
            Require(onboarding.CurrentStep == FirstSessionStep.UnlockProduction
                    && guidance.DisplayText.Contains("PRODUCTION UPGRADE"),
                "Tutorial did not resume at an exact mid-session checkpoint.");

            int completions = 0;
            onboarding.StepCompleted += (_, __) => completions++;
            PurchaseAll(persistence.Wallet, persistence.ProductionUpgrade.PurchasePad);
            Require(completions == 1
                    && onboarding.CurrentStep == FirstSessionStep.UnlockWorker,
                "Legitimate Production unlock did not advance exactly once.");
            persistence.ProgressionService.EvaluateCurrentState();
            Require(completions == 1, "Completed tutorial step replayed after evaluation.");

            M10ProgressionSaveData complete = M10ProgressionSaveData.CreateFresh();
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
                complete.flags[(int)flag].value = true;
            complete.metrics[(int)ProgressMetricId.WoodCollected].value = 1;
            complete.metrics[(int)ProgressMetricId.WoodSold].value = 1;
            complete.metrics[(int)ProgressMetricId.PlanksProduced].value = 10;
            complete.metrics[(int)ProgressMetricId.CratesProduced].value = 5;
            persistence.ProgressionService.RestorePersistentState(complete);
            onboarding.RestorePersistentState(M18OnboardingSaveData.CreateFresh());
            guidance.Refresh();
            Require(onboarding.IsTutorialComplete
                    && !onboarding.TargetMarker.activeSelf
                    && guidance.DisplayText.StartsWith("OBJECTIVE:", StringComparison.Ordinal),
                "Completed onboarding did not hand control back to M17 guidance.");

            onboarding.gameObject.SetActive(false);
            Require(!onboarding.TargetMarker.activeSelf,
                "Onboarding marker leaked after its owner was disabled.");
            onboarding.gameObject.SetActive(true);
            Require(!onboarding.TargetMarker.activeSelf,
                "Completed onboarding marker reappeared after re-enable.");
        }

        private static void PurchaseAll(IndustryTycoon.Economy.Wallet wallet,
            IndustryTycoon.Interaction.PurchasePad pad)
        {
            Require(wallet != null && pad != null && pad.IsAvailable,
                "Expected purchase pad is not legitimately available.");
            wallet.Deposit(pad.RemainingCost);
            int guard = 0;
            while (!pad.IsCompleted && guard++ < 1024) pad.ProcessPaymentStep();
            Require(pad.IsCompleted, "Purchase did not complete.");
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode
                && SessionState.GetBool(Pending, false))
                EditorApplication.delayCall += Complete;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (SessionState.GetBool(Running, false)
                && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                SessionState.SetInt(Errors, SessionState.GetInt(Errors, 0) + 1);
        }

        private static void Stop(bool success, string message)
        {
            SessionState.SetBool(Success, success);
            SessionState.SetString(Message, message);
            SessionState.SetBool(Running, false);
            SessionState.SetBool(Pending, true);
            EditorApplication.update -= Update;
            EditorApplication.ExitPlaymode();
        }

        private static void Complete()
        {
            EditorApplication.delayCall -= Complete;
            if (!SessionState.GetBool(Pending, false)) return;
            bool success = SessionState.GetBool(Success, false)
                           && SessionState.GetInt(Errors, 0) == 0;
            string message = SessionState.GetString(Message, "M18 smoke produced no result.");
            bool commandLine = SessionState.GetBool(CommandLine, false);
            SessionState.SetBool(Pending, false);
            if (success) Debug.Log("M18 PlayMode smoke PASS: " + message);
            else Debug.LogError("M18 PlayMode smoke FAILED: " + message);
            if (commandLine) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
