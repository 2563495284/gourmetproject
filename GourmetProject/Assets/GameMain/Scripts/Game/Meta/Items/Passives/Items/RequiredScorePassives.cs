using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_req_normal_down")]
    [PassiveItemModel("item_req_normal_up")]
    public sealed class RequiredScoreNormalModel : RequiredScorePctModel
    {
        public RequiredScoreNormalModel() : base(MealTier.Normal)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_req_super_down")]
    [PassiveItemModel("item_req_super_up")]
    public sealed class RequiredScoreSuperModel : RequiredScorePctModel
    {
        public RequiredScoreSuperModel() : base(MealTier.Super)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_req_feast_down")]
    [PassiveItemModel("item_req_feast_up")]
    public sealed class RequiredScoreFeastModel : RequiredScorePctModel
    {
        public RequiredScoreFeastModel() : base(MealTier.Feast)
        {
        }
    }

    /// <summary>「分数变1」：获得时登记生效局数（消耗与判定在 GameRun/RewardGranter）。</summary>
    [Preserve]
    [PassiveItemModel("item_score_to_one")]
    public sealed class RequiredScoreToOneModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            Run?.AddScoreToOneMeals(PassiveParam.ParseInt(Param, "meals", 0));
        }
    }
}
