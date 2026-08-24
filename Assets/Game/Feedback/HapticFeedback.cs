using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class HapticFeedback : MonoBehaviour
    {
        [SerializeField] private bool hapticsEnabled = true;
        [SerializeField, Min(0f)] private float lightCooldown = 0.12f;
        [SerializeField, Min(0f)] private float importantCooldown = 0.35f;

        private float _lastVibrationTime = float.NegativeInfinity;
        private float _lastImportantTime = float.NegativeInfinity;

        public bool Enabled
        {
            get => hapticsEnabled;
            set => hapticsEnabled = value;
        }

        public void PlayLight()
        {
            if (!CanPlay(_lastVibrationTime, lightCooldown))
            {
                return;
            }

            Vibrate();
            _lastVibrationTime = Time.unscaledTime;
        }

        public void PlayImportant()
        {
            if (!CanPlay(_lastImportantTime, importantCooldown))
            {
                return;
            }

            Vibrate();
            float currentTime = Time.unscaledTime;
            _lastVibrationTime = currentTime;
            _lastImportantTime = currentTime;
        }

        private bool CanPlay(float lastFeedbackTime, float cooldown)
        {
            return hapticsEnabled && Time.unscaledTime - lastFeedbackTime >= cooldown;
        }

        private static void Vibrate()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        private void OnValidate()
        {
            lightCooldown = Mathf.Max(0f, lightCooldown);
            importantCooldown = Mathf.Max(0f, importantCooldown);
        }
    }
}
