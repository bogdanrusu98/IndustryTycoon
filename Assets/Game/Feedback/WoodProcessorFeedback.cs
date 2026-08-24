using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class WoodProcessorFeedback : MonoBehaviour
    {
        private sealed class OutputSlot
        {
            public GameObject Visual;
            public Vector3 BaseScale;
            public float PopElapsed = -1f;
        }

        [Header("References")]
        [SerializeField] private WoodProcessor processor;
        [SerializeField] private Transform workingBlade;
        [SerializeField] private Transform outputVisualRoot;
        [SerializeField] private GameObject resourceVisualPrefab;
        [SerializeField] private TextMesh inputText;
        [SerializeField] private TextMesh outputText;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private ParticleSystem completionParticles;

        [Header("Capped Output Visuals")]
        [SerializeField, Range(1, 12)] private int maximumOutputVisuals = 6;
        [SerializeField, Min(1)] private int planksPerVisual = 2;
        [SerializeField, Min(1)] private int itemsPerRow = 3;
        [SerializeField, Range(0.4f, 1.2f)] private float visualScale = 0.82f;
        [SerializeField, Min(0f)] private float horizontalSpacing = 0.72f;
        [SerializeField, Min(0f)] private float verticalSpacing = 0.16f;
        [SerializeField, Min(0f)] private float depthSpacing = 0.38f;

        [Header("Feel")]
        [SerializeField, Min(1f)] private float bladeRotationSpeed = 280f;
        [SerializeField, Min(0.05f)] private float outputPopDuration = 0.16f;

        private readonly List<OutputSlot> _outputSlots = new List<OutputSlot>(6);
        private bool _hasActivePops;
        private int _visibleOutputCount;
        private int _lastOutputPlanks;

        public int MaximumOutputVisuals => maximumOutputVisuals;
        public int PlanksPerVisual => planksPerVisual;
        public int OutputVisualPoolCount => _outputSlots.Count;
        public int VisibleOutputVisualCount => _visibleOutputCount;
        public float BladeRotationSpeed => bladeRotationSpeed;
        public float OutputPopDuration => outputPopDuration;
        public int CompletionFeedbackCount { get; private set; }

        private void Awake()
        {
            EnsureOutputPool();
            _lastOutputPlanks = processor != null ? processor.OutputPlanks : 0;
            RefreshAll(false);
        }

        private void OnEnable()
        {
            if (processor == null)
            {
                return;
            }

            processor.BufferChanged += HandleBufferChanged;
            processor.ProcessingChanged += HandleProcessingChanged;
            processor.RecipeCompleted += HandleRecipeCompleted;
            _lastOutputPlanks = processor.OutputPlanks;
            RefreshAll(false);
        }

        private void OnDisable()
        {
            if (processor != null)
            {
                processor.BufferChanged -= HandleBufferChanged;
                processor.ProcessingChanged -= HandleProcessingChanged;
                processor.RecipeCompleted -= HandleRecipeCompleted;
            }

            FinishPops();
        }

        private void Update()
        {
            if (processor != null && processor.IsProcessing && workingBlade != null)
            {
                workingBlade.Rotate(Vector3.up, bladeRotationSpeed * Time.deltaTime, Space.Self);
            }

            if (!_hasActivePops)
            {
                return;
            }

            _hasActivePops = false;
            for (int i = 0; i < _outputSlots.Count; i++)
            {
                OutputSlot slot = _outputSlots[i];
                if (slot.PopElapsed < 0f || !slot.Visual.activeSelf)
                {
                    continue;
                }

                slot.PopElapsed = Mathf.Min(outputPopDuration, slot.PopElapsed + Time.deltaTime);
                float normalizedTime = Mathf.Clamp01(slot.PopElapsed / outputPopDuration);
                float scale = FeedbackTween.EaseOutBack(normalizedTime);
                slot.Visual.transform.localScale = slot.BaseScale * scale;
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

        private void HandleBufferChanged(int inputWood, int outputPlanks, int reservedOutput)
        {
            bool outputIncreased = outputPlanks > _lastOutputPlanks;
            _lastOutputPlanks = outputPlanks;
            RefreshLabels();
            RefreshOutputVisuals(outputIncreased);
        }

        private void HandleProcessingChanged(bool isProcessing)
        {
            RefreshStatus();
        }

        private void HandleRecipeCompleted(int inputWood, int outputPlanks)
        {
            CompletionFeedbackCount++;
            completionParticles?.Emit(6);
        }

        private void EnsureOutputPool()
        {
            if (resourceVisualPrefab == null)
            {
                return;
            }

            Transform parent = outputVisualRoot != null ? outputVisualRoot : transform;
            while (_outputSlots.Count < maximumOutputVisuals)
            {
                GameObject visual = Instantiate(resourceVisualPrefab, parent);
                visual.name = $"Processor Output Planks {_outputSlots.Count + 1:00}";
                ResourceVisual selector = visual.GetComponent<ResourceVisual>();
                selector?.Show(ResourceType.Plank);
                visual.SetActive(false);
                _outputSlots.Add(new OutputSlot
                {
                    Visual = visual,
                    BaseScale = visual.transform.localScale * visualScale
                });
            }
        }

        private void RefreshAll(bool animateOutput)
        {
            RefreshLabels();
            RefreshStatus();
            RefreshOutputVisuals(animateOutput);
        }

        private void RefreshLabels()
        {
            if (processor == null)
            {
                return;
            }

            if (inputText != null)
            {
                inputText.text = $"WOOD IN  {processor.InputWood} / {processor.InputCapacity}";
            }

            if (outputText != null)
            {
                outputText.text = $"PLANK OUT  {processor.OutputPlanks} / {processor.OutputCapacity}";
            }
        }

        private void RefreshStatus()
        {
            if (statusText != null)
            {
                statusText.text = processor != null && processor.IsProcessing
                    ? "SAWMILL  WORKING"
                    : "SAWMILL  IDLE";
            }
        }

        private void RefreshOutputVisuals(bool animateTop)
        {
            EnsureOutputPool();
            int outputPlanks = processor != null ? processor.OutputPlanks : 0;
            int requestedVisuals = outputPlanks <= 0
                ? 0
                : Mathf.CeilToInt(outputPlanks / (float)planksPerVisual);
            int visibleCount = Mathf.Min(requestedVisuals, _outputSlots.Count);

            for (int i = 0; i < _outputSlots.Count; i++)
            {
                OutputSlot slot = _outputSlots[i];
                bool shouldBeVisible = i < visibleCount;
                slot.Visual.SetActive(shouldBeVisible);
                if (!shouldBeVisible)
                {
                    slot.PopElapsed = -1f;
                    continue;
                }

                int row = i / itemsPerRow;
                int column = i % itemsPerRow;
                float centeredColumn = column - ((Mathf.Min(itemsPerRow, visibleCount - (row * itemsPerRow)) - 1) * 0.5f);
                slot.Visual.transform.localPosition = new Vector3(
                    centeredColumn * horizontalSpacing,
                    row * verticalSpacing,
                    -(row * depthSpacing));
                slot.Visual.transform.localRotation = Quaternion.Euler(0f, row % 2 == 0 ? -4f : 4f, 0f);
                slot.Visual.transform.localScale = slot.BaseScale;
            }

            if (animateTop && visibleCount > 0)
            {
                OutputSlot top = _outputSlots[visibleCount - 1];
                top.PopElapsed = 0f;
                top.Visual.transform.localScale = top.BaseScale * 0.45f;
                _hasActivePops = true;
            }

            _visibleOutputCount = visibleCount;
        }

        private void FinishPops()
        {
            _hasActivePops = false;
            for (int i = 0; i < _outputSlots.Count; i++)
            {
                OutputSlot slot = _outputSlots[i];
                slot.PopElapsed = -1f;
                slot.Visual.transform.localScale = slot.BaseScale;
            }
        }

        private void OnValidate()
        {
            maximumOutputVisuals = Mathf.Clamp(maximumOutputVisuals, 1, 12);
            planksPerVisual = Mathf.Max(1, planksPerVisual);
            itemsPerRow = Mathf.Max(1, itemsPerRow);
            visualScale = Mathf.Clamp(visualScale, 0.4f, 1.2f);
            horizontalSpacing = Mathf.Max(0f, horizontalSpacing);
            verticalSpacing = Mathf.Max(0f, verticalSpacing);
            depthSpacing = Mathf.Max(0f, depthSpacing);
            bladeRotationSpeed = Mathf.Max(1f, bladeRotationSpeed);
            outputPopDuration = Mathf.Max(0.05f, outputPopDuration);
        }
    }
}
