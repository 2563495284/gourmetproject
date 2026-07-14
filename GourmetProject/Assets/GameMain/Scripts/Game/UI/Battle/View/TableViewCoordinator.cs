using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>进入查看餐桌态前对行动选择态中部的快照，用于返回时原样恢复标题/卡片/跳过按钮。</summary>
    internal readonly struct ActionSelectSnapshot
    {
        public readonly bool HasSnapshot;
        public readonly string Title;
        public readonly bool CardsActive;
        public readonly bool SkipActive;

        public ActionSelectSnapshot(string title, bool cardsActive, bool skipActive)
        {
            HasSnapshot = true;
            Title = title;
            CardsActive = cardsActive;
            SkipActive = skipActive;
        }

        public static ActionSelectSnapshot None => default;
    }

    /// <summary>供 <see cref="TableViewCoordinator"/> 回调壳的能力集合，UI 原语仍由 BattleForm 落地。</summary>
    internal interface ITableViewHost
    {
        GameplayView CurrentView { get; }

        GameRun Run { get; }

        BattleWorldController World { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void RestoreBattleWorld();

        void BindWorldHoverCallbacks();

        void PlayShowCardsWhenReady();

        void OpenTableEdit();

        ActionSelectSnapshot CaptureActionSelectSnapshot();

        void RestoreActionSelection(ActionSelectSnapshot snapshot);
    }

    /// <summary>
    /// 查看餐桌态的进入 / 返回编排：记录来源态并在返回时按来源分派（美食恢复世界、行动选择恢复卡片快照、
    /// 餐桌编辑续接碎片包等）。快照与返回决策集中于此，BattleForm 只提供 UI 原语。
    /// </summary>
    internal sealed class TableViewCoordinator
    {
        private readonly ITableViewHost _host;
        private GameplayView _returnView = GameplayView.None;
        private ActionSelectSnapshot _snapshot;

        public TableViewCoordinator(ITableViewHost host)
        {
            _host = host;
        }

        public bool IsActive => _host.CurrentView == GameplayView.TableView;

        public void Open()
        {
            BattleWorldController world = _host.World;
            if (world == null || _host.Run == null || !world.CanEnterTableView)
            {
                return;
            }

            _returnView = _host.CurrentView;
            _snapshot = _returnView == GameplayView.ActionSelect
                ? _host.CaptureActionSelectSnapshot()
                : ActionSelectSnapshot.None;
            _host.BindWorldHoverCallbacks();
            _host.SwitchTo(GameplayView.TableView, () =>
            {
                _host.BindWorldHoverCallbacks();
                world.BeginTableView(_host.Run);
                _host.BindWorldHoverCallbacks();
            });
        }

        public void Back()
        {
            GameplayView target = _returnView;
            if (target == GameplayView.None || target == GameplayView.TableView)
            {
                target = GameplayView.ActionSelect;
            }

            _returnView = GameplayView.None;
            BattleWorldController world = _host.World;
            world?.EndTableView();

            switch (target)
            {
                case GameplayView.Food:
                    _host.SwitchTo(GameplayView.Food, _host.RestoreBattleWorld);
                    break;
                case GameplayView.ActionSelect:
                    world?.HideWorld();
                    ActionSelectSnapshot snap = _snapshot;
                    _snapshot = ActionSelectSnapshot.None;
                    _host.SwitchTo(
                        GameplayView.ActionSelect,
                        () => _host.RestoreActionSelection(snap),
                        _host.PlayShowCardsWhenReady);
                    break;
                case GameplayView.TableEdit:
                    if (_host.Run != null && _host.Run.PendingFragmentPack.Count > 0)
                    {
                        _host.OpenTableEdit();
                    }
                    else
                    {
                        world?.HideWorld();
                        _host.SwitchTo(GameplayView.Shop);
                    }

                    break;
                default:
                    world?.HideWorld();
                    _host.SwitchTo(target);
                    break;
            }
        }
    }
}
