using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IndustryTycoon.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Visual-only release checks and deterministic portrait captures. The
    /// capture pass mutates an in-memory scene only and never changes gameplay
    /// state, progression authority or the saved scene.
    /// </summary>
    public static class VisualCompletionValidation
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string ProductionRoot = "Assets/Art/Production";
        private const string OutputRoot = "Logs/VisualCompletion";
        private const string ScreenshotRoot = OutputRoot + "/Screenshots";

        private sealed class CoverageEntry
        {
            public string Entity;
            public string Visual;
            public string Source;
            public string Integrated;
            public string Fallback;
        }

        private static readonly CoverageEntry[] Coverage =
        {
            Entry("Player", "Player Animated Production Model", "Mobile Optimized Safe: Idle/Walk/Run", "Assets/Art/Production/Characters/Player", string.Empty),
            Entry("Lumber Worker", "LumberWorker Animated Production Model", "Game Ready Builder: Idle/Walk/Run", "Assets/Art/Production/Characters/LumberWorker", string.Empty),
            Entry("Loose Wood", "WoodLog Production Visual", "Mobile Game Wood Log 1011082912", "Assets/Art/Production/Resources/WoodLog", string.Empty),
            Entry("Plank", "Plank Production Visual", "Mobile Wooden Plank L 1011082921", "Assets/Art/Production/Resources/Plank", string.Empty),
            Entry("Cash", "Cash bundle", "No matching export", "Fallback retained", "No production Cash source"),
            Entry("Crate", "Crate Visual", "Mobile Lowpoly Wooden Crate", "Assets/Art/Production/Resources/WoodCrate", string.Empty),
            Entry("Iron Ore", "Iron Ore Visual", "Optimized Iron Ore", "Assets/Art/Production/Resources/IronOre", string.Empty),
            Entry("Iron Bar", "Iron Bar Visual", "Iron Ingot Mobile", "Assets/Art/Production/Resources/IronBar", string.Empty),
            Entry("Tool", "Tool Visual", "Mobile Stylized Toolbox", "Assets/Art/Production/Resources/Tool", string.Empty),
            Entry("Wood Processor", "Processor Production Visual", "Saw Station Optimized", "Assets/Art/Production/Machines/Processor", string.Empty),
            Entry("Processor Saw", "SawBlade Production Visual", "Saw Blade", "Assets/Art/Production/Machines/SawBlade", string.Empty),
            Entry("Tool Workshop", "ToolWorkshop Production Visual", "Ironforge Tool Workshop", "Assets/Art/Production/Buildings/ToolWorkshop", string.Empty),
            Entry("Central Warehouse", "Warehouse Production Visual", "Central Warehouse Assembly", "Assets/Art/Production/Buildings/Warehouse", string.Empty),
            Entry("Assembly Plant", "AssemblyPlant Production Visual", "Industrial Production Plant", "Assets/Art/Production/Buildings/AssemblyPlant", string.Empty),
            Entry("Industrial Gate", "IndustrialGate Production Visual", "Industrial District Lockdown", "Assets/Art/Production/Buildings/IndustrialGate", string.Empty),
            Entry("Mine Barrier", "MineBarrier Production Visual", "Industrial Lockdown Barrier", "Assets/Art/Production/Buildings/MineBarrier", string.Empty),
            Entry("Mine Entrance", "MineEntrance Production Visual", "Lowpoly Mine Entrance", "Assets/Art/Production/Buildings/MineEntrance", string.Empty),
            Entry("Mine Rock Left", "MineRockA Production Visual", "Rock Cluster Variants", "Assets/Art/Production/Environment/MineRockA", string.Empty),
            Entry("Mine Rock Right", "MineRockB Production Visual", "Rugged Stone Formation", "Assets/Art/Production/Environment/MineRockB", string.Empty),
            Entry("Ore Storage", "OreHopper Production Visual", "Teal Industrial Hopper", "Assets/Art/Production/Machines/OreHopper", string.Empty),
            Entry("Automated Drill", "DrillBit Production Visual", "Titan Bore Drill Bit", "Assets/Art/Production/Machines/DrillBit", string.Empty),
            Entry("Courier", "Courier Production Visual", "Teal Cargo Cruiser", "Assets/Art/Production/Vehicles/Courier", string.Empty),
            Entry("Courier Wheels", "CourierWheel Production Visual ×4", "Dedicated wheel export", "Assets/Art/Production/Vehicles/CourierWheel", string.Empty),
            Entry("Wood Stockpile", "Curated primitive staging", "No matching export", "Fallback retained", "No production Stockpile source"),
            Entry("Auto Feeder", "Curated primitive machine", "No matching export", "Fallback retained", "No production Auto Feeder source"),
            Entry("Packing Station", "Recovered Packing Production Visual", "Saw station conveyor shell reused without blade", "Assets/Art/Production/Machines/Processor", string.Empty),
            Entry("Smelter", "Recovered Smelter Production Visual", "Ironforge furnace shell reused", "Assets/Art/Production/Buildings/ToolWorkshop", string.Empty),
            Entry("Market Kiosk", "Curated primitive kiosk", "Courier wheel export was ambiguous", "Fallback retained", "No valid Market kiosk source")
        };

        [MenuItem("Industry Tycoon/Visual Completion/Audit Scene and Assets")]
        public static void AuditFromMenu() => AuditFromCommandLine();

        [MenuItem("Industry Tycoon/Visual Completion/Capture Portrait Screenshots")]
        public static void CaptureFromMenu() => CaptureFromCommandLine();

        public static void AuditFromCommandLine()
        {
            Directory.CreateDirectory(OutputRoot);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var results = new List<string>
            {
                "Visual completion audit",
                "Unity " + Application.unityVersion,
                "schema=" + M9SaveSchema.CurrentVersion,
                string.Empty
            };

            Require(M9SaveSchema.CurrentVersion == 9, "Save schema changed from v9.");
            Camera camera = FindComponent<Camera>(scene);
            Require(camera != null, "Main camera is missing.");
            int cameraMask = camera.cullingMask;

            int missingScripts = 0;
            foreach (GameObject gameObject in AllObjects(scene))
            {
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            }
            Require(missingScripts == 0, "Scene contains missing scripts: " + missingScripts);

            GameObject[] labelBackdrops = AllObjects(scene)
                .Where(gameObject => gameObject.name.EndsWith(" Backdrop", StringComparison.Ordinal))
                .ToArray();
            Require(labelBackdrops.Length > 0,
                "World-label readability backdrops are missing.");
            Require(labelBackdrops.All(gameObject =>
                    gameObject.GetComponentsInChildren<Collider>(true).Length == 0),
                "A world-label backdrop can block a gameplay path.");
            results.Add($"WORLD_LABELS|backdrops={labelBackdrops.Length}|colliders=0|PASS");

            string[] integratedNames = Coverage
                .Where(item => item.Visual.Contains("Production Visual", StringComparison.Ordinal))
                .Select(item => item.Visual.Replace(" ×4", string.Empty))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (string visualName in integratedNames)
            {
                GameObject[] matches = AllObjects(scene)
                    .Where(gameObject => gameObject.name == visualName)
                    .ToArray();
                int expectedCount = visualName == "CourierWheel Production Visual" ? 4 : 1;
                Require(matches.Length == expectedCount,
                    $"{visualName} count is {matches.Length}; expected {expectedCount}.");
                foreach (GameObject visual in matches)
                {
                    ValidateVisual(visual, cameraMask, results,
                        visualName.EndsWith("Production Visual", StringComparison.Ordinal));
                }
            }

            ValidateResourceVisuals(results);

            ValidatePlaceholderDisabled(scene, "Processor Platform", "Processor Housing");
            ValidatePlaceholderDisabled(scene, "Workshop Platform", "Workshop Rear Wall", "Workshop Roof", "Forge Workbench", "Tool Press");
            ValidatePlaceholderDisabled(scene, "Warehouse Floor", "Warehouse Rack 1", "Warehouse Rack 2", "Warehouse Rack 3", "Warehouse Rack 4", "Warehouse Rack 5");
            ValidatePlaceholderDisabled(scene, "Assembly Plant Body", "Assembly Line");
            ValidatePlaceholderDisabled(scene, "Locked Barrier Left", "Locked Barrier Right", "Locked Barrier Beam");
            ValidatePlaceholderDisabled(scene, "Ore Bin", "Rear Rock Left", "Rear Rock Right");
            ValidatePlaceholderDisabled(scene, "Courier Chassis", "Courier Cabin", "Courier Windscreen", "Courier Cargo Deck");

            string[] productionMaterials = AssetDatabase.FindAssets("t:Material", new[] { ProductionRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();
            foreach (string materialPath in productionMaterials)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                Require(material != null && material.shader != null,
                    "Missing material/shader: " + materialPath);
                Require(material.shader.name != "Hidden/InternalErrorShader",
                    "Pink/error material: " + materialPath);
                Require(material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null,
                    "Production material has no Base Map: " + materialPath);
            }

            results.Add(string.Empty);
            results.Add("COVERAGE");
            foreach (CoverageEntry item in Coverage)
            {
                results.Add(string.Join("|", item.Entity, item.Visual, item.Source,
                    item.Integrated, string.IsNullOrEmpty(item.Fallback) ? "production" : item.Fallback));
            }

            File.WriteAllLines(Path.Combine(OutputRoot, "visual-audit.txt"), results);
            Debug.Log($"Visual completion audit PASS: {integratedNames.Length} visual categories, "
                      + $"{productionMaterials.Length} materials, zero missing scripts/pink/missing textures.");
        }

        private static void ValidateResourceVisuals(List<string> results)
        {
            const string prefabPath = "Assets/Game/Prefabs/WoodCarryVisual.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                string[] names = { "Crate Visual", "Iron Ore Visual", "Iron Bar Visual", "Tool Visual" };
                foreach (string name in names)
                {
                    Transform visual = root.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(candidate => candidate.name == name);
                    Require(visual != null, "Integrated resource visual is missing: " + name);
                    Renderer renderer = visual.GetComponent<Renderer>();
                    Require(renderer != null && renderer.enabled,
                        "Integrated resource renderer is missing or disabled: " + name);
                    Material material = renderer.sharedMaterial;
                    Require(material != null && material.shader != null
                            && material.shader.name != "Hidden/InternalErrorShader",
                        "Integrated resource has a missing/pink material: " + name);
                    Require(material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null,
                        "Integrated resource is missing its production texture: " + name);
                    Require(visual.GetComponentsInChildren<Collider>(true).Length == 0
                            && visual.GetComponentsInChildren<Rigidbody>(true).Length == 0,
                        "Integrated resource visual owns gameplay physics: " + name);
                    results.Add($"VISUAL|{name}|prefab={prefabPath}|renderer=enabled|material=PASS");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void CaptureFromCommandLine()
        {
            Directory.CreateDirectory(ScreenshotRoot);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera camera = FindComponent<Camera>(scene);
            Canvas canvas = FindComponent<Canvas>(scene);
            Require(camera != null && canvas != null, "Screenshot camera or HUD is missing.");

            var target = new RenderTexture(540, 960, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                name = "Visual Completion Capture"
            };
            var pixels = new Texture2D(540, 960, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            camera.fieldOfView = 50f;

            canvas.gameObject.SetActive(false);
            CaptureWorld(scene, camera, target, pixels, "01-new-lumber",
                new Vector3(0f, 0.8f, 2.5f), Array.Empty<string>());
            CaptureWorld(scene, camera, target, pixels, "02-developed-lumber",
                new Vector3(-4.8f, 0.8f, 6.8f),
                new[] { "Wood Processor", "Wood Stockpile", "Wood Auto Feeder", "Packing Station", "Crate Courier Delivery", "Tool Workshop" });
            CaptureWorld(scene, camera, target, pixels, "03-mining",
                new Vector3(7.2f, 0.8f, 22.2f),
                new[] { "Mine Teaser", "Mine Area", "Iron Smelter", "Ore Storage", "Automated Mining Drill" });
            CaptureWorld(scene, camera, target, pixels, "04-warehouse-logistics",
                new Vector3(-10.8f, 0.8f, 18.5f),
                new[] { "Central Warehouse", "Industrial Logistics M13", "Crate Courier Delivery", "Crate Courier" });
            CaptureWorld(scene, camera, target, pixels, "05-industrial-district",
                new Vector3(-5.5f, 0.8f, 23.4f),
                new[] { "Industrial District M14", "Assembly Plant" });

            canvas.gameObject.SetActive(true);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.5f;
            CaptureUi(scene, camera, canvas, target, pixels, "06-market", "Market Orders Panel");
            CaptureUi(scene, camera, canvas, target, pixels, "07-building-panel", "Production Panel");
            CaptureUi(scene, camera, canvas, target, pixels, "08-goals-contracts", "Contract Panel");
            CaptureUi(scene, camera, canvas, target, pixels, "09-main-menu", "Main Menu");
            CaptureUi(scene, camera, canvas, target, pixels, "10-shop", "Shop Panel");
            CaptureUi(scene, camera, canvas, target, pixels, "11-settings", "Settings Panel");

            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(pixels);
            Debug.Log("Visual completion portrait screenshots written to " + ScreenshotRoot + ".");
        }

        private static void CaptureWorld(
            Scene scene,
            Camera camera,
            RenderTexture target,
            Texture2D pixels,
            string name,
            Vector3 lookTarget,
            IEnumerable<string> rootsToActivate)
        {
            foreach (string rootName in rootsToActivate)
            {
                GameObject root = FindObject(scene, rootName);
                if (root != null)
                {
                    ActivateHierarchy(root.transform);
                    ActivateTree(root.transform);
                }
            }

            camera.transform.position = lookTarget + new Vector3(0f, 18.5f, -13.5f);
            camera.transform.LookAt(lookTarget + Vector3.up * 0.4f);
            Render(camera, target, pixels, name);
        }

        private static void CaptureUi(
            Scene scene,
            Camera camera,
            Canvas canvas,
            RenderTexture target,
            Texture2D pixels,
            string name,
            string panelName)
        {
            string[] panels =
            {
                "Contract Panel", "Achievements Panel", "Industrial Operations Panel",
                "Production Panel", "Market Orders Panel", "Shop Panel", "Main Menu",
                "Pause Menu", "Settings Panel", "Help Panel", "Welcome Back Overlay"
            };
            foreach (string candidate in panels)
            {
                GameObject panel = FindObject(scene, candidate);
                if (panel != null)
                {
                    panel.SetActive(candidate == panelName);
                }
            }

            GameObject navigation = FindObject(scene, "Mobile Navigation");
            if (navigation != null)
            {
                navigation.SetActive(panelName != "Main Menu");
            }
            Canvas.ForceUpdateCanvases();
            Render(camera, target, pixels, name);
        }

        private static void Render(
            Camera camera,
            RenderTexture target,
            Texture2D pixels,
            string name)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            pixels.Apply(false, false);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(ScreenshotRoot, name + ".png"), pixels.EncodeToPNG());
        }

        private static void ValidateVisual(
            GameObject visual,
            int cameraMask,
            List<string> results,
            bool requiresProductionChild)
        {
            Require(!requiresProductionChild
                    || (visual.transform.parent != null
                        && visual.name.EndsWith("Production Visual", StringComparison.Ordinal)),
                "Production visual is not attached below a Visual hierarchy: " + visual.name);
            Vector3 scale = visual.transform.lossyScale;
            Require(IsFinite(scale) && Mathf.Abs(scale.x * scale.y * scale.z) > 0.000001f,
                "Invalid visual/parent scale: " + visual.name);
            Require(IsFinite(visual.transform.position), "Invalid position: " + visual.name);
            Require((cameraMask & (1 << visual.layer)) != 0,
                "Main camera culls production visual layer: " + visual.name);
            Require(visual.GetComponentsInChildren<Collider>(true).Length == 0
                    && visual.GetComponentsInChildren<Rigidbody>(true).Length == 0,
                "Production visual owns gameplay physics: " + visual.name);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length > 0, "Production visual has no renderer: " + visual.name);
            foreach (Renderer renderer in renderers)
            {
                Require(renderer.enabled, "Production renderer is disabled: " + visual.name);
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(material != null && material.shader != null
                            && material.shader.name != "Hidden/InternalErrorShader",
                        "Production visual has a missing/pink material: " + visual.name);
                    Require(material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null,
                        "Production visual is missing its texture: " + visual.name);
                }
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Require(IsFinite(bounds.size) && bounds.size.sqrMagnitude > 0.0001f,
                "Production visual has invalid bounds: " + visual.name);
            Require(bounds.min.y > -0.35f,
                $"Production visual is buried below the world: {visual.name} y={bounds.min.y:0.###}");
            results.Add($"VISUAL|{visual.name}|active={visual.activeInHierarchy}|renderers={renderers.Length}"
                        + $"|bounds={bounds.size.x:0.##},{bounds.size.y:0.##},{bounds.size.z:0.##}"
                        + $"|minY={bounds.min.y:0.##}|layer={visual.layer}|PASS");
        }

        private static void ValidatePlaceholderDisabled(Scene scene, params string[] names)
        {
            foreach (string name in names)
            {
                Renderer[] renderers = AllObjects(scene)
                    .Where(candidate => candidate.name == name)
                    .Select(candidate => candidate.GetComponent<Renderer>())
                    .Where(renderer => renderer != null)
                    .ToArray();
                Require(renderers.Length == 0 || renderers.Any(renderer => !renderer.enabled),
                    "Obsolete placeholder renderer is still enabled: " + name);
            }
        }

        private static IEnumerable<GameObject> AllObjects(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(transform => transform.gameObject);
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault();
        }

        private static GameObject FindObject(Scene scene, string name)
        {
            return AllObjects(scene).FirstOrDefault(gameObject => gameObject.name == name);
        }

        private static void ActivateHierarchy(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                current.gameObject.SetActive(true);
            }
        }

        private static void ActivateTree(Transform transform)
        {
            transform.gameObject.SetActive(true);
            for (int i = 0; i < transform.childCount; i++) ActivateTree(transform.GetChild(i));
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static CoverageEntry Entry(
            string entity,
            string visual,
            string source,
            string integrated,
            string fallback)
        {
            return new CoverageEntry
            {
                Entity = entity,
                Visual = visual,
                Source = source,
                Integrated = integrated,
                Fallback = fallback
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
