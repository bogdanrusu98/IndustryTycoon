using System.Collections;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Logistics;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class CourierUnlockFeedback : MonoBehaviour
    {
        [SerializeField] private FirstCourierUnlock courierUnlock;
        [SerializeField] private Transform courierVisual;
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
            if (courierVisual != null)
            {
                _baseScale = courierVisual.localScale;
            }
        }

        private void OnEnable()
        {
            if (courierUnlock == null)
            {
                return;
            }

            courierUnlock.CourierActivated += HandleCourierActivated;
            if (courierUnlock.IsCourierActivated && !_hasPresented)
            {
                HandleCourierActivated();
            }
        }

        private void OnDisable()
        {
            if (courierUnlock != null)
            {
                courierUnlock.CourierActivated -= HandleCourierActivated;
            }

            if (_unlockRoutine != null)
            {
                StopCoroutine(_unlockRoutine);
                _unlockRoutine = null;
            }

            if (courierVisual != null
                && courierUnlock != null
                && courierUnlock.IsCourierActivated)
            {
                courierVisual.localScale = _baseScale;
            }
        }

        private void HandleCourierActivated()
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
            if (courierVisual == null)
            {
                _unlockRoutine = null;
                yield break;
            }

            courierVisual.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < unlockDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / unlockDuration);
                courierVisual.localScale = _baseScale
                                            * FeedbackTween.EaseOutBack(normalizedTime);
                yield return null;
            }

            courierVisual.localScale = _baseScale;
            _unlockRoutine = null;
        }

        private void OnValidate()
        {
            unlockDuration = Mathf.Max(0.1f, unlockDuration);
        }
    }
}
