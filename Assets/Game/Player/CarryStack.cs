using System;
using System.Collections.Generic;
using IndustryTycoon.Core;
using UnityEngine;

namespace IndustryTycoon.Player
{
    public sealed class CarryStack : MonoBehaviour
    {
        [Header("Capacity")]
        [SerializeField, Min(1)] private int capacity = 12;

        [Header("Visuals")]
        [SerializeField] private ResourceType displayedResourceType = ResourceType.Wood;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject itemVisualPrefab;
        [SerializeField, Min(1)] private int itemsPerRow = 3;
        [SerializeField, Min(0f)] private float horizontalSpacing = 0.82f;
        [SerializeField, Min(0f)] private float verticalSpacing = 0.40f;
        [SerializeField, Min(0f)] private float depthSpacing = 0.10f;

        [Header("Visual Feel")]
        [SerializeField, Min(0.01f)] private float placementDuration = 0.14f;
        [SerializeField, Min(0.01f)] private float addBounceDuration = 0.18f;
        [SerializeField, Range(1f, 1.5f)] private float addScaleOvershoot = 1.18f;
        [SerializeField, Min(0f)] private float addLift = 0.16f;

        private readonly Dictionary<ResourceType, int> _amounts = new Dictionary<ResourceType, int>();
        private readonly List<GameObject> _visualItems = new List<GameObject>();
        private readonly List<Vector3> _visualBaseScales = new List<Vector3>();
        private readonly List<Vector3> _visualStartPositions = new List<Vector3>();
        private readonly List<Vector3> _visualTargetPositions = new List<Vector3>();
        private readonly List<Quaternion> _visualStartRotations = new List<Quaternion>();
        private readonly List<Quaternion> _visualTargetRotations = new List<Quaternion>();
        private readonly List<float> _visualAnimationElapsed = new List<float>();
        private int _totalAmount;
        private int _lastVisibleCount;
        private bool _hasVisualAnimations;

        public event Action Changed;
        public event Action<ResourceType, int, int> ItemsAdded;
        public event Action<ResourceType, int, int> ItemsRemoved;

        public int Capacity => capacity;
        public int TotalAmount => _totalAmount;
        public int AvailableCapacity => Mathf.Max(0, capacity - _totalAmount);
        public Transform VisualRoot => visualRoot != null ? visualRoot : transform;
        public float PlacementDuration => placementDuration;
        public float AddBounceDuration => addBounceDuration;
        public float AddScaleOvershoot => addScaleOvershoot;

        private void Awake()
        {
            EnsureVisualPool();
            RefreshVisuals();
        }

        public int GetAmount(ResourceType resourceType)
        {
            return _amounts.TryGetValue(resourceType, out int amount) ? amount : 0;
        }

        public bool CanAdd(int amount)
        {
            return amount > 0 && amount <= AvailableCapacity;
        }

        public bool CanRemove(ResourceType resourceType, int amount)
        {
            return amount > 0 && GetAmount(resourceType) >= amount;
        }

        public bool TryAdd(ResourceType resourceType, int amount)
        {
            if (!CanAdd(amount))
            {
                return false;
            }

            _amounts[resourceType] = GetAmount(resourceType) + amount;
            _totalAmount += amount;
            RefreshVisuals();
            Changed?.Invoke();
            ItemsAdded?.Invoke(resourceType, amount, _totalAmount);
            return true;
        }

        public bool TryRemove(ResourceType resourceType, int amount)
        {
            if (!CanRemove(resourceType, amount))
            {
                return false;
            }

            int remainingAmount = GetAmount(resourceType) - amount;
            if (remainingAmount > 0)
            {
                _amounts[resourceType] = remainingAmount;
            }
            else
            {
                _amounts.Remove(resourceType);
            }

            _totalAmount -= amount;
            RefreshVisuals();
            Changed?.Invoke();
            ItemsRemoved?.Invoke(resourceType, amount, _totalAmount);
            return true;
        }

        public bool TryGetTopVisualPose(
            ResourceType resourceType,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            int amount = GetAmount(resourceType);
            if (resourceType == displayedResourceType
                && amount > 0
                && amount <= _visualItems.Count)
            {
                Transform visual = _visualItems[amount - 1].transform;
                position = visual.position;
                rotation = visual.rotation;
                scale = visual.lossyScale;
                return true;
            }

            Transform fallback = VisualRoot;
            position = fallback.position;
            rotation = fallback.rotation;
            scale = Vector3.one;
            return false;
        }

