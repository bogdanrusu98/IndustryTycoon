using System.Collections.Generic;
using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class WoodStockpileFeedback : MonoBehaviour
    {
        private sealed class VisualSlot
        {
            public GameObject Visual;
            public Vector3 BaseScale;
            public float PopElapsed = -1f;
        }

        [Header("References")]
        [SerializeField] private WoodStockpile stockpile;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject woodVisualPrefab;
        [SerializeField] private TextMesh amountText;
        [SerializeField] private ParticleSystem depositParticles;

        [Header("Capped Visuals")]
        [SerializeField, Range(1, 12)] private int maximumVisualItems = 10;
        [SerializeField, Min(1)] private int woodPerVisual = 3;
        [SerializeField, Min(1)] private int itemsPerRow = 5;
        [SerializeField, Range(0.4f, 1f)] private float visualScale = 0.68f;
        [SerializeField, Min(0f)] private float horizontalSpacing = 0.52f;
        [SerializeField, Min(0f)] private float depthSpacing = 0.48f;

        [Header("Feel")]
        [SerializeField, Min(0.05f)] private float popDuration = 0.16f;

        private readonly List<VisualSlot> _slots = new List<VisualSlot>(10);
        private bool _hasActivePops;
        private int _visibleCount;
        private int _lastStoredWood;

        public int MaximumVisualItems => maximumVisualItems;
        public int WoodPerVisual => woodPerVisual;
        public float VisualScale => visualScale;
        public int VisualPoolCount => _slots.Count;
        public int VisibleVisualCount => _visibleCount;
        public float PopDuration => popDuration;
        public int DepositFeedbackCount { get; private set; }

        private void Awake()
        {
            EnsureVisualPool();
            _lastStoredWood = stockpile != null ? stockpile.StoredWood : 0;
            RefreshVisuals(_lastStoredWood, false);
        }

        private void OnEnable()
        {
            if (stockpile == null)
            {
                return;
            }

            stockpile.StateChanged += HandleStateChanged;
            stockpile.WoodDeposited += HandleWoodDeposited;
            _lastStoredWood = stockpile.StoredWood;
            RefreshVisuals(_lastStoredWood, false);
        }

        private void OnDisable()
        {
            if (stockpile != null)
            {
                stockpile.StateChanged -= HandleStateChanged;
                stockpile.WoodDeposited -= HandleWoodDeposited;
            }

            FinishPops();
        }

        private void Update()
        {
            if (!_hasActivePops)
            {
                return;
            }

            _hasActivePops = false;
            for (int i = 0; i < _slots.Count; i++)
            {
                VisualSlot slot = _slots[i];
                if (slot.PopElapsed < 0f || !slot.Visual.activeSelf)
                {
                    continue;
                }

                slot.PopElapsed = Mathf.Min(popDuration, slot.PopElapsed + Time.deltaTime);
                float normalizedTime = Mathf.Clamp01(slot.PopElapsed / popDuration);
                slot.Visual.transform.localScale = slot.BaseScale
                                                   * FeedbackTween.EaseOutBack(normalizedTime);
                if (normalizedTime >= 1f)
                {
                    slot.PopElapsed = -1f;
                    slot.Visual.transform.localScale = slot.BaseScale;
                }
                else
                {
                    _hasActivePops = true;
                }
            }
        }

        private void HandleStateChanged(int storedWood, int incomingReservations)
        {
            bool storedWoodIncreased = storedWood > _lastStoredWood;
            _lastStoredWood = storedWood;
            RefreshVisuals(storedWood, storedWoodIncreased);
        }

        private void HandleWoodDeposited(int storedWood)
        {
            DepositFeedbackCount++;
            depositParticles?.Emit(3);
        }

        private void EnsureVisualPool()
        {
            if (woodVisualPrefab == null)
            {
                return;
            }

            Transform parent = visualRoot != null ? visualRoot : transform;
            while (_slots.Count < maximumVisualItems)
            {
                GameObject visual = Instantiate(woodVisualPrefab, parent);
                visual.name = $"Stockpile Bundle {_slots.Count + 1:00}";
                Vector3 baseScale = visual.transform.localScale * visualScale;
                visual.transform.localScale = baseScale;
                visual.SetActive(false);
                _slots.Add(new VisualSlot
                {
                    Visual = visual,
                    BaseScale = baseScale
                });
            }
        }

        private void RefreshVisuals(int storedWood, bool animateTop)
        {
            EnsureVisualPool();
            int requestedVisuals = storedWood <= 0
                ? 0
                : Mathf.CeilToInt(storedWood / (float)woodPerVisual);
            int nextVisibleCount = Mathf.Min(requestedVisuals, _slots.Count);

            for (int i = 0; i < _slots.Count; i++)
            {
                VisualSlot slot = _slots[i];
                bool shouldBeVisible = i < nextVisibleCount;
                if (!shouldBeVisible)
                {
                    slot.PopElapsed = -1f;
                    slot.Visual.SetActive(false);
                    continue;
                }

                int row = i / itemsPerRow;
                int column = i % itemsPerRow;
                float horizontalSlot = GetAlternatingSlot(column);
                Transform visualTransform = slot.Visual.transform;
                visualTransform.localPosition = new Vector3(
                    horizontalSlot * horizontalSpacing,
                    row * 0.17f,
                    row * depthSpacing);
                visualTransform.localRotation = Quaternion.Euler(
                    0f,
                    (i & 1) == 0 ? -6f : 6f,
                    0f);

                bool becameVisible = !slot.Visual.activeSelf;
                slot.Visual.SetActive(true);
                if (becameVisible || (animateTop && i == nextVisibleCount - 1))
                {
                    slot.PopElapsed = 0f;
                    visualTransform.localScale = Vector3.zero;
                    _hasActivePops = true;
                }
                else if (slot.PopElapsed < 0f)
                {
                    visualTransform.localScale = slot.BaseScale;
                }
            }

            _visibleCount = nextVisibleCount;
            if (amountText != null && stockpile != null)
            {
                amountText.text = $"WOOD {storedWood} / {stockpile.Capacity}";
            }
        }

        private void FinishPops()
        {
            _hasActivePops = false;
            for (int i = 0; i < _slots.Count; i++)
            {
                VisualSlot slot = _slots[i];
                slot.PopElapsed = -1f;
                if (slot.Visual.activeSelf)
                {
                    slot.Visual.transform.localScale = slot.BaseScale;
                }
            }
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
            maximumVisualItems = Mathf.Clamp(maximumVisualItems, 1, 12);
            woodPerVisual = Mathf.Max(1, woodPerVisual);
            itemsPerRow = Mathf.Max(1, itemsPerRow);
            visualScale = Mathf.Clamp(visualScale, 0.4f, 1f);
            horizontalSpacing = Mathf.Max(0f, horizontalSpacing);
            depthSpacing = Mathf.Max(0f, depthSpacing);
            popDuration = Mathf.Max(0.05f, popDuration);
        }
    }
}
