using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Persistence;
using IndustryTycoon.Progression;

namespace IndustryTycoon.Market
{
    public enum DemandTier { Normal = 0, High = 1, Boom = 2 }
    public enum ProductionOrderState { Available = 0, Active = 1, Completed = 2, Claimed = 3 }
    public enum IndustrialFlowStep { IronBar = 0, Tool = 1, MachinePart = 2 }

    [Serializable]
    public sealed class M16DemandSaveRecord
    {
        public ResourceType resourceType;
        public DemandTier tier;
    }

    [Serializable]
    public sealed class M16ProductionOrderSaveRecord
    {
        public long id;
        public ResourceType resourceType;
        public int quantity;
        public int rewardCash;
        public ProductionOrderState state;
    }

    [Serializable]
    public sealed class M16MarketSaveData
    {
        public const int ResourceCount = 7;
        public const int VisibleOrderCount = 3;

        public long seed;
        public long rotationIndex;
        public long nextRotationUtcUnixSeconds;
        public long orderSequence;
        public M16DemandSaveRecord[] demand;
        public M16ProductionOrderSaveRecord[] orders;
        public long lastIronBarProductionUtc;
        public long lastToolProductionUtc;
        public long flowBonusExpiresUtc;
        public long lastAcceptedUtcUnixSeconds;

        public static M16MarketSaveData CreateFresh(long utcNow)
        {
            long safeNow = M9UnixTime.IsPlausible(utcNow) ? utcNow : 0L;
            var data = new M16MarketSaveData
            {
                seed = Mix(safeNow ^ 0x4D31364D41524B45L),
                nextRotationUtcUnixSeconds = safeNow + M16MarketRules.DefaultRotationSeconds,
                lastAcceptedUtcUnixSeconds = safeNow,
                demand = new M16DemandSaveRecord[ResourceCount],
                orders = new M16ProductionOrderSaveRecord[VisibleOrderCount]
            };
            for (int i = 0; i < ResourceCount; i++)
                data.demand[i] = new M16DemandSaveRecord { resourceType = (ResourceType)i };
            for (int i = 0; i < VisibleOrderCount; i++)
                data.orders[i] = new M16ProductionOrderSaveRecord { state = ProductionOrderState.Claimed };
            return data;
        }

        internal static long Mix(long value)
        {
            unchecked
            {
                ulong x = (ulong)value;
                x ^= x >> 30; x *= 0xbf58476d1ce4e5b9UL;
                x ^= x >> 27; x *= 0x94d049bb133111ebUL;
                x ^= x >> 31;
                return (long)x;
            }
        }
    }

    public readonly struct MarketUnlockContext
    {
        private readonly int _mask;
        public MarketUnlockContext(int mask) => _mask = mask;
        public bool IsUnlocked(ResourceType type) => (_mask & (1 << (int)type)) != 0;
        public int Mask => _mask;
        public static MarketUnlockContext All => new MarketUnlockContext((1 << M16MarketSaveData.ResourceCount) - 1);
    }

    public static class M16MarketRules
    {
        public const int DefaultRotationSeconds = 300;
        public const int DefaultFlowWindowSeconds = 180;
        public const int DefaultFlowBonusSeconds = 180;
        public const int HighBasisPoints = 11500;
        public const int BoomBasisPoints = 13000;
        public const int FlowBasisPoints = 10500;

        private static readonly int[] BaseValues = { 5, 15, 40, 10, 30, 100, 250 };
        private static readonly int[] OrderQuantities = { 16, 10, 6, 12, 8, 5, 3 };

        public static int GetBaseValue(ResourceType type) => IsResource(type) ? BaseValues[(int)type] : 0;

        public static int GetEmpireLevel(M10ProgressionSaveData data)
        {
            if (data == null || data.GetMetric(ProgressMetricId.MarketUnlocked) < 1) return 20;
            if (data.GetMetric(ProgressMetricId.ProductionOrdersCompleted) < 1) return 21;
            if (data.GetMetric(ProgressMetricId.HighBoomSales) < 1) return 22;
            if (data.GetMetric(ProgressMetricId.FlowBonusesTriggered) < 1) return 23;
            if (data.GetMetric(ProgressMetricId.AdvancedMachinePartOrdersCompleted) < 1) return 24;
            return 25;
        }
        public static int GetDemandBasisPoints(DemandTier tier) => tier == DemandTier.Boom
            ? BoomBasisPoints : tier == DemandTier.High ? HighBasisPoints : 10000;

        public static int CalculateSaleValue(ResourceType type, DemandTier tier, bool flowBonus)
        {
            long value = GetBaseValue(type);
            value = (value * GetDemandBasisPoints(tier) + 5000L) / 10000L;
            if (flowBonus && type == ResourceType.MachinePart)
                value = (value * FlowBasisPoints + 5000L) / 10000L;
            return (int)Math.Min(int.MaxValue, Math.Max(0L, value));
        }

