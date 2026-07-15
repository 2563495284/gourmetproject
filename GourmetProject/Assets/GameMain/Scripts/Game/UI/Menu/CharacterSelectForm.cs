using System.Collections.Generic;
using GourmetProject.Game.Flow;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 角色选择界面（仿杀戮尖塔）：左右箭头切换角色，翻页圆点指示当前位置，
    /// 「选中」确认开局（播放占位转场），「返回」回主菜单。
    /// 角色数值/系统暂未实现，数据来自 <see cref="CharacterOptions"/> 占位列表。
    /// </summary>
    public sealed class CharacterSelectForm : UGuiForm
    {
        private const string Tag = "CharacterSelect";

        private static readonly Color DotSelected = new(1f, 0.6f, 0.16f, 1f);
        private static readonly Color DotNormal = new(1f, 1f, 1f, 0.45f);

        private Image _portraitImage;
        private Text _nameText;
        private Text _descText;
        private Button _leftArrow;
        private Button _rightArrow;
        private Button _confirmButton;
        private Button _backButton;

        private readonly List<Image> _dots = new();
        private CharacterOption[] _options;
        private int _index;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _options = CharacterOptions.All;

            _portraitImage = FindRequiredComponentInChildren<Image>("PortraitImage");
            _nameText = FindRequiredComponentInChildren<Text>("CharacterName");
            _descText = FindRequiredComponentInChildren<Text>("CharacterDesc");
            _leftArrow = FindRequiredComponentInChildren<Button>("LeftArrow");
            _rightArrow = FindRequiredComponentInChildren<Button>("RightArrow");
            _confirmButton = FindRequiredComponentInChildren<Button>("ConfirmButton");
            _backButton = FindRequiredComponentInChildren<Button>("BackButton");

            CollectDots();

            _leftArrow.onClick.AddListener(OnPrevClicked);
            _rightArrow.onClick.AddListener(OnNextClicked);
            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _backButton.onClick.AddListener(OnBackClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _index = 0;
            Refresh();
        }

        private void OnPrevClicked()
        {
            if (_options.Length == 0)
            {
                return;
            }

            _index = (_index - 1 + _options.Length) % _options.Length;
            Refresh();
        }

        private void OnNextClicked()
        {
            if (_options.Length == 0)
            {
                return;
            }

            _index = (_index + 1) % _options.Length;
            Refresh();
        }

        private void OnConfirmClicked()
        {
            if (_options.Length == 0)
            {
                return;
            }

            CharacterOption option = _options[_index];
            var data = new CartoonSceneTransitionData
            {
                TransitionType = CartoonTransitionType.FoodWipe,
                Message = "开饭啦！",
                CoverDuration = 0.42f,
                HoldDuration = 0.2f,
                RevealDuration = 0.34f,
                OnCovered = () =>
                {
                    Log.Info($"Selected character '{option.Id}', starting run.", Tag);
                    GameplayEntryRequest.RequestNewRun(option.Id);
                },
            };

            CartoonSceneTransitionForm.Show(data);
        }

        private void OnBackClicked()
        {
            GameApp.UI.CloseUIForm(UIForm);
            GameApp.UI.OpenUIForm(UIForms.MainMenu, UIForms.GroupDefault);
        }

        private void Refresh()
        {
            bool hasOptions = _options.Length > 0;
            _leftArrow.interactable = _options.Length > 1;
            _rightArrow.interactable = _options.Length > 1;
            _confirmButton.interactable = hasOptions;

            if (!hasOptions)
            {
                _nameText.text = string.Empty;
                _descText.text = string.Empty;
                _portraitImage.enabled = false;
                return;
            }

            CharacterOption option = _options[_index];
            _nameText.text = option.DisplayName;
            _descText.text = option.Description;
            ApplyPortrait(option.PortraitResource);
            RefreshDots();
        }

        private void ApplyPortrait(string portraitResource)
        {
            Sprite sprite = string.IsNullOrEmpty(portraitResource)
                ? null
                : Resources.Load<Sprite>(portraitResource);

            if (sprite != null)
            {
                _portraitImage.sprite = sprite;
                _portraitImage.enabled = true;
            }
            else
            {
                // 立绘资源尚未就绪时保留占位底图，避免空界面。
                Log.Warning($"Portrait sprite not found at Resources path '{portraitResource}'.", Tag);
            }
        }

        private void CollectDots()
        {
            _dots.Clear();

            Transform dotsRoot = FindChild("PageDots");
            if (dotsRoot == null)
            {
                return;
            }

            foreach (Transform child in dotsRoot)
            {
                if (child.TryGetComponent(out Image image))
                {
                    _dots.Add(image);
                }
            }
        }

        private void RefreshDots()
        {
            for (int i = 0; i < _dots.Count; i++)
            {
                bool active = i < _options.Length;
                _dots[i].gameObject.SetActive(active);
                if (active)
                {
                    _dots[i].color = i == _index ? DotSelected : DotNormal;
                }
            }
        }

        private Transform FindChild(string childName)
        {
            foreach (Transform child in CachedTransform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
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

            throw new MissingComponentException($"CharacterSelectForm requires child '{childName}' with component {typeof(T).Name}.");
        }
    }
}
