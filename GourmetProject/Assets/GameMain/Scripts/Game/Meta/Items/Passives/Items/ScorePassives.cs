using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_perma_flat_all")]
    public sealed class PermanentAddFlatAllModel : ScoreSpecModel
    {
        public PermanentAddFlatAllModel() : base(ItemScoreEffectType.PermanentAddFlatAll)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_perma_mult_all")]
    public sealed class PermanentAddMultAllModel : ScoreSpecModel
    {
        public PermanentAddMultAllModel() : base(ItemScoreEffectType.PermanentAddMultAll)
        {
        }
    }

    /// <summary>食物数阈值 → 终局倍率（lte/gte 由 effectParam 区分）。</summary>
    [Preserve]
    [PassiveItemModel("item_count_le_mult")]
    [PassiveItemModel("item_count_ge_mult")]
    public sealed class CountThresholdFinalMultModel : ScoreSpecModel
    {
        public CountThresholdFinalMultModel() : base(ItemScoreEffectType.CountThresholdFinalMult)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_skill_count_mult")]
    public sealed class PerSkillMultFlatModel : ScoreSpecModel
    {
        public PerSkillMultFlatModel() : base(ItemScoreEffectType.PerSkillMultFlat)
        {
        }
    }

    /// <summary>指定上菜顺序倍率 +N（first/last 由 effectParam index 区分）。</summary>
    [Preserve]
    [PassiveItemModel("item_first_+2")]
    [PassiveItemModel("item_last_+2")]
    public sealed class NthServeMultFlatModel : ScoreSpecModel
    {
        public NthServeMultFlatModel() : base(ItemScoreEffectType.NthServeMultFlat)
        {
        }
    }

    /// <summary>指定上菜顺序 ×N（first/last 由 effectParam index 区分）。</summary>
    [Preserve]
    [PassiveItemModel("item_first_x2")]
    [PassiveItemModel("item_last_x2")]
    public sealed class NthServeMultModel : ScoreSpecModel
    {
        public NthServeMultModel() : base(ItemScoreEffectType.NthServeMult)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_every3_next_mult")]
    public sealed class EveryNthServeMultModel : PassiveItemModel
    {
        private const int DefaultEvery = 3;

        public override void ApplyToBattle(BattleSession session)
        {
            if (session != null)
            {
                session.Served += (dish, serveIndex) => OnServed(session, dish, serveIndex);
            }
        }

        private void OnServed(BattleSession session, DishInstance dish, int serveIndex)
        {
            int every = System.Math.Max(1, PassiveParam.ParseInt(Param, "every", DefaultEvery));
            if (dish == null || serveIndex <= every || serveIndex % (every + 1) != 0)
            {
                return;
            }

            float value = Value;
            if (value <= 0f)
            {
                return;
            }

            session.AddServeMultiplierFlat(dish, value);
            Flash();
        }
    }

    /// <summary>所有菜额外「视为食物数」。</summary>
    [Preserve]
    [PassiveItemModel("item_count_as_all")]
    public sealed class CountAsBonusAllModel : PassiveItemModel
    {
        public override int ExtraCountAsPerDish() => (int)Value;
    }
}
