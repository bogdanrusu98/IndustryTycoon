using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    /// <summary>Reimports every staging model independently of the historical coverage table.</summary>
    public static class ModernAssetRecoveryAudit
    {
        public const string Output = "Logs/ModernVisual/SourceAudit";
        private const string Temporary = "Assets/VisualRecoveryStaging";

        public static void Run()
        {
            RunTo(Output);
        }

        public static void RunTo(string output)
        {
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Temporary);
            var report = new List<string>();
            string[] files = Directory.GetFiles("Meshy Import", "*", SearchOption.AllDirectories);
            File.WriteAllLines(output + "/all-files.txt", files.OrderBy(p => p).Select(p => p + "|" + new FileInfo(p).Length));
            string[] models = files.Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToArray();
            for (int i = 0; i < models.Length; i++)
            {
                string id = i.ToString("D2");
                string dest = Temporary + "/" + id + ".fbx";
                File.Copy(models[i], dest, true);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(dest);
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                foreach (Material sourceMaterial in AssetDatabase.LoadAllAssetsAtPath(dest).OfType<Material>())
                    report.Add($"EMBEDDED-MATERIAL|{id}|{sourceMaterial.name}|shader={sourceMaterial.shader.name}|textures={string.Join(",", sourceMaterial.GetTexturePropertyNames().Select(n => n + "=" + sourceMaterial.GetTexture(n)))}");
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.isReadable = true;
                importer.SaveAndReimport();
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(dest);
                string hash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(models[i])) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                report.Add($"SOURCE|{id}|{models[i]}|sha256={hash}|root={asset.transform.localEulerAngles}|scale={asset.transform.localScale}|axisBake={importer.bakeAxisConversion}|fileScale={importer.fileScale}");
                foreach (Transform t in asset.GetComponentsInChildren<Transform>(true))
                {
                    Mesh mesh = t.GetComponent<MeshFilter>()?.sharedMesh;
                    Renderer renderer = t.GetComponent<Renderer>();
                    report.Add($"NODE|{id}|{t.name}|mesh={mesh?.name}|submeshes={mesh?.subMeshCount}|pos={t.localPosition.ToString("F6")}|rot={t.localEulerAngles}|scale={t.localScale}|active={t.gameObject.activeSelf}|enabled={renderer?.enabled}|materials={renderer?.sharedMaterials.Length}|vertices={mesh?.vertexCount}|triangles={mesh?.triangles.Length / 3}|normals={mesh?.normals.Length}|bounds-center={mesh?.bounds.center.ToString("F6")}|bounds-size={mesh?.bounds.size.ToString("F6")}");
                }
                string[] textures = Directory.GetFiles(Path.GetDirectoryName(models[i]), "*.png");
                report.AddRange(textures.Select(p => "TEXTURE|" + id + "|" + p));
                string color = textures.FirstOrDefault(p => p.IndexOf("base", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? textures.FirstOrDefault(p => p.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? textures.FirstOrDefault(p => p.EndsWith("texture.png", StringComparison.OrdinalIgnoreCase));
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                Texture2D tex = null;
                if (color != null) { tex = new Texture2D(2, 2); tex.LoadImage(File.ReadAllBytes(color)); mat.SetTexture("_BaseMap", tex); }
                mat.SetFloat("_Smoothness", 0.18f);
                Render(asset, mat, output + "/" + id + ".png");
                UnityEngine.Object.DestroyImmediate(mat);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
            File.WriteAllLines(output + "/geometry.txt", report);
            // The sources remain outside production and must never be shipped or committed.
            AssetDatabase.DeleteAsset(Temporary);
            Debug.Log("SOURCE RECOVERY AUDIT: " + models.Length + " FBX files inspected and rendered.");
        }

        internal static void Render(GameObject asset, Material material, string output)
        {
            var preview = new PreviewRenderUtility();
            GameObject instance = UnityEngine.Object.Instantiate(asset);
            try
            {
                if (material != null)
                    foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, r.sharedMaterials.Length)).ToArray();
                preview.AddSingleGO(instance);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
                preview.cameraFieldOfView = 32;
                preview.camera.transform.position = bounds.center + new Vector3(1.6f,1.1f,-2.3f).normalized * (radius/Mathf.Sin(16*Mathf.Deg2Rad)*1.12f);
                preview.camera.transform.LookAt(bounds.center);
                preview.camera.nearClipPlane = radius * 0.01f;
                preview.camera.farClipPlane = radius * 12;
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(0.76f, 0.81f, 0.80f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40, -35, 0);
                preview.lights[1].intensity = 0.8f;
                preview.BeginStaticPreview(new Rect(0, 0, 512, 512));
                preview.camera.Render();
                Texture2D result = preview.EndStaticPreview();
                File.WriteAllBytes(output, result.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(result);
            }
            finally { preview.Cleanup(); }
        }
    }
}
