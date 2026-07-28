using System;
using System.Collections.Generic;
using GourmetProject.Game.Flow;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 角色选择界面（仿杀戮尖塔）：左右箭头切换角色，翻页圆点指示当前位置，
    /// 「选中」确认开局（播放转场），「返回」回主菜单。
    /// 角色列表、名称、描述、立绘路径与开局角色 id 均来自 <c>TbCharacter</c>。
    /// </summary>
    public sealed class CharacterSelectForm : UGuiForm
    {
        private const string Tag = "CharacterSelect";

        private static readonly Color DotSelected = new(1f, 0.6f, 0.16f, 1f);
        private static readonly Color DotNormal = new(1f, 1f, 1f, 0.45f);
        public Text _nameText;
        public Text _descText;
        public Button _leftArrow;
        public Button _rightArrow;
        public Button _confirmButton;
        public Button _backButton;
        public Image _portraitImage;

        private readonly List<Image> _dots = new();
        private IReadOnlyList<cfg.Character> _characters = Array.Empty<cfg.Character>();
        private int _index;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            EnsureReferences();
            CollectDots();

            _leftArrow.onClick.AddListener(OnPrevClicked);
            _rightArrow.onClick.AddListener(OnNextClicked);
            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _backButton.onClick.AddListener(OnBackClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            ReloadCharacters();
            _index = 0;
            Refresh();
        }

        private void OnPrevClicked()
        {
            int count = _characters.Count;
            if (count == 0)
            {
                return;
            }

            _index = (_index - 1 + count) % count;
            Refresh();
        }

        private void OnNextClicked()
        {
            int count = _characters.Count;
            if (count == 0)
            {
                return;
            }

            _index = (_index + 1) % count;
            Refresh();
        }

        private void OnConfirmClicked()
        {
            if (_characters.Count == 0)
            {
                return;
            }

            cfg.Character character = _characters[_index];
            var data = new CartoonSceneTransitionData
            {
                TransitionType = CartoonTransitionType.Fade,
                Message = "",
                CoverDuration = 0.42f,
                HoldDuration = 0.2f,
                RevealDuration = 0.34f,
                OnCovered = () =>
                {
                    Log.Info($"Selected character '{character.Id}', starting run.", Tag);
                    GameplayEntryRequest.RequestNewRun(character.Id);
                },
                IsReadyToReveal = IsBattleReady,
            };

            CartoonSceneTransitionForm.Show(data);
        }

        private static bool IsBattleReady()
        {
            return GameApp.Scenes.IsLoaded(SceneNames.Battle) && BattleForm.Active != null;
        }

        private void OnBackClicked()
        {
            GameApp.UI.CloseUIForm(UIForm);
            GameApp.UI.OpenUIForm(UIForms.MainMenu, UIForms.GroupDefault);
        }

        private void Refresh()
        {
            int count = _characters.Count;
            bool hasOptions = count > 0;
            _leftArrow.interactable = count > 1;
            _rightArrow.interactable = count > 1;
            _confirmButton.interactable = hasOptions;

            if (!hasOptions)
            {
                _nameText.text = string.Empty;
                _descText.text = string.Empty;
                SetPortrait(null);
                RefreshDots();
                return;
            }

            _index = Mathf.Clamp(_index, 0, count - 1);
            cfg.Character character = _characters[_index];
            _nameText.text = string.IsNullOrWhiteSpace(character.Name)
                ? character.Id
                : character.Name;
            _descText.text = character.Desc ?? string.Empty;
            SetPortrait(LoadPortrait(character.Portrait));
            RefreshDots();
        }

        private void ReloadCharacters()
        {
            _characters =
                GameApp.Config?.Tables?.TbCharacter?.DataList
                ?? Array.Empty<cfg.Character>();

            if (_characters.Count == 0)
            {
                Log.Warning("TbCharacter has no selectable characters.", Tag);
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
                bool active = i < _characters.Count;
                _dots[i].gameObject.SetActive(active);
                if (active)
                {
                    _dots[i].color = i == _index ? DotSelected : DotNormal;
                }
            }
        }

        private void EnsureReferences()
        {
            _nameText ??= FindRequiredComponentInChildren<Text>("CharacterName");
            _descText ??= FindRequiredComponentInChildren<Text>("CharacterDesc");
            _leftArrow ??= FindRequiredComponentInChildren<Button>("LeftArrow");
            _rightArrow ??= FindRequiredComponentInChildren<Button>("RightArrow");
            _confirmButton ??= FindRequiredComponentInChildren<Button>("ConfirmButton");
            _backButton ??= FindRequiredComponentInChildren<Button>("BackButton");
            _portraitImage ??= FindOptionalComponentInChildren<Image>("CharacterPortrait");
        }

        private static Sprite LoadPortrait(string resourcePath)
        {
            return string.IsNullOrWhiteSpace(resourcePath)
                ? null
                : Resources.Load<Sprite>(resourcePath);
        }

        private void SetPortrait(Sprite portrait)
        {
            if (_portraitImage == null)
            {
                return;
            }

            _portraitImage.sprite = portrait;
            _portraitImage.enabled = portrait != null;
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
            T component = FindOptionalComponentInChildren<T>(childName);
            if (component != null)
            {
                return component;
            }

            throw new MissingComponentException($"CharacterSelectForm requires child '{childName}' with component {typeof(T).Name}.");
        }

        private T FindOptionalComponentInChildren<T>(string childName) where T : Component
        {
            foreach (Transform child in CachedTransform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName && child.TryGetComponent(out T component))
                {
                    return component;
                }
            }

            return null;
        }
    }
}
