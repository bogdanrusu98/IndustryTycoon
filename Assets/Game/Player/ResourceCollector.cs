using System.Collections.Generic;
using IndustryTycoon.ResourceSystem;
using UnityEngine;

namespace IndustryTycoon.Player
{
    [RequireComponent(typeof(CarryStack))]
    public sealed class ResourceCollector : MonoBehaviour
    {
        private sealed class Attraction
        {
            public ResourcePickup Pickup;
            public int ReservedAmount;
            public float Delay;
        }

        [Header("References")]
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Transform pickupTarget;

        [Header("Pickup")]
        [SerializeField, Min(0.1f)] private float pickupRadius = 2.25f;
        [SerializeField, Min(0.02f)] private float scanInterval = 0.08f;
        [SerializeField] private LayerMask pickupMask = ~0;
        [SerializeField, Range(8, 128)] private int queryCapacity = 64;

        [Header("Attraction")]
        [SerializeField, Min(0.05f)] private float attractionDuration = 0.35f;
        [SerializeField, Min(0f)] private float attractionArcHeight = 0.75f;
        [SerializeField, Min(0f)] private float attractionStagger = 0.025f;
        [SerializeField, Min(0f)] private float maximumStagger = 0.075f;

        private readonly List<Attraction> _activeAttractions = new List<Attraction>(16);
        private Collider[] _queryResults;
        private float _timeUntilScan;
        private int _reservedCapacity;

        public float AttractionDuration => attractionDuration;
        public float AttractionArcHeight => attractionArcHeight;
        public float AttractionStagger => attractionStagger;
        public float MaximumStagger => maximumStagger;
        public int ReservedCapacity => _reservedCapacity;

        private void Awake()
        {
            if (carryStack == null)
            {
                carryStack = GetComponent<CarryStack>();
            }

            if (pickupTarget == null)
            {
                pickupTarget = carryStack.VisualRoot;
            }

            _queryResults = new Collider[Mathf.Max(8, queryCapacity)];
        }

        private void Update()
        {
            UpdateAttractions();

            _timeUntilScan -= Time.deltaTime;
            if (_timeUntilScan > 0f)
            {
                return;
            }

            _timeUntilScan = scanInterval;
            ScanForResources();
        }

        private void OnDisable()
        {
            for (int i = 0; i < _activeAttractions.Count; i++)
            {
                ResourcePickup pickup = _activeAttractions[i].Pickup;
                if (pickup != null)
                {
                    pickup.CancelAttraction();
                }
            }

            _activeAttractions.Clear();
            _reservedCapacity = 0;
        }

        private void ScanForResources()
        {
            int availableCapacity = carryStack.AvailableCapacity - _reservedCapacity;
            if (availableCapacity <= 0)
            {
                return;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                pickupRadius,
                _queryResults,
                pickupMask,
                QueryTriggerInteraction.Collide);

            int acceptedPickupCount = 0;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _queryResults[i];
                _queryResults[i] = null;
                if (hit == null)
                {
                    continue;
                }

                ResourcePickup pickup = hit.GetComponentInParent<ResourcePickup>();
                if (pickup == null || !pickup.IsAvailable || pickup.Amount > availableCapacity)
                {
                    continue;
                }

                if (!pickup.TryBeginAttraction())
                {
                    continue;
                }

                _activeAttractions.Add(new Attraction
                {
                    Pickup = pickup,
                    ReservedAmount = pickup.Amount,
                    Delay = Mathf.Min(maximumStagger, acceptedPickupCount * attractionStagger)
                });
                acceptedPickupCount++;
                _reservedCapacity += pickup.Amount;
                availableCapacity -= pickup.Amount;

                if (availableCapacity <= 0)
                {
                    break;
                }
            }
        }

        private void UpdateAttractions()
        {
            Vector3 targetPosition = pickupTarget != null ? pickupTarget.position : transform.position;
            for (int i = _activeAttractions.Count - 1; i >= 0; i--)
            {
                Attraction attraction = _activeAttractions[i];
                ResourcePickup pickup = attraction.Pickup;
                if (pickup == null)
                {
                    _reservedCapacity = Mathf.Max(0, _reservedCapacity - attraction.ReservedAmount);
                    _activeAttractions.RemoveAt(i);
                    continue;
                }

                if (!pickup.IsAttracted)
                {
                    pickup.CancelAttraction();
                    _reservedCapacity = Mathf.Max(0, _reservedCapacity - attraction.ReservedAmount);
                    _activeAttractions.RemoveAt(i);
                    continue;
                }

                if (attraction.Delay > 0f)
                {
                    attraction.Delay = Mathf.Max(0f, attraction.Delay - Time.deltaTime);
                    continue;
                }

                bool reachedTarget = pickup.AdvanceAttraction(
                    targetPosition,
                    Time.deltaTime,
                    attractionDuration,
                    attractionArcHeight);
                if (!reachedTarget)
                {
                    continue;
                }

                _reservedCapacity = Mathf.Max(0, _reservedCapacity - attraction.ReservedAmount);
                _activeAttractions.RemoveAt(i);

                if (carryStack.TryAdd(pickup.ResourceType, pickup.Amount))
                {
                    pickup.CompleteCollection();
                }
                else
                {
                    pickup.CancelAttraction();
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.65f);
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }

        private void OnValidate()
        {
            pickupRadius = Mathf.Max(0.1f, pickupRadius);
            scanInterval = Mathf.Max(0.02f, scanInterval);
            queryCapacity = Mathf.Clamp(queryCapacity, 8, 128);
            attractionDuration = Mathf.Max(0.05f, attractionDuration);
            attractionArcHeight = Mathf.Max(0f, attractionArcHeight);
            attractionStagger = Mathf.Max(0f, attractionStagger);
            maximumStagger = Mathf.Max(0f, maximumStagger);
        }
    }
}
