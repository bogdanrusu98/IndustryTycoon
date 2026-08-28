using System;

namespace IndustryTycoon.Persistence
{
    public sealed class OfflineProgressionRules
    {
        public const long FourHoursInSeconds = 4L * 60L * 60L;

        public long MaximumCreditedAwaySeconds = FourHoursInSeconds;
        public double OfflineEfficiency = 0.60d;
        public long ReturnScreenThresholdSeconds = 5L * 60L;

        // M8 scene timing: upgraded spawner 1.25s / 2x, conservative far-route worker,
        // feeder cadence, machine recipes, and conservative two-Crate courier round trip.
        public double WoodProductionSecondsPerWood = 0.625d;
        public double WorkerCollectionSecondsPerWood = 6.50d;
        public double FeederTransferSecondsPerWood = 0.75d;
        public double ProcessorSecondsPerRecipe = 1.10d;
        public double PackingSecondsPerRecipe = 1.50d;
        public double CourierSecondsPerTrip = 11.40d;

        public int ProcessorWoodPerRecipe = 2;
        public int ProcessorPlanksPerRecipe = 1;
        public int PackingPlanksPerRecipe = 2;
        public int PackingCratesPerRecipe = 1;
        public int CourierCratesPerTrip = 2;
        public int CashPerDeliveredCrate = 40;

        public static OfflineProgressionRules CreateDefault()
        {
            return new OfflineProgressionRules();
        }
    }

    public sealed class OfflineProgressionInput
    {
        public long LastEvaluationUtcUnixSeconds;
        public long NowUtcUnixSeconds;

        public bool WorkerUnlocked;
        public bool ProcessorUnlocked;
        public bool AutoFeederUnlocked;
        public bool PackingUnlocked;
        public bool CourierUnlocked;

        public int StockpileWood;
        public int StockpileCapacity = 30;
        public int ProcessorInputWood;
        public int ProcessorInputCapacity = 24;
        public int ProcessorOutputPlanks;
        public int ProcessorOutputCapacity = 12;
        public int PackingInputPlanks;
        public int PackingInputCapacity = 24;
        public int PackingOutputCrates;
        public int PackingOutputCapacity = 12;

        public int PendingOfflineCash;
        public long PendingOfflineAwaySeconds;
        public bool ReturnScreenPending;
    }

    public sealed class OfflineProgressionResult
    {
        internal OfflineProgressionResult()
        {
        }

        public bool SkippedBecauseReturnPending { get; internal set; }
        public bool HadValidTimestamps { get; internal set; }
        public long ObservedAwaySeconds { get; internal set; }
        public long CreditedAwaySeconds { get; internal set; }
        public double EffectiveAutomationSeconds { get; internal set; }
        public long NextEvaluationUtcUnixSeconds { get; internal set; }

        public int WorkerWoodCollected { get; internal set; }
        public int FeederWoodTransferred { get; internal set; }
        public int ProcessorRecipesCompleted { get; internal set; }
        public int ProcessorPlanksProduced { get; internal set; }
        public int PackingRecipesCompleted { get; internal set; }
        public int PackingCratesProduced { get; internal set; }
        public int CourierCratesDelivered { get; internal set; }
        public int OfflineCashEarned { get; internal set; }

        public int StockpileWood { get; internal set; }
        public int ProcessorInputWood { get; internal set; }
        public int ProcessorOutputPlanks { get; internal set; }
        public int PackingInputPlanks { get; internal set; }
        public int PackingOutputCrates { get; internal set; }
        public int PendingOfflineCash { get; internal set; }
        public long PendingOfflineAwaySeconds { get; internal set; }
        public bool ReturnScreenPending { get; internal set; }
        public bool ShouldShowReturnScreen => ReturnScreenPending;
    }

