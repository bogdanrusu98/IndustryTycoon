using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Logistics
{
    public enum WarehouseTransferMode
    {
        Deposit = 0,
        Withdraw = 1
    }

    [RequireComponent(typeof(Collider))]
    public sealed class WarehouseTransferZone : MonoBehaviour
    {
        [SerializeField] private CentralWarehouse warehouse;
        [SerializeField] private WarehouseTransferMode mode;
        [SerializeField] private ResourceType withdrawalResource = ResourceType.Plank;
        [SerializeField, Min(0.02f)] private float transferInterval = 0.10f;

        private Coroutine _transferCoroutine;
        private CarryStack _activeCarryStack;

        public CentralWarehouse Warehouse => warehouse;
        public WarehouseTransferMode Mode => mode;
        public ResourceType WithdrawalResource => withdrawalResource;

        private void OnTriggerEnter(Collider other)
        {
            CarryStack stack = other.GetComponentInParent<CarryStack>();
            if (stack == null || _transferCoroutine != null)
            {
                return;
            }

            _activeCarryStack = stack;
            _transferCoroutine = StartCoroutine(TransferRoutine());
        }

        private void OnTriggerExit(Collider other)
        {
            CarryStack stack = other.GetComponentInParent<CarryStack>();
            if (stack == null || stack != _activeCarryStack)
            {
                return;
            }

            StopTransfer();
        }

        private void OnDisable()
        {
            StopTransfer();
        }

        public void SetWithdrawalResource(ResourceType resourceType)
        {
            withdrawalResource = resourceType;
        }

        private IEnumerator TransferRoutine()
        {
            var wait = new WaitForSeconds(transferInterval);
            while (_activeCarryStack != null)
            {
                if (mode == WarehouseTransferMode.Deposit)
                {
                    ResourceType? active = _activeCarryStack.ActiveResourceType;
                    if (active.HasValue)
                    {
                        warehouse?.TryDepositFrom(_activeCarryStack, active.Value);
                    }
                }
                else
                {
                    warehouse?.TryWithdrawTo(_activeCarryStack, withdrawalResource);
                }

                yield return wait;
            }

            _transferCoroutine = null;
        }

        private void StopTransfer()
        {
            if (_transferCoroutine != null)
            {
                StopCoroutine(_transferCoroutine);
                _transferCoroutine = null;
            }

            _activeCarryStack = null;
        }

        private void OnValidate()
        {
            transferInterval = Mathf.Max(0.02f, transferInterval);
            Collider trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }
    }
}
