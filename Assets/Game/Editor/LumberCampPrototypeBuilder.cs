using System;
using System.Collections.Generic;
using System.IO;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.UI;
using IndustryTycoon.Workers;
using UnityEditor;
using UnityEditor.Build.Reporting;
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

        private readonly struct M8Services
        {
            public M8Services(
                LumberCampCompletion completion,
                LumberCampPacingProbe pacingProbe,
                ParticleSystem completionParticles)
            {
                Completion = completion;
                PacingProbe = pacingProbe;
                CompletionParticles = completionParticles;
            }

            public LumberCampCompletion Completion { get; }
            public LumberCampPacingProbe PacingProbe { get; }
            public ParticleSystem CompletionParticles { get; }
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

        public static void BuildWindowsPlayerFromCommandLine()
        {
            string buildPath = GetCommandLineValue("-customBuildPath");
            if (string.IsNullOrWhiteSpace(buildPath))
            {
                throw new InvalidOperationException(
                    "BuildWindowsPlayerFromCommandLine requires -customBuildPath <file.exe>.");
            }

            string fullBuildPath = Path.GetFullPath(buildPath);
            string buildDirectory = Path.GetDirectoryName(fullBuildPath);
            if (string.IsNullOrWhiteSpace(buildDirectory))
            {
                throw new InvalidOperationException(
                    $"Invalid Windows Player build path: {fullBuildPath}");
            }

            Directory.CreateDirectory(buildDirectory);
            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = fullBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows Player build failed: {report.summary.result}, "
                    + $"{report.summary.totalErrors} errors, "
                    + $"{report.summary.totalWarnings} warnings.");
            }

            Debug.Log(
                $"Windows Player build passed: {fullBuildPath} "
                + $"({report.summary.totalSize} bytes, "
                + $"{report.summary.totalWarnings} warnings).");
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
            Material plankMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Plank.mat",
                new Color(0.86f, 0.61f, 0.29f),
                0.24f);
            Material plankAccentMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Plank_Accent.mat",
                new Color(0.48f, 0.24f, 0.08f),
                0.18f);
            Material crateMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Crate.mat",
                new Color(0.57f, 0.32f, 0.11f),
                0.20f);
            Material crateAccentMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Crate_Accent.mat",
                new Color(0.94f, 0.66f, 0.22f),
                0.24f);
            Material processorMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Processor.mat",
                new Color(0.10f, 0.38f, 0.48f),
                0.30f);
            Material packingMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Packing_Station.mat",
                new Color(0.46f, 0.18f, 0.58f),
                0.30f);
            Material courierMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Courier.mat",
                new Color(0.08f, 0.44f, 0.64f),
                0.32f);
            Material courierAccentMaterial = CreateOrUpdateMaterial(
                MaterialFolder + "/Courier_Accent.mat",
                new Color(1f, 0.70f, 0.16f),
                0.26f);
            Material feedbackParticleMaterial = CreateOrUpdateParticleMaterial(
                MaterialFolder + "/Feedback_Particle.mat");

            GameObject woodVisualPrefab = BuildWoodVisualPrefab(
                barkMaterial,
                cutMaterial,
                plankMaterial,
                plankAccentMaterial,
                crateMaterial,
                crateAccentMaterial);
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
            SalePoint salePoint = CreateSalePoint(
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
            FirstWorkerUnlock workerUnlock = CreateWorkerAutomation(
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
            FirstProcessorUnlock processorUnlock = CreateWoodProcessing(
                scene,
                workerUnlock,
                stockpile,
                wallet,
                cashPile,
                player.GetComponent<CarryStack>(),
                playerCollider,
                purchaseMaterial,
                purchaseCompleteMaterial,
                cashAccentMaterial,
                cashVisualPrefab,
                cashCollectionTarget,
                woodVisualPrefab,
                processorMaterial,
                sawBladeMaterial,
                plankMaterial,
                crateMaterial,
                crateAccentMaterial,
                packingMaterial,
                courierMaterial,
                courierAccentMaterial,
                feedbackParticleMaterial,
                feedbackServices,
                followCamera);
            FirstAutoFeederUnlock autoFeederUnlock = FindRoot(
                    scene,
                    "Auto Feeder Automation")
                ?.GetComponent<FirstAutoFeederUnlock>();
            FirstPackingStationUnlock packingStationUnlock = FindRoot(
                    scene,
                    "Packing Station Automation")
                ?.GetComponent<FirstPackingStationUnlock>();
            FirstCourierUnlock courierUnlock = FindRoot(
                    scene,
                    "Courier Automation")
                ?.GetComponent<FirstCourierUnlock>();
            CrateCourier courier = FindRoot(scene, "Crate Courier Delivery")
                ?.GetComponentInChildren<CrateCourier>(true);
            Require(processorUnlock != null
                    && autoFeederUnlock != null
                    && packingStationUnlock != null
                    && courierUnlock != null
                    && courier != null,
                "M8 requires the complete M4-M7 progression chain.");

            M8Services m8Services = CreateM8Progression(
                scene,
                player.GetComponent<CarryStack>(),
                salePoint,
                productionUpgrade,
                workerUnlock,
                processorUnlock,
                autoFeederUnlock,
                packingStationUnlock,
                courierUnlock,
                courier,
                processorMaterial,
                sawBladeMaterial,
                purchaseCompleteMaterial,
                feedbackParticleMaterial);
            CreateLighting(scene);
            CreateHud(
                scene,
                player.GetComponent<CarryStack>(),
                wallet,
                productionUpgrade,
                workerUnlock,
                processorUnlock,
                autoFeederUnlock,
                packingStationUnlock,
                courierUnlock,
                m8Services,
                feedbackServices,
                followCamera);

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
            EnsureFolder("Assets/Game/Processing");
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

        private static GameObject BuildWoodVisualPrefab(
            Material barkMaterial,
            Material cutMaterial,
            Material plankMaterial,
            Material plankAccentMaterial,
            Material crateMaterial,
            Material crateAccentMaterial)
        {
            GameObject root = new GameObject("WoodCarryVisual");
            try
            {
                GameObject woodVisual = new GameObject("Wood Visual");
                woodVisual.transform.SetParent(root.transform, false);
                CreateWoodGeometry(woodVisual.transform, barkMaterial, cutMaterial);

                GameObject plankVisual = new GameObject("Plank Visual");
                plankVisual.transform.SetParent(root.transform, false);
                CreatePlankGeometry(plankVisual.transform, plankMaterial, plankAccentMaterial);
                plankVisual.SetActive(false);

                GameObject crateVisual = new GameObject("Crate Visual");
                crateVisual.transform.SetParent(root.transform, false);
                CreateCrateGeometry(crateVisual.transform, crateMaterial, crateAccentMaterial);
                crateVisual.SetActive(false);

                ResourceVisual resourceVisual = root.AddComponent<ResourceVisual>();
                SetObjectReference(resourceVisual, "woodRoot", woodVisual);
                SetObjectReference(resourceVisual, "plankRoot", plankVisual);
                SetObjectReference(resourceVisual, "crateRoot", crateVisual);
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

        private static void CreatePlankGeometry(
            Transform parent,
            Material plankMaterial,
            Material accentMaterial)
        {
            GameObject board = CreatePrimitiveChild(
                "Plank Board",
                PrimitiveType.Cube,
                parent,
                plankMaterial);
            board.transform.localScale = new Vector3(1.12f, 0.16f, 0.36f);

            GameObject stripe = CreatePrimitiveChild(
                "Plank Grain",
                PrimitiveType.Cube,
                parent,
                accentMaterial);
            stripe.transform.localPosition = new Vector3(0f, 0.087f, 0f);
            stripe.transform.localScale = new Vector3(0.90f, 0.025f, 0.055f);
        }

        private static void CreateCrateGeometry(
            Transform parent,
            Material crateMaterial,
            Material accentMaterial)
        {
            GameObject box = CreatePrimitiveChild(
                "Crate Box",
                PrimitiveType.Cube,
                parent,
                crateMaterial);
            box.transform.localScale = new Vector3(0.84f, 0.68f, 0.72f);

            GameObject horizontalBand = CreatePrimitiveChild(
                "Crate Horizontal Band",
                PrimitiveType.Cube,
                parent,
                accentMaterial);
            horizontalBand.transform.localPosition = new Vector3(0f, 0f, -0.372f);
            horizontalBand.transform.localScale = new Vector3(0.90f, 0.14f, 0.045f);

            GameObject verticalBand = CreatePrimitiveChild(
                "Crate Vertical Band",
                PrimitiveType.Cube,
                parent,
                accentMaterial);
            verticalBand.transform.localPosition = new Vector3(0f, 0f, -0.376f);
            verticalBand.transform.localScale = new Vector3(0.14f, 0.74f, 0.05f);

            GameObject lid = CreatePrimitiveChild(
                "Crate Lid",
                PrimitiveType.Cube,
                parent,
                accentMaterial);
            lid.transform.localPosition = new Vector3(0f, 0.37f, 0f);
            lid.transform.localScale = new Vector3(0.90f, 0.08f, 0.78f);
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
            spawnerObject.transform.position = new Vector3(1.5f, 0f, 5.25f);

            WoodSpawner spawner = spawnerObject.AddComponent<WoodSpawner>();
            spawner.ConfigurePrefab(woodResourcePrefab);
            SetVector2(spawner, "spawnArea", new Vector2(7f, 7.5f));
            SetInteger(spawner, "maximumActiveCount", 24);
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
                "SELL MATERIALS\nWOOD $5  PLANK $15  CRATE $40",
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
            SetInteger(salePoint, "plankValue", 15);
            SetInteger(salePoint, "crateValue", 40);
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

        private static FirstProcessorUnlock CreateWoodProcessing(
            Scene scene,
            FirstWorkerUnlock workerUnlock,
            WoodStockpile stockpile,
            Wallet wallet,
            CashPile cashPile,
            CarryStack carryStack,
            Collider playerCollider,
            Material purchaseAvailableMaterial,
            Material purchaseCompletedMaterial,
            Material cashAccentMaterial,
            GameObject cashVisualPrefab,
            Transform tokenOrigin,
            GameObject resourceVisualPrefab,
            Material processorMaterial,
            Material bladeMaterial,
            Material plankMaterial,
            Material crateMaterial,
            Material crateAccentMaterial,
            Material packingMaterial,
            Material courierMaterial,
            Material courierAccentMaterial,
            Material particleMaterial,
            FeedbackServices feedbackServices,
            SmoothFollowCamera followCamera)
        {
            PurchasePad processorPurchasePad = CreatePurchasePad(
                scene,
                "Processor Purchase Pad",
                "WOOD PROCESSOR",
                new Vector3(-8.2f, 0f, -0.25f),
                360,
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

            GameObject processorRoot = new GameObject("Wood Processor");
            SceneManager.MoveGameObjectToScene(processorRoot, scene);
            processorRoot.transform.position = new Vector3(-8.2f, 0f, 5.2f);

            GameObject processorVisual = new GameObject("Processor Visual");
            processorVisual.transform.SetParent(processorRoot.transform, false);

            GameObject platform = CreatePrimitiveChild(
                "Processor Platform",
                PrimitiveType.Cube,
                processorVisual.transform,
                processorMaterial);
            platform.transform.localPosition = new Vector3(0f, 0.20f, 0.30f);
            platform.transform.localScale = new Vector3(3.8f, 0.34f, 2.35f);

            GameObject housing = CreatePrimitiveChild(
                "Processor Housing",
                PrimitiveType.Cube,
                processorVisual.transform,
                processorMaterial);
            housing.transform.localPosition = new Vector3(0f, 0.78f, 0.46f);
            housing.transform.localScale = new Vector3(1.52f, 1.02f, 1.08f);

            GameObject workingBlade = CreatePrimitiveChild(
                "Working Blade",
                PrimitiveType.Cylinder,
                processorVisual.transform,
                bladeMaterial);
            workingBlade.transform.localPosition = new Vector3(0f, 1.35f, -0.14f);
            workingBlade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            workingBlade.transform.localScale = new Vector3(0.66f, 0.055f, 0.66f);

            GameObject inputZone = new GameObject("Processor Input Zone");
            inputZone.transform.SetParent(processorRoot.transform, false);
            inputZone.transform.localPosition = new Vector3(-1.60f, 0f, -1.55f);
            BoxCollider inputTrigger = AddKinematicTrigger(inputZone, new Vector3(2.2f, 2.4f, 2.2f));
            CreateZoneDisc("Wood Input Area", inputZone.transform, processorMaterial, 1.08f);
            TextMesh inputText = CreateWorldLabel(
                "Input Amount",
                inputZone.transform,
                "WOOD IN  0 / 24",
                new Vector3(0f, 1.38f, 0.08f),
                new Color(1f, 0.78f, 0.34f));

            GameObject outputZone = new GameObject("Processor Output Zone");
            outputZone.transform.SetParent(processorRoot.transform, false);
            outputZone.transform.localPosition = new Vector3(1.60f, 0f, -1.55f);
            BoxCollider outputTrigger = AddKinematicTrigger(outputZone, new Vector3(2.2f, 2.4f, 2.2f));
            CreateZoneDisc("Plank Output Area", outputZone.transform, plankMaterial, 1.08f);
            TextMesh outputText = CreateWorldLabel(
                "Output Amount",
                outputZone.transform,
                "PLANK OUT  0 / 12",
                new Vector3(0f, 1.38f, 0.08f),
                new Color(1f, 0.88f, 0.52f));

            TextMesh statusText = CreateWorldLabel(
                "Processor Status",
                processorVisual.transform,
                "SAWMILL  IDLE",
                new Vector3(0f, 2.28f, 0.35f),
                Color.white);

            GameObject outputVisualRoot = new GameObject("Processor Output Visuals");
            outputVisualRoot.transform.SetParent(processorVisual.transform, false);
            outputVisualRoot.transform.localPosition = new Vector3(1.12f, 0.48f, 0.18f);

            ParticleSystem completionParticles = CreateFeedbackParticleSystem(
                "Processing Complete Burst",
                outputVisualRoot.transform,
                new Vector3(0f, 0.42f, 0f),
                new Color(1f, 0.69f, 0.28f),
                particleMaterial,
                32,
                0.34f,
                1.28f,
                0.11f);

            WoodProcessor processor = processorRoot.AddComponent<WoodProcessor>();
            SetInteger(processor, "inputCapacity", 24);
            SetInteger(processor, "outputCapacity", 12);
            SetFloat(processor, "processingDuration", 1.10f);

            ProcessorInputZone inputCollector = inputZone.AddComponent<ProcessorInputZone>();
            SetObjectReference(inputCollector, "processor", processor);
            SetObjectReference(inputCollector, "carryStack", carryStack);
            SetObjectReference(inputCollector, "playerCollider", playerCollider);
            SetFloat(inputCollector, "transferInterval", 0.10f);
            Require(inputCollector.GetComponent<Collider>() == inputTrigger,
                "Processor input component must share its trigger collider.");

            ProcessorOutputZone outputCollector = outputZone.AddComponent<ProcessorOutputZone>();
            SetObjectReference(outputCollector, "processor", processor);
            SetObjectReference(outputCollector, "carryStack", carryStack);
            SetObjectReference(outputCollector, "playerCollider", playerCollider);
            SetFloat(outputCollector, "transferInterval", 0.10f);
            Require(outputCollector.GetComponent<Collider>() == outputTrigger,
                "Processor output component must share its trigger collider.");

            WoodProcessorFeedback processorFeedback = processorRoot.AddComponent<WoodProcessorFeedback>();
            SetObjectReference(processorFeedback, "processor", processor);
            SetObjectReference(processorFeedback, "workingBlade", workingBlade.transform);
            SetObjectReference(processorFeedback, "outputVisualRoot", outputVisualRoot.transform);
            SetObjectReference(processorFeedback, "resourceVisualPrefab", resourceVisualPrefab);
            SetObjectReference(processorFeedback, "inputText", inputText);
            SetObjectReference(processorFeedback, "outputText", outputText);
            SetObjectReference(processorFeedback, "statusText", statusText);
            SetObjectReference(processorFeedback, "completionParticles", completionParticles);
            SetInteger(processorFeedback, "maximumOutputVisuals", 6);
            SetInteger(processorFeedback, "planksPerVisual", 2);
            SetInteger(processorFeedback, "itemsPerRow", 3);
            SetFloat(processorFeedback, "visualScale", 0.82f);
            SetFloat(processorFeedback, "bladeRotationSpeed", 280f);
            SetFloat(processorFeedback, "outputPopDuration", 0.16f);

            processorRoot.SetActive(false);
            processorPurchasePad.gameObject.SetActive(false);

            GameObject automationRoot = new GameObject("Processor Automation");
            SceneManager.MoveGameObjectToScene(automationRoot, scene);

            FirstProcessorUnlock processorUnlock = automationRoot.AddComponent<FirstProcessorUnlock>();
            SetObjectReference(processorUnlock, "workerUnlock", workerUnlock);
            SetObjectReference(processorUnlock, "processorPurchasePad", processorPurchasePad);
            SetObjectReference(processorUnlock, "processorPurchasePadRoot", processorPurchasePad.gameObject);
            SetObjectReference(processorUnlock, "processorRoot", processorRoot);

            ParticleSystem unlockParticles = CreateFeedbackParticleSystem(
                "Processor Unlock Burst",
                automationRoot.transform,
                processorRoot.transform.position + new Vector3(0f, 1.0f, 0f),
                new Color(1f, 0.72f, 0.24f),
                particleMaterial,
                40,
                0.58f,
                1.85f,
                0.14f);

            ProcessorUnlockFeedback unlockFeedback = automationRoot.AddComponent<ProcessorUnlockFeedback>();
            SetObjectReference(unlockFeedback, "processorUnlock", processorUnlock);
            SetObjectReference(unlockFeedback, "processorVisual", processorVisual.transform);
            SetObjectReference(unlockFeedback, "unlockParticles", unlockParticles);
            SetObjectReference(unlockFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(unlockFeedback, "hapticFeedback", feedbackServices.Haptics);
            SetObjectReference(unlockFeedback, "followCamera", followCamera);
            SetFloat(unlockFeedback, "unlockDuration", 0.65f);

            FirstAutoFeederUnlock autoFeederUnlock = CreateFirstInputLogistics(
                scene,
                processorUnlock,
                stockpile,
                processor,
                wallet,
                playerCollider,
                purchaseAvailableMaterial,
                purchaseCompletedMaterial,
                cashAccentMaterial,
                cashVisualPrefab,
                tokenOrigin,
                resourceVisualPrefab,
                processorMaterial,
                bladeMaterial,
                particleMaterial,
                feedbackServices,
                followCamera);
            CreatePackingStation(
                scene,
                autoFeederUnlock,
                wallet,
                cashPile,
                carryStack,
                playerCollider,
                purchaseAvailableMaterial,
                purchaseCompletedMaterial,
                cashAccentMaterial,
                cashVisualPrefab,
                tokenOrigin,
                resourceVisualPrefab,
                packingMaterial,
                bladeMaterial,
                plankMaterial,
                crateMaterial,
                crateAccentMaterial,
                particleMaterial,
                courierMaterial,
                courierAccentMaterial,
                feedbackServices,
                followCamera);
            return processorUnlock;
        }

        private static FirstAutoFeederUnlock CreateFirstInputLogistics(
            Scene scene,
            FirstProcessorUnlock processorUnlock,
            WoodStockpile stockpile,
            WoodProcessor processor,
            Wallet wallet,
            Collider playerCollider,
            Material purchaseAvailableMaterial,
            Material purchaseCompletedMaterial,
            Material cashAccentMaterial,
            GameObject cashVisualPrefab,
            Transform tokenOrigin,
            GameObject woodVisualPrefab,
            Material conveyorMaterial,
            Material rollerMaterial,
            Material particleMaterial,
            FeedbackServices feedbackServices,
            SmoothFollowCamera followCamera)
        {
            PurchasePad purchasePad = CreatePurchasePad(
                scene,
                "Auto Feeder Purchase Pad",
                "AUTO FEEDER",
                new Vector3(-8.2f, 0f, -8f),
                600,
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

            GameObject feederRoot = new GameObject("Wood Auto Feeder");
            SceneManager.MoveGameObjectToScene(feederRoot, scene);

            GameObject feederVisual = new GameObject("Auto Feeder Visual");
            feederVisual.transform.SetParent(feederRoot.transform, false);

            GameObject routeStartObject = new GameObject("Route Start");
            routeStartObject.transform.SetParent(feederVisual.transform, false);
            routeStartObject.transform.position = new Vector3(5.45f, 1.02f, 5.15f);

            GameObject routeControlObject = new GameObject("Route Control");
            routeControlObject.transform.SetParent(feederVisual.transform, false);
            routeControlObject.transform.position = new Vector3(-1.6f, 1.18f, 7.25f);

            GameObject routeEndObject = new GameObject("Route End");
            routeEndObject.transform.SetParent(feederVisual.transform, false);
            routeEndObject.transform.position = new Vector3(-9.75f, 1.02f, 3.72f);

            Transform routeStart = routeStartObject.transform;
            Transform routeControl = routeControlObject.transform;
            Transform routeEnd = routeEndObject.transform;

            CreateConveyorRail(
                "Source Left Rail",
                feederVisual.transform,
                routeStart.position,
                routeControl.position,
                -0.48f,
                conveyorMaterial);
            CreateConveyorRail(
                "Source Right Rail",
                feederVisual.transform,
                routeStart.position,
                routeControl.position,
                0.48f,
                conveyorMaterial);
            CreateConveyorRail(
                "Destination Left Rail",
                feederVisual.transform,
                routeControl.position,
                routeEnd.position,
                -0.48f,
                conveyorMaterial);
            CreateConveyorRail(
                "Destination Right Rail",
                feederVisual.transform,
                routeControl.position,
                routeEnd.position,
                0.48f,
                conveyorMaterial);

            const int SlatCount = 19;
            for (int i = 0; i < SlatCount; i++)
            {
                float progress = i / (float)(SlatCount - 1);
                Vector3 position = EvaluateQuadraticRoute(
                    routeStart.position,
                    routeControl.position,
                    routeEnd.position,
                    progress);
                Vector3 direction = EvaluateQuadraticRouteDirection(
                    routeStart.position,
                    routeControl.position,
                    routeEnd.position,
                    progress);
                GameObject slat = CreatePrimitiveChild(
                    $"Belt Slat {i + 1:00}",
                    PrimitiveType.Cube,
                    feederVisual.transform,
                    conveyorMaterial);
                slat.transform.position = position + new Vector3(0f, -0.42f, 0f);
                slat.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                slat.transform.localScale = new Vector3(0.88f, 0.12f, 0.78f);
            }

            GameObject sourceRoller = CreateConveyorRoller(
                "Source Roller",
                feederVisual.transform,
                routeStart.position + new Vector3(0f, -0.34f, 0f),
                EvaluateQuadraticRouteDirection(
                    routeStart.position,
                    routeControl.position,
                    routeEnd.position,
                    0f),
                rollerMaterial);
            GameObject destinationRoller = CreateConveyorRoller(
                "Destination Roller",
                feederVisual.transform,
                routeEnd.position + new Vector3(0f, -0.34f, 0f),
                EvaluateQuadraticRouteDirection(
                    routeStart.position,
                    routeControl.position,
                    routeEnd.position,
                    1f),
                rollerMaterial);

            GameObject transferVisualRoot = new GameObject("Transfer Visual Pool");
            transferVisualRoot.transform.SetParent(feederVisual.transform, false);

            GameObject statusIndicator = CreatePrimitiveChild(
                "Auto Feeder Status Indicator",
                PrimitiveType.Cube,
                feederVisual.transform,
                conveyorMaterial);
            statusIndicator.transform.position = routeControl.position
                                                 + new Vector3(0f, 0.35f, 0f);
            statusIndicator.transform.localScale = new Vector3(0.74f, 0.20f, 0.74f);

            TextMesh statusText = CreateWorldLabel(
                "Auto Feeder Status",
                feederVisual.transform,
                "AUTO FEEDER  OFFLINE",
                routeControl.position + new Vector3(0f, 1.02f, 0f),
                Color.white);

            WoodAutoFeeder feeder = feederRoot.AddComponent<WoodAutoFeeder>();
            WoodAutoFeederFeedback feederFeedback =
                feederRoot.AddComponent<WoodAutoFeederFeedback>();
            SetObjectReference(feeder, "stockpile", stockpile);
            SetObjectReference(feeder, "processor", processor);
            SetObjectReference(feeder, "presentation", feederFeedback);
            SetFloat(feeder, "launchInterval", 0.75f);
            SetFloat(feeder, "travelDuration", 0.55f);

            SetObjectReference(feederFeedback, "autoFeeder", feeder);
            SetObjectReference(
                feederFeedback,
                "transferVisualRoot",
                transferVisualRoot.transform);
            SetObjectReference(feederFeedback, "woodVisualPrefab", woodVisualPrefab);
            SetObjectReference(feederFeedback, "routeStart", routeStart);
            SetObjectReference(feederFeedback, "routeControl", routeControl);
            SetObjectReference(feederFeedback, "routeEnd", routeEnd);
            SetObjectReference(feederFeedback, "statusText", statusText);
            SetObjectReference(
                feederFeedback,
                "statusIndicator",
                statusIndicator.GetComponent<Renderer>());
            SetObjectReference(feederFeedback, "idleMaterial", conveyorMaterial);
            SetObjectReference(feederFeedback, "movingMaterial", purchaseCompletedMaterial);
            SetObjectReference(feederFeedback, "destinationFullMaterial", rollerMaterial);
            SetObjectReference(feederFeedback, "sourceRoller", sourceRoller.transform);
            SetObjectReference(
                feederFeedback,
                "destinationRoller",
                destinationRoller.transform);
            SetInteger(feederFeedback, "visualPoolSize", 2);
            SetFloat(feederFeedback, "transferVisualScale", 0.72f);
            SetFloat(feederFeedback, "rollerSpeed", 260f);

            feederRoot.SetActive(false);
            purchasePad.gameObject.SetActive(false);

            GameObject automationRoot = new GameObject("Auto Feeder Automation");
            SceneManager.MoveGameObjectToScene(automationRoot, scene);

            FirstAutoFeederUnlock unlock =
                automationRoot.AddComponent<FirstAutoFeederUnlock>();
            SetObjectReference(unlock, "processorUnlock", processorUnlock);
            SetObjectReference(unlock, "autoFeederPurchasePad", purchasePad);
            SetObjectReference(
                unlock,
                "autoFeederPurchasePadRoot",
                purchasePad.gameObject);
            SetObjectReference(unlock, "autoFeederRoot", feederRoot);

            ParticleSystem unlockParticles = CreateFeedbackParticleSystem(
                "Auto Feeder Unlock Burst",
                automationRoot.transform,
                routeControl.position + new Vector3(0f, 0.55f, 0f),
                new Color(0.22f, 0.92f, 0.76f),
                particleMaterial,
                40,
                0.58f,
                1.85f,
                0.14f);

            AutoFeederUnlockFeedback unlockFeedback =
                automationRoot.AddComponent<AutoFeederUnlockFeedback>();
            SetObjectReference(unlockFeedback, "autoFeederUnlock", unlock);
            SetObjectReference(unlockFeedback, "autoFeederVisual", feederVisual.transform);
            SetObjectReference(unlockFeedback, "unlockParticles", unlockParticles);
            SetObjectReference(unlockFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(unlockFeedback, "hapticFeedback", feedbackServices.Haptics);
            SetObjectReference(unlockFeedback, "followCamera", followCamera);
            SetFloat(unlockFeedback, "unlockDuration", 0.65f);
            return unlock;
        }

        private static FirstPackingStationUnlock CreatePackingStation(
            Scene scene,
            FirstAutoFeederUnlock autoFeederUnlock,
            Wallet wallet,
            CashPile cashPile,
            CarryStack carryStack,
            Collider playerCollider,
            Material purchaseAvailableMaterial,
            Material purchaseCompletedMaterial,
            Material cashAccentMaterial,
            GameObject cashVisualPrefab,
            Transform tokenOrigin,
            GameObject resourceVisualPrefab,
            Material packingMaterial,
            Material metalMaterial,
            Material plankMaterial,
            Material crateMaterial,
            Material crateAccentMaterial,
            Material particleMaterial,
            Material courierMaterial,
            Material courierAccentMaterial,
            FeedbackServices feedbackServices,
            SmoothFollowCamera followCamera)
        {
            PurchasePad purchasePad = CreatePurchasePad(
                scene,
                "Packing Station Purchase Pad",
                "PACKING STATION",
                new Vector3(-8.2f, 0f, 14.2f),
                900,
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

            GameObject stationRoot = new GameObject("Packing Station");
            SceneManager.MoveGameObjectToScene(stationRoot, scene);
            stationRoot.transform.position = new Vector3(-8.2f, 0f, 11.5f);

            GameObject stationVisual = new GameObject("Packing Workshop Visual");
            stationVisual.transform.SetParent(stationRoot.transform, false);

            GameObject platform = CreatePrimitiveChild(
                "Packing Platform",
                PrimitiveType.Cube,
                stationVisual.transform,
                packingMaterial);
            platform.transform.localPosition = new Vector3(0f, 0.20f, 0.35f);
            platform.transform.localScale = new Vector3(4.1f, 0.34f, 2.55f);

            GameObject rearWall = CreatePrimitiveChild(
                "Workshop Rear Wall",
                PrimitiveType.Cube,
                stationVisual.transform,
                packingMaterial);
            rearWall.transform.localPosition = new Vector3(0f, 1.05f, 1.18f);
            rearWall.transform.localScale = new Vector3(3.45f, 1.52f, 0.20f);

            GameObject roof = CreatePrimitiveChild(
                "Workshop Roof",
                PrimitiveType.Cube,
                stationVisual.transform,
                crateAccentMaterial);
            roof.transform.localPosition = new Vector3(0f, 1.86f, 0.34f);
            roof.transform.localScale = new Vector3(3.9f, 0.18f, 2.08f);

            GameObject leftPost = CreatePrimitiveChild(
                "Workshop Left Post",
                PrimitiveType.Cube,
                stationVisual.transform,
                metalMaterial);
            leftPost.transform.localPosition = new Vector3(-1.62f, 1.02f, 0.38f);
            leftPost.transform.localScale = new Vector3(0.20f, 1.62f, 0.20f);

            GameObject rightPost = CreatePrimitiveChild(
                "Workshop Right Post",
                PrimitiveType.Cube,
                stationVisual.transform,
                metalMaterial);
            rightPost.transform.localPosition = new Vector3(1.62f, 1.02f, 0.38f);
            rightPost.transform.localScale = new Vector3(0.20f, 1.62f, 0.20f);

            GameObject packingTable = CreatePrimitiveChild(
                "Packing Table",
                PrimitiveType.Cube,
                stationVisual.transform,
                crateMaterial);
            packingTable.transform.localPosition = new Vector3(0f, 0.74f, 0.35f);
            packingTable.transform.localScale = new Vector3(2.25f, 0.28f, 1.18f);

            GameObject workingPart = CreatePrimitiveChild(
                "Packing Tape Arm",
                PrimitiveType.Cube,
                stationVisual.transform,
                crateAccentMaterial);
            workingPart.transform.localPosition = new Vector3(0f, 1.22f, 0.34f);
            workingPart.transform.localScale = new Vector3(1.72f, 0.16f, 0.24f);

            GameObject tapeRoll = CreatePrimitiveChild(
                "Packing Tape Roll",
                PrimitiveType.Cylinder,
                workingPart.transform,
                crateAccentMaterial);
            tapeRoll.transform.localPosition = new Vector3(0.72f, 0f, 0f);
            tapeRoll.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tapeRoll.transform.localScale = new Vector3(0.26f, 0.10f, 0.26f);

            GameObject statusIndicator = CreatePrimitiveChild(
                "Packing Status Indicator",
                PrimitiveType.Cube,
                stationVisual.transform,
                packingMaterial);
            statusIndicator.transform.localPosition = new Vector3(0f, 2.04f, 0.95f);
            statusIndicator.transform.localScale = new Vector3(0.86f, 0.18f, 0.16f);

            GameObject inputZoneObject = new GameObject("Packing Input Zone");
            inputZoneObject.transform.SetParent(stationRoot.transform, false);
            inputZoneObject.transform.localPosition = new Vector3(-1.65f, 0f, -1.70f);
            BoxCollider inputTrigger = AddKinematicTrigger(
                inputZoneObject,
                new Vector3(2.2f, 2.4f, 2.2f));
            CreateZoneDisc(
                "Plank Input Area",
                inputZoneObject.transform,
                plankMaterial,
                1.08f);
            TextMesh inputText = CreateWorldLabel(
                "Packing Input Amount",
                inputZoneObject.transform,
                "PLANK IN  0 / 24",
                new Vector3(0f, 1.38f, 0.08f),
                new Color(1f, 0.84f, 0.38f));

            GameObject outputZoneObject = new GameObject("Packing Output Zone");
            outputZoneObject.transform.SetParent(stationRoot.transform, false);
            outputZoneObject.transform.localPosition = new Vector3(1.65f, 0f, -1.70f);
            BoxCollider outputTrigger = AddKinematicTrigger(
                outputZoneObject,
                new Vector3(2.2f, 2.4f, 2.2f));
            CreateZoneDisc(
                "Crate Output Area",
                outputZoneObject.transform,
                crateMaterial,
                1.08f);
            TextMesh outputText = CreateWorldLabel(
                "Packing Output Amount",
                outputZoneObject.transform,
                "CRATE OUT  0 / 12",
                new Vector3(0f, 1.38f, 0.08f),
                new Color(1f, 0.76f, 0.30f));

            GameObject courierPickupPoint = new GameObject("Courier Pickup Point");
            courierPickupPoint.transform.SetParent(stationRoot.transform, false);
            courierPickupPoint.transform.localPosition = new Vector3(2.55f, 0f, -0.65f);
            CreateZoneDisc(
                "Courier Pickup Marker",
                courierPickupPoint.transform,
                courierAccentMaterial,
                0.72f);
            TextMesh courierPickupLabel = CreateWorldLabel(
                "Courier Pickup Label",
                courierPickupPoint.transform,
                "COURIER\nPICKUP",
                new Vector3(0f, 1.60f, 0.08f),
                new Color(1f, 0.82f, 0.34f));
            courierPickupLabel.characterSize = 0.044f;

            TextMesh statusText = CreateWorldLabel(
                "Packing Status",
                stationVisual.transform,
                "PACKER  NO PLANKS",
                new Vector3(0f, 2.52f, 0.48f),
                Color.white);

            GameObject outputVisualRoot = new GameObject("Packing Crate Output Visuals");
            outputVisualRoot.transform.SetParent(stationVisual.transform, false);
            outputVisualRoot.transform.localPosition = new Vector3(1.08f, 0.58f, 0.28f);

            ParticleSystem completionParticles = CreateFeedbackParticleSystem(
                "Packing Complete Burst",
                outputVisualRoot.transform,
                new Vector3(0f, 0.48f, 0f),
                new Color(1f, 0.70f, 0.22f),
                particleMaterial,
                32,
                0.34f,
                1.28f,
                0.11f);

            PackingStation station = stationRoot.AddComponent<PackingStation>();
            SetInteger(station, "inputCapacity", 24);
            SetInteger(station, "outputCapacity", 12);
            SetFloat(station, "processingDuration", 1.50f);

            PackingStationInputZone inputZone =
                inputZoneObject.AddComponent<PackingStationInputZone>();
            SetObjectReference(inputZone, "packingStation", station);
            SetObjectReference(inputZone, "carryStack", carryStack);
            SetObjectReference(inputZone, "playerCollider", playerCollider);
            SetFloat(inputZone, "transferInterval", 0.10f);
            Require(inputZone.GetComponent<Collider>() == inputTrigger,
                "Packing input component must share its trigger collider.");

            PackingStationOutputZone outputZone =
                outputZoneObject.AddComponent<PackingStationOutputZone>();
            SetObjectReference(outputZone, "packingStation", station);
            SetObjectReference(outputZone, "carryStack", carryStack);
            SetObjectReference(outputZone, "playerCollider", playerCollider);
            SetFloat(outputZone, "transferInterval", 0.10f);
            Require(outputZone.GetComponent<Collider>() == outputTrigger,
                "Packing output component must share its trigger collider.");

            PackingStationFeedback stationFeedback =
                stationRoot.AddComponent<PackingStationFeedback>();
            SetObjectReference(stationFeedback, "station", station);
            SetObjectReference(stationFeedback, "workingPart", workingPart.transform);
            SetObjectReference(
                stationFeedback,
                "outputVisualRoot",
                outputVisualRoot.transform);
            SetObjectReference(
                stationFeedback,
                "resourceVisualPrefab",
                resourceVisualPrefab);
            SetObjectReference(stationFeedback, "inputText", inputText);
            SetObjectReference(stationFeedback, "outputText", outputText);
            SetObjectReference(stationFeedback, "statusText", statusText);
            SetObjectReference(
                stationFeedback,
                "statusIndicator",
                statusIndicator.GetComponent<Renderer>());
            SetObjectReference(stationFeedback, "idleMaterial", packingMaterial);
            SetObjectReference(
                stationFeedback,
                "workingMaterial",
                purchaseCompletedMaterial);
            SetObjectReference(
                stationFeedback,
                "outputFullMaterial",
                crateAccentMaterial);
            SetObjectReference(
                stationFeedback,
                "completionParticles",
                completionParticles);
            SetInteger(stationFeedback, "maximumOutputVisuals", 6);
            SetInteger(stationFeedback, "cratesPerVisual", 2);
            SetInteger(stationFeedback, "itemsPerRow", 3);
            SetFloat(stationFeedback, "visualScale", 0.78f);
            SetFloat(stationFeedback, "horizontalSpacing", 0.78f);
            SetFloat(stationFeedback, "verticalSpacing", 0.30f);
            SetFloat(stationFeedback, "depthSpacing", 0.42f);
            SetFloat(stationFeedback, "workingRotationSpeed", 220f);
            SetFloat(stationFeedback, "outputPopDuration", 0.18f);

            stationRoot.SetActive(false);
            purchasePad.gameObject.SetActive(false);

            GameObject automationRoot = new GameObject("Packing Station Automation");
            SceneManager.MoveGameObjectToScene(automationRoot, scene);

            FirstPackingStationUnlock unlock =
                automationRoot.AddComponent<FirstPackingStationUnlock>();
            SetObjectReference(unlock, "autoFeederUnlock", autoFeederUnlock);
            SetObjectReference(unlock, "packingStationPurchasePad", purchasePad);
            SetObjectReference(
                unlock,
                "packingStationPurchasePadRoot",
                purchasePad.gameObject);
            SetObjectReference(unlock, "packingStationRoot", stationRoot);

            ParticleSystem unlockParticles = CreateFeedbackParticleSystem(
                "Packing Station Unlock Burst",
                automationRoot.transform,
                stationRoot.transform.position + new Vector3(0f, 1.15f, 0f),
                new Color(0.92f, 0.42f, 1f),
                particleMaterial,
                40,
                0.58f,
                1.85f,
                0.14f);

            PackingStationUnlockFeedback unlockFeedback =
                automationRoot.AddComponent<PackingStationUnlockFeedback>();
            SetObjectReference(unlockFeedback, "packingStationUnlock", unlock);
            SetObjectReference(unlockFeedback, "packingStationVisual", stationVisual.transform);
            SetObjectReference(unlockFeedback, "unlockParticles", unlockParticles);
            SetObjectReference(unlockFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(unlockFeedback, "hapticFeedback", feedbackServices.Haptics);
            SetObjectReference(unlockFeedback, "followCamera", followCamera);
            SetFloat(unlockFeedback, "unlockDuration", 0.65f);

            CreateCourierDelivery(
                scene,
                unlock,
                station,
                cashPile,
                wallet,
                playerCollider,
                courierPickupPoint.transform,
                purchaseAvailableMaterial,
                purchaseCompletedMaterial,
                cashAccentMaterial,
                cashVisualPrefab,
                tokenOrigin,
                resourceVisualPrefab,
                courierMaterial,
                courierAccentMaterial,
                crateMaterial,
                particleMaterial,
                feedbackServices,
                followCamera);
            return unlock;
        }

        private static FirstCourierUnlock CreateCourierDelivery(
            Scene scene,
            FirstPackingStationUnlock packingStationUnlock,
            PackingStation packingStation,
            CashPile cashPile,
            Wallet wallet,
            Collider playerCollider,
            Transform pickupPoint,
            Material purchaseAvailableMaterial,
            Material purchaseCompletedMaterial,
            Material cashAccentMaterial,
            GameObject cashVisualPrefab,
            Transform tokenOrigin,
            GameObject resourceVisualPrefab,
            Material courierMaterial,
            Material courierAccentMaterial,
            Material crateMaterial,
            Material particleMaterial,
            FeedbackServices feedbackServices,
            SmoothFollowCamera followCamera)
        {
            PurchasePad purchasePad = CreatePurchasePad(
                scene,
                "Courier Purchase Pad",
                "DELIVERY COURIER",
                new Vector3(0f, 0f, -8f),
                1500,
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

            GameObject deliveryRoot = new GameObject("Crate Courier Delivery");
            SceneManager.MoveGameObjectToScene(deliveryRoot, scene);

            GameObject deliveryPoint = new GameObject("Delivery Point");
            deliveryPoint.transform.SetParent(deliveryRoot.transform, false);
            deliveryPoint.transform.position = new Vector3(1.15f, 0f, -4.5f);
            CreateZoneDisc(
                "Cash Delivery Marker",
                deliveryPoint.transform,
                purchaseCompletedMaterial,
                0.82f);
            GameObject deliveryArrow = CreatePrimitiveChild(
                "Cash Delivery Arrow",
                PrimitiveType.Cube,
                deliveryPoint.transform,
                cashAccentMaterial);
            deliveryArrow.transform.localPosition = new Vector3(-0.48f, 0.16f, 0f);
            deliveryArrow.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            deliveryArrow.transform.localScale = new Vector3(0.62f, 0.09f, 0.22f);
            CreateWorldLabel(
                "Delivery Point Label",
                deliveryPoint.transform,
                "DELIVERY  TO CASH",
                new Vector3(0f, 1.12f, 0.10f),
                new Color(0.52f, 1f, 0.58f));

            GameObject courierObject = new GameObject("Crate Courier");
            courierObject.transform.SetParent(deliveryRoot.transform, false);
            courierObject.transform.position = deliveryPoint.transform.position;

            GameObject courierVisual = new GameObject("Courier Visual");
            courierVisual.transform.SetParent(courierObject.transform, false);

            GameObject chassis = CreatePrimitiveChild(
                "Courier Chassis",
                PrimitiveType.Cube,
                courierVisual.transform,
                courierMaterial);
            chassis.transform.localPosition = new Vector3(0f, 0.58f, 0f);
            chassis.transform.localScale = new Vector3(1.34f, 0.42f, 1.88f);

            GameObject cabin = CreatePrimitiveChild(
                "Courier Cabin",
                PrimitiveType.Cube,
                courierVisual.transform,
                courierMaterial);
            cabin.transform.localPosition = new Vector3(0f, 0.96f, -0.36f);
            cabin.transform.localScale = new Vector3(1.10f, 0.58f, 0.88f);

            GameObject windscreen = CreatePrimitiveChild(
                "Courier Windscreen",
                PrimitiveType.Cube,
                courierVisual.transform,
                courierAccentMaterial);
            windscreen.transform.localPosition = new Vector3(0f, 1.02f, -0.82f);
            windscreen.transform.localScale = new Vector3(0.82f, 0.30f, 0.08f);

            GameObject cargoDeck = CreatePrimitiveChild(
                "Courier Cargo Deck",
                PrimitiveType.Cube,
                courierVisual.transform,
                crateMaterial);
            cargoDeck.transform.localPosition = new Vector3(0f, 0.91f, 0.56f);
            cargoDeck.transform.localScale = new Vector3(1.16f, 0.14f, 0.64f);

            GameObject frontLeftWheel = CreateCourierWheel(
                "Front Left Wheel",
                courierVisual.transform,
                new Vector3(-0.72f, 0.34f, -0.58f),
                courierAccentMaterial);
            GameObject frontRightWheel = CreateCourierWheel(
                "Front Right Wheel",
                courierVisual.transform,
                new Vector3(0.72f, 0.34f, -0.58f),
                courierAccentMaterial);
            CreateCourierWheel(
                "Rear Left Wheel",
                courierVisual.transform,
                new Vector3(-0.72f, 0.34f, 0.62f),
                courierAccentMaterial);
            CreateCourierWheel(
                "Rear Right Wheel",
                courierVisual.transform,
                new Vector3(0.72f, 0.34f, 0.62f),
                courierAccentMaterial);

            GameObject carriedCrateAnchor = new GameObject("Carried Crate Anchor");
            carriedCrateAnchor.transform.SetParent(courierVisual.transform, false);
            carriedCrateAnchor.transform.localPosition = new Vector3(0f, 1.24f, 0.52f);

            GameObject statusIndicator = CreatePrimitiveChild(
                "Courier Status Indicator",
                PrimitiveType.Cube,
                courierVisual.transform,
                courierMaterial);
            statusIndicator.transform.localPosition = new Vector3(0f, 1.46f, -0.16f);
            statusIndicator.transform.localScale = new Vector3(0.62f, 0.12f, 0.18f);

            TextMesh statusText = CreateWorldLabel(
                "Courier Status",
                courierVisual.transform,
                "COURIER  LOCKED",
                new Vector3(0f, 2.02f, 0f),
                Color.white);

            ParticleSystem pickupParticles = CreateFeedbackParticleSystem(
                "Courier Pickup Burst",
                courierObject.transform,
                new Vector3(0f, 1.05f, 0.42f),
                new Color(1f, 0.70f, 0.22f),
                particleMaterial,
                20,
                0.28f,
                1.10f,
                0.09f);
            ParticleSystem deliveryParticles = CreateFeedbackParticleSystem(
                "Courier Delivery Burst",
                courierObject.transform,
                new Vector3(0f, 1.05f, -0.25f),
                new Color(0.20f, 1f, 0.46f),
                particleMaterial,
                24,
                0.32f,
                1.20f,
                0.10f);

            CrateCourier courier = courierObject.AddComponent<CrateCourier>();
            SetObjectReference(courier, "packingStation", packingStation);
            SetObjectReference(courier, "cashPile", cashPile);
            SetObjectReference(courier, "pickupPoint", pickupPoint);
            SetObjectReference(courier, "deliveryPoint", deliveryPoint.transform);
            SetFloat(courier, "movementSpeed", 3.5f);
            SetFloat(courier, "rotationSpeed", 540f);
            SetFloat(courier, "stopDistance", 0.08f);
            SetFloat(courier, "pickupDelay", 0.60f);
            SetFloat(courier, "deliveryDelay", 0.45f);
            SetFloat(courier, "retryInterval", 0.75f);

            CrateCourierFeedback courierFeedback =
                courierObject.AddComponent<CrateCourierFeedback>();
            SetObjectReference(courierFeedback, "courier", courier);
            SetObjectReference(courierFeedback, "courierVisual", courierVisual.transform);
            SetObjectReference(
                courierFeedback,
                "carriedCrateAnchor",
                carriedCrateAnchor.transform);
            SetObjectReference(
                courierFeedback,
                "resourceVisualPrefab",
                resourceVisualPrefab);
            SetObjectReference(courierFeedback, "statusText", statusText);
            SetObjectReference(
                courierFeedback,
                "statusIndicator",
                statusIndicator.GetComponent<Renderer>());
            SetObjectReference(courierFeedback, "idleMaterial", courierMaterial);
            SetObjectReference(
                courierFeedback,
                "movingMaterial",
                courierAccentMaterial);
            SetObjectReference(
                courierFeedback,
                "deliveryMaterial",
                purchaseCompletedMaterial);
            SetObjectReference(
                courierFeedback,
                "leftWheel",
                frontLeftWheel.transform);
            SetObjectReference(
                courierFeedback,
                "rightWheel",
                frontRightWheel.transform);
            SetObjectReference(courierFeedback, "pickupParticles", pickupParticles);
            SetObjectReference(courierFeedback, "deliveryParticles", deliveryParticles);
            SetInteger(courierFeedback, "cargoVisualPoolSize", 2);
            SetFloat(courierFeedback, "cargoVisualScale", 0.58f);
            SetFloat(courierFeedback, "wheelRotationSpeed", 420f);

            deliveryRoot.SetActive(false);
            purchasePad.gameObject.SetActive(false);

            GameObject automationRoot = new GameObject("Courier Automation");
            SceneManager.MoveGameObjectToScene(automationRoot, scene);

            FirstCourierUnlock unlock = automationRoot.AddComponent<FirstCourierUnlock>();
            SetObjectReference(unlock, "packingStationUnlock", packingStationUnlock);
            SetObjectReference(unlock, "courierPurchasePad", purchasePad);
            SetObjectReference(
                unlock,
                "courierPurchasePadRoot",
                purchasePad.gameObject);
            SetObjectReference(unlock, "courierRoot", deliveryRoot);

            ParticleSystem unlockParticles = CreateFeedbackParticleSystem(
                "Courier Unlock Burst",
                automationRoot.transform,
                deliveryPoint.transform.position + new Vector3(0f, 1f, 0f),
                new Color(0.20f, 0.78f, 1f),
                particleMaterial,
                36,
                0.52f,
                1.70f,
                0.13f);

            CourierUnlockFeedback unlockFeedback =
                automationRoot.AddComponent<CourierUnlockFeedback>();
            SetObjectReference(unlockFeedback, "courierUnlock", unlock);
            SetObjectReference(unlockFeedback, "courierVisual", courierVisual.transform);
            SetObjectReference(unlockFeedback, "unlockParticles", unlockParticles);
            SetObjectReference(unlockFeedback, "audioFeedback", feedbackServices.Audio);
            SetObjectReference(unlockFeedback, "hapticFeedback", feedbackServices.Haptics);
            SetObjectReference(unlockFeedback, "followCamera", followCamera);
            SetFloat(unlockFeedback, "unlockDuration", 0.65f);
            return unlock;
        }

        private static GameObject CreateCourierWheel(
            string name,
            Transform parent,
            Vector3 localPosition,
            Material material)
        {
            GameObject wheel = CreatePrimitiveChild(
                name,
                PrimitiveType.Cylinder,
                parent,
                material);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(0.28f, 0.14f, 0.28f);
            return wheel;
        }

        private static void CreateConveyorRail(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float lateralOffset,
            Material material)
        {
            Vector3 direction = end - start;
            Vector3 flatDirection = new Vector3(direction.x, 0f, direction.z).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, flatDirection);
            Vector3 offset = side * lateralOffset + new Vector3(0f, -0.34f, 0f);
            GameObject rail = CreatePrimitiveChild(
                name,
                PrimitiveType.Cube,
                parent,
                material);
            rail.transform.position = ((start + end) * 0.5f) + offset;
            rail.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            rail.transform.localScale = new Vector3(0.12f, 0.18f, direction.magnitude);
        }

        private static GameObject CreateConveyorRoller(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 routeDirection,
            Material material)
        {
            GameObject roller = CreatePrimitiveChild(
                name,
                PrimitiveType.Cylinder,
                parent,
                material);
            Vector3 side = Vector3.Cross(Vector3.up, routeDirection.normalized);
            roller.transform.position = position;
            roller.transform.rotation = Quaternion.FromToRotation(Vector3.up, side);
            roller.transform.localScale = new Vector3(0.34f, 0.52f, 0.34f);
            return roller;
        }

        private static Vector3 EvaluateQuadraticRoute(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float normalizedProgress)
        {
            float progress = Mathf.Clamp01(normalizedProgress);
            Vector3 first = Vector3.Lerp(start, control, progress);
            Vector3 second = Vector3.Lerp(control, end, progress);
            return Vector3.Lerp(first, second, progress);
        }

        private static Vector3 EvaluateQuadraticRouteDirection(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float normalizedProgress)
        {
            float progress = Mathf.Clamp01(normalizedProgress);
            Vector3 direction = (2f * (1f - progress) * (control - start))
                                + (2f * progress * (end - control));
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : (end - start).normalized;
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

        private static M8Services CreateM8Progression(
            Scene scene,
            CarryStack carryStack,
            SalePoint salePoint,
            WoodProductionUpgrade productionUpgrade,
            FirstWorkerUnlock workerUnlock,
            FirstProcessorUnlock processorUnlock,
            FirstAutoFeederUnlock autoFeederUnlock,
            FirstPackingStationUnlock packingStationUnlock,
            FirstCourierUnlock courierUnlock,
            CrateCourier courier,
            Material mineMaterial,
            Material metalMaterial,
            Material completionMaterial,
            Material particleMaterial)
        {
            GameObject mineRoot = new GameObject("Mine Teaser");
            SceneManager.MoveGameObjectToScene(mineRoot, scene);
            mineRoot.transform.position = new Vector3(7.2f, 0f, 13.1f);

            GameObject platform = CreatePrimitiveChild(
                "Mine Preview Platform",
                PrimitiveType.Cube,
                mineRoot.transform,
                mineMaterial);
            platform.transform.localPosition = new Vector3(0f, 0.16f, 0f);
            platform.transform.localScale = new Vector3(4.1f, 0.30f, 2.35f);

            GameObject barrierLeft = CreatePrimitiveChild(
                "Locked Barrier Left",
                PrimitiveType.Cube,
                mineRoot.transform,
                completionMaterial);
            barrierLeft.transform.localPosition = new Vector3(-1.35f, 0.72f, -0.62f);
            barrierLeft.transform.localScale = new Vector3(0.18f, 1.12f, 0.18f);

            GameObject barrierRight = CreatePrimitiveChild(
                "Locked Barrier Right",
                PrimitiveType.Cube,
                mineRoot.transform,
                completionMaterial);
            barrierRight.transform.localPosition = new Vector3(1.35f, 0.72f, -0.62f);
            barrierRight.transform.localScale = new Vector3(0.18f, 1.12f, 0.18f);

            GameObject barrierBeam = CreatePrimitiveChild(
                "Locked Barrier Beam",
                PrimitiveType.Cube,
                mineRoot.transform,
                completionMaterial);
            barrierBeam.transform.localPosition = new Vector3(0f, 0.88f, -0.62f);
            barrierBeam.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            barrierBeam.transform.localScale = new Vector3(2.85f, 0.22f, 0.18f);

            GameObject pickaxeHandle = CreatePrimitiveChild(
                "Pickaxe Handle",
                PrimitiveType.Cube,
                mineRoot.transform,
                completionMaterial);
            pickaxeHandle.transform.localPosition = new Vector3(0f, 0.92f, 0.28f);
            pickaxeHandle.transform.localRotation = Quaternion.Euler(0f, 0f, -38f);
            pickaxeHandle.transform.localScale = new Vector3(0.14f, 1.45f, 0.14f);

            GameObject pickaxeHead = CreatePrimitiveChild(
                "Pickaxe Head",
                PrimitiveType.Cube,
                mineRoot.transform,
                metalMaterial);
            pickaxeHead.transform.localPosition = new Vector3(-0.52f, 1.58f, 0.28f);
            pickaxeHead.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            pickaxeHead.transform.localScale = new Vector3(1.10f, 0.18f, 0.24f);

            TextMesh mineLabel = CreateWorldLabel(
                "Mine Teaser Label",
                mineRoot.transform,
                "\u26CF  MINE - LOCKED",
                new Vector3(0f, 2.30f, 0.10f),
                new Color(1f, 0.84f, 0.34f));
            mineLabel.characterSize = 0.050f;
            mineRoot.SetActive(false);

            GameObject m8Root = new GameObject("Lumber Camp Progression");
            SceneManager.MoveGameObjectToScene(m8Root, scene);

            ParticleSystem completionParticles = CreateFeedbackParticleSystem(
                "Lumber Camp Complete Burst",
                m8Root.transform,
                new Vector3(1.15f, 1.0f, -4.5f),
                new Color(0.28f, 1f, 0.58f),
                particleMaterial,
                48,
                0.52f,
                1.80f,
                0.14f);

            LumberCampCompletion completion =
                m8Root.AddComponent<LumberCampCompletion>();
            SetObjectReference(completion, "courierUnlock", courierUnlock);
            SetObjectReference(completion, "courier", courier);
            SetObjectReference(completion, "mineTeaserRoot", mineRoot);

            LumberCampPacingProbe pacingProbe =
                m8Root.AddComponent<LumberCampPacingProbe>();
            SetObjectReference(pacingProbe, "carryStack", carryStack);
            SetObjectReference(pacingProbe, "salePoint", salePoint);
            SetObjectReference(pacingProbe, "productionUpgrade", productionUpgrade);
            SetObjectReference(pacingProbe, "workerUnlock", workerUnlock);
            SetObjectReference(pacingProbe, "processorUnlock", processorUnlock);
            SetObjectReference(pacingProbe, "autoFeederUnlock", autoFeederUnlock);
            SetObjectReference(pacingProbe, "packingStationUnlock", packingStationUnlock);
            SetObjectReference(pacingProbe, "courierUnlock", courierUnlock);
            SetObjectReference(pacingProbe, "courier", courier);
            SetObjectReference(pacingProbe, "completion", completion);

            return new M8Services(completion, pacingProbe, completionParticles);
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

        private static void CreateHud(
            Scene scene,
            CarryStack carryStack,
            Wallet wallet,
            WoodProductionUpgrade productionUpgrade,
            FirstWorkerUnlock workerUnlock,
            FirstProcessorUnlock processorUnlock,
            FirstAutoFeederUnlock autoFeederUnlock,
            FirstPackingStationUnlock packingStationUnlock,
            FirstCourierUnlock courierUnlock,
            M8Services m8Services,
            FeedbackServices feedbackServices,
            SmoothFollowCamera followCamera)
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

            GameObject guidancePanel = new GameObject(
                "Next Unlock",
                typeof(RectTransform),
                typeof(Image));
            guidancePanel.transform.SetParent(safeAreaObject.transform, false);
            RectTransform guidancePanelRect =
                guidancePanel.GetComponent<RectTransform>();
            guidancePanelRect.anchorMin = new Vector2(0.5f, 1f);
            guidancePanelRect.anchorMax = new Vector2(0.5f, 1f);
            guidancePanelRect.pivot = new Vector2(0.5f, 1f);
            guidancePanelRect.sizeDelta = new Vector2(820f, 132f);
            guidancePanelRect.anchoredPosition = new Vector2(0f, -238f);
            guidancePanel.GetComponent<Image>().color =
                new Color(0.07f, 0.09f, 0.07f, 0.78f);

            Text guidanceText = CreateHudLine(
                "Next Unlock Text",
                guidancePanel.transform,
                "NEXT: PRODUCTION UPGRADE\n$0 / $120",
                Vector2.zero,
                Vector2.one,
                34);
            NextUnlockGuidance guidance =
                guidancePanel.AddComponent<NextUnlockGuidance>();
            SetObjectReference(guidance, "productionUpgrade", productionUpgrade);
            SetObjectReference(guidance, "workerUnlock", workerUnlock);
            SetObjectReference(guidance, "processorUnlock", processorUnlock);
            SetObjectReference(guidance, "autoFeederUnlock", autoFeederUnlock);
            SetObjectReference(guidance, "packingStationUnlock", packingStationUnlock);
            SetObjectReference(guidance, "courierUnlock", courierUnlock);
            SetObjectReference(guidance, "completion", m8Services.Completion);
            SetObjectReference(guidance, "guidanceText", guidanceText);

            GameObject bannerRoot = new GameObject(
                "Completion Banner",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image));
            bannerRoot.transform.SetParent(safeAreaObject.transform, false);
            RectTransform bannerRect = bannerRoot.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 0.66f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.66f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(880f, 196f);
            bannerRect.anchoredPosition = Vector2.zero;
            bannerRoot.GetComponent<Image>().color =
                new Color(0.05f, 0.28f, 0.16f, 0.94f);

            CreateHudLine(
                "Completion Text",
                bannerRoot.transform,
                "LUMBER CAMP COMPLETE\nMINE AREA REVEALED",
                Vector2.zero,
                Vector2.one,
                44);
            CanvasGroup bannerCanvasGroup = bannerRoot.GetComponent<CanvasGroup>();
            bannerCanvasGroup.blocksRaycasts = false;
            bannerCanvasGroup.interactable = false;

            LumberCampCompletionFeedback completionFeedback =
                safeAreaObject.AddComponent<LumberCampCompletionFeedback>();
            SetObjectReference(completionFeedback, "completion", m8Services.Completion);
            SetObjectReference(completionFeedback, "bannerRoot", bannerRoot);
            SetObjectReference(
                completionFeedback,
                "bannerCanvasGroup",
                bannerCanvasGroup);
            SetObjectReference(completionFeedback, "bannerTransform", bannerRect);
            SetObjectReference(
                completionFeedback,
                "completionParticles",
                m8Services.CompletionParticles);
            SetObjectReference(
                completionFeedback,
                "audioFeedback",
                feedbackServices.Audio);
            SetObjectReference(
                completionFeedback,
                "hapticFeedback",
                feedbackServices.Haptics);
            SetObjectReference(completionFeedback, "followCamera", followCamera);
            SetFloat(completionFeedback, "entranceDuration", 0.24f);
            SetFloat(completionFeedback, "holdDuration", 1.45f);
            SetFloat(completionFeedback, "exitDuration", 0.30f);
            bannerRoot.SetActive(false);
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
            Require(GetIntegerValue(woodSpawner, "maximumActiveCount") == 24,
                "WoodSpawner active cap must prevent excessive loose-Wood clutter.");

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
            Require(salePoint.PlankValue == 15, "Sale Point plank value must be $15.");
            Require(salePoint.CrateValue == 40, "Sale Point crate value must be $40.");
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
            Require(stockpile.StoredWood == 0
                    && stockpile.IncomingReservations == 0
                    && stockpile.OutgoingReservations == 0,
                "Wood Stockpile must begin empty with no incoming or outgoing reservation.");
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
            ResourceVisual resourceVisual = carryVisualPrefab.GetComponent<ResourceVisual>();
            Require(resourceVisual != null
                    && resourceVisual.WoodRoot != null
                    && resourceVisual.PlankRoot != null
                    && resourceVisual.CrateRoot != null,
                "Carried resource visuals must contain reusable Wood, Plank, and Crate variants.");

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
            Require(PlayerSettings.defaultScreenWidth == 1080
                    && PlayerSettings.defaultScreenHeight == 1920,
                "Default standalone resolution must retain the 1080 x 1920 portrait target.");
            GameObject portraitHudObject = FindRoot(scene, "HUD");
            CanvasScaler hudScaler = portraitHudObject != null
                ? portraitHudObject.GetComponent<CanvasScaler>()
                : null;
            SafeAreaFitter safeAreaFitter = portraitHudObject != null
                ? portraitHudObject.GetComponentInChildren<SafeAreaFitter>(true)
                : null;
            Require(hudScaler != null
                    && hudScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                    && hudScaler.referenceResolution == new Vector2(1080f, 1920f)
                    && safeAreaFitter != null,
                "Portrait HUD must retain its 1080 x 1920 scaler and Safe Area fitter.");
            ValidateM4Scene(
                scene,
                carryStack,
                wallet,
                playerCollider,
                workerAutomation,
                audioFeedback,
                hapticFeedback,
                followCamera);
            ValidateM5Scene(
                scene,
                wallet,
                playerCollider,
                audioFeedback,
                hapticFeedback,
                followCamera);
            ValidateM6Scene(
                scene,
                carryStack,
                wallet,
                playerCollider,
                audioFeedback,
                hapticFeedback,
                followCamera);
            ValidateM7Scene(
                scene,
                wallet,
                cashPile,
                playerCollider,
                audioFeedback,
                hapticFeedback,
                followCamera);
            ValidateM8Scene(
                scene,
                carryStack,
                salePoint,
                productionUpgrade,
                workerAutomation,
                audioFeedback,
                hapticFeedback,
                followCamera);
            ValidateCoreLoopLogic();
            ValidateM3Logic();
            ValidateM4Logic();
            ValidateM5Logic();
            ValidateM6Logic();
            ValidateM7Logic();
            ValidateM8Logic();
        }

        private static void ValidateM4Scene(
            Scene scene,
            CarryStack carryStack,
            Wallet wallet,
            Collider playerCollider,
            FirstWorkerUnlock workerUnlock,
            AudioFeedback audioFeedback,
            HapticFeedback hapticFeedback,
            SmoothFollowCamera followCamera)
        {
            GameObject purchaseObject = FindRoot(scene, "Processor Purchase Pad");
            GameObject processorObject = FindRoot(scene, "Wood Processor");
            GameObject automationObject = FindRoot(scene, "Processor Automation");
            Require(purchaseObject != null, "The prototype scene has no Processor Purchase Pad.");
            Require(processorObject != null, "The prototype scene has no Wood Processor.");
            Require(automationObject != null, "The prototype scene has no Processor Automation root.");

            BoxCollider purchaseTrigger = ValidateTriggerZone(
                purchaseObject,
                "Processor Purchase Pad");
            Transform inputTransform = processorObject.transform.Find("Processor Input Zone");
            Transform outputTransform = processorObject.transform.Find("Processor Output Zone");
            Require(inputTransform != null && outputTransform != null,
                "Wood Processor requires separate input and output zones.");
            BoxCollider inputTrigger = ValidateTriggerZone(
                inputTransform.gameObject,
                "Processor Input Zone");
            BoxCollider outputTrigger = ValidateTriggerZone(
                outputTransform.gameObject,
                "Processor Output Zone");
            Require(Vector3.Distance(inputTransform.position, outputTransform.position) >= 2.8f,
                "Processor input and output interaction zones are too close.");
            Require(Vector3.Distance(purchaseObject.transform.position, inputTransform.position) >= 3.0f,
                "Processor purchase and input zones are too close for portrait controls.");

            WoodSpawner woodSpawner = FindRoot(scene, "Wood Spawner")?.GetComponent<WoodSpawner>();
            ResourceCollector resourceCollector = carryStack.GetComponent<ResourceCollector>();
            SphereCollider looseWoodCollider = woodSpawner != null && woodSpawner.WoodPrefab != null
                ? woodSpawner.WoodPrefab.GetComponent<SphereCollider>()
                : null;
            Require(woodSpawner != null && resourceCollector != null && looseWoodCollider != null,
                "Processor output clearance validation requires WoodSpawner and player pickup geometry.");
            Vector2 spawnArea = GetVector2Value(woodSpawner, "spawnArea");
            float spawnLeftEdge = woodSpawner.transform.position.x - (spawnArea.x * 0.5f);
            float spawnRightEdge = woodSpawner.transform.position.x + (spawnArea.x * 0.5f);
            Vector3 outputCenter = outputTransform.TransformPoint(outputTrigger.center);
            float outputRightEdge = outputCenter.x
                                    + (outputTrigger.size.x
                                       * Mathf.Abs(outputTransform.lossyScale.x)
                                       * 0.5f);
            CharacterController playerController = playerCollider as CharacterController;
            Require(playerController != null,
                "Processor output clearance validation requires the player CharacterController.");
            float playerCollisionRadius = playerController.radius
                                          * Mathf.Max(
                                              Mathf.Abs(playerController.transform.lossyScale.x),
                                              Mathf.Abs(playerController.transform.lossyScale.z));
            float looseWoodRadius = looseWoodCollider.radius
                                    * Mathf.Abs(looseWoodCollider.transform.lossyScale.x);
            float requiredLooseWoodClearance = playerCollisionRadius
                                             + GetFloatValue(resourceCollector, "pickupRadius")
                                             + looseWoodRadius
                                             + 0.10f;
            Require(spawnLeftEdge - outputRightEdge >= requiredLooseWoodClearance,
                "Loose Wood spawn bounds overlap the Processor output pickup area.");

            GameObject stockpileObject = FindRoot(scene, "Wood Stockpile");
            BoxCollider stockpileTrigger = stockpileObject != null
                ? stockpileObject.GetComponent<BoxCollider>()
                : null;
            Require(stockpileTrigger != null,
                "Loose Wood spawn clearance validation requires the Wood Stockpile trigger.");
            Vector3 stockpileCenter = stockpileObject.transform.TransformPoint(
                stockpileTrigger.center);
            float stockpileLeftEdge = stockpileCenter.x
                                      - (stockpileTrigger.size.x
                                         * Mathf.Abs(stockpileObject.transform.lossyScale.x)
                                         * 0.5f);
            Require(stockpileLeftEdge - spawnRightEdge >= looseWoodRadius + 0.05f,
                "Loose Wood spawn bounds overlap the Wood Stockpile presentation.");

            PurchasePad purchasePad = purchaseObject.GetComponent<PurchasePad>();
            PurchasePadFeedback purchaseFeedback = purchaseObject.GetComponent<PurchasePadFeedback>();
            WoodProcessor processor = processorObject.GetComponent<WoodProcessor>();
            ProcessorInputZone inputZone = inputTransform.GetComponent<ProcessorInputZone>();
            ProcessorOutputZone outputZone = outputTransform.GetComponent<ProcessorOutputZone>();
            WoodProcessorFeedback processorFeedback =
                processorObject.GetComponent<WoodProcessorFeedback>();
            FirstProcessorUnlock processorUnlock =
                automationObject.GetComponent<FirstProcessorUnlock>();
            ProcessorUnlockFeedback unlockFeedback =
                automationObject.GetComponent<ProcessorUnlockFeedback>();

            Require(purchasePad != null
                    && purchasePad.Wallet == wallet
                    && purchasePad.PlayerCollider == playerCollider
                    && purchasePad.InteractionCollider == purchaseTrigger,
                "Processor Purchase Pad gameplay references are incomplete.");
            Require(purchasePad.PurchaseLabel == "WOOD PROCESSOR"
                    && purchasePad.TotalCost == 360
                    && purchasePad.SpendPerTick == 5
                    && Mathf.Approximately(purchasePad.SpendInterval, 0.10f),
                "Processor Purchase Pad must cost $360 and spend $5 / 0.10 seconds.");
            Require(!purchasePad.StartsAvailable
                    && !purchasePad.IsAvailable
                    && !purchaseTrigger.enabled
                    && !purchaseObject.activeSelf,
                "Processor Purchase Pad must begin locked, disabled, and hidden.");
            Require(purchaseFeedback != null
                    && purchaseFeedback.TokenPoolSize == 4
                    && Mathf.Approximately(purchaseFeedback.TokenFlightDuration, 0.22f)
                    && GetObjectReference(purchaseFeedback, "tokenVisualPrefab") != null
                    && GetObjectReference(purchaseFeedback, "purchaseParticles") != null
                    && GetObjectReference(purchaseFeedback, "audioFeedback") == audioFeedback,
                "Processor Purchase Pad must reuse the capped M2 purchase feedback.");

            Require(processor != null && !processorObject.activeSelf,
                "Wood Processor must begin inactive.");
            Require(processor.InputCapacity == 24
                    && processor.OutputCapacity == 12
                    && processor.RecipeInputWood == 2
                    && processor.RecipeOutputPlanks == 1
                    && Mathf.Approximately(processor.ProcessingDuration, 1.10f),
                "Wood Processor must use 24 Wood input, 12 Plank output, 2:1 recipe, and 1.10 seconds.");
            Require(processor.InputWood == 0
                    && processor.ReservedInputCapacity == 0
                    && processor.AvailableInputCapacity == processor.InputCapacity
                    && processor.OutputPlanks == 0
                    && processor.ReservedOutputCapacity == 0,
                "Wood Processor buffers must begin empty and unreserved.");
            Require(inputZone != null
                    && inputZone.Processor == processor
                    && inputZone.CarryStack == carryStack
                    && inputZone.PlayerCollider == playerCollider
                    && Mathf.Approximately(inputZone.TransferInterval, 0.10f)
                    && inputZone.GetComponent<Collider>() == inputTrigger,
                "Processor input-zone references or cadence are incorrect.");
            Require(outputZone != null
                    && outputZone.Processor == processor
                    && outputZone.CarryStack == carryStack
                    && outputZone.PlayerCollider == playerCollider
                    && Mathf.Approximately(outputZone.TransferInterval, 0.10f)
                    && outputZone.GetComponent<Collider>() == outputTrigger,
                "Processor output-zone references or cadence are incorrect.");

            Require(processorFeedback != null
                    && processorFeedback.MaximumOutputVisuals == 6
                    && processorFeedback.PlanksPerVisual == 2
                    && Mathf.Approximately(processorFeedback.BladeRotationSpeed, 280f)
                    && Mathf.Approximately(processorFeedback.OutputPopDuration, 0.16f),
                "Processor presentation pool or working feedback tuning is incorrect.");
            Require(GetObjectReference(processorFeedback, "processor") == processor
                    && GetObjectReference(processorFeedback, "workingBlade") != null
                    && GetObjectReference(processorFeedback, "outputVisualRoot") != null
                    && GetObjectReference(processorFeedback, "resourceVisualPrefab") != null
                    && GetObjectReference(processorFeedback, "inputText") != null
                    && GetObjectReference(processorFeedback, "outputText") != null
                    && GetObjectReference(processorFeedback, "statusText") != null
                    && GetObjectReference(processorFeedback, "completionParticles") != null,
                "Processor presentation references are incomplete.");

            Require(processorUnlock != null
                    && processorUnlock.WorkerUnlock == workerUnlock
                    && processorUnlock.ProcessorPurchasePad == purchasePad
                    && processorUnlock.ProcessorPurchasePadRoot == purchaseObject
                    && processorUnlock.ProcessorRoot == processorObject,
                "Processor Automation unlock-gate references are incomplete.");
            Require(!processorUnlock.IsPadUnlocked && !processorUnlock.IsProcessorActivated,
                "Processor Automation must begin fully locked.");
            Require(unlockFeedback != null
                    && Mathf.Approximately(unlockFeedback.UnlockDuration, 0.65f)
                    && GetObjectReference(unlockFeedback, "processorUnlock") == processorUnlock
                    && GetObjectReference(unlockFeedback, "processorVisual") != null
                    && GetObjectReference(unlockFeedback, "unlockParticles") != null
                    && GetObjectReference(unlockFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(unlockFeedback, "hapticFeedback") == hapticFeedback
                    && GetObjectReference(unlockFeedback, "followCamera") == followCamera,
                "Processor unlock feedback must reuse M2 presentation services.");
        }

        private static void ValidateM5Scene(
            Scene scene,
            Wallet wallet,
            Collider playerCollider,
            AudioFeedback audioFeedback,
            HapticFeedback hapticFeedback,
            SmoothFollowCamera followCamera)
        {
            GameObject purchaseObject = FindRoot(scene, "Auto Feeder Purchase Pad");
            GameObject feederObject = FindRoot(scene, "Wood Auto Feeder");
            GameObject automationObject = FindRoot(scene, "Auto Feeder Automation");
            GameObject processorAutomationObject = FindRoot(scene, "Processor Automation");
            GameObject processorObject = FindRoot(scene, "Wood Processor");
            GameObject stockpileObject = FindRoot(scene, "Wood Stockpile");
            Require(purchaseObject != null,
                "The prototype scene has no Auto Feeder Purchase Pad.");
            Require(feederObject != null,
                "The prototype scene has no fixed Wood Auto Feeder.");
            Require(automationObject != null,
                "The prototype scene has no Auto Feeder Automation root.");
            Require(processorAutomationObject != null
                    && processorObject != null
                    && stockpileObject != null,
                "M5 requires the accepted Processor and Wood Stockpile roots.");

            BoxCollider purchaseTrigger = ValidateTriggerZone(
                purchaseObject,
                "Auto Feeder Purchase Pad");
            PurchasePad purchasePad = purchaseObject.GetComponent<PurchasePad>();
            PurchasePadFeedback purchaseFeedback =
                purchaseObject.GetComponent<PurchasePadFeedback>();
            WoodAutoFeeder feeder = feederObject.GetComponent<WoodAutoFeeder>();
            WoodAutoFeederFeedback feederFeedback =
                feederObject.GetComponent<WoodAutoFeederFeedback>();
            FirstAutoFeederUnlock unlock =
                automationObject.GetComponent<FirstAutoFeederUnlock>();
            AutoFeederUnlockFeedback unlockFeedback =
                automationObject.GetComponent<AutoFeederUnlockFeedback>();
            FirstProcessorUnlock processorUnlock =
                processorAutomationObject.GetComponent<FirstProcessorUnlock>();
            WoodProcessor processor = processorObject.GetComponent<WoodProcessor>();
            WoodStockpile stockpile = stockpileObject.GetComponent<WoodStockpile>();

            Require(purchasePad != null
                    && purchasePad.Wallet == wallet
                    && purchasePad.PlayerCollider == playerCollider
                    && purchasePad.InteractionCollider == purchaseTrigger,
                "Auto Feeder Purchase Pad gameplay references are incomplete.");
            Require(purchasePad.PurchaseLabel == "AUTO FEEDER"
                    && purchasePad.TotalCost == 600
                    && purchasePad.SpendPerTick == 5
                    && Mathf.Approximately(purchasePad.SpendInterval, 0.10f),
                "Auto Feeder Purchase Pad must cost $600 and reuse the $5 / 0.10-second cadence.");
            Require(!purchasePad.StartsAvailable
                    && !purchasePad.IsAvailable
                    && !purchaseTrigger.enabled
                    && !purchaseObject.activeSelf,
                "Auto Feeder Purchase Pad must begin locked, disabled, and hidden.");
            Require(purchaseFeedback != null
                    && purchaseFeedback.TokenPoolSize == 4
                    && Mathf.Approximately(purchaseFeedback.TokenFlightDuration, 0.22f)
                    && GetObjectReference(purchaseFeedback, "tokenVisualPrefab") != null
                    && GetObjectReference(purchaseFeedback, "purchaseParticles") != null
                    && GetObjectReference(purchaseFeedback, "audioFeedback") == audioFeedback,
                "Auto Feeder Purchase Pad must reuse the capped M2 purchase feedback.");

            Require(feeder != null
                    && feeder.Stockpile == stockpile
                    && feeder.Processor == processor
                    && feeder.Presentation == feederFeedback,
                "The fixed Auto Feeder route references are incomplete.");
            Require(Mathf.Approximately(feeder.LaunchInterval, 0.75f)
                    && Mathf.Approximately(feeder.TravelDuration, 0.55f)
                    && feeder.ActiveTransferCount == 0
                    && feeder.CompletedTransferCount == 0
                    && feeder.CancelledTransferCount == 0
                    && !feederObject.activeSelf,
                "Auto Feeder must start inactive with a 0.75 / 0.55-second cadence.");
            Require(feederObject.GetComponentsInChildren<Rigidbody>(true).Length == 0
                    && feederObject.GetComponentsInChildren<Collider>(true).Length == 0,
                "Auto Feeder presentation must not use Rigidbody or collider physics.");

            Require(feederFeedback != null
                    && feederFeedback.ConfiguredVisualPoolSize == 2
                    && Mathf.Approximately(feederFeedback.TransferVisualScale, 0.72f)
                    && Mathf.Approximately(feederFeedback.RollerSpeed, 260f),
                "Auto Feeder must use the configured two-item capped visual pool.");
            Transform routeStart = GetObjectReference(feederFeedback, "routeStart") as Transform;
            Transform routeControl = GetObjectReference(feederFeedback, "routeControl") as Transform;
            Transform routeEnd = GetObjectReference(feederFeedback, "routeEnd") as Transform;
            Require(GetObjectReference(feederFeedback, "autoFeeder") == feeder
                    && GetObjectReference(feederFeedback, "transferVisualRoot") != null
                    && GetObjectReference(feederFeedback, "woodVisualPrefab") != null
                    && routeStart != null
                    && routeControl != null
                    && routeEnd != null
                    && GetObjectReference(feederFeedback, "statusText") != null
                    && GetObjectReference(feederFeedback, "statusIndicator") != null
                    && GetObjectReference(feederFeedback, "sourceRoller") != null
                    && GetObjectReference(feederFeedback, "destinationRoller") != null,
                "Auto Feeder path, status, roller, or visual-pool references are incomplete.");
            Require(Vector3.Distance(routeStart.position, routeEnd.position) >= 14f
                    && routeControl.position.z > routeStart.position.z
                    && routeControl.position.z > routeEnd.position.z,
                "Auto Feeder must visibly connect the Stockpile to Processor along its fixed path.");

            Require(unlock != null
                    && unlock.ProcessorUnlock == processorUnlock
                    && unlock.AutoFeederPurchasePad == purchasePad
                    && unlock.AutoFeederPurchasePadRoot == purchaseObject
                    && unlock.AutoFeederRoot == feederObject,
                "Auto Feeder unlock-gate references are incomplete.");
            Require(!unlock.IsPadUnlocked && !unlock.IsAutoFeederActivated,
                "Auto Feeder must begin fully locked behind Processor activation.");
            Require(unlockFeedback != null
                    && Mathf.Approximately(unlockFeedback.UnlockDuration, 0.65f)
                    && GetObjectReference(unlockFeedback, "autoFeederUnlock") == unlock
                    && GetObjectReference(unlockFeedback, "autoFeederVisual") != null
                    && GetObjectReference(unlockFeedback, "unlockParticles") != null
                    && GetObjectReference(unlockFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(unlockFeedback, "hapticFeedback") == hapticFeedback
                    && GetObjectReference(unlockFeedback, "followCamera") == followCamera,
                "Auto Feeder unlock feedback must reuse M2 presentation services.");

            GameObject saleObject = FindRoot(scene, "Sale Point");
            GameObject workerPurchaseObject = FindRoot(scene, "Worker Purchase Pad");
            Require(saleObject != null && workerPurchaseObject != null,
                "M5 portrait separation checks require the existing interaction roots.");
            Require(!purchaseTrigger.bounds.Intersects(
                        saleObject.GetComponent<BoxCollider>().bounds)
                    && !purchaseTrigger.bounds.Intersects(
                        workerPurchaseObject.GetComponent<BoxCollider>().bounds),
                "Auto Feeder purchase interaction overlaps an accepted portrait interaction zone.");
        }

        private static void ValidateM6Scene(
            Scene scene,
            CarryStack carryStack,
            Wallet wallet,
            Collider playerCollider,
            AudioFeedback audioFeedback,
            HapticFeedback hapticFeedback,
            SmoothFollowCamera followCamera)
        {
            GameObject purchaseObject = FindRoot(scene, "Packing Station Purchase Pad");
            GameObject stationObject = FindRoot(scene, "Packing Station");
            GameObject automationObject = FindRoot(scene, "Packing Station Automation");
            GameObject feederAutomationObject = FindRoot(scene, "Auto Feeder Automation");
            Require(purchaseObject != null,
                "The prototype scene has no Packing Station Purchase Pad.");
            Require(stationObject != null,
                "The prototype scene has no Packing Station.");
            Require(automationObject != null,
                "The prototype scene has no Packing Station Automation root.");
            Require(feederAutomationObject != null,
                "M6 requires the accepted Auto Feeder Automation root.");

            BoxCollider purchaseTrigger = ValidateTriggerZone(
                purchaseObject,
                "Packing Station Purchase Pad");
            Transform inputTransform = stationObject.transform.Find("Packing Input Zone");
            Transform outputTransform = stationObject.transform.Find("Packing Output Zone");
            Transform visualTransform = stationObject.transform.Find("Packing Workshop Visual");
            Require(inputTransform != null && outputTransform != null && visualTransform != null,
                "Packing Station requires distinct workshop, input, and output roots.");
            BoxCollider inputTrigger = ValidateTriggerZone(
                inputTransform.gameObject,
                "Packing Input Zone");
            BoxCollider outputTrigger = ValidateTriggerZone(
                outputTransform.gameObject,
                "Packing Output Zone");
            Require(Vector3.Distance(inputTransform.position, outputTransform.position) >= 2.8f,
                "Packing input and output interaction zones are too close.");
            Require(Vector3.Distance(purchaseObject.transform.position, inputTransform.position) >= 3f,
                "Packing purchase and input zones are too close for portrait controls.");
            Require(visualTransform.Find("Workshop Roof") != null
                    && visualTransform.Find("Packing Tape Arm") != null
                    && visualTransform.Find("Packing Table") != null,
                "Packing Station must remain visually distinct as a recognizable workshop.");
            Require(visualTransform.GetComponentsInChildren<Rigidbody>(true).Length == 0
                    && visualTransform.GetComponentsInChildren<Collider>(true).Length == 0,
                "Packing workshop presentation must not use Rigidbody or collider physics.");

            PurchasePad purchasePad = purchaseObject.GetComponent<PurchasePad>();
            PurchasePadFeedback purchaseFeedback =
                purchaseObject.GetComponent<PurchasePadFeedback>();
            PackingStation station = stationObject.GetComponent<PackingStation>();
            PackingStationInputZone inputZone =
                inputTransform.GetComponent<PackingStationInputZone>();
            PackingStationOutputZone outputZone =
                outputTransform.GetComponent<PackingStationOutputZone>();
            PackingStationFeedback stationFeedback =
                stationObject.GetComponent<PackingStationFeedback>();
            FirstPackingStationUnlock unlock =
                automationObject.GetComponent<FirstPackingStationUnlock>();
            PackingStationUnlockFeedback unlockFeedback =
                automationObject.GetComponent<PackingStationUnlockFeedback>();
            FirstAutoFeederUnlock autoFeederUnlock =
                feederAutomationObject.GetComponent<FirstAutoFeederUnlock>();

            Require(purchasePad != null
                    && purchasePad.Wallet == wallet
                    && purchasePad.PlayerCollider == playerCollider
                    && purchasePad.InteractionCollider == purchaseTrigger,
                "Packing Station Purchase Pad gameplay references are incomplete.");
            Require(purchasePad.PurchaseLabel == "PACKING STATION"
                    && purchasePad.TotalCost == 900
                    && purchasePad.SpendPerTick == 5
                    && Mathf.Approximately(purchasePad.SpendInterval, 0.10f),
                "Packing Station Purchase Pad must cost $900 and reuse the $5 / 0.10-second cadence.");
            Require(!purchasePad.StartsAvailable
                    && !purchasePad.IsAvailable
                    && !purchaseTrigger.enabled
                    && !purchaseObject.activeSelf,
                "Packing Station Purchase Pad must begin locked, disabled, and hidden.");
            Require(purchaseFeedback != null
                    && purchaseFeedback.TokenPoolSize == 4
                    && Mathf.Approximately(purchaseFeedback.TokenFlightDuration, 0.22f)
                    && GetObjectReference(purchaseFeedback, "tokenVisualPrefab") != null
                    && GetObjectReference(purchaseFeedback, "purchaseParticles") != null
                    && GetObjectReference(purchaseFeedback, "audioFeedback") == audioFeedback,
                "Packing Station Purchase Pad must reuse the capped M2 purchase feedback.");

            Require(station != null && !stationObject.activeSelf,
                "Packing Station must begin inactive.");
            Require(station.InputCapacity == 24
                    && station.OutputCapacity == 12
                    && station.RecipeInputPlanks == 2
                    && station.RecipeOutputCrates == 1
                    && Mathf.Approximately(station.ProcessingDuration, 1.50f),
                "Packing Station must use 24 Plank input, 12 Crate output, 2:1 recipe, and 1.50 seconds.");
            Require(station.InputPlanks == 0
                    && station.ProcessingInputPlanks == 0
                    && station.AvailableInputCapacity == station.InputCapacity
                    && station.OutputCrates == 0
                    && station.ReservedOutputCapacity == 0
                    && station.AvailableOutputCapacity == station.OutputCapacity,
                "Packing Station buffers must begin empty and unreserved.");
            Require(inputZone != null
                    && inputZone.PackingStation == station
                    && inputZone.CarryStack == carryStack
                    && inputZone.PlayerCollider == playerCollider
                    && Mathf.Approximately(inputZone.TransferInterval, 0.10f)
                    && inputZone.GetComponent<Collider>() == inputTrigger,
                "Packing input-zone references or cadence are incorrect.");
            Require(outputZone != null
                    && outputZone.PackingStation == station
                    && outputZone.CarryStack == carryStack
                    && outputZone.PlayerCollider == playerCollider
                    && Mathf.Approximately(outputZone.TransferInterval, 0.10f)
                    && outputZone.GetComponent<Collider>() == outputTrigger,
                "Packing output-zone references or cadence are incorrect.");

            Require(stationFeedback != null
                    && stationFeedback.MaximumOutputVisuals == 6
                    && stationFeedback.CratesPerVisual == 2
                    && Mathf.Approximately(stationFeedback.WorkingRotationSpeed, 220f)
                    && Mathf.Approximately(stationFeedback.OutputPopDuration, 0.18f),
                "Packing Station presentation pool or working feedback tuning is incorrect.");
            Require(GetObjectReference(stationFeedback, "station") == station
                    && GetObjectReference(stationFeedback, "workingPart") != null
                    && GetObjectReference(stationFeedback, "outputVisualRoot") != null
                    && GetObjectReference(stationFeedback, "resourceVisualPrefab") != null
                    && GetObjectReference(stationFeedback, "inputText") != null
                    && GetObjectReference(stationFeedback, "outputText") != null
                    && GetObjectReference(stationFeedback, "statusText") != null
                    && GetObjectReference(stationFeedback, "statusIndicator") != null
                    && GetObjectReference(stationFeedback, "completionParticles") != null,
                "Packing Station presentation references are incomplete.");

            Require(unlock != null
                    && unlock.AutoFeederUnlock == autoFeederUnlock
                    && unlock.PackingStationPurchasePad == purchasePad
                    && unlock.PackingStationPurchasePadRoot == purchaseObject
                    && unlock.PackingStationRoot == stationObject,
                "Packing Station unlock-gate references are incomplete.");
            Require(!unlock.IsPadUnlocked && !unlock.IsPackingStationActivated,
                "Packing Station must begin fully locked behind Auto Feeder activation.");
            Require(unlockFeedback != null
                    && Mathf.Approximately(unlockFeedback.UnlockDuration, 0.65f)
                    && GetObjectReference(unlockFeedback, "packingStationUnlock") == unlock
                    && GetObjectReference(unlockFeedback, "packingStationVisual") == visualTransform
                    && GetObjectReference(unlockFeedback, "unlockParticles") != null
                    && GetObjectReference(unlockFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(unlockFeedback, "hapticFeedback") == hapticFeedback
                    && GetObjectReference(unlockFeedback, "followCamera") == followCamera,
                "Packing Station unlock feedback must reuse M2 presentation services.");

            GameObject workerPurchaseObject = FindRoot(scene, "Worker Purchase Pad");
            GameObject feederPurchaseObject = FindRoot(scene, "Auto Feeder Purchase Pad");
            Require(workerPurchaseObject != null && feederPurchaseObject != null,
                "M6 portrait separation checks require accepted progression pads.");
            Require(Vector3.Distance(
                        purchaseObject.transform.position,
                        workerPurchaseObject.transform.position) >= 3.5f
                    && Vector3.Distance(
                        purchaseObject.transform.position,
                        feederPurchaseObject.transform.position) >= 6f,
                "Packing Station purchase pad is too close to an accepted interaction zone.");
        }

        private static void ValidateM7Scene(
            Scene scene,
            Wallet wallet,
            CashPile cashPile,
            Collider playerCollider,
            AudioFeedback audioFeedback,
            HapticFeedback hapticFeedback,
            SmoothFollowCamera followCamera)
        {
            GameObject purchaseObject = FindRoot(scene, "Courier Purchase Pad");
            GameObject deliveryRoot = FindRoot(scene, "Crate Courier Delivery");
            GameObject automationObject = FindRoot(scene, "Courier Automation");
            GameObject packingObject = FindRoot(scene, "Packing Station");
            GameObject packingAutomationObject =
                FindRoot(scene, "Packing Station Automation");
            Require(purchaseObject != null,
                "The prototype scene has no Courier Purchase Pad.");
            Require(deliveryRoot != null,
                "The prototype scene has no Crate Courier Delivery root.");
            Require(automationObject != null,
                "The prototype scene has no Courier Automation root.");
            Require(packingObject != null && packingAutomationObject != null,
                "M7 requires the accepted Packing Station roots.");

            BoxCollider purchaseTrigger = ValidateTriggerZone(
                purchaseObject,
                "Courier Purchase Pad");
            Transform courierTransform = deliveryRoot.transform.Find("Crate Courier");
            Transform deliveryPoint = deliveryRoot.transform.Find("Delivery Point");
            Transform pickupPoint = packingObject.transform.Find("Courier Pickup Point");
            Transform packingOutput = packingObject.transform.Find("Packing Output Zone");
            Require(courierTransform != null
                    && deliveryPoint != null
                    && pickupPoint != null
                    && packingOutput != null,
                "Courier requires one agent and fixed Packing pickup / Cash delivery points.");
            Require(deliveryRoot.GetComponentsInChildren<CrateCourier>(true).Length == 1,
                "M7 must contain exactly one Crate Courier.");
            Require(Vector3.Distance(pickupPoint.position, packingOutput.position) >= 1.2f
                    && Vector3.Distance(pickupPoint.position, packingOutput.position) <= 2.2f,
                "Courier pickup must remain visibly adjacent to, but clear of, manual output.");
            Require(Vector3.Distance(deliveryPoint.position, cashPile.transform.position) <= 3f,
                "Courier Delivery Point must remain visibly connected to the Cash Pile.");

            PurchasePad purchasePad = purchaseObject.GetComponent<PurchasePad>();
            PurchasePadFeedback purchaseFeedback =
                purchaseObject.GetComponent<PurchasePadFeedback>();
            CrateCourier courier = courierTransform.GetComponent<CrateCourier>();
            CrateCourierFeedback courierFeedback =
                courierTransform.GetComponent<CrateCourierFeedback>();
            FirstCourierUnlock unlock =
                automationObject.GetComponent<FirstCourierUnlock>();
            CourierUnlockFeedback unlockFeedback =
                automationObject.GetComponent<CourierUnlockFeedback>();
            PackingStation packingStation = packingObject.GetComponent<PackingStation>();
            FirstPackingStationUnlock packingUnlock =
                packingAutomationObject.GetComponent<FirstPackingStationUnlock>();

            Require(purchasePad != null
                    && purchasePad.Wallet == wallet
                    && purchasePad.PlayerCollider == playerCollider
                    && purchasePad.InteractionCollider == purchaseTrigger,
                "Courier Purchase Pad gameplay references are incomplete.");
            Require(purchasePad.PurchaseLabel == "DELIVERY COURIER"
                    && purchasePad.TotalCost == 1500
                    && purchasePad.SpendPerTick == 5
                    && Mathf.Approximately(purchasePad.SpendInterval, 0.10f),
                "Courier Purchase Pad must cost $1500 and reuse the $5 / 0.10-second cadence.");
            Require(!purchasePad.StartsAvailable
                    && !purchasePad.IsAvailable
                    && !purchaseTrigger.enabled
                    && !purchaseObject.activeSelf,
                "Courier Purchase Pad must begin locked, disabled, and hidden.");
            Require(purchaseFeedback != null
                    && purchaseFeedback.TokenPoolSize == 4
                    && Mathf.Approximately(purchaseFeedback.TokenFlightDuration, 0.22f)
                    && GetObjectReference(purchaseFeedback, "tokenVisualPrefab") != null
                    && GetObjectReference(purchaseFeedback, "purchaseParticles") != null
                    && GetObjectReference(purchaseFeedback, "audioFeedback") == audioFeedback,
                "Courier Purchase Pad must reuse the capped purchase feedback.");

            Require(courier != null && !deliveryRoot.activeSelf,
                "The single Crate Courier delivery root must begin inactive.");
            Require(courier.PackingStation == packingStation
                    && courier.CashPile == cashPile
                    && courier.PickupPoint == pickupPoint
                    && courier.DeliveryPoint == deliveryPoint,
                "Courier fixed-route authoritative references are incomplete.");
            Require(courier.AcceptedResourceType == ResourceType.Crate
                    && courier.Capacity == 2
                    && courier.CashPerCrate == 40,
                "Courier must accept only Crates, carry at most two, and pay $40 each.");
            Require(Mathf.Approximately(courier.MovementSpeed, 3.5f)
                    && Mathf.Approximately(courier.RotationSpeed, 540f)
                    && Mathf.Approximately(courier.StopDistance, 0.08f)
                    && Mathf.Approximately(courier.PickupDelay, 0.60f)
                    && Mathf.Approximately(courier.DeliveryDelay, 0.45f)
                    && Mathf.Approximately(courier.RetryInterval, 0.75f),
                "Courier tuning must remain 3.5 speed / 0.60 pickup / 0.45 delivery / 0.75 retry.");
            Require(courier.State == CrateCourierState.Disabled
                    && courier.ReservedCrates == 0
                    && courier.CarriedCrates == 0
                    && courier.CompletedTripCount == 0,
                "Courier must begin disabled with no claim, cargo, or completed delivery.");
            Require(packingStation.MaximumCourierReservedCrates == 2
                    && packingStation.ReservedCourierOutputCrates == 0,
                "Packing output must begin with an empty, capped two-Crate courier claim.");

            Require(courierFeedback != null
                    && courierFeedback.ConfiguredCargoVisualPoolSize == 2
                    && Mathf.Approximately(courierFeedback.CargoVisualScale, 0.58f)
                    && Mathf.Approximately(courierFeedback.WheelRotationSpeed, 420f),
                "Courier presentation must use exactly two capped cargo visuals.");
            Require(GetObjectReference(courierFeedback, "courier") == courier
                    && GetObjectReference(courierFeedback, "courierVisual") != null
                    && GetObjectReference(courierFeedback, "carriedCrateAnchor") != null
                    && GetObjectReference(courierFeedback, "resourceVisualPrefab") != null
                    && GetObjectReference(courierFeedback, "statusText") != null
                    && GetObjectReference(courierFeedback, "statusIndicator") != null
                    && GetObjectReference(courierFeedback, "pickupParticles") != null
                    && GetObjectReference(courierFeedback, "deliveryParticles") != null,
                "Courier presentation references are incomplete.");
            Require(courierTransform.GetComponentsInChildren<Rigidbody>(true).Length == 0
                    && courierTransform.GetComponentsInChildren<Collider>(true).Length == 0,
                "Courier and carried Crate presentation must not use physics components.");

            Require(unlock != null
                    && unlock.PackingStationUnlock == packingUnlock
                    && unlock.CourierPurchasePad == purchasePad
                    && unlock.CourierPurchasePadRoot == purchaseObject
                    && unlock.CourierRoot == deliveryRoot,
                "Courier unlock-gate references are incomplete.");
            Require(!unlock.IsPadUnlocked && !unlock.IsCourierActivated,
                "Courier must begin fully locked behind Packing Station activation.");
            Require(unlockFeedback != null
                    && Mathf.Approximately(unlockFeedback.UnlockDuration, 0.65f)
                    && GetObjectReference(unlockFeedback, "courierUnlock") == unlock
                    && GetObjectReference(unlockFeedback, "courierVisual")
                       == courierTransform.Find("Courier Visual")
                    && GetObjectReference(unlockFeedback, "unlockParticles") != null
                    && GetObjectReference(unlockFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(unlockFeedback, "hapticFeedback") == hapticFeedback
                    && GetObjectReference(unlockFeedback, "followCamera") == followCamera,
                "Courier unlock feedback must reuse accepted presentation services.");

            string[] separatedRoots =
            {
                "Cash Pile",
                "Sale Point",
                "Purchase Pad",
                "Worker Purchase Pad",
                "Processor Purchase Pad",
                "Auto Feeder Purchase Pad",
                "Packing Station Purchase Pad"
            };
            for (int i = 0; i < separatedRoots.Length; i++)
            {
                GameObject otherRoot = FindRoot(scene, separatedRoots[i]);
                Require(otherRoot != null,
                    $"M7 portrait check is missing '{separatedRoots[i]}'.");
                BoxCollider otherTrigger = otherRoot.GetComponent<BoxCollider>();
                Require(otherTrigger != null
                        && Vector3.Distance(
                            purchaseObject.transform.position,
                            otherRoot.transform.position) >= 3.4f
                        && !purchaseTrigger.bounds.Intersects(otherTrigger.bounds),
                    $"Courier Purchase Pad overlaps '{separatedRoots[i]}' in portrait layout.");
            }
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

        private static void ValidateM8Scene(
            Scene scene,
            CarryStack carryStack,
            SalePoint salePoint,
            WoodProductionUpgrade productionUpgrade,
            FirstWorkerUnlock workerUnlock,
            AudioFeedback audioFeedback,
            HapticFeedback hapticFeedback,
            SmoothFollowCamera followCamera)
        {
            GameObject progressionRoot = FindRoot(scene, "Lumber Camp Progression");
            GameObject mineRoot = FindRoot(scene, "Mine Teaser");
            GameObject hudRoot = FindRoot(scene, "HUD");
            GameObject processorAutomation = FindRoot(scene, "Processor Automation");
            GameObject feederAutomation = FindRoot(scene, "Auto Feeder Automation");
            GameObject packingAutomation = FindRoot(scene, "Packing Station Automation");
            GameObject courierAutomation = FindRoot(scene, "Courier Automation");
            GameObject deliveryRoot = FindRoot(scene, "Crate Courier Delivery");
            GameObject processorRoot = FindRoot(scene, "Wood Processor");
            GameObject packingRoot = FindRoot(scene, "Packing Station");
            GameObject packingPurchaseRoot =
                FindRoot(scene, "Packing Station Purchase Pad");
            Require(progressionRoot != null
                    && mineRoot != null
                    && hudRoot != null
                    && processorAutomation != null
                    && feederAutomation != null
                    && packingAutomation != null
                    && courierAutomation != null
                    && deliveryRoot != null
                    && processorRoot != null
                    && packingRoot != null
                    && packingPurchaseRoot != null,
                "M8 progression, HUD, Mine, or accepted unlock roots are missing.");

            FirstProcessorUnlock processorUnlock =
                processorAutomation.GetComponent<FirstProcessorUnlock>();
            FirstAutoFeederUnlock feederUnlock =
                feederAutomation.GetComponent<FirstAutoFeederUnlock>();
            FirstPackingStationUnlock packingUnlock =
                packingAutomation.GetComponent<FirstPackingStationUnlock>();
            FirstCourierUnlock courierUnlock =
                courierAutomation.GetComponent<FirstCourierUnlock>();
            CrateCourier courier =
                deliveryRoot.GetComponentInChildren<CrateCourier>(true);
            LumberCampCompletion completion =
                progressionRoot.GetComponent<LumberCampCompletion>();
            LumberCampPacingProbe pacingProbe =
                progressionRoot.GetComponent<LumberCampPacingProbe>();
            NextUnlockGuidance guidance =
                hudRoot.GetComponentInChildren<NextUnlockGuidance>(true);
            LumberCampCompletionFeedback completionFeedback =
                hudRoot.GetComponentInChildren<LumberCampCompletionFeedback>(true);
            Require(processorUnlock != null
                    && feederUnlock != null
                    && packingUnlock != null
                    && courierUnlock != null
                    && courier != null
                    && completion != null
                    && pacingProbe != null
                    && guidance != null
                    && completionFeedback != null,
                "M8 runtime components are missing from the generated scene.");

            Require(completion.CourierUnlock == courierUnlock
                    && completion.Courier == courier
                    && completion.MineTeaserRoot == mineRoot
                    && !completion.IsCompleted
                    && completion.CompletionCount == 0,
                "Lumber Camp completion must start incomplete and reference authoritative Courier state.");
            Require(!mineRoot.activeSelf
                    && mineRoot.GetComponentsInChildren<Collider>(true).Length == 0
                    && mineRoot.GetComponentsInChildren<Rigidbody>(true).Length == 0,
                "Mine teaser must begin hidden and remain presentation-only.");
            TextMesh mineLabel = mineRoot.GetComponentInChildren<TextMesh>(true);
            Require(mineLabel != null
                    && mineLabel.text.Contains("MINE")
                    && mineLabel.text.Contains("LOCKED"),
                "Mine teaser requires a clear locked label.");

            Transform processorOutput =
                processorRoot.transform.Find("Processor Output Zone");
            Transform packingInput = packingRoot.transform.Find("Packing Input Zone");
            Transform packingOutput = packingRoot.transform.Find("Packing Output Zone");
            Require(processorOutput != null
                    && packingInput != null
                    && packingOutput != null,
                "M8 layout audit requires Processor and Packing interaction zones.");

            WoodSpawner woodSpawner = FindRoot(scene, "Wood Spawner")
                ?.GetComponent<WoodSpawner>();
            ResourceCollector resourceCollector =
                carryStack.GetComponent<ResourceCollector>();
            CharacterController playerController =
                carryStack.GetComponent<CharacterController>();
            SphereCollider looseWoodCollider =
                woodSpawner != null && woodSpawner.WoodPrefab != null
                    ? woodSpawner.WoodPrefab.GetComponent<SphereCollider>()
                    : null;
            BoxCollider packingOutputTrigger =
                packingOutput.GetComponent<BoxCollider>();
            Require(woodSpawner != null
                    && resourceCollector != null
                    && playerController != null
                    && looseWoodCollider != null
                    && packingOutputTrigger != null,
                "M8 Packing output clearance requires Wood and player pickup geometry.");
            Vector2 spawnArea = GetVector2Value(woodSpawner, "spawnArea");
            float spawnLeftEdge = woodSpawner.transform.position.x
                                  - (spawnArea.x * 0.5f);
            Vector3 packingOutputCenter = packingOutput.TransformPoint(
                packingOutputTrigger.center);
            float packingOutputRightEdge = packingOutputCenter.x
                                           + (packingOutputTrigger.size.x
                                              * Mathf.Abs(
                                                  packingOutput.lossyScale.x)
                                              * 0.5f);
            float playerCollisionRadius = playerController.radius
                                          * Mathf.Max(
                                              Mathf.Abs(
                                                  playerController.transform.lossyScale.x),
                                              Mathf.Abs(
                                                  playerController.transform.lossyScale.z));
            float looseWoodRadius = looseWoodCollider.radius
                                    * Mathf.Abs(
                                        looseWoodCollider.transform.lossyScale.x);
            float requiredLooseWoodClearance = playerCollisionRadius
                                             + GetFloatValue(
                                                 resourceCollector,
                                                 "pickupRadius")
                                             + looseWoodRadius
                                             + 0.10f;
            Require(spawnLeftEdge - packingOutputRightEdge
                    >= requiredLooseWoodClearance,
                "Loose Wood spawn bounds overlap the Packing output pickup area.");

            float plankTransportDistance = Vector3.Distance(
                processorOutput.position,
                packingInput.position);
            float crateTransportDistance = Vector3.Distance(
                packingOutput.position,
                salePoint.transform.position);
            float packingPadDistance = Vector3.Distance(
                packingPurchaseRoot.transform.position,
                packingRoot.transform.position);
            Require(plankTransportDistance >= 5.5f
                    && plankTransportDistance <= 7f,
                $"Processor to Packing flow must remain readable without excessive repetition; found {plankTransportDistance:0.00} units.");
            Require(crateTransportDistance >= 12f
                    && crateTransportDistance <= 15.5f,
                $"Packing to Sale travel must remain meaningful but short; found {crateTransportDistance:0.00} units.");
            Require(packingPadDistance >= 2.5f
                    && packingPadDistance <= 3.2f
                    && Vector3.Distance(
                        packingPurchaseRoot.transform.position,
                        packingInput.position) >= 4.5f,
                "Packing purchase pad must visibly belong to the station without conflicting with its input.");
            Require(Vector3.Distance(
                        mineRoot.transform.position,
                        packingRoot.transform.position) >= 8f,
                "Mine teaser must not visually crowd the completed Packing area.");

            Require(pacingProbe.CarryStack == carryStack
                    && pacingProbe.SalePoint == salePoint
                    && pacingProbe.ProductionUpgrade == productionUpgrade
                    && pacingProbe.WorkerUnlock == workerUnlock
                    && pacingProbe.ProcessorUnlock == processorUnlock
                    && pacingProbe.AutoFeederUnlock == feederUnlock
                    && pacingProbe.PackingStationUnlock == packingUnlock
                    && pacingProbe.CourierUnlock == courierUnlock
                    && pacingProbe.Courier == courier
                    && pacingProbe.Completion == completion,
                "Development pacing probe is not wired exclusively to authoritative events.");

            Require(guidance.ProductionUpgrade == productionUpgrade
                    && guidance.WorkerUnlock == workerUnlock
                    && guidance.ProcessorUnlock == processorUnlock
                    && guidance.AutoFeederUnlock == feederUnlock
                    && guidance.PackingStationUnlock == packingUnlock
                    && guidance.CourierUnlock == courierUnlock
                    && guidance.Completion == completion
                    && guidance.GuidanceText != null,
                "Next Unlock guidance references are incomplete.");
            Require(guidance.GuidanceText.text
                    == "NEXT: PRODUCTION UPGRADE\n$0 / $120",
                "Next Unlock initial copy must show authoritative paid / total progress.");
            RectTransform guidanceRect = guidance.GetComponent<RectTransform>();
            Require(guidanceRect != null
                    && guidanceRect.sizeDelta.x <= 880f
                    && guidanceRect.sizeDelta.y <= 150f,
                "Next Unlock HUD must remain compact in portrait safe area.");

            Require(completionFeedback.Completion == completion
                    && completionFeedback.BannerRoot != null
                    && !completionFeedback.BannerRoot.activeSelf
                    && Mathf.Approximately(completionFeedback.EntranceDuration, 0.24f)
                    && Mathf.Approximately(completionFeedback.HoldDuration, 1.45f)
                    && Mathf.Approximately(completionFeedback.ExitDuration, 0.30f)
                    && GetObjectReference(completionFeedback, "completionParticles") != null
                    && GetObjectReference(completionFeedback, "audioFeedback") == audioFeedback
                    && GetObjectReference(completionFeedback, "hapticFeedback") == hapticFeedback
                    && GetObjectReference(completionFeedback, "followCamera") == followCamera,
                "Completion presentation must be short, non-blocking, and reuse M2 feedback services.");
            Text completionText =
                completionFeedback.BannerRoot.GetComponentInChildren<Text>(true);
            Require(completionText != null
                    && completionText.text.Contains("LUMBER CAMP COMPLETE")
                    && completionText.text.Contains("MINE AREA REVEALED"),
                "Completion presentation must make the off-camera Mine reveal discoverable.");
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

            Require(particleSystems.Count == 22,
                $"The prototype requires twenty-two reusable feedback emitters; found {particleSystems.Count}.");
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

        private static void ValidateM4Logic()
        {
            GameObject validationRoot = new GameObject("M4 Logic Validation");
            try
            {
                GameObject carryObject = new GameObject("M4 CarryStack");
                carryObject.transform.SetParent(validationRoot.transform, false);
                CarryStack carryStack = carryObject.AddComponent<CarryStack>();

                Require(!carryStack.HasActiveResource
                        && carryStack.ActiveResourceType == null
                        && carryStack.ReservedResourceType == null,
                    "An empty CarryStack retained an active or reserved resource type.");
                Require(carryStack.TryReserveCapacity(ResourceType.Wood, 1)
                        && carryStack.ReservedResourceType == ResourceType.Wood
                        && !carryStack.TryReserveCapacity(ResourceType.Plank, 1)
                        && !carryStack.TryAdd(ResourceType.Plank, 1)
                        && carryStack.TryCommitReservedAdd(ResourceType.Wood, 1),
                    "A pending Wood reservation did not prevent mixed Plank ownership.");
                Require(carryStack.ActiveResourceType == ResourceType.Wood
                        && !carryStack.TryAdd(ResourceType.Plank, 1)
                        && carryStack.TryRemove(ResourceType.Wood, 1)
                        && !carryStack.HasActiveResource,
                    "CarryStack did not enforce or clear its Wood-only active type.");
                Require(carryStack.TryReserveCapacity(ResourceType.Plank, 1)
                        && carryStack.ReservedResourceType == ResourceType.Plank
                        && !carryStack.TryReserveCapacity(ResourceType.Wood, 1)
                        && !carryStack.TryAdd(ResourceType.Wood, 1)
                        && carryStack.TryCommitReservedAdd(ResourceType.Plank, 1)
                        && carryStack.ActiveResourceType == ResourceType.Plank
                        && !carryStack.TryAdd(ResourceType.Wood, 1)
                        && carryStack.TryRemove(ResourceType.Plank, 1)
                        && carryStack.TotalAmount == 0,
                    "CarryStack did not enforce or clear its Plank-only active type.");

                GameObject processorObject = new GameObject("M4 WoodProcessor");
                processorObject.transform.SetParent(validationRoot.transform, false);
                WoodProcessor processor = processorObject.AddComponent<WoodProcessor>();
                int bufferEventCount = 0;
                processor.BufferChanged += (inputWood, outputPlanks, reservedOutput) =>
                {
                    bufferEventCount++;
                    Require(inputWood == processor.InputWood
                            && outputPlanks == processor.OutputPlanks
                            && reservedOutput == processor.ReservedOutputCapacity,
                        "Processor buffer feedback preceded authoritative state.");
                };

                Require(carryStack.TryAdd(ResourceType.Wood, 1)
                        && processor.TryTransferInputFrom(carryStack)
                        && processor.InputWood == 1
                        && processor.OutputPlanks == 0
                        && !processor.TryStartProcessing(),
                    "One incomplete Wood recipe mutated output or started in Edit Mode.");
                for (int i = 1; i < processor.InputCapacity; i++)
                {
                    Require(carryStack.TryAdd(ResourceType.Wood, 1)
                            && processor.TryTransferInputFrom(carryStack),
                        "Processor rejected an in-capacity Wood input transfer.");
                }

                Require(processor.InputWood == processor.InputCapacity
                        && processor.OutputPlanks == 0
                        && processor.ReservedOutputCapacity == 0
                        && bufferEventCount == processor.InputCapacity,
                    "Processor input buffer exceeded capacity or duplicated deposited Wood.");
                Require(carryStack.TryAdd(ResourceType.Wood, 1)
                        && !processor.TryTransferInputFrom(carryStack)
                        && processor.InputWood == processor.InputCapacity
                        && carryStack.GetAmount(ResourceType.Wood) == 1,
                    "A full processor input consumed or duplicated carried Wood.");
                Require(carryStack.TryRemove(ResourceType.Wood, 1)
                        && carryStack.TryAdd(ResourceType.Plank, 1)
                        && !processor.TryTransferInputFrom(carryStack)
                        && processor.InputWood == processor.InputCapacity
                        && carryStack.TryRemove(ResourceType.Plank, 1),
                    "Processor input accepted Planks or corrupted its Wood buffer.");

                GameObject cashObject = new GameObject("M4 CashPile");
                cashObject.transform.SetParent(validationRoot.transform, false);
                CashPile cashPile = cashObject.AddComponent<CashPile>();
                GameObject saleObject = new GameObject("M4 SalePoint");
                saleObject.transform.SetParent(validationRoot.transform, false);
                saleObject.AddComponent<BoxCollider>().isTrigger = true;
                SalePoint salePoint = saleObject.AddComponent<SalePoint>();
                SetObjectReference(salePoint, "carryStack", carryStack);
                SetObjectReference(salePoint, "cashPile", cashPile);
                SetInteger(salePoint, "woodValue", 5);
                SetInteger(salePoint, "plankValue", 15);
                int saleEventCount = 0;
                salePoint.UnitSold += feedback =>
                {
                    saleEventCount++;
                    Require(feedback.RemainingAmount == carryStack.TotalAmount,
                        "Typed sale feedback preceded authoritative CarryStack removal.");
                };

                Require(carryStack.TryAdd(ResourceType.Wood, 1)
                        && salePoint.TryUnloadOne()
                        && cashPile.StoredCash == 5,
                    "Generic Sale Point did not sell one Wood for $5.");
                Require(carryStack.TryAdd(ResourceType.Plank, 1)
                        && salePoint.TryUnloadOne()
                        && cashPile.StoredCash == 20
                        && saleEventCount == 2,
                    "Generic Sale Point did not sell one Plank for $15.");

                GameObject walletObject = new GameObject("M4 Wallet");
                walletObject.transform.SetParent(validationRoot.transform, false);
                Wallet wallet = walletObject.AddComponent<Wallet>();
                GameObject padObject = new GameObject("M4 Processor Purchase Pad");
                padObject.transform.SetParent(validationRoot.transform, false);
                BoxCollider padCollider = padObject.AddComponent<BoxCollider>();
                padCollider.isTrigger = true;
                PurchasePad processorPad = padObject.AddComponent<PurchasePad>();
                SetObjectReference(processorPad, "wallet", wallet);
                SetObjectReference(processorPad, "interactionCollider", padCollider);
                SetString(processorPad, "purchaseLabel", "WOOD PROCESSOR");
                SetBoolean(processorPad, "startsAvailable", false);
                SetInteger(processorPad, "totalCost", 360);
                SetInteger(processorPad, "spendPerTick", 5);
                wallet.Deposit(360);
                Require(processorPad.ProcessPaymentStep() == 0
                        && processorPad.RemainingCost == 360
                        && wallet.Balance == 360,
                    "Locked Processor Purchase Pad accepted payment.");
                Require(processorPad.SetAvailable(true),
                    "Processor Purchase Pad could not unlock after its prerequisite.");
                for (int i = 0; i < 13; i++)
                {
                    Require(processorPad.ProcessPaymentStep() == 5,
                        "Processor Purchase Pad rejected valid partial funding.");
                }

                Require(processorPad.RemainingCost == 295 && wallet.Balance == 295,
                    "Processor Purchase Pad did not preserve $65 partial progress.");
                processorPad.enabled = false;
                processorPad.enabled = true;
                Require(processorPad.RemainingCost == 295,
                    "Processor Purchase Pad lost partial progress across lifecycle changes.");
                int completionCount = 0;
                processorPad.Completed += () => completionCount++;
                int paymentGuard = 0;
                while (!processorPad.IsCompleted && paymentGuard++ < 80)
                {
                    processorPad.ProcessPaymentStep();
                }

                Require(processorPad.IsCompleted
                        && processorPad.RemainingCost == 0
                        && wallet.Balance == 0
                        && completionCount == 1
                        && processorPad.ProcessPaymentStep() == 0,
                    "Processor Purchase Pad did not complete exactly once at $360.");
            }
            finally
            {
                Object.DestroyImmediate(validationRoot);
            }
        }

        private static void ValidateM5Logic()
        {
            GameObject validationRoot = new GameObject("M5 Logic Validation");
            try
            {
                GameObject walletObject = new GameObject("M5 Wallet");
                walletObject.transform.SetParent(validationRoot.transform, false);
                Wallet wallet = walletObject.AddComponent<Wallet>();

                GameObject padObject = new GameObject("M5 Auto Feeder Purchase Pad");
                padObject.transform.SetParent(validationRoot.transform, false);
                BoxCollider padCollider = padObject.AddComponent<BoxCollider>();
                padCollider.isTrigger = true;
                PurchasePad purchasePad = padObject.AddComponent<PurchasePad>();
                SetObjectReference(purchasePad, "wallet", wallet);
                SetObjectReference(purchasePad, "interactionCollider", padCollider);
                SetString(purchasePad, "purchaseLabel", "AUTO FEEDER");
                SetBoolean(purchasePad, "startsAvailable", false);
                SetInteger(purchasePad, "totalCost", 600);
                SetInteger(purchasePad, "spendPerTick", 5);
                wallet.Deposit(600);

                Require(purchasePad.ProcessPaymentStep() == 0
                        && purchasePad.RemainingCost == 600
                        && wallet.Balance == 600,
                    "Locked Auto Feeder Purchase Pad accepted payment.");
                Require(purchasePad.SetAvailable(true),
                    "Auto Feeder Purchase Pad could not unlock after its prerequisite.");
                for (int i = 0; i < 13; i++)
                {
                    Require(purchasePad.ProcessPaymentStep() == 5,
                        "Auto Feeder Purchase Pad rejected valid partial funding.");
                }

                Require(purchasePad.RemainingCost == 535 && wallet.Balance == 535,
                    "Auto Feeder Purchase Pad did not retain its $65 partial payment.");
                purchasePad.enabled = false;
                purchasePad.enabled = true;
                Require(purchasePad.RemainingCost == 535,
                    "Auto Feeder Purchase Pad lost progress across lifecycle changes.");

                int purchaseCompletionCount = 0;
                purchasePad.Completed += () => purchaseCompletionCount++;
                int paymentGuard = 0;
                while (!purchasePad.IsCompleted && paymentGuard++ < 128)
                {
                    purchasePad.ProcessPaymentStep();
                }

                Require(purchasePad.IsCompleted
                        && purchasePad.RemainingCost == 0
                        && wallet.Balance == 0
                        && purchaseCompletionCount == 1
                        && purchasePad.ProcessPaymentStep() == 0,
                    "Auto Feeder Purchase Pad did not complete exactly once at $600.");

                GameObject stockpileObject = new GameObject("M5 WoodStockpile");
                stockpileObject.transform.SetParent(validationRoot.transform, false);
                WoodStockpile stockpile = stockpileObject.AddComponent<WoodStockpile>();
                DepositValidationWood(stockpile);
                DepositValidationWood(stockpile);

                GameObject processorObject = new GameObject("M5 WoodProcessor");
                processorObject.transform.SetParent(validationRoot.transform, false);
                WoodProcessor processor = processorObject.AddComponent<WoodProcessor>();
                GameObject carryObject = new GameObject("M5 CarryStack");
                carryObject.transform.SetParent(validationRoot.transform, false);
                CarryStack carryStack = carryObject.AddComponent<CarryStack>();

                for (int i = 0; i < processor.InputCapacity - 1; i++)
                {
                    Require(carryStack.TryAdd(ResourceType.Wood, 1)
                            && processor.TryTransferInputFrom(carryStack),
                        "Manual input could not fill the unreserved Processor slots.");
                }

                Require(carryStack.TryAdd(ResourceType.Wood, 1),
                    "M5 validation could not prepare manually carried Wood.");
                Require(processor.TryReserveInput(
                            out ProcessorInputReservation destinationReservation),
                    "Auto Feeder could not reserve its Processor destination.");
                Require(stockpile.TryReserveOutgoing(
                            out WoodStockpileOutgoingReservation sourceReservation),
                    "Auto Feeder could not claim its Stockpile source.");
                Require(processor.InputWood == processor.InputCapacity - 1
                        && processor.ReservedInputCapacity == 1
                        && processor.AvailableInputCapacity == 0
                        && stockpile.StoredWood == 1
                        && stockpile.OutgoingReservations == 1
                        && stockpile.TotalOwnedWood == 2,
                    "M5 reservations did not preserve source ownership or destination capacity.");

                Require(stockpile.TryTransferOneTo(carryStack)
                        && stockpile.StoredWood == 0
                        && stockpile.OutgoingReservations == 1
                        && carryStack.GetAmount(ResourceType.Wood) == 2
                        && !stockpile.TryTransferOneTo(carryStack),
                    "Player/conveyor contention did not leave each Wood with exactly one owner.");
                Require(!processor.TryTransferInputFrom(carryStack)
                        && carryStack.GetAmount(ResourceType.Wood) == 2
                        && processor.InputWood == processor.InputCapacity - 1,
                    "Manual feed consumed a destination slot reserved by the Auto Feeder.");

                Require(WoodInputTransferTransaction.TryCommit(
                            stockpile,
                            sourceReservation,
                            processor,
                            destinationReservation)
                        && stockpile.StoredWood == 0
                        && stockpile.OutgoingReservations == 0
                        && processor.InputWood == processor.InputCapacity
                        && processor.ReservedInputCapacity == 0
                        && !sourceReservation.IsValid
                        && !destinationReservation.IsValid,
                    "One completed Auto Feeder transaction did not transfer exactly one Wood.");
                Require(!WoodInputTransferTransaction.TryCommit(
                            stockpile,
                            sourceReservation,
                            processor,
                            destinationReservation),
                    "Stale M5 reservation handles duplicated a completed transfer.");

                GameObject cancellationStockpileObject =
                    new GameObject("M5 Cancellation Stockpile");
                cancellationStockpileObject.transform.SetParent(
                    validationRoot.transform,
                    false);
                WoodStockpile cancellationStockpile =
                    cancellationStockpileObject.AddComponent<WoodStockpile>();
                GameObject cancellationProcessorObject =
                    new GameObject("M5 Cancellation Processor");
                cancellationProcessorObject.transform.SetParent(
                    validationRoot.transform,
                    false);
                WoodProcessor cancellationProcessor =
                    cancellationProcessorObject.AddComponent<WoodProcessor>();
                DepositValidationWood(cancellationStockpile);

                Require(cancellationProcessor.TryReserveInput(
                            out ProcessorInputReservation cancelledDestination),
                    "M5 cancellation validation could not reserve its destination.");
                Require(cancellationStockpile.TryReserveOutgoing(
                            out WoodStockpileOutgoingReservation cancelledSource)
                        && cancellationStockpile.ReleaseOutgoing(cancelledSource)
                        && cancellationProcessor.ReleaseReservedInput(cancelledDestination)
                        && cancellationStockpile.StoredWood == 1
                        && cancellationStockpile.OutgoingReservations == 0
                        && cancellationProcessor.InputWood == 0
                        && cancellationProcessor.ReservedInputCapacity == 0,
                    "Explicit M5 cancellation did not refund/release both reservations.");

                Require(cancellationProcessor.TryReserveInput(out cancelledDestination)
                        && cancellationStockpile.TryReserveOutgoing(out cancelledSource),
                    "M5 cancellation validation could not reacquire a transaction.");
                cancellationProcessor.enabled = false;
                Require(!cancelledDestination.IsValid,
                    "A disabled Processor exposed its destination reservation as usable.");
                cancellationProcessor.enabled = true;
                Require(cancellationProcessor.ReleaseReservedInput(cancelledDestination)
                        && cancellationStockpile.ReleaseOutgoing(cancelledSource)
                        && cancellationStockpile.StoredWood == 1
                        && cancellationStockpile.OutgoingReservations == 0
                        && cancellationProcessor.InputWood == 0,
                    "M5 could not explicitly resolve the disabled-Processor transaction.");

                Require(cancellationProcessor.TryReserveInput(out cancelledDestination)
                        && cancellationStockpile.TryReserveOutgoing(out cancelledSource),
                    "M5 source-disable validation could not reacquire a transaction.");
                cancellationStockpile.enabled = false;
                Require(!cancelledSource.IsValid,
                    "A disabled Stockpile exposed its source reservation as usable.");
                cancellationStockpile.enabled = true;
                Require(cancellationStockpile.ReleaseOutgoing(cancelledSource)
                        && cancellationProcessor.ReleaseReservedInput(cancelledDestination)
                        && cancellationStockpile.StoredWood == 1
                        && cancellationStockpile.OutgoingReservations == 0
                        && cancellationProcessor.ReservedInputCapacity == 0,
                    "M5 could not explicitly resolve the disabled-Stockpile transaction.");

                GameObject repeatedStockpileObject =
                    new GameObject("M5 Repeated Stockpile");
                repeatedStockpileObject.transform.SetParent(validationRoot.transform, false);
                WoodStockpile repeatedStockpile =
                    repeatedStockpileObject.AddComponent<WoodStockpile>();
                GameObject repeatedProcessorObject =
                    new GameObject("M5 Repeated Processor");
                repeatedProcessorObject.transform.SetParent(validationRoot.transform, false);
                WoodProcessor repeatedProcessor =
                    repeatedProcessorObject.AddComponent<WoodProcessor>();

                const int StableCycleCount = 20;
                for (int i = 0; i < StableCycleCount; i++)
                {
                    DepositValidationWood(repeatedStockpile);
                    Require(repeatedProcessor.TryReserveInput(
                                out ProcessorInputReservation repeatedDestination)
                            && repeatedStockpile.TryReserveOutgoing(
                                out WoodStockpileOutgoingReservation repeatedSource)
                            && WoodInputTransferTransaction.TryCommit(
                                repeatedStockpile,
                                repeatedSource,
                                repeatedProcessor,
                                repeatedDestination)
                            && repeatedStockpile.TotalOwnedWood == 0
                            && repeatedStockpile.OutgoingReservations == 0
                            && repeatedProcessor.InputWood == i + 1
                            && repeatedProcessor.ReservedInputCapacity == 0
                            && repeatedProcessor.InputWood
                               + repeatedProcessor.ReservedInputCapacity
                               <= repeatedProcessor.InputCapacity,
                        "Repeated M5 transfer cycles leaked, duplicated, or exceeded capacity.");
                }

                GameObject repeatedCarryObject = new GameObject("M5 Manual Feed CarryStack");
                repeatedCarryObject.transform.SetParent(validationRoot.transform, false);
                CarryStack repeatedCarry = repeatedCarryObject.AddComponent<CarryStack>();
                Require(repeatedCarry.TryAdd(ResourceType.Wood, 1)
                        && repeatedProcessor.TryTransferInputFrom(repeatedCarry)
                        && repeatedProcessor.InputWood == StableCycleCount + 1
                        && repeatedCarry.TotalAmount == 0,
                    "Manual Processor feeding no longer works after repeated automation cycles.");
            }
            finally
            {
                Object.DestroyImmediate(validationRoot);
            }
        }

        private static void ValidateM6Logic()
        {
            GameObject validationRoot = new GameObject("M6 Logic Validation");
            try
            {
                GameObject isolationObject = new GameObject("M6 Carry Isolation");
                isolationObject.transform.SetParent(validationRoot.transform, false);
                CarryStack isolationStack = isolationObject.AddComponent<CarryStack>();
                Require(isolationStack.TryAdd(ResourceType.Crate, 1)
                        && isolationStack.GetAmount(ResourceType.Crate) == 1
                        && !isolationStack.TryAdd(ResourceType.Wood, 1)
                        && !isolationStack.TryAdd(ResourceType.Plank, 1)
                        && isolationStack.TryRemove(ResourceType.Crate, 1)
                        && isolationStack.TotalAmount == 0,
                    "CarryStack did not preserve Crate type isolation or empty-stack reuse.");
                Require(isolationStack.TryReserveCapacity(ResourceType.Crate, 1)
                        && !isolationStack.CanAccept(ResourceType.Wood, 1)
                        && !isolationStack.CanAccept(ResourceType.Plank, 1)
                        && isolationStack.TryCommitReservedAdd(ResourceType.Crate, 1)
                        && isolationStack.GetAmount(ResourceType.Crate) == 1
                        && isolationStack.ReservedCapacity == 0,
                    "CarryStack Crate reservation mixed types or failed to commit atomically.");
                Require(isolationStack.TryRemove(ResourceType.Crate, 1)
                        && isolationStack.TryAdd(ResourceType.Wood, 1)
                        && !isolationStack.TryAdd(ResourceType.Crate, 1)
                        && isolationStack.TryRemove(ResourceType.Wood, 1)
                        && isolationStack.TryAdd(ResourceType.Plank, 1)
                        && !isolationStack.TryAdd(ResourceType.Crate, 1)
                        && isolationStack.TryRemove(ResourceType.Plank, 1)
                        && !isolationStack.TryAdd((ResourceType)99, 1),
                    "CarryStack accepted mixed or unsupported M6 resource ownership.");

                GameObject rejectionStationObject =
                    new GameObject("M6 Rejection Packing Station");
                rejectionStationObject.transform.SetParent(validationRoot.transform, false);
                PackingStation rejectionStation =
                    rejectionStationObject.AddComponent<PackingStation>();
                Require(isolationStack.TryAdd(ResourceType.Wood, 1)
                        && !rejectionStation.TryTransferInputFrom(isolationStack)
                        && isolationStack.GetAmount(ResourceType.Wood) == 1
                        && rejectionStation.InputPlanks == 0
                        && isolationStack.TryRemove(ResourceType.Wood, 1)
                        && isolationStack.TryAdd(ResourceType.Crate, 1)
                        && !rejectionStation.TryTransferInputFrom(isolationStack)
                        && isolationStack.GetAmount(ResourceType.Crate) == 1
                        && rejectionStation.InputPlanks == 0
                        && isolationStack.TryRemove(ResourceType.Crate, 1),
                    "Packing input accepted Wood/Crate or changed rejected ownership.");

                GameObject stationObject = new GameObject("M6 Packing Station");
                stationObject.transform.SetParent(validationRoot.transform, false);
                PackingStation station = stationObject.AddComponent<PackingStation>();
                GameObject transferCarryObject = new GameObject("M6 Plank Transfer Carry");
                transferCarryObject.transform.SetParent(validationRoot.transform, false);
                CarryStack transferCarry = transferCarryObject.AddComponent<CarryStack>();
                for (int batch = 0; batch < 2; batch++)
                {
                    Require(transferCarry.TryAdd(ResourceType.Plank, transferCarry.Capacity),
                        "M6 validation could not prepare a full Plank CarryStack.");
                    for (int unit = 0; unit < transferCarry.Capacity; unit++)
                    {
                        Require(station.TryTransferInputFrom(transferCarry),
                            "Packing Station rejected Planks before reaching input capacity.");
                    }
                }

                Require(station.InputPlanks == station.InputCapacity
                        && station.ProcessingInputPlanks == 0
                        && station.AvailableInputCapacity == 0
                        && transferCarry.TotalAmount == 0,
                    "Packing Station input capacity lost or duplicated deposited Planks.");
                Require(transferCarry.TryAdd(ResourceType.Plank, 1)
                        && !station.TryTransferInputFrom(transferCarry)
                        && transferCarry.GetAmount(ResourceType.Plank) == 1
                        && station.InputPlanks == station.InputCapacity,
                    "Packing Station exceeded input capacity or consumed a rejected Plank.");

                GameObject cashObject = new GameObject("M6 Sale Cash");
                cashObject.transform.SetParent(validationRoot.transform, false);
                CashPile cashPile = cashObject.AddComponent<CashPile>();
                GameObject saleObject = new GameObject("M6 Sale Point");
                saleObject.transform.SetParent(validationRoot.transform, false);
                saleObject.AddComponent<BoxCollider>().isTrigger = true;
                SalePoint salePoint = saleObject.AddComponent<SalePoint>();
                SetObjectReference(salePoint, "carryStack", isolationStack);
                SetObjectReference(salePoint, "cashPile", cashPile);
                SetInteger(salePoint, "woodValue", 5);
                SetInteger(salePoint, "plankValue", 15);
                SetInteger(salePoint, "crateValue", 40);
                Require(salePoint.GetUnitValue(ResourceType.Wood) == 5
                        && salePoint.GetUnitValue(ResourceType.Plank) == 15
                        && salePoint.GetUnitValue(ResourceType.Crate) == 40
                        && (4 * salePoint.GetUnitValue(ResourceType.Wood)) == 20
                        && (2 * salePoint.GetUnitValue(ResourceType.Plank)) == 30
                        && salePoint.GetUnitValue(ResourceType.Crate) == 40,
                    "M6 resource values no longer preserve $5 Wood / $15 Plank / $40 Crate economics.");
                Require(isolationStack.TryAdd(ResourceType.Wood, 1)
                        && salePoint.TryUnloadOne()
                        && cashPile.StoredCash == 5
                        && isolationStack.TryAdd(ResourceType.Plank, 1)
                        && salePoint.TryUnloadOne()
                        && cashPile.StoredCash == 20
                        && isolationStack.TryAdd(ResourceType.Crate, 1)
                        && salePoint.TryUnloadOne()
                        && cashPile.StoredCash == 60
                        && isolationStack.TotalAmount == 0,
                    "Generic Sale Point did not progressively sell Wood, Plank, and Crate values.");

                GameObject walletObject = new GameObject("M6 Purchase Wallet");
                walletObject.transform.SetParent(validationRoot.transform, false);
                Wallet wallet = walletObject.AddComponent<Wallet>();
                GameObject padObject = new GameObject("M6 Packing Purchase Pad");
                padObject.transform.SetParent(validationRoot.transform, false);
                BoxCollider padCollider = padObject.AddComponent<BoxCollider>();
                padCollider.isTrigger = true;
                PurchasePad purchasePad = padObject.AddComponent<PurchasePad>();
                SetObjectReference(purchasePad, "wallet", wallet);
                SetObjectReference(purchasePad, "interactionCollider", padCollider);
                SetString(purchasePad, "purchaseLabel", "PACKING STATION");
                SetBoolean(purchasePad, "startsAvailable", false);
                SetInteger(purchasePad, "totalCost", 900);
                SetInteger(purchasePad, "spendPerTick", 5);
                wallet.Deposit(900);
                Require(purchasePad.ProcessPaymentStep() == 0
                        && purchasePad.RemainingCost == 900
                        && purchasePad.SetAvailable(true),
                    "Locked M6 purchase pad accepted payment or could not unlock.");
                for (int i = 0; i < 13; i++)
                {
                    Require(purchasePad.ProcessPaymentStep() == 5,
                        "Packing Station pad rejected valid partial funding.");
                }

                Require(purchasePad.RemainingCost == 835 && wallet.Balance == 835,
                    "Packing Station pad did not retain its $65 partial payment.");
                purchasePad.enabled = false;
                purchasePad.enabled = true;
                Require(purchasePad.RemainingCost == 835,
                    "Packing Station pad lost partial payment across lifecycle changes.");
                int completionCount = 0;
                purchasePad.Completed += () => completionCount++;
                int paymentGuard = 0;
                while (!purchasePad.IsCompleted && paymentGuard++ < 200)
                {
                    purchasePad.ProcessPaymentStep();
                }

                Require(purchasePad.IsCompleted
                        && purchasePad.RemainingCost == 0
                        && wallet.Balance == 0
                        && completionCount == 1
                        && purchasePad.ProcessPaymentStep() == 0,
                    "Packing Station pad did not complete exactly once at $900.");
            }
            finally
            {
                Object.DestroyImmediate(validationRoot);
            }
        }

        private static void ValidateM7Logic()
        {
            GameObject validationRoot = new GameObject("M7 Logic Validation");
            try
            {
                GameObject walletObject = new GameObject("M7 Courier Purchase Wallet");
                walletObject.transform.SetParent(validationRoot.transform, false);
                Wallet wallet = walletObject.AddComponent<Wallet>();
                GameObject padObject = new GameObject("M7 Courier Purchase Pad");
                padObject.transform.SetParent(validationRoot.transform, false);
                BoxCollider padCollider = padObject.AddComponent<BoxCollider>();
                padCollider.isTrigger = true;
                PurchasePad purchasePad = padObject.AddComponent<PurchasePad>();
                SetObjectReference(purchasePad, "wallet", wallet);
                SetObjectReference(purchasePad, "interactionCollider", padCollider);
                SetString(purchasePad, "purchaseLabel", "DELIVERY COURIER");
                SetBoolean(purchasePad, "startsAvailable", false);
                SetInteger(purchasePad, "totalCost", 1500);
                SetInteger(purchasePad, "spendPerTick", 5);
                wallet.Deposit(1500);

                Require(purchasePad.ProcessPaymentStep() == 0
                        && purchasePad.RemainingCost == 1500
                        && purchasePad.SetAvailable(true),
                    "Locked M7 Courier pad accepted payment or could not unlock.");
                for (int i = 0; i < 13; i++)
                {
                    Require(purchasePad.ProcessPaymentStep() == 5,
                        "Courier pad rejected valid partial funding.");
                }

                Require(purchasePad.RemainingCost == 1435 && wallet.Balance == 1435,
                    "Courier pad did not retain its $65 partial payment.");
                purchasePad.enabled = false;
                purchasePad.enabled = true;
                Require(purchasePad.RemainingCost == 1435,
                    "Courier pad lost partial payment across lifecycle changes.");

                int completionCount = 0;
                purchasePad.Completed += () => completionCount++;
                int paymentGuard = 0;
                while (!purchasePad.IsCompleted && paymentGuard++ < 400)
                {
                    purchasePad.ProcessPaymentStep();
                }

                Require(purchasePad.IsCompleted
                        && purchasePad.RemainingCost == 0
                        && wallet.Balance == 0
                        && completionCount == 1
                        && purchasePad.ProcessPaymentStep() == 0,
                    "Courier pad did not complete exactly once at $1500.");

                GameObject stationObject = new GameObject("M7 Packing Reservation");
                stationObject.transform.SetParent(validationRoot.transform, false);
                PackingStation packingStation =
                    stationObject.AddComponent<PackingStation>();
                Require(packingStation.MaximumCourierReservedCrates == 2
                        && packingStation.ReservedCourierOutputCrates == 0
                        && !packingStation.TryReserveCourierOutput(
                            2,
                            out PackingStationOutputReservation emptyReservation)
                        && !emptyReservation.IsValid,
                    "Empty Packing output created an invalid M7 courier claim.");

                GameObject cashObject = new GameObject("M7 Delivery Cash Pile");
                cashObject.transform.SetParent(validationRoot.transform, false);
                CashPile cashPile = cashObject.AddComponent<CashPile>();
                GameObject courierObject = new GameObject("M7 Courier");
                courierObject.transform.SetParent(validationRoot.transform, false);
                CrateCourier courier = courierObject.AddComponent<CrateCourier>();
                Require(courier.AcceptedResourceType == ResourceType.Crate
                        && courier.Capacity == 2
                        && courier.CashPerCrate == 40
                        && !courier.TryCommitDelivery()
                        && cashPile.StoredCash == 0,
                    "Courier accepted the wrong product/capacity or credited a non-trip.");

                GameObject deliveryWalletObject =
                    new GameObject("M7 Delivery Isolation Wallet");
                deliveryWalletObject.transform.SetParent(validationRoot.transform, false);
                Wallet deliveryWallet = deliveryWalletObject.AddComponent<Wallet>();
                Require(cashPile.Deposit(courier.CashPerCrate) == 40
                        && cashPile.StoredCash == 40
                        && deliveryWallet.Balance == 0
                        && cashPile.Deposit(courier.Capacity * courier.CashPerCrate) == 80
                        && cashPile.StoredCash == 120
                        && deliveryWallet.Balance == 0,
                    "M7 Cash Pile did not preserve exact $40/$80 delivery isolation.");
                Require(cashPile.TryWithdrawAll(out int collectedCash)
                        && collectedCash == 120
                        && cashPile.StoredCash == 0
                        && deliveryWallet.Balance == 0
                        && deliveryWallet.Deposit(collectedCash) == 120,
                    "M7 Wallet changed before explicit Cash Pile collection.");
            }
            finally
            {
                Object.DestroyImmediate(validationRoot);
            }
        }

        private static void ValidateM8Logic()
        {
            LumberCampProgressStage[] orderedStages =
            {
                LumberCampProgressStage.ProductionUpgrade,
                LumberCampProgressStage.Worker,
                LumberCampProgressStage.Processor,
                LumberCampProgressStage.AutoFeeder,
                LumberCampProgressStage.PackingStation,
                LumberCampProgressStage.Courier,
                LumberCampProgressStage.FirstCourierDelivery,
                LumberCampProgressStage.Complete
            };
            for (int i = 0; i < orderedStages.Length; i++)
            {
                Require((int)orderedStages[i] == i,
                    "M8 Next Unlock stage order changed or can skip a prerequisite.");
            }

            Require(NextUnlockGuidance.BuildDisplayText(
                        LumberCampProgressStage.AutoFeeder,
                        420,
                        600)
                    == "NEXT: AUTO FEEDER\n$420 / $600",
                "M8 guidance did not render authoritative paid / total progress.");
            Require(NextUnlockGuidance.BuildDisplayText(
                        LumberCampProgressStage.FirstCourierDelivery,
                        0,
                        0)
                    == "NEXT: FIRST COURIER DELIVERY"
                    && NextUnlockGuidance.BuildDisplayText(
                        LumberCampProgressStage.Complete,
                        0,
                        0)
                    == "LUMBER CAMP COMPLETE",
                "M8 guidance requires distinct delivery-waiting and completed states.");

            GameObject probeObject = new GameObject("M8 Pacing Probe Logic Validation");
            try
            {
                LumberCampPacingProbe probe =
                    probeObject.AddComponent<LumberCampPacingProbe>();
                probe.ResetProbe();
                Require(probe.RecordedMilestoneCount == 1
                        && probe.HasTimestamp(
                            LumberCampPacingMilestone.SessionStart)
                        && Mathf.Approximately(
                            (float)probe.GetElapsedSeconds(
                                LumberCampPacingMilestone.SessionStart),
                            0f)
                        && !probe.HasTimestamp(
                            LumberCampPacingMilestone.FirstSale)
                        && probe.AreRecordedTimestampsOrdered()
                        && !probe.HasCompleteOrderedSequence(),
                    "M8 pacing probe did not reset to one valid session-start timestamp.");

                probe.ResetProbe();
                Require(probe.RecordedMilestoneCount == 1
                        && probe.AutomaticReportCount == 0
                        && probe.BuildReport().Contains("Sale --:--")
                        && probe.BuildReport().Contains("Complete --:--"),
                    "M8 pacing probe retained stale timestamps or report state after reset.");
            }
            finally
            {
                Object.DestroyImmediate(probeObject);
            }
        }

        private static void DepositValidationWood(WoodStockpile stockpile)
        {
            Require(stockpile != null
                    && stockpile.TryReserveIncoming(
                        out WoodStockpileReservation reservation)
                    && stockpile.TryDepositReserved(reservation),
                "M5 validation could not deposit one authoritative Stockpile Wood.");
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

        private static string GetCommandLineValue(string argumentName)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(
                        arguments[i],
                        argumentName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[i + 1];
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

        private static float GetFloatValue(Object target, string propertyName)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on {target.GetType().Name}.");
            }

            return property.floatValue;
        }

        private static Vector2 GetVector2Value(Object target, string propertyName)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Serialized property '{propertyName}' was not found on {target.GetType().Name}.");
            }

            return property.vector2Value;
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
