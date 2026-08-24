using IndustryTycoon.Workers;
using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public sealed class LumberWorkerFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LumberWorker worker;
        [SerializeField] private Transform carriedWoodAnchor;
        [SerializeField] private Transform depositTarget;
        [SerializeField] private GameObject woodVisualPrefab;

        [Header("Feel")]
        [SerializeField, Min(0.05f)] private float cargoPopDuration = 0.16f;
        [SerializeField, Min(0.05f)] private float depositFlightDuration = 0.20f;
        [SerializeField, Min(0f)] private float depositArcHeight = 0.48f;

        private GameObject _carriedVisual;
        private GameObject _depositVisual;
        private Vector3 _carriedBaseScale = Vector3.one;
        private Vector3 _depositBaseScale = Vector3.one;
        private Vector3 _depositStart;
        private float _cargoPopElapsed = -1f;
        private float _depositElapsed = -1f;

        public int VisualPoolCount => (_carriedVisual != null ? 1 : 0)
                                      + (_depositVisual != null ? 1 : 0);
        public int ActiveVisualCount => (_carriedVisual != null && _carriedVisual.activeSelf ? 1 : 0)
                                        + (_depositVisual != null && _depositVisual.activeSelf ? 1 : 0);
        public float CargoPopDuration => cargoPopDuration;
        public float DepositFlightDuration => depositFlightDuration;
        public int DepositPresentationCount { get; private set; }

        private void Awake()
        {
            EnsureVisualPool();
            ApplyCargoState(worker != null && worker.IsCarrying, false);
        }

        private void OnEnable()
        {
            if (worker == null)
            {
                return;
            }

            worker.CargoChanged += HandleCargoChanged;
            worker.WoodDeposited += HandleWoodDeposited;
            ApplyCargoState(worker.IsCarrying, false);
        }

        private void OnDisable()
        {
            if (worker != null)
            {
                worker.CargoChanged -= HandleCargoChanged;
                worker.WoodDeposited -= HandleWoodDeposited;
            }

            _cargoPopElapsed = -1f;
            _depositElapsed = -1f;
            if (_carriedVisual != null)
            {
                _carriedVisual.SetActive(false);
                _carriedVisual.transform.localScale = _carriedBaseScale;
            }

            if (_depositVisual != null)
            {
                _depositVisual.SetActive(false);
                _depositVisual.transform.localScale = _depositBaseScale;
            }
        }

        private void Update()
        {
            UpdateCargoPop();
            UpdateDepositFlight();
        }

        private void HandleCargoChanged(bool isCarrying)
        {
            ApplyCargoState(isCarrying, isCarrying);
        }

        private void HandleWoodDeposited()
        {
            EnsureVisualPool();
            if (_depositVisual == null)
            {
                return;
            }

            DepositPresentationCount++;
            _depositStart = carriedWoodAnchor != null
                ? carriedWoodAnchor.position
                : transform.position + Vector3.up;
            _depositElapsed = 0f;
            _depositVisual.SetActive(true);
            _depositVisual.transform.SetPositionAndRotation(_depositStart, transform.rotation);
            _depositVisual.transform.localScale = _depositBaseScale;
        }

        private void EnsureVisualPool()
        {
            if (woodVisualPrefab == null)
            {
                return;
            }

            if (_carriedVisual == null)
            {
                Transform carryParent = carriedWoodAnchor != null ? carriedWoodAnchor : transform;
                _carriedVisual = Instantiate(woodVisualPrefab, carryParent);
                _carriedVisual.name = "Worker Carried Wood";
                _carriedVisual.transform.localPosition = Vector3.zero;
                _carriedVisual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                _carriedBaseScale = _carriedVisual.transform.localScale * 0.82f;
                _carriedVisual.transform.localScale = _carriedBaseScale;
                _carriedVisual.SetActive(false);
            }

            if (_depositVisual == null)
            {
                _depositVisual = Instantiate(woodVisualPrefab, transform);
                _depositVisual.name = "Worker Deposit Visual";
                _depositBaseScale = _depositVisual.transform.localScale * 0.82f;
                _depositVisual.transform.localScale = _depositBaseScale;
                _depositVisual.SetActive(false);
            }
        }

        private void ApplyCargoState(bool isCarrying, bool animate)
        {
            EnsureVisualPool();
            if (_carriedVisual == null)
            {
                return;
            }

            _carriedVisual.SetActive(isCarrying);
            if (!isCarrying)
            {
                _cargoPopElapsed = -1f;
                _carriedVisual.transform.localScale = _carriedBaseScale;
                return;
            }

            _carriedVisual.transform.localPosition = Vector3.zero;
            _carriedVisual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            if (animate)
            {
                _cargoPopElapsed = 0f;
                _carriedVisual.transform.localScale = Vector3.zero;
            }
            else
            {
                _cargoPopElapsed = -1f;
                _carriedVisual.transform.localScale = _carriedBaseScale;
            }
        }

        private void UpdateCargoPop()
        {
            if (_cargoPopElapsed < 0f || _carriedVisual == null || !_carriedVisual.activeSelf)
            {
                return;
            }

            _cargoPopElapsed = Mathf.Min(cargoPopDuration, _cargoPopElapsed + Time.deltaTime);
            float normalizedTime = Mathf.Clamp01(_cargoPopElapsed / cargoPopDuration);
            _carriedVisual.transform.localScale = _carriedBaseScale
                                                  * FeedbackTween.EaseOutBack(normalizedTime);
            if (normalizedTime >= 1f)
            {
                _cargoPopElapsed = -1f;
                _carriedVisual.transform.localScale = _carriedBaseScale;
            }
        }

        private void UpdateDepositFlight()
        {
            if (_depositElapsed < 0f || _depositVisual == null)
            {
                return;
            }

            _depositElapsed = Mathf.Min(depositFlightDuration, _depositElapsed + Time.deltaTime);
            float normalizedTime = Mathf.Clamp01(_depositElapsed / depositFlightDuration);
            Vector3 destination = depositTarget != null ? depositTarget.position : transform.position;
            Transform visual = _depositVisual.transform;
            visual.position = FeedbackTween.EvaluateArc(
                _depositStart,
                destination,
                normalizedTime,
                depositArcHeight);
            visual.rotation = Quaternion.Euler(0f, normalizedTime * 260f, normalizedTime * 120f);
            visual.localScale = _depositBaseScale * Mathf.Lerp(1f, 0.62f, normalizedTime);
            if (normalizedTime >= 1f)
            {
                _depositElapsed = -1f;
                _depositVisual.SetActive(false);
                _depositVisual.transform.localScale = _depositBaseScale;
            }
        }

        private void OnValidate()
        {
            cargoPopDuration = Mathf.Max(0.05f, cargoPopDuration);
            depositFlightDuration = Mathf.Max(0.05f, depositFlightDuration);
            depositArcHeight = Mathf.Max(0f, depositArcHeight);
        }
    }
}
