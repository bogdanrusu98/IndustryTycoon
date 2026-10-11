using System;
using System.IO;
using System.Linq;
using IndustryTycoon.Feedback;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Workers;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>Production presentation only; gameplay roots and resource ownership are untouched.</summary>
    public static class AnimatedProductionIntegration
    {
        public const string Root = "Assets/Art/Production/";
        public const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";

        public static void AuditGameplay()
        {
            const string baseline = "Assets/AnimatedAssetBaseline.unity";
            File.Copy("Logs/AnimatedAssets/baseline.unity", baseline, true);
            AssetDatabase.ImportAsset(baseline);
            try
            {
                var signature = typeof(ExhaustiveAssetUsageAudit).GetMethod("GameplaySignature",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                string[] Read(string path)
                {
                    EditorSceneManager.OpenScene(path);
                    return ((string[])signature.Invoke(null, null))
                        .Where(s => !s.Contains("IndustryTycoon.Feedback.CharacterAnimationFeedback"))
                        .Select(s => s.Replace("Player/VisualRoot:UnityEngine.Transform", "Player/Capsule Placeholder:UnityEngine.Transform"))
                        .ToArray();
                }
                string[] before = Read(baseline), after = Read(ScenePath);
                File.WriteAllLines("Logs/AnimatedAssets/gameplay-before.txt", before);
                File.WriteAllLines("Logs/AnimatedAssets/gameplay-after.txt", after);
                if (!before.SequenceEqual(after)) throw new InvalidOperationException("Unexpected gameplay signature changes; inspect audit differences.");
                File.WriteAllText("Logs/AnimatedAssets/gameplay-safety.txt", $"PASS: {before.Length} component/root records unchanged. Only CharacterAnimationFeedback additions and cash visual pop target remapping excluded.\n");
            }
            finally { AssetDatabase.DeleteAsset(baseline); }
        }

        [MenuItem("Industry Tycoon/Production/Integrate Animated Exports")]
        public static void Integrate()
        {
            foreach (string path in AssetDatabase.FindAssets("t:Model", new[] {Root + "Characters"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                M19ProductionAssetPostprocessor.ConfigureCharacterClips(importer);
                importer.SaveAndReimport();
            }
            ControllerFor("Player");
            ControllerFor("LumberWorker");
            foreach (string name in new[] {"WoodCarryVisual", "WoodResource", "Player"})
            {
                string path = $"Assets/Game/Prefabs/{name}.prefab";
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (name == "Player") AttachCharacter(prefab, "Player");
                    else if (name == "WoodResource") ReplaceLooseWood(prefab.transform.Find("Visual"));
                    else ReplaceResources(prefab.GetComponent<ResourceVisual>());
                    PrefabUtility.SaveAsPrefabAsset(prefab, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            Apply(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Inspect();
        }

        public static void Apply(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var player in root.GetComponentsInChildren<PlayerMovement>(true))
                    AttachCharacter(player.gameObject, "Player");
                foreach (var worker in root.GetComponentsInChildren<LumberWorker>(true))
                    AttachCharacter(worker.gameObject, "LumberWorker");
                foreach (var resource in root.GetComponentsInChildren<ResourceVisual>(true)) ReplaceResources(resource);
                foreach (var feedback in root.GetComponentsInChildren<WoodProcessorFeedback>(true))
                {
                    var serialized = new SerializedObject(feedback);
                    var output = (Transform)serialized.FindProperty("outputVisualRoot").objectReferenceValue;
                    var zone = feedback.GetComponentInChildren<ProcessorOutputZone>(true);
                    // The new thin planks were completely occluded inside the production building.
                    // Display them beside the existing pickup zone; leave the buffer/trigger untouched.
                    if (output != null && zone != null) output.position = zone.transform.position + Vector3.up * .25f;
                }
            }
            Transform visual = scene.GetRootGameObjects().First(g => g.GetComponent<PlayerMovement>() != null).transform.Find("VisualRoot");
            foreach (var feedback in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CashPileFeedback>(true)))
            {
                var serialized = new SerializedObject(feedback);
                serialized.FindProperty("collectionPopTarget").objectReferenceValue = visual;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static void ReplaceResources(ResourceVisual resource)
        {
            ReplaceStatic(resource.PlankRoot.transform, "Plank", 1.12f);
            ReplaceStatic(resource.WoodRoot.transform, "WoodLog", 1.12f);
        }

        public static void ReplaceLooseWood(Transform visual) => ReplaceStatic(visual, "WoodLog", 1.12f);

        private static void ReplaceStatic(Transform target, string family, float size)
        {
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Transform visual = target.Find(family + " VisualRoot");
            GameObject model;
            if (visual == null)
            {
                visual = new GameObject(family + " VisualRoot").transform;
                visual.SetParent(target, false);
                var facing = new GameObject("Model Facing").transform;
                facing.SetParent(visual, false);
                model = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Root + $"Resources/{family}/{family}.fbx"));
                model.transform.SetParent(facing, false);
                model.name = family + " Production Visual";
            }
            else model = visual.GetChild(0).GetChild(0).gameObject;
            visual.localPosition = Vector3.zero;
            visual.localScale = Vector3.one;
            Material material = MaterialFor($"Resources/{family}/{family}");
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Normalize in the resource's local space, so pool/carry/flight scale is inherited.
            var filter = model.GetComponentInChildren<MeshFilter>(true);
            Bounds local = filter.sharedMesh.bounds;
            var bounds = new Bounds(target.InverseTransformPoint(filter.transform.TransformPoint(local.center)), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(target.InverseTransformPoint(filter.transform.TransformPoint(
                    local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)))));
            float scale = size / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            visual.localScale = Vector3.one * scale;
            visual.localPosition = -bounds.center * scale;
        }

        public static void AttachCharacter(GameObject gameplay, string family)
        {
            // Preserve original presentation anchors referenced by feedback and every gameplay component.
            foreach (var t in gameplay.GetComponentsInChildren<Transform>(true))
                if (t.name == "Capsule Placeholder" || t.name == "Worker Capsule" ||
                    t.name == "Facing Marker" || t.name == "Worker Facing Marker")
                    foreach (var renderer in t.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + $"Characters/{family}/Idle.fbx");
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Root + $"Characters/{family}/Idle.fbx").OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException(family + " invalid humanoid avatar");
            var visual = gameplay.transform.Find("VisualRoot")?.gameObject ?? new GameObject("VisualRoot");
            visual.transform.SetParent(gameplay.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            var model = visual.transform.childCount > 0 ? visual.transform.GetChild(0).gameObject
                : (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(visual.transform, false);
            model.name = family + " Animated Production Model";
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                // Exporter's separate unskinned origin marker is not part of the character.
                renderer.enabled = renderer.name != "Icosphere";
                renderer.sharedMaterials = Enumerable.Repeat(MaterialFor($"Characters/{family}/{family}"), renderer.sharedMaterials.Length).ToArray();
            }
            var animator = model.GetComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = ControllerFor(family);
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            // Use a sampled idle clone: skinned animation bounds include motion envelopes,
            // and the bind-pose feet differ from the Humanoid retargeted idle pose.
            var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            Bounds bounds = BindBounds(skin);
            float height = family == "Player" ? 1.85f : 1.70f;
            visual.transform.localScale = Vector3.one * (height / bounds.size.y);
            bounds = BindBounds(skin);
            visual.transform.position += gameplay.transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var preview = Object.Instantiate(model);
            try
            {
                preview.SetActive(true);
                preview.transform.SetPositionAndRotation(Vector3.zero, model.transform.rotation);
                preview.transform.localScale = model.transform.lossyScale;
                var previewAnimator = preview.GetComponent<Animator>();
                previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                previewAnimator.Rebind();
                previewAnimator.Update(0f);
                float idleGround = SkinBounds(preview.GetComponentInChildren<SkinnedMeshRenderer>()).min.y;
                visual.transform.position = new Vector3(visual.transform.position.x,
                    gameplay.transform.position.y - idleGround, visual.transform.position.z);
            }
            finally { Object.DestroyImmediate(preview); }
            var feedback = gameplay.GetComponent<CharacterAnimationFeedback>() ?? gameplay.AddComponent<CharacterAnimationFeedback>();
            feedback.Configure(animator, gameplay.GetComponent<PlayerMovement>(), gameplay.GetComponent<LumberWorker>());
        }

        private static Bounds BindBounds(SkinnedMeshRenderer skin)
        {
            Bounds local = skin.sharedMesh.bounds;
            var result = new Bounds(skin.transform.TransformPoint(local.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(skin.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
            return result;
        }

        internal static Bounds SkinBounds(SkinnedMeshRenderer skin)
        {
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh, true);
                Vector3[] vertices = mesh.vertices;
                var bounds = new Bounds(skin.transform.TransformPoint(vertices[0]), Vector3.zero);
                foreach (var vertex in vertices) bounds.Encapsulate(skin.transform.TransformPoint(vertex));
                return bounds;
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static RuntimeAnimatorController ControllerFor(string family)
        {
            string folder = Root + $"Characters/{family}/";
            string path = folder + family + ".controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)
                ?? AnimatorController.CreateAnimatorControllerAtPath(path);
            if (!controller.parameters.Any(p => p.name == "Speed"))
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var state = controller.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == "Locomotion").state
                ?? controller.layers[0].stateMachine.AddState("Locomotion");
            var tree = state.motion as BlendTree;
            if (tree == null)
            {
                tree = new BlendTree { name = "Authoritative movement speed", blendParameter = "Speed", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
            }
            tree.children = Array.Empty<ChildMotion>();
            string[] names = {"Idle", "Walk", "Run"};
            float[] speeds = {0f, 3.5f, 5.5f};
            for (int i = 0; i < names.Length; i++)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(folder + names[i] + ".fbx").OfType<AnimationClip>()
                    .Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                tree.AddChild(clip, speeds[i]);
            }
            state.motion = tree;
            controller.layers[0].stateMachine.defaultState = state;
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Material MaterialFor(string key)
        {
            string name = Path.GetFileName(key);
            string path = Root + $"Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + key + "_BaseColor.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + key + "_MetallicSmoothness.png"));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_Metallic", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void Inspect()
        {
            Directory.CreateDirectory("Logs/AnimatedAssets");
            using var report = new StreamWriter("Logs/AnimatedAssets/import.txt");
            foreach (string path in AssetDatabase.FindAssets("t:Model", new[] {Root + "Characters", Root + "Resources/Plank", Root + "Resources/WoodLog"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                report.WriteLine(path);
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    report.WriteLine($" NODE {t.name} p={t.localPosition:F4} r={t.localEulerAngles:F2} s={t.localScale:F4}");
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    report.WriteLine($" RENDERER {r.name} {r.GetType().Name} bounds={r.bounds} materials={r.sharedMaterials.Length}");
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (a is Avatar avatar) report.WriteLine($" AVATAR {avatar.name} valid={avatar.isValid} human={avatar.isHuman}");
                    if (a is AnimationClip clip) report.WriteLine($" CLIP {clip.name} length={clip.length} human={clip.isHumanMotion} root={clip.hasRootCurves}");
                    if (a is Mesh mesh) report.WriteLine($" MESH {mesh.name} vertices={mesh.vertexCount} triangles={mesh.triangles.Length / 3} bounds={mesh.bounds}");
                }
            }
        }
    }
}
