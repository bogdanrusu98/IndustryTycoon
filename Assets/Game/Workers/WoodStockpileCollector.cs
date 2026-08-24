using System.Collections;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Workers
{
    [RequireComponent(typeof(Collider))]
    public sealed class WoodStockpileCollector : MonoBehaviour
    {
        [SerializeField] private WoodStockpile stockpile;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Collider playerCollider;
        [SerializeField, Min(0.02f)] private float transferInterval = 0.10f;

        private WaitForSeconds _transferWait;
        private Coroutine _transferCoroutine;
        private bool _isStartingTransfer;
        private bool _isPlayerInside;

        public WoodStockpile Stockpile => stockpile;
        public CarryStack CarryStack => carryStack;
        public Collider PlayerCollider => playerCollider;
        public float TransferInterval => transferInterval;
        public bool IsPlayerInside => _isPlayerInside;
        public bool IsTransferring => _transferCoroutine != null;

        private void Awake()
        {
            RebuildTransferWait();
        }

        private void OnEnable()
        {
            if (stockpile != null)
            {
                stockpile.StateChanged += HandleStockpileChanged;
            }

            if (carryStack != null)
            {
                carryStack.Changed += HandleCarryChanged;
            }
        }

        private void OnDisable()
        {
            if (stockpile != null)
            {
                stockpile.StateChanged -= HandleStockpileChanged;
            }

            if (carryStack != null)
            {
                carryStack.Changed -= HandleCarryChanged;
            }

            _isPlayerInside = false;
            _isStartingTransfer = false;
            StopTransfer();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != playerCollider)
            {
                return;
            }

            _isPlayerInside = true;
            TryStartTransfer();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other != playerCollider)
            {
                return;
            }

            _isPlayerInside = false;
            StopTransfer();
        }

        public bool TryTransferOne()
        {
            return _isPlayerInside
                   && stockpile != null
                   && carryStack != null
                   && stockpile.TryTransferOneTo(carryStack);
        }

        private void TryStartTransfer()
        {
            if (_transferCoroutine != null
                || _isStartingTransfer
                || !_isPlayerInside
                || stockpile == null
                || carryStack == null
                || stockpile.StoredWood <= 0
                || carryStack.AvailableCapacity <= 0)
            {
                return;
            }

            _isStartingTransfer = true;
            try
            {
                _transferCoroutine = StartCoroutine(TransferRoutine());
            }
            finally
            {
                _isStartingTransfer = false;
            }
        }

        private IEnumerator TransferRoutine()
        {
            while (_isPlayerInside && stockpile != null && carryStack != null)
            {
                if (!stockpile.TryTransferOneTo(carryStack))
                {
                    break;
                }

                yield return _transferWait;
            }

            _transferCoroutine = null;
        }

        private void StopTransfer()
        {
            if (_transferCoroutine == null)
            {
                return;
            }

            StopCoroutine(_transferCoroutine);
            _transferCoroutine = null;
        }

        private void HandleStockpileChanged(int storedWood, int incomingReservations)
        {
            TryStartTransfer();
        }

        private void HandleCarryChanged()
        {
            TryStartTransfer();
        }

        private void RebuildTransferWait()
        {
            _transferWait = new WaitForSeconds(transferInterval);
        }

        private void OnValidate()
        {
            transferInterval = Mathf.Max(0.02f, transferInterval);
        }
    }
}
