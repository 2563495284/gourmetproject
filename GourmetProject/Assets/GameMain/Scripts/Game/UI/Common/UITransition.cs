using System;
using DG.Tweening;
using UnityEngine;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// 通用局部过程动画工具：对某个「变化区域」做「淡出 → swap → 淡入」的中部内容切换。
    /// 常驻壳（左列 / 右列 / 行动轴 / 菜谱抽屉外框）不参与动画，只对传入的 <see cref="CanvasGroup"/> 做渐隐渐显。
    /// 时长 &lt;= 0（或 region 为 null）时立即执行 swap（等价于直切，功能与动画解耦，便于独立开发/调试）。
    /// 动画走 unscaled 时间，暂停 / 时间缩放下仍生效；全部基于 DOTween 核心 API（无需 Modules asmdef）。
    /// </summary>
    public static class UITransition
    {
        public const float DefaultFadeOut = 0.12f;
        public const float DefaultFadeIn = 0.15f;

        /// <summary>
        /// 对 <paramref name="region"/> 淡出 → 在淡出完成后执行 <paramref name="swap"/>（隐藏旧内容 + 构建新内容）→ 淡入。
        /// region 为 null 或两段时长都 &lt;= 0 时立即 swap（直切）。
        /// </summary>
        public static Tween FadeSwap(
            CanvasGroup region,
            Action swap,
            float fadeOut = DefaultFadeOut,
            float fadeIn = DefaultFadeIn,
            Action onDone = null)
        {
            if (region == null || (fadeOut <= 0f && fadeIn <= 0f))
            {
                swap?.Invoke();
                onDone?.Invoke();
                return null;
            }

            DOTween.Kill(region);

            Sequence seq = DOTween.Sequence().SetTarget(region).SetUpdate(true);

            if (fadeOut > 0f)
            {
                seq.Append(FadeTween(region, region.alpha, 0f, fadeOut));
            }
            else
            {
                region.alpha = 0f;
            }

            seq.AppendCallback(() => swap?.Invoke());

            if (fadeIn > 0f)
            {
                seq.Append(FadeTween(region, 0f, 1f, fadeIn));
            }
            else
            {
                region.alpha = 1f;
            }

            seq.OnComplete(() =>
            {
                region.alpha = 1f;
                onDone?.Invoke();
            });
            return seq;
        }

        /// <summary>把 region 的 alpha 渐变到 <paramref name="target"/>；时长 &lt;= 0 或 region 为 null 时立即赋值。</summary>
        public static Tween Fade(CanvasGroup region, float target, float duration)
        {
            if (region == null)
            {
                return null;
            }

            if (duration <= 0f)
            {
                region.alpha = target;
                return null;
            }

            DOTween.Kill(region);
            return FadeTween(region, region.alpha, target, duration).SetTarget(region);
        }

        private static Tween FadeTween(CanvasGroup region, float from, float to, float duration)
        {
            region.alpha = from;
            return DOTween.To(() => region.alpha, a => region.alpha = a, to, duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
    }
}
