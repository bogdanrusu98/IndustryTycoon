using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    [DisallowMultipleComponent]
    public sealed class MobileProgressBar : MonoBehaviour
    {
        [SerializeField] private Image fill;
        [SerializeField] private Text valueText;

        public Image Fill => fill;
        public Text ValueText => valueText;
        public float NormalizedValue => fill != null ? fill.fillAmount : 0f;

        public void Configure(Image fillImage, Text label = null)
        {
            fill = fillImage;
            valueText = label;
        }

        public void SetProgress(long current, long target, bool showValue)
        {
            long safeTarget = System.Math.Max(1L, target);
            long safeCurrent = System.Math.Max(0L, System.Math.Min(current, safeTarget));
            if (fill != null)
            {
                fill.type = Image.Type.Sliced;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;
                fill.fillAmount = safeCurrent / (float)safeTarget;
                fill.rectTransform.anchorMax = new Vector2(fill.fillAmount, 1f);
                fill.enabled = safeCurrent > 0;
            }

            if (valueText != null)
            {
                valueText.gameObject.SetActive(showValue);
                valueText.text = showValue ? $"{safeCurrent:N0} / {safeTarget:N0}" : string.Empty;
            }
        }
    }
}
