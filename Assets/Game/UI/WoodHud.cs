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
                countText.text = $"Wood: 0 / {carryStack.Capacity}";
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
                default:
                    resourceLabel = "Wood";
                    break;
            }

            countText.text = $"{resourceLabel}: {carryStack.TotalAmount} / {carryStack.Capacity}";
        }
    }
}
