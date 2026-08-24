using System.Collections;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class WorkerUnlockFeedback : MonoBehaviour
    {
        [SerializeField] private FirstWorkerUnlock workerUnlock;
        [SerializeField] private Transform workerVisual;
        [SerializeField] private ParticleSystem unlockParticles;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;
        [SerializeField] private SmoothFollowCamera followCamera;
        [SerializeField, Min(0.1f)] private float unlockDuration = 0.65f;

        private Vector3 _workerBaseScale = Vector3.one;
        private Coroutine _unlockRoutine;
        private bool _hasPresented;

        public float UnlockDuration => unlockDuration;
        public bool IsPresenting => _unlockRoutine != null;
        public int PresentationCount { get; private set; }

        private void Awake()
        {
            if (workerVisual != null)
            {
                _workerBaseScale = workerVisual.localScale;
            }
        }

        private void OnEnable()
        {
            if (workerUnlock == null)
            {
                return;
            }

            workerUnlock.WorkerActivated += HandleWorkerActivated;
            if (workerUnlock.IsWorkerActivated && !_hasPresented)
            {
                HandleWorkerActivated();
            }
        }

        private void OnDisable()
        {
            if (workerUnlock != null)
            {
                workerUnlock.WorkerActivated -= HandleWorkerActivated;
            }

            if (_unlockRoutine != null)
            {
                StopCoroutine(_unlockRoutine);
                _unlockRoutine = null;
            }

            if (workerVisual != null && workerUnlock != null && workerUnlock.IsWorkerActivated)
            {
                workerVisual.localScale = _workerBaseScale;
            }
        }

        private void HandleWorkerActivated()
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
            if (workerVisual == null)
            {
                _unlockRoutine = null;
                yield break;
            }

            workerVisual.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < unlockDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / unlockDuration);
                workerVisual.localScale = _workerBaseScale
                                          * FeedbackTween.EaseOutBack(normalizedTime);
                yield return null;
            }

            workerVisual.localScale = _workerBaseScale;
            _unlockRoutine = null;
        }

        private void OnValidate()
        {
            unlockDuration = Mathf.Max(0.1f, unlockDuration);
        }
    }
}
