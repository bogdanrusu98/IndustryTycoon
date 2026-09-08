using System;

namespace IndustryTycoon.Processing
{
    /// <summary>
    /// Owns the transient resources for one buffered recipe cycle. All inputs and
    /// output capacity are validated before any stored input is moved.
    /// </summary>
    public sealed class BufferedRecipeTransaction
    {
        private readonly int[] _requiredInputs;
        private readonly int[] _ownedInputs;
        private readonly int _outputAmount;
        private int _reservedOutputCapacity;

        public BufferedRecipeTransaction(int[] requiredInputs, int outputAmount)
        {
            if (requiredInputs == null || requiredInputs.Length == 0)
            {
                throw new ArgumentException(
                    "A buffered recipe requires at least one input.",
                    nameof(requiredInputs));
            }

            if (outputAmount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputAmount),
                    "A buffered recipe must produce a positive output amount.");
            }

            _requiredInputs = new int[requiredInputs.Length];
            _ownedInputs = new int[requiredInputs.Length];
            for (int i = 0; i < requiredInputs.Length; i++)
            {
                if (requiredInputs[i] <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(requiredInputs),
                        "Every buffered recipe input must be positive.");
                }

                _requiredInputs[i] = requiredInputs[i];
            }

            _outputAmount = outputAmount;
        }

        public int InputCount => _requiredInputs.Length;
        public int OutputAmount => _outputAmount;
        public int ReservedOutputCapacity => _reservedOutputCapacity;
        public bool HasReservation => _reservedOutputCapacity > 0;

        public int GetRequiredInput(int inputIndex)
        {
            return IsValidInputIndex(inputIndex) ? _requiredInputs[inputIndex] : 0;
        }

        public int GetOwnedInput(int inputIndex)
        {
            return IsValidInputIndex(inputIndex) ? _ownedInputs[inputIndex] : 0;
        }

        public bool TryBegin(
            int[] storedInputs,
            int storedOutput,
            int outputCapacity)
        {
            if (HasAnyOwnership()
                || !HasMatchingInputs(storedInputs)
                || storedOutput < 0
                || outputCapacity < 0
                || storedOutput > outputCapacity
                || _outputAmount > outputCapacity - storedOutput)
            {
                return false;
            }

            for (int i = 0; i < _requiredInputs.Length; i++)
            {
                if (storedInputs[i] < _requiredInputs[i])
                {
                    return false;
                }
            }

            // Nothing below can reject. Ownership moves only after every input and
            // the output reservation have passed validation.
            for (int i = 0; i < _requiredInputs.Length; i++)
            {
                storedInputs[i] -= _requiredInputs[i];
                _ownedInputs[i] = _requiredInputs[i];
            }

            _reservedOutputCapacity = _outputAmount;
            return true;
        }

        public bool TryCommit(ref int storedOutput, int outputCapacity)
        {
            if (!HasCompleteOwnership()
                || storedOutput < 0
                || outputCapacity < 0
                || storedOutput > outputCapacity
                || _reservedOutputCapacity > outputCapacity - storedOutput)
            {
                return false;
            }

            storedOutput += _reservedOutputCapacity;
            ClearOwnership();
            return true;
        }

        public bool TryRollback(int[] storedInputs, int[] inputCapacities)
        {
            if (!HasMatchingInputs(storedInputs)
                || !HasMatchingInputs(inputCapacities))
            {
                return false;
            }

            for (int i = 0; i < _ownedInputs.Length; i++)
            {
                if (storedInputs[i] < 0
                    || inputCapacities[i] < 0
                    || storedInputs[i] > inputCapacities[i]
                    || _ownedInputs[i] > inputCapacities[i] - storedInputs[i])
                {
                    return false;
                }
            }

            for (int i = 0; i < _ownedInputs.Length; i++)
            {
                storedInputs[i] += _ownedInputs[i];
            }

            ClearOwnership();
            return true;
        }

        private bool HasAnyOwnership()
        {
            if (_reservedOutputCapacity != 0)
            {
                return true;
            }

            for (int i = 0; i < _ownedInputs.Length; i++)
            {
                if (_ownedInputs[i] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasCompleteOwnership()
        {
            if (_reservedOutputCapacity != _outputAmount)
            {
                return false;
            }

            for (int i = 0; i < _ownedInputs.Length; i++)
            {
                if (_ownedInputs[i] != _requiredInputs[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasMatchingInputs(int[] inputs)
        {
            return inputs != null && inputs.Length == _requiredInputs.Length;
        }

        private bool IsValidInputIndex(int inputIndex)
        {
            return inputIndex >= 0 && inputIndex < _requiredInputs.Length;
        }

        private void ClearOwnership()
        {
            for (int i = 0; i < _ownedInputs.Length; i++)
            {
                _ownedInputs[i] = 0;
            }

            _reservedOutputCapacity = 0;
        }
    }
}
