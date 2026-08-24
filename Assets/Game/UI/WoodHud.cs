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

            string resourceLabel = resourceType == ResourceType.Plank ? "Plank" : "Wood";
            countText.text = $"{resourceLabel}: {carryStack.TotalAmount} / {carryStack.Capacity}";
        }
    }
}
