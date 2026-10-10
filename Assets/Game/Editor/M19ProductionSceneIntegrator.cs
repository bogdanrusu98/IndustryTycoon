using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Adds production art below existing gameplay-owned transforms. Primitive
    /// presentation renderers are disabled, never deleted, so every integration
    /// has a deterministic fallback and no collider or gameplay reference moves.
    /// </summary>
    public static class M19ProductionSceneIntegrator
    {
        private const string ProductionRoot = "Assets/Art/Production";
        private const string MaterialRoot = ProductionRoot + "/Materials";

        public static void ReplaceResourceVisuals(GameObject resourceVisualRoot)
        {
            if (resourceVisualRoot == null)
            {
                throw new ArgumentNullException(nameof(resourceVisualRoot));
            }

            ReplaceResource(resourceVisualRoot.transform.Find("Crate Visual"),
                "Resources/WoodCrate/WoodCrate", 0.82f, new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0f));
            ReplaceResource(resourceVisualRoot.transform.Find("Iron Ore Visual"),
                "Resources/IronOre/IronOre", 0.78f, new Vector3(0f, 0f, 0f),
                new Vector3(0f, 18f, 0f));
            ReplaceResource(resourceVisualRoot.transform.Find("Iron Bar Visual"),
                "Resources/IronBar/IronBar", 0.92f, new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0f));
            ReplaceResource(resourceVisualRoot.transform.Find("Tool Visual"),
                "Resources/Tool/Tool", 0.84f, new Vector3(0f, 0f, 0f),
                new Vector3(0f, 18f, 0f));
        }

        public static void IntegrateScene(Scene scene)
        {
            AttachBuilding(scene, "Wood Processor", "Processor Visual",
                "Machines/Processor/Processor", 3.35f, new Vector3(0f, 0f, 0.28f),
                new Vector3(0f, 180f, 0f),
                "Processor Platform", "Processor Housing");
            AttachBuilding(scene, "Tool Workshop", "Tool Workshop Visual",
                "Buildings/ToolWorkshop/ToolWorkshop", 4.05f, new Vector3(0f, 0f, 0.18f),
                Vector3.zero,
                "Workshop Platform", "Workshop Rear Wall", "Workshop Roof",
                "Forge Workbench", "Tool Press");
            AttachBuilding(scene, "Central Warehouse", null,
                "Buildings/Warehouse/Warehouse", 5.1f, new Vector3(0f, 0f, 0.30f),
                new Vector3(0f, 180f, 0f),
                "Warehouse Floor", "Warehouse Rack 1", "Warehouse Rack 2",
                "Warehouse Rack 3", "Warehouse Rack 4", "Warehouse Rack 5");
            AttachBuilding(scene, "Assembly Plant", null,
                "Buildings/AssemblyPlant/AssemblyPlant", 4.05f,
                new Vector3(0f, 0f, 0.48f), new Vector3(0f, 180f, 0f),
                "Assembly Plant Body", "Assembly Line");
            GameObject district = FindRoot(scene, "Industrial District M14");
            Transform progressionGate = district != null
                ? FindDescendant(district.transform, "Industrial District Progression Gate")
                : null;
            AttachModel(progressionGate, "Buildings/IndustrialGate/IndustrialGate", 4.45f,
                Vector3.zero, Vector3.zero, true);
            DisableOwnRenderer(progressionGate);

            GameObject mineTeaser = FindRoot(scene, "Mine Teaser");
            if (mineTeaser != null)
            {
                Transform locked = FindDescendant(mineTeaser.transform, "Mine Locked Presentation");
                AttachModel(locked, "Buildings/MineBarrier/MineBarrier", 2.85f,
                    new Vector3(0f, 0f, -0.62f), Vector3.zero, true);
                DisableNamedRenderers(locked,
                    "Locked Barrier Left", "Locked Barrier Right", "Locked Barrier Beam");

                Transform mineArea = FindDescendant(mineTeaser.transform, "Mine Area");
                AttachModel(mineArea, "Buildings/MineEntrance/MineEntrance", 3.85f,
                    new Vector3(0f, 0f, 7.45f), Vector3.zero, true);

                Transform rearRockLeft = FindDescendant(mineTeaser.transform, "Rear Rock Left");
                AttachModel(rearRockLeft, "Environment/MineRockA/MineRockA", 3.15f,
                    Vector3.zero, new Vector3(0f, 28f, 0f), true);
                DisableOwnRenderer(rearRockLeft);

                Transform rearRockRight = FindDescendant(mineTeaser.transform, "Rear Rock Right");
                AttachModel(rearRockRight, "Environment/MineRockB/MineRockB", 3.30f,
                    Vector3.zero, new Vector3(0f, -22f, 0f), true);
                DisableOwnRenderer(rearRockRight);
            }

            GameObject storage = FindDescendantObject(scene, "Ore Storage");
            if (storage != null)
            {
                AttachModel(storage.transform, "Machines/OreHopper/OreHopper", 2.35f,
                    new Vector3(0f, 0f, 0.20f), Vector3.zero, true);
                DisableNamedRenderers(storage.transform, "Ore Bin");
            }

            GameObject processor = FindRoot(scene, "Wood Processor");
            Transform blade = processor != null
                ? FindDescendant(processor.transform, "Working Blade")
                : null;
            if (blade != null)
            {
                AttachModel(blade, "Machines/SawBlade/SawBlade", 1.30f,
                    Vector3.zero, new Vector3(90f, 0f, 0f), false);
                DisableOwnRenderer(blade);
            }

            GameObject drill = FindDescendantObject(scene, "Automated Mining Drill");
            Transform drillBit = drill != null
                ? FindDescendant(drill.transform, "Drill Bit")
                : null;
            if (drillBit != null)
            {
                AttachModel(drillBit, "Machines/DrillBit/DrillBit", 1.48f,
                    Vector3.zero, new Vector3(90f, 0f, 0f), false);
                DisableOwnRenderer(drillBit);
            }

            GameObject courier = FindDescendantObject(scene, "Crate Courier");
            Transform courierVisual = courier != null
                ? FindDescendant(courier.transform, "Courier Visual")
                : null;
            if (courierVisual != null)
            {
                AttachModel(courierVisual, "Vehicles/Courier/Courier", 1.92f,
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 90f, 0f), true);
                DisableNamedRenderers(courierVisual,
                    "Courier Chassis", "Courier Cabin", "Courier Windscreen", "Courier Cargo Deck");
                AttachCourierWheels(courierVisual);
            }

            RaiseLabel(scene, "Processor Status", 2.72f);
            RaiseLabel(scene, "Tool Workshop Label", 3.04f);
            RaiseLabel(scene, "Tool Workshop Status", 2.64f);
            RaiseLabel(scene, "Warehouse Label", 2.52f);

            ValidateProductionIntegration(scene);
        }

        public static void ValidateProductionIntegration(Scene scene)
        {
            string[] requiredModels =
            {
                "Processor Production Visual", "ToolWorkshop Production Visual",
                "Warehouse Production Visual", "AssemblyPlant Production Visual",
                "IndustrialGate Production Visual", "MineBarrier Production Visual",
                "MineEntrance Production Visual", "OreHopper Production Visual",
                "SawBlade Production Visual", "DrillBit Production Visual",
                "Courier Production Visual", "MineRockA Production Visual",
                "MineRockB Production Visual"
            };
            foreach (string name in requiredModels)
            {
                GameObject visual = FindDescendantObject(scene, name);
                if (visual == null)
                {
                    throw new InvalidOperationException($"M19 production visual is missing: {name}");
                }

                if (visual.GetComponentsInChildren<Collider>(true).Length != 0
                    || visual.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                {
                    throw new InvalidOperationException(
                        $"M19 production visual must not own gameplay physics: {name}");
                }

                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    Material material = renderer.sharedMaterial;
                    if (material == null || material.shader == null
                        || !material.shader.name.StartsWith(
                            "Universal Render Pipeline/", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"M19 visual has a missing/non-URP material: {name}");
                    }
                }
            }
        }

        private static void ReplaceResource(
            Transform root,
            string assetKey,
            float targetSize,
            Vector3 localPosition,
            Vector3 localEuler)
        {
            if (root == null)
            {
                return;
            }

            string modelPath = $"{ProductionRoot}/{assetKey}.fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            MeshFilter sourceFilter = source != null
                ? source.GetComponentInChildren<MeshFilter>(true)
                : null;
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                return;
            }

            Renderer[] oldRenderers = root.GetComponentsInChildren<Renderer>(true);
            MeshFilter[] oldFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (Renderer renderer in oldRenderers)
            {
                UnityEngine.Object.DestroyImmediate(renderer);
            }

            foreach (MeshFilter filter in oldFilters)
            {
                UnityEngine.Object.DestroyImmediate(filter);
            }

            MeshFilter targetFilter = root.gameObject.AddComponent<MeshFilter>();
            MeshRenderer targetRenderer = root.gameObject.AddComponent<MeshRenderer>();
            targetFilter.sharedMesh = sourceFilter.sharedMesh;
            targetRenderer.sharedMaterial = CreateOrUpdateMaterial(assetKey);
            targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
            targetRenderer.receiveShadows = false;
            targetRenderer.lightProbeUsage = LightProbeUsage.Off;
            targetRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            float largest = Mathf.Max(
                sourceFilter.sharedMesh.bounds.size.x,
                sourceFilter.sharedMesh.bounds.size.y,
                sourceFilter.sharedMesh.bounds.size.z);
            root.localPosition = localPosition;
            root.localRotation = Quaternion.Euler(localEuler);
            root.localScale = Vector3.one * (targetSize / Mathf.Max(0.0001f, largest));
        }

        private static void AttachBuilding(
            Scene scene,
            string rootName,
            string visualName,
            string assetKey,
            float targetSize,
            Vector3 localPosition,
            Vector3 localEuler,
            params string[] placeholders)
        {
            GameObject root = FindRoot(scene, rootName) ?? FindDescendantObject(scene, rootName);
            Transform parent = root != null
                ? (string.IsNullOrEmpty(visualName)
                    ? root.transform
                    : FindDescendant(root.transform, visualName))
                : null;
            if (parent == null)
            {
                throw new InvalidOperationException($"M19 target hierarchy is missing: {rootName}/{visualName}");
            }

            AttachModel(parent, assetKey, targetSize, localPosition, localEuler, true);
            DisableNamedRenderers(root.transform, placeholders);
        }

        private static GameObject AttachModel(
            Transform parent,
            string assetKey,
            float targetSize,
            Vector3 localPosition,
            Vector3 localEuler,
            bool castShadows)
        {
            if (parent == null)
            {
                throw new InvalidOperationException($"M19 parent is missing for {assetKey}.");
            }

            string modelPath = $"{ProductionRoot}/{assetKey}.fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null)
            {
                throw new InvalidOperationException($"M19 model import failed: {modelPath}");
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = Path.GetFileName(assetKey) + " Production Visual";
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(localEuler);
            Vector3 parentScale = parent.lossyScale;
            Vector3 inverseParentScale = new Vector3(
                1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
            instance.transform.localScale = inverseParentScale;

            Material material = CreateOrUpdateMaterial(assetKey);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                throw new InvalidOperationException($"M19 model contains no renderer: {modelPath}");
            }

            foreach (Renderer renderer in renderers)
            {
                Material[] assigned = new Material[Math.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < assigned.Length; i++)
                {
                    assigned[i] = material;
                }

                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = castShadows
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = castShadows;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            Bounds bounds = CalculateWorldBounds(renderers);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest <= 0.0001f)
            {
                throw new InvalidOperationException($"M19 model has invalid bounds: {modelPath}");
            }

            float scale = targetSize / largest;
            instance.transform.localScale = inverseParentScale * scale;
            bounds = CalculateWorldBounds(renderers);
            Vector3 targetWorld = parent.TransformPoint(localPosition);
            Vector3 groundedCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            instance.transform.position += targetWorld - groundedCenter;
            return instance;
        }

        private static void AttachCourierWheels(Transform courierVisual)
        {
            string[] wheelNames =
            {
                "Front Left Wheel", "Front Right Wheel", "Rear Left Wheel", "Rear Right Wheel"
            };
            foreach (string wheelName in wheelNames)
            {
                Transform wheel = FindDescendant(courierVisual, wheelName);
                if (wheel == null)
                {
                    throw new InvalidOperationException($"M19 courier wheel anchor is missing: {wheelName}");
                }

                AttachModel(wheel, "Vehicles/CourierWheel/CourierWheel", 0.58f,
                    Vector3.zero, new Vector3(0f, 90f, 0f), false);
                DisableOwnRenderer(wheel);
            }
        }

        private static Material CreateOrUpdateMaterial(string assetKey)
        {
            EnsureFolder(MaterialRoot);
            string name = Path.GetFileName(assetKey);
            string path = $"{MaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP Lit shader is unavailable for M19.");
            }

            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{ProductionRoot}/{assetKey}_BaseColor.png");
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{ProductionRoot}/{assetKey}_Normal.png");
            if (baseMap == null)
            {
                throw new InvalidOperationException($"M19 Base Map is missing: {assetKey}");
            }

            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", IsMetalAsset(name) ? 0.22f : 0.02f);
            material.SetFloat("_Smoothness", IsMetalAsset(name) ? 0.34f : 0.20f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.renderQueue = -1;
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 0.75f);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static bool IsMetalAsset(string name)
        {
            return name != "WoodCrate"
                   && name != "IronOre"
                   && name != "MineEntrance"
                   && !name.StartsWith("MineRock", StringComparison.Ordinal);
        }

        private static Bounds CalculateWorldBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void DisableNamedRenderers(Transform root, params string[] names)
        {
            if (root == null)
            {
                return;
            }

            var set = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (set.Contains(renderer.gameObject.name))
                {
                    renderer.enabled = false;
                }
            }
        }

        private static void DisableOwnRenderer(Transform root)
        {
            Renderer renderer = root != null ? root.GetComponent<Renderer>() : null;
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static void RaiseLabel(Scene scene, string name, float localY)
        {
            GameObject label = FindDescendantObject(scene, name);
            if (label == null)
            {
                return;
            }

            Vector3 position = label.transform.localPosition;
            position.y = localY;
            label.transform.localPosition = position;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
        }

        private static GameObject FindDescendantObject(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform result = FindDescendant(root.transform, name);
                if (result != null)
                {
                    return result.gameObject;
                }
            }

            return null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindDescendant(root.GetChild(i), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }
    }
}
