using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>进入查看胃态前对行动选择态中部的快照，用于返回时原样恢复标题/卡片/跳过按钮。</summary>
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

    /// <summary>供 <see cref="StomachViewCoordinator"/> 回调壳的能力集合，UI 原语仍由 BattleForm 落地。</summary>
    internal interface IStomachViewHost
    {
        GameplayView CurrentView { get; }

        GameRun Run { get; }

        BattleWorldController World { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void RestoreBattleWorld();

        void PlayShowCardsWhenReady();

        void OpenBoardEdit();

        ActionSelectSnapshot CaptureActionSelectSnapshot();

        void RestoreActionSelection(ActionSelectSnapshot snapshot);
    }

    /// <summary>
    /// 查看胃态的进入 / 返回编排：记录来源态并在返回时按来源分派（美食恢复世界、行动选择恢复卡片快照、
    /// 棋盘编辑续接碎片包等）。快照与返回决策集中于此，BattleForm 只提供 UI 原语。
    /// </summary>
    internal sealed class StomachViewCoordinator
    {
        private readonly IStomachViewHost _host;
        private GameplayView _returnView = GameplayView.None;
        private ActionSelectSnapshot _snapshot;

        public StomachViewCoordinator(IStomachViewHost host)
        {
            _host = host;
        }

        public bool IsActive => _host.CurrentView == GameplayView.StomachView;

        public void Open()
        {
            BattleWorldController world = _host.World;
            if (world == null || _host.Run == null || !world.CanEnterStomachView)
            {
                return;
            }

            _returnView = _host.CurrentView;
            _snapshot = _returnView == GameplayView.ActionSelect
                ? _host.CaptureActionSelectSnapshot()
                : ActionSelectSnapshot.None;
            _host.SwitchTo(GameplayView.StomachView, () => world.BeginStomachView(_host.Run));
        }

        public void Back()
        {
            GameplayView target = _returnView;
            if (target == GameplayView.None || target == GameplayView.StomachView)
            {
                target = GameplayView.ActionSelect;
            }

            _returnView = GameplayView.None;
            BattleWorldController world = _host.World;
            world?.EndStomachView();

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
                case GameplayView.BoardEdit:
                    if (_host.Run != null && _host.Run.HasPendingFragmentPack)
                    {
                        _host.OpenBoardEdit();
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
