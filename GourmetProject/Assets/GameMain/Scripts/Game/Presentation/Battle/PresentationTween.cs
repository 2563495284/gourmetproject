using System.Collections;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>战斗表现层共享的轻量补间协程，避免在多个组件里重复实现。</summary>
    internal static class PresentationTween
    {
        public static IEnumerator MoveTo(Transform target, Vector3 end, float duration)
        {
            if (target == null)
            {
                yield break;
            }

            Vector3 start = target.position;
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                target.position = Vector3.Lerp(start, end, eased);
                yield return null;
            }

            if (target != null)
            {
                target.position = end;
            }
        }

        /// <summary>缩放 punch：先放大到 peak 倍再回弹到原始尺寸。</summary>
        public static IEnumerator PunchScale(Transform target, float peak, float duration)
        {
            yield return PunchLocalScale(target, peak, duration);
        }

        /// <summary>局部缩放 punch：先放大到 peak 倍再回弹到原始尺寸。</summary>
        public static IEnumerator PunchLocalScale(Transform target, float peak, float duration)
        {
            if (target == null)
            {
                yield break;
            }

            Vector3 baseScale = target.localScale;
            Vector3 peakScale = baseScale * Mathf.Max(0.0001f, peak);
            float half = Mathf.Max(0.0001f, duration * 0.5f);

            float elapsed = 0f;
            while (elapsed < half && target != null)
            {
                elapsed += Time.deltaTime;
                target.localScale = Vector3.Lerp(baseScale, peakScale, elapsed / half);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < half && target != null)
            {
                elapsed += Time.deltaTime;
                target.localScale = Vector3.Lerp(peakScale, baseScale, elapsed / half);
                yield return null;
            }

            if (target != null)
            {
                target.localScale = baseScale;
            }
        }

        /// <summary>局部左右晃动：围绕当前 localRotation 做衰减式 Z 轴摇摆，结束后恢复。</summary>
        public static IEnumerator WobbleLocalRotation(Transform target, float degrees, float cycles, float duration)
        {
            if (target == null)
            {
                yield break;
            }

            Quaternion baseRotation = target.localRotation;
            float safeDuration = Mathf.Max(0.0001f, duration);
            float safeCycles = Mathf.Max(0f, cycles);

            float elapsed = 0f;
            while (elapsed < safeDuration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float decay = 1f - t;
                float angle = Mathf.Sin(t * safeCycles * Mathf.PI * 2f) * degrees * decay;
                target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }

            if (target != null)
            {
                target.localRotation = baseRotation;
            }
        }

        /// <summary>局部缩放 punch 与左右晃动同步执行，适合食物落定这类一次性反馈。</summary>
        public static IEnumerator PunchScaleAndWobble(
            Transform target,
            float peak,
            float punchDuration,
            float wobbleDegrees,
            float wobbleCycles,
            float wobbleDuration)
        {
            if (target == null)
            {
                yield break;
            }

            Vector3 baseScale = target.localScale;
            Quaternion baseRotation = target.localRotation;
            float safePunchDuration = Mathf.Max(0.0001f, punchDuration);
            float safeWobbleDuration = Mathf.Max(0.0001f, wobbleDuration);
            float totalDuration = Mathf.Max(safePunchDuration, safeWobbleDuration);
            float safePeak = Mathf.Max(0.0001f, peak);
            float safeCycles = Mathf.Max(0f, wobbleCycles);

            float elapsed = 0f;
            while (elapsed < totalDuration && target != null)
            {
                elapsed += Time.deltaTime;

                float punchT = Mathf.Clamp01(elapsed / safePunchDuration);
                float scaleMul = PunchScaleMultiplier(punchT, safePeak);
                target.localScale = baseScale * scaleMul;

                float wobbleT = Mathf.Clamp01(elapsed / safeWobbleDuration);
                float decay = 1f - wobbleT;
                float angle = Mathf.Sin(wobbleT * safeCycles * Mathf.PI * 2f) * wobbleDegrees * decay;
                target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);

                yield return null;
            }

            if (target != null)
            {
                target.localScale = baseScale;
                target.localRotation = baseRotation;
            }
        }

        private static float PunchScaleMultiplier(float t, float peak)
        {
            float clamped = Mathf.Clamp01(t);
            if (clamped < 0.5f)
            {
                return Mathf.Lerp(1f, peak, clamped / 0.5f);
            }

            return Mathf.Lerp(peak, 1f, (clamped - 0.5f) / 0.5f);
        }
    }
}
