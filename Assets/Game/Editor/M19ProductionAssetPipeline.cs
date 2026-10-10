using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Deterministic import policy and audit helpers for the curated M19 production art.
    /// Runtime code never depends on this editor-only type.
    /// </summary>
    public sealed class M19ProductionAssetPostprocessor : AssetPostprocessor
    {
        internal const string ProductionRoot = "Assets/Art/Production/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ProductionRoot, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.meshOptimizationFlags = MeshOptimizationFlags.Everything;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.generateSecondaryUV = false;
            importer.useFileScale = true;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ProductionRoot, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            bool isNormal = assetPath.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = assetPath.Contains("/Resources/", StringComparison.Ordinal)
                ? 512
                : 1024;

            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = importer.maxTextureSize;
            android.format = TextureImporterFormat.ETC2_RGBA8;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
        }
    }

    public static class M19ProductionAssetPipeline
    {
        private const string AuditPath = "Logs/M19-asset-audit.txt";
        private const string PreviewFolder = "Logs/M19-previews";

        [MenuItem("Industry Tycoon/M19/Reimport Production Assets")]
        public static void ReimportProductionAssets()
        {
            string[] paths = AssetDatabase.FindAssets(string.Empty, new[]
                {
                    M19ProductionAssetPostprocessor.ProductionRoot.TrimEnd('/')
                })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < paths.Length; i++)
            {
                AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"M19 reimported {paths.Length} production asset files.");
        }

        public static void AuditFromCommandLine()
        {
            ReimportProductionAssets();
            Directory.CreateDirectory("Logs");
            var lines = new List<string>
            {
                "M19 production asset audit",
                $"Unity {Application.unityVersion}",
                string.Empty
            };

            string[] modelGuids = AssetDatabase.FindAssets(
                "t:Model",
                new[] { M19ProductionAssetPostprocessor.ProductionRoot.TrimEnd('/') });
            foreach (string path in modelGuids.Select(AssetDatabase.GUIDToAssetPath)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    lines.Add($"MODEL|{path}|BROKEN");
                    continue;
                }

                MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
                SkinnedMeshRenderer[] skinned =
                    model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Mesh[] meshes = filters.Select(filter => filter.sharedMesh)
                    .Concat(skinned.Select(renderer => renderer.sharedMesh))
                    .Where(mesh => mesh != null)
                    .Distinct()
                    .ToArray();
                long vertices = meshes.Sum(mesh => (long)mesh.vertexCount);
                long triangles = meshes.Sum(mesh => (long)mesh.triangles.Length / 3L);
                int materials = model.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .Distinct()
                    .Count();
                Bounds bounds = CalculateBounds(model);
                lines.Add(
                    $"MODEL|{path}|meshes={meshes.Length}|vertices={vertices}|triangles={triangles}"
                    + $"|materials={materials}|bounds={bounds.size.x:0.###},{bounds.size.y:0.###},{bounds.size.z:0.###}");
            }

            string[] textureGuids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { M19ProductionAssetPostprocessor.ProductionRoot.TrimEnd('/') });
            foreach (string path in textureGuids.Select(AssetDatabase.GUIDToAssetPath)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                TextureImporterPlatformSettings android =
                    importer != null ? importer.GetPlatformTextureSettings("Android") : null;
                lines.Add(
                    $"TEXTURE|{path}|source={texture?.width}x{texture?.height}"
                    + $"|max={importer?.maxTextureSize}|androidMax={android?.maxTextureSize}"
                    + $"|normal={importer?.textureType == TextureImporterType.NormalMap}"
                    + $"|readable={importer?.isReadable}|mipmaps={importer?.mipmapEnabled}");
            }

            File.WriteAllLines(AuditPath, lines);
            Debug.Log($"M19 production asset audit written to {AuditPath}.");
        }

        public static void RenderPreviewsFromCommandLine()
        {
            Directory.CreateDirectory(PreviewFolder);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP Lit is unavailable for M19 previews.");
            }

            string[] modelGuids = AssetDatabase.FindAssets(
                "t:Model",
                new[] { M19ProductionAssetPostprocessor.ProductionRoot.TrimEnd('/') });
            foreach (string path in modelGuids.Select(AssetDatabase.GUIDToAssetPath)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Path.ChangeExtension(path, null) + "_BaseColor.png");
                if (model == null || baseMap == null)
                {
                    continue;
                }

                var preview = new PreviewRenderUtility();
                GameObject instance = UnityEngine.Object.Instantiate(model);
                instance.hideFlags = HideFlags.HideAndDontSave;
                var previewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                previewMaterial.SetTexture("_BaseMap", baseMap);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = previewMaterial;
                }

                preview.AddSingleGO(instance);
                Bounds bounds = CalculateBounds(instance);
                float radius = Mathf.Max(0.2f, bounds.extents.magnitude);
                preview.cameraFieldOfView = 28f;
                preview.camera.transform.position =
                    bounds.center + new Vector3(radius * 1.4f, radius * 0.85f, -radius * 1.8f);
                preview.camera.transform.LookAt(bounds.center);
                preview.camera.nearClipPlane = Mathf.Max(0.01f, radius * 0.01f);
                preview.camera.farClipPlane = radius * 10f;
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(0.10f, 0.12f, 0.15f, 1f);
                preview.lights[0].intensity = 1.25f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
                preview.lights[1].intensity = 0.65f;

                preview.BeginStaticPreview(new Rect(0f, 0f, 512f, 512f));
                preview.camera.Render();
                Texture2D result = preview.EndStaticPreview();
                string output = Path.Combine(
                    PreviewFolder,
                    Path.GetFileNameWithoutExtension(path) + "-"
                    + new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty).Name + ".png");
                File.WriteAllBytes(output, result.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(result);
                UnityEngine.Object.DestroyImmediate(previewMaterial);
                preview.Cleanup();
            }

            Debug.Log($"M19 production previews written to {PreviewFolder}.");
        }

        public static void CaptureM22BaselineFromCommandLine()
        {
            CaptureSceneMetrics("Logs/M19-M22-baseline-metrics.txt");
        }

        public static void CaptureM19MetricsFromCommandLine()
        {
            CaptureSceneMetrics("Logs/M19-production-metrics.txt");
        }

        public static void RenderSceneValidationFromCommandLine()
        {
            const string folder = "Logs/M19-scene-validation";
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Prototype_LumberCamp.unity",
                OpenSceneMode.Single);
            Camera camera = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault();
            if (camera == null)
            {
                throw new InvalidOperationException("M19 portrait validation camera is missing.");
            }

            foreach (Canvas canvas in scene.GetRootGameObjects()
                         .SelectMany(root => root.GetComponentsInChildren<Canvas>(true)))
            {
                canvas.gameObject.SetActive(false);
            }

            string[] visibleRoots =
            {
                "Wood Processor", "Tool Workshop", "Central Warehouse", "Assembly Plant",
                "Industrial District M14", "Mine Teaser", "Mine Area", "Iron Smelter",
                "Ore Storage", "Automated Mining Drill", "Crate Courier Delivery",
                "Crate Courier"
            };
            foreach (string name in visibleRoots)
            {
                GameObject gameObject = FindSceneObject(scene, name);
                if (gameObject != null)
                {
                    ActivateHierarchy(gameObject.transform);
                    gameObject.SetActive(true);
                }
            }

            var views = new[]
            {
                new { Name = "lumber", Target = new Vector3(-5.0f, 0.8f, 4.7f) },
                new { Name = "mining", Target = new Vector3(7.2f, 0.8f, 22.2f) },
                new { Name = "logistics", Target = new Vector3(-11.0f, 0.8f, 18.5f) },
                new { Name = "industrial", Target = new Vector3(-5.5f, 0.8f, 23.4f) }
            };
            var targetTexture = new RenderTexture(540, 960, 24, RenderTextureFormat.ARGB32);
            var result = new Texture2D(540, 960, TextureFormat.RGB24, false);
            camera.targetTexture = targetTexture;
            camera.fieldOfView = 48f;
            foreach (var view in views)
            {
                camera.transform.position = view.Target + new Vector3(0f, 12.5f, -10.5f);
                camera.transform.LookAt(view.Target);
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = targetTexture;
                result.ReadPixels(new Rect(0f, 0f, 540f, 960f), 0, 0);
                result.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(
                    Path.Combine(folder, view.Name + "-portrait.png"),
                    result.EncodeToPNG());
            }

            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(result);
            targetTexture.Release();
            UnityEngine.Object.DestroyImmediate(targetTexture);
            Debug.Log($"M19 portrait validation renders written to {folder}.");
        }

        private static void CaptureSceneMetrics(string outputPath)
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Prototype_LumberCamp.unity",
                OpenSceneMode.Single);
            GameObject[] objects = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(transform => transform.gameObject)
                .Distinct()
                .ToArray();
            Renderer[] renderers = objects.SelectMany(gameObject =>
                    gameObject.GetComponents<Renderer>())
                .ToArray();
            long vertices = 0;
            long triangles = 0;
            long activeVertices = 0;
            long activeTriangles = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = skinned.sharedMesh;
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    mesh = filter != null ? filter.sharedMesh : null;
                }
                if (mesh == null)
                {
                    continue;
                }

                long meshTriangles = mesh.triangles.LongLength / 3L;
                vertices += mesh.vertexCount;
                triangles += meshTriangles;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    activeVertices += mesh.vertexCount;
                    activeTriangles += meshTriangles;
                }
            }

            Material[] materials = renderers.SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Distinct()
                .ToArray();
            Texture[] textures = materials.SelectMany(GetMaterialTextures)
                .Where(texture => texture != null)
                .Distinct()
                .ToArray();
            long textureBytes = textures.Sum(Profiler.GetRuntimeMemorySizeLong);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? "Logs");
            File.WriteAllLines(outputPath, new[]
            {
                $"scene={scene.path}",
                $"gameObjects={objects.Length}",
                $"activeGameObjects={objects.Count(gameObject => gameObject.activeInHierarchy)}",
                $"renderers={renderers.Length}",
                $"enabledRenderers={renderers.Count(renderer => renderer.enabled)}",
                $"activeRenderers={renderers.Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)}",
                $"vertices={vertices}",
                $"triangles={triangles}",
                $"activeVertices={activeVertices}",
                $"activeTriangles={activeTriangles}",
                $"materials={materials.Length}",
                $"textures={textures.Length}",
                $"textureMemoryBytes={textureBytes}"
            });
            Debug.Log($"Scene metrics written to {outputPath}.");
        }

        private static IEnumerable<Texture> GetMaterialTextures(Material material)
        {
            string[] propertyNames = material.GetTexturePropertyNames();
            for (int i = 0; i < propertyNames.Length; i++)
            {
                Texture texture = material.GetTexture(propertyNames[i]);
                if (texture != null)
                {
                    yield return texture;
                }
            }
        }

        private static GameObject FindSceneObject(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform match = FindTransform(root.transform, name);
                if (match != null)
                {
                    return match.gameObject;
                }
            }

            return null;
        }

        private static Transform FindTransform(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindTransform(root.GetChild(i), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static void ActivateHierarchy(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                current.gameObject.SetActive(true);
            }
        }

        private static Bounds CalculateBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
