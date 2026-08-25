using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Logistics;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class CrateCourierFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CrateCourier courier;
        [SerializeField] private Transform courierVisual;
        [SerializeField] private Transform carriedCrateAnchor;
        [SerializeField] private GameObject resourceVisualPrefab;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private Renderer statusIndicator;
        [SerializeField] private Material idleMaterial;
        [SerializeField] private Material movingMaterial;
        [SerializeField] private Material deliveryMaterial;
        [SerializeField] private Transform leftWheel;
        [SerializeField] private Transform rightWheel;
        [SerializeField] private ParticleSystem pickupParticles;
        [SerializeField] private ParticleSystem deliveryParticles;

        [Header("Capped Cargo Visuals")]
        [SerializeField, Range(1, 2)] private int cargoVisualPoolSize = 2;
        [SerializeField, Range(0.25f, 1f)] private float cargoVisualScale = 0.58f;
        [SerializeField, Min(1f)] private float wheelRotationSpeed = 420f;

        private readonly List<GameObject> _cargoVisuals = new List<GameObject>(2);
        private readonly List<Vector3> _cargoBaseScales = new List<Vector3>(2);
        private CrateCourierState _displayedState = CrateCourierState.Disabled;
        private int _visibleCargoCount;

        public int ConfiguredCargoVisualPoolSize => cargoVisualPoolSize;
        public int CargoVisualPoolCount => _cargoVisuals.Count;
        public int VisibleCargoCount => _visibleCargoCount;
        public float CargoVisualScale => cargoVisualScale;
        public float WheelRotationSpeed => wheelRotationSpeed;
        public CrateCourierState DisplayedState => _displayedState;
        public int PickupFeedbackCount { get; private set; }
        public int DeliveryFeedbackCount { get; private set; }

        private void Awake()
        {
            EnsureCargoVisualPool();
            SynchronizeFromCourier();
        }

        private void OnEnable()
        {
            EnsureCargoVisualPool();
            if (courier != null)
            {
                courier.StateChanged += HandleStateChanged;
                courier.CargoChanged += HandleCargoChanged;
                courier.PickupCompleted += HandlePickupCompleted;
                courier.DeliveryCompleted += HandleDeliveryCompleted;
            }

            SynchronizeFromCourier();
        }

        private void OnDisable()
        {
            if (courier != null)
            {
                courier.StateChanged -= HandleStateChanged;
                courier.CargoChanged -= HandleCargoChanged;
                courier.PickupCompleted -= HandlePickupCompleted;
                courier.DeliveryCompleted -= HandleDeliveryCompleted;
            }

            SetVisibleCargo(0);
            RefreshState(CrateCourierState.Disabled);
        }

        private void Update()
        {
            if (_displayedState != CrateCourierState.MoveToPickup
                && _displayedState != CrateCourierState.MoveToDelivery)
            {
                return;
            }

            float rotation = wheelRotationSpeed * Time.deltaTime;
            leftWheel?.Rotate(Vector3.right, rotation, Space.Self);
            rightWheel?.Rotate(Vector3.right, rotation, Space.Self);
        }

        private void HandleStateChanged(CrateCourierState state)
        {
            RefreshState(state);
        }

        private void HandleCargoChanged(int carriedCrates)
        {
            SetVisibleCargo(carriedCrates);
        }

        private void HandlePickupCompleted(uint generation, int crateCount)
        {
            PickupFeedbackCount++;
            pickupParticles?.Emit(8);
        }

        private void HandleDeliveryCompleted(
            uint generation,
            int crateCount,
            int cashValue)
        {
            DeliveryFeedbackCount++;
            deliveryParticles?.Emit(12);
        }

        private void EnsureCargoVisualPool()
        {
            if (resourceVisualPrefab == null)
            {
                return;
            }

            Transform parent = carriedCrateAnchor != null
                ? carriedCrateAnchor
                : courierVisual != null
                    ? courierVisual
                    : transform;
            while (_cargoVisuals.Count < cargoVisualPoolSize)
            {
                GameObject visual = Instantiate(resourceVisualPrefab, parent);
                visual.name = $"Courier Cargo Crate {_cargoVisuals.Count + 1:00}";
                ResourceVisual resourceVisual = visual.GetComponent<ResourceVisual>();
                resourceVisual?.Show(ResourceType.Crate);
                Vector3 baseScale = visual.transform.localScale * cargoVisualScale;
                visual.transform.localScale = baseScale;
                visual.SetActive(false);
                _cargoVisuals.Add(visual);
                _cargoBaseScales.Add(baseScale);
            }
        }

        private void SynchronizeFromCourier()
        {
            RefreshState(courier != null
                ? courier.State
                : CrateCourierState.Disabled);
            SetVisibleCargo(courier != null ? courier.CarriedCrates : 0);
        }

        private void SetVisibleCargo(int carriedCrates)
        {
            int visibleCount = Mathf.Clamp(
                carriedCrates,
                0,
                Mathf.Min(cargoVisualPoolSize, _cargoVisuals.Count));
            for (int i = 0; i < _cargoVisuals.Count; i++)
            {
                GameObject visual = _cargoVisuals[i];
                bool shouldBeVisible = i < visibleCount;
                visual.SetActive(shouldBeVisible);
                if (!shouldBeVisible)
                {
                    continue;
                }

                visual.transform.localPosition = new Vector3(
                    i == 0 ? -0.34f : 0.34f,
                    i == 0 ? 0f : 0.08f,
                    0f);
                visual.transform.localRotation = Quaternion.Euler(
                    0f,
                    i == 0 ? -6f : 6f,
                    0f);
                visual.transform.localScale = _cargoBaseScales[i];
            }

            _visibleCargoCount = visibleCount;
        }

        private void RefreshState(CrateCourierState state)
        {
            _displayedState = state;
            if (statusText != null)
            {
                switch (state)
                {
                    case CrateCourierState.MoveToPickup:
                        statusText.text = "COURIER  TO PICKUP";
                        break;
                    case CrateCourierState.Pickup:
                        statusText.text = "COURIER  LOADING";
                        break;
                    case CrateCourierState.MoveToDelivery:
                        statusText.text = "COURIER  DELIVERING";
                        break;
                    case CrateCourierState.Deliver:
                        statusText.text = "COURIER  UNLOADING";
                        break;
                    case CrateCourierState.Wait:
                        statusText.text = "COURIER  WAITING";
                        break;
                    default:
                        statusText.text = "COURIER  LOCKED";
                        break;
                }
            }

            if (statusIndicator == null)
            {
                return;
            }

            Material stateMaterial = state == CrateCourierState.MoveToPickup
                                     || state == CrateCourierState.MoveToDelivery
                ? movingMaterial
                : state == CrateCourierState.Deliver
                    ? deliveryMaterial
                    : idleMaterial;
            if (stateMaterial != null)
            {
                statusIndicator.sharedMaterial = stateMaterial;
            }
        }

        private void OnValidate()
        {
            cargoVisualPoolSize = Mathf.Clamp(cargoVisualPoolSize, 1, 2);
            cargoVisualScale = Mathf.Clamp(cargoVisualScale, 0.25f, 1f);
            wheelRotationSpeed = Mathf.Max(1f, wheelRotationSpeed);
        }
    }
}
