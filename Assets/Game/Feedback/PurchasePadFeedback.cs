using System.Collections.Generic;
using IndustryTycoon.Interaction;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class PurchasePadFeedback : MonoBehaviour
    {
        private sealed class TokenSlot
        {
            public GameObject Visual;
            public Vector3 BaseScale;
            public Vector3 StartPosition;
            public float Elapsed = -1f;
        }

        [Header("References")]
        [SerializeField] private PurchasePad purchasePad;
        [SerializeField] private Transform tokenOrigin;
        [SerializeField] private Transform tokenTarget;
        [SerializeField] private Transform padVisual;
        [SerializeField] private Transform progressFill;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private GameObject tokenVisualPrefab;
        [SerializeField] private ParticleSystem purchaseParticles;
        [SerializeField] private AudioFeedback audioFeedback;

        [Header("Tokens")]
        [SerializeField, Range(1, 6)] private int tokenPoolSize = 4;
        [SerializeField, Min(0.05f)] private float tokenFlightDuration = 0.22f;
        [SerializeField, Min(0f)] private float tokenArcHeight = 0.52f;
        [SerializeField, Range(0.1f, 1f)] private float tokenScale = 0.42f;

        [Header("Pad Response")]
        [SerializeField, Min(0.05f)] private float tickPulseDuration = 0.12f;
        [SerializeField, Min(0.05f)] private float emptyWalletDuration = 0.28f;
        [SerializeField, Min(0.05f)] private float completionDuration = 0.42f;
        [SerializeField, Min(0.1f)] private float progressFullWidth = 2.15f;

        private readonly List<TokenSlot> _tokenSlots = new List<TokenSlot>();
        private Vector3 _padBaseScale;
        private Vector3 _progressBaseScale;
        private Vector3 _progressBasePosition;
        private Vector3 _statusBaseScale;
        private Color _statusBaseColor;
        private float _padPulseElapsed = -1f;
        private float _padPulseDuration;
        private float _padPulseStrength;
        private float _emptyWalletElapsed = -1f;
        private bool _hasActiveTokens;
        private int _nextTokenSlot;

        public int TokenPoolSize => tokenPoolSize;
        public int TokenPoolCount => _tokenSlots.Count;
        public int ActiveTokenCount { get; private set; }
        public float TokenFlightDuration => tokenFlightDuration;
        public float TickPulseDuration => tickPulseDuration;
        public float EmptyWalletDuration => emptyWalletDuration;
        public float CompletionDuration => completionDuration;
        public int PaymentFeedbackCount { get; private set; }
        public int FundingPausedFeedbackCount { get; private set; }
        public int CompletionFeedbackCount { get; private set; }

        private void Awake()
        {
            _padBaseScale = padVisual != null ? padVisual.localScale : Vector3.one;
            _progressBaseScale = progressFill != null ? progressFill.localScale : Vector3.one;
            _progressBasePosition = progressFill != null ? progressFill.localPosition : Vector3.zero;
            _statusBaseScale = statusText != null ? statusText.transform.localScale : Vector3.one;
            _statusBaseColor = statusText != null ? statusText.color : Color.white;
            EnsureTokenPool();
            RefreshProgressFill();
        }

        private void OnEnable()
        {
            if (purchasePad == null)
            {
                return;
            }

            purchasePad.PaymentProcessed += HandlePaymentProcessed;
            purchasePad.FundingPaused += HandleFundingPaused;
            purchasePad.Completed += HandleCompleted;
            RefreshProgressFill();
        }

        private void OnDisable()
        {
            if (purchasePad != null)
            {
                purchasePad.PaymentProcessed -= HandlePaymentProcessed;
                purchasePad.FundingPaused -= HandleFundingPaused;
                purchasePad.Completed -= HandleCompleted;
            }

            ResetPresentation();
        }

        private void Update()
        {
            UpdateTokens();
            UpdatePadPulse();
            UpdateEmptyWalletResponse();
        }

        private void HandlePaymentProcessed(int spentAmount, int remainingCost)
        {
            if (spentAmount <= 0)
            {
                return;
            }

            PaymentFeedbackCount++;
            StartTokenFlight();
            StartPadPulse(tickPulseDuration, 1.055f);
            RefreshProgressFill();
            purchaseParticles?.Emit(1);
            audioFeedback?.PlayPurchaseTick();
        }

        private void HandleFundingPaused()
        {
            FundingPausedFeedbackCount++;
            _emptyWalletElapsed = 0f;
            StartPadPulse(emptyWalletDuration, 1.10f);
            purchaseParticles?.Emit(5);
        }

        private void HandleCompleted()
        {
            CompletionFeedbackCount++;
            _emptyWalletElapsed = -1f;
            StartPadPulse(completionDuration, 1.18f);
            RefreshProgressFill();
            purchaseParticles?.Emit(16);
        }

        private void EnsureTokenPool()
        {
            if (tokenVisualPrefab == null)
            {
                return;
            }

            while (_tokenSlots.Count < tokenPoolSize)
            {
                GameObject visual = Instantiate(tokenVisualPrefab, transform);
                visual.name = $"Purchase Token {_tokenSlots.Count + 1:00}";
                visual.SetActive(false);
                _tokenSlots.Add(new TokenSlot
                {
                    Visual = visual,
                    BaseScale = visual.transform.localScale
                });
            }
        }

        private void StartTokenFlight()
        {
            EnsureTokenPool();
            if (_tokenSlots.Count == 0)
            {
                return;
            }

            int slotIndex = FindAvailableTokenSlot();
            TokenSlot slot = _tokenSlots[slotIndex];
            bool wasActive = slot.Elapsed >= 0f;
            slot.StartPosition = tokenOrigin != null ? tokenOrigin.position : transform.position;
            slot.Elapsed = 0f;
            slot.Visual.SetActive(true);
            slot.Visual.transform.position = slot.StartPosition;
            slot.Visual.transform.rotation = Quaternion.identity;
            slot.Visual.transform.localScale = slot.BaseScale * tokenScale;
            if (!wasActive)
            {
                ActiveTokenCount++;
            }

            _hasActiveTokens = true;
        }

        private int FindAvailableTokenSlot()
        {
            for (int offset = 0; offset < _tokenSlots.Count; offset++)
            {
                int index = (_nextTokenSlot + offset) % _tokenSlots.Count;
                if (_tokenSlots[index].Elapsed < 0f)
                {
                    _nextTokenSlot = (index + 1) % _tokenSlots.Count;
                    return index;
                }
            }

            int reusedIndex = _nextTokenSlot;
            _nextTokenSlot = (_nextTokenSlot + 1) % _tokenSlots.Count;
            return reusedIndex;
        }

        private void UpdateTokens()
        {
            if (!_hasActiveTokens)
            {
                return;
            }

            _hasActiveTokens = false;
            Vector3 destination = tokenTarget != null ? tokenTarget.position : transform.position;
            for (int i = 0; i < _tokenSlots.Count; i++)
            {
                TokenSlot slot = _tokenSlots[i];
                if (slot.Elapsed < 0f)
                {
                    continue;
                }

                slot.Elapsed = Mathf.Min(tokenFlightDuration, slot.Elapsed + Time.deltaTime);
                float normalizedTime = Mathf.Clamp01(slot.Elapsed / tokenFlightDuration);
                Transform visual = slot.Visual.transform;
                visual.position = FeedbackTween.EvaluateArc(
                    slot.StartPosition,
                    destination,
                    normalizedTime,
                    tokenArcHeight);
                visual.rotation = Quaternion.Euler(
                    normalizedTime * 280f,
                    normalizedTime * 420f,
                    0f);
                float scalePulse = 1f + (Mathf.Sin(normalizedTime * Mathf.PI) * 0.22f);
                visual.localScale = slot.BaseScale
                                    * (tokenScale * scalePulse * Mathf.Lerp(1f, 0.55f, normalizedTime));

                if (normalizedTime >= 1f)
                {
                    slot.Elapsed = -1f;
                    slot.Visual.SetActive(false);
                    slot.Visual.transform.localScale = slot.BaseScale;
                    ActiveTokenCount = Mathf.Max(0, ActiveTokenCount - 1);
                }
                else
                {
                    _hasActiveTokens = true;
                }
            }
        }

        private void StartPadPulse(float duration, float peakScale)
        {
            _padPulseElapsed = 0f;
            _padPulseDuration = Mathf.Max(0.05f, duration);
            _padPulseStrength = peakScale;
        }

        private void UpdatePadPulse()
        {
            if (_padPulseElapsed < 0f || padVisual == null)
            {
                return;
            }

            _padPulseElapsed = Mathf.Min(_padPulseDuration, _padPulseElapsed + Time.deltaTime);
            float normalizedTime = Mathf.Clamp01(_padPulseElapsed / _padPulseDuration);
            float pulse = Mathf.Sin(normalizedTime * Mathf.PI) * (_padPulseStrength - 1f);
            padVisual.localScale = _padBaseScale * (1f + pulse);
            if (normalizedTime >= 1f)
            {
                padVisual.localScale = _padBaseScale;
                _padPulseElapsed = -1f;
            }
        }

        private void UpdateEmptyWalletResponse()
        {
            if (_emptyWalletElapsed < 0f || statusText == null)
            {
                return;
            }

            _emptyWalletElapsed = Mathf.Min(
                emptyWalletDuration,
                _emptyWalletElapsed + Time.deltaTime);
            float normalizedTime = Mathf.Clamp01(_emptyWalletElapsed / emptyWalletDuration);
            float pulse = Mathf.Sin(normalizedTime * Mathf.PI);
            statusText.color = Color.Lerp(_statusBaseColor, new Color(1f, 0.35f, 0.12f), pulse);
            statusText.transform.localScale = _statusBaseScale * (1f + (pulse * 0.10f));
            if (normalizedTime >= 1f)
            {
                statusText.color = _statusBaseColor;
                statusText.transform.localScale = _statusBaseScale;
                _emptyWalletElapsed = -1f;
            }
        }

        private void RefreshProgressFill()
        {
            if (progressFill == null || purchasePad == null)
            {
                return;
            }

            float progress = purchasePad.TotalCost > 0
                ? 1f - (purchasePad.RemainingCost / (float)purchasePad.TotalCost)
                : 1f;
            progress = Mathf.Clamp01(progress);
            progressFill.gameObject.SetActive(progress > 0f);
            float width = Mathf.Max(0.01f, progressFullWidth * progress);
            progressFill.localScale = new Vector3(
                width,
                _progressBaseScale.y,
                _progressBaseScale.z);
            progressFill.localPosition = new Vector3(
                _progressBasePosition.x - (progressFullWidth * 0.5f) + (width * 0.5f),
                _progressBasePosition.y,
                _progressBasePosition.z);
        }

        private void ResetPresentation()
        {
            _hasActiveTokens = false;
            ActiveTokenCount = 0;
            for (int i = 0; i < _tokenSlots.Count; i++)
            {
                TokenSlot slot = _tokenSlots[i];
                slot.Elapsed = -1f;
                slot.Visual.SetActive(false);
                slot.Visual.transform.localScale = slot.BaseScale;
            }

            _padPulseElapsed = -1f;
            _emptyWalletElapsed = -1f;
            if (padVisual != null)
            {
                padVisual.localScale = _padBaseScale;
            }

            if (statusText != null)
            {
                statusText.color = _statusBaseColor;
                statusText.transform.localScale = _statusBaseScale;
            }
        }

        private void OnValidate()
        {
            tokenPoolSize = Mathf.Clamp(tokenPoolSize, 1, 6);
            tokenFlightDuration = Mathf.Max(0.05f, tokenFlightDuration);
            tokenArcHeight = Mathf.Max(0f, tokenArcHeight);
            tokenScale = Mathf.Clamp(tokenScale, 0.1f, 1f);
            tickPulseDuration = Mathf.Max(0.05f, tickPulseDuration);
            emptyWalletDuration = Mathf.Max(0.05f, emptyWalletDuration);
            completionDuration = Mathf.Max(0.05f, completionDuration);
            progressFullWidth = Mathf.Max(0.1f, progressFullWidth);
        }
    }
}
