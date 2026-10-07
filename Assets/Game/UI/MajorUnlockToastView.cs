using System.Collections;
using System.Collections.Generic;
using IndustryTycoon.Feedback;
using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class MajorUnlockToastView : MonoBehaviour
    {
        [SerializeField] private LumberCampProgressionService progression;
        [SerializeField] private GameObject toastRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text toastText;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;
        [SerializeField, Min(0.1f)] private float holdDuration = 1.1f;

        private readonly Queue<string> _messages = new Queue<string>();
        private Coroutine _routine;

        public int PresentationCount { get; private set; }

        private void Awake()
        {
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
            if (progression != null) progression.EmpireLevelAdvanced += HandleLevelAdvanced;
        }

        private void OnDisable()
        {
            if (progression != null) progression.EmpireLevelAdvanced -= HandleLevelAdvanced;
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            _messages.Clear();
            toastRoot?.SetActive(false);
        }

        public static string GetMajorUnlockMessage(int level)
        {
            switch (level)
            {
                case 2: return "MINING AREA UNLOCKED";
                case 5: return "CENTRAL LOGISTICS DISCOVERED";
                case 10: return "INDUSTRIAL DISTRICT OPEN";
                case 12: return "ASSEMBLY PLANT AVAILABLE";
                case 16: return "FACTORY SPECIALIZATION UNLOCKED";
                case 21: return "MARKET & PRODUCTION ORDERS OPEN";
                case 25: return "EMPIRE LEVEL 25 COMPLETE";
                default: return string.Empty;
            }
        }

        private void HandleLevelAdvanced(int level)
        {
            string message = GetMajorUnlockMessage(level);
            if (string.IsNullOrEmpty(message)) return;
            _messages.Enqueue(message);
            if (_routine == null && isActiveAndEnabled) _routine = StartCoroutine(Present());
        }

        private IEnumerator Present()
        {
            while (_messages.Count > 0)
            {
                if (toastText != null) toastText.text = _messages.Dequeue();
                toastRoot?.SetActive(true);
                if (canvasGroup != null) canvasGroup.alpha = 1f;
                PresentationCount++;
                audioFeedback?.PlayUnlock();
                hapticFeedback?.PlayImportant();
                yield return new WaitForSecondsRealtime(holdDuration);
                if (canvasGroup != null) canvasGroup.alpha = 0f;
                toastRoot?.SetActive(false);
            }
            _routine = null;
        }
    }
}
