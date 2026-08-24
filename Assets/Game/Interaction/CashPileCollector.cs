using System.Collections;
using System.Collections.Generic;
using System;
using IndustryTycoon.Economy;
using UnityEngine;

namespace IndustryTycoon.Interaction
{
    [RequireComponent(typeof(Collider))]
    public sealed class CashPileCollector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CashPile cashPile;
        [SerializeField] private Wallet wallet;
        [SerializeField] private Collider playerCollider;
        [SerializeField] private Transform flightOrigin;
        [SerializeField] private Transform flightTarget;
        [SerializeField] private GameObject flightVisualPrefab;

        [Header("Flight")]
        [SerializeField, Min(1)] private int maximumFlightVisuals = 6;
        [SerializeField, Min(0.05f)] private float flightDuration = 0.45f;
        [SerializeField, Min(0f)] private float flightStagger = 0.04f;
        [SerializeField, Min(0f)] private float arcHeight = 1.25f;

        private readonly List<GameObject> _flightVisuals = new List<GameObject>();
        private readonly List<Vector3> _flightStarts = new List<Vector3>();
        private readonly List<Vector3> _flightBaseScales = new List<Vector3>();
        private Coroutine _collectionCoroutine;
        private int _pendingCash;
        private int _activeVisualCount;
        private bool _isPlayerInside;

        public event Action<int> CollectionCompleted;

        public CashPile CashPile => cashPile;
        public Wallet Wallet => wallet;
        public Collider PlayerCollider => playerCollider;
        public int PendingCash => _pendingCash;
        public int MaximumFlightVisuals => maximumFlightVisuals;
        public float FlightDuration => flightDuration;
        public float FlightStagger => flightStagger;
        public float ArcHeight => arcHeight;
        public bool IsCollecting => _collectionCoroutine != null;
        public int ActiveVisualCount => _activeVisualCount;
        public int FlightVisualPoolCount => _flightVisuals.Count;

        private void Awake()
        {
            EnsureFlightVisualPool();
        }

        private void OnEnable()
        {
            if (cashPile != null)
            {
                cashPile.StoredCashChanged += HandleStoredCashChanged;
            }
        }

        private void OnDisable()
        {
            if (cashPile != null)
            {
                cashPile.StoredCashChanged -= HandleStoredCashChanged;
            }

            _isPlayerInside = false;
            if (_collectionCoroutine != null)
            {
                StopCoroutine(_collectionCoroutine);
                _collectionCoroutine = null;
            }

            RestorePendingCash();
            HideFlightVisuals();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != playerCollider)
            {
                return;
            }

