using System;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Workers
{
    public readonly struct WoodStockpileReservation
    {
        private readonly WoodStockpile _stockpile;
        private readonly uint _token;

        internal WoodStockpileReservation(WoodStockpile stockpile, uint token)
        {
            _stockpile = stockpile;
            _token = token;
        }

        public bool IsValid => _stockpile != null && _stockpile.IsReservationValid(this);

        internal WoodStockpile Stockpile => _stockpile;
        internal uint Token => _token;
    }

    public sealed class WoodStockpile : MonoBehaviour
    {
        [SerializeField, Min(1)] private int capacity = 30;

        private int _storedWood;
        private int _incomingReservations;
        private uint _activeReservationToken;
        private uint _nextReservationToken;

        public event Action<int, int> StateChanged;
        public event Action<int> WoodDeposited;
        public event Action<int> WoodWithdrawn;

        public int Capacity => capacity;
        public int StoredWood => _storedWood;
        public int IncomingReservations => _incomingReservations;
        public int AvailableIncomingCapacity => Mathf.Max(
            0,
            capacity - _storedWood - _incomingReservations);
        public bool IsFull => AvailableIncomingCapacity <= 0;

        private void OnDisable()
        {
            InvalidateIncomingReservation();
        }

        public bool TryReserveIncoming(out WoodStockpileReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || _incomingReservations != 0
                || AvailableIncomingCapacity <= 0)
            {
                return false;
            }

            _nextReservationToken++;
            if (_nextReservationToken == 0)
            {
                _nextReservationToken = 1;
            }

            _activeReservationToken = _nextReservationToken;
            _incomingReservations = 1;
            reservation = new WoodStockpileReservation(this, _activeReservationToken);
            NotifyStateChanged();
            return true;
        }

        public bool IsReservationValid(WoodStockpileReservation reservation)
        {
            return isActiveAndEnabled
                   && reservation.Stockpile == this
                   && reservation.Token != 0
                   && reservation.Token == _activeReservationToken
                   && _incomingReservations == 1;
        }

        public bool ReleaseIncoming(WoodStockpileReservation reservation)
        {
            if (!IsReservationValid(reservation))
            {
                return false;
            }

            InvalidateIncomingReservation();
            NotifyStateChanged();
            return true;
        }

        public bool TryDepositReserved(WoodStockpileReservation reservation)
        {
            if (!IsReservationValid(reservation) || _storedWood >= capacity)
            {
                return false;
            }

            _storedWood++;
            InvalidateIncomingReservation();
            NotifyStateChanged();
            WoodDeposited?.Invoke(_storedWood);
            return true;
        }

        public bool TryTransferOneTo(CarryStack carryStack)
        {
            if (!isActiveAndEnabled
                || carryStack == null
                || _storedWood <= 0
                || !carryStack.TryReserveCapacity(1))
            {
                return false;
            }

            _storedWood--;
            if (!carryStack.TryCommitReservedAdd(ResourceType.Wood, 1))
            {
                _storedWood++;
                carryStack.ReleaseReservedCapacity(1);
                return false;
            }

            NotifyStateChanged();
            WoodWithdrawn?.Invoke(_storedWood);
            return true;
        }

        private void InvalidateIncomingReservation()
        {
            _incomingReservations = 0;
            _activeReservationToken = 0;
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke(_storedWood, _incomingReservations);
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
        }
    }
}
