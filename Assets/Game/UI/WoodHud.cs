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

            countText.text = $"Wood: {carryStack.GetAmount(ResourceType.Wood)} / {carryStack.Capacity}";
        }
    }
}
