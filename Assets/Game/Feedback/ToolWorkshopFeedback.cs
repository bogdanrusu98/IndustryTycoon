using IndustryTycoon.Processing;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public enum ToolWorkshopFeedbackState
    {
        WaitingForInput,
        Working,
        OutputFull,
        Idle
    }

    public sealed class ToolWorkshopFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ToolWorkshop workshop;
        [SerializeField] private GameObject outputVisual;
        [SerializeField] private TextMesh plankInputText;
        [SerializeField] private TextMesh ironBarInputText;
        [SerializeField] private TextMesh outputText;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private Renderer statusIndicator;
        [SerializeField] private Material idleMaterial;
        [SerializeField] private Material workingMaterial;
        [SerializeField] private Material outputFullMaterial;
        [SerializeField] private ParticleSystem completionParticles;

        private int _storedPlanks;
        private int _processingPlanks;
        private int _storedIronBars;
        private int _processingIronBars;
        private int _storedTools;
        private int _reservedOutputCapacity;

        public ToolWorkshop Workshop => workshop;
        public GameObject OutputVisual => outputVisual;
        public bool IsOutputVisualVisible => outputVisual != null
                                             && outputVisual.activeSelf;
        public int CompletionFeedbackCount { get; private set; }
        public ToolWorkshopFeedbackState DisplayedState { get; private set; }

        private void Awake()
        {
            SynchronizeFromWorkshop();
            RefreshAll();
        }

        private void OnEnable()
        {
            if (workshop != null)
            {
                workshop.BufferChanged += HandleBufferChanged;
                workshop.RecipeCompleted += HandleRecipeCompleted;
            }

            SynchronizeFromWorkshop();
            RefreshAll();
        }

        private void OnDisable()
        {
            if (workshop != null)
            {
                workshop.BufferChanged -= HandleBufferChanged;
                workshop.RecipeCompleted -= HandleRecipeCompleted;
            }
        }

        private void HandleBufferChanged(
            int storedPlanks,
            int processingPlanks,
            int storedIronBars,
            int processingIronBars,
            int storedTools,
            int reservedOutputCapacity)
        {
            _storedPlanks = storedPlanks;
            _processingPlanks = processingPlanks;
            _storedIronBars = storedIronBars;
            _processingIronBars = processingIronBars;
            _storedTools = storedTools;
            _reservedOutputCapacity = reservedOutputCapacity;
            RefreshAll();
        }

        private void HandleRecipeCompleted(
            int consumedPlanks,
            int consumedIronBars,
            int producedTools)
        {
            CompletionFeedbackCount++;
            completionParticles?.Emit(8);
        }

        private void SynchronizeFromWorkshop()
        {
            _storedPlanks = workshop != null ? workshop.InputPlanks : 0;
            _processingPlanks = workshop != null
                ? workshop.ProcessingInputPlanks
                : 0;
            _storedIronBars = workshop != null ? workshop.InputIronBars : 0;
            _processingIronBars = workshop != null
                ? workshop.ProcessingInputIronBars
                : 0;
            _storedTools = workshop != null ? workshop.OutputTools : 0;
            _reservedOutputCapacity = workshop != null
                ? workshop.ReservedOutputCapacity
                : 0;
        }

        private void RefreshAll()
        {
            RefreshLabels();
            RefreshStatus();
            if (outputVisual != null)
            {
                outputVisual.SetActive(_storedTools > 0);
            }
        }

        private void RefreshLabels()
        {
            if (plankInputText != null)
            {
                int capacity = workshop != null ? workshop.PlankInputCapacity : 0;
                plankInputText.text = _processingPlanks > 0
                    ? $"PLANK IN  {_storedPlanks} + {_processingPlanks} / {capacity}"
                    : $"PLANK IN  {_storedPlanks} / {capacity}";
            }

            if (ironBarInputText != null)
            {
                int capacity = workshop != null ? workshop.IronBarInputCapacity : 0;
                ironBarInputText.text = _processingIronBars > 0
                    ? $"IRON BAR IN  {_storedIronBars} + {_processingIronBars} / {capacity}"
                    : $"IRON BAR IN  {_storedIronBars} / {capacity}";
            }

            if (outputText != null)
            {
                int capacity = workshop != null ? workshop.OutputCapacity : 0;
                outputText.text = _reservedOutputCapacity > 0
                    ? $"TOOL OUT  {_storedTools} + {_reservedOutputCapacity} / {capacity}"
                    : $"TOOL OUT  {_storedTools} / {capacity}";
            }
        }

        private void RefreshStatus()
        {
            ToolWorkshopFeedbackState state = ResolveState();
            DisplayedState = state;

            if (statusText != null)
            {
                switch (state)
                {
                    case ToolWorkshopFeedbackState.Working:
                        statusText.text = "WORKSHOP  WORKING";
                        break;
                    case ToolWorkshopFeedbackState.OutputFull:
                        statusText.text = "TOOL OUTPUT FULL";
                        break;
                    case ToolWorkshopFeedbackState.WaitingForInput:
                        statusText.text = BuildStarvationStatus();
                        break;
                    default:
                        statusText.text = "WORKSHOP  IDLE";
                        break;
                }
            }

            if (statusIndicator == null)
            {
                return;
            }

            Material stateMaterial = state == ToolWorkshopFeedbackState.Working
                ? workingMaterial
                : state == ToolWorkshopFeedbackState.OutputFull
                    ? outputFullMaterial
                    : idleMaterial;
            if (stateMaterial != null)
            {
                statusIndicator.sharedMaterial = stateMaterial;
            }
        }

        private ToolWorkshopFeedbackState ResolveState()
        {
            if (workshop == null)
            {
                return ToolWorkshopFeedbackState.Idle;
            }

            if (workshop.IsProcessing)
            {
                return ToolWorkshopFeedbackState.Working;
            }

            if (_storedTools + _reservedOutputCapacity >= workshop.OutputCapacity)
            {
                return ToolWorkshopFeedbackState.OutputFull;
            }

            if (_storedPlanks < workshop.RecipeInputPlanks
                || _storedIronBars < workshop.RecipeInputIronBars)
            {
                return ToolWorkshopFeedbackState.WaitingForInput;
            }

            return ToolWorkshopFeedbackState.Idle;
        }

        private string BuildStarvationStatus()
        {
            bool needsPlanks = workshop != null
                               && _storedPlanks < workshop.RecipeInputPlanks;
            bool needsIronBars = workshop != null
                                 && _storedIronBars < workshop.RecipeInputIronBars;
            if (needsPlanks && needsIronBars)
            {
                return "NEEDS PLANKS + IRON";
            }

            return needsPlanks ? "NEEDS PLANKS" : "NEEDS IRON BARS";
        }
    }
}
