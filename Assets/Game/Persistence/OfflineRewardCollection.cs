using System;

namespace IndustryTycoon.Persistence
{
    public static class OfflineRewardCollection
    {
        public static bool TryCollect(
            M9SaveData data,
            int walletBalance,
            double multiplier,
            out int creditedCash,
            out int resultingWalletBalance)
        {
            creditedCash = 0;
            resultingWalletBalance = walletBalance;
            if (data == null
                || !data.returnScreenPending
                || data.pendingOfflineCash < 0
                || walletBalance < 0
                || multiplier < 1d
                || double.IsNaN(multiplier)
                || double.IsInfinity(multiplier))
            {
                return false;
            }

            double scaledReward = data.pendingOfflineCash * multiplier;
            if (scaledReward < 0d
                || scaledReward > int.MaxValue
                || double.IsNaN(scaledReward)
                || double.IsInfinity(scaledReward))
            {
                return false;
            }

            long wholeReward = (long)Math.Floor(scaledReward);
            long resultingBalance = (long)walletBalance + wholeReward;
            if (wholeReward < 0L || resultingBalance > int.MaxValue)
            {
                return false;
            }

            creditedCash = (int)wholeReward;
            resultingWalletBalance = (int)resultingBalance;
            data.pendingOfflineCash = 0;
            data.pendingOfflineAwaySeconds = 0L;
            data.returnScreenPending = false;
            return true;
        }
    }
}
