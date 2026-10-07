using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Persistence;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    public sealed class IndustrialLogisticsNetwork : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour[] endpoints;
        [SerializeField] private IndustrialLogisticsRoute[] routes;

        private readonly Dictionary<string, MonoBehaviour> _endpointById =
            new Dictionary<string, MonoBehaviour>(StringComparer.Ordinal);

        public event Action StateChanged;
        public event Action<ResourceType, int> ResourceTransported;

        public IndustrialLogisticsRoute[] Routes => routes;

        private void Awake()
        {
            RebuildEndpointIndex();
        }

        private void OnEnable()
        {
            Subscribe(true);
        }

        private void OnDisable()
        {
            Subscribe(false);
        }

        public M13LogisticsSaveData CapturePersistentState(CentralWarehouse warehouse)
        {
            var data = M13LogisticsSaveData.CreateFresh();
            if (warehouse != null)
            {
                int[] amounts = warehouse.CaptureAmounts();
                int[] capacities = warehouse.CaptureCapacities();
                for (int i = 0; i < data.warehouse.resources.Length; i++)
                {
                    data.warehouse.resources[i].amount = amounts[i];
                    data.warehouse.resources[i].capacity = capacities[i];
                }
            }

            if (routes == null)
            {
                data.routes = Array.Empty<M13RouteSaveRecord>();
                return data;
            }

            data.routes = new M13RouteSaveRecord[routes.Length];
            for (int i = 0; i < routes.Length; i++)
            {
                IndustrialLogisticsRoute route = routes[i];
                data.routes[i] = route == null
                    ? new M13RouteSaveRecord { id = $"missing_route_{i}" }
                    : new M13RouteSaveRecord
                    {
                        id = route.RouteId,
                        sourceEndpointId = route.SourceEndpointId,
                        destinationEndpointId = route.DestinationEndpointId,
                        resourceType = route.ResourceType,
                        batchSize = route.BatchSize,
                        transferIntervalSeconds = route.TransferInterval,
                        enabled = route.RouteEnabled,
                        state = route.State,
                        capacityUpgradeLevel = route.CapacityUpgradeLevel,
                        speedUpgradeLevel = route.SpeedUpgradeLevel
                    };
            }

            return data;
        }

        public bool RestorePersistentState(
            M13LogisticsSaveData data,
            CentralWarehouse warehouse)
        {
            if (data == null || data.warehouse == null || data.routes == null)
            {
                return false;
            }

            RebuildEndpointIndex();
            int resourceCount = Enum.GetValues(typeof(ResourceType)).Length;
            var amounts = new int[resourceCount];
            var capacities = new int[resourceCount];
            if (data.warehouse.resources == null
                || data.warehouse.resources.Length != resourceCount)
            {
                return false;
            }

            for (int i = 0; i < data.warehouse.resources.Length; i++)
            {
                M13WarehouseResourceSaveRecord record = data.warehouse.resources[i];
                if (record == null || (int)record.resourceType != i)
                {
                    return false;
                }

                amounts[i] = record.amount;
                capacities[i] = record.capacity;
            }

            if (warehouse != null && !warehouse.RestoreStableState(amounts, capacities))
            {
                return false;
            }

            if (routes != null)
            {
                for (int i = 0; i < routes.Length; i++)
                {
                    IndustrialLogisticsRoute route = routes[i];
                    M13RouteSaveRecord record = FindRoute(data, route?.RouteId);
                    if (route == null || record == null)
                    {
                        continue;
                    }

                    if (!_endpointById.TryGetValue(
                            record.sourceEndpointId,
                            out MonoBehaviour source)
                        || !_endpointById.TryGetValue(
                            record.destinationEndpointId,
                            out MonoBehaviour destination)
                        || !route.TryConfigure(
                            source,
                            destination,
                            record.resourceType,
                            record.batchSize,
                            record.transferIntervalSeconds,
                            record.enabled))
                    {
                        route.SetRouteEnabled(false);
                    }

                    route.RestoreRuntimeState(record.enabled);
                    route.RestoreUpgradeLevels(
                        record.capacityUpgradeLevel,
                        record.speedUpgradeLevel);
                }
            }

            StateChanged?.Invoke();
            return true;
        }

        private void Subscribe(bool subscribe)
        {
            if (routes == null)
            {
                return;
            }

            for (int i = 0; i < routes.Length; i++)
            {
                IndustrialLogisticsRoute route = routes[i];
                if (route == null)
                {
                    continue;
                }

                if (subscribe)
                {
                    route.StateChanged += HandleRouteStateChanged;
                    route.TransferCommitted += HandleTransferCommitted;
                }
                else
                {
                    route.StateChanged -= HandleRouteStateChanged;
                    route.TransferCommitted -= HandleTransferCommitted;
                }
            }
        }

        private void HandleRouteStateChanged()
        {
            StateChanged?.Invoke();
        }

        private void HandleTransferCommitted(
            IndustrialLogisticsRoute route,
            ResourceType resourceType,
            int amount)
        {
            ResourceTransported?.Invoke(resourceType, amount);
            StateChanged?.Invoke();
        }

        private void RebuildEndpointIndex()
        {
            _endpointById.Clear();
            if (endpoints == null)
            {
                return;
            }

            for (int i = 0; i < endpoints.Length; i++)
            {
                MonoBehaviour behaviour = endpoints[i];
                if (!(behaviour is IIndustrialResourceEndpoint endpoint)
                    || string.IsNullOrWhiteSpace(endpoint.EndpointId)
                    || _endpointById.ContainsKey(endpoint.EndpointId))
                {
                    continue;
                }

                _endpointById.Add(endpoint.EndpointId, behaviour);
            }
        }

        private static M13RouteSaveRecord FindRoute(
            M13LogisticsSaveData data,
            string routeId)
        {
            if (data?.routes == null || string.IsNullOrEmpty(routeId))
            {
                return null;
            }

            for (int i = 0; i < data.routes.Length; i++)
            {
                M13RouteSaveRecord record = data.routes[i];
                if (record != null
                    && string.Equals(record.id, routeId, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }
    }
}
