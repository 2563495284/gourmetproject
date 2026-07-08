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

            switch (node.NodeType)
            {
                case cfg.TimelineNodeType.Shop:
                {
                    ShopNodeTipView tip = _shopTip?.Invoke();
                    if (tip != null)
                    {
                        trigger.SetTip(tip, () => tip.Bind(node.Day));
                    }

                    break;
                }

                case cfg.TimelineNodeType.Interest:
                {
                    InterestNodeTipView tip = _interestTip?.Invoke();
                    if (tip != null)
                    {
                        trigger.SetTip(tip, () => BindInterestNodeTip(run, tip, node));
                    }

                    break;
                }

                case cfg.TimelineNodeType.Boss:
                {
                    BossFeastTipView tip = _bossTip?.Invoke();
                    if (tip != null)
                    {
                        trigger.SetTip(tip, () => BindBossNodeTip(run, tip, node));
                    }

                    break;
                }

                default:
                    trigger.ClearTip();
                    break;
            }
        }

        private static void BindInterestNodeTip(GameRun run, InterestNodeTipView tip, cfg.TimelineNode node)
        {
            if (tip == null || node == null)
            {
                return;
            }

            int threshold = Mathf.Max(0, Mathf.RoundToInt(node.PayloadValue));
            int goldPer = int.TryParse(node.PayloadParam, out int parsedGoldPer) ? parsedGoldPer : 1;
            int maxGain = run?.InterestCap ?? 0;
            int currentGain = TimelineMath.Interest(run?.Gold ?? 0, threshold, goldPer, maxGain);
            string desc = threshold > 0
                ? $"每有{threshold}枚金币，获得{goldPer}枚，最高可获得{maxGain}枚。当前可获得{currentGain}枚"
                : "当前节点没有有效金币阈值。";
            tip.Bind(desc, node.Day);
        }

        private static void BindBossNodeTip(GameRun run, BossFeastTipView tip, cfg.TimelineNode node)
        {
            if (tip == null || node == null)
            {
                return;
            }

            cfg.Boss boss = PreviewBoss(run, node);
            if (boss == null)
            {
                tip.Bind("恶魔", "即将迎来周末盛宴。", run?.RequiredScore ?? 0);
                return;
            }

            int required = run != null ? run.ComputeBossRequiredScore(boss.ScoreProfileId) : 0;
            tip.Bind(boss, BossMechanicDescription(boss.Modifier), required);
        }

        private static cfg.Boss PreviewBoss(GameRun run, cfg.TimelineNode node)
        {
            if (run == null || node == null)
            {
                return null;
            }

            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Boss, $"w{run.WeekIndex}_{node.Id}");
            RngState state = rng.State;
            try
            {
                return BossService.RollBoss(run, rng, node.PayloadParam);
            }
            finally
            {
                rng.State = state;
            }
        }

        private static string BossMechanicDescription(string modifier)
        {
            switch (modifier)
            {
                case "small_board":
                    return "特殊机制：胃部棋盘空间缩小，需要更谨慎地规划摆放。";
                case "limit_serve":
                    return "特殊机制：本场最多上菜 5 次。";
                case "":
                case null:
                    return "击败 Boss，完成本周盛宴挑战。";
                default:
                    return $"特殊机制：{modifier}";
            }
        }
    }
}
