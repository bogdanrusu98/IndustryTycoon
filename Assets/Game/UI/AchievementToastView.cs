using System.Collections;
using System.Collections.Generic;
using IndustryTycoon.Feedback;
using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class AchievementToastView : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progressionService;
        [SerializeField] private GameObject toastRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform toastTransform;
        [SerializeField] private Text toastText;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;

        [Header("Nonblocking Timing")]
        [SerializeField, Min(0.05f)] private float entranceDuration = 0.20f;
        [SerializeField, Min(0.1f)] private float holdDuration = 0.90f;
        [SerializeField, Min(0.05f)] private float exitDuration = 0.25f;
        [SerializeField, Min(0f)] private float entranceOffset = 54f;

        private readonly Queue<int> _pendingAchievements = new Queue<int>();
        private Coroutine _toastRoutine;
        private Vector2 _baseAnchoredPosition;

        public LumberCampProgressionService ProgressionService => progressionService;
        public GameObject ToastRoot => toastRoot;
        public CanvasGroup CanvasGroup => canvasGroup;
        public Text ToastText => toastText;
        public float EntranceDuration => entranceDuration;
        public float HoldDuration => holdDuration;
        public float ExitDuration => exitDuration;
        public int PresentationCount { get; private set; }
        public int QueuedCount => _pendingAchievements.Count;
        public bool IsPresenting => _toastRoutine != null;

        private void Awake()
        {
            if (toastTransform != null)
            {
                _baseAnchoredPosition = toastTransform.anchoredPosition;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            toastRoot?.SetActive(false);
        }

        private void OnEnable()
        {
            if (progressionService != null)
            {
                progressionService.AchievementUnlocked += HandleAchievementUnlocked;
            }
        }

        private void OnDisable()
        {
            if (progressionService != null)
            {
                progressionService.AchievementUnlocked -= HandleAchievementUnlocked;
            }

            if (_toastRoutine != null)
            {
                StopCoroutine(_toastRoutine);
                _toastRoutine = null;
            }

            _pendingAchievements.Clear();
            if (toastTransform != null)
            {
                toastTransform.anchoredPosition = _baseAnchoredPosition;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            toastRoot?.SetActive(false);
        }

        private void HandleAchievementUnlocked(int achievementIndex)
        {
            if (achievementIndex < 0
                || achievementIndex
                >= LumberCampProgressionCatalog.AchievementCount)
            {
                return;
            }

            _pendingAchievements.Enqueue(achievementIndex);
            if (_toastRoutine == null && isActiveAndEnabled)
            {
                _toastRoutine = StartCoroutine(PresentQueuedToasts());
            }
        }

        private IEnumerator PresentQueuedToasts()
        {
            while (_pendingAchievements.Count > 0)
            {
                int achievementIndex = _pendingAchievements.Dequeue();
                LumberCampAchievementDefinition definition =
                    LumberCampProgressionCatalog.GetAchievement(achievementIndex);
                if (toastText != null)
                {
                    bool rewardGranted = progressionService != null
                                         && progressionService
                                             .IsAchievementRewarded(achievementIndex);
                    toastText.text = "ACHIEVEMENT UNLOCKED\n"
                                     + definition.Name.ToUpperInvariant()
                                     + (rewardGranted
                                         ? $"  +${definition.RewardCash}"
                                         : $"  REWARD PENDING: ${definition.RewardCash}");
                }

                PresentationCount++;
                toastRoot?.SetActive(true);
                audioFeedback?.PlayUnlock();
                hapticFeedback?.PlayImportant();

                yield return Animate(0f, 1f, entranceDuration, true);
                float held = 0f;
                while (held < holdDuration)
                {
                    held += Time.unscaledDeltaTime;
                    yield return null;
                }

                yield return Animate(1f, 0f, exitDuration, false);
                toastRoot?.SetActive(false);
            }

            _toastRoutine = null;
        }

        private IEnumerator Animate(
            float startAlpha,
            float endAlpha,
            float duration,
            bool entering)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = FeedbackTween.EaseOutCubic(normalized);
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, eased);
                }

                if (toastTransform != null)
                {
                    float offset = entering
                        ? Mathf.Lerp(entranceOffset, 0f, eased)
                        : Mathf.Lerp(0f, -entranceOffset, eased);
                    toastTransform.anchoredPosition = _baseAnchoredPosition
                                                      + (Vector2.up * offset);
                }

                yield return null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = endAlpha;
            }

            if (toastTransform != null)
            {
                toastTransform.anchoredPosition = entering
                    ? _baseAnchoredPosition
                    : _baseAnchoredPosition - (Vector2.up * entranceOffset);
            }
        }

        private void OnValidate()
        {
            entranceDuration = Mathf.Max(0.05f, entranceDuration);
            holdDuration = Mathf.Max(0.1f, holdDuration);
            exitDuration = Mathf.Max(0.05f, exitDuration);
            entranceOffset = Mathf.Max(0f, entranceOffset);
        }
    }
}
