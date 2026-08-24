using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 食谱一格条目：dishId + 玩家后续获得的风味。
    /// dishId 自带风味与后续风味在玩法上组成同一个有序队列；内部仍分别保存，
    /// 以兼容现有食物变体与存档结构。
    /// </summary>
    public sealed class RecipeBookSlot
    {
        private readonly List<string> _extraFlavorIds = new List<string>();
        private readonly List<string> _extraSkillIds = new List<string>();
        private BigDouble _scoreFlatBonus;
        private BigDouble _scoreMultiplier = BigDouble.One;

        public RecipeBookSlot(string dishId)
        {
            DishId = ContentIdAliases.NormalizeDishId(dishId) ?? string.Empty;
        }

        public string DishId { get; private set; }

        /// <summary>取得食物后继续获得的风味 id，按获得时间从早到晚排列。</summary>
        public IReadOnlyList<string> ExtraFlavorIds => _extraFlavorIds;

        public IReadOnlyList<string> ExtraSkillIds => _extraSkillIds;

        public BigDouble ScoreFlatBonus => _scoreFlatBonus;

        public BigDouble ScoreMultiplier => _scoreMultiplier;

        public bool HasExtraFlavors => _extraFlavorIds.Count > 0;

        public void ReplaceDishId(string dishId)
        {
            if (!string.IsNullOrEmpty(dishId))
            {
                DishId = ContentIdAliases.NormalizeDishId(dishId);
            }
        }

        /// <summary>
        /// 替换食物本体，并把给定的完整风味序列保存为后天风味。
        /// 仅供替换到无原生风味的食物本体时使用，以保持原有风味顺序。
        /// </summary>
        internal void ReplaceDishIdAndExtraFlavors(string dishId, IReadOnlyList<string> flavorIds)
        {
            if (string.IsNullOrEmpty(dishId))
            {
                return;
            }

            DishId = ContentIdAliases.NormalizeDishId(dishId);
            _extraFlavorIds.Clear();
            if (flavorIds == null)
            {
                return;
            }

            foreach (string flavorId in flavorIds)
            {
                if (!string.IsNullOrEmpty(flavorId))
                {
                    _extraFlavorIds.Add(ContentIdAliases.NormalizeFlavorId(flavorId));
                }
            }
        }

        public void AddFlavor(string flavorId, int flavorLimit = int.MaxValue)
        {
            if (string.IsNullOrEmpty(flavorId))
            {
                return;
            }

            flavorLimit = System.Math.Max(1, flavorLimit);
            if (_extraFlavorIds.Count >= flavorLimit)
            {
                _extraFlavorIds.RemoveAt(0);
            }

            _extraFlavorIds.Add(ContentIdAliases.NormalizeFlavorId(flavorId));
        }

        public bool RemoveOldestFlavor()
        {
            if (_extraFlavorIds.Count == 0)
            {
                return false;
            }

            _extraFlavorIds.RemoveAt(0);
            return true;
        }

        public bool RemoveFlavor(string flavorId)
        {
            if (_extraFlavorIds.Count == 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(flavorId))
            {
                _extraFlavorIds.RemoveAt(_extraFlavorIds.Count - 1);
                return true;
            }

            return _extraFlavorIds.Remove(ContentIdAliases.NormalizeFlavorId(flavorId));
        }

        public bool ReplaceFlavor(string toFlavorId)
        {
            if (string.IsNullOrEmpty(toFlavorId))
            {
                return false;
            }

            if (_extraFlavorIds.Count == 0)
            {
                _extraFlavorIds.Add(ContentIdAliases.NormalizeFlavorId(toFlavorId));
            }
            else
            {
                _extraFlavorIds[_extraFlavorIds.Count - 1] = ContentIdAliases.NormalizeFlavorId(toFlavorId);
            }

            return true;
        }

        public void AddExtraSkill(string skillId)
        {
            if (!string.IsNullOrEmpty(skillId) && !_extraSkillIds.Contains(skillId))
            {
                _extraSkillIds.Add(skillId);
            }
        }

        public void MultiplyScore(BigDouble multiplier)
        {
            if (multiplier > BigDouble.Zero)
            {
                _scoreMultiplier *= multiplier;
            }
        }

        public void AddScoreFlat(BigDouble amount)
        {
            _scoreFlatBonus += amount;
        }

        public void RestoreScoreFlatBonus(BigDouble amount)
        {
            _scoreFlatBonus = amount;
        }

        public void RestoreScoreMultiplier(BigDouble multiplier)
        {
            _scoreMultiplier = multiplier > BigDouble.Zero ? multiplier : BigDouble.One;
        }

        public RecipeBookSlot Clone()
        {
            var copy = new RecipeBookSlot(DishId);
            copy._extraFlavorIds.AddRange(_extraFlavorIds);
            copy._extraSkillIds.AddRange(_extraSkillIds);
            copy._scoreFlatBonus = _scoreFlatBonus;
            copy._scoreMultiplier = _scoreMultiplier;
            return copy;
        }
    }
}
