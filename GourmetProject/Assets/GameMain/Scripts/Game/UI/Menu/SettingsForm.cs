using System.Collections.Generic;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Run;
using GourmetProject.Game.Settings;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 设置界面。设置行预制在 SettingsForm/Content 中，运行时只绑定
    /// <see cref="SettingsCatalog"/> 的数据与交互。
    /// </summary>
    public sealed class SettingsForm : UGuiForm
    {
        [SerializeField] private List<GameObject> _settingRows = new List<GameObject>();

        private Button _applyButton;
        private Button _backButton;
        private GameObject _gameplayActions;
        private Button _returnMenuButton;
        private Button _abandonRunButton;

        private readonly List<SettingDescriptor> _descriptors = new List<SettingDescriptor>();
        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _applyButton = CachedTransform.Find("ApplyButton").GetComponent<Button>();
            _backButton = CachedTransform.Find("BackButton").GetComponent<Button>();
            _gameplayActions = CachedTransform.Find("GameplayActions")?.gameObject;
            _returnMenuButton = FindOptionalButton("ReturnMenuButton");
            _abandonRunButton = FindOptionalButton("AbandonRunButton");

            _applyButton.onClick.AddListener(OnApplyClicked);
            _backButton.onClick.AddListener(OnBackClicked);
            _returnMenuButton?.onClick.AddListener(OnReturnMenuClicked);
            _abandonRunButton?.onClick.AddListener(OnAbandonRunClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (_gameplayActions != null)
            {
                // GameplayActions 的返回主菜单/放弃游戏入口暂不展示，保留对象与逻辑供后续启用。
                _gameplayActions.SetActive(false);
            }

            BuildRows();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            UnbindRows();
            base.OnClose(isShutdown, userData);
        }

        private void BuildRows()
        {
            UnbindRows();
            _descriptors.Clear();
            _descriptors.AddRange(SettingsCatalog.BuildDefault());

            int boundCount = Mathf.Min(_descriptors.Count, _settingRows.Count);
            for (int i = 0; i < _settingRows.Count; i++)
            {
                GameObject row = _settingRows[i];
                if (row == null)
                {
                    continue;
                }

                bool shouldShow = i < boundCount;
                row.SetActive(shouldShow);
                if (shouldShow)
                {
                    BindRow(_descriptors[i], row.transform);
                }
            }

            if (_descriptors.Count != _settingRows.Count)
            {
                Debug.LogWarning(
                    $"SettingsForm 预制行数量({_settingRows.Count})与设置描述符数量({_descriptors.Count})不一致。",
                    this);
            }
        }

        private static void BindRow(SettingDescriptor descriptor, Transform row)
        {
            var label = row.Find("Label").GetComponent<Text>();
            label.text = descriptor.Label;

            switch (descriptor.ControlType)
            {
                case SettingControlType.Dropdown:
                {
                    var dropdown = row.Find("Dropdown").GetComponent<Dropdown>();
                    dropdown.onValueChanged.RemoveAllListeners();
                    dropdown.ClearOptions();
                    dropdown.AddOptions(descriptor.GetOptions());
                    dropdown.SetValueWithoutNotify(descriptor.GetSelectedIndex());
                    dropdown.onValueChanged.AddListener(i => descriptor.SetSelectedIndex(i));
                    break;
                }

                case SettingControlType.Slider:
                {
                    var slider = row.Find("Slider").GetComponent<Slider>();
                    var valueText = row.Find("Value").GetComponent<Text>();
                    slider.onValueChanged.RemoveAllListeners();
                    slider.minValue = descriptor.SliderMin;
                    slider.maxValue = descriptor.SliderMax;
                    slider.wholeNumbers = descriptor.SliderWholeNumbers;
                    float current = descriptor.GetSliderValue();
                    slider.SetValueWithoutNotify(current);
                    valueText.text = FormatSlider(descriptor, current);
                    slider.onValueChanged.AddListener(v =>
                    {
                        descriptor.SetSliderValue(v);
                        valueText.text = FormatSlider(descriptor, v);
                    });
                    break;
                }

                case SettingControlType.Toggle:
                {
                    var toggle = row.Find("Toggle").GetComponent<Toggle>();
                    toggle.onValueChanged.RemoveAllListeners();
                    toggle.SetIsOnWithoutNotify(descriptor.GetToggleValue());
                    toggle.onValueChanged.AddListener(v => descriptor.SetToggleValue(v));
                    break;
                }
            }
        }

        private static string FormatSlider(SettingDescriptor descriptor, float value)
        {
            return descriptor.FormatSliderValue != null ? descriptor.FormatSliderValue(value) : value.ToString("0.00");
        }

        private void UnbindRows()
        {
            foreach (var row in _settingRows)
            {
                if (row == null)
                {
                    continue;
                }

                Transform rowTransform = row.transform;
                rowTransform.Find("Dropdown")?.GetComponent<Dropdown>()?.onValueChanged.RemoveAllListeners();
                rowTransform.Find("Slider")?.GetComponent<Slider>()?.onValueChanged.RemoveAllListeners();
                rowTransform.Find("Toggle")?.GetComponent<Toggle>()?.onValueChanged.RemoveAllListeners();
            }
        }

        private void OnApplyClicked()
        {
            // 值已在控件回调里写入 SettingsService，这里统一落地并保存。
            foreach (var descriptor in _descriptors)
            {
                descriptor.Apply?.Invoke();
            }

            GameApp.Settings.ApplyAll();
            GameApp.Settings.Save();
        }

        private void OnBackClicked()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void OnReturnMenuClicked()
        {
            var data = new ConfirmDialogData
            {
                Title = "返回主菜单",
                Message = "当前未保存的进度将丢失，可以从上一次存档继续游戏。",
                ConfirmText = "返回",
                CancelText = "取消",
                OnConfirm = ReturnToMenuWithoutSave,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private void OnAbandonRunClicked()
        {
            var data = new ConfirmDialogData
            {
                Title = "放弃游戏",
                Message = "确定放弃当前游戏吗？这会按失败结算并删除当前运行存档。",
                ConfirmText = "放弃",
                CancelText = "取消",
                OnConfirm = OpenDefeatFromSettings,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private void ReturnToMenuWithoutSave()
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
                    GameplayFlowSignal.RequestReturnToMenu();
                },
            };

            CartoonSceneTransitionForm.Show(data);
        }

        private void OpenDefeatFromSettings()
        {
            int total = BattleForm.Active?.LastBattleTotal ?? 0;
            GameApp.UI.CloseUIForm(UIForm);
            GameApp.UI.OpenUIForm(UIForms.Defeat, UIForms.GroupDialog, new GourmetProject.Game.UI.Meta.DefeatFormData(total));
        }

        private Button FindOptionalButton(string childName)
        {
            Transform[] children = CachedTransform.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child.name == childName && child.TryGetComponent(out Button button))
                {
                    return button;
                }
            }

            return null;
        }
    }

    public sealed class SettingsFormData
    {
        public SettingsFormData(bool inGameplay)
        {
            InGameplay = inGameplay;
        }

        public bool InGameplay { get; }
    }
}
