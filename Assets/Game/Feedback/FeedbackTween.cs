using UnityEngine;

namespace IndustryTycoon.Feedback
{
    public static class FeedbackTween
    {
        private const float BackOvershoot = 1.70158f;

        public static float EaseOutCubic(float normalizedTime)
        {
            float inverse = 1f - Mathf.Clamp01(normalizedTime);
            return 1f - (inverse * inverse * inverse);
        }

        public static float EaseInOutCubic(float normalizedTime)
        {
            float time = Mathf.Clamp01(normalizedTime);
            return time < 0.5f
                ? 4f * time * time * time
                : 1f - Mathf.Pow(-2f * time + 2f, 3f) * 0.5f;
        }

        public static float EaseOutBack(float normalizedTime)
        {
            float shiftedTime = Mathf.Clamp01(normalizedTime) - 1f;
            float adjustedOvershoot = BackOvershoot + 1f;
            return 1f + adjustedOvershoot * shiftedTime * shiftedTime * shiftedTime
                + BackOvershoot * shiftedTime * shiftedTime;
        }

        public static Vector3 EvaluateArc(
            Vector3 start,
            Vector3 end,
            float normalizedTime,
            float arcHeight)
        {
            float time = Mathf.Clamp01(normalizedTime);
            Vector3 position = Vector3.LerpUnclamped(start, end, EaseOutCubic(time));
            position.y += Mathf.Sin(time * Mathf.PI) * arcHeight;
            return position;
        }
    }
}
