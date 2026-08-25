using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Processing
{
    [RequireComponent(typeof(Collider))]
    public sealed class PackingStationInputZone : MonoBehaviour
    {
        [SerializeField] private PackingStation packingStation;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Collider playerCollider;
        [SerializeField, Min(0.02f)] private float transferInterval = 0.10f;

        private WaitForSeconds _transferWait;
        private Coroutine _transferCoroutine;
        private bool _isStartingTransfer;
        private bool _isPlayerInside;

        public PackingStation PackingStation => packingStation;
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
            if (packingStation != null)
            {
                packingStation.BufferChanged += HandleBufferChanged;
            }

            if (carryStack != null)
            {
                carryStack.Changed += HandleCarryChanged;
            }
        }

        private void OnDisable()
        {
            if (packingStation != null)
            {
                packingStation.BufferChanged -= HandleBufferChanged;
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
            if (!isActiveAndEnabled || other != playerCollider)
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
            return isActiveAndEnabled
                   && _isPlayerInside
                   && packingStation != null
                   && carryStack != null
                   && packingStation.TryTransferInputFrom(carryStack);
        }

        private void TryStartTransfer()
        {
            if (!isActiveAndEnabled
                || _transferCoroutine != null
                || _isStartingTransfer
                || !_isPlayerInside
                || packingStation == null
                || carryStack == null)
            {
                return;
            }

            if (!packingStation.isActiveAndEnabled
                || packingStation.AvailableInputCapacity <= 0
                || !carryStack.CanRemove(ResourceType.Plank, 1))
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
            while (_isPlayerInside && packingStation != null && carryStack != null)
            {
                if (!packingStation.TryTransferInputFrom(carryStack))
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

        private void HandleBufferChanged(
            int inputPlanks,
            int processingInputPlanks,
            int outputCrates,
            int reservedOutputCapacity)
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
