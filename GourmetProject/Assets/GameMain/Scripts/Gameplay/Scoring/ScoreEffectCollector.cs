using System.Collections.Generic;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>效果来源把待执行效果放入收集器，由结算器统一排序与执行。</summary>
    public sealed class ScoreEffectCollector
    {
        private readonly List<ScoreEffectEntry> _entries = new List<ScoreEffectEntry>();
        private int _nextSequence;

        public IReadOnlyList<ScoreEffectEntry> Entries => _entries;

        public void Add(ScoreEffectEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            entry.Sequence = _nextSequence++;
            _entries.Add(entry);
        }
    }
}
