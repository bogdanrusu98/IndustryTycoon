using System;
using IndustryTycoon.Core;

namespace IndustryTycoon.Progression
{
    public sealed class LumberCampProgressionModel
    {
        private readonly Func<int, bool> _tryGrantRewardCash;
        private M10ProgressionSaveData _state;
        private bool _isEvaluating;
        private bool _evaluationPending;
        private bool _reentrantMutationChanged;
        private int _lastNotifiedEmpireLevel;

        public LumberCampProgressionModel(
            M10ProgressionSaveData state,
            Func<int, bool> tryGrantRewardCash)
        {
            _tryGrantRewardCash = tryGrantRewardCash;
            Restore(state ?? M10ProgressionSaveData.CreateFresh());
        }

        public event Action StateChanged;
        public event Action<int> AchievementUnlocked;
        public event Action<int> EmpireLevelAdvanced;

        public int ObjectiveIndex => _state.objectiveIndex;
        public bool AreAllObjectivesCompleted => ObjectiveIndex
                                                 >= LumberCampProgressionCatalog
                                                     .ObjectiveCount;
        public int ActiveContractIndex => _state.activeContractIndex;
        private bool HasContractCursor => ActiveContractIndex >= 0
                                          && ActiveContractIndex
                                          < LumberCampProgressionCatalog.ContractCount;
        public bool HasActiveContract => HasContractCursor
                                         && _state.activeContractState
                                         != ContractProgressState.Locked;
        public bool HasPendingContract => HasContractCursor;
        public ContractProgressState ActiveContractState =>
            HasContractCursor
                ? _state.activeContractState
                : ContractProgressState.Claimed;
        public int EmpireLevel => M10ProgressionRules.ResolveEmpireLevel(_state);

        public long GetMetric(ProgressMetricId metric)
        {
            return _state.GetMetric(metric);
        }

        public bool GetFlag(ProgressFlagId flag)
        {
            return _state.GetFlag(flag);
        }

        public bool IsContractClaimed(int contractIndex)
        {
            return contractIndex >= 0
                   && contractIndex < LumberCampProgressionCatalog.ContractCount
                   && _state.claimedContracts != null
                   && contractIndex < _state.claimedContracts.Length
                   && _state.claimedContracts[contractIndex];
        }

        public bool IsAchievementUnlocked(int achievementIndex)
        {
            M10AchievementSaveRecord record =
                _state.FindAchievementRecord(achievementIndex);
            return record != null && record.unlocked;
        }

        public bool IsAchievementRewarded(int achievementIndex)
        {
            M10AchievementSaveRecord record =
                _state.FindAchievementRecord(achievementIndex);
            return record != null && record.rewarded;
        }

        public void GetObjectiveProgress(out long current, out long target)
        {
            if (AreAllObjectivesCompleted)
            {
                current = 1L;
                target = 1L;
                return;
            }

            MainObjectiveDefinition objective =
                LumberCampProgressionCatalog.GetObjective(ObjectiveIndex);
            target = objective.Target;
            switch (objective.ConditionKind)
            {
                case ObjectiveConditionKind.Metric:
                    current = Math.Min(target, _state.GetMetric(objective.Metric));
                    break;
                case ObjectiveConditionKind.Flag:
                    current = _state.GetFlag(objective.Flag) ? 1L : 0L;
                    break;
                case ObjectiveConditionKind.EmpireLevel:
                    current = Math.Min(target, EmpireLevel);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(objective.ConditionKind));
            }
        }

        public string BuildObjectiveDisplayText()
        {
            if (AreAllObjectivesCompleted)
            {
                return "OBJECTIVES COMPLETE — EMPIRE LEVEL 5";
            }

            MainObjectiveDefinition objective =
                LumberCampProgressionCatalog.GetObjective(ObjectiveIndex);
            if (objective.ConditionKind == ObjectiveConditionKind.Flag)
            {
                return $"OBJECTIVE: {objective.Label}";
            }

            GetObjectiveProgress(out long current, out long target);
            return $"OBJECTIVE: {objective.Label} — {current} / {target}";
        }

