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

        public AudioSource AudioSource => audioSource;

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
