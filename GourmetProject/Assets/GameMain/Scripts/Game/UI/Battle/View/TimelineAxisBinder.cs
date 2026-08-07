using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Tooltips;
using UnityEngine;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 顶部时间轴（<see cref="ActionAxisBar"/>）的构建 + 每个时间线节点的 hover Tip 装配。
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

        public void BuildPresentation(
            GameRun run,
            IReadOnlyList<RuntimeTimelineNodeSnapshot> nodes,
            float lengthDays,
            string executingNodeId,
            bool animate)
        {
            _axis?.BuildPresentation(
                run,
                nodes,
                lengthDays,
                (node, go) => ConfigureNodeTip(run, node, go),
                executingNodeId,
                animate);
        }

        public TimelineAxisViewState CreateState(
            GameRun run,
            IReadOnlyList<RuntimeTimelineNodeSnapshot> nodes = null,
            float? lengthDays = null,
            float? currentDay = null,
            string executingNodeId = null)
        {
            return ActionAxisBar.CreateState(
                run,
                nodes,
                lengthDays ?? run?.TimelineLengthDays ?? 1f,
                currentDay ?? run?.CurrentDay ?? 0f,
                executingNodeId);
        }

        public void PlayAdvance(
            GameRun run,
            float fromDay,
            float toDay,
            string arrivingNodeId,
            Action onComplete)
        {
            TimelineAxisViewState target = CreateState(
                run,
                currentDay: toDay,
                executingNodeId: arrivingNodeId);
            _axis?.PlayCue(
                TimelinePresentationCue.Advance(fromDay, toDay, arrivingNodeId, target),
                onComplete);
            if (_axis == null)
            {
                onComplete?.Invoke();
            }
        }

        public void PlayNodeCue(
            GameRun run,
            string nodeId,
            TimelinePresentationCueKind kind,
            Action onComplete)
        {
            TimelineAxisViewState target = CreateState(
                run,
                executingNodeId: kind == TimelinePresentationCueKind.TriggerStart ? nodeId : null);
            _axis?.PlayCue(TimelinePresentationCue.Node(kind, nodeId, target), onComplete);
            if (_axis == null)
            {
                onComplete?.Invoke();
            }
        }

        public void PlayMutation(
            GameRun run,
            TimelineMutationResult result,
            string executingNodeId,
            Action onComplete)
        {
            if (_axis == null || result == null || !result.Changed)
            {
                onComplete?.Invoke();
                return;
            }

            TimelineAxisViewState before = CreateState(
                run,
                result.Before,
                result.BeforeLengthDays,
                run?.CurrentDay,
                executingNodeId);
            TimelineAxisViewState after = CreateState(
                run,
                result.After,
                result.AfterLengthDays,
                run?.CurrentDay,
                executingNodeId);
            _axis.BindState(before, run, (node, go) => ConfigureNodeTip(run, node, go), animate: false);
            IReadOnlyList<TimelinePresentationCue> cues = TimelineAxisPresentationPlanner.BuildMutation(
                before,
                after,
                result.Cause == TimelineMutationCause.Skip,
                result.TargetNodeId);
            if (cues.Count == 0)
            {
                _axis.BindState(after, run, (node, go) => ConfigureNodeTip(run, node, go), animate: false);
                onComplete?.Invoke();
                return;
            }

            for (int i = 0; i < cues.Count; i++)
            {
                _axis.PlayCue(cues[i], i == cues.Count - 1 ? onComplete : null);
            }
        }

        public void CompletePresentation()
        {
            _axis?.CompletePresentation();
        }

        public bool BeginActiveItemTargeting(
            GameRun run,
            string executingNodeId,
            ItemDefinition item,
            IReadOnlyList<ActiveTarget> targets,
            Action<ActiveTarget> onConfirm,
            Action onCancel)
        {
            if (_axis == null || run == null || item == null || targets == null || onConfirm == null)
            {
                return false;
            }

            Rebuild(run, executingNodeId);
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
            tip.Bind(action.Name, desc);
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
                    "不应该出现此条信息，请联系开发者。");
                return;
            }

            cfg.BossDebuff debuff = BossService.PreviewBossDebuff(run, node);
            tip.Bind(debuff?.Name ?? string.Empty, debuff?.Desc ?? string.Empty);
        }

        private static cfg.Food PreviewBoss(GameRun run, cfg.TimelineNode node, cfg.GameAction action)
        {
            return run != null && node != null && action != null
                ? BossService.ResolveBossFood(run)
                : null;
        }

    }
}
