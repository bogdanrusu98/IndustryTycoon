using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Core;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndustryTycoon.Editor
{
    /// <summary>Independent source census and non-isolated, normal-camera Play Mode visibility proof.</summary>
    [InitializeOnLoad]
    public static class ExhaustiveAssetUsageAudit
    {
        public const string Output = "Logs/AssetUsageAudit";
        private const string Key = "IndustryTycoon.ExhaustiveAudit";
        private static int frame, shot;
        private static Camera camera;
        private static RenderTexture target;
        private static Texture2D pixels;
        private static readonly List<string> evidence = new List<string>();
        private static readonly string[] Targets = {
            "Processor Production Visual", "SawBlade Production Visual", "ToolWorkshop Production Visual",
            "Recovered Smelter Production Visual", "Recovered Packing Production Visual", "Packing Parcel Production Visual",
            "Warehouse Production Visual", "AssemblyPlant Production Visual", "OreHopper Production Visual",
            "MineEntrance Production Visual", "MineRockA Production Visual", "MineRockB Production Visual",
            "DrillBit Production Visual", "Courier Production Visual", "Front Left Wheel", "Front Right Wheel",
            "Rear Left Wheel", "Rear Right Wheel", "Crate Visual", "Iron Ore Visual", "Iron Bar Visual", "Tool Visual",
            "MineBarrier Production Visual", "IndustrialGate Production Visual",
            "Saw One Station Production Visual", "Saw One Blade Production Visual",
            "Saw Two (Unlock) Station Production Visual", "Saw Two (Unlock) Blade Production Visual",
            "Iron Vein Production Visual", "Stored Ore Production Visual",
            "Escarpment Model 0", "Escarpment Model 1", "Escarpment Model 2",
            "Escarpment Model 3", "Escarpment Model 4", "Escarpment Model 5"
        };

        static ExhaustiveAssetUsageAudit()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state => {
                if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + "Done", false)) return;
                SessionState.SetBool(Key + "Done", false);
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetBool(Key + "Failed", false) ? 1 : 0);
            };
        }

        public static void InspectSources()
        {
            Directory.CreateDirectory(Output);
            ModernAssetRecoveryAudit.RunTo(Output + "/Sources");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity");
            Inventory("before");
        }

        public static void FinalizeStaticAudit()
        {
            ModernAssetRecoveryAudit.RunTo(Output + "/Sources");
            CompareGameplay();
            Inventory("after");
            Debug.Log("EXHAUSTIVE STATIC AUDIT PASS.");
        }

        public static void Inventory(string suffix)
        {
            var rows = new List<string>();
            foreach (Renderer r in Objects().SelectMany(g => g.GetComponents<Renderer>()))
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                rows.Add($"{PathOf(r.transform)}|active={r.gameObject.activeInHierarchy}|enabled={r.enabled}|position={r.transform.position:F3}|rotation={r.transform.eulerAngles:F3}|scale={r.transform.lossyScale:F3}|bounds={r.bounds}|mesh={(filter != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "text/particle")}|materials={string.Join(",",r.sharedMaterials.Select(AssetDatabase.GetAssetPath))}");
            }
            File.WriteAllLines(Output + "/renderers-" + suffix + ".txt", rows);
            // Deliberately excludes art transform values, includes every gameplay root and physics shape.
            var safety = new List<string>();
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                safety.Add($"ROOT|{g.name}|{g.transform.position:F6}|{g.transform.rotation:F6}|{g.transform.localScale:F6}");
            foreach (Collider c in Objects().SelectMany(g => g.GetComponents<Collider>()))
                safety.Add($"COLLIDER|{PathOf(c.transform)}|{c.transform.position:F6}|{c.transform.rotation:F6}|{c.transform.lossyScale:F6}|{EditorJsonUtility.ToJson(c)}");
            foreach (MonoBehaviour b in Objects().SelectMany(g => g.GetComponents<MonoBehaviour>()))
                if (b != null) safety.Add($"SCRIPT|{PathOf(b.transform)}|{b.GetType().FullName}");
            File.WriteAllLines(Output + "/safety-" + suffix + ".txt", safety.OrderBy(s => s));
            var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art/Production" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(p => {
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
                    return p + "|shader=" + m.shader.name + "|" + string.Join(";", m.GetTexturePropertyNames().Select(n => n + "=" + AssetDatabase.GetAssetPath(m.GetTexture(n))));
                });
            File.WriteAllLines(Output + "/production-materials.txt", materials);
        }

        public static void CompareGameplay()
        {
            const string temporary = "Assets/ExhaustiveBaseline.unity";
            File.Copy(Output + "/baseline.unity", temporary, true);
            AssetDatabase.ImportAsset(temporary, ImportAssetOptions.ForceSynchronousImport);
            try
            {
                EditorSceneManager.OpenScene(temporary);
                string[] before = GameplaySignature();
                EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity");
                string[] after = GameplaySignature();
                File.WriteAllLines(Output + "/gameplay-before.txt", before);
                File.WriteAllLines(Output + "/gameplay-after.txt", after);
                if (!before.SequenceEqual(after)) throw new InvalidOperationException("Gameplay serialized state changed; inspect gameplay-before/after.txt.");
                File.WriteAllText(Output + "/gameplay-safety.txt", "PASS: " + before.Length + " normalized serialized component/root records identical. All MonoBehaviour configuration/references, colliders, rigidbodies and scene root transforms preserved.\n");
            }
            finally { AssetDatabase.DeleteAsset(temporary); }
        }

        private static string[] GameplaySignature()
        {
            string Reference(int id)
            {
                UnityEngine.Object obj = EditorUtility.EntityIdToObject(id);
                if (obj == null) return "null";
                if (obj is Component c && c.gameObject.scene.IsValid()) return PathOf(c.transform) + ":" + c.GetType().FullName;
                if (obj is GameObject g && g.scene.IsValid()) return PathOf(g.transform);
                return AssetDatabase.GetAssetPath(obj) + ":" + obj.name;
            }
            var rows = new List<string>();
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                rows.Add($"ROOT|{g.name}|{g.transform.position:F6}|{g.transform.rotation:F6}|{g.transform.localScale:F6}");
            foreach (Component c in Objects().SelectMany(g=>g.GetComponents<Component>()).Where(c=>c is MonoBehaviour || c is Collider || c is Rigidbody))
            {
                string json = Regex.Replace(EditorJsonUtility.ToJson(c), "\"instanceID\":(-?[0-9]+)", m => "\"reference\":\"" + Reference(int.Parse(m.Groups[1].Value)) + "\"");
                rows.Add(PathOf(c.transform) + "|" + c.GetType().FullName + "|" + json);
            }
            return rows.OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        }

        public static void RebuildAndRun()
        {
            LumberCampPrototypeBuilder.BuildFromCommandLine();
            Run();
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output + "/Runtime");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Prototype_LumberCamp.unity");
            Inventory("after");
            UnityEngine.Object.FindAnyObjectByType<LocalPersistenceService>(FindObjectsInactive.Include).enabled = false;
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + "Done", false);
            SessionState.SetBool(Key + "Failed", false);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            try
            {
                frame++;
                if (frame == 30)
                {
                    camera = Camera.main;
                    target = new RenderTexture(540, 960, 24, RenderTextureFormat.ARGB32);
                    target.Create(); camera.targetTexture = target;
                    pixels = new Texture2D(540, 960, TextureFormat.RGB24, false);
                    Canvas canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                    ModernVisualCapture.ApplyFixture(true);
                    evidence.Add("target|hierarchy|active|renderer|layer|culling|frustum|visiblePixels|material|bounds|rotation|lossyScale|cameraPosition|cameraFOV");
                }
                if (frame < 60) return;
                if ((frame - 60) % 45 == 0) Setup();
                if ((frame - 60) % 45 != 44) return;
                Probe();
                if (++shot < Targets.Length) return;
                File.WriteAllLines(Output + "/Runtime/visibility.tsv", evidence);
                Inventory("runtime");
                VerifyGates();
                Debug.Log("EXHAUSTIVE AUDIT PASS: " + Targets.Length + " normal-camera visibility probes and authoritative gating checks.");
                Stop(false);
            }
            catch (Exception ex) { Debug.LogException(ex); Stop(true); }
        }

        private static GameObject Target()
        {
            if (shot >= 18 && shot <= 21)
                return Find("Player").GetComponentsInChildren<Transform>(true).First(t => t.name == Targets[shot] && t.gameObject.activeInHierarchy).gameObject;
            return Find(Targets[shot]);
        }

        private static void Setup()
        {
            UnityEngine.Object.FindAnyObjectByType<MobilePanelNavigation>().CloseAll();
            if (shot == 22) ModernVisualCapture.ApplyFixture(true, true);
            if (shot == 23) ModernVisualCapture.ApplyFixture(false);
            if (shot == 24) ModernVisualCapture.ApplyFixture(true);
            var player = Find("Player");
            if (shot >= 18 && shot <= 21)
            {
                CarryStack carry = player.GetComponent<CarryStack>();
                CharacterController cc = player.GetComponent<CharacterController>();
                cc.enabled=false; player.transform.position=new Vector3(0,0,-8); cc.enabled=true;
                carry.RestoreStableState(new[] { ResourceType.Crate, ResourceType.IronOre, ResourceType.IronBar, ResourceType.Tool }[shot - 18], 3);
            }
            GameObject focus = Target();
            if (focus == null) throw new InvalidOperationException("Missing audit target " + Targets[shot]);
            Renderer r = focus.GetComponentsInChildren<Renderer>(true).First(x => x.enabled);
            Vector3 p = shot >= 18 && shot <= 21 ? new Vector3(0,0,-8) : new Vector3(r.bounds.center.x,0,r.bounds.center.z - 3);
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false; player.transform.position = p; controller.enabled = true;
            camera.GetComponent<SmoothFollowCamera>().SnapToTarget();
            foreach (string n in new[] { "Welcome Back Overlay", "Major Unlock Banner", "Achievement Toast", "Completion Banner" }) Find(n)?.SetActive(false);
        }

        private static Color32[] Render()
        {
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0,0,540,960),0,0); pixels.Apply(); RenderTexture.active = previous;
            return pixels.GetPixels32();
        }

        private static void Probe()
        {
            GameObject focus = Target();
            Renderer[] renderers = focus.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            Color32[] baseline = Render();
            File.WriteAllBytes(Output + "/Runtime/" + shot.ToString("D2") + ".png", pixels.EncodeToPNG());
            // Differential image proof detects actual visible fragments including occlusion by other art/HUD.
            // No camera, layer, material, gating or ancestor is changed for the baseline rendering.
            foreach (Renderer r in renderers) r.enabled = false;
            Color32[] without = Render();
            foreach (Renderer r in renderers) r.enabled = true;
            int changed = 0;
            for (int i=0;i<baseline.Length;i++)
                if (Math.Abs(baseline[i].r-without[i].r)+Math.Abs(baseline[i].g-without[i].g)+Math.Abs(baseline[i].b-without[i].b)>20) changed++;
            foreach (Renderer r in renderers)
            {
                evidence.Add($"{Targets[shot]}|{PathOf(r.transform)}|{r.gameObject.activeInHierarchy}|{r.enabled}|{r.gameObject.layer}|{(camera.cullingMask & (1<<r.gameObject.layer))!=0}|{GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),r.bounds)}|{changed}|{AssetDatabase.GetAssetPath(r.sharedMaterial)}|{r.bounds}|{r.transform.eulerAngles:F3}|{r.transform.lossyScale:F3}|{camera.transform.position:F3}|{camera.fieldOfView}");
                if (!r.gameObject.activeInHierarchy || changed < 20 || (camera.cullingMask & (1<<r.gameObject.layer)) == 0
                    || !GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), r.bounds)
                    || !AssetDatabase.GetAssetPath(r.sharedMaterial).StartsWith("Assets/Art/Production/Materials/"))
                    throw new InvalidOperationException("Runtime production visibility failed: " + Targets[shot] + ", pixels=" + changed);
            }
            File.WriteAllLines(Output + "/Runtime/visibility.tsv", evidence);
        }

        private static void VerifyGates()
        {
            void Check(string name, bool active)
            {
                if (Find(name).activeInHierarchy != active)
                    throw new InvalidOperationException("Incorrect production gating: " + name);
            }
            ModernVisualCapture.ApplyFixture(false);
            Check("Saw One Station Production Visual", true);
            Check("Saw Two (Unlock) Station Production Visual", false);
            Check("Saw Two (Unlock) Blade Production Visual", false);
            Check("MineEntrance Production Visual", false);
            Check("AssemblyPlant Production Visual", false);
            Check("IndustrialGate Production Visual", true);
            ModernVisualCapture.ApplyFixture(true, true);
            Check("MineBarrier Production Visual", true);
            Check("MineEntrance Production Visual", false);
            ModernVisualCapture.ApplyFixture(true);
            Check("MineBarrier Production Visual", false);
            Check("IndustrialGate Production Visual", false);
            Check("Saw Two (Unlock) Station Production Visual", true);
            Check("Stored Ore Production Visual", true);
            UnityEngine.Object.FindAnyObjectByType<IndustryTycoon.Mining.OreStorage>().RestoreStableState(0);
            Check("Stored Ore Production Visual", false);
            File.WriteAllText(Output + "/Runtime/gating.txt", "PASS: fresh / mine-locked / fully developed states; second cutter and blade follow unlock; mine art follows MineUnlock; industrial art follows blocker; stored ore follows storage feedback.\n");
        }

        private static IEnumerable<GameObject> Objects() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject);
        private static GameObject Find(string name) => Objects().FirstOrDefault(g=>g.name==name);
        internal static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        private static void Stop(bool failed)
        {
            SessionState.SetBool(Key,false); SessionState.SetBool(Key+"Done",true); SessionState.SetBool(Key+"Failed",failed);
            EditorApplication.update-=Tick; EditorApplication.ExitPlaymode();
        }
    }
}
