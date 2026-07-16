using System.Collections.Generic;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 菜谱一格条目：dishId + 玩家用「调味小票」永久附加的额外风味。
    /// 额外风味绑定在条目对象上，随移动/删除一起走，避免与 dishId 列表错位。
    /// </summary>
    public sealed class RecipeBookSlot
    {
        private readonly List<string> _extraFlavorIds = new List<string>();
        private readonly List<string> _extraSkillIds = new List<string>();
        private float _scoreFlatBonus;
        private float _scoreMultiplier = 1f;

        public RecipeBookSlot(string dishId)
        {
            DishId = dishId ?? string.Empty;
        }

        public string DishId { get; }

        /// <summary>玩家永久附加的额外风味 id（可叠加，与菜谱变体自带风味叠加）。</summary>
        public IReadOnlyList<string> ExtraFlavorIds => _extraFlavorIds;

        public IReadOnlyList<string> ExtraSkillIds => _extraSkillIds;

        public float ScoreFlatBonus => _scoreFlatBonus;

        public float ScoreMultiplier => _scoreMultiplier;

        public bool HasExtraFlavors => _extraFlavorIds.Count > 0;

        public void AddFlavor(string flavorId, int flavorLimit = int.MaxValue)
        {
            if (string.IsNullOrEmpty(flavorId))
            {
                return;
            }

            flavorLimit = System.Math.Max(1, flavorLimit);
            if (_extraFlavorIds.Count < flavorLimit)
            {
                _extraFlavorIds.Add(flavorId);
                return;
            }

            _extraFlavorIds[_extraFlavorIds.Count - 1] = flavorId;
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

            return _extraFlavorIds.Remove(flavorId);
        }

        public bool ReplaceFlavor(string toFlavorId)
        {
            if (string.IsNullOrEmpty(toFlavorId))
            {
                return false;
            }

            if (_extraFlavorIds.Count == 0)
            {
                _extraFlavorIds.Add(toFlavorId);
            }
            else
            {
                _extraFlavorIds[_extraFlavorIds.Count - 1] = toFlavorId;
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

        public void MultiplyScore(float multiplier)
        {
            if (multiplier > 0f)
            {
                _scoreMultiplier *= multiplier;
            }
        }

        public void AddScoreFlat(float amount)
        {
            _scoreFlatBonus += amount;
        }

        public void RestoreScoreFlatBonus(float amount)
        {
            _scoreFlatBonus = amount;
        }

        public void RestoreScoreMultiplier(float multiplier)
        {
            _scoreMultiplier = multiplier > 0f ? multiplier : 1f;
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
