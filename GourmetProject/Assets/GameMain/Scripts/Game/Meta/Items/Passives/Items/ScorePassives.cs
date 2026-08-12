using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_perma_flat_all")]
    [PassiveItemModel("item_perma_flat_all_plus")]
    public sealed class PermanentAddFlatAllModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            if (Run == null || Value <= 0f)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<GourmetProject.Game.Run.RecipeBookSlot> recipe =
                Run.RecipeEntries;
            for (int dishIndex = 0; dishIndex < recipe.Count; dishIndex++)
            {
                Run.AddRecipeScoreFlat(dishIndex, Value);
            }

            MarkIconUsed();
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

    /// <summary>结算开始时检查食物数阈值，命中后所有食物倍率 ×N（lte/gte 由 effectParam 区分）。</summary>
    [Preserve]
    [PassiveItemModel("item_count_le_mult")]
    [PassiveItemModel("item_count_ge_mult")]
    public sealed class CountThresholdAllDishMultModel : ScoreSpecModel
    {
        public CountThresholdAllDishMultModel() : base(ItemScoreEffectType.CountThresholdAllDishMult)
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

    /// <summary>首个正式上菜立即获得倍率 +N。</summary>
    [Preserve]
    [PassiveItemModel("item_first_+2")]
    public sealed class FirstServeMultFlatModel : PassiveItemModel
    {
        public override void ApplyToBattle(BattleSession session)
        {
            if (session != null)
            {
                session.Served += (dish, serveIndex) => OnServed(session, dish, serveIndex);
            }
        }

        private void OnServed(BattleSession session, DishInstance dish, int serveIndex)
        {
            if (!IsStillHeld || dish == null || serveIndex != 1 || Value <= 0f)
            {
                return;
            }

            session.AddPassiveServeMultiplierFlat(dish, Value, ItemId, Def?.Name);
        }
    }

    /// <summary>点击结算后，在最早的结算阶段给最后上菜的食物增加倍率。</summary>
    [Preserve]
    [PassiveItemModel("item_last_+2")]
    public sealed class LastServeMultFlatModel : ScoreSpecModel
    {
        public LastServeMultFlatModel()
            : base(ItemScoreEffectType.NthServeMultFlat)
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
            if (!IsStillHeld)
            {
                return;
            }

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

            session.AddPassiveServeMultiplierFlat(dish, value, ItemId, Def?.Name);
        }
    }

    /// <summary>所有食物额外「视为食物数」。</summary>
    [Preserve]
    [PassiveItemModel("item_count_as_all")]
    public sealed class CountAsBonusAllModel : PassiveItemModel
    {
        public override int ExtraCountAsPerDish() => (int)Value;
    }

    /// <summary>暖心壁灯：按本场结算开始时仍拥有的红心数，为每道食物增加加法分。</summary>
    [Preserve]
    [PassiveItemModel("item_heart_flat_all")]
    public sealed class HeartRemainingFlatAllModel : PassiveItemModel
    {
        public override System.Collections.Generic.IEnumerable<ItemScoreSpec> BuildScoreSpecs()
        {
            int hearts = Run != null ? System.Math.Max(0, Run.HeartsRemaining) : 0;
            yield return new ItemScoreSpec(
                ItemScoreEffectType.AllDishFlat,
                Value * hearts,
                Param,
                ItemId,
                Def?.Name ?? ItemId);
        }
    }

    /// <summary>裂纹心形镜：按本场结算开始时的空红心数，为每道食物增加倍率加区。</summary>
    [Preserve]
    [PassiveItemModel("item_empty_heart_mult_all")]
    public sealed class EmptyHeartMultFlatAllModel : PassiveItemModel
    {
        public override System.Collections.Generic.IEnumerable<ItemScoreSpec> BuildScoreSpecs()
        {
            int emptyHearts = Run != null
                ? System.Math.Max(0, Run.HeartCapacity - Run.HeartsRemaining)
                : 0;
            yield return new ItemScoreSpec(
                ItemScoreEffectType.AllDishMultFlat,
                Value * emptyHearts,
                Param,
                ItemId,
                Def?.Name ?? ItemId);
        }
    }
}
