using IndustryTycoon.Core;
using IndustryTycoon.Interaction;
using IndustryTycoon.Player;
using IndustryTycoon.Processing;
using IndustryTycoon.Progression;
using UnityEngine;

namespace IndustryTycoon.UI
{
    /// <summary>
    /// Event-driven recovery hints for common early-session stalls. It only
    /// publishes subordinate contextual hints and never changes gameplay or objectives.
    /// </summary>
    public sealed class EarlySessionFeedback : MonoBehaviour
    {
        [SerializeField] private FirstSessionOnboardingService onboarding;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private PurchasePad[] purchasePads;
        [SerializeField] private WoodProcessor processor;
        [SerializeField] private ProcessorInputZone processorInput;
        [SerializeField] private ProcessorOutputZone processorOutput;
        [SerializeField] private PackingStation packingStation;
        [SerializeField] private PackingStationInputZone packingInput;
        [SerializeField] private PackingStationOutputZone packingOutput;

        private bool _processorFull;
        private bool _packingFull;

        private void OnEnable()
        {
            if (carryStack != null) carryStack.ItemsAdded += HandleItemsAdded;
            SetPadSubscriptions(true);
            if (processor != null) processor.BufferChanged += HandleProcessorChanged;
            if (packingStation != null) packingStation.BufferChanged += HandlePackingChanged;
            if (processorInput != null) processorInput.InteractionAttempted += HandleProcessorInput;
            if (processorOutput != null) processorOutput.InteractionAttempted += HandleProcessorOutput;
            if (packingInput != null) packingInput.InteractionAttempted += HandlePackingInput;
            if (packingOutput != null) packingOutput.InteractionAttempted += HandlePackingOutput;
        }

        private void OnDisable()
        {
            if (carryStack != null) carryStack.ItemsAdded -= HandleItemsAdded;
            SetPadSubscriptions(false);
            if (processor != null) processor.BufferChanged -= HandleProcessorChanged;
            if (packingStation != null) packingStation.BufferChanged -= HandlePackingChanged;
            if (processorInput != null) processorInput.InteractionAttempted -= HandleProcessorInput;
            if (processorOutput != null) processorOutput.InteractionAttempted -= HandleProcessorOutput;
            if (packingInput != null) packingInput.InteractionAttempted -= HandlePackingInput;
            if (packingOutput != null) packingOutput.InteractionAttempted -= HandlePackingOutput;
        }

        private void SetPadSubscriptions(bool subscribe)
        {
            if (purchasePads == null) return;
            for (int i = 0; i < purchasePads.Length; i++)
            {
                PurchasePad pad = purchasePads[i];
                if (pad == null) continue;
                if (subscribe) pad.FundingPaused += HandleFundingPaused;
                else pad.FundingPaused -= HandleFundingPaused;
            }
        }

        private void HandleFundingPaused()
        {
            PurchasePad active = null;
            if (purchasePads != null)
            {
                for (int i = 0; i < purchasePads.Length; i++)
                {
                    if (purchasePads[i] != null && purchasePads[i].IsPlayerInside)
                    {
                        active = purchasePads[i];
                        break;
                    }
                }
            }

            onboarding?.ShowContextHint(active != null
                ? $"NEED ${active.RemainingCost} — SELL GOODS FOR CASH"
                : "NOT ENOUGH CASH — SELL GOODS FIRST");
        }

        private void HandleItemsAdded(ResourceType type, int amount, int total)
        {
            if (carryStack == null || total < carryStack.Capacity) return;
            string destination = type == ResourceType.Wood
                ? "SELL WOOD OR LOAD PROCESSOR"
                : type == ResourceType.Plank
                    ? "LOAD PACKING OR SELL PLANKS"
                    : type == ResourceType.Crate
                        ? "SELL CRATES OR LET COURIER LOAD"
                        : "DELIVER OR SELL THIS RESOURCE";
            onboarding?.ShowContextHint("CARRY FULL — " + destination);
        }

        private void HandleProcessorChanged(int input, int output, int reserved)
        {
            bool full = processor != null
                        && output + reserved >= processor.OutputCapacity;
            if (full && !_processorFull)
                onboarding?.ShowContextHint("PROCESSOR OUTPUT FULL — COLLECT PLANKS");
            _processorFull = full;
        }

        private void HandlePackingChanged(int input, int processing, int output, int reserved)
        {
            bool full = packingStation != null
                        && output + reserved >= packingStation.OutputCapacity;
            if (full && !_packingFull)
                onboarding?.ShowContextHint("PACKING OUTPUT FULL — COLLECT CRATES");
            _packingFull = full;
        }

        private void HandleProcessorInput() => RequireCarried(ResourceType.Wood, "PROCESSOR INPUT NEEDS WOOD");
        private void HandleProcessorOutput() => RequireSpaceFor(ResourceType.Plank, "PROCESSOR OUTPUT: COLLECT PLANKS");
        private void HandlePackingInput() => RequireCarried(ResourceType.Plank, "PACKING INPUT NEEDS PLANKS");
        private void HandlePackingOutput() => RequireSpaceFor(ResourceType.Crate, "PACKING OUTPUT: COLLECT CRATES");

        private void RequireCarried(ResourceType expected, string emptyMessage)
        {
            if (carryStack == null) return;
            if (!carryStack.TryGetActiveResourceType(out ResourceType actual))
            {
                onboarding?.ShowContextHint(emptyMessage);
            }
            else if (actual != expected)
            {
                onboarding?.ShowContextHint($"WRONG RESOURCE — NEED {expected.ToString().ToUpperInvariant()}");
            }
        }

        private void RequireSpaceFor(ResourceType expected, string emptyMessage)
        {
            if (carryStack == null) return;
            if (carryStack.TotalAmount == 0)
            {
                onboarding?.ShowContextHint(emptyMessage);
            }
            else if (!carryStack.CanAccept(expected, 1))
            {
                onboarding?.ShowContextHint(
                    carryStack.AvailableCapacity <= 0
                        ? "CARRY FULL — EMPTY IT BEFORE COLLECTING OUTPUT"
                        : $"CARRY HOLDS ANOTHER RESOURCE — EMPTY IT FOR {expected.ToString().ToUpperInvariant()}");
            }
        }
    }
}
