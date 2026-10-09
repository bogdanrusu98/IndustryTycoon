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

        private void HandleAdded(ResourceType type, int amount, int total) =>
            Show($"+{amount} {Label(type)}  •  CARRY {total}/{carryStack.Capacity}");

        private void HandleRemoved(ResourceType type, int amount, int total) =>
            Show($"DELIVERED {amount} {Label(type)}  •  CARRY {total}/{carryStack.Capacity}");

        private void HandleSale(SaleFeedbackData sale) =>
            Show($"SOLD {Label(sale.ResourceType)}  •  +${sale.CashValue}");

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
            yield return new WaitForSecondsRealtime(holdSeconds);
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            toastRoot?.SetActive(false);
            _routine = null;
        }

        private static string Label(ResourceType type) => type.ToString().ToUpperInvariant();
    }
}
