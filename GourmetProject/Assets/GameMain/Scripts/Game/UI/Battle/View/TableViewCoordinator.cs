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

    /// <summary>
    /// 菜谱查看与餐桌查看共享的返回锚点。两个查看页互相替换时保留首次来源，
    /// 只有真正返回来源页或退出查看链时才清空。
    /// </summary>
    internal sealed class InspectionNavigationContext
    {
        public bool HasOrigin { get; private set; }

        public GameplayView ReturnView { get; private set; } = GameplayView.None;

        public ActionSelectSnapshot ActionSnapshot { get; private set; } = ActionSelectSnapshot.None;

        public void Capture(GameplayView current, ActionSelectSnapshot actionSnapshot)
        {
            if (HasOrigin || IsInspectionView(current))
            {
                return;
            }

            HasOrigin = true;
            ReturnView = current;
            ActionSnapshot = actionSnapshot;
        }

        public void Clear()
        {
            HasOrigin = false;
            ReturnView = GameplayView.None;
            ActionSnapshot = ActionSelectSnapshot.None;
        }

        public static bool IsInspectionView(GameplayView view)
        {
            return view == GameplayView.RecipeInspect || view == GameplayView.TableView;
        }
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
    /// 查看餐桌态的进入 / 返回编排：记录来源态并在返回时按来源分派（食物恢复世界、行动选择恢复卡片快照、
    /// 餐桌编辑续接碎片包等）。快照与返回决策集中于此，BattleForm 只提供 UI 原语。
    /// </summary>
    internal sealed class TableViewCoordinator
    {
        private readonly ITableViewHost _host;
        private readonly InspectionNavigationContext _navigation;
        private bool _transitioning;

        public TableViewCoordinator(ITableViewHost host, InspectionNavigationContext navigation)
        {
            _host = host;
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        }

        public bool IsActive => _host.CurrentView == GameplayView.TableView;

        public bool IsViewingBattleTable => IsActive && _navigation.ReturnView == GameplayView.Food;

        public bool IsTransitioning => _transitioning;

        public void CancelPendingTransition()
        {
            _transitioning = false;
        }

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
            GameplayView previous = _host.CurrentView;
            _navigation.Capture(
                previous,
                previous == GameplayView.ActionSelect
                ? _host.CaptureActionSelectSnapshot()
                : ActionSelectSnapshot.None);
            _host.BindWorldHoverCallbacks();
            _host.SwitchTo(GameplayView.TableView, () =>
            {
                _host.BindWorldHoverCallbacks();
                world.BeginTableView(_host.Run, SourceBattleTable());
                _host.BindWorldHoverCallbacks();
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

            GameplayView previous = _host.CurrentView;
            _navigation.Capture(
                previous,
                previous == GameplayView.ActionSelect
                ? _host.CaptureActionSelectSnapshot()
                : ActionSelectSnapshot.None);
            _transitioning = true;
            _host.BindWorldHoverCallbacks();
            _host.SwitchTo(GameplayView.TableView, () =>
            {
                _host.BindWorldHoverCallbacks();
                world.BeginTableCellTargeting(_host.Run, SourceBattleTable());
                _host.BindWorldHoverCallbacks();
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

            GameplayView target = _navigation.ReturnView;
            if (target == GameplayView.None || target == GameplayView.TableView)
            {
                target = GameplayView.ActionSelect;
            }

            _transitioning = true;
            CompleteBack(target, onBack);
        }

        private void CompleteBack(GameplayView target, Action onBack)
        {
            ActionSelectSnapshot snap = _navigation.ActionSnapshot;
            _navigation.Clear();

            switch (target)
            {
                case GameplayView.Food:
                    _host.SwitchTo(
                        GameplayView.Food,
                        _host.RestoreBattleWorld,
                        () => CompleteBackTransition(onBack));
                    break;
                case GameplayView.ActionSelect:
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
                        _host.SwitchTo(
                            GameplayView.Shop,
                            buildCenter: null,
                            onShown: () => CompleteBackTransition(onBack));
                    }

                    break;
                default:
                    _host.SwitchTo(
                        target,
                        buildCenter: null,
                        onShown: () => CompleteBackTransition(onBack));
                    break;
            }
        }

        /// <summary>从餐桌直接切到菜谱时，在目标页 swap 点结束餐桌，不恢复来源页。</summary>
        public Action BeginPeerExit()
        {
            if (_transitioning || !IsActive)
            {
                return null;
            }

            _transitioning = true;
            return () => _transitioning = false;
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
            return _navigation.ReturnView == GameplayView.Food ? _host.Session?.DiningTable : null;
        }
    }
}
