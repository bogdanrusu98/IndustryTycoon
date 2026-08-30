using System;
using System.IO;
using IndustryTycoon.Core;
using IndustryTycoon.Interaction;
using IndustryTycoon.Mining;
using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using IndustryTycoon.Progression;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndustryTycoon.Editor
{
    /// <summary>
    /// Fast, deterministic M11 coverage. The suite uses only temporary save files
    /// and never enters Play Mode or touches the player's persistent-data path.
    /// Timed machine ownership is covered by the focused M11 Play Mode smoke.
    /// </summary>
    public static class LumberCampM11Tests
    {
        private const long BaselineUtc = 1700000000L;
        private const int Version2MetricCount = 10;
        private const int Version2FlagCount = 7;

        private static int _assertionCount;
        private static int _testCount;

        [Serializable]
        private sealed class Version2Fixture
        {
            public string schema;
            public int version;
            public int walletCash;
            public int cashPileStoredCash;
            public M9CarrySaveRecord carry;
            public M9PurchasePadSaveRecord[] purchasePads;
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
            public M10ProgressionSaveData progression;
        }

        [MenuItem("Industry Tycoon/Prototype/Run M11 Deterministic Tests")]
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

            Run("M11 catalog, objective order, costs, and sale values",
                TestCatalogEconomyAndObjectiveOrder);
            Run("single-resource CarryStack Mining isolation",
                TestCarryStackMiningTypeIsolation);
            Run("Mine unlock idempotence and completed-save synchronization",
                TestMineUnlockOnce);
            Run("authoritative Mining metrics and reload stability",
                TestMiningMetricCommits);
            Run("real v2 to v3 migration with completed Lumber Camp",
                TestVersion2ToVersion3Migration);
            Run("canonical Mining persistence and transient exclusion",
                TestMiningPersistenceAndCanonicalState);
            Run("Mining local-store round trip and Reset Save",
                TestStoreRoundTripAndReset);

            Debug.Log(
                $"M11 deterministic tests PASS: {_testCount} tests, "
                + $"{_assertionCount} assertions.");
        }

        private static void TestCatalogEconomyAndObjectiveOrder()
        {
            Require(M9SaveSchema.CurrentVersion == M9SaveSchema.Version3,
                "M11 must own save schema v3.");
            Require((int)ResourceType.IronOre == 3
                    && (int)ResourceType.IronBar == 4,
                "Mining resources must remain append-only after the M1-M10 IDs.");
            Require(LumberCampProgressionCatalog.MetricCount == 17
                    && LumberCampProgressionCatalog.FlagCount == 8
                    && LumberCampProgressionCatalog.ObjectiveCount == 13,
                "The M11 metric, flag, or objective catalog is incomplete.");

            MainObjectiveId[] expectedIds =
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
            long[] expectedTargets =
            {
                1L, 1L, 10L, 1L, 1L, 5L, 1L,
                1L, 5L, 10L, 1L, 5L, 1L
            };
            for (int i = 0; i < expectedIds.Length; i++)
            {
                MainObjectiveDefinition objective =
                    LumberCampProgressionCatalog.GetObjective(i);
                Require(objective.Id == expectedIds[i]
                        && objective.Target == expectedTargets[i],
                    $"Objective {i} has the wrong M11 identity or target.");
            }

            Require(M11MiningPurchasePadIds.GetTotalCost(
                        M11MiningPurchasePadIds.SmelterIndex) == 1200
                    && M11MiningPurchasePadIds.GetTotalCost(
                        M11MiningPurchasePadIds.AutomatedDrillIndex) == 2400,
                "Smelter or Automated Drill baseline cost changed.");
            Require(string.Equals(
                        LumberCampProgressionCatalog.GetMetricStableId(
                            ProgressMetricId.IronOreMined),
                        "iron_ore_mined",
                        StringComparison.Ordinal)
                    && string.Equals(
                        LumberCampProgressionCatalog.GetMetricStableId(
                            ProgressMetricId.DrillUnlocked),
                        "drill_unlocked",
                        StringComparison.Ordinal),
                "Mining metric stable IDs changed.");

            GameObject saleObject = new GameObject("M11 Sale Value Fixture");
            try
            {
                saleObject.AddComponent<BoxCollider>();
                SalePoint salePoint = saleObject.AddComponent<SalePoint>();
                Require(salePoint.IronOreValue == 10
                        && salePoint.GetUnitValue(ResourceType.IronOre) == 10,
                    "Iron Ore must sell for exactly $10.");
                Require(salePoint.IronBarValue == 30
                        && salePoint.GetUnitValue(ResourceType.IronBar) == 30,
                    "Iron Bar must sell for exactly $30.");
            }
            finally
            {
                Object.DestroyImmediate(saleObject);
            }
        }

        private static void TestCarryStackMiningTypeIsolation()
        {
            GameObject carryObject = new GameObject("M11 CarryStack Fixture");
            try
            {
                CarryStack carry = carryObject.AddComponent<CarryStack>();
                Require(carry.Capacity == 12
                        && carry.TryAdd(ResourceType.IronOre, carry.Capacity),
                    "CarryStack could not fill atomically with Iron Ore.");
                Require(!carry.CanAccept(ResourceType.IronOre, 1)
                        && !carry.TryAdd(ResourceType.IronOre, 1)
                        && !carry.TryAdd(ResourceType.IronBar, 1)
                        && !carry.TryAdd(ResourceType.Wood, 1),
                    "A full or Ore-typed CarryStack accepted another resource.");
                Require(carry.GetAmount(ResourceType.IronOre) == carry.Capacity
                        && carry.TotalAmount == carry.Capacity,
                    "Rejected additions changed the Ore amount.");

                Require(carry.TryRemove(ResourceType.IronOre, carry.Capacity)
                        && carry.TotalAmount == 0
                        && !carry.HasActiveResource,
                    "Removing all Ore did not clear the active resource type.");
                Require(carry.TryReserveCapacity(ResourceType.IronBar, 1)
                        && carry.ReservedResourceType == ResourceType.IronBar
                        && !carry.CanAccept(ResourceType.IronOre, 1),
                    "An Iron Bar reservation did not isolate CarryStack type.");
                Require(carry.TryCommitReservedAdd(ResourceType.IronBar, 1)
                        && carry.GetAmount(ResourceType.IronBar) == 1
                        && carry.ReservedCapacity == 0
                        && !carry.CanAccept(ResourceType.IronOre, 1),
                    "Reserved Bar commit was not atomic or type-safe.");
            }
            finally
            {
                Object.DestroyImmediate(carryObject);
            }
        }

        private static void TestMineUnlockOnce()
        {
            GameObject owner = new GameObject("M11 Mine Unlock Fixture");
            GameObject lockedRoot = new GameObject("Locked Teaser");
            GameObject mineRoot = new GameObject("Mine Area");
            lockedRoot.transform.SetParent(owner.transform);
            mineRoot.transform.SetParent(owner.transform);
            try
            {
                LumberCampCompletion completion =
                    owner.AddComponent<LumberCampCompletion>();
                MineUnlock mineUnlock = owner.AddComponent<MineUnlock>();
                SetObjectReference(
                    mineUnlock,
                    "lumberCampCompletion",
                    completion);
                SetObjectReference(mineUnlock, "lockedTeaserRoot", lockedRoot);
                SetObjectReference(mineUnlock, "mineAreaRoot", mineRoot);

                completion.RestoreCompleted(false);
                mineUnlock.RestoreUnlocked(false);
                Require(!mineUnlock.TryUnlock()
                        && !mineUnlock.IsUnlocked
                        && mineUnlock.UnlockCount == 0,
                    "Mine unlocked before authoritative Lumber completion.");

                completion.RestoreCompleted(true);
                mineUnlock.RestoreUnlocked(false);
                Require(lockedRoot.activeSelf && !mineRoot.activeSelf,
                    "Completed Lumber save did not present the locked Mine boundary.");
                int unlockEvents = 0;
                mineUnlock.Unlocked += () => unlockEvents++;
                Require(mineUnlock.TryUnlock()
                        && mineUnlock.IsUnlocked
                        && mineUnlock.UnlockCount == 1
                        && unlockEvents == 1
                        && !lockedRoot.activeSelf
                        && mineRoot.activeSelf,
                    "First Mine unlock did not commit and reveal exactly once.");
                Require(!mineUnlock.TryUnlock()
                        && mineUnlock.UnlockCount == 1
                        && unlockEvents == 1,
                    "Repeated Mine unlock duplicated its commit or event.");

                mineUnlock.RestoreUnlocked(true);
                mineUnlock.SynchronizeFromCompletionState();
                Require(mineUnlock.UnlockCount == 1
                        && unlockEvents == 1
                        && mineRoot.activeSelf,
                    "Restoring an unlocked Mine fabricated another unlock event.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static void TestMiningMetricCommits()
        {
            var rewards = new FakeRewardWallet();
            var model = new LumberCampProgressionModel(
                M10ProgressionSaveData.CreateFresh(),
                rewards.TryGrant);

            Require(model.RecordIronOreMined(10),
                "Manual Ore commit was rejected.");
            Require(model.RecordIronOreProduced(4),
                "Drill Ore commit was rejected.");
            Require(model.RecordSale(ResourceType.IronOre, 3, 30),
                "Iron Ore sale commit was rejected.");
            Require(model.RecordIronBarsProduced(5),
                "Smelter Bar commit was rejected.");
            Require(model.RecordSale(ResourceType.IronBar, 2, 60),
                "Iron Bar sale commit was rejected.");
            Require(model.RecordMineUnlocked()
                    && model.RecordDrillUnlocked()
                    && model.RecordFlag(ProgressFlagId.SmelterUnlocked),
                "A canonical Mining unlock commit was rejected.");

            Require(model.GetMetric(ProgressMetricId.IronOreMined) == 10L
                    && model.GetMetric(ProgressMetricId.IronOreProduced) == 4L
                    && model.GetMetric(ProgressMetricId.IronOreSold) == 3L
                    && model.GetMetric(ProgressMetricId.IronBarsProduced) == 5L
                    && model.GetMetric(ProgressMetricId.IronBarsSold) == 2L
                    && model.GetMetric(ProgressMetricId.MineUnlocked) == 1L
                    && model.GetMetric(ProgressMetricId.DrillUnlocked) == 1L
                    && model.GetMetric(ProgressMetricId.TotalCashEarned) == 90L
                    && model.GetFlag(ProgressFlagId.SmelterUnlocked),
                "Mining metrics did not match their authoritative commits.");

            Require(!model.RecordIronOreMined(0)
                    && !model.RecordIronOreProduced(-1)
                    && !model.RecordIronBarsProduced(0)
                    && !model.RecordSale(ResourceType.IronOre, 0, 10)
                    && !model.RecordSale(ResourceType.IronBar, 1, 0)
                    && !model.RecordPlayerCollection(ResourceType.IronOre, 1)
                    && !model.RecordMineUnlocked()
                    && !model.RecordDrillUnlocked()
                    && !model.RecordFlag(ProgressFlagId.SmelterUnlocked),
                "A failed, presentation-only, or repeated Mining commit was accepted.");
            Require(model.GetMetric(ProgressMetricId.IronOreMined) == 10L
                    && model.GetMetric(ProgressMetricId.IronOreProduced) == 4L
                    && model.GetMetric(ProgressMetricId.IronBarsProduced) == 5L
                    && model.GetMetric(ProgressMetricId.TotalCashEarned) == 90L,
                "Rejected Mining commits changed lifetime metrics.");

            M10ProgressionSaveData snapshot = model.CapturePersistentState();
            var reloadRewards = new FakeRewardWallet();
            var reloaded = new LumberCampProgressionModel(
                snapshot,
                reloadRewards.TryGrant);
            Require(!reloaded.EvaluateAll()
                    && reloadRewards.GrantCount == 0
                    && reloaded.GetMetric(ProgressMetricId.IronOreMined) == 10L
                    && reloaded.GetMetric(ProgressMetricId.IronBarsProduced) == 5L,
                "Reload/evaluation fabricated a Mining metric or reward.");
        }

        private static void TestVersion2ToVersion3Migration()
        {
            string completedJson = JsonUtility.ToJson(
                CreateVersion2Fixture(lumberCompleted: true));
            Require(!completedJson.Contains("\"mining\""),
                "The v2 fixture accidentally contains an M11 Mining payload.");
            M9SaveDecodeResult migrated = M9SaveCodec.Decode(
                completedJson,
                CreateValidationSettings(),
                BaselineUtc + 60L);
            Require(migrated.IsSuccess
                    && migrated.WasMigrated
                    && migrated.Data.version == M9SaveSchema.Version3,
                "A valid schema-v2 save did not migrate explicitly to v3: "
                + migrated.Diagnostic);

            M9SaveData data = migrated.Data;
            Require(data.walletCash == 731
                    && data.cashPileStoredCash == 219
                    && data.carry.resourceType == ResourceType.Crate
                    && data.carry.amount == 7
                    && data.stockpileWood == 17
                    && data.processorInputWood == 9
                    && data.processorOutputPlanks == 8
                    && data.packingInputPlanks == 11
                    && data.packingOutputCrates == 6
                    && data.pendingOfflineCash == 240
                    && data.pendingOfflineAwaySeconds == 7200L
                    && data.returnScreenPending,
                "v2 to v3 migration changed valid M10 economy or machine state.");
            Require(data.progression.GetMetric(ProgressMetricId.WoodProduced) == 5L
                    && data.progression.GetMetric(
                        ProgressMetricId.CourierTripsCompleted) == 5L
                    && data.progression.GetFlag(ProgressFlagId.CourierUnlocked)
                    && data.progression.GetFlag(
                        ProgressFlagId.LumberCampCompleted),
                "v2 to v3 migration did not preserve valid M10 progression.");
            Require(data.mining != null
                    && data.mining.mineUnlocked
                    && data.progression.GetMetric(
                        ProgressMetricId.MineUnlocked) == 1L
                    && data.progression.GetMetric(
                        ProgressMetricId.IronOreMined) == 0L
                    && data.progression.objectiveIndex
                       == (int)MainObjectiveId.MineTenIronOre,
                "Completed v2 Lumber Camp did not canonically unlock the v3 Mine.");
            Require(data.mining.TryGetPurchasePad(
                        M11MiningPurchasePadIds.Smelter,
                        out M9PurchasePadSaveRecord smelterPad)
                    && !smelterPad.completed
                    && smelterPad.paidCash == 0
                    && data.mining.TryGetPurchasePad(
                        M11MiningPurchasePadIds.AutomatedDrill,
                        out M9PurchasePadSaveRecord drillPad)
                    && !drillPad.completed
                    && drillPad.paidCash == 0
                    && data.mining.smelterInputIronOre == 0
                    && data.mining.smelterOutputIronBars == 0
                    && data.mining.oreStorageIronOre == 0,
                "v2 migration fabricated Mining purchases or inventory.");

            string incompleteJson = JsonUtility.ToJson(
                CreateVersion2Fixture(lumberCompleted: false));
            M9SaveDecodeResult incomplete = M9SaveCodec.Decode(
                incompleteJson,
                CreateValidationSettings(),
                BaselineUtc + 60L);
            Require(incomplete.IsSuccess
                    && incomplete.WasMigrated
                    && !incomplete.Data.mining.mineUnlocked
                    && incomplete.Data.progression.GetMetric(
                        ProgressMetricId.MineUnlocked) == 0L,
                "An incomplete v2 Lumber Camp incorrectly unlocked the v3 Mine.");
        }

        private static void TestMiningPersistenceAndCanonicalState()
        {
            M9SaveData source = CreateFullyPopulatedVersion3Save();
            Require(M9SaveCodec.TryEncode(
                    source,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out string json,
                    out M9SaveData normalized,
                    out string failure,
                    prettyPrint: true),
                "Canonical M11 save could not encode: " + failure);
            Require(json.Contains("\"version\": 3")
                    && json.Contains("\"mining\"")
                    && json.Contains("\"smelterInputIronOre\": 23")
                    && json.Contains("\"smelterOutputIronBars\": 11")
                    && json.Contains("\"oreStorageIronOre\": 29"),
                "Encoded v3 save omitted canonical Mining state.");
            string[] forbiddenTransientNames =
            {
                "processingInputOre",
                "reservedOutputCapacity",
                "incomingReservations",
                "cycleElapsed",
                "isProcessing",
                "isProducing",
                "miningCoroutine",
                "processingCoroutine"
            };
            for (int i = 0; i < forbiddenTransientNames.Length; i++)
            {
                Require(!json.Contains(forbiddenTransientNames[i]),
                    "Save leaked transient field " + forbiddenTransientNames[i] + ".");
            }

            Require(normalized.mining.mineUnlocked
                    && normalized.mining.smelterInputIronOre == 23
                    && normalized.mining.smelterOutputIronBars == 11
                    && normalized.mining.oreStorageIronOre == 29
                    && normalized.progression.GetMetric(
                        ProgressMetricId.MineUnlocked) == 1L
                    && normalized.progression.GetMetric(
                        ProgressMetricId.DrillUnlocked) == 1L
                    && normalized.progression.GetFlag(
                        ProgressFlagId.SmelterUnlocked),
                "Canonical normalization lost Mining state or unlock metrics.");

            M9SaveDecodeResult roundTrip = M9SaveCodec.Decode(
                json,
                CreateValidationSettings(),
                BaselineUtc + 10L);
            Require(roundTrip.IsSuccess
                    && !roundTrip.WasMigrated
                    && roundTrip.Data.carry.resourceType == ResourceType.IronBar
                    && roundTrip.Data.carry.amount == 7
                    && roundTrip.Data.mining.smelterInputIronOre == 23
                    && roundTrip.Data.mining.smelterOutputIronBars == 11
                    && roundTrip.Data.mining.oreStorageIronOre == 29
                    && roundTrip.Data.progression.GetMetric(
                        ProgressMetricId.IronOreMined) == 10L
                    && roundTrip.Data.progression.GetMetric(
                        ProgressMetricId.IronOreProduced) == 7L
                    && roundTrip.Data.progression.GetMetric(
                        ProgressMetricId.IronBarsProduced) == 5L,
                "v3 Mining JSON round trip changed canonical state: "
                + roundTrip.Diagnostic);

            M9SaveData overflow = CreateFullyPopulatedVersion3Save();
            overflow.mining.smelterInputIronOre = int.MaxValue;
            overflow.mining.smelterOutputIronBars = int.MaxValue;
            overflow.mining.oreStorageIronOre = int.MaxValue;
            Require(M9SaveValidator.TryNormalize(
                    overflow,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out M9SaveData clamped,
                    out failure)
                    && clamped.mining.smelterInputIronOre == 24
                    && clamped.mining.smelterOutputIronBars == 12
                    && clamped.mining.oreStorageIronOre == 30,
                "Mining buffers did not clamp to 24/12/30: " + failure);

            M9SaveData unavailable = CreateLumberCompleteVersion3Save();
            M9PurchasePadSaveRecord partialSmelter = unavailable.mining.purchasePads[
                M11MiningPurchasePadIds.SmelterIndex];
            partialSmelter.paidCash = 1199;
            unavailable.mining.purchasePads[
                M11MiningPurchasePadIds.AutomatedDrillIndex].completed = true;
            unavailable.mining.purchasePads[
                M11MiningPurchasePadIds.AutomatedDrillIndex].paidCash = 2400;
            unavailable.mining.smelterInputIronOre = 8;
            unavailable.mining.smelterOutputIronBars = 4;
            unavailable.mining.oreStorageIronOre = 30;
            Require(M9SaveValidator.TryNormalize(
                    unavailable,
                    CreateValidationSettings(),
                    BaselineUtc,
                    out M9SaveData canonical,
                    out failure)
                    && canonical.mining.purchasePads[
                        M11MiningPurchasePadIds.SmelterIndex].paidCash == 1199
                    && !canonical.mining.purchasePads[
                        M11MiningPurchasePadIds.SmelterIndex].completed
                    && canonical.mining.smelterInputIronOre == 0
                    && canonical.mining.smelterOutputIronBars == 0
                    && !canonical.mining.purchasePads[
                        M11MiningPurchasePadIds.AutomatedDrillIndex].completed
                    && canonical.mining.purchasePads[
                        M11MiningPurchasePadIds.AutomatedDrillIndex].paidCash == 0
                    && canonical.mining.oreStorageIronOre == 0,
                "Unavailable downstream Mining state was not cleared canonically: "
                + failure);
        }

        private static void TestStoreRoundTripAndReset()
        {
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "IndustryTycoonM11DeterministicTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                var clock = new ManualUtcClock(BaselineUtc);
                var store = new M9LocalSaveStore(
                    testDirectory,
                    clock,
                    CreateValidationSettings(),
                    M9LocalSaveStore.DefaultFileName,
                    preserveInvalidFiles: false);
                M9SaveWriteResult write = store.Save(
                    CreateFullyPopulatedVersion3Save(),
                    prettyPrint: true);
                Require(write.IsSuccess && File.Exists(store.PrimaryPath),
                    "M11 local-store write failed: " + write.Diagnostic);

                clock.UtcNowUnixSeconds += 30L;
                M9SaveLoadResult loaded = store.Load();
                Require(loaded.Status == M9SaveLoadStatus.LoadedPrimary
                        && !loaded.WasMigrated
                        && loaded.Data.mining.mineUnlocked
                        && loaded.Data.mining.smelterInputIronOre == 23
                        && loaded.Data.mining.smelterOutputIronBars == 11
                        && loaded.Data.mining.oreStorageIronOre == 29
                        && loaded.Data.progression.GetMetric(
                            ProgressMetricId.IronOreMined) == 10L
                        && loaded.Data.progression.GetMetric(
                            ProgressMetricId.IronBarsProduced) == 5L,
                    "Local-store reload changed Mining persistence: "
                    + loaded.Diagnostic);

                Require(store.TryDeleteSave(out string deleteFailure)
                        && !File.Exists(store.PrimaryPath)
                        && !File.Exists(store.TemporaryPath)
                        && !File.Exists(store.BackupPath),
                    "Reset Save did not remove all local-store artifacts: "
                    + deleteFailure);
                M9SaveLoadResult reset = store.Load();
                Require(reset.Status == M9SaveLoadStatus.FreshNoSave
                        && !reset.Data.lumberCampCompleted
                        && !reset.Data.mining.mineUnlocked
                        && reset.Data.mining.smelterInputIronOre == 0
                        && reset.Data.mining.smelterOutputIronBars == 0
                        && reset.Data.mining.oreStorageIronOre == 0
                        && !reset.Data.mining.purchasePads[
                            M11MiningPurchasePadIds.SmelterIndex].completed
                        && !reset.Data.mining.purchasePads[
                            M11MiningPurchasePadIds.AutomatedDrillIndex].completed
                        && reset.Data.progression.GetMetric(
                            ProgressMetricId.IronOreMined) == 0L
                        && reset.Data.progression.GetMetric(
                            ProgressMetricId.IronOreProduced) == 0L
                        && reset.Data.progression.GetMetric(
                            ProgressMetricId.IronBarsProduced) == 0L
                        && reset.Data.carry.amount == 0,
                    "Reset Save did not return an exact fresh M11 state.");
            }
            finally
            {
                if (Directory.Exists(testDirectory))
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
            }
        }

        private static Version2Fixture CreateVersion2Fixture(bool lumberCompleted)
        {
            M9SaveData source = M9SaveData.CreateFresh(BaselineUtc);
            source.walletCash = 731;
            source.cashPileStoredCash = 219;
            source.carry.resourceType = ResourceType.Crate;
            source.carry.amount = 7;
            source.lumberCampCompleted = lumberCompleted;
            source.stockpileWood = 17;
            source.processorInputWood = 9;
            source.processorOutputPlanks = 8;
            source.packingInputPlanks = 11;
            source.packingOutputCrates = 6;
            source.pendingOfflineCash = 240;
            source.pendingOfflineAwaySeconds = 7200L;
            source.returnScreenPending = true;
            source.lastEvaluationUtcUnixSeconds = BaselineUtc - 120L;
            source.lastWriteUtcUnixSeconds = BaselineUtc - 60L;

            for (int i = 0; i < source.purchasePads.Length; i++)
            {
                if (lumberCompleted)
                {
                    CompletePad(source.purchasePads[i], M9PurchasePadIds.GetTotalCost(i));
                }
            }

            M10ProgressionSaveData progression =
                M10ProgressionSaveData.CreateFresh();
            long[] legacyMetricValues =
            {
                321L, 4L, 5L, 20L, 10L,
                3L, 5L, 2L, 5L, 5L
            };
            for (int i = 0; i < Version2MetricCount; i++)
            {
                progression.metrics[i].value = legacyMetricValues[i];
            }

            for (int i = 0; i < Version2FlagCount; i++)
            {
                progression.flags[i].value = lumberCompleted;
            }

            var legacyMetrics = new M10MetricSaveRecord[Version2MetricCount];
            var legacyFlags = new M10FlagSaveRecord[Version2FlagCount];
            Array.Copy(progression.metrics, legacyMetrics, legacyMetrics.Length);
            Array.Copy(progression.flags, legacyFlags, legacyFlags.Length);
            progression.metrics = legacyMetrics;
            progression.flags = legacyFlags;
            progression.objectiveIndex = lumberCompleted ? 9 : 0;
            progression.activeContractState =
                ContractProgressState.CompletedUnclaimed;

            return new Version2Fixture
            {
                schema = M9SaveSchema.Id,
                version = M9SaveSchema.Version2,
                walletCash = source.walletCash,
                cashPileStoredCash = source.cashPileStoredCash,
                carry = source.carry,
                purchasePads = source.purchasePads,
                lumberCampCompleted = source.lumberCampCompleted,
                stockpileWood = source.stockpileWood,
                processorInputWood = source.processorInputWood,
                processorOutputPlanks = source.processorOutputPlanks,
                packingInputPlanks = source.packingInputPlanks,
                packingOutputCrates = source.packingOutputCrates,
                pendingOfflineCash = source.pendingOfflineCash,
                pendingOfflineAwaySeconds = source.pendingOfflineAwaySeconds,
                returnScreenPending = source.returnScreenPending,
                lastEvaluationUtcUnixSeconds = source.lastEvaluationUtcUnixSeconds,
                lastWriteUtcUnixSeconds = source.lastWriteUtcUnixSeconds,
                progression = progression
            };
        }

        private static M9SaveData CreateLumberCompleteVersion3Save()
        {
            M9SaveData data = M9SaveData.CreateFresh(BaselineUtc);
            for (int i = 0; i < data.purchasePads.Length; i++)
            {
                CompletePad(data.purchasePads[i], M9PurchasePadIds.GetTotalCost(i));
            }

            data.lumberCampCompleted = true;
            data.mining.mineUnlocked = true;
            return data;
        }

        private static M9SaveData CreateFullyPopulatedVersion3Save()
        {
            M9SaveData data = CreateLumberCompleteVersion3Save();
            data.walletCash = 777;
            data.cashPileStoredCash = 99;
            data.carry.resourceType = ResourceType.IronBar;
            data.carry.amount = 7;
            CompletePad(
                data.mining.purchasePads[M11MiningPurchasePadIds.SmelterIndex],
                M11MiningPurchasePadIds.GetTotalCost(
                    M11MiningPurchasePadIds.SmelterIndex));
            CompletePad(
                data.mining.purchasePads[
                    M11MiningPurchasePadIds.AutomatedDrillIndex],
                M11MiningPurchasePadIds.GetTotalCost(
                    M11MiningPurchasePadIds.AutomatedDrillIndex));
            data.mining.smelterInputIronOre = 23;
            data.mining.smelterOutputIronBars = 11;
            data.mining.oreStorageIronOre = 29;

            SetMetric(data.progression, ProgressMetricId.CourierTripsCompleted, 5L);
            SetMetric(data.progression, ProgressMetricId.PlanksProduced, 10L);
            SetMetric(data.progression, ProgressMetricId.CratesProduced, 5L);
            SetMetric(data.progression, ProgressMetricId.IronOreMined, 10L);
            SetMetric(data.progression, ProgressMetricId.IronOreProduced, 7L);
            SetMetric(data.progression, ProgressMetricId.IronOreSold, 3L);
            SetMetric(data.progression, ProgressMetricId.IronBarsProduced, 5L);
            SetMetric(data.progression, ProgressMetricId.IronBarsSold, 2L);
            return data;
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
                oreStorageCapacity = 30
            };
        }

        private static void CompletePad(M9PurchasePadSaveRecord pad, int totalCost)
        {
            pad.paidCash = totalCost;
            pad.completed = true;
        }

        private static void SetMetric(
            M10ProgressionSaveData progression,
            ProgressMetricId metric,
            long value)
        {
            progression.metrics[(int)metric].value = value;
        }

        private static void SetObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            Require(property != null,
                $"Missing serialized property {target.GetType().Name}.{propertyName}.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Run(string name, Action test)
        {
            test();
            _testCount++;
            Debug.Log("M11 deterministic test PASS: " + name);
        }

        private static void Require(bool condition, string message)
        {
            _assertionCount++;
            if (!condition)
            {
                throw new InvalidOperationException(
                    "M11 deterministic test failed: " + message);
            }
        }

        private sealed class FakeRewardWallet
        {
            public int GrantCount { get; private set; }

            public bool TryGrant(int amount)
            {
                if (amount <= 0)
                {
                    return false;
                }

                GrantCount++;
                return true;
            }
        }
    }
}
