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

    public readonly struct WoodStockpileOutgoingReservation
    {
        private readonly WoodStockpile _stockpile;
        private readonly uint _token;

        internal WoodStockpileOutgoingReservation(WoodStockpile stockpile, uint token)
        {
            _stockpile = stockpile;
            _token = token;
        }

        public bool IsValid => _stockpile != null
                               && _stockpile.IsOutgoingReservationValid(this);

        internal WoodStockpile Stockpile => _stockpile;
        internal uint Token => _token;
    }

    public sealed class WoodStockpile : MonoBehaviour
    {
        [SerializeField, Min(1)] private int capacity = 30;

        private int _storedWood;
        private int _incomingReservations;
        private int _outgoingReservations;
        private uint _activeReservationToken;
        private uint _nextReservationToken;
        private uint _activeOutgoingReservationToken;
        private uint _nextOutgoingReservationToken;
        private bool _isPlayerWithdrawalInProgress;

        public event Action<int, int> StateChanged;
        public event Action<int> WoodDeposited;
        public event Action<int> WoodWithdrawn;

        public int Capacity => capacity;
        public int StoredWood => _storedWood;
        public int IncomingReservations => _incomingReservations;
        public int OutgoingReservations => _outgoingReservations;
        public int AvailableWood => _storedWood;
        public int TotalOwnedWood => _storedWood + _outgoingReservations;
        public int AvailableIncomingCapacity => Mathf.Max(
            0,
            capacity - _storedWood - _incomingReservations - _outgoingReservations);
        public bool IsFull => AvailableIncomingCapacity <= 0;

        private void OnDisable()
        {
            bool stateChanged = _incomingReservations > 0 || _outgoingReservations > 0;
            InvalidateIncomingReservation();
            RefundAndInvalidateOutgoingReservation();
            _isPlayerWithdrawalInProgress = false;
            if (stateChanged)
            {
                NotifyStateChanged();
            }
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

        public bool TryReserveOutgoing(
            out WoodStockpileOutgoingReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || _outgoingReservations != 0
                || _storedWood <= 0)
            {
                return false;
            }

            _nextOutgoingReservationToken++;
            if (_nextOutgoingReservationToken == 0)
            {
                _nextOutgoingReservationToken = 1;
            }

            _activeOutgoingReservationToken = _nextOutgoingReservationToken;
            _storedWood--;
            _outgoingReservations = 1;
            reservation = new WoodStockpileOutgoingReservation(
                this,
                _activeOutgoingReservationToken);
            NotifyStateChanged();
            WoodWithdrawn?.Invoke(_storedWood);
            return true;
        }

        public bool IsOutgoingReservationValid(
            WoodStockpileOutgoingReservation reservation)
        {
            return isActiveAndEnabled
                   && reservation.Stockpile == this
                   && reservation.Token != 0
                   && reservation.Token == _activeOutgoingReservationToken
                   && _outgoingReservations == 1;
        }

        public bool ReleaseOutgoing(WoodStockpileOutgoingReservation reservation)
        {
            if (!IsOutgoingReservationValid(reservation))
            {
                return false;
            }

            _storedWood++;
            InvalidateOutgoingReservation();
            NotifyStateChanged();
            return true;
        }

        internal void FinalizeOutgoingForTransfer(
            WoodStockpileOutgoingReservation reservation)
        {
            Debug.Assert(IsOutgoingReservationValid(reservation));
            InvalidateOutgoingReservation();
        }

        internal void PublishOutgoingTransferCommitted()
        {
            NotifyStateChanged();
        }

        public bool TryTransferOneTo(CarryStack carryStack)
        {
            if (!isActiveAndEnabled
                || _isPlayerWithdrawalInProgress
                || carryStack == null
                || _storedWood <= 0
                || !carryStack.TryReserveCapacity(ResourceType.Wood, 1))
            {
                return false;
            }

            _isPlayerWithdrawalInProgress = true;
            bool transferred = false;
            try
            {
                _storedWood--;
                if (!carryStack.TryCommitReservedAdd(ResourceType.Wood, 1))
                {
                    _storedWood++;
                    carryStack.ReleaseReservedCapacity(1);
                    return false;
                }

                transferred = true;
            }
            finally
            {
                _isPlayerWithdrawalInProgress = false;
            }

            if (!transferred)
            {
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

        private void RefundAndInvalidateOutgoingReservation()
        {
            if (_outgoingReservations > 0)
            {
                _storedWood += _outgoingReservations;
            }

            InvalidateOutgoingReservation();
        }

        private void InvalidateOutgoingReservation()
        {
            _outgoingReservations = 0;
            _activeOutgoingReservationToken = 0;
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
