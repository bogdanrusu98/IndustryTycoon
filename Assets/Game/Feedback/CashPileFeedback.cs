using System.Collections.Generic;
using IndustryTycoon.Economy;
using IndustryTycoon.Interaction;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class CashPileFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CashPile cashPile;
        [SerializeField] private CashPileCollector cashCollector;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform collectionPopTarget;
        [SerializeField] private ParticleSystem growthParticles;
        [SerializeField] private ParticleSystem collectionParticles;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;

        [Header("Feel")]
        [SerializeField, Min(0.05f)] private float bundlePopDuration = 0.16f;
        [SerializeField, Range(1f, 1.3f)] private float cappedValueScale = 1.14f;
        [SerializeField, Min(0.05f)] private float collectionPopDuration = 0.18f;

        private readonly List<Transform> _bundleVisuals = new List<Transform>();
        private readonly List<Vector3> _bundleBaseScales = new List<Vector3>();
        private readonly List<float> _bundlePopElapsed = new List<float>();
        private Vector3 _visualRootBaseScale;
        private Vector3 _collectionTargetBaseScale;
        private float _rootPulseElapsed = -1f;
        private float _collectionPopElapsed = -1f;
        private int _previousStoredCash;
        private bool _isAnimating;

        public float BundlePopDuration => bundlePopDuration;
        public int CachedBundleCount => _bundleVisuals.Count;
        public int CollectionFeedbackCount { get; private set; }
        public bool IsAnimating => _isAnimating;

        private void Awake()
        {
            _visualRootBaseScale = visualRoot != null ? visualRoot.localScale : Vector3.one;
            _collectionTargetBaseScale = collectionPopTarget != null
                ? collectionPopTarget.localScale
                : Vector3.one;
            CacheBundleVisuals();
        }

        private void OnEnable()
        {
            _previousStoredCash = cashPile != null ? cashPile.StoredCash : 0;
            if (cashPile != null)
            {
                cashPile.StoredCashChanged += HandleStoredCashChanged;
            }

            if (cashCollector != null)
            {
                cashCollector.CollectionCompleted += HandleCollectionCompleted;
            }
        }

        private void OnDisable()
        {
            if (cashPile != null)
            {
                cashPile.StoredCashChanged -= HandleStoredCashChanged;
            }

            if (cashCollector != null)
            {
                cashCollector.CollectionCompleted -= HandleCollectionCompleted;
            }

            ResetPresentation();
        }

        private void Update()
        {
            if (!_isAnimating)
            {
                return;
            }

            _isAnimating = false;
            UpdateBundlePops();
            UpdateRootPulse();
            UpdateCollectionPop();
        }

        private void HandleStoredCashChanged(int storedCash)
        {
            CacheBundleVisuals();
            int previousVisibleCount = GetVisibleCount(_previousStoredCash);
            int currentVisibleCount = GetVisibleCount(storedCash);

            if (storedCash > _previousStoredCash)
            {
                for (int i = previousVisibleCount; i < currentVisibleCount && i < _bundleVisuals.Count; i++)
                {
                    _bundlePopElapsed[i] = 0f;
                    _bundleVisuals[i].localScale = _bundleBaseScales[i] * 0.42f;
                }

                _rootPulseElapsed = 0f;
                growthParticles?.Emit(currentVisibleCount > previousVisibleCount ? 3 : 2);
                _isAnimating = true;
            }
            else if (storedCash <= 0)
            {
                ResetBundleScales();
                if (visualRoot != null)
                {
                    visualRoot.localScale = _visualRootBaseScale;
                }
            }

            _previousStoredCash = storedCash;
        }

        private void HandleCollectionCompleted(int collectedCash)
        {
            if (collectedCash <= 0)
            {
                return;
            }

            CollectionFeedbackCount++;
            _collectionPopElapsed = 0f;
            collectionParticles?.Emit(12);
            audioFeedback?.PlayCashCollect();
            hapticFeedback?.PlayLight();
            _isAnimating = true;
        }

        private void CacheBundleVisuals()
        {
            if (visualRoot == null)
            {
                return;
            }

            while (_bundleVisuals.Count < visualRoot.childCount)
            {
                Transform visual = visualRoot.GetChild(_bundleVisuals.Count);
                _bundleVisuals.Add(visual);
                _bundleBaseScales.Add(visual.localScale);
                _bundlePopElapsed.Add(-1f);
            }
        }

        private int GetVisibleCount(int storedCash)
        {
            if (cashPile == null || storedCash <= 0)
            {
                return 0;
            }

            long required = ((long)storedCash + cashPile.CashPerVisual - 1L)
                            / cashPile.CashPerVisual;
            return (int)System.Math.Min(cashPile.MaximumVisualItems, required);
        }

        private void UpdateBundlePops()
        {
            for (int i = 0; i < _bundleVisuals.Count; i++)
            {
                if (_bundlePopElapsed[i] < 0f)
                {
                    continue;
                }

                float elapsed = Mathf.Min(bundlePopDuration, _bundlePopElapsed[i] + Time.deltaTime);
                _bundlePopElapsed[i] = elapsed;
                float normalizedTime = Mathf.Clamp01(elapsed / bundlePopDuration);
                _bundleVisuals[i].localScale = _bundleBaseScales[i]
                                                * Mathf.LerpUnclamped(
                                                    0.42f,
                                                    1f,
                                                    FeedbackTween.EaseOutBack(normalizedTime));
                if (normalizedTime >= 1f)
                {
                    _bundleVisuals[i].localScale = _bundleBaseScales[i];
                    _bundlePopElapsed[i] = -1f;
                }
                else
                {
                    _isAnimating = true;
                }
            }
        }

        private void UpdateRootPulse()
        {
            if (_rootPulseElapsed < 0f || visualRoot == null)
            {
                return;
            }

            _rootPulseElapsed = Mathf.Min(bundlePopDuration, _rootPulseElapsed + Time.deltaTime);
            float normalizedTime = Mathf.Clamp01(_rootPulseElapsed / bundlePopDuration);
            int visualCapacityCash = cashPile != null
                ? cashPile.MaximumVisualItems * cashPile.CashPerVisual
                : 1;
            float excessRatio = visualCapacityCash > 0
                ? Mathf.Clamp01((_previousStoredCash - visualCapacityCash) / (float)visualCapacityCash)
                : 0f;
            float valueScale = Mathf.Lerp(1f, cappedValueScale, excessRatio);
            float pulse = Mathf.Sin(normalizedTime * Mathf.PI) * 0.08f;
            visualRoot.localScale = _visualRootBaseScale * (valueScale + pulse);
            if (normalizedTime >= 1f)
            {
                visualRoot.localScale = _visualRootBaseScale * valueScale;
                _rootPulseElapsed = -1f;
            }
            else
            {
                _isAnimating = true;
            }
        }

        private void UpdateCollectionPop()
        {
            if (_collectionPopElapsed < 0f || collectionPopTarget == null)
            {
                return;
            }

            _collectionPopElapsed = Mathf.Min(
                collectionPopDuration,
                _collectionPopElapsed + Time.deltaTime);
            float normalizedTime = Mathf.Clamp01(_collectionPopElapsed / collectionPopDuration);
            float pulse = Mathf.Sin(normalizedTime * Mathf.PI) * 0.08f;
            collectionPopTarget.localScale = _collectionTargetBaseScale * (1f + pulse);
            if (normalizedTime >= 1f)
            {
                collectionPopTarget.localScale = _collectionTargetBaseScale;
                _collectionPopElapsed = -1f;
            }
            else
            {
                _isAnimating = true;
            }
        }

        private void ResetBundleScales()
        {
            for (int i = 0; i < _bundleVisuals.Count; i++)
            {
                _bundleVisuals[i].localScale = _bundleBaseScales[i];
                _bundlePopElapsed[i] = -1f;
            }
        }

        private void ResetPresentation()
        {
            _isAnimating = false;
            _rootPulseElapsed = -1f;
            _collectionPopElapsed = -1f;
            ResetBundleScales();
            if (visualRoot != null)
            {
                visualRoot.localScale = _visualRootBaseScale;
            }

            if (collectionPopTarget != null)
            {
                collectionPopTarget.localScale = _collectionTargetBaseScale;
            }
        }

        private void OnValidate()
        {
            bundlePopDuration = Mathf.Max(0.05f, bundlePopDuration);
            cappedValueScale = Mathf.Clamp(cappedValueScale, 1f, 1.3f);
            collectionPopDuration = Mathf.Max(0.05f, collectionPopDuration);
        }
    }
}
