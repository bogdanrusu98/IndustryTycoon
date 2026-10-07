using UnityEngine;

namespace IndustryTycoon.UI
{
    public sealed class WorldGuidanceMarker : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float pulseSpeed = 2.4f;
        [SerializeField, Range(0.01f, 0.5f)] private float pulseScale = 0.16f;
        private Vector3 _baseScale;

        private void Awake() => _baseScale = transform.localScale;

        private void OnEnable()
        {
            if (_baseScale == Vector3.zero) _baseScale = transform.localScale;
        }

        private void OnDisable() => transform.localScale = _baseScale;

        private void Update()
        {
            float scale = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseScale;
            transform.localScale = _baseScale * scale;
        }
    }
}
