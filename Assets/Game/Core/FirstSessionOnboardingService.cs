using System;
using System.Collections;
using IndustryTycoon.Player;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    [DefaultExecutionOrder(-850)]
    public sealed class FirstSessionOnboardingService : MonoBehaviour
    {
        [Header("Authoritative State")]
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private PlayerDragInput dragInput;
        [SerializeField] private WoodStockpile stockpile;

        [Header("Presentation Targets")]
        [SerializeField] private GameObject targetMarker;
        [SerializeField] private Transform[] stepTargets;
        [SerializeField] private Transform packingStationTarget;
        [SerializeField] private Vector3 markerOffset = new Vector3(0f, 0.18f, 0f);

        private M18OnboardingSaveData _state = M18OnboardingSaveData.CreateFresh();
        private Coroutine _hintRoutine;
        private string _temporaryHint = string.Empty;
        private bool _runtimeReady;
        private bool _restoring;

        public event Action StateChanged;
        public event Action<FirstSessionStep, string> StepCompleted;
        public event Action<FirstTimeExplanation, string> ExplanationPresented;
        public event Action GuidanceChanged;

        public FirstSessionStep CurrentStep => (FirstSessionStep)_state.currentStep;
        public bool IsTutorialActive => !_state.tutorialSkipped
                                        && _state.currentStep < M18OnboardingRules.StepCount;
        public bool IsTutorialComplete => _state.currentStep >= M18OnboardingRules.StepCount;
        public string GuidanceText => !string.IsNullOrEmpty(_temporaryHint)
            ? _temporaryHint
            : IsTutorialActive
                ? M18OnboardingRules.GetInstruction(CurrentStep)
                : string.Empty;
        public GameObject TargetMarker => targetMarker;
        public int CompletionEventCount { get; private set; }
        public int ExplanationEventCount { get; private set; }

        private void Start()
        {
            _runtimeReady = true;
            Subscribe(true);
            if (!_state.tutorialStarted && !IsTutorialComplete)
            {
                _state.tutorialStarted = true;
                InternalAnalytics.Publish(InternalAnalytics.TutorialStarted);
                StateChanged?.Invoke();
            }

            ReconcileFromAuthoritativeState(false);
            RefreshPresentation();
        }

        private void OnEnable()
        {
            if (_runtimeReady)
            {
                Subscribe(true);
                ReconcileFromAuthoritativeState(false);
                RefreshPresentation();
            }
        }

        private void OnDisable()
        {
            Subscribe(false);
            if (_hintRoutine != null)
            {
                StopCoroutine(_hintRoutine);
                _hintRoutine = null;
            }

            _temporaryHint = string.Empty;
            targetMarker?.SetActive(false);
        }

        public M18OnboardingSaveData CapturePersistentState()
        {
            return M18OnboardingRules.Normalize(_state);
        }

        public void RestorePersistentState(M18OnboardingSaveData state)
        {
            _restoring = true;
            try
            {
                _state = M18OnboardingRules.Normalize(state);
                ReconcileFromAuthoritativeState(true);
                SeedAlreadyUnlockedExplanations();
                RefreshPresentation();
            }
            finally
            {
                _restoring = false;
            }
        }

        public void ShowContextHint(string message, float seconds = 2.5f)
        {
            if (string.IsNullOrWhiteSpace(message) || !isActiveAndEnabled)
            {
                return;
            }

            if (_hintRoutine != null)
            {
                StopCoroutine(_hintRoutine);
            }

            _hintRoutine = StartCoroutine(ShowHintRoutine(message, seconds));
        }

        private IEnumerator ShowHintRoutine(string message, float seconds)
        {
            _temporaryHint = message;
            GuidanceChanged?.Invoke();
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, seconds));
            _temporaryHint = string.Empty;
            _hintRoutine = null;
            GuidanceChanged?.Invoke();
        }

        private void Subscribe(bool subscribe)
        {
            if (progression != null)
            {
                if (subscribe) progression.StateChanged += HandleAuthoritativeChanged;
                else progression.StateChanged -= HandleAuthoritativeChanged;
            }

            if (dragInput != null)
            {
                if (subscribe) dragInput.DragStarted += HandleDragStarted;
                else dragInput.DragStarted -= HandleDragStarted;
            }

            if (stockpile != null)
            {
                if (subscribe) stockpile.WoodDeposited += HandleStockpileDeposit;
                else stockpile.WoodDeposited -= HandleStockpileDeposit;
            }
        }

        private void HandleDragStarted()
        {
            if (CurrentStep == FirstSessionStep.MoveToWood)
            {
                CompleteCurrentStep();
            }
        }

        private void HandleStockpileDeposit(int storedWood)
        {
            if (storedWood > 0 && CurrentStep == FirstSessionStep.UseStockpile)
            {
                CompleteCurrentStep();
            }
        }

        private void HandleAuthoritativeChanged()
        {
            ReconcileFromAuthoritativeState(false);
            EvaluateIntroductions();
            RefreshPresentation();
        }

        private void ReconcileFromAuthoritativeState(bool silent)
        {
            if (progression == null)
            {
                return;
            }

            while (IsTutorialActive && IsCurrentStepSatisfied(silent))
            {
                CompleteCurrentStep(silent);
            }
        }

        private bool IsCurrentStepSatisfied(bool allowRestoreCatchUp)
        {
            switch (CurrentStep)
            {
                case FirstSessionStep.MoveToWood:
                    return progression.GetMetric(ProgressMetricId.WoodCollected) > 0L
                           || (allowRestoreCatchUp
                               && (progression.GetMetric(ProgressMetricId.WoodSold) > 0L
                                   || progression.GetFlag(ProgressFlagId.ProductionUpgradeUnlocked)));
                case FirstSessionStep.CollectWood:
                    return progression.GetMetric(ProgressMetricId.WoodCollected) > 0L
                           || (allowRestoreCatchUp
                               && (progression.GetMetric(ProgressMetricId.WoodSold) > 0L
                                   || progression.GetFlag(ProgressFlagId.ProductionUpgradeUnlocked)));
                case FirstSessionStep.SellWood:
                    return progression.GetMetric(ProgressMetricId.WoodSold) > 0L
                           || (allowRestoreCatchUp
                               && progression.GetFlag(ProgressFlagId.ProductionUpgradeUnlocked));
                case FirstSessionStep.UnlockProduction:
                    return progression.GetFlag(ProgressFlagId.ProductionUpgradeUnlocked);
                case FirstSessionStep.UnlockWorker:
                    return progression.GetFlag(ProgressFlagId.WorkerUnlocked);
                case FirstSessionStep.UseStockpile:
                    return (stockpile != null && stockpile.StoredWood > 0)
                           || (allowRestoreCatchUp
                               && progression.GetFlag(ProgressFlagId.ProcessorUnlocked));
                case FirstSessionStep.UnlockProcessor:
                    return progression.GetFlag(ProgressFlagId.ProcessorUnlocked);
                case FirstSessionStep.ProducePlank:
                    return progression.GetMetric(ProgressMetricId.PlanksProduced) > 0L
                           || (allowRestoreCatchUp
                               && progression.GetFlag(ProgressFlagId.AutoFeederUnlocked));
                case FirstSessionStep.UnlockAutomation:
                    return progression.GetFlag(ProgressFlagId.AutoFeederUnlocked);
                case FirstSessionStep.ProduceCrate:
                    return progression.GetMetric(ProgressMetricId.CratesProduced) > 0L
                           || (allowRestoreCatchUp
                               && progression.GetFlag(ProgressFlagId.CourierUnlocked));
                case FirstSessionStep.UnlockCourier:
                    return progression.GetFlag(ProgressFlagId.CourierUnlocked);
                case FirstSessionStep.CompleteLumberMilestone:
                    return progression.GetFlag(ProgressFlagId.LumberCampCompleted);
                default:
                    return false;
            }
        }

        private void CompleteCurrentStep(bool silent = false)
        {
            if (!IsTutorialActive)
            {
                return;
            }

            FirstSessionStep completed = CurrentStep;
            int bit = 1 << (int)completed;
            if ((_state.completedStepsMask & bit) != 0)
            {
                return;
            }

            _state.completedStepsMask |= bit;
            _state.currentStep++;
            if (!silent && !_restoring)
            {
                string parameter = completed.ToString();
                CompletionEventCount++;
                InternalAnalytics.Publish(InternalAnalytics.TutorialStepCompleted, parameter);
                InternalAnalytics.Publish(InternalAnalytics.FirstSessionProgression, parameter);
                PublishMilestoneAnalytics(completed);
                StepCompleted?.Invoke(completed, M18OnboardingRules.GetCompletionMessage(completed));
            }

            if (_state.currentStep >= M18OnboardingRules.StepCount)
            {
                _state.currentStep = M18OnboardingRules.StepCount;
                _state.completedStepsMask = M18OnboardingRules.AllStepsMask;
                if (!silent && !_restoring)
                {
                    InternalAnalytics.Publish(InternalAnalytics.TutorialCompleted);
                }
            }

            if (!_restoring)
            {
                StateChanged?.Invoke();
            }

            RefreshPresentation();
        }

        private static void PublishMilestoneAnalytics(FirstSessionStep step)
        {
            switch (step)
            {
                case FirstSessionStep.UnlockWorker:
                    InternalAnalytics.Publish(InternalAnalytics.FirstWorker);
                    break;
                case FirstSessionStep.UnlockProcessor:
                    InternalAnalytics.Publish(InternalAnalytics.FirstProcessor);
                    break;
                case FirstSessionStep.UnlockAutomation:
                    InternalAnalytics.Publish(InternalAnalytics.FirstAutomation);
                    break;
                case FirstSessionStep.ProduceCrate:
                    InternalAnalytics.Publish(InternalAnalytics.FirstCrate);
                    break;
                case FirstSessionStep.UnlockCourier:
                    InternalAnalytics.Publish(InternalAnalytics.FirstCourier);
                    break;
            }
        }

        private void EvaluateIntroductions()
        {
            if (_restoring || progression == null)
            {
                return;
            }

            M10ProgressionSaveData snapshot = progression.CapturePersistentState();
            int empireLevel = progression.EmpireLevel;
            bool changed = false;
            for (int i = 0; i < M18OnboardingRules.ExplanationCount; i++)
            {
                FirstTimeExplanation explanation = (FirstTimeExplanation)i;
                int bit = 1 << i;
                if ((_state.explanationsSeenMask & bit) != 0
                    || !M18OnboardingRules.IsExplanationUnlocked(
                        explanation, snapshot, empireLevel))
                {
                    continue;
                }

                _state.explanationsSeenMask |= bit;
                changed = true;
                ExplanationEventCount++;
                string message = M18OnboardingRules.GetExplanation(explanation);
                ExplanationPresented?.Invoke(explanation, message);
            }

            if (changed) StateChanged?.Invoke();
        }

        private void SeedAlreadyUnlockedExplanations()
        {
            if (progression == null)
            {
                return;
            }

            M10ProgressionSaveData snapshot = progression.CapturePersistentState();
            int empireLevel = progression.EmpireLevel;
            for (int i = 0; i < M18OnboardingRules.ExplanationCount; i++)
            {
                if (M18OnboardingRules.IsExplanationUnlocked(
                        (FirstTimeExplanation)i, snapshot, empireLevel))
                {
                    _state.explanationsSeenMask |= 1 << i;
                }
            }
        }

        private void RefreshPresentation()
        {
            if (targetMarker != null)
            {
                Transform target = ResolveCurrentTarget();
                bool show = IsTutorialActive
                            && target != null;
                targetMarker.SetActive(show);
                if (show)
                {
                    targetMarker.transform.position =
                        target.position + markerOffset;
                }
            }

            GuidanceChanged?.Invoke();
        }

        private Transform ResolveCurrentTarget()
        {
            if (!IsTutorialActive
                || stepTargets == null
                || _state.currentStep < 0
                || _state.currentStep >= stepTargets.Length)
            {
                return null;
            }

            if (CurrentStep == FirstSessionStep.ProduceCrate
                && progression != null
                && progression.GetFlag(ProgressFlagId.PackingStationUnlocked)
                && packingStationTarget != null)
            {
                return packingStationTarget;
            }

            return stepTargets[_state.currentStep];
        }
    }
}
