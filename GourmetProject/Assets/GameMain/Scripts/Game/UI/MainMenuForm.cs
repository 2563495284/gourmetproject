using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
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
        private int _transitionPreviewIndex;
        private readonly CartoonTransitionType[] _transitionPreviewTypes =
        {
            CartoonTransitionType.FoodWipe,
            CartoonTransitionType.IrisWipe,
            CartoonTransitionType.Fade,
            CartoonTransitionType.Curtain,
            CartoonTransitionType.PageTurn,
            CartoonTransitionType.SauceSplat,
            CartoonTransitionType.CartoonBurst,
            CartoonTransitionType.FoodCurtain,
        };

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
            bool hasSave = GameApp.Save.Has(UIForms.GameSaveSlot);
            var data = new CartoonSceneTransitionData
            {
                TransitionType = NextTransitionPreviewType(),
                Message = hasSave ? "继续开饭！" : "开饭啦！",
                CoverDuration = 0.42f,
                HoldDuration = 0.2f,
                RevealDuration = 0.34f,
                OnCovered = () => Log.Info(hasSave ? "Continue game (gameplay not implemented yet)." : "Start new game (gameplay not implemented yet).", Tag),
            };

            GameApp.UI.OpenUIForm(UIForms.CartoonSceneTransition, UIForms.GroupDialog, data);
        }

        private CartoonTransitionType NextTransitionPreviewType()
        {
            var type = _transitionPreviewTypes[_transitionPreviewIndex % _transitionPreviewTypes.Length];
            _transitionPreviewIndex++;
            return type;
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
