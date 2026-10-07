using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Persistence;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    public enum RetentionGoalPeriod
    {
        Daily = 0,
        Weekly = 1
    }

    public enum RetentionGoalType
    {
        Produce = 0,
        Transport = 1,
        Sell = 2,
        EarnCash = 3
        ,UpgradeBuilding = 4
        ,UpgradeRoute = 5
        ,Deliver = 6
    }

    [Serializable]
    public sealed class M13GoalSaveRecord
    {
        public string id;
        public RetentionGoalType type;
        public ResourceType resourceType;
        public ProgressMetricId metric;
        public long baseline;
        public long progress;
        public long target;
        public int rewardCash;
        public bool completed;
        public bool completionReported;
        public bool claimed;
    }

    [Serializable]
    public sealed class M13GoalPeriodSaveData
    {
        public long periodId;
        public M13GoalSaveRecord[] goals;
    }

    [Serializable]
    public sealed class M13RetentionSaveData
    {
        public long lastAcceptedUtcUnixSeconds;
        public bool clockAnomalyDetected;
        public M13GoalPeriodSaveData daily;
        public M13GoalPeriodSaveData weekly;

        public static M13RetentionSaveData CreateFresh(long utcNowUnixSeconds = 0L)
        {
            return new M13RetentionSaveData
            {
                lastAcceptedUtcUnixSeconds = M9UnixTime.IsPlausible(
                    utcNowUnixSeconds)
                    ? utcNowUnixSeconds
                    : 0L,
                daily = new M13GoalPeriodSaveData
                {
                    periodId = 0L,
                    goals = Array.Empty<M13GoalSaveRecord>()
                },
                weekly = new M13GoalPeriodSaveData
                {
                    periodId = 0L,
                    goals = Array.Empty<M13GoalSaveRecord>()
                }
            };
        }
    }

    public readonly struct GoalUnlockContext
    {
        public GoalUnlockContext(
            bool processor,
            bool packing,
            bool mine,
            bool smelter,
            bool workshop,
            bool logistics,
            bool assemblyPlant = false)
        {
            Processor = processor;
            Packing = packing;
            Mine = mine;
            Smelter = smelter;
            Workshop = workshop;
            Logistics = logistics;
            AssemblyPlant = assemblyPlant;
        }

        public bool Processor { get; }
        public bool Packing { get; }
        public bool Mine { get; }
        public bool Smelter { get; }
        public bool Workshop { get; }
        public bool Logistics { get; }
        public bool AssemblyPlant { get; }
    }

    internal readonly struct GoalCandidate
    {
        public GoalCandidate(
            RetentionGoalType type,
            ResourceType resourceType,
            ProgressMetricId metric,
            long dailyTarget,
            int dailyReward)
        {
            Type = type;
            ResourceType = resourceType;
            Metric = metric;
            DailyTarget = dailyTarget;
            DailyReward = dailyReward;
        }

        public RetentionGoalType Type { get; }
        public ResourceType ResourceType { get; }
        public ProgressMetricId Metric { get; }
        public long DailyTarget { get; }
        public int DailyReward { get; }
    }

    public sealed class RetentionGoalModel
    {
        // Large device-clock jumps are ambiguous without a backend. Freeze period
        // advancement instead of risking repeatable reward resets.
        public const long MaximumAcceptedForwardStepSeconds = 45L * 24L * 60L * 60L;
        private const int GoalCountPerPeriod = 3;

        private readonly Func<ProgressMetricId, long> _getMetric;
        private readonly Func<int, bool> _tryGrantCash;
        private readonly Action _reportDailyCompletion;
        private M13RetentionSaveData _state;

        public RetentionGoalModel(
            M13RetentionSaveData state,
            Func<ProgressMetricId, long> getMetric,
            Func<int, bool> tryGrantCash,
            Action reportDailyCompletion)
        {
            _getMetric = getMetric ?? throw new ArgumentNullException(nameof(getMetric));
            _tryGrantCash = tryGrantCash;
            _reportDailyCompletion = reportDailyCompletion;
            _state = Normalize(state ?? M13RetentionSaveData.CreateFresh());
        }

        public event Action StateChanged;

        public M13GoalPeriodSaveData Daily => _state.daily;
        public M13GoalPeriodSaveData Weekly => _state.weekly;
        public bool ClockAnomalyDetected => _state.clockAnomalyDetected;

        public bool Evaluate(long utcNowUnixSeconds, GoalUnlockContext unlocks)
        {
            bool changed = AdvancePeriods(utcNowUnixSeconds, unlocks);
            changed |= EvaluatePeriod(_state.daily, true);
            changed |= EvaluatePeriod(_state.weekly, false);
            if (changed)
            {
                StateChanged?.Invoke();
            }

            return changed;
        }

        public bool TryClaim(RetentionGoalPeriod period, int goalIndex)
        {
            M13GoalPeriodSaveData periodData = period == RetentionGoalPeriod.Daily
                ? _state.daily
                : _state.weekly;
            if (periodData?.goals == null
                || goalIndex < 0
                || goalIndex >= periodData.goals.Length)
            {
                return false;
            }

            M13GoalSaveRecord goal = periodData.goals[goalIndex];
            if (goal == null
                || !goal.completed
                || goal.claimed
                || _tryGrantCash == null
                || !_tryGrantCash(goal.rewardCash))
            {
                return false;
            }

            goal.claimed = true;
            StateChanged?.Invoke();
            return true;
        }

        public M13RetentionSaveData CapturePersistentState()
        {
            return DeepClone(_state);
        }

        public void Restore(M13RetentionSaveData state)
        {
            _state = Normalize(state ?? M13RetentionSaveData.CreateFresh());
            StateChanged?.Invoke();
        }

        public static long GetDailyPeriodId(long utcUnixSeconds)
        {
            return Math.Max(0L, utcUnixSeconds) / 86400L;
        }

        public static long GetWeeklyPeriodId(long utcUnixSeconds)
        {
            long day = GetDailyPeriodId(utcUnixSeconds);
            return (day + 3L) / 7L; // Monday 00:00 UTC.
        }

        private bool AdvancePeriods(
            long utcNowUnixSeconds,
            GoalUnlockContext unlocks)
        {
            long accepted = _state.lastAcceptedUtcUnixSeconds;
            bool plausible = M9UnixTime.IsPlausible(utcNowUnixSeconds);
            long effective = accepted;
            bool changed = false;
            if (!plausible)
            {
                if (!_state.clockAnomalyDetected)
                {
                    _state.clockAnomalyDetected = true;
                    changed = true;
                }
            }
            else if (accepted <= 0L)
            {
                effective = utcNowUnixSeconds;
                _state.lastAcceptedUtcUnixSeconds = effective;
                changed = true;
            }
            else if (utcNowUnixSeconds < accepted)
            {
                if (!_state.clockAnomalyDetected)
                {
                    _state.clockAnomalyDetected = true;
                    changed = true;
                }
            }
            else if (utcNowUnixSeconds - accepted
                     > MaximumAcceptedForwardStepSeconds)
            {
                if (!_state.clockAnomalyDetected)
                {
                    _state.clockAnomalyDetected = true;
                    changed = true;
                }
            }
            else
            {
                effective = utcNowUnixSeconds;
                if (effective != accepted)
                {
                    _state.lastAcceptedUtcUnixSeconds = effective;
                    changed = true;
                }
            }

            if (effective <= 0L)
            {
                return changed;
            }

            long dailyId = GetDailyPeriodId(effective);
            long weeklyId = GetWeeklyPeriodId(effective);
            if (_state.daily.periodId == 0L || dailyId > _state.daily.periodId)
            {
                _state.daily = GeneratePeriod(
                    RetentionGoalPeriod.Daily,
                    dailyId,
                    unlocks);
                changed = true;
            }

            if (_state.weekly.periodId == 0L || weeklyId > _state.weekly.periodId)
            {
                _state.weekly = GeneratePeriod(
                    RetentionGoalPeriod.Weekly,
                    weeklyId,
                    unlocks);
                changed = true;
            }

            return changed;
        }

        private bool EvaluatePeriod(M13GoalPeriodSaveData period, bool daily)
        {
            if (period?.goals == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < period.goals.Length; i++)
            {
                M13GoalSaveRecord goal = period.goals[i];
                if (goal == null)
                {
                    continue;
                }

                long lifetime = Math.Max(0L, _getMetric(goal.metric));
                long progress = Math.Min(
                    goal.target,
                    Math.Max(0L, lifetime - goal.baseline));
                if (goal.progress != progress)
                {
                    goal.progress = progress;
                    changed = true;
                }

                if (!goal.completed && progress >= goal.target)
                {
                    goal.completed = true;
                    changed = true;
                }

                if (daily && goal.completed && !goal.completionReported)
                {
                    goal.completionReported = true;
                    _reportDailyCompletion?.Invoke();
                    changed = true;
                }
            }

            return changed;
        }

        private M13GoalPeriodSaveData GeneratePeriod(
            RetentionGoalPeriod period,
            long periodId,
            GoalUnlockContext unlocks)
        {
            List<GoalCandidate> candidates = BuildCandidates(unlocks);
            int count = Math.Min(GoalCountPerPeriod, candidates.Count);
            var goals = new M13GoalSaveRecord[count];
            int offset = candidates.Count > 0
                ? (int)((periodId + ((int)period * 17L)) % candidates.Count)
                : 0;
            for (int i = 0; i < count; i++)
            {
                GoalCandidate candidate = candidates[(offset + i) % candidates.Count];
                bool weekly = period == RetentionGoalPeriod.Weekly;
                long target = weekly
                    ? SaturatingMultiply(candidate.DailyTarget, 5L)
                    : candidate.DailyTarget;
                int reward = weekly
                    ? Math.Min(int.MaxValue, candidate.DailyReward * 4)
                    : candidate.DailyReward;
                goals[i] = new M13GoalSaveRecord
                {
                    id = BuildStableId(period, periodId, candidate),
                    type = candidate.Type,
                    resourceType = candidate.ResourceType,
                    metric = candidate.Metric,
                    baseline = Math.Max(0L, _getMetric(candidate.Metric)),
                    target = target,
                    rewardCash = reward
                };
            }

            return new M13GoalPeriodSaveData
            {
                periodId = periodId,
                goals = goals
            };
        }

        private static List<GoalCandidate> BuildCandidates(GoalUnlockContext unlocks)
        {
            var result = new List<GoalCandidate>
            {
                new GoalCandidate(
                    RetentionGoalType.Produce,
                    ResourceType.Wood,
                    ProgressMetricId.WoodProduced,
                    20L,
                    100),
                new GoalCandidate(
                    RetentionGoalType.Sell,
                    ResourceType.Wood,
                    ProgressMetricId.WoodSold,
                    10L,
                    120),
                new GoalCandidate(
                    RetentionGoalType.EarnCash,
                    ResourceType.Wood,
                    ProgressMetricId.TotalCashEarned,
                    500L,
                    150)
            };
            if (unlocks.Processor)
            {
                AddResourceGoals(
                    result,
                    ResourceType.Plank,
                    ProgressMetricId.PlanksProduced,
                    ProgressMetricId.PlanksSold,
                    10L,
                    8L,
                    160);
            }

            if (unlocks.Packing)
            {
                AddResourceGoals(
                    result,
                    ResourceType.Crate,
                    ProgressMetricId.CratesProduced,
                    ProgressMetricId.CratesSold,
                    5L,
                    4L,
                    220);
            }

            if (unlocks.Mine)
            {
                AddResourceGoals(
                    result,
                    ResourceType.IronOre,
                    ProgressMetricId.IronOreMined,
                    ProgressMetricId.IronOreSold,
                    12L,
                    8L,
                    180);
            }

            if (unlocks.Smelter)
            {
                AddResourceGoals(
                    result,
                    ResourceType.IronBar,
                    ProgressMetricId.IronBarsProduced,
                    ProgressMetricId.IronBarsSold,
                    6L,
                    4L,
                    240);
            }

            if (unlocks.Workshop)
            {
                AddResourceGoals(
                    result,
                    ResourceType.Tool,
                    ProgressMetricId.ToolsProduced,
                    ProgressMetricId.ToolsSold,
                    4L,
                    3L,
                    300);
            }

            if (unlocks.Logistics)
            {
                if (unlocks.Processor)
                {
                    result.Add(new GoalCandidate(
                        RetentionGoalType.Transport,
                        ResourceType.Plank,
                        ProgressMetricId.PlanksTransported,
                        8L,
                        220));
                }

                if (unlocks.Smelter)
                {
                    result.Add(new GoalCandidate(
                        RetentionGoalType.Transport,
                        ResourceType.IronBar,
                        ProgressMetricId.IronBarsTransported,
                        5L,
                        300));
                }

                result.Add(new GoalCandidate(
                    RetentionGoalType.UpgradeRoute,
                    ResourceType.Wood,
                    ProgressMetricId.LogisticsUpgradesPurchased,
                    1L,
                    500));
            }

            if (unlocks.AssemblyPlant)
            {
                AddResourceGoals(
                    result,
                    ResourceType.MachinePart,
                    ProgressMetricId.MachinePartsProduced,
                    ProgressMetricId.MachinePartsSold,
                    3L,
                    2L,
                    600);
                result.Add(new GoalCandidate(
                    RetentionGoalType.UpgradeBuilding,
                    ResourceType.MachinePart,
                    ProgressMetricId.ProductionUpgradesPurchased,
                    1L,
                    550));
                if (unlocks.Logistics)
                {
                    result.Add(new GoalCandidate(
                        RetentionGoalType.Deliver,
                        ResourceType.MachinePart,
                        ProgressMetricId.MachinePartsTransported,
                        2L,
                        700));
                }
            }

            return result;
        }

        private static void AddResourceGoals(
            ICollection<GoalCandidate> target,
            ResourceType resourceType,
            ProgressMetricId produced,
            ProgressMetricId sold,
            long produceTarget,
            long sellTarget,
            int reward)
        {
            target.Add(new GoalCandidate(
                RetentionGoalType.Produce,
                resourceType,
                produced,
                produceTarget,
                reward));
            target.Add(new GoalCandidate(
                RetentionGoalType.Sell,
                resourceType,
                sold,
                sellTarget,
                reward + 40));
        }

        private static string BuildStableId(
            RetentionGoalPeriod period,
            long periodId,
            GoalCandidate candidate)
        {
            return $"{period.ToString().ToLowerInvariant()}_{periodId}_"
                   + $"{candidate.Type.ToString().ToLowerInvariant()}_"
                   + $"{candidate.ResourceType.ToString().ToLowerInvariant()}";
        }

        private static long SaturatingMultiply(long value, long multiplier)
        {
            return value > long.MaxValue / multiplier
                ? long.MaxValue
                : value * multiplier;
        }

        private static M13RetentionSaveData Normalize(M13RetentionSaveData source)
        {
            var result = DeepClone(source);
            result.daily = result.daily ?? new M13GoalPeriodSaveData
            {
                goals = Array.Empty<M13GoalSaveRecord>()
            };
            result.weekly = result.weekly ?? new M13GoalPeriodSaveData
            {
                goals = Array.Empty<M13GoalSaveRecord>()
            };
            result.daily.goals = result.daily.goals
                                 ?? Array.Empty<M13GoalSaveRecord>();
            result.weekly.goals = result.weekly.goals
                                  ?? Array.Empty<M13GoalSaveRecord>();
            return result;
        }

        private static M13RetentionSaveData DeepClone(M13RetentionSaveData source)
        {
            if (source == null)
            {
                return M13RetentionSaveData.CreateFresh();
            }

            return new M13RetentionSaveData
            {
                lastAcceptedUtcUnixSeconds = source.lastAcceptedUtcUnixSeconds,
                clockAnomalyDetected = source.clockAnomalyDetected,
                daily = ClonePeriod(source.daily),
                weekly = ClonePeriod(source.weekly)
            };
        }

        private static M13GoalPeriodSaveData ClonePeriod(
            M13GoalPeriodSaveData source)
        {
            if (source == null)
            {
                return new M13GoalPeriodSaveData
                {
                    goals = Array.Empty<M13GoalSaveRecord>()
                };
            }

            M13GoalSaveRecord[] sourceGoals = source.goals
                                              ?? Array.Empty<M13GoalSaveRecord>();
            var goals = new M13GoalSaveRecord[sourceGoals.Length];
            for (int i = 0; i < goals.Length; i++)
            {
                M13GoalSaveRecord goal = sourceGoals[i];
                if (goal == null)
                {
                    continue;
                }

                goals[i] = new M13GoalSaveRecord
                {
                    id = goal.id,
                    type = goal.type,
                    resourceType = goal.resourceType,
                    metric = goal.metric,
                    baseline = Math.Max(0L, goal.baseline),
                    progress = Math.Max(0L, goal.progress),
                    target = Math.Max(1L, goal.target),
                    rewardCash = Math.Max(1, goal.rewardCash),
                    completed = goal.completed,
                    completionReported = goal.completionReported,
                    claimed = goal.claimed
                };
            }

            return new M13GoalPeriodSaveData
            {
                periodId = Math.Max(0L, source.periodId),
                goals = goals
            };
        }
    }

    public static class M13RetentionSaveValidator
    {
        public static bool TryNormalize(
            M13RetentionSaveData source,
            long fallbackUtcUnixSeconds,
            out M13RetentionSaveData normalized,
            out string failureReason)
        {
            normalized = null;
            failureReason = null;
            if (source == null
                || !TryNormalizePeriod(source.daily, out M13GoalPeriodSaveData daily)
                || !TryNormalizePeriod(source.weekly, out M13GoalPeriodSaveData weekly))
            {
                failureReason = "M13 retention goal state is missing or invalid.";
                return false;
            }

            long timestamp = M9UnixTime.IsPlausible(
                source.lastAcceptedUtcUnixSeconds)
                ? source.lastAcceptedUtcUnixSeconds
                : M9UnixTime.IsPlausible(fallbackUtcUnixSeconds)
                    ? fallbackUtcUnixSeconds
                    : 0L;
            normalized = new M13RetentionSaveData
            {
                lastAcceptedUtcUnixSeconds = timestamp,
                clockAnomalyDetected = source.clockAnomalyDetected,
                daily = daily,
                weekly = weekly
            };
            return true;
        }

        private static bool TryNormalizePeriod(
            M13GoalPeriodSaveData source,
            out M13GoalPeriodSaveData normalized)
        {
            normalized = null;
            if (source?.goals == null || source.goals.Length > 3)
            {
                return false;
            }

            var goals = new M13GoalSaveRecord[source.goals.Length];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.goals.Length; i++)
            {
                M13GoalSaveRecord goal = source.goals[i];
                if (goal == null
                    || string.IsNullOrWhiteSpace(goal.id)
                    || !ids.Add(goal.id)
                    || !Enum.IsDefined(typeof(RetentionGoalType), goal.type)
                    || !Enum.IsDefined(typeof(ResourceType), goal.resourceType)
                    || !Enum.IsDefined(typeof(ProgressMetricId), goal.metric)
                    || goal.target <= 0L
                    || goal.rewardCash <= 0
                    || goal.baseline < 0L
                    || goal.progress < 0L
                    || goal.progress > goal.target
                    || (goal.completed && goal.progress < goal.target)
                    || (goal.completionReported && !goal.completed)
                    || (goal.claimed && !goal.completed))
                {
                    return false;
                }

                goals[i] = new M13GoalSaveRecord
                {
                    id = goal.id,
                    type = goal.type,
                    resourceType = goal.resourceType,
                    metric = goal.metric,
                    baseline = goal.baseline,
                    progress = goal.progress,
                    target = goal.target,
                    rewardCash = goal.rewardCash,
                    completed = goal.completed,
                    completionReported = goal.completionReported,
                    claimed = goal.claimed
                };
            }

            normalized = new M13GoalPeriodSaveData
            {
                periodId = Math.Max(0L, source.periodId),
                goals = goals
            };
            return true;
        }
    }

    [DefaultExecutionOrder(-850)]
    public sealed class RetentionGoalService : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private Wallet wallet;

        private RetentionGoalModel _model;
        private double _nextClockCheck;

        public event Action StateChanged;

        public RetentionGoalModel Model => _model;
        public M13GoalPeriodSaveData Daily => _model?.Daily;
        public M13GoalPeriodSaveData Weekly => _model?.Weekly;

        private void Awake()
        {
            _model = CreateModel(M13RetentionSaveData.CreateFresh());
            _model.StateChanged += HandleStateChanged;
        }

        private void OnEnable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged += HandleProgressionChanged;
            }
        }

        private void Start()
        {
            EvaluateNow();
        }

        private void Update()
        {
            if (Time.realtimeSinceStartupAsDouble < _nextClockCheck)
            {
                return;
            }

            _nextClockCheck = Time.realtimeSinceStartupAsDouble + 30d;
            EvaluateNow();
        }

        private void OnDisable()
        {
            if (progressionService != null)
            {
                progressionService.StateChanged -= HandleProgressionChanged;
            }
        }

        private void OnDestroy()
        {
            if (_model != null)
            {
                _model.StateChanged -= HandleStateChanged;
            }
        }

        public bool EvaluateNow()
        {
            return _model != null && _model.Evaluate(
                SystemUtcClock.Instance.UtcNowUnixSeconds,
                BuildUnlockContext());
        }

        public bool TryClaim(RetentionGoalPeriod period, int goalIndex)
        {
            return _model != null && _model.TryClaim(period, goalIndex);
        }

        public M13RetentionSaveData CapturePersistentState()
        {
            return _model?.CapturePersistentState()
                   ?? M13RetentionSaveData.CreateFresh();
        }

        public void RestorePersistentState(
            M13RetentionSaveData state,
            long utcNowUnixSeconds)
        {
            _model.Restore(state);
            _model.Evaluate(utcNowUnixSeconds, BuildUnlockContext());
        }

        private RetentionGoalModel CreateModel(M13RetentionSaveData state)
        {
            return new RetentionGoalModel(
                state,
                metric => progressionService != null
                    ? progressionService.GetMetric(metric)
                    : 0L,
                TryGrantCash,
                () => progressionService?.RecordDailyGoalCompleted());
        }

        private GoalUnlockContext BuildUnlockContext()
        {
            return new GoalUnlockContext(
                progressionService != null
                && progressionService.GetFlag(ProgressFlagId.ProcessorUnlocked),
                progressionService != null
                && progressionService.GetFlag(ProgressFlagId.PackingStationUnlocked),
                progressionService != null
                && progressionService.GetMetric(ProgressMetricId.MineUnlocked) > 0L,
                progressionService != null
                && progressionService.GetFlag(ProgressFlagId.SmelterUnlocked),
                progressionService != null
                && progressionService.GetMetric(
                    ProgressMetricId.ToolWorkshopUnlocked) > 0L,
                progressionService != null
                && progressionService.GetMetric(
                    ProgressMetricId.IndustrialRouteOperational) > 0L,
                progressionService != null
                && progressionService.GetMetric(
                    ProgressMetricId.AssemblyPlantUnlocked) > 0L);
        }

        private bool TryGrantCash(int amount)
        {
            return amount > 0
                   && wallet != null
                   && wallet.Balance <= int.MaxValue - amount
                   && wallet.Deposit(amount) == amount;
        }

        private void HandleProgressionChanged()
        {
            EvaluateNow();
        }

        private void HandleStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
