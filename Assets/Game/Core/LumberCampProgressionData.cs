using System;

namespace IndustryTycoon.Progression
{
    [Serializable]
    public sealed class M10MetricSaveRecord
    {
        public string id;
        public long value;
    }

    [Serializable]
    public sealed class M10FlagSaveRecord
    {
        public string id;
        public bool value;
    }

    [Serializable]
    public sealed class M10AchievementSaveRecord
    {
        public string id;
        public bool unlocked;
        public bool rewarded;
    }

    [Serializable]
    public sealed class M10ProgressionSaveData
    {
        public M10MetricSaveRecord[] metrics;
        public M10FlagSaveRecord[] flags;
        public int objectiveIndex;

        public int activeContractIndex;
        public long activeContractBaseline;
        public ContractProgressState activeContractState;
        public bool[] claimedContracts;

        public M10AchievementSaveRecord[] achievements;

        public static M10ProgressionSaveData CreateFresh()
        {
            var data = new M10ProgressionSaveData
            {
                metrics = new M10MetricSaveRecord[
                    LumberCampProgressionCatalog.MetricCount],
                flags = new M10FlagSaveRecord[
                    LumberCampProgressionCatalog.FlagCount],
                objectiveIndex = 0,
                activeContractIndex = 0,
                activeContractBaseline = 0L,
                activeContractState = ContractProgressState.Active,
                claimedContracts = new bool[
                    LumberCampProgressionCatalog.ContractCount],
                achievements = new M10AchievementSaveRecord[
                    LumberCampProgressionCatalog.AchievementCount]
            };

            for (int i = 0; i < data.metrics.Length; i++)
            {
                data.metrics[i] = new M10MetricSaveRecord
                {
                    id = LumberCampProgressionCatalog.GetMetricStableId(
                        (ProgressMetricId)i)
                };
            }

            for (int i = 0; i < data.flags.Length; i++)
            {
                data.flags[i] = new M10FlagSaveRecord
                {
                    id = LumberCampProgressionCatalog.GetFlagStableId(
                        (ProgressFlagId)i)
                };
            }

            for (int i = 0; i < data.achievements.Length; i++)
            {
                data.achievements[i] = new M10AchievementSaveRecord
                {
                    id = LumberCampProgressionCatalog.GetAchievement(i).StableId
                };
            }

            return data;
        }

        public M10ProgressionSaveData DeepClone()
        {
            var clone = CreateFresh();
            for (int i = 0; i < clone.metrics.Length; i++)
            {
                clone.metrics[i].value = GetMetric((ProgressMetricId)i);
            }

            for (int i = 0; i < clone.flags.Length; i++)
            {
                clone.flags[i].value = GetFlag((ProgressFlagId)i);
            }

            clone.objectiveIndex = objectiveIndex;
            clone.activeContractIndex = activeContractIndex;
            clone.activeContractBaseline = activeContractBaseline;
            clone.activeContractState = activeContractState;
            if (claimedContracts != null)
            {
                int copyCount = Math.Min(
                    claimedContracts.Length,
                    clone.claimedContracts.Length);
                Array.Copy(claimedContracts, clone.claimedContracts, copyCount);
            }

            if (achievements != null)
            {
                for (int i = 0; i < clone.achievements.Length; i++)
                {
                    M10AchievementSaveRecord source = FindAchievementRecord(i);
                    if (source == null)
                    {
                        continue;
                    }

                    clone.achievements[i].unlocked = source.unlocked;
                    clone.achievements[i].rewarded = source.rewarded;
                }
            }

            return clone;
        }

        public long GetMetric(ProgressMetricId metric)
        {
            string stableId = LumberCampProgressionCatalog.GetMetricStableId(metric);
            if (metrics != null)
            {
                for (int i = 0; i < metrics.Length; i++)
                {
                    M10MetricSaveRecord record = metrics[i];
                    if (record != null
                        && string.Equals(record.id, stableId, StringComparison.Ordinal))
                    {
                        return Math.Max(0L, record.value);
                    }
                }
            }

            return 0L;
        }

        public bool GetFlag(ProgressFlagId flag)
        {
            string stableId = LumberCampProgressionCatalog.GetFlagStableId(flag);
            if (flags != null)
            {
                for (int i = 0; i < flags.Length; i++)
                {
                    M10FlagSaveRecord record = flags[i];
                    if (record != null
                        && string.Equals(record.id, stableId, StringComparison.Ordinal))
                    {
                        return record.value;
                    }
                }
            }

            return false;
        }

