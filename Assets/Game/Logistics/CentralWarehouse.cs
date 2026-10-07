using System;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    [Serializable]
    public sealed class WarehouseResourceSlot
    {
        public ResourceType resourceType;
        [Min(1)] public int capacity = 60;
    }

    public sealed class CentralWarehouse : MonoBehaviour, IIndustrialResourceEndpoint
    {
        public const string StableEndpointId = "central_warehouse";
        private const int ResourceCount = 6;

        [SerializeField] private WarehouseResourceSlot[] slots = CreateDefaultSlots();

        private readonly int[] _amounts = new int[ResourceCount];
        private readonly int[] _capacities = new int[ResourceCount];
        private readonly int[] _reservedIncoming = new int[ResourceCount];
        private readonly int[] _reservedOutgoing = new int[ResourceCount];
        private uint _nextToken;
        private IndustrialResourceReservation _activeIncoming;
        private IndustrialResourceReservation _activeOutgoing;

        public event Action StateChanged;
        public event Action<ResourceType, int> ResourceDeposited;
        public event Action<ResourceType, int> ResourceWithdrawn;

        public string EndpointId => StableEndpointId;
        public bool IsEndpointOperational => isActiveAndEnabled;
        public WarehouseResourceSlot[] ConfiguredSlots => slots;

        private void Awake()
        {
            ApplyConfiguredCapacities();
        }

        private void OnDisable()
        {
            ReleaseAllReservations();
        }

        public int GetAmount(ResourceType resourceType)
        {
            return IsSupported(resourceType) ? _amounts[(int)resourceType] : 0;
        }

        public int GetCapacity(ResourceType resourceType)
        {
            return IsSupported(resourceType) ? _capacities[(int)resourceType] : 0;
        }

        public int GetAvailable(ResourceType resourceType)
        {
            return IsSupported(resourceType)
                ? Mathf.Max(0, _amounts[(int)resourceType]
                               - _reservedOutgoing[(int)resourceType])
                : 0;
        }

        public int GetAvailableCapacity(ResourceType resourceType)
        {
            if (!IsSupported(resourceType))
            {
                return 0;
            }

            int index = (int)resourceType;
            return Mathf.Max(
                0,
                _capacities[index] - _amounts[index] - _reservedIncoming[index]);
        }

        public bool TryDepositFrom(CarryStack carryStack, ResourceType resourceType)
        {
            if (!isActiveAndEnabled
                || carryStack == null
                || GetAvailableCapacity(resourceType) <= 0
                || !carryStack.TryRemove(resourceType, 1))
            {
                return false;
            }

            _amounts[(int)resourceType]++;
            AssertInvariants();
            StateChanged?.Invoke();
            ResourceDeposited?.Invoke(resourceType, 1);
            return true;
        }

        public bool TryWithdrawTo(CarryStack carryStack, ResourceType resourceType)
        {
            if (!isActiveAndEnabled
                || carryStack == null
                || GetAvailable(resourceType) <= 0
                || !carryStack.TryReserveCapacity(resourceType, 1))
            {
                return false;
            }

            int index = (int)resourceType;
            _amounts[index]--;
            if (!carryStack.TryCommitReservedAdd(resourceType, 1))
            {
                _amounts[index]++;
                carryStack.ReleaseReservedCapacity(1);
                return false;
            }

            AssertInvariants();
            StateChanged?.Invoke();
            ResourceWithdrawn?.Invoke(resourceType, 1);
            return true;
        }

        public bool TryReserveOutgoing(
            ResourceType resourceType,
            int amount,
            out IndustrialResourceReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || !IsSupported(resourceType)
                || amount <= 0
                || _activeOutgoing.IsValid
                || GetAvailable(resourceType) < amount)
            {
                return false;
            }

            int index = (int)resourceType;
            _reservedOutgoing[index] += amount;
            _activeOutgoing = CreateReservation(
                IndustrialReservationKind.Outgoing,
                resourceType,
                amount);
            reservation = _activeOutgoing;
            AssertInvariants();
            return true;
        }

        public bool TryReserveIncoming(
            ResourceType resourceType,
            int amount,
            out IndustrialResourceReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || !IsSupported(resourceType)
                || amount <= 0
                || _activeIncoming.IsValid
                || GetAvailableCapacity(resourceType) < amount)
            {
                return false;
            }

            int index = (int)resourceType;
            _reservedIncoming[index] += amount;
            _activeIncoming = CreateReservation(
                IndustrialReservationKind.Incoming,
                resourceType,
                amount);
            reservation = _activeIncoming;
            AssertInvariants();
            return true;
        }

        public bool IsReservationValid(IndustrialResourceReservation reservation)
        {
            if (!isActiveAndEnabled
                || !ReferenceEquals(reservation.Endpoint, this)
                || reservation.Token == 0
                || !IsSupported(reservation.ResourceType)
                || reservation.Amount <= 0)
            {
                return false;
            }

            IndustrialResourceReservation active =
                reservation.Kind == IndustrialReservationKind.Incoming
                    ? _activeIncoming
                    : _activeOutgoing;
            return active.Token == reservation.Token
                   && active.Kind == reservation.Kind
                   && active.ResourceType == reservation.ResourceType
                   && active.Amount == reservation.Amount;
        }

        public bool ReleaseReservation(IndustrialResourceReservation reservation)
        {
            if (!IsReservationValid(reservation))
            {
                return false;
            }

            ClearReservation(reservation);
            AssertInvariants();
            return true;
        }

        public void FinalizeReservationForTransfer(
            IndustrialResourceReservation reservation)
        {
            if (!IsReservationValid(reservation))
            {
                throw new InvalidOperationException(
                    "Warehouse transfer reservation is no longer valid.");
            }

            int index = (int)reservation.ResourceType;
            if (reservation.Kind == IndustrialReservationKind.Outgoing)
            {
                _amounts[index] -= reservation.Amount;
            }
            else
            {
                _amounts[index] += reservation.Amount;
            }

            ClearReservation(reservation);
            AssertInvariants();
        }

        public void PublishTransferCommitted(
            IndustrialResourceReservation reservation)
        {
            StateChanged?.Invoke();
            if (reservation.Kind == IndustrialReservationKind.Outgoing)
            {
                ResourceWithdrawn?.Invoke(
                    reservation.ResourceType,
                    reservation.Amount);
            }
            else
            {
                ResourceDeposited?.Invoke(
                    reservation.ResourceType,
                    reservation.Amount);
            }
        }

        public bool RestoreStableState(int[] amounts, int[] capacities)
        {
            if (amounts == null
                || capacities == null
                || amounts.Length != ResourceCount
                || capacities.Length != ResourceCount)
            {
                return false;
            }

            for (int i = 0; i < ResourceCount; i++)
            {
                if (capacities[i] <= 0 || amounts[i] < 0 || amounts[i] > capacities[i])
                {
                    return false;
                }
            }

            ReleaseAllReservations();
            Array.Copy(amounts, _amounts, ResourceCount);
            Array.Copy(capacities, _capacities, ResourceCount);
            SynchronizeSlotsFromCapacities();
            AssertInvariants();
            StateChanged?.Invoke();
            return true;
        }

        public int[] CaptureAmounts()
        {
            var copy = new int[ResourceCount];
            Array.Copy(_amounts, copy, ResourceCount);
            return copy;
        }

        public int[] CaptureCapacities()
        {
            var copy = new int[ResourceCount];
            Array.Copy(_capacities, copy, ResourceCount);
            return copy;
        }

        private IndustrialResourceReservation CreateReservation(
            IndustrialReservationKind kind,
            ResourceType resourceType,
            int amount)
        {
            _nextToken++;
            if (_nextToken == 0)
            {
                _nextToken = 1;
            }

            return new IndustrialResourceReservation(
                this,
                _nextToken,
                kind,
                resourceType,
                amount);
        }

        private void ClearReservation(IndustrialResourceReservation reservation)
        {
            int index = (int)reservation.ResourceType;
            if (reservation.Kind == IndustrialReservationKind.Incoming)
            {
                _reservedIncoming[index] -= reservation.Amount;
                _activeIncoming = default;
            }
            else
            {
                _reservedOutgoing[index] -= reservation.Amount;
                _activeOutgoing = default;
            }
        }

        private void ReleaseAllReservations()
        {
            Array.Clear(_reservedIncoming, 0, _reservedIncoming.Length);
            Array.Clear(_reservedOutgoing, 0, _reservedOutgoing.Length);
            _activeIncoming = default;
            _activeOutgoing = default;
        }

        private void ApplyConfiguredCapacities()
        {
            for (int i = 0; i < ResourceCount; i++)
            {
                _capacities[i] = 60;
            }

            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    WarehouseResourceSlot slot = slots[i];
                    if (slot != null && IsSupported(slot.resourceType))
                    {
                        _capacities[(int)slot.resourceType] = Mathf.Max(1, slot.capacity);
                    }
                }
            }

            SynchronizeSlotsFromCapacities();
        }

        private void SynchronizeSlotsFromCapacities()
        {
            slots = new WarehouseResourceSlot[ResourceCount];
            for (int i = 0; i < ResourceCount; i++)
            {
                slots[i] = new WarehouseResourceSlot
                {
                    resourceType = (ResourceType)i,
                    capacity = Mathf.Max(1, _capacities[i])
                };
            }
        }

        private void AssertInvariants()
        {
            for (int i = 0; i < ResourceCount; i++)
            {
                Debug.Assert(_amounts[i] >= 0 && _amounts[i] <= _capacities[i]);
                Debug.Assert(_reservedOutgoing[i] >= 0
                             && _reservedOutgoing[i] <= _amounts[i]);
                Debug.Assert(_reservedIncoming[i] >= 0
                             && _amounts[i] + _reservedIncoming[i] <= _capacities[i]);
            }
        }

        private static bool IsSupported(ResourceType resourceType)
        {
            int index = (int)resourceType;
            return index >= 0 && index < ResourceCount;
        }

        private static WarehouseResourceSlot[] CreateDefaultSlots()
        {
            var result = new WarehouseResourceSlot[ResourceCount];
            for (int i = 0; i < ResourceCount; i++)
            {
                result[i] = new WarehouseResourceSlot
                {
                    resourceType = (ResourceType)i,
                    capacity = 60
                };
            }

            return result;
        }

        private void OnValidate()
        {
            if (slots == null || slots.Length != ResourceCount)
            {
                slots = CreateDefaultSlots();
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    slots[i] = new WarehouseResourceSlot
                    {
                        resourceType = (ResourceType)i,
                        capacity = 60
                    };
                }

                slots[i].resourceType = (ResourceType)i;
                slots[i].capacity = Mathf.Max(1, slots[i].capacity);
            }
        }
    }
}
