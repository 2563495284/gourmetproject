using System.Globalization;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Scoring;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>食物奖励金币百分比（利润提成 +/克扣工钱 -）。</summary>
    [Preserve]
    [PassiveItemModel("item_gold_percent")]
    [PassiveItemModel("item_gold_meal_penalty")]
    public sealed class MealRewardGoldPctModel : PassiveItemModel
    {
        public override float MealRewardGoldPct() => Value;
    }

    [Preserve]
    [PassiveItemModel("item_gold_boss")]
    public sealed class BossGoldModel : PassiveItemModel
    {
        private bool _claimed;

        public override int BossCompleteGold() => _claimed ? 0 : System.Math.Max(0, (int)Value);

        public override int ClaimBossCompleteGold()
        {
            int amount = BossCompleteGold();
            if (amount <= 0)
            {
                return 0;
            }

            _claimed = true;
            MarkIconUsed();
            return amount;
        }

        public override string CaptureState()
            => JoinState(CaptureIconState(), _claimed ? "claimed:1" : string.Empty);

        public override void RestoreState(string data)
        {
            RestoreIconState(data);
            _claimed = ParseStateBool(data, "claimed", false);
        }
    }

    [Preserve]
    [PassiveItemModel("item_gold_on_event")]
    public sealed class EventGoldModel : PassiveItemModel
    {
        public override int EventEnterGold() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_gold_on_shop")]
    public sealed class ShopEnterGoldModel : PassiveItemModel
    {
        public override int ShopEnterGold() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_gold_on_active")]
    public sealed class ActiveUseGoldModel : PassiveItemModel
    {
        public override int ActiveUseGold() => (int)Value;
    }

    [Preserve]
    [PassiveItemModel("item_min_gold")]
    public sealed class MinGoldGuaranteeModel : PassiveItemModel
    {
        public override bool TryGetMinGoldGuarantee(out int value)
        {
            value = (int)Value;
            return true;
        }
    }

    [Preserve]
    [PassiveItemModel("item_gold_week_clear")]
    public sealed class GoldWeekClearModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            if (Run != null && !string.IsNullOrEmpty(Run.AddWeekEndAnchoredTimelineNode("act_gold_clear", ItemId)))
            {
                MarkIconUsed();
            }
        }
    }

    [Preserve]
    [PassiveItemModel("item_interest_cap")]
    public sealed class InterestCapModel : PassiveItemModel
    {
        public override bool TryGetInterestCapOverride(out int value)
        {
            value = (int)Value;
            return true;
        }
    }

    [Preserve]
    [PassiveItemModel("item_extra_interest")]
    public sealed class ExtraInterestModel : PassiveItemModel
    {
        private int _appliedTimelineWeek;
        private string _appliedTimelineId = string.Empty;

        public override bool IsIconUsed => false;

        public override void OnAcquired()
        {
            ApplyToWeekTimeline();
        }

        public override void ApplyToWeekTimeline()
        {
            if (Run == null || string.IsNullOrEmpty(Run.CurrentTimelineId))
            {
                return;
            }

            int timelineWeek = Run.CurrentTimelineWeekIndex;
            string timelineId = Run.CurrentTimelineId;
            if (_appliedTimelineWeek == timelineWeek && _appliedTimelineId == timelineId)
            {
                return;
            }

            string actionId = ResolveInterestActionId();
            if (string.IsNullOrEmpty(actionId))
            {
                return;
            }

            int desiredCount = System.Math.Max(0, (int)Value);
            int existingCount = 0;
            foreach (GourmetProject.Game.Run.RuntimeTimelineNode node in Run.RuntimeTimelineNodes)
            {
                if (node.WeekEndAnchored
                    && node.SourceItemId == ItemId
                    && node.ActionId == actionId)
                {
                    existingCount++;
                }
            }

            while (existingCount < desiredCount)
            {
                if (string.IsNullOrEmpty(Run.AddWeekEndAnchoredTimelineNode(actionId, ItemId)))
                {
                    break;
                }

                existingCount++;
            }

            if (existingCount >= desiredCount)
            {
                _appliedTimelineWeek = timelineWeek;
                _appliedTimelineId = timelineId;
            }
        }

        public override string CaptureState()
            => JoinState(
                CaptureIconState(),
                _appliedTimelineWeek > 0 ? $"timelineWeek:{_appliedTimelineWeek}" : string.Empty,
                !string.IsNullOrEmpty(_appliedTimelineId) ? $"timelineId:{_appliedTimelineId}" : string.Empty);

        public override void RestoreState(string data)
        {
            RestoreIconState(data);
            _appliedTimelineWeek = System.Math.Max(0, ParseStateInt(data, "timelineWeek", 0));
            _appliedTimelineId = ParseStateString(data, "timelineId");
        }

        private string ResolveInterestActionId()
        {
            string configuredActionId = PassiveParam.ParseString(Param, "action");
            if (!string.IsNullOrEmpty(configuredActionId)
                && Run.Tables.TbAction.GetOrDefault(configuredActionId) != null)
            {
                return configuredActionId;
            }

            foreach (cfg.GameAction action in Run.Tables.TbAction.DataList)
            {
                if (action.Behavior == cfg.ActionBehavior.Interest)
                {
                    return action.Id;
                }
            }

            return string.Empty;
        }
    }

    /// <summary>食物分红：获得时登记生效局数；每局额外金币由 MealBonusGoldPerMeal 提供。</summary>
    [Preserve]
    [PassiveItemModel("item_gold_meal_bonus")]
    public sealed class MealBonusGoldModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            Run?.AddMealBonusMeals(PassiveParam.ParseInt(Param, "meals", 0));
        }

        public override void OnRemoved()
        {
            Run?.ClearMealBonusMeals();
        }

        public override bool IsIconUsed => Run != null && Run.MealBonusRemaining <= 0;

        public override int MealBonusGoldPerMeal() => (int)Value;
    }

    /// <summary>获得时随机金币（range:min,max）。</summary>
    [Preserve]
    [PassiveItemModel("item_gold_random")]
    public sealed class GoldNowModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            PassiveOnAcquireEffects.ApplyGoldNow(Run, Definition);
            MarkIconUsed();
        }
    }

    /// <summary>高利贷：获得时发钱，并把 effectParam 指定的还款行动追加到本周末。</summary>
    [Preserve]
    [PassiveItemModel("item_loan")]
    public sealed class LoanModel : PassiveItemModel
    {
        public override void OnAcquired()
        {
            if (Run == null)
            {
                return;
            }

            int grant = System.Math.Max(0, (int)Value);
            string repaymentActionId = Param.Trim();
            cfg.GameAction repaymentAction = Run.Tables.TbAction.GetOrDefault(repaymentActionId);
            if (grant <= 0
                || string.IsNullOrEmpty(repaymentActionId)
                || repaymentAction == null
                || repaymentAction.Behavior != cfg.ActionBehavior.Effect
                || repaymentAction.EffectType != cfg.EffectType.GainGold
                || repaymentAction.EffectValue >= 0f)
            {
                return;
            }

            // 先确认还款节点确实落轴，再发放本金；无时间轴/节点落点失败时不会白拿金币。
            TimelineMutationResult timelineResult = PassiveTimelineMutationService.AddWeekEndNode(
                Run,
                Def.Name,
                repaymentActionId,
                ItemId);
            if (!timelineResult.Changed)
            {
                return;
            }

            Run.Gold += grant;
            MarkIconUsed();
            PassiveMutationPresenter.ShowTimeline(Run, timelineResult);
        }
    }

    [Preserve]
    [PassiveItemModel("item_gold_on_transfer")]
    public sealed class GoldOnTransferCountModel : PassiveItemModel
    {
        private const int DefaultTransferCount = 10;
        private const int DefaultGold = 10;

        private int _transferCount;

        public override string InfoText => _transferCount.ToString(CultureInfo.InvariantCulture);

        public override void ApplyToBattle(BattleSession session)
        {
            if (session != null)
            {
                session.SweetTransferTriggered += OnSweetTransferTriggered;
            }
        }

        public override void OnSweetTransferTriggered(SweetTransferOccurrence occurrence)
        {
            if (!IsStillHeld)
            {
                return;
            }

            _transferCount++;
            int threshold = System.Math.Max(1, PassiveParam.ParseInt(Param, "count", DefaultTransferCount));
            if (_transferCount >= threshold)
            {
                int rewardCount = _transferCount / threshold;
                _transferCount %= threshold;
                Run.Gold += System.Math.Max(0, GoldAmount) * rewardCount;
                Flash();
            }

            RefreshInfoText();
        }

        public override string CaptureState()
            => $"count:{_transferCount.ToString(CultureInfo.InvariantCulture)}";

        public override void RestoreState(string data)
        {
            int threshold = System.Math.Max(1, PassiveParam.ParseInt(Param, "count", DefaultTransferCount));
            int restoredCount = System.Math.Max(0, ParseStateInt(data, "count", 0));

            // 旧版本达到门槛后会保存 rewarded:1 且停止累计；奖励已经发过，迁移成新周期的 0 进度。
            if (ParseStateBool(data, "rewarded", false) && restoredCount >= threshold)
            {
                restoredCount %= threshold;
            }

            _transferCount = restoredCount;
        }

        private int GoldAmount => Value > 0f ? (int)Value : DefaultGold;
    }

    [Preserve]
    [PassiveItemModel("item_cake_to_gold")]
    public sealed class GoldOnCakeLayersModel : PassiveItemModel
    {
        private const int DefaultThreshold = 100;
        private const int DefaultGold = 10;

        public override int GoldForCakeLayers(int happyCakeLayers)
        {
            int threshold = System.Math.Max(0, PassiveParam.ParseInt(Param, "threshold", DefaultThreshold));
            return happyCakeLayers > threshold ? GoldAmount : 0;
        }

        private int GoldAmount => Value > 0f ? (int)Value : DefaultGold;
    }
}