        private void Update()
        {
            if (!_hasVisualAnimations)
            {
                return;
            }

            _hasVisualAnimations = false;
            float animationDuration = Mathf.Max(placementDuration, addBounceDuration);
            for (int i = 0; i < _visualItems.Count; i++)
            {
                if (_visualAnimationElapsed[i] < 0f || !_visualItems[i].activeSelf)
                {
                    continue;
                }

                float elapsed = Mathf.Min(animationDuration, _visualAnimationElapsed[i] + Time.deltaTime);
                _visualAnimationElapsed[i] = elapsed;
                Transform visual = _visualItems[i].transform;

                float placementTime = Mathf.Clamp01(elapsed / placementDuration);
                float placementEase = placementTime * placementTime * (3f - (2f * placementTime));
                visual.localPosition = Vector3.Lerp(
                    _visualStartPositions[i],
                    _visualTargetPositions[i],
                    placementEase);
                visual.localRotation = Quaternion.Slerp(
                    _visualStartRotations[i],
                    _visualTargetRotations[i],
                    placementEase);

                float bounceTime = Mathf.Clamp01(elapsed / addBounceDuration);
                float scaleMultiplier;
                if (bounceTime < 0.62f)
                {
                    float riseTime = bounceTime / 0.62f;
                    float riseEase = 1f - Mathf.Pow(1f - riseTime, 3f);
                    scaleMultiplier = Mathf.Lerp(0.58f, addScaleOvershoot, riseEase);
                }
                else
                {
                    float settleTime = (bounceTime - 0.62f) / 0.38f;
                    float settleEase = settleTime * settleTime * (3f - (2f * settleTime));
                    scaleMultiplier = Mathf.Lerp(addScaleOvershoot, 1f, settleEase);
                }

                visual.localScale = _visualBaseScales[i] * scaleMultiplier;
                if (elapsed < animationDuration)
                {
                    _hasVisualAnimations = true;
                }
                else
                {
                    _visualAnimationElapsed[i] = -1f;
                    ApplyFinalVisualPose(i);
                }
            }
        }

        private void OnDisable()
        {
            FinishVisualAnimations();
        }

        private void EnsureVisualPool()
        {
            if (itemVisualPrefab == null)
            {
                return;
            }

            Transform parent = VisualRoot;
            while (_visualItems.Count < capacity)
            {
                GameObject visual = Instantiate(itemVisualPrefab, parent);
                visual.name = $"Carried {displayedResourceType} {_visualItems.Count + 1:00}";
                visual.SetActive(false);
                _visualItems.Add(visual);
                _visualBaseScales.Add(visual.transform.localScale);
                _visualStartPositions.Add(Vector3.zero);
                _visualTargetPositions.Add(Vector3.zero);
                _visualStartRotations.Add(Quaternion.identity);
                _visualTargetRotations.Add(Quaternion.identity);
                _visualAnimationElapsed.Add(-1f);
            }
        }

        private void RefreshVisuals()
        {
            EnsureVisualPool();
            int visibleCount = Mathf.Min(GetAmount(displayedResourceType), _visualItems.Count);

            for (int i = 0; i < _visualItems.Count; i++)
            {
                GameObject visual = _visualItems[i];
                bool shouldBeVisible = i < visibleCount;
                if (!shouldBeVisible)
                {
                    visual.SetActive(false);
                    _visualAnimationElapsed[i] = -1f;
                    continue;
                }

                int row = i / itemsPerRow;
                int column = i % itemsPerRow;
                float horizontalSlot = GetAlternatingSlot(column);
                Vector3 targetPosition = new Vector3(
                    horizontalSlot * horizontalSpacing,
                    row * verticalSpacing,
                    -(column % 2) * depthSpacing);
                Quaternion targetRotation = Quaternion.Euler(
                    0f,
                    row % 2 == 0 ? -5f : 5f,
                    0f);
                _visualTargetPositions[i] = targetPosition;
                _visualTargetRotations[i] = targetRotation;

                bool isNewVisual = i >= _lastVisibleCount || !visual.activeSelf;
                visual.SetActive(true);
                if (isNewVisual)
                {
                    _visualStartPositions[i] = targetPosition + (Vector3.down * addLift);
                    _visualStartRotations[i] = targetRotation * Quaternion.Euler(0f, 0f, -16f);
                    _visualAnimationElapsed[i] = 0f;
                    visual.transform.localPosition = _visualStartPositions[i];
                    visual.transform.localRotation = _visualStartRotations[i];
                    visual.transform.localScale = _visualBaseScales[i] * 0.58f;
                    _hasVisualAnimations = true;
                }
                else if (_visualAnimationElapsed[i] < 0f)
                {
                    ApplyFinalVisualPose(i);
                }
            }

            _lastVisibleCount = visibleCount;
        }

        private void FinishVisualAnimations()
        {
            _hasVisualAnimations = false;
            for (int i = 0; i < _visualItems.Count; i++)
            {
                if (_visualItems[i].activeSelf)
                {
                    ApplyFinalVisualPose(i);
                }

                _visualAnimationElapsed[i] = -1f;
            }
        }

        private void ApplyFinalVisualPose(int index)
        {
            Transform visual = _visualItems[index].transform;
            visual.localPosition = _visualTargetPositions[index];
            visual.localRotation = _visualTargetRotations[index];
            visual.localScale = _visualBaseScales[index];
        }

        private static float GetAlternatingSlot(int column)
        {
            if (column == 0)
            {
                return 0f;
            }

            int distanceFromCenter = (column + 1) / 2;
            return column % 2 == 1 ? -distanceFromCenter : distanceFromCenter;
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
            itemsPerRow = Mathf.Max(1, itemsPerRow);
            horizontalSpacing = Mathf.Max(0f, horizontalSpacing);
            verticalSpacing = Mathf.Max(0f, verticalSpacing);
            depthSpacing = Mathf.Max(0f, depthSpacing);
            placementDuration = Mathf.Max(0.01f, placementDuration);
            addBounceDuration = Mathf.Max(0.01f, addBounceDuration);
            addScaleOvershoot = Mathf.Clamp(addScaleOvershoot, 1f, 1.5f);
            addLift = Mathf.Max(0f, addLift);
        }
    }
}
