using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Core;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Progression;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IndustryTycoon.Editor
{
    /// <summary>Focused 9:16 evidence and multi-resolution audit for UI Visual Rebuild Pass 1.</summary>
    [InitializeOnLoad]
    public static class IndustrialPlaycraftCapture
    {
        private const string SessionKey = "IndustryTycoon.IndustrialPlaycraftCapture";
        private const string OutputFolder = "Documentation/UIVisualPass1/after";
        private static readonly string[] ShotNames =
        {
            "01-early-lumber",
            "02-active-carry-stack",
            "03-active-primary-objective",
            "04-later-empire-progression"
        };

        private static int _frames;
        private static int _shot;
        private static Camera _camera;
        private static Canvas _canvas;
        private static RenderTexture _target;
        private static Texture2D _pixels;

        static IndustrialPlaycraftCapture()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                EditorApplication.update += Tick;
            }

            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode
                    && SessionState.GetBool(SessionKey + ".Done", false))
                {
                    bool failed = SessionState.GetBool(SessionKey + ".Failed", false);
                    SessionState.SetBool(SessionKey + ".Done", false);
                    EditorApplication.delayCall += () => EditorApplication.Exit(failed ? 1 : 0);
                }
            };
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity");
            UnityEngine.Object.FindAnyObjectByType<LocalPersistenceService>(FindObjectsInactive.Include).enabled = false;
            Directory.CreateDirectory(OutputFolder);
            SessionState.SetBool(SessionKey, true);
            SessionState.SetBool(SessionKey + ".Done", false);
            SessionState.SetBool(SessionKey + ".Failed", false);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        public static void ApplyCoreHudFromCommandLine()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity");
            ModernPresentationBuilder.ApplyCoreHud(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Industrial Playcraft core HUD applied to the existing prototype scene.");
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            try
            {
                _frames++;
                if (_frames == 30)
                {
                    Initialize();
                }
                if (_frames < 40)
                {
                    return;
                }

                if ((_frames - 40) % 18 == 0)
                {
                    SetupShot(_shot);
                }
                if ((_frames - 40) % 18 == 17)
                {
                    Capture(ShotNames[_shot]);
                    _shot++;
                    if (_shot == ShotNames.Length)
                    {
                        ValidateMobileLayouts();
                        Stop(false);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Stop(true);
            }
        }

        private static void Initialize()
        {
            _camera = Camera.main;
            _canvas = Find("HUD").GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _camera;
            _canvas.planeDistance = 1f;
            UnityEngine.Object.FindAnyObjectByType<SafeAreaFitter>().enabled = false;
            Resize(540, 960, new Rect(0, 0, 540, 960), true);
            ModernVisualCapture.ApplyFixture(false);
        }

        private static void SetupShot(int index)
        {
            UnityEngine.Object.FindAnyObjectByType<MobilePanelNavigation>().CloseAll();
            Resize(540, 960, new Rect(0, 0, 540, 960), true);
            if (index == 0)
            {
                ModernVisualCapture.ApplyFixture(false);
            }
            else if (index == 1)
            {
                ModernVisualCapture.ApplyFixture(false);
                UnityEngine.Object.FindAnyObjectByType<CarryStack>().RestoreStableState(ResourceType.Wood, 8);
            }
            else if (index == 2)
            {
                ApplyObjectiveFixture();
            }
            else
            {
                ModernVisualCapture.ApplyFixture(true);
            }

            Vector3 position = index == 3 ? new Vector3(12f, 0f, 25f) : new Vector3(0f, 0f, -1f);
            GameObject player = Find("Player");
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
            _camera.GetComponent<SmoothFollowCamera>().SnapToTarget();

            foreach (string transient in new[]
                     {
                         "Welcome Back Overlay", "Major Unlock Banner",
                         "Achievement Toast", "Completion Banner", "Transaction Toast"
                     })
            {
                Find(transient)?.SetActive(false);
            }

            Canvas.ForceUpdateCanvases();
        }

        private static void ApplyObjectiveFixture()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            M9SaveData data = M9SaveData.CreateFresh(now);
            SetFlag(data.progression, ProgressFlagId.WorkerUnlocked);
            SetFlag(data.progression, ProgressFlagId.ProcessorUnlocked);
            SetMetric(data.progression, ProgressMetricId.PlanksProduced, 6L);
            data.onboarding.tutorialStarted = true;
            data.onboarding.tutorialSkipped = true;
            data.onboarding.currentStep = M18OnboardingRules.StepCount;
            data.onboarding.completedStepsMask = M18OnboardingRules.AllStepsMask;

            LocalPersistenceService persistence = UnityEngine.Object.FindAnyObjectByType<LocalPersistenceService>(FindObjectsInactive.Include);
            typeof(LocalPersistenceService)
                .GetMethod("ApplyStableState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(persistence, new object[] { data, now });
        }

        private static void SetMetric(M10ProgressionSaveData data, ProgressMetricId metric, long value)
        {
            string id = LumberCampProgressionCatalog.GetMetricStableId(metric);
            M10MetricSaveRecord record = data.metrics.First(item => item.id == id);
            record.value = value;
        }

        private static void SetFlag(M10ProgressionSaveData data, ProgressFlagId flag)
        {
            string id = LumberCampProgressionCatalog.GetFlagStableId(flag);
            M10FlagSaveRecord record = data.flags.First(item => item.id == id);
            record.value = true;
        }

        private static void Resize(int width, int height, Rect safeArea, bool allocatePixels)
        {
            if (_target != null)
            {
                _camera.targetTexture = null;
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
            }
            if (_pixels != null)
            {
                UnityEngine.Object.DestroyImmediate(_pixels);
                _pixels = null;
            }

            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            _target.Create();
            _camera.targetTexture = _target;
            if (allocatePixels)
            {
                _pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            }

            RectTransform safe = (RectTransform)_canvas.transform.Find("Safe Area");
            SafeAreaFitter.CalculateAnchors(
                safeArea,
                new Vector2Int(width, height),
                out Vector2 minimum,
                out Vector2 maximum);
            safe.anchorMin = minimum;
            safe.anchorMax = maximum;
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
        }

        private static void Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            _camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _pixels.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            _pixels.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(OutputFolder, name + ".png"), _pixels.EncodeToPNG());
        }

        private static void ValidateMobileLayouts()
        {
            var rows = new List<string>
            {
                "Industrial Playcraft Pass 1 mobile layout audit",
                "schema=" + M9SaveSchema.CurrentVersion,
                "large-number=" + WalletHud.FormatAmount(int.MaxValue)
            };
            var cases = new[]
            {
                (width: 360, height: 640, safe: new Rect(0, 20, 360, 592)),
                (width: 1080, height: 1920, safe: new Rect(0, 64, 1080, 1760)),
                (width: 1440, height: 3200, safe: new Rect(0, 96, 1440, 2944))
            };

            foreach (var layout in cases)
            {
                Resize(layout.width, layout.height, layout.safe, false);
                Canvas.ForceUpdateCanvases();
                RequireInside(Find("CASH Chip").GetComponent<RectTransform>(), layout.width, layout.height);
                RequireInside(Find("CARRY Chip").GetComponent<RectTransform>(), layout.width, layout.height);
                RequireInside(Find("EMPIRE Chip").GetComponent<RectTransform>(), layout.width, layout.height);
                RequireInside(Find("Main Objective").GetComponent<RectTransform>(), layout.width, layout.height);
                RectTransform pause = Find("Pause Button").GetComponent<RectTransform>();
                RequireInside(pause, layout.width, layout.height);
                if (pause.rect.width < MobileUiTheme.MinimumTouchTarget
                    || pause.rect.height < MobileUiTheme.MinimumTouchTarget)
                {
                    throw new InvalidOperationException("Pause touch target is below the design-system minimum.");
                }

                rows.Add($"{layout.width}x{layout.height}|safe={layout.safe}|HUD=PASS|touch=PASS");
            }

            rows.Add("android-back=owned-by-MobilePanelNavigation");
            File.WriteAllLines("Documentation/UIVisualPass1/mobile-validation.txt", rows);
        }

        private static void RequireInside(RectTransform rect, int width, int height)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(_camera, corners[i]);
                if (point.x < -1f || point.x > width + 1f || point.y < -1f || point.y > height + 1f)
                {
                    throw new InvalidOperationException($"{rect.name} escapes {width}x{height}: {point}");
                }
            }
        }

        private static GameObject Find(string name)
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(item => item.gameObject)
                .FirstOrDefault(item => item.name == name);
        }

        private static void Stop(bool failed)
        {
            SessionState.SetBool(SessionKey, false);
            SessionState.SetBool(SessionKey + ".Done", true);
            SessionState.SetBool(SessionKey + ".Failed", failed);
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
        }
    }
}
