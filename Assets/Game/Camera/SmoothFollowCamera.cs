using UnityEngine;

namespace IndustryTycoon.CameraSystem
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class SmoothFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -9f);
        [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float maximumFollowSpeed = 60f;
        [Header("Additive Impulse")]
        [SerializeField, Min(0f)] private float impulseAmplitude = 0.06f;
        [SerializeField, Min(0f)] private float impulseDuration = 0.18f;

        private Vector3 _followVelocity;
        private Vector3 _basePosition;
        private Vector3 _currentImpulseOffset;
        private float _activeImpulseAmplitude;
        private float _activeImpulseDuration;
        private float _impulseElapsed;

        public Vector3 BasePosition => _basePosition;
        public Vector3 CurrentImpulseOffset => _currentImpulseOffset;
        public bool IsImpulseActive => _activeImpulseDuration > 0f
            && _impulseElapsed < _activeImpulseDuration;
        public float ImpulseAmplitude
        {
            get => impulseAmplitude;
            set => impulseAmplitude = Mathf.Max(0f, value);
        }

        public float ImpulseDuration
        {
            get => impulseDuration;
            set => impulseDuration = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            _basePosition = transform.position;
        }

        private void Start()
        {
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desiredPosition = target.position + offset;
            _basePosition = Vector3.SmoothDamp(
                _basePosition,
                desiredPosition,
                ref _followVelocity,
                followSmoothTime,
                maximumFollowSpeed,
                Time.deltaTime);
            _currentImpulseOffset = EvaluateImpulseOffset(Time.unscaledDeltaTime);
            transform.position = _basePosition + _currentImpulseOffset;
            LookAtTarget(_basePosition);
        }

        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            _followVelocity = Vector3.zero;
            _basePosition = target.position + offset;
            _activeImpulseAmplitude = 0f;
            _activeImpulseDuration = 0f;
            _impulseElapsed = 0f;
            _currentImpulseOffset = Vector3.zero;
            transform.position = _basePosition;
            LookAtTarget(_basePosition);
        }

        public void TriggerImpulse()
        {
            TriggerImpulse(impulseAmplitude, impulseDuration);
        }

        public void TriggerImpulse(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f)
            {
                return;
            }

            _activeImpulseAmplitude = amplitude;
            _activeImpulseDuration = duration;
            _impulseElapsed = 0f;
        }

        public void ClearImpulse()
        {
            _activeImpulseAmplitude = 0f;
            _activeImpulseDuration = 0f;
            _impulseElapsed = 0f;
            _currentImpulseOffset = Vector3.zero;
            transform.position = _basePosition;
        }

        private Vector3 EvaluateImpulseOffset(float deltaTime)
        {
            if (!IsImpulseActive)
            {
                return Vector3.zero;
            }

            _impulseElapsed = Mathf.Min(_impulseElapsed + Mathf.Max(0f, deltaTime), _activeImpulseDuration);
            float normalizedTime = _impulseElapsed / _activeImpulseDuration;
            float envelope = 1f - normalizedTime;
            envelope *= envelope;

            float horizontalWave = Mathf.Sin(normalizedTime * Mathf.PI * 5f);
            float verticalWave = Mathf.Sin(normalizedTime * Mathf.PI * 4f + Mathf.PI * 0.25f);
            return (transform.right * horizontalWave + transform.up * verticalWave * 0.55f)
                * (_activeImpulseAmplitude * envelope);
        }

        private void LookAtTarget(Vector3 cameraPosition)
        {
            Vector3 lookDirection = (target.position + lookAtOffset) - cameraPosition;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            }
        }

        private void OnValidate()
        {
            followSmoothTime = Mathf.Max(0.01f, followSmoothTime);
            maximumFollowSpeed = Mathf.Max(0f, maximumFollowSpeed);
            impulseAmplitude = Mathf.Max(0f, impulseAmplitude);
            impulseDuration = Mathf.Max(0f, impulseDuration);
        }
    }
}
