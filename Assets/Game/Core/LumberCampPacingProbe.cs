using System;
using System.Text;
using IndustryTycoon.Core;
using IndustryTycoon.Interaction;
using IndustryTycoon.Logistics;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.ResourceSystem;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Progression
{
    public enum LumberCampPacingMilestone
    {
        SessionStart,
        FirstWoodPickup,
        FirstSale,
        ProductionUpgrade,
        Worker,
        Processor,
        AutoFeeder,
        PackingStation,
        Courier,
        FirstCourierDelivery,
        LumberCampCompletion
    }

    public sealed class LumberCampPacingProbe : MonoBehaviour
    {
        private const int MilestoneCount = 11;

        [Header("Authoritative Events")]
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private SalePoint salePoint;
        [SerializeField] private WoodProductionUpgrade productionUpgrade;
        [SerializeField] private FirstWorkerUnlock workerUnlock;
        [SerializeField] private FirstProcessorUnlock processorUnlock;
        [SerializeField] private FirstAutoFeederUnlock autoFeederUnlock;
        [SerializeField] private FirstPackingStationUnlock packingStationUnlock;
        [SerializeField] private FirstCourierUnlock courierUnlock;
        [SerializeField] private CrateCourier courier;
        [SerializeField] private LumberCampCompletion completion;

        private readonly double[] _elapsedSeconds = new double[MilestoneCount];
        private double _sessionStartedAt;
        private bool _automaticReportLogged;

        public CarryStack CarryStack => carryStack;
        public SalePoint SalePoint => salePoint;
        public WoodProductionUpgrade ProductionUpgrade => productionUpgrade;
        public FirstWorkerUnlock WorkerUnlock => workerUnlock;
        public FirstProcessorUnlock ProcessorUnlock => processorUnlock;
        public FirstAutoFeederUnlock AutoFeederUnlock => autoFeederUnlock;
        public FirstPackingStationUnlock PackingStationUnlock => packingStationUnlock;
        public FirstCourierUnlock CourierUnlock => courierUnlock;
        public CrateCourier Courier => courier;
        public LumberCampCompletion Completion => completion;
        public int RecordedMilestoneCount { get; private set; }
        public int AutomaticReportCount { get; private set; }

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ResetProbe();
#else
            enabled = false;
#endif
        }

        private void OnEnable()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SubscribeEvents();
            CaptureCurrentAuthoritativeState();
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnsubscribeEvents();
#endif
        }

        [ContextMenu("Reset Pacing Probe")]
        public void ResetProbe()
        {
            for (int i = 0; i < _elapsedSeconds.Length; i++)
            {
                _elapsedSeconds[i] = double.NaN;
            }

            _sessionStartedAt = Time.realtimeSinceStartupAsDouble;
            _elapsedSeconds[(int)LumberCampPacingMilestone.SessionStart] = 0d;
            RecordedMilestoneCount = 1;
            AutomaticReportCount = 0;
            _automaticReportLogged = false;
        }

        public bool HasTimestamp(LumberCampPacingMilestone milestone)
        {
            return !double.IsNaN(_elapsedSeconds[(int)milestone]);
        }

        public double GetElapsedSeconds(LumberCampPacingMilestone milestone)
        {
            return _elapsedSeconds[(int)milestone];
        }

        public bool AreRecordedTimestampsOrdered()
        {
            double previous = 0d;
            for (int i = 0; i < _elapsedSeconds.Length; i++)
            {
                double value = _elapsedSeconds[i];
                if (double.IsNaN(value))
                {
                    continue;
                }

                if (double.IsInfinity(value) || value < 0d || value < previous)
                {
                    return false;
                }

                previous = value;
            }

            return true;
        }

        public bool HasCompleteOrderedSequence()
        {
            if (RecordedMilestoneCount != MilestoneCount)
            {
                return false;
            }

            for (int i = 0; i < _elapsedSeconds.Length; i++)
            {
                if (double.IsNaN(_elapsedSeconds[i]))
                {
                    return false;
                }
            }

            return AreRecordedTimestampsOrdered();
        }

        public string BuildReport()
        {
            var report = new StringBuilder(320);
            report.Append("M8 PACING | Start ");
            AppendTime(report, LumberCampPacingMilestone.SessionStart);
            report.Append(" | Wood pickup ");
            AppendTime(report, LumberCampPacingMilestone.FirstWoodPickup);
            report.Append(" | Sale ");
            AppendTime(report, LumberCampPacingMilestone.FirstSale);
            report.Append(" (<01:00) | Production ");
            AppendTime(report, LumberCampPacingMilestone.ProductionUpgrade);
            report.Append(" (02:00-04:00) | Worker ");
            AppendTime(report, LumberCampPacingMilestone.Worker);
            report.Append(" (05:00-08:00) | Processor ");
            AppendTime(report, LumberCampPacingMilestone.Processor);
            report.Append(" (08:00-12:00) | Feeder ");
            AppendTime(report, LumberCampPacingMilestone.AutoFeeder);
            report.Append(" (12:00-17:00) | Packing ");
            AppendTime(report, LumberCampPacingMilestone.PackingStation);
            report.Append(" (17:00-23:00) | Courier ");
            AppendTime(report, LumberCampPacingMilestone.Courier);
            report.Append(" (23:00-30:00) | First delivery ");
            AppendTime(report, LumberCampPacingMilestone.FirstCourierDelivery);
            report.Append(" | Complete ");
            AppendTime(report, LumberCampPacingMilestone.LumberCampCompletion);
            return report.ToString();
        }

        [ContextMenu("Log Pacing Report")]
        public void LogReport()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(BuildReport(), this);
