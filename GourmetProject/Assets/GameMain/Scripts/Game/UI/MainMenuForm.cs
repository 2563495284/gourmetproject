using GourmetProject.Game.Roguelike;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 主菜单：开始/继续游戏、设置、退出（二次确认）。
    /// </summary>
    public sealed class MainMenuForm : UGuiForm
    {
        private const string Tag = "MainMenu";

        private Button _startButton;
        private Button _settingsButton;
        private Button _quitButton;
        private Text _startLabel;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _startButton = CachedTransform.Find("StartButton").GetComponent<Button>();
            _settingsButton = CachedTransform.Find("SettingsButton").GetComponent<Button>();
            _quitButton = CachedTransform.Find("QuitButton").GetComponent<Button>();
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
            if (GameApp.Save.Has(UIForms.GameSaveSlot)
                && GameApp.Save.TryLoad(UIForms.GameSaveSlot, out RunSaveData saved))
            {
                RunSession.PendingLoad = saved;
                Log.Info("Continue clicked: loaded run save.", Tag);
            }
            else
            {
                RunSession.Clear();
                Log.Info("Start clicked: requesting new run.", Tag);
            }

            GourmetProject.Game.Procedure.GameplayLauncher.RequestStart();
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
    }
}
