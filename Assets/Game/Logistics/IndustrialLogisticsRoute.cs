using System;
using IndustryTycoon.Core;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    public enum IndustrialRouteState
    {
        Disabled = 0,
        Transferring = 1,
        PausedEmptySource = 2,
        PausedFullDestination = 3,
        InvalidConfiguration = 4,
        PausedUnavailableEndpoint = 5
    }

    public sealed class IndustrialLogisticsRoute : MonoBehaviour
    {
        [SerializeField] private string routeId = "industrial_route";
        [SerializeField] private MonoBehaviour sourceEndpoint;
        [SerializeField] private MonoBehaviour destinationEndpoint;
        [SerializeField] private ResourceType resourceType = ResourceType.Plank;
        [SerializeField, Min(1)] private int batchSize = 1;
        [SerializeField, Min(0.1f)] private float transferInterval = 2f;
        [SerializeField] private bool routeEnabled;
        [SerializeField, Range(0, 3)] private int capacityUpgradeLevel;
        [SerializeField, Range(0, 3)] private int speedUpgradeLevel;

        private IIndustrialResourceEndpoint _source;
        private IIndustrialResourceEndpoint _destination;
        private float _elapsed;
        private IndustrialRouteState _state = IndustrialRouteState.Disabled;

        public event Action StateChanged;
        public event Action<IndustrialLogisticsRoute, ResourceType, int>
            TransferCommitted;

        public string RouteId => routeId;
        public string SourceEndpointId => _source != null ? _source.EndpointId : string.Empty;
        public string DestinationEndpointId => _destination != null
            ? _destination.EndpointId
            : string.Empty;
        public ResourceType ResourceType => resourceType;
        public int BatchSize => batchSize;
        public float TransferInterval => transferInterval;
        public bool RouteEnabled => routeEnabled;
        public IndustrialRouteState State => _state;
        public int CapacityUpgradeLevel => capacityUpgradeLevel;
        public int SpeedUpgradeLevel => speedUpgradeLevel;

        public int GetNextUpgradeCost(bool capacity)
        {
            int next = (capacity ? capacityUpgradeLevel : speedUpgradeLevel) + 1;
            return next > 3 ? int.MaxValue : (capacity ? 1200 : 1500) * next * next;
        }

        public bool TryApplyNextUpgrade(bool capacity)
        {
            if (capacity)
            {
                if (capacityUpgradeLevel >= 3) return false;
                capacityUpgradeLevel++;
                batchSize++;
            }
            else
            {
                if (speedUpgradeLevel >= 3) return false;
                speedUpgradeLevel++;
                transferInterval = Mathf.Max(0.1f, transferInterval * 0.8f);
            }

            // Timers are presentation-only. Restarting the interval means an upgrade
            // can never commit an in-flight transfer twice.
            _elapsed = 0f;
            RefreshState();
            StateChanged?.Invoke();
            return true;
        }

        public void RestoreUpgradeLevels(int capacityLevel, int speedLevel)
        {
            capacityUpgradeLevel = Mathf.Clamp(capacityLevel, 0, 3);
            speedUpgradeLevel = Mathf.Clamp(speedLevel, 0, 3);
            _elapsed = 0f;
            RefreshState();
        }

        private void Awake()
        {
            ResolveEndpoints();
            RefreshState();
        }

        private void OnEnable()
        {
            ResolveEndpoints();
            RefreshState();
        }

        private void OnDisable()
        {
            SetState(IndustrialRouteState.Disabled);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float deltaSeconds)
        {
            if (!routeEnabled)
            {
                SetState(IndustrialRouteState.Disabled);
                return;
            }

            if (!HasValidConfiguration())
            {
                SetState(IndustrialRouteState.InvalidConfiguration);
                return;
            }

            if (!_source.IsEndpointOperational || !_destination.IsEndpointOperational)
            {
                _elapsed = 0f;
                SetState(IndustrialRouteState.PausedUnavailableEndpoint);
                return;
            }

            if (_source.GetAvailable(resourceType) < batchSize)
            {
                _elapsed = 0f;
                SetState(IndustrialRouteState.PausedEmptySource);
                return;
            }

            if (_destination.GetAvailableCapacity(resourceType) < batchSize)
            {
                _elapsed = 0f;
                SetState(IndustrialRouteState.PausedFullDestination);
                return;
            }

            SetState(IndustrialRouteState.Transferring);
            _elapsed += Mathf.Max(0f, deltaSeconds);
            if (_elapsed + 0.0001f < transferInterval)
            {
                return;
            }

            _elapsed = Mathf.Repeat(_elapsed, transferInterval);
            if (!IndustrialResourceTransferTransaction.TryCommit(
                    _source,
                    _destination,
                    resourceType,
                    batchSize))
            {
                RefreshState();
                return;
            }

            TransferCommitted?.Invoke(this, resourceType, batchSize);
            RefreshState();
        }

        public bool TryConfigure(
            MonoBehaviour source,
            MonoBehaviour destination,
            ResourceType resource,
            int amount,
            float interval,
            bool enabled)
        {
            IIndustrialResourceEndpoint sourceContract =
                source as IIndustrialResourceEndpoint;
            IIndustrialResourceEndpoint destinationContract =
                destination as IIndustrialResourceEndpoint;
            if (sourceContract == null
                || destinationContract == null
                || ReferenceEquals(sourceContract, destinationContract)
                || amount <= 0
                || interval < 0.1f)
            {
                return false;
            }

            sourceEndpoint = source;
            destinationEndpoint = destination;
            _source = sourceContract;
            _destination = destinationContract;
            resourceType = resource;
            batchSize = amount;
            transferInterval = interval;
            routeEnabled = enabled;
            _elapsed = 0f;
            RefreshState();
            StateChanged?.Invoke();
            return true;
        }

        public void SetRouteEnabled(bool enabled)
        {
            if (routeEnabled == enabled)
            {
                return;
            }

            routeEnabled = enabled;
            _elapsed = 0f;
            RefreshState();
            StateChanged?.Invoke();
        }

        public void RestoreRuntimeState(bool enabled)
        {
            routeEnabled = enabled;
            // In-flight presentation is intentionally not persisted. Ownership is
            // still at the source, so loading resumes from a stable route boundary.
            _elapsed = 0f;
            RefreshState();
        }

        public bool HasValidConfiguration()
        {
            return _source != null
                   && _destination != null
                   && !ReferenceEquals(_source, _destination)
                   && batchSize > 0
                   && transferInterval >= 0.1f;
        }

        private void ResolveEndpoints()
        {
            _source = sourceEndpoint as IIndustrialResourceEndpoint;
            _destination = destinationEndpoint as IIndustrialResourceEndpoint;
        }

        private void RefreshState()
        {
            if (!routeEnabled || !isActiveAndEnabled)
            {
                SetState(IndustrialRouteState.Disabled);
            }
            else if (!HasValidConfiguration())
            {
                SetState(IndustrialRouteState.InvalidConfiguration);
            }
            else if (!_source.IsEndpointOperational || !_destination.IsEndpointOperational)
            {
                SetState(IndustrialRouteState.PausedUnavailableEndpoint);
            }
            else if (_source.GetAvailable(resourceType) < batchSize)
            {
                SetState(IndustrialRouteState.PausedEmptySource);
            }
            else if (_destination.GetAvailableCapacity(resourceType) < batchSize)
            {
                SetState(IndustrialRouteState.PausedFullDestination);
            }
            else
            {
                SetState(IndustrialRouteState.Transferring);
            }
        }

        private void SetState(IndustrialRouteState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                routeId = "industrial_route";
            }

            batchSize = Mathf.Max(1, batchSize);
            transferInterval = Mathf.Max(0.1f, transferInterval);
        }
    }
}
