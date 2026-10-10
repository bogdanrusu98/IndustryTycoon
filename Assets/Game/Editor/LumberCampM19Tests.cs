using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using IndustryTycoon.Feedback;
using IndustryTycoon.Logistics;
using IndustryTycoon.Mining;
using IndustryTycoon.Persistence;
using IndustryTycoon.Processing;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM19Tests
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string ProductionRoot = "Assets/Art/Production";

        [Test]
        public void Schema_RemainsVersion9()
        {
            Assert.AreEqual(9, M9SaveSchema.CurrentVersion);
        }

        [Test]
        public void ProductionImports_AreMobileSafeAndDeterministic()
        {
            string[] models = AssetDatabase.FindAssets("t:Model", new[] { ProductionRoot });
            string[] textures = AssetDatabase.FindAssets("t:Texture2D", new[] { ProductionRoot });
            Assert.AreEqual(18, models.Length, "Unexpected production model inventory.");
            Assert.AreEqual(30, textures.Length, "Unexpected production texture inventory.");

            foreach (string guid in models)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.NotNull(importer, path);
                Assert.IsFalse(importer.isReadable, path + " retained CPU mesh data.");
                Assert.AreEqual(ModelImporterMeshCompression.Medium,
                    importer.meshCompression, path);
                Assert.AreEqual(ModelImporterAnimationType.None,
                    importer.animationType, path);
                Assert.AreEqual(ModelImporterMaterialImportMode.None,
                    importer.materialImportMode, path);
            }

            foreach (string guid in textures)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.NotNull(importer, path);
                Assert.IsFalse(importer.isReadable, path + " retained CPU texture data.");
                Assert.IsTrue(importer.mipmapEnabled, path + " has no mipmaps.");
                Assert.IsTrue(importer.streamingMipmaps, path + " is not streamable.");
                Assert.LessOrEqual(importer.maxTextureSize,
                    path.Contains("/Resources/") ? 512 : 1024, path);
                TextureImporterPlatformSettings android =
                    importer.GetPlatformTextureSettings("Android");
                Assert.IsTrue(android.overridden, path + " has no Android override.");
                Assert.AreEqual(TextureImporterFormat.ETC2_RGBA8, android.format, path);
            }
        }

        [Test]
        public void Scene_ProductionVisualsPreserveGameplayRootsAndPhysics()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string[] gameplayRoots =
            {
                "Wood Processor", "Central Warehouse", "Assembly Plant",
                "Industrial District M14", "Mine Teaser"
            };
            foreach (string rootName in gameplayRoots)
            {
                Assert.IsTrue(scene.GetRootGameObjects().Any(root => root.name == rootName),
                    "Gameplay root moved or disappeared: " + rootName);
            }

            GameObject[] productionVisuals = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(transform => transform.name.EndsWith(
                    "Production Visual", StringComparison.Ordinal))
                .Select(transform => transform.gameObject)
                .ToArray();
            Assert.GreaterOrEqual(productionVisuals.Length, 17);
            foreach (GameObject visual in productionVisuals)
            {
                Assert.Zero(visual.GetComponentsInChildren<Collider>(true).Length,
                    visual.name + " owns a gameplay collider.");
                Assert.Zero(visual.GetComponentsInChildren<Rigidbody>(true).Length,
                    visual.name + " owns a rigidbody.");
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.NotNull(renderer.sharedMaterial, visual.name);
                    Assert.That(renderer.sharedMaterial.shader.name,
                        Does.StartWith("Universal Render Pipeline/"), visual.name);
                }
            }

            GameObject gate = Find(scene, "Industrial District Progression Gate");
            GameObject gateVisual = Find(scene, "IndustrialGate Production Visual");
            Assert.NotNull(gate);
            Assert.NotNull(gateVisual);
            Assert.AreEqual(gate.transform, gateVisual.transform.parent,
                "Gate art would remain after the authoritative blocker unlocks.");
        }

        [Test]
        public void ResourcePrefab_UsesOneRendererPerVariantAndKeepsSelectors()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/WoodCarryVisual.prefab");
            Assert.NotNull(prefab);
            ResourceVisual selector = prefab.GetComponent<ResourceVisual>();
            Assert.NotNull(selector);
            foreach (GameObject variant in new[]
                     { selector.CrateRoot, selector.IronOreRoot, selector.IronBarRoot, selector.ToolRoot })
            {
                Assert.NotNull(variant);
                Renderer[] renderers = variant.GetComponentsInChildren<Renderer>(true);
                Assert.AreEqual(1, renderers.Length, variant.name);
                Assert.That(renderers[0].sharedMaterial.shader.name,
                    Does.StartWith("Universal Render Pipeline/"), variant.name);
                Assert.Zero(variant.GetComponentsInChildren<Collider>(true).Length, variant.name);
            }
        }

        [Test]
        public void ProceduralParts_AreSeparateAndFullyWired()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            WoodProcessorFeedback saw = FindComponent<WoodProcessorFeedback>(scene);
            CrateCourierFeedback courier = FindComponent<CrateCourierFeedback>(scene);
            MiningWorldFeedback drill = FindComponent<MiningWorldFeedback>(scene);
            Assert.NotNull(GetField<Transform>(saw, "workingBlade"));
            Assert.NotNull(GetField<Transform>(courier, "leftWheel"));
            Assert.NotNull(GetField<Transform>(courier, "rightWheel"));
            Assert.NotNull(GetField<Transform>(courier, "rearLeftWheel"));
            Assert.NotNull(GetField<Transform>(courier, "rearRightWheel"));
            Assert.NotNull(drill.DrillBit);
            Assert.AreEqual(360f, drill.DrillBitRotationSpeed, 0.001f);
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault();
        }

        private static GameObject Find(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == name)?.gameObject;
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(target) as T;
        }
    }

    public sealed class LumberCampM19PlayModeTests
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";

        [UnityTest]
        public IEnumerator SawCourierAndDrillRotateOnlyInActiveStates()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            WoodProcessorFeedback saw = FindAny<WoodProcessorFeedback>();
            WoodProcessor processor = FindAny<WoodProcessor>();
            Transform blade = GetField<Transform>(saw, "workingBlade");
            Quaternion bladeBefore = blade.localRotation;
            SetField(processor, "_isProcessing", true);
            InvokeUpdate(saw);
            Assert.AreNotEqual(bladeBefore, blade.localRotation,
                "Saw blade did not rotate while processing.");
            Quaternion bladeActive = blade.localRotation;
            SetField(processor, "_isProcessing", false);
            InvokeUpdate(saw);
            Assert.AreEqual(bladeActive, blade.localRotation,
                "Saw blade rotated while idle.");

            CrateCourierFeedback courierFeedback = FindAny<CrateCourierFeedback>();
            Transform[] wheels =
            {
                GetField<Transform>(courierFeedback, "leftWheel"),
                GetField<Transform>(courierFeedback, "rightWheel"),
                GetField<Transform>(courierFeedback, "rearLeftWheel"),
                GetField<Transform>(courierFeedback, "rearRightWheel")
            };
            Quaternion[] wheelBefore = wheels.Select(wheel => wheel.localRotation).ToArray();
            SetField(courierFeedback, "_displayedState", CrateCourierState.MoveToPickup);
            InvokeUpdate(courierFeedback);
            for (int i = 0; i < wheels.Length; i++)
            {
                Assert.AreNotEqual(wheelBefore[i], wheels[i].localRotation,
                    "Courier wheel did not rotate: " + i);
            }

            MiningWorldFeedback miningFeedback = FindAny<MiningWorldFeedback>();
            AutomatedDrill drill = FindAny<AutomatedDrill>();
            Quaternion drillBefore = miningFeedback.DrillBit.localRotation;
            SetField(drill, "_state", AutomatedDrillState.Producing);
            InvokeUpdate(miningFeedback);
            Assert.AreNotEqual(drillBefore, miningFeedback.DrillBit.localRotation,
                "Drill bit did not rotate while producing.");
            Quaternion drillActive = miningFeedback.DrillBit.localRotation;
            SetField(drill, "_state", AutomatedDrillState.Idle);
            InvokeUpdate(miningFeedback);
            Assert.AreEqual(drillActive, miningFeedback.DrillBit.localRotation,
                "Drill bit rotated while idle.");

            yield return new ExitPlayMode();
        }

        private static T FindAny<T>() where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            return target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, "Missing field " + name);
            field.SetValue(target, value);
        }

        private static void InvokeUpdate(object target)
        {
            MethodInfo method = target.GetType().GetMethod(
                "Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, "Missing Update on " + target.GetType().Name);
            method.Invoke(target, null);
        }
    }
}