    public static class OfflineProgressionCalculator
    {
        public static OfflineProgressionResult Calculate(
            OfflineProgressionInput input,
            OfflineProgressionRules rules = null)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            rules = rules ?? OfflineProgressionRules.CreateDefault();
            var result = CreateSanitizedResult(input);
            EvaluateElapsed(
                input.LastEvaluationUtcUnixSeconds,
                input.NowUtcUnixSeconds,
                Math.Max(0L, rules.MaximumCreditedAwaySeconds),
                out bool validTimestamps,
                out long observedAwaySeconds,
                out long creditedAwaySeconds,
                out long nextEvaluationUtcUnixSeconds);

            result.HadValidTimestamps = validTimestamps;
            result.NextEvaluationUtcUnixSeconds = nextEvaluationUtcUnixSeconds;

            // A visible, uncollected return is an outstanding settlement. Preserve it
            // byte-for-byte and only advance the monotonic evaluation anchor.
            if (input.ReturnScreenPending)
            {
                result.SkippedBecauseReturnPending = true;
                result.ReturnScreenPending = true;
                return result;
            }

            result.ObservedAwaySeconds = observedAwaySeconds;
            result.CreditedAwaySeconds = creditedAwaySeconds;
            double efficiency = ClampFinite(rules.OfflineEfficiency, 0d, 1d, 0d);
            result.EffectiveAutomationSeconds = creditedAwaySeconds * efficiency;

            int stockpileCapacity = Math.Max(0, input.StockpileCapacity);
            int processorInputCapacity = Math.Max(0, input.ProcessorInputCapacity);
            int processorOutputCapacity = Math.Max(0, input.ProcessorOutputCapacity);
            int packingOutputCapacity = Math.Max(0, input.PackingOutputCapacity);

            if (input.WorkerUnlocked && result.EffectiveAutomationSeconds > 0d)
            {
                long productionBudget = CalculateOperationBudget(
                    result.EffectiveAutomationSeconds,
                    rules.WoodProductionSecondsPerWood);
                long workerBudget = CalculateOperationBudget(
                    result.EffectiveAutomationSeconds,
                    rules.WorkerCollectionSecondsPerWood);
                int availableStockpileCapacity = stockpileCapacity - result.StockpileWood;
                result.WorkerWoodCollected = MinToInt(
                    productionBudget,
                    workerBudget,
                    availableStockpileCapacity);
                result.StockpileWood += result.WorkerWoodCollected;
            }

            if (input.ProcessorUnlocked
                && input.AutoFeederUnlocked
                && result.EffectiveAutomationSeconds > 0d)
            {
                long feederBudget = CalculateOperationBudget(
                    result.EffectiveAutomationSeconds,
                    rules.FeederTransferSecondsPerWood);
                int processorInputSpace = processorInputCapacity - result.ProcessorInputWood;
                result.FeederWoodTransferred = MinToInt(
                    feederBudget,
                    result.StockpileWood,
                    processorInputSpace);
                result.StockpileWood -= result.FeederWoodTransferred;
                result.ProcessorInputWood += result.FeederWoodTransferred;

                int woodPerRecipe = Math.Max(1, rules.ProcessorWoodPerRecipe);
                int planksPerRecipe = Math.Max(1, rules.ProcessorPlanksPerRecipe);
                long timeRecipeBudget = CalculateOperationBudget(
                    result.EffectiveAutomationSeconds,
                    rules.ProcessorSecondsPerRecipe);
                long inputRecipeBudget = result.ProcessorInputWood / woodPerRecipe;
                long outputRecipeBudget =
                    (processorOutputCapacity - result.ProcessorOutputPlanks)
                    / planksPerRecipe;
                result.ProcessorRecipesCompleted = MinToInt(
                    timeRecipeBudget,
                    inputRecipeBudget,
                    outputRecipeBudget);
                result.ProcessorInputWood -=
                    result.ProcessorRecipesCompleted * woodPerRecipe;
                result.ProcessorPlanksProduced =
                    result.ProcessorRecipesCompleted * planksPerRecipe;
                result.ProcessorOutputPlanks += result.ProcessorPlanksProduced;
            }

