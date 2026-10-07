using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;

namespace IndustryTycoon.Persistence
{
    [Serializable]
    public sealed class M13WarehouseResourceSaveRecord
    {
        public ResourceType resourceType;
        public int amount;
        public int capacity = 60;
    }

    [Serializable]
    public sealed class M13WarehouseSaveData
    {
        public M13WarehouseResourceSaveRecord[] resources;

        public static M13WarehouseSaveData CreateFresh()
        {
            int count = Enum.GetValues(typeof(ResourceType)).Length;
            var result = new M13WarehouseSaveData
            {
                resources = new M13WarehouseResourceSaveRecord[count]
            };
            for (int i = 0; i < count; i++)
            {
                result.resources[i] = new M13WarehouseResourceSaveRecord
                {
                    resourceType = (ResourceType)i,
                    capacity = 60
                };
            }

            return result;
        }

        public M13WarehouseResourceSaveRecord Get(ResourceType resourceType)
        {
            int index = (int)resourceType;
            return resources != null && index >= 0 && index < resources.Length
                ? resources[index]
                : null;
        }
    }

    [Serializable]
    public sealed class M13RouteSaveRecord
    {
        public string id;
        public string sourceEndpointId;
        public string destinationEndpointId;
        public ResourceType resourceType;
        public int batchSize = 1;
        public float transferIntervalSeconds = 2f;
        public bool enabled;
        public IndustrialRouteState state;
    }

    [Serializable]
    public sealed class M13LogisticsSaveData
    {
        public M13WarehouseSaveData warehouse;
        public M13RouteSaveRecord[] routes;

        public static M13LogisticsSaveData CreateFresh()
        {
            return new M13LogisticsSaveData
            {
                warehouse = M13WarehouseSaveData.CreateFresh(),
                routes = new[]
                {
                    new M13RouteSaveRecord
                    {
                        id = "processor_to_warehouse",
                        sourceEndpointId = "wood_processor",
                        destinationEndpointId = CentralWarehouse.StableEndpointId,
                        resourceType = ResourceType.Plank,
                        batchSize = 1,
                        transferIntervalSeconds = 2f,
                        state = IndustrialRouteState.Disabled
                    },
                    new M13RouteSaveRecord
                    {
                        id = "warehouse_to_packing",
                        sourceEndpointId = CentralWarehouse.StableEndpointId,
                        destinationEndpointId = "packing_station",
                        resourceType = ResourceType.Plank,
                        batchSize = 1,
                        transferIntervalSeconds = 2f,
                        state = IndustrialRouteState.Disabled
                    },
                    new M13RouteSaveRecord
                    {
                        id = "smelter_to_warehouse",
                        sourceEndpointId = "iron_smelter",
                        destinationEndpointId = CentralWarehouse.StableEndpointId,
                        resourceType = ResourceType.IronBar,
                        batchSize = 1,
                        transferIntervalSeconds = 2f,
                        state = IndustrialRouteState.Disabled
                    }
                }
            };
        }
    }

    public static class M13LogisticsSaveValidator
    {
        public const int MaximumRoutes = 16;
        public const int MaximumPerResourceCapacity = 100000;

        public static bool TryNormalize(
            M13LogisticsSaveData source,
            out M13LogisticsSaveData normalized,
            out string failureReason)
        {
            normalized = null;
            failureReason = null;
            int resourceCount = Enum.GetValues(typeof(ResourceType)).Length;
            if (source?.warehouse?.resources == null
                || source.warehouse.resources.Length != resourceCount
                || source.routes == null
                || source.routes.Length > MaximumRoutes)
            {
                failureReason = "M13 logistics state has an invalid canonical shape.";
                return false;
            }

            normalized = new M13LogisticsSaveData
            {
                warehouse = M13WarehouseSaveData.CreateFresh(),
                routes = new M13RouteSaveRecord[source.routes.Length]
            };
            var seenResources = new bool[resourceCount];
            for (int i = 0; i < source.warehouse.resources.Length; i++)
            {
                M13WarehouseResourceSaveRecord record =
                    source.warehouse.resources[i];
                int index = record != null ? (int)record.resourceType : -1;
                if (index < 0 || index >= resourceCount || seenResources[index])
                {
                    failureReason =
                        "M13 Warehouse contains a missing, duplicate, or unknown resource.";
                    normalized = null;
                    return false;
                }

                seenResources[index] = true;
                int capacity = Clamp(
                    record.capacity,
                    1,
                    MaximumPerResourceCapacity);
                normalized.warehouse.resources[index].capacity = capacity;
                normalized.warehouse.resources[index].amount = Clamp(
                    record.amount,
                    0,
                    capacity);
            }

            var seenRoutes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.routes.Length; i++)
            {
                M13RouteSaveRecord route = source.routes[i];
                if (route == null
                    || string.IsNullOrWhiteSpace(route.id)
                    || string.IsNullOrWhiteSpace(route.sourceEndpointId)
                    || string.IsNullOrWhiteSpace(route.destinationEndpointId)
                    || string.Equals(
                        route.sourceEndpointId,
                        route.destinationEndpointId,
                        StringComparison.Ordinal)
                    || !seenRoutes.Add(route.id)
                    || !Enum.IsDefined(typeof(ResourceType), route.resourceType))
                {
                    failureReason = "M13 route configuration is invalid or duplicated.";
                    normalized = null;
                    return false;
                }

                normalized.routes[i] = new M13RouteSaveRecord
                {
                    id = route.id,
                    sourceEndpointId = route.sourceEndpointId,
                    destinationEndpointId = route.destinationEndpointId,
                    resourceType = route.resourceType,
                    batchSize = Clamp(route.batchSize, 1, 100),
                    transferIntervalSeconds = ClampFinite(
                        route.transferIntervalSeconds,
                        0.1f,
                        3600f,
                        2f),
                    enabled = route.enabled,
                    state = NormalizeState(route.state, route.enabled)
                };
            }