        public void GetActiveContractProgress(out long current, out long target)
        {
            current = 0L;
            target = 0L;
            if (!HasActiveContract)
            {
                return;
            }

            LumberCampContractDefinition contract =
                LumberCampProgressionCatalog.GetContract(ActiveContractIndex);
            target = contract.Target;
            if (_state.activeContractState == ContractProgressState.Locked)
            {
                return;
            }

            long lifetime = _state.GetMetric(contract.Metric);
            current = Math.Min(
                target,
                Math.Max(0L, lifetime - _state.activeContractBaseline));
        }

        public void GetAchievementProgress(
            int achievementIndex,
            out long current,
            out long target)
        {
            M10ProgressionRules.GetAchievementProgress(
                _state,
                achievementIndex,
                out current,
                out target);
            current = Math.Min(current, target);
        }

        public M10ProgressionSaveData CapturePersistentState()
        {
            return _state.DeepClone();
        }

        public void Restore(M10ProgressionSaveData state)
        {
            if (!M10ProgressionSaveValidator.TryNormalize(
                    state,
                    out M10ProgressionSaveData normalized,
                    out string failure))
            {
                throw new ArgumentException(failure, nameof(state));
            }

            _state = normalized;
            // Loading/resetting establishes the comparison baseline silently. Only
            // subsequent authoritative mutations may publish level-up feedback.
            _lastNotifiedEmpireLevel =
                M10ProgressionRules.ResolveEmpireLevel(_state);
        }

        public bool EvaluateAll()
        {
            if (_isEvaluating)
            {
                _evaluationPending = true;
                return false;
            }

            bool changed = EvaluateTransitions();
            if (changed)
            {
                StateChanged?.Invoke();
            }

            return changed;
        }

        public bool RecordWoodProduced(int amount)
        {
            return RecordSingleMetric(ProgressMetricId.WoodProduced, amount);
        }

        public bool RecordPlayerCollection(ResourceType resourceType, int amount)
        {
            return resourceType == ResourceType.Wood
                && RecordSingleMetric(ProgressMetricId.WoodCollected, amount);
        }

        public bool RecordSale(ResourceType resourceType, int quantity, int cashValue)
        {
            if (quantity <= 0 || cashValue <= 0)
            {
                return false;
            }

            ProgressMetricId soldMetric;
            switch (resourceType)
            {
                case ResourceType.Wood:
                    soldMetric = ProgressMetricId.WoodSold;
                    break;
                case ResourceType.Plank:
                    soldMetric = ProgressMetricId.PlanksSold;
                    break;
                case ResourceType.Crate:
                    soldMetric = ProgressMetricId.CratesSold;
                    break;
                case ResourceType.IronOre:
                    soldMetric = ProgressMetricId.IronOreSold;
                    break;
                case ResourceType.IronBar:
                    soldMetric = ProgressMetricId.IronBarsSold;
                    break;
                case ResourceType.Tool:
                    soldMetric = ProgressMetricId.ToolsSold;
                    break;
                default:
                    return false;
            }

            bool changed = _state.IncrementMetric(soldMetric, quantity);
            changed |= _state.IncrementMetric(
                ProgressMetricId.TotalCashEarned,
                cashValue);
            return FinishAuthoritativeMutation(changed);
        }

        public bool RecordPlanksProduced(int amount)
        {
            return RecordSingleMetric(ProgressMetricId.PlanksProduced, amount);
        }

        public bool RecordCratesProduced(int amount)
        {
            return RecordSingleMetric(ProgressMetricId.CratesProduced, amount);
        }

        public bool RecordIronOreMined(int amount)
        {
            return RecordSingleMetric(ProgressMetricId.IronOreMined, amount);
        }

        public bool RecordIronOreProduced(int amount)
        {
            return RecordSingleMetric(ProgressMetricId.IronOreProduced, amount);
        }

        public bool RecordIronBarsProduced(int amount)
        {
            return RecordSingleMetric(ProgressMetricId.IronBarsProduced, amount);
        }

        public bool RecordToolsProduced(int amount)
        {
            return RecordSingleMetric(ProgressMetricId.ToolsProduced, amount);
        }

        public bool RecordMineUnlocked()
        {
            return FinishAuthoritativeMutation(
                _state.SetMetricOnce(ProgressMetricId.MineUnlocked));
        }

        public bool RecordDrillUnlocked()
        {
            return FinishAuthoritativeMutation(
                _state.SetMetricOnce(ProgressMetricId.DrillUnlocked));
        }

        public bool RecordToolWorkshopUnlocked()
        {
            return FinishAuthoritativeMutation(
                _state.SetMetricOnce(ProgressMetricId.ToolWorkshopUnlocked));
        }

