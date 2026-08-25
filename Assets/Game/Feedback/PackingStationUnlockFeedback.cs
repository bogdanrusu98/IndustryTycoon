using System.Collections;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class PackingStationUnlockFeedback : MonoBehaviour
    {
        [SerializeField] private FirstPackingStationUnlock packingStationUnlock;
        [SerializeField] private Transform packingStationVisual;
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
            if (packingStationVisual != null)
            {
                _baseScale = packingStationVisual.localScale;
            }
        }

        private void OnEnable()
        {
            if (packingStationUnlock == null)
            {
                return;
            }

            packingStationUnlock.PackingStationActivated += HandlePackingStationActivated;
            if (packingStationUnlock.IsPackingStationActivated && !_hasPresented)
            {
                HandlePackingStationActivated();
            }
        }

        private void OnDisable()
        {
            if (packingStationUnlock != null)
            {
                packingStationUnlock.PackingStationActivated -= HandlePackingStationActivated;
            }

            if (_unlockRoutine != null)
            {
                StopCoroutine(_unlockRoutine);
                _unlockRoutine = null;
            }

            if (packingStationVisual != null
                && packingStationUnlock != null
                && packingStationUnlock.IsPackingStationActivated)
            {
                packingStationVisual.localScale = _baseScale;
            }
        }

        private void HandlePackingStationActivated()
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
            if (packingStationVisual == null)
            {
                _unlockRoutine = null;
                yield break;
            }

            packingStationVisual.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < unlockDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / unlockDuration);
                packingStationVisual.localScale = _baseScale
                                                  * FeedbackTween.EaseOutBack(normalizedTime);
                yield return null;
            }

            packingStationVisual.localScale = _baseScale;
            _unlockRoutine = null;
        }

        private void OnValidate()
        {
            unlockDuration = Mathf.Max(0.1f, unlockDuration);
        }
    }
}
