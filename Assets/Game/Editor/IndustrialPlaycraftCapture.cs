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
        private static string OutputFolder => SessionState.GetString(SessionKey + ".Folder", "Documentation/UIHudComposition/initial");
        private static readonly string[] ShotNames =
        {
            "01-early-lumber",
            "02-active-carry-stack",
            "03-active-primary-objective",
            "04-later-empire-progression",
            "05-carry-full",
            "06-wrong-resource",
            "07-pickup"
        };

        private static int _frames;
        private static int _shot;
        private static double _captureAt;
        private static Camera _camera;
        private static Canvas _canvas;
        private static RenderTexture _target;
        private static Texture2D _pixels;
        private static Rect _safeRect;

        static IndustrialPlaycraftCapture()
        {
            Application.logMessageReceived += (_, __, type) =>
            {
                if ((SessionState.GetBool(SessionKey, false) || SessionState.GetBool(SessionKey + ".Done", false))
                    && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                    SessionState.SetBool(SessionKey + ".Failed", true);
            };
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
            _frames = _shot = 0;
            _captureAt = 0;
            string[] args = Environment.GetCommandLineArgs();
            int iteration = Array.IndexOf(args, "-visualIteration");
            SessionState.SetString(SessionKey + ".Folder", "Documentation/UIHudComposition/" + (iteration >= 0 ? args[iteration + 1] : "initial"));
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

        public static void RebuildAndRun()
        {
            IndustrialHudAssets.Ensure(true);
            ApplyCoreHudFromCommandLine();
            Run();
        }

        public static void ApplyCoreHudFromCommandLine()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity");
            ModernPresentationBuilder.ApplyCoreHud(scene);
            var follow=UnityEngine.Object.FindAnyObjectByType<SmoothFollowCamera>();
            var settings=new SerializedObject(follow);
            settings.FindProperty("offset").vector3Value=ModernWorldComposition.CameraOffset;
            settings.FindProperty("lookAtOffset").vector3Value=ModernWorldComposition.CameraLookAtOffset;
            settings.ApplyModifiedPropertiesWithoutUndo();
            follow.SnapToTarget();
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

                if (_captureAt == 0)
                {
                    SetupShot(_shot);
                    _captureAt = EditorApplication.timeSinceStartup + 0.45;
                }
                if (EditorApplication.timeSinceStartup >= _captureAt)
                {
                    Capture(ShotNames[_shot]);
                    _captureAt = 0;
                    _shot++;
                    if (_shot == ShotNames.Length)
                    {
                        ValidateHudContracts();
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
            Resize(1080, 1920, new Rect(0, 0, 1080, 1920), true);
            ModernVisualCapture.ApplyFixture(false);
        }

        private static void SetupShot(int index)
        {
            UnityEngine.Object.FindAnyObjectByType<MobilePanelNavigation>().CloseAll();
            Resize(1080, 1920, new Rect(0, 0, 1080, 1920), true);
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
            else if (index == 3)
            {
                ModernVisualCapture.ApplyFixture(true);
                UnityEngine.Object.FindAnyObjectByType<CarryStack>().RestoreStableState(ResourceType.Wood, 1);
            }
            else
            {
                ModernVisualCapture.ApplyFixture(false);
                var carry = UnityEngine.Object.FindAnyObjectByType<CarryStack>();
                carry.RestoreStableState(ResourceType.Wood, index == 4 ? carry.Capacity : 5);
            }

            // Fixture transitions are not player unlocks: clear the prior fixture's queued notifications.
            var unlockToast = UnityEngine.Object.FindAnyObjectByType<MajorUnlockToastView>();
            unlockToast.enabled = false;
            unlockToast.enabled = true;
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
            if (index >= 4)
            {
                var guidance = UnityEngine.Object.FindAnyObjectByType<NextUnlockGuidance>();
                string objective = guidance.DisplayText;
                var onboarding = UnityEngine.Object.FindAnyObjectByType<FirstSessionOnboardingService>();
                onboarding.ShowContextHint(index == 4 ? "CARRY FULL — SELL OR DELIVER" : index == 5 ? "WRONG RESOURCE — NEED PLANKS" : string.Empty, 20);
                if (guidance.DisplayText != objective)
                    throw new InvalidOperationException("Warning replaced primary objective.");
                if (index == 6) UnityEngine.Object.FindAnyObjectByType<CarryStack>().TryAdd(ResourceType.Wood, 1);
            }
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
            _safeRect = safeArea;
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
                (width: 1440, height: 3200, safe: new Rect(40, 96, 1360, 2944))
            };

            foreach (var layout in cases)
            {
                Resize(layout.width, layout.height, layout.safe, false);
                Canvas.ForceUpdateCanvases();
                RequireInside(Find("CASH Chip").GetComponent<RectTransform>(), layout.width, layout.height);
                RequireInside(Find("CARRY Chip").GetComponent<RectTransform>(), layout.width, layout.height);
                RequireInside(Find("EMPIRE Chip").GetComponent<RectTransform>(), layout.width, layout.height);
                RequireInside(Find("Main Objective").GetComponent<RectTransform>(), layout.width, layout.height);
                RequireInside(Find("Context Hint").GetComponent<RectTransform>(), layout.width, layout.height);
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
            File.WriteAllLines(Path.Combine(OutputFolder, "mobile-validation.txt"), rows);
        }

        private static void RequireInside(RectTransform rect, int width, int height)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(_camera, corners[i]);
                if (point.x < _safeRect.xMin - 1f || point.x > _safeRect.xMax + 1f
                    || point.y < _safeRect.yMin - 1f || point.y > _safeRect.yMax + 1f)
                {
                    throw new InvalidOperationException($"{rect.name} escapes {width}x{height}: {point}");
                }
            }
        }

        private static void ValidateHudContracts()
        {
            var evidence = new List<string>();
            var follow = _camera.GetComponent<SmoothFollowCamera>();
            if ((_camera.fieldOfView != 43f)
                || (follow.Offset - ModernWorldComposition.CameraOffset).sqrMagnitude > .0001f
                || (follow.LookAtOffset - ModernWorldComposition.CameraLookAtOffset).sqrMagnitude > .0001f)
                throw new InvalidOperationException("Portrait camera lost the reviewed framing.");
            Vector3 playerPosition = Find("Player").transform.position;
            Vector3 feet = _camera.WorldToViewportPoint(playerPosition);
            Vector3 head = _camera.WorldToViewportPoint(playerPosition + Vector3.up * 1.8f);
            float projectedHeight = head.y - feet.y;
            if (projectedHeight < .07f || projectedHeight > .16f || feet.y < .3f || feet.y > .6f)
                throw new InvalidOperationException($"Player readability/framing failed: height={projectedHeight}, feet={feet}.");
            evidence.Add($"portrait camera/player readability=PASS (height={projectedHeight:P1}, foot viewport={feet})");
            var carry = UnityEngine.Object.FindAnyObjectByType<CarryStack>();
            var guidance = UnityEngine.Object.FindAnyObjectByType<NextUnlockGuidance>();
            var onboarding = UnityEngine.Object.FindAnyObjectByType<FirstSessionOnboardingService>();
            var chip = Find("CARRY Chip");
            Sprite previous = null;
            foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            {
                if (!carry.RestoreStableState(type, 3))
                    throw new InvalidOperationException("Resource fixture was rejected: " + type);
                Image icon = chip.GetComponentInChildren<ResourceHudIcon>().GetComponent<Image>();
                if (icon.sprite == null || icon.sprite == previous || !icon.enabled)
                    throw new InvalidOperationException("Missing or reused resource artwork: " + type);
                if (!chip.GetComponentsInChildren<Text>().Any(t => t.text == ResourceHudIcon.Label(type)))
                    throw new InvalidOperationException("Incorrect carried resource label: " + type);
                previous = icon.sprite;
            }
            evidence.Add("seven runtime resource types/icons/labels=PASS");
            if (!carry.RestoreStableState(null, 0))
                throw new InvalidOperationException("Empty carry fixture was rejected.");
            if (chip.GetComponent<CanvasGroup>().alpha != 0)
                throw new InvalidOperationException("Empty carry chip is not hidden.");
            if (Find("Wood Text").GetComponent<Text>().text != $"0 / {carry.Capacity}")
                throw new InvalidOperationException("Hidden carry value lost its runtime binding.");
            carry.RestoreStableState(ResourceType.Wood, carry.Capacity);
            if (!Find("Full Badge").activeInHierarchy)
                throw new InvalidOperationException("Full carry badge is absent.");
            evidence.Add("empty carry hidden/full badge=PASS");
            string primary = guidance.DisplayText;
            foreach (string warning in new[] { "CARRY FULL", "WRONG RESOURCE", "NOT ENOUGH CASH" })
            {
                onboarding.ShowContextHint(warning);
                if (guidance.DisplayText != primary || Find("Context Hint").GetComponent<CanvasGroup>().alpha != 1)
                    throw new InvalidOperationException("Warning/objective separation failed: " + warning);
            }
            evidence.Add("M18 objective independent from three warnings=PASS");
            ModernVisualCapture.ApplyFixture(false);
            if (!string.IsNullOrEmpty(onboarding.ContextHint))
                throw new InvalidOperationException("Restore retained a stale contextual hint.");
            var empire = UnityEngine.Object.FindAnyObjectByType<EmpireLevelView>();
            var empireBar = Find("Empire Progress").GetComponent<MobileProgressBar>();
            if (empire.DisplayedLevel != 1 || empireBar.NormalizedValue != 0)
                throw new InvalidOperationException("Fresh Empire display is incorrect.");
            ModernVisualCapture.ApplyFixture(true);
                UnityEngine.Object.FindAnyObjectByType<CarryStack>().RestoreStableState(ResourceType.Wood, 1);
            if (empire.DisplayedLevel != 25 || empireBar.NormalizedValue != 1)
                throw new InvalidOperationException("Completed Empire display is incorrect.");
            evidence.Add("Empire runtime Lv1/empty to Lv25/full; restore hints cleared=PASS");
            var nav = UnityEngine.Object.FindAnyObjectByType<MobilePanelNavigation>();
            nav.CloseAll();
            var pauseButton = Find("Pause Button").GetComponent<Button>();
            var events = UnityEngine.EventSystems.EventSystem.current;
            var pointer = new UnityEngine.EventSystems.PointerEventData(events)
            {
                position = RectTransformUtility.WorldToScreenPoint(_camera, pauseButton.transform.position
                    + pauseButton.transform.TransformVector(new Vector3(0, -59, 0)))
            };
            var hits = new List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(pointer, hits);
            if (!pauseButton.IsInteractable() || !hits.Any(hit => hit.gameObject == pauseButton.gameObject))
                throw new InvalidOperationException("Pause button does not receive pointer raycasts.");
            pauseButton.onClick.Invoke();
            if (!nav.HasBlockingPanel || Find("Wood Counter").activeInHierarchy)
                throw new InvalidOperationException("Pause binding/visibility failed.");
            nav.Back();
            if (nav.HasBlockingPanel || !Find("Wood Counter").activeInHierarchy)
                throw new InvalidOperationException("Back did not restore exploration HUD.");
            evidence.Add("Pause pointer raycast/existing action/Android Back handler/restore HUD=PASS");
            foreach (Image icon in _canvas.GetComponentsInChildren<Image>(true).Where(i => i.name.EndsWith(" Illustration")))
                if (icon.GetComponent<CanvasRenderer>() == null || icon.sprite == null)
                    throw new InvalidOperationException("Emblem missing CanvasRenderer.");
            if (M9SaveSchema.CurrentVersion != 9) throw new InvalidOperationException("Save schema changed.");
            evidence.Add("emblem renderers/save schema v9=PASS");
            File.WriteAllLines(Path.Combine(OutputFolder, "runtime-validation.txt"), evidence);
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
            SessionState.SetBool(SessionKey + ".Failed", failed || SessionState.GetBool(SessionKey + ".Failed", false));
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
        }
    }
}
