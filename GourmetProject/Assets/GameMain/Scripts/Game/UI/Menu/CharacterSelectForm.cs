using System;
using System.Collections.Generic;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Library;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 角色选择界面（仿杀戮尖塔）：左右箭头切换角色，翻页圆点指示当前位置，
    /// 根据存档状态提供继续游戏或选择角色开新局入口，「返回」回主菜单。
    /// 角色列表、名称、描述、立绘路径与开局角色 id 均来自 <c>TbCharacter</c>。
    /// </summary>
    public sealed class CharacterSelectForm : UGuiForm
    {
        private const string Tag = "CharacterSelect";
        private const float RecipeButtonGlowPadding = 28f;
        private const float ActionButtonHorizontalOffset = 130f;

        private static readonly Color DotSelected = new(1f, 0.6f, 0.16f, 1f);
        private static readonly Color DotNormal = new(1f, 1f, 1f, 0.45f);
        private static readonly Color RecipeButtonGlowColor =
            new(0.25f, 1f, 0.35f, 0.9f);
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int PaddingId = Shader.PropertyToID("_Padding");
        public Text _nameText;
        public Text _descText;
        public Button _leftArrow;
        public Button _rightArrow;
        public Button _confirmButton;
        public Button _continueButton;
        public Button _backButton;
        public Image _portraitImage;
        [SerializeField] private RecipeReadonlyBookView _recipeReadonlyBookView;
        [SerializeField] private Button _recipeViewButton;
        [SerializeField] private Image _recipeViewGlow;
        [SerializeField] private FoodTipsView _foodTipsPrefab;

        private readonly List<Image> _dots = new();
        private IReadOnlyList<cfg.Character> _characters = Array.Empty<cfg.Character>();
        private IReadOnlyList<string> _recipePreviewDishIds =
            Array.Empty<string>();
        private GameplayDatabase _database;
        private Material _recipeViewGlowMaterial;
        private FoodTipsView _foodTipsView;
        private bool _recipeViewOpen;
        private Text _confirmLabel;
        private Vector2 _confirmButtonDefaultPosition;
        private int _index;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            EnsureReferences();
            CollectDots();
            _confirmButtonDefaultPosition =
                _confirmButton.GetComponent<RectTransform>().anchoredPosition;

            _leftArrow.onClick.AddListener(OnPrevClicked);
            _rightArrow.onClick.AddListener(OnNextClicked);
            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _continueButton.onClick.AddListener(OnContinueClicked);
            _backButton.onClick.AddListener(OnBackClicked);
            _recipeViewButton.onClick.AddListener(OnRecipeViewClicked);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            ReloadCharacters();
            ReloadGameplayDatabase();
            _index = 0;
            SetRecipeViewOpen(false);
            RefreshSaveEntryState();
            Refresh();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _foodTipsView?.Hide();
            SetRecipeViewOpen(false);
            base.OnClose(isShutdown, userData);
        }

        private void OnDestroy()
        {
            if (_recipeViewGlowMaterial != null)
            {
                Destroy(_recipeViewGlowMaterial);
                _recipeViewGlowMaterial = null;
            }
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
            if (RunPersistence.HasSave)
            {
                var dialogData = new ConfirmDialogData
                {
                    Title = "开始新游戏",
                    Message = "开始新游戏将会失去当前存档，是否继续？",
                    ConfirmText = "开始新游戏",
                    CancelText = "取消",
                    OnConfirm = () => ConfirmStartNewRun(character),
                };
                GameApp.UI.OpenUIForm(
                    UIForms.ConfirmDialog,
                    UIForms.GroupDialog,
                    dialogData);
                return;
            }

            StartNewRun(character);
        }

        private void OnContinueClicked()
        {
            if (!RunPersistence.HasSave)
            {
                RefreshSaveEntryState();
                return;
            }

            ShowBattleTransition(() =>
            {
                GameApp.UI.CloseUIForm(UIForm);
                GameplayEntryRequest.RequestContinue();
            });
        }

        private static void ConfirmStartNewRun(cfg.Character character)
        {
            RunPersistence.Delete();
            StartNewRun(character);
        }

        private static void StartNewRun(cfg.Character character)
        {
            ShowBattleTransition(() =>
            {
                Log.Info($"Selected character '{character.Id}', starting run.", Tag);
                GameplayEntryRequest.RequestNewRun(character.Id);
            });
        }

        private static void ShowBattleTransition(Action onCovered)
        {
            var data = new CartoonSceneTransitionData
            {
                TransitionType = CartoonTransitionType.Fade,
                Message = "",
                CoverDuration = 0.42f,
                HoldDuration = 0.2f,
                RevealDuration = 0.34f,
                OnCovered = onCovered,
                IsReadyToReveal = IsBattleReady,
            };

            CartoonSceneTransitionForm.Show(data);
        }

        private void RefreshSaveEntryState()
        {
            bool hasSave = RunPersistence.HasSave;
            _continueButton.gameObject.SetActive(hasSave);
            _confirmLabel.text = hasSave ? "开始新游戏" : "开始游戏";

            RectTransform confirmRect =
                _confirmButton.GetComponent<RectTransform>();
            confirmRect.anchoredPosition = _confirmButtonDefaultPosition
                + (hasSave
                    ? Vector2.right * ActionButtonHorizontalOffset
                    : Vector2.zero);

            RectTransform continueRect =
                _continueButton.GetComponent<RectTransform>();
            continueRect.anchoredPosition = _confirmButtonDefaultPosition
                + Vector2.left * ActionButtonHorizontalOffset;
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

        private void OnRecipeViewClicked()
        {
            if (_recipeViewOpen)
            {
                SetRecipeViewOpen(false);
                return;
            }

            OpenRecipeView();
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
                RefreshRecipePreview(null);
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
            RefreshRecipePreview(character);
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

        private void ReloadGameplayDatabase()
        {
            _database = null;
            cfg.Tables tables = GameApp.Config?.Tables;
            if (tables == null)
            {
                Log.Warning(
                    "Cannot build character recipe preview because config tables are unavailable.",
                    Tag);
                return;
            }

            try
            {
                _database = GameplayContentBuilder.BuildDatabase(tables);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    $"Cannot build character recipe preview: {exception.Message}",
                    Tag);
            }
        }

        private void RefreshRecipePreview(cfg.Character character)
        {
            if (_recipeReadonlyBookView == null)
            {
                SetRecipeViewAvailable(false);
                return;
            }

            if (character?.InitialRecipeId == null
                || character.InitialRecipeId.Count == 0
                || _database == null)
            {
                SetRecipeViewAvailable(false);
                return;
            }

            string recipeId = character.InitialRecipeId[0];
            RecipeDef recipe = _database.GetRecipe(recipeId);
            if (recipe == null)
            {
                Log.Warning(
                    $"Character '{character.Id}' references missing recipe '{recipeId}'.",
                    Tag);
                SetRecipeViewAvailable(false);
                return;
            }

            List<string> candidates =
                RecipeRoller.CollectPossibleDishIds(recipe);
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (_database.GetDish(candidates[i]) == null)
                {
                    Log.Warning(
                        $"Recipe '{recipeId}' references missing dish '{candidates[i]}'.",
                        Tag);
                    candidates.RemoveAt(i);
                }
            }

            if (candidates.Count == 0)
            {
                Log.Warning(
                    $"Recipe '{recipeId}' has no valid candidate dishes.",
                    Tag);
                SetRecipeViewAvailable(false);
                return;
            }

            _recipePreviewDishIds = candidates;
            SetRecipeViewAvailable(true);
            if (_recipeViewOpen)
            {
                OpenRecipeView();
            }
        }

        private void OpenRecipeView()
        {
            if (_database == null
                || _recipePreviewDishIds.Count == 0
                || _recipeReadonlyBookView == null)
            {
                SetRecipeViewOpen(false);
                return;
            }

            _recipeReadonlyBookView.OpenForReadonlyDishPool(
                _database,
                _recipePreviewDishIds,
                "可能获得的菜品",
                GetFoodTips);
            SetRecipeViewOpen(true);
        }

        private void SetRecipeViewAvailable(bool available)
        {
            if (_recipeViewButton != null)
            {
                _recipeViewButton.interactable = available;
            }

            if (!available)
            {
                _recipePreviewDishIds = Array.Empty<string>();
                SetRecipeViewOpen(false);
            }
        }

        private void SetRecipeViewOpen(bool open)
        {
            bool canOpen =
                _recipeViewButton != null
                && _recipeViewButton.interactable
                && _recipeReadonlyBookView != null;
            _recipeViewOpen = open && canOpen;

            if (_recipeReadonlyBookView != null)
            {
                _recipeReadonlyBookView.gameObject.SetActive(_recipeViewOpen);
            }

            if (!_recipeViewOpen)
            {
                _foodTipsView?.Hide();
            }

            SetRecipeViewGlow(_recipeViewOpen);
        }

        private FoodTipsView GetFoodTips()
        {
            if (_foodTipsView == null && _foodTipsPrefab != null)
            {
                _foodTipsView = Instantiate(
                    _foodTipsPrefab,
                    CachedTransform,
                    false);
                _foodTipsView.gameObject.name =
                    "FoodTipsView_Runtime";
                _foodTipsView.Hide();
            }

            if (_foodTipsView != null)
            {
                _foodTipsView.transform.SetAsLastSibling();
            }

            return _foodTipsView;
        }

        private void SetRecipeViewGlow(bool visible)
        {
            if (_recipeViewGlow == null)
            {
                return;
            }

            _recipeViewGlow.gameObject.SetActive(visible);
            _recipeViewGlow.color =
                visible ? RecipeButtonGlowColor : Color.clear;
            if (!visible)
            {
                return;
            }

            EnsureRecipeViewGlowMaterial();
            if (_recipeViewGlowMaterial == null)
            {
                return;
            }

            Rect rect = _recipeViewGlow.rectTransform.rect;
            _recipeViewGlowMaterial.SetVector(
                QuadSizeId,
                new Vector4(rect.width, rect.height, 0f, 0f));
            _recipeViewGlowMaterial.SetFloat(
                PaddingId,
                RecipeButtonGlowPadding);
        }

        private void EnsureRecipeViewGlowMaterial()
        {
            if (_recipeViewGlow == null || _recipeViewGlowMaterial != null)
            {
                return;
            }

            Material baseMaterial = _recipeViewGlow.material;
            if (baseMaterial == null
                || baseMaterial.shader == null
                || baseMaterial.shader.name != "GourmetProject/UIOuterGlow")
            {
                baseMaterial =
                    Resources.Load<Material>("Materials/UIOuterGlow");
            }

            if (baseMaterial == null)
            {
                Log.Warning(
                    "RecipeViewButton cannot load UIOuterGlow material.",
                    Tag);
                return;
            }

            _recipeViewGlowMaterial = new Material(baseMaterial);
            _recipeViewGlow.material = _recipeViewGlowMaterial;
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
            _recipeReadonlyBookView ??=
                FindOptionalComponentInChildren<RecipeReadonlyBookView>(
                    "RecipeReadonlyBookView");
            _nameText ??= FindRequiredComponentInChildren<Text>("CharacterName");
            _descText ??= FindRequiredComponentInChildren<Text>("CharacterDesc");
            _leftArrow ??= FindRequiredComponentInChildren<Button>("LeftArrow");
            _rightArrow ??= FindRequiredComponentInChildren<Button>("RightArrow");
            _confirmButton ??= FindRequiredComponentInChildren<Button>("ConfirmButton");
            _continueButton ??= FindRequiredComponentInChildren<Button>(
                "ContinueButton");
            _backButton ??= FindRequiredComponentInChildren<Button>(
                "BackButton",
                _recipeReadonlyBookView?.transform);
            _portraitImage ??= FindOptionalComponentInChildren<Image>("CharacterPortrait");
            _recipeViewButton ??=
                FindRequiredComponentInChildren<Button>("RecipeViewButton");
            _recipeViewGlow ??=
                FindOptionalComponentInChildren<Image>("TargetGlow");

            _confirmLabel = _confirmButton.transform.Find("Text")
                ?.GetComponent<Text>();
            if (_confirmLabel == null)
            {
                throw new MissingComponentException(
                    "CharacterSelectForm requires ConfirmButton/Text with component Text.");
            }
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

        private T FindRequiredComponentInChildren<T>(
            string childName,
            Transform excludedRoot = null) where T : Component
        {
            T component = FindOptionalComponentInChildren<T>(
                childName,
                excludedRoot);
            if (component != null)
            {
                return component;
            }

            throw new MissingComponentException($"CharacterSelectForm requires child '{childName}' with component {typeof(T).Name}.");
        }

        private T FindOptionalComponentInChildren<T>(
            string childName,
            Transform excludedRoot = null) where T : Component
        {
            foreach (Transform child in CachedTransform.GetComponentsInChildren<Transform>(true))
            {
                if (excludedRoot != null
                    && (child == excludedRoot
                        || child.IsChildOf(excludedRoot)))
                {
                    continue;
                }

                if (child.name == childName && child.TryGetComponent(out T component))
                {
                    return component;
                }
            }

            return null;
        }
    }
}
