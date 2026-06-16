namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 一个标签效果的结算行为。通过注册表按 <see cref="Model.TagEffectType"/> 派发，
    /// 新增效果只需实现本接口并注册，无需改动 ScoreCalculator。
    /// </summary>
    public interface ITagEffect : IScoreEffect
    {
    }
}
