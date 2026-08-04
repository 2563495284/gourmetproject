using GourmetProject.Game.Adapter;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 主菜单：开始游戏、设置、退出（二次确认）。
    /// </summary>
    public sealed class MainMenuForm : UGuiForm
    {
        private Button _startButton;
        private Button _settingsButton;
        private Button _quitButton;
        private RectTransform _leftSelectionArrow;
        private RectTransform _rightSelectionArrow;

        private const float SelectionArrowGap = 27.5f;
        private const float SelectionArrowVerticalOffset = -2.5f;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _startButton = FindRequiredComponentInChildren<Button>("StartButton");
            _settingsButton = FindRequiredComponentInChildren<Button>("SettingsButton");
            _quitButton = FindRequiredComponentInChildren<Button>("QuitButton");
            _leftSelectionArrow = FindRequiredComponentInChildren<RectTransform>("SelectionArrowLeft");
            _rightSelectionArrow = FindRequiredComponentInChildren<RectTransform>("SelectionArrowRight");

            _startButton.onClick.AddListener(OnStartClicked);
            _settingsButton.onClick.AddListener(OnSettingsClicked);
            _quitButton.onClick.AddListener(OnQuitClicked);

            BindSelectionArrowEvents(_startButton);
            BindSelectionArrowEvents(_settingsButton);
            BindSelectionArrowEvents(_quitButton);
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

            MoveSelectionArrows(_startButton);
        }

        private void BindSelectionArrowEvents(Button button)
        {
            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = button.gameObject.AddComponent<EventTrigger>();
            }

            AddSelectionArrowEvent(trigger, EventTriggerType.PointerEnter, button);
            AddSelectionArrowEvent(trigger, EventTriggerType.Select, button);
        }

        private void AddSelectionArrowEvent(EventTrigger trigger, EventTriggerType eventType, Button button)
        {
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(_ => MoveSelectionArrows(button));
            trigger.triggers.Add(entry);
        }

        private void MoveSelectionArrows(Button button)
        {
            var target = (RectTransform)button.transform;
            Vector2 targetPosition = target.anchoredPosition;
            float horizontalOffset = target.rect.width * 0.5f + SelectionArrowGap;

            _leftSelectionArrow.anchoredPosition = new Vector2(
                targetPosition.x - horizontalOffset - _leftSelectionArrow.rect.width * 0.5f,
                targetPosition.y + SelectionArrowVerticalOffset);
            _rightSelectionArrow.anchoredPosition = new Vector2(
                targetPosition.x + horizontalOffset + _rightSelectionArrow.rect.width * 0.5f,
                targetPosition.y + SelectionArrowVerticalOffset);
        }

        private void OnStartClicked()
        {
            // 存档入口统一放在经营方向选择界面。
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
                ConfirmText = "应用",
                CancelText = "返回",
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
