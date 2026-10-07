using System;
using System.Collections.Generic;
using System.IO;
using IndustryTycoon.Core;
using IndustryTycoon.Interaction;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Fast deterministic coverage for M12's cross-industry transaction,
    /// progression, persistence migration, and offline settlement contracts.
    /// </summary>
    public static class LumberCampM12Tests
    {
        private const long BaselineUtc = 2000000000L;
        private const int Version3MetricCount = 17;
        private const int Version3FlagCount = 8;
        private const int Version3ContractCount = 5;
        private const int Version3AchievementCount = 15;

        // Literal v3 IDs and values are deliberately frozen here. Never source these
        // from the live catalog: this fixture must fail if a persisted identifier is
        // renamed or reordered by a later milestone.
        private static readonly string[] Version3MetricStableIds =
        {
            "total_cash_earned",
            "wood_collected",
            "wood_produced",
            "wood_sold",
            "planks_produced",
            "planks_sold",
            "crates_produced",
            "crates_sold",
            "courier_trips_completed",
            "crates_delivered",
            "iron_ore_mined",
            "iron_ore_produced",
            "iron_ore_sold",
            "iron_bars_produced",
            "iron_bars_sold",
            "mine_unlocked",
            "drill_unlocked"
        };

        private static readonly long[] Version3MetricValues =
        {
            12000L,
            100L,
            100L,
            20L,
            50L,
            20L,
            25L,
            1L,
            10L,
            10L,
            10L,
            7L,
            3L,
            5L,
            2L,
            1L,
            1L
        };

        private static readonly string[] Version3FlagStableIds =
        {
            "production_upgrade_unlocked",
            "worker_unlocked",
            "processor_unlocked",
            "auto_feeder_unlocked",
            "packing_station_unlocked",
            "courier_unlocked",
            "lumber_camp_completed",
            "smelter_unlocked"
        };

        private static readonly string[] Version3AchievementStableIds =
        {
            "first_sale",
            "first_hire",
            "processing_begins",
            "automation_online",
            "packed_and_ready",
            "delivery_service",
            "lumberjack",
            "mass_production",
            "plank_factory",
            "crate_maker",
            "on_the_road",
            "merchant",
            "tycoon_in_training",
            "fully_automated_input",
            "lumber_camp_complete"
        };

        private static int _testCount;
        private static int _assertionCount;

        [MenuItem("Industry Tycoon/Prototype/Run M12 Cross-Industry Tests")]
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
            _testCount = 0;
            _assertionCount = 0;

            Run("M12 catalog, recipe, capacities, and Tool value", TestCatalog);
            Run("Multi-input recipe atomicity and ownership", TestRecipeTransaction);
            Run("CarryStack Tool type isolation", TestCarryToolIsolation);
            Run("Empire levels advance once and restore silently", TestEmpireLevels);
            Run("Tool metrics, objectives, and achievements", TestToolProgression);
            Run("Tool contracts retain non-retroactive baselines", TestToolContracts);
            Run("Frozen schema-v3 migrates losslessly to v4", TestVersion3Migration);
            Run("Schema-v4 Workshop partial, round-trip, and reset", TestVersion4WorkshopPersistence);
            Run("Impossible Workshop unlock state is rejected", TestImpossibleWorkshopState);
            Run("Offline Workshop uses only preloaded buffers", TestOfflineWorkshop);
            Run("Offline Workshop does not inflate Tool lifetime metrics", TestOfflineMetricIsolation);

            Debug.Log(
                $"M12 deterministic tests PASS: {_testCount} tests, "
                + $"{_assertionCount} assertions.");
        }

        private static void TestCatalog()
        {
            Require(M9SaveSchema.CurrentVersion >= M9SaveSchema.Version4,
                "Current schema must preserve v4 migration support.");
            Require((int)ResourceType.Tool == 5,
                "Tool must remain append-only after M11 resources.");
            Require(LumberCampProgressionCatalog.MetricCount >= 20
                    && LumberCampProgressionCatalog.FlagCount == 8
                    && LumberCampProgressionCatalog.ObjectiveCount == 16
                    && LumberCampProgressionCatalog.ContractCount >= 7
                    && LumberCampProgressionCatalog.AchievementCount == 19,
                "M12 progression catalog sizes are not canonical.");
            Require(string.Equals(
                        LumberCampProgressionCatalog.GetMetricStableId(
                            ProgressMetricId.ToolsProduced),
                        "tools_produced",
                        StringComparison.Ordinal)
                    && string.Equals(
                        LumberCampProgressionCatalog.GetMetricStableId(
                            ProgressMetricId.ToolsSold),
                        "tools_sold",
                        StringComparison.Ordinal)
                    && string.Equals(
                        LumberCampProgressionCatalog.GetMetricStableId(
                            ProgressMetricId.ToolWorkshopUnlocked),
                        "tool_workshop_unlocked",
                        StringComparison.Ordinal),
                "Tool metric stable IDs changed.");
            Require(M12WorkshopPurchasePadIds.ToolWorkshopCost == 3200
                    && string.Equals(
                        M12WorkshopPurchasePadIds.ToolWorkshop,
                        "tool_workshop",
                        StringComparison.Ordinal),
                "Tool Workshop purchase identity or cost changed.");

            MainObjectiveId[] objectiveIds =
            {
                MainObjectiveId.UnlockToolWorkshop,
                MainObjectiveId.ProduceFiveTools,
                MainObjectiveId.ReachEmpireLevelFive
            };
            long[] objectiveTargets = { 1L, 5L, 5L };
            for (int i = 0; i < objectiveIds.Length; i++)
            {
                MainObjectiveDefinition definition =
                    LumberCampProgressionCatalog.GetObjective(13 + i);
                Require(definition.Id == objectiveIds[i]
                        && definition.Target == objectiveTargets[i],
                    $"M12 objective {i} is out of order or has the wrong target.");
            }

            LumberCampContractDefinition produceContract =
                LumberCampProgressionCatalog.GetContract(
                    (int)LumberCampContractId.ProduceTenTools);
            LumberCampContractDefinition sellContract =
                LumberCampProgressionCatalog.GetContract(
                    (int)LumberCampContractId.SellFifteenTools);
            Require(produceContract.Metric == ProgressMetricId.ToolsProduced
                    && produceContract.Target == 10L
                    && produceContract.RewardCash == 800
                    && sellContract.Metric == ProgressMetricId.ToolsSold
                    && sellContract.Target == 15L
                    && sellContract.RewardCash == 1200,
                "M12 Tool contracts have incorrect targets or rewards.");

            GameObject fixture = new GameObject("M12 Catalog Fixture");
            try
            {
                fixture.AddComponent<BoxCollider>();
                ToolWorkshop workshop = fixture.AddComponent<ToolWorkshop>();
                SalePoint salePoint = fixture.AddComponent<SalePoint>();
                Require(workshop.PlankInputCapacity == 24
                        && workshop.IronBarInputCapacity == 12
                        && workshop.OutputCapacity == 12,
                    "Tool Workshop buffer capacities changed.");
                Require(workshop.RecipeInputPlanks == 2
                        && workshop.RecipeInputIronBars == 1
                        && workshop.RecipeOutputTools == 1
                        && Math.Abs(workshop.ProcessingDuration - 2f) < 0.0001f,
                    "Tool Workshop recipe or duration changed.");
                Require(salePoint.ToolValue == 100
                        && salePoint.GetUnitValue(ResourceType.Tool) == 100,
                    "Tool must sell for exactly $100.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        private static void TestRecipeTransaction()
        {
            var transaction = new BufferedRecipeTransaction(
                new[] { 2, 1 },
                1);

            int[] insufficientPlanks = { 1, 1 };
            Require(!transaction.TryBegin(insufficientPlanks, 0, 12)
                    && insufficientPlanks[0] == 1
                    && insufficientPlanks[1] == 1
                    && !transaction.HasReservation,
                "Insufficient Planks consumed or reserved recipe ownership.");

            int[] insufficientIron = { 2, 0 };
            Require(!transaction.TryBegin(insufficientIron, 0, 12)
                    && insufficientIron[0] == 2
                    && insufficientIron[1] == 0
                    && !transaction.HasReservation,
                "Insufficient Iron consumed or reserved recipe ownership.");

            int[] outputFull = { 2, 1 };
            Require(!transaction.TryBegin(outputFull, 12, 12)
                    && outputFull[0] == 2
                    && outputFull[1] == 1
                    && !transaction.HasReservation,
                "A full output consumed inputs or reserved output capacity.");

            int[] exactInputs = { 2, 1 };
            int output = 0;
            Require(transaction.TryBegin(exactInputs, output, 12)
                    && exactInputs[0] == 0
                    && exactInputs[1] == 0
                    && transaction.GetOwnedInput(0) == 2
                    && transaction.GetOwnedInput(1) == 1
                    && transaction.ReservedOutputCapacity == 1,
                "Exact 2 Planks + 1 Iron Bar did not begin atomically.");
            Require(transaction.TryCommit(ref output, 12)
                    && output == 1
                    && !transaction.HasReservation
                    && transaction.GetOwnedInput(0) == 0
                    && transaction.GetOwnedInput(1) == 0,
                "Exact recipe did not commit exactly one Tool.");

            int[] repeatedInputs = { 6, 3 };
            output = 0;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Require(transaction.TryBegin(repeatedInputs, output, 12)
                        && transaction.TryCommit(ref output, 12),
                    $"Repeated recipe cycle {cycle} failed.");
            }

            Require(repeatedInputs[0] == 0
                    && repeatedInputs[1] == 0
                    && output == 3
                    && !transaction.HasReservation,
                "Repeated recipe cycles duplicated or lost resources.");

            int[] rollbackInputs = { 4, 2 };
            output = 5;
            Require(transaction.TryBegin(rollbackInputs, output, 12)
                    && rollbackInputs[0] == 2
                    && rollbackInputs[1] == 1
                    && transaction.TryRollback(
                        rollbackInputs,
                        new[] { 24, 12 })
                    && rollbackInputs[0] == 4
                    && rollbackInputs[1] == 2
                    && output == 5
                    && !transaction.HasReservation,
                "Recipe rollback did not restore exact stable ownership.");
        }

        private static void TestCarryToolIsolation()
        {
            GameObject fixture = new GameObject("M12 CarryStack Fixture");
            try
            {
                CarryStack carry = fixture.AddComponent<CarryStack>();
                Require(carry.TryAdd(ResourceType.Tool, 4)
                        && carry.ActiveResourceType == ResourceType.Tool
                        && carry.GetAmount(ResourceType.Tool) == 4,
                    "CarryStack did not accept Tools as one resource type.");
                Require(!carry.TryAdd(ResourceType.Plank, 1)
                        && !carry.TryAdd(ResourceType.IronBar, 1)
                        && carry.TotalAmount == 4
                        && carry.GetAmount(ResourceType.Tool) == 4,
                    "A Tool stack mixed with another resource.");
                Require(carry.TryReserveCapacity(ResourceType.Tool, 2)
                        && !carry.TryReserveCapacity(ResourceType.Plank, 1)
                        && carry.TryCommitReservedAdd(ResourceType.Tool, 2)
                        && carry.TotalAmount == 6
                        && carry.ReservedCapacity == 0,
                    "Tool capacity reservation was not type-safe and atomic.");
                Require(carry.TryRemove(ResourceType.Tool, 6)
                        && !carry.HasActiveResource
                        && carry.TryAdd(ResourceType.Plank, 1)
                        && !carry.TryAdd(ResourceType.Tool, 1),
                    "Clearing Tools did not reset, or later Planks mixed with Tools.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        private static void TestEmpireLevels()
        {
            M10ProgressionSaveData state = CreateM11CompleteProgression();
            var levels = new List<int>();
            var model = new LumberCampProgressionModel(state, amount => false);
            model.EmpireLevelAdvanced += levels.Add;

            Require(model.EmpireLevel == 3,
                "M11-complete progression must begin M12 at Empire Level 3.");
            Require(model.RecordToolWorkshopUnlocked()
                    && !model.RecordToolWorkshopUnlocked()
                    && model.EmpireLevel == 4
                    && SequenceEquals(levels, 4),
                "Workshop unlock did not advance exactly once to Level 4.");
            Require(model.RecordToolsProduced(1)
                    && model.EmpireLevel == 5
                    && SequenceEquals(levels, 4, 5),
                "First Tool did not advance exactly once to Level 5.");
            Require(model.RecordToolsProduced(3)
                    && !model.RecordToolsProduced(0)
                    && !model.RecordToolsProduced(-1)
                    && !model.EvaluateAll()
                    && model.EmpireLevel == 5
                    && SequenceEquals(levels, 4, 5),
                "Later mutations regressed or replayed Empire feedback.");

            M10ProgressionSaveData captured = model.CapturePersistentState();
            var restoredLevels = new List<int>();
            var restored = new LumberCampProgressionModel(captured, amount => false);
            restored.EmpireLevelAdvanced += restoredLevels.Add;
            restored.EvaluateAll();
            Require(restored.EmpireLevel == 5 && restoredLevels.Count == 0,
                "Restore replayed Empire transition feedback.");

            M10ProgressionSaveData outOfOrder = M10ProgressionSaveData.CreateFresh();
            SetMetric(outOfOrder, ProgressMetricId.DrillUnlocked, 1L);
            SetMetric(outOfOrder, ProgressMetricId.ToolWorkshopUnlocked, 1L);
            SetMetric(outOfOrder, ProgressMetricId.ToolsProduced, 1L);
            var crossed = new List<int>();
            var jumpModel = new LumberCampProgressionModel(outOfOrder, amount => false);
            jumpModel.EmpireLevelAdvanced += crossed.Add;
            Require(jumpModel.EmpireLevel == 1
                    && jumpModel.RecordFlag(ProgressFlagId.LumberCampCompleted)
                    && jumpModel.EmpireLevel == 5
                    && SequenceEquals(crossed, 2, 3, 4, 5),
                "Crossed Empire levels were skipped or duplicated.");
        }

        private static void TestToolProgression()
        {
            var rewards = new RewardRecorder();
            var unlocked = new List<int>();
            var model = new LumberCampProgressionModel(
                CreateM11CompleteProgression(),
                rewards.TryGrant);
            model.AchievementUnlocked += unlocked.Add;

            Require(CurrentObjectiveIs(model, MainObjectiveId.UnlockToolWorkshop),
                "M11-complete state did not begin with the Workshop objective.");
            Require(model.RecordToolWorkshopUnlocked()
                    && !model.RecordToolWorkshopUnlocked()
                    && model.GetMetric(ProgressMetricId.ToolWorkshopUnlocked) == 1L
                    && CurrentObjectiveIs(model, MainObjectiveId.ProduceFiveTools),
                "Workshop unlock was not binary or did not advance objectives.");
            Require(Count(unlocked, LumberCampAchievementId.CrossIndustry) == 1,
                "Cross-Industry did not unlock exactly once.");

            Require(model.RecordToolsProduced(1)
                    && model.GetMetric(ProgressMetricId.ToolsProduced) == 1L,
                "Committed Tool production was not recorded exactly.");
            model.GetObjectiveProgress(out long current, out long target);
            Require(current == 1L && target == 5L,
                "Produce 5 Tools objective exposed incorrect progress.");
            Require(Count(unlocked, LumberCampAchievementId.FirstTool) == 1
                    && Count(unlocked, LumberCampAchievementId.EmpireLevelFive) == 1,
                "First Tool or Empire Level 5 achievement was not exactly once.");
            Require(!model.RecordToolsProduced(0)
                    && !model.RecordToolsProduced(-5)
                    && model.GetMetric(ProgressMetricId.ToolsProduced) == 1L,
                "Rejected production changed the Tool lifetime metric.");

            Require(model.RecordSale(ResourceType.Tool, 1, 100)
                    && model.GetMetric(ProgressMetricId.ToolsSold) == 1L
                    && model.GetMetric(ProgressMetricId.TotalCashEarned) == 100L,
                "Authoritative Tool sale did not record one Tool and $100.");
            Require(!model.RecordSale(ResourceType.Tool, 0, 100)
                    && !model.RecordSale(ResourceType.Tool, 1, 0)
                    && model.GetMetric(ProgressMetricId.ToolsSold) == 1L
                    && model.GetMetric(ProgressMetricId.TotalCashEarned) == 100L,
                "Rejected Tool sale mutated progression.");

            Require(model.RecordToolsProduced(24)
                    && model.GetMetric(ProgressMetricId.ToolsProduced) == 25L
                    && model.AreAllObjectivesCompleted
                    && Count(unlocked, LumberCampAchievementId.Toolmaker) == 1,
                "Toolmaker or the final M12 objectives did not resolve exactly.");
            LumberCampAchievementId[] m12Achievements =
            {
                LumberCampAchievementId.FirstTool,
                LumberCampAchievementId.CrossIndustry,
                LumberCampAchievementId.Toolmaker,
                LumberCampAchievementId.EmpireLevelFive
            };
            for (int i = 0; i < m12Achievements.Length; i++)
            {
                int index = (int)m12Achievements[i];
                Require(model.IsAchievementUnlocked(index)
                        && model.IsAchievementRewarded(index)
                        && Count(unlocked, m12Achievements[i]) == 1,
                    $"M12 achievement {m12Achievements[i]} was not handled exactly once.");
            }

            int eventCount = unlocked.Count;
            Require(!model.EvaluateAll() && unlocked.Count == eventCount,
                "Stable reevaluation duplicated a Tool achievement or metric.");
        }

        private static void TestToolContracts()
        {
            M10ProgressionSaveData state = CreateM11CompleteProgression();
            for (int i = 0; i < Version3ContractCount; i++)
            {
                state.claimedContracts[i] = true;
            }

            SetMetric(state, ProgressMetricId.ToolWorkshopUnlocked, 1L);
            SetMetric(state, ProgressMetricId.ToolsProduced, 7L);
            SetMetric(state, ProgressMetricId.ToolsSold, 3L);
            state.activeContractIndex = (int)LumberCampContractId.ProduceTenTools;
            state.activeContractBaseline = 7L;
            state.activeContractState = ContractProgressState.Active;

            var rewards = new RewardRecorder();
            var model = new LumberCampProgressionModel(state, rewards.TryGrant);
            model.GetActiveContractProgress(out long current, out long target);
            Require(current == 0L && target == 10L,
                "Produce Tools contract counted pre-activation production.");
            Require(model.RecordToolsProduced(9)
                    && model.ActiveContractState == ContractProgressState.Active,
                "Produce Tools contract completed before its exact target.");
            Require(model.RecordToolsProduced(1)
                    && model.ActiveContractState
                    == ContractProgressState.CompletedUnclaimed
                    && model.TryClaimActiveContract()
                    && rewards.Count(800) == 1,
                "Produce 10 Tools contract did not complete/reward exactly once.");

            model.GetActiveContractProgress(out current, out target);
            Require(model.ActiveContractIndex
                    == (int)LumberCampContractId.SellFifteenTools
                    && current == 0L
                    && target == 15L,
                "Sell Tools contract did not establish a non-retroactive baseline.");
            Require(model.RecordSale(ResourceType.Tool, 14, 1400)
                    && model.ActiveContractState == ContractProgressState.Active,
                "Sell Tools contract completed before 15 post-activation sales.");
            Require(model.RecordSale(ResourceType.Tool, 1, 100)
                    && model.ActiveContractState
                    == ContractProgressState.CompletedUnclaimed
                    && model.TryClaimActiveContract()
                    && rewards.Count(1200) == 1
                    && !model.HasActiveContract,
                "Sell 15 Tools contract did not complete/reward exactly once.");
            Require(!model.TryClaimActiveContract()
                    && rewards.Count(800) == 1
                    && rewards.Count(1200) == 1,
                "A Tool contract reward could be claimed twice.");
        }

        private static void TestVersion3Migration()
        {
            string version3Json = JsonUtility.ToJson(CreateVersion3Fixture());
            Require(version3Json.Contains("\"version\":3")
                    && !version3Json.Contains("\"workshop\""),
                "Frozen v3 fixture accidentally contains v4 state.");

            M9SaveDecodeResult migrated = M9SaveCodec.Decode(
                version3Json,
                CreateValidationSettings(),
                BaselineUtc + 60L);
            Require(migrated.IsSuccess
                    && migrated.WasMigrated
                    && migrated.Data.version == M9SaveSchema.CurrentVersion,
                "A canonical v3 save did not migrate to the current schema: "
                + migrated.Diagnostic);

            M9SaveData data = migrated.Data;
            Require(data.walletCash == 876
                    && data.cashPileStoredCash == 234
                    && data.carry.resourceType == ResourceType.IronBar
                    && data.carry.amount == 6
                    && data.stockpileWood == 17
                    && data.processorInputWood == 9
                    && data.processorOutputPlanks == 8
                    && data.packingInputPlanks == 11
                    && data.packingOutputCrates == 6,
                "v3 migration changed valid economy, Carry, or Lumber buffers.");
            for (int i = 0; i < Version3MetricCount; i++)
            {
                long expected = Version3MetricValues[i];
                Require(data.progression.GetMetric((ProgressMetricId)i) == expected,
                    $"v3 metric {(ProgressMetricId)i} changed during migration.");
            }

            for (int i = 0; i < Version3FlagCount; i++)
            {
                Require(data.progression.GetFlag((ProgressFlagId)i),
                    $"v3 flag {(ProgressFlagId)i} changed during migration.");
            }

            Require(data.progression.GetMetric(ProgressMetricId.ToolsProduced) == 0L
                    && data.progression.GetMetric(ProgressMetricId.ToolsSold) == 0L
                    && data.progression.GetMetric(
                        ProgressMetricId.ToolWorkshopUnlocked) == 0L,
                "v3 migration invented M12 metrics.");
            Require(data.mining.mineUnlocked
                    && data.mining.smelterInputIronOre == 7
                    && data.mining.smelterOutputIronBars == 6
                    && data.mining.oreStorageIronOre == 9
                    && data.mining.purchasePads[0].completed
                    && data.mining.purchasePads[1].completed,
                "v3 migration changed canonical M11 Mining state.");
            Require(IsFreshWorkshop(data.workshop),
                "v3 migration did not create an exact fresh Workshop.");
            Require(data.progression.activeContractIndex
                    == (int)LumberCampContractId.ProduceTenTools
                    && data.progression.activeContractBaseline == 0L
                    && data.progression.activeContractState
                    == ContractProgressState.Active,
                "Completed v3 contracts did not activate M12 with a fresh baseline.");
            for (int i = 0; i < Version3AchievementCount; i++)
            {
                M10AchievementSaveRecord record =
                    data.progression.FindAchievementRecord(i);
                Require(record != null && record.unlocked && record.rewarded,
                    $"v3 achievement {i} changed during migration.");
            }

            for (int i = Version3AchievementCount;
                 i < LumberCampProgressionCatalog.AchievementCount;
                 i++)
            {
                M10AchievementSaveRecord record =
                    data.progression.FindAchievementRecord(i);
                Require(record != null && !record.unlocked && !record.rewarded,
                    $"v3 migration invented M12 achievement state at {i}.");
            }

            Require(M9SaveCodec.TryEncode(
                    data,
                    CreateValidationSettings(),
                    BaselineUtc + 60L,
                    out string firstJson,
                    out _,
                    out string encodeFailure),
                "Migrated v4 state did not encode: " + encodeFailure);
            M9SaveDecodeResult reloaded = M9SaveCodec.Decode(
                firstJson,
                CreateValidationSettings(),
                BaselineUtc + 60L);
            Require(reloaded.IsSuccess && !reloaded.WasMigrated,
                "Resaved v4 state remigrated or failed: " + reloaded.Diagnostic);
            Require(M9SaveCodec.TryEncode(
                    reloaded.Data,
                    CreateValidationSettings(),
                    BaselineUtc + 60L,
                    out string secondJson,
                    out _,
                    out string secondFailure)
                    && string.Equals(firstJson, secondJson, StringComparison.Ordinal),
                "v3-to-v4 migration was not idempotent: " + secondFailure);
        }

        private static void TestVersion4WorkshopPersistence()
        {
            M9SaveData partial = CreateWorkshopEligibleState();
            partial.workshop.purchasePad.paidCash = 1700;
            Require(M9SaveCodec.TryEncode(
                    partial,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out string partialJson,
                    out _,
                    out string partialFailure),
                "Eligible partial Workshop state did not encode: " + partialFailure);
            M9SaveDecodeResult partialReload = M9SaveCodec.Decode(
                partialJson,
                CreateValidationSettings(),
                BaselineUtc);
            Require(partialReload.IsSuccess
                    && !partialReload.Data.workshop.purchasePad.completed
                    && partialReload.Data.workshop.purchasePad.paidCash == 1700
                    && partialReload.Data.progression.GetMetric(
                        ProgressMetricId.ToolWorkshopUnlocked) == 0L,
                "Partial Workshop purchase did not round-trip exactly.");

            M9SaveData completed = CreateWorkshopEligibleState();
            completed.workshop.purchasePad.completed = true;
            completed.workshop.purchasePad.paidCash = 1;
            completed.workshop.inputPlanks = 23;
            completed.workshop.inputIronBars = 11;
            completed.workshop.outputTools = 12;
            completed.carry.resourceType = ResourceType.Tool;
            completed.carry.amount = 7;
            SetMetric(completed.progression, ProgressMetricId.ToolsProduced, 9L);
            SetMetric(completed.progression, ProgressMetricId.ToolsSold, 3L);

            Require(M9SaveCodec.TryEncode(
                    completed,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out string completedJson,
                    out M9SaveData normalized,
                    out string completedFailure),
                "Completed Workshop state did not encode: " + completedFailure);
            M9SaveDecodeResult completedReload = M9SaveCodec.Decode(
                completedJson,
                CreateValidationSettings(),
                BaselineUtc);
            Require(completedReload.IsSuccess
                    && normalized.workshop.purchasePad.paidCash == 3200
                    && completedReload.Data.workshop.purchasePad.completed
                    && completedReload.Data.workshop.purchasePad.paidCash == 3200
                    && completedReload.Data.workshop.inputPlanks == 23
                    && completedReload.Data.workshop.inputIronBars == 11
                    && completedReload.Data.workshop.outputTools == 12
                    && completedReload.Data.carry.resourceType == ResourceType.Tool
                    && completedReload.Data.carry.amount == 7
                    && completedReload.Data.progression.GetMetric(
                        ProgressMetricId.ToolWorkshopUnlocked) == 1L
                    && completedReload.Data.progression.GetMetric(
                        ProgressMetricId.ToolsProduced) == 9L
                    && completedReload.Data.progression.GetMetric(
                        ProgressMetricId.ToolsSold) == 3L,
                "Completed Workshop buffers, Carry Tool, or metrics changed on round-trip.");

            using (var scope = new TemporarySaveScope())
            {
                var store = new M9LocalSaveStore(
                    scope.DirectoryPath,
                    new ManualUtcClock(BaselineUtc),
                    CreateValidationSettings(),
                    preserveInvalidFiles: false);
                Require(store.Save(normalized).IsSuccess,
                    "Reset fixture could not persist v4 Workshop state.");
                Require(store.TryDeleteSave(out string deleteFailure),
                    "Reset could not delete v4 Workshop state: " + deleteFailure);
                M9SaveLoadResult reset = store.Load();
                Require(reset.Status == M9SaveLoadStatus.FreshNoSave
                        && IsFreshWorkshop(reset.Data.workshop)
                        && reset.Data.carry.amount == 0
                        && reset.Data.progression.GetMetric(
                            ProgressMetricId.ToolsProduced) == 0L
                        && reset.Data.progression.GetMetric(
                            ProgressMetricId.ToolsSold) == 0L
                        && reset.Data.progression.GetMetric(
                            ProgressMetricId.ToolWorkshopUnlocked) == 0L,
                    "Reset Save retained M12 Workshop, Carry, or progression state.");
            }
        }

        private static void TestImpossibleWorkshopState()
        {
            M9SaveData metricContradiction = CreateWorkshopEligibleState();
            SetMetric(
                metricContradiction.progression,
                ProgressMetricId.ToolWorkshopUnlocked,
                1L);
            Require(!M9SaveValidator.TryNormalize(
                    metricContradiction,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out _,
                    out _),
                "Workshop unlock metric survived an incomplete canonical pad.");

            M9SaveData unavailableCompletion = M9SaveData.CreateFresh(BaselineUtc);
            unavailableCompletion.workshop.purchasePad.completed = true;
            unavailableCompletion.workshop.purchasePad.paidCash = 3200;
            Require(!M9SaveValidator.TryNormalize(
                    unavailableCompletion,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out _,
                    out _),
                "Workshop completed before both input industries were established.");

            M9SaveData unavailablePartial = M9SaveData.CreateFresh(BaselineUtc);
            unavailablePartial.workshop.purchasePad.paidCash = 900;
            Require(M9SaveValidator.TryNormalize(
                    unavailablePartial,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out M9SaveData cleared,
                    out string failure)
                    && cleared.workshop.purchasePad.paidCash == 0
                    && !cleared.workshop.purchasePad.completed,
                "Unavailable Workshop partial progress was not cleared canonically: "
                + failure);
        }

        private static void TestOfflineWorkshop()
        {
            OfflineProgressionRules rules = CreateWorkshopOfflineRules();
            OfflineProgressionInput preloaded = CreateWorkshopOfflineInput(10L);
            preloaded.ToolWorkshopUnlocked = true;
            preloaded.ToolWorkshopInputPlanks = 7;
            preloaded.ToolWorkshopInputIronBars = 2;
            preloaded.ToolWorkshopOutputTools = 3;
            OfflineProgressionResult processed =
                OfflineProgressionCalculator.Calculate(preloaded, rules);
            Require(processed.ToolWorkshopRecipesCompleted == 2
                    && processed.ToolsProduced == 2
                    && processed.ToolWorkshopInputPlanks == 3
                    && processed.ToolWorkshopInputIronBars == 0
                    && processed.ToolWorkshopOutputTools == 5,
                "Offline Workshop did not apply exact min(planks/2, bars, output, time)." );

            OfflineProgressionInput outputFull = CreateWorkshopOfflineInput(60L);
            outputFull.ToolWorkshopUnlocked = true;
            outputFull.ToolWorkshopInputPlanks = 4;
            outputFull.ToolWorkshopInputIronBars = 2;
            outputFull.ToolWorkshopOutputTools = 12;
            OfflineProgressionResult full =
                OfflineProgressionCalculator.Calculate(outputFull, rules);
            Require(full.ToolWorkshopRecipesCompleted == 0
                    && full.ToolsProduced == 0
                    && full.ToolWorkshopInputPlanks == 4
                    && full.ToolWorkshopInputIronBars == 2
                    && full.ToolWorkshopOutputTools == 12,
                "Full offline Workshop output consumed inputs.");

            OfflineProgressionInput locked = CreateWorkshopOfflineInput(60L);
            locked.ToolWorkshopUnlocked = false;
            locked.ToolWorkshopInputPlanks = 4;
            locked.ToolWorkshopInputIronBars = 2;
            OfflineProgressionResult lockedResult =
                OfflineProgressionCalculator.Calculate(locked, rules);
            Require(lockedResult.ToolWorkshopRecipesCompleted == 0
                    && lockedResult.ToolWorkshopInputPlanks == 4
                    && lockedResult.ToolWorkshopInputIronBars == 2
                    && lockedResult.ToolWorkshopOutputTools == 0,
                "Locked offline Workshop processed stored inputs.");

            OfflineProgressionInput noTeleport = CreateWorkshopOfflineInput(60L);
            noTeleport.ToolWorkshopUnlocked = true;
            noTeleport.ProcessorOutputPlanks = 12;
            noTeleport.PackingInputPlanks = 24;
            noTeleport.ToolWorkshopInputPlanks = 0;
            noTeleport.ToolWorkshopInputIronBars = 0;
            OfflineProgressionResult isolated =
                OfflineProgressionCalculator.Calculate(noTeleport, rules);
            Require(isolated.ToolWorkshopRecipesCompleted == 0
                    && isolated.ToolsProduced == 0
                    && isolated.ToolWorkshopInputPlanks == 0
                    && isolated.ToolWorkshopInputIronBars == 0
                    && isolated.ToolWorkshopOutputTools == 0,
                "Offline settlement teleported another industry's resources into Workshop.");
        }

        private static void TestOfflineMetricIsolation()
        {
            M9SaveData state = CreateWorkshopEligibleState();
            state.workshop.purchasePad.completed = true;
            state.workshop.purchasePad.paidCash = 3200;
            state.workshop.inputPlanks = 4;
            state.workshop.inputIronBars = 2;
            SetMetric(state.progression, ProgressMetricId.ToolWorkshopUnlocked, 1L);
            SetMetric(state.progression, ProgressMetricId.ToolsProduced, 6L);

            OfflineProgressionInput input = CreateWorkshopOfflineInput(4L);
            input.ToolWorkshopUnlocked = true;
            input.ToolWorkshopInputPlanks = state.workshop.inputPlanks;
            input.ToolWorkshopInputIronBars = state.workshop.inputIronBars;
            input.ToolWorkshopOutputTools = state.workshop.outputTools;
            OfflineProgressionResult result = OfflineProgressionCalculator.Calculate(
                input,
                CreateWorkshopOfflineRules());
            Require(result.ToolsProduced == 2,
                "Offline metric-isolation fixture did not process two Tools.");

            state.workshop.inputPlanks = result.ToolWorkshopInputPlanks;
            state.workshop.inputIronBars = result.ToolWorkshopInputIronBars;
            state.workshop.outputTools = result.ToolWorkshopOutputTools;
            state.lastEvaluationUtcUnixSeconds = result.NextEvaluationUtcUnixSeconds;
            Require(M9SaveCodec.TryEncode(
                    state,
                    CreateValidationSettings(),
                    BaselineUtc + 4L,
                    out string json,
                    out _,
                    out string failure),
                "Offline-settled Workshop state did not encode: " + failure);
            M9SaveDecodeResult reloaded = M9SaveCodec.Decode(
                json,
                CreateValidationSettings(),
                BaselineUtc + 4L);
            Require(reloaded.IsSuccess
                    && reloaded.Data.workshop.inputPlanks == 0
                    && reloaded.Data.workshop.inputIronBars == 0
                    && reloaded.Data.workshop.outputTools == 2
                    && reloaded.Data.progression.GetMetric(
                        ProgressMetricId.ToolsProduced) == 6L
                    && reloaded.Data.progression.GetMetric(
                        ProgressMetricId.ToolsSold) == 0L,
                "Offline Workshop settlement inflated lifetime Tool metrics.");
        }

        private static M10ProgressionSaveData CreateM11CompleteProgression()
        {
            M10ProgressionSaveData state = M10ProgressionSaveData.CreateFresh();
            SetFlag(state, ProgressFlagId.ProductionUpgradeUnlocked);
            SetFlag(state, ProgressFlagId.WorkerUnlocked);
            SetFlag(state, ProgressFlagId.ProcessorUnlocked);
            SetFlag(state, ProgressFlagId.AutoFeederUnlocked);
            SetFlag(state, ProgressFlagId.PackingStationUnlocked);
            SetFlag(state, ProgressFlagId.CourierUnlocked);
            SetFlag(state, ProgressFlagId.LumberCampCompleted);
            SetFlag(state, ProgressFlagId.SmelterUnlocked);
            SetMetric(state, ProgressMetricId.PlanksProduced, 10L);
            SetMetric(state, ProgressMetricId.CratesProduced, 5L);
            SetMetric(state, ProgressMetricId.CourierTripsCompleted, 5L);
            SetMetric(state, ProgressMetricId.IronOreMined, 10L);
            SetMetric(state, ProgressMetricId.IronBarsProduced, 5L);
            SetMetric(state, ProgressMetricId.MineUnlocked, 1L);
            SetMetric(state, ProgressMetricId.DrillUnlocked, 1L);
            return state;
        }

        private static M9SaveData CreateWorkshopEligibleState()
        {
            M9SaveData data = M9SaveData.CreateFresh(BaselineUtc);
            for (int i = 0; i < data.purchasePads.Length; i++)
            {
                data.purchasePads[i].completed = true;
                data.purchasePads[i].paidCash = M9PurchasePadIds.GetTotalCost(i);
            }

            data.lumberCampCompleted = true;
            data.mining.mineUnlocked = true;
            for (int i = 0; i < data.mining.purchasePads.Length; i++)
            {
                data.mining.purchasePads[i].completed = true;
                data.mining.purchasePads[i].paidCash =
                    M11MiningPurchasePadIds.GetTotalCost(i);
            }

            data.progression = CreateM11CompleteProgression();
            return data;
        }

        private static Version3Fixture CreateVersion3Fixture()
        {
            var progression = new Version3ProgressionFixture
            {
                metrics = new Version3MetricFixture[Version3MetricCount],
                flags = new Version3FlagFixture[Version3FlagCount],
                claimedContracts = new bool[Version3ContractCount],
                achievements = new Version3AchievementFixture[
                    Version3AchievementCount],
                objectiveIndex = 13,
                activeContractIndex = Version3ContractCount,
                activeContractBaseline = 0L,
                activeContractState = (int)ContractProgressState.Claimed
            };
            for (int i = 0; i < progression.metrics.Length; i++)
            {
                progression.metrics[i] = new Version3MetricFixture
                {
                    id = Version3MetricStableIds[i],
                    value = Version3MetricValues[i]
                };
            }

            for (int i = 0; i < progression.flags.Length; i++)
            {
                progression.flags[i] = new Version3FlagFixture
                {
                    id = Version3FlagStableIds[i],
                    value = true
                };
            }

            for (int i = 0; i < progression.claimedContracts.Length; i++)
            {
                progression.claimedContracts[i] = true;
            }

            for (int i = 0; i < progression.achievements.Length; i++)
            {
                progression.achievements[i] = new Version3AchievementFixture
                {
                    id = Version3AchievementStableIds[i],
                    unlocked = true,
                    rewarded = true
                };
            }

            var mining = new Version3MiningFixture
            {
                mineUnlocked = true,
                purchasePads = new[]
                {
                    new Version3PurchasePadFixture
                    {
                        id = "iron_smelter",
                        paidCash = 1200,
                        completed = true
                    },
                    new Version3PurchasePadFixture
                    {
                        id = "automated_drill",
                        paidCash = 2400,
                        completed = true
                    }
                },
                smelterInputIronOre = 7,
                smelterOutputIronBars = 6,
                oreStorageIronOre = 9
            };

            string[] padIds =
            {
                "production_upgrade",
                "lumber_worker",
                "wood_processor",
                "auto_feeder",
                "packing_station",
                "delivery_courier"
            };
            int[] padCosts = { 120, 240, 360, 600, 900, 1500 };
            var pads = new Version3PurchasePadFixture[padIds.Length];
            for (int i = 0; i < padIds.Length; i++)
            {
                pads[i] = new Version3PurchasePadFixture
                {
                    id = padIds[i],
                    paidCash = padCosts[i],
                    completed = true
                };
            }

            return new Version3Fixture
            {
                schema = "industry-tycoon-local-save",
                version = 3,
                walletCash = 876,
                cashPileStoredCash = 234,
                carry = new Version3CarryFixture
                {
                    resourceType = 4,
                    amount = 6
                },
                purchasePads = pads,
                lumberCampCompleted = true,
                stockpileWood = 17,
                processorInputWood = 9,
                processorOutputPlanks = 8,
                packingInputPlanks = 11,
                packingOutputCrates = 6,
                lastEvaluationUtcUnixSeconds = BaselineUtc,
                lastWriteUtcUnixSeconds = BaselineUtc,
                progression = progression,
                mining = mining
            };
        }

        private static M9SaveValidationSettings CreateValidationSettings()
        {
            return new M9SaveValidationSettings
            {
                carryCapacity = 12,
                stockpileCapacity = 30,
                processorInputCapacity = 24,
                processorOutputCapacity = 12,
                packingInputCapacity = 24,
                packingOutputCapacity = 12,
                smelterInputCapacity = 24,
                smelterOutputCapacity = 12,
                oreStorageCapacity = 30,
                workshopPlankInputCapacity = 24,
                workshopIronBarInputCapacity = 12,
                workshopToolOutputCapacity = 12
            };
        }

        private static OfflineProgressionRules CreateWorkshopOfflineRules()
        {
            return new OfflineProgressionRules
            {
                MaximumCreditedAwaySeconds = 3600L,
                OfflineEfficiency = 1d,
                ReturnScreenThresholdSeconds = long.MaxValue,
                ToolWorkshopSecondsPerRecipe = 2d,
                ToolWorkshopPlanksPerRecipe = 2,
                ToolWorkshopIronBarsPerRecipe = 1,
                ToolWorkshopToolsPerRecipe = 1
            };
        }

        private static OfflineProgressionInput CreateWorkshopOfflineInput(
            long awaySeconds)
        {
            return new OfflineProgressionInput
            {
                LastEvaluationUtcUnixSeconds = BaselineUtc,
                NowUtcUnixSeconds = BaselineUtc + awaySeconds,
                ToolWorkshopPlankCapacity = 24,
                ToolWorkshopIronBarCapacity = 12,
                ToolWorkshopOutputCapacity = 12
            };
        }

        private static bool CurrentObjectiveIs(
            LumberCampProgressionModel model,
            MainObjectiveId objective)
        {
            return model != null
                   && !model.AreAllObjectivesCompleted
                   && LumberCampProgressionCatalog.GetObjective(model.ObjectiveIndex).Id
                   == objective;
        }

        private static bool IsFreshWorkshop(M12WorkshopSaveData workshop)
        {
            return workshop != null
                   && workshop.purchasePad != null
                   && string.Equals(
                       workshop.purchasePad.id,
                       M12WorkshopPurchasePadIds.ToolWorkshop,
                       StringComparison.Ordinal)
                   && workshop.purchasePad.paidCash == 0
                   && !workshop.purchasePad.completed
                   && workshop.inputPlanks == 0
                   && workshop.inputIronBars == 0
                   && workshop.outputTools == 0;
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

        private static int Count(
            List<int> unlocked,
            LumberCampAchievementId achievement)
        {
            int count = 0;
            int expected = (int)achievement;
            for (int i = 0; i < unlocked.Count; i++)
            {
                if (unlocked[i] == expected)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool SequenceEquals(List<int> actual, params int[] expected)
        {
            if (actual == null || expected == null || actual.Count != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void Run(string name, Action test)
        {
            test();
            _testCount++;
            Debug.Log($"M12 deterministic test PASS: {name}");
        }

        private static void Require(bool condition, string message)
        {
            _assertionCount++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class RewardRecorder
        {
            private readonly List<int> _amounts = new List<int>();

            public bool TryGrant(int amount)
            {
                _amounts.Add(amount);
                return true;
            }

            public int Count(int amount)
            {
                int count = 0;
                for (int i = 0; i < _amounts.Count; i++)
                {
                    if (_amounts[i] == amount)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private sealed class TemporarySaveScope : IDisposable
        {
            public TemporarySaveScope()
            {
                DirectoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "IndustryTycoon_M12_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(DirectoryPath);
            }

            public string DirectoryPath { get; }

            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, true);
                }
            }
        }

        [Serializable]
        private sealed class Version3Fixture
        {
            public string schema;
            public int version;
            public int walletCash;
            public int cashPileStoredCash;
            public Version3CarryFixture carry;
            public Version3PurchasePadFixture[] purchasePads;
            public bool lumberCampCompleted;
            public int stockpileWood;
            public int processorInputWood;
            public int processorOutputPlanks;
            public int packingInputPlanks;
            public int packingOutputCrates;
            public int pendingOfflineCash;
            public long pendingOfflineAwaySeconds;
            public bool returnScreenPending;
            public long lastEvaluationUtcUnixSeconds;
            public long lastWriteUtcUnixSeconds;
            public Version3ProgressionFixture progression;
            public Version3MiningFixture mining;
        }

        [Serializable]
        private sealed class Version3CarryFixture
        {
            public int resourceType;
            public int amount;
        }

        [Serializable]
        private sealed class Version3PurchasePadFixture
        {
            public string id;
            public int paidCash;
            public bool completed;
        }

        [Serializable]
        private sealed class Version3MetricFixture
        {
            public string id;
            public long value;
        }

        [Serializable]
        private sealed class Version3FlagFixture
        {
            public string id;
            public bool value;
        }

        [Serializable]
        private sealed class Version3AchievementFixture
        {
            public string id;
            public bool unlocked;
            public bool rewarded;
        }

        [Serializable]
        private sealed class Version3ProgressionFixture
        {
            public Version3MetricFixture[] metrics;
            public Version3FlagFixture[] flags;
            public int objectiveIndex;
            public int activeContractIndex;
            public long activeContractBaseline;
            public int activeContractState;
            public bool[] claimedContracts;
            public Version3AchievementFixture[] achievements;
        }

        [Serializable]
        private sealed class Version3MiningFixture
        {
            public bool mineUnlocked;
            public Version3PurchasePadFixture[] purchasePads;
            public int smelterInputIronOre;
            public int smelterOutputIronBars;
            public int oreStorageIronOre;
        }
    }
}