            int courierCrateBudget = 0;
            int cashPerCrate = Math.Max(1, rules.CashPerDeliveredCrate);
            if (input.PackingUnlocked
                && input.CourierUnlocked
                && result.EffectiveAutomationSeconds > 0d)
            {
                long tripBudget = CalculateOperationBudget(
                    result.EffectiveAutomationSeconds,
                    rules.CourierSecondsPerTrip);
                long timeCrateBudget = SaturatingMultiply(
                    tripBudget,
                    Math.Max(1, rules.CourierCratesPerTrip));
                long cashCapacityBudget =
                    ((long)int.MaxValue - result.PendingOfflineCash) / cashPerCrate;
                courierCrateBudget = MinToInt(timeCrateBudget, cashCapacityBudget);

                // First batch: legitimate pre-existing output can leave, freeing Packing
                // output capacity while the station processes its pre-loaded input.
                DeliverCrates(result, ref courierCrateBudget, cashPerCrate);
            }

            if (input.PackingUnlocked && result.EffectiveAutomationSeconds > 0d)
            {
                int planksPerRecipe = Math.Max(1, rules.PackingPlanksPerRecipe);
                int cratesPerRecipe = Math.Max(1, rules.PackingCratesPerRecipe);
                long timeRecipeBudget = CalculateOperationBudget(
                    result.EffectiveAutomationSeconds,
                    rules.PackingSecondsPerRecipe);
                long inputRecipeBudget = result.PackingInputPlanks / planksPerRecipe;
                long outputRecipeBudget =
                    (packingOutputCapacity - result.PackingOutputCrates)
                    / cratesPerRecipe;
                result.PackingRecipesCompleted = MinToInt(
                    timeRecipeBudget,
                    inputRecipeBudget,
                    outputRecipeBudget);
                result.PackingInputPlanks -=
                    result.PackingRecipesCompleted * planksPerRecipe;
                result.PackingCratesProduced =
                    result.PackingRecipesCompleted * cratesPerRecipe;
                result.PackingOutputCrates += result.PackingCratesProduced;
            }

            if (courierCrateBudget > 0)
            {
                // Second batch: the courier may also deliver Crates made exclusively
                // from Packing input that existed at the start of this settlement.
                DeliverCrates(result, ref courierCrateBudget, cashPerCrate);
            }

            result.PendingOfflineAwaySeconds = SaturatingAdd(
                result.PendingOfflineAwaySeconds,
                creditedAwaySeconds);
            long threshold = Math.Max(0L, rules.ReturnScreenThresholdSeconds);
            result.ReturnScreenPending = observedAwaySeconds >= threshold
                                         && observedAwaySeconds > 0L;
            return result;
        }

        public static long CalculateCreditedAwaySeconds(
            long lastEvaluationUtcUnixSeconds,
            long nowUtcUnixSeconds,
            long maximumCreditedAwaySeconds = OfflineProgressionRules.FourHoursInSeconds)
        {
            EvaluateElapsed(
                lastEvaluationUtcUnixSeconds,
                nowUtcUnixSeconds,
                Math.Max(0L, maximumCreditedAwaySeconds),
                out _,
                out _,
                out long creditedAwaySeconds,
                out _);
            return creditedAwaySeconds;
        }

        private static OfflineProgressionResult CreateSanitizedResult(
            OfflineProgressionInput input)
        {
            int stockpileCapacity = Math.Max(0, input.StockpileCapacity);
            int processorInputCapacity = Math.Max(0, input.ProcessorInputCapacity);
            int processorOutputCapacity = Math.Max(0, input.ProcessorOutputCapacity);
            int packingInputCapacity = Math.Max(0, input.PackingInputCapacity);
            int packingOutputCapacity = Math.Max(0, input.PackingOutputCapacity);
            return new OfflineProgressionResult
            {
                StockpileWood = Clamp(input.StockpileWood, 0, stockpileCapacity),
                ProcessorInputWood = Clamp(
                    input.ProcessorInputWood,
                    0,
                    processorInputCapacity),
                ProcessorOutputPlanks = Clamp(
                    input.ProcessorOutputPlanks,
                    0,
                    processorOutputCapacity),
                PackingInputPlanks = Clamp(
                    input.PackingInputPlanks,
                    0,
                    packingInputCapacity),
                PackingOutputCrates = Clamp(
                    input.PackingOutputCrates,
                    0,
                    packingOutputCapacity),
                PendingOfflineCash = Math.Max(0, input.PendingOfflineCash),
                PendingOfflineAwaySeconds = Math.Max(0L, input.PendingOfflineAwaySeconds),
                ReturnScreenPending = input.ReturnScreenPending
            };
        }