#endif
        }

        private void SubscribeEvents()
        {
            if (carryStack != null)
            {
                carryStack.ItemsAdded += HandleItemsAdded;
            }

            if (salePoint != null)
            {
                salePoint.UnitSold += HandleUnitSold;
            }

            if (productionUpgrade != null)
            {
                productionUpgrade.Applied += HandleProductionApplied;
            }

            if (workerUnlock != null)
            {
                workerUnlock.WorkerActivated += HandleWorkerActivated;
            }

            if (processorUnlock != null)
            {
                processorUnlock.ProcessorActivated += HandleProcessorActivated;
            }

            if (autoFeederUnlock != null)
            {
                autoFeederUnlock.AutoFeederActivated += HandleAutoFeederActivated;
            }

            if (packingStationUnlock != null)
            {
                packingStationUnlock.PackingStationActivated +=
                    HandlePackingStationActivated;
            }

            if (courierUnlock != null)
            {
                courierUnlock.CourierActivated += HandleCourierActivated;
            }

            if (courier != null)
            {
                courier.DeliveryCompleted += HandleCourierDeliveryCompleted;
            }

            if (completion != null)
            {
                completion.Completed += HandleLumberCampCompleted;
            }
        }

        private void UnsubscribeEvents()
        {
            if (carryStack != null)
            {
                carryStack.ItemsAdded -= HandleItemsAdded;
            }

            if (salePoint != null)
            {
                salePoint.UnitSold -= HandleUnitSold;
            }

            if (productionUpgrade != null)
            {
                productionUpgrade.Applied -= HandleProductionApplied;
            }

            if (workerUnlock != null)
            {
                workerUnlock.WorkerActivated -= HandleWorkerActivated;
            }

            if (processorUnlock != null)
            {
                processorUnlock.ProcessorActivated -= HandleProcessorActivated;
            }

            if (autoFeederUnlock != null)
            {
                autoFeederUnlock.AutoFeederActivated -= HandleAutoFeederActivated;
            }

            if (packingStationUnlock != null)
            {
                packingStationUnlock.PackingStationActivated -=
                    HandlePackingStationActivated;
            }

            if (courierUnlock != null)
            {
                courierUnlock.CourierActivated -= HandleCourierActivated;
            }

            if (courier != null)
            {
                courier.DeliveryCompleted -= HandleCourierDeliveryCompleted;
            }

            if (completion != null)
            {
                completion.Completed -= HandleLumberCampCompleted;
            }
        }

        private void CaptureCurrentAuthoritativeState()
        {
            if (carryStack != null && carryStack.GetAmount(ResourceType.Wood) > 0)
            {
                Record(LumberCampPacingMilestone.FirstWoodPickup);
            }

            if (productionUpgrade != null && productionUpgrade.IsApplied)
            {
                Record(LumberCampPacingMilestone.ProductionUpgrade);
            }

            if (workerUnlock != null && workerUnlock.IsWorkerActivated)
            {
                Record(LumberCampPacingMilestone.Worker);
            }

            if (processorUnlock != null && processorUnlock.IsProcessorActivated)
            {
                Record(LumberCampPacingMilestone.Processor);
            }

            if (autoFeederUnlock != null && autoFeederUnlock.IsAutoFeederActivated)
            {
                Record(LumberCampPacingMilestone.AutoFeeder);
            }

            if (packingStationUnlock != null
                && packingStationUnlock.IsPackingStationActivated)
            {
                Record(LumberCampPacingMilestone.PackingStation);
            }

            if (courierUnlock != null && courierUnlock.IsCourierActivated)
            {
                Record(LumberCampPacingMilestone.Courier);
            }

            if (courier != null && courier.CompletedTripCount > 0)
            {
                Record(LumberCampPacingMilestone.FirstCourierDelivery);
            }

            if (completion != null && completion.IsCompleted)
            {
                Record(LumberCampPacingMilestone.LumberCampCompletion);
                LogAutomaticReportOnce();
            }
        }

        private void HandleItemsAdded(ResourceType resourceType, int amount, int totalAmount)
        {
            if (resourceType == ResourceType.Wood && amount > 0)
            {
                Record(LumberCampPacingMilestone.FirstWoodPickup);
            }
        }

        private void HandleUnitSold(SaleFeedbackData feedback)
        {
            Record(LumberCampPacingMilestone.FirstSale);
        }

        private void HandleProductionApplied()
        {
            Record(LumberCampPacingMilestone.ProductionUpgrade);
        }

        private void HandleWorkerActivated()
        {
            Record(LumberCampPacingMilestone.Worker);
        }

        private void HandleProcessorActivated()
        {
            Record(LumberCampPacingMilestone.Processor);
        }

        private void HandleAutoFeederActivated()
        {
            Record(LumberCampPacingMilestone.AutoFeeder);
        }

        private void HandlePackingStationActivated()
        {
            Record(LumberCampPacingMilestone.PackingStation);
        }

        private void HandleCourierActivated()
        {
            Record(LumberCampPacingMilestone.Courier);
        }

        private void HandleCourierDeliveryCompleted(
            uint generation,
            int crateCount,
            int cashValue)
        {
            Record(LumberCampPacingMilestone.FirstCourierDelivery);
        }

        private void HandleLumberCampCompleted()
        {
            Record(LumberCampPacingMilestone.FirstCourierDelivery);
            Record(LumberCampPacingMilestone.LumberCampCompletion);
            LogAutomaticReportOnce();
        }

        private void Record(LumberCampPacingMilestone milestone)
        {
            int index = (int)milestone;
            if (!double.IsNaN(_elapsedSeconds[index]))
            {
                return;
            }

            _elapsedSeconds[index] = Math.Max(
                0d,
                Time.realtimeSinceStartupAsDouble - _sessionStartedAt);
            RecordedMilestoneCount++;
        }

        private void LogAutomaticReportOnce()
        {
            if (_automaticReportLogged)
            {
                return;
            }

            _automaticReportLogged = true;
            AutomaticReportCount++;
            Debug.Log(BuildReport(), this);
        }

        private void AppendTime(
            StringBuilder report,
            LumberCampPacingMilestone milestone)
        {
            double elapsed = GetElapsedSeconds(milestone);
            if (double.IsNaN(elapsed))
            {
                report.Append("--:--");
                return;
            }

            int totalSeconds = Math.Max(0, (int)Math.Round(elapsed));
            report.Append(totalSeconds / 60);
            report.Append(':');
            report.Append((totalSeconds % 60).ToString("00"));
        }
    }
}
