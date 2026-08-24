using System.Collections.Generic;
using IndustryTycoon.Interaction;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class SalePointFeedback : MonoBehaviour
    {
        private sealed class FlightSlot
        {
            public GameObject Visual;
            public Vector3 BaseScale;
            public Vector3 StartPosition;
            public Quaternion StartRotation;
            public float Elapsed = -1f;
        }

        [Header("References")]
        [SerializeField] private SalePoint salePoint;
        [SerializeField] private Transform flightTarget;
        [SerializeField] private Transform responseVisual;
        [SerializeField] private GameObject woodVisualPrefab;
        [SerializeField] private ParticleSystem saleParticles;
        [SerializeField] private AudioFeedback audioFeedback;

        [Header("Flight")]
        [SerializeField, Range(1, 6)] private int poolSize = 4;
        [SerializeField, Min(0.05f)] private float flightDuration = 0.18f;
        [SerializeField, Min(0f)] private float arcHeight = 0.55f;

        [Header("Response")]
        [SerializeField, Min(0.05f)] private float responseDuration = 0.14f;
        [SerializeField, Range(1f, 1.3f)] private float salePopScale = 1.08f;
        [SerializeField, Range(1f, 1.4f)] private float emptyPopScale = 1.20f;

        private readonly List<FlightSlot> _flightSlots = new List<FlightSlot>();
        private Vector3 _responseBaseScale;
        private float _responseElapsed = -1f;
        private float _responsePeakScale = 1f;
        private bool _hasActiveFlights;
        private int _nextSlot;

        public int PoolSize => poolSize;
        public int PoolCount => _flightSlots.Count;
        public int ActiveFlightCount { get; private set; }
        public float FlightDuration => flightDuration;
        public int FeedbackCount { get; private set; }
        public int EmptyFeedbackCount { get; private set; }

        private void Awake()
        {
            _responseBaseScale = responseVisual != null ? responseVisual.localScale : Vector3.one;
            EnsurePool();
        }

        private void OnEnable()
        {
            if (salePoint != null)
            {
                salePoint.UnitSold += HandleUnitSold;
            }
        }

        private void OnDisable()
        {
            if (salePoint != null)
            {
                salePoint.UnitSold -= HandleUnitSold;
            }

            ResetPresentation();
        }

        private void Update()
        {
            UpdateFlights();
            UpdateResponse();
        }

        private void HandleUnitSold(SaleFeedbackData feedback)
        {
            FeedbackCount++;
            if (feedback.BecameEmpty)
            {
                EmptyFeedbackCount++;
            }

            StartFlight(feedback);
            _responseElapsed = 0f;
            _responsePeakScale = feedback.BecameEmpty ? emptyPopScale : salePopScale;
            saleParticles?.Emit(feedback.BecameEmpty ? 10 : 3);
            audioFeedback?.PlaySale();
        }

        private void EnsurePool()
        {
            if (woodVisualPrefab == null)
            {
                return;
            }

            while (_flightSlots.Count < poolSize)
            {
                GameObject visual = Instantiate(woodVisualPrefab, transform);
                visual.name = $"Sale Flight Log {_flightSlots.Count + 1:00}";
                visual.SetActive(false);
                _flightSlots.Add(new FlightSlot
                {
                    Visual = visual,
                    BaseScale = visual.transform.localScale
                });
            }
        }

        private void StartFlight(SaleFeedbackData feedback)
        {
            EnsurePool();
            if (_flightSlots.Count == 0)
            {
                return;
            }

            int slotIndex = FindAvailableSlot();
            FlightSlot slot = _flightSlots[slotIndex];
            bool wasActive = slot.Elapsed >= 0f;
            slot.StartPosition = feedback.StartPosition;
            slot.StartRotation = feedback.StartRotation;
            slot.Elapsed = 0f;
            slot.Visual.SetActive(true);
            slot.Visual.transform.SetPositionAndRotation(feedback.StartPosition, feedback.StartRotation);
            slot.Visual.transform.localScale = feedback.StartScale;
            slot.BaseScale = feedback.StartScale;
            if (!wasActive)
            {
                ActiveFlightCount++;
            }

            _hasActiveFlights = true;
        }

        private int FindAvailableSlot()
        {
            for (int offset = 0; offset < _flightSlots.Count; offset++)
            {
                int index = (_nextSlot + offset) % _flightSlots.Count;
                if (_flightSlots[index].Elapsed < 0f)
                {
                    _nextSlot = (index + 1) % _flightSlots.Count;
                    return index;
                }
            }

            int reusedIndex = _nextSlot;
            _nextSlot = (_nextSlot + 1) % _flightSlots.Count;
            return reusedIndex;
        }

        private void UpdateFlights()
        {
            if (!_hasActiveFlights)
            {
                return;
            }

            _hasActiveFlights = false;
            Vector3 targetPosition = flightTarget != null ? flightTarget.position : transform.position;
            for (int i = 0; i < _flightSlots.Count; i++)
            {
                FlightSlot slot = _flightSlots[i];
                if (slot.Elapsed < 0f)
                {
                    continue;
                }

                slot.Elapsed = Mathf.Min(flightDuration, slot.Elapsed + Time.deltaTime);
                float normalizedTime = Mathf.Clamp01(slot.Elapsed / flightDuration);
                Transform visual = slot.Visual.transform;
                visual.position = FeedbackTween.EvaluateArc(
                    slot.StartPosition,
                    targetPosition,
                    normalizedTime,
                    arcHeight);
                visual.rotation = Quaternion.AngleAxis(normalizedTime * 300f, Vector3.up)
                                  * slot.StartRotation;
                float scaleMultiplier = Mathf.Lerp(1f, 0.48f, normalizedTime * normalizedTime);
                visual.localScale = slot.BaseScale * scaleMultiplier;

                if (normalizedTime >= 1f)
                {
                    slot.Elapsed = -1f;
                    slot.Visual.SetActive(false);
                    slot.Visual.transform.localScale = slot.BaseScale;
                    ActiveFlightCount = Mathf.Max(0, ActiveFlightCount - 1);
                }
                else
                {
                    _hasActiveFlights = true;
                }
            }
        }

        private void UpdateResponse()
        {
            if (_responseElapsed < 0f || responseVisual == null)
            {
                return;
            }

            _responseElapsed = Mathf.Min(responseDuration, _responseElapsed + Time.deltaTime);
            float normalizedTime = Mathf.Clamp01(_responseElapsed / responseDuration);
            float pulse = Mathf.Sin(normalizedTime * Mathf.PI) * (_responsePeakScale - 1f);
            responseVisual.localScale = _responseBaseScale * (1f + pulse);
            if (normalizedTime >= 1f)
            {
                responseVisual.localScale = _responseBaseScale;
                _responseElapsed = -1f;
            }
        }

        private void ResetPresentation()
        {
            _hasActiveFlights = false;
            ActiveFlightCount = 0;
            for (int i = 0; i < _flightSlots.Count; i++)
            {
                FlightSlot slot = _flightSlots[i];
                slot.Elapsed = -1f;
                slot.Visual.SetActive(false);
                slot.Visual.transform.localScale = slot.BaseScale;
            }

            _responseElapsed = -1f;
            if (responseVisual != null)
            {
                responseVisual.localScale = _responseBaseScale;
            }
        }

        private void OnValidate()
        {
            poolSize = Mathf.Clamp(poolSize, 1, 6);
            flightDuration = Mathf.Max(0.05f, flightDuration);
            arcHeight = Mathf.Max(0f, arcHeight);
            responseDuration = Mathf.Max(0.05f, responseDuration);
            salePopScale = Mathf.Clamp(salePopScale, 1f, 1.3f);
            emptyPopScale = Mathf.Clamp(emptyPopScale, 1f, 1.4f);
        }
    }
}
