using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using UnityEngine;

namespace GourmetProject.Game.UI.Common
{
    [Serializable]
    internal sealed class GameplayTransitionSettings
    {
        [Min(0f)] public float CenterFadeOut = 0.12f;
        [Min(0f)] public float CenterFadeIn = 0.18f;
        [Min(0f)] public float CoverDuration = 0.16f;
        [Min(0f)] public float CoveredHoldDuration = 0.03f;
        [Min(0f)] public float RevealDuration = 0.24f;
    }

    [Serializable]
    internal sealed class StaggerTransitionSettings
    {
        [Min(0f)] public float Duration = 0.12f;
        [Min(0f)] public float Interval = 0.02f;
        [Min(0f)] public float MaxDelay = 0.10f;
    }

    [Serializable]
    internal sealed class RewardFormTransitionSettings
    {
        [Min(0f)] public float BackgroundFade = 0.12f;
        [Min(0f)] public float CloseDuration = 0.10f;
        [Min(0f)] public float InitialRowsDelay = 0.02f;
        public StaggerTransitionSettings Rows = new StaggerTransitionSettings();
    }

    /// <summary>
    /// 通用局部过程动画工具：对某个「变化区域」做「淡出 → swap → 淡入」的中部内容切换。
    /// 常驻壳（左列 / 右列 / 时间轴）不参与动画，只对传入的 <see cref="CanvasGroup"/> 做渐隐渐显。
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
                seq.Append(FadeTween(region, 0f, fadeOut));
            }
            else
            {
                region.alpha = 0f;
            }

            seq.AppendCallback(() => swap?.Invoke());

            if (fadeIn > 0f)
            {
                seq.Append(FadeTween(region, 1f, fadeIn));
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

        /// <summary>
        /// 普通中部页面切换：旧页面原地淡出，交换内容后新页面原地淡入。
        /// 只修改中部 CanvasGroup 的透明度，不触碰任何 RectTransform。
        /// </summary>
        public static Tween FadeSwapStable(
            CanvasGroup region,
            Action swap,
            float fadeOut,
            float fadeIn,
            Action onDone = null)
        {
            if (region == null)
            {
                swap?.Invoke();
                onDone?.Invoke();
                return null;
            }

            bool originalInteractable = region.interactable;
            bool originalBlocksRaycasts = region.blocksRaycasts;
            bool finished = false;

            DOTween.Kill(region, complete: false);
            region.interactable = false;
            region.blocksRaycasts = false;

            Sequence seq = DOTween.Sequence().SetTarget(region).SetUpdate(true);
            if (fadeOut > 0f)
            {
                seq.Append(FadeTween(region, 0f, fadeOut).SetEase(Ease.InSine));
            }
            else
            {
                region.alpha = 0f;
            }

            seq.AppendCallback(() =>
            {
                swap?.Invoke();
                region.alpha = 0f;
            });

            if (fadeIn > 0f)
            {
                seq.Append(FadeTween(region, 1f, fadeIn).SetEase(Ease.OutSine));
            }
            else
            {
                region.alpha = 1f;
            }

            Action restore = () =>
            {
                region.alpha = 1f;
                region.interactable = originalInteractable;
                region.blocksRaycasts = originalBlocksRaycasts;
            };

            seq.OnComplete(() =>
            {
                finished = true;
                restore();
                onDone?.Invoke();
            });
            seq.OnKill(() =>
            {
                if (!finished)
                {
                    restore();
                }
            });
            return seq;
        }

        /// <summary>
        /// 用独立的中部遮罩覆盖世界空间内容，在完全遮住时交换页面，再揭开目标页面。
        /// 遮罩始终只拦截自身矩形范围内的输入。
        /// </summary>
        public static Tween CoverSwap(
            CanvasGroup cover,
            Action swap,
            float coverDuration,
            float coveredHoldDuration,
            float revealDuration,
            Action onDone = null)
        {
            return CoverSwap(
                cover,
                swap,
                coverDuration,
                coveredHoldDuration,
                revealDuration,
                null,
                null,
                CancellationToken.None,
                onDone);
        }

        /// <summary>
        /// 在遮罩完全覆盖并完成页面交换后，可选地播放一段 covered presentation。
        /// bindCoveredSkip 会收到“定位到揭幕起点”的一次性跳过回调；传入 null 表示演出已结束或取消。
        /// </summary>
        internal static Tween CoverSwap(
            CanvasGroup cover,
            Action swap,
            float coverDuration,
            float coveredHoldDuration,
            float revealDuration,
            Func<Tween> coveredPresentation,
            Action<Action> bindCoveredSkip,
            CancellationToken cancellationToken,
            Action onDone)
        {
            if (cover == null)
            {
                bindCoveredSkip?.Invoke(null);
                swap?.Invoke();
                onDone?.Invoke();
                return null;
            }

            bool finished = false;
            bool skipRequested = false;
            bool restored = false;
            CancellationTokenRegistration cancellationRegistration = default;
            Tween presentation = coveredPresentation?.Invoke();
            DOTween.Kill(cover, complete: false);
            cover.gameObject.SetActive(true);
            cover.alpha = 0f;
            cover.interactable = false;
            // 既有 CoverSwap 仍会同步拦截输入；带入场演出的路径则延迟到序列首帧，
            // 避免创建后同帧取消时留下尚未清理的输入遮罩。
            cover.blocksRaycasts = presentation == null;

            Sequence seq = null;
            Action restore = () =>
            {
                if (restored)
                {
                    return;
                }

                restored = true;
                bindCoveredSkip?.Invoke(null);
                if (cover != null)
                {
                    cover.alpha = 0f;
                    cover.interactable = false;
                    cover.blocksRaycasts = false;
                }
            };
            Action cancelIfRequested = () =>
            {
                if (!cancellationToken.IsCancellationRequested || finished)
                {
                    return;
                }

                restore();
                seq.Kill(complete: false);
            };

            seq = DOTween.Sequence().SetTarget(cover).SetUpdate(true);
            if (presentation != null)
            {
                seq.AppendCallback(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelIfRequested();
                        return;
                    }

                    cover.blocksRaycasts = true;
                });
            }
            if (coverDuration > 0f)
            {
                seq.Append(FadeTween(cover, 1f, coverDuration).SetEase(Ease.InOutSine));
            }
            else
            {
                cover.alpha = 1f;
            }

            seq.AppendCallback(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelIfRequested();
                    return;
                }

                swap?.Invoke();
                Canvas.ForceUpdateCanvases();
            });

            if (coveredHoldDuration > 0f)
            {
                seq.AppendInterval(coveredHoldDuration);
            }

            Action skipToReveal = null;
            if (presentation != null)
            {
                seq.AppendCallback(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        bindCoveredSkip?.Invoke(skipToReveal);
                    }
                });
                seq.Append(presentation);
            }

            float revealStart = seq.Duration();
            if (presentation != null && bindCoveredSkip != null)
            {
                skipToReveal = () =>
                {
                    if (skipRequested || finished || !seq.IsActive())
                    {
                        return;
                    }

                    skipRequested = true;
                    bindCoveredSkip(null);
                    seq.Goto(revealStart, andPlay: true);
                };
            }

            if (revealDuration > 0f)
            {
                seq.Append(FadeTween(cover, 0f, revealDuration).SetEase(Ease.InOutSine));
            }
            else
            {
                cover.alpha = 0f;
            }

            seq.OnComplete(() =>
            {
                bool wasCancelled = cancellationToken.IsCancellationRequested;
                finished = true;
                cancellationRegistration.Dispose();
                restore();
                if (!wasCancelled)
                {
                    onDone?.Invoke();
                }
            });
            seq.OnKill(() =>
            {
                if (!finished)
                {
                    finished = true;
                    restore();
                }
            });
            cancellationRegistration = cancellationToken.Register(() =>
            {
                if (finished)
                {
                    return;
                }

                // Cancel 发生在 Sequence 首帧前时 DOTween.Kill 暂时不会生效，
                // 但同步恢复状态后，首回调还会根据 token 再次 Kill，且不会执行 swap。
                restore();
                seq.Kill(complete: false);
            });
            return seq;
        }

        /// <summary>让一组奖励卡只做透明度错峰淡入；不修改布局、位置或缩放。</summary>
        public static Sequence StaggerIn(
            IReadOnlyList<RectTransform> items,
            float duration = 0.12f,
            float interval = 0.02f,
            float maxDelay = 0.10f,
            float startDelay = 0f)
        {
            if (items == null || items.Count == 0)
            {
                return null;
            }

            var rects = new List<RectTransform>(items.Count);
            var groups = new List<CanvasGroup>(items.Count);
            var raycastStates = new List<bool>(items.Count);
            Sequence seq = DOTween.Sequence().SetUpdate(true);

            for (int i = 0; i < items.Count; i++)
            {
                RectTransform rect = items[i];
                if (rect == null)
                {
                    continue;
                }

                CanvasGroup group = rect.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = rect.gameObject.AddComponent<CanvasGroup>();
                }

                DOTween.Kill(group);
                bool wasBlockingRaycasts = group.blocksRaycasts;
                group.alpha = 0f;
                group.blocksRaycasts = false;

                rects.Add(rect);
                groups.Add(group);
                raycastStates.Add(wasBlockingRaycasts);

                float delay = Mathf.Max(0f, startDelay)
                    + Mathf.Min(Mathf.Max(0f, maxDelay), i * Mathf.Max(0f, interval));
                float tweenDuration = Mathf.Max(0.01f, duration);
                seq.Insert(delay, DOTween.To(() => group.alpha, a => group.alpha = a, 1f, tweenDuration)
                    .SetEase(Ease.OutSine)
                    .SetUpdate(true));
            }

            if (rects.Count == 0)
            {
                seq.Kill();
                return null;
            }

            Action restore = () =>
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    if (groups[i] != null)
                    {
                        groups[i].alpha = 1f;
                        groups[i].blocksRaycasts = raycastStates[i];
                    }
                }
            };
            seq.OnComplete(() => restore());
            seq.OnKill(() => restore());
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
            return FadeTween(region, target, duration).SetTarget(region);
        }

        private static Tween FadeTween(CanvasGroup region, float to, float duration)
        {
            return DOTween.To(() => region.alpha, a => region.alpha = a, to, duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
    }
}
