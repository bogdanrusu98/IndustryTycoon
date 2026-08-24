using System;
using IndustryTycoon.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IndustryTycoon.ResourceSystem
{
    public enum ResourceClaimPriority
    {
        Worker = 0,
        Player = 100
    }

    public readonly struct ResourceClaimHandle
    {
        private readonly ResourcePickup _pickup;
        private readonly Object _owner;
        private readonly uint _revision;

        internal ResourceClaimHandle(ResourcePickup pickup, Object owner, uint revision)
        {
            _pickup = pickup;
            _owner = owner;
            _revision = revision;
        }

        public ResourcePickup Pickup => _pickup;
        public bool IsValid => _pickup != null && _pickup.IsClaimValid(this);
        public bool IsAttractionValid => _pickup != null && _pickup.IsAttractionValid(this);

        internal Object Owner => _owner;
        internal uint Revision => _revision;
    }

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
        private Object _claimOwner;
        private ResourceClaimPriority _claimPriority;
        private uint _claimRevision;
        private ResourceClaimHandle _legacyAttractionHandle;
        private bool _hasCachedSpawnScale;

        public ResourceType ResourceType => resourceType;
        public int Amount => amount;
        public bool IsAvailable => _state == PickupState.Available && isActiveAndEnabled;
        public bool IsAttracted => _state == PickupState.Attracted && isActiveAndEnabled;
        public bool IsClaimed => IsAvailable && _claimOwner != null;
        public Object ClaimOwner => _claimOwner;

        private void Awake()
        {
            _pickupCollider = GetComponent<Collider>();
            CacheSpawnScale();
        }

        private void OnDisable()
        {
            _state = PickupState.Inactive;
            _attractionElapsed = 0f;
            InvalidateClaim();
            SetColliderEnabled(false);
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
            CacheSpawnScale();
            InvalidateClaim();
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = _spawnScale;
            _state = PickupState.Available;
            _attractionElapsed = 0f;
            SetColliderEnabled(true);
        }

        public bool TryClaim(
            Object claimant,
            ResourceClaimPriority priority,
            out ResourceClaimHandle claim)
        {
            claim = default;
            if (claimant == null || !IsAvailable)
            {
                return false;
            }

            ClearDestroyedClaimOwner();
            if (_claimOwner == claimant)
            {
                if (priority > _claimPriority)
                {
                    _claimPriority = priority;
                }

                claim = CreateClaimHandle();
                return true;
            }

            if (_claimOwner != null && priority <= _claimPriority)
            {
                return false;
            }

            AdvanceClaimRevision();
            _claimOwner = claimant;
            _claimPriority = priority;
            claim = CreateClaimHandle();
            return true;
        }

        public bool TryReleaseClaim(ResourceClaimHandle claim)
        {
            if (!IsClaimValid(claim))
            {
                return false;
            }

            InvalidateClaim();
            return true;
        }

        public bool IsClaimedBy(Object claimant)
        {
            ClearDestroyedClaimOwner();
            return claimant != null && IsAvailable && _claimOwner == claimant;
        }

        public bool IsClaimValid(ResourceClaimHandle claim)
        {
            ClearDestroyedClaimOwner();
            return IsAvailable && MatchesClaim(claim);
        }

        public bool IsAttractionValid(ResourceClaimHandle claim)
        {
            ClearDestroyedClaimOwner();
            return IsAttracted && MatchesClaim(claim);
        }

        public bool TryBeginAttraction(
            Object claimant,
            ResourceClaimPriority priority,
            out ResourceClaimHandle claim)
        {
            if (!TryClaim(claimant, priority, out claim))
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

        public bool TryBeginAttraction()
        {
            return TryBeginAttraction(
                this,
                ResourceClaimPriority.Player,
                out _legacyAttractionHandle);
        }

        public bool AdvanceAttraction(
            ResourceClaimHandle claim,
            Vector3 targetPosition,
            float deltaTime,
            float duration,
            float arcHeight)
        {
            if (!IsAttractionValid(claim))
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

        public bool AdvanceAttraction(
            Vector3 targetPosition,
            float deltaTime,
            float duration,
            float arcHeight)
        {
            return AdvanceAttraction(
                _legacyAttractionHandle,
                targetPosition,
                deltaTime,
                duration,
                arcHeight);
        }

        public bool TryCompleteAttraction(ResourceClaimHandle claim)
        {
            if (!IsAttractionValid(claim))
            {
                return false;
            }

            CompleteClaimedPickup();
            return true;
        }

        public void CompleteCollection()
        {
            TryCompleteAttraction(_legacyAttractionHandle);
        }

        public bool TryConsumeClaim(
            ResourceClaimHandle claim,
            out ResourceType consumedType,
            out int consumedAmount)
        {
            consumedType = resourceType;
            consumedAmount = 0;
            if (!IsClaimValid(claim))
            {
                return false;
            }

            consumedAmount = amount;
            CompleteClaimedPickup();
            return true;
        }

        public bool CancelAttraction(ResourceClaimHandle claim)
        {
            if (!IsAttractionValid(claim))
            {
                return false;
            }

            _state = PickupState.Available;
            _attractionElapsed = 0f;
            transform.localScale = _attractionStartScale;
            InvalidateClaim();
            SetColliderEnabled(true);
            return true;
        }

        public void CancelAttraction()
        {
            CancelAttraction(_legacyAttractionHandle);
        }

        public void MarkReleased()
        {
            CacheSpawnScale();
            _state = PickupState.Inactive;
            _attractionElapsed = 0f;
            transform.localScale = _spawnScale;
            InvalidateClaim();
            SetColliderEnabled(false);
        }

        private void CompleteClaimedPickup()
        {
            _state = PickupState.Inactive;
            _attractionElapsed = 0f;
            SetColliderEnabled(false);
            InvalidateClaim();

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

        private ResourceClaimHandle CreateClaimHandle()
        {
            return new ResourceClaimHandle(this, _claimOwner, _claimRevision);
        }

        private bool MatchesClaim(ResourceClaimHandle claim)
        {
            return claim.Pickup == this
                   && claim.Owner != null
                   && claim.Owner == _claimOwner
                   && claim.Revision == _claimRevision;
        }

        private void ClearDestroyedClaimOwner()
        {
            if (!ReferenceEquals(_claimOwner, null) && _claimOwner == null)
            {
                InvalidateClaim();
            }
        }

        private void InvalidateClaim()
        {
            _claimOwner = null;
            _claimPriority = ResourceClaimPriority.Worker;
            _legacyAttractionHandle = default;
            AdvanceClaimRevision();
        }

        private void AdvanceClaimRevision()
        {
            _claimRevision++;
            if (_claimRevision == 0)
            {
                _claimRevision = 1;
            }
        }

        private void CacheSpawnScale()
        {
            if (_hasCachedSpawnScale)
            {
                return;
            }

            _spawnScale = transform.localScale;
            _hasCachedSpawnScale = true;
        }

        private void SetColliderEnabled(bool isEnabled)
        {
            if (_pickupCollider == null)
            {
                _pickupCollider = GetComponent<Collider>();
            }

            if (_pickupCollider != null)
            {
                _pickupCollider.enabled = isEnabled;
            }
        }
    }
}
