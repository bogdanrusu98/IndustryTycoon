using System;
using System.Collections.Generic;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
using IndustryTycoon.Player;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.UI;
using IndustryTycoon.Workers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    public static class LumberCampPrototypeBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string LegacyScenePath = "Assets/Scenes/Prototype_LumberCamp.unity";
        private const string MissingSampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PrefabFolder = "Assets/Game/Prefabs";
        private const string MaterialFolder = PrefabFolder + "/Materials";
        private const string WoodVisualPrefabPath = PrefabFolder + "/WoodCarryVisual.prefab";
        private const string WoodResourcePrefabPath = PrefabFolder + "/WoodResource.prefab";
        private const string CashVisualPrefabPath = PrefabFolder + "/CashBundleVisual.prefab";
        private const string PlayerPrefabPath = PrefabFolder + "/Player.prefab";

        private readonly struct FeedbackServices
        {
            public FeedbackServices(AudioFeedback audio, HapticFeedback haptics)
            {
                Audio = audio;
                Haptics = haptics;
            }

            public AudioFeedback Audio { get; }
            public HapticFeedback Haptics { get; }
        }

        [MenuItem("Industry Tycoon/Prototype/Rebuild Lumber Camp")]
        private static void RebuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Lumber Camp Prototype",
                    "Exit Play Mode before rebuilding the prototype.",
                    "OK");
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!IsGeneratedScratchScene(activeScene)
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            SceneAsset existingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existingScene != null && !EditorUtility.DisplayDialog(
                    "Rebuild Lumber Camp?",
                    "This will replace the generated prototype scene and prefabs.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            BuildPrototype();
        }

        [MenuItem("Industry Tycoon/Prototype/Validate Lumber Camp")]
        private static void ValidateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ValidateSavedPrototype(true);
        }

        public static void BuildFromCommandLine()
        {
            BuildPrototype();
        }

        public static void ValidateFromCommandLine()
        {
            ValidateSavedPrototype(false);
        }

        private static void BuildPrototype()
        {
            EnsureProjectFolders();
            MigrateLegacyPrototypeScene();

            Material groundMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Ground.mat",
                new Color(0.28f, 0.50f, 0.22f),
                0f);
            Material barkMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Wood_Bark.mat",
                new Color(0.33f, 0.15f, 0.055f),
                0.12f);
            Material cutMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Wood_Cut.mat",
                new Color(0.76f, 0.49f, 0.22f),
                0.2f);
            Material playerMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Player.mat",
                new Color(0.17f, 0.48f, 0.92f),
                0.35f);
            Material playerAccentMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Player_Accent.mat",
                new Color(1f, 0.78f, 0.17f),
                0.25f);
            Material saleMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Sale_Point.mat",
                new Color(0.95f, 0.48f, 0.10f),
                0.2f);
            Material cashMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Cash.mat",
                new Color(0.16f, 0.68f, 0.30f),
                0.15f);
            Material cashAccentMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Cash_Accent.mat",
                new Color(1f, 0.84f, 0.22f),
                0.25f);
            Material purchaseMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Purchase_Pad.mat",
                new Color(0.45f, 0.22f, 0.92f),
                0.25f);
            Material purchaseCompleteMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Purchase_Complete.mat",
                new Color(0.15f, 0.78f, 0.62f),
                0.3f);
            Material sawBaseMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Saw_Base.mat",
                new Color(0.78f, 0.20f, 0.09f),
                0.2f);
            Material sawBladeMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Saw_Blade.mat",
                new Color(0.68f, 0.72f, 0.76f),
                0.65f);
            Material workerMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Worker.mat",
                new Color(0.10f, 0.68f, 0.80f),
                0.32f);
            Material stockpileMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Wood_Stockpile.mat",
                new Color(0.52f, 0.27f, 0.08f),
                0.18f);
            Material feedbackParticleMaterial = CreateOrUpdateParticleMaterial(
                MaterialFolder + "/Feedback_Particle.mat");

            GameObject woodVisualPrefab = BuildWoodVisualPrefab(barkMaterial, cutMaterial);
            BuildWoodResourcePrefab(barkMaterial, cutMaterial);
            GameObject cashVisualPrefab = BuildCashVisualPrefab(cashMaterial, cashAccentMaterial);
            GameObject playerPrefab = BuildPlayerPrefab(
                playerMaterial,
                playerAccentMaterial,
                woodVisualPrefab,
                feedbackParticleMaterial);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureEnvironment();

            CreateGround(scene, groundMaterial);
            CreateWalkableBounds(scene);
            GameObject player = InstantiatePrefabInScene(playerPrefab, scene);
            player.name = "Player";
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            UnityEngine.Camera mainCamera = CreateCamera(scene, player.transform);
            SmoothFollowCamera followCamera = mainCamera.GetComponent<SmoothFollowCamera>();
            PlayerMovement playerMovement = player.GetComponent<PlayerMovement>();
            SetObjectReference(playerMovement, "movementCamera", mainCamera.transform);

            FeedbackServices feedbackServices = CreateFeedbackServices(scene);
            PlayerPickupFeedback pickupFeedback = player.GetComponent<PlayerPickupFeedback>();
            SetObjectReference(pickupFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(pickupFeedback, "hapticFeedback", feedbackServices.Haptics);

            WoodSpawner woodSpawner = CreateWoodSpawner(scene);
            Wallet wallet = player.GetComponent<Wallet>();
            CharacterController playerCollider = player.GetComponent<CharacterController>();
            Transform cashCollectionTarget = player.transform.Find("Cash Collection Target");
            CashPile cashPile = CreateCashPile(
                scene,
                wallet,
                playerCollider,
                cashCollectionTarget,
                cashVisualPrefab,
                cashMaterial,
                cashAccentMaterial,
                feedbackParticleMaterial,
                feedbackServices,
                player.transform.Find("Capsule Placeholder"));
            CreateSalePoint(
                scene,
                player.GetComponent<CarryStack>(),
                cashPile,
                playerCollider,
                saleMaterial,
                cutMaterial,
                woodVisualPrefab,
                feedbackParticleMaterial,
                feedbackServices.Audio);
            PurchasePad purchasePad = CreatePurchasePad(
                scene,
                "Purchase Pad",
                "SECOND SAW",
                new Vector3(4.25f, 0f, -4.5f),
                120,
                true,
                wallet,
                playerCollider,
                purchaseMaterial,
                purchaseCompleteMaterial,
                cashAccentMaterial,
                cashVisualPrefab,
                cashCollectionTarget,
                feedbackParticleMaterial,
                feedbackServices.Audio);
            WoodProductionUpgrade productionUpgrade = CreateProductionStation(
                scene,
                purchasePad,
                woodSpawner,
                sawBaseMaterial,
                sawBladeMaterial,
                playerAccentMaterial,
                feedbackParticleMaterial,
                feedbackServices,
                followCamera);
            WoodStockpile stockpile = CreateWoodStockpile(
                scene,
                player.GetComponent<CarryStack>(),
                playerCollider,
                woodVisualPrefab,
                stockpileMaterial,
                cutMaterial,
                feedbackParticleMaterial);
            CreateWorkerAutomation(
                scene,
                productionUpgrade,
                woodSpawner,
                stockpile,
                wallet,
                playerCollider,
                workerMaterial,
                playerAccentMaterial,
                purchaseMaterial,
                purchaseCompleteMaterial,
                cashAccentMaterial,
                cashVisualPrefab,
                cashCollectionTarget,
                woodVisualPrefab,
                feedbackParticleMaterial,
                feedbackServices,
                followCamera);
            CreateLighting(scene);
            CreateHud(scene, player.GetComponent<CarryStack>(), wallet);

            ConfigurePortraitSettings();
            UpdateBuildSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Unable to save prototype scene at {ScenePath}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateSceneContents(scene);

            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Selection.activeObject = sceneAsset;
            EditorGUIUtility.PingObject(sceneAsset);
            Debug.Log($"Lumber Camp prototype rebuilt successfully: {ScenePath}");
        }

        private static void EnsureProjectFolders()
        {
            EnsureFolder("Assets/Game/Core");
            EnsureFolder("Assets/Game/Player");
            EnsureFolder("Assets/Game/Resources");
            EnsureFolder("Assets/Game/Economy");
            EnsureFolder("Assets/Game/Interaction");
            EnsureFolder("Assets/Game/Feedback");
            EnsureFolder("Assets/Game/Camera");
            EnsureFolder("Assets/Game/UI");
            EnsureFolder("Assets/Game/Workers");
            EnsureFolder(PrefabFolder);
            EnsureFolder("Assets/Game/Scenes");
            EnsureFolder("Assets/Game/Editor");
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separatorIndex = path.LastIndexOf('/');
            if (separatorIndex <= 0)
            {
                throw new InvalidOperationException($"Invalid asset folder path: {path}");
            }

            string parent = path.Substring(0, separatorIndex);
            string folderName = path.Substring(separatorIndex + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static void MigrateLegacyPrototypeScene()
        {
            SceneAsset destinationScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            SceneAsset legacyScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(LegacyScenePath);
            if (destinationScene != null || legacyScene == null)
            {
                return;
            }

            string error = AssetDatabase.MoveAsset(LegacyScenePath, ScenePath);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException(
                    $"Could not move the legacy prototype scene to {ScenePath}: {error}");
            }
        }

        private static Material CreateOrUpdateMaterial(string path, Color color, float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    throw new InvalidOperationException("The URP Lit shader is unavailable.");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateOrUpdateParticleMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                {
                    throw new InvalidOperationException("The URP Particles Unlit shader is unavailable.");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            Color color = Color.white;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject BuildWoodVisualPrefab(Material barkMaterial, Material cutMaterial)
        {
            GameObject root = new GameObject("WoodCarryVisual");
            try
            {
                CreateWoodGeometry(root.transform, barkMaterial, cutMaterial);
                root.transform.localScale = Vector3.one * 0.7f;
                return SavePrefabAndReload(root, WoodVisualPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildCashVisualPrefab(
            Material cashMaterial,
            Material accentMaterial)
        {
            GameObject root = new GameObject("CashBundleVisual");
            try
            {
                GameObject bills = CreatePrimitiveChild(
                    "Bills",
                    PrimitiveType.Cube,
                    root.transform,
                    cashMaterial);
                bills.transform.localScale = new Vector3(0.62f, 0.09f, 0.38f);

                GameObject band = CreatePrimitiveChild(
                    "Band",
                    PrimitiveType.Cube,
                    root.transform,
                    accentMaterial);
                band.transform.localPosition = new Vector3(0f, 0.055f, 0f);
                band.transform.localScale = new Vector3(0.13f, 0.10f, 0.40f);
                return SavePrefabAndReload(root, CashVisualPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ResourcePickup BuildWoodResourcePrefab(Material barkMaterial, Material cutMaterial)
        {
            GameObject root = new GameObject("WoodResource");
            try
            {
                SphereCollider pickupCollider = root.AddComponent<SphereCollider>();
                pickupCollider.isTrigger = true;
                pickupCollider.center = new Vector3(0f, 0.05f, 0f);
                pickupCollider.radius = 0.62f;

                ResourcePickup pickup = root.AddComponent<ResourcePickup>();
                pickup.Configure(ResourceType.Wood, 1);

                GameObject visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                CreateWoodGeometry(visual.transform, barkMaterial, cutMaterial);

                GameObject savedPrefab = SavePrefabAndReload(root, WoodResourcePrefabPath);
                ResourcePickup savedPickup = savedPrefab.GetComponent<ResourcePickup>();
                Require(savedPickup != null, "WoodResource prefab did not retain ResourcePickup.");
                return savedPickup;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateWoodGeometry(
            Transform parent,
            Material barkMaterial,
            Material cutMaterial)
        {
            GameObject body = CreatePrimitiveChild("Bark", PrimitiveType.Cylinder, parent, barkMaterial);
            body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            body.transform.localScale = new Vector3(0.28f, 0.55f, 0.28f);

            CreateLogEnd("Cut End Left", parent, cutMaterial, -0.565f);
            CreateLogEnd("Cut End Right", parent, cutMaterial, 0.565f);
        }

        private static void CreateLogEnd(string name, Transform parent, Material material, float xPosition)
        {
            GameObject logEnd = CreatePrimitiveChild(name, PrimitiveType.Cylinder, parent, material);
            logEnd.transform.localPosition = new Vector3(xPosition, 0f, 0f);
            logEnd.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            logEnd.transform.localScale = new Vector3(0.235f, 0.018f, 0.235f);
        }

        private static GameObject BuildPlayerPrefab(
            Material playerMaterial,
            Material playerAccentMaterial,
            GameObject woodVisualPrefab,
            Material particleMaterial)
        {
            GameObject root = new GameObject("Player");
            try
            {
                CharacterController characterController = root.AddComponent<CharacterController>();
                characterController.center = new Vector3(0f, 1f, 0f);
                characterController.height = 2f;
                characterController.radius = 0.45f;
                characterController.slopeLimit = 45f;
                characterController.stepOffset = 0.3f;
                characterController.skinWidth = 0.08f;

                PlayerDragInput dragInput = root.AddComponent<PlayerDragInput>();
                PlayerMovement movement = root.AddComponent<PlayerMovement>();
                CarryStack carryStack = root.AddComponent<CarryStack>();
                ResourceCollector collector = root.AddComponent<ResourceCollector>();
                root.AddComponent<Wallet>();
                PlayerPickupFeedback pickupFeedback = root.AddComponent<PlayerPickupFeedback>();

                GameObject capsule = CreatePrimitiveChild(
                    "Capsule Placeholder",
                    PrimitiveType.Capsule,
                    root.transform,
                    playerMaterial);
                capsule.transform.localPosition = new Vector3(0f, 1f, 0f);
                capsule.transform.localScale = new Vector3(0.85f, 1f, 0.85f);

                GameObject facingMarker = CreatePrimitiveChild(
                    "Facing Marker",
                    PrimitiveType.Cube,
                    root.transform,
                    playerAccentMaterial);
                facingMarker.transform.localPosition = new Vector3(0f, 1.15f, 0.43f);
                facingMarker.transform.localScale = new Vector3(0.22f, 0.16f, 0.12f);

                GameObject carryAnchor = new GameObject("Carry Stack Anchor");
                carryAnchor.transform.SetParent(root.transform, false);
                carryAnchor.transform.localPosition = new Vector3(0f, 1.6f, -0.65f);

                GameObject cashCollectionTarget = new GameObject("Cash Collection Target");
                cashCollectionTarget.transform.SetParent(root.transform, false);
                cashCollectionTarget.transform.localPosition = new Vector3(0f, 1.35f, 0.15f);

                ParticleSystem pickupParticles = CreateFeedbackParticleSystem(
                    "Wood Pickup Burst",
                    carryAnchor.transform,
                    new Vector3(0f, 0.10f, 0f),
                    new Color(1f, 0.67f, 0.18f),
                    particleMaterial,
                    48,
                    0.32f,
                    1.45f,
                    0.13f);

                SetObjectReference(movement, "dragInput", dragInput);
                SetObjectReference(carryStack, "visualRoot", carryAnchor.transform);
                SetObjectReference(carryStack, "itemVisualPrefab", woodVisualPrefab);
                SetFloat(carryStack, "horizontalSpacing", 0.82f);
                SetFloat(carryStack, "verticalSpacing", 0.40f);
                SetFloat(carryStack, "depthSpacing", 0.10f);
                SetFloat(carryStack, "placementDuration", 0.14f);
                SetFloat(carryStack, "addBounceDuration", 0.18f);
                SetFloat(carryStack, "addScaleOvershoot", 1.18f);
                SetObjectReference(collector, "carryStack", carryStack);
                SetObjectReference(collector, "pickupTarget", carryAnchor.transform);
                SetFloat(collector, "attractionDuration", 0.24f);
                SetFloat(collector, "attractionArcHeight", 0.80f);
                SetFloat(collector, "attractionStagger", 0.025f);
                SetFloat(collector, "maximumStagger", 0.075f);
                SetObjectReference(pickupFeedback, "carryStack", carryStack);
                SetObjectReference(pickupFeedback, "pickupParticles", pickupParticles);

                return SavePrefabAndReload(root, PlayerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreatePrimitiveChild(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Material material)
        {
            GameObject child = GameObject.CreatePrimitive(primitiveType);
            child.name = name;
            child.transform.SetParent(parent, false);

            Collider collider = child.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return child;
        }

        private static GameObject SavePrefabAndReload(GameObject root, string assetPath)
        {
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Require(savedPrefab != null, $"Unable to reload generated prefab at {assetPath}.");
            return savedPrefab;
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.47f, 0.52f, 0.57f);
            RenderSettings.reflectionIntensity = 0.55f;
            RenderSettings.skybox = null;
        }

        private static void CreateGround(Scene scene, Material groundMaterial)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = new Vector3(0f, -0.15f, 2f);
            ground.transform.localScale = new Vector3(24f, 0.3f, 28f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
        }

        private static void CreateWalkableBounds(Scene scene)
        {
            GameObject root = new GameObject("Walkable Bounds");
            SceneManager.MoveGameObjectToScene(root, scene);

            CreateBoundary("West", root.transform, new Vector3(-12.5f, 1.5f, 2f), new Vector3(1f, 3f, 28f));
            CreateBoundary("East", root.transform, new Vector3(12.5f, 1.5f, 2f), new Vector3(1f, 3f, 28f));
            CreateBoundary("South", root.transform, new Vector3(0f, 1.5f, -12.5f), new Vector3(24f, 3f, 1f));
            CreateBoundary("North", root.transform, new Vector3(0f, 1.5f, 16.5f), new Vector3(24f, 3f, 1f));
        }

        private static void CreateBoundary(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size)
        {
            GameObject boundary = new GameObject(name);
            boundary.transform.SetParent(parent, false);
            boundary.transform.localPosition = localPosition;
            BoxCollider collider = boundary.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private static UnityEngine.Camera CreateCamera(Scene scene, Transform player)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";

            UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.fieldOfView = 43f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.58f, 0.78f, 0.94f);
            cameraObject.AddComponent<AudioListener>();

            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;

            SmoothFollowCamera followCamera = cameraObject.AddComponent<SmoothFollowCamera>();
            SetObjectReference(followCamera, "target", player);
            SetFloat(followCamera, "impulseAmplitude", 0.06f);
            SetFloat(followCamera, "impulseDuration", 0.18f);
            followCamera.SnapToTarget();
            return camera;
        }

        private static FeedbackServices CreateFeedbackServices(Scene scene)
        {
            GameObject root = new GameObject("Feedback Services");
            SceneManager.MoveGameObjectToScene(root, scene);

            AudioSource audioSource = root.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.82f;

            AudioFeedback audioFeedback = root.AddComponent<AudioFeedback>();
            SetObjectReference(audioFeedback, "audioSource", audioSource);
            HapticFeedback hapticFeedback = root.AddComponent<HapticFeedback>();
            return new FeedbackServices(audioFeedback, hapticFeedback);
        }

        private static ParticleSystem CreateFeedbackParticleSystem(
            string name,
            Transform parent,
            Vector3 localPosition,
            Color color,
            Material material,
            int maximumParticles,
            float lifetime,
            float speed,
            float size)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.1f, lifetime);
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = maximumParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.18f;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static WoodSpawner CreateWoodSpawner(Scene scene)
        {
            GameObject woodResourcePrefabObject = AssetDatabase.LoadAssetAtPath<GameObject>(
                WoodResourcePrefabPath);
            ResourcePickup woodResourcePrefab = woodResourcePrefabObject != null
                ? woodResourcePrefabObject.GetComponent<ResourcePickup>()
                : null;
            Require(woodResourcePrefab != null && AssetDatabase.Contains(woodResourcePrefab),
                "The generated WoodResource prefab component is not a persistent asset.");

            GameObject spawnerObject = new GameObject("Wood Spawner");
            SceneManager.MoveGameObjectToScene(spawnerObject, scene);
            spawnerObject.transform.position = new Vector3(0f, 0f, 5.25f);

            WoodSpawner spawner = spawnerObject.AddComponent<WoodSpawner>();
            spawner.ConfigurePrefab(woodResourcePrefab);
            SetVector2(spawner, "spawnArea", new Vector2(11f, 7.5f));
            SetInteger(spawner, "maximumActiveCount", 32);
            EditorUtility.SetDirty(spawner);
            Require(spawner.WoodPrefab != null, "WoodSpawner rejected its prefab reference.");
            return spawner;
        }

        private static CashPile CreateCashPile(
            Scene scene,
            Wallet wallet,
            Collider playerCollider,
            Transform collectionTarget,
            GameObject cashVisualPrefab,
            Material cashMaterial,
            Material accentMaterial,
            Material particleMaterial,
            FeedbackServices feedbackServices,
            Transform playerPopTarget)
        {
            GameObject root = new GameObject("Cash Pile");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = new Vector3(-1.35f, 0f, -4.5f);

            AddKinematicTrigger(root, new Vector3(2.4f, 2.4f, 2.4f));
            CreateZoneDisc("Collection Area", root.transform, cashMaterial, 1.15f);

            GameObject visualRoot = new GameObject("Cash Stack Visuals");
            visualRoot.transform.SetParent(root.transform, false);
            visualRoot.transform.localPosition = new Vector3(0f, 0.18f, 0f);

            GameObject flightOrigin = new GameObject("Cash Flight Origin");
            flightOrigin.transform.SetParent(root.transform, false);
            flightOrigin.transform.localPosition = new Vector3(0f, 0.48f, 0f);

            TextMesh amountText = CreateWorldLabel(
                "Cash Amount",
                root.transform,
                "$0",
                new Vector3(0f, 1.55f, 0.2f),
                new Color(1f, 0.92f, 0.48f));

            CashPile cashPile = root.AddComponent<CashPile>();
            SetObjectReference(cashPile, "visualRoot", visualRoot.transform);
            SetObjectReference(cashPile, "cashVisualPrefab", cashVisualPrefab);
            SetObjectReference(cashPile, "amountText", amountText);
            SetInteger(cashPile, "maximumVisualItems", 8);
            SetInteger(cashPile, "cashPerVisual", 10);

            CashPileCollector collector = root.AddComponent<CashPileCollector>();
            SetObjectReference(collector, "cashPile", cashPile);
            SetObjectReference(collector, "wallet", wallet);
            SetObjectReference(collector, "playerCollider", playerCollider);
            SetObjectReference(collector, "flightOrigin", flightOrigin.transform);
            SetObjectReference(collector, "flightTarget", collectionTarget);
            SetObjectReference(collector, "flightVisualPrefab", cashVisualPrefab);
            SetInteger(collector, "maximumFlightVisuals", 4);
            SetFloat(collector, "flightDuration", 0.34f);
            SetFloat(collector, "flightStagger", 0.04f);
            SetFloat(collector, "arcHeight", 0.8f);

            ParticleSystem growthParticles = CreateFeedbackParticleSystem(
                "Cash Growth Burst",
                root.transform,
                new Vector3(0f, 0.48f, 0f),
                new Color(0.20f, 1f, 0.42f),
                particleMaterial,
                40,
                0.34f,
                1.25f,
                0.11f);
            ParticleSystem collectionParticles = CreateFeedbackParticleSystem(
                "Cash Collection Burst",
                collectionTarget,
                Vector3.zero,
                new Color(1f, 0.86f, 0.22f),
                particleMaterial,
                48,
                0.38f,
                1.65f,
                0.13f);

            CashPileFeedback cashFeedback = root.AddComponent<CashPileFeedback>();
            SetObjectReference(cashFeedback, "cashPile", cashPile);
            SetObjectReference(cashFeedback, "cashCollector", collector);
            SetObjectReference(cashFeedback, "visualRoot", visualRoot.transform);
            SetObjectReference(cashFeedback, "collectionPopTarget", playerPopTarget);
            SetObjectReference(cashFeedback, "growthParticles", growthParticles);
            SetObjectReference(cashFeedback, "collectionParticles", collectionParticles);
            SetObjectReference(cashFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(cashFeedback, "hapticFeedback", feedbackServices.Haptics);
            SetFloat(cashFeedback, "bundlePopDuration", 0.16f);
            SetFloat(cashFeedback, "collectionPopDuration", 0.18f);

            GameObject marker = CreatePrimitiveChild(
                "Dollar Marker",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            marker.transform.localPosition = new Vector3(0f, 0.12f, -0.72f);
            marker.transform.localScale = new Vector3(0.65f, 0.07f, 0.16f);
            return cashPile;
        }

        private static SalePoint CreateSalePoint(
            Scene scene,
            CarryStack carryStack,
            CashPile cashPile,
            Collider playerCollider,
            Material saleMaterial,
            Material accentMaterial,
            GameObject woodVisualPrefab,
            Material particleMaterial,
            AudioFeedback audioFeedback)
        {
            GameObject root = new GameObject("Sale Point");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = new Vector3(-5.4f, 0f, -4.5f);

            AddKinematicTrigger(root, new Vector3(2.8f, 2.4f, 2.8f));
            CreateZoneDisc("Sale Area", root.transform, saleMaterial, 1.35f);

            GameObject arrow = CreatePrimitiveChild(
                "Delivery Marker",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            arrow.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            arrow.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            arrow.transform.localScale = new Vector3(0.82f, 0.10f, 0.28f);

            CreateWorldLabel(
                "Sale Label",
                root.transform,
                "SELL WOOD\n$5 EACH",
                new Vector3(0f, 1.55f, 0.15f),
                Color.white);

            GameObject flightTarget = new GameObject("Sale Flight Target");
            flightTarget.transform.SetParent(root.transform, false);
            flightTarget.transform.localPosition = new Vector3(0f, 0.45f, 0f);

            ParticleSystem saleParticles = CreateFeedbackParticleSystem(
                "Sale Burst",
                root.transform,
                new Vector3(0f, 0.42f, 0f),
                new Color(1f, 0.52f, 0.12f),
                particleMaterial,
                40,
                0.34f,
                1.45f,
                0.12f);

            SalePoint salePoint = root.AddComponent<SalePoint>();
            SetObjectReference(salePoint, "carryStack", carryStack);
            SetObjectReference(salePoint, "cashPile", cashPile);
            SetObjectReference(salePoint, "playerCollider", playerCollider);
            SetEnum(salePoint, "resourceType", (int)ResourceType.Wood);
            SetInteger(salePoint, "woodValue", 5);
            SetFloat(salePoint, "unloadInterval", 0.2f);

            SalePointFeedback saleFeedback = root.AddComponent<SalePointFeedback>();
            SetObjectReference(saleFeedback, "salePoint", salePoint);
            SetObjectReference(saleFeedback, "flightTarget", flightTarget.transform);
            SetObjectReference(saleFeedback, "responseVisual", arrow.transform);
            SetObjectReference(saleFeedback, "woodVisualPrefab", woodVisualPrefab);
            SetObjectReference(saleFeedback, "saleParticles", saleParticles);
            SetObjectReference(saleFeedback, "audioFeedback", audioFeedback);
            SetInteger(saleFeedback, "poolSize", 4);
            SetFloat(saleFeedback, "flightDuration", 0.18f);
            SetFloat(saleFeedback, "arcHeight", 0.55f);
            return salePoint;
        }

        private static PurchasePad CreatePurchasePad(
            Scene scene,
            string rootName,
            string purchaseLabel,
            Vector3 position,
            int totalCost,
            bool startsAvailable,
            Wallet wallet,
            Collider playerCollider,
            Material availableMaterial,
            Material completedMaterial,
            Material accentMaterial,
            GameObject cashVisualPrefab,
            Transform tokenOrigin,
            Material particleMaterial,
            AudioFeedback audioFeedback)
        {
            GameObject root = new GameObject(rootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = position;

            BoxCollider trigger = AddKinematicTrigger(root, new Vector3(3f, 2.4f, 3f));
            GameObject padVisual = CreateZoneDisc("Upgrade Area", root.transform, availableMaterial, 1.45f);

            GameObject plusHorizontal = CreatePrimitiveChild(
                "Upgrade Plus Horizontal",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            plusHorizontal.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            plusHorizontal.transform.localScale = new Vector3(0.92f, 0.10f, 0.25f);

            GameObject plusVertical = CreatePrimitiveChild(
                "Upgrade Plus Vertical",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            plusVertical.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            plusVertical.transform.localScale = new Vector3(0.25f, 0.10f, 0.92f);

            TextMesh statusText = CreateWorldLabel(
                "Purchase Status",
                root.transform,
                $"{purchaseLabel}\n${totalCost} / ${totalCost}",
                new Vector3(0f, 1.55f, 0.15f),
                Color.white);

            GameObject progressTrack = CreatePrimitiveChild(
                "Purchase Progress Track",
                PrimitiveType.Cube,
                root.transform,
                availableMaterial);
            progressTrack.transform.localPosition = new Vector3(0f, 0.20f, -0.95f);
            progressTrack.transform.localScale = new Vector3(2.35f, 0.08f, 0.24f);

            GameObject progressFill = CreatePrimitiveChild(
                "Purchase Progress Fill",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            progressFill.transform.localPosition = new Vector3(0f, 0.25f, -0.95f);
            progressFill.transform.localScale = new Vector3(2.15f, 0.10f, 0.15f);
            progressFill.SetActive(false);

            GameObject tokenTarget = new GameObject("Purchase Token Target");
            tokenTarget.transform.SetParent(root.transform, false);
            tokenTarget.transform.localPosition = new Vector3(0f, 0.42f, 0f);

            ParticleSystem purchaseParticles = CreateFeedbackParticleSystem(
                "Purchase Burst",
                root.transform,
                new Vector3(0f, 0.44f, 0f),
                new Color(0.78f, 0.48f, 1f),
                particleMaterial,
                56,
                0.38f,
                1.55f,
                0.12f);

            PurchasePad purchasePad = root.AddComponent<PurchasePad>();
            SetObjectReference(purchasePad, "wallet", wallet);
            SetObjectReference(purchasePad, "playerCollider", playerCollider);
            SetObjectReference(purchasePad, "interactionCollider", trigger);
            SetObjectReference(purchasePad, "padRenderer", padVisual.GetComponent<Renderer>());
            SetObjectReference(purchasePad, "availableMaterial", availableMaterial);
            SetObjectReference(purchasePad, "completedMaterial", completedMaterial);
            SetObjectReference(purchasePad, "statusText", statusText);
            SetString(purchasePad, "purchaseLabel", purchaseLabel);
            SetBoolean(purchasePad, "startsAvailable", startsAvailable);
            SetInteger(purchasePad, "totalCost", totalCost);
            SetInteger(purchasePad, "spendPerTick", 5);
            SetFloat(purchasePad, "spendInterval", 0.1f);

            PurchasePadFeedback purchaseFeedback = root.AddComponent<PurchasePadFeedback>();
            SetObjectReference(purchaseFeedback, "purchasePad", purchasePad);
            SetObjectReference(purchaseFeedback, "tokenOrigin", tokenOrigin);
            SetObjectReference(purchaseFeedback, "tokenTarget", tokenTarget.transform);
            SetObjectReference(purchaseFeedback, "padVisual", padVisual.transform);
            SetObjectReference(purchaseFeedback, "progressFill", progressFill.transform);
            SetObjectReference(purchaseFeedback, "statusText", statusText);
            SetObjectReference(purchaseFeedback, "tokenVisualPrefab", cashVisualPrefab);
            SetObjectReference(purchaseFeedback, "purchaseParticles", purchaseParticles);
            SetObjectReference(purchaseFeedback, "audioFeedback", audioFeedback);
            SetInteger(purchaseFeedback, "tokenPoolSize", 4);
            SetFloat(purchaseFeedback, "tokenFlightDuration", 0.22f);
            SetFloat(purchaseFeedback, "tokenArcHeight", 0.52f);
            SetFloat(purchaseFeedback, "tickPulseDuration", 0.12f);
            SetFloat(purchaseFeedback, "emptyWalletDuration", 0.28f);
            SetFloat(purchaseFeedback, "completionDuration", 0.42f);
            trigger.enabled = startsAvailable;
            return purchasePad;
        }

        private static WoodProductionUpgrade CreateProductionStation(
            Scene scene,
            PurchasePad purchasePad,
            WoodSpawner woodSpawner,
            Material baseMaterial,
            Material bladeMaterial,
            Material accentMaterial,
            Material particleMaterial,
            FeedbackServices feedbackServices,
            SmoothFollowCamera followCamera)
        {
            GameObject root = new GameObject("Saw Station");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = new Vector3(4.25f, 0f, -0.9f);

            GameObject platform = CreatePrimitiveChild(
                "Station Platform",
                PrimitiveType.Cube,
                root.transform,
                baseMaterial);
            platform.transform.localPosition = new Vector3(0f, 0.16f, 0f);
            platform.transform.localScale = new Vector3(3.2f, 0.30f, 1.45f);

            GameObject firstCutter = CreateCutterVisual(
                "Saw One",
                root.transform,
                new Vector3(-0.75f, 0.35f, 0f),
                bladeMaterial,
                accentMaterial);
            firstCutter.SetActive(true);

            GameObject secondCutter = CreateCutterVisual(
                "Saw Two (Unlock)",
                root.transform,
                new Vector3(0.75f, 0.35f, 0f),
                bladeMaterial,
                accentMaterial);
            secondCutter.SetActive(false);

            TextMesh statusText = CreateWorldLabel(
                "Production Status",
                root.transform,
                "WOOD PRODUCTION  1x",
                new Vector3(0f, 1.7f, 0.15f),
                Color.white);

            ParticleSystem unlockParticles = CreateFeedbackParticleSystem(
                "Unlock Burst",
                secondCutter.transform,
                new Vector3(0f, 0.72f, 0f),
                new Color(0.28f, 0.92f, 1f),
                particleMaterial,
                40,
                0.58f,
                1.85f,
                0.14f);

            WoodProductionUpgrade upgrade = root.AddComponent<WoodProductionUpgrade>();
            SetObjectReference(upgrade, "purchasePad", purchasePad);
            SetObjectReference(upgrade, "woodSpawner", woodSpawner);
            SetObjectReference(upgrade, "secondCutterVisual", secondCutter);
            SetObjectReference(upgrade, "statusText", statusText);
            SetFloat(upgrade, "productionMultiplier", 2f);

            ProductionUnlockFeedback unlockFeedback = root.AddComponent<ProductionUnlockFeedback>();
            SetObjectReference(unlockFeedback, "productionUpgrade", upgrade);
            SetObjectReference(unlockFeedback, "secondCutterVisual", secondCutter.transform);
            SetObjectReference(unlockFeedback, "unlockParticles", unlockParticles);
            SetObjectReference(unlockFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(unlockFeedback, "hapticFeedback", feedbackServices.Haptics);
            SetObjectReference(unlockFeedback, "followCamera", followCamera);
            SetFloat(unlockFeedback, "unlockDuration", 0.65f);
            return upgrade;
        }

        private static WoodStockpile CreateWoodStockpile(
            Scene scene,
            CarryStack carryStack,
            Collider playerCollider,
            GameObject woodVisualPrefab,
            Material stockpileMaterial,
            Material accentMaterial,
            Material particleMaterial)
        {
            GameObject root = new GameObject("Wood Stockpile");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = new Vector3(7.2f, 0f, 5.1f);

            BoxCollider trigger = AddKinematicTrigger(root, new Vector3(3f, 2.4f, 2.8f));
            CreateZoneDisc("Stockpile Collection Area", root.transform, stockpileMaterial, 1.42f);

            GameObject platform = CreatePrimitiveChild(
                "Stockpile Platform",
                PrimitiveType.Cube,
                root.transform,
                stockpileMaterial);
            platform.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            platform.transform.localScale = new Vector3(2.75f, 0.32f, 1.95f);

            GameObject leftRail = CreatePrimitiveChild(
                "Left Rail",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            leftRail.transform.localPosition = new Vector3(-1.26f, 0.46f, 0.28f);
            leftRail.transform.localScale = new Vector3(0.16f, 0.62f, 1.45f);

            GameObject rightRail = CreatePrimitiveChild(
                "Right Rail",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            rightRail.transform.localPosition = new Vector3(1.26f, 0.46f, 0.28f);
            rightRail.transform.localScale = new Vector3(0.16f, 0.62f, 1.45f);

            GameObject visualRoot = new GameObject("Stockpile Wood Visuals");
            visualRoot.transform.SetParent(root.transform, false);
            visualRoot.transform.localPosition = new Vector3(0f, 0.45f, -0.24f);

            GameObject depositPoint = new GameObject("Deposit Point");
            depositPoint.transform.SetParent(root.transform, false);
            depositPoint.transform.localPosition = new Vector3(0f, 0.92f, -0.18f);

            TextMesh amountText = CreateWorldLabel(
                "Stockpile Amount",
                root.transform,
                "WOOD 0 / 30",
                new Vector3(0f, 1.72f, 0.18f),
                new Color(1f, 0.88f, 0.50f));

            ParticleSystem depositParticles = CreateFeedbackParticleSystem(
                "Stockpile Deposit Burst",
                depositPoint.transform,
                Vector3.zero,
                new Color(1f, 0.64f, 0.20f),
                particleMaterial,
                32,
                0.32f,
                1.25f,
                0.11f);

            WoodStockpile stockpile = root.AddComponent<WoodStockpile>();
            SetInteger(stockpile, "capacity", 30);

            WoodStockpileCollector collector = root.AddComponent<WoodStockpileCollector>();
            SetObjectReference(collector, "stockpile", stockpile);
            SetObjectReference(collector, "carryStack", carryStack);
            SetObjectReference(collector, "playerCollider", playerCollider);
            SetFloat(collector, "transferInterval", 0.10f);
            Require(collector.GetComponent<Collider>() == trigger,
                "Wood Stockpile collector must share its trigger collider.");

            WoodStockpileFeedback feedback = root.AddComponent<WoodStockpileFeedback>();
            SetObjectReference(feedback, "stockpile", stockpile);
            SetObjectReference(feedback, "visualRoot", visualRoot.transform);
            SetObjectReference(feedback, "woodVisualPrefab", woodVisualPrefab);
            SetObjectReference(feedback, "amountText", amountText);
            SetObjectReference(feedback, "depositParticles", depositParticles);
            SetInteger(feedback, "maximumVisualItems", 10);
            SetInteger(feedback, "woodPerVisual", 3);
            SetInteger(feedback, "itemsPerRow", 5);
            SetFloat(feedback, "visualScale", 0.68f);
            SetFloat(feedback, "popDuration", 0.16f);
            return stockpile;
        }

        private static FirstWorkerUnlock CreateWorkerAutomation(
            Scene scene,
            WoodProductionUpgrade productionUpgrade,
            WoodSpawner woodSpawner,
            WoodStockpile stockpile,
            Wallet wallet,
            Collider playerCollider,
            Material workerMaterial,
            Material workerAccentMaterial,
            Material purchaseAvailableMaterial,
            Material purchaseCompletedMaterial,
            Material cashAccentMaterial,
            GameObject cashVisualPrefab,
            Transform tokenOrigin,
            GameObject woodVisualPrefab,
            Material particleMaterial,
            FeedbackServices feedbackServices,
            SmoothFollowCamera followCamera)
        {
            PurchasePad workerPurchasePad = CreatePurchasePad(
                scene,
                "Worker Purchase Pad",
                "LUMBER WORKER",
                new Vector3(4.25f, 0f, -8f),
                240,
                false,
                wallet,
                playerCollider,
                purchaseAvailableMaterial,
                purchaseCompletedMaterial,
                cashAccentMaterial,
                cashVisualPrefab,
                tokenOrigin,
                particleMaterial,
                feedbackServices.Audio);

            GameObject workerRoot = new GameObject("Lumber Worker");
            SceneManager.MoveGameObjectToScene(workerRoot, scene);
            workerRoot.transform.position = new Vector3(7.2f, 0f, 1.7f);

            GameObject workerVisual = new GameObject("Worker Visual");
            workerVisual.transform.SetParent(workerRoot.transform, false);

            GameObject capsule = CreatePrimitiveChild(
                "Worker Capsule",
                PrimitiveType.Capsule,
                workerVisual.transform,
                workerMaterial);
            capsule.transform.localPosition = new Vector3(0f, 0.86f, 0f);
            capsule.transform.localScale = new Vector3(0.72f, 0.84f, 0.72f);

            GameObject facingMarker = CreatePrimitiveChild(
                "Worker Facing Marker",
                PrimitiveType.Cube,
                workerVisual.transform,
                workerAccentMaterial);
            facingMarker.transform.localPosition = new Vector3(0f, 1.02f, 0.37f);
            facingMarker.transform.localScale = new Vector3(0.19f, 0.14f, 0.10f);

            GameObject carriedWoodAnchor = new GameObject("Worker Carry Anchor");
            carriedWoodAnchor.transform.SetParent(workerVisual.transform, false);
            carriedWoodAnchor.transform.localPosition = new Vector3(0f, 1.52f, -0.36f);

            Transform depositPoint = stockpile.transform.Find("Deposit Point");
            Require(depositPoint != null, "Wood Stockpile requires a Deposit Point.");

            LumberWorker worker = workerRoot.AddComponent<LumberWorker>();
            SetObjectReference(worker, "woodSpawner", woodSpawner);
            SetObjectReference(worker, "stockpile", stockpile);
            SetObjectReference(worker, "depositPoint", depositPoint);
            SetFloat(worker, "moveSpeed", 3.5f);
            SetFloat(worker, "rotationSpeed", 540f);
            SetFloat(worker, "stopDistance", 0.35f);
            SetFloat(worker, "searchInterval", 0.35f);
            SetFloat(worker, "pickupDelay", 0.12f);
            SetFloat(worker, "depositDelay", 0.15f);

            LumberWorkerFeedback workerFeedback = workerRoot.AddComponent<LumberWorkerFeedback>();
            SetObjectReference(workerFeedback, "worker", worker);
            SetObjectReference(workerFeedback, "carriedWoodAnchor", carriedWoodAnchor.transform);
            SetObjectReference(workerFeedback, "depositTarget", depositPoint);
            SetObjectReference(workerFeedback, "woodVisualPrefab", woodVisualPrefab);
            SetFloat(workerFeedback, "cargoPopDuration", 0.16f);
            SetFloat(workerFeedback, "depositFlightDuration", 0.20f);
            SetFloat(workerFeedback, "depositArcHeight", 0.48f);

            workerRoot.SetActive(false);
            workerPurchasePad.gameObject.SetActive(false);

            GameObject automationRoot = new GameObject("Worker Automation");
            SceneManager.MoveGameObjectToScene(automationRoot, scene);

            FirstWorkerUnlock workerUnlock = automationRoot.AddComponent<FirstWorkerUnlock>();
            SetObjectReference(workerUnlock, "productionUpgrade", productionUpgrade);
            SetObjectReference(workerUnlock, "workerPurchasePad", workerPurchasePad);
            SetObjectReference(workerUnlock, "workerPurchasePadRoot", workerPurchasePad.gameObject);
            SetObjectReference(workerUnlock, "workerRoot", workerRoot);

            ParticleSystem unlockParticles = CreateFeedbackParticleSystem(
                "Worker Unlock Burst",
                automationRoot.transform,
                workerRoot.transform.position + new Vector3(0f, 0.9f, 0f),
                new Color(0.22f, 0.90f, 1f),
                particleMaterial,
                40,
                0.58f,
                1.85f,
                0.14f);

            WorkerUnlockFeedback unlockFeedback = automationRoot.AddComponent<WorkerUnlockFeedback>();
            SetObjectReference(unlockFeedback, "workerUnlock", workerUnlock);
            SetObjectReference(unlockFeedback, "workerVisual", workerVisual.transform);
            SetObjectReference(unlockFeedback, "unlockParticles", unlockParticles);
            SetObjectReference(unlockFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(unlockFeedback, "hapticFeedback", feedbackServices.Haptics);
            SetObjectReference(unlockFeedback, "followCamera", followCamera);
            SetFloat(unlockFeedback, "unlockDuration", 0.65f);
            return workerUnlock;
        }

        private static GameObject CreateCutterVisual(
            string name,
            Transform parent,
            Vector3 localPosition,
            Material bladeMaterial,
            Material accentMaterial)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            GameObject stand = CreatePrimitiveChild(
                "Stand",
                PrimitiveType.Cube,
                root.transform,
                accentMaterial);
            stand.transform.localPosition = new Vector3(0f, 0.28f, 0.12f);
            stand.transform.localScale = new Vector3(0.22f, 0.70f, 0.22f);

            GameObject blade = CreatePrimitiveChild(
                "Blade",
                PrimitiveType.Cylinder,
                root.transform,
                bladeMaterial);
            blade.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            blade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            blade.transform.localScale = new Vector3(0.46f, 0.055f, 0.46f);

            GameObject hub = CreatePrimitiveChild(
                "Hub",
                PrimitiveType.Cylinder,
                root.transform,
                accentMaterial);
            hub.transform.localPosition = new Vector3(0f, 0.72f, -0.065f);
            hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hub.transform.localScale = new Vector3(0.12f, 0.07f, 0.12f);
            return root;
        }

        private static GameObject CreateZoneDisc(
            string name,
            Transform parent,
            Material material,
            float radius)
        {
            GameObject disc = CreatePrimitiveChild(name, PrimitiveType.Cylinder, parent, material);
            disc.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            disc.transform.localScale = new Vector3(radius, 0.07f, radius);
            return disc;
        }

        private static BoxCollider AddKinematicTrigger(GameObject root, Vector3 size)
        {
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, size.y * 0.45f, 0f);
            trigger.size = size;

            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.constraints = RigidbodyConstraints.FreezeAll;
            return trigger;
        }

        private static TextMesh CreateWorldLabel(
            string name,
            Transform parent,
            string text,
            Vector3 localPosition,
            Color color)
        {
            GameObject labelObject = new GameObject(name);
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            labelObject.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);

            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 64;
            label.characterSize = 0.055f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = color;
            label.richText = false;
            labelObject.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            return label;
        }

        private static void CreateLighting(Scene scene)
        {
            GameObject lightObject = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.color = new Color(1f, 0.93f, 0.82f);
            directionalLight.intensity = 1.25f;
            directionalLight.shadows = LightShadows.Soft;
            RenderSettings.sun = directionalLight;
        }

        private static void CreateHud(Scene scene, CarryStack carryStack, Wallet wallet)
        {
            GameObject canvasObject = new GameObject(
                "HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject safeAreaObject = new GameObject("Safe Area", typeof(RectTransform));
            safeAreaObject.transform.SetParent(canvasObject.transform, false);
            RectTransform safeAreaRect = safeAreaObject.GetComponent<RectTransform>();
            StretchToParent(safeAreaRect);
            safeAreaObject.AddComponent<SafeAreaFitter>();

            GameObject panelObject = new GameObject("Wood Counter", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(safeAreaObject.transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.sizeDelta = new Vector2(470f, 184f);
            panelRect.anchoredPosition = new Vector2(0f, -36f);
            panelObject.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.07f, 0.82f);

            Text woodText = CreateHudLine(
                "Wood Text",
                panelObject.transform,
                "Wood: 0 / 12",
                new Vector2(0f, 0.5f),
                Vector2.one,
                40);
            Text cashText = CreateHudLine(
                "Cash Text",
                panelObject.transform,
                "$ 0",
                Vector2.zero,
                new Vector2(1f, 0.5f),
                42);

            WoodHud woodHud = panelObject.AddComponent<WoodHud>();
            SetObjectReference(woodHud, "carryStack", carryStack);
            SetObjectReference(woodHud, "countText", woodText);

            WalletHud walletHud = panelObject.AddComponent<WalletHud>();
            SetObjectReference(walletHud, "wallet", wallet);
            SetObjectReference(walletHud, "cashText", cashText);
            SetFloat(walletHud, "animationDuration", 0.22f);
        }

        private static Text CreateHudLine(
            string name,
            Transform parent,
            string initialText,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int fontSize)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = anchorMin;
            textRect.anchorMax = anchorMax;
            textRect.offsetMin = new Vector2(18f, 6f);
            textRect.offsetMax = new Vector2(-18f, -6f);

            Text text = textObject.GetComponent<Text>();
            text.text = initialText;
            text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void ConfigurePortraitSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
        }

        private static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            EditorBuildSettingsScene[] existingScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < existingScenes.Length; i++)
            {
                EditorBuildSettingsScene existingScene = existingScenes[i];
                if (existingScene.path == ScenePath
                    || existingScene.path == LegacyScenePath
                    || existingScene.path == MissingSampleScenePath
                    || AssetDatabase.LoadAssetAtPath<SceneAsset>(existingScene.path) == null)
                {
                    continue;
                }

                scenes.Add(existingScene);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static GameObject InstantiatePrefabInScene(GameObject prefab, Scene scene)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            SceneManager.MoveGameObjectToScene(instance, scene);
            return instance;
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = GetRequiredProperty(serializedObject, target, propertyName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInteger(Object target, string propertyName, int value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = GetRequiredProperty(serializedObject, target, propertyName);
            property.intValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = GetRequiredProperty(serializedObject, target, propertyName);
            property.stringValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBoolean(Object target, string propertyName, bool value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = GetRequiredProperty(serializedObject, target, propertyName);
            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = GetRequiredProperty(serializedObject, target, propertyName);
            property.floatValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector2(Object target, string propertyName, Vector2 value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = GetRequiredProperty(serializedObject, target, propertyName);
            property.vector2Value = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string propertyName, int enumIndex)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = GetRequiredProperty(serializedObject, target, propertyName);
            property.enumValueIndex = enumIndex;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty GetRequiredProperty(
            SerializedObject serializedObject,
            Object target,
            string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on {target.GetType().Name}.");
            }

            return property;
        }

        private static void ValidateSavedPrototype(bool showSuccessDialog)
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Require(sceneAsset != null, $"Missing prototype scene at {ScenePath}.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ValidateSceneContents(scene);

            Debug.Log($"Lumber Camp prototype validation passed: {ScenePath}");
            if (showSuccessDialog)
            {
                EditorUtility.DisplayDialog(
                    "Lumber Camp Prototype",
                    "Validation passed.",
                    "OK");
            }
        }

        private static void ValidateSceneContents(Scene scene)
        {
            Require(scene.IsValid() && scene.isLoaded, "The prototype scene is not loaded.");

            GameObject player = FindRoot(scene, "Player");
            GameObject cameraObject = FindRoot(scene, "Main Camera");
            GameObject spawnerObject = FindRoot(scene, "Wood Spawner");
            GameObject hudObject = FindRoot(scene, "HUD");
            GameObject saleObject = FindRoot(scene, "Sale Point");
            GameObject cashObject = FindRoot(scene, "Cash Pile");
            GameObject purchaseObject = FindRoot(scene, "Purchase Pad");
            GameObject sawStationObject = FindRoot(scene, "Saw Station");
            GameObject feedbackServicesObject = FindRoot(scene, "Feedback Services");
            GameObject workerPurchaseObject = FindRoot(scene, "Worker Purchase Pad");
            GameObject stockpileObject = FindRoot(scene, "Wood Stockpile");
            GameObject workerObject = FindRoot(scene, "Lumber Worker");
            GameObject workerAutomationObject = FindRoot(scene, "Worker Automation");

            Require(player != null, "The prototype scene has no Player root.");
            CharacterController playerCollider = player.GetComponent<CharacterController>();
            Require(playerCollider != null, "Player requires a CharacterController.");
            Require(player.GetComponent<PlayerDragInput>() != null, "Player requires drag input.");
            Require(player.GetComponent<PlayerMovement>() != null, "Player requires movement.");
            Require(player.GetComponent<CarryStack>() != null, "Player requires a CarryStack.");
            Require(player.GetComponent<ResourceCollector>() != null, "Player requires a ResourceCollector.");
            Wallet wallet = player.GetComponent<Wallet>();
            Require(wallet != null, "Player requires a Wallet.");
            Require(player.transform.Find("Cash Collection Target") != null,
                "Player requires a cash collection target.");

            GameObject walkableBounds = FindRoot(scene, "Walkable Bounds");
            Require(walkableBounds != null, "The prototype scene has no walkable bounds.");
            Require(walkableBounds.GetComponentsInChildren<BoxCollider>(true).Length == 4,
                "Walkable bounds require four boundary colliders.");

            UnityEngine.Camera mainCamera = cameraObject != null
                ? cameraObject.GetComponent<UnityEngine.Camera>()
                : null;
            Require(mainCamera != null, "The prototype scene has no camera.");
            SmoothFollowCamera followCamera = mainCamera.GetComponent<SmoothFollowCamera>();
            Require(followCamera != null, "Camera requires smooth follow.");
            Require(GetObjectReference(followCamera, "target") != null, "Camera follow target is not assigned.");
            Require(Mathf.Approximately(followCamera.ImpulseAmplitude, 0.06f)
                    && Mathf.Approximately(followCamera.ImpulseDuration, 0.18f),
                "Camera impulse tuning must remain subtle at 0.06 / 0.18 seconds.");

            Require(feedbackServicesObject != null, "The prototype scene has no Feedback Services root.");
            AudioSource sharedAudioSource = feedbackServicesObject.GetComponent<AudioSource>();
            AudioFeedback audioFeedback = feedbackServicesObject.GetComponent<AudioFeedback>();
            HapticFeedback hapticFeedback = feedbackServicesObject.GetComponent<HapticFeedback>();
            Require(sharedAudioSource != null && audioFeedback != null && hapticFeedback != null,
                "Feedback Services requires shared audio and haptic components.");
            Require(audioFeedback.AudioSource == sharedAudioSource,
                "AudioFeedback must use the shared AudioSource.");
            Require(!sharedAudioSource.playOnAwake && !sharedAudioSource.loop
                    && Mathf.Approximately(sharedAudioSource.spatialBlend, 0f),
                "Shared feedback audio must be a non-looping 2D source with playOnAwake disabled.");

            WoodSpawner woodSpawner = spawnerObject != null
                ? spawnerObject.GetComponent<WoodSpawner>()
                : null;
            Require(woodSpawner != null, "The prototype scene has no WoodSpawner.");
            Require(GetObjectReference(woodSpawner, "woodPrefab") != null,
                "WoodSpawner prefab reference is not assigned.");
            Require(Mathf.Approximately(woodSpawner.BaseSpawnInterval, 1.25f),
                "WoodSpawner base interval must be 1.25 seconds.");
            Require(Mathf.Approximately(woodSpawner.ProductionRateMultiplier, 1f),
                "WoodSpawner must begin at 1x production.");
            Require(GetIntegerValue(woodSpawner, "maximumActiveCount") == 32,
                "WoodSpawner active cap must leave room to observe the upgrade.");

            WoodHud woodHud = hudObject != null
                ? hudObject.GetComponentInChildren<WoodHud>(true)
                : null;
            WalletHud walletHud = hudObject != null
                ? hudObject.GetComponentInChildren<WalletHud>(true)
                : null;
            Require(woodHud != null, "The prototype scene has no Wood HUD.");
            Require(GetObjectReference(woodHud, "carryStack") != null, "Wood HUD CarryStack is not assigned.");
            Require(GetObjectReference(woodHud, "countText") != null, "Wood HUD text is not assigned.");
            Require(walletHud != null, "The prototype scene has no Wallet HUD.");
            Require(GetObjectReference(walletHud, "wallet") == wallet, "Wallet HUD Wallet is not assigned.");
            Require(GetObjectReference(walletHud, "cashText") != null, "Wallet HUD text is not assigned.");
            Require(Mathf.Approximately(walletHud.AnimationDuration, 0.22f),
                "Wallet HUD animation duration must be 0.22 seconds.");

            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            CarryStack carryStack = player.GetComponent<CarryStack>();
            ResourceCollector collector = player.GetComponent<ResourceCollector>();
            PlayerPickupFeedback pickupFeedback = player.GetComponent<PlayerPickupFeedback>();
            Require(GetObjectReference(movement, "movementCamera") != null,
                "Player movement camera is not assigned.");
            Require(GetObjectReference(carryStack, "visualRoot") != null,
                "CarryStack visual root is not assigned.");
            Require(GetObjectReference(carryStack, "itemVisualPrefab") != null,
                "CarryStack item visual prefab is not assigned.");
            Require(GetObjectReference(collector, "carryStack") != null,
                "ResourceCollector CarryStack is not assigned.");
            Require(GetObjectReference(collector, "pickupTarget") != null,
                "ResourceCollector pickup target is not assigned.");
            Require(Mathf.Approximately(collector.AttractionDuration, 0.24f)
                    && Mathf.Approximately(collector.AttractionArcHeight, 0.80f)
                    && Mathf.Approximately(collector.AttractionStagger, 0.025f)
                    && Mathf.Approximately(collector.MaximumStagger, 0.075f),
                "Pickup attraction feel tuning is not configured as expected.");
            Require(carryStack.Capacity == 12,
                "CarryStack capacity must remain 12.");
            Require(Mathf.Approximately(carryStack.PlacementDuration, 0.14f)
                    && Mathf.Approximately(carryStack.AddBounceDuration, 0.18f)
                    && Mathf.Approximately(carryStack.AddScaleOvershoot, 1.18f),
                "CarryStack visual feel tuning is not configured as expected.");
            Require(pickupFeedback != null,
                "Player requires pickup presentation feedback.");
            Require(GetObjectReference(pickupFeedback, "carryStack") == carryStack
                    && GetObjectReference(pickupFeedback, "pickupParticles") != null
                    && GetObjectReference(pickupFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(pickupFeedback, "hapticFeedback") == hapticFeedback,
                "Player pickup feedback references are incomplete.");

            Require(saleObject != null, "The prototype scene has no Sale Point.");
            Require(cashObject != null, "The prototype scene has no Cash Pile.");
            Require(purchaseObject != null, "The prototype scene has no Purchase Pad.");
            Require(sawStationObject != null, "The prototype scene has no Saw Station.");
            Require(workerPurchaseObject != null,
                "The prototype scene has no Worker Purchase Pad.");
            Require(stockpileObject != null, "The prototype scene has no Wood Stockpile.");
            Require(workerObject != null, "The prototype scene has no Lumber Worker.");
            Require(workerAutomationObject != null,
                "The prototype scene has no Worker Automation root.");

            BoxCollider saleTrigger = ValidateTriggerZone(saleObject, "Sale Point");
            BoxCollider cashTrigger = ValidateTriggerZone(cashObject, "Cash Pile");
            BoxCollider purchaseTrigger = ValidateTriggerZone(purchaseObject, "Purchase Pad");
            BoxCollider workerPurchaseTrigger = ValidateTriggerZone(
                workerPurchaseObject,
                "Worker Purchase Pad");
            BoxCollider stockpileTrigger = ValidateTriggerZone(stockpileObject, "Wood Stockpile");
            Require(!saleTrigger.bounds.Intersects(cashTrigger.bounds),
                "Sale Point and Cash Pile triggers must not overlap.");
            Require(!saleTrigger.bounds.Intersects(purchaseTrigger.bounds),
                "Sale Point and Purchase Pad triggers must not overlap.");
            Require(!cashTrigger.bounds.Intersects(purchaseTrigger.bounds),
                "Cash Pile and Purchase Pad triggers must not overlap.");
            Require(!stockpileTrigger.bounds.Intersects(saleTrigger.bounds)
                    && !stockpileTrigger.bounds.Intersects(cashTrigger.bounds)
                    && !stockpileTrigger.bounds.Intersects(purchaseTrigger.bounds),
                "Wood Stockpile trigger must remain separate from the existing interaction zones.");
            Require(Vector3.Distance(
                        workerPurchaseObject.transform.position,
                        purchaseObject.transform.position) >= 3.25f,
                "Worker and production purchase pads are too close for reliable portrait controls.");

            CashPile cashPile = cashObject.GetComponent<CashPile>();
            CashPileCollector cashCollector = cashObject.GetComponent<CashPileCollector>();
            CashPileFeedback cashFeedback = cashObject.GetComponent<CashPileFeedback>();
            SalePoint salePoint = saleObject.GetComponent<SalePoint>();
            SalePointFeedback saleFeedback = saleObject.GetComponent<SalePointFeedback>();
            PurchasePad purchasePad = purchaseObject.GetComponent<PurchasePad>();
            PurchasePadFeedback purchaseFeedback = purchaseObject.GetComponent<PurchasePadFeedback>();
            WoodProductionUpgrade productionUpgrade = sawStationObject.GetComponent<WoodProductionUpgrade>();
            ProductionUnlockFeedback unlockFeedback = sawStationObject.GetComponent<ProductionUnlockFeedback>();
            PurchasePad workerPurchasePad = workerPurchaseObject.GetComponent<PurchasePad>();
            PurchasePadFeedback workerPurchaseFeedback =
                workerPurchaseObject.GetComponent<PurchasePadFeedback>();
            WoodStockpile stockpile = stockpileObject.GetComponent<WoodStockpile>();
            WoodStockpileCollector stockpileCollector =
                stockpileObject.GetComponent<WoodStockpileCollector>();
            WoodStockpileFeedback stockpileFeedback =
                stockpileObject.GetComponent<WoodStockpileFeedback>();
            LumberWorker worker = workerObject.GetComponent<LumberWorker>();
            LumberWorkerFeedback workerFeedback = workerObject.GetComponent<LumberWorkerFeedback>();
            FirstWorkerUnlock workerAutomation =
                workerAutomationObject.GetComponent<FirstWorkerUnlock>();
            WorkerUnlockFeedback workerUnlockFeedback =
                workerAutomationObject.GetComponent<WorkerUnlockFeedback>();

            Require(cashPile != null, "Cash Pile requires its logical cash component.");
            Require(cashPile.MaximumVisualItems > 0 && cashPile.MaximumVisualItems <= 8,
                "Cash Pile visuals must use a small capped pool.");
            Require(GetObjectReference(cashPile, "visualRoot") != null,
                "Cash Pile visual root is not assigned.");
            Require(GetObjectReference(cashPile, "cashVisualPrefab") != null,
                "Cash Pile visual prefab is not assigned.");
            Require(GetObjectReference(cashPile, "amountText") != null,
                "Cash Pile amount text is not assigned.");

            Require(cashCollector != null, "Cash Pile requires a collector.");
            Require(cashCollector.CashPile == cashPile, "Cash collector CashPile is not assigned.");
            Require(cashCollector.Wallet == wallet, "Cash collector Wallet is not assigned.");
            Require(cashCollector.PlayerCollider == playerCollider,
                "Cash collector player collider is not assigned.");
            Require(GetObjectReference(cashCollector, "flightOrigin") != null,
                "Cash collector flight origin is not assigned.");
            Require(GetObjectReference(cashCollector, "flightTarget") != null,
                "Cash collector flight target is not assigned.");
            Require(GetObjectReference(cashCollector, "flightVisualPrefab") != null,
                "Cash collector flight visual is not assigned.");
            Require(cashCollector.MaximumFlightVisuals > 0 && cashCollector.MaximumFlightVisuals <= 8,
                "Cash flight visuals must use a small capped pool.");
            Require(Mathf.Approximately(cashCollector.FlightDuration, 0.34f)
                    && Mathf.Approximately(cashCollector.FlightStagger, 0.04f),
                "Cash flight feel must use 0.34-second flights with 0.04-second stagger.");
            Require(cashFeedback != null
                    && Mathf.Approximately(cashFeedback.BundlePopDuration, 0.16f),
                "Cash Pile presentation feedback is missing or incorrectly tuned.");
            Require(GetObjectReference(cashFeedback, "cashPile") == cashPile
                    && GetObjectReference(cashFeedback, "cashCollector") == cashCollector
                    && GetObjectReference(cashFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(cashFeedback, "hapticFeedback") == hapticFeedback,
                "Cash Pile feedback references are incomplete.");

            Require(salePoint != null, "Sale Point requires its gameplay component.");
            Require(salePoint.CarryStack == carryStack, "Sale Point CarryStack is not assigned.");
            Require(salePoint.CashPile == cashPile, "Sale Point CashPile is not assigned.");
            Require(salePoint.PlayerCollider == playerCollider,
                "Sale Point player collider is not assigned.");
            Require(salePoint.ResourceType == ResourceType.Wood, "Sale Point must unload Wood.");
            Require(salePoint.WoodValue == 5, "Sale Point wood value must be $5.");
            Require(Mathf.Approximately(salePoint.UnloadInterval, 0.2f),
                "Sale Point unload interval must be 0.2 seconds.");
            Require(saleFeedback != null
                    && saleFeedback.PoolSize == 4
                    && Mathf.Approximately(saleFeedback.FlightDuration, 0.18f),
                "Sale Point presentation pool or timing is incorrect.");
            Require(GetObjectReference(saleFeedback, "salePoint") == salePoint
                    && GetObjectReference(saleFeedback, "woodVisualPrefab") != null
                    && GetObjectReference(saleFeedback, "saleParticles") != null
                    && GetObjectReference(saleFeedback, "audioFeedback") == audioFeedback,
                "Sale Point feedback references are incomplete.");

            Require(purchasePad != null, "Purchase Pad requires its gameplay component.");
            Require(purchasePad.Wallet == wallet, "Purchase Pad Wallet is not assigned.");
            Require(purchasePad.PlayerCollider == playerCollider,
                "Purchase Pad player collider is not assigned.");
            Require(purchasePad.InteractionCollider == purchaseTrigger,
                "Purchase Pad interaction collider is not assigned.");
            Require(purchasePad.TotalCost == 120, "Purchase Pad cost must be $120.");
            Require(purchasePad.SpendPerTick == 5, "Purchase Pad must spend $5 per tick.");
            Require(Mathf.Approximately(purchasePad.SpendInterval, 0.1f),
                "Purchase Pad spend interval must be 0.1 seconds.");
            Require(GetObjectReference(purchasePad, "statusText") != null,
                "Purchase Pad status text is not assigned.");
            Require(GetObjectReference(purchasePad, "availableMaterial") != null
                    && GetObjectReference(purchasePad, "completedMaterial") != null,
                "Purchase Pad state materials are not assigned.");
            Require(purchaseFeedback != null
                    && purchaseFeedback.TokenPoolSize == 4
                    && Mathf.Approximately(purchaseFeedback.TokenFlightDuration, 0.22f)
                    && Mathf.Approximately(purchaseFeedback.TickPulseDuration, 0.12f)
                    && Mathf.Approximately(purchaseFeedback.EmptyWalletDuration, 0.28f),
                "Purchase Pad presentation pool or timing is incorrect.");
            Require(GetObjectReference(purchaseFeedback, "purchasePad") == purchasePad
                    && GetObjectReference(purchaseFeedback, "tokenVisualPrefab") != null
                    && GetObjectReference(purchaseFeedback, "progressFill") != null
                    && GetObjectReference(purchaseFeedback, "purchaseParticles") != null
                    && GetObjectReference(purchaseFeedback, "audioFeedback") == audioFeedback,
                "Purchase Pad feedback references are incomplete.");

            Require(productionUpgrade != null, "Saw Station requires a production upgrade.");
            Require(productionUpgrade.PurchasePad == purchasePad,
                "Production upgrade PurchasePad is not assigned.");
            Require(productionUpgrade.WoodSpawner == woodSpawner,
                "Production upgrade WoodSpawner is not assigned.");
            Require(Mathf.Approximately(productionUpgrade.ProductionMultiplier, 2f),
                "Production upgrade multiplier must be 2x.");
            Require(productionUpgrade.SecondCutterVisual != null,
                "Production upgrade second cutter visual is not assigned.");
            Require(!productionUpgrade.SecondCutterVisual.activeSelf,
                "Second cutter must begin locked and hidden.");
            Require(GetObjectReference(productionUpgrade, "statusText") != null,
                "Production upgrade status text is not assigned.");
            Require(unlockFeedback != null
                    && Mathf.Approximately(unlockFeedback.UnlockDuration, 0.65f),
                "Production unlock presentation is missing or incorrectly tuned.");
            Require(GetObjectReference(unlockFeedback, "productionUpgrade") == productionUpgrade
                    && GetObjectReference(unlockFeedback, "secondCutterVisual")
                       == productionUpgrade.SecondCutterVisual.transform
                    && GetObjectReference(unlockFeedback, "unlockParticles") != null
                    && GetObjectReference(unlockFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(unlockFeedback, "hapticFeedback") == hapticFeedback
                    && GetObjectReference(unlockFeedback, "followCamera") == followCamera,
                "Production unlock feedback references are incomplete.");

            Require(workerPurchasePad != null,
                "Worker Purchase Pad requires its gameplay component.");
            Require(workerPurchasePad.Wallet == wallet
                    && workerPurchasePad.PlayerCollider == playerCollider
                    && workerPurchasePad.InteractionCollider == workerPurchaseTrigger,
                "Worker Purchase Pad gameplay references are incomplete.");
            Require(workerPurchasePad.PurchaseLabel == "LUMBER WORKER"
                    && workerPurchasePad.TotalCost == 240
                    && workerPurchasePad.SpendPerTick == 5
                    && Mathf.Approximately(workerPurchasePad.SpendInterval, 0.10f),
                "Worker Purchase Pad must cost $240 and reuse the $5 / 0.10-second cadence.");
            Require(!workerPurchasePad.StartsAvailable
                    && !workerPurchasePad.IsAvailable
                    && !workerPurchaseTrigger.enabled
                    && !workerPurchaseObject.activeSelf,
                "Worker Purchase Pad must begin locked, disabled, and hidden.");
            Require(workerPurchaseFeedback != null
                    && workerPurchaseFeedback.TokenPoolSize == 4
                    && Mathf.Approximately(workerPurchaseFeedback.TokenFlightDuration, 0.22f),
                "Worker Purchase Pad must reuse the capped M2 purchase feedback.");
            Require(GetObjectReference(workerPurchaseFeedback, "purchasePad") == workerPurchasePad
                    && GetObjectReference(workerPurchaseFeedback, "tokenVisualPrefab") != null
                    && GetObjectReference(workerPurchaseFeedback, "purchaseParticles") != null
                    && GetObjectReference(workerPurchaseFeedback, "audioFeedback") == audioFeedback,
                "Worker Purchase Pad feedback references are incomplete.");

            Require(stockpile != null && stockpile.Capacity == 30,
                "Wood Stockpile requires an authoritative capacity of 30.");
            Require(stockpile.StoredWood == 0 && stockpile.IncomingReservations == 0,
                "Wood Stockpile must begin empty with no incoming reservation.");
            Require(stockpileCollector != null
                    && stockpileCollector.Stockpile == stockpile
                    && stockpileCollector.CarryStack == carryStack
                    && stockpileCollector.PlayerCollider == playerCollider
                    && Mathf.Approximately(stockpileCollector.TransferInterval, 0.10f),
                "Wood Stockpile collector references or transfer cadence are incorrect.");
            Require(stockpileFeedback != null
                    && stockpileFeedback.MaximumVisualItems == 10
                    && stockpileFeedback.WoodPerVisual == 3
                    && Mathf.Approximately(stockpileFeedback.VisualScale, 0.68f)
                    && Mathf.Approximately(stockpileFeedback.PopDuration, 0.16f),
                "Wood Stockpile feedback must use ten capped visuals at three wood each.");
            Require(GetObjectReference(stockpileFeedback, "stockpile") == stockpile
                    && GetObjectReference(stockpileFeedback, "visualRoot") != null
                    && GetObjectReference(stockpileFeedback, "woodVisualPrefab") != null
                    && GetObjectReference(stockpileFeedback, "amountText") != null
                    && GetObjectReference(stockpileFeedback, "depositParticles") != null,
                "Wood Stockpile feedback references are incomplete.");

            Require(worker != null && !workerObject.activeSelf,
                "The single Lumber Worker must begin inactive.");
            Require(worker.WoodSpawner == woodSpawner
                    && worker.Stockpile == stockpile
                    && worker.DepositPoint == stockpileObject.transform.Find("Deposit Point"),
                "Lumber Worker gameplay references are incomplete.");
            Require(Mathf.Approximately(worker.MoveSpeed, 3.5f)
                    && Mathf.Approximately(worker.SearchInterval, 0.35f)
                    && Mathf.Approximately(worker.PickupDelay, 0.12f)
                    && Mathf.Approximately(worker.DepositDelay, 0.15f),
                "Lumber Worker tuning must remain 3.5 speed / 0.35 search / 0.12 pickup / 0.15 deposit.");
            Require(workerFeedback != null
                    && Mathf.Approximately(workerFeedback.CargoPopDuration, 0.16f)
                    && Mathf.Approximately(workerFeedback.DepositFlightDuration, 0.20f),
                "Lumber Worker presentation timing is incorrect.");
            Require(GetObjectReference(workerFeedback, "worker") == worker
                    && GetObjectReference(workerFeedback, "carriedWoodAnchor") != null
                    && GetObjectReference(workerFeedback, "depositTarget") == worker.DepositPoint
                    && GetObjectReference(workerFeedback, "woodVisualPrefab") != null,
                "Lumber Worker feedback references are incomplete.");

            Require(workerAutomation != null
                    && workerAutomation.ProductionUpgrade == productionUpgrade
                    && workerAutomation.WorkerPurchasePad == workerPurchasePad
                    && workerAutomation.WorkerPurchasePadRoot == workerPurchaseObject
                    && workerAutomation.WorkerRoot == workerObject,
                "Worker Automation unlock gate references are incomplete.");
            Require(!workerAutomation.IsPadUnlocked && !workerAutomation.IsWorkerActivated,
                "Worker Automation must begin fully locked.");
            Require(workerUnlockFeedback != null
                    && Mathf.Approximately(workerUnlockFeedback.UnlockDuration, 0.65f),
                "Worker unlock feedback is missing or incorrectly tuned.");
            Require(GetObjectReference(workerUnlockFeedback, "workerUnlock") == workerAutomation
                    && GetObjectReference(workerUnlockFeedback, "workerVisual") != null
                    && GetObjectReference(workerUnlockFeedback, "unlockParticles") != null
                    && GetObjectReference(workerUnlockFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(workerUnlockFeedback, "hapticFeedback") == hapticFeedback
                    && GetObjectReference(workerUnlockFeedback, "followCamera") == followCamera,
                "Worker unlock feedback references are incomplete.");

            ValidateFeedbackParticles(scene);

            ResourcePickup woodPrefab = AssetDatabase.LoadAssetAtPath<ResourcePickup>(WoodResourcePrefabPath);
            Require(woodPrefab != null, "WoodResource prefab is missing.");
            Require(woodPrefab.ResourceType == ResourceType.Wood, "WoodResource has the wrong resource type.");
            Require(woodPrefab.GetComponent<Collider>() != null, "WoodResource requires a pickup collider.");

            GameObject carryVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WoodVisualPrefabPath);
            Require(carryVisualPrefab != null, "WoodCarryVisual prefab is missing.");
            Require(carryVisualPrefab.GetComponentInChildren<Rigidbody>(true) == null,
                "Carried visuals must not use Rigidbody physics.");
            Require(carryVisualPrefab.GetComponentInChildren<Collider>(true) == null,
                "Carried visuals must not contain colliders.");

            GameObject cashVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CashVisualPrefabPath);
            Require(cashVisualPrefab != null, "CashBundleVisual prefab is missing.");
            Require(cashVisualPrefab.GetComponentInChildren<Rigidbody>(true) == null,
                "Cash visuals must not use Rigidbody physics.");
            Require(cashVisualPrefab.GetComponentInChildren<Collider>(true) == null,
                "Cash visuals must not contain colliders.");

            bool buildSceneFound = false;
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                if (buildScenes[i].enabled && buildScenes[i].path == ScenePath)
                {
                    buildSceneFound = true;
                    break;
                }
            }

            Require(buildSceneFound, "The prototype scene is not enabled in Build Settings.");
            Require(PlayerSettings.defaultInterfaceOrientation == UIOrientation.Portrait,
                "Default orientation must be Portrait.");
            ValidateCoreLoopLogic();
            ValidateM3Logic();
        }

        private static BoxCollider ValidateTriggerZone(GameObject root, string label)
        {
            BoxCollider trigger = root.GetComponent<BoxCollider>();
            Require(trigger != null && trigger.isTrigger, $"{label} requires a BoxCollider trigger.");
            Rigidbody rigidbody = root.GetComponent<Rigidbody>();
            Require(rigidbody != null, $"{label} requires a Rigidbody for reliable trigger events.");
            Require(rigidbody.isKinematic && !rigidbody.useGravity,
                $"{label} Rigidbody must be kinematic with gravity disabled.");
            return trigger;
        }

        private static void ValidateFeedbackParticles(Scene scene)
        {
            var particleSystems = new List<ParticleSystem>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                ParticleSystem[] rootParticles = roots[i].GetComponentsInChildren<ParticleSystem>(true);
                particleSystems.AddRange(rootParticles);
            }

            Require(particleSystems.Count == 9,
                $"The prototype requires nine reusable feedback emitters; found {particleSystems.Count}.");
            for (int i = 0; i < particleSystems.Count; i++)
            {
                ParticleSystem particles = particleSystems[i];
                ParticleSystem.MainModule main = particles.main;
                Require(!main.playOnAwake && !main.loop,
                    $"Feedback particle system '{particles.name}' must not loop or play on awake.");
                Require(main.maxParticles > 0 && main.maxParticles <= 64,
                    $"Feedback particle system '{particles.name}' has an invalid mobile particle cap.");
                ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
                Require(renderer != null && renderer.sharedMaterial != null,
                    $"Feedback particle system '{particles.name}' requires a shared material.");
            }
        }

        private static void ValidateCoreLoopLogic()
        {
            GameObject carryObject = new GameObject("CarryStack Logic Validation");
            GameObject cashObject = new GameObject("Cash Logic Validation");
            GameObject walletObject = new GameObject("Wallet Logic Validation");
            GameObject purchaseObject = new GameObject("Purchase Logic Validation");
            GameObject spawnerObject = new GameObject("Spawner Logic Validation");
            GameObject upgradeObject = new GameObject("Upgrade Logic Validation");
            try
            {
                CarryStack stack = carryObject.AddComponent<CarryStack>();
                int carryChangeCount = 0;
                int addedUnitCount = 0;
                int removedUnitCount = 0;
                stack.Changed += () => carryChangeCount++;
                stack.ItemsAdded += (_, amount, totalAmount) =>
                {
                    addedUnitCount += amount;
                    Require(totalAmount == stack.TotalAmount,
                        "CarryStack ItemsAdded fired before authoritative state was updated.");
                };
                stack.ItemsRemoved += (_, amount, totalAmount) =>
                {
                    removedUnitCount += amount;
                    Require(totalAmount == stack.TotalAmount,
                        "CarryStack ItemsRemoved fired before authoritative state was updated.");
                };
                Require(!stack.TryAdd(ResourceType.Wood, 0)
                        && !stack.TryAdd(ResourceType.Wood, -1),
                    "CarryStack accepted a non-positive add amount.");
                for (int i = 0; i < stack.Capacity; i++)
                {
                    Require(stack.TryAdd(ResourceType.Wood, 1),
                        "CarryStack rejected an item before reaching capacity.");
                }

                Require(!stack.TryAdd(ResourceType.Wood, 1),
                    "CarryStack accepted an item beyond capacity.");
                Require(stack.TotalAmount == stack.Capacity,
                    "CarryStack logical amount does not match capacity.");
                Require(!stack.TryRemove(ResourceType.Wood, 0)
                        && !stack.TryRemove(ResourceType.Wood, -1)
                        && !stack.TryRemove(ResourceType.Wood, stack.Capacity + 1),
                    "CarryStack accepted an invalid removal.");
                Require(carryChangeCount == stack.Capacity,
                    "CarryStack raised Changed for a rejected mutation.");
                Require(addedUnitCount == stack.Capacity,
                    "CarryStack add feedback events did not match accepted inventory mutations.");

                cashObject.AddComponent<BoxCollider>().isTrigger = true;
                CashPile cashPile = cashObject.AddComponent<CashPile>();
                SalePoint salePoint = cashObject.AddComponent<SalePoint>();
                SetObjectReference(salePoint, "carryStack", stack);
                SetObjectReference(salePoint, "cashPile", cashPile);
                SetInteger(salePoint, "woodValue", 5);

                int soldUnitCount = 0;
                int emptiedFeedbackCount = 0;
                salePoint.UnitSold += feedback =>
                {
                    soldUnitCount++;
                    if (feedback.BecameEmpty)
                    {
                        emptiedFeedbackCount++;
                    }

                    Require(feedback.RemainingWood == stack.GetAmount(ResourceType.Wood)
                            && cashPile.StoredCash == soldUnitCount * 5,
                        "Sale feedback fired before authoritative wood/cash state was settled.");
                };

                for (int i = 0; i < stack.Capacity; i++)
                {
                    Require(salePoint.TryUnloadOne(),
                        "Sale Point failed before all carried wood was unloaded.");
                    Require(stack.GetAmount(ResourceType.Wood) == stack.Capacity - i - 1,
                        "Sale Point did not remove exactly one wood.");
                    Require(cashPile.StoredCash == (i + 1) * 5,
                        "Sale Point did not deposit exactly $5 per wood.");
                }

                Require(!salePoint.TryUnloadOne(),
                    "Sale Point sold wood from an empty CarryStack.");
                Require(stack.TotalAmount == 0 && cashPile.StoredCash == 60,
                    "A full 12-wood sale must end with zero wood and $60 in the pile.");
                Require(soldUnitCount == stack.Capacity
                        && removedUnitCount == stack.Capacity
                        && emptiedFeedbackCount == 1,
                    "Sale/CarryStack feedback event totals did not match the authoritative full sale.");
                Require(!stack.TryRemove(ResourceType.Wood, 1),
                    "CarryStack allowed wood to become negative.");

                Require(cashPile.TryWithdrawAll(out int firstCashClaim) && firstCashClaim == 60,
                    "Cash Pile did not atomically claim the full sold amount.");
                Require(!cashPile.TryWithdrawAll(out int duplicateClaim) && duplicateClaim == 0,
                    "Cash Pile allowed a duplicate claim.");
                Require(cashPile.Deposit(5) == 5 && cashPile.StoredCash == 5,
                    "New cash did not remain separately available after a claim.");

                Wallet wallet = walletObject.AddComponent<Wallet>();
                Require(wallet.Deposit(0) == 0 && wallet.Deposit(-5) == 0,
                    "Wallet accepted a non-positive deposit.");
                Require(wallet.Deposit(firstCashClaim) == 60 && wallet.Balance == 60,
                    "Wallet did not receive the claimed cash exactly once.");
                Require(wallet.SpendUpTo(0) == 0 && wallet.SpendUpTo(-5) == 0,
                    "Wallet accepted a non-positive spend.");
                Require(wallet.SpendUpTo(65) == 60 && wallet.Balance == 0,
                    "Wallet insufficient-funds spending was not clamped safely.");

                BoxCollider purchaseTrigger = purchaseObject.AddComponent<BoxCollider>();
                purchaseTrigger.isTrigger = true;
                PurchasePad purchasePad = purchaseObject.AddComponent<PurchasePad>();
                SetObjectReference(purchasePad, "wallet", wallet);
                SetObjectReference(purchasePad, "interactionCollider", purchaseTrigger);
                SetInteger(purchasePad, "totalCost", 120);
                SetInteger(purchasePad, "spendPerTick", 5);

                int completionCount = 0;
                int paymentFeedbackTotal = 0;
                purchasePad.Completed += () => completionCount++;
                purchasePad.PaymentProcessed += (spentAmount, remainingCost) =>
                {
                    paymentFeedbackTotal += spentAmount;
                    Require(remainingCost == purchasePad.RemainingCost,
                        "Purchase feedback fired before authoritative progress was updated.");
                };
                Require(purchasePad.ProcessPaymentStep() == 0 && purchasePad.RemainingCost == 120,
                    "Purchase Pad changed progress with an empty wallet.");
                wallet.Deposit(3);
                Require(purchasePad.ProcessPaymentStep() == 3
                        && purchasePad.RemainingCost == 117
                        && wallet.Balance == 0,
                    "Purchase Pad did not consume the actual partial wallet balance.");

                int remainingBeforeLeave = purchasePad.RemainingCost;
                purchasePad.enabled = false;
                purchasePad.enabled = true;
                Require(purchasePad.RemainingCost == remainingBeforeLeave,
                    "Purchase Pad lost partial progress across leave/re-entry lifecycle.");
                Require(purchasePad.ProcessPaymentStep() == 0,
                    "Purchase Pad advanced while the wallet was empty.");

                wallet.Deposit(117);
                int paymentGuard = 0;
                while (!purchasePad.IsCompleted && paymentGuard++ < 30)
                {
                    purchasePad.ProcessPaymentStep();
                }

                Require(purchasePad.IsCompleted
                        && purchasePad.RemainingCost == 0
                        && wallet.Balance == 0,
                    "Purchase Pad failed to complete an exact $120 payment.");
                Require(completionCount == 1, "Purchase Pad completed more than once.");
                Require(paymentFeedbackTotal == 120,
                    "Purchase payment feedback did not equal the actual $120 spent.");
                wallet.Deposit(10);
                Require(purchasePad.ProcessPaymentStep() == 0
                        && wallet.Balance == 10
                        && completionCount == 1,
                    "Completed Purchase Pad consumed additional cash or completed twice.");

                WoodSpawner spawner = spawnerObject.AddComponent<WoodSpawner>();
                GameObject secondCutter = new GameObject("Second Cutter Validation");
                secondCutter.transform.SetParent(upgradeObject.transform, false);
                secondCutter.SetActive(false);
                WoodProductionUpgrade upgrade = upgradeObject.AddComponent<WoodProductionUpgrade>();
                SetObjectReference(upgrade, "purchasePad", purchasePad);
                SetObjectReference(upgrade, "woodSpawner", spawner);
                SetObjectReference(upgrade, "secondCutterVisual", secondCutter);
                SetFloat(upgrade, "productionMultiplier", 2f);

                int appliedFeedbackCount = 0;
                upgrade.Applied += () =>
                {
                    appliedFeedbackCount++;
                    Require(Mathf.Approximately(spawner.ProductionRateMultiplier, 2f)
                            && secondCutter.activeSelf,
                        "Upgrade feedback fired before authoritative production state was applied.");
                };

                float intervalBefore = spawner.EffectiveSpawnInterval;
                Require(upgrade.TryApply(), "Production upgrade did not apply after purchase completion.");
                Require(Mathf.Approximately(spawner.ProductionRateMultiplier, 2f)
                        && Mathf.Approximately(spawner.EffectiveSpawnInterval, intervalBefore * 0.5f),
                    "Production upgrade did not produce an exact 2x spawn cadence.");
                Require(secondCutter.activeSelf, "Production upgrade did not reveal the second cutter.");
                Require(appliedFeedbackCount == 1,
                    "Production upgrade applied feedback did not fire exactly once.");
                Require(!upgrade.TryApply()
                        && Mathf.Approximately(spawner.ProductionRateMultiplier, 2f)
                        && appliedFeedbackCount == 1,
                    "Production upgrade was not idempotent.");
            }
            finally
            {
                Object.DestroyImmediate(upgradeObject);
                Object.DestroyImmediate(spawnerObject);
                Object.DestroyImmediate(purchaseObject);
                Object.DestroyImmediate(walletObject);
                Object.DestroyImmediate(cashObject);
                Object.DestroyImmediate(carryObject);
            }
        }

        private static void ValidateM3Logic()
        {
            GameObject validationRoot = new GameObject("M3 Logic Validation");
            try
            {
                GameObject workerOwner = new GameObject("Worker Claimant");
                workerOwner.transform.SetParent(validationRoot.transform, false);
                GameObject playerOwner = new GameObject("Player Claimant");
                playerOwner.transform.SetParent(validationRoot.transform, false);
                GameObject pickupObject = new GameObject("Claimed Wood");
                pickupObject.transform.SetParent(validationRoot.transform, false);
                pickupObject.AddComponent<BoxCollider>();
                ResourcePickup pickup = pickupObject.AddComponent<ResourcePickup>();
                pickup.Configure(ResourceType.Wood, 1);
                pickup.PrepareForSpawn(Vector3.zero, Quaternion.identity);

                Require(pickup.TryClaim(
                            workerOwner,
                            ResourceClaimPriority.Worker,
                            out ResourceClaimHandle workerClaim)
                        && workerClaim.IsValid
                        && pickup.IsClaimedBy(workerOwner),
                    "Worker could not establish one soft loose-wood claim.");
                Require(pickup.TryBeginAttraction(
                            playerOwner,
                            ResourceClaimPriority.Player,
                            out ResourceClaimHandle playerClaim)
                        && playerClaim.IsAttractionValid
                        && !workerClaim.IsValid,
                    "Player did not atomically preempt the worker's soft claim.");
                Require(!pickup.TryConsumeClaim(
                            workerClaim,
                            out ResourceType duplicateType,
                            out int duplicateAmount)
                        && duplicateAmount == 0,
                    "A preempted worker claim consumed loose wood.");
                Require(pickup.TryCompleteAttraction(playerClaim)
                        && !playerClaim.IsAttractionValid
                        && !pickupObject.activeSelf,
                    "Player attraction did not consume loose wood exactly once.");

                pickupObject.SetActive(true);
                pickup.PrepareForSpawn(Vector3.one, Quaternion.identity);
                Require(!workerClaim.IsValid && !playerClaim.IsAttractionValid,
                    "A recycled ResourcePickup revived a stale ownership handle.");
                Require(pickup.TryClaim(
                            workerOwner,
                            ResourceClaimPriority.Worker,
                            out ResourceClaimHandle recycledClaim),
                    "Recycled loose wood could not accept a fresh worker claim.");
                pickup.MarkReleased();
                Require(!recycledClaim.IsValid && !pickup.IsClaimed,
                    "A despawned/released ResourcePickup retained stale ownership.");
                pickup.PrepareForSpawn(Vector3.zero, Quaternion.identity);
                Require(pickup.TryClaim(
                            workerOwner,
                            ResourceClaimPriority.Worker,
                            out ResourceClaimHandle abortClaim)
                        && pickup.TryReleaseClaim(abortClaim)
                        && pickup.IsAvailable
                        && !pickup.IsClaimed,
                    "An aborted worker target did not release its loose-wood claim.");

                GameObject carryObject = new GameObject("M3 CarryStack");
                carryObject.transform.SetParent(validationRoot.transform, false);
                CarryStack carryStack = carryObject.AddComponent<CarryStack>();
                Require(carryStack.TryReserveCapacity(carryStack.Capacity)
                        && carryStack.ReservedCapacity == carryStack.Capacity
                        && carryStack.AvailableCapacity == 0
                        && !carryStack.TryAdd(ResourceType.Wood, 1)
                        && !carryStack.TryReserveCapacity(1),
                    "CarryStack shared reservations did not protect the 12-item capacity.");
                Require(carryStack.ReleaseReservedCapacity(carryStack.Capacity)
                        && carryStack.ReservedCapacity == 0,
                    "CarryStack failed to release a shared capacity reservation.");

                GameObject stockpileObject = new GameObject("M3 WoodStockpile");
                stockpileObject.transform.SetParent(validationRoot.transform, false);
                WoodStockpile stockpile = stockpileObject.AddComponent<WoodStockpile>();
                int depositEventCount = 0;
                stockpile.WoodDeposited += storedWood =>
                {
                    depositEventCount++;
                    Require(storedWood == stockpile.StoredWood,
                        "Stockpile deposit feedback preceded authoritative state.");
                };
                Require(!stockpile.TryTransferOneTo(carryStack)
                        && stockpile.StoredWood == 0,
                    "Empty Wood Stockpile allowed a negative withdrawal.");
                stockpile.enabled = false;
                Require(!stockpile.TryReserveIncoming(out _)
                        && !stockpile.TryTransferOneTo(carryStack),
                    "Disabled Wood Stockpile accepted an incoming or outgoing transfer.");
                stockpile.enabled = true;

                for (int i = 0; i < stockpile.Capacity; i++)
                {
                    Require(stockpile.TryReserveIncoming(out WoodStockpileReservation reservation)
                            && stockpile.StoredWood + stockpile.IncomingReservations
                               <= stockpile.Capacity
                            && stockpile.TryDepositReserved(reservation),
                        "Wood Stockpile failed an in-capacity reserve/deposit cycle.");
                }

                Require(stockpile.StoredWood == stockpile.Capacity
                        && stockpile.IncomingReservations == 0
                        && stockpile.IsFull
                        && depositEventCount == stockpile.Capacity
                        && !stockpile.TryReserveIncoming(out _),
                    "Wood Stockpile exceeded capacity or accepted work while full.");

                for (int i = 0; i < carryStack.Capacity; i++)
                {
                    Require(stockpile.TryTransferOneTo(carryStack),
                        "Stockpile withdrawal stopped before CarryStack reached 12/12.");
                }

                Require(carryStack.TotalAmount == carryStack.Capacity
                        && stockpile.StoredWood == stockpile.Capacity - carryStack.Capacity
                        && !stockpile.TryTransferOneTo(carryStack),
                    "Stockpile withdrawal exceeded CarryStack capacity or mutated stock incorrectly.");
                Require(carryStack.TryRemove(ResourceType.Wood, carryStack.Capacity),
                    "M3 validation could not clear the full CarryStack.");

                while (!stockpile.IsFull)
                {
                    Require(stockpile.TryReserveIncoming(out WoodStockpileReservation refill)
                            && stockpile.TryDepositReserved(refill),
                        "Wood Stockpile could not refill to capacity.");
                }

                Require(stockpile.TryTransferOneTo(carryStack)
                        && stockpile.StoredWood == stockpile.Capacity - 1,
                    "Withdrawing from a full stockpile did not free exactly one slot.");
                Require(stockpile.TryReserveIncoming(out WoodStockpileReservation resumedReservation)
                        && stockpile.StoredWood + stockpile.IncomingReservations
                           == stockpile.Capacity,
                    "Freeing one stockpile slot did not permit one incoming reservation.");
                Require(stockpile.ReleaseIncoming(resumedReservation),
                    "Stockpile failed to release a valid incoming reservation.");
                Require(stockpile.TryReserveIncoming(out WoodStockpileReservation currentReservation),
                    "Stockpile failed to replace a released incoming reservation.");
                Require(!stockpile.TryDepositReserved(resumedReservation)
                        && stockpile.TryDepositReserved(currentReservation)
                        && stockpile.StoredWood == stockpile.Capacity
                        && stockpile.IncomingReservations == 0,
                    "Stockpile accepted a stale reservation or failed an exactly-once deposit.");

                GameObject walletObject = new GameObject("M3 Wallet");
                walletObject.transform.SetParent(validationRoot.transform, false);
                Wallet wallet = walletObject.AddComponent<Wallet>();
                GameObject padObject = new GameObject("M3 Worker Purchase Pad");
                padObject.transform.SetParent(validationRoot.transform, false);
                BoxCollider padCollider = padObject.AddComponent<BoxCollider>();
                padCollider.isTrigger = true;
                PurchasePad workerPad = padObject.AddComponent<PurchasePad>();
                SetObjectReference(workerPad, "wallet", wallet);
                SetObjectReference(workerPad, "interactionCollider", padCollider);
                SetString(workerPad, "purchaseLabel", "LUMBER WORKER");
                SetBoolean(workerPad, "startsAvailable", false);
                SetInteger(workerPad, "totalCost", 240);
                SetInteger(workerPad, "spendPerTick", 5);

                wallet.Deposit(240);
                Require(workerPad.ProcessPaymentStep() == 0
                        && workerPad.RemainingCost == 240
                        && wallet.Balance == 240,
                    "Locked Worker Purchase Pad accepted payment.");
                Require(workerPad.SetAvailable(true),
                    "Worker Purchase Pad could not become available after its prerequisite.");
                for (int i = 0; i < 13; i++)
                {
                    Require(workerPad.ProcessPaymentStep() == 5,
                        "Worker Purchase Pad rejected valid partial funding.");
                }

                Require(workerPad.RemainingCost == 175 && wallet.Balance == 175,
                    "Worker Purchase Pad partial progress is incorrect.");
                workerPad.enabled = false;
                workerPad.enabled = true;
                Require(workerPad.RemainingCost == 175,
                    "Worker Purchase Pad lost partial progress across lifecycle changes.");

                int workerCompletionCount = 0;
                workerPad.Completed += () => workerCompletionCount++;
                int paymentGuard = 0;
                while (!workerPad.IsCompleted && paymentGuard++ < 40)
                {
                    workerPad.ProcessPaymentStep();
                }

                Require(workerPad.IsCompleted
                        && workerPad.RemainingCost == 0
                        && wallet.Balance == 0
                        && workerCompletionCount == 1
                        && workerPad.ProcessPaymentStep() == 0,
                    "Worker Purchase Pad did not complete exactly once without a negative wallet.");
            }
            finally
            {
                Object.DestroyImmediate(validationRoot);
            }
        }

        private static GameObject FindRoot(Scene scene, string rootName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == rootName)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static bool IsGeneratedScratchScene(Scene scene)
        {
            return scene.IsValid()
                   && string.IsNullOrEmpty(scene.path)
                   && FindRoot(scene, "Ground") != null
                   && FindRoot(scene, "Player") != null
                   && FindRoot(scene, "Main Camera") != null
                   && FindRoot(scene, "Wood Spawner") != null;
        }

        private static Object GetObjectReference(Object target, string propertyName)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            return property != null ? property.objectReferenceValue : null;
        }

        private static int GetIntegerValue(Object target, string propertyName)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on {target.GetType().Name}.");
            }

            return property.intValue;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
