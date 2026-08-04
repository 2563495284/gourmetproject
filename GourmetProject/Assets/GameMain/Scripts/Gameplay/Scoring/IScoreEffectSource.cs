namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把某类来源（食物技能、风味、材质、遗物等）转换为结算效果队列。</summary>
    public interface IScoreEffectSource
    {
        void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector);
    }
}
