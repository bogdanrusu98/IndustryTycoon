using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class PlayerPickupFeedback : MonoBehaviour
    {
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private ParticleSystem pickupParticles;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;
        [SerializeField, Range(1, 12)] private int particlesPerPickup = 3;
        [SerializeField, Range(1, 24)] private int fullStackParticles = 12;

        public int FeedbackCount { get; private set; }

        private void OnEnable()
        {
            if (carryStack != null)
            {
                carryStack.ItemsAdded += HandleItemsAdded;
            }
        }

        private void OnDisable()
        {
            if (carryStack != null)
            {
                carryStack.ItemsAdded -= HandleItemsAdded;
            }
        }

        private void HandleItemsAdded(ResourceType resourceType, int amount, int totalAmount)
        {
            FeedbackCount++;
            audioFeedback?.PlayPickup();

            if (pickupParticles != null)
            {
                int particleCount = totalAmount >= carryStack.Capacity
                    ? fullStackParticles
                    : Mathf.Max(1, particlesPerPickup * amount);
                pickupParticles.Emit(particleCount);
            }

            if (totalAmount >= carryStack.Capacity)
            {
                hapticFeedback?.PlayLight();
            }
        }

        private void OnValidate()
        {
            particlesPerPickup = Mathf.Clamp(particlesPerPickup, 1, 12);
            fullStackParticles = Mathf.Clamp(fullStackParticles, 1, 24);
        }
    }
}
