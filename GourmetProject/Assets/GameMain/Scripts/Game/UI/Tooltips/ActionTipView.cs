using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴节点 / 道具 hover Tips 的共享基类（对应原型图 Tips.png 的四张卡）。
    /// 统一结构：左侧图标 + 卡片（标题 / 描述框 / 可选底部信息行）。
    ///
    /// 每类 Tips 有独立 prefab（<c>BossFeastTipView</c> / <c>ShopNodeTipView</c> /
    /// <c>InterestNodeTipView</c> / <c>ItemTipView</c>），各自的子类只负责把业务数据
    /// 翻译成标题 / 描述 / 底行文案，公共展示 / 显隐逻辑全部收敛在此基类，避免四处漂移。
    ///
    /// 作为可挂在任意 Canvas 下的 MonoBehaviour View（非 UGuiForm），
    /// 通过 <see cref="Show"/> / <see cref="Hide"/> 驱动，配合 <c>TipHoverTrigger</c>
    /// 实现"鼠标悬浮显示 tips"——真实触发点后续由持有方接线。
    /// </summary>
    public abstract class ActionTipView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("左侧图标（Image 优先，缺图用 Emoji/文字兜底）")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private Text _iconLabel;

        [Header("卡片内容")]
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descText;

        [Header("底部信息行（可空：道具 Tips 无此行）")]
        [SerializeField] private GameObject _footerRoot;
        [SerializeField] private Text _footerText;

        /// <summary>显示 Tips（不吃射线，纯展示）。</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        /// <summary>隐藏 Tips。</summary>
        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        /// <summary>设置左侧图标：有 sprite 用图，否则回退 emoji/文字。</summary>
        protected void ApplyIcon(Sprite sprite, string emoji)
        {
            bool hasSprite = sprite != null;
            if (_iconImage != null)
            {
                _iconImage.enabled = hasSprite;
                _iconImage.sprite = sprite;
            }

            if (_iconLabel != null)
            {
                _iconLabel.gameObject.SetActive(!hasSprite && !string.IsNullOrEmpty(emoji));
                _iconLabel.text = emoji ?? string.Empty;
            }
        }

        /// <summary>设置标题与描述框正文。</summary>
        protected void ApplyTexts(string title, string desc)
        {
            if (_titleText != null)
            {
                _titleText.text = title ?? string.Empty;
            }

            if (_descText != null)
            {
                _descText.text = desc ?? string.Empty;
            }
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
