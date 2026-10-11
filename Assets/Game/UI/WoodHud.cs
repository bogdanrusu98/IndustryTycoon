using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class WoodHud : MonoBehaviour
    {
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Text countText;
        [SerializeField] private Text resourceLabelText;
        [SerializeField] private TycoonIcon resourceIcon;
        [SerializeField] private CanvasGroup chipCanvasGroup;

        private void OnEnable()
        {
            if (carryStack != null)
            {
                carryStack.Changed += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (carryStack != null)
            {
                carryStack.Changed -= Refresh;
            }
        }

        private void Refresh()
        {
            if (carryStack == null || countText == null)
            {
                return;
            }

            if (!carryStack.TryGetActiveResourceType(out ResourceType resourceType))
            {
                countText.text = $"0 / {carryStack.Capacity}";
                if (resourceLabelText != null)
                {
                    resourceLabelText.text = "CARRY EMPTY";
                }
                if (resourceIcon != null)
                {
                    resourceIcon.Glyph = TycoonGlyph.Crate;
                    resourceIcon.color = MobileUiTheme.MutedInk;
                }
                if (chipCanvasGroup != null)
                {
                    chipCanvasGroup.alpha = 0.68f;
                }
                return;
            }

            string resourceLabel;
            switch (resourceType)
            {
                case ResourceType.Plank:
                    resourceLabel = "Plank";
                    break;
                case ResourceType.Crate:
                    resourceLabel = "Crate";
                    break;
                case ResourceType.IronOre:
                    resourceLabel = "Iron Ore";
                    break;
                case ResourceType.IronBar:
                    resourceLabel = "Iron Bar";
                    break;
                case ResourceType.Tool:
                    resourceLabel = "Tool";
                    break;
                case ResourceType.MachinePart:
                    resourceLabel = "Machine Part";
                    break;
                default:
                    resourceLabel = "Wood";
                    break;
            }

            countText.text = $"{carryStack.TotalAmount} / {carryStack.Capacity}";
            if (resourceLabelText != null)
            {
                resourceLabelText.text = resourceLabel.ToUpperInvariant();
            }
            if (resourceIcon != null)
            {
                resourceIcon.Glyph = GlyphFor(resourceType);
                resourceIcon.color = MobileUiTheme.RewardOrange;
            }
            if (chipCanvasGroup != null)
            {
                chipCanvasGroup.alpha = 1f;
            }
        }

        private static TycoonGlyph GlyphFor(ResourceType resourceType)
        {
            switch (resourceType)
            {
                case ResourceType.Plank: return TycoonGlyph.Plank;
                case ResourceType.Crate: return TycoonGlyph.Crate;
                case ResourceType.IronOre: return TycoonGlyph.IronOre;
                case ResourceType.IronBar: return TycoonGlyph.IronBar;
                case ResourceType.Tool: return TycoonGlyph.Tool;
                case ResourceType.MachinePart: return TycoonGlyph.MachinePart;
                default: return TycoonGlyph.Wood;
            }
        }
    }
}
