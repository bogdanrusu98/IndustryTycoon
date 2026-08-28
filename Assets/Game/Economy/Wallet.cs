using System;
using UnityEngine;

namespace IndustryTycoon.Economy
{
    public sealed class Wallet : MonoBehaviour
    {
        [SerializeField, Min(0)] private int balance;

        public event Action<int> BalanceChanged;

        public int Balance => balance;

        public int Deposit(int amount)
        {
            if (amount <= 0 || balance >= int.MaxValue)
            {
                return 0;
            }

            int acceptedAmount = (int)Math.Min((long)amount, (long)int.MaxValue - balance);
            if (acceptedAmount <= 0)
            {
                return 0;
            }

            balance += acceptedAmount;
            BalanceChanged?.Invoke(balance);
            return acceptedAmount;
        }

        public int AddCash(int amount)
        {
            return Deposit(amount);
        }

        public bool RestoreBalance(int restoredBalance)
        {
            if (restoredBalance < 0)
            {
                return false;
            }

            if (balance == restoredBalance)
            {
                return true;
            }

            balance = restoredBalance;
            BalanceChanged?.Invoke(balance);
            return true;
        }

        public int SpendUpTo(int requestedAmount)
        {
            if (requestedAmount <= 0 || balance <= 0)
            {
                return 0;
            }

            int spentAmount = Math.Min(requestedAmount, balance);
            balance -= spentAmount;
            BalanceChanged?.Invoke(balance);
            return spentAmount;
        }

        private void OnValidate()
        {
            balance = Mathf.Max(0, balance);
        }
    }
}
