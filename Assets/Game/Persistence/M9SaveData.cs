using System;
using IndustryTycoon.Core;
using IndustryTycoon.Progression;

namespace IndustryTycoon.Persistence
{
    public static class M9SaveSchema
    {
        public const string Id = "industry-tycoon-local-save";
        public const int Version1 = 1;
        public const int Version2 = 2;
        public const int CurrentVersion = Version2;
    }

    public static class M9PurchasePadIds
    {
        public const string ProductionUpgrade = "production_upgrade";
        public const string LumberWorker = "lumber_worker";
        public const string WoodProcessor = "wood_processor";
        public const string AutoFeeder = "auto_feeder";
        public const string PackingStation = "packing_station";
        public const string DeliveryCourier = "delivery_courier";

        public const int Count = 6;

        public static string GetId(int index)
        {
            switch (index)
            {
                case 0:
                    return ProductionUpgrade;
                case 1:
                    return LumberWorker;
                case 2:
                    return WoodProcessor;
                case 3:
                    return AutoFeeder;
                case 4:
                    return PackingStation;
                case 5:
                    return DeliveryCourier;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static int GetTotalCost(int index)
        {
            switch (index)
            {
                case 0:
                    return 120;
                case 1:
                    return 240;
                case 2:
                    return 360;
                case 3:
                    return 600;
                case 4:
                    return 900;
                case 5:
                    return 1500;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static bool TryGetIndex(string id, out int index)
        {
            for (int candidate = 0; candidate < Count; candidate++)
            {
                if (string.Equals(id, GetId(candidate), StringComparison.Ordinal))
                {
                    index = candidate;
                    return true;
                }
            }

            index = -1;
            return false;
        }
    }

    [Serializable]
    public sealed class M9CarrySaveRecord
    {
        public ResourceType resourceType = ResourceType.Wood;
        public int amount;
    }

    [Serializable]
    public sealed class M9PurchasePadSaveRecord
    {
        public string id;
        public int paidCash;
        public bool completed;
    }

    [Serializable]
    public sealed class M9SaveData
    {
        public string schema = M9SaveSchema.Id;
        public int version = M9SaveSchema.CurrentVersion;

        public int walletCash;
        public int cashPileStoredCash;
        public M9CarrySaveRecord carry = new M9CarrySaveRecord();
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

        public M10ProgressionSaveData progression =
            M10ProgressionSaveData.CreateFresh();

        public static M9SaveData CreateFresh(long utcNowUnixSeconds)
        {
            long safeTimestamp = M9UnixTime.IsPlausible(utcNowUnixSeconds)
                ? utcNowUnixSeconds
                : 0L;
            var data = new M9SaveData
            {
                purchasePads = new M9PurchasePadSaveRecord[M9PurchasePadIds.Count],
                progression = M10ProgressionSaveData.CreateFresh(),
                lastEvaluationUtcUnixSeconds = safeTimestamp,
                lastWriteUtcUnixSeconds = safeTimestamp
            };

            for (int i = 0; i < data.purchasePads.Length; i++)
            {
                data.purchasePads[i] = new M9PurchasePadSaveRecord
                {
                    id = M9PurchasePadIds.GetId(i)
                };
            }

            return data;
        }

        public bool TryGetPurchasePad(string id, out M9PurchasePadSaveRecord record)
        {
            if (purchasePads != null)
            {
                for (int i = 0; i < purchasePads.Length; i++)
                {
                    M9PurchasePadSaveRecord candidate = purchasePads[i];
                    if (candidate != null
                        && string.Equals(candidate.id, id, StringComparison.Ordinal))
                    {
                        record = candidate;
                        return true;
                    }
                }
            }

            record = null;
            return false;
        }
    }

    public sealed class M9SaveValidationSettings
    {
        public int carryCapacity = 12;
        public int stockpileCapacity = 30;
        public int processorInputCapacity = 24;
        public int processorOutputCapacity = 12;
        public int packingInputCapacity = 24;
        public int packingOutputCapacity = 12;
        public long maximumPendingAwaySeconds = 365L * 24L * 60L * 60L;

        public static M9SaveValidationSettings CreateDefault()
        {
            return new M9SaveValidationSettings();
        }
    }

    public static class M9SaveValidator
    {
        public static bool TryNormalize(
            M9SaveData source,
            M9SaveValidationSettings settings,
            long fallbackUtcUnixSeconds,
            out M9SaveData normalized,
            out string failureReason)
        {
            normalized = null;
            failureReason = null;
            if (source == null)
            {
                failureReason = "Save data is null.";
                return false;
            }

            if (!string.Equals(source.schema, M9SaveSchema.Id, StringComparison.Ordinal))
            {
                failureReason = "Save schema identifier is missing or invalid.";
                return false;
            }

            if (source.version != M9SaveSchema.CurrentVersion)
            {
                failureReason = $"Save version {source.version} is not current.";
                return false;
            }

            settings = settings ?? M9SaveValidationSettings.CreateDefault();
            if (source.carry == null)
            {
                failureReason = "Carry record is missing.";
                return false;
            }

            if (source.purchasePads == null
                || source.purchasePads.Length != M9PurchasePadIds.Count)
            {
                failureReason = "Save must contain exactly six PurchasePad records.";
                return false;
            }

            var orderedPads = new M9PurchasePadSaveRecord[M9PurchasePadIds.Count];
            var seenPads = new bool[M9PurchasePadIds.Count];
            for (int i = 0; i < source.purchasePads.Length; i++)
            {
                M9PurchasePadSaveRecord sourcePad = source.purchasePads[i];
                if (sourcePad == null
                    || !M9PurchasePadIds.TryGetIndex(sourcePad.id, out int padIndex)
                    || seenPads[padIndex])
                {
                    failureReason = "PurchasePad records contain a missing, unknown, or duplicate stable ID.";
                    return false;
                }

                seenPads[padIndex] = true;
                int totalCost = M9PurchasePadIds.GetTotalCost(padIndex);
                bool completed = sourcePad.completed;
                int paidCash = completed
                    ? totalCost
                    : Clamp(sourcePad.paidCash, 0, totalCost - 1);
                if (completed)
                {
                    paidCash = totalCost;
                }

                orderedPads[padIndex] = new M9PurchasePadSaveRecord
                {
                    id = M9PurchasePadIds.GetId(padIndex),
                    paidCash = paidCash,
                    completed = completed
                };
            }

            // A downstream pad cannot have progress until every prerequisite is complete.
            bool prerequisiteComplete = true;
            for (int i = 0; i < orderedPads.Length; i++)
            {
                if (!prerequisiteComplete)
                {
                    orderedPads[i].paidCash = 0;
                    orderedPads[i].completed = false;
                }

                prerequisiteComplete &= orderedPads[i].completed;
            }

            int carryCapacity = Math.Max(1, settings.carryCapacity);
            int carryAmount = Clamp(source.carry.amount, 0, carryCapacity);
            ResourceType carryType = source.carry.resourceType;
            if (!IsSupportedResource(carryType))
            {
                carryType = ResourceType.Wood;
                carryAmount = 0;
            }

            long safeFallbackTimestamp = M9UnixTime.IsPlausible(fallbackUtcUnixSeconds)
                ? fallbackUtcUnixSeconds
                : 0L;
            long lastEvaluation = NormalizeTimestamp(
                source.lastEvaluationUtcUnixSeconds,
                safeFallbackTimestamp);
            long lastWrite = NormalizeTimestamp(
                source.lastWriteUtcUnixSeconds,
                safeFallbackTimestamp);

            if (!M10ProgressionSaveValidator.TryNormalize(
                    source.progression,
                    out M10ProgressionSaveData normalizedProgression,
                    out failureReason))
            {
                return false;
            }

            bool normalizedLumberCampCompleted = source.lumberCampCompleted
                                                  && orderedPads[
                                                      M9PurchasePadIds.Count - 1]
                                                      .completed;
            if (!ValidateExactProgressionFlags(
                    normalizedProgression,
                    orderedPads,
                    normalizedLumberCampCompleted,
                    out failureReason))
            {
                return false;
            }

            SeedExactProgressionFlags(
                normalizedProgression,
                orderedPads,
                normalizedLumberCampCompleted);
            if (!M10ProgressionSaveValidator.TryNormalize(
                    normalizedProgression,
                    out normalizedProgression,
                    out failureReason))
            {
                return false;
            }

            normalized = new M9SaveData
            {
                schema = M9SaveSchema.Id,
                version = M9SaveSchema.CurrentVersion,
                walletCash = Math.Max(0, source.walletCash),
                cashPileStoredCash = Math.Max(0, source.cashPileStoredCash),
                carry = new M9CarrySaveRecord
                {
                    resourceType = carryType,
                    amount = carryAmount
                },
                purchasePads = orderedPads,
                lumberCampCompleted = normalizedLumberCampCompleted,
                stockpileWood = Clamp(
                    source.stockpileWood,
                    0,
                    Math.Max(1, settings.stockpileCapacity)),
                processorInputWood = Clamp(
                    source.processorInputWood,
                    0,
                    Math.Max(1, settings.processorInputCapacity)),
                processorOutputPlanks = Clamp(
                    source.processorOutputPlanks,
                    0,
                    Math.Max(1, settings.processorOutputCapacity)),
                packingInputPlanks = Clamp(
                    source.packingInputPlanks,
                    0,
                    Math.Max(1, settings.packingInputCapacity)),
                packingOutputCrates = Clamp(
                    source.packingOutputCrates,
                    0,
                    Math.Max(1, settings.packingOutputCapacity)),
                pendingOfflineCash = Math.Max(0, source.pendingOfflineCash),
                pendingOfflineAwaySeconds = ClampLong(
                    source.pendingOfflineAwaySeconds,
                    0L,
                    Math.Max(0L, settings.maximumPendingAwaySeconds)),
                returnScreenPending = source.returnScreenPending,
                lastEvaluationUtcUnixSeconds = lastEvaluation,
                lastWriteUtcUnixSeconds = lastWrite,
                progression = normalizedProgression
            };
            return true;
        }

        private static bool ValidateExactProgressionFlags(
            M10ProgressionSaveData progression,
            M9PurchasePadSaveRecord[] orderedPads,
            bool lumberCampCompleted,
            out string failureReason)
        {
            failureReason = null;
            ProgressFlagId[] canonicalPadFlags =
            {
                ProgressFlagId.ProductionUpgradeUnlocked,
                ProgressFlagId.WorkerUnlocked,
                ProgressFlagId.ProcessorUnlocked,
                ProgressFlagId.AutoFeederUnlocked,
                ProgressFlagId.PackingStationUnlocked,
                ProgressFlagId.CourierUnlocked
            };
            for (int i = 0; i < canonicalPadFlags.Length; i++)
            {
                if (progression.GetFlag(canonicalPadFlags[i])
                    && !orderedPads[i].completed)
                {
                    failureReason =
                        $"M10 flag {canonicalPadFlags[i]} contradicts its PurchasePad.";
                    return false;
                }
            }

            if (progression.GetFlag(ProgressFlagId.LumberCampCompleted)
                && !lumberCampCompleted)
            {
                failureReason =
                    "M10 Lumber Camp flag contradicts canonical completion state.";
                return false;
            }

            return true;
        }

        private static void SeedExactProgressionFlags(
            M10ProgressionSaveData progression,
            M9PurchasePadSaveRecord[] orderedPads,
            bool lumberCampCompleted)
        {
            if (progression == null || orderedPads == null)
            {
                return;
            }

            if (orderedPads[0].completed)
            {
                progression.SetFlag(ProgressFlagId.ProductionUpgradeUnlocked);
            }

            if (orderedPads[1].completed)
            {
                progression.SetFlag(ProgressFlagId.WorkerUnlocked);
            }

            if (orderedPads[2].completed)
            {
                progression.SetFlag(ProgressFlagId.ProcessorUnlocked);
            }

            if (orderedPads[3].completed)
            {
                progression.SetFlag(ProgressFlagId.AutoFeederUnlocked);
            }

            if (orderedPads[4].completed)
            {
                progression.SetFlag(ProgressFlagId.PackingStationUnlocked);
            }

            if (orderedPads[5].completed)
            {
                progression.SetFlag(ProgressFlagId.CourierUnlocked);
            }

            if (lumberCampCompleted)
            {
                progression.SetFlag(ProgressFlagId.LumberCampCompleted);
            }
        }

        private static long NormalizeTimestamp(long value, long fallback)
        {
            return M9UnixTime.IsPlausible(value) ? value : fallback;
        }

        private static bool IsSupportedResource(ResourceType resourceType)
        {
            return resourceType == ResourceType.Wood
                   || resourceType == ResourceType.Plank
                   || resourceType == ResourceType.Crate;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static long ClampLong(long value, long minimum, long maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }

    public static class M9UnixTime
    {
        // UTC 2000-01-01 through 9999-12-31. Zero remains an explicit unset value.
        public const long MinimumPlausibleUnixSeconds = 946684800L;
        public const long MaximumPlausibleUnixSeconds = 253402300799L;

        public static bool IsPlausible(long utcUnixSeconds)
        {
            return utcUnixSeconds >= MinimumPlausibleUnixSeconds
                   && utcUnixSeconds <= MaximumPlausibleUnixSeconds;
        }
    }
}
