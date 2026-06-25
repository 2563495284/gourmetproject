using GourmetProject.Core.Rng;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 奖励生成所需的稳定上下文，集中传递以避免池服务直接读取 UI 状态。
    /// </summary>
    public readonly struct RewardContext
    {
        public RewardContext(
            cfg.Tables tables,
            GameRun run,
            cfg.Week week,
            cfg.RewardPackage package,
            IRandomStream rng,
            ActionExecutionContext actionContext = null)
        {
            Tables = tables;
            Run = run;
            Week = week;
            Package = package;
            Rng = rng;
            ActionContext = actionContext;
        }

        public cfg.Tables Tables { get; }

        public GameRun Run { get; }

        public cfg.Week Week { get; }

        public cfg.RewardPackage Package { get; }

        public IRandomStream Rng { get; }

        public ActionExecutionContext ActionContext { get; }

        public int RewardHiddenScore => DishHiddenScore;

        public int DishHiddenScore => Run == null ? Week?.RewardHiddenScore ?? 0 : HiddenScoreService.DishHiddenScore(Run, ActionContext);

        public int PassiveItemHiddenScore => Run == null ? Week?.RewardHiddenScore ?? 0 : HiddenScoreService.PassiveItemHiddenScore(Run, ActionContext);

        public int ActiveItemHiddenScore => Run == null ? 0 : HiddenScoreService.ActiveItemHiddenScore(Run, ActionContext);

        public int FragmentHiddenScore => Run == null ? Week?.RewardHiddenScore ?? 0 : HiddenScoreService.FragmentHiddenScore(Run, ActionContext);
    }
}
