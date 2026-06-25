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
            if (target == null)
            {
                yield break;
            }

            Vector3 baseScale = target.localScale;
            Vector3 peakScale = baseScale * peak;
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
    }
}
