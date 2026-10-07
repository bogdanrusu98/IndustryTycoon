using System;
using IndustryTycoon.Core;
using IndustryTycoon.Market;
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

        [Serializable]
        private sealed class Version3SaveData
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
            public M11MiningSaveData mining;
        }

        [Serializable]
        private sealed class Version4SaveData
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
            public M11MiningSaveData mining;
            public M12WorkshopSaveData workshop;
        }

        [Serializable]
        private sealed class Version5SaveData
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
            public M11MiningSaveData mining;
            public M12WorkshopSaveData workshop;
            public M13LogisticsSaveData logistics;
            public M13RetentionSaveData retention;
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
                case M9SaveSchema.Version4:
                    return DecodeVersion4(
                        trimmedJson,
                        validationSettings,
                        fallbackUtcUnixSeconds);
                case M9SaveSchema.Version5:
                    return DecodeVersion5(
                        trimmedJson,
                        validationSettings,
                        fallbackUtcUnixSeconds);
                case M9SaveSchema.Version6:
                    return DecodeVersion6(
                        trimmedJson,
                        validationSettings,
                        fallbackUtcUnixSeconds);
                case M9SaveSchema.Version7:
                    return DecodeVersion7(trimmedJson, validationSettings,
                        fallbackUtcUnixSeconds);
                case M9SaveSchema.Version8:
                    return DecodeVersion8(trimmedJson, validationSettings,
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
                version = M9SaveSchema.CurrentVersion,
                walletCash = legacy.walletCash,
                cashPileStoredCash = legacy.cashPileStoredCash,
                carry = SanitizeLegacyCarry(legacy.carry, false),
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
                mining = M11MiningSaveData.CreateFresh(),
                workshop = M12WorkshopSaveData.CreateFresh(),
                logistics = M13LogisticsSaveData.CreateFresh(),
                retention = M13RetentionSaveData.CreateFresh(
                    legacy.lastWriteUtcUnixSeconds),
                industrial = M14IndustrialSaveData.CreateFresh()
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

            if (!TryUpgradeLegacyProgression(
                    legacy.progression,
                    10,
                    7,
                    5,
                    15,
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
                version = M9SaveSchema.CurrentVersion,
                walletCash = legacy.walletCash,
                cashPileStoredCash = legacy.cashPileStoredCash,
                carry = SanitizeLegacyCarry(legacy.carry, false),
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
                mining = M11MiningSaveData.CreateFresh(),
                workshop = M12WorkshopSaveData.CreateFresh(),
                logistics = M13LogisticsSaveData.CreateFresh(),
                retention = M13RetentionSaveData.CreateFresh(
                    legacy.lastWriteUtcUnixSeconds),
                industrial = M14IndustrialSaveData.CreateFresh()
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
            Version3SaveData legacy;
            try
            {
                legacy = JsonUtility.FromJson<Version3SaveData>(json);
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
                    "Version 3 save body is missing.");
            }

            if (!TryUpgradeLegacyProgression(
                    legacy.progression,
                    17,
                    8,
                    5,
                    15,
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
                version = M9SaveSchema.CurrentVersion,
                walletCash = legacy.walletCash,
                cashPileStoredCash = legacy.cashPileStoredCash,
                carry = SanitizeLegacyCarry(legacy.carry, true),
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
                mining = legacy.mining,
                workshop = M12WorkshopSaveData.CreateFresh(),
                logistics = M13LogisticsSaveData.CreateFresh(),
                retention = M13RetentionSaveData.CreateFresh(
                    legacy.lastWriteUtcUnixSeconds),
                industrial = M14IndustrialSaveData.CreateFresh()
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

        private static M9SaveDecodeResult DecodeVersion4(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            Version4SaveData legacy;
            try
            {
                legacy = JsonUtility.FromJson<Version4SaveData>(json);
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
                    "Version 4 save body is missing.");
            }

            if (!TryUpgradeLegacyProgression(
                    legacy.progression,
                    20,
                    8,
                    7,
                    19,
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
                version = M9SaveSchema.CurrentVersion,
                walletCash = legacy.walletCash,
                cashPileStoredCash = legacy.cashPileStoredCash,
                carry = SanitizeLegacyCarry(legacy.carry, true),
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
                mining = legacy.mining,
                workshop = legacy.workshop,
                logistics = M13LogisticsSaveData.CreateFresh(),
                retention = M13RetentionSaveData.CreateFresh(
                    legacy.lastWriteUtcUnixSeconds),
                industrial = M14IndustrialSaveData.CreateFresh()
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

        private static M9SaveDecodeResult DecodeVersion5(
            string json,
            M9SaveValidationSettings validationSettings,
            long fallbackUtcUnixSeconds)
        {
            Version5SaveData legacy;
            try
            {
                legacy = JsonUtility.FromJson<Version5SaveData>(json);
            }
            catch (Exception exception)
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.MalformedJson,
                    $"Save body could not be decoded: {exception.Message}");
            }

            string upgradeFailure = null;
            M10ProgressionSaveData upgradedProgression = null;
            M13LogisticsSaveData upgradedLogistics = null;
            if (legacy == null
                || !TryUpgradeLegacyProgression(
                    legacy.progression, 31, 8, 11, 19,
                    out upgradedProgression,
                    out upgradeFailure)
                || !TryUpgradeVersion5Logistics(
                    legacy.logistics,
                    out upgradedLogistics,
                    out upgradeFailure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed,
                    upgradeFailure ?? "Version 5 save body is missing.");
            }

            var decoded = new M9SaveData
            {
                schema = legacy.schema,
                version = M9SaveSchema.CurrentVersion,
                walletCash = legacy.walletCash,
                cashPileStoredCash = legacy.cashPileStoredCash,
                carry = SanitizeLegacyCarry(legacy.carry, true),
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
                mining = legacy.mining,
                workshop = legacy.workshop,
                logistics = upgradedLogistics,
                retention = legacy.retention,
                industrial = M14IndustrialSaveData.CreateFresh()
            };

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

            return M9SaveDecodeResult.Succeeded(normalized, true);
        }

        private static M9SaveDecodeResult DecodeVersion6(
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

            string upgradeFailure = null;
            M10ProgressionSaveData upgraded = null;
            if (decoded == null || !TryUpgradeLegacyProgression(decoded.progression,
                    39, 8, 16, 19, out upgraded, out upgradeFailure))
                return M9SaveDecodeResult.Failed(M9SaveDecodeStatus.ValidationFailed,
                    upgradeFailure ?? "Version 6 save body is missing.");
            decoded.version = M9SaveSchema.CurrentVersion;
            decoded.progression = upgraded;
            decoded.factoryScaling = M15FactorySaveData.CreateFresh();
            decoded.market = M16MarketSaveData.CreateFresh(fallbackUtcUnixSeconds);
            if (!M9SaveValidator.TryNormalize(decoded, validationSettings,
                    fallbackUtcUnixSeconds, out M9SaveData normalized,
                    out string failure))
            {
                return M9SaveDecodeResult.Failed(
                    M9SaveDecodeStatus.ValidationFailed, failure);
            }
            return M9SaveDecodeResult.Succeeded(normalized, true);
        }

        private static M9SaveDecodeResult DecodeVersion7(string json,
            M9SaveValidationSettings settings, long fallback)
        {
            M9SaveData decoded;
            try { decoded = JsonUtility.FromJson<M9SaveData>(json); }
            catch (Exception exception)
            { return M9SaveDecodeResult.Failed(M9SaveDecodeStatus.MalformedJson, exception.Message); }
            string upgradeFailure = null;
            M10ProgressionSaveData upgraded = null;
            if (decoded == null || !TryUpgradeLegacyProgression(decoded.progression,
                    39, 8, 16, 19, out upgraded, out upgradeFailure))
                return M9SaveDecodeResult.Failed(M9SaveDecodeStatus.ValidationFailed,
                    upgradeFailure ?? "Version 7 save body is missing.");
            decoded.version = M9SaveSchema.CurrentVersion;
            decoded.progression = upgraded;
            decoded.market = M16MarketSaveData.CreateFresh(fallback);
            return M9SaveValidator.TryNormalize(decoded, settings, fallback,
                    out M9SaveData normalized, out string failure)
                ? M9SaveDecodeResult.Succeeded(normalized, true)
                : M9SaveDecodeResult.Failed(M9SaveDecodeStatus.ValidationFailed, failure);
        }

        private static M9SaveDecodeResult DecodeVersion8(string json,
            M9SaveValidationSettings settings, long fallback)
        {
            M9SaveData decoded;
            try { decoded = JsonUtility.FromJson<M9SaveData>(json); }
            catch (Exception exception)
            { return M9SaveDecodeResult.Failed(M9SaveDecodeStatus.MalformedJson, exception.Message); }
            return M9SaveValidator.TryNormalize(decoded, settings, fallback,
                    out M9SaveData normalized, out string failure)
                ? M9SaveDecodeResult.Succeeded(normalized)
                : M9SaveDecodeResult.Failed(M9SaveDecodeStatus.ValidationFailed, failure);
        }

        private static bool TryUpgradeVersion5Logistics(
            M13LogisticsSaveData source,
            out M13LogisticsSaveData upgraded,
            out string failure)
        {
            upgraded = null;
            failure = null;
            if (source?.warehouse?.resources == null
                || source.warehouse.resources.Length != 6
                || source.routes == null)
            {
                failure = "Version 5 logistics state is missing or malformed.";
                return false;
            }

            upgraded = M13LogisticsSaveData.CreateFresh();
            for (int i = 0; i < 6; i++)
            {
                M13WarehouseResourceSaveRecord old = source.warehouse.resources[i];
                if (old == null || (int)old.resourceType != i)
                {
                    failure = "Version 5 Warehouse resources are not canonical.";
                    upgraded = null;
                    return false;
                }
                upgraded.warehouse.resources[i].amount = old.amount;
                upgraded.warehouse.resources[i].capacity = old.capacity;
            }
            M13RouteSaveRecord[] defaults = M13LogisticsSaveData.CreateFresh().routes;
            upgraded.routes = new M13RouteSaveRecord[source.routes.Length + 3];
            Array.Copy(source.routes, upgraded.routes, source.routes.Length);
            for (int i = 0; i < 3; i++)
                upgraded.routes[source.routes.Length + i] = defaults[3 + i];
            return true;
        }

        private static bool TryUpgradeLegacyProgression(
            M10ProgressionSaveData source,
            int legacyMetricCount,
            int legacyFlagCount,
            int legacyContractCount,
            int legacyAchievementCount,
            out M10ProgressionSaveData upgraded,
            out string failureReason)
        {
            upgraded = null;
            failureReason = null;
            if (source == null
                || source.metrics == null
                || source.metrics.Length != legacyMetricCount
                || source.flags == null
                || source.flags.Length != legacyFlagCount
                || source.claimedContracts == null
                || source.claimedContracts.Length
                != legacyContractCount
                || source.achievements == null
                || source.achievements.Length
                != legacyAchievementCount)
            {
                failureReason =
                    "Legacy progression has a missing or incorrectly sized canonical record set.";
                return false;
            }

            upgraded = M10ProgressionSaveData.CreateFresh();
            var seenMetrics = new bool[legacyMetricCount];
            for (int i = 0; i < source.metrics.Length; i++)
            {
                M10MetricSaveRecord record = source.metrics[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetMetricId(
                        record.id,
                        out ProgressMetricId metric)
                    || (int)metric < 0
                    || (int)metric >= legacyMetricCount
                    || seenMetrics[(int)metric])
                {
                    failureReason =
                        "Legacy progression contains a missing, unknown, or duplicate metric ID.";
                    upgraded = null;
                    return false;
                }

                seenMetrics[(int)metric] = true;
                upgraded.metrics[(int)metric].value = record.value;
            }

            var seenFlags = new bool[legacyFlagCount];
            for (int i = 0; i < source.flags.Length; i++)
            {
                M10FlagSaveRecord record = source.flags[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetFlagId(
                        record.id,
                        out ProgressFlagId flag)
                    || (int)flag < 0
                    || (int)flag >= legacyFlagCount
                    || seenFlags[(int)flag])
                {
                    failureReason =
                        "Legacy progression contains a missing, unknown, or duplicate flag ID.";
                    upgraded = null;
                    return false;
                }

                seenFlags[(int)flag] = true;
                upgraded.flags[(int)flag].value = record.value;
            }

            int claimedPrefix = 0;
            bool encounteredUnclaimed = false;
            for (int i = 0; i < source.claimedContracts.Length; i++)
            {
                if (!source.claimedContracts[i])
                {
                    encounteredUnclaimed = true;
                    continue;
                }

                if (encounteredUnclaimed)
                {
                    failureReason =
                        "Legacy progression contracts do not form an ordered claimed prefix.";
                    upgraded = null;
                    return false;
                }

                upgraded.claimedContracts[i] = true;
                claimedPrefix++;
            }

            if (source.activeContractIndex != claimedPrefix)
            {
                failureReason =
                    "Legacy progression active contract contradicts its claim state.";
                upgraded = null;
                return false;
            }

            upgraded.activeContractIndex = claimedPrefix;
            if (claimedPrefix >= legacyContractCount)
            {
                // Appended contracts always start non-retroactively after migration.
                LumberCampContractDefinition next =
                    LumberCampProgressionCatalog.GetContract(claimedPrefix);
                upgraded.activeContractBaseline = upgraded.GetMetric(next.Metric);
                upgraded.activeContractState =
                    M10ProgressionRules.IsContractEligible(upgraded, next)
                        ? ContractProgressState.Active
                        : ContractProgressState.Locked;
            }
            else
            {
                upgraded.activeContractBaseline = source.activeContractBaseline;
                upgraded.activeContractState = source.activeContractState;
            }

            var seenAchievements = new bool[legacyAchievementCount];
            for (int i = 0; i < source.achievements.Length; i++)
            {
                M10AchievementSaveRecord record = source.achievements[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetAchievementIndex(
                        record.id,
                        out int achievementIndex)
                    || achievementIndex < 0
                    || achievementIndex >= legacyAchievementCount
                    || seenAchievements[achievementIndex])
                {
                    failureReason =
                        "Legacy progression contains a missing, unknown, or duplicate achievement ID.";
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
            M9CarrySaveRecord legacy,
            bool includeMiningResources)
        {
            if (legacy == null)
            {
                return null;
            }

            if (legacy.resourceType == ResourceType.Wood
                || legacy.resourceType == ResourceType.Plank
                || legacy.resourceType == ResourceType.Crate
                || (includeMiningResources
                    && (legacy.resourceType == ResourceType.IronOre
                        || legacy.resourceType == ResourceType.IronBar
                        || legacy.resourceType == ResourceType.Tool)))
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
