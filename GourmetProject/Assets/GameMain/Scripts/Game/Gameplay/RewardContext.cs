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
            IRandomStream rng)
        {
            Tables = tables;
            Run = run;
            Week = week;
            Package = package;
            Rng = rng;
        }

        public cfg.Tables Tables { get; }

        public GameRun Run { get; }

        public cfg.Week Week { get; }

        public cfg.RewardPackage Package { get; }

        public IRandomStream Rng { get; }

        public int RewardHiddenScore => Run?.RewardHiddenScore ?? Week?.RewardHiddenScore ?? 0;
    }
}
