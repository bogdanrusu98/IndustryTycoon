using UnityEngine;

namespace IndustryTycoon.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        private void OnEnable()
        {
            _rectTransform = (RectTransform)transform;
            ApplySafeArea();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled)
            {
                ApplySafeArea();
            }
        }

        private void ApplySafeArea()
        {
            if (_rectTransform == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
            if (safeArea == _lastSafeArea && screenSize == _lastScreenSize)
            {
                return;
            }

            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;

            CalculateAnchors(safeArea, screenSize, out Vector2 anchorMin, out Vector2 anchorMax);

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
        }

        public static void CalculateAnchors(
            Rect safeArea,
            Vector2Int screenSize,
            out Vector2 anchorMin,
            out Vector2 anchorMax)
        {
            float width = Mathf.Max(1f, screenSize.x);
            float height = Mathf.Max(1f, screenSize.y);
            float xMin = Mathf.Clamp(safeArea.xMin, 0f, width);
            float yMin = Mathf.Clamp(safeArea.yMin, 0f, height);
            float xMax = Mathf.Clamp(safeArea.xMax, xMin, width);
            float yMax = Mathf.Clamp(safeArea.yMax, yMin, height);
            anchorMin = new Vector2(xMin / width, yMin / height);
            anchorMax = new Vector2(xMax / width, yMax / height);
        }
    }
}