        public bool RecordCourierDelivery(int crateCount, int cashValue)
        {
            if (crateCount <= 0 || cashValue <= 0)
            {
                return false;
            }

            bool changed = _state.IncrementMetric(
                ProgressMetricId.CourierTripsCompleted,
                1L);
            changed |= _state.IncrementMetric(
                ProgressMetricId.CratesDelivered,
                crateCount);
            changed |= _state.IncrementMetric(
                ProgressMetricId.CratesTransported,
                crateCount);
            changed |= _state.IncrementMetric(
                ProgressMetricId.TotalCashEarned,
                cashValue);
            return FinishAuthoritativeMutation(changed);
        }

        public bool RecordFlag(ProgressFlagId flag)
        {
            return FinishAuthoritativeMutation(_state.SetFlag(flag));
        }

        public bool RecordIndustrialTransport(ResourceType resourceType, int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            ProgressMetricId metric;
            switch (resourceType)
            {
                case ResourceType.Wood:
                    metric = ProgressMetricId.WoodTransported;
                    break;
                case ResourceType.Plank:
                    metric = ProgressMetricId.PlanksTransported;
                    break;
                case ResourceType.Crate:
                    metric = ProgressMetricId.CratesTransported;
                    break;
                case ResourceType.IronOre:
                    metric = ProgressMetricId.IronOreTransported;
                    break;
                case ResourceType.IronBar:
                    metric = ProgressMetricId.IronBarsTransported;
                    break;
                case ResourceType.Tool:
                    metric = ProgressMetricId.ToolsTransported;
                    break;
                default:
                    return false;
            }

            bool changed = _state.IncrementMetric(metric, amount);
            changed |= _state.SetMetricOnce(
                ProgressMetricId.IndustrialRouteOperational);
            return FinishAuthoritativeMutation(changed);
        }

        public bool RecordDailyGoalCompleted()
        {
            return RecordSingleMetric(ProgressMetricId.DailyGoalsCompleted, 1);
        }

        public bool RecordWarehouseUnlocked()
        {
            return FinishAuthoritativeMutation(
                _state.SetMetricOnce(ProgressMetricId.WarehouseUnlocked));
        }

        public bool TryClaimActiveContract()
        {
            if (_isEvaluating
                || !HasActiveContract
                || _state.activeContractState
                != ContractProgressState.CompletedUnclaimed)
            {
                return false;
            }

            LumberCampContractDefinition contract =
                LumberCampProgressionCatalog.GetContract(ActiveContractIndex);
            bool rewardGranted = false;
            _isEvaluating = true;
            try
            {
                rewardGranted = _tryGrantRewardCash != null
                                && _tryGrantRewardCash(contract.RewardCash);
                if (rewardGranted)
                {
                    if (contract.IsCrossIndustryLogistics)
                    {
                        _state.IncrementMetric(
                            ProgressMetricId
                                .CrossIndustryLogisticsContractsCompleted,
                            1L);
                    }

                    _state.claimedContracts[ActiveContractIndex] = true;
                    _state.activeContractIndex++;
                    if (_state.activeContractIndex
                        >= LumberCampProgressionCatalog.ContractCount)
                    {
                        _state.activeContractBaseline = 0L;
                        _state.activeContractState = ContractProgressState.Claimed;
                    }
                    else
                    {
                        LumberCampContractDefinition next =
                            LumberCampProgressionCatalog.GetContract(
                                _state.activeContractIndex);
                        _state.activeContractBaseline = _state.GetMetric(next.Metric);
                        _state.activeContractState =
                            M10ProgressionRules.IsContractEligible(_state, next)
                                ? ContractProgressState.Active
                                : ContractProgressState.Locked;
                    }
                }
            }
            finally
            {
                _isEvaluating = false;
            }

            // Wallet callbacks can synchronously finish a PurchasePad. Drain any
            // resulting flags/objectives/achievements before publishing claim state.
            // Claiming changes progression ownership directly (including M13's
            // cross-industry milestone), so always drain derived transitions.
            // Wallet callbacks may have queued additional mutations as well.
            bool transitionChanged = EvaluateTransitions();
            if (!rewardGranted)
            {
                if (transitionChanged)
                {
                    StateChanged?.Invoke();
                }

                return false;
            }

            StateChanged?.Invoke();
            return true;
        }

