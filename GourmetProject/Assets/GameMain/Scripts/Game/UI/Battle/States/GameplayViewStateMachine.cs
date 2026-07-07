using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.View;

namespace GourmetProject.Game.UI.Battle.States
{
    /// <summary>
    /// 中部内容区各态在「淡出完成后落地」时需要回调壳的能力集合。BattleForm 实现它，
    /// 各 <see cref="IGameplayViewState"/> 只透过这个接口驱动壳，不直接依赖 BattleForm 全量。
    /// </summary>
    internal interface IBattleViewHost
    {
        GameRun Run { get; }

        RecipeBooksPresenter Recipe { get; }

        /// <summary>按 view 落地常驻壳通用配置：面板显隐 / 行动轴 / 白底 / 菜谱抽屉态 / 刷新常驻信息。</summary>
        void ApplyShellForView(GameplayView view);

        void SetCenterTitle(string text);

        void RebuildActionAxis();

        void OpenShopPanel();

        void OpenRecipeEditPanel();

        void BuildBattleRecipe();

        void BuyRecipeBook();
    }

    /// <summary>中部单一态：淡出完成后由状态机调用 <see cref="Enter"/> 落地本态的特化构建。</summary>
    internal interface IGameplayViewState
    {
        GameplayView Kind { get; }

        void Enter(IBattleViewHost host, Action buildCenter);
    }

    /// <summary>
    /// 中部八态状态机：只负责把「淡出完成后的落地」派发给对应 <see cref="IGameplayViewState"/>。
    /// 淡入淡出调度与 <c>_current</c>/<c>_inBattle</c> 仍由 BattleForm.SwitchTo 持有，这里保持无状态、纯派发。
    /// </summary>
    internal sealed class GameplayViewStateMachine
    {
        private readonly IBattleViewHost _host;
        private readonly Dictionary<GameplayView, IGameplayViewState> _states;

        public GameplayViewStateMachine(IBattleViewHost host)
        {
            _host = host;
            _states = new Dictionary<GameplayView, IGameplayViewState>();
            Register(new ActionSelectState());
            Register(new ShopState());
            Register(new RecipeEditState());
            Register(new RewardDishPackState());
            Register(new FoodState());
            Register(new BoardEditState());
            Register(new StomachViewState());
        }

        private void Register(IGameplayViewState state)
        {
            _states[state.Kind] = state;
        }

        /// <summary>在中部淡出完成后落地某一态：先落地常驻壳通用配置，再执行该态的特化构建。</summary>
        public void Apply(GameplayView view, Action buildCenter)
        {
            if (_host.Run == null)
            {
                return;
            }

            _host.ApplyShellForView(view);
            if (_states.TryGetValue(view, out IGameplayViewState state))
            {
                state.Enter(_host, buildCenter);
            }
        }
    }
}
