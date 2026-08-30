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

        public LumberCampProgressionModel(
            M10ProgressionSaveData state,
            Func<int, bool> tryGrantRewardCash)
        {
            _tryGrantRewardCash = tryGrantRewardCash;
            Restore(state ?? M10ProgressionSaveData.CreateFresh());
        }

        public event Action StateChanged;
        public event Action<int> AchievementUnlocked;

        public int ObjectiveIndex => _state.objectiveIndex;
        public bool AreAllObjectivesCompleted => ObjectiveIndex
                                                 >= LumberCampProgressionCatalog
                                                     .ObjectiveCount;
        public int ActiveContractIndex => _state.activeContractIndex;
        public bool HasActiveContract => ActiveContractIndex >= 0
                                         && ActiveContractIndex
                                         < LumberCampProgressionCatalog.ContractCount;
        public ContractProgressState ActiveContractState =>
            HasActiveContract
                ? _state.activeContractState
                : ContractProgressState.Claimed;

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
            current = objective.ConditionKind == ObjectiveConditionKind.Flag
                ? (_state.GetFlag(objective.Flag) ? 1L : 0L)
                : Math.Min(target, _state.GetMetric(objective.Metric));
        }

        public string BuildObjectiveDisplayText()
        {
            if (AreAllObjectivesCompleted)
            {
                return "OBJECTIVE: MINING COMPLETE";
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
                ProgressMetricId.TotalCashEarned,
                cashValue);
            return FinishAuthoritativeMutation(changed);
        }

        public bool RecordFlag(ProgressFlagId flag)
        {
            return FinishAuthoritativeMutation(_state.SetFlag(flag));
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
                        _state.activeContractState = ContractProgressState.Active;
                    }
                }
            }
            finally
            {
                _isEvaluating = false;
            }

            // Wallet callbacks can synchronously finish a PurchasePad. Drain any
            // resulting flags/objectives/achievements before publishing claim state.
            bool transitionChanged = _evaluationPending
                                     || _reentrantMutationChanged
                ? EvaluateTransitions()
                : false;
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

                    if (HasActiveContract)
                    {
                        LumberCampContractDefinition contract =
                            LumberCampProgressionCatalog.GetContract(
                                ActiveContractIndex);
                        long progress = Math.Max(
                            0L,
                            _state.GetMetric(contract.Metric)
                            - _state.activeContractBaseline);
                        if (progress >= contract.Target
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
    }
}
