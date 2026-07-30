using System;
using System.Collections.Generic;
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
        private readonly Func<TimelineNodeTipView> _timelineTip;

        public TimelineAxisBinder(
            ActionAxisBar axis,
            Func<TimelineNodeTipView> timelineTip)
        {
            _axis = axis;
            _timelineTip = timelineTip;
        }

        public void Rebuild(GameRun run, string executingNodeId = null)
        {
            _axis?.Build(
                run,
                (node, go) => ConfigureNodeTip(run, node, go),
                executingNodeId);
        }

        public bool BeginActiveItemTargeting(
            GameRun run,
            ItemDefinition item,
            IReadOnlyList<ActiveTarget> targets,
            Action<ActiveTarget> onConfirm,
            Action onCancel)
        {
            if (_axis == null || run == null || item == null || targets == null || onConfirm == null)
            {
                return false;
            }

            Rebuild(run);
            if (ItemActiveUsage.IsTimelineAddEffect(item.EffectType))
            {
                var days = new List<int>(targets.Count);
                foreach (ActiveTarget target in targets)
                {
                    days.Add(target.X);
                }

                return _axis.BeginAddDaySelection(
                    run,
                    item.EffectParam,
                    days,
                    day => onConfirm(new ActiveTarget(
                        day.ToString(),
                        day,
                        targetKind: cfg.ItemTargetKind.Global)),
                    onCancel);
            }

            if (item.EffectType == ItemEffectTypes.TimelineDeleteNode)
            {
                return _axis.BeginDeleteNodeSelection(
                    run,
                    TargetIds(targets),
                    nodeId => onConfirm(new ActiveTarget(
                        nodeId,
                        targetKind: cfg.ItemTargetKind.Global)),
                    onCancel);
            }

            if (item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                || item.EffectType == ItemEffectTypes.TimelineExecutePast)
            {
                return _axis.BeginExecuteNodeSelection(
                    run,
                    TargetIds(targets),
                    nodeId => onConfirm(new ActiveTarget(
                        nodeId,
                        targetKind: cfg.ItemTargetKind.Global)),
                    onCancel);
            }

            return false;
        }

        private static List<string> TargetIds(IReadOnlyList<ActiveTarget> targets)
        {
            var ids = new List<string>(targets?.Count ?? 0);
            if (targets == null)
            {
                return ids;
            }

            foreach (ActiveTarget target in targets)
            {
                if (!string.IsNullOrEmpty(target.Id))
                {
                    ids.Add(target.Id);
                }
            }

            return ids;
        }

        public void EndActiveItemTargeting()
        {
            _axis?.EndSelection();
        }

        public void CancelActiveItemTargeting()
        {
            _axis?.CancelSelection();
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
            if (action == null)
            {
                trigger.ClearTip();
                return;
            }

            TimelineNodeTipView tip = _timelineTip?.Invoke();
            if (tip == null)
            {
                trigger.ClearTip();
                return;
            }

            bool isBoss = ActionDisplay.KindOf(run?.Tables, action) == ActionDisplayKind.Boss;
            trigger.SetTip(
                tip,
                isBoss
                    ? () => BindBossNodeTip(run, tip, node, action)
                    : () => BindActionNodeTip(run, tip, node, action));
        }

        private static void BindActionNodeTip(
            GameRun run,
            TimelineNodeTipView tip,
            cfg.TimelineNode node,
            cfg.GameAction action)
        {
            if (tip == null || node == null || action == null)
            {
                return;
            }

            string desc = EventService.FormatRuntimeText(run, action.Desc);
            tip.Bind(action.Name, desc, $"节点天数：{node.Day}天");
        }

        private static void BindBossNodeTip(
            GameRun run,
            TimelineNodeTipView tip,
            cfg.TimelineNode node,
            cfg.GameAction action)
        {
            if (tip == null || node == null)
            {
                return;
            }

            cfg.Food boss = PreviewBoss(run, node, action);
            if (boss == null)
            {
                tip.Bind(
                    "Bug",
                    "不应该出现此条信息，请联系开发者。",
                    $"美味度要求：{(run?.RequiredScore ?? 0):N0}");
                return;
            }

            cfg.BossDebuff debuff = PreviewBossDebuff(run, node, action);
            int required = run != null
                ? HiddenScoreService.TargetScore(
                    run,
                    new ActionExecutionContext(action) { TargetScoreDayOverride = node.Day },
                    debuff?.TargetScoreHiddenOffset ?? 0)
                : 0;
            tip.Bind(debuff.Name, debuff.Desc, $"美味度要求：{required:N0}");
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

            string bossKey = $"w{run.WeekIndex}_{node.Id}";
            IRandomStream rng = GameApp.Random.DomainStream(
                SeedDomains.Boss,
                BossService.BuildBossDebuffSeedKey(run, bossKey, node.Id));
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
