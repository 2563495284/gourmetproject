using System;
using GourmetProject.Game.Presentation.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    internal interface IBattleTableFragmentEditLayer
    {
        bool IsVisible { get; }

        bool IsConfigured { get; }

        GameObject BoardEditPanel { get; }

        void Initialize();

        void Bind(Action onAction);

        void Show();

        void HideImmediate();

        void ApplyActionState(TableFragmentEditActionState state);
    }

    /// <summary>
    /// 餐桌碎片编辑的独立 UI 层。BoardEditPanel 永久归属此层；该组件只管理层输入、显隐和确认/跳过按钮。
    /// </summary>
    public sealed class BattleTableFragmentEditLayer : MonoBehaviour, IBattleTableFragmentEditLayer
    {
        private static readonly Color ConfirmColor = new Color(0.08f, 0.62f, 0.12f, 1f);

        [SerializeField] private CanvasGroup _group;
        [SerializeField] private GameObject _boardEditPanel;
        [SerializeField] private Button _actionButton;

        private Color _skipColor = new Color(0.72f, 0.02f, 0.02f, 1f);
        private Action _onAction;

        public bool IsVisible => gameObject.activeSelf && _group != null && _group.alpha > 0.999f;

        internal bool IsConfigured =>
            _group != null
            && _boardEditPanel != null
            && _actionButton != null
            && _boardEditPanel.transform.parent == transform;

        bool IBattleTableFragmentEditLayer.IsConfigured => IsConfigured;

        public GameObject BoardEditPanel => _boardEditPanel;

        public void Initialize()
        {
            _group = _group != null ? _group : GetComponent<CanvasGroup>();
            if (!IsConfigured)
            {
                Debug.LogError($"{nameof(BattleTableFragmentEditLayer)} prefab 引用不完整。", this);
            }

            if (_actionButton != null && _actionButton.targetGraphic != null)
            {
                _skipColor = _actionButton.targetGraphic.color;
            }

            BindButton();
            ApplyActionState(new TableFragmentEditActionState(false, false));
            HideImmediate();
        }

        public void Bind(Action onAction)
        {
            _onAction = onAction;
            BindButton();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_boardEditPanel != null)
            {
                _boardEditPanel.SetActive(true);
            }

            if (_group != null)
            {
                _group.alpha = 1f;
                _group.interactable = true;
                _group.blocksRaycasts = true;
            }
        }

        public void HideImmediate()
        {
            if (_group != null)
            {
                _group.alpha = 0f;
                _group.interactable = false;
                _group.blocksRaycasts = false;
            }

            if (_boardEditPanel != null)
            {
                _boardEditPanel.SetActive(false);
            }

            gameObject.SetActive(false);
        }

        public void ApplyActionState(TableFragmentEditActionState state)
        {
            if (_actionButton == null)
            {
                return;
            }

            _actionButton.interactable = state.Interactable;
            if (_actionButton.targetGraphic != null)
            {
                _actionButton.targetGraphic.color = state.CanConfirm ? ConfirmColor : _skipColor;
            }

            TMP_Text label = _actionButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = state.CanConfirm ? "确认" : "跳过";
            }
        }

        private void BindButton()
        {
            if (_actionButton == null)
            {
                return;
            }

            _actionButton.onClick.RemoveListener(OnActionClicked);
            _actionButton.onClick.AddListener(OnActionClicked);
        }

        private void OnActionClicked()
        {
            _onAction?.Invoke();
        }
    }
}
