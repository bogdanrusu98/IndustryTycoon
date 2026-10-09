using System.Collections;
using System.Linq;
using IndustryTycoon.Monetization;
using IndustryTycoon.Persistence;
using IndustryTycoon.Progression;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM22PlayModeTests
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";

        [UnityTest]
        public IEnumerator RuntimeGraph_SoakMetricsLifecycleAndMissingScriptsPass()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            for (int i = 0; i < 30; i++) yield return null;

            LocalPersistenceService[] persistence = Object.FindObjectsByType<LocalPersistenceService>(
                FindObjectsInactive.Include);
            MonetizationService[] monetization = Object.FindObjectsByType<MonetizationService>(
                FindObjectsInactive.Include);
            LumberCampProgressionService[] progression =
                Object.FindObjectsByType<LumberCampProgressionService>(
                    FindObjectsInactive.Include);
            Assert.AreEqual(1, persistence.Length, "Duplicate persistence manager.");
            Assert.AreEqual(1, monetization.Length, "Duplicate monetization manager.");
            Assert.AreEqual(1, progression.Length, "Duplicate progression manager.");

            int missingScripts = 0;
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject root in roots)
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transform.gameObject);
            Assert.AreEqual(0, missingScripts, "Scene contains missing scripts.");

            long startManaged = Profiler.GetMonoUsedSizeLong();
            double totalMs = 0d;
            double maxMs = 0d;
            long maxGcFrame = 0L;
            int samples = 0;
            using (ProfilerRecorder gc = ProfilerRecorder.StartNew(
                       ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                for (int i = 0; i < 600; i++)
                {
                    yield return null;
                    double milliseconds = Time.unscaledDeltaTime * 1000d;
                    totalMs += milliseconds;
                    maxMs = System.Math.Max(maxMs, milliseconds);
                    if (gc.Valid) maxGcFrame = System.Math.Max(maxGcFrame, gc.LastValue);
                    samples++;
                }
            }
            long endManaged = Profiler.GetMonoUsedSizeLong();
            long growth = endManaged - startManaged;
            double averageMs = samples > 0 ? totalMs / samples : 0d;
            Assert.LessOrEqual(growth, 8L * 1024L * 1024L,
                "Managed memory grew without a practical short-soak bound.");
            Assert.Less(averageMs, 33.34d,
                "Editor PlayMode average frame exceeded the 30 FPS smoke bound.");
            Debug.Log($"M22_PERF editor-playmode frames={samples}, avg={averageMs:F2}ms, "
                      + $"max={maxMs:F2}ms, max-gc-frame={maxGcFrame}B, "
                      + $"managed-growth={growth}B, objects={Resources.FindObjectsOfTypeAll<GameObject>().Count()}");
            yield return new ExitPlayMode();
        }
    }
}
