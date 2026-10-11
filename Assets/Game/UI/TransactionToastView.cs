using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Interaction;
using IndustryTycoon.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>Compact, non-blocking pickup, drop and sale confirmation.</summary>
    public sealed class TransactionToastView : MonoBehaviour
    {
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private SalePoint salePoint;
        [SerializeField] private GameObject toastRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text messageText;
        [SerializeField] private ResourceHudIcon resourceIcon;
        [SerializeField, Min(0.25f)] private float holdSeconds = 0.8f;
        private Coroutine _routine;

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
            if (carryStack != null)
            {
                carryStack.ItemsAdded += HandleAdded;
                carryStack.ItemsRemoved += HandleRemoved;
            }
            if (salePoint != null) salePoint.UnitSold += HandleSale;
        }

        private void OnDisable()
        {
            if (carryStack != null)
            {
                carryStack.ItemsAdded -= HandleAdded;
                carryStack.ItemsRemoved -= HandleRemoved;
            }
            if (salePoint != null) salePoint.UnitSold -= HandleSale;
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            toastRoot?.SetActive(false);
        }

        private void HandleAdded(ResourceType type, int amount, int total)
        {
            resourceIcon?.Show(type);
            Show($"+{amount}");
        }
        private void HandleRemoved(ResourceType type, int amount, int total)
        {
            resourceIcon?.Show(type);
            Show($"−{amount}");
        }
        private void HandleSale(SaleFeedbackData sale)
        {
            resourceIcon?.Show(sale.ResourceType);
            Show($"+${sale.CashValue}");
        }
        private void Show(string message)
        {
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(message)) return;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Present(message));
        }

        private IEnumerator Present(string message)
        {
            if (messageText != null) messageText.text = message;
            toastRoot?.SetActive(true);
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            float popElapsed = 0f;
            while (popElapsed < .16f)
            {
                popElapsed += Time.unscaledDeltaTime;
                float pop = IndustryTycoon.Feedback.FeedbackTween.EaseOutCubic(popElapsed / .16f);
                if (toastRoot != null) toastRoot.transform.localScale = Vector3.one * Mathf.Lerp(.82f, 1f, pop);
                yield return null;
            }
            if (toastRoot != null) toastRoot.transform.localScale = Vector3.one;
            yield return new WaitForSecondsRealtime(holdSeconds);
            float elapsed=0f;
            while(elapsed<0.3f)
            {
                elapsed+=Time.unscaledDeltaTime;
                if(canvasGroup!=null)canvasGroup.alpha=1f-IndustryTycoon.Feedback.FeedbackTween.EaseOutCubic(elapsed/0.3f);
                yield return null;
            }
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            toastRoot?.SetActive(false);
            _routine = null;
        }

    }
}
