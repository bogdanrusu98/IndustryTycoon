using System.Collections;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.ResourceSystem;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class ProductionUnlockFeedback : MonoBehaviour
    {
        [SerializeField] private WoodProductionUpgrade productionUpgrade;
        [SerializeField] private Transform secondCutterVisual;
        [SerializeField] private ParticleSystem unlockParticles;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;
        [SerializeField] private SmoothFollowCamera followCamera;
        [SerializeField, Min(0.1f)] private float unlockDuration = 0.65f;

        private Vector3 _secondCutterBaseScale;
        private Coroutine _unlockRoutine;
        private bool _hasPresented;

        public float UnlockDuration => unlockDuration;
        public bool IsPresenting => _unlockRoutine != null;
        public int PresentationCount { get; private set; }

        private void Awake()
        {
            _secondCutterBaseScale = secondCutterVisual != null
                ? secondCutterVisual.localScale
                : Vector3.one;
        }

        private void OnEnable()
        {
            if (productionUpgrade == null)
            {
                return;
            }

            productionUpgrade.Applied += HandleApplied;
            if (productionUpgrade.IsApplied && !_hasPresented)
            {
                HandleApplied();
            }
        }

        private void OnDisable()
        {
            if (productionUpgrade != null)
            {
                productionUpgrade.Applied -= HandleApplied;
            }

            if (_unlockRoutine != null)
            {
                StopCoroutine(_unlockRoutine);
                _unlockRoutine = null;
            }

            if (secondCutterVisual != null && productionUpgrade != null && productionUpgrade.IsApplied)
            {
                secondCutterVisual.localScale = _secondCutterBaseScale;
            }
        }

        private void HandleApplied()
        {
            if (_hasPresented)
            {
                return;
            }

            _hasPresented = true;
            PresentationCount++;
            if (_unlockRoutine != null)
            {
                StopCoroutine(_unlockRoutine);
            }

            _unlockRoutine = StartCoroutine(AnimateUnlock());
            unlockParticles?.Emit(20);
            audioFeedback?.PlayUnlock();
            hapticFeedback?.PlayImportant();
            followCamera?.TriggerImpulse();
        }

        private IEnumerator AnimateUnlock()
        {
            if (secondCutterVisual == null)
            {
                _unlockRoutine = null;
                yield break;
            }

            secondCutterVisual.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < unlockDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / unlockDuration);
                float scale = FeedbackTween.EaseOutBack(normalizedTime);
                secondCutterVisual.localScale = _secondCutterBaseScale * scale;
                yield return null;
            }

            secondCutterVisual.localScale = _secondCutterBaseScale;
            _unlockRoutine = null;
        }

        private void OnValidate()
        {
            unlockDuration = Mathf.Max(0.1f, unlockDuration);
        }
    }
}
