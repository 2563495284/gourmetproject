using System.Collections.Generic;
using GourmetProject.Gameplay.Board;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 结算演出「逐菜渐进揭示」状态：点「吃」时按结算前基线快照每道菜，
    /// 演出过程中随 cue 逐步揭示分数/倍率/复制技能/甜蜜传递，供 hover tips 与演出同步显示。
    /// </summary>
    public sealed class SettlementRevealState
    {
        private sealed class DishReveal
        {
            public float BaseScore;
            public float Flat;
            public float Multiplier;
            public int SkillCount;
            public int TransferredCount;
        }

        private readonly Dictionary<int, DishReveal> _byDish = new();

        /// <summary>结算前对某道菜拍基线：分数=基础、倍率=结算前倍率、技能/传递条目=结算前数量。</summary>
        public void CaptureBaseline(DishInstance dish)
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
                SkillCount = dish.SkillIds?.Count ?? 0,
                TransferredCount = dish.TransferredSkills?.Count ?? 0,
            };
        }

        /// <summary>揭示某道菜「加法分」的当前累加值（ScoreLine.After）。</summary>
        public void RevealFlat(int dishInstanceId, float flatAfter)
        {
            if (_byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.Flat = flatAfter;
            }
        }

        /// <summary>揭示某道菜「倍率」的当前累加值（ScoreLine.After）。</summary>
        public void RevealMultiplier(int dishInstanceId, float multiplierAfter)
        {
            if (_byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.Multiplier = multiplierAfter;
            }
        }

        /// <summary>揭示某道菜结算阶段新复制到的技能（追加在技能列表末尾）。</summary>
        public void RevealCopiedSkills(int dishInstanceId, int count)
        {
            if (count > 0 && _byDish.TryGetValue(dishInstanceId, out DishReveal reveal))
            {
                reveal.SkillCount += count;
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

        /// <summary>取出某道菜当前已揭示的 tips 覆盖参数（未拍基线返回 false）。</summary>
        public bool TryBuildReveal(DishInstance dish, out FoodTipsReveal reveal)
        {
            if (dish != null && _byDish.TryGetValue(dish.Id, out DishReveal state))
            {
                reveal = new FoodTipsReveal(
                    state.BaseScore + state.Flat,
                    state.Multiplier,
                    state.SkillCount,
                    state.TransferredCount);
                return true;
            }

            reveal = null;
            return false;
        }
    }
}
