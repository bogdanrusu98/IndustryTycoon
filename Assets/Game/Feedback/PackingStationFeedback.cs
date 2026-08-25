using System.Collections.Generic;
using IndustryTycoon.Core;
using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public enum PackingStationFeedbackState
    {
        WaitingForInput,
        Working,
        OutputFull,
        Idle
    }

    public sealed class PackingStationFeedback : MonoBehaviour
    {
        private sealed class OutputSlot
        {
            public GameObject Visual;
            public Vector3 BaseScale;
            public float PopElapsed = -1f;
        }

        [Header("References")]
        [SerializeField] private PackingStation station;
        [SerializeField] private Transform workingPart;
        [SerializeField] private Transform outputVisualRoot;
        [SerializeField] private GameObject resourceVisualPrefab;
        [SerializeField] private TextMesh inputText;
        [SerializeField] private TextMesh outputText;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private Renderer statusIndicator;
        [SerializeField] private Material idleMaterial;
        [SerializeField] private Material workingMaterial;
        [SerializeField] private Material outputFullMaterial;
        [SerializeField] private ParticleSystem completionParticles;

        [Header("Capped Output Visuals")]
        [SerializeField, Range(1, 12)] private int maximumOutputVisuals = 6;
        [SerializeField, Min(1)] private int cratesPerVisual = 2;
        [SerializeField, Min(1)] private int itemsPerRow = 3;
        [SerializeField, Range(0.4f, 1.2f)] private float visualScale = 0.78f;
        [SerializeField, Min(0f)] private float horizontalSpacing = 0.72f;
        [SerializeField, Min(0f)] private float verticalSpacing = 0.24f;
        [SerializeField, Min(0f)] private float depthSpacing = 0.52f;

        [Header("Feel")]
        [SerializeField, Min(1f)] private float workingRotationSpeed = 220f;
        [SerializeField, Min(0.05f)] private float outputPopDuration = 0.16f;

        private readonly List<OutputSlot> _outputSlots = new List<OutputSlot>(6);
        private int _inputPlanks;
        private int _processingInputPlanks;
        private int _outputCrates;
        private int _reservedOutputCapacity;
        private int _visibleOutputCount;
        private int _lastOutputCrates;
        private bool _hasActivePops;

        public int MaximumOutputVisuals => maximumOutputVisuals;
        public int CratesPerVisual => cratesPerVisual;
        public int OutputVisualPoolCount => _outputSlots.Count;
        public int VisibleOutputVisualCount => _visibleOutputCount;
        public float WorkingRotationSpeed => workingRotationSpeed;
        public float OutputPopDuration => outputPopDuration;
        public int CompletionFeedbackCount { get; private set; }
        public PackingStationFeedbackState DisplayedState { get; private set; }

        private void Awake()
        {
            EnsureOutputPool();
            SynchronizeFromStation();
            RefreshAll(false);
        }

        private void OnEnable()
        {
            if (station == null)
            {
                RefreshAll(false);
                return;
            }

            station.BufferChanged += HandleBufferChanged;
            station.ProcessingChanged += HandleProcessingChanged;
            station.RecipeCompleted += HandleRecipeCompleted;
            SynchronizeFromStation();
            RefreshAll(false);
        }

        private void OnDisable()
        {
            if (station != null)
            {
                station.BufferChanged -= HandleBufferChanged;
                station.ProcessingChanged -= HandleProcessingChanged;
                station.RecipeCompleted -= HandleRecipeCompleted;
            }

            FinishPops();
        }

        private void Update()
        {
            if (station != null && station.IsProcessing && workingPart != null)
            {
                workingPart.Rotate(
                    Vector3.up,
                    workingRotationSpeed * Time.deltaTime,
                    Space.Self);
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

                slot.PopElapsed = Mathf.Min(
                    outputPopDuration,
                    slot.PopElapsed + Time.deltaTime);
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

        private void HandleBufferChanged(
            int inputStored,
            int inFlightInput,
            int outputStored,
            int outputReserved)
        {
            bool outputIncreased = outputStored > _lastOutputCrates;
            _inputPlanks = inputStored;
            _processingInputPlanks = inFlightInput;
            _outputCrates = outputStored;
            _reservedOutputCapacity = outputReserved;
            _lastOutputCrates = outputStored;
            RefreshLabels();
            RefreshStatus();
            RefreshOutputVisuals(outputIncreased);
        }

        private void HandleProcessingChanged(bool isProcessing)
        {
            RefreshStatus();
        }

        private void HandleRecipeCompleted(int inputPlanks, int outputCrates)
        {
            CompletionFeedbackCount++;
            completionParticles?.Emit(8);
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
                visual.name = $"Packing Output Crates {_outputSlots.Count + 1:00}";
                ResourceVisual selector = visual.GetComponent<ResourceVisual>();
                selector?.Show(ResourceType.Crate);
                visual.SetActive(false);
                _outputSlots.Add(new OutputSlot
                {
                    Visual = visual,
                    BaseScale = visual.transform.localScale * visualScale
                });
            }
        }

        private void SynchronizeFromStation()
        {
            _inputPlanks = station != null ? station.InputPlanks : 0;
            _processingInputPlanks = station != null ? station.ProcessingInputPlanks : 0;
            _outputCrates = station != null ? station.OutputCrates : 0;
            _reservedOutputCapacity = station != null ? station.ReservedOutputCapacity : 0;
            _lastOutputCrates = _outputCrates;
        }

        private void RefreshAll(bool animateOutput)
        {
            RefreshLabels();
            RefreshStatus();
            RefreshOutputVisuals(animateOutput);
        }

        private void RefreshLabels()
        {
            if (inputText != null)
            {
                int inputCapacity = station != null ? station.InputCapacity : 0;
                inputText.text = _processingInputPlanks > 0
                    ? $"PLANK IN  {_inputPlanks} + {_processingInputPlanks} / {inputCapacity}"
                    : $"PLANK IN  {_inputPlanks} / {inputCapacity}";
            }

            if (outputText != null)
            {
                int outputCapacity = station != null ? station.OutputCapacity : 0;
                outputText.text = _reservedOutputCapacity > 0
                    ? $"CRATE OUT  {_outputCrates} + {_reservedOutputCapacity} / {outputCapacity}"
                    : $"CRATE OUT  {_outputCrates} / {outputCapacity}";
            }
        }

        private void RefreshStatus()
        {
            PackingStationFeedbackState state = ResolveState();
            DisplayedState = state;

            if (statusText != null)
            {
                switch (state)
                {
                    case PackingStationFeedbackState.Working:
                        statusText.text = "PACKER  WORKING";
                        break;
                    case PackingStationFeedbackState.OutputFull:
                        statusText.text = "CRATE OUTPUT FULL";
                        break;
                    case PackingStationFeedbackState.WaitingForInput:
                        statusText.text = "PACKER  NO PLANKS";
                        break;
                    default:
                        statusText.text = "PACKER  IDLE";
                        break;
                }
            }

            if (statusIndicator == null)
            {
                return;
            }

            Material stateMaterial = state == PackingStationFeedbackState.Working
                ? workingMaterial
                : state == PackingStationFeedbackState.OutputFull
                    ? outputFullMaterial
                    : idleMaterial;
            if (stateMaterial != null)
            {
                statusIndicator.sharedMaterial = stateMaterial;
            }
        }

        private PackingStationFeedbackState ResolveState()
        {
            if (station == null)
            {
                return PackingStationFeedbackState.Idle;
            }

            if (station.IsProcessing)
            {
                return PackingStationFeedbackState.Working;
            }

            if (_outputCrates + _reservedOutputCapacity >= station.OutputCapacity)
            {
                return PackingStationFeedbackState.OutputFull;
            }

            if (_inputPlanks < station.RecipeInputPlanks)
            {
                return PackingStationFeedbackState.WaitingForInput;
            }

            return PackingStationFeedbackState.Idle;
        }

        private void RefreshOutputVisuals(bool animateTop)
        {
            int requestedVisuals = _outputCrates <= 0
                ? 0
                : Mathf.CeilToInt(_outputCrates / (float)cratesPerVisual);
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
                int itemsInRow = Mathf.Min(itemsPerRow, visibleCount - (row * itemsPerRow));
                float centeredColumn = column - ((itemsInRow - 1) * 0.5f);
                slot.Visual.transform.localPosition = new Vector3(
                    centeredColumn * horizontalSpacing,
                    row * verticalSpacing,
                    -(row * depthSpacing));
                slot.Visual.transform.localRotation = Quaternion.Euler(
                    0f,
                    row % 2 == 0 ? -6f : 6f,
                    0f);
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
            cratesPerVisual = Mathf.Max(1, cratesPerVisual);
            itemsPerRow = Mathf.Max(1, itemsPerRow);
            visualScale = Mathf.Clamp(visualScale, 0.4f, 1.2f);
            horizontalSpacing = Mathf.Max(0f, horizontalSpacing);
            verticalSpacing = Mathf.Max(0f, verticalSpacing);
            depthSpacing = Mathf.Max(0f, depthSpacing);
            workingRotationSpeed = Mathf.Max(1f, workingRotationSpeed);
            outputPopDuration = Mathf.Max(0.05f, outputPopDuration);
        }
    }
}
