using DG.Tweening;
using GameStartStudio.UI;
using GourmetProject.Game.UI.Common;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 时间轴节点 / 装饰品和消耗品 hover Tips 的共享基类。
    /// 统一结构：卡片（标题 / 描述框 / 可选底部信息行）。
    ///
    /// 时间轴节点共用 <c>TimelineNodeTipView</c>，装饰品和消耗品使用带动态词条布局的
    /// <c>ItemTipView</c>；公共展示 / 显隐逻辑收敛在此基类。
    ///
    /// 作为可挂在任意 Canvas 下的 MonoBehaviour View（非 UGuiForm），
    /// 通过 <see cref="Show"/> / <see cref="Hide"/> 驱动，配合 <c>TipHoverTrigger</c>
    /// 实现"鼠标悬浮显示 tips"——真实触发点后续由持有方接线。
    /// </summary>
    public abstract class ActionTipView : MonoBehaviour
    {
        private const float ShowDuration = 0.12f;
        private const float HideDuration = 0.08f;

        [Header("Root")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("卡片内容")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descText;
        [SerializeField] private TmpTextVertexAnimator _titleAnimator;

        [Header("底部信息行（可空：装饰品和消耗品 Tips 无此行）")]
        [SerializeField] private GameObject _footerRoot;
        [SerializeField] private TMP_Text _footerText;

        protected TMP_Text TitleText => _titleText;
        protected TMP_Text DescText => _descText;

        private Tween _visibilityTween;

        /// <summary>显示 Tips（不吃射线，纯展示）。</summary>
        public void Show()
        {
            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                KillVisibilityTween();
                if (!wasActive)
                {
                    _canvasGroup.alpha = 0f;
                }

                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
                _visibilityTween = _canvasGroup
                    .DOFade(1f, ShowDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() => _visibilityTween = null);
            }
        }

        /// <summary>隐藏 Tips。</summary>
        public void Hide()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            if (_canvasGroup == null || !Application.isPlaying)
            {
                gameObject.SetActive(false);
                return;
            }

            KillVisibilityTween();
            _visibilityTween = _canvasGroup
                .DOFade(0f, HideDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _visibilityTween = null;
                    gameObject.SetActive(false);
                });
        }

        /// <summary>设置标题与描述框正文。</summary>
        protected void ApplyTexts(string title, string desc)
        {
            if (_titleText != null)
            {
                _titleText.text = title ?? string.Empty;
                EnsureTitleAnimator();
                _titleAnimator?.Rebuild();
            }

            if (_descText != null)
            {
                SemanticDescriptionFormatter.Set(_descText, desc);
            }
        }

        private void OnDisable()
        {
            KillVisibilityTween();
        }

        private void EnsureTitleAnimator()
        {
            if (_titleAnimator == null && _titleText != null)
            {
                _titleAnimator = _titleText.GetComponent<TmpTextVertexAnimator>();
            }

            _titleAnimator?.SetPreset(TmpTextAnimationPreset.TipTitle);
        }

        private void KillVisibilityTween()
        {
            if (_visibilityTween == null)
            {
                return;
            }

            _visibilityTween.Kill();
            _visibilityTween = null;
        }

        /// <summary>设置底部信息行；<paramref name="footer"/> 为空则隐藏整行。</summary>
        protected void ApplyFooter(string footer)
        {
            bool has = !string.IsNullOrEmpty(footer);
            if (_footerRoot != null)
            {
                _footerRoot.SetActive(has);
            }

            if (_footerText != null)
            {
                _footerText.text = footer ?? string.Empty;
            }
        }
    }
}
