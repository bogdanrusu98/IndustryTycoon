using System;
using IndustryTycoon.Core;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using IndustryTycoon.Interaction;
using IndustryTycoon.Player;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    [InitializeOnLoad]
    public static class LumberCampPlayModeSmokeTest
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype_LumberCamp.unity";
        private const string RunningKey = "IndustryTycoon.M2.Smoke.Running";
        private const string CommandLineKey = "IndustryTycoon.M2.Smoke.CommandLine";
        private const string FinishPendingKey = "IndustryTycoon.M2.Smoke.FinishPending";
        private const string SuccessKey = "IndustryTycoon.M2.Smoke.Success";
        private const string ResultMessageKey = "IndustryTycoon.M2.Smoke.ResultMessage";

        private static readonly Vector3 NeutralPosition = new Vector3(0f, 0f, -9f);

        private enum Stage
        {
            Warmup,
            MeasureBaseProduction,
            FirstSaleExitEarly,
            VerifyFirstSaleStopped,
            FirstSale,
            LeaveAfterFirstSale,
            FirstCashCollection,
            WaitForFirstCashHud,
            PartialPurchase,
            LeaveAfterPartialPurchase,
            SecondSale,
            LeaveAfterSecondSale,
            SecondCashCollection,
            WaitForSecondCashHud,
            CompletePurchase,
            PrepareUpgradedMeasurement,
            MeasureUpgradedProduction,
            RepeatSale,
            LeaveAfterRepeatSale,
            RepeatCashCollection,
            VerifyCompletedPad
        }

        private static CharacterController _playerController;
        private static ResourceCollector _resourceCollector;
        private static CarryStack _carryStack;
        private static Wallet _wallet;
        private static WoodSpawner _woodSpawner;
        private static SalePoint _salePoint;
        private static CashPile _cashPile;
        private static CashPileCollector _cashCollector;
        private static PurchasePad _purchasePad;
        private static WoodProductionUpgrade _productionUpgrade;
        private static PlayerPickupFeedback _pickupFeedback;
        private static SalePointFeedback _saleFeedback;
        private static CashPileFeedback _cashFeedback;
        private static PurchasePadFeedback _purchaseFeedback;
        private static ProductionUnlockFeedback _unlockFeedback;
        private static WalletHud _walletHud;
        private static SmoothFollowCamera _followCamera;
        private static Text _woodText;
        private static Text _cashText;

        private static Stage _stage;
        private static double _stageStartedAt;
        private static double _runStartedAt;
        private static bool _runtimeInitialized;
        private static bool _observedCashInFlight;
        private static int _completionCount;
        private static int _baseSpawnStart;
        private static int _baseSpawnDelta;
        private static int _upgradedSpawnStart;
        private static int _upgradedSpawnDelta;
        private static int _previousWood;
        private static int _previousPileCash;
        private static int _previousWalletCash;
        private static int _previousRemainingCost;
        private static int _observedSaleUnits;
        private static int _observedPaymentUnits;
        private static bool _attemptedDuplicateCashCollection;
        private static int _cashCollectionEventCount;
        private static int _cashCollectionEventTotal;
        private static int _saleEventCount;
        private static int _paymentEventTotal;
        private static int _upgradeAppliedCount;

        static LumberCampPlayModeSmokeTest()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

            if (SessionState.GetBool(RunningKey, false))
            {
                EditorApplication.update -= UpdateSmokeTest;
                EditorApplication.update += UpdateSmokeTest;
            }

            if (SessionState.GetBool(FinishPendingKey, false))
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
            }
        }

        [MenuItem("Industry Tycoon/Prototype/Run Full Loop Smoke Test")]
        private static void RunFromMenu()
        {
            StartSmokeTest(false);
        }

        public static void RunFromCommandLine()
        {
            StartSmokeTest(true);
        }

        private static void StartSmokeTest(bool commandLine)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before starting the M2 smoke test.");
            }

            if (!commandLine && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new InvalidOperationException($"Missing prototype scene at {ScenePath}.");
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(CommandLineKey, commandLine);
            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);
            _runtimeInitialized = false;

            EditorApplication.update -= UpdateSmokeTest;
            EditorApplication.update += UpdateSmokeTest;
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode
                && SessionState.GetBool(RunningKey, false))
            {
                InitializeRuntimeState();
            }
            else if (state == PlayModeStateChange.EnteredEditMode
                     && SessionState.GetBool(FinishPendingKey, false))
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
            }
        }

        private static void UpdateSmokeTest()
        {
            if (!SessionState.GetBool(RunningKey, false))
            {
                EditorApplication.update -= UpdateSmokeTest;
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            try
            {
                if (!_runtimeInitialized)
                {
                    InitializeRuntimeState();
                }

                if (Now - _runStartedAt > 45d)
                {
                    throw new InvalidOperationException("Full-loop smoke test exceeded its 45-second timeout.");
                }

                TickCurrentStage();
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void InitializeRuntimeState()
        {
            if (_runtimeInitialized || !EditorApplication.isPlaying)
            {
                return;
            }

            _playerController = Object.FindAnyObjectByType<CharacterController>();
            _resourceCollector = Object.FindAnyObjectByType<ResourceCollector>();
            _carryStack = Object.FindAnyObjectByType<CarryStack>();
            _wallet = Object.FindAnyObjectByType<Wallet>();
            _woodSpawner = Object.FindAnyObjectByType<WoodSpawner>();
            _salePoint = Object.FindAnyObjectByType<SalePoint>();
            _cashPile = Object.FindAnyObjectByType<CashPile>();
            _cashCollector = Object.FindAnyObjectByType<CashPileCollector>();
            _purchasePad = Object.FindAnyObjectByType<PurchasePad>();
            _productionUpgrade = Object.FindAnyObjectByType<WoodProductionUpgrade>();
            _pickupFeedback = Object.FindAnyObjectByType<PlayerPickupFeedback>();
            _saleFeedback = Object.FindAnyObjectByType<SalePointFeedback>();
            _cashFeedback = Object.FindAnyObjectByType<CashPileFeedback>();
            _purchaseFeedback = Object.FindAnyObjectByType<PurchasePadFeedback>();
            _unlockFeedback = Object.FindAnyObjectByType<ProductionUnlockFeedback>();
            _walletHud = Object.FindAnyObjectByType<WalletHud>();
            _followCamera = Object.FindAnyObjectByType<SmoothFollowCamera>();
            _woodText = FindHudText("Wood Text");
            _cashText = FindHudText("Cash Text");

            Require(_playerController != null, "Smoke test could not find the Player CharacterController.");
            Require(_resourceCollector != null, "Smoke test could not find the ResourceCollector.");
            Require(_carryStack != null, "Smoke test could not find the CarryStack.");
            Require(_wallet != null, "Smoke test could not find the Wallet.");
            Require(_woodSpawner != null, "Smoke test could not find the WoodSpawner.");
            Require(_salePoint != null, "Smoke test could not find the Sale Point.");
            Require(_cashPile != null && _cashCollector != null,
                "Smoke test could not find the Cash Pile components.");
            Require(_purchasePad != null, "Smoke test could not find the Purchase Pad.");
            Require(_productionUpgrade != null, "Smoke test could not find the production upgrade.");
            Require(_pickupFeedback != null
                    && _saleFeedback != null
                    && _cashFeedback != null
                    && _purchaseFeedback != null
                    && _unlockFeedback != null,
                "Smoke test could not find all M2 presentation components.");
            Require(_walletHud != null, "Smoke test could not find the animated Wallet HUD.");
            Require(_followCamera != null, "Smoke test could not find the follow camera.");
            Require(_woodText != null && _cashText != null, "Smoke test could not find both HUD lines.");

            _resourceCollector.enabled = false;
            MovePlayerTo(NeutralPosition);
            _completionCount = 0;
            _purchasePad.Completed += HandlePurchaseCompleted;
            _salePoint.UnitSold += HandleUnitSold;
            _cashCollector.CollectionCompleted += HandleCashCollectionCompleted;
            _purchasePad.PaymentProcessed += HandlePaymentProcessed;
            _productionUpgrade.Applied += HandleUpgradeApplied;
            _observedCashInFlight = false;
            _attemptedDuplicateCashCollection = false;
            _cashCollectionEventCount = 0;
            _cashCollectionEventTotal = 0;
            _saleEventCount = 0;
            _paymentEventTotal = 0;
            _upgradeAppliedCount = 0;
            _baseSpawnDelta = 0;
            _upgradedSpawnDelta = 0;
            _runStartedAt = Now;
            AdvanceTo(Stage.Warmup);
            _runtimeInitialized = true;
        }

        private static void TickCurrentStage()
        {
            Require(_wallet.Balance >= 0, "Wallet became negative during the smoke test.");
            Require(_carryStack.TotalAmount >= 0, "CarryStack became negative during the smoke test.");
            Require(_purchasePad.RemainingCost >= 0,
                "Purchase Pad remaining cost became negative during the smoke test.");

            switch (_stage)
            {
                case Stage.Warmup:
                    TickWarmup();
                    break;
                case Stage.MeasureBaseProduction:
                    TickBaseProductionMeasurement();
                    break;
                case Stage.FirstSaleExitEarly:
                    TickFirstSaleExitEarly();
                    break;
                case Stage.VerifyFirstSaleStopped:
                    TickVerifyFirstSaleStopped();
                    break;
                case Stage.FirstSale:
                    TickSale(60, Stage.LeaveAfterFirstSale);
                    break;
                case Stage.LeaveAfterFirstSale:
                    TickLeaveBeforeCash(Stage.FirstCashCollection);
                    break;
                case Stage.FirstCashCollection:
                    TickCashCollection(60, Stage.WaitForFirstCashHud);
                    break;
                case Stage.WaitForFirstCashHud:
                    TickCashHudSettle(Stage.PartialPurchase);
                    break;
                case Stage.PartialPurchase:
                    TickPartialPurchase();
                    break;
                case Stage.LeaveAfterPartialPurchase:
                    TickLeaveAfterPartialPurchase();
                    break;
                case Stage.SecondSale:
                    TickSale(60, Stage.LeaveAfterSecondSale);
                    break;
                case Stage.LeaveAfterSecondSale:
                    TickLeaveBeforeCash(Stage.SecondCashCollection);
                    break;
                case Stage.SecondCashCollection:
                    TickCashCollection(60, Stage.WaitForSecondCashHud);
                    break;
                case Stage.WaitForSecondCashHud:
                    TickCashHudSettle(Stage.CompletePurchase);
                    break;
                case Stage.CompletePurchase:
                    TickCompletePurchase();
                    break;
                case Stage.PrepareUpgradedMeasurement:
                    TickPrepareUpgradedMeasurement();
                    break;
                case Stage.MeasureUpgradedProduction:
                    TickUpgradedProductionMeasurement();
                    break;
                case Stage.RepeatSale:
                    TickSale(5, Stage.LeaveAfterRepeatSale);
                    break;
                case Stage.LeaveAfterRepeatSale:
                    TickLeaveBeforeCash(Stage.RepeatCashCollection);
                    break;
                case Stage.RepeatCashCollection:
                    TickCashCollection(5, Stage.VerifyCompletedPad);
                    break;
                case Stage.VerifyCompletedPad:
                    TickCompletedPadVerification();
                    break;
            }
        }

        private static void TickWarmup()
        {
            if (!HasWaited(0.6d))
            {
                return;
            }

            Require(_carryStack.TotalAmount == 0, "CarryStack did not start empty.");
            Require(_wallet.Balance == 0 && _cashPile.StoredCash == 0,
                "Wallet and Cash Pile did not start empty.");
            Require(_purchasePad.RemainingCost == 120 && !_purchasePad.IsCompleted,
                "Purchase Pad did not start at $120.");
            Require(Mathf.Approximately(_woodSpawner.ProductionRateMultiplier, 1f),
                "Wood production did not start at 1x.");
            Require(!_productionUpgrade.SecondCutterVisual.activeSelf,
                "Second cutter was visible before purchase completion.");
            Require(_saleFeedback.PoolCount == 4
                    && _cashCollector.FlightVisualPoolCount == 4
                    && _cashFeedback.CachedBundleCount == 8
                    && _purchaseFeedback.TokenPoolCount == 4,
                "M2 presentation pools were not prewarmed to their configured caps.");

            _baseSpawnStart = _woodSpawner.ActiveCount;
            AdvanceTo(Stage.MeasureBaseProduction);
        }

        private static void TickBaseProductionMeasurement()
        {
            EnsureStageTimeout(4d);
            if (!HasWaited(2.7d))
            {
                return;
            }

            _baseSpawnDelta = _woodSpawner.ActiveCount - _baseSpawnStart;
            Require(_baseSpawnDelta >= 1 && _baseSpawnDelta <= 3,
                $"Unexpected 1x production delta: {_baseSpawnDelta} logs in 2.7 seconds.");

            FillCarryStack(12);
            BeginSaleObservation();
            MovePlayerTo(_salePoint.transform.position);
            AdvanceTo(Stage.FirstSaleExitEarly);
        }

        private static void TickFirstSaleExitEarly()
        {
            EnsureStageTimeout(2d);
            ObserveSaleProgress();
            if (_observedSaleUnits < 1)
            {
                return;
            }

            Require(_observedSaleUnits == 1
                    && _carryStack.GetAmount(ResourceType.Wood) == 11
                    && _cashPile.StoredCash == 5,
                "First Sale Point step did not settle at exactly 11 wood / $5.");
            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.VerifyFirstSaleStopped);
        }

        private static void TickVerifyFirstSaleStopped()
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.45d))
            {
                return;
            }

            Require(!_salePoint.IsPlayerInside,
                "Sale Point still considered the player inside after the early exit.");
            Require(_carryStack.GetAmount(ResourceType.Wood) == 11
                    && _cashPile.StoredCash == 5
                    && _observedSaleUnits == 1,
                "Sale Point continued unloading after the player left its trigger.");

            MovePlayerTo(_salePoint.transform.position);
            AdvanceTo(Stage.FirstSale);
        }

        private static void TickSale(int expectedCash, Stage nextStage)
        {
            EnsureStageTimeout(5d);
            ObserveSaleProgress();
            int currentWood = _carryStack.GetAmount(ResourceType.Wood);
            int currentCash = _cashPile.StoredCash;

            if (currentWood > 0)
            {
                return;
            }

            int expectedUnits = expectedCash / _salePoint.WoodValue;
            Require(currentCash == expectedCash && _observedSaleUnits == expectedUnits,
                $"Sale ended at ${currentCash} after {_observedSaleUnits} observed units; expected ${expectedCash}.");
            Require(_carryStack.TotalAmount == 0, "Sale allowed CarryStack totals to become inconsistent.");

            MovePlayerTo(NeutralPosition);
            AdvanceTo(nextStage);
        }

        private static void ObserveSaleProgress()
        {
            int currentWood = _carryStack.GetAmount(ResourceType.Wood);
            int currentCash = _cashPile.StoredCash;
            if (currentWood == _previousWood)
            {
                return;
            }

            int unloadedUnits = _previousWood - currentWood;
            Require(unloadedUnits == 1,
                $"Sale Point unloaded {unloadedUnits} wood in one observed step instead of one.");
            Require(currentCash - _previousPileCash == _salePoint.WoodValue,
                "Sale Point cash and CarryStack amount lost synchronization.");
            _observedSaleUnits += unloadedUnits;
            _previousWood = currentWood;
            _previousPileCash = currentCash;
        }

        private static void TickLeaveBeforeCash(Stage cashStage)
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.35d))
            {
                return;
            }

            Require(!_salePoint.IsPlayerInside, "Sale Point did not stop cleanly after the player left.");
            _observedCashInFlight = false;
            _attemptedDuplicateCashCollection = false;
            MovePlayerTo(_cashPile.transform.position);
            AdvanceTo(cashStage);
        }

        private static void TickCashCollection(int expectedWalletGain, Stage nextStage)
        {
            EnsureStageTimeout(3d);
            if (_cashCollector.PendingCash == expectedWalletGain
                && _cashPile.StoredCash == 0
                && _wallet.Balance == 0)
            {
                _observedCashInFlight = true;
                if (!_attemptedDuplicateCashCollection)
                {
                    Require(!_cashCollector.TryStartCollection(),
                        "Cash collector accepted a duplicate collection while cash was already pending.");
                    _attemptedDuplicateCashCollection = true;
                }
            }

            if (_wallet.Balance < expectedWalletGain)
            {
                return;
            }

            Require(_observedCashInFlight,
                "Cash reached the Wallet without an observed in-flight ownership phase.");
            Require(_wallet.Balance == expectedWalletGain
                    && _cashPile.StoredCash == 0
                    && _cashCollector.PendingCash == 0,
                "Cash collection did not settle the exact claimed amount.");

            int expectedEventCount = nextStage == Stage.WaitForFirstCashHud
                ? 1
                : nextStage == Stage.WaitForSecondCashHud ? 2 : 3;
            int expectedEventTotal = nextStage == Stage.WaitForFirstCashHud
                ? 60
                : nextStage == Stage.WaitForSecondCashHud ? 120 : 125;
            Require(_cashCollectionEventCount == expectedEventCount
                    && _cashCollectionEventTotal == expectedEventTotal,
                "Cash collection completion events did not match settled Wallet ownership.");

            MovePlayerTo(NeutralPosition);
            AdvanceTo(nextStage);
        }

        private static void TickCashHudSettle(Stage nextStage)
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.40d))
            {
                return;
            }

            Require(_walletHud.DisplayedBalance == _wallet.Balance
                    && _cashText.text == $"$ {_wallet.Balance}",
                "Animated Wallet HUD did not converge exactly to the authoritative cash value.");

            _previousWalletCash = _wallet.Balance;
            _previousRemainingCost = _purchasePad.RemainingCost;
            _observedPaymentUnits = 0;
            MovePlayerTo(_purchasePad.transform.position);
            AdvanceTo(nextStage);
        }

        private static void TickPartialPurchase()
        {
            EnsureStageTimeout(4d);
            ObservePurchaseProgress();
            if (_wallet.Balance > 0)
            {
                return;
            }

            Require(_purchasePad.RemainingCost == 60 && !_purchasePad.IsCompleted,
                "Partial purchase did not pause at exactly $60 remaining.");
            Require(_observedPaymentUnits == 60,
                "Purchase progress did not match the actual $60 spent.");
            Require(_paymentEventTotal == 60
                    && _purchaseFeedback.PaymentFeedbackCount == 12
                    && _purchaseFeedback.FundingPausedFeedbackCount == 1,
                "Purchase feedback did not match the first funded/empty-wallet phase.");

            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.LeaveAfterPartialPurchase);
        }

        private static void TickLeaveAfterPartialPurchase()
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.5d))
            {
                return;
            }

            Require(!_purchasePad.IsPlayerInside, "Purchase Pad did not detect the player leaving.");
            Require(_purchasePad.RemainingCost == 60 && _wallet.Balance == 0,
                "Purchase progress changed while away from the pad with zero cash.");

            FillCarryStack(12);
            BeginSaleObservation();
            MovePlayerTo(_salePoint.transform.position);
            AdvanceTo(Stage.SecondSale);
        }

        private static void TickCompletePurchase()
        {
            EnsureStageTimeout(4d);
            ObservePurchaseProgress();
            if (!_purchasePad.IsCompleted)
            {
                return;
            }

            Require(_purchasePad.RemainingCost == 0 && _wallet.Balance == 0,
                "Completed purchase did not consume exactly the remaining $60.");
            Require(_observedPaymentUnits == 60, "Final purchase progress did not match actual spending.");
            Require(_completionCount == 1, "Purchase Pad did not complete exactly once.");
            Require(!_purchasePad.InteractionCollider.enabled,
                "Completed Purchase Pad remained purchasable.");
            Require(_productionUpgrade.IsApplied
                    && _productionUpgrade.SecondCutterVisual.activeSelf,
                "Second cutter did not visibly activate on completion.");
            Require(Mathf.Approximately(_woodSpawner.ProductionRateMultiplier, 2f)
                    && Mathf.Approximately(
                        _woodSpawner.EffectiveSpawnInterval,
                        _woodSpawner.BaseSpawnInterval * 0.5f),
                "Production cadence did not change from 1.25s to 0.625s.");
            Require(_paymentEventTotal == 120
                    && _upgradeAppliedCount == 1
                    && _purchaseFeedback.PaymentFeedbackCount == 24
                    && _purchaseFeedback.CompletionFeedbackCount == 1
                    && _unlockFeedback.PresentationCount == 1,
                "Purchase/unlock feedback did not match the single authoritative completion.");

            MovePlayerTo(NeutralPosition);
            AdvanceTo(Stage.PrepareUpgradedMeasurement);
        }

        private static void TickPrepareUpgradedMeasurement()
        {
            EnsureStageTimeout(2d);
            if (!HasWaited(0.35d))
            {
                return;
            }

            _upgradedSpawnStart = _woodSpawner.ActiveCount;
            AdvanceTo(Stage.MeasureUpgradedProduction);
        }

        private static void TickUpgradedProductionMeasurement()
        {
            EnsureStageTimeout(4d);
            if (!HasWaited(2.7d))
            {
                return;
            }

            _upgradedSpawnDelta = _woodSpawner.ActiveCount - _upgradedSpawnStart;
            Require(_upgradedSpawnDelta > _baseSpawnDelta,
                $"2x production was not faster: before {_baseSpawnDelta}, after {_upgradedSpawnDelta}.");
            Require(_upgradedSpawnDelta >= 3,
                $"2x production produced only {_upgradedSpawnDelta} logs in 2.7 seconds.");

            FillCarryStack(1);
            BeginSaleObservation();
            MovePlayerTo(_salePoint.transform.position);
            AdvanceTo(Stage.RepeatSale);
        }

        private static void TickCompletedPadVerification()
        {
            EnsureStageTimeout(3d);
            if (_wallet.Balance < 5)
            {
                return;
            }

            if (!HasWaited(0.1d))
            {
                MovePlayerTo(_purchasePad.transform.position);
                return;
            }

            if (!HasWaited(0.7d))
            {
                return;
            }

            Require(_wallet.Balance == 5, "Completed Purchase Pad consumed cash from the repeat loop.");
            Require(_purchasePad.IsCompleted && _completionCount == 1,
                "Completed Purchase Pad changed completion state.");
            Require(_woodText.text == "Wood: 0 / 12", "Wood HUD did not finish at 0 / 12.");
            Require(_cashText.text == "$ 5", "Wallet HUD did not finish at $5.");
            Require(_walletHud.DisplayedBalance == _wallet.Balance,
                "Wallet HUD did not converge to the final authoritative value.");
            Require(_saleEventCount == 25
                    && _saleFeedback.FeedbackCount == 25
                    && _saleFeedback.EmptyFeedbackCount == 3,
                "Sale presentation events did not match 12 + 12 + 1 successful sales.");
            Require(_pickupFeedback.FeedbackCount == 25,
                "Pickup presentation did not match accepted CarryStack additions.");
            Require(_cashFeedback.CollectionFeedbackCount == 3
                    && _cashCollectionEventCount == 3
                    && _cashCollectionEventTotal == 125,
                "Cash presentation did not match the three settled collections.");
            Require(_saleFeedback.PoolCount == 4 && _saleFeedback.ActiveFlightCount == 0,
                "Sale visual pool changed size or remained active after settling.");
            Require(_cashCollector.FlightVisualPoolCount == 4
                    && _cashCollector.ActiveVisualCount == 0
                    && _cashFeedback.CachedBundleCount == 8,
                "Cash visual pools changed size or remained active after settling.");
            Require(_purchaseFeedback.TokenPoolCount == 4
                    && _purchaseFeedback.ActiveTokenCount == 0,
                "Purchase token pool changed size or remained active after settling.");
            Require(!_unlockFeedback.IsPresenting
                    && !_followCamera.IsImpulseActive
                    && _followCamera.CurrentImpulseOffset.sqrMagnitude < 0.000001f,
                "Unlock presentation or camera impulse did not settle cleanly.");

            Pass(
                $"M2 full-loop Play Mode smoke passed: sale 12 + 12 + 1 wood, "
                + $"early sale exit stopped, pooled feedback settled, HUD converged, completion count 1, "
                + $"production delta {_baseSpawnDelta} -> {_upgradedSpawnDelta} logs / 2.7s.");
        }

        private static void BeginSaleObservation()
        {
            _previousWood = _carryStack.GetAmount(ResourceType.Wood);
            _previousPileCash = _cashPile.StoredCash;
            _observedSaleUnits = 0;
        }

        private static void ObservePurchaseProgress()
        {
            int currentRemaining = _purchasePad.RemainingCost;
            int currentWallet = _wallet.Balance;
            if (currentRemaining == _previousRemainingCost)
            {
                return;
            }

            int paid = _previousRemainingCost - currentRemaining;
            Require(paid == _purchasePad.SpendPerTick,
                $"Purchase Pad paid ${paid} in one observed step instead of ${_purchasePad.SpendPerTick}.");
            Require(_previousWalletCash - currentWallet == paid,
                "Purchase progress did not equal the actual Wallet spend.");
            _observedPaymentUnits += paid;
            _previousRemainingCost = currentRemaining;
            _previousWalletCash = currentWallet;
        }

        private static void FillCarryStack(int amount)
        {
            Require(_carryStack.TotalAmount == 0, "Smoke test expected an empty CarryStack before loading.");
            for (int i = 0; i < amount; i++)
            {
                Require(_carryStack.TryAdd(ResourceType.Wood, 1),
                    "CarryStack rejected smoke-test wood before capacity.");
            }

            Require(_carryStack.GetAmount(ResourceType.Wood) == amount,
                "CarryStack logical amount did not match the loaded smoke-test amount.");
            if (amount == _carryStack.Capacity)
            {
                Require(!_carryStack.TryAdd(ResourceType.Wood, 1),
                    "CarryStack accepted wood beyond capacity during Play Mode.");
            }
        }

        private static void MovePlayerTo(Vector3 destination)
        {
            Vector3 planarDestination = new Vector3(destination.x, 0f, destination.z);
            _playerController.Move(planarDestination - _playerController.transform.position);
            Physics.SyncTransforms();
        }

        private static Text FindHudText(string objectName)
        {
            Text[] texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].name == objectName)
                {
                    return texts[i];
                }
            }

            return null;
        }

        private static void HandlePurchaseCompleted()
        {
            _completionCount++;
        }

        private static void HandleUnitSold(SaleFeedbackData feedback)
        {
            _saleEventCount++;
        }

        private static void HandleCashCollectionCompleted(int amount)
        {
            _cashCollectionEventCount++;
            _cashCollectionEventTotal += amount;
        }

        private static void HandlePaymentProcessed(int spentAmount, int remainingCost)
        {
            _paymentEventTotal += spentAmount;
        }

        private static void HandleUpgradeApplied()
        {
            _upgradeAppliedCount++;
        }

        private static void AdvanceTo(Stage nextStage)
        {
            _stage = nextStage;
            _stageStartedAt = Now;
        }

        private static bool HasWaited(double seconds)
        {
            return Now - _stageStartedAt >= seconds;
        }

        private static void EnsureStageTimeout(double seconds)
        {
            if (Now - _stageStartedAt > seconds)
            {
                throw new InvalidOperationException($"Smoke test timed out in stage {_stage}.");
            }
        }

        private static double Now => Time.realtimeSinceStartupAsDouble;

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Pass(string message)
        {
            Debug.Log(message);
            EndRun(true, message);
        }

        private static void Fail(string message)
        {
            string result = $"M2 full-loop Play Mode smoke failed: {message}";
            Debug.LogError(result);
            EndRun(false, result);
        }

        private static void EndRun(bool success, string message)
        {
            if (_purchasePad != null)
            {
                _purchasePad.Completed -= HandlePurchaseCompleted;
                _purchasePad.PaymentProcessed -= HandlePaymentProcessed;
            }

            if (_salePoint != null)
            {
                _salePoint.UnitSold -= HandleUnitSold;
            }

            if (_cashCollector != null)
            {
                _cashCollector.CollectionCompleted -= HandleCashCollectionCompleted;
            }

            if (_productionUpgrade != null)
            {
                _productionUpgrade.Applied -= HandleUpgradeApplied;
            }

            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(FinishPendingKey, true);
            SessionState.SetBool(SuccessKey, success);
            SessionState.SetString(ResultMessageKey, message);
            EditorApplication.update -= UpdateSmokeTest;
            _runtimeInitialized = false;

            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
            else
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
            }
        }

        private static void CompleteAfterPlayMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += CompleteAfterPlayMode;
                return;
            }

            if (!SessionState.GetBool(FinishPendingKey, false))
            {
                return;
            }

            bool commandLine = SessionState.GetBool(CommandLineKey, false);
            bool success = SessionState.GetBool(SuccessKey, false);
            string message = SessionState.GetString(ResultMessageKey, string.Empty);
            SessionState.SetBool(FinishPendingKey, false);
            SessionState.SetBool(CommandLineKey, false);
            SessionState.SetString(ResultMessageKey, string.Empty);

            if (commandLine)
            {
                if (!success && !string.IsNullOrEmpty(message))
                {
                    Debug.LogError(message);
                }

                EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}
