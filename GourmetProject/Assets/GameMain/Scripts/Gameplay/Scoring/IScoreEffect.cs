namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>统一结算效果接口。标签、棋盘格、遗物和周规则最终都执行这个接口。</summary>
    public interface IScoreEffect
    {
        void Apply(ScoreContext context);
    }
}
