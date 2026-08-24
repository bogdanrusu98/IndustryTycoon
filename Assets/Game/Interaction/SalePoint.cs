using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Economy;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Interaction
{
    public readonly struct SaleFeedbackData
    {
        public SaleFeedbackData(
            int cashValue,
            int remainingWood,
            Vector3 startPosition,
            Quaternion startRotation,
            Vector3 startScale)
            : this(
                ResourceType.Wood,
                cashValue,
                remainingWood,
                startPosition,
                startRotation,
                startScale)
        {
        }

        public SaleFeedbackData(
            ResourceType resourceType,
            int cashValue,
            int remainingAmount,
            Vector3 startPosition,
            Quaternion startRotation,
            Vector3 startScale)
        {
            ResourceType = resourceType;
            CashValue = cashValue;
            RemainingAmount = remainingAmount;
            StartPosition = startPosition;
            StartRotation = startRotation;
            StartScale = startScale;
        }

        public ResourceType ResourceType { get; }
        public int CashValue { get; }
        public int RemainingAmount { get; }
        public int RemainingWood => RemainingAmount;
        public bool BecameEmpty => RemainingAmount == 0;
        public Vector3 StartPosition { get; }
        public Quaternion StartRotation { get; }
        public Vector3 StartScale { get; }
    }

    [RequireComponent(typeof(Collider))]
    public sealed class SalePoint : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private CashPile cashPile;
        [SerializeField] private Collider playerCollider;

        [Header("Sale")]
        [SerializeField] private ResourceType resourceType = ResourceType.Wood;
        [SerializeField, Min(1)] private int woodValue = 5;
        [SerializeField, Min(1)] private int plankValue = 15;
        [SerializeField, Min(0.02f)] private float unloadInterval = 0.2f;

        private WaitForSeconds _unloadWait;
        private Coroutine _unloadCoroutine;
        private bool _isStartingUnload;
        private bool _isPlayerInside;

        public event System.Action<SaleFeedbackData> UnitSold;

        public CarryStack CarryStack => carryStack;
        public CashPile CashPile => cashPile;
        public Collider PlayerCollider => playerCollider;
        public ResourceType ResourceType => resourceType;
        public int WoodValue => woodValue;
        public int PlankValue => plankValue;
        public float UnloadInterval => unloadInterval;
        public bool IsPlayerInside => _isPlayerInside;

        private void Awake()
        {
            RebuildUnloadWait();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != playerCollider || _isPlayerInside)
            {
                return;
            }

            _isPlayerInside = true;
            if (carryStack != null)
            {
                carryStack.Changed += HandleCarryStackChanged;
            }

            TryStartUnloading();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other != playerCollider)
            {
                return;
            }

            LeaveSalePoint();
        }

        private void OnDisable()
        {
            LeaveSalePoint();
        }

        public bool TryUnloadOne()
        {
            if (carryStack == null
                || cashPile == null
                || !TryResolveCurrentSale(out ResourceType soldResourceType, out int unitValue)
                || !cashPile.CanDeposit(unitValue))
            {
                return false;
            }

            carryStack.TryGetTopVisualPose(
                soldResourceType,
                out Vector3 startPosition,
                out Quaternion startRotation,
                out Vector3 startScale);
            if (!carryStack.TryRemove(soldResourceType, 1))
            {
                return false;
            }

            cashPile.Deposit(unitValue);
            UnitSold?.Invoke(new SaleFeedbackData(
                soldResourceType,
                unitValue,
                carryStack.GetAmount(soldResourceType),
                startPosition,
                startRotation,
                startScale));
            return true;
        }

        private void HandleCarryStackChanged()
        {
            if (_isPlayerInside)
            {
                TryStartUnloading();
            }
        }

        private void TryStartUnloading()
        {
            if (_unloadCoroutine != null
                || _isStartingUnload
                || !_isPlayerInside
                || carryStack == null
                || cashPile == null)
            {
                return;
            }

            if (!TryResolveCurrentSale(out _, out int unitValue)
                || !cashPile.CanDeposit(unitValue))
            {
                return;
            }

            _isStartingUnload = true;
            try
            {
                _unloadCoroutine = StartCoroutine(UnloadRoutine());
            }
            finally
            {
                _isStartingUnload = false;
            }
        }

        private IEnumerator UnloadRoutine()
        {
            while (_isPlayerInside && TryUnloadOne())
            {
                yield return _unloadWait;
            }

            _unloadCoroutine = null;
        }

        private void LeaveSalePoint()
        {
            if (carryStack != null)
            {
                carryStack.Changed -= HandleCarryStackChanged;
            }

            _isPlayerInside = false;
            _isStartingUnload = false;
            if (_unloadCoroutine == null)
            {
                return;
            }

            StopCoroutine(_unloadCoroutine);
            _unloadCoroutine = null;
        }

        private void RebuildUnloadWait()
        {
            _unloadWait = new WaitForSeconds(unloadInterval);
        }

        public int GetUnitValue(ResourceType carriedResourceType)
        {
            switch (carriedResourceType)
            {
                case ResourceType.Wood:
                    return woodValue;
                case ResourceType.Plank:
                    return plankValue;
                default:
                    return 0;
            }
        }

        private bool TryResolveCurrentSale(
            out ResourceType carriedResourceType,
            out int unitValue)
        {
            carriedResourceType = resourceType;
            unitValue = 0;
            if (carryStack == null
                || !carryStack.TryGetActiveResourceType(out carriedResourceType)
                || !carryStack.CanRemove(carriedResourceType, 1))
            {
                return false;
            }

            unitValue = GetUnitValue(carriedResourceType);
            return unitValue > 0;
        }

        private void OnValidate()
        {
            woodValue = Mathf.Max(1, woodValue);
            plankValue = Mathf.Max(1, plankValue);
            unloadInterval = Mathf.Max(0.02f, unloadInterval);
        }
    }
}
