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
    /// 设置界面。根据 <see cref="SettingsCatalog"/> 的描述符动态生成设置行，
    /// 完全数据驱动——新增设置项不需要改本类。
    /// </summary>
    public sealed class SettingsForm : UGuiForm
    {
        [SerializeField] private GameObject _dropdownRowPrefab;
        [SerializeField] private GameObject _sliderRowPrefab;
        [SerializeField] private GameObject _toggleRowPrefab;

        private Transform _content;
        private Button _applyButton;
        private Button _backButton;
        private GameObject _gameplayActions;
        private Button _returnMenuButton;
        private Button _abandonRunButton;

        private readonly List<SettingDescriptor> _descriptors = new List<SettingDescriptor>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private bool _inGameplay;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _content = CachedTransform.Find("Content");
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

            _inGameplay = userData is SettingsFormData { InGameplay: true };
            if (_gameplayActions != null)
            {
                _gameplayActions.SetActive(_inGameplay);
            }

            BuildRows();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            ClearRows();
            base.OnClose(isShutdown, userData);
        }

        private void BuildRows()
        {
            ClearRows();
            _descriptors.Clear();
            _descriptors.AddRange(SettingsCatalog.BuildDefault());

            foreach (var descriptor in _descriptors)
            {
                GameObject row = InstantiateRow(descriptor.ControlType);
                if (row == null)
                {
                    continue;
                }

                _rows.Add(row);
                BindRow(descriptor, row.transform);
            }
        }

        private GameObject InstantiateRow(SettingControlType type)
        {
            GameObject prefab = type switch
            {
                SettingControlType.Dropdown => _dropdownRowPrefab,
                SettingControlType.Slider => _sliderRowPrefab,
                SettingControlType.Toggle => _toggleRowPrefab,
                _ => null,
            };

            if (prefab == null)
            {
                return null;
            }

            GameObject row = Instantiate(prefab, _content, false);
            row.transform.localScale = Vector3.one;
            return row;
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

        private void ClearRows()
        {
            foreach (var row in _rows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }

            _rows.Clear();
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
                Message = "当前进度将保存，可以从主菜单继续游戏。",
                ConfirmText = "返回",
                CancelText = "取消",
                OnConfirm = ReturnToMenuWithSave,
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

        private void ReturnToMenuWithSave()
        {
            GameRun run = GameRunContext.Current;
            if (run != null)
            {
                RunPersistence.Save(run);
            }

            GameApp.UI.CloseUIForm(UIForm);
            GameplayFlowSignal.RequestReturnToMenu();
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
