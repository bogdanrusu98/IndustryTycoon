using System;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.Workers;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public enum LumberCampProgressStage
    {
        ProductionUpgrade,
        Worker,
        Processor,
        AutoFeeder,
        PackingStation,
        Courier,
        FirstCourierDelivery,
        Complete
    }

    public sealed class NextUnlockGuidance : MonoBehaviour
    {
        [Header("Authoritative Progression")]
        [SerializeField] private WoodProductionUpgrade productionUpgrade;
        [SerializeField] private FirstWorkerUnlock workerUnlock;
        [SerializeField] private FirstProcessorUnlock processorUnlock;
        [SerializeField] private FirstAutoFeederUnlock autoFeederUnlock;
        [SerializeField] private FirstPackingStationUnlock packingStationUnlock;
        [SerializeField] private FirstCourierUnlock courierUnlock;
        [SerializeField] private LumberCampCompletion completion;
        [SerializeField] private LumberCampProgressionService progressionService;

        [Header("Presentation")]
        [SerializeField] private Text guidanceText;

        private string _displayText = string.Empty;

        public event Action<LumberCampProgressStage, int, int> GuidanceChanged;

        public WoodProductionUpgrade ProductionUpgrade => productionUpgrade;
        public FirstWorkerUnlock WorkerUnlock => workerUnlock;
        public FirstProcessorUnlock ProcessorUnlock => processorUnlock;
        public FirstAutoFeederUnlock AutoFeederUnlock => autoFeederUnlock;
        public FirstPackingStationUnlock PackingStationUnlock => packingStationUnlock;
        public FirstCourierUnlock CourierUnlock => courierUnlock;
        public LumberCampCompletion Completion => completion;
        public LumberCampProgressionService ProgressionService => progressionService;
        public Text GuidanceText => guidanceText;
        public LumberCampProgressStage CurrentStage { get; private set; }
        public int PaidAmount { get; private set; }
        public int TotalCost { get; private set; }
        public string DisplayText => _displayText;

        private void OnEnable()
        {
            SubscribePurchaseProgress();

            if (productionUpgrade != null)
            {
                productionUpgrade.Applied += HandleProgressionChanged;
            }

            if (workerUnlock != null)
            {
                workerUnlock.WorkerActivated += HandleProgressionChanged;
            }

            if (processorUnlock != null)
            {
                processorUnlock.ProcessorActivated += HandleProgressionChanged;
            }

            if (autoFeederUnlock != null)
            {
                autoFeederUnlock.AutoFeederActivated += HandleProgressionChanged;
            }

            if (packingStationUnlock != null)
            {
                packingStationUnlock.PackingStationActivated += HandleProgressionChanged;
            }

            if (courierUnlock != null)
            {
                courierUnlock.CourierActivated += HandleProgressionChanged;
            }

            if (completion != null)
            {
                completion.Completed += HandleProgressionChanged;
            }

            if (progressionService != null)
            {
                progressionService.StateChanged += HandleProgressionChanged;
            }

            Refresh();
        }

        private void OnDisable()
        {
            UnsubscribePurchaseProgress();

            if (productionUpgrade != null)
            {
                productionUpgrade.Applied -= HandleProgressionChanged;
            }

            if (workerUnlock != null)
            {
                workerUnlock.WorkerActivated -= HandleProgressionChanged;
            }

            if (processorUnlock != null)
            {
                processorUnlock.ProcessorActivated -= HandleProgressionChanged;
            }

            if (autoFeederUnlock != null)
            {
                autoFeederUnlock.AutoFeederActivated -= HandleProgressionChanged;
            }

            if (packingStationUnlock != null)
            {
                packingStationUnlock.PackingStationActivated -= HandleProgressionChanged;
            }

            if (courierUnlock != null)
            {
                courierUnlock.CourierActivated -= HandleProgressionChanged;
            }

            if (completion != null)
            {
                completion.Completed -= HandleProgressionChanged;
            }

            if (progressionService != null)
            {
                progressionService.StateChanged -= HandleProgressionChanged;
            }
        }

        public bool Refresh()
        {
            if (progressionService != null
                && progressionService.isActiveAndEnabled)
            {
                string objectiveText = progressionService.ObjectiveDisplayText;
                bool objectiveChanged = !string.Equals(
                    _displayText,
                    objectiveText,
                    StringComparison.Ordinal);
                PaidAmount = 0;
                TotalCost = 0;
                _displayText = objectiveText;
                if (guidanceText != null)
                {
                    guidanceText.text = _displayText;
                }

                if (objectiveChanged)
                {
                    GuidanceChanged?.Invoke(CurrentStage, 0, 0);
                }

                return objectiveChanged;
            }

            LumberCampProgressStage nextStage = ResolveCurrentStage();
            PurchasePad purchasePad = ResolveCurrentPurchasePad(nextStage);
            int nextTotalCost = purchasePad != null ? purchasePad.TotalCost : 0;
            int nextPaidAmount = purchasePad != null
                ? Mathf.Clamp(nextTotalCost - purchasePad.RemainingCost, 0, nextTotalCost)
                : 0;
            string nextDisplayText = BuildDisplayText(
                nextStage,
                nextPaidAmount,
                nextTotalCost);

            bool changed = CurrentStage != nextStage
                           || PaidAmount != nextPaidAmount
                           || TotalCost != nextTotalCost
                           || !string.Equals(
                               _displayText,
                               nextDisplayText,
                               StringComparison.Ordinal);

            CurrentStage = nextStage;
            PaidAmount = nextPaidAmount;
            TotalCost = nextTotalCost;
            _displayText = nextDisplayText;

            if (guidanceText != null)
            {
                guidanceText.text = _displayText;
            }

            if (changed)
            {
                GuidanceChanged?.Invoke(CurrentStage, PaidAmount, TotalCost);
            }

            return changed;
        }

        public LumberCampProgressStage ResolveCurrentStage()
        {
            if (completion != null && completion.IsCompleted)
            {
                return LumberCampProgressStage.Complete;
            }

            if (productionUpgrade == null || !productionUpgrade.IsApplied)
            {
                return LumberCampProgressStage.ProductionUpgrade;
            }

            if (workerUnlock == null || !workerUnlock.IsWorkerActivated)
            {
                return LumberCampProgressStage.Worker;
            }

            if (processorUnlock == null || !processorUnlock.IsProcessorActivated)
            {
                return LumberCampProgressStage.Processor;
            }

            if (autoFeederUnlock == null || !autoFeederUnlock.IsAutoFeederActivated)
            {
                return LumberCampProgressStage.AutoFeeder;
            }

            if (packingStationUnlock == null || !packingStationUnlock.IsPackingStationActivated)
            {
                return LumberCampProgressStage.PackingStation;
            }

            if (courierUnlock == null || !courierUnlock.IsCourierActivated)
            {
                return LumberCampProgressStage.Courier;
            }

            return LumberCampProgressStage.FirstCourierDelivery;
        }

        public static string BuildDisplayText(
            LumberCampProgressStage stage,
            int paidAmount,
            int totalCost)
        {
            switch (stage)
            {
                case LumberCampProgressStage.ProductionUpgrade:
                    return BuildPurchaseText("PRODUCTION UPGRADE", paidAmount, totalCost);
                case LumberCampProgressStage.Worker:
                    return BuildPurchaseText("WORKER", paidAmount, totalCost);
                case LumberCampProgressStage.Processor:
                    return BuildPurchaseText("PROCESSOR", paidAmount, totalCost);
                case LumberCampProgressStage.AutoFeeder:
                    return BuildPurchaseText("AUTO FEEDER", paidAmount, totalCost);
                case LumberCampProgressStage.PackingStation:
                    return BuildPurchaseText("PACKING STATION", paidAmount, totalCost);
                case LumberCampProgressStage.Courier:
                    return BuildPurchaseText("COURIER", paidAmount, totalCost);
                case LumberCampProgressStage.FirstCourierDelivery:
                    return "NEXT: FIRST COURIER DELIVERY";
                case LumberCampProgressStage.Complete:
                    return "LUMBER CAMP COMPLETE";
                default:
                    return string.Empty;
            }
        }

        private static string BuildPurchaseText(
            string label,
            int paidAmount,
            int totalCost)
        {
            return $"NEXT: {label}\n${paidAmount} / ${totalCost}";
        }

        private PurchasePad ResolveCurrentPurchasePad(LumberCampProgressStage stage)
        {
            switch (stage)
            {
                case LumberCampProgressStage.ProductionUpgrade:
                    return productionUpgrade != null ? productionUpgrade.PurchasePad : null;
                case LumberCampProgressStage.Worker:
                    return workerUnlock != null ? workerUnlock.WorkerPurchasePad : null;
                case LumberCampProgressStage.Processor:
                    return processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null;
                case LumberCampProgressStage.AutoFeeder:
                    return autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null;
                case LumberCampProgressStage.PackingStation:
                    return packingStationUnlock != null
                        ? packingStationUnlock.PackingStationPurchasePad
                        : null;
                case LumberCampProgressStage.Courier:
                    return courierUnlock != null ? courierUnlock.CourierPurchasePad : null;
                default:
                    return null;
            }
        }

        private void SubscribePurchaseProgress()
        {
            SetPurchaseProgressSubscription(
                productionUpgrade != null ? productionUpgrade.PurchasePad : null,
                true);
            SetPurchaseProgressSubscription(
                workerUnlock != null ? workerUnlock.WorkerPurchasePad : null,
                true);
            SetPurchaseProgressSubscription(
                processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null,
                true);
            SetPurchaseProgressSubscription(
                autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null,
                true);
            SetPurchaseProgressSubscription(
                packingStationUnlock != null
                    ? packingStationUnlock.PackingStationPurchasePad
                    : null,
                true);
            SetPurchaseProgressSubscription(
                courierUnlock != null ? courierUnlock.CourierPurchasePad : null,
                true);
        }

        private void UnsubscribePurchaseProgress()
        {
            SetPurchaseProgressSubscription(
                productionUpgrade != null ? productionUpgrade.PurchasePad : null,
                false);
            SetPurchaseProgressSubscription(
                workerUnlock != null ? workerUnlock.WorkerPurchasePad : null,
                false);
            SetPurchaseProgressSubscription(
                processorUnlock != null ? processorUnlock.ProcessorPurchasePad : null,
                false);
            SetPurchaseProgressSubscription(
                autoFeederUnlock != null ? autoFeederUnlock.AutoFeederPurchasePad : null,
                false);
            SetPurchaseProgressSubscription(
                packingStationUnlock != null
                    ? packingStationUnlock.PackingStationPurchasePad
                    : null,
                false);
            SetPurchaseProgressSubscription(
                courierUnlock != null ? courierUnlock.CourierPurchasePad : null,
                false);
        }

        private void SetPurchaseProgressSubscription(PurchasePad purchasePad, bool subscribe)
        {
            if (purchasePad == null)
            {
                return;
            }

            if (subscribe)
            {
                purchasePad.ProgressChanged += HandlePurchaseProgressChanged;
            }
            else
            {
                purchasePad.ProgressChanged -= HandlePurchaseProgressChanged;
            }
        }

        private void HandlePurchaseProgressChanged(int remainingCost)
        {
            Refresh();
        }

        private void HandleProgressionChanged()
        {
            Refresh();
        }
    }
}
