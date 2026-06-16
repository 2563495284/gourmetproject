namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把某类来源（食品标签、格子标签、遗物等）转换为结算效果队列。</summary>
    public interface IScoreEffectSource
    {
        void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector);
    }
}
