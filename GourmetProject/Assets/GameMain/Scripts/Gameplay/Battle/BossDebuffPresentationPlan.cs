using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// Boss Debuff 的纯表现差分。玩法数据在装配时已经生效，UI 只用这份快照暂时伪装并逐步揭示。
    /// </summary>
    public sealed class BossDebuffPresentationPlan
    {
        public BossDebuffPresentationPlan(string debuffId, IEnumerable<string> dialogues)
        {
            DebuffId = debuffId ?? string.Empty;
            Dialogues = new List<string>();
            if (dialogues == null)
            {
                return;
            }

            foreach (string dialogue in dialogues)
            {
                if (!string.IsNullOrWhiteSpace(dialogue))
                {
                    Dialogues.Add(dialogue.Trim());
                }
            }
        }

        public string DebuffId { get; }

        public List<string> Dialogues { get; }

        public List<GridPos> AddedCells { get; } = new List<GridPos>();

        public List<GridPos> RemovedCells { get; } = new List<GridPos>();

        public List<GridPos> DisabledCells { get; } = new List<GridPos>();

        public List<string> DuplicatedDishIds { get; } = new List<string>();

        public int InitialRecipeEntryCount { get; private set; }

        public int FinalRecipeEntryCount { get; private set; }

        public void SetRecipeEntryCounts(int initial, int final)
        {
            InitialRecipeEntryCount = Math.Max(0, initial);
            FinalRecipeEntryCount = Math.Max(0, final);
        }

        public bool HasIntroPresentation => AddedCells.Count > 0
            || RemovedCells.Count > 0
            || DisabledCells.Count > 0
            || DuplicatedDishIds.Count > 0
            || string.Equals(DebuffId, "debuff_omakase", StringComparison.Ordinal);
    }

    /// <summary>单场对白洗牌袋：耗尽前不放回，重洗后避免和上一句立即重复。</summary>
    public sealed class BossDialogueShuffleBag
    {
        private readonly List<string> _source = new List<string>();
        private readonly List<string> _remaining = new List<string>();
        private readonly IRandomStream _rng;
        private string _last;

        public BossDialogueShuffleBag(IEnumerable<string> dialogues, IRandomStream rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            if (dialogues == null)
            {
                return;
            }

            foreach (string dialogue in dialogues)
            {
                if (!string.IsNullOrWhiteSpace(dialogue))
                {
                    _source.Add(dialogue.Trim());
                }
            }
        }

        public int Count => _source.Count;

        public string Draw()
        {
            if (_source.Count == 0)
            {
                return string.Empty;
            }

            if (_source.Count == 1)
            {
                _last = _source[0];
                return _last;
            }

            if (_remaining.Count == 0)
            {
                _remaining.AddRange(_source);
                _rng.Shuffle(_remaining);
                if (!string.IsNullOrEmpty(_last)
                    && string.Equals(_remaining[_remaining.Count - 1], _last, StringComparison.Ordinal))
                {
                    int swapIndex = _remaining.Count - 2;
                    (_remaining[swapIndex], _remaining[_remaining.Count - 1]) =
                        (_remaining[_remaining.Count - 1], _remaining[swapIndex]);
                }
            }

            int index = _remaining.Count - 1;
            _last = _remaining[index];
            _remaining.RemoveAt(index);
            return _last;
        }
    }
}
