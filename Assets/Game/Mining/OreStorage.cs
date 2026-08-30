using System;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Mining
{
    public readonly struct OreStorageReservation
    {
        private readonly OreStorage _storage;
        private readonly uint _token;

        internal OreStorageReservation(OreStorage storage, uint token)
        {
            _storage = storage;
            _token = token;
        }

        public bool IsValid => _storage != null
                               && _storage.IsIncomingReservationValid(this);

        internal OreStorage Storage => _storage;
        internal uint Token => _token;
    }

    public sealed class OreStorage : MonoBehaviour
    {
        [SerializeField, Min(1)] private int capacity = 30;

        private int _storedOre;
        private int _incomingReservations;
        private uint _activeIncomingReservationToken;
        private uint _nextIncomingReservationToken;
        private bool _isPlayerWithdrawalInProgress;

        public event Action<int, int> StateChanged;
        public event Action<int> OreDeposited;
        public event Action<int> OreWithdrawn;

        public int Capacity => capacity;
        public int StoredOre => _storedOre;
        public int IncomingReservations => _incomingReservations;
        public int AvailableOre => _storedOre;
        public int AvailableIncomingCapacity => Mathf.Max(
            0,
            capacity - _storedOre - _incomingReservations);
        public bool IsFull => AvailableIncomingCapacity <= 0;

        private void OnDisable()
        {
            bool stateChanged = _incomingReservations > 0;
            InvalidateIncomingReservation();
            _isPlayerWithdrawalInProgress = false;
            if (stateChanged)
            {
                NotifyStateChanged();
            }
        }

        public bool TryReserveIncoming(out OreStorageReservation reservation)
        {
            reservation = default;
            if (!isActiveAndEnabled
                || _incomingReservations != 0
                || AvailableIncomingCapacity <= 0)
            {
                return false;
            }

            _nextIncomingReservationToken++;
            if (_nextIncomingReservationToken == 0)
            {
                _nextIncomingReservationToken = 1;
            }

            _activeIncomingReservationToken = _nextIncomingReservationToken;
            _incomingReservations = 1;
            reservation = new OreStorageReservation(
                this,
                _activeIncomingReservationToken);
            AssertInvariants();
            NotifyStateChanged();
            // StateChanged is synchronous and may disable/restore this storage.
            // Never report ownership when a listener invalidated the token.
            return reservation.IsValid;
        }

        public bool IsIncomingReservationValid(OreStorageReservation reservation)
        {
            return isActiveAndEnabled
                   && reservation.Storage == this
                   && reservation.Token != 0
                   && reservation.Token == _activeIncomingReservationToken
                   && _incomingReservations == 1
                   && _storedOre + _incomingReservations <= capacity;
        }

        public bool ReleaseIncoming(OreStorageReservation reservation)
        {
            if (!IsIncomingReservationValid(reservation))
            {
                return false;
            }

            InvalidateIncomingReservation();
            AssertInvariants();
            NotifyStateChanged();
            return true;
        }

        public bool TryDepositReserved(OreStorageReservation reservation)
        {
            if (!IsIncomingReservationValid(reservation) || _storedOre >= capacity)
            {
                return false;
            }

            _storedOre++;
            InvalidateIncomingReservation();
            AssertInvariants();
            NotifyStateChanged();
            OreDeposited?.Invoke(_storedOre);
            return true;
        }

        public bool TryTransferOneTo(CarryStack carryStack)
        {
            if (!isActiveAndEnabled
                || _isPlayerWithdrawalInProgress
                || carryStack == null
                || _storedOre <= 0
                || !carryStack.TryReserveCapacity(ResourceType.IronOre, 1))
            {
                return false;
            }

            _isPlayerWithdrawalInProgress = true;
            bool transferred = false;
            try
            {
                _storedOre--;
                if (!carryStack.TryCommitReservedAdd(ResourceType.IronOre, 1))
                {
                    _storedOre++;
                    bool released = carryStack.ReleaseReservedCapacity(1);
                    Debug.Assert(
                        released,
                        "Ore Storage failed to release a rejected CarryStack reservation.");
                    return false;
                }

                transferred = true;
            }
            finally
            {
                _isPlayerWithdrawalInProgress = false;
                AssertInvariants();
            }

            if (!transferred)
            {
                return false;
            }

            NotifyStateChanged();
            OreWithdrawn?.Invoke(_storedOre);
            return true;
        }

        public bool RestoreStableState(int storedOre)
        {
            if (storedOre < 0 || storedOre > capacity)
            {
                return false;
            }

            InvalidateIncomingReservation();
            _isPlayerWithdrawalInProgress = false;
            _storedOre = storedOre;
            AssertInvariants();
            NotifyStateChanged();
            return true;
        }

        private void InvalidateIncomingReservation()
        {
            _incomingReservations = 0;
            _activeIncomingReservationToken = 0;
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke(_storedOre, _incomingReservations);
        }

        private void AssertInvariants()
        {
            Debug.Assert(_storedOre >= 0, "Ore Storage became negative.");
            Debug.Assert(
                _incomingReservations == 0 || _incomingReservations == 1,
                "Ore Storage has more than one incoming reservation.");
            Debug.Assert(
                _storedOre + _incomingReservations <= capacity,
                "Ore Storage ownership exceeded capacity.");
            Debug.Assert(
                _incomingReservations > 0
                    ? _activeIncomingReservationToken != 0
                    : _activeIncomingReservationToken == 0,
                "Ore Storage reservation token does not match its claim.");
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
        }
    }
}
