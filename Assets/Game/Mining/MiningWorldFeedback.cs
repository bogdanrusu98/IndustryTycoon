using UnityEngine;

namespace IndustryTycoon.Mining
{
    /// <summary>
    /// Event-driven world-space status copy for the compact Mining slice. It owns
    /// no gameplay state and adds no per-frame callback; the Iron Vein's active
    /// cycle drives only the small progress fill transform.
    /// </summary>
    public sealed class MiningWorldFeedback : MonoBehaviour
    {
        [Header("Manual Mining")]
        [SerializeField] private IronVein ironVein;
        [SerializeField] private TextMesh miningStatusText;
        [SerializeField] private Transform miningProgressFill;
        [SerializeField] private float miningProgressWidth = 2.4f;

        [Header("Smelter")]
        [SerializeField] private Smelter smelter;
        [SerializeField] private TextMesh smelterInputText;
        [SerializeField] private TextMesh smelterOutputText;
        [SerializeField] private TextMesh smelterStatusText;

        [Header("Drill Storage")]
        [SerializeField] private OreStorage oreStorage;
        [SerializeField] private TextMesh oreStorageText;
        [SerializeField] private GameObject storedOreVisual;
        [SerializeField] private AutomatedDrill drill;
        [SerializeField] private TextMesh drillStatusText;

        private void OnEnable()
        {
            if (ironVein != null)
            {
                ironVein.ProgressChanged += HandleMiningProgressChanged;
                ironVein.EligibilityChanged += HandleMiningEligibilityChanged;
            }

            if (smelter != null)
            {
                smelter.BufferChanged += HandleSmelterBufferChanged;
                smelter.ProcessingChanged += HandleSmelterProcessingChanged;
            }

            if (oreStorage != null)
            {
                oreStorage.StateChanged += HandleOreStorageChanged;
            }

            if (drill != null)
            {
                drill.StateChanged += HandleDrillStateChanged;
            }

            RefreshAll();
        }

        private void OnDisable()
        {
            if (ironVein != null)
            {
                ironVein.ProgressChanged -= HandleMiningProgressChanged;
                ironVein.EligibilityChanged -= HandleMiningEligibilityChanged;
            }

            if (smelter != null)
            {
                smelter.BufferChanged -= HandleSmelterBufferChanged;
                smelter.ProcessingChanged -= HandleSmelterProcessingChanged;
            }

            if (oreStorage != null)
            {
                oreStorage.StateChanged -= HandleOreStorageChanged;
            }

            if (drill != null)
            {
                drill.StateChanged -= HandleDrillStateChanged;
            }
        }

        private void RefreshAll()
        {
            HandleMiningProgressChanged(ironVein != null ? ironVein.Progress01 : 0f);
            HandleMiningEligibilityChanged(ironVein != null && ironVein.IsEligible);
            HandleSmelterBufferChanged(
                smelter != null ? smelter.InputOre : 0,
                smelter != null ? smelter.ProcessingInputOre : 0,
                smelter != null ? smelter.OutputBars : 0,
                smelter != null ? smelter.ReservedOutputCapacity : 0);
            HandleSmelterProcessingChanged(smelter != null && smelter.IsProcessing);
            HandleOreStorageChanged(
                oreStorage != null ? oreStorage.StoredOre : 0,
                oreStorage != null ? oreStorage.IncomingReservations : 0);
            HandleDrillStateChanged(
                drill != null ? drill.State : AutomatedDrillState.Disabled);
        }

        private void HandleMiningProgressChanged(float progress)
        {
            if (miningProgressFill == null)
            {
                return;
            }

            float normalized = Mathf.Clamp01(progress);
            Vector3 scale = miningProgressFill.localScale;
            scale.x = miningProgressWidth * normalized;
            miningProgressFill.localScale = scale;
            Vector3 position = miningProgressFill.localPosition;
            position.x = (normalized - 1f) * miningProgressWidth * 0.5f;
            miningProgressFill.localPosition = position;
        }

        private void HandleMiningEligibilityChanged(bool eligible)
        {
            if (miningStatusText == null)
            {
                return;
            }

            if (ironVein != null && ironVein.IsPausedByCarry)
            {
                miningStatusText.text = "MINING PAUSED — EMPTY OR CHANGE CARRY";
            }
            else if (eligible)
            {
                miningStatusText.text = "MINING IRON ORE...";
            }
            else
            {
                miningStatusText.text = "STAND IN RADIUS TO MINE";
            }
        }

        private void HandleSmelterBufferChanged(
            int inputOre,
            int processingInputOre,
            int outputBars,
            int reservedOutputCapacity)
        {
            if (smelterInputText != null)
            {
                int capacity = smelter != null ? smelter.InputCapacity : 24;
                smelterInputText.text = $"ORE INPUT\n{inputOre + processingInputOre} / {capacity}";
            }

            if (smelterOutputText != null)
            {
                int capacity = smelter != null ? smelter.OutputCapacity : 12;
                smelterOutputText.text = $"BAR OUTPUT\n{outputBars} / {capacity}";
            }

            HandleSmelterProcessingChanged(smelter != null && smelter.IsProcessing);
        }

        private void HandleSmelterProcessingChanged(bool processing)
        {
            if (smelterStatusText == null)
            {
                return;
            }

            if (processing)
            {
                smelterStatusText.text = "SMELTING";
            }
            else if (smelter != null && smelter.IsOutputFull)
            {
                smelterStatusText.text = "OUTPUT FULL";
            }
            else
            {
                smelterStatusText.text = "WAITING FOR 2 ORE";
            }
        }

        private void HandleOreStorageChanged(int storedOre, int incomingReservations)
        {
            if (oreStorageText != null)
            {
                int capacity = oreStorage != null ? oreStorage.Capacity : 30;
                oreStorageText.text = $"ORE STORAGE\n{storedOre} / {capacity}";
            }

            if (storedOreVisual != null)
            {
                storedOreVisual.SetActive(storedOre > 0);
                if (storedOre > 0)
                {
                    float normalized = oreStorage != null
                        ? Mathf.Clamp01((float)storedOre / oreStorage.Capacity)
                        : 0f;
                    storedOreVisual.transform.localScale = new Vector3(
                        0.75f + (0.60f * normalized),
                        0.20f + (0.35f * normalized),
                        0.55f + (0.27f * normalized));
                }
            }
        }

        private void HandleDrillStateChanged(AutomatedDrillState state)
        {
            if (drillStatusText == null)
            {
                return;
            }

            switch (state)
            {
                case AutomatedDrillState.Producing:
                    drillStatusText.text = "DRILLING";
                    break;
                case AutomatedDrillState.StorageFull:
                    drillStatusText.text = "PAUSED — STORAGE FULL";
                    break;
                case AutomatedDrillState.MissingStorage:
                    drillStatusText.text = "STORAGE OFFLINE";
                    break;
                case AutomatedDrillState.Idle:
                    drillStatusText.text = "READY";
                    break;
                default:
                    drillStatusText.text = "LOCKED";
                    break;
            }
        }

        private void OnValidate()
        {
            miningProgressWidth = Mathf.Max(0.1f, miningProgressWidth);
        }
    }
}
