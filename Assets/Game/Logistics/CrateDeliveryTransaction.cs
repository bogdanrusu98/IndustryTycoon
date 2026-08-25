using IndustryTycoon.Economy;

namespace IndustryTycoon.Logistics
{
    internal static class CrateDeliveryTransaction
    {
        internal static bool TryCommit(
            CrateCourier courier,
            uint tripGeneration,
            CashPile cashPile)
        {
            if (courier == null
                || cashPile == null
                || !courier.TryGetPendingDelivery(
                    tripGeneration,
                    out int crateCount,
                    out int cashValue)
                || !cashPile.CanDeposit(cashValue))
            {
                return false;
            }

            courier.FinalizeDeliveryForTransfer(
                tripGeneration,
                crateCount,
                cashValue);
            cashPile.FinalizeDepositForTransfer(cashValue);

            courier.PublishDeliveryCommitted(
                tripGeneration,
                crateCount,
                cashValue);
            cashPile.PublishDepositCommitted();
            return true;
        }
    }
}
