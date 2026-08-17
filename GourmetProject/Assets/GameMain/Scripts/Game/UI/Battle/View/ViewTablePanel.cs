using System;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>查看餐桌态的 UI 覆盖层；退出入口由面板自身持有，不复用左栏的查看按钮。</summary>
    public sealed class ViewTablePanel : MonoBehaviour
    {
        [SerializeField] private Button _exitEditButton;

        private Action _onExit;

        public void Bind(Action onExit)
        {
            _onExit = onExit;
            if (_exitEditButton == null)
            {
                return;
            }

            _exitEditButton.onClick.RemoveListener(OnExitClicked);
            _exitEditButton.onClick.AddListener(OnExitClicked);
        }

        public void SetExitVisible(bool visible)
        {
            if (_exitEditButton != null)
            {
                _exitEditButton.gameObject.SetActive(visible);
            }
        }

        private void OnDestroy()
        {
            if (_exitEditButton != null)
            {
                _exitEditButton.onClick.RemoveListener(OnExitClicked);
            }
        }

        private void OnExitClicked()
        {
            _onExit?.Invoke();
        }
    }
}