        private bool RecordSingleMetric(ProgressMetricId metric, int amount)
        {
            return amount > 0
                   && FinishAuthoritativeMutation(
                       _state.IncrementMetric(metric, amount));
        }

        private bool FinishAuthoritativeMutation(bool changed)
        {
            if (!changed)
            {
                return false;
            }

            if (_isEvaluating)
            {
                _reentrantMutationChanged = true;
                _evaluationPending = true;
                return true;
            }

            EvaluateTransitions();
            StateChanged?.Invoke();
            return true;
        }

        private bool EvaluateTransitions()
        {
            if (_isEvaluating)
            {
                _evaluationPending = true;
                return false;
            }

            _isEvaluating = true;
            bool changed = false;
            try
            {
                do
                {
                    _evaluationPending = false;
                    if (_reentrantMutationChanged)
                    {
                        changed = true;
                        _reentrantMutationChanged = false;
                    }

                    int resolvedObjective =
                        M10ProgressionRules.ResolveObjectiveIndex(_state);
                    if (_state.objectiveIndex != resolvedObjective)
                    {
                        _state.objectiveIndex = resolvedObjective;
                        changed = true;
                    }

                    PublishEmpireLevelAdvancements();

                    int empireLevel = M10ProgressionRules.ResolveEmpireLevel(_state);
                    if (empireLevel == 9
                        && _state.SetMetricOnce(
                            ProgressMetricId.IndustrialDistrictReady))
                    {
                        changed = true;
                        PublishEmpireLevelAdvancements();
                    }

                    if (HasContractCursor)
                    {
                        LumberCampContractDefinition contract =
                            LumberCampProgressionCatalog.GetContract(
                                ActiveContractIndex);
                        bool eligible = M10ProgressionRules.IsContractEligible(
                            _state,
                            contract);
                        if (eligible
                            && _state.activeContractState
                            == ContractProgressState.Locked)
                        {
                            _state.activeContractBaseline =
                                _state.GetMetric(contract.Metric);
                            _state.activeContractState = ContractProgressState.Active;
                            changed = true;
                        }

                        long progress = Math.Max(
                            0L,
                            _state.GetMetric(contract.Metric)
                            - _state.activeContractBaseline);
                        if (eligible
                            && progress >= contract.Target
                            && _state.activeContractState
                            == ContractProgressState.Active)
                        {
                            _state.activeContractState =
                                ContractProgressState.CompletedUnclaimed;
                            changed = true;
                        }
                    }

                    for (int i = 0;
                         i < LumberCampProgressionCatalog.AchievementCount;
                         i++)
                    {
                        M10AchievementSaveRecord record =
                            _state.FindAchievementRecord(i);
                        if (record == null)
                        {
                            continue;
                        }

                        bool newlyUnlocked = false;
                        if (!record.unlocked
                            && M10ProgressionRules.IsAchievementSatisfied(_state, i))
                        {
                            record.unlocked = true;
                            newlyUnlocked = true;
                            changed = true;
                        }

                        if (record.unlocked
                            && !record.rewarded
                            && _tryGrantRewardCash != null
                            && _tryGrantRewardCash(
                                LumberCampProgressionCatalog
                                    .GetAchievement(i)
                                    .RewardCash))
                        {
                            record.rewarded = true;
                            changed = true;
                        }

                        if (newlyUnlocked)
                        {
                            AchievementUnlocked?.Invoke(i);
                        }
                    }
                }
                while (_evaluationPending || _reentrantMutationChanged);
            }
            finally
            {
                _isEvaluating = false;
                _evaluationPending = false;
                _reentrantMutationChanged = false;
            }

            return changed;
        }

        private void PublishEmpireLevelAdvancements()
        {
            int resolvedLevel = M10ProgressionRules.ResolveEmpireLevel(_state);
            if (resolvedLevel <= _lastNotifiedEmpireLevel)
            {
                return;
            }

            int firstAdvancedLevel = _lastNotifiedEmpireLevel + 1;
            // Advance the guard before invoking callbacks so synchronous re-entry
            // cannot publish the same level twice.
            _lastNotifiedEmpireLevel = resolvedLevel;
            for (int level = firstAdvancedLevel; level <= resolvedLevel; level++)
            {
                EmpireLevelAdvanced?.Invoke(level);
            }
        }
    }
}
