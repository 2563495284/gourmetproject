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

        public ActiveItemUseCoordinator(BattleForm host)
        {
            _host = host;
        }

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

            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                CancelTargeting();
                return;
            }

            if (IsWorldTargetKind(_pendingItem.TargetKind))
            {
                UpdateWorldTargeting();
            }
        }

        public void OpenActionPopup(string itemId, RunItemSlotView slot)
        {
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
                _host.RefreshAfterActiveItem(boardChanged: false, persist: false);
                return;
            }

            IActiveUseContext ctx = CreateContext(contextKind);
            if (ctx == null)
            {
                _host.ShowActiveItemMessage($"{item.Name}：现在不能使用主动道具。");
                return;
            }

            if (!ItemActiveUsage.RequiresTarget(item.TargetKind))
            {
                ApplyAndConsume(ctx, item, Array.Empty<ActiveTarget>());
                return;
            }

            IReadOnlyList<ActiveTarget> targets = ctx.EnumerateTargets(item.TargetKind);
            if (targets == null || targets.Count == 0)
            {
                _host.ShowActiveItemMessage($"{item.Name}：没有可选目标。");
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
                _host.RefreshAfterActiveItem(boardChanged: false, persist: false);
                return;
            }

            bool persist = ResolveContextKind() != ActiveUseContextKind.Battle;
            _host.ShowActiveItemMessage($"{item.Name}：已丢弃。");
            _host.RefreshAfterActiveItem(boardChanged: false, persist);
        }

        private void BeginTargeting(IActiveUseContext ctx, ItemDefinition item, RunItemSlotView slot, IReadOnlyList<ActiveTarget> targets)
        {
            _pendingContext = ctx;
            _pendingItem = item;
            _pendingSlot = slot;
            _pendingStartScreen = slot != null ? slot.IconScreenCenter() : Vector2.zero;
            _selectedTargets.Clear();
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

        private void BeginWorldTargeting()
        {
            BattleWorldController world = _host.ActiveWorld;
            if (world == null)
            {
                CancelTargeting();
                return;
            }

            world.BeginActiveItemWorldTargeting();
            _worldArrow = WorldTargetArrow.Create(world.ActiveTargetArrowPrefab, world.ActiveTargetRoot, world.ActiveTargetCellSize);
            if (_worldArrow == null)
            {
                CancelTargeting();
                return;
            }

            _worldArrow.SetEndpoints(world.ScreenToWorld(_pendingStartScreen), world.ScreenToWorld(Mouse.current != null ? Mouse.current.position.ReadValue() : _pendingStartScreen));
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
            _worldArrow?.SetEndpoints(world.ScreenToWorld(_pendingStartScreen), world.ScreenToWorld(pointer));

            bool hasHover = _pendingItem.TargetKind == cfg.ItemTargetKind.DiningTableCell
                ? world.TryPointerCellTarget(out ActiveTarget hovered)
                : world.TryPointerDishTarget(out hovered);
            ActiveTarget? hoverTarget = hasHover ? hovered : null;
            world.SetActiveItemTargetHighlights(_pendingItem.TargetKind, _selectedTargets, hoverTarget);

            if (Time.frameCount <= _targetFrame || !WorldInput.PrimaryPressedThisFrame || !hasHover)
            {
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
            CleanupTargeting();
            ApplyAndConsume(ctx, item, targets);
        }

        private void CancelTargeting(bool showMessage = true)
        {
            string itemName = _pendingItem != null ? _pendingItem.Name : null;
            CleanupTargeting();
            if (showMessage && !string.IsNullOrEmpty(itemName))
            {
                _host.ShowActiveItemMessage($"{itemName}：已取消。");
            }
        }

        private void CleanupTargeting()
        {
            _pendingItem = null;
            _pendingContext = null;
            _pendingSlot = null;
            _selectedTargets.Clear();
            _worldArrow?.Destroy();
            _worldArrow = null;
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
        }

        private void ApplyAndConsume(IActiveUseContext ctx, ItemDefinition item, IReadOnlyList<ActiveTarget> targets)
        {
            ActiveItemUseResult result = ActiveItemEffectRegistry.Apply(ctx, item, targets);
            _host.ShowActiveItemMessage(result.Message);
            if (!result.Success)
            {
                _host.RefreshAfterActiveItem(boardChanged: false, persist: false);
                return;
            }

            _host.ActiveRun?.UseActiveItem(item.Id);
            _host.RefreshAfterActiveItem(result.BoardChanged, ctx.ContextKind != ActiveUseContextKind.Battle);
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

            if (contextKind == ActiveUseContextKind.Battle && (_host.ActiveSession == null || _host.ActiveSession.IsSettled))
            {
                reason = "战斗已经结束。";
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

            if (ItemActiveUsage.RequiresTarget(item.TargetKind) && ctx.EnumerateTargets(item.TargetKind).Count == 0)
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
                ActiveUseContextKind.Map => new MapUseContext(_host.ActiveRun, _host.ActiveLoop),
                ActiveUseContextKind.Shop => new ShopUseContext(_host.ActiveRun),
                _ => null,
            };
        }

        private ActiveUseContextKind ResolveContextKind()
        {
            if (_host.CurrentView == GameplayView.Food && _host.InBattle)
            {
                return ActiveUseContextKind.Battle;
            }

            return _host.CurrentView switch
            {
                GameplayView.Shop => ActiveUseContextKind.Shop,
                GameplayView.RecipeEdit => ActiveUseContextKind.Shop,
                GameplayView.TableEdit => ActiveUseContextKind.Shop,
                GameplayView.TableView => ActiveUseContextKind.Shop,
                GameplayView.ActionSelect => ActiveUseContextKind.Map,
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
    }
}
