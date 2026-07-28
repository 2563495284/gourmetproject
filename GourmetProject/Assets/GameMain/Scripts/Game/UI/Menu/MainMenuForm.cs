using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 主菜单：开始/继续游戏、设置、退出（二次确认）。
    /// </summary>
    public sealed class MainMenuForm : UGuiForm
    {
        private Button _startButton;
        private Button _abandonButton;
        private Button _settingsButton;
        private Button _quitButton;
        private Text _startLabel;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _startButton = FindRequiredComponentInChildren<Button>("StartButton");
            _abandonButton = FindRequiredComponentInChildren<Button>("AbandonButton");
            _settingsButton = FindRequiredComponentInChildren<Button>("SettingsButton");
            _quitButton = FindRequiredComponentInChildren<Button>("QuitButton");
            _startLabel = _startButton.transform.Find("Text").GetComponent<Text>();

            _startButton.onClick.AddListener(OnStartClicked);
            _abandonButton.onClick.AddListener(OnAbandonClicked);
            _settingsButton.onClick.AddListener(OnSettingsClicked);
            _quitButton.onClick.AddListener(OnQuitClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // Launch 场景内的同步背景只用于遮住首个 UI 资源加载帧。
            // 主菜单完成实例化后立即关闭，避免它在后续 Battle 场景中遮挡世界相机。
            GameObject startupBackground = GameObject.Find("StartupBackground");
            if (startupBackground != null)
            {
                startupBackground.SetActive(false);
            }

            RefreshState();
        }

        // 刷新「开始/继续」文案与「放弃」按钮可见性：仅在存在存档（可继续）时显示放弃。
        private void RefreshState()
        {
            bool hasSave = GameApp.Save.Has(UIForms.GameSaveSlot);
            _startLabel.text = hasSave ? "继续游戏" : "开始游戏";
            _abandonButton.gameObject.SetActive(hasSave);
        }

        private void OnStartClicked()
        {
            // 有存档则「继续游戏」：过场盖住后再登记继续请求，由 ProcedureMenu 轮询切入玩法流程并读档。
            if (GameApp.Save.Has(UIForms.GameSaveSlot))
            {
                var data = new CartoonSceneTransitionData
                {
                    TransitionType = CartoonTransitionType.Fade,
                    Message = "",
                    CoverDuration = 0.42f,
                    HoldDuration = 0.2f,
                    RevealDuration = 0.34f,
                    OnCovered = () =>
                    {
                        GameApp.UI.CloseUIForm(UIForm);
                        GameplayEntryRequest.RequestContinue();
                    },
                    IsReadyToReveal = IsBattleReady,
                };

                CartoonSceneTransitionForm.Show(data);
                return;
            }

            // 否则杀戮尖塔流程：主菜单 -> 角色选择 -> 确认开局。
            // 开局转场由角色选择界面在“确认”时触发。
            GameApp.UI.CloseUIForm(UIForm);
            GameApp.UI.OpenUIForm(UIForms.CharacterSelect, UIForms.GroupDefault);
        }

        private static bool IsBattleReady()
        {
            return GameApp.Scenes.IsLoaded(SceneNames.Battle) && BattleForm.Active != null;
        }

        private void OnAbandonClicked()
        {
            // 放弃当前游戏：二次确认后删除存档，回到「开始游戏」状态。
            var data = new ConfirmDialogData
            {
                Title = "放弃游戏",
                Message = "确定要放弃当前的游戏进度吗？此操作无法撤销。",
                ConfirmText = "放弃",
                CancelText = "取消",
                OnConfirm = AbandonCurrentRun,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private void AbandonCurrentRun()
        {
            RunPersistence.Delete();
            RefreshState();
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
