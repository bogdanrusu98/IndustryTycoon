using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class AudioFeedback : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [Header("Optional Clips")]
        [SerializeField] private AudioClip pickupClip;
        [SerializeField] private AudioClip saleClip;
        [SerializeField] private AudioClip cashCollectClip;
        [SerializeField] private AudioClip purchaseTickClip;
        [SerializeField] private AudioClip unlockClip;
        [SerializeField] private AudioClip buttonClip;
        [SerializeField] private AudioClip objectiveClip;
        [SerializeField] private AudioClip productionClip;
        [SerializeField] private AudioClip warningClip;

        public AudioSource AudioSource => audioSource;

        public float OutputVolume
        {
            get => audioSource != null ? audioSource.volume : 1f;
            set
            {
                if (audioSource != null) audioSource.volume = Mathf.Clamp01(value);
            }
        }

        public void PlayPickup()
        {
            Play(pickupClip);
        }

        public void PlaySale()
        {
            Play(saleClip);
        }

        public void PlayCashCollect()
        {
            Play(cashCollectClip);
        }

        public void PlayPurchaseTick()
        {
            Play(purchaseTickClip);
        }

        public void PlayUnlock()
        {
            Play(unlockClip);
        }

        public void PlayButton() => Play(buttonClip);
        public void PlayObjective() => Play(objectiveClip);
        public void PlayProduction() => Play(productionClip);
        public void PlayWarning() => Play(warningClip);

        private void Play(AudioClip clip)
        {
            if (audioSource == null || clip == null || !audioSource.isActiveAndEnabled)
            {
                return;
            }

            audioSource.PlayOneShot(clip);
        }
    }
}
