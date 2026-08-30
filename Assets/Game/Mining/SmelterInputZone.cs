using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Mining
{
    [RequireComponent(typeof(Collider))]
    public sealed class SmelterInputZone : MonoBehaviour
    {
        [SerializeField] private Smelter smelter;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Collider playerCollider;
        [SerializeField, Min(0.02f)] private float transferInterval = 0.10f;

        private WaitForSeconds _transferWait;
        private Coroutine _transferCoroutine;
        private bool _isStartingTransfer;
        private bool _isPlayerInside;

        public Smelter Smelter => smelter;
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
            if (smelter != null)
            {
                smelter.BufferChanged += HandleBufferChanged;
            }

            if (carryStack != null)
            {
                carryStack.Changed += HandleCarryChanged;
            }
        }

        private void OnDisable()
        {
            if (smelter != null)
            {
                smelter.BufferChanged -= HandleBufferChanged;
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
                   && smelter != null
                   && carryStack != null
                   && smelter.TryTransferInputFrom(carryStack);
        }

        private void TryStartTransfer()
        {
            if (!isActiveAndEnabled
                || _transferCoroutine != null
                || _isStartingTransfer
                || !_isPlayerInside
                || smelter == null
                || carryStack == null
                || !smelter.isActiveAndEnabled
                || smelter.AvailableInputCapacity <= 0
                || !carryStack.CanRemove(ResourceType.IronOre, 1))
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
            while (_isPlayerInside && smelter != null && carryStack != null)
            {
                if (!smelter.TryTransferInputFrom(carryStack))
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
            int inputOre,
            int processingInputOre,
            int outputBars,
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
