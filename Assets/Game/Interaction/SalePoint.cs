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
        {
            CashValue = cashValue;
            RemainingWood = remainingWood;
            StartPosition = startPosition;
            StartRotation = startRotation;
            StartScale = startScale;
        }

        public int CashValue { get; }
        public int RemainingWood { get; }
        public bool BecameEmpty => RemainingWood == 0;
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
                || !cashPile.CanDeposit(woodValue)
                || !carryStack.CanRemove(resourceType, 1))
            {
                return false;
            }

            carryStack.TryGetTopVisualPose(
                resourceType,
                out Vector3 startPosition,
                out Quaternion startRotation,
                out Vector3 startScale);
            if (!carryStack.TryRemove(resourceType, 1))
            {
                return false;
            }

            cashPile.Deposit(woodValue);
            UnitSold?.Invoke(new SaleFeedbackData(
                woodValue,
                carryStack.GetAmount(resourceType),
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
                || cashPile == null
                || carryStack.GetAmount(resourceType) <= 0
                || !cashPile.CanDeposit(woodValue))
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

        private void OnValidate()
        {
            woodValue = Mathf.Max(1, woodValue);
            unloadInterval = Mathf.Max(0.02f, unloadInterval);
        }
    }
}
