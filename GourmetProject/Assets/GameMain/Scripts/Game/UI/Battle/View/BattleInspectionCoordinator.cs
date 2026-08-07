using System;
using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.UI.Battle.View
{
    internal enum BattleInspectionView
    {
        None,
        Recipe,
        Table,
    }

    internal interface IBattleInspectionHost
    {
        GameRun Run { get; }

        BattleSession Session { get; }

        GameplayView CurrentView { get; }

        BattleWorldController World { get; }

        IBattleInspectionLayer InspectionLayer { get; }

        bool ActionAxisVisible { get; }

        void SetActionAxisVisible(bool visible);

        void BeginInspectionSource();

        void RestoreInspectionSource(GameplayView sourceView);

        void RestoreBattleWorld();

        void BindWorldHoverCallbacks();

        void RefreshPersistent();

        FoodTipsView FoodTips();
    }

    /// <summary>
    /// 菜谱/餐桌只读查看的独立覆盖层协调器。来源 GameplayView 始终不变；协调器仅冻结来源表现，
    /// 在自己的层内切换视图，并在关闭时恢复来源。
    /// </summary>
    internal sealed class BattleInspectionCoordinator
    {
        private readonly IBattleInspectionHost _host;
        private BattleInspectionView _view;
        private GameplayView _sourceView = GameplayView.None;
        private bool _sourceActionAxisVisible;
        private bool _tableTargeting;
        private bool _transitioning;

        public BattleInspectionCoordinator(IBattleInspectionHost host)
        {
            _host = host;
        }

        public BattleInspectionView View => _view;

        // 从冻结来源开始即视为活跃。这样淡入首帧或强制切页时也能完整恢复来源，
        // 不会因为视图尚未在动画交换点写入而遗留一个被冻结的 Center。
        public bool IsActive => _sourceView != GameplayView.None;

        public bool IsTableVisible => _view == BattleInspectionView.Table;

        public bool IsTableTargeting => IsTableVisible && _tableTargeting;

        public bool IsTransitioning => _transitioning || _host.InspectionLayer?.IsTransitioning == true;

        public void OpenRecipe(int bookIndex, bool useBattleRecipe = false)
        {
            if (_tableTargeting
                || IsTransitioning
                || _host.Run == null
                || bookIndex != 0
                || _host.InspectionLayer?.RecipeView == null)
            {
                return;
            }

            if (_view == BattleInspectionView.Recipe)
            {
                return;
            }

            if (!BeginSession())
            {
                return;
            }

            _transitioning = true;
            _host.InspectionLayer.TransitionTo(
                BattleInspectionView.Recipe,
                () =>
                {
                    EndTablePresentation();
                    _host.SetActionAxisVisible(_sourceActionAxisVisible);
                    _view = BattleInspectionView.Recipe;
                    IReadOnlyList<RecipeReadonlyDishEntry> entries = useBattleRecipe
                        ? BuildBattleReadonlyEntries(bookIndex)
                        : null;
                    _host.InspectionLayer.RecipeView.Open(
                        _host.Run,
                        RecipeReadonlyBookRequest.ReadonlyBook(
                            bookIndex,
                            Close,
                            _host.RefreshPersistent,
                            entries),
                        _host.FoodTips);
                },
                CompleteTransition);
        }

        public bool ShowPassiveRecipe(
            IReadOnlyList<RecipeReadonlyDishEntry> entries,
            Action onShown)
        {
            if (_tableTargeting
                || IsTransitioning
                || _host.Run == null
                || _host.InspectionLayer?.RecipeView == null)
            {
                return false;
            }

            if (_view == BattleInspectionView.Recipe)
            {
                BindPassiveRecipe(entries);
                _host.RefreshPersistent();
                onShown?.Invoke();
                return true;
            }

            if (!BeginSession())
            {
                return false;
            }

            _transitioning = true;
            _host.InspectionLayer.TransitionTo(
                BattleInspectionView.Recipe,
                () =>
                {
                    EndTablePresentation();
                    _host.SetActionAxisVisible(_sourceActionAxisVisible);
                    _view = BattleInspectionView.Recipe;
                    BindPassiveRecipe(entries);
                },
                () =>
                {
                    CompleteTransition();
                    onShown?.Invoke();
                });
            return true;
        }

        public void OpenTable()
        {
            if (_tableTargeting || IsTransitioning || _host.Run == null)
            {
                return;
            }

            if (_view == BattleInspectionView.Table)
            {
                return;
            }

            BattleWorldController world = _host.World;
            if (world == null || !world.CanEnterTableView || !BeginSession())
            {
                return;
            }

            _transitioning = true;
            _host.InspectionLayer.TransitionTo(
                BattleInspectionView.Table,
                () =>
                {
                    _host.SetActionAxisVisible(false);
                    _view = BattleInspectionView.Table;
                    _tableTargeting = false;
                    if (_host.InspectionLayer.TablePanel != null)
                    {
                        _host.InspectionLayer.TablePanel.gameObject.SetActive(true);
                    }

                    world.BeginTableView(_host.Run, SourceBattleTable());
                    _host.BindWorldHoverCallbacks();
                },
                CompleteTransition);
        }

        public bool ShowPassiveTable(GpTable table, Action onShown)
        {
            if (_tableTargeting
                || IsTransitioning
                || _host.Run == null
                || _host.World == null)
            {
                return false;
            }

            BattleWorldController world = _host.World;
            if (_view == BattleInspectionView.Table)
            {
                world.EndTableView();
                world.BeginTableView(_host.Run, table);
                _host.BindWorldHoverCallbacks();
                _host.RefreshPersistent();
                onShown?.Invoke();
                return true;
            }

            if (!world.CanEnterTableView || !BeginSession())
            {
                return false;
            }

            _transitioning = true;
            _host.InspectionLayer.TransitionTo(
                BattleInspectionView.Table,
                () =>
                {
                    _host.SetActionAxisVisible(false);
                    _view = BattleInspectionView.Table;
                    _tableTargeting = false;
                    if (_host.InspectionLayer.TablePanel != null)
                    {
                        _host.InspectionLayer.TablePanel.gameObject.SetActive(true);
                    }

                    world.BeginTableView(_host.Run, table);
                    _host.BindWorldHoverCallbacks();
                },
                () =>
                {
                    CompleteTransition();
                    onShown?.Invoke();
                });
            return true;
        }

        public bool OpenTableTargeting(Action onOpened)
        {
            if (IsTransitioning || _host.Run == null || _host.World == null)
            {
                return false;
            }

            BattleWorldController world = _host.World;
            if (_view != BattleInspectionView.Table && !world.CanEnterTableView)
            {
                return false;
            }

            if (!BeginSession())
            {
                return false;
            }

            if (_view == BattleInspectionView.Table)
            {
                _tableTargeting = true;
                _host.InspectionLayer.TablePanel?.gameObject.SetActive(false);
                world.BeginTableCellTargeting(_host.Run, SourceBattleTable());
                _host.BindWorldHoverCallbacks();
                _host.RefreshPersistent();
                onOpened?.Invoke();
                return true;
            }

            _transitioning = true;
            _host.InspectionLayer.TransitionTo(
                BattleInspectionView.Table,
                () =>
                {
                    _host.SetActionAxisVisible(false);
                    _view = BattleInspectionView.Table;
                    _tableTargeting = true;
                    _host.InspectionLayer.TablePanel?.gameObject.SetActive(false);
                    world.BeginTableCellTargeting(_host.Run, SourceBattleTable());
                    _host.BindWorldHoverCallbacks();
                },
                () =>
                {
                    CompleteTransition();
                    onOpened?.Invoke();
                });
            return true;
        }

        public void Close()
        {
            Close(null);
        }

        public void Close(Action onClosed)
        {
            if (!IsActive)
            {
                onClosed?.Invoke();
                return;
            }

            _transitioning = true;
            _host.InspectionLayer.Hide(() => FinishClose(onClosed));
        }

        public void ForceClose()
        {
            if (!IsActive)
            {
                _host.InspectionLayer?.ForceHide();
                return;
            }

            _host.InspectionLayer?.ForceHide();
            FinishClose(null);
        }

        private bool BeginSession()
        {
            if (IsActive)
            {
                return true;
            }

            if (_host.CurrentView == GameplayView.None || _host.InspectionLayer == null)
            {
                return false;
            }

            _sourceView = _host.CurrentView;
            _sourceActionAxisVisible = _host.ActionAxisVisible;
            _host.BeginInspectionSource();
            if (_sourceView == GameplayView.Food)
            {
                _host.World?.SuspendWorld();
            }

            return true;
        }

        private void FinishClose(Action onClosed)
        {
            EndTablePresentation();
            if (_sourceView == GameplayView.Food)
            {
                _host.RestoreBattleWorld();
            }
            else
            {
                _host.World?.SuspendWorld();
            }

            GameplayView sourceView = _sourceView;
            bool sourceAxisVisible = _sourceActionAxisVisible;
            _view = BattleInspectionView.None;
            _sourceView = GameplayView.None;
            _sourceActionAxisVisible = false;
            _tableTargeting = false;
            _transitioning = false;
            _host.SetActionAxisVisible(sourceAxisVisible);
            _host.RestoreInspectionSource(sourceView);
            _host.RefreshPersistent();
            onClosed?.Invoke();
        }

        private void EndTablePresentation()
        {
            if (_view == BattleInspectionView.Table)
            {
                _host.World?.EndTableView();
            }

            _tableTargeting = false;
        }

        private void CompleteTransition()
        {
            _transitioning = false;
            _host.RefreshPersistent();
        }

        private void BindPassiveRecipe(IReadOnlyList<RecipeReadonlyDishEntry> entries)
        {
            _host.InspectionLayer.RecipeView.Open(
                _host.Run,
                RecipeReadonlyBookRequest.ReadonlyBook(
                    0,
                    Close,
                    _host.RefreshPersistent,
                    entries),
                _host.FoodTips);
        }

        private GpTable SourceBattleTable()
        {
            return _sourceView == GameplayView.Food ? _host.Session?.DiningTable : null;
        }

        private IReadOnlyList<RecipeReadonlyDishEntry> BuildBattleReadonlyEntries(int bookIndex)
        {
            BattleSession session = _host.Session;
            if (session == null || bookIndex < 0 || bookIndex >= session.Slots.Count)
            {
                return null;
            }

            IReadOnlyList<BattleRecipeEntrySnapshot> source = session.GetBattleRecipeEntries(bookIndex);
            var entries = new List<RecipeReadonlyDishEntry>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                BattleRecipeEntrySnapshot entry = source[i];
                if (entry == null)
                {
                    continue;
                }

                var slot = new RecipeBookSlot(entry.DishId);
                AddRange(flavorId => slot.AddFlavor(flavorId), entry.ExtraFlavorIds);
                AddRange(slot.AddExtraSkill, entry.ExtraSkillIds);
                slot.RestoreScoreFlatBonus(entry.ScoreFlatBonus);
                slot.RestoreScoreMultiplier(entry.ScoreMultiplier);
                entries.Add(new RecipeReadonlyDishEntry(
                    slot,
                    entry.Status,
                    entry.SkillsDisabled,
                    entry.ExcludedFromScore));
            }

            return entries;
        }

        private static void AddRange(Action<string> add, IReadOnlyList<string> values)
        {
            if (add == null || values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                add(values[i]);
            }
        }
    }
}
