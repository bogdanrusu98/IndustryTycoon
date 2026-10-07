using System;
using IndustryTycoon.Economy;
using IndustryTycoon.Persistence;
using UnityEngine;

namespace IndustryTycoon.Progression
{
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

        private void Start() => EvaluateNow();

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

        public bool EvaluateNow() => _model != null && _model.Evaluate(
            SystemUtcClock.Instance.UtcNowUnixSeconds,
            BuildUnlockContext());

        public bool TryClaim(RetentionGoalPeriod period, int goalIndex) =>
            _model != null && _model.TryClaim(period, goalIndex);

        public M13RetentionSaveData CapturePersistentState() =>
            _model?.CapturePersistentState() ?? M13RetentionSaveData.CreateFresh();

        public void RestorePersistentState(
            M13RetentionSaveData state,
            long utcNowUnixSeconds)
        {
            _model.Restore(state);
            _model.Evaluate(utcNowUnixSeconds, BuildUnlockContext());
        }

        private RetentionGoalModel CreateModel(M13RetentionSaveData state) =>
            new RetentionGoalModel(
                state,
                metric => progressionService != null
                    ? progressionService.GetMetric(metric)
                    : 0L,
                TryGrantCash,
                () => progressionService?.RecordDailyGoalCompleted());

        private GoalUnlockContext BuildUnlockContext() => new GoalUnlockContext(
            progressionService != null
            && progressionService.GetFlag(ProgressFlagId.ProcessorUnlocked),
            progressionService != null
            && progressionService.GetFlag(ProgressFlagId.PackingStationUnlocked),
            progressionService != null
            && progressionService.GetMetric(ProgressMetricId.MineUnlocked) > 0L,
            progressionService != null
            && progressionService.GetFlag(ProgressFlagId.SmelterUnlocked),
            progressionService != null
            && progressionService.GetMetric(ProgressMetricId.ToolWorkshopUnlocked) > 0L,
            progressionService != null
            && progressionService.GetMetric(ProgressMetricId.IndustrialRouteOperational) > 0L,
            progressionService != null
            && progressionService.GetMetric(ProgressMetricId.AssemblyPlantUnlocked) > 0L);

        private bool TryGrantCash(int amount) => amount > 0
                                                 && wallet != null
                                                 && wallet.Balance
                                                 <= int.MaxValue - amount
                                                 && wallet.Deposit(amount) == amount;

        private void HandleProgressionChanged() => EvaluateNow();
        private void HandleStateChanged() => StateChanged?.Invoke();
    }
}
