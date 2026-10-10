using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public enum MobileUiSurface
    {
        Background,
        Elevated,
        Card,
        Navigation,
        Scrim,
        Track
    }

    public enum MobileUiAction
    {
        Primary,
        Reward,
        Secondary,
        Danger,
        Disabled
    }

    /// <summary>
    /// Shared visual tokens for the generated portrait UI. The scene stores the
    /// resolved values, while editor tooling and future runtime-created views use
    /// the same palette, typography and interaction states.
    /// </summary>
    public static class MobileUiTheme
    {
        public static readonly Color Charcoal = new Color(0.025f, 0.037f, 0.052f, 0.992f);
        public static readonly Color DarkNavy = new Color(0.035f, 0.065f, 0.090f, 0.985f);
        public static readonly Color Elevated = new Color(0.055f, 0.095f, 0.125f, 1f);
        public static readonly Color Card = new Color(0.075f, 0.125f, 0.155f, 1f);
        public static readonly Color Navigation = new Color(0.020f, 0.047f, 0.065f, 0.995f);
        public static readonly Color Track = new Color(0.16f, 0.205f, 0.235f, 1f);
        public static readonly Color Scrim = new Color(0.010f, 0.020f, 0.032f, 0.92f);

        public static readonly Color Teal = new Color(0.055f, 0.69f, 0.65f, 1f);
        public static readonly Color TealDark = new Color(0.035f, 0.42f, 0.43f, 1f);
        public static readonly Color Orange = new Color(0.98f, 0.56f, 0.12f, 1f);
        public static readonly Color Yellow = new Color(1f, 0.78f, 0.20f, 1f);
        public static readonly Color Danger = new Color(0.78f, 0.20f, 0.19f, 1f);
        public static readonly Color Disabled = new Color(0.20f, 0.25f, 0.28f, 1f);

        public static readonly Color TextPrimary = new Color(0.94f, 0.98f, 1f, 1f);
        public static readonly Color TextSecondary = new Color(0.66f, 0.75f, 0.80f, 1f);
        public static readonly Color TextDark = new Color(0.025f, 0.04f, 0.05f, 1f);

        public static Color Surface(MobileUiSurface surface)
        {
            switch (surface)
            {
                case MobileUiSurface.Background: return Charcoal;
                case MobileUiSurface.Elevated: return Elevated;
                case MobileUiSurface.Card: return Card;
                case MobileUiSurface.Navigation: return Navigation;
                case MobileUiSurface.Scrim: return Scrim;
                case MobileUiSurface.Track: return Track;
                default: return DarkNavy;
            }
        }

        public static Color Action(MobileUiAction action)
        {
            switch (action)
            {
                case MobileUiAction.Primary: return Teal;
                case MobileUiAction.Reward: return Orange;
                case MobileUiAction.Danger: return Danger;
                case MobileUiAction.Disabled: return Disabled;
                default: return Card;
            }
        }

        public static void ApplySurface(
            Image image,
            Sprite roundedSprite,
            Color color,
            bool raycastTarget = true)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = roundedSprite;
            image.type = roundedSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycastTarget;
        }

        public static void ApplyButton(
            Button button,
            Sprite roundedSprite,
            Color baseColor)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.GetComponent<Image>();
            ApplySurface(image, roundedSprite, baseColor);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.78f, 0.82f, 0.84f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.48f, 0.52f, 0.54f, 0.78f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        public static void ApplyText(
            Text text,
            bool secondary = false,
            FontStyle style = FontStyle.Bold)
        {
            if (text == null)
            {
                return;
            }

            text.color = secondary ? TextSecondary : TextPrimary;
            text.fontStyle = style;
            text.raycastTarget = false;
        }
    }
}
