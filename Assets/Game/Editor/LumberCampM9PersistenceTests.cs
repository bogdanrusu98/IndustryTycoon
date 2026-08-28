using System;
using System.IO;
using IndustryTycoon.Core;
using IndustryTycoon.Persistence;
using IndustryTycoon.Progression;
using UnityEditor;
using UnityEngine;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Fast deterministic M9 model/store tests. These do not enter Play Mode and never
    /// touch the player's real persistent-data directory.
    /// </summary>
    public static class LumberCampM9PersistenceTests
    {
        private const long BaselineUtc = 2000000000L;
        private static int _assertionCount;
        private static int _testCount;

        [MenuItem("Industry Tycoon/Prototype/Run M9 Persistence + Offline Tests")]
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

            Run("Fresh no-save state", TestFreshNoSaveState);
            Run("Codec and authoritative-field round trip", TestCodecRoundTrip);
            Run("Partial PurchasePad round trip", TestPartialPurchasePadRoundTrip);
            Run("Completed chain and buffer store round trip", TestCompletedStoreRoundTrip);
            Run("Repeated save/load conservation", TestRepeatedSaveLoadConservation);
            Run("Backup recovery", TestBackupRecovery);
            Run("Corrupt and unsupported fallback", TestInvalidFileFallbacks);
            Run("Invalid value normalization/rejection", TestValidationBoundaries);
            Run("Reset returns exact fresh state", TestStoreReset);
            Run("Schema v1 to v2 migration", TestVersion1Migration);
            Run("Migrated v2 resave is idempotent", TestMigratedResave);
            Run("Corrupt M10 structure fallback", TestCorruptM10Fallback);

            Run("Negative, invalid, capped elapsed and efficiency", TestElapsedRules);
            Run("Automation gates and Worker production", TestAutomationGates);
            Run("Feeder/Processor boundaries", TestProcessorBoundaries);
            Run("Packing isolation and existing-input processing", TestPackingBoundaries);
            Run("Courier legitimacy and exact cash", TestCourierSettlement);
            Run("Return threshold and duplicate interval guard", TestReturnAndDuplicateGuard);
            Run("Pending persistence and collect exactly once", TestPendingAndCollection);

            Debug.Log(
                $"M9 deterministic persistence/offline tests PASS: {_testCount} tests, "
                + $"{_assertionCount} assertions.");
        }

        private static void TestFreshNoSaveState()
        {
            using (var scope = new TemporarySaveScope())
            {
                M9LocalSaveStore store = scope.CreateStore(new ManualUtcClock(BaselineUtc));
                M9SaveLoadResult load = store.Load();

                Require(load.Status == M9SaveLoadStatus.FreshNoSave,
                    "Missing files must produce FreshNoSave.");
                AssertFresh(load.Data, BaselineUtc);
            }
        }

        private static void TestCodecRoundTrip()
        {
            M9SaveData expected = CreateFullyPopulatedSave();
            Require(M9SaveCodec.TryEncode(
                    expected,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out string json,
                    out M9SaveData encoded,
                    out string failure),
                "Valid M9 data did not encode: " + failure);

            M9SaveDecodeResult decoded = M9SaveCodec.Decode(
                json,
                CreateValidationSettings(),
                BaselineUtc);
            Require(decoded.IsSuccess,
                "Encoded M9 data did not decode: " + decoded.Diagnostic);
            string compactJson = json.ToLowerInvariant();
            string[] forbiddenTransientFields =
            {
                "reservation",
                "resourceclaim",
                "workertarget",
                "couriertarget",
                "inflight",
                "processingprogress",
                "workercarriedwood",
                "couriercarriedcrates",
                "coroutine",
                "visualobject"
            };
            for (int i = 0; i < forbiddenTransientFields.Length; i++)
            {
                Require(!compactJson.Contains(forbiddenTransientFields[i]),
                    $"Transient field '{forbiddenTransientFields[i]}' leaked into JSON.");
            }

            AssertEquivalent(encoded, decoded.Data, includeWriteTimestamp: true);
            Require(decoded.Data.walletCash == 731
                    && decoded.Data.cashPileStoredCash == 219,
                "Wallet/CashPile values did not round-trip.");
            Require(decoded.Data.carry.resourceType == ResourceType.Crate
                    && decoded.Data.carry.amount == 7,
                "Carry resource type/count did not round-trip.");
            Require(decoded.Data.lumberCampCompleted,
                "Lumber Camp completion did not round-trip.");
            Require(decoded.Data.stockpileWood == 17
                    && decoded.Data.processorInputWood == 9
                    && decoded.Data.processorOutputPlanks == 8
                    && decoded.Data.packingInputPlanks == 11
                    && decoded.Data.packingOutputCrates == 6,
                "Authoritative Stockpile/Processor/Packing buffers did not round-trip.");
        }

        private static void TestPartialPurchasePadRoundTrip()
        {
            M9SaveData expected = M9SaveData.CreateFresh(BaselineUtc);
            CompletePad(expected, 0);
            expected.purchasePads[1].paidCash = 137;

            Require(M9SaveCodec.TryEncode(
                    expected,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out string json,
                    out _,
                    out string failure),
                "Partial PurchasePad save did not encode: " + failure);
            M9SaveDecodeResult decoded = M9SaveCodec.Decode(
                json,
                CreateValidationSettings(),
                BaselineUtc);

            Require(decoded.IsSuccess, "Partial PurchasePad save did not decode.");
            Require(decoded.Data.purchasePads[0].completed
                    && decoded.Data.purchasePads[0].paidCash == 120,
                "Completed prerequisite pad was not canonicalized.");
            Require(!decoded.Data.purchasePads[1].completed
                    && decoded.Data.purchasePads[1].paidCash == 137,
                "Partial PurchasePad progress changed during round-trip.");
            for (int i = 2; i < M9PurchasePadIds.Count; i++)
            {
                Require(!decoded.Data.purchasePads[i].completed
                        && decoded.Data.purchasePads[i].paidCash == 0,
                    "Locked downstream PurchasePad gained state.");
            }
        }

        private static void TestCompletedStoreRoundTrip()
        {
            using (var scope = new TemporarySaveScope())
            {
                var clock = new ManualUtcClock(BaselineUtc);
                M9LocalSaveStore store = scope.CreateStore(clock);
                M9SaveData expected = CreateFullyPopulatedSave();

                M9SaveWriteResult write = store.Save(expected, prettyPrint: true);
                Require(write.IsSuccess, "Full save write failed: " + write.Diagnostic);
                Require(File.Exists(store.PrimaryPath)
                        && !File.Exists(store.TemporaryPath),
                    "Atomic write did not leave one committed primary.");

                M9SaveLoadResult load = store.Load();
                Require(load.Status == M9SaveLoadStatus.LoadedPrimary,
                    "Committed primary was not loaded.");
                AssertEquivalent(write.PersistedData, load.Data, includeWriteTimestamp: true);
                for (int i = 0; i < M9PurchasePadIds.Count; i++)
                {
                    Require(load.Data.purchasePads[i].completed
                            && load.Data.purchasePads[i].paidCash
                            == M9PurchasePadIds.GetTotalCost(i),
                        $"Completed unlock chain changed at pad {i}.");
                }
            }
        }

        private static void TestRepeatedSaveLoadConservation()
        {
            using (var scope = new TemporarySaveScope())
            {
                var clock = new ManualUtcClock(BaselineUtc);
                M9LocalSaveStore store = scope.CreateStore(clock);
                M9SaveData stable = CreateFullyPopulatedSave();

                for (int cycle = 0; cycle < 6; cycle++)
                {
                    M9SaveWriteResult write = store.Save(stable);
                    Require(write.IsSuccess, $"Save cycle {cycle} failed.");
                    M9SaveLoadResult load = store.Load();
                    Require(load.LoadedExisting, $"Load cycle {cycle} fell back fresh.");
                    AssertEquivalent(stable, load.Data, includeWriteTimestamp: true);
                    stable = load.Data;
                }

                Require(CalculateLogicalChecksum(stable)
                        == CalculateLogicalChecksum(CreateFullyPopulatedSave()),
                    "Repeated save/load changed authoritative totals.");
            }
        }

        private static void TestBackupRecovery()
        {
            using (var scope = new TemporarySaveScope())
            {
                var clock = new ManualUtcClock(BaselineUtc);
                M9LocalSaveStore store = scope.CreateStore(clock);
                M9SaveData first = CreateFullyPopulatedSave();
                Require(store.Save(first).IsSuccess, "Initial backup test save failed.");

                M9SaveData second = CreateFullyPopulatedSave();
                second.walletCash = 999;
                Require(store.Save(second).IsSuccess, "Replacement save failed.");
                Require(File.Exists(store.BackupPath),
                    "Replacement did not retain the prior primary as backup.");

                File.WriteAllText(store.PrimaryPath, "{ corrupt primary");
                M9SaveLoadResult recovered = store.Load();
                Require(recovered.Status == M9SaveLoadStatus.RecoveredBackup,
                    "A valid backup was not used after primary corruption.");
                Require(recovered.Data.walletCash == first.walletCash,
                    "Backup recovery returned the wrong committed generation.");
                Require(File.Exists(store.PrimaryPath),
                    "Backup recovery did not repair the primary path.");
            }
        }

        private static void TestInvalidFileFallbacks()
        {
            using (var corruptScope = new TemporarySaveScope())
            {
                M9LocalSaveStore store = corruptScope.CreateStore(
                    new ManualUtcClock(BaselineUtc));
                Directory.CreateDirectory(store.DirectoryPath);
                File.WriteAllText(store.PrimaryPath, "not-json");

                M9SaveLoadResult load = store.Load();
                Require(load.Status == M9SaveLoadStatus.FreshInvalidSave,
                    "Corrupted primary did not fail safely to fresh state.");
                AssertFresh(load.Data, BaselineUtc);
            }

            using (var unsupportedScope = new TemporarySaveScope())
            {
                M9LocalSaveStore store = unsupportedScope.CreateStore(
                    new ManualUtcClock(BaselineUtc));
                Directory.CreateDirectory(store.DirectoryPath);
                File.WriteAllText(
                    store.PrimaryPath,
                    "{\"schema\":\"industry-tycoon-local-save\",\"version\":999}");

                M9SaveLoadResult load = store.Load();
                Require(load.Status == M9SaveLoadStatus.FreshUnsupportedVersion,
                    "Unsupported schema version did not use the explicit fallback path.");
                AssertFresh(load.Data, BaselineUtc);
            }
        }

        private static void TestValidationBoundaries()
        {
            M9SaveValidationSettings settings = CreateValidationSettings();
            settings.maximumPendingAwaySeconds = 1000L;
            M9SaveData source = M9SaveData.CreateFresh(BaselineUtc);
            source.walletCash = -50;
            source.cashPileStoredCash = -10;
            source.carry.resourceType = (ResourceType)999;
            source.carry.amount = int.MaxValue;
            source.purchasePads[0].completed = true;
            source.purchasePads[0].paidCash = -80;
            source.purchasePads[1].paidCash = int.MaxValue;
            source.purchasePads[2].completed = true;
            source.purchasePads[2].paidCash = int.MaxValue;
            source.lumberCampCompleted = true;
            source.stockpileWood = int.MaxValue;
            source.processorInputWood = -1;
            source.processorOutputPlanks = int.MaxValue;
            source.packingInputPlanks = -1;
            source.packingOutputCrates = int.MaxValue;
            source.pendingOfflineCash = -1;
            source.pendingOfflineAwaySeconds = long.MaxValue;
            source.lastEvaluationUtcUnixSeconds = -99;
            source.lastWriteUtcUnixSeconds = long.MaxValue;

            Require(M9SaveValidator.TryNormalize(
                    source,
                    settings,
                    BaselineUtc,
                    out M9SaveData normalized,
                    out string failure),
                "Sanitizable values were rejected: " + failure);
            Require(normalized.walletCash == 0 && normalized.cashPileStoredCash == 0,
                "Negative cash values were not clamped.");
            Require(normalized.carry.resourceType == ResourceType.Wood
                    && normalized.carry.amount == 0,
                "Invalid CarryStack resource did not reset safely.");
            Require(normalized.purchasePads[0].completed
                    && normalized.purchasePads[0].paidCash == 120,
                "Completed pad was not normalized to exact cost.");
            Require(!normalized.purchasePads[1].completed
                    && normalized.purchasePads[1].paidCash == 239,
                "Partial pad was not clamped below completion cost.");
            Require(!normalized.purchasePads[2].completed
                    && normalized.purchasePads[2].paidCash == 0,
                "Impossible downstream unlock state was not removed.");
            Require(!normalized.lumberCampCompleted,
                "Completion survived without its unlock chain.");
            Require(normalized.stockpileWood == settings.stockpileCapacity
                    && normalized.processorInputWood == 0
                    && normalized.processorOutputPlanks == settings.processorOutputCapacity
                    && normalized.packingInputPlanks == 0
                    && normalized.packingOutputCrates == settings.packingOutputCapacity,
                "Negative/out-of-range machine buffers were not clamped.");
            Require(normalized.pendingOfflineCash == 0
                    && normalized.pendingOfflineAwaySeconds == 1000L,
                "Pending reward bounds were not normalized.");
            Require(normalized.lastEvaluationUtcUnixSeconds == BaselineUtc
                    && normalized.lastWriteUtcUnixSeconds == BaselineUtc,
                "Invalid UTC timestamps did not use the safe fallback.");

            M9SaveData duplicatePads = M9SaveData.CreateFresh(BaselineUtc);
            duplicatePads.purchasePads[1].id = duplicatePads.purchasePads[0].id;
            Require(!M9SaveValidator.TryNormalize(
                    duplicatePads,
                    settings,
                    BaselineUtc,
                    out _,
                    out _),
                "Duplicate/unknown PurchasePad identity was not rejected.");

            M9SaveData missingCarry = M9SaveData.CreateFresh(BaselineUtc);
            missingCarry.carry = null;
            Require(!M9SaveValidator.TryNormalize(
                    missingCarry,
                    settings,
                    BaselineUtc,
                    out _,
                    out _),
                "Missing canonical CarryStack record was not rejected.");
        }

        private static void TestStoreReset()
        {
            using (var scope = new TemporarySaveScope())
            {
                M9LocalSaveStore store = scope.CreateStore(
                    new ManualUtcClock(BaselineUtc));
                Require(store.Save(CreateFullyPopulatedSave()).IsSuccess,
                    "Reset test could not create its save.");
                Require(store.TryDeleteSave(out string failure),
                    "Save reset failed: " + failure);
                Require(!File.Exists(store.PrimaryPath)
                        && !File.Exists(store.TemporaryPath)
                        && !File.Exists(store.BackupPath),
                    "Save reset left a primary/temp/backup artifact.");

                M9SaveLoadResult load = store.Load();
                Require(load.Status == M9SaveLoadStatus.FreshNoSave,
                    "Reset save did not return to no-save state.");
                AssertFresh(load.Data, BaselineUtc);
            }
        }

        private static void TestVersion1Migration()
        {
            M9SaveDecodeResult result = M9SaveCodec.Decode(
                BuildVersion1Fixture(),
                CreateValidationSettings(),
                BaselineUtc);
            Require(result.IsSuccess && result.WasMigrated,
                "A valid M9 v1 save did not take the explicit migration path.");
            M9SaveData migrated = result.Data;
            Require(migrated.version == M9SaveSchema.Version2,
                "Migrated save did not become schema v2.");
            Require(migrated.walletCash == 731
                    && migrated.cashPileStoredCash == 219
                    && migrated.carry.resourceType == ResourceType.Crate
                    && migrated.carry.amount == 7,
                "Migration changed M9 economy or CarryStack state.");
            Require(migrated.stockpileWood == 17
                    && migrated.processorInputWood == 9
                    && migrated.processorOutputPlanks == 8
                    && migrated.packingInputPlanks == 11
                    && migrated.packingOutputCrates == 6
                    && migrated.pendingOfflineCash == 240
                    && migrated.pendingOfflineAwaySeconds == 7200L
                    && migrated.returnScreenPending,
                "Migration changed M9 buffers or pending-return state.");
            for (int i = 0; i < M9PurchasePadIds.Count; i++)
            {
                Require(migrated.purchasePads[i].completed
                        && migrated.purchasePads[i].paidCash
                        == M9PurchasePadIds.GetTotalCost(i),
                    $"Migration changed completed PurchasePad {i}.");
            }

            for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
            {
                Require(migrated.progression.GetMetric((ProgressMetricId)i) == 0L,
                    $"Migration invented historical metric {(ProgressMetricId)i}.");
            }

            Require(migrated.progression.GetFlag(
                        ProgressFlagId.ProductionUpgradeUnlocked)
                    && migrated.progression.GetFlag(ProgressFlagId.WorkerUnlocked)
                    && migrated.progression.GetFlag(ProgressFlagId.ProcessorUnlocked)
                    && migrated.progression.GetFlag(ProgressFlagId.AutoFeederUnlocked)
                    && migrated.progression.GetFlag(
                        ProgressFlagId.PackingStationUnlocked)
                    && migrated.progression.GetFlag(ProgressFlagId.CourierUnlocked)
                    && migrated.progression.GetFlag(
                        ProgressFlagId.LumberCampCompleted),
                "Migration did not seed exact canonical unlock/completion flags.");
            Require(migrated.progression.objectiveIndex == 2,
                "Zero historical metrics should stop migrated objectives at Produce Planks.");
            Require(migrated.progression.activeContractIndex == 0
                    && migrated.progression.activeContractBaseline == 0L
                    && migrated.progression.activeContractState
                    == ContractProgressState.Active,
                "Migration did not create the safe first-contract baseline.");

            LumberCampAchievementId[] grandfathered =
            {
                LumberCampAchievementId.FirstHire,
                LumberCampAchievementId.ProcessingBegins,
                LumberCampAchievementId.AutomationOnline,
                LumberCampAchievementId.DeliveryService,
                LumberCampAchievementId.FullyAutomatedInput,
                LumberCampAchievementId.LumberCampComplete
            };
            for (int i = 0; i < grandfathered.Length; i++)
            {
                M10AchievementSaveRecord record = migrated.progression
                    .FindAchievementRecord((int)grandfathered[i]);
                Require(record != null && record.unlocked && record.rewarded,
                    $"Exact migrated achievement {grandfathered[i]} was not grandfathered safely.");
            }

            Require(migrated.walletCash == 731,
                "Grandfathered achievements changed the migrated Wallet.");
        }

        private static void TestMigratedResave()
        {
            using (var scope = new TemporarySaveScope())
            {
                var clock = new ManualUtcClock(BaselineUtc);
                M9LocalSaveStore store = scope.CreateStore(clock);
                Directory.CreateDirectory(store.DirectoryPath);
                File.WriteAllText(store.PrimaryPath, BuildVersion1Fixture());

                M9SaveLoadResult migrated = store.Load();
                Require(migrated.LoadedExisting
                        && migrated.WasMigrated
                        && migrated.ShouldRewritePrimary,
                    "Store did not expose the required v1 rewrite state.");
                M9SaveWriteResult write = store.Save(migrated.Data);
                Require(write.IsSuccess && write.PersistedData.version == 2,
                    "Migrated state did not resave as schema v2.");

                M9SaveLoadResult secondLoad = store.Load();
                Require(secondLoad.Status == M9SaveLoadStatus.LoadedPrimary
                        && !secondLoad.WasMigrated
                        && !secondLoad.ShouldRewritePrimary,
                    "A resaved v2 file remigrated destructively.");
                AssertEquivalent(
                    write.PersistedData,
                    secondLoad.Data,
                    includeWriteTimestamp: true);
            }
        }

        private static void TestCorruptM10Fallback()
        {
            using (var scope = new TemporarySaveScope())
            {
                M9LocalSaveStore store = scope.CreateStore(
                    new ManualUtcClock(BaselineUtc));
                M9SaveData corrupt = M9SaveData.CreateFresh(BaselineUtc);
                corrupt.progression.metrics = new M10MetricSaveRecord[0];
                Directory.CreateDirectory(store.DirectoryPath);
                File.WriteAllText(
                    store.PrimaryPath,
                    JsonUtility.ToJson(corrupt));

                M9SaveLoadResult load = store.Load();
                Require(load.Status == M9SaveLoadStatus.FreshInvalidSave,
                    "Structurally corrupt M10 records bypassed controlled fallback.");
                AssertFresh(load.Data, BaselineUtc);
            }

            using (var scope = new TemporarySaveScope())
            {
                M9LocalSaveStore store = scope.CreateStore(
                    new ManualUtcClock(BaselineUtc));
                M9SaveData contradictory = M9SaveData.CreateFresh(BaselineUtc);
                contradictory.progression.flags[
                    (int)ProgressFlagId.WorkerUnlocked].value = true;
                Directory.CreateDirectory(store.DirectoryPath);
                File.WriteAllText(
                    store.PrimaryPath,
                    JsonUtility.ToJson(contradictory));

                M9SaveLoadResult load = store.Load();
                Require(load.Status == M9SaveLoadStatus.FreshInvalidSave,
                    "M10 unlock flag contradicting its PurchasePad bypassed fallback.");
                AssertFresh(load.Data, BaselineUtc);
            }
        }

        private static void TestElapsedRules()
        {
            OfflineProgressionRules rules = CreateDeterministicRules();
            OfflineProgressionResult backwards = OfflineProgressionCalculator.Calculate(
                CreateOfflineInput(BaselineUtc, BaselineUtc - 120L),
                rules);
            Require(backwards.ObservedAwaySeconds == 0L
                    && backwards.CreditedAwaySeconds == 0L
                    && backwards.EffectiveAutomationSeconds == 0d
                    && backwards.NextEvaluationUtcUnixSeconds == BaselineUtc,
                "Backward time awarded progression or moved the anchor backward.");

            OfflineProgressionResult invalid = OfflineProgressionCalculator.Calculate(
                CreateOfflineInput(-1L, BaselineUtc),
                rules);
            Require(!invalid.HadValidTimestamps
                    && invalid.ObservedAwaySeconds == 0L
                    && invalid.CreditedAwaySeconds == 0L,
                "Invalid time awarded offline progression.");

            OfflineProgressionResult capped = OfflineProgressionCalculator.Calculate(
                CreateOfflineInput(BaselineUtc, BaselineUtc + (5L * 60L * 60L)),
                rules);
            Require(capped.ObservedAwaySeconds == 5L * 60L * 60L,
                "Observed away interval changed unexpectedly.");
            Require(capped.CreditedAwaySeconds == 4L * 60L * 60L,
                "Away time was not capped at four hours.");
            RequireNearly(capped.EffectiveAutomationSeconds, 8640d, 0.0001d,
                "Offline efficiency was not exactly 60%.");
            Require(OfflineProgressionCalculator.CalculateCreditedAwaySeconds(
                        BaselineUtc,
                        BaselineUtc + 20000L)
                    == OfflineProgressionRules.FourHoursInSeconds,
                "Standalone credited-time calculation ignored the cap.");
        }

        private static void TestAutomationGates()
        {
            OfflineProgressionRules rules = CreateDeterministicRules();
            OfflineProgressionInput locked = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            locked.StockpileWood = 1;
            OfflineProgressionResult lockedResult =
                OfflineProgressionCalculator.Calculate(locked, rules);
            Require(lockedResult.WorkerWoodCollected == 0
                    && lockedResult.StockpileWood == 1,
                "Pre-Worker state invented autonomous Wood.");

            OfflineProgressionInput worker = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            worker.WorkerUnlocked = true;
            worker.StockpileCapacity = 5;
            OfflineProgressionResult workerResult =
                OfflineProgressionCalculator.Calculate(worker, rules);
            Require(workerResult.WorkerWoodCollected == 5
                    && workerResult.StockpileWood == 5,
                "Worker offline collection did not respect rate/capacity.");

            OfflineProgressionInput nearlyFull = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            nearlyFull.WorkerUnlocked = true;
            nearlyFull.StockpileWood = 4;
            nearlyFull.StockpileCapacity = 5;
            OfflineProgressionResult capped =
                OfflineProgressionCalculator.Calculate(nearlyFull, rules);
            Require(capped.WorkerWoodCollected == 1 && capped.StockpileWood == 5,
                "Worker exceeded Stockpile capacity.");
        }

        private static void TestProcessorBoundaries()
        {
            OfflineProgressionRules rules = CreateDeterministicRules();
            OfflineProgressionInput onlyProcessor = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            onlyProcessor.ProcessorUnlocked = true;
            onlyProcessor.StockpileWood = 10;
            RequireNoProcessorAutomation(
                OfflineProgressionCalculator.Calculate(onlyProcessor, rules),
                10,
                "Processor ran without Auto Feeder.");

            OfflineProgressionInput onlyFeeder = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            onlyFeeder.AutoFeederUnlocked = true;
            onlyFeeder.StockpileWood = 10;
            RequireNoProcessorAutomation(
                OfflineProgressionCalculator.Calculate(onlyFeeder, rules),
                10,
                "Auto Feeder ran without Processor unlock.");

            OfflineProgressionInput both = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            both.ProcessorUnlocked = true;
            both.AutoFeederUnlocked = true;
            both.StockpileWood = 10;
            OfflineProgressionResult progressed =
                OfflineProgressionCalculator.Calculate(both, rules);
            Require(progressed.FeederWoodTransferred == 6
                    && progressed.ProcessorRecipesCompleted == 3
                    && progressed.ProcessorPlanksProduced == 3
                    && progressed.StockpileWood == 4
                    && progressed.ProcessorInputWood == 0
                    && progressed.ProcessorOutputPlanks == 3,
                "Feeder/Processor aggregate settlement was not deterministic.");

            OfflineProgressionInput outputFull = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            outputFull.ProcessorUnlocked = true;
            outputFull.AutoFeederUnlocked = true;
            outputFull.StockpileWood = 5;
            outputFull.ProcessorInputWood = 4;
            outputFull.ProcessorInputCapacity = 10;
            outputFull.ProcessorOutputPlanks = 2;
            outputFull.ProcessorOutputCapacity = 2;
            OfflineProgressionResult stopped =
                OfflineProgressionCalculator.Calculate(outputFull, rules);
            Require(stopped.ProcessorRecipesCompleted == 0
                    && stopped.ProcessorPlanksProduced == 0
                    && stopped.ProcessorOutputPlanks == 2,
                "Processor processed while its output was full.");
            Require(stopped.StockpileWood + stopped.ProcessorInputWood == 9,
                "Output-full feeder reconciliation lost/duplicated Wood.");
        }

        private static void TestPackingBoundaries()
        {
            OfflineProgressionRules rules = CreateDeterministicRules();
            OfflineProgressionInput noTransfer = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            noTransfer.ProcessorUnlocked = true;
            noTransfer.AutoFeederUnlocked = true;
            noTransfer.PackingUnlocked = true;
            noTransfer.StockpileWood = 6;
            OfflineProgressionResult isolated =
                OfflineProgressionCalculator.Calculate(noTransfer, rules);
            Require(isolated.ProcessorOutputPlanks == 3
                    && isolated.PackingInputPlanks == 0
                    && isolated.PackingOutputCrates == 0
                    && isolated.PackingRecipesCompleted == 0,
                "Processor Planks moved magically into Packing.");

            OfflineProgressionInput existingInput = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            existingInput.PackingUnlocked = true;
            existingInput.ProcessorOutputPlanks = 7;
            existingInput.PackingInputPlanks = 5;
            OfflineProgressionResult packed =
                OfflineProgressionCalculator.Calculate(existingInput, rules);
            Require(packed.PackingRecipesCompleted == 2
                    && packed.PackingInputPlanks == 1
                    && packed.PackingCratesProduced == 2
                    && packed.PackingOutputCrates == 2,
                "Packing did not consume only its pre-existing input in exact batches.");
            Require(packed.ProcessorOutputPlanks == 7,
                "Packing consumed Processor output without automation.");
        }

        private static void TestCourierSettlement()
        {
            OfflineProgressionRules rules = CreateDeterministicRules();
            OfflineProgressionInput legitimate = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            legitimate.PackingUnlocked = true;
            legitimate.CourierUnlocked = true;
            legitimate.PackingOutputCrates = 3;
            OfflineProgressionResult delivered =
                OfflineProgressionCalculator.Calculate(legitimate, rules);
            Require(delivered.CourierCratesDelivered == 3
                    && delivered.PackingOutputCrates == 0
                    && delivered.OfflineCashEarned == 120
                    && delivered.PendingOfflineCash == 120,
                "Courier did not deliver legitimate Crates at exactly $40 each.");

            OfflineProgressionInput packedThenDelivered = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            packedThenDelivered.PackingUnlocked = true;
            packedThenDelivered.CourierUnlocked = true;
            packedThenDelivered.PackingInputPlanks = 4;
            OfflineProgressionResult sameInterval =
                OfflineProgressionCalculator.Calculate(packedThenDelivered, rules);
            Require(sameInterval.PackingCratesProduced == 2
                    && sameInterval.CourierCratesDelivered == 2
                    && sameInterval.OfflineCashEarned == 80
                    && sameInterval.PackingInputPlanks == 0
                    && sameInterval.PackingOutputCrates == 0,
                "Courier did not settle Crates legitimately made from loaded Packing input.");

            OfflineProgressionInput impossible = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            impossible.PackingUnlocked = true;
            impossible.CourierUnlocked = true;
            OfflineProgressionResult none =
                OfflineProgressionCalculator.Calculate(impossible, rules);
            Require(none.CourierCratesDelivered == 0
                    && none.OfflineCashEarned == 0
                    && none.PendingOfflineCash == 0,
                "Courier invented Crates or Cash without a legitimate source.");

            OfflineProgressionInput courierBeforePacking = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 10L);
            courierBeforePacking.CourierUnlocked = true;
            courierBeforePacking.PackingOutputCrates = 3;
            OfflineProgressionResult gated =
                OfflineProgressionCalculator.Calculate(courierBeforePacking, rules);
            Require(gated.CourierCratesDelivered == 0 && gated.OfflineCashEarned == 0,
                "Courier ran without its Packing prerequisite.");
        }

        private static void TestReturnAndDuplicateGuard()
        {
            OfflineProgressionRules rules = CreateDeterministicRules();
            OfflineProgressionResult shortAway = OfflineProgressionCalculator.Calculate(
                CreateOfflineInput(BaselineUtc, BaselineUtc + 299L),
                rules);
            Require(!shortAway.ReturnScreenPending,
                "Return screen appeared below its five-minute threshold.");

            OfflineProgressionResult meaningful = OfflineProgressionCalculator.Calculate(
                CreateOfflineInput(BaselineUtc, BaselineUtc + 300L),
                rules);
            Require(meaningful.ReturnScreenPending,
                "Return screen did not appear at the configured threshold.");

            OfflineProgressionInput pending = CreateOfflineInput(
                BaselineUtc,
                BaselineUtc + 3600L);
            pending.ReturnScreenPending = true;
            pending.PendingOfflineCash = 160;
            pending.PendingOfflineAwaySeconds = 600L;
            pending.WorkerUnlocked = true;
            pending.StockpileWood = 3;
            OfflineProgressionResult skipped =
                OfflineProgressionCalculator.Calculate(pending, rules);
            Require(skipped.SkippedBecauseReturnPending,
                "Outstanding settlement was not recognized.");
            Require(skipped.PendingOfflineCash == 160
                    && skipped.PendingOfflineAwaySeconds == 600L
                    && skipped.ReturnScreenPending
                    && skipped.StockpileWood == 3
                    && skipped.WorkerWoodCollected == 0
                    && skipped.OfflineCashEarned == 0,
                "Reloading an outstanding return mutated or duplicated its settlement.");
            Require(skipped.NextEvaluationUtcUnixSeconds == BaselineUtc + 3600L,
                "Duplicate guard did not advance the evaluation anchor monotonically.");
        }

        private static void TestPendingAndCollection()
        {
            using (var scope = new TemporarySaveScope())
            {
                var clock = new ManualUtcClock(BaselineUtc);
                M9LocalSaveStore store = scope.CreateStore(clock);
                M9SaveData pending = M9SaveData.CreateFresh(BaselineUtc);
                pending.pendingOfflineCash = 120;
                pending.pendingOfflineAwaySeconds = 7200L;
                pending.returnScreenPending = true;
                Require(store.Save(pending).IsSuccess,
                    "Pending reward could not be persisted.");

                M9SaveData reloaded = store.Load().Data;
                Require(reloaded.pendingOfflineCash == 120
                        && reloaded.pendingOfflineAwaySeconds == 7200L
                        && reloaded.returnScreenPending,
                    "Pending reward did not survive closing before COLLECT.");

                Require(OfflineRewardCollection.TryCollect(
                        reloaded,
                        50,
                        1d,
                        out int credited,
                        out int resultingWallet),
                    "Valid 1x COLLECT failed.");
                Require(credited == 120
                        && resultingWallet == 170
                        && reloaded.pendingOfflineCash == 0
                        && reloaded.pendingOfflineAwaySeconds == 0L
                        && !reloaded.returnScreenPending,
                    "COLLECT did not transfer/clear the reward atomically.");
                Require(!OfflineRewardCollection.TryCollect(
                        reloaded,
                        resultingWallet,
                        1d,
                        out int secondCredit,
                        out int secondWallet)
                        && secondCredit == 0
                        && secondWallet == resultingWallet,
                    "A second COLLECT duplicated the reward.");

                Require(store.Save(reloaded).IsSuccess,
                    "Collected state could not be persisted.");
                M9SaveData collectedReload = store.Load().Data;
                Require(collectedReload.pendingOfflineCash == 0
                        && collectedReload.pendingOfflineAwaySeconds == 0L
                        && !collectedReload.returnScreenPending,
                    "Collected reward returned after save/load.");

                M9SaveData zero = M9SaveData.CreateFresh(BaselineUtc);
                zero.returnScreenPending = true;
                Require(OfflineRewardCollection.TryCollect(
                        zero,
                        10,
                        1d,
                        out int zeroCredit,
                        out int unchangedWallet)
                        && zeroCredit == 0
                        && unchangedWallet == 10
                        && !zero.returnScreenPending,
                    "Zero-value return could not be collected safely.");

                M9SaveData invalidMultiplier = M9SaveData.CreateFresh(BaselineUtc);
                invalidMultiplier.returnScreenPending = true;
                invalidMultiplier.pendingOfflineCash = 40;
                Require(!OfflineRewardCollection.TryCollect(
                        invalidMultiplier,
                        10,
                        double.NaN,
                        out _,
                        out _)
                        && invalidMultiplier.pendingOfflineCash == 40
                        && invalidMultiplier.returnScreenPending,
                    "Invalid multiplier mutated the pending reward.");

                M9SaveData overflow = M9SaveData.CreateFresh(BaselineUtc);
                overflow.returnScreenPending = true;
                overflow.pendingOfflineCash = 2;
                overflow.pendingOfflineAwaySeconds = 300L;
                Require(!OfflineRewardCollection.TryCollect(
                        overflow,
                        int.MaxValue,
                        1d,
                        out int overflowCredit,
                        out int overflowWallet)
                        && overflowCredit == 0
                        && overflowWallet == int.MaxValue
                        && overflow.pendingOfflineCash == 2
                        && overflow.pendingOfflineAwaySeconds == 300L
                        && overflow.returnScreenPending,
                    "Overflowing COLLECT mutated or credited the reward.");
            }
        }

        private static OfflineProgressionInput CreateOfflineInput(long last, long now)
        {
            return new OfflineProgressionInput
            {
                LastEvaluationUtcUnixSeconds = last,
                NowUtcUnixSeconds = now,
                StockpileCapacity = 30,
                ProcessorInputCapacity = 24,
                ProcessorOutputCapacity = 12,
                PackingInputCapacity = 24,
                PackingOutputCapacity = 12
            };
        }

        private static OfflineProgressionRules CreateDeterministicRules()
        {
            return new OfflineProgressionRules
            {
                MaximumCreditedAwaySeconds = OfflineProgressionRules.FourHoursInSeconds,
                OfflineEfficiency = 0.60d,
                ReturnScreenThresholdSeconds = 300L,
                WoodProductionSecondsPerWood = 1d,
                WorkerCollectionSecondsPerWood = 1d,
                FeederTransferSecondsPerWood = 1d,
                ProcessorSecondsPerRecipe = 1d,
                PackingSecondsPerRecipe = 1d,
                CourierSecondsPerTrip = 1d,
                ProcessorWoodPerRecipe = 2,
                ProcessorPlanksPerRecipe = 1,
                PackingPlanksPerRecipe = 2,
                PackingCratesPerRecipe = 1,
                CourierCratesPerTrip = 2,
                CashPerDeliveredCrate = 40
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
                packingOutputCapacity = 12
            };
        }

        private static M9SaveData CreateFullyPopulatedSave()
        {
            M9SaveData data = M9SaveData.CreateFresh(BaselineUtc);
            data.walletCash = 731;
            data.cashPileStoredCash = 219;
            data.carry.resourceType = ResourceType.Crate;
            data.carry.amount = 7;
            for (int i = 0; i < M9PurchasePadIds.Count; i++)
            {
                CompletePad(data, i);
            }

            data.lumberCampCompleted = true;
            data.stockpileWood = 17;
            data.processorInputWood = 9;
            data.processorOutputPlanks = 8;
            data.packingInputPlanks = 11;
            data.packingOutputCrates = 6;
            data.pendingOfflineCash = 240;
            data.pendingOfflineAwaySeconds = 7200L;
            data.returnScreenPending = true;
            M10ProgressionSaveData progression = data.progression;
            for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
            {
                progression.metrics[i].value = (i + 1L) * 17L;
            }

            for (int i = 0; i < LumberCampProgressionCatalog.FlagCount; i++)
            {
                progression.flags[i].value = true;
            }

            progression.claimedContracts[0] = true;
            progression.objectiveIndex = LumberCampProgressionCatalog.ObjectiveCount;
            progression.activeContractIndex = 1;
            progression.activeContractBaseline = 80L;
            progression.activeContractState = ContractProgressState.Active;
            for (int i = 0; i < progression.achievements.Length; i++)
            {
                progression.achievements[i].unlocked = i < 6;
                progression.achievements[i].rewarded = i < 5;
            }

            return data;
        }

        private static string BuildVersion1Fixture()
        {
            // Literal legacy fixture: it intentionally contains no M10 fields.
            return "{"
                   + "\"schema\":\"industry-tycoon-local-save\","
                   + "\"version\":1,"
                   + "\"walletCash\":731,"
                   + "\"cashPileStoredCash\":219,"
                   + "\"carry\":{\"resourceType\":2,\"amount\":7},"
                   + "\"purchasePads\":["
                   + "{\"id\":\"production_upgrade\",\"paidCash\":120,\"completed\":true},"
                   + "{\"id\":\"lumber_worker\",\"paidCash\":240,\"completed\":true},"
                   + "{\"id\":\"wood_processor\",\"paidCash\":360,\"completed\":true},"
                   + "{\"id\":\"auto_feeder\",\"paidCash\":600,\"completed\":true},"
                   + "{\"id\":\"packing_station\",\"paidCash\":900,\"completed\":true},"
                   + "{\"id\":\"delivery_courier\",\"paidCash\":1500,\"completed\":true}],"
                   + "\"lumberCampCompleted\":true,"
                   + "\"stockpileWood\":17,"
                   + "\"processorInputWood\":9,"
                   + "\"processorOutputPlanks\":8,"
                   + "\"packingInputPlanks\":11,"
                   + "\"packingOutputCrates\":6,"
                   + "\"pendingOfflineCash\":240,"
                   + "\"pendingOfflineAwaySeconds\":7200,"
                   + "\"returnScreenPending\":true,"
                   + "\"lastEvaluationUtcUnixSeconds\":2000000000,"
                   + "\"lastWriteUtcUnixSeconds\":2000000000"
                   + "}";
        }

        private static void CompletePad(M9SaveData data, int index)
        {
            data.purchasePads[index].completed = true;
            data.purchasePads[index].paidCash = M9PurchasePadIds.GetTotalCost(index);
        }

        private static void AssertFresh(M9SaveData data, long expectedTimestamp)
        {
            Require(data != null, "Fresh save data is null.");
            Require(data.schema == M9SaveSchema.Id
                    && data.version == M9SaveSchema.CurrentVersion,
                "Fresh save schema/version is invalid.");
            Require(data.walletCash == 0
                    && data.cashPileStoredCash == 0
                    && data.carry != null
                    && data.carry.resourceType == ResourceType.Wood
                    && data.carry.amount == 0,
                "Fresh economy/CarryStack state differs from M8.");
            Require(data.purchasePads != null
                    && data.purchasePads.Length == M9PurchasePadIds.Count,
                "Fresh save does not contain the canonical pad set.");
            for (int i = 0; i < M9PurchasePadIds.Count; i++)
            {
                Require(data.purchasePads[i] != null
                        && data.purchasePads[i].id == M9PurchasePadIds.GetId(i)
                        && data.purchasePads[i].paidCash == 0
                        && !data.purchasePads[i].completed,
                    $"Fresh PurchasePad {i} is not exact.");
            }

            Require(!data.lumberCampCompleted
                    && data.stockpileWood == 0
                    && data.processorInputWood == 0
                    && data.processorOutputPlanks == 0
                    && data.packingInputPlanks == 0
                    && data.packingOutputCrates == 0,
                "Fresh progression/buffer state differs from M8.");
            Require(data.pendingOfflineCash == 0
                    && data.pendingOfflineAwaySeconds == 0L
                    && !data.returnScreenPending,
                "Fresh save contains a pending return reward.");
            Require(data.lastEvaluationUtcUnixSeconds == expectedTimestamp
                    && data.lastWriteUtcUnixSeconds == expectedTimestamp,
                "Fresh timestamps were not initialized from the injected UTC clock.");
            Require(data.progression != null
                    && data.progression.metrics.Length
                    == LumberCampProgressionCatalog.MetricCount
                    && data.progression.flags.Length
                    == LumberCampProgressionCatalog.FlagCount
                    && data.progression.achievements.Length
                    == LumberCampProgressionCatalog.AchievementCount
                    && data.progression.claimedContracts.Length
                    == LumberCampProgressionCatalog.ContractCount,
                "Fresh M10 canonical record sets are missing.");
            for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
            {
                Require(data.progression.GetMetric((ProgressMetricId)i) == 0L,
                    $"Fresh metric {(ProgressMetricId)i} is nonzero.");
            }

            for (int i = 0; i < LumberCampProgressionCatalog.FlagCount; i++)
            {
                Require(!data.progression.GetFlag((ProgressFlagId)i),
                    $"Fresh flag {(ProgressFlagId)i} is set.");
            }

            Require(data.progression.objectiveIndex == 0
                    && data.progression.activeContractIndex == 0
                    && data.progression.activeContractBaseline == 0L
                    && data.progression.activeContractState
                    == ContractProgressState.Active,
                "Fresh objective or first-contract state is not exact.");
            for (int i = 0; i < LumberCampProgressionCatalog.ContractCount; i++)
            {
                Require(!data.progression.claimedContracts[i],
                    $"Fresh contract {i} is already claimed.");
            }

            for (int i = 0; i < LumberCampProgressionCatalog.AchievementCount; i++)
            {
                M10AchievementSaveRecord record =
                    data.progression.FindAchievementRecord(i);
                Require(record != null && !record.unlocked && !record.rewarded,
                    $"Fresh achievement {i} is not locked/unrewarded.");
            }
        }

        private static void AssertEquivalent(
            M9SaveData expected,
            M9SaveData actual,
            bool includeWriteTimestamp)
        {
            Require(expected != null && actual != null, "Save comparison received null data.");
            Require(expected.schema == actual.schema && expected.version == actual.version,
                "Schema/version changed during persistence.");
            Require(expected.walletCash == actual.walletCash
                    && expected.cashPileStoredCash == actual.cashPileStoredCash,
                "Cash state changed during persistence.");
            Require(expected.carry.resourceType == actual.carry.resourceType
                    && expected.carry.amount == actual.carry.amount,
                "CarryStack state changed during persistence.");
            Require(expected.purchasePads.Length == actual.purchasePads.Length,
                "PurchasePad count changed during persistence.");
            for (int i = 0; i < expected.purchasePads.Length; i++)
            {
                Require(expected.purchasePads[i].id == actual.purchasePads[i].id
                        && expected.purchasePads[i].paidCash
                        == actual.purchasePads[i].paidCash
                        && expected.purchasePads[i].completed
                        == actual.purchasePads[i].completed,
                    $"PurchasePad {i} changed during persistence.");
            }

            Require(expected.lumberCampCompleted == actual.lumberCampCompleted
                    && expected.stockpileWood == actual.stockpileWood
                    && expected.processorInputWood == actual.processorInputWood
                    && expected.processorOutputPlanks == actual.processorOutputPlanks
                    && expected.packingInputPlanks == actual.packingInputPlanks
                    && expected.packingOutputCrates == actual.packingOutputCrates,
                "Completion or machine buffers changed during persistence.");
            Require(expected.pendingOfflineCash == actual.pendingOfflineCash
                    && expected.pendingOfflineAwaySeconds
                    == actual.pendingOfflineAwaySeconds
                    && expected.returnScreenPending == actual.returnScreenPending,
                "Pending return state changed during persistence.");
            Require(expected.lastEvaluationUtcUnixSeconds
                    == actual.lastEvaluationUtcUnixSeconds,
                "Evaluation timestamp changed during persistence.");
            if (includeWriteTimestamp)
            {
                Require(expected.lastWriteUtcUnixSeconds
                        == actual.lastWriteUtcUnixSeconds,
                    "Write timestamp changed unexpectedly.");
            }

            AssertProgressionEquivalent(expected.progression, actual.progression);
        }

        private static void AssertProgressionEquivalent(
            M10ProgressionSaveData expected,
            M10ProgressionSaveData actual)
        {
            Require(expected != null && actual != null,
                "M10 progression comparison received null data.");
            for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
            {
                ProgressMetricId metric = (ProgressMetricId)i;
                Require(expected.GetMetric(metric) == actual.GetMetric(metric),
                    $"Metric {metric} changed during persistence.");
            }

            for (int i = 0; i < LumberCampProgressionCatalog.FlagCount; i++)
            {
                ProgressFlagId flag = (ProgressFlagId)i;
                Require(expected.GetFlag(flag) == actual.GetFlag(flag),
                    $"Flag {flag} changed during persistence.");
            }

            Require(expected.objectiveIndex == actual.objectiveIndex
                    && expected.activeContractIndex == actual.activeContractIndex
                    && expected.activeContractBaseline
                    == actual.activeContractBaseline
                    && expected.activeContractState == actual.activeContractState,
                "Objective or active Contract state changed during persistence.");
            for (int i = 0; i < LumberCampProgressionCatalog.ContractCount; i++)
            {
                Require(expected.claimedContracts[i]
                        == actual.claimedContracts[i],
                    $"Contract claim flag {i} changed during persistence.");
            }

            for (int i = 0; i < LumberCampProgressionCatalog.AchievementCount; i++)
            {
                M10AchievementSaveRecord expectedRecord =
                    expected.FindAchievementRecord(i);
                M10AchievementSaveRecord actualRecord =
                    actual.FindAchievementRecord(i);
                Require(expectedRecord != null
                        && actualRecord != null
                        && expectedRecord.unlocked == actualRecord.unlocked
                        && expectedRecord.rewarded == actualRecord.rewarded,
                    $"Achievement state {i} changed during persistence.");
            }
        }

        private static long CalculateLogicalChecksum(M9SaveData data)
        {
            long checksum = data.walletCash;
            checksum = (checksum * 397L) + data.cashPileStoredCash;
            checksum = (checksum * 397L) + data.carry.amount;
            checksum = (checksum * 397L) + (int)data.carry.resourceType;
            checksum = (checksum * 397L) + data.stockpileWood;
            checksum = (checksum * 397L) + data.processorInputWood;
            checksum = (checksum * 397L) + data.processorOutputPlanks;
            checksum = (checksum * 397L) + data.packingInputPlanks;
            checksum = (checksum * 397L) + data.packingOutputCrates;
            checksum = (checksum * 397L) + data.pendingOfflineCash;
            for (int i = 0; i < data.purchasePads.Length; i++)
            {
                checksum = (checksum * 397L) + data.purchasePads[i].paidCash;
                checksum = (checksum * 397L) + (data.purchasePads[i].completed ? 1 : 0);
            }

            if (data.progression != null)
            {
                for (int i = 0; i < LumberCampProgressionCatalog.MetricCount; i++)
                {
                    checksum = (checksum * 397L)
                               + data.progression.GetMetric((ProgressMetricId)i);
                }

                for (int i = 0; i < LumberCampProgressionCatalog.FlagCount; i++)
                {
                    checksum = (checksum * 397L)
                               + (data.progression.GetFlag((ProgressFlagId)i) ? 1 : 0);
                }

                checksum = (checksum * 397L) + data.progression.objectiveIndex;
                checksum = (checksum * 397L) + data.progression.activeContractIndex;
                checksum = (checksum * 397L) + data.progression.activeContractBaseline;
            }

            return checksum;
        }

        private static void RequireNoProcessorAutomation(
            OfflineProgressionResult result,
            int expectedStockpile,
            string message)
        {
            Require(result.FeederWoodTransferred == 0
                    && result.ProcessorRecipesCompleted == 0
                    && result.ProcessorPlanksProduced == 0
                    && result.StockpileWood == expectedStockpile,
                message);
        }

        private static void Run(string name, Action test)
        {
            test();
            _testCount++;
            Debug.Log($"M9 deterministic test PASS: {name}");
        }

        private static void Require(bool condition, string message)
        {
            _assertionCount++;
            if (!condition)
            {
                throw new InvalidOperationException("M9 deterministic test failed: " + message);
            }
        }

        private static void RequireNearly(
            double actual,
            double expected,
            double tolerance,
            string message)
        {
            Require(Math.Abs(actual - expected) <= tolerance,
                $"{message} Expected {expected}, got {actual}.");
        }

        private sealed class TemporarySaveScope : IDisposable
        {
            private readonly string _rootPath;

            public TemporarySaveScope()
            {
                string parent = Path.Combine(
                    Path.GetTempPath(),
                    "IndustryTycoonM9DeterministicTests");
                _rootPath = Path.Combine(parent, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_rootPath);
            }

            public M9LocalSaveStore CreateStore(ManualUtcClock clock)
            {
                return new M9LocalSaveStore(
                    _rootPath,
                    clock,
                    CreateValidationSettings(),
                    M9LocalSaveStore.DefaultFileName,
                    preserveInvalidFiles: false);
            }

            public void Dispose()
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, recursive: true);
                }
            }
        }
    }
}
