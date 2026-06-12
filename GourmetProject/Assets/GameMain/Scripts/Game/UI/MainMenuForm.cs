using GourmetProject.Game.Gameplay;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 主菜单：开始/继续游戏、设置、退出（二次确认）。
    /// </summary>
    public sealed class MainMenuForm : UGuiForm
    {
        private Button _startButton;
        private Button _settingsButton;
        private Button _quitButton;
        private Text _startLabel;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _startButton = FindRequiredComponentInChildren<Button>("StartButton");
            _settingsButton = FindRequiredComponentInChildren<Button>("SettingsButton");
            _quitButton = FindRequiredComponentInChildren<Button>("QuitButton");
            _startLabel = _startButton.transform.Find("Text").GetComponent<Text>();

            _startButton.onClick.AddListener(OnStartClicked);
            _settingsButton.onClick.AddListener(OnSettingsClicked);
            _quitButton.onClick.AddListener(OnQuitClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            bool hasSave = GameApp.Save.Has(UIForms.GameSaveSlot);
            _startLabel.text = hasSave ? "继续游戏" : "开始游戏";
        }

        private void OnStartClicked()
        {
            // 有存档则「继续游戏」：登记继续请求，由 ProcedureMenu 轮询切入玩法流程并读档。
            if (GameApp.Save.Has(UIForms.GameSaveSlot))
            {
                GameApp.UI.CloseUIForm(UIForm);
                GameplayEntryRequest.RequestContinue();
                return;
            }

            // 否则杀戮尖塔流程：主菜单 -> 角色选择 -> 确认开局。
            // 开局转场由角色选择界面在“确认”时触发。
            GameApp.UI.CloseUIForm(UIForm);
            GameApp.UI.OpenUIForm(UIForms.CharacterSelect, UIForms.GroupDefault);
        }

        private void OnSettingsClicked()
        {
            GameApp.UI.OpenUIForm(UIForms.Settings, UIForms.GroupDefault);
        }

        private void OnQuitClicked()
        {
            var data = new ConfirmDialogData
            {
                Title = "退出游戏",
                Message = "确定要退出游戏吗？",
                ConfirmText = "退出",
                CancelText = "取消",
                OnConfirm = () => GameEntry.Shutdown(ShutdownType.Quit),
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private T FindRequiredComponentInChildren<T>(string childName) where T : Component
        {
            foreach (Transform child in CachedTransform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName && child.TryGetComponent(out T component))
                {
                    return component;
                }
            }

            throw new MissingComponentException($"MainMenuForm requires child '{childName}' with component {typeof(T).Name}.");
        }
    }
}