        public M10AchievementSaveRecord FindAchievementRecord(int achievementIndex)
        {
            if (achievementIndex < 0
                || achievementIndex >= LumberCampProgressionCatalog.AchievementCount
                || achievements == null)
            {
                return null;
            }

            string stableId = LumberCampProgressionCatalog
                .GetAchievement(achievementIndex)
                .StableId;
            for (int i = 0; i < achievements.Length; i++)
            {
                M10AchievementSaveRecord record = achievements[i];
                if (record != null
                    && string.Equals(record.id, stableId, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        internal bool IncrementMetric(ProgressMetricId metric, long amount)
        {
            if (amount <= 0L || metrics == null)
            {
                return false;
            }

            int index = (int)metric;
            if (index < 0 || index >= metrics.Length || metrics[index] == null)
            {
                return false;
            }

            long current = Math.Max(0L, metrics[index].value);
            metrics[index].value = current > long.MaxValue - amount
                ? long.MaxValue
                : current + amount;
            return metrics[index].value != current;
        }

        internal bool SetMetricOnce(ProgressMetricId metric)
        {
            if (metrics == null)
            {
                return false;
            }

            int index = (int)metric;
            if (index < 0
                || index >= metrics.Length
                || metrics[index] == null
                || metrics[index].value >= 1L)
            {
                return false;
            }

            metrics[index].value = 1L;
            return true;
        }

        internal bool SetFlag(ProgressFlagId flag)
        {
            if (flags == null)
            {
                return false;
            }

            int index = (int)flag;
            if (index < 0
                || index >= flags.Length
                || flags[index] == null
                || flags[index].value)
            {
                return false;
            }

            flags[index].value = true;
            return true;
        }

        internal void GrandfatherAchievement(LumberCampAchievementId achievement)
        {
            M10AchievementSaveRecord record = FindAchievementRecord((int)achievement);
            if (record == null)
            {
                return;
            }

            // Exact v1 unlock/completion facts are retained without changing the
            // legacy Wallet. Marking the reward handled prevents migration cash.
            record.unlocked = true;
            record.rewarded = true;
        }
    }

    public static class M10ProgressionRules
    {
        public static bool IsObjectiveSatisfied(
            M10ProgressionSaveData data,
            int objectiveIndex)
        {
            if (data == null
                || objectiveIndex < 0
                || objectiveIndex >= LumberCampProgressionCatalog.ObjectiveCount)
            {
                return false;
            }

            MainObjectiveDefinition definition =
                LumberCampProgressionCatalog.GetObjective(objectiveIndex);
            return definition.ConditionKind == ObjectiveConditionKind.Flag
                ? data.GetFlag(definition.Flag)
                : data.GetMetric(definition.Metric) >= definition.Target;
        }

        public static int ResolveObjectiveIndex(M10ProgressionSaveData data)
        {
            int index = 0;
            while (index < LumberCampProgressionCatalog.ObjectiveCount
                   && IsObjectiveSatisfied(data, index))
            {
                index++;
            }

            return index;
        }

        public static void GetAchievementProgress(
            M10ProgressionSaveData data,
            int achievementIndex,
            out long current,
            out long target)
        {
            current = 0L;
            target = 1L;
            if (data == null
                || achievementIndex < 0
                || achievementIndex >= LumberCampProgressionCatalog.AchievementCount)
            {
                return;
            }

            switch ((LumberCampAchievementId)achievementIndex)
            {
                case LumberCampAchievementId.FirstSale:
                    current = SaturatingAdd(
                        data.GetMetric(ProgressMetricId.WoodSold),
                        data.GetMetric(ProgressMetricId.PlanksSold),
                        data.GetMetric(ProgressMetricId.CratesSold));
                    current = SaturatingAdd(
                        current,
                        data.GetMetric(ProgressMetricId.IronOreSold));
                    current = SaturatingAdd(
                        current,
                        data.GetMetric(ProgressMetricId.IronBarsSold));
                    break;
                case LumberCampAchievementId.FirstHire:
                    current = BoolToLong(data.GetFlag(ProgressFlagId.WorkerUnlocked));
                    break;
                case LumberCampAchievementId.ProcessingBegins:
                    current = BoolToLong(data.GetFlag(ProgressFlagId.ProcessorUnlocked));
                    break;
                case LumberCampAchievementId.AutomationOnline:
                    current = BoolToLong(data.GetFlag(ProgressFlagId.AutoFeederUnlocked));
                    break;
                case LumberCampAchievementId.PackedAndReady:
                    current = data.GetMetric(ProgressMetricId.CratesProduced);
                    break;
                case LumberCampAchievementId.DeliveryService:
                    current = BoolToLong(data.GetFlag(ProgressFlagId.CourierUnlocked));
                    break;
                case LumberCampAchievementId.Lumberjack:
                    current = data.GetMetric(ProgressMetricId.WoodCollected);
                    target = 100L;
                    break;
                case LumberCampAchievementId.MassProduction:
                    current = data.GetMetric(ProgressMetricId.WoodProduced);
                    target = 100L;
                    break;
                case LumberCampAchievementId.PlankFactory:
                    current = data.GetMetric(ProgressMetricId.PlanksProduced);
                    target = 50L;
                    break;
                case LumberCampAchievementId.CrateMaker:
                    current = data.GetMetric(ProgressMetricId.CratesProduced);
                    target = 25L;
                    break;
                case LumberCampAchievementId.OnTheRoad:
                    current = data.GetMetric(ProgressMetricId.CourierTripsCompleted);
                    target = 10L;
                    break;
                case LumberCampAchievementId.Merchant:
                    current = data.GetMetric(ProgressMetricId.TotalCashEarned);
                    target = 2500L;
                    break;
                case LumberCampAchievementId.TycoonInTraining:
                    current = data.GetMetric(ProgressMetricId.TotalCashEarned);
                    target = 10000L;
                    break;
                case LumberCampAchievementId.FullyAutomatedInput:
                    current = BoolToLong(data.GetFlag(ProgressFlagId.WorkerUnlocked))
                              + BoolToLong(
                                  data.GetFlag(ProgressFlagId.AutoFeederUnlocked));
                    target = 2L;
                    break;
                case LumberCampAchievementId.LumberCampComplete:
                    current = BoolToLong(
                        data.GetFlag(ProgressFlagId.LumberCampCompleted));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(achievementIndex));
            }
        }

        public static bool IsAchievementSatisfied(
            M10ProgressionSaveData data,
            int achievementIndex)
        {
            GetAchievementProgress(data, achievementIndex, out long current, out long target);
            return current >= target;
        }

        private static long BoolToLong(bool value)
        {
            return value ? 1L : 0L;
        }

        private static long SaturatingAdd(long first, long second, long third)
        {
            long total = SaturatingAdd(first, second);
            return SaturatingAdd(total, third);
        }

        private static long SaturatingAdd(long first, long second)
        {
            first = Math.Max(0L, first);
            second = Math.Max(0L, second);
            return first > long.MaxValue - second ? long.MaxValue : first + second;
        }
    }

    public static class M10ProgressionSaveValidator
    {
        public static bool TryNormalize(
            M10ProgressionSaveData source,
            out M10ProgressionSaveData normalized,
            out string failureReason)
        {
            normalized = null;
            failureReason = null;
            if (source == null)
            {
                failureReason = "M10 progression data is missing.";
                return false;
            }

            if (!TryNormalizeMetrics(source, out M10MetricSaveRecord[] metrics, out failureReason)
                || !TryNormalizeFlags(source, out M10FlagSaveRecord[] flags, out failureReason)
                || !TryNormalizeAchievements(
                    source,
                    out M10AchievementSaveRecord[] achievements,
                    out failureReason))
            {
                return false;
            }

            if (source.claimedContracts == null
                || source.claimedContracts.Length
                != LumberCampProgressionCatalog.ContractCount)
            {
                failureReason = "M10 contract claim state has the wrong size.";
                return false;
            }

            if (!Enum.IsDefined(
                    typeof(ContractProgressState),
                    source.activeContractState))
            {
                failureReason = "M10 contract state is unknown.";
                return false;
            }

            normalized = M10ProgressionSaveData.CreateFresh();
            normalized.metrics = metrics;
            normalized.flags = flags;
            normalized.achievements = achievements;
            for (int i = 0; i < normalized.achievements.Length; i++)
            {
                if (normalized.achievements[i].unlocked
                    && !M10ProgressionRules.IsAchievementSatisfied(normalized, i))
                {
                    failureReason =
                        "M10 unlocked achievement contradicts shared metrics/flags.";
                    normalized = null;
                    return false;
                }
            }

            int activeIndex = 0;
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
                        "M10 claimed contracts must form one ordered prefix.";
                    return false;
                }

                normalized.claimedContracts[i] = true;
                activeIndex++;
            }

            if (source.activeContractIndex != activeIndex)
            {
                failureReason =
                    "M10 active contract index contradicts claimed state.";
                return false;
            }

            normalized.activeContractIndex = activeIndex;
            if (activeIndex >= LumberCampProgressionCatalog.ContractCount)
            {
                normalized.activeContractBaseline = 0L;
                normalized.activeContractState = ContractProgressState.Claimed;
            }
            else
            {
                LumberCampContractDefinition contract =
                    LumberCampProgressionCatalog.GetContract(activeIndex);
                long currentMetric = normalized.GetMetric(contract.Metric);
                normalized.activeContractBaseline = ClampLong(
                    source.activeContractBaseline,
                    0L,
                    currentMetric);
                long progress = currentMetric - normalized.activeContractBaseline;
                normalized.activeContractState = progress >= contract.Target
                    ? ContractProgressState.CompletedUnclaimed
                    : ContractProgressState.Active;
            }

            if (source.activeContractState != normalized.activeContractState)
            {
                failureReason =
                    "M10 active contract state contradicts shared metrics.";
                normalized = null;
                return false;
            }

            // The objective cursor is a persisted cache only; lifetime metrics/flags
            // remain the sole progression truth.
            normalized.objectiveIndex = M10ProgressionRules.ResolveObjectiveIndex(normalized);
            return true;
        }

        private static bool TryNormalizeMetrics(
            M10ProgressionSaveData source,
            out M10MetricSaveRecord[] normalized,
            out string failureReason)
        {
            normalized = null;
            failureReason = null;
            if (source.metrics == null
                || source.metrics.Length != LumberCampProgressionCatalog.MetricCount)
            {
                failureReason = "M10 metric records have the wrong size.";
                return false;
            }

            normalized = new M10MetricSaveRecord[
                LumberCampProgressionCatalog.MetricCount];
            var seen = new bool[normalized.Length];
            for (int i = 0; i < source.metrics.Length; i++)
            {
                M10MetricSaveRecord record = source.metrics[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetMetricId(
                        record.id,
                        out ProgressMetricId metric)
                    || seen[(int)metric])
                {
                    failureReason = "M10 metric records contain a missing, unknown, or duplicate ID.";
                    return false;
                }

                int index = (int)metric;
                seen[index] = true;
                normalized[index] = new M10MetricSaveRecord
                {
                    id = LumberCampProgressionCatalog.GetMetricStableId(metric),
                    value = IsBinaryMetric(metric)
                        ? ClampLong(record.value, 0L, 1L)
                        : Math.Max(0L, record.value)
                };
            }

            return true;
        }

        private static bool IsBinaryMetric(ProgressMetricId metric)
        {
            return metric == ProgressMetricId.MineUnlocked
                   || metric == ProgressMetricId.DrillUnlocked;
        }

        private static bool TryNormalizeFlags(
            M10ProgressionSaveData source,
            out M10FlagSaveRecord[] normalized,
            out string failureReason)
        {
            normalized = null;
            failureReason = null;
            if (source.flags == null
                || source.flags.Length != LumberCampProgressionCatalog.FlagCount)
            {
                failureReason = "M10 progress flag records have the wrong size.";
                return false;
            }

            normalized = new M10FlagSaveRecord[
                LumberCampProgressionCatalog.FlagCount];
            var seen = new bool[normalized.Length];
            for (int i = 0; i < source.flags.Length; i++)
            {
                M10FlagSaveRecord record = source.flags[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetFlagId(
                        record.id,
                        out ProgressFlagId flag)
                    || seen[(int)flag])
                {
                    failureReason = "M10 progress flags contain a missing, unknown, or duplicate ID.";
                    return false;
                }

                int index = (int)flag;
                seen[index] = true;
                normalized[index] = new M10FlagSaveRecord
                {
                    id = LumberCampProgressionCatalog.GetFlagStableId(flag),
                    value = record.value
                };
            }

            return true;
        }

        private static bool TryNormalizeAchievements(
            M10ProgressionSaveData source,
            out M10AchievementSaveRecord[] normalized,
            out string failureReason)
        {
            normalized = null;
            failureReason = null;
            if (source.achievements == null
                || source.achievements.Length
                != LumberCampProgressionCatalog.AchievementCount)
            {
                failureReason = "M10 achievement records have the wrong size.";
                return false;
            }

            normalized = new M10AchievementSaveRecord[
                LumberCampProgressionCatalog.AchievementCount];
            var seen = new bool[normalized.Length];
            for (int i = 0; i < source.achievements.Length; i++)
            {
                M10AchievementSaveRecord record = source.achievements[i];
                if (record == null
                    || !LumberCampProgressionCatalog.TryGetAchievementIndex(
                        record.id,
                        out int achievementIndex)
                    || seen[achievementIndex])
                {
                    failureReason = "M10 achievements contain a missing, unknown, or duplicate ID.";
                    return false;
                }

                if (record.rewarded && !record.unlocked)
                {
                    failureReason =
                        "M10 rewarded achievement is not marked unlocked.";
                    return false;
                }

                seen[achievementIndex] = true;
                normalized[achievementIndex] = new M10AchievementSaveRecord
                {
                    id = LumberCampProgressionCatalog
                        .GetAchievement(achievementIndex)
                        .StableId,
                    unlocked = record.unlocked,
                    rewarded = record.rewarded
                };
            }

            return true;
        }

        private static long ClampLong(long value, long minimum, long maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }
}
