using IndustryTycoon.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>Subordinate hint surface; never writes to the primary objective.</summary>
    public sealed class ContextHintView : MonoBehaviour
    {
        [SerializeField] private FirstSessionOnboardingService onboarding;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Text label;
        public void Configure(FirstSessionOnboardingService source, CanvasGroup canvasGroup, Text text)
        { onboarding=source; group=canvasGroup; label=text; }
        private void OnEnable() { if(onboarding!=null)onboarding.GuidanceChanged+=Refresh; Refresh(); }
        private void OnDisable() { if(onboarding!=null)onboarding.GuidanceChanged-=Refresh; if(group!=null)group.alpha=0; }
        private void Refresh()
        {
            string hint=onboarding!=null?onboarding.ContextHint:string.Empty;
            if(label!=null)label.text=NextUnlockGuidance.BuildPresentationTitle(hint);
            if(group!=null) {group.alpha=string.IsNullOrEmpty(hint)?0:1;group.blocksRaycasts=false;group.interactable=false;}
        }
    }
}
