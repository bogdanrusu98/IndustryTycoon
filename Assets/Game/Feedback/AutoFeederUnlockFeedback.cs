using System.Collections;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Logistics;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class AutoFeederUnlockFeedback : MonoBehaviour
    {
        [SerializeField] private FirstAutoFeederUnlock autoFeederUnlock;
        [SerializeField] private Transform autoFeederVisual;
        [SerializeField] private ParticleSystem unlockParticles;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;
        [SerializeField] private SmoothFollowCamera followCamera;
        [SerializeField, Min(0.1f)] private float unlockDuration = 0.65f;

        private Vector3 _baseScale = Vector3.one;
        private Coroutine _unlockRoutine;
        private bool _hasPresented;

        public float UnlockDuration => unlockDuration;
        public bool IsPresenting => _unlockRoutine != null;
        public int PresentationCount { get; private set; }

        private void Awake()
        {
            if (autoFeederVisual != null)
            {
                _baseScale = autoFeederVisual.localScale;
            }
        }

        private void OnEnable()
        {
            if (autoFeederUnlock == null)
            {
                return;
            }

            autoFeederUnlock.AutoFeederActivated += HandleAutoFeederActivated;
            if (autoFeederUnlock.IsAutoFeederActivated && !_hasPresented)
            {
                HandleAutoFeederActivated();
            }
        }

        private void OnDisable()
        {
            if (autoFeederUnlock != null)
            {
                autoFeederUnlock.AutoFeederActivated -= HandleAutoFeederActivated;
            }

            if (_unlockRoutine != null)
            {
                StopCoroutine(_unlockRoutine);
                _unlockRoutine = null;
            }

            if (autoFeederVisual != null
                && autoFeederUnlock != null
                && autoFeederUnlock.IsAutoFeederActivated)
            {
                autoFeederVisual.localScale = _baseScale;
            }
        }

        private void HandleAutoFeederActivated()
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
            if (autoFeederVisual == null)
            {
                _unlockRoutine = null;
                yield break;
            }

            autoFeederVisual.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < unlockDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / unlockDuration);
                autoFeederVisual.localScale = _baseScale
                                             * FeedbackTween.EaseOutBack(normalizedTime);
                yield return null;
            }

            autoFeederVisual.localScale = _baseScale;
            _unlockRoutine = null;
        }

        private void OnValidate()
        {
            unlockDuration = Mathf.Max(0.1f, unlockDuration);
        }
    }
}
