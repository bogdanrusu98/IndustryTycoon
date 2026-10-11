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
        [SerializeField] private ResourceHudIcon resourceIcon;
        [SerializeField] private CanvasGroup chipCanvasGroup;
        [SerializeField] private MobileProgressBar capacityBar;
        [SerializeField] private GameObject fullBadge;
        private void OnEnable() { if(carryStack!=null)carryStack.Changed+=Refresh; Refresh(); }
        private void OnDisable() { if(carryStack!=null)carryStack.Changed-=Refresh; }
        private void Refresh()
        {
            bool occupied=carryStack!=null && carryStack.TotalAmount>0;
            // Keep the original bound value synchronized even while the chip is hidden.
            if (countText != null && carryStack != null)
                countText.text = $"{carryStack.TotalAmount} / {carryStack.Capacity}";
            if(chipCanvasGroup!=null)
            {
                chipCanvasGroup.alpha=occupied?1:0;
                chipCanvasGroup.blocksRaycasts=false;
                chipCanvasGroup.interactable=false;
            }
            if(fullBadge!=null)fullBadge.SetActive(occupied && carryStack.AvailableCapacity==0);
            if(!occupied || !carryStack.TryGetActiveResourceType(out ResourceType type))return;
            if(resourceLabelText!=null)resourceLabelText.text=ResourceHudIcon.Label(type);
            if(resourceIcon!=null)resourceIcon.Show(type);
            if(capacityBar!=null)capacityBar.SetProgress(carryStack.TotalAmount,carryStack.Capacity,false);
        }
    }
}
