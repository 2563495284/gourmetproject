using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 主动道具使用 UI 协调器：槽位点击 -> 气泡 -> 可选目标选择 -> Apply -> 消耗。
    /// </summary>
    internal sealed class ActiveItemUseCoordinator
    {
        private readonly BattleForm _host;
        private readonly List<ActiveTarget> _selectedTargets = new List<ActiveTarget>();
        private readonly List<ActiveTarget> _candidateTargets = new List<ActiveTarget>();
        private readonly List<GameObject> _targetButtons = new List<GameObject>();

        private ActiveItemActionPopup _popup;
        private TargetArrowView _uiArrow;
        private WorldTargetArrow _worldArrow;
        private ActiveItemTargetOverlayView _targetOverlay;
        private RectTransform _targetPanel;
        private Text _targetPrompt;
        private Button _targetConfirmButton;
        private ItemDefinition _pendingItem;
        private IActiveUseContext _pendingContext;
        private RunItemSlotView _pendingSlot;
        private Vector2 _pendingStartScreen;
        private int _targetFrame;
        private bool _recipePanelTargeting;
        private bool _tableCellTargeting;
        private bool _timelineAxisTargeting;
        private bool _cursorStateCaptured;
        private bool _previousCursorVisible;
        private CursorLockMode _previousCursorLockMode;

        public ActiveItemUseCoordinator(BattleForm host)
        {
            _host = host;
        }

        public bool IsRecipePanelTargeting => _recipePanelTargeting;

        public void Dispose()
        {
            CancelTargeting(showMessage: false);
            ClosePopup();
        }

        public void Update()
        {
            if (_pendingItem == null)
            {
                return;
            }

            if (ItemActiveUsage.RequiresFoodBattle(_pendingItem)
                && (_host.CurrentView != GameplayView.Food
                    || !_host.InBattle
                    || _host.ActiveSession == null
                    || _host.ActiveSession.IsSettled))
            {
                CancelTargeting(showMessage: false);
                return;
            }

            if (_timelineAxisTargeting && !IsTimelineAxisContextValid())
            {
                CancelTargeting(showMessage: false);
                return;
            }

            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                if (_recipePanelTargeting)
                {
                    _host.CancelActiveItemRecipeTarget();
                }
                else
                {
                    CancelTargeting();
                }

                return;
            }

            if (_recipePanelTargeting)
            {
                return;
            }

            if (IsWorldTargetKind(_pendingItem.TargetKind))
            {
                UpdateWorldTargeting();
            }
        }

        public void OpenActionPopup(string itemId, RunItemSlotView slot)
        {
            if (_recipePanelTargeting)
            {
                return;
            }

            ClosePopup();
            CancelTargeting(showMessage: false);

            GameRun run = _host.ActiveRun;
            ItemDefinition item = ItemDefinition.Get(GameApp.Config.Tables, itemId, cfg.ItemKind.Active);
            if (run == null || item == null || slot == null)
            {
                return;
            }

            slot.HideTip();
            ActiveUseContextKind contextKind = ResolveContextKind();
            bool canUse = CanUse(item, contextKind, out string reason);
            bool canDiscard = run.HasItem(itemId);
            Transform parent = _host.ActiveItemLayer;
            if (_host.ActiveItemPopupPrefab == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少主动道具弹窗 prefab。");
                return;
            }

            _popup = UnityEngine.Object.Instantiate(_host.ActiveItemPopupPrefab, parent);
            _popup.transform.SetAsLastSibling();
            _popup.Open(
                item,
                slot.IconScreenCenter(),
                canUse,
                reason,
                canDiscard,
                () => BeginUse(item, slot),
                () => Discard(item),
                () => _popup = null);
        }

        private void BeginUse(ItemDefinition item, RunItemSlotView slot)
        {
            GameRun run = _host.ActiveRun;
            if (run == null || item == null)
            {
                return;
            }

            ActiveUseContextKind contextKind = ResolveContextKind();
            if (!CanUse(item, contextKind, out string reason))
            {
                _host.ShowActiveItemMessage($"{item.Name}：{reason}");
                _host.RefreshAfterActiveItem(boardChanged: false);
                return;
            }

            IActiveUseContext ctx = CreateContext(contextKind);
            if (ctx == null)
            {
                _host.ShowActiveItemMessage($"{item.Name}：现在不能使用主动道具。");
                return;
            }

            if (!ItemActiveUsage.RequiresTarget(item))
            {
                ApplyAndConsume(
                    ctx,
                    item,
                    Array.Empty<ActiveTarget>(),
                    refreshActionContent: item.EffectType != ItemEffectTypes.ResetBossDebuff);
                return;
            }

            IReadOnlyList<ActiveTarget> targets = ctx.EnumerateTargets(item);
            if (targets == null || targets.Count == 0)
            {
                _host.ShowActiveItemMessage($"{item.Name}：没有可选目标。");
                return;
            }

            if (ShouldUseRecipePanelTargeting(item))
            {
                BeginRecipePanelTargeting(ctx, item, slot);
                return;
            }

            if (ShouldUseTableCellTargeting(item) && !CanUseCurrentBattleTableCellTargeting())
            {
                BeginTableCellTargeting(ctx, item, slot);
                return;
            }

            if (ShouldUseTimelineAxisTargeting(item))
            {
                BeginTimelineAxisTargeting(ctx, item, slot, targets);
                return;
            }

            BeginTargeting(ctx, item, slot, targets);
        }

        private void Discard(ItemDefinition item)
        {
            GameRun run = _host.ActiveRun;
            if (run == null || item == null)
            {
                return;
            }

            if (!run.RemoveItem(item.Id))
            {
                _host.ShowActiveItemMessage($"{item.Name}：没有可丢弃的道具。");
                _host.RefreshAfterActiveItem(boardChanged: false);
                return;
            }

            _host.ShowActiveItemMessage($"{item.Name}：已丢弃。");
            _host.RefreshAfterActiveItem(boardChanged: false);
        }

        private void BeginTargeting(IActiveUseContext ctx, ItemDefinition item, RunItemSlotView slot, IReadOnlyList<ActiveTarget> targets)
        {
            _recipePanelTargeting = false;
            _pendingContext = ctx;
            _pendingItem = item;
            _pendingSlot = slot;
            _pendingStartScreen = slot != null ? slot.IconScreenCenter() : Vector2.zero;
            _selectedTargets.Clear();
            _candidateTargets.Clear();
            _candidateTargets.AddRange(targets);
            _targetFrame = Time.frameCount;

            if (IsWorldTargetKind(item.TargetKind) && _host.CurrentView == GameplayView.Food)
            {
                BeginWorldTargeting();
            }
            else
            {
                BeginUiTargeting(targets);
            }
        }

        private void BeginRecipePanelTargeting(IActiveUseContext ctx, ItemDefinition item, RunItemSlotView slot)
        {
            _pendingContext = ctx;
            _pendingItem = item;
            _pendingSlot = slot;
            _pendingStartScreen = slot != null ? slot.IconScreenCenter() : Vector2.zero;
            _selectedTargets.Clear();
            _recipePanelTargeting = true;
            _targetFrame = Time.frameCount;
            _host.RefreshPersistentHud();
            _host.OpenActiveItemRecipeTarget(
                item,
                () => CancelTargeting(),
                CompleteRecipePanelTargeting,
                () =>
                {
                    if (_pendingItem == item && _recipePanelTargeting)
                    {
                        _host.ShowActiveItemMessage($"{item.Name}：选择菜品，右键或 Esc 取消。");
                    }
                });
        }

        private void BeginTableCellTargeting(IActiveUseContext ctx, ItemDefinition item, RunItemSlotView slot)
        {
            _recipePanelTargeting = false;
            _tableCellTargeting = true;
            _pendingContext = ctx;
            _pendingItem = item;
            _pendingSlot = slot;
            _pendingStartScreen = slot != null ? slot.IconScreenCenter() : Vector2.zero;
            _selectedTargets.Clear();
            _targetFrame = Time.frameCount;

            _host.OpenActiveItemTableCellTarget(() =>
            {
                if (_pendingItem == item && _tableCellTargeting)
                {
                    BeginWorldTargeting();
                    return;
                }

                _host.CloseActiveItemTableCellTarget();
            });
        }

        private void BeginTimelineAxisTargeting(
            IActiveUseContext ctx,
            ItemDefinition item,
            RunItemSlotView slot,
            IReadOnlyList<ActiveTarget> targets)
        {
            _recipePanelTargeting = false;
            _tableCellTargeting = false;
            _timelineAxisTargeting = true;
            _pendingContext = ctx;
            _pendingItem = item;
            _pendingSlot = slot;
            _pendingStartScreen = slot != null ? slot.IconScreenCenter() : Vector2.zero;
            _selectedTargets.Clear();
            _candidateTargets.Clear();
            _candidateTargets.AddRange(targets);
            _targetFrame = Time.frameCount;

            bool opened = _host.BeginActiveItemTimelineAxisTarget(
                item,
                targets,
                CompleteTimelineAxisTargeting,
                () => CancelTargeting());
            if (!opened)
            {
                CancelTargeting(showMessage: false);
                _host.ShowActiveItemMessage($"{item.Name}：行动轴上没有可选目标。");
                return;
            }

            CreateUiArrow();
            string instruction = ItemActiveUsage.IsTimelineAddEffect(item.EffectType)
                ? "指向当前或未来日期预览"
                : item.EffectType == ItemEffectTypes.TimelineDeleteNode
                    ? "指向红色节点"
                    : "指向高亮节点";
            _host.ShowActiveItemMessage(
                $"{item.Name}：{instruction}，单击立即使用；Esc 或右键取消。");
        }

        private void CompleteTimelineAxisTargeting(ActiveTarget target)
        {
            if (!_timelineAxisTargeting || _pendingContext == null || _pendingItem == null)
            {
                CancelTargeting(showMessage: false);
                return;
            }

            IActiveUseContext ctx = _pendingContext;
            ItemDefinition item = _pendingItem;
            CleanupTargeting();
            ApplyAndConsume(
                ctx,
                item,
                new[] { target },
                refreshActionContent: false);
        }

        private void CompleteRecipePanelTargeting(ActiveTarget target, Action onComplete)
        {
            if (_pendingContext == null || _pendingItem == null)
            {
                CancelTargeting(showMessage: false);
                onComplete?.Invoke();
                return;
            }

            IActiveUseContext ctx = _pendingContext;
            ItemDefinition item = _pendingItem;
            if (ShouldPlayRecipeFlavorApply(item, target))
            {
                CompleteRecipeFlavorTargeting(ctx, item, target, onComplete);
                return;
            }

            CleanupTargeting();
            ApplyAndConsume(ctx, item, new[] { target });
            onComplete?.Invoke();
        }

        private void CompleteRecipeFlavorTargeting(
            IActiveUseContext ctx,
            ItemDefinition item,
            ActiveTarget target,
            Action onComplete)
        {
            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(ctx, item, new[] { target });
            _host.ShowActiveItemMessage(result.Message);
            if (!result.Success)
            {
                CleanupTargeting();
                _host.RefreshAfterActiveItem(boardChanged: false);
                onComplete?.Invoke();
                return;
            }

            if (_host.ActiveRun?.UseActiveItem(item.Id) != true)
            {
                CleanupTargeting();
                _host.ShowActiveItemMessage($"{item.Name}：道具已失效。");
                _host.RefreshAfterActiveItem(result.BoardChanged);
                onComplete?.Invoke();
                return;
            }

            CleanupTargeting();

            bool animationStarted = _host.PlayActiveItemRecipeFlavorApplied(target, FinishRecipeFlavorTargeting);
            if (!animationStarted)
            {
                FinishRecipeFlavorTargeting();
            }

            void FinishRecipeFlavorTargeting()
            {
                _host.RefreshAfterActiveItem(
                    result.BoardChanged,
                    result.ActionChoicesChanged);
                onComplete?.Invoke();
            }
        }

        private void BeginWorldTargeting()
        {
            BattleWorldController world = _host.ActiveWorld;
            if (world == null)
            {
                CancelTargeting();
                return;
            }

            bool dimPlacedDishes = _pendingItem.EffectType == ItemEffectTypes.AddMaterial;
            world.BeginActiveItemWorldTargeting(dimPlacedDishes);
            CaptureAndHideCursor();
            _worldArrow = WorldTargetArrow.Create(
                world.ActiveTargetArrowPrefab,
                world.ActiveTargetRoot,
                world.ActiveTargetCellSize,
                world.WorldCamera,
                _pendingStartScreen);
            if (_worldArrow == null)
            {
                CancelTargeting();
                return;
            }

            _worldArrow.UpdateTo(Mouse.current != null ? Mouse.current.position.ReadValue() : _pendingStartScreen);
            _host.ShowActiveItemMessage($"{_pendingItem.Name}：选择目标，右键或 Esc 取消。");
        }

        private void UpdateWorldTargeting()
        {
            BattleWorldController world = _host.ActiveWorld;
            if (world == null || _pendingItem == null)
            {
                CancelTargeting();
                return;
            }

            Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : _pendingStartScreen;
            _worldArrow?.UpdateTo(pointer);

            bool pointerHasTarget = _pendingItem.TargetKind == cfg.ItemTargetKind.DiningTableCell
                ? world.TryPointerCellTarget(out ActiveTarget hovered)
                : world.TryPointerDishTarget(out hovered);
            bool hasHover = pointerHasTarget && ContainsTarget(_candidateTargets, hovered);
            ActiveTarget? hoverTarget = hasHover ? hovered : null;
            world.SetActiveItemTargetHighlights(
                _pendingItem.TargetKind,
                _candidateTargets,
                _selectedTargets,
                hoverTarget);
            _worldArrow?.SetTargetHighlighted(hasHover);

            if (Time.frameCount <= _targetFrame || !WorldInput.PrimaryPressedThisFrame)
            {
                return;
            }

            if (!hasHover)
            {
                CancelTargeting();
                return;
            }

            AddTarget(hovered);
        }

        private void BeginUiTargeting(IReadOnlyList<ActiveTarget> targets)
        {
            CreateTargetOverlay();
            CreateUiArrow();

            if (_targetPrompt != null)
            {
                _targetPrompt.text = $"{_pendingItem.Name}：选择目标（{RequiredTargetCount()} 个）";
            }

            foreach (ActiveTarget target in targets)
            {
                AddTargetButton(target);
            }

            RefreshConfirmButton();
        }

        private void AddTargetButton(ActiveTarget target)
        {
            if (_targetOverlay == null || _targetOverlay.TargetButtonPrefab == null || _targetPanel == null)
            {
                Debug.LogError("主动道具选目标遮罩缺少 TargetButtonTemplate。");
                return;
            }

            Button button = UnityEngine.Object.Instantiate(_targetOverlay.TargetButtonPrefab, _targetPanel);
            button.gameObject.name = "TargetButton";
            button.gameObject.SetActive(true);
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = new Vector2(240f, 34f);
            button.onClick.AddListener(() => AddTarget(target));

            Text text = _targetOverlay.LabelOf(button);
            if (text != null)
            {
                text.text = TargetLabel(target);
            }

            _targetButtons.Add(button.gameObject);
        }

        private void AddTarget(ActiveTarget target)
        {
            if (ContainsTarget(_selectedTargets, target))
            {
                return;
            }

            _selectedTargets.Add(target);
            RefreshConfirmButton();

            if (_selectedTargets.Count >= RequiredTargetCount())
            {
                CompleteTargeting();
            }
        }

        private void CompleteTargeting()
        {
            if (_pendingContext == null || _pendingItem == null)
            {
                CancelTargeting(showMessage: false);
                return;
            }

            IActiveUseContext ctx = _pendingContext;
            ItemDefinition item = _pendingItem;
            ActiveTarget[] targets = _selectedTargets.ToArray();
            if (ShouldPlayDishFlavorApply(item, targets))
            {
                CompleteDishFlavorTargeting(ctx, item, targets);
                return;
            }

            if (ShouldPlayCellMaterialApply(item, targets))
            {
                CompleteCellMaterialTargeting(ctx, item, targets, closeTableCellTarget: _tableCellTargeting);
                return;
            }

            CleanupTargeting();
            ApplyAndConsume(ctx, item, targets);
        }

        private void CompleteDishFlavorTargeting(
            IActiveUseContext ctx,
            ItemDefinition item,
            ActiveTarget[] targets)
        {
            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(ctx, item, targets);
            _host.ShowActiveItemMessage(result.Message);
            if (!result.Success)
            {
                CleanupTargeting();
                _host.RefreshAfterActiveItem(boardChanged: false);
                return;
            }

            if (_host.ActiveRun?.UseActiveItem(item.Id) != true)
            {
                CleanupTargeting();
                _host.ShowActiveItemMessage($"{item.Name}：道具已失效。");
                _host.RefreshAfterActiveItem(result.BoardChanged);
                return;
            }

            ActiveTarget target = targets[0];
            BattleWorldController world = _host.ActiveWorld;
            CleanupTargeting();

            bool animationStarted = world != null
                && world.PlayActiveItemDishFlavorApplied(target, FinishDishFlavorTargeting);
            if (!animationStarted)
            {
                FinishDishFlavorTargeting();
            }

            void FinishDishFlavorTargeting()
            {
                bool movedToTemporaryArea = int.TryParse(target.Id, out int dishId)
                    && _host.ActiveSession?.FindTemporaryAreaDishById(dishId) != null;
                _host.RefreshAfterActiveItem(
                    result.BoardChanged && !movedToTemporaryArea,
                    result.ActionChoicesChanged);
            }
        }

        private void CompleteCellMaterialTargeting(
            IActiveUseContext ctx,
            ItemDefinition item,
            ActiveTarget[] targets,
            bool closeTableCellTarget)
        {
            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(ctx, item, targets);
            _host.ShowActiveItemMessage(result.Message);
            if (!result.Success)
            {
                CleanupTargeting();
                if (closeTableCellTarget)
                {
                    _host.CloseActiveItemTableCellTarget();
                }

                _host.RefreshAfterActiveItem(boardChanged: false);
                return;
            }

            if (_host.ActiveRun?.UseActiveItem(item.Id) != true)
            {
                CleanupTargeting();
                _host.ShowActiveItemMessage($"{item.Name}：道具已失效。");
                _host.RefreshAfterActiveItem(result.BoardChanged);
                return;
            }

            CleanupTargeting();

            ActiveTarget target = targets.Length > 0 ? targets[0] : default;
            bool animationStarted = _host.ActiveWorld != null
                && target.TargetKind == cfg.ItemTargetKind.DiningTableCell
                && _host.ActiveWorld.PlayActiveItemCellMaterialApplied(new GridPos(target.X, target.Y), item.EffectParam, FinishTableCellTargeting);

            if (!animationStarted)
            {
                FinishTableCellTargeting();
            }

            void FinishTableCellTargeting()
            {
                if (closeTableCellTarget)
                {
                    _host.CloseActiveItemTableCellTarget();
                }

                _host.RefreshAfterActiveItem(
                    result.BoardChanged,
                    result.ActionChoicesChanged);
            }
        }

        private void CancelTargeting(bool showMessage = true)
        {
            string itemName = _pendingItem != null ? _pendingItem.Name : null;
            bool closeTableCellTarget = _tableCellTargeting;
            CleanupTargeting();
            if (closeTableCellTarget)
            {
                _host.CloseActiveItemTableCellTarget();
            }

            if (showMessage && !string.IsNullOrEmpty(itemName))
            {
                _host.ShowActiveItemMessage($"{itemName}：已取消。");
            }
        }

        private void CleanupTargeting()
        {
            bool closeTimelineAxisTarget = _timelineAxisTargeting;
            _pendingItem = null;
            _pendingContext = null;
            _pendingSlot = null;
            _recipePanelTargeting = false;
            _tableCellTargeting = false;
            _timelineAxisTargeting = false;
            _selectedTargets.Clear();
            _candidateTargets.Clear();
            _worldArrow?.Destroy();
            _worldArrow = null;
            RestoreCursorState();
            if (_uiArrow != null)
            {
                UnityEngine.Object.Destroy(_uiArrow.gameObject);
                _uiArrow = null;
            }

            if (_targetOverlay != null)
            {
                UnityEngine.Object.Destroy(_targetOverlay.gameObject);
                _targetOverlay = null;
                _targetPanel = null;
                _targetPrompt = null;
                _targetConfirmButton = null;
            }

            _targetButtons.Clear();
            _host.ActiveWorld?.EndActiveItemWorldTargeting();
            if (closeTimelineAxisTarget)
            {
                _host.EndActiveItemTimelineAxisTarget();
            }
        }

        private void ApplyAndConsume(
            IActiveUseContext ctx,
            ItemDefinition item,
            IReadOnlyList<ActiveTarget> targets,
            bool refreshActionContent = true)
        {
            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(ctx, item, targets);
            _host.ShowActiveItemMessage(result.Message);
            if (!result.Success)
            {
                _host.RefreshAfterActiveItem(
                    boardChanged: false,
                    refreshActionContent: refreshActionContent);
                return;
            }

            if (_host.ActiveRun?.UseActiveItem(item.Id) != true)
            {
                if (!string.IsNullOrEmpty(result.CreatedTimelineNodeId))
                {
                    ctx.DeleteTimelineNode(result.CreatedTimelineNodeId);
                }

                _host.ShowActiveItemMessage($"{item.Name}：道具已失效。");
                _host.RefreshAfterActiveItem(
                    result.BoardChanged,
                    result.ActionChoicesChanged,
                    refreshActionContent);
                return;
            }

            _host.RefreshAfterActiveItem(
                result.BoardChanged,
                result.ActionChoicesChanged,
                refreshActionContent);
        }

        private bool CanUse(ItemDefinition item, ActiveUseContextKind contextKind, out string reason)
        {
            reason = string.Empty;
            GameRun run = _host.ActiveRun;
            if (run == null || item == null || !run.HasItem(item.Id))
            {
                reason = "没有可用道具。";
                return false;
            }

            if (new ItemRuntime(run).BlocksActiveItems())
            {
                reason = "当前被动效果禁止使用主动道具。";
                return false;
            }

            if (ItemActiveUsage.IsTodoTimelineEffect(item.EffectType))
            {
                reason = "功能开发中：需要玩家在时间轴上指定位置/节点。";
                return false;
            }

            if (item.EffectType == ItemEffectTypes.RerollAction && !_host.IsDailyActionSelectionActive)
            {
                reason = "只能在日常行动选择时使用。";
                return false;
            }

            if ((ItemActiveUsage.IsTimelineAddEffect(item.EffectType)
                    || item.EffectType == ItemEffectTypes.TimelineDeleteNode)
                && contextKind == ActiveUseContextKind.ActionSelect
                && !_host.IsDailyActionSelectionActive)
            {
                reason = "时间轴节点卡期间不能使用。";
                return false;
            }

            if ((item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                    || item.EffectType == ItemEffectTypes.TimelineExecutePast)
                && contextKind == ActiveUseContextKind.ActionSelect
                && !_host.IsDailyActionSelectionActive)
            {
                reason = "时间轴节点卡期间不能使用加急单。";
                return false;
            }

            if (item.EffectType == ItemEffectTypes.ResetBossDebuff
                && TimelineService.GetNearestUntriggeredBossNode(run) == null)
            {
                reason = "没有可重掷的 Boss 节点。";
                return false;
            }

            if (contextKind == ActiveUseContextKind.Battle && (_host.ActiveSession == null || _host.ActiveSession.IsSettled))
            {
                reason = "战斗已经结束。";
                return false;
            }

            if (item.EffectType == ItemEffectTypes.HalfNextActionCost
                && contextKind == ActiveUseContextKind.Battle)
            {
                reason = "美食战斗中不能使用。";
                return false;
            }

            if (ItemActiveUsage.RequiresFoodBattle(item)
                && (_host.CurrentView != GameplayView.Food || !_host.InBattle))
            {
                reason = "只能在美食战斗中使用。";
                return false;
            }

            if (!ItemActiveUsage.CanUse(item, contextKind))
            {
                reason = "现在不是使用时机。";
                return false;
            }

            IActiveUseContext ctx = CreateContext(contextKind);
            if (ctx == null)
            {
                reason = "当前界面不能使用。";
                return false;
            }

            if (ItemActiveUsage.RequiresTarget(item) && ctx.EnumerateTargets(item).Count == 0)
            {
                reason = "没有可选目标。";
                return false;
            }

            return true;
        }

        private IActiveUseContext CreateContext(ActiveUseContextKind kind)
        {
            return kind switch
            {
                ActiveUseContextKind.Battle => new BattleUseContext(_host.ActiveSession, _host.ActiveRun),
                ActiveUseContextKind.ActionSelect => new ActionSelectUseContext(_host.ActiveRun, _host.ActiveLoop),
                ActiveUseContextKind.Shop => new ShopUseContext(_host.ActiveRun, _host.ActiveLoop),
                ActiveUseContextKind.Event => new ShopUseContext(_host.ActiveRun, _host.ActiveLoop, ActiveUseContextKind.Event),
                ActiveUseContextKind.Reward => new ShopUseContext(_host.ActiveRun, _host.ActiveLoop, ActiveUseContextKind.Reward),
                _ => null,
            };
        }

        private ActiveUseContextKind ResolveContextKind()
        {
            if (_host.CurrentView == GameplayView.Food && _host.InBattle)
            {
                return ActiveUseContextKind.Battle;
            }

            if (_host.CurrentView == GameplayView.TableView && _host.IsViewingBattleTable)
            {
                return ActiveUseContextKind.Battle;
            }

            return _host.CurrentView switch
            {
                GameplayView.Shop => ActiveUseContextKind.Shop,
                GameplayView.RecipeSelection => ActiveUseContextKind.Shop,
                GameplayView.TableEdit => ActiveUseContextKind.Shop,
                GameplayView.TableView => ActiveUseContextKind.Shop,
                GameplayView.ActionSelect => ActiveUseContextKind.ActionSelect,
                GameplayView.Event => ActiveUseContextKind.Event,
                _ => ActiveUseContextKind.Reward,
            };
        }

        private void ClosePopup()
        {
            if (_popup != null)
            {
                _popup.Close(invokeClose: false);
                _popup = null;
            }
        }

        private void CreateUiArrow()
        {
            Transform parent = _host.ActiveItemLayer;
            if (_host.ActiveItemTargetArrowPrefab == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少主动道具目标箭头 prefab。");
                return;
            }

            _uiArrow = UnityEngine.Object.Instantiate(_host.ActiveItemTargetArrowPrefab, parent);
            _uiArrow.transform.SetAsLastSibling();
            _uiArrow.SetupArrow(_pendingStartScreen);
        }

        private void CreateTargetOverlay()
        {
            Transform parent = _host.ActiveItemLayer;
            if (_host.ActiveItemTargetOverlayPrefab == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少主动道具选目标遮罩 prefab。");
                return;
            }

            _targetOverlay = UnityEngine.Object.Instantiate(_host.ActiveItemTargetOverlayPrefab, parent);
            _targetOverlay.StretchToParent();
            _targetOverlay.transform.SetAsLastSibling();
            _targetPanel = _targetOverlay.Panel;
            _targetPrompt = _targetOverlay.PromptText;
            _targetConfirmButton = _targetOverlay.ConfirmButton;
            _targetOverlay.ConfigureButtons(CompleteTargeting, () => CancelTargeting());
            _targetConfirmButton.gameObject.SetActive(RequiredTargetCount() > 1);
        }

        private void RefreshConfirmButton()
        {
            if (_targetConfirmButton != null)
            {
                _targetConfirmButton.interactable = _selectedTargets.Count >= RequiredTargetCount();
            }
        }

        private int RequiredTargetCount()
        {
            return Mathf.Max(1, _pendingItem != null ? _pendingItem.TargetCount : 1);
        }

        private string TargetLabel(ActiveTarget target)
        {
            GameRun run = _host.ActiveRun;
            if (_pendingItem != null
                && (_pendingItem.EffectType == ItemEffectTypes.TimelineExecuteFuture
                    || _pendingItem.EffectType == ItemEffectTypes.TimelineExecutePast))
            {
                cfg.TimelineNode node = TimelineService.GetNode(run, target.Id);
                cfg.GameAction action = TimelineService.NodeAction(run, node);
                return node != null
                    ? $"第 {node.Day} 天 · {action?.Name ?? node.ActionId}"
                    : target.Id;
            }

            switch (target.TargetKind)
            {
                case cfg.ItemTargetKind.RecipeDish:
                {
                    DishDef dish = run?.Database?.GetDish(target.Id);
                    return $"菜谱{target.X + 1}-{target.Y + 1} {dish?.Name ?? target.Id}";
                }
                case cfg.ItemTargetKind.DiningTableCell:
                    return $"餐桌格 ({target.X + 1},{target.Y + 1})";
                case cfg.ItemTargetKind.DiningTableDish:
                {
                    if (int.TryParse(target.Id, out int id))
                    {
                        DishDef dish = _host.ActiveSession?.FindDishById(id)?.Def;
                        return $"{dish?.Name ?? target.Id} ({target.X + 1},{target.Y + 1})";
                    }

                    return target.Id;
                }
                case cfg.ItemTargetKind.Material:
                    return run?.Database?.GetMaterial(target.Id)?.Name ?? target.Id;
                case cfg.ItemTargetKind.FlavorSlot:
                {
                    string flavor = string.IsNullOrEmpty(target.Id)
                        ? "空风味槽"
                        : run?.Database?.GetFlavor(target.Id)?.Name ?? target.Id;
                    return $"菜谱{target.X + 1}-{target.Y + 1} {flavor}";
                }
                default:
                    return target.Id;
            }
        }

        private static bool IsWorldTargetKind(cfg.ItemTargetKind kind)
        {
            return kind == cfg.ItemTargetKind.DiningTableCell || kind == cfg.ItemTargetKind.DiningTableDish;
        }

        private static bool ShouldUseRecipePanelTargeting(ItemDefinition item)
        {
            return item != null
                && item.TargetKind == cfg.ItemTargetKind.RecipeDish
                && item.EffectType == ItemEffectTypes.AddFlavor;
        }

        private static bool ShouldUseTableCellTargeting(ItemDefinition item)
        {
            return item != null
                && item.TargetKind == cfg.ItemTargetKind.DiningTableCell
                && item.EffectType == ItemEffectTypes.AddMaterial;
        }

        private static bool ShouldUseTimelineAxisTargeting(ItemDefinition item)
        {
            return item != null
                && (ItemActiveUsage.IsTimelineAddEffect(item.EffectType)
                    || item.EffectType == ItemEffectTypes.TimelineDeleteNode
                    || item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                    || item.EffectType == ItemEffectTypes.TimelineExecutePast);
        }

        private bool CanUseCurrentBattleTableCellTargeting()
        {
            return _host.CurrentView == GameplayView.Food && _host.InBattle;
        }

        private bool IsTimelineAxisContextValid()
        {
            return _host.CurrentView == GameplayView.Shop
                || _host.CurrentView == GameplayView.Event
                || (_host.CurrentView == GameplayView.ActionSelect && _host.IsDailyActionSelectionActive);
        }

        private static bool ShouldPlayCellMaterialApply(ItemDefinition item, IReadOnlyList<ActiveTarget> targets)
        {
            return ShouldUseTableCellTargeting(item)
                && targets != null
                && targets.Count > 0
                && targets[0].TargetKind == cfg.ItemTargetKind.DiningTableCell;
        }

        private static bool ShouldPlayDishFlavorApply(ItemDefinition item, IReadOnlyList<ActiveTarget> targets)
        {
            return item != null
                && item.EffectType == ItemEffectTypes.AddFlavor
                && item.TargetKind == cfg.ItemTargetKind.DiningTableDish
                && targets != null
                && targets.Count > 0
                && targets[0].TargetKind == cfg.ItemTargetKind.DiningTableDish;
        }

        private static bool ShouldPlayRecipeFlavorApply(ItemDefinition item, ActiveTarget target)
        {
            return ShouldUseRecipePanelTargeting(item)
                && target.TargetKind == cfg.ItemTargetKind.RecipeDish;
        }

        private static bool ContainsTarget(IReadOnlyList<ActiveTarget> targets, ActiveTarget candidate)
        {
            if (targets == null)
            {
                return false;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ActiveTarget target = targets[i];
                if (target.TargetKind == candidate.TargetKind
                    && target.Id == candidate.Id
                    && target.X == candidate.X
                    && target.Y == candidate.Y)
                {
                    return true;
                }
            }

            return false;
        }

        private void CaptureAndHideCursor()
        {
            if (!_cursorStateCaptured)
            {
                _cursorStateCaptured = true;
                _previousCursorVisible = Cursor.visible;
                _previousCursorLockMode = Cursor.lockState;
            }

            Cursor.visible = false;
        }

        private void RestoreCursorState()
        {
            if (!_cursorStateCaptured)
            {
                return;
            }

            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLockMode;
            _cursorStateCaptured = false;
        }
    }
}
