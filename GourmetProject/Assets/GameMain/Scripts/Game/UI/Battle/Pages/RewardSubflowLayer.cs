using GourmetProject.Game.UI.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.Pages
{
    /// <summary>
    /// Battle 内部的临时领奖覆盖层。它不拥有业务状态，只保证背景、输入拦截和领奖面板
    /// 在同一个可见层中同步交接；来源页面始终留在其下方。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup), typeof(Image))]
    public sealed class RewardSubflowLayer : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Image _background;

        internal bool IsVisible => gameObject.activeSelf
            && _group != null
            && _group.alpha > 0.999f
            && _group.blocksRaycasts;

        internal void Initialize()
        {
            _group = _group != null ? _group : GetComponent<CanvasGroup>();
            _background = _background != null ? _background : GetComponent<Image>();
            if (_group == null || _background == null)
            {
                Debug.LogError($"{nameof(RewardSubflowLayer)} prefab 缺少 CanvasGroup 或 Image。", this);
            }

            HideImmediate();
        }

        internal void Prepare()
        {
            if (_group == null)
            {
                Debug.LogError($"{nameof(RewardSubflowLayer)} 尚未从 prefab 初始化。", this);
                return;
            }

            gameObject.SetActive(true);
            _group.alpha = 1f;
            _group.interactable = true;
            _group.blocksRaycasts = true;
        }

        internal void Show(Component panel)
        {
            Prepare();
            if (panel == null)
            {
                return;
            }

            panel.gameObject.SetActive(true);
            panel.transform.SetAsLastSibling();
        }

        internal void Hide(Component panel)
        {
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }
        }

        internal void HideImmediate()
        {
            if (_group != null)
            {
                _group.alpha = 0f;
                _group.interactable = false;
                _group.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }
    }
}