            return true;
        }

        private static IndustrialRouteState NormalizeState(
            IndustrialRouteState state,
            bool enabled)
        {
            if (!enabled)
            {
                return IndustrialRouteState.Disabled;
            }

            return Enum.IsDefined(typeof(IndustrialRouteState), state)
                ? state
                : IndustrialRouteState.InvalidConfiguration;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static float ClampFinite(
            float value,
            float minimum,
            float maximum,
            float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : value < minimum
                    ? minimum
                    : value > maximum
                        ? maximum
                        : value;
        }
    }

    public static class M13OfflineLogisticsCalculator
    {
        public static int Settle(
            M9SaveData data,
            long creditedAwaySeconds,
            double efficiency)
        {
            if (data?.logistics?.routes == null
                || data.logistics.warehouse == null
                || data.returnScreenPending
                || creditedAwaySeconds <= 0L
                || efficiency <= 0d)
            {
                return 0;
            }

            double effectiveSeconds = creditedAwaySeconds
                                      * Math.Min(1d, Math.Max(0d, efficiency));
            // Freeze every source budget before settlement. This prevents a newly
            // arrived item from traversing multiple routes during one offline pass.
            var initialBudgets = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < data.logistics.routes.Length; i++)
            {
                M13RouteSaveRecord route = data.logistics.routes[i];
                if (route == null || !route.enabled)
                {
                    continue;
                }

                string key = BuildKey(route.sourceEndpointId, route.resourceType);
                if (!initialBudgets.ContainsKey(key))
                {
                    initialBudgets[key] = ReadAmount(
                        data,
                        route.sourceEndpointId,
                        route.resourceType);
                }
            }

            M13RouteSaveRecord[] ordered =
                (M13RouteSaveRecord[])data.logistics.routes.Clone();
            Array.Sort(
                ordered,
                (left, right) => string.CompareOrdinal(left?.id, right?.id));
            int totalTransferred = 0;
            for (int i = 0; i < ordered.Length; i++)
            {
                M13RouteSaveRecord route = ordered[i];
                if (route == null || !route.enabled || route.batchSize <= 0)
                {
                    continue;
                }

                long operationBudget = (long)Math.Floor(
                    effectiveSeconds / Math.Max(0.1d, route.transferIntervalSeconds));
                if (operationBudget <= 0L)
                {
                    continue;
                }

                string sourceKey = BuildKey(
                    route.sourceEndpointId,
                    route.resourceType);
                int sourceBudget = initialBudgets.TryGetValue(
                    sourceKey,
                    out int initial)
                    ? initial
                    : 0;
                int destinationSpace = ReadCapacity(
                    data,
                    route.destinationEndpointId,
                    route.resourceType)
                    - ReadAmount(
                        data,
                        route.destinationEndpointId,
                        route.resourceType);
                long amountBudget = operationBudget * route.batchSize;
                int transfer = (int)Math.Min(
                    int.MaxValue,
                    Math.Min(
                        amountBudget,
                        Math.Min(sourceBudget, Math.Max(0, destinationSpace))));
                transfer -= transfer % route.batchSize;
                if (transfer <= 0)
                {
                    continue;
                }

                if (!WriteDelta(
                        data,
                        route.sourceEndpointId,
                        route.resourceType,
                        -transfer))
                {
                    continue;
                }

                if (!WriteDelta(
                        data,
                        route.destinationEndpointId,
                        route.resourceType,
                        transfer))
                {
                    bool rolledBack = WriteDelta(
                        data,
                        route.sourceEndpointId,
                        route.resourceType,
                        transfer);
                    if (!rolledBack)
                    {
                        throw new InvalidOperationException(
                            "Offline logistics could not restore a rejected atomic transfer.");
                    }

                    continue;
                }

                initialBudgets[sourceKey] = sourceBudget - transfer;
                totalTransferred += transfer;
            }

