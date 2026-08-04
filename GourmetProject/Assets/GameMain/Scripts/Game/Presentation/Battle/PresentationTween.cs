using System.Threading;
using DG.Tweening;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>经营挑战表现层共享的 DOTween + Awaitable 补间工具，避免在多个组件里重复实现。</summary>
    internal static class PresentationTween
    {
        public static async Awaitable MoveToAsync(Transform target, Vector3 end, float duration, CancellationToken cancellationToken)
        {
            if (target == null)
            {
                return;
            }

            Tween tween = target.DOMove(end, Mathf.Max(0.0001f, duration))
                .SetEase(Ease.OutCubic)
                .SetLink(target.gameObject);
            await AwaitCompletionAsync(tween, cancellationToken);

            if (target != null)
            {
                target.position = end;
            }
        }

        /// <summary>缩放 punch：先放大到 peak 倍再回弹到原始尺寸。</summary>
        public static Awaitable PunchScaleAsync(Transform target, float peak, float duration, CancellationToken cancellationToken)
        {
            return PunchLocalScaleAsync(target, peak, duration, cancellationToken);
        }

        /// <summary>局部缩放 punch：先放大到 peak 倍再回弹到原始尺寸。</summary>
        public static async Awaitable PunchLocalScaleAsync(Transform target, float peak, float duration, CancellationToken cancellationToken)
        {
            if (target == null)
            {
                return;
            }

            Vector3 baseScale = target.localScale;
            Vector3 peakScale = baseScale * Mathf.Max(0.0001f, peak);
            Sequence sequence = DOTween.Sequence()
                .SetLink(target.gameObject)
                .Append(target.DOScale(peakScale, Mathf.Max(0.0001f, duration * 0.5f)).SetEase(Ease.Linear))
                .Append(target.DOScale(baseScale, Mathf.Max(0.0001f, duration * 0.5f)).SetEase(Ease.Linear));
            await AwaitCompletionAsync(sequence, cancellationToken);

            if (target != null)
            {
                target.localScale = baseScale;
            }
        }

        /// <summary>局部左右晃动：围绕当前 localRotation 做衰减式 Z 轴摇摆，结束后恢复。</summary>
        public static async Awaitable WobbleLocalRotationAsync(Transform target, float degrees, float cycles, float duration, CancellationToken cancellationToken)
        {
            if (target == null)
            {
                return;
            }

            Quaternion baseRotation = target.localRotation;
            float safeDuration = Mathf.Max(0.0001f, duration);
            float safeCycles = Mathf.Max(0f, cycles);
            Tween tween = DOVirtual.Float(0f, 1f, safeDuration, t =>
                {
                    if (target == null)
                    {
                        return;
                    }

                    float decay = 1f - t;
                    float angle = Mathf.Sin(t * safeCycles * Mathf.PI * 2f) * degrees * decay;
                    target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);
                })
                .SetEase(Ease.Linear)
                .SetLink(target.gameObject);
            await AwaitCompletionAsync(tween, cancellationToken);

            if (target != null)
            {
                target.localRotation = baseRotation;
            }
        }

        /// <summary>局部缩放 punch 与左右晃动同步执行，适合食物落定这类一次性反馈。</summary>
        public static async Awaitable PunchScaleAndWobbleAsync(
            Transform target,
            float peak,
            float punchDuration,
            float wobbleDegrees,
            float wobbleCycles,
            float wobbleDuration,
            CancellationToken cancellationToken)
        {
            if (target == null)
            {
                return;
            }

            Vector3 baseScale = target.localScale;
            Quaternion baseRotation = target.localRotation;
            float safePunchDuration = Mathf.Max(0.0001f, punchDuration);
            float safeWobbleDuration = Mathf.Max(0.0001f, wobbleDuration);
            float totalDuration = Mathf.Max(safePunchDuration, safeWobbleDuration);
            float safePeak = Mathf.Max(0.0001f, peak);
            float safeCycles = Mathf.Max(0f, wobbleCycles);

            Tween tween = DOVirtual.Float(0f, totalDuration, totalDuration, elapsed =>
                {
                    if (target == null)
                    {
                        return;
                    }

                    float punchT = Mathf.Clamp01(elapsed / safePunchDuration);
                    float scaleMul = PunchScaleMultiplier(punchT, safePeak);
                    target.localScale = baseScale * scaleMul;

                    float wobbleT = Mathf.Clamp01(elapsed / safeWobbleDuration);
                    float decay = 1f - wobbleT;
                    float angle = Mathf.Sin(wobbleT * safeCycles * Mathf.PI * 2f) * wobbleDegrees * decay;
                    target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);
                })
                .SetEase(Ease.Linear)
                .SetLink(target.gameObject);
            await AwaitCompletionAsync(tween, cancellationToken);

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

        public static async Awaitable AwaitCompletionAsync(Tween tween, CancellationToken cancellationToken)
        {
            try
            {
                while (tween != null && tween.active && !tween.IsComplete())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
            }
            catch
            {
                tween?.Kill();
                throw;
            }
        }
    }
}
