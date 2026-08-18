using System.Collections.Generic;
using BreakInfinity;
using GourmetProject.Gameplay.Board;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 结算演出「逐菜渐进揭示」状态：点「吃」时按结算前基线快照每个食物，
    /// 演出过程中随 cue 逐步揭示分数/倍率/复制技能/甜蜜传递，供 hover tips 与演出同步显示。
    /// </summary>
    public sealed class SettlementRevealState
    {
        private sealed class DishReveal
        {
            public BigDouble BaseScore;
            public BigDouble Flat;
            public BigDouble Multiplier;
            public int SkillCount;
            public int CopiedCount;
            public int TransferredCount;
            public int CountAs;
            public int TemporaryEffectCount;
        }

        private readonly Dictionary<int, DishReveal> _byDish = new();

        /// <summary>结算前对某道菜拍基线：分数=基础、倍率=结算前倍率、技能/传递条目=结算前数量。</summary>
        public void CaptureBaseline(DishInstance dish, int? countAsOverride = null)
        {
            if (dish == null)
            {
                return;
            }

            _byDish[dish.Id] = new DishReveal
            {
                BaseScore = dish.BaseScoreBeforeSettlement,
                Flat = 0f,
                Multiplier = dish.BaseMultiplierBeforeSettlement,
                SkillCount = CountIntrinsicSkills(dish),
                CopiedCount = CountCopiedSkills(dish),
                TransferredCount = dish.TransferredSkills?.Count ?? 0,
                CountAs = System.Math.Max(1, countAsOverride ?? dish.EffectiveCountAs),
                TemporaryEffectCount = dish.TemporaryCategoryEffects?.Count ?? 0,
            };
        }

        /// <summary>揭示某道菜「加法分」的当前累加值（ScoreLine.After）。</summary>
        public void RevealFlat(int dishInstanceId, BigDouble flatAfter)
        {
            if (_byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.Flat = flatAfter;
            }
        }

        /// <summary>揭示某道菜「倍率」的当前累加值（ScoreLine.After）。</summary>
        public void RevealMultiplier(int dishInstanceId, BigDouble multiplierAfter)
        {
            if (_byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.Multiplier = multiplierAfter;
            }
        }

        /// <summary>揭示某道菜结算阶段新复制到的技能（追加在外源技能列表中的复制技能末尾）。</summary>
        public void RevealCopiedSkills(int dishInstanceId, int count)
        {
            if (count > 0 && _byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.CopiedCount += count;
            }
        }

        /// <summary>揭示某道菜结算阶段新收到的甜蜜传递子技能（追加在传递列表末尾）。</summary>
        public void RevealTransferred(int dishInstanceId, int count)
        {
            if (count > 0 && _byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.TransferredCount += count;
            }
        }

        public void RevealCountAs(int dishInstanceId, int countAs)
        {
            if (_byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.CountAs = System.Math.Max(1, countAs);
            }
        }

        public void RevealTemporaryEffects(int dishInstanceId, int count)
        {
            if (count > 0 && _byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.TemporaryEffectCount += count;
            }
        }

        /// <summary>取出某道菜当前已揭示的 tips 覆盖参数（未拍基线返回 false）。</summary>
        public bool TryBuildReveal(DishInstance dish, out FoodTipsReveal reveal)
        {
            if (dish != null && _byDish.TryGetValue(dish.Id, out DishReveal state))
            {
                reveal = new FoodTipsReveal(
                    state.BaseScore + state.Flat,
                    state.Multiplier,
                    state.SkillCount,
                    state.CopiedCount,
                    state.TransferredCount,
                    state.CountAs,
                    state.TemporaryEffectCount);
                return true;
            }

            reveal = null;
            return false;
        }

        private static int CountIntrinsicSkills(DishInstance dish)
        {
            return CountSkillsBySource(dish, copied: false);
        }

        private static int CountCopiedSkills(DishInstance dish)
        {
            return CountSkillsBySource(dish, copied: true);
        }

        private static int CountSkillsBySource(DishInstance dish, bool copied)
        {
            if (dish?.SkillIds == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < dish.SkillIds.Count; i++)
            {
                bool hasSource = !string.IsNullOrEmpty(dish.GetSkillSource(dish.SkillIds[i]));
                if (hasSource == copied)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
