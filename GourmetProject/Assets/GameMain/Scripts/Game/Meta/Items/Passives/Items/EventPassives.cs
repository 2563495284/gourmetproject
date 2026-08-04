using System.Globalization;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>幸运符：提高 act_event 抽到 Reward 型事件的权重。</summary>
    [Preserve]
    [PassiveItemModel("item_lucky_chance")]
    public sealed class LuckyEventChanceModel : PassiveItemModel
    {
        public override float LuckyEventChanceBonus() => Value;
    }

    /// <summary>街角路牌：候选保底筛选完成后，提高包含 Event 行动的大组权重。</summary>
    [Preserve]
    [PassiveItemModel("item_more_events")]
    public sealed class MoreEventsModel : PassiveItemModel
    {
        public override float EventActionLargeGroupWeightBonus() => Value;
    }

    /// <summary>超级计划：候选保底筛选完成后，提高包含 Super 行动的大组权重。</summary>
    [Preserve]
    [PassiveItemModel("item_more_super_actions")]
    public sealed class MoreSuperActionsModel : PassiveItemModel
    {
        public override float SuperActionLargeGroupWeightBonus() => Value;
    }

    /// <summary>幸运摇杆：提高抽奖机归一后的总中奖概率。</summary>
    [Preserve]
    [PassiveItemModel("item_slot_win_chance")]
    public sealed class SlotWinChanceModel : PassiveItemModel
    {
        public override float SlotWinChanceBonus() => Value;
    }

    /// <summary>奖励隐藏分增益：数值直接来自对应隐藏分列，由基类按用途透传。</summary>
    [Preserve]
    [PassiveItemModel("item_dish_hidden_bonus")]
    [PassiveItemModel("item_fragment_hidden_bonus")]
    [PassiveItemModel("item_passive_hidden_bonus")]
    [PassiveItemModel("item_better_food_rewards")]
    [PassiveItemModel("item_larger_fragment_rewards")]
    [PassiveItemModel("item_better_passive_rewards")]
    public sealed class HiddenScoreBonusModel : PassiveItemModel
    {
    }

    /// <summary>
    /// 好运连连：经历配置数量的自然事件后，下一次（即第 value+1 次）保底奖励事件。
    /// 计数器记录自上次保底以来已完成的自然抽取数，不因自然抽到 Reward 而重置。
    /// 作为本模型的 per-instance 状态，只在持有时存在并随存档序列化。
    /// </summary>
    [Preserve]
    [PassiveItemModel("item_lucky_guarantee")]
    public sealed class LuckyEventGuaranteeModel : PassiveItemModel
    {
        private int _streak;

        public override int LuckyEventGuaranteeEvery() => System.Math.Max(0, (int)Value);

        public override int EventGuaranteeStreak => _streak;

        public override void IncrementEventGuaranteeStreak() => _streak++;

        public override void ResetEventGuaranteeStreak() => _streak = 0;

        public override string CaptureState() => _streak.ToString(CultureInfo.InvariantCulture);

        public override void RestoreState(string data)
        {
            _streak = int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : 0;
        }
    }
}
