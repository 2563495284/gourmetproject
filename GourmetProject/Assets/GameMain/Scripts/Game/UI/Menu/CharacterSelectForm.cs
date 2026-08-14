using System;
using System.Collections.Generic;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Game.Tutorial;
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
using UnityEngine.Sprites;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;
using TMPro;

namespace GourmetProject.Game.UI.Menu
{
    /// <summary>
    /// 经营方向选择界面（仿杀戮尖塔）：左右箭头切换经营方向，翻页圆点指示当前位置，
    /// 根据存档状态提供继续游戏或选择经营方向开新局入口，「返回」回主菜单。
    /// 经营方向列表、名称、描述、立绘路径与开局经营方向 id 均来自 <c>TbCharacter</c>。
    /// </summary>
    public sealed class CharacterSelectForm : UGuiForm
    {
        private const string Tag = "CharacterSelect";
        private const float RecipeButtonGlowPadding = 24f;
        private const float RecipeButtonGlowRadius = 12f;
        private const float ActionButtonHorizontalOffset = 130f;

        private static readonly Color DotSelected = new(1f, 0.6f, 0.16f, 1f);
        private static readonly Color DotNormal = new(1f, 1f, 1f, 0.45f);
        private static readonly Color RecipeButtonGlowColor =
            new(0.25f, 1f, 0.35f, 0.75f);
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int ContentSizeId = Shader.PropertyToID("_ContentSize");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUVRect");
        private static readonly int GlowRadiusId = Shader.PropertyToID("_GlowRadius");
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _descText;
        [SerializeField] private Button _leftArrow;
        [SerializeField] private Button _rightArrow;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _backButton;
        [SerializeField] private TMP_Text _confirmLabel;
        [SerializeField] private Image _portraitImage;
        [SerializeField] private RecipeReadonlyBookView _recipeReadonlyBookView;
        [SerializeField] private Button _recipeViewButton;
        [SerializeField] private Image _recipeViewGlow;
        [SerializeField] private FoodTipsView _foodTipsPrefab;

