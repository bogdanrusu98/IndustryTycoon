using IndustryTycoon.Monetization;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class PrivacySettingsView : MonoBehaviour
    {
        [SerializeField] private MonetizationService monetization;
        [SerializeField] private Toggle consentToggle;
        [SerializeField] private Button privacyPolicyButton;
        [SerializeField] private Text statusText;

        private void OnEnable()
        {
            consentToggle?.onValueChanged.AddListener(SetConsent);
            privacyPolicyButton?.onClick.AddListener(ShowPolicyPlaceholder);
            if (consentToggle != null) consentToggle.SetIsOnWithoutNotify(
                monetization != null && monetization.Consent == ConsentStatus.Granted);
            RefreshStatus();
        }
        private void OnDisable()
        {
            consentToggle?.onValueChanged.RemoveListener(SetConsent);
            privacyPolicyButton?.onClick.RemoveListener(ShowPolicyPlaceholder);
        }
        private void SetConsent(bool granted)
        {
            monetization?.SetConsent(granted ? ConsentStatus.Granted : ConsentStatus.Denied);
            RefreshStatus();
        }
        private void ShowPolicyPlaceholder()
        {
            if (statusText != null) statusText.text = "PRIVACY POLICY URL NOT CONFIGURED";
        }
        private void RefreshStatus()
        {
            if (statusText != null) statusText.text = monetization?.Consent == ConsentStatus.Granted
                ? "OPTIONAL ADS & ANALYTICS: ALLOWED" : "OPTIONAL ADS & ANALYTICS: DISABLED";
        }
    }
}