        public static int CalculateOrderReward(ResourceType type, int quantity, long seed)
        {
            int premiumPercent = 115 + PositiveModulo(M16MarketSaveData.Mix(seed), 16);
            long baseTotal = (long)GetBaseValue(type) * Math.Max(1, quantity);
            return (int)Math.Min(int.MaxValue, (baseTotal * premiumPercent + 50L) / 100L);
        }

        public static void RotateDemand(M16MarketSaveData data, MarketUnlockContext unlocks)
        {
            EnsureDemand(data);
            var unlocked = new List<ResourceType>();
            for (int i = 0; i < M16MarketSaveData.ResourceCount; i++)
            {
                data.demand[i].tier = DemandTier.Normal;
                if (unlocks.IsUnlocked((ResourceType)i)) unlocked.Add((ResourceType)i);
            }
            unlocked.Sort((a, b) => Score(data, a).CompareTo(Score(data, b)));
            if (unlocked.Count >= 3) data.demand[(int)unlocked[0]].tier = DemandTier.Boom;
            int highCount = Math.Min(2, Math.Max(0, unlocked.Count - (unlocked.Count >= 3 ? 1 : 0)));
            int start = unlocked.Count >= 3 ? 1 : 0;
            for (int i = 0; i < highCount; i++) data.demand[(int)unlocked[start + i]].tier = DemandTier.High;
        }

        public static bool Advance(M16MarketSaveData data, long utcNow, int rotationSeconds,
            MarketUnlockContext unlocks)
        {
            if (data == null || !M9UnixTime.IsPlausible(utcNow)) return false;
            rotationSeconds = Math.Max(30, rotationSeconds);
            if (data.nextRotationUtcUnixSeconds <= 0)
                data.nextRotationUtcUnixSeconds = utcNow + rotationSeconds;
            if (utcNow < data.nextRotationUtcUnixSeconds) return false;
            long elapsed = utcNow - data.nextRotationUtcUnixSeconds;
            long rotations = 1L + elapsed / rotationSeconds;
            data.rotationIndex = SaturatingAdd(data.rotationIndex, rotations);
            data.nextRotationUtcUnixSeconds = data.nextRotationUtcUnixSeconds > long.MaxValue - rotations * rotationSeconds
                ? long.MaxValue : data.nextRotationUtcUnixSeconds + rotations * rotationSeconds;
            RotateDemand(data, unlocks);
            RefillOrders(data, unlocks);
            return true;
        }

        public static void RefillOrders(M16MarketSaveData data, MarketUnlockContext unlocks)
        {
            if (data.orders == null || data.orders.Length != M16MarketSaveData.VisibleOrderCount)
                data.orders = new M16ProductionOrderSaveRecord[M16MarketSaveData.VisibleOrderCount];
            var unlocked = new List<ResourceType>();
            for (int i = 0; i < M16MarketSaveData.ResourceCount; i++)
                if (unlocks.IsUnlocked((ResourceType)i)) unlocked.Add((ResourceType)i);
            if (unlocked.Count == 0) return;
            for (int i = 0; i < data.orders.Length; i++)
            {
                if (data.orders[i] != null && data.orders[i].state != ProductionOrderState.Claimed) continue;
                long sequence = ++data.orderSequence;
                ResourceType type = unlocked[PositiveModulo(M16MarketSaveData.Mix(data.seed ^ sequence), unlocked.Count)];
                int quantity = OrderQuantities[(int)type];
                data.orders[i] = new M16ProductionOrderSaveRecord
                {
                    id = sequence,
                    resourceType = type,
                    quantity = quantity,
                    rewardCash = CalculateOrderReward(type, quantity, data.seed ^ sequence),
                    state = ProductionOrderState.Available
                };
            }
        }

        public static DemandTier GetDemand(M16MarketSaveData data, ResourceType type) =>
            data?.demand != null && IsResource(type) && data.demand.Length > (int)type && data.demand[(int)type] != null
                ? data.demand[(int)type].tier : DemandTier.Normal;

        public static bool TryActivateOrder(M16MarketSaveData data, int index)
        {
            if (data?.orders == null || index < 0 || index >= data.orders.Length
                || data.orders[index]?.state != ProductionOrderState.Available) return false;
            for (int i = 0; i < data.orders.Length; i++)
                if (data.orders[i]?.state == ProductionOrderState.Active) return false;
            data.orders[index].state = ProductionOrderState.Active;
            return true;
        }

        public static bool TryTransitionOrder(M16ProductionOrderSaveRecord order,
            ProductionOrderState from, ProductionOrderState to)
        {
            if (order == null || order.state != from) return false;
            order.state = to;
            return true;
        }

        public static bool RecordFlowProduction(M16MarketSaveData data, IndustrialFlowStep step,
            long utcNow, int rollingWindowSeconds, int bonusSeconds, out bool newlyTriggered)
        {
            newlyTriggered = false;
            if (data == null || !M9UnixTime.IsPlausible(utcNow)) return false;
            rollingWindowSeconds = Math.Max(10, rollingWindowSeconds);
            bonusSeconds = Math.Max(10, bonusSeconds);
            if (step == IndustrialFlowStep.IronBar)
            {
                data.lastIronBarProductionUtc = utcNow;
                return true;
            }
            if (step == IndustrialFlowStep.Tool)
            {
                if (!Within(utcNow, data.lastIronBarProductionUtc, rollingWindowSeconds)) return false;
                data.lastToolProductionUtc = utcNow;
                return true;
            }
            if (step != IndustrialFlowStep.MachinePart
                || !Within(utcNow, data.lastToolProductionUtc, rollingWindowSeconds)) return false;
            newlyTriggered = utcNow >= data.flowBonusExpiresUtc;
            data.flowBonusExpiresUtc = Math.Max(data.flowBonusExpiresUtc, utcNow + bonusSeconds);
            return true;
        }

