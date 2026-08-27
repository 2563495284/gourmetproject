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
using TMPro;

namespace GourmetProject.Game.UI.Battle
{
    /// <summary>
    /// 消耗品使用 UI 协调器：槽位点击 -> 气泡 -> 可选目标选择 -> Apply -> 消耗。
    /// </summary>
    internal sealed class ActiveItemUseCoordinator
    {
        private readonly BattleForm _host;
        private readonly List<ActiveTarget> _selectedTargets = new List<ActiveTarget>();
        private readonly List<ActiveTarget> _candidateTargets = new List<ActiveTarget>();
        private readonly List<GameObject> _targetButtons = new List<GameObject>();

        private ActiveItemActionPopup _popup;
        private TargetArrowView _uiArrow;
        private ActiveItemTargetOverlayView _targetOverlay;
        private RectTransform _targetPanel;
        private TMP_Text _targetPrompt;
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

            if (_timelineAxisTargeting
                && _candidateTargets.Count == 0
                && Time.frameCount > _targetFrame
                && Mouse.current != null
                && Mouse.current.leftButton.wasPressedThisFrame)
            {
                CancelTargeting();
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
            bool interruptedRecipeTargeting = _recipePanelTargeting;
            if (interruptedRecipeTargeting)
            {
                _host.CancelActiveItemRecipeTarget();
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
            if (interruptedRecipeTargeting)
            {
                canUse = false;
                reason = "已取消当前目标选择，请重新点击使用。";
            }

            bool canDiscard = run.HasItem(itemId);
            Transform parent = _host.ActiveItemLayer;
            if (_host.ActiveItemPopupPrefab == null)
            {
                Debug.LogError($"{nameof(BattleForm)} 缺少消耗品弹窗 prefab。");
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
                _host.ShowActiveItemMessage($"{item.Name}：现在不能使用消耗品。");
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

            IReadOnlyList<ActiveTarget> targets =
                ctx.EnumerateTargets(item) ?? Array.Empty<ActiveTarget>();

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

            bool rewardContext = _host.IsRewardItemContextActive;
            if (!run.RemoveItem(item.Id))
            {
                _host.ShowActiveItemMessage($"{item.Name}：没有可丢弃的装饰品和消耗品。");
                return;
            }

            _host.ShowActiveItemMessage($"{item.Name}：已丢弃。");
            _host.RefreshPersistentHud();
            if (rewardContext)
            {
                _host.CommitRewardInventoryMutation();
            }
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
                        _host.ShowActiveItemMessage($"{item.Name}：选择食物，右键或 Esc 取消。");
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

            bool opened = _host.OpenActiveItemTableCellTarget(() =>
            {
                if (_pendingItem == item && _tableCellTargeting)
                {
                    BeginWorldTargeting();
                    return;
                }

                _host.CloseActiveItemTableCellTarget();
            });
            if (!opened)
            {
                CancelTargeting(showMessage: false);
                _host.ShowActiveItemMessage($"{item.Name}：当前不能选择餐桌格子。");
            }
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
                _host.ShowActiveItemMessage($"{item.Name}：时间轴上没有可选目标。");
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
            bool commitAddPreview = ItemActiveUsage.IsTimelineAddEffect(item.EffectType);
            if (!commitAddPreview)
            {
                CleanupTargeting();
            }

            ApplyAndConsume(
                ctx,
                item,
                new[] { target },
                refreshActionContent: false,
                commitTimelineAddPreview: commitAddPreview);
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

            if (_host.ActiveRun?.UseActiveItem(item.Id, ctx.ContextKind.ToString().ToLowerInvariant()) != true)
            {
                CleanupTargeting();
                _host.ShowActiveItemMessage($"{item.Name}：装饰品和消耗品已失效。");
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

            bool dimPlacedDishes = false;
            world.BeginActiveItemWorldTargeting(dimPlacedDishes);
            if (_pendingItem.TargetKind == cfg.ItemTargetKind.DiningTableDish)
            {
                _host.SetServingOutletActiveItemTargeting(true);
            }
            CaptureAndHideCursor();
            CreateUiArrow();
            if (_uiArrow == null)
            {
                CancelTargeting();
                return;
            }

            _uiArrow.SetEndScreenPoint(
                Mouse.current != null ? Mouse.current.position.ReadValue() : _pendingStartScreen);
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
            _uiArrow?.SetEndScreenPoint(pointer);

            ActiveTarget hovered = default;
            bool servingOutletHover = false;
            bool pointerHasTarget;
            if (_pendingItem.TargetKind == cfg.ItemTargetKind.DiningTableCell)
            {
                pointerHasTarget = world.TryPointerCellTarget(out hovered);
            }
            else
            {
                servingOutletHover = _host.TryServingOutletDishTarget(pointer, out hovered);
                pointerHasTarget = servingOutletHover || world.TryPointerDishTarget(out hovered);
            }

            bool hasHover = pointerHasTarget && ContainsTarget(_candidateTargets, hovered);
            ActiveTarget? hoverTarget = hasHover ? hovered : null;
            world.SetActiveItemTargetHighlights(
                _pendingItem.TargetKind,
                _candidateTargets,
                _selectedTargets,
                hoverTarget);
            _host.SetServingOutletActiveItemTargetHighlighted(
                hasHover && servingOutletHover);

            bool primaryPressed = Mouse.current != null
                && Mouse.current.leftButton.wasPressedThisFrame
                && (servingOutletHover || !WorldInput.PointerOverUi);
            if (Time.frameCount <= _targetFrame || !primaryPressed)
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
                Debug.LogError("消耗品选目标遮罩缺少 TargetButtonTemplate。");
                return;
            }

            Button button = UnityEngine.Object.Instantiate(_targetOverlay.TargetButtonPrefab, _targetPanel);
            button.gameObject.name = "TargetButton";
            button.gameObject.SetActive(true);
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = new Vector2(240f, 34f);
            button.onClick.AddListener(() => AddTarget(target, button));

            TMP_Text text = _targetOverlay.LabelOf(button);
            if (text != null)
            {
                text.text = TargetLabel(target);
            }

            _targetButtons.Add(button.gameObject);
        }

        private void AddTarget(ActiveTarget target, Button sourceButton = null)
        {
            if (ContainsTarget(_selectedTargets, target))
            {
                if (RequiredTargetCount() > 1)
                {
                    RemoveTarget(_selectedTargets, target);
                    SetTargetButtonSelected(sourceButton, false);
                    RefreshConfirmButton();
                    RefreshTargetPrompt();
                }
                return;
            }

            if (_selectedTargets.Count >= RequiredTargetCount())
            {
                return;
            }

            _selectedTargets.Add(target);
            SetTargetButtonSelected(sourceButton, true);
            RefreshConfirmButton();
            RefreshTargetPrompt();

            if (RequiredTargetCount() == 1)
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

            if (_selectedTargets.Count < RequiredTargetCount())
            {
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

            if (_host.ActiveRun?.UseActiveItem(item.Id, ctx.ContextKind.ToString().ToLowerInvariant()) != true)
            {
                CleanupTargeting();
                _host.ShowActiveItemMessage($"{item.Name}：装饰品和消耗品已失效。");
                _host.RefreshAfterActiveItem(result.BoardChanged);
                return;
            }

            ActiveTarget target = targets[0];
            BattleWorldController world = _host.ActiveWorld;
            bool servingOutletTarget = _host.IsServingOutletDishTarget(target);
            CleanupTargeting();

            bool animationStarted = servingOutletTarget
                ? _host.PlayActiveItemServingOutletFlavorApplied(
                    target,
                    FinishDishFlavorTargeting)
                : world != null
                    && world.PlayActiveItemDishFlavorApplied(
                        target,
                        FinishDishFlavorTargeting);
            if (!animationStarted)
            {
                FinishDishFlavorTargeting();
            }

            void FinishDishFlavorTargeting()
            {
                bool movedToTemporaryArea = int.TryParse(target.Id, out int dishId)
                    && _host.ActiveSession?.FindTemporaryAreaDishById(dishId) != null;
                if (movedToTemporaryArea)
                {
                    // The numb animation owns the table-to-temporary-area visual transition, so the
                    // regular board sync is intentionally skipped below. Resume automatic output
                    // explicitly once that transition has released the world's interaction lock.
                    world?.EnsureNextDishPrepared();
                }

                _host.RefreshAfterActiveItem(
                    result.BoardChanged && !movedToTemporaryArea,
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

        private void CleanupTargeting(bool closeTimelineAxisTarget = true)
        {
            closeTimelineAxisTarget &= _timelineAxisTargeting;
            _pendingItem = null;
            _pendingContext = null;
            _pendingSlot = null;
            _recipePanelTargeting = false;
            _tableCellTargeting = false;
            _timelineAxisTargeting = false;
            _selectedTargets.Clear();
            _candidateTargets.Clear();
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
            _host.SetServingOutletActiveItemTargetHighlighted(false);
            _host.SetServingOutletActiveItemTargeting(false);
            if (closeTimelineAxisTarget)
            {
                _host.EndActiveItemTimelineAxisTarget();
            }
        }

        private void ApplyAndConsume(
            IActiveUseContext ctx,
            ItemDefinition item,
            IReadOnlyList<ActiveTarget> targets,
            bool refreshActionContent = true,
            bool commitTimelineAddPreview = false)
        {
            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(ctx, item, targets);
            _host.ShowActiveItemMessage(result.Message);
            if (!result.Success)
            {
                if (commitTimelineAddPreview)
                {
                    CleanupTargeting();
                }

                _host.RefreshAfterActiveItem(
                    boardChanged: false,
                    refreshActionContent: refreshActionContent);
                return;
            }

            if (_host.ActiveRun?.UseActiveItem(item.Id, ctx.ContextKind.ToString().ToLowerInvariant()) != true)
            {
                if (!string.IsNullOrEmpty(result.CreatedTimelineNodeId))
                {
                    ctx.DeleteTimelineNode(result.CreatedTimelineNodeId);
                }

                if (commitTimelineAddPreview)
                {
                    CleanupTargeting();
                }

                _host.ShowActiveItemMessage($"{item.Name}：装饰品和消耗品已失效。");
                _host.RefreshAfterActiveItem(
                    result.BoardChanged,
                    result.ActionChoicesChanged,
                    refreshActionContent);
                return;
            }

            if (commitTimelineAddPreview)
            {
                bool committed = _host.CommitActiveItemTimelineAxisPreview(
                    result.CreatedTimelineNodeId);
                CleanupTargeting(closeTimelineAxisTarget: !committed);
            }

            _host.RefreshAfterActiveItem(
                result.BoardChanged,
                result.ActionChoicesChanged,
                refreshActionContent);
            if (!string.IsNullOrEmpty(result.PresentationNodeId))
            {
                _host.PlayActiveItemBossDebuffReroll(result);
            }

            if (ctx.ContextKind == ActiveUseContextKind.Reward)
            {
                _host.CommitRewardInventoryMutation();
            }
        }

        private bool CanUse(ItemDefinition item, ActiveUseContextKind contextKind, out string reason)
        {
            reason = string.Empty;
            GameRun run = _host.ActiveRun;
            if (!ItemActiveUsage.CanUse(run, item, contextKind, out reason)) return false;

            if (_host.IsTableFragmentEditActive)
            {
                reason = "餐桌碎片编辑期间不能使用消耗品。";
                return false;
            }

            if (_host.IsActiveItemUseBlocked)
            {
                reason = "当前奖励流程中不能使用消耗品。";
                return false;
            }

            if (ItemActiveUsage.IsTimelineAxisTargetEffect(item.EffectType)
                && !_host.IsActionAxisVisible)
            {
                reason = "只能在行动轴显示时使用。";
                return false;
            }

            if (item.EffectType == ItemEffectTypes.RerollAction
                && !_host.IsDailyActionSelectionActive)
            {
                reason = "只能在普通行动选择时使用。";
                return false;
            }

            if (item.EffectType == ItemEffectTypes.ResetBossDebuff
                && TimelineService.GetNearestUntriggeredBossNode(run) == null)
            {
                reason = "没有可重掷的 星级评鉴节点。";
                return false;
            }

            if (contextKind == ActiveUseContextKind.Battle && (_host.ActiveSession == null || _host.ActiveSession.IsSettled))
            {
                reason = "经营挑战已经结束。";
                return false;
            }

            if (ItemActiveUsage.RequiresFoodBattle(item)
                && (_host.CurrentView != GameplayView.Food || !_host.InBattle))
            {
                reason = "只能在经营挑战中使用。";
                return false;
            }

            IActiveUseContext ctx = CreateContext(contextKind);
            if (ctx == null)
            {
                reason = "当前界面不能使用。";
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
            return ResolveContextKind(
                _host.CurrentView,
                _host.InBattle,
                _host.IsRewardItemContextActive);
        }

        internal static ActiveUseContextKind ResolveContextKind(
            GameplayView currentView,
            bool inBattle,
            bool rewardContextActive)
        {
            if (rewardContextActive)
            {
                return ActiveUseContextKind.Reward;
            }

            if (currentView == GameplayView.Food && inBattle)
            {
                return ActiveUseContextKind.Battle;
            }

            return currentView switch
            {
                GameplayView.Shop => ActiveUseContextKind.Shop,
                GameplayView.RecipeSelection => ActiveUseContextKind.Shop,
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
                Debug.LogError($"{nameof(BattleForm)} 缺少消耗品目标箭头 prefab。");
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
                Debug.LogError($"{nameof(BattleForm)} 缺少消耗品选目标遮罩 prefab。");
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

        private void RefreshTargetPrompt()
        {
            if (_targetPrompt != null && _pendingItem != null)
            {
                _targetPrompt.text = $"{_pendingItem.Name}：已选 {_selectedTargets.Count}/{RequiredTargetCount()}";
            }
        }

        private static void SetTargetButtonSelected(Button button, bool selected)
        {
            if (button?.targetGraphic == null)
            {
                return;
            }

            button.targetGraphic.color = selected
                ? new Color(0.48f, 0.92f, 0.58f, 1f)
                : Color.white;
        }

        private static void RemoveTarget(List<ActiveTarget> targets, ActiveTarget candidate)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ActiveTarget target = targets[i];
                if (target.TargetKind == candidate.TargetKind
                    && target.Id == candidate.Id
                    && target.X == candidate.X
                    && target.Y == candidate.Y)
                {
                    targets.RemoveAt(i);
                    return;
                }
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
                    return $"食谱{target.X + 1}-{target.Y + 1} {dish?.Name ?? target.Id}";
                }
                case cfg.ItemTargetKind.DiningTableCell:
                    return $"餐桌格 ({target.X + 1},{target.Y + 1})";
                case cfg.ItemTargetKind.DiningTableDish:
                {
                    if (int.TryParse(target.Id, out int id))
                    {
                        DishInstance prepared = _host.ActiveSession?.PreparedServe?.Dish;
                        bool atServingOutlet = prepared != null && prepared.Id == id;
                        DishDef dish = atServingOutlet
                            ? prepared.Def
                            : _host.ActiveSession?.FindDishById(id)?.Def;
                        if (atServingOutlet)
                        {
                            return $"{dish?.Name ?? target.Id}（出餐口）";
                        }

                        return $"{dish?.Name ?? target.Id} ({target.X + 1},{target.Y + 1})";
                    }

                    return target.Id;
                }
                case cfg.ItemTargetKind.FlavorSlot:
                {
                    string flavor = string.IsNullOrEmpty(target.Id)
                        ? "空风味槽"
                        : run?.Database?.GetFlavor(target.Id)?.Name ?? target.Id;
                    return $"食谱{target.X + 1}-{target.Y + 1} {flavor}";
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
            return false;
        }

        private static bool ShouldUseTimelineAxisTargeting(ItemDefinition item)
        {
            return item != null
                && ItemActiveUsage.IsTimelineAxisTargetEffect(item.EffectType);
        }

        private bool CanUseCurrentBattleTableCellTargeting()
        {
            return _host.CurrentView == GameplayView.Food && _host.InBattle;
        }

        private bool IsTimelineAxisContextValid()
        {
            return _host.IsActionAxisVisible;
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
