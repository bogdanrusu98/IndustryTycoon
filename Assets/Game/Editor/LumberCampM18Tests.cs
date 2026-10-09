using System;
using System.Collections.Generic;
using IndustryTycoon.Persistence;
using IndustryTycoon.Progression;
using NUnit.Framework;

namespace IndustryTycoon.EditorTests
{
    public sealed class LumberCampM18Tests
    {
        private const long Now = 1800000000L;

        [Test] public void FreshSequence_IsShortOrderedAndActionable() => TestFreshSequence();
        [Test] public void SaveLoad_ResumesAtMultipleStepsOnSchema8() => TestSaveLoad();
        [Test] public void CompletedSteps_CannotReplayOrSkipGates() => TestNoReplayOrSkipping();
        [Test] public void Explanations_AppearOnlyAfterLegitimateUnlock() => TestIntroductions();
        [Test] public void AnalyticsHooks_AreInternalAndCanonical() => TestAnalyticsHooks();
        [Test] public void ExistingSchema8Save_DefaultsSafely() => TestExistingV8Compatibility();

        public static void RunAllForValidator()
        {
            TestFreshSequence();
            TestSaveLoad();
            TestNoReplayOrSkipping();
            TestIntroductions();
            TestAnalyticsHooks();
            TestExistingV8Compatibility();
        }

        private static void TestFreshSequence()
        {
            M18OnboardingSaveData state = M18OnboardingSaveData.CreateFresh();
            Require(state.currentStep == 0 && state.completedStepsMask == 0
                    && !state.tutorialStarted && !state.tutorialSkipped,
                "Fresh onboarding state is not clean.");
            Require(M18OnboardingRules.StepCount == 12,
                "First-session onboarding must remain a compact 12-step flow.");
            for (int i = 0; i < M18OnboardingRules.StepCount; i++)
            {
                string instruction = M18OnboardingRules.GetInstruction((FirstSessionStep)i);
                Require(!string.IsNullOrWhiteSpace(instruction)
                        && !instruction.Contains("\n"),
                    $"Step {i} is missing its short HUD instruction.");
            }
        }

        private static void TestSaveLoad()
        {
            foreach (int completed in new[] { 0, 1, 3, 6, 9, 12 })
            {
                M9SaveData save = M9SaveData.CreateFresh(Now);
                save.onboarding.tutorialStarted = completed > 0;
                save.onboarding.completedStepsMask = completed == 0
                    ? 0
                    : (1 << completed) - 1;
                save.onboarding.currentStep = completed;
                Require(M9SaveCodec.TryEncode(
                        save,
                        M9SaveValidationSettings.CreateDefault(),
                        Now,
                        out string json,
                        out _,
                        out string failure),
                    $"Onboarding checkpoint {completed} did not encode: {failure}");
                M9SaveDecodeResult decoded = M9SaveCodec.Decode(
                    json,
                    M9SaveValidationSettings.CreateDefault(),
                    Now);
                Require(decoded.IsSuccess
                        && decoded.Data.version == M9SaveSchema.CurrentVersion
                        && decoded.Data.onboarding.currentStep == completed,
                    $"Onboarding checkpoint {completed} did not resume exactly.");
            }
        }

        private static void TestNoReplayOrSkipping()
        {
            var malformed = new M18OnboardingSaveData
            {
                tutorialStarted = true,
                currentStep = 11,
                completedStepsMask = (1 << 0) | (1 << 2) | (1 << 10)
            };
            M18OnboardingSaveData normalized = M18OnboardingRules.Normalize(malformed);
            Require(normalized.currentStep == 1
                    && normalized.completedStepsMask == 1,
                "A hole in completed steps bypassed a progression gate.");

            normalized = M18OnboardingRules.Normalize(normalized);
            Require(normalized.currentStep == 1
                    && normalized.completedStepsMask == 1,
                "Repeated restore changed a completed step or replayed state.");

            malformed.tutorialSkipped = true;
            normalized = M18OnboardingRules.Normalize(malformed);
            Require(normalized.currentStep == M18OnboardingRules.StepCount
                    && normalized.completedStepsMask == M18OnboardingRules.AllStepsMask,
                "Full tutorial skip did not normalize atomically.");
        }

        private static void TestIntroductions()
        {
            M10ProgressionSaveData progression = M10ProgressionSaveData.CreateFresh();
            for (int i = 0; i < M18OnboardingRules.ExplanationCount; i++)
            {
                Require(!M18OnboardingRules.IsExplanationUnlocked(
                        (FirstTimeExplanation)i,
                        progression,
                        1),
                    $"Explanation {(FirstTimeExplanation)i} appeared on a fresh save.");
            }

            progression.flags[(int)ProgressFlagId.LumberCampCompleted].value = true;
            Require(M18OnboardingRules.IsExplanationUnlocked(
                    FirstTimeExplanation.Mining,
                    progression,
                    2)
                    && !M18OnboardingRules.IsExplanationUnlocked(
                        FirstTimeExplanation.Smelter,
                        progression,
                        2),
                "Mining/Smelter introductions ignored their real unlock gates.");

            progression.metrics[(int)ProgressMetricId.WarehouseUnlocked].value = 1;
            Require(M18OnboardingRules.IsExplanationUnlocked(
                        FirstTimeExplanation.Warehouse, progression, 6)
                    && M18OnboardingRules.IsExplanationUnlocked(
                        FirstTimeExplanation.DailyWeeklyGoals, progression, 6)
                    && !M18OnboardingRules.IsExplanationUnlocked(
                        FirstTimeExplanation.LogisticsRoutes, progression, 6),
                "Warehouse systems were introduced before legitimate availability.");

            progression.metrics[(int)ProgressMetricId.MarketUnlocked].value = 1;
            Require(M18OnboardingRules.IsExplanationUnlocked(
                        FirstTimeExplanation.MarketDemand, progression, 21)
                    && M18OnboardingRules.IsExplanationUnlocked(
                        FirstTimeExplanation.ProductionOrders, progression, 21),
                "Market explanations did not unlock with the Market.");
        }

        private static void TestAnalyticsHooks()
        {
            string[] names =
            {
                InternalAnalytics.TutorialStarted,
                InternalAnalytics.TutorialStepCompleted,
                InternalAnalytics.TutorialCompleted,
                InternalAnalytics.FirstWorker,
                InternalAnalytics.FirstProcessor,
                InternalAnalytics.FirstAutomation,
                InternalAnalytics.FirstCrate,
                InternalAnalytics.FirstCourier,
                InternalAnalytics.FirstSessionProgression
            };
            var unique = new HashSet<string>(names, StringComparer.Ordinal);
            Require(unique.Count == names.Length,
                "Analytics hook names are duplicated.");
            foreach (string name in names)
                Require(!string.IsNullOrWhiteSpace(name) && name == name.ToLowerInvariant(),
                    "Analytics hook names must remain stable lowercase IDs.");
        }

        private static void TestExistingV8Compatibility()
        {
            M9SaveData save = M9SaveData.CreateFresh(Now);
            save.onboarding = null;
            Require(M9SaveCodec.TryEncode(save, M9SaveValidationSettings.CreateDefault(), Now,
                    out string json, out M9SaveData normalized, out string failure),
                "Existing v8 save without M18 state failed: " + failure);
            Require(normalized.onboarding != null
                    && normalized.onboarding.currentStep == 0
                    && json.Contains("\"version\":" + M9SaveSchema.CurrentVersion),
                "Current save did not receive a safe fresh onboarding state.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
