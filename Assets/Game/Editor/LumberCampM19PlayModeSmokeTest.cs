using System;
using System.Linq;
using System.Reflection;
using IndustryTycoon.Feedback;
using IndustryTycoon.Logistics;
using IndustryTycoon.Mining;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    [InitializeOnLoad]
    public static class LumberCampM19PlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string Prefix = "IndustryTycoon.M19.Smoke.";
        private const string Running = Prefix + "Running";
        private const string CommandLine = Prefix + "CommandLine";
        private const string FinishPending = Prefix + "FinishPending";
        private const string Success = Prefix + "Success";
        private const string Message = Prefix + "Message";
        private const string ErrorCount = Prefix + "ErrorCount";
        private static bool _executed;
        private static int _warmupFrames;

        static LumberCampM19PlayModeSmokeTest()
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

        [MenuItem("Industry Tycoon/M19/Run Production Visual Smoke Test")]
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
                throw new InvalidOperationException("Exit Play Mode before M19 smoke.");
            if (!commandLine && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
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
            _warmupFrames = 0;
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
                || !EditorApplication.isPlaying || _executed) return;
            if (++_warmupFrames < 4) return;
            _executed = true;
            try
            {
                RunSmoke();
                Pass("URP visuals/resources and active-only Saw/Courier/Drill rotation PASS");
            }
            catch (Exception exception) { Fail(exception.ToString()); }
        }

        private static void RunSmoke()
        {
            Require(M9SaveSchema.CurrentVersion == 9, "Save schema changed from v9.");
            Scene scene = SceneManager.GetActiveScene();
            int missingScripts = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Sum(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transform.gameObject));
            Require(missingScripts == 0, "Scene contains missing scripts.");

            WoodProcessorFeedback saw = Find<WoodProcessorFeedback>();
            WoodProcessor processor = Find<WoodProcessor>();
            Transform blade = GetField<Transform>(saw, "workingBlade");
            Quaternion bladeBefore = blade.localRotation;
            SetField(processor, "_isProcessing", true);
            InvokeUpdate(saw);
            Require(blade.localRotation != bladeBefore, "Saw blade did not rotate while active.");
            Quaternion bladeActive = blade.localRotation;
            SetField(processor, "_isProcessing", false);
            InvokeUpdate(saw);
            Require(blade.localRotation == bladeActive, "Saw blade rotated while idle.");

            CrateCourierFeedback courier = Find<CrateCourierFeedback>();
            Transform[] wheels =
            {
                GetField<Transform>(courier, "leftWheel"),
                GetField<Transform>(courier, "rightWheel"),
                GetField<Transform>(courier, "rearLeftWheel"),
                GetField<Transform>(courier, "rearRightWheel")
            };
            Quaternion[] before = wheels.Select(wheel => wheel.localRotation).ToArray();
            SetField(courier, "_displayedState", CrateCourierState.MoveToPickup);
            InvokeUpdate(courier);
            Require(wheels.Select((wheel, index) => wheel.localRotation != before[index]).All(x => x),
                "Not all Courier wheels rotated while moving.");
            Quaternion[] active = wheels.Select(wheel => wheel.localRotation).ToArray();
            SetField(courier, "_displayedState", CrateCourierState.Wait);
            InvokeUpdate(courier);
            Require(wheels.Select((wheel, index) => wheel.localRotation == active[index]).All(x => x),
                "Courier wheels rotated while stopped.");

            MiningWorldFeedback mining = Find<MiningWorldFeedback>();
            AutomatedDrill drill = Find<AutomatedDrill>();
            Quaternion drillBefore = mining.DrillBit.localRotation;
            SetField(drill, "_state", AutomatedDrillState.Producing);
            InvokeUpdate(mining);
            Require(mining.DrillBit.localRotation != drillBefore,
                "Drill bit did not rotate while producing.");
            Quaternion drillActive = mining.DrillBit.localRotation;
            SetField(drill, "_state", AutomatedDrillState.Idle);
            InvokeUpdate(mining);
            Require(mining.DrillBit.localRotation == drillActive,
                "Drill bit rotated while idle.");
        }

        private static T Find<T>() where T : Component
        {
            T component = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (component == null) throw new InvalidOperationException("Missing " + typeof(T).Name);
            return component;
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            T value = field?.GetValue(target) as T;
            if (value == null) throw new InvalidOperationException("Missing reference " + name);
            return value;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing field " + name);
            field.SetValue(target, value);
        }

        private static void InvokeUpdate(object target)
        {
            MethodInfo update = target.GetType().GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);
            if (update == null) throw new InvalidOperationException("Missing Update on " + target.GetType().Name);
            update.Invoke(target, null);
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
            string message = SessionState.GetString(Message, "M19 smoke produced no result.");
            bool commandLine = SessionState.GetBool(CommandLine, false);
            SessionState.SetBool(FinishPending, false);
            if (success) Debug.Log("M19 PlayMode smoke PASS: " + message);
            else Debug.LogError("M19 PlayMode smoke FAILED: " + message);
            if (commandLine) EditorApplication.Exit(success ? 0 : 1);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