        private static long Score(M16MarketSaveData data, ResourceType type) =>
            M16MarketSaveData.Mix(data.seed ^ (data.rotationIndex * 31L) ^ ((int)type * 0x9E3779B9L));

        private static void EnsureDemand(M16MarketSaveData data)
        {
            if (data.demand != null && data.demand.Length == M16MarketSaveData.ResourceCount) return;
            data.demand = new M16DemandSaveRecord[M16MarketSaveData.ResourceCount];
            for (int i = 0; i < data.demand.Length; i++)
                data.demand[i] = new M16DemandSaveRecord { resourceType = (ResourceType)i };
        }

        internal static bool IsResource(ResourceType type) => (int)type >= 0 && (int)type < M16MarketSaveData.ResourceCount;
        private static int PositiveModulo(long value, int modulus) => (int)((ulong)value % (uint)Math.Max(1, modulus));
        private static bool Within(long now, long then, int window) => then > 0 && now >= then && now - then <= window;
        private static long SaturatingAdd(long value, long amount) => value > long.MaxValue - amount ? long.MaxValue : value + amount;
    }

    public static class M16MarketSaveValidator
    {
        public static bool TryNormalize(M16MarketSaveData source, long fallbackUtc,
            out M16MarketSaveData normalized, out string failure)
        {
            normalized = null; failure = null;
            if (source?.demand == null || source.demand.Length != M16MarketSaveData.ResourceCount
                || source.orders == null || source.orders.Length != M16MarketSaveData.VisibleOrderCount)
            { failure = "M16 market state has an invalid canonical shape."; return false; }
            normalized = M16MarketSaveData.CreateFresh(fallbackUtc);
            normalized.seed = source.seed;
            normalized.rotationIndex = Math.Max(0L, source.rotationIndex);
            normalized.nextRotationUtcUnixSeconds = M9UnixTime.IsPlausible(source.nextRotationUtcUnixSeconds)
                ? source.nextRotationUtcUnixSeconds : normalized.nextRotationUtcUnixSeconds;
            normalized.orderSequence = Math.Max(0L, source.orderSequence);
            var seen = new bool[M16MarketSaveData.ResourceCount];
            int high = 0, boom = 0, active = 0;
            for (int i = 0; i < source.demand.Length; i++)
            {
                M16DemandSaveRecord record = source.demand[i];
                if (record == null || !M16MarketRules.IsResource(record.resourceType) || seen[(int)record.resourceType]
                    || !Enum.IsDefined(typeof(DemandTier), record.tier))
                { failure = "M16 demand records are invalid."; normalized = null; return false; }
                seen[(int)record.resourceType] = true;
                normalized.demand[(int)record.resourceType].tier = record.tier;
                if (record.tier == DemandTier.High) high++;
                if (record.tier == DemandTier.Boom) boom++;
            }
            if (high > 2 || boom > 1) { failure = "M16 demand tier caps are invalid."; normalized = null; return false; }
            for (int i = 0; i < source.orders.Length; i++)
            {
                var order = source.orders[i];
                if (order == null || (order.id == 0 && order.quantity == 0 && order.rewardCash == 0
                                      && order.state == ProductionOrderState.Claimed)) continue;
                if (order.id <= 0 || !M16MarketRules.IsResource(order.resourceType) || order.quantity <= 0
                    || order.rewardCash <= 0 || !Enum.IsDefined(typeof(ProductionOrderState), order.state))
                { failure = "M16 Production Order record is invalid."; normalized = null; return false; }
                if (order.state == ProductionOrderState.Active) active++;
                normalized.orders[i] = new M16ProductionOrderSaveRecord { id = order.id, resourceType = order.resourceType,
                    quantity = order.quantity, rewardCash = order.rewardCash, state = order.state };
            }
            if (active > 1) { failure = "M16 permits only one active Production Order."; normalized = null; return false; }
            normalized.lastIronBarProductionUtc = Math.Max(0L, source.lastIronBarProductionUtc);
            normalized.lastToolProductionUtc = Math.Max(0L, source.lastToolProductionUtc);
            normalized.flowBonusExpiresUtc = Math.Max(0L, source.flowBonusExpiresUtc);
            normalized.lastAcceptedUtcUnixSeconds = M9UnixTime.IsPlausible(source.lastAcceptedUtcUnixSeconds)
                ? source.lastAcceptedUtcUnixSeconds : (M9UnixTime.IsPlausible(fallbackUtc) ? fallbackUtc : 0L);
            return true;
        }
    }
}