        [SerializeField] private List<Image> _dots = new();
        private IReadOnlyList<cfg.Character> _characters = Array.Empty<cfg.Character>();
        private IReadOnlyList<string> _recipePreviewDishIds =
            Array.Empty<string>();
        private GameplayDatabase _database;
        private Material _recipeViewGlowMaterial;
        private FoodTipsView _foodTipsView;
        private RectTransform _tutorialTitleBanner;
        private RectTransform _tutorialCharacterName;
        private RectTransform _tutorialCharacterDescription;
        private RectTransform _tutorialButtonList;
        private bool _recipeViewOpen;
        private Vector2 _confirmButtonDefaultPosition;
        private int _index;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            ResolveTutorialAnchors();
            EnsureReferences();
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
            RegisterTutorialAnchors();
            PlayDirectionTutorialIfNeeded();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (string.Equals(TutorialRuntime.CurrentId, TutorialId.DirectionSelection, StringComparison.Ordinal))
            {
                TutorialRuntime.CloseForPageChange();
            }
            UnregisterTutorialAnchors();
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
                    ConfirmText = "是",
                    CancelText = "否",
                    OnConfirm = () => ConfirmStartNewRun(character),
                };
                GameApp.UI.OpenUIForm(
                    UIForms.ConfirmDialog,
                    UIForms.GroupDialog,
                    dialogData);
                return;
            }

            TutorialRuntime.Publish(TutorialSignal.DirectionConfirmed);
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
            if (GameRunContext.Current != null)
            {
                GameAnalyticsService.TrackRunEnded(
                    GameRunContext.Current,
                    "replaced",
                    isDeath: false);
            }
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
            _confirmLabel.text = "新游戏";

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

        private void RegisterTutorialAnchors()
        {
            TutorialAnchorRegistry.Register(TutorialAnchorId.Direction, _portraitImage?.rectTransform);
            TutorialAnchorRegistry.Register(TutorialAnchorId.DirectionTitle, _tutorialTitleBanner);
            TutorialAnchorRegistry.Register(TutorialAnchorId.DirectionName, _tutorialCharacterName);
            TutorialAnchorRegistry.Register(TutorialAnchorId.DirectionDescription, _tutorialCharacterDescription);
            TutorialAnchorRegistry.Register(TutorialAnchorId.DirectionButtons, _tutorialButtonList);
            TutorialAnchorRegistry.Register(
                TutorialAnchorId.DirectionStart,
                _confirmButton != null ? _confirmButton.transform as RectTransform : null);
        }

        private void UnregisterTutorialAnchors()
        {
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.Direction, _portraitImage?.rectTransform);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.DirectionTitle, _tutorialTitleBanner);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.DirectionName, _tutorialCharacterName);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.DirectionDescription, _tutorialCharacterDescription);
            TutorialAnchorRegistry.Unregister(TutorialAnchorId.DirectionButtons, _tutorialButtonList);
            TutorialAnchorRegistry.Unregister(
                TutorialAnchorId.DirectionStart,
                _confirmButton != null ? _confirmButton.transform as RectTransform : null);
        }

        private static void PlayDirectionTutorialIfNeeded()
        {
            if (RunPersistence.HasSave || TutorialProgressService.IsCompleted(TutorialId.DirectionSelection))
            {
                return;
            }

            GameSaveData save = GameSavePersistence.Load();
            if (!save.GuideProgress.CoreTutorialRunConsumed)
            {
                TutorialRuntime.Play(TutorialId.DirectionSelection);
            }
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
                "可能获得的食物",
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
                    UIForms.ResolveTooltipLayer(UIGroupTransform),
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

            Image sourceImage = _recipeViewButton?.targetGraphic as Image;
            Sprite sourceSprite = sourceImage != null ? sourceImage.sprite : null;
            _recipeViewGlow.sprite = sourceSprite;
            _recipeViewGlow.rectTransform.sizeDelta =
                Vector2.one * (RecipeButtonGlowPadding * 2f);
            _recipeViewGlow.gameObject.SetActive(visible && sourceSprite != null);
            _recipeViewGlow.color =
                visible && sourceSprite != null
                    ? RecipeButtonGlowColor
                    : Color.clear;
            if (!visible || sourceSprite == null)
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
            Vector2 contentSize = GetDisplayedSpriteSize(sourceImage);
            _recipeViewGlowMaterial.SetVector(
                ContentSizeId,
                new Vector4(contentSize.x, contentSize.y, 0f, 0f));

            Vector4 outerUv = DataUtility.GetOuterUV(sourceSprite);
            _recipeViewGlowMaterial.SetVector(
                SpriteUvRectId,
                new Vector4(
                    outerUv.x,
                    outerUv.y,
                    outerUv.z - outerUv.x,
                    outerUv.w - outerUv.y));
            _recipeViewGlowMaterial.SetFloat(
                GlowRadiusId,
                RecipeButtonGlowRadius);
        }

        private static Vector2 GetDisplayedSpriteSize(Image image)
        {
            Vector2 size = image.rectTransform.rect.size;
            Sprite sprite = image.sprite;
            if (!image.preserveAspect
                || sprite == null
                || size.x <= 0f
                || size.y <= 0f
                || sprite.rect.height <= 0f)
            {
                return size;
            }

            float spriteAspect = sprite.rect.width / sprite.rect.height;
            if (spriteAspect > size.x / size.y)
            {
                size.y = size.x / spriteAspect;
            }
            else
            {
                size.x = size.y * spriteAspect;
            }

            return size;
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
                || baseMaterial.shader.name
                    != "GourmetProject/UIAlphaConvolutionGlow")
            {
                baseMaterial =
                    Resources.Load<Material>(
                        "Materials/UIAlphaConvolutionGlow");
            }

            if (baseMaterial == null)
            {
                Log.Warning(
                    "RecipeViewButton cannot load UIAlphaConvolutionGlow material.",
                    Tag);
                return;
            }

            _recipeViewGlowMaterial = new Material(baseMaterial);
            _recipeViewGlow.material = _recipeViewGlowMaterial;
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
            RequireReference(_nameText, nameof(_nameText));
            RequireReference(_descText, nameof(_descText));
            RequireReference(_leftArrow, nameof(_leftArrow));
            RequireReference(_rightArrow, nameof(_rightArrow));
            RequireReference(_confirmButton, nameof(_confirmButton));
            RequireReference(_continueButton, nameof(_continueButton));
            RequireReference(_backButton, nameof(_backButton));
            RequireReference(_confirmLabel, nameof(_confirmLabel));
            RequireReference(
                _recipeReadonlyBookView,
                nameof(_recipeReadonlyBookView));
            RequireReference(_recipeViewButton, nameof(_recipeViewButton));
            RequireReference(_recipeViewGlow, nameof(_recipeViewGlow));
            RequireReference(_foodTipsPrefab, nameof(_foodTipsPrefab));
            RequireReference(_tutorialTitleBanner, nameof(_tutorialTitleBanner));
            RequireReference(_tutorialCharacterName, nameof(_tutorialCharacterName));
            RequireReference(_tutorialCharacterDescription, nameof(_tutorialCharacterDescription));
            RequireReference(_tutorialButtonList, nameof(_tutorialButtonList));

            if (_dots == null || _dots.Count == 0)
            {
                throw new MissingComponentException(
                    "CharacterSelectForm requires serialized page dot references.");
            }

            for (int i = 0; i < _dots.Count; i++)
            {
                RequireReference(_dots[i], $"{nameof(_dots)}[{i}]");
            }
        }

        private void ResolveTutorialAnchors()
        {
            const string designRoot = "BackgroundRoot/DesignRoot/";
            _tutorialTitleBanner = CachedTransform.Find(designRoot + "TitleBanner") as RectTransform;
            _tutorialCharacterName = CachedTransform.Find(designRoot + "CharacterName") as RectTransform;
            _tutorialCharacterDescription = CachedTransform.Find(designRoot + "CharacterDesc") as RectTransform;
            _tutorialButtonList = CachedTransform.Find(designRoot + "ButtonList") as RectTransform;
        }

        private static void RequireReference(
            UnityEngine.Object reference,
            string fieldName)
        {
            if (reference == null)
            {
                throw new MissingReferenceException(
                    $"CharacterSelectForm requires serialized reference '{fieldName}'.");
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

    }
}
