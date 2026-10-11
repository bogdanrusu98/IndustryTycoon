using IndustryTycoon.Player;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    /// <summary>Reads authoritative movement. Never moves a gameplay or visual transform.</summary>
    public sealed class CharacterAnimationFeedback : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerMovement player;
        [SerializeField] private LumberWorker worker;
        private static readonly int Speed = Animator.StringToHash("Speed");

        public Animator Animator => animator;

        public void Configure(Animator target, PlayerMovement playerMovement, LumberWorker lumberWorker)
        {
            animator = target;
            player = playerMovement;
            worker = lumberWorker;
            animator.applyRootMotion = false;
        }

        private void OnEnable()
        {
            if (animator == null) return;
            animator.applyRootMotion = false;
            animator.SetFloat(Speed, 0f);
        }

        private void LateUpdate()
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            float speed = player != null ? player.PlanarSpeed
                : worker != null && worker.isActiveAndEnabled && worker.State != LumberWorkerState.Idle
                    ? worker.PlanarSpeed : 0f;
            // No carry/pickup/deposit clips were exported. Existing cargo and transfer
            // feedback remains authoritative for those presentations; locomotion continues.
            animator.SetFloat(Speed, speed, 0.08f, Time.deltaTime);
        }
    }
}
