using System;
using System.Collections.Generic;
using System.Text;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 唯一菜谱页面：作为 <c>BattleForm</c> 中部内容区的复用状态视图。
    /// 承载普通查看、商店删除、事件删除和主动道具选菜流程。
    /// </summary>
    public sealed partial class RecipeReadonlyBookView : MonoBehaviour
    {
        private static Font s_defaultFont;

        [Header("Header")]
        [SerializeField] private Text _titleText;

        [Header("Books")]
        [SerializeField] private RectTransform _bookContainer;
        [SerializeField] private RecipeEditBookView _bookPrefab;
        [SerializeField] private RecipeEditDishView _dishPrefab;

        [Header("Back")]
        [SerializeField] private Button _backButton;

        [Header("Compare Popup Templates")]
        [SerializeField] private RectTransform _compareOverlayPrefab;
        [SerializeField] private Image _comparePanelPrefab;
        [SerializeField] private Image _compareCardPrefab;
        [SerializeField] private Text _compareTextPrefab;
        [SerializeField] private Button _compareButtonPrefab;

        [Header("Compare Popup Style")]
        [SerializeField] private Color _compareOverlayColor = new Color(0f, 0f, 0f, 0.58f);
        [SerializeField] private Color _comparePanelColor = new Color(0.96f, 0.91f, 0.82f, 1f);
        [SerializeField] private Color _compareCardColor = new Color(1f, 0.97f, 0.9f, 1f);

        private readonly List<GameObject> _spawned = new();
        private readonly List<RecipeEditDishView> _spawnedDishes = new();
        private readonly List<RecipeEditBookView> _spawnedBooks = new();
        private GameRun _run;
        private Action _onExit;
        private Action _onChanged;
        private Func<FoodTipsView> _getFoodTips;
        private RecipeEditDishView _hoveredTipDish;
        private GameObject _compareOverlay;
        private RecipeReadonlyBookStateMachine _stateMachine;
        private int _readonlyEntriesBookIndex = -1;
        private IReadOnlyList<RecipeBookSlot> _readonlyEntries;
        private bool _compareTemplateMissingReported;
        private bool _wired;

        private void Awake()
        {
            EnsureWired();
        }

        private void OnDisable()
        {
            _stateMachine?.Clear();
            ClearCompareOverlay();
            HideRecipeDishTips();
            ClearSpawned();
        }

        /// <summary>商店删除食物：点击食物后通过通用确认弹窗二次确认。</summary>
        public void OpenForShopDelete(
            GameRun run,
            Action onExit,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(run, RecipeReadonlyBookRequest.ShopDeleteDish(onExit, onChanged), getFoodTips);
        }

        /// <summary>只读查看单本菜谱：禁拖拽，菜品只响应悬停 tips。</summary>
        public void OpenForReadonlyBook(
            GameRun run,
            int bookIndex,
            Action onExit,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(run, RecipeReadonlyBookRequest.ReadonlyBook(bookIndex, onExit, onChanged), getFoodTips);
        }

        /// <summary>以主动道具选择态打开菜谱面板：禁用拖拽/删除，只允许点击菜品进入确认。</summary>
        public void OpenForActiveRecipeDish(
            GameRun run,
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget, Action> onTargetConfirmed,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(run, RecipeReadonlyBookRequest.ActiveItemTarget(item, onCancel, onTargetConfirmed, onChanged), getFoodTips);
        }

        public void OpenForEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged,
            Func<FoodTipsView> getFoodTips = null)
        {
            Open(run, RecipeReadonlyBookRequest.EventDeleteDish(title, onCancel, onTargetConfirmed, onChanged), getFoodTips);
        }

        internal void Open(GameRun run, RecipeReadonlyBookRequest request, Func<FoodTipsView> getFoodTips = null)
        {
            EnsureWired();
            _run = run;
            _onExit = request.OnExit;
            _onChanged = request.OnChanged;
            _getFoodTips = getFoodTips;
            _readonlyEntriesBookIndex = request.Mode == RecipeReadonlyBookMode.ReadonlyBook ? request.BookIndex : -1;
            _readonlyEntries = request.Mode == RecipeReadonlyBookMode.ReadonlyBook ? request.ReadonlyEntries : null;

            switch (request.Mode)
            {
                case RecipeReadonlyBookMode.ReadonlyBook:
                    _stateMachine.Switch(new ReadonlyRecipeBookState(request.BookIndex));
                    break;
                case RecipeReadonlyBookMode.ShopDeleteDish:
                    _stateMachine.Switch(new ShopDeleteDishState(request.OnExit));
                    break;
                case RecipeReadonlyBookMode.ActiveItemTarget:
                    _stateMachine.Switch(new ActiveRecipeDishSelectState(
                        request.Item,
                        request.OnCancel,
                        request.OnTargetConfirmed));
                    break;
                case RecipeReadonlyBookMode.EventDeleteDish:
                    _stateMachine.Switch(new EventRecipeDishDeleteState(
                        request.Title,
                        request.OnCancel,
                        target => request.OnTargetConfirmed?.Invoke(target, null)));
                    break;
            }
        }

        /// <summary>供外部（如金币变化）请求刷新当前工作区状态。</summary>
        public void Refresh()
        {
            if (_run != null && isActiveAndEnabled)
            {
                _stateMachine?.Refresh();
            }
        }

        public bool PlayActiveItemRecipeFlavorApplied(ActiveTarget target, Action onComplete)
        {
            if (target.TargetKind != cfg.ItemTargetKind.RecipeDish || _run == null)
            {
                return false;
            }

            RecipeEditDishView dish = FindDish(target.X, target.Y);
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            if (dish == null || def == null)
            {
                return false;
            }

            HideRecipeDishTips(dish);
            List<string> flavorIds = ComposeFlavorIds(def, slot.ExtraFlavorIds);
            dish.PlayFlavorTransform(def, flavorIds, onComplete);
            return true;
        }

        private void EnsureWired()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;
            _stateMachine ??= new RecipeReadonlyBookStateMachine(this);
            if (_titleText == null)
            {
                Transform title = transform.Find("Title");
                _titleText = title != null ? title.GetComponent<Text>() : null;
            }

            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(() => _stateMachine?.OnExitClicked());
            }
        }

        private void RebuildBooksForCurrentState()
        {
            ClearCompareOverlay();
            bool preserveScroll = _spawnedBooks.Count > 0 && _spawnedBooks[0] != null;
            Vector2 previousScroll = preserveScroll
                ? _spawnedBooks[0].NormalizedPosition
                : new Vector2(0f, 1f);
            ClearSpawned();
            RecipeReadonlyBookState state = _stateMachine?.Current;
            if (state == null || _run == null || _bookContainer == null || _bookPrefab == null || _dishPrefab == null)
            {
                return;
            }

            ConfigureBooksLayoutGroup();
            string title = state is ShopDeleteDishState
                ? $"删除食物　花费 {ShopService.DeleteCost(_run)} 金币"
                : state.PanelTitle;
            SetText(_titleText, title);

            SetButtonText(_backButton, state.ExitButtonText);

            const int i = 0;
            if (state.BookIndexFilter < 0 || state.BookIndexFilter == i)
            {
                RecipeEditBookView book = Instantiate(_bookPrefab, _bookContainer);
                book.gameObject.name = "RecipeWarehouse";
                StretchWarehouseToContainer(book);
                book.Bind(i, null);
                _spawned.Add(book.gameObject);
                _spawnedBooks.Add(book);

                RectTransform dishContainer = book.DishContainer;
                if (dishContainer != null)
                {
                    IReadOnlyList<RecipeBookSlot> entries = EntriesForBook(i);
                    for (int k = 0; k < entries.Count; k++)
                    {
                        RecipeBookSlot slot = entries[k];
                        string dishId = slot.DishId;
                        DishDef def = _run.Database.GetDish(dishId);
                        RecipeEditDishView dish = Instantiate(_dishPrefab, dishContainer);
                        dish.gameObject.name = $"RecipeDish_{i + 1}_{k + 1}";
                        dish.Bind(
                            DishName(GameApp.Config.Tables, dishId),
                            DishShapeText(dishId),
                            i,
                            k,
                            false,
                            state.CanClickDish ? OnRecipeDishClicked : null,
                            def,
                            null,
                            null,
                            ShowRecipeDishTips,
                            HideRecipeDishTips,
                            ComposeFlavorIds(def, slot.ExtraFlavorIds),
                            DishIconPreviewMode.Warehouse);
                        _spawned.Add(dish.gameObject);
                        _spawnedDishes.Add(dish);
                    }
                }

                book.ApplyImmediateLayout();
                book.SetNormalizedPosition(
                    preserveScroll ? previousScroll : new Vector2(0f, 1f));
            }

            _onChanged?.Invoke();
        }

        private IReadOnlyList<RecipeBookSlot> EntriesForBook(int bookIndex)
        {
            if (_readonlyEntries != null && _readonlyEntriesBookIndex == bookIndex)
            {
                return _readonlyEntries;
            }

            return _run != null && bookIndex == 0 ? _run.RecipeEntries : Array.Empty<RecipeBookSlot>();
        }

        private void ConfigureBooksLayoutGroup()
        {
            HorizontalLayoutGroup layout = _bookContainer.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            layout.enabled = false;
        }

        private void StretchWarehouseToContainer(RecipeEditBookView book)
        {
            if (book == null)
            {
                return;
            }

            RectTransform rect = (RectTransform)book.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void OnRecipeDishClicked(RecipeEditDishView dish)
        {
            _stateMachine?.Current?.OnDishClicked(this, dish);
        }

        private RecipeEditDishView FindDish(int bookIndex, int dishIndex)
        {
            for (int i = 0; i < _spawnedDishes.Count; i++)
            {
                RecipeEditDishView dish = _spawnedDishes[i];
                if (dish != null && dish.BookIndex == bookIndex && dish.DishIndex == dishIndex)
                {
                    return dish;
                }
            }

            return null;
        }

        private void ShowRecipeDishTips(RecipeEditDishView dish)
        {
            if (dish == null || _run == null)
            {
                return;
            }

            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips == null)
            {
                return;
            }

            RecipeBookSlot slot = RecipeSlot(new ActiveTarget(string.Empty, dish.BookIndex, dish.DishIndex, cfg.ItemTargetKind.RecipeDish));
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            if (slot == null || def == null)
            {
                return;
            }

            _hoveredTipDish = dish;
            tips.Bind(BuildRecipeDishTipsData(def, slot));
            tips.Show();
            tips.transform.SetAsLastSibling();
            tips.PlaceAroundRectTransform((RectTransform)dish.transform, GetComponentInParent<Canvas>());
        }

        private void HideRecipeDishTips(RecipeEditDishView dish = null)
        {
            if (dish != null && _hoveredTipDish != null && _hoveredTipDish != dish)
            {
                return;
            }

            _hoveredTipDish = null;
            FoodTipsView tips = _getFoodTips?.Invoke();
            if (tips != null)
            {
                tips.Hide();
            }
        }

        private bool TryBuildRecipeTarget(RecipeEditDishView dish, out ActiveTarget target)
        {
            target = default;
            if (_run == null || dish == null)
            {
                return false;
            }

            IReadOnlyList<RecipeBookSlot> entries = EntriesForBook(dish.BookIndex);
            if (dish.DishIndex < 0 || dish.DishIndex >= entries.Count)
            {
                return false;
            }

            RecipeBookSlot slot = entries[dish.DishIndex];
            target = new ActiveTarget(slot.DishId, dish.BookIndex, dish.DishIndex, cfg.ItemTargetKind.RecipeDish);
            return true;
        }

        private void ShowActiveItemCompare(ItemDefinition item, ActiveTarget target, Action onBack, Action onConfirm)
        {
            ClearCompareOverlay();
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            if (def == null)
            {
                return;
            }

            RectTransform overlay = CreateStretchChild("ActiveItemDishCompare_Runtime", transform);
            if (overlay == null)
            {
                return;
            }

            _compareOverlay = overlay.gameObject;
            Image overlayImage = _compareOverlay.GetComponent<Image>();
            if (overlayImage != null)
            {
                overlayImage.color = _compareOverlayColor;
                overlayImage.raycastTarget = true;
            }

            _compareOverlay.transform.SetAsLastSibling();

            Image panelImage = CreateImage(_comparePanelPrefab, "Panel", _compareOverlay.transform, new Vector2(960f, 520f));
            if (panelImage == null)
            {
                ClearCompareOverlay();
                return;
            }

            panelImage.color = _comparePanelColor;
            Transform panel = panelImage.transform;

            CreateText(
                "Title",
                panel,
                $"{item.Name}：确认目标菜品",
                new Vector2(0f, 216f),
                new Vector2(860f, 44f),
                24,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            CreateDishInfoCard(
                panel,
                "原菜品",
                BuildDishInfo(def, ComposeFlavorIds(def, slot.ExtraFlavorIds), slot),
                new Vector2(-260f, 28f));

            CreateText(
                "Arrow",
                panel,
                "=>",
                new Vector2(0f, 40f),
                new Vector2(92f, 60f),
                32,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            List<string> previewExtraFlavors = PreviewExtraFlavors(slot.ExtraFlavorIds, item.EffectParam);
            CreateDishInfoCard(
                panel,
                "使用后",
                BuildDishInfo(def, ComposeFlavorIds(def, previewExtraFlavors), slot),
                new Vector2(260f, 28f));

            CreateButton(
                "BackButton",
                panel,
                "返回",
                new Vector2(-260f, -214f),
                new Vector2(180f, 48f),
                onBack);

            CreateButton(
                "ConfirmButton",
                panel,
                "确认",
                new Vector2(260f, -214f),
                new Vector2(180f, 48f),
                () =>
                {
                    onConfirm?.Invoke();
                });
        }

        private void ShowEventDeleteConfirm(string title, ActiveTarget target, Action onCancel, Action<ActiveTarget> onConfirm)
        {
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            string dishName = def != null ? def.Name : target.Id;
            var data = new ConfirmDialogData
            {
                Title = string.IsNullOrWhiteSpace(title) ? "确认删除菜品" : title,
                Message = $"确定要从菜谱中删除「{dishName}」吗？",
                ConfirmText = "删除",
                CancelText = "返回",
                OnConfirm = () => onConfirm?.Invoke(target),
                OnCancel = onCancel,
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private void ShowShopDeleteConfirm(ActiveTarget target)
        {
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null ? null : _run.Database.GetDish(slot.DishId);
            if (slot == null || def == null)
            {
                return;
            }

            int cost = ShopService.DeleteCost(_run);
            var data = new ConfirmDialogData
            {
                Title = "确认删除食物",
                Message = $"花费 {cost} 金币，从菜谱中删除「{def.Name}」？",
                ConfirmText = $"删除 -{cost}",
                CancelText = "返回",
                OnConfirm = () =>
                {
                    if (!ShopService.DeleteDishAt(_run, target.Y))
                    {
                        RebuildBooksForCurrentState();
                        return;
                    }

                    _onChanged?.Invoke();
                    RebuildBooksForCurrentState();
                },
            };
            GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, data);
        }

        private RecipeBookSlot RecipeSlot(ActiveTarget target)
        {
            if (_run == null || target.X != 0)
            {
                return null;
            }

            IReadOnlyList<RecipeBookSlot> entries = EntriesForBook(target.X);
            return target.Y >= 0 && target.Y < entries.Count ? entries[target.Y] : null;
        }

        private void CreateDishInfoCard(Transform parent, string title, string info, Vector2 center)
        {
            Image image = CreateImage(_compareCardPrefab, title, parent, new Vector2(350f, 330f), center);
            if (image == null)
            {
                return;
            }

            image.color = _compareCardColor;
            Transform card = image.transform;

            CreateText(
                "Title",
                card,
                title,
                new Vector2(0f, 132f),
                new Vector2(310f, 36f),
                21,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);

            Text body = CreateText(
                "Body",
                card,
                info,
                new Vector2(0f, -28f),
                new Vector2(300f, 250f),
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            if (body != null)
            {
                body.horizontalOverflow = HorizontalWrapMode.Wrap;
                body.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        private string BuildDishInfo(DishDef def, IReadOnlyList<string> flavorIds, RecipeBookSlot slot)
        {
            var sb = new StringBuilder();
            sb.AppendLine(def.Name);
            float score = def.Deliciousness + (slot != null ? slot.ScoreFlatBonus : 0f);
            sb.AppendLine($"美味度 {score}　形状 {def.Shape.Width}x{def.Shape.Height}");
            if (slot != null && Math.Abs(slot.ScoreMultiplier - 1f) > 0.0001f)
            {
                sb.AppendLine($"倍率 x{slot.ScoreMultiplier:0.##}");
            }

            List<string> skillIds = ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> lines = DishInfoText.TagLines(skillIds, flavorIds, _run.Database, out List<string> terms);
            foreach (string line in lines)
            {
                sb.AppendLine(line);
            }

            string termText = DishInfoText.TermBlock(terms);
            if (!string.IsNullOrEmpty(termText))
            {
                sb.AppendLine();
                sb.Append(termText);
            }

            return sb.ToString().TrimEnd();
        }

        private FoodTipsData BuildRecipeDishTipsData(DishDef def, RecipeBookSlot slot)
        {
            List<string> skillIds = ComposeSkillIds(def, slot?.ExtraSkillIds);
            List<string> flavorIds = ComposeFlavorIds(def, slot?.ExtraFlavorIds);
            var skills = new List<FoodInfoEntry>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                if (skill != null)
                {
                    skills.Add(new FoodInfoEntry(skill.Name, skill.Desc));
                }
            }

            var flavorNames = new List<string>();
            var flavorDetails = new List<FoodInfoEntry>();
            foreach (string flavorId in flavorIds)
            {
                FlavorDef flavor = _run.Database.GetFlavor(flavorId);
                if (flavor == null)
                {
                    continue;
                }

                flavorNames.Add(flavor.Name);
                flavorDetails.Add(new FoodInfoEntry(flavor.Name, flavor.Desc));
            }

            var summary = new FoodSummaryTipsData(def.Name, skills, flavorNames);
            float multiplier = slot != null ? slot.ScoreMultiplier : 1f;
            float score = def.Deliciousness + (slot != null ? slot.ScoreFlatBonus : 0f);
            return new FoodTipsData(
                summary,
                new FoodScoreTipsData(score, multiplier),
                Array.Empty<FoodMaterialTipsEntry>(),
                flavorDetails,
                Array.Empty<FoodInfoEntry>(),
                BuildRecipeSpecialTags(skillIds));
        }

        private IReadOnlyList<FoodInfoEntry> BuildRecipeSpecialTags(IReadOnlyList<string> skillIds)
        {
            if (skillIds == null || _run?.Database == null)
            {
                return Array.Empty<FoodInfoEntry>();
            }

            var termIds = new List<string>();
            foreach (string skillId in skillIds)
            {
                SkillDef skill = _run.Database.GetSkill(skillId);
                AddUniqueRange(termIds, skill?.TermIds);
            }

            var tags = new List<FoodInfoEntry>(termIds.Count);
            foreach (string termId in termIds)
            {
                cfg.Term term = GameApp.Config?.Tables?.TbTerm?.GetOrDefault(termId);
                tags.Add(term != null
                    ? new FoodInfoEntry(term.Name, term.Desc)
                    : new FoodInfoEntry(termId, string.Empty));
            }

            return tags;
        }

        private static void AddUniqueRange(List<string> list, IReadOnlyList<string> values)
        {
            if (list == null || values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value) && !list.Contains(value))
                {
                    list.Add(value);
                }
            }
        }

        private static List<string> ComposeSkillIds(DishDef def, IReadOnlyList<string> extraSkillIds)
        {
            var ids = new List<string>();
            if (def?.SkillIds != null)
            {
                ids.AddRange(def.SkillIds);
            }

            if (extraSkillIds != null)
            {
                ids.AddRange(extraSkillIds);
            }

            return ids;
        }

        private static List<string> ComposeFlavorIds(DishDef def, IReadOnlyList<string> extraFlavorIds)
        {
            var ids = new List<string>();
            if (def != null && !string.IsNullOrEmpty(def.FlavorId))
            {
                ids.Add(def.FlavorId);
            }

            if (extraFlavorIds != null)
            {
                ids.AddRange(extraFlavorIds);
            }

            return ids;
        }

        private List<string> PreviewExtraFlavors(IReadOnlyList<string> current, string flavorId)
        {
            var ids = new List<string>();
            if (current != null)
            {
                ids.AddRange(current);
            }

            if (string.IsNullOrEmpty(flavorId))
            {
                return ids;
            }

            int flavorLimit = Math.Max(1, _run != null ? _run.FoodFlavorLimit : 1);
            if (ids.Count < flavorLimit)
            {
                ids.Add(flavorId);
            }
            else
            {
                ids[ids.Count - 1] = flavorId;
            }

            return ids;
        }

        private static string DishName(cfg.Tables tables, string dishId)
        {
            cfg.DishVariant variant = tables.TbDishVariant.GetOrDefault(dishId);
            if (variant != null)
            {
                cfg.DishBase baseDish = tables.TbDishBase.GetOrDefault(variant.BaseId);
                if (baseDish != null)
                {
                    return baseDish.Name;
                }
            }

            return dishId;
        }

        private string DishShapeText(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            return dish?.Shape == null ? string.Empty : $"{dish.Shape.Width}x{dish.Shape.Height}";
        }

        private void ClearSpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
            _spawnedDishes.Clear();
            _spawnedBooks.Clear();
        }

        private void ClearCompareOverlay()
        {
            if (_compareOverlay != null)
            {
                Destroy(_compareOverlay);
                _compareOverlay = null;
            }
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetButtonText(Button button, string value)
        {
            Text text = button != null ? button.GetComponentInChildren<Text>(true) : null;
            SetText(text, value);
        }

        private RectTransform CreateStretchChild(string name, Transform parent)
        {
            if (_compareOverlayPrefab == null)
            {
                ReportMissingCompareTemplate(nameof(_compareOverlayPrefab));
                return null;
            }

            RectTransform rect = Instantiate(_compareOverlayPrefab, parent, false);
            rect.gameObject.name = name;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.gameObject.SetActive(true);
            return rect;
        }

        private static void ConfigureRect(RectTransform rect, string name, Vector2 size, Vector2 anchoredPosition)
        {
            if (rect == null)
            {
                return;
            }

            rect.gameObject.name = name;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private Image CreateImage(
            Image prefab,
            string name,
            Transform parent,
            Vector2 size,
            Vector2 anchoredPosition = default)
        {
            if (prefab == null)
            {
                ReportMissingCompareTemplate(name);
                return null;
            }

            Image image = Instantiate(prefab, parent, false);
            ConfigureRect((RectTransform)image.transform, name, size, anchoredPosition);
            image.gameObject.SetActive(true);
            return image;
        }

        private Text CreateText(
            string name,
            Transform parent,
            string text,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            FontStyle style,
            TextAnchor alignment)
        {
            if (_compareTextPrefab == null)
            {
                ReportMissingCompareTemplate(nameof(_compareTextPrefab));
                return null;
            }

            Text label = Instantiate(_compareTextPrefab, parent, false);
            ConfigureRect((RectTransform)label.transform, name, size, anchoredPosition);
            if (label.font == null)
            {
                label.font = ResolveFont();
            }

            label.text = text ?? string.Empty;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = Color.black;
            label.raycastTarget = false;
            label.gameObject.SetActive(true);
            return label;
        }

        private Button CreateButton(
            string name,
            Transform parent,
            string label,
            Vector2 anchoredPosition,
            Vector2 size,
            Action onClick)
        {
            if (_compareButtonPrefab == null)
            {
                ReportMissingCompareTemplate(nameof(_compareButtonPrefab));
                return null;
            }

            Button button = Instantiate(_compareButtonPrefab, parent, false);
            ConfigureRect((RectTransform)button.transform, name, size, anchoredPosition);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
            if (button.targetGraphic == null)
            {
                button.targetGraphic = button.GetComponent<Image>();
            }

            SetButtonText(button, label);
            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                ConfigureRect((RectTransform)text.transform, "Label", size, Vector2.zero);
                if (text.font == null)
                {
                    text.font = ResolveFont();
                }

                text.fontSize = 20;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.black;
                text.raycastTarget = false;
                text.gameObject.SetActive(true);
            }

            button.gameObject.SetActive(true);
            return button;
        }

        private void ReportMissingCompareTemplate(string templateName)
        {
            if (_compareTemplateMissingReported)
            {
                return;
            }

            Debug.LogError($"{nameof(RecipeReadonlyBookView)} 缺少对比弹窗模板：{templateName}。", this);
            _compareTemplateMissingReported = true;
        }

        private static Font ResolveFont()
        {
            if (s_defaultFont == null)
            {
                s_defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (s_defaultFont == null)
                {
                    s_defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }

            return s_defaultFont;
        }
    }
}
