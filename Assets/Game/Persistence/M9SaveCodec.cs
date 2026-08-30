using System;
using IndustryTycoon.Core;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.Persistence
{
    public enum M9SaveDecodeStatus
    {
        Success = 0,
        Empty = 1,
        MalformedJson = 2,
        InvalidSchema = 3,
        UnsupportedVersion = 4,
        ValidationFailed = 5
    }

    public sealed class M9SaveDecodeResult
    {
        private M9SaveDecodeResult(
            M9SaveDecodeStatus status,
            M9SaveData data,
            string diagnostic,
            bool wasMigrated)
        {
            Status = status;
            Data = data;
            Diagnostic = diagnostic ?? string.Empty;
            WasMigrated = wasMigrated;
        }

        public M9SaveDecodeStatus Status { get; }
        public M9SaveData Data { get; }
        public string Diagnostic { get; }
        public bool WasMigrated { get; }
        public bool IsSuccess => Status == M9SaveDecodeStatus.Success && Data != null;

        internal static M9SaveDecodeResult Succeeded(
            M9SaveData data,
            bool wasMigrated = false)
        {
            return new M9SaveDecodeResult(
                M9SaveDecodeStatus.Success,
                data,
                null,
                wasMigrated);
        }

        internal static M9SaveDecodeResult Failed(
            M9SaveDecodeStatus status,
            string diagnostic)
        {
            return new M9SaveDecodeResult(status, null, diagnostic, false);
        }
    }

    public static class M9SaveCodec
    {
        [Serializable]
        private sealed class SaveHeader
        {
            public string schema = null;
            public int version = 0;
        }

        [Serializable]
        private sealed class Version1SaveData
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
        }

        [Serializable]
        private sealed class Version2SaveData
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

        public static M9SaveDecodeResult Decode(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.Empty,
                    "Save JSON is empty.");
            }

            string trimmedJson = json.Trim();
            if (trimmedJson.Length < 2
                || trimmedJson[0] != '{'
                || trimmedJson[trimmedJson.Length - 1] != '}')
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    "Save JSON does not contain an object root.");
            }

            SaveHeader header;
            try
            {
                header = JsonUtility.FromJson<SaveHeader>(trimmedJson);
            }
            catch (Exception exception)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save header could not be decoded: {exception.Message}");
            }

            if (header == null
                || !string.Equals(header.schema, M9SaveSchema.Id, StringComparison.Ordinal))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.InvalidSchema,
                    "Save schema identifier is unsupported.");
            }

            // Future schema migrations enter through this explicit version switch.
            switch (header.version)
            {
                case M9SaveSchema.Version1:
                    return DecodeVersion1(
                        trimmedJson,
                        validationSettings,
                        fallbackUtcUnixSeconds);
                case M9SaveSchema.Version2:
                    return DecodeVersion2(
                        trimmedJson,
                        validationSettings,
                        fallbackUtcUnixSeconds);
                case M9SaveSchema.Version3:
                    return DecodeVersion3(
                        trimmedJson,
                        validationSettings,
                        fallbackUtcUnixSeconds);
                default:
                    return M9SaveDecodeResult.Failed(
                        M9SaveDecodeStatus.UnsupportedVersion,
                        $"Save version {header.version} is unsupported.");
            }
        }

        public static bool TryEncode(
            M9SaveData data,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds,
            out string json,
            out M9SaveData normalizedData,
            out string failureReason,
            bool prettyPrint = false)
        {
            json = null;
            normalizedData = null;
            failureReason = null;
            if (!M9SaveValidator.TryNormalize(
                    data,
                    validationSettings,
                    fallbackUtcUnixSeconds,
                    out normalizedData,
                    out failureReason))
            {
                return false;
            }

            try
            {
                json = JsonUtility.ToJson(normalizedData, prettyPrint);
            }
            catch (Exception exception)
            {
                failureReason = $"Save JSON could not be encoded: {exception.Message}";
                json = null;
                normalizedData = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                failureReason = "Save JSON encoder returned no data.";
                normalizedData = null;
                return false;
            }

            return true;
        }

        private static M9SaveDecodeResult DecodeVersion1(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            Version1SaveData legacy;
            try
            {
                legacy = JsonUtility.FromJson<Version1SaveData>(json);
            }
            catch (Exception exception)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save body could not be decoded: {exception.Message}");
            }

            if (legacy == null)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    "Version 1 save body is missing.");
            }

            var migrated = new M9SaveData
            {
                schema = legacy.schema,
                version = M9SaveSchema.Version3,
                walletCash = legacy.walletCash,
                cashPileStoredCash = legacy.cashPileStoredCash,
                carry = SanitizeLegacyCarry(legacy.carry),
                purchasePads = legacy.purchasePads,
                lumberCampCompleted = legacy.lumberCampCompleted,
                stockpileWood = legacy.stockpileWood,
                processorInputWood = legacy.processorInputWood,
                processorOutputPlanks = legacy.processorOutputPlanks,
                packingInputPlanks = legacy.packingInputPlanks,
                packingOutputCrates = legacy.packingOutputCrates,
                pendingOfflineCash = legacy.pendingOfflineCash,
                pendingOfflineAwaySeconds = legacy.pendingOfflineAwaySeconds,
                returnScreenPending = legacy.returnScreenPending,
                lastEvaluationUtcUnixSeconds = legacy.lastEvaluationUtcUnixSeconds,
                lastWriteUtcUnixSeconds = legacy.lastWriteUtcUnixSeconds,
                progression = M10ProgressionSaveData.CreateFresh(),
                mining = M11MiningSaveData.CreateFresh()
            };

            if (!M9SaveValidator.TryNormalize(
                    migrated,
                    validationSettings,
                    fallbackUtcUnixSeconds,
                    out M9SaveData normalized,
                    out string validationFailure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    validationFailure);
            }

            GrandfatherExactVersion1Achievements(normalized.progression);
            if (!M9SaveValidator.TryNormalize(
                    normalized,
                    validationSettings,
                    fallbackUtcUnixSeconds,
                    out normalized,
                    out validationFailure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    validationFailure);
            }

            return M9SaveDecodeResult.Succeeded(normalized, true);
        }

        private static M9SaveDecodeResult DecodeVersion2(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            Version2SaveData legacy;
            try
            {
                legacy = JsonUtility.FromJson<Version2SaveData>(json);
            }
            catch (Exception exception)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save body could not be decoded: {exception.Message}");
            }

            if (legacy == null)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    "Version 2 save body is missing.");
            }

            if (!TryUpgradeVersion2Progression(
                    legacy.progression,
                    out M10ProgressionSaveData upgradedProgression,
                    out string upgradeFailure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    upgradeFailure);
            }

            var migrated = new M9SaveData
            {
                schema = legacy.schema,
                version = M9SaveSchema.Version3,
                walletCash = legacy.walletCash,
                cashPileStoredCash = legacy.cashPileStoredCash,
                carry = SanitizeLegacyCarry(legacy.carry),
                purchasePads = legacy.purchasePads,
                lumberCampCompleted = legacy.lumberCampCompleted,
                stockpileWood = legacy.stockpileWood,
                processorInputWood = legacy.processorInputWood,
                processorOutputPlanks = legacy.processorOutputPlanks,
                packingInputPlanks = legacy.packingInputPlanks,
                packingOutputCrates = legacy.packingOutputCrates,
                pendingOfflineCash = legacy.pendingOfflineCash,
                pendingOfflineAwaySeconds = legacy.pendingOfflineAwaySeconds,
                returnScreenPending = legacy.returnScreenPending,
                lastEvaluationUtcUnixSeconds = legacy.lastEvaluationUtcUnixSeconds,
                lastWriteUtcUnixSeconds = legacy.lastWriteUtcUnixSeconds,
                progression = upgradedProgression,
                mining = M11MiningSaveData.CreateFresh()
            };

            if (!M9SaveValidator.TryNormalize(
                    migrated,
                    validationSettings,
                    fallbackUtcUnixSeconds,
                    out M9SaveData normalized,
                    out string validationFailure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    validationFailure);
            }

            return M9SaveDecodeResult.Succeeded(normalized, true);
        }

        private static M9SaveDecodeResult DecodeVersion3(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            M9SaveData decoded;
            try
            {
                decoded = JsonUtility.FromJson<M9SaveData>(json);
            }
            catch (Exception exception)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save body could not be decoded: {exception.Message}");
            }

            if (!M9SaveValidator.TryNormalize(
                    decoded,
                    validationSettings,
                    fallbackUtcUnixSeconds,
                    out M9SaveData normalized,
                    out string validationFailure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    validationFailure);
            }

            return M9SaveDecodeResult.Succeeded(normalized);
        }

        private static bool TryUpgradeVersion2Progression(
            M10ProgressionSaveData source,
            out M10ProgressionSaveData upgraded,
            out string failureReason)
        {
            const int version2MetricCount = 10;
            const int version2FlagCount = 7;
            upgraded = null;
            failureReason = null;
            if (source == null
                || source.metrics == null
                || source.metrics.Length != version2MetricCount
                || source.flags == null
                || source.flags.Length != version2FlagCount
                || source.claimedContracts == null
                || source.claimedContracts.Length
                != LumberCampProgressionCatalog.ContractCount
                || source.achievements == null
                || source.achievements.Length
                != LumberCampProgressionCatalog.AchievementCount)
            {
                failureReason =
                    "Version 2 progression has a missing or incorrectly sized canonical record set.";
                return false;
            }

            upgraded = M10ProgressionSaveData.CreateFresh();
            var seenMetrics = new bool[version2MetricCount];
            for (int i = 0; i < source.metrics.Length; i++)
            {
                M10MetricSaveRecord record = source.metrics[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetMetricId(
                        record.id,
                        out ProgressMetricId metric)
                    || (int)metric < 0
                    || (int)metric >= version2MetricCount
                    || seenMetrics[(int)metric])
                {
                    failureReason =
                        "Version 2 progression contains a missing, unknown, or duplicate metric ID.";
                    upgraded = null;
                    return false;
                }

                seenMetrics[(int)metric] = true;
                upgraded.metrics[(int)metric].value = record.value;
            }

            var seenFlags = new bool[version2FlagCount];
            for (int i = 0; i < source.flags.Length; i++)
            {
                M10FlagSaveRecord record = source.flags[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetFlagId(
                        record.id,
                        out ProgressFlagId flag)
                    || (int)flag < 0
                    || (int)flag >= version2FlagCount
                    || seenFlags[(int)flag])
                {
                    failureReason =
                        "Version 2 progression contains a missing, unknown, or duplicate flag ID.";
                    upgraded = null;
                    return false;
                }

                seenFlags[(int)flag] = true;
                upgraded.flags[(int)flag].value = record.value;
            }

            Array.Copy(
                source.claimedContracts,
                upgraded.claimedContracts,
                source.claimedContracts.Length);
            upgraded.activeContractIndex = source.activeContractIndex;
            upgraded.activeContractBaseline = source.activeContractBaseline;
            upgraded.activeContractState = source.activeContractState;

            var seenAchievements = new bool[
                LumberCampProgressionCatalog.AchievementCount];
            for (int i = 0; i < source.achievements.Length; i++)
            {
                M10AchievementSaveRecord record = source.achievements[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetAchievementIndex(
                        record.id,
                        out int achievementIndex)
                    || seenAchievements[achievementIndex])
                {
                    failureReason =
                        "Version 2 progression contains a missing, unknown, or duplicate achievement ID.";
                    upgraded = null;
                    return false;
                }

                seenAchievements[achievementIndex] = true;
                upgraded.achievements[achievementIndex].unlocked = record.unlocked;
                upgraded.achievements[achievementIndex].rewarded = record.rewarded;
            }

            // objectiveIndex was always a derived cursor. The v3 validator resolves
            // it from stable metrics/flags after applying the corrected M11 order.
            upgraded.objectiveIndex = 0;
            return true;
        }

        private static M9CarrySaveRecord SanitizeLegacyCarry(
            M9CarrySaveRecord legacy)
        {
            if (legacy == null)
            {
                return null;
            }

            if (legacy.resourceType == ResourceType.Wood
                || legacy.resourceType == ResourceType.Plank
                || legacy.resourceType == ResourceType.Crate)
            {
                return new M9CarrySaveRecord
                {
                    resourceType = legacy.resourceType,
                    amount = legacy.amount
                };
            }

            return new M9CarrySaveRecord
            {
                resourceType = ResourceType.Wood,
                amount = 0
            };
        }

        private static void GrandfatherExactVersion1Achievements(
            M10ProgressionSaveData progression)
        {
            if (progression == null)
            {
                return;
            }

            if (progression.GetFlag(ProgressFlagId.WorkerUnlocked))
            {
                progression.GrandfatherAchievement(
                    LumberCampAchievementId.FirstHire);
            }

            if (progression.GetFlag(ProgressFlagId.ProcessorUnlocked))
            {
                progression.GrandfatherAchievement(
                    LumberCampAchievementId.ProcessingBegins);
            }

            if (progression.GetFlag(ProgressFlagId.AutoFeederUnlocked))
            {
                progression.GrandfatherAchievement(
                    LumberCampAchievementId.AutomationOnline);
            }

            if (progression.GetFlag(ProgressFlagId.CourierUnlocked))
            {
                progression.GrandfatherAchievement(
                    LumberCampAchievementId.DeliveryService);
            }

            if (progression.GetFlag(ProgressFlagId.WorkerUnlocked)
                && progression.GetFlag(ProgressFlagId.AutoFeederUnlocked))
            {
                progression.GrandfatherAchievement(
                    LumberCampAchievementId.FullyAutomatedInput);
            }

            if (progression.GetFlag(ProgressFlagId.LumberCampCompleted))
            {
                progression.GrandfatherAchievement(
                    LumberCampAchievementId.LumberCampComplete);
            }
        }
    }
}
