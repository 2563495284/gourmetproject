using System.Globalization;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    [Preserve]
    [PassiveItemModel("item_req_normal_down")]
    [PassiveItemModel("item_req_normal_up")]
    public sealed class RequiredScoreNormalModel : RequiredScorePctModel
    {
        public RequiredScoreNormalModel() : base(cfg.FoodActionKind.Normal)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_req_super_down")]
    [PassiveItemModel("item_req_super_up")]
    public sealed class RequiredScoreSuperModel : RequiredScorePctModel
    {
        public RequiredScoreSuperModel() : base(cfg.FoodActionKind.Super)
        {
        }
    }

    [Preserve]
    [PassiveItemModel("item_req_feast_down")]
    [PassiveItemModel("item_req_feast_up")]
    public sealed class RequiredScoreFeastModel : RequiredScorePctModel
    {
        public RequiredScoreFeastModel() : base(cfg.FoodActionKind.Feast)
        {
        }
    }

    /// <summary>「分数变1」：获得时登记生效局数（消耗与判定在 GameRun/RewardGranter）。</summary>
    [Preserve]
    [PassiveItemModel("item_score_to_one")]
    public sealed class RequiredScoreToOneModel : PassiveItemModel
    {
        public override string InfoText => Run != null && Run.ScoreToOneRemaining > 0
            ? Run.ScoreToOneRemaining.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        public override void OnAcquired()
        {
            Run?.AddScoreToOneMeals(PassiveParam.ParseInt(Param, "meals", 0));
        }

        public override void OnRemoved()
        {
            Run?.ClearScoreToOneMeals();
        }

        public override bool IsIconUsed => Run != null && Run.ScoreToOneRemaining <= 0;
    }
}
