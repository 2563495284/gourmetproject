using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>进入查看餐桌态前对行动选择态中部的快照，用于返回时原样恢复标题/卡片/跳过按钮。</summary>
    internal readonly struct ActionSelectSnapshot
    {
        public readonly bool HasSnapshot;
        public readonly bool CardsActive;

        public ActionSelectSnapshot(bool cardsActive)
        {
            HasSnapshot = true;
            CardsActive = cardsActive;
        }

        public static ActionSelectSnapshot None => default;
    }

    /// <summary>供 <see cref="TableViewCoordinator"/> 回调壳的能力集合，UI 原语仍由 BattleForm 落地。</summary>
    internal interface ITableViewHost
    {
        GameplayView CurrentView { get; }

        GameRun Run { get; }

        BattleSession Session { get; }

        BattleWorldController World { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void RestoreBattleWorld();

        void BindWorldHoverCallbacks();

        void PlayShowCardsWhenReady();

        void OpenTableEdit(Action onShown = null);

        ActionSelectSnapshot CaptureActionSelectSnapshot();

        void RestoreActionSelection(ActionSelectSnapshot snapshot);
    }

    /// <summary>
    /// 查看餐桌态的进入 / 返回编排：记录来源态并在返回时按来源分派（美食恢复世界、行动选择恢复卡片快照、
    /// 餐桌编辑续接碎片包等）。快照与返回决策集中于此，BattleForm 只提供 UI 原语。
    /// </summary>
    internal sealed class TableViewCoordinator
    {
        private const float TableViewFadeDuration = 0.16f;

        private readonly ITableViewHost _host;
        private GameplayView _returnView = GameplayView.None;
        private ActionSelectSnapshot _snapshot;
        private bool _transitioning;

        public TableViewCoordinator(ITableViewHost host)
        {
            _host = host;
        }

        public bool IsActive => _host.CurrentView == GameplayView.TableView;

        public bool IsViewingBattleTable => IsActive && _returnView == GameplayView.Food;

        public bool IsTransitioning => _transitioning;

        public void Open()
        {
            if (_transitioning)
            {
                return;
            }

            BattleWorldController world = _host.World;
            if (world == null || _host.Run == null || !world.CanEnterTableView)
            {
                return;
            }

            _transitioning = true;
            _returnView = _host.CurrentView;
            _snapshot = _returnView == GameplayView.ActionSelect
                ? _host.CaptureActionSelectSnapshot()
                : ActionSelectSnapshot.None;
            _host.BindWorldHoverCallbacks();
            _host.SwitchTo(GameplayView.TableView, () =>
            {
                _host.BindWorldHoverCallbacks();
                world.BeginTableView(_host.Run, SourceBattleTable());
                _host.BindWorldHoverCallbacks();
                world.FadeTableViewIn(TableViewFadeDuration);
            }, CompleteTransition);
        }

        public void OpenForCellTargeting(Action onOpened)
        {
            if (_transitioning)
            {
                onOpened?.Invoke();
                return;
            }

            BattleWorldController world = _host.World;
            if (world == null || _host.Run == null)
            {
                onOpened?.Invoke();
                return;
            }

            if (IsActive)
            {
                world.BeginTableCellTargeting(_host.Run, SourceBattleTable());
                _host.BindWorldHoverCallbacks();
                onOpened?.Invoke();
                return;
            }

            if (!world.CanEnterTableView)
            {
                onOpened?.Invoke();
                return;
            }

            _returnView = _host.CurrentView;
            _snapshot = _returnView == GameplayView.ActionSelect
                ? _host.CaptureActionSelectSnapshot()
                : ActionSelectSnapshot.None;
            _transitioning = true;
            _host.BindWorldHoverCallbacks();
            _host.SwitchTo(GameplayView.TableView, () =>
            {
                _host.BindWorldHoverCallbacks();
                world.BeginTableCellTargeting(_host.Run, SourceBattleTable());
                _host.BindWorldHoverCallbacks();
                world.FadeTableViewIn(TableViewFadeDuration);
            }, () =>
            {
                CompleteTransition();
                onOpened?.Invoke();
            });
        }

        public void Back(Action onBack = null)
        {
            if (_transitioning)
            {
                return;
            }

            GameplayView target = _returnView;
            if (target == GameplayView.None || target == GameplayView.TableView)
            {
                target = GameplayView.ActionSelect;
            }

            BattleWorldController world = _host.World;
            _transitioning = true;
            if (world == null)
            {
                CompleteBack(target, null, onBack);
                return;
            }

            world.FadeTableViewOut(TableViewFadeDuration, () => CompleteBack(target, world, onBack));
        }

        private void CompleteBack(GameplayView target, BattleWorldController world, Action onBack)
        {
            _returnView = GameplayView.None;
            world?.EndTableView();

            switch (target)
            {
                case GameplayView.Food:
                    _host.SwitchTo(
                        GameplayView.Food,
                        _host.RestoreBattleWorld,
                        () => CompleteBackTransition(onBack));
                    break;
                case GameplayView.ActionSelect:
                    world?.SuspendWorld();
                    ActionSelectSnapshot snap = _snapshot;
                    _snapshot = ActionSelectSnapshot.None;
                    _host.SwitchTo(
                        GameplayView.ActionSelect,
                        () => _host.RestoreActionSelection(snap),
                        () =>
                        {
                            _host.PlayShowCardsWhenReady();
                            CompleteBackTransition(onBack);
                        });
                    break;
                case GameplayView.TableEdit:
                    if (_host.Run != null && _host.Run.PendingFragmentPack.Count > 0)
                    {
                        _host.OpenTableEdit(() => CompleteBackTransition(onBack));
                    }
                    else
                    {
                        world?.SuspendWorld();
                        _host.SwitchTo(
                            GameplayView.Shop,
                            onShown: () => CompleteBackTransition(onBack));
                    }

                    break;
                default:
                    world?.SuspendWorld();
                    _host.SwitchTo(target, onShown: () => CompleteBackTransition(onBack));
                    break;
            }
        }

        private void CompleteBackTransition(Action onBack)
        {
            CompleteTransition();
            onBack?.Invoke();
        }

        private void CompleteTransition()
        {
            _transitioning = false;
        }

        private GpTable SourceBattleTable()
        {
            return _returnView == GameplayView.Food ? _host.Session?.DiningTable : null;
        }
    }
}
