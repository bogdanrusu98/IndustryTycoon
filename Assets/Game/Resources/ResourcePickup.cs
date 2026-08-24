using System;
using IndustryTycoon.Core;
using UnityEngine;

namespace IndustryTycoon.ResourceSystem
{
    [RequireComponent(typeof(Collider))]
    public sealed class ResourcePickup : MonoBehaviour
    {
        [SerializeField] private ResourceType resourceType = ResourceType.Wood;
        [SerializeField, Min(1)] private int amount = 1;

        private enum PickupState
        {
            Inactive,
            Available,
            Attracted
        }

        private Collider _pickupCollider;
        private Action<ResourcePickup> _releaseAction;
        private PickupState _state;
        private Vector3 _attractionStart;
        private Quaternion _attractionStartRotation;
        private Vector3 _spawnScale;
        private Vector3 _attractionStartScale;
        private float _attractionElapsed;

        public ResourceType ResourceType => resourceType;
        public int Amount => amount;
        public bool IsAvailable => _state == PickupState.Available && isActiveAndEnabled;
        public bool IsAttracted => _state == PickupState.Attracted && isActiveAndEnabled;

        private void Awake()
        {
            _pickupCollider = GetComponent<Collider>();
            _spawnScale = transform.localScale;
        }

        private void OnValidate()
        {
            amount = Mathf.Max(1, amount);
        }

        public void Configure(ResourceType type, int resourceAmount)
        {
            resourceType = type;
            amount = Mathf.Max(1, resourceAmount);
        }

        public void SetReleaseAction(Action<ResourcePickup> releaseAction)
        {
            _releaseAction = releaseAction;
        }

        public void PrepareForSpawn(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = _spawnScale;
            _state = PickupState.Available;
            _attractionElapsed = 0f;
            SetColliderEnabled(true);
        }

        public bool TryBeginAttraction()
        {
            if (!IsAvailable)
            {
                return false;
            }

            _state = PickupState.Attracted;
            _attractionStart = transform.position;
            _attractionStartRotation = transform.rotation;
            _attractionStartScale = transform.localScale;
            _attractionElapsed = 0f;
            SetColliderEnabled(false);
            return true;
        }

        public bool AdvanceAttraction(
            Vector3 targetPosition,
            float deltaTime,
            float duration,
            float arcHeight)
        {
            if (_state != PickupState.Attracted)
            {
                return false;
            }

            _attractionElapsed += deltaTime;
            float normalizedTime = Mathf.Clamp01(_attractionElapsed / Mathf.Max(0.01f, duration));
            float easedTime = normalizedTime * normalizedTime * (3f - (2f * normalizedTime));
            float verticalArc = Mathf.Sin(normalizedTime * Mathf.PI) * arcHeight;
            float scalePulse = Mathf.Sin(normalizedTime * Mathf.PI) * 0.12f;
            float arrivalScale = Mathf.Lerp(1f, 0.82f, normalizedTime * normalizedTime);

            transform.position = Vector3.Lerp(_attractionStart, targetPosition, easedTime)
                                 + (Vector3.up * verticalArc);
            transform.rotation = Quaternion.AngleAxis(normalizedTime * 360f, Vector3.up)
                                 * _attractionStartRotation;
            transform.localScale = _attractionStartScale * (arrivalScale + scalePulse);
            return normalizedTime >= 1f;
        }

        public void CompleteCollection()
        {
            if (_state != PickupState.Attracted)
            {
                return;
            }

            _state = PickupState.Inactive;
            Action<ResourcePickup> releaseAction = _releaseAction;
            if (releaseAction != null)
            {
                releaseAction(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        public void CancelAttraction()
        {
            if (_state != PickupState.Attracted)
            {
                return;
            }

            _state = PickupState.Available;
            _attractionElapsed = 0f;
            transform.localScale = _attractionStartScale;
            SetColliderEnabled(true);
        }

        public void MarkReleased()
        {
            _state = PickupState.Inactive;
            _attractionElapsed = 0f;
            transform.localScale = _spawnScale;
            SetColliderEnabled(false);
        }

        private void SetColliderEnabled(bool isEnabled)
        {
            if (_pickupCollider == null)
            {
                _pickupCollider = GetComponent<Collider>();
            }

            _pickupCollider.enabled = isEnabled;
        }
    }
}
