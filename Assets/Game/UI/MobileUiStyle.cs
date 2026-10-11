using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>
    /// Reusable Industrial Playcraft surface/button styling. It is deliberately
    /// opt-in so later screen passes can migrate without changing existing views.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class MobileUiStyle : MonoBehaviour
    {
        [SerializeField] private MobileUiComponent component;
        [SerializeField] private MobileUiState state;
        [SerializeField] private Sprite roundedSprite;
        [SerializeField] private bool useDepth = true;

        public MobileUiComponent Component => component;
        public MobileUiState State => state;

        public void Configure(
            MobileUiComponent value,
            Sprite sprite,
            MobileUiState visualState = MobileUiState.Default,
            bool depth = true)
        {
            component = value;
            roundedSprite = sprite;
            state = visualState;
            useDepth = depth;
            Apply();
        }

        public void SetState(MobileUiState value)
        {
            if (state == value)
            {
                return;
            }

            state = value;
            Apply();
        }

        public void Apply()
        {
            Image image = GetComponent<Image>();
            Color baseColor = MobileUiTheme.ComponentColor(component);
            Color resolvedColor = MobileUiTheme.StateColor(state, baseColor);
            MobileUiTheme.ApplySurface(image, roundedSprite, resolvedColor, image.raycastTarget);

            Button button = GetComponent<Button>();
            if (button != null)
            {
                MobileUiTheme.ApplyButton(button, roundedSprite, resolvedColor);
                button.interactable = state != MobileUiState.Locked
                                      && state != MobileUiState.Disabled
                                      && component != MobileUiComponent.DisabledButton;
            }

            Shadow shadow = GetComponent<Shadow>();
            if (useDepth)
            {
                if (shadow == null)
                {
                    shadow = gameObject.AddComponent<Shadow>();
                }

                shadow.effectColor = MobileUiTheme.WarmShadow;
                shadow.effectDistance = new Vector2(0f, -6f);
                shadow.useGraphicAlpha = true;
            }
            else if (shadow != null)
            {
                shadow.enabled = false;
            }
        }

        private void Awake() => Apply();

    }

}
