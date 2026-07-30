using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 行动轴节点 / 道具 hover Tips 的共享基类。
    /// 统一结构：卡片（标题 / 描述框 / 可选底部信息行）。
    ///
    /// 行动轴节点共用 <c>TimelineNodeTipView</c>，道具使用带动态词条布局的
    /// <c>ItemTipView</c>；公共展示 / 显隐逻辑收敛在此基类。
    ///
    /// 作为可挂在任意 Canvas 下的 MonoBehaviour View（非 UGuiForm），
    /// 通过 <see cref="Show"/> / <see cref="Hide"/> 驱动，配合 <c>TipHoverTrigger</c>
    /// 实现"鼠标悬浮显示 tips"——真实触发点后续由持有方接线。
    /// </summary>
    public abstract class ActionTipView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("卡片内容")]
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descText;

        [Header("底部信息行（可空：道具 Tips 无此行）")]
        [SerializeField] private GameObject _footerRoot;
        [SerializeField] private Text _footerText;

        protected Text TitleText => _titleText;
        protected Text DescText => _descText;

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
