using System.Collections;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class ProcessorUnlockFeedback : MonoBehaviour
    {
        [SerializeField] private FirstProcessorUnlock processorUnlock;
        [SerializeField] private Transform processorVisual;
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
            if (processorVisual != null)
            {
                _baseScale = processorVisual.localScale;
            }
        }

        private void OnEnable()
        {
            if (processorUnlock == null)
            {
                return;
            }

            processorUnlock.ProcessorActivated += HandleProcessorActivated;
            if (processorUnlock.IsProcessorActivated && !_hasPresented)
            {
                HandleProcessorActivated();
            }
        }

        private void OnDisable()
        {
            if (processorUnlock != null)
            {
                processorUnlock.ProcessorActivated -= HandleProcessorActivated;
            }

            if (_unlockRoutine != null)
            {
                StopCoroutine(_unlockRoutine);
                _unlockRoutine = null;
            }

            if (processorVisual != null && processorUnlock != null && processorUnlock.IsProcessorActivated)
            {
                processorVisual.localScale = _baseScale;
            }
        }

        private void HandleProcessorActivated()
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
            if (processorVisual == null)
            {
                _unlockRoutine = null;
                yield break;
            }

            processorVisual.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < unlockDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / unlockDuration);
                processorVisual.localScale = _baseScale * FeedbackTween.EaseOutBack(normalizedTime);
                yield return null;
            }

            processorVisual.localScale = _baseScale;
            _unlockRoutine = null;
        }

        private void OnValidate()
        {
            unlockDuration = Mathf.Max(0.1f, unlockDuration);
        }
    }
}
