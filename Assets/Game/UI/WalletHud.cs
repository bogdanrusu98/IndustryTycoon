using System.Collections;
using IndustryTycoon.Economy;
using IndustryTycoon.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    public sealed class WalletHud : MonoBehaviour
    {
        [SerializeField] private Wallet wallet;
        [SerializeField] private Text cashText;
        [SerializeField, Min(0f)] private float animationDuration = 0.22f;

        private Coroutine _displayRoutine;
        private int _displayedBalance;
        private int _targetBalance;

        public int DisplayedBalance => _displayedBalance;
        public float AnimationDuration => animationDuration;

        private void OnEnable()
        {
            if (wallet == null)
            {
                SetDisplayedBalance(0);
                return;
            }

            wallet.BalanceChanged += HandleBalanceChanged;
            _targetBalance = wallet.Balance;
            SetDisplayedBalance(_targetBalance);
        }

        private void OnDisable()
        {
            if (wallet != null)
            {
                wallet.BalanceChanged -= HandleBalanceChanged;
            }

            if (_displayRoutine != null)
            {
                StopCoroutine(_displayRoutine);
                _displayRoutine = null;
            }
        }

        private void HandleBalanceChanged(int balance)
        {
            _targetBalance = Mathf.Max(0, balance);
            if (animationDuration <= 0f)
            {
                SetDisplayedBalance(_targetBalance);
                return;
            }

            if (_displayRoutine == null)
            {
                _displayRoutine = StartCoroutine(AnimateDisplay());
            }
        }

        private IEnumerator AnimateDisplay()
        {
            while (_displayedBalance != _targetBalance)
            {
                int startBalance = _displayedBalance;
                int segmentTarget = _targetBalance;
                float elapsed = 0f;

                while (elapsed < animationDuration && segmentTarget == _targetBalance)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float progress = animationDuration > 0f ? elapsed / animationDuration : 1f;
                    float easedProgress = FeedbackTween.EaseOutCubic(progress);
                    int displayedValue = Mathf.RoundToInt(
                        Mathf.LerpUnclamped(startBalance, segmentTarget, easedProgress));
                    SetDisplayedBalance(displayedValue);
                    yield return null;
                }

                if (segmentTarget == _targetBalance)
                {
                    SetDisplayedBalance(segmentTarget);
                }
            }

            _displayRoutine = null;
        }

        private void SetDisplayedBalance(int balance)
        {
            _displayedBalance = Mathf.Max(0, balance);
            if (cashText != null)
            {
                cashText.text = $"$ {_displayedBalance}";
            }
        }

        private void OnValidate()
        {
            animationDuration = Mathf.Max(0f, animationDuration);
        }
    }
}
