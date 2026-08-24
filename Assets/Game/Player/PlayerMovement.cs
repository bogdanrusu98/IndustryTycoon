using UnityEngine;

namespace IndustryTycoon.Player
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerDragInput))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerDragInput dragInput;
        [SerializeField] private Transform movementCamera;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 5.5f;
        [SerializeField, Min(0f)] private float acceleration = 18f;
        [SerializeField, Min(0f)] private float deceleration = 24f;
        [SerializeField, Min(0f)] private float rotationSpeed = 720f;
        [SerializeField] private float groundedVerticalSpeed = -2f;

        private CharacterController _characterController;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            if (dragInput == null)
            {
                dragInput = GetComponent<PlayerDragInput>();
            }
        }

        private void Update()
        {
            Vector2 input = dragInput.ReadMoveInput();
            Vector3 moveDirection = GetCameraRelativeDirection(input);
            Vector3 targetVelocity = moveDirection * moveSpeed;
            float velocityChangeRate = input.sqrMagnitude > 0.0001f ? acceleration : deceleration;
            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity,
                targetVelocity,
                velocityChangeRate * Time.deltaTime);

            if (_characterController.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedVerticalSpeed;
            }
            else
            {
                _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            }

            Vector3 velocity = _planarVelocity;
            velocity.y = _verticalVelocity;
            CollisionFlags collisionFlags = _characterController.Move(velocity * Time.deltaTime);
            if ((collisionFlags & CollisionFlags.Below) != 0 && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedVerticalSpeed;
            }

            RotateTowards(moveDirection);
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            Vector3 forward = movementCamera != null ? movementCamera.forward : Vector3.forward;
            Vector3 right = movementCamera != null ? movementCamera.right : Vector3.right;

            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            return Vector3.ClampMagnitude((right * input.x) + (forward * input.y), 1f);
        }

        private void RotateTowards(Vector3 moveDirection)
        {
            if (moveDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            acceleration = Mathf.Max(0f, acceleration);
            deceleration = Mathf.Max(0f, deceleration);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
        }
    }
}
