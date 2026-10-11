using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using IndustryTycoon.Core;
using IndustryTycoon.Feedback;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.Workers;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IndustryTycoon.EditorTests
{
    public sealed class AnimatedProductionTests
    {
        [Test]
        public void ImportedCharacters_HaveValidAvatarsLoopingClipsAndBakedRoots()
        {
            foreach (string family in new[] {"Player", "LumberWorker"})
            foreach (string name in new[] {"Idle", "Walk", "Run"})
            {
                string path = $"Assets/Art/Production/Characters/{family}/{name}.fbx";
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Single();
                Assert.IsTrue(avatar.isValid && avatar.isHuman, path);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var clip = importer.clipAnimations.Single();
                Assert.IsTrue(clip.loopTime && clip.lockRootRotation && clip.lockRootHeightY && clip.lockRootPositionXZ, path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
                Assert.Greater(skin.bones.Length, 20);
                Assert.IsTrue(skin.bones.All(b => b != null));
            }
        }

        [Test]
        public void SerializedPresentation_PreservesRootsAndReplacesVisiblePrimitives()
        {
            var scene = EditorSceneManager.OpenScene(IndustryTycoon.Editor.AnimatedProductionIntegration.ScenePath);
            foreach (var root in scene.GetRootGameObjects().Where(g => g.GetComponent<PlayerMovement>() || g.GetComponent<LumberWorker>()))
            {
                var visual = root.transform.Find("VisualRoot");
                Assert.NotNull(visual, root.name);
                var animator = visual.GetComponentInChildren<Animator>(true);
                Assert.IsFalse(animator.applyRootMotion);
                Assert.NotNull(animator.runtimeAnimatorController);
                Assert.Zero(visual.GetComponentsInChildren<Collider>(true).Length);
                Assert.AreEqual(1, visual.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled));
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer && r.GetComponent<TextMesh>() == null))
                    Assert.IsFalse(renderer.enabled, root.name + "/" + renderer.name);
            }
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Player.prefab");
            Assert.NotNull(player.GetComponent<CharacterController>());
            Assert.NotNull(player.transform.Find("VisualRoot"));
            var resource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/WoodCarryVisual.prefab").GetComponent<ResourceVisual>();
            var renderers = resource.PlankRoot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            Assert.AreEqual(1, renderers.Length);
            Assert.That(AssetDatabase.GetAssetPath(renderers[0].GetComponent<MeshFilter>().sharedMesh), Does.EndWith("Plank.fbx"));
            Assert.Zero(resource.PlankRoot.GetComponentsInChildren<Animator>(true).Length);
            var loose = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/WoodResource.prefab");
            Assert.AreEqual(ResourceType.Wood, loose.GetComponent<ResourcePickup>().ResourceType);
            Assert.AreEqual(1, loose.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled));
            Assert.NotNull(loose.transform.Find("Visual/WoodLog VisualRoot"));
        }

        [UnityTest]
        public IEnumerator Runtime_AnimationOwnershipCarryPoolingAndProduction()
        {
            EditorSceneManager.OpenScene(IndustryTycoon.Editor.AnimatedProductionIntegration.ScenePath);
            foreach (var persistence in Object.FindObjectsByType<LocalPersistenceService>(FindObjectsInactive.Include)) persistence.enabled = false;
            // Keep random spawn overlap from starting a pickup flight during the ground-contact probe.
            foreach (var collector in Object.FindObjectsByType<ResourceCollector>(FindObjectsInactive.Include)) collector.enabled = false;
            yield return new EnterPlayMode();
            yield return null;
            Directory.CreateDirectory("Logs/AnimatedAssets");
            // Every instantiated pool slot must carry both replacements, including inactive slots.
            var pooledVisuals = Object.FindObjectsByType<ResourceVisual>(FindObjectsInactive.Include);
            Assert.Greater(pooledVisuals.Length, 20);
            foreach (var selector in pooledVisuals)
            foreach (var root in new[] {selector.WoodRoot, selector.PlankRoot})
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
                Assert.AreEqual(1, renderers.Length, selector.name + "/" + root.name);
                Assert.NotNull(renderers[0].sharedMaterial.GetTexture("_BaseMap"));
                Assert.That(AssetDatabase.GetAssetPath(renderers[0].GetComponent<MeshFilter>().sharedMesh),
                    Does.EndWith(root == selector.WoodRoot ? "WoodLog.fbx" : "Plank.fbx"));
                Assert.Zero(root.GetComponentsInChildren<Animator>(true).Length);
            }
            // Plank has no loose pickup spawn path. Probe the shared presentation in world space
            // without inventing a new pickup/resource owner, and exercise both selector variants.
            var worldProbe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/WoodCarryVisual.prefab"));
            try
            {
                var selector = worldProbe.GetComponent<ResourceVisual>();
                foreach (var type in new[] {ResourceType.Wood, ResourceType.Plank})
                {
                    selector.Show(type);
                    worldProbe.transform.SetPositionAndRotation(new Vector3(3f, 0f, -3f), Quaternion.identity);
                    worldProbe.transform.localScale = Vector3.one;
                    var renderer = worldProbe.GetComponentsInChildren<Renderer>().Single(r => r.enabled);
                    worldProbe.transform.position += Vector3.up * -renderer.bounds.min.y;
                    Assert.That(renderer.bounds.min.y, Is.EqualTo(0f).Within(.001f));
                    Assert.That(renderer.bounds.size.x, Is.EqualTo(1.12f).Within(.02f));
                    Capture(worldProbe, "World-" + type);
                }
            }
            finally { Object.Destroy(worldProbe); }
            foreach (var loose in Object.FindObjectsByType<ResourcePickup>(FindObjectsInactive.Include))
            {
                Assert.AreEqual(1, loose.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled));
                Assert.NotNull(loose.transform.Find("Visual/WoodLog VisualRoot"));
                if (loose.gameObject.activeInHierarchy)
                {
                    var renderer = loose.GetComponentsInChildren<Renderer>().Single(r => r.enabled);
                    Assert.That(renderer.bounds.min.y, Is.InRange(-.05f, .10f), "Loose log ground contact");
                    Capture(loose.gameObject, "Loose-WoodLog");
                    break;
                }
            }
            var player = Object.FindAnyObjectByType<PlayerMovement>();
            player.GetComponent<ResourceCollector>().enabled = false;
            var worker = Object.FindAnyObjectByType<LumberWorker>(FindObjectsInactive.Include);
            worker.gameObject.SetActive(true);
            worker.enabled = false;
            player.enabled = false;
            var report = new System.Text.StringBuilder();
            foreach (var actor in new[] {player.gameObject, worker.gameObject})
            {
                actor.GetComponent<CharacterAnimationFeedback>().enabled = false;
                var animator = actor.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var skin = actor.GetComponentInChildren<SkinnedMeshRenderer>();
                var visual = actor.transform.Find("VisualRoot");
                Vector3 rootPosition = actor.transform.position, localPosition = animator.transform.localPosition;
                Quaternion rootRotation = actor.transform.rotation;
                foreach (float speed in new[] {0f, 3.5f, 5.5f})
                {
                    animator.SetFloat("Speed", speed);
                    Quaternion initial = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation;
                    float motion = 0f, minGround = float.MaxValue, maxGround = float.MinValue;
                    for (int i = 0; i < 90; i++)
                    {
                        animator.Update(1f / 30f);
                        motion = Mathf.Max(motion, Quaternion.Angle(initial, animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation));
                        Bounds bounds = IndustryTycoon.Editor.AnimatedProductionIntegration.SkinBounds(skin);
                        Assert.That(bounds.size.y, Is.InRange(1.25f, 2.1f), actor.name + " rendered height");
                        float floor = bounds.min.y - rootPosition.y;
                        minGround = Mathf.Min(minGround, floor); maxGround = Mathf.Max(maxGround, floor);
                        Assert.Less(Vector3.Distance(rootPosition, actor.transform.position), .0001f);
                        Assert.Less(Quaternion.Angle(rootRotation, actor.transform.rotation), .001f);
                        Assert.Less(Vector3.Distance(localPosition, animator.transform.localPosition), .001f);
                    }
                    Assert.Greater(motion, .05f, actor.name + " clip did not animate");
                    Assert.Greater(minGround, -.15f, actor.name + " penetrated ground");
                    Assert.Less(maxGround, .25f, actor.name + " floats above ground");
                    report.AppendLine($"{actor.name}|speed={speed}|legMotion={motion:F2}|ground={minGround:F3}..{maxGround:F3}|root motion=0");
                    File.WriteAllText("Logs/AnimatedAssets/runtime.txt", report.ToString());
                    Capture(actor, actor.name.Replace(" ", "") + "-" + speed);
                }
                animator.SetFloat("Speed", 0f);
                animator.Update(.1f);
                Vector3 toeDirection = animator.GetBoneTransform(HumanBodyBones.LeftToes).position
                    - animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                float facing = Vector3.Dot(toeDirection.normalized, actor.transform.forward);
                report.AppendLine($"{actor.name}|toe forward alignment={facing:F3}");
                Assert.Greater(facing, .3f, actor.name + " does not face gameplay forward");
            }
            var carry = player.GetComponent<CarryStack>();
            foreach (var type in new[] {ResourceType.Wood, ResourceType.Plank, ResourceType.Plank})
            {
                Assert.IsTrue(carry.TryAdd(type, 6));
                for (int i = 0; i < 20; i++) yield return null;
                var selectors = player.GetComponentsInChildren<ResourceVisual>().Where(v => v.gameObject.activeInHierarchy).ToArray();
                Assert.AreEqual(6, selectors.Length);
                Assert.IsTrue(selectors.All(s => s.DisplayedResourceType == type));
                if (type == ResourceType.Plank)
                    Assert.IsTrue(selectors.All(s => s.PlankRoot.GetComponentsInChildren<Renderer>().Count(r => r.enabled) == 1));
                else Assert.IsTrue(selectors.All(s => s.WoodRoot.GetComponentsInChildren<Renderer>().Count(r => r.enabled) == 1));
                Capture(player.gameObject, "Carry-" + type);
                Assert.IsTrue(carry.TryRemove(type, 6));
            }
            var sale = Object.FindAnyObjectByType<IndustryTycoon.Interaction.SalePoint>();
            var saleFeedback = sale.GetComponent<SalePointFeedback>();
            int salePoolSize = saleFeedback.PoolCount;
            foreach (var type in new[] {ResourceType.Wood, ResourceType.Plank})
            {
                Assert.IsTrue(carry.TryAdd(type, 1));
                float carrySettled = Time.time + .25f;
                while (Time.time < carrySettled) yield return null;
                Assert.IsTrue(sale.TryUnloadOne());
                Assert.AreEqual(0, carry.TotalAmount);
                Assert.IsTrue(sale.GetComponentsInChildren<ResourceVisual>()
                    .Any(v => v.gameObject.activeInHierarchy && v.DisplayedResourceType == type));
                float visibleFlight = Time.time + .08f;
                while (Time.time < visibleFlight) yield return null;
                var flight = sale.GetComponentsInChildren<ResourceVisual>().Single(v => v.gameObject.activeInHierarchy);
                Assert.Greater(flight.GetComponentsInChildren<Renderer>().Single(r => r.enabled).bounds.size.magnitude, .1f);
                Capture(flight.gameObject, "Sale-transfer-" + type);
                float flightDeadline = Time.time + .4f;
                while (Time.time < flightDeadline) yield return null;
                Assert.AreEqual(0, saleFeedback.ActiveFlightCount);
                Assert.AreEqual(salePoolSize, saleFeedback.PoolCount);
            }
            var processor = Object.FindAnyObjectByType<WoodProcessor>(FindObjectsInactive.Include);
            processor.gameObject.SetActive(true);
            Assert.IsTrue(processor.RestoreStableState(0, 0));
            Assert.IsTrue(carry.TryAdd(ResourceType.Wood, 4));
            for (int i = 0; i < 4; i++) Assert.IsTrue(processor.TryTransferInputFrom(carry));
            Assert.Zero(carry.TotalAmount);
            yield return null;
            // Manual input uses the existing numeric buffer label; auto-feeder flights use the shared pool.
            Capture(processor.gameObject, "Processor-input");
            Assert.IsTrue(processor.IsProcessing);
            float timeout = Time.realtimeSinceStartup + 8f;
            while (processor.OutputPlanks < 2 && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.AreEqual(2, processor.OutputPlanks);
            float outputSettled = Time.time + .3f;
            while (Time.time < outputSettled) yield return null;
            var outputs = processor.GetComponentsInChildren<ResourceVisual>().Where(v => v.gameObject.activeInHierarchy).ToArray();
            Assert.IsTrue(outputs.Any(v => v.DisplayedResourceType == ResourceType.Plank));
            Capture(processor.gameObject, "Processor-output", outputs.First(v => v.DisplayedResourceType == ResourceType.Plank)
                .GetComponentsInChildren<Renderer>().Single(r => r.enabled));
            Assert.IsTrue(processor.TryTransferOutputTo(carry));
            Assert.AreEqual(1, carry.GetAmount(ResourceType.Plank));
            carry.TryRemove(ResourceType.Plank, 1);
            player.enabled = true;
            var playerFeedback = player.GetComponent<CharacterAnimationFeedback>();
            playerFeedback.enabled = true;
            var input = player.GetComponent<PlayerDragInput>();
            for (int i = 0; i < 25; i++)
            {
                SetField(input, "_hasReleasedDragSample", true);
                SetField(input, "_releasedDragInput", Vector2.up);
                yield return null;
            }
            Assert.Greater(playerFeedback.Animator.GetFloat("Speed"), .1f, "Player movement did not drive Animator");
            SetField(input, "_hasReleasedDragSample", false);
            float idleDeadline = Time.time + 1.5f;
            while (Time.time < idleDeadline) yield return null;
            Assert.Less(playerFeedback.Animator.GetFloat("Speed"), .1f, "Idle did not resume");
            worker.GetComponent<CharacterAnimationFeedback>().enabled = true;
            worker.enabled = true;
            int deposits = worker.CompletedDepositCount;
            bool carrying = false, walking = false;
            timeout = Time.realtimeSinceStartup + 20f;
            while (worker.CompletedDepositCount == deposits && Time.realtimeSinceStartup < timeout)
            {
                walking |= worker.GetComponent<CharacterAnimationFeedback>().Animator.GetFloat("Speed") > 1f;
                if (worker.IsCarrying && !carrying) { Capture(worker.gameObject, "Worker-carry"); carrying = true; }
                yield return null;
            }
            Assert.IsTrue(walking && carrying, "Worker movement/carry animation was not exercised");
            Assert.Greater(worker.CompletedDepositCount, deposits);
            report.AppendLine("Authoritative player move/idle and worker pickup/carry/deposit animation mapping PASS");
            File.WriteAllText("Logs/AnimatedAssets/runtime.txt", report + "CarryStack reuse, selector and Lumber production/output transfer PASS\n");
            yield return new ExitPlayMode();
        }

        private static void Capture(GameObject actor, string name, Renderer measuredRenderer = null)
        {
            var camera = Camera.main;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            var target = new RenderTexture(540, 960, 24);
            var pixels = new Texture2D(540, 960, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            try
            {
                // Preserve normal gameplay FOV and camera offset; center the audited subject.
                var follow = camera.GetComponent<IndustryTycoon.CameraSystem.SmoothFollowCamera>();
                camera.transform.position = actor.transform.position + follow.Offset;
                camera.transform.LookAt(actor.transform.position + follow.LookAtOffset);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 540, 960), 0, 0);
                pixels.Apply();
                File.WriteAllBytes("Logs/AnimatedAssets/" + name + ".png", pixels.EncodeToPNG());
                bool character = actor.GetComponent<CharacterAnimationFeedback>() != null;
                if (character) measuredRenderer = actor.GetComponentInChildren<SkinnedMeshRenderer>();
                else if (measuredRenderer == null && (actor.GetComponent<ResourceVisual>() != null || actor.GetComponent<ResourcePickup>() != null))
                    measuredRenderer = actor.GetComponentsInChildren<Renderer>().Single(r => r.enabled);
                if (measuredRenderer != null)
                {
                    Color32[] withModel = pixels.GetPixels32();
                    measuredRenderer.enabled = false;
                    camera.Render();
                    pixels.ReadPixels(new Rect(0, 0, 540, 960), 0, 0); pixels.Apply();
                    measuredRenderer.enabled = true;
                    Color32[] withoutModel = pixels.GetPixels32();
                    int changed = 0;
                    for (int i = 0; i < withModel.Length; i++)
                        if (Mathf.Abs(withModel[i].r - withoutModel[i].r) + Mathf.Abs(withModel[i].g - withoutModel[i].g) + Mathf.Abs(withModel[i].b - withoutModel[i].b) > 20) changed++;
                    Assert.Greater(changed, character ? 100 : 10, name + " not readable in normal camera");
                }
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
            }
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
