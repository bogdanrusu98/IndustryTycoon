using System;
using System.Collections;
using IndustryTycoon.Core;
using IndustryTycoon.Player;
using UnityEngine;

namespace IndustryTycoon.Mining
{
    [RequireComponent(typeof(Collider))]
    public sealed class IronVein : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MineUnlock mineUnlock;
        [SerializeField] private CarryStack carryStack;
        [SerializeField] private Collider playerCollider;
        [SerializeField] private ParticleSystem miningParticles;

        [Header("Mining")]
        [SerializeField, Min(0.02f)] private float miningDuration = 1.25f;

        private Coroutine _miningCoroutine;
        private float _cycleElapsed;
        private bool _isPlayerInside;

        public event Action<int> OreMined;
        public event Action<float> ProgressChanged;
        public event Action<bool> EligibilityChanged;

        public MineUnlock MineUnlock => mineUnlock;
        public CarryStack CarryStack => carryStack;
        public Collider PlayerCollider => playerCollider;
        public float MiningDuration => miningDuration;
        public float Progress01 => Mathf.Clamp01(_cycleElapsed / miningDuration);
        public bool IsPlayerInside => _isPlayerInside;
        public bool IsMining => _miningCoroutine != null;
        public bool IsEligible => CanMine();
        public bool IsPausedByCarry => _isPlayerInside
                                       && carryStack != null
                                       && !carryStack.CanAccept(ResourceType.IronOre, 1);
        public int CompletedCycleCount { get; private set; }

        private void OnEnable()
        {
            if (carryStack != null)
            {
                carryStack.Changed += HandleCarryChanged;
            }

            if (mineUnlock != null)
            {
                mineUnlock.Unlocked += HandleMineUnlocked;
            }

            TryStartMining();
        }

        private void OnDisable()
        {
            if (carryStack != null)
            {
                carryStack.Changed -= HandleCarryChanged;
            }

            if (mineUnlock != null)
            {
                mineUnlock.Unlocked -= HandleMineUnlocked;
            }

            _isPlayerInside = false;
            StopMiningRoutine();
            EligibilityChanged?.Invoke(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || other != playerCollider)
            {
                return;
            }

            _isPlayerInside = true;
            EligibilityChanged?.Invoke(CanMine());
            TryStartMining();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other != playerCollider)
            {
                return;
            }

            _isPlayerInside = false;
            StopMiningRoutine();
            EligibilityChanged?.Invoke(false);
        }

        public bool TryStartMining()
        {
            if (_miningCoroutine != null || !CanMine())
            {
                return false;
            }

            _miningCoroutine = StartCoroutine(MiningRoutine());
            return _miningCoroutine != null;
        }

        public bool TryMineOne()
        {
            if (!CanMine() || !carryStack.TryAdd(ResourceType.IronOre, 1))
            {
                return false;
            }

            _cycleElapsed = 0f;
            CompletedCycleCount++;
            ProgressChanged?.Invoke(0f);
            miningParticles?.Emit(4);
            OreMined?.Invoke(1);
            return true;
        }

        public void ResetTransientState()
        {
            StopMiningRoutine();
            _cycleElapsed = 0f;
            CompletedCycleCount = 0;
            ProgressChanged?.Invoke(0f);
            TryStartMining();
        }

        private IEnumerator MiningRoutine()
        {
            while (CanMine())
            {
                _cycleElapsed = Mathf.Min(
                    miningDuration,
                    _cycleElapsed + Time.deltaTime);
                ProgressChanged?.Invoke(Progress01);
                if (_cycleElapsed < miningDuration)
                {
                    yield return null;
                    continue;
                }

                if (!TryMineOne())
                {
                    break;
                }

                // Prevent more than one authoritative commit in the same frame.
                yield return null;
            }

            _miningCoroutine = null;
            EligibilityChanged?.Invoke(CanMine());
        }

        private bool CanMine()
        {
            return isActiveAndEnabled
                   && gameObject.activeInHierarchy
                   && _isPlayerInside
                   && (mineUnlock == null || mineUnlock.IsUnlocked)
                   && carryStack != null
                   && carryStack.CanAccept(ResourceType.IronOre, 1);
        }

        private void StopMiningRoutine()
        {
            if (_miningCoroutine == null)
            {
                return;
            }

            StopCoroutine(_miningCoroutine);
            _miningCoroutine = null;
        }

        private void HandleCarryChanged()
        {
            EligibilityChanged?.Invoke(CanMine());
            TryStartMining();
        }

        private void HandleMineUnlocked()
        {
            EligibilityChanged?.Invoke(CanMine());
            TryStartMining();
        }

        private void OnValidate()
        {
            miningDuration = Mathf.Max(0.02f, miningDuration);
        }
    }
}