            _isPlayerInside = true;
            TryStartCollection();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == playerCollider)
            {
                _isPlayerInside = false;
            }
        }

        public bool TryStartCollection()
        {
            if (_collectionCoroutine != null
                || _pendingCash > 0
                || cashPile == null
                || wallet == null
                || wallet.Balance >= int.MaxValue
                || cashPile.StoredCash <= 0)
            {
                return false;
            }

            _collectionCoroutine = StartCoroutine(CollectionRoutine());
            return true;
        }

        private IEnumerator CollectionRoutine()
        {
            if (!cashPile.TryWithdrawAll(out _pendingCash))
            {
                _collectionCoroutine = null;
                yield break;
            }

            PrepareFlightVisuals();
            float elapsed = 0f;
            float totalFlightDuration = flightDuration
                                        + (Mathf.Max(0, _activeVisualCount - 1) * flightStagger);
            while (elapsed < totalFlightDuration)
            {
                elapsed += Time.deltaTime;
                UpdateFlightVisuals(elapsed);
                yield return null;
            }

            UpdateFlightVisuals(totalFlightDuration);

            int cashToDeposit = _pendingCash;
            HideFlightVisuals();

            int depositedCash = wallet.Deposit(cashToDeposit);
            int remainder = cashToDeposit - depositedCash;
            if (remainder > 0 && cashPile != null)
            {
                remainder -= cashPile.Deposit(remainder);
            }

            _pendingCash = remainder;
            if (depositedCash > 0)
            {
                CollectionCompleted?.Invoke(depositedCash);
            }

            _collectionCoroutine = null;
            if (_pendingCash == 0
                && _isPlayerInside
                && wallet.Balance < int.MaxValue
                && cashPile != null
                && cashPile.StoredCash > 0)
            {
                TryStartCollection();
            }
        }

        private void HandleStoredCashChanged(int storedCash)
        {
            if (_isPlayerInside && storedCash > 0)
            {
                TryStartCollection();
            }
        }

        private void EnsureFlightVisualPool()
        {
            if (flightVisualPrefab == null)
            {
                return;
            }

            while (_flightVisuals.Count < maximumFlightVisuals)
            {
                GameObject visual = Instantiate(flightVisualPrefab, transform);
                visual.name = $"Flying Cash {_flightVisuals.Count + 1:00}";
                visual.SetActive(false);
                _flightVisuals.Add(visual);
                _flightBaseScales.Add(visual.transform.localScale);
            }
        }

        private void PrepareFlightVisuals()
        {
            EnsureFlightVisualPool();
            int cashPerItem = cashPile != null ? cashPile.CashPerVisual : 1;
            long requiredVisuals = ((long)_pendingCash + cashPerItem - 1L) / cashPerItem;
            int pooledVisualCount = Mathf.Min(maximumFlightVisuals, _flightVisuals.Count);
            _activeVisualCount = (int)System.Math.Min(pooledVisualCount, requiredVisuals);
            _flightStarts.Clear();

            Vector3 origin = flightOrigin != null
                ? flightOrigin.position
                : (cashPile != null ? cashPile.transform.position : transform.position);
            for (int i = 0; i < _flightVisuals.Count; i++)
            {
                GameObject visual = _flightVisuals[i];
                bool isActive = i < _activeVisualCount;
                visual.SetActive(isActive);
                if (!isActive)
                {
                    continue;
                }

                Vector3 offset = new Vector3(
                    ((i % 3) - 1) * 0.18f,
                    (i / 3) * 0.08f,
                    (i % 2 == 0 ? -1f : 1f) * 0.12f);
                Vector3 startPosition = origin + offset;
                visual.transform.position = startPosition;
                visual.transform.rotation = Quaternion.identity;
                visual.transform.localScale = _flightBaseScales[i] * 0.72f;
                _flightStarts.Add(startPosition);
            }
        }

        private void UpdateFlightVisuals(float elapsed)
        {
            Vector3 target = flightTarget != null
                ? flightTarget.position
                : (wallet != null ? wallet.transform.position + Vector3.up : transform.position);

            for (int i = 0; i < _activeVisualCount; i++)
            {
                float normalizedTime = Mathf.Clamp01((elapsed - (i * flightStagger)) / flightDuration);
                float easedTime = 1f - Mathf.Pow(1f - normalizedTime, 3f);
                float verticalArc = Mathf.Sin(normalizedTime * Mathf.PI) * arcHeight;
                GameObject visual = _flightVisuals[i];
                visual.transform.position = Vector3.Lerp(_flightStarts[i], target, easedTime)
                                            + (Vector3.up * verticalArc);
                visual.transform.rotation = Quaternion.AngleAxis(
                    (normalizedTime * 420f) + (i * 18f),
                    Vector3.up);

                float scaleMultiplier;
                if (normalizedTime < 0.72f)
                {
                    float riseTime = normalizedTime / 0.72f;
                    scaleMultiplier = Mathf.Lerp(
                        0.72f,
                        1.08f,
                        Mathf.SmoothStep(0f, 1f, riseTime));
                }
                else
                {
                    float settleTime = (normalizedTime - 0.72f) / 0.28f;
                    scaleMultiplier = Mathf.Lerp(
                        1.08f,
                        0.45f,
                        Mathf.SmoothStep(0f, 1f, settleTime));
                }

                visual.transform.localScale = _flightBaseScales[i] * scaleMultiplier;
            }
        }

        private void RestorePendingCash()
        {
            if (_pendingCash <= 0)
            {
                return;
            }

            int pendingCash = _pendingCash;
            if (cashPile != null)
            {
                pendingCash -= cashPile.Deposit(pendingCash);
            }

            if (pendingCash > 0 && wallet != null)
            {
                pendingCash -= wallet.Deposit(pendingCash);
            }

            _pendingCash = pendingCash;
        }

        private void HideFlightVisuals()
        {
            for (int i = 0; i < _flightVisuals.Count; i++)
            {
                _flightVisuals[i].transform.localScale = _flightBaseScales[i];
                _flightVisuals[i].SetActive(false);
            }

            _flightStarts.Clear();
            _activeVisualCount = 0;
        }

        private void OnValidate()
        {
            maximumFlightVisuals = Mathf.Max(1, maximumFlightVisuals);
            flightDuration = Mathf.Max(0.05f, flightDuration);
            flightStagger = Mathf.Max(0f, flightStagger);
            arcHeight = Mathf.Max(0f, arcHeight);
        }
    }
}
