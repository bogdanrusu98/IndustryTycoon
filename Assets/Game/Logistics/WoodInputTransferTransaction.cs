using IndustryTycoon.Processing;
using IndustryTycoon.Workers;

namespace IndustryTycoon.Logistics
{
    public static class WoodInputTransferTransaction
    {
        public static bool TryCommit(
            WoodStockpile stockpile,
            WoodStockpileOutgoingReservation sourceReservation,
            WoodProcessor processor,
            ProcessorInputReservation destinationReservation)
        {
            if (stockpile == null
                || processor == null
                || !sourceReservation.IsValid
                || !destinationReservation.IsValid)
            {
                return false;
            }

            // Unity gameplay mutations run on the main thread. Validate both handles
            // before silently resolving either side, then publish only after both
            // authoritative states are complete so callbacks cannot observe a half-transfer.
            stockpile.FinalizeOutgoingForTransfer(sourceReservation);
            processor.CommitReservedInputForTransfer(destinationReservation);

            stockpile.PublishOutgoingTransferCommitted();
            processor.PublishInputTransferCommitted();
            return true;
        }
    }
}
