using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Progression;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Fast deterministic M10 progression tests. These exercise only the shared
    /// progression model and its serializable snapshot; they never enter Play Mode
    /// or touch the player's persistent-data directory.
    /// </summary>
    public static class LumberCampM10ProgressionTests
    {
        private static int _assertionCount;
        private static int _testCount;

        [MenuItem("Industry Tycoon/Prototype/Run M10 Progression Tests")]
        private static void RunFromMenu()
        {
            RunAllForValidator();
        }

        public static void RunFromCommandLine()
        {
            try
            {
                RunAllForValidator();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void RunAllForValidator()
        {
            _assertionCount = 0;
            _testCount = 0;

            Run("Canonical objective, contract, and achievement catalog", TestCatalog);
            Run("Successful and failed authoritative metric commits", TestMetricCommits);
            Run("Gameplay Cash sources and reward exclusion", TestCashSourcesAndRewardExclusion);
            Run("Once-only and re-entrant transition protection", TestOnceOnlyTransitions);
            Run("Reward re-entry drains progression to a fixed point", TestRewardReentryFixedPoint);
            Run("Ordered objective advancement through Lumber Camp", TestOrderedObjectives);
            Run("Numeric and already-satisfied future objectives", TestNumericAndFutureObjectives);
            Run("Non-retroactive contract baseline and exact completion", TestContractBaseline);
            Run("Contract failed claim, claim once, and next activation", TestContractClaimOnce);
            Run("Full predefined contract sequence and final state", TestContractSequence);
            Run("Achievement threshold, unlock once, reward once, and reload", TestAchievementThreshold);
            Run("Achievement reward retry remains exactly once", TestAchievementRewardRetry);
            Run("Multi-condition achievement", TestMultiConditionAchievement);
            Run("All achievement progress requirements", TestAllAchievementRequirements);
            Run("Progression snapshot and validator JSON round trip", TestSnapshotRoundTrip);
            Run("Progression validator normalization and rejection", TestValidatorBoundaries);

            Debug.Log(
                $"M10 deterministic progression tests PASS: {_testCount} tests, "
                + $"{_assertionCount} assertions.");
        }

        private static void TestCatalog()
        {
            Require(LumberCampProgressionCatalog.MetricCount == 17,
                "Metric catalog count changed unexpectedly.");
            Require(LumberCampProgressionCatalog.FlagCount == 8,
                "Flag catalog count changed unexpectedly.");
            Require(LumberCampProgressionCatalog.ObjectiveCount == 13,
                "Objective catalog must contain the corrected M10 and M11 sequence.");
            Require(LumberCampProgressionCatalog.ContractCount == 5,
                "Contract catalog must contain exactly the M10 sequence.");
            Require(LumberCampProgressionCatalog.AchievementCount == 15,
                "Achievement catalog must contain exactly the M10 set.");

            MainObjectiveId[] objectiveIds =
            {
                MainObjectiveId.UnlockWorker,
                MainObjectiveId.UnlockProcessor,
                MainObjectiveId.ProduceTenPlanks,
                MainObjectiveId.UnlockAutoFeeder,
                MainObjectiveId.UnlockPackingStation,
                MainObjectiveId.ProduceFiveCrates,
                MainObjectiveId.UnlockCourier,
                MainObjectiveId.CompleteLumberCamp,
                MainObjectiveId.CompleteFiveCourierDeliveries,
                MainObjectiveId.MineTenIronOre,
                MainObjectiveId.UnlockSmelter,
                MainObjectiveId.ProduceFiveIronBars,
                MainObjectiveId.UnlockAutomatedDrill
            };
            long[] objectiveTargets =
            {
                1L, 1L, 10L, 1L, 1L, 5L, 1L,
                1L, 5L, 10L, 1L, 5L, 1L
            };
            for (int i = 0; i < objectiveIds.Length; i++)
            {
                MainObjectiveDefinition definition =
                    LumberCampProgressionCatalog.GetObjective(i);
                Require(definition.Id == objectiveIds[i]
                        && definition.Target == objectiveTargets[i],
                    $"Objective {i} has the wrong identity or target.");
            }

            LumberCampContractId[] contractIds =
            {
                LumberCampContractId.SellTwentyWood,
                LumberCampContractId.ProduceFifteenPlanks,
                LumberCampContractId.SellTwentyPlanks,
                LumberCampContractId.ProduceTenCrates,
                LumberCampContractId.DeliverTenCrates
            };
            long[] contractTargets = { 20L, 15L, 20L, 10L, 10L };
            int[] contractRewards = { 150, 250, 300, 400, 600 };
            for (int i = 0; i < contractIds.Length; i++)
            {
                LumberCampContractDefinition definition =
                    LumberCampProgressionCatalog.GetContract(i);
                Require(definition.Id == contractIds[i]
                        && definition.Target == contractTargets[i]
                        && definition.RewardCash == contractRewards[i],
                    $"Contract {i} has the wrong identity, target, or reward.");
            }

            int[] achievementRewards =
            {
                50, 75, 100, 125, 150,
                200, 150, 150, 200, 250,
                250, 300, 500, 250, 500
            };
            for (int i = 0; i < LumberCampProgressionCatalog.AchievementCount; i++)
            {
                LumberCampAchievementDefinition definition =
                    LumberCampProgressionCatalog.GetAchievement(i);
                Require((int)definition.Id == i
                        && definition.RewardCash == achievementRewards[i]
                        && !string.IsNullOrEmpty(definition.StableId),
                    $"Achievement {i} has the wrong identity, stable ID, or reward.");
            }
        }

        private static void TestMetricCommits()
        {
            var rewards = new FakeRewardWallet { AcceptRewards = false };
            var model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                rewards.TryGrant);

            Require(model.RecordWoodProduced(3),
                "Successful Wood production was rejected.");
            Require(model.RecordPlayerCollection(ResourceType.Wood, 4),
                "Successful player Wood collection was rejected.");
            Require(model.RecordPlanksProduced(2),
                "Successful Plank production was rejected.");
            Require(model.RecordCratesProduced(1),
                "Successful Crate production was rejected.");
            Require(model.RecordFlag(ProgressFlagId.ProductionUpgradeUnlocked),
                "Successful unlock commit was rejected.");

            Require(model.GetMetric(ProgressMetricId.WoodProduced) == 3L,
                "Wood production did not increment exactly once.");
            Require(model.GetMetric(ProgressMetricId.WoodCollected) == 4L,
                "Wood collection did not increment exactly once.");
            Require(model.GetMetric(ProgressMetricId.PlanksProduced) == 2L,
                "Plank production did not increment exactly once.");
            Require(model.GetMetric(ProgressMetricId.CratesProduced) == 1L,
                "Crate production did not increment exactly once.");
            Require(model.GetFlag(ProgressFlagId.ProductionUpgradeUnlocked),
                "Unlock flag was not stored.");

            Require(!model.RecordWoodProduced(0)
                    && !model.RecordWoodProduced(-1)
                    && !model.RecordPlayerCollection(ResourceType.Wood, 0)
                    && !model.RecordPlayerCollection(ResourceType.Plank, 2)
                    && !model.RecordPlanksProduced(0)
                    && !model.RecordCratesProduced(-1),
                "A failed/non-authoritative metric input was accepted.");
            Require(!model.RecordSale(ResourceType.Wood, 0, 20)
                    && !model.RecordSale(ResourceType.Wood, 1, 0)
                    && !model.RecordCourierDelivery(0, 40)
                    && !model.RecordCourierDelivery(1, 0),
                "A failed Sale/Courier input was accepted.");
            Require(!model.RecordFlag(ProgressFlagId.ProductionUpgradeUnlocked),
                "A repeated once-only unlock changed state.");

            Require(model.GetMetric(ProgressMetricId.WoodProduced) == 3L
                    && model.GetMetric(ProgressMetricId.WoodCollected) == 4L
                    && model.GetMetric(ProgressMetricId.PlanksProduced) == 2L
                    && model.GetMetric(ProgressMetricId.CratesProduced) == 1L
                    && model.GetMetric(ProgressMetricId.TotalCashEarned) == 0L
                    && model.GetMetric(ProgressMetricId.WoodSold) == 0L
                    && model.GetMetric(ProgressMetricId.CourierTripsCompleted) == 0L,
                "A failed transaction mutated progression metrics.");
        }

        private static void TestCashSourcesAndRewardExclusion()
        {
            var achievementRewards = new FakeRewardWallet();
            var achievementModel = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                achievementRewards.TryGrant);

            Require(achievementModel.RecordSale(ResourceType.Plank, 3, 90),
                "Successful SalePoint commit was rejected.");
            Require(achievementModel.GetMetric(ProgressMetricId.PlanksSold) == 3L
                    && achievementModel.GetMetric(ProgressMetricId.TotalCashEarned) == 90L,
                "SalePoint did not record sold quantity and gameplay Cash.");
            Require(achievementRewards.CountGranted(50) == 1,
                "First Sale did not route its reward through the grant callback.");
            Require(achievementModel.GetMetric(ProgressMetricId.TotalCashEarned) == 90L,
                "Achievement Cash leaked into TotalCashEarned.");

            Require(achievementModel.RecordSale(ResourceType.Crate, 2, 80),
                "Successful Crate SalePoint commit was rejected.");
            Require(achievementModel.GetMetric(ProgressMetricId.CratesSold) == 2L
                    && achievementModel.GetMetric(ProgressMetricId.TotalCashEarned) == 170L
                    && achievementRewards.CountGranted(50) == 1,
                "Crate sales or the once-only First Sale reward were recorded incorrectly.");

            Require(achievementModel.RecordCourierDelivery(2, 80),
                "Successful Courier delivery commit was rejected.");
            Require(achievementModel.GetMetric(
                        ProgressMetricId.CourierTripsCompleted) == 1L
                    && achievementModel.GetMetric(ProgressMetricId.CratesDelivered) == 2L
                    && achievementModel.GetMetric(ProgressMetricId.TotalCashEarned) == 250L,
                "Courier did not record one trip, delivered Crates, and gameplay Cash.");

            achievementRewards.TryGrant(999);
            Require(achievementModel.GetMetric(ProgressMetricId.TotalCashEarned) == 250L,
                "An external/offline-style Wallet grant changed gameplay Cash metrics.");

            var contractRewards = new FakeRewardWallet();
            var contractModel = new LumberCampProgressionModel(
                CreateStateWithHandledFirstSale(),
                contractRewards.TryGrant);
            Require(contractModel.RecordSale(ResourceType.Wood, 20, 200),
                "Contract-driving sale was rejected.");
            long cashBeforeClaim = contractModel.GetMetric(
                ProgressMetricId.TotalCashEarned);
            Require(contractModel.TryClaimActiveContract(),
                "Completed first contract could not be claimed.");
            Require(contractRewards.TotalGranted == 150,
                "First contract did not grant exactly $150.");
            Require(contractModel.GetMetric(ProgressMetricId.TotalCashEarned)
                    == cashBeforeClaim,
                "Contract reward leaked into TotalCashEarned.");
        }

        private static void TestOnceOnlyTransitions()
        {
            LumberCampProgressionModel model = null;
            int rewardAttempts = 0;
            bool? reentryResult = null;
            Func<int, bool> reentrantGrant = amount =>
            {
                rewardAttempts++;
                reentryResult = model.EvaluateAll();
                return amount > 0;
            };

            model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                reentrantGrant);
            int unlockEvents = 0;
            model.AchievementUnlocked += achievementIndex =>
            {
                if (achievementIndex == (int)LumberCampAchievementId.FirstHire)
                {
                    unlockEvents++;
                }
            };

            Require(model.RecordFlag(ProgressFlagId.WorkerUnlocked),
                "Worker unlock commit was rejected.");
            Require(reentryResult == false,
                "A reward callback re-entered transition evaluation.");
            Require(rewardAttempts == 1
                    && unlockEvents == 1
                    && model.IsAchievementUnlocked(
                        (int)LumberCampAchievementId.FirstHire)
                    && model.IsAchievementRewarded(
                        (int)LumberCampAchievementId.FirstHire),
                "Re-entry duplicated the First Hire unlock or reward.");
            Require(!model.RecordFlag(ProgressFlagId.WorkerUnlocked)
                    && !model.EvaluateAll(),
                "Repeated completion/evaluation changed once-only state.");
            Require(rewardAttempts == 1 && unlockEvents == 1,
                "Repeated completion/evaluation duplicated a reward or toast event.");

            M10ProgressionSaveData snapshot = model.CapturePersistentState();
            var reloadRewards = new FakeRewardWallet();
            var reloaded = new LumberCampProgressionModel(
                snapshot,
                reloadRewards.TryGrant);
            Require(!reloaded.EvaluateAll()
                    && reloadRewards.GrantCount == 0
                    && reloaded.GetMetric(ProgressMetricId.TotalCashEarned) == 0L,
                "Restore/re-evaluation fabricated metrics or repeated a reward.");
        }

        private static void TestRewardReentryFixedPoint()
        {
            LumberCampProgressionModel model = null;
            int stateChangedCount = 0;
            Func<int, bool> grantAndUnlockWorker = amount =>
            {
                if (amount == 150)
                {
                    Require(model.RecordFlag(ProgressFlagId.WorkerUnlocked),
                        "Re-entrant PurchasePad completion did not store its flag.");
                }

                return amount > 0;
            };

            model = new LumberCampProgressionModel(
                CreateStateWithHandledFirstSale(),
                grantAndUnlockWorker);
            model.RecordSale(ResourceType.Wood, 20, 100);
            model.StateChanged += () => stateChangedCount++;
            Require(model.TryClaimActiveContract(),
                "Re-entrant contract reward claim failed.");
            Require(model.GetFlag(ProgressFlagId.WorkerUnlocked)
                    && model.ObjectiveIndex
                    == (int)MainObjectiveId.UnlockProcessor,
                "Reward-triggered unlock left the objective cursor stale.");
            Require(stateChangedCount == 1,
                "A re-entrant reward published duplicate/intermediate state changes.");
        }

        private static void TestOrderedObjectives()
        {
            var model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                amount => amount > 0);

            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockWorker),
                "Fresh progression did not start at Unlock Worker.");
            Require(model.BuildObjectiveDisplayText() == "OBJECTIVE: UNLOCK WORKER",
                "Fresh objective HUD text is wrong.");

            model.RecordFlag(ProgressFlagId.WorkerUnlocked);
            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockProcessor),
                "Worker unlock did not advance to Unlock Processor.");
            model.RecordFlag(ProgressFlagId.ProcessorUnlocked);
            Require(CurrentObjectiveIs(model, MainObjectiveId.ProduceTenPlanks),
                "Processor unlock did not advance to Produce 10 Planks.");
            model.RecordPlanksProduced(10);
            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockAutoFeeder),
                "Ten Planks did not advance to Unlock Auto Feeder.");
            model.RecordFlag(ProgressFlagId.AutoFeederUnlocked);
            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockPackingStation),
                "Auto Feeder unlock did not advance to Unlock Packing Station.");
            model.RecordFlag(ProgressFlagId.PackingStationUnlocked);
            Require(CurrentObjectiveIs(model, MainObjectiveId.ProduceFiveCrates),
                "Packing Station unlock did not advance to Produce 5 Crates.");
            model.RecordCratesProduced(5);
            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockCourier),
                "Five Crates did not advance to Unlock Courier.");
            model.RecordFlag(ProgressFlagId.CourierUnlocked);
            Require(CurrentObjectiveIs(model, MainObjectiveId.CompleteLumberCamp),
                "Courier unlock did not advance to Complete Lumber Camp.");
            model.RecordCourierDelivery(1, 40);
            Require(CurrentObjectiveIs(model, MainObjectiveId.CompleteLumberCamp),
                "A Courier trip bypassed the authoritative Lumber Camp flag.");
            model.RecordFlag(ProgressFlagId.LumberCampCompleted);
            Require(CurrentObjectiveIs(
                    model,
                    MainObjectiveId.CompleteFiveCourierDeliveries),
                "Lumber Camp completion did not advance to five Courier deliveries.");
            model.GetObjectiveProgress(out long courierCurrent, out long courierTarget);
            Require(courierCurrent == 1L && courierTarget == 5L,
                "The first completion trip was not retained by the reordered objective.");
            for (int i = 0; i < 4; i++)
            {
                model.RecordCourierDelivery(1, 40);
            }

            Require(CurrentObjectiveIs(model, MainObjectiveId.MineTenIronOre),
                "Five Courier trips did not advance to manual Mining.");
            Require(model.RecordMineUnlocked() && !model.RecordMineUnlocked(),
                "Mine unlock metric was not binary and once-only.");
            model.RecordIronOreMined(10);
            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockSmelter),
                "Ten manually mined Ore did not advance to Unlock Smelter.");
            model.RecordFlag(ProgressFlagId.SmelterUnlocked);
            Require(CurrentObjectiveIs(model, MainObjectiveId.ProduceFiveIronBars),
                "Smelter unlock did not advance to Produce Iron Bars.");
            model.RecordIronBarsProduced(5);
            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockAutomatedDrill),
                "Five Iron Bars did not advance to Unlock Automated Drill.");
            Require(model.RecordDrillUnlocked() && !model.RecordDrillUnlocked(),
                "Drill unlock metric was not binary and once-only.");
            Require(model.AreAllObjectivesCompleted
                    && model.ObjectiveIndex
                    == LumberCampProgressionCatalog.ObjectiveCount,
                "Automated Drill unlock did not finish the objective sequence.");
            Require(model.BuildObjectiveDisplayText()
                    == "OBJECTIVE: MINING COMPLETE",
                "Completed objective HUD text is wrong.");
            model.GetObjectiveProgress(out long current, out long target);
            Require(current == 1L && target == 1L,
                "Completed objective progress is not stable.");
        }

        private static void TestNumericAndFutureObjectives()
        {
            var numericModel = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                amount => amount > 0);
            numericModel.RecordFlag(ProgressFlagId.WorkerUnlocked);
            numericModel.RecordFlag(ProgressFlagId.ProcessorUnlocked);
            numericModel.RecordPlanksProduced(6);
            numericModel.GetObjectiveProgress(out long current, out long target);
            Require(current == 6L && target == 10L,
                "Produce Planks objective did not expose 6 / 10 progress.");
            Require(numericModel.BuildObjectiveDisplayText()
                    == "OBJECTIVE: PRODUCE PLANKS — 6 / 10",
                "Numeric objective HUD text is wrong.");
            numericModel.RecordPlanksProduced(4);
            Require(CurrentObjectiveIs(numericModel, MainObjectiveId.UnlockAutoFeeder),
                "Exact numeric target did not advance once.");

            var futureModel = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                amount => amount > 0);
            futureModel.RecordPlanksProduced(12);
            futureModel.RecordFlag(ProgressFlagId.AutoFeederUnlocked);
            futureModel.RecordFlag(ProgressFlagId.PackingStationUnlocked);
            futureModel.RecordCratesProduced(7);
            futureModel.RecordFlag(ProgressFlagId.CourierUnlocked);
            for (int i = 0; i < 6; i++)
            {
                futureModel.RecordCourierDelivery(1, 40);
            }

            futureModel.RecordFlag(ProgressFlagId.LumberCampCompleted);
            futureModel.RecordIronOreMined(10);
            futureModel.RecordFlag(ProgressFlagId.SmelterUnlocked);
            futureModel.RecordIronBarsProduced(5);
            futureModel.RecordDrillUnlocked();
            Require(CurrentObjectiveIs(futureModel, MainObjectiveId.UnlockWorker),
                "Future progress skipped an earlier unsatisfied objective.");
            futureModel.RecordFlag(ProgressFlagId.WorkerUnlocked);
            Require(CurrentObjectiveIs(futureModel, MainObjectiveId.UnlockProcessor),
                "Ordered progression skipped Unlock Processor.");
            futureModel.RecordFlag(ProgressFlagId.ProcessorUnlocked);
            Require(futureModel.AreAllObjectivesCompleted,
                "Already-satisfied lifetime metrics/flags forced repeated work.");
        }

        private static void TestContractBaseline()
        {
            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            SetMetric(state, ProgressMetricId.WoodSold, 40L);
            state.activeContractBaseline = 40L;
            var rewards = new FakeRewardWallet();
            var model = new LumberCampProgressionModel(state, rewards.TryGrant);

            model.GetActiveContractProgress(out long current, out long target);
            Require(current == 0L && target == 20L,
                "Historical Wood sales before activation counted toward the contract.");
            model.RecordSale(ResourceType.Wood, 7, 70);
            model.GetActiveContractProgress(out current, out target);
            Require(current == 7L
                    && target == 20L
                    && model.ActiveContractState == ContractProgressState.Active,
                "Partial non-retroactive contract progress is wrong.");

            model.RecordPlanksProduced(25);
            model.RecordSale(ResourceType.Wood, 13, 130);
            model.GetActiveContractProgress(out current, out target);
            Require(current == 20L
                    && target == 20L
                    && model.ActiveContractState
                    == ContractProgressState.CompletedUnclaimed,
                "Exact contract target did not complete exactly once.");
            Require(model.TryClaimActiveContract(),
                "Exactly completed contract could not be claimed.");
            model.GetActiveContractProgress(out current, out target);
            M10ProgressionSaveData captured = model.CapturePersistentState();
            Require(model.ActiveContractIndex
                    == (int)LumberCampContractId.ProduceFifteenPlanks
                    && current == 0L
                    && target == 15L
                    && captured.activeContractBaseline == 25L,
                "Next contract did not capture its activation baseline.");
        }

        private static void TestContractClaimOnce()
        {
            var rewards = new FakeRewardWallet { AcceptRewards = false };
            var model = new LumberCampProgressionModel(
                CreateStateWithHandledFirstSale(),
                rewards.TryGrant);
            model.RecordPlanksProduced(8);
            model.RecordSale(ResourceType.Wood, 20, 200);
            Require(model.ActiveContractState
                    == ContractProgressState.CompletedUnclaimed,
                "First contract did not enter CompletedUnclaimed.");

            Require(!model.TryClaimActiveContract(),
                "A failed Wallet grant claimed the contract.");
            Require(model.ActiveContractIndex == 0
                    && !model.IsContractClaimed(0)
                    && model.ActiveContractState
                    == ContractProgressState.CompletedUnclaimed
                    && rewards.GrantCount == 0,
                "Failed claim mutated contract state or credited Cash.");

            rewards.AcceptRewards = true;
            long gameplayCashBefore = model.GetMetric(
                ProgressMetricId.TotalCashEarned);
            Require(model.TryClaimActiveContract(),
                "Retry after Wallet availability did not claim the contract.");
            Require(model.IsContractClaimed(0)
                    && model.ActiveContractIndex == 1
                    && rewards.GrantCount == 1
                    && rewards.TotalGranted == 150,
                "Successful contract claim did not reward/advance exactly once.");
            Require(model.GetMetric(ProgressMetricId.TotalCashEarned)
                    == gameplayCashBefore,
                "Contract claim changed gameplay Cash metrics.");

            int attemptsAfterSuccess = rewards.AttemptCount;
            Require(!model.TryClaimActiveContract()
                    && rewards.AttemptCount == attemptsAfterSuccess
                    && rewards.GrantCount == 1,
                "A second claim duplicated the first contract reward.");
            model.GetActiveContractProgress(out long current, out long target);
            Require(current == 0L && target == 15L,
                "Historical Plank production counted after next-contract activation.");
        }

        private static void TestContractSequence()
        {
            var rewards = new FakeRewardWallet();
            var model = new LumberCampProgressionModel(
                CreateStateWithHandledFirstSale(includePackedAndReady: true),
                rewards.TryGrant);

            model.RecordSale(ResourceType.Wood, 20, 200);
            Require(model.TryClaimActiveContract(),
                "Sell 20 Wood contract claim failed.");
            model.RecordPlanksProduced(15);
            Require(model.TryClaimActiveContract(),
                "Produce 15 Planks contract claim failed.");
            model.RecordSale(ResourceType.Plank, 20, 400);
            Require(model.TryClaimActiveContract(),
                "Sell 20 Planks contract claim failed.");
            model.RecordCratesProduced(10);
            Require(model.TryClaimActiveContract(),
                "Produce 10 Crates contract claim failed.");
            model.RecordCourierDelivery(4, 160);
            model.GetActiveContractProgress(out long current, out long target);
            Require(current == 4L
                    && target == 10L
                    && model.ActiveContractState == ContractProgressState.Active,
                "Deliver 10 Crates partial progress is wrong.");
            model.RecordCourierDelivery(6, 240);
            Require(model.ActiveContractState
                    == ContractProgressState.CompletedUnclaimed,
                "Deliver 10 Crates did not complete at the exact target.");
            Require(model.TryClaimActiveContract(),
                "Final contract claim failed.");

            Require(!model.HasActiveContract
                    && model.ActiveContractIndex
                    == LumberCampProgressionCatalog.ContractCount
                    && model.ActiveContractState == ContractProgressState.Claimed,
                "Final claim did not produce a clean no-active-contract state.");
            for (int i = 0; i < LumberCampProgressionCatalog.ContractCount; i++)
            {
                Require(model.IsContractClaimed(i),
                    $"Contract {i} was not persisted as claimed.");
            }

            Require(rewards.GrantCount == 5 && rewards.TotalGranted == 1700,
                "Predefined contracts did not grant exactly $1,700 once.");
            Require(model.GetMetric(ProgressMetricId.TotalCashEarned) == 1000L,
                "Contract rewards polluted gameplay-generated Cash.");
            int attemptsBeforeDuplicate = rewards.AttemptCount;
            Require(!model.TryClaimActiveContract()
                    && rewards.AttemptCount == attemptsBeforeDuplicate,
                "Final contract could be claimed twice.");
            model.GetActiveContractProgress(out current, out target);
            Require(current == 0L && target == 0L,
                "No-active-contract progress did not clear cleanly.");
        }

        private static void TestAchievementThreshold()
        {
            var rewards = new FakeRewardWallet();
            var unlocked = new List<int>();
            var model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                rewards.TryGrant);
            model.AchievementUnlocked += unlocked.Add;
            int achievementIndex = (int)LumberCampAchievementId.Lumberjack;

            model.GetAchievementProgress(
                achievementIndex,
                out long current,
                out long target);
            Require(current == 0L && target == 100L,
                "Lumberjack initial threshold progress is wrong.");
            model.RecordPlayerCollection(ResourceType.Wood, 99);
            model.GetAchievementProgress(achievementIndex, out current, out target);
            Require(current == 99L
                    && target == 100L
                    && !model.IsAchievementUnlocked(achievementIndex),
                "Lumberjack unlocked before its threshold.");

            model.RecordPlayerCollection(ResourceType.Wood, 1);
            Require(model.IsAchievementUnlocked(achievementIndex)
                    && model.IsAchievementRewarded(achievementIndex)
                    && Count(unlocked, achievementIndex) == 1
                    && rewards.CountGranted(150) == 1,
                "Lumberjack did not unlock, toast, and reward exactly once.");
            model.RecordPlayerCollection(ResourceType.Wood, 10);
            model.EvaluateAll();
            model.GetAchievementProgress(achievementIndex, out current, out target);
            Require(current == 100L
                    && target == 100L
                    && Count(unlocked, achievementIndex) == 1
                    && rewards.CountGranted(150) == 1,
                "Post-threshold progress duplicated Lumberjack or exceeded UI target.");

            M10ProgressionSaveData saved = model.CapturePersistentState();
            var reloadRewards = new FakeRewardWallet();
            var reloadedEvents = new List<int>();
            var reloaded = new LumberCampProgressionModel(
                saved,
                reloadRewards.TryGrant);
            reloaded.AchievementUnlocked += reloadedEvents.Add;
            Require(!reloaded.EvaluateAll()
                    && reloadRewards.GrantCount == 0
                    && reloadedEvents.Count == 0,
                "Reload retriggered an achievement reward or toast.");
        }

        private static void TestAchievementRewardRetry()
        {
            var rewards = new FakeRewardWallet { AcceptRewards = false };
            var unlocked = new List<int>();
            var model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                rewards.TryGrant);
            model.AchievementUnlocked += unlocked.Add;
            int achievementIndex = (int)LumberCampAchievementId.MassProduction;

            model.RecordWoodProduced(100);
            Require(model.IsAchievementUnlocked(achievementIndex)
                    && !model.IsAchievementRewarded(achievementIndex)
                    && Count(unlocked, achievementIndex) == 1
                    && rewards.GrantCount == 0,
                "Failed achievement reward did not retain unlocked/unrewarded state.");
            Require(!model.EvaluateAll(),
                "A rejected reward incorrectly reported a persistent change.");
            Require(Count(unlocked, achievementIndex) == 1,
                "Reward retry duplicated the achievement toast.");

            rewards.AcceptRewards = true;
            Require(model.EvaluateAll(),
                "Unlocked achievement reward did not retry when Wallet became available.");
            Require(model.IsAchievementRewarded(achievementIndex)
                    && rewards.GrantCount == 1
                    && rewards.TotalGranted == 150,
                "Achievement retry did not grant exactly one reward.");
            Require(!model.EvaluateAll()
                    && rewards.GrantCount == 1
                    && Count(unlocked, achievementIndex) == 1,
                "Rewarded achievement retriggered on later evaluation.");
        }

        private static void TestMultiConditionAchievement()
        {
            var rewards = new FakeRewardWallet();
            var unlocked = new List<int>();
            var model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                rewards.TryGrant);
            model.AchievementUnlocked += unlocked.Add;
            int achievementIndex =
                (int)LumberCampAchievementId.FullyAutomatedInput;

            model.RecordFlag(ProgressFlagId.WorkerUnlocked);
            model.GetAchievementProgress(
                achievementIndex,
                out long current,
                out long target);
            Require(current == 1L
                    && target == 2L
                    && !model.IsAchievementUnlocked(achievementIndex),
                "Fully Automated Input unlocked with only Worker.");

            model.RecordFlag(ProgressFlagId.AutoFeederUnlocked);
            model.GetAchievementProgress(achievementIndex, out current, out target);
            Require(current == 2L
                    && target == 2L
                    && model.IsAchievementUnlocked(achievementIndex)
                    && model.IsAchievementRewarded(achievementIndex),
                "Worker + Auto Feeder did not unlock Fully Automated Input.");
            Require(Count(unlocked, achievementIndex) == 1
                    && rewards.CountGranted(250) == 1,
                "Multi-condition achievement did not toast/reward exactly once.");

            int grantsBeforeRepeat = rewards.GrantCount;
            Require(!model.RecordFlag(ProgressFlagId.WorkerUnlocked)
                    && !model.RecordFlag(ProgressFlagId.AutoFeederUnlocked)
                    && !model.EvaluateAll()
                    && rewards.GrantCount == grantsBeforeRepeat
                    && Count(unlocked, achievementIndex) == 1,
                "Repeated multi-condition flags duplicated its transition.");
        }

        private static void TestAllAchievementRequirements()
        {
            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            SetMetric(state, ProgressMetricId.WoodSold, 2L);
            SetMetric(state, ProgressMetricId.PlanksSold, 3L);
            SetMetric(state, ProgressMetricId.CratesSold, 4L);
            SetMetric(state, ProgressMetricId.WoodCollected, 11L);
            SetMetric(state, ProgressMetricId.WoodProduced, 12L);
            SetMetric(state, ProgressMetricId.PlanksProduced, 13L);
            SetMetric(state, ProgressMetricId.CratesProduced, 14L);
            SetMetric(state, ProgressMetricId.CourierTripsCompleted, 15L);
            SetMetric(state, ProgressMetricId.TotalCashEarned, 16L);
            SetFlag(state, ProgressFlagId.WorkerUnlocked);
            SetFlag(state, ProgressFlagId.ProcessorUnlocked);
            SetFlag(state, ProgressFlagId.AutoFeederUnlocked);
            SetFlag(state, ProgressFlagId.CourierUnlocked);
            SetFlag(state, ProgressFlagId.LumberCampCompleted);

            long[] expectedCurrent =
            {
                9L, 1L, 1L, 1L, 14L,
                1L, 11L, 12L, 13L, 14L,
                15L, 16L, 16L, 2L, 1L
            };
            long[] expectedTarget =
            {
                1L, 1L, 1L, 1L, 1L,
                1L, 100L, 100L, 50L, 25L,
                10L, 2500L, 10000L, 2L, 1L
            };
            for (int i = 0; i < LumberCampProgressionCatalog.AchievementCount; i++)
            {
                M10ProgressionRules.GetAchievementProgress(
                    state,
                    i,
                    out long current,
                    out long target);
                Require(current == expectedCurrent[i]
                        && target == expectedTarget[i],
                    $"Achievement {i} uses the wrong shared metric/flag requirement.");
            }
        }

        private static void TestSnapshotRoundTrip()
        {
            var rewards = new FakeRewardWallet();
            var model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                rewards.TryGrant);
            model.RecordWoodProduced(12);
            model.RecordSale(ResourceType.Wood, 5, 50);
            model.RecordFlag(ProgressFlagId.WorkerUnlocked);

            M10ProgressionSaveData detached = model.CapturePersistentState();
            SetMetric(detached, ProgressMetricId.WoodProduced, 999L);
            Require(model.GetMetric(ProgressMetricId.WoodProduced) == 12L,
                "Captured progression snapshot was not a deep clone.");

            M10ProgressionSaveData source = model.CapturePersistentState();
            string json = JsonUtility.ToJson(source);
            M10ProgressionSaveData decoded =
                JsonUtility.FromJson<M10ProgressionSaveData>(json);
            Require(decoded != null && !string.IsNullOrEmpty(json),
                "Progression snapshot did not serialize/deserialize.");
            Require(M10ProgressionSaveValidator.TryNormalize(
                    decoded,
                    out M10ProgressionSaveData normalized,
                    out string failure),
                "Round-tripped progression snapshot was rejected: " + failure);
            Require(normalized.GetMetric(ProgressMetricId.WoodProduced) == 12L
                    && normalized.GetMetric(ProgressMetricId.WoodSold) == 5L
                    && normalized.GetMetric(ProgressMetricId.TotalCashEarned) == 50L
                    && normalized.GetFlag(ProgressFlagId.WorkerUnlocked)
                    && normalized.objectiveIndex
                    == (int)MainObjectiveId.UnlockProcessor,
                "Snapshot round trip changed metrics, flags, or objective state.");

            var reloadRewards = new FakeRewardWallet();
            var restored = new LumberCampProgressionModel(
                normalized,
                reloadRewards.TryGrant);
            restored.GetActiveContractProgress(out long current, out long target);
            Require(restored.GetMetric(ProgressMetricId.WoodProduced) == 12L
                    && restored.GetMetric(ProgressMetricId.TotalCashEarned) == 50L
                    && current == 5L
                    && target == 20L
                    && reloadRewards.GrantCount == 0,
                "Model restore changed state or granted fake load rewards.");
        }

        private static void TestValidatorBoundaries()
        {
            M10ProgressionSaveData reorder =
                M10ProgressionSaveData.CreateFresh();
            SetMetric(reorder, ProgressMetricId.WoodProduced, 17L);
            SetMetric(reorder, ProgressMetricId.WoodSold, 4L);
            SetFlag(reorder, ProgressFlagId.WorkerUnlocked);
            reorder.objectiveIndex = 999;
            reorder.activeContractBaseline = 99L;
            Array.Reverse(reorder.metrics);
            Array.Reverse(reorder.flags);
            Array.Reverse(reorder.achievements);
            Require(M10ProgressionSaveValidator.TryNormalize(
                    reorder,
                    out M10ProgressionSaveData normalized,
                    out string failure),
                "Stable-ID reordered snapshot was rejected: " + failure);
            Require(normalized.GetMetric(ProgressMetricId.WoodProduced) == 17L
                    && normalized.GetMetric(ProgressMetricId.WoodSold) == 4L
                    && normalized.GetFlag(ProgressFlagId.WorkerUnlocked),
                "Stable-ID normalization mapped progression records incorrectly.");
            Require(normalized.objectiveIndex
                    == (int)MainObjectiveId.UnlockProcessor,
                "Validator trusted a duplicated objective cursor instead of metrics/flags.");
            Require(normalized.activeContractBaseline == 4L
                    && normalized.activeContractState == ContractProgressState.Active,
                "Validator did not clamp the active baseline to its lifetime metric.");

            M10ProgressionSaveData negative =
                M10ProgressionSaveData.CreateFresh();
            SetMetric(negative, ProgressMetricId.WoodCollected, -5L);
            Require(M10ProgressionSaveValidator.TryNormalize(
                    negative,
                    out M10ProgressionSaveData nonNegative,
                    out failure)
                    && nonNegative.GetMetric(ProgressMetricId.WoodCollected) == 0L,
                "Validator did not deterministically clamp a negative metric.");

            M10ProgressionSaveData binary = M10ProgressionSaveData.CreateFresh();
            SetMetric(binary, ProgressMetricId.MineUnlocked, 100L);
            SetMetric(binary, ProgressMetricId.DrillUnlocked, -10L);
            Require(M10ProgressionSaveValidator.TryNormalize(
                        binary,
                        out M10ProgressionSaveData normalizedBinary,
                        out failure)
                    && normalizedBinary.GetMetric(
                        ProgressMetricId.MineUnlocked) == 1L
                    && normalizedBinary.GetMetric(
                        ProgressMetricId.DrillUnlocked) == 0L,
                "Binary Mining unlock metrics were not normalized to 0/1.");

            M10ProgressionSaveData duplicate = M10ProgressionSaveData.CreateFresh();
            duplicate.metrics[1].id = duplicate.metrics[0].id;
            Require(!M10ProgressionSaveValidator.TryNormalize(
                    duplicate,
                    out _,
                    out failure)
                    && !string.IsNullOrEmpty(failure),
                "Validator accepted duplicate metric stable IDs.");

            M10ProgressionSaveData invalidClaims =
                M10ProgressionSaveData.CreateFresh();
            invalidClaims.claimedContracts = new bool[1];
            Require(!M10ProgressionSaveValidator.TryNormalize(
                    invalidClaims,
                    out _,
                    out failure)
                    && !string.IsNullOrEmpty(failure),
                "Validator accepted a malformed contract claim array.");

            M10ProgressionSaveData nonPrefixClaims =
                M10ProgressionSaveData.CreateFresh();
            nonPrefixClaims.claimedContracts[1] = true;
            Require(!M10ProgressionSaveValidator.TryNormalize(
                    nonPrefixClaims,
                    out _,
                    out failure)
                    && !string.IsNullOrEmpty(failure),
                "Validator erased a non-prefix once-only contract claim.");

            M10ProgressionSaveData contradictoryIndex =
                M10ProgressionSaveData.CreateFresh();
            contradictoryIndex.activeContractIndex = 1;
            Require(!M10ProgressionSaveValidator.TryNormalize(
                    contradictoryIndex,
                    out _,
                    out failure)
                    && !string.IsNullOrEmpty(failure),
                "Validator accepted a contract index that contradicted claim flags.");

            M10ProgressionSaveData rewardedButLocked =
                M10ProgressionSaveData.CreateFresh();
            rewardedButLocked.achievements[0].rewarded = true;
            Require(!M10ProgressionSaveValidator.TryNormalize(
                    rewardedButLocked,
                    out _,
                    out failure)
                    && !string.IsNullOrEmpty(failure),
                "Validator erased a rewarded once-only achievement bit.");

            M10ProgressionSaveData unlockedWithoutProgress =
                M10ProgressionSaveData.CreateFresh();
            unlockedWithoutProgress.achievements[
                (int)LumberCampAchievementId.Lumberjack].unlocked = true;
            Require(!M10ProgressionSaveValidator.TryNormalize(
                    unlockedWithoutProgress,
                    out _,
                    out failure)
                    && !string.IsNullOrEmpty(failure),
                "Validator accepted an unlocked achievement without its metric.");
        }

        private static M10ProgressionSaveData CreateStateWithHandledFirstSale(
            bool includePackedAndReady = false)
        {
            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            SetMetric(state, ProgressMetricId.WoodSold, 1L);
            state.activeContractBaseline = 1L;
            M10AchievementSaveRecord firstSale = state.FindAchievementRecord(
                (int)LumberCampAchievementId.FirstSale);
            firstSale.unlocked = true;
            firstSale.rewarded = true;
            if (includePackedAndReady)
            {
                SetMetric(state, ProgressMetricId.CratesProduced, 1L);
                M10AchievementSaveRecord packed = state.FindAchievementRecord(
                    (int)LumberCampAchievementId.PackedAndReady);
                packed.unlocked = true;
                packed.rewarded = true;
            }

            return state;
        }

        private static void SetMetric(
            M10ProgressionSaveData state,
            ProgressMetricId metric,
            long value)
        {
            state.metrics[(int)metric].value = value;
        }

        private static void SetFlag(
            M10ProgressionSaveData state,
            ProgressFlagId flag)
        {
            state.flags[(int)flag].value = true;
        }

        private static bool CurrentObjectiveIs(
            LumberCampProgressionModel model,
            MainObjectiveId objectiveId)
        {
            return model != null
                   && !model.AreAllObjectivesCompleted
                   && LumberCampProgressionCatalog
                       .GetObjective(model.ObjectiveIndex)
                       .Id == objectiveId;
        }

        private static int Count(List<int> values, int expected)
        {
            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == expected)
                {
                    count++;
                }
            }

            return count;
        }

        private static void Run(string name, Action test)
        {
            test();
            _testCount++;
            Debug.Log($"M10 deterministic test PASS: {name}");
        }

        private static void Require(bool condition, string message)
        {
            _assertionCount++;
            if (!condition)
            {
                throw new InvalidOperationException(
                    "M10 deterministic test failed: " + message);
            }
        }

        private sealed class FakeRewardWallet
        {
            private readonly List<int> _grants = new List<int>();

            public bool AcceptRewards { get; set; } = true;
            public int AttemptCount { get; private set; }
            public int GrantCount => _grants.Count;
            public int TotalGranted { get; private set; }

            public bool TryGrant(int amount)
            {
                AttemptCount++;
                if (!AcceptRewards || amount <= 0)
                {
                    return false;
                }

                _grants.Add(amount);
                TotalGranted += amount;
                return true;
            }

            public int CountGranted(int amount)
            {
                int count = 0;
                for (int i = 0; i < _grants.Count; i++)
                {
                    if (_grants[i] == amount)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }
}