        private static void EvaluateElapsed(
            long lastEvaluationUtcUnixSeconds,
            long nowUtcUnixSeconds,
            long maximumCreditedAwaySeconds,
            out bool validTimestamps,
            out long observedAwaySeconds,
            out long creditedAwaySeconds,
            out long nextEvaluationUtcUnixSeconds)
        {
            bool validLast = M9UnixTime.IsPlausible(lastEvaluationUtcUnixSeconds);
            bool validNow = M9UnixTime.IsPlausible(nowUtcUnixSeconds);
            validTimestamps = validLast && validNow;
            observedAwaySeconds = 0L;
            creditedAwaySeconds = 0L;

            if (validLast && validNow)
            {
                nextEvaluationUtcUnixSeconds = Math.Max(
                    lastEvaluationUtcUnixSeconds,
                    nowUtcUnixSeconds);
                if (nowUtcUnixSeconds > lastEvaluationUtcUnixSeconds)
                {
                    observedAwaySeconds =
                        nowUtcUnixSeconds - lastEvaluationUtcUnixSeconds;
                    creditedAwaySeconds = Math.Min(
                        observedAwaySeconds,
                        maximumCreditedAwaySeconds);
                }

                return;
            }

            // Invalid clocks award nothing. Keep a valid previous anchor, or establish
            // the valid current value, so a later correction cannot fabricate time.
            nextEvaluationUtcUnixSeconds = validLast
                ? lastEvaluationUtcUnixSeconds
                : validNow
                    ? nowUtcUnixSeconds
                    : 0L;
        }

        private static void DeliverCrates(
            OfflineProgressionResult result,
            ref int remainingCrateBudget,
            int cashPerCrate)
        {
            int delivered = Math.Min(result.PackingOutputCrates, remainingCrateBudget);
            if (delivered <= 0)
            {
                return;
            }

            result.PackingOutputCrates -= delivered;
            remainingCrateBudget -= delivered;
            result.CourierCratesDelivered += delivered;
            int earned = delivered * cashPerCrate;
            result.OfflineCashEarned += earned;
            result.PendingOfflineCash += earned;
        }

        private static long CalculateOperationBudget(
            double effectiveSeconds,
            double secondsPerOperation)
        {
            if (effectiveSeconds <= 0d
                || double.IsNaN(effectiveSeconds)
                || double.IsInfinity(effectiveSeconds)
                || secondsPerOperation <= 0d
                || double.IsNaN(secondsPerOperation)
                || double.IsInfinity(secondsPerOperation))
            {
                return 0L;
            }

            double budget = Math.Floor(effectiveSeconds / secondsPerOperation);
            return budget >= long.MaxValue ? long.MaxValue : Math.Max(0L, (long)budget);
        }

        private static long SaturatingMultiply(long left, int right)
        {
            if (left <= 0L || right <= 0)
            {
                return 0L;
            }

            return left > long.MaxValue / right ? long.MaxValue : left * right;
        }

        private static long SaturatingAdd(long left, long right)
        {
            left = Math.Max(0L, left);
            right = Math.Max(0L, right);
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private static int MinToInt(params long[] values)
        {
            long minimum = int.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                minimum = Math.Min(minimum, Math.Max(0L, values[i]));
            }

            return (int)minimum;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static double ClampFinite(
            double value,
            double minimum,
            double maximum,
            double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return fallback;
            }

            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }
}
