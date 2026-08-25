using IndustryTycoon.Processing;

namespace IndustryTycoon.Logistics
{
    internal static class CratePickupTransaction
    {
        internal static bool TryCommit(
            PackingStation packingStation,
            PackingStationOutputReservation reservation,
            CrateCourier courier,
            uint tripGeneration)
        {
            if (packingStation == null
                || courier == null
                || !packingStation.CanCommitCourierOutputReservation(reservation))
            {
                return false;
            }

            int crateCount = reservation.ReservedCrates;
            if (!courier.CanAcceptPickup(
                    tripGeneration,
                    reservation,
                    crateCount))
            {
                return false;
            }

            int committedCrates =
                packingStation.FinalizeCourierOutputReservation(reservation);
            courier.FinalizePickupForTransfer(
                tripGeneration,
                reservation,
                committedCrates);

            packingStation.PublishCourierOutputCommitted();
            courier.PublishPickupCommitted(tripGeneration, committedCrates);
            return true;
        }
    }
}
