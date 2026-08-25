using System.Collections;
using IndustryTycoon.CameraSystem;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class LumberCampCompletionFeedback : MonoBehaviour
    {
        [SerializeField] private LumberCampCompletion completion;
        [SerializeField] private GameObject bannerRoot;
        [SerializeField] private CanvasGroup bannerCanvasGroup;
        [SerializeField] private RectTransform bannerTransform;
        [SerializeField] private ParticleSystem completionParticles;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;
        [SerializeField] private SmoothFollowCamera followCamera;
        [SerializeField, Min(0.05f)] private float entranceDuration = 0.24f;
        [SerializeField, Min(0f)] private float holdDuration = 1.45f;
        [SerializeField, Min(0.05f)] private float exitDuration = 0.30f;

        private Vector3 _baseScale = Vector3.one;
        private Coroutine _presentationRoutine;
        private bool _hasPresented;

        public LumberCampCompletion Completion => completion;
        public GameObject BannerRoot => bannerRoot;
        public float EntranceDuration => entranceDuration;
        public float HoldDuration => holdDuration;
        public float ExitDuration => exitDuration;
        public bool IsPresenting => _presentationRoutine != null;
        public int PresentationCount { get; private set; }

        private void Awake()
        {
            if (bannerTransform != null)
            {
                _baseScale = bannerTransform.localScale;
            }

            SetBannerVisible(false);
        }

        private void OnEnable()
        {
            if (completion == null)
            {
                return;
            }

            completion.Completed += HandleCompleted;
            if (completion.IsCompleted && !_hasPresented)
            {
                HandleCompleted();
            }
        }

        private void OnDisable()
        {
            if (completion != null)
            {
                completion.Completed -= HandleCompleted;
            }

            if (_presentationRoutine != null)
            {
                StopCoroutine(_presentationRoutine);
                _presentationRoutine = null;
            }

            SetBannerVisible(false);
        }

        private void HandleCompleted()
        {
            if (_hasPresented)
            {
                return;
            }

            _hasPresented = true;
            PresentationCount++;
            if (_presentationRoutine != null)
            {
                StopCoroutine(_presentationRoutine);
            }

            _presentationRoutine = StartCoroutine(PresentCompletion());
            completionParticles?.Emit(28);
            audioFeedback?.PlayUnlock();
            hapticFeedback?.PlayImportant();
            followCamera?.TriggerImpulse(0.08f, 0.26f);
        }

        private IEnumerator PresentCompletion()
        {
            SetBannerVisible(true);
            float elapsed = 0f;
            while (elapsed < entranceDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / entranceDuration);
                if (bannerCanvasGroup != null)
                {
                    bannerCanvasGroup.alpha = FeedbackTween.EaseOutCubic(normalizedTime);
                }

                if (bannerTransform != null)
                {
                    float scale = Mathf.LerpUnclamped(
                        0.78f,
                        1f,
                        FeedbackTween.EaseOutBack(normalizedTime));
                    bannerTransform.localScale = _baseScale * scale;
                }

                yield return null;
            }

            if (holdDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(holdDuration);
            }

            elapsed = 0f;
            while (elapsed < exitDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / exitDuration);
                if (bannerCanvasGroup != null)
                {
                    bannerCanvasGroup.alpha = 1f - FeedbackTween.EaseInOutCubic(normalizedTime);
                }

                yield return null;
            }

            SetBannerVisible(false);
            _presentationRoutine = null;
        }

        private void SetBannerVisible(bool visible)
        {
            if (bannerRoot != null)
            {
                bannerRoot.SetActive(visible);
            }

            if (bannerCanvasGroup != null)
            {
                bannerCanvasGroup.alpha = visible ? 1f : 0f;
                bannerCanvasGroup.blocksRaycasts = false;
                bannerCanvasGroup.interactable = false;
            }

            if (bannerTransform != null)
            {
                bannerTransform.localScale = _baseScale;
            }
        }

        private void OnValidate()
        {
            entranceDuration = Mathf.Max(0.05f, entranceDuration);
            holdDuration = Mathf.Max(0f, holdDuration);
            exitDuration = Mathf.Max(0.05f, exitDuration);
        }
    }
}
