using System;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Runtime;
using UnityEngine;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 顶部行动轴（<see cref="ActionAxisBar"/>）的构建 + 每个时间线节点的 hover Tip 装配。
    /// Tip 视图实例由外层惰性创建后通过 getter 注入，Boss 预览用独立 RNG 快照避免污染随机流。
    /// </summary>
    internal sealed class TimelineAxisBinder
    {
        private readonly ActionAxisBar _axis;
        private readonly Func<ShopNodeTipView> _shopTip;
        private readonly Func<InterestNodeTipView> _interestTip;
        private readonly Func<BossFeastTipView> _bossTip;

        public TimelineAxisBinder(
            ActionAxisBar axis,
            Func<ShopNodeTipView> shopTip,
            Func<InterestNodeTipView> interestTip,
            Func<BossFeastTipView> bossTip)
        {
            _axis = axis;
            _shopTip = shopTip;
            _interestTip = interestTip;
            _bossTip = bossTip;
        }

        public void Rebuild(GameRun run)
        {
            _axis?.Build(run, (node, go) => ConfigureNodeTip(run, node, go));
        }

        private void ConfigureNodeTip(GameRun run, cfg.TimelineNode node, GameObject nodeObject)
        {
            if (node == null || nodeObject == null)
            {
                return;
            }

            TipHoverTrigger trigger = nodeObject.GetComponent<TipHoverTrigger>();
            if (trigger == null)
            {
                trigger = nodeObject.AddComponent<TipHoverTrigger>();
            }

            cfg.GameAction action = TimelineService.NodeAction(run, node);
            switch (ActionDisplay.KindOf(run?.Tables, action))
            {
                case ActionDisplayKind.Shop:
                {
                    ShopNodeTipView tip = _shopTip?.Invoke();
                    if (tip != null)
                    {
                        trigger.SetTip(tip, () => tip.Bind(node.Day));
                    }

                    break;
                }

                case ActionDisplayKind.Interest:
                {
                    InterestNodeTipView tip = _interestTip?.Invoke();
                    if (tip != null)
                    {
                        trigger.SetTip(tip, () => BindInterestNodeTip(run, tip, node, action));
                    }

                    break;
                }

                case ActionDisplayKind.Boss:
                {
                    BossFeastTipView tip = _bossTip?.Invoke();
                    if (tip != null)
                    {
                        trigger.SetTip(tip, () => BindBossNodeTip(run, tip, node, action));
                    }

                    break;
                }

                default:
                    trigger.ClearTip();
                    break;
            }
        }

        private static void BindInterestNodeTip(GameRun run, InterestNodeTipView tip, cfg.TimelineNode node, cfg.GameAction action)
        {
            if (tip == null || node == null || action == null)
            {
                return;
            }

            int threshold = Mathf.Max(0, run?.InterestThreshold ?? 0);
            int goldPer = run != null && run.InterestGoldPer > 0 ? run.InterestGoldPer : 1;
            int maxGain = run?.InterestCap ?? 0;
            int currentGain = TimelineMath.Interest(run?.Gold ?? 0, threshold, goldPer, maxGain);
            string desc = threshold > 0
                ? $"每有{threshold}枚金币，获得{goldPer}枚，最高可获得{maxGain}枚。当前可获得{currentGain}枚"
                : "当前节点没有有效金币阈值。";
            tip.Bind(desc, node.Day);
        }

        private static void BindBossNodeTip(GameRun run, BossFeastTipView tip, cfg.TimelineNode node, cfg.GameAction action)
        {
            if (tip == null || node == null)
            {
                return;
            }

            cfg.Food boss = PreviewBoss(run, node, action);
            if (boss == null)
            {
                tip.Bind("Bug", "不应该出现此条信息，请联系开发者。", run?.RequiredScore ?? 0);
                return;
            }

            cfg.BossDebuff debuff = PreviewBossDebuff(run, node, action);
            int required = run != null
                ? HiddenScoreService.TargetScore(
                    run,
                    new ActionExecutionContext(action) { TargetScoreDayOverride = node.Day },
                    debuff?.TargetScoreHiddenOffset ?? 0)
                : 0;
            tip.Bind(debuff.Name, debuff.Desc, required);
        }

        private static cfg.Food PreviewBoss(GameRun run, cfg.TimelineNode node, cfg.GameAction action)
        {
            return run != null && node != null && action != null
                ? BossService.ResolveBossFood(run)
                : null;
        }

        private static cfg.BossDebuff PreviewBossDebuff(GameRun run, cfg.TimelineNode node, cfg.GameAction action)
        {
            if (run == null || node == null || action == null)
            {
                return null;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Boss, $"w{run.WeekIndex}_{node.Id}_debuff");
            RngState state = rng.State;
            try
            {
                return BossService.RollBossDebuff(run, rng, mutateHistoryOnExhaustion: false);
            }
            finally
            {
                rng.State = state;
            }
        }
    }
}
