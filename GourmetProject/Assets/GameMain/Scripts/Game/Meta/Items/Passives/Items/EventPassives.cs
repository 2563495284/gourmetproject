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

    /// <summary>热闹街区：提高 act_event 抽到 Event 型事件的权重。</summary>
    [Preserve]
    [PassiveItemModel("item_more_events")]
    public sealed class MoreEventsModel : PassiveItemModel
    {
        public override float MoreEventsBonus() => Value;
    }

    /// <summary>
    /// 好运连连：每 x 个事件保底一个奖励事件。计数器（自上次保底以来抽到的 Event 型结果数）
    /// 作为本模型的 per-instance 状态，只在持有时存在并随存档序列化。
    /// </summary>
    [Preserve]
    [PassiveItemModel("item_lucky_guarantee")]
    public sealed class LuckyEventGuaranteeModel : PassiveItemModel
    {
        private int _streak;

        public override int LuckyEventGuaranteeEvery() => (int)Value;

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
