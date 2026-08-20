using System;
using System.Collections.Generic;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Settings;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Widgets;
using TMPro;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 设置界面。设置行预制在 SettingsForm/Content 中，运行时只绑定
    /// <see cref="SettingsCatalog"/> 的数据与交互。
    /// </summary>
    public sealed class SettingsForm : UGuiForm
    {
        [SerializeField] private List<GameObject> _settingRows = new List<GameObject>();
        [SerializeField] private Button _applyButton;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _returnMenuButton;

        private bool _inGameplay;
        private SettingsFormData _formData;

        private readonly List<SettingDescriptor> _descriptors = new List<SettingDescriptor>();
        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _applyButton.onClick.AddListener(OnApplyClicked);
            _backButton.onClick.AddListener(OnBackClicked);
            _returnMenuButton.onClick.AddListener(OnReturnMenuClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _formData = userData as SettingsFormData;
            _inGameplay = _formData?.InGameplay == true;
            _formData?.AcquireSettlementPause();
            _returnMenuButton.gameObject.SetActive(_inGameplay);

            BuildRows();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            UnbindRows();
            _formData?.ReleaseSettlementPause();
            _formData = null;
            base.OnClose(isShutdown, userData);
        }

        private void BuildRows()
        {
            UnbindRows();
            _descriptors.Clear();
            _descriptors.AddRange(SettingsCatalog.BuildDefault());
            EnsureRowCapacity(_descriptors.Count);

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

        private void EnsureRowCapacity(int requiredCount)
        {
            if (_settingRows.Count == 0)
            {
                return;
            }

            GameObject template = _settingRows[_settingRows.Count - 1];
            while (_settingRows.Count < requiredCount && template != null)
            {
                GameObject clone = Instantiate(template, template.transform.parent);
                clone.name = $"SettingRow{_settingRows.Count + 1}";
                clone.SetActive(false);
                _settingRows.Add(clone);
            }
        }

        private static void BindRow(SettingDescriptor descriptor, Transform row)
        {
            var label = row.Find("Label").GetComponent<TMP_Text>();
            label.text = descriptor.Label;

            switch (descriptor.ControlType)
            {
                case SettingControlType.Dropdown:
                {
                    var dropdown = row.Find("Dropdown").GetComponent<TMP_Dropdown>();
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
                    var valueText = row.Find("Value").GetComponent<TMP_Text>();
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
                rowTransform.Find("Dropdown")?.GetComponent<TMP_Dropdown>()?.onValueChanged.RemoveAllListeners();
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
            ToastService.Show("应用成功", ToastKind.Success);
        }

        private void OnBackClicked()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void OnReturnMenuClicked()
        {
            if (!_inGameplay)
            {
                return;
            }

            var data = new ConfirmDialogData
            {
                Title = "返回主菜单",
                Message = "当前未保存的进度将丢失，可以从上一次存档继续游戏。",
                ConfirmText = "确定",
                CancelText = "返回",
                OnConfirm = ReturnToMenuWithoutSave,
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
    }

    public sealed class SettingsFormData
    {
        private readonly Func<bool> _pauseSettlementPlayback;
        private readonly Action _resumeSettlementPlayback;
        private bool _settlementPauseOwned;

        public SettingsFormData(
            bool inGameplay,
            Func<bool> pauseSettlementPlayback = null,
            Action resumeSettlementPlayback = null)
        {
            InGameplay = inGameplay;
            _pauseSettlementPlayback = pauseSettlementPlayback;
            _resumeSettlementPlayback = resumeSettlementPlayback;
        }

        public bool InGameplay { get; }

        internal bool SettlementPauseOwned => _settlementPauseOwned;

        internal void AcquireSettlementPause()
        {
            if (!_settlementPauseOwned)
            {
                _settlementPauseOwned = _pauseSettlementPlayback?.Invoke() == true;
            }
        }

        internal void ReleaseSettlementPause()
        {
            if (!_settlementPauseOwned)
            {
                return;
            }

            _settlementPauseOwned = false;
            _resumeSettlementPlayback?.Invoke();
        }
    }
}