            return totalTransferred;
        }

        private static int ReadAmount(
            M9SaveData data,
            string endpointId,
            ResourceType resourceType)
        {
            if (string.Equals(
                    endpointId,
                    CentralWarehouse.StableEndpointId,
                    StringComparison.Ordinal))
            {
                return data.logistics.warehouse.Get(resourceType)?.amount ?? 0;
            }

            if (string.Equals(endpointId, "wood_processor", StringComparison.Ordinal))
            {
                return resourceType == ResourceType.Wood
                    ? data.processorInputWood
                    : resourceType == ResourceType.Plank
                        ? data.processorOutputPlanks
                        : 0;
            }

            if (string.Equals(endpointId, "packing_station", StringComparison.Ordinal))
            {
                return resourceType == ResourceType.Plank
                    ? data.packingInputPlanks
                    : resourceType == ResourceType.Crate
                        ? data.packingOutputCrates
                        : 0;
            }

            if (string.Equals(endpointId, "iron_smelter", StringComparison.Ordinal))
            {
                if (data.mining == null)
                {
                    return 0;
                }

                return resourceType == ResourceType.IronOre
                    ? data.mining.smelterInputIronOre
                    : resourceType == ResourceType.IronBar
                        ? data.mining.smelterOutputIronBars
                        : 0;
            }

            return 0;
        }

        private static int ReadCapacity(
            M9SaveData data,
            string endpointId,
            ResourceType resourceType)
        {
            if (string.Equals(
                    endpointId,
                    CentralWarehouse.StableEndpointId,
                    StringComparison.Ordinal))
            {
                return data.logistics.warehouse.Get(resourceType)?.capacity ?? 0;
            }

            if (string.Equals(endpointId, "wood_processor", StringComparison.Ordinal))
            {
                return resourceType == ResourceType.Wood ? 24 : 0;
            }

            if (string.Equals(endpointId, "packing_station", StringComparison.Ordinal))
            {
                return resourceType == ResourceType.Plank ? 24 : 0;
            }

            if (string.Equals(endpointId, "iron_smelter", StringComparison.Ordinal))
            {
                return resourceType == ResourceType.IronOre ? 24 : 0;
            }

            return 0;
        }

        private static bool WriteDelta(
            M9SaveData data,
            string endpointId,
            ResourceType resourceType,
            int delta)
        {
            if (string.Equals(
                    endpointId,
                    CentralWarehouse.StableEndpointId,
                    StringComparison.Ordinal))
            {
                M13WarehouseResourceSaveRecord record =
                    data.logistics.warehouse.Get(resourceType);
                if (record == null
                    || record.amount + delta < 0
                    || record.amount + delta > record.capacity)
                {
                    return false;
                }

                record.amount += delta;
                return true;
            }

            if (string.Equals(endpointId, "wood_processor", StringComparison.Ordinal))
            {
                if (resourceType == ResourceType.Wood)
                {
                    data.processorInputWood += delta;
                    return data.processorInputWood >= 0;
                }

                if (resourceType == ResourceType.Plank)
                {
                    data.processorOutputPlanks += delta;
                    return data.processorOutputPlanks >= 0;
                }
            }

            if (string.Equals(endpointId, "packing_station", StringComparison.Ordinal))
            {
                if (resourceType == ResourceType.Plank)
                {
                    data.packingInputPlanks += delta;
                    return data.packingInputPlanks >= 0;
                }

                if (resourceType == ResourceType.Crate)
                {
                    data.packingOutputCrates += delta;
                    return data.packingOutputCrates >= 0;
                }
            }

            if (string.Equals(endpointId, "iron_smelter", StringComparison.Ordinal))
            {
                if (data.mining == null)
                {
                    return false;
                }

                if (resourceType == ResourceType.IronOre)
                {
                    int updated = data.mining.smelterInputIronOre + delta;
                    if (updated < 0 || updated > 24)
                    {
                        return false;
                    }

                    data.mining.smelterInputIronOre = updated;
                    return true;
                }

                if (resourceType == ResourceType.IronBar)
                {
                    int updated = data.mining.smelterOutputIronBars + delta;
                    if (updated < 0 || updated > 12)
                    {
                        return false;
                    }

                    data.mining.smelterOutputIronBars = updated;
                    return true;
                }
            }

            return false;
        }

        private static string BuildKey(string endpointId, ResourceType resourceType)
        {
            return endpointId + ":" + (int)resourceType;
        }
    }
}
