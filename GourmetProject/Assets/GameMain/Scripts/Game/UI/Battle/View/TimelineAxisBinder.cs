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
    /// 顶部时间轴状态绑定 + 每个时间线节点的 hover Tip 装配。
    /// Tip 视图实例由外层惰性创建后通过 getter 注入，Boss 预览用独立 RNG 快照避免污染随机流。
    /// </summary>
    internal sealed class TimelineAxisBinder
    {
        private readonly TimelineAxisView _axis;
        private readonly Func<TimelineNodeTipView> _timelineTip;
        private float? _presentationDay;

        public TimelineAxisBinder(
            TimelineAxisView axis,
            Func<TimelineNodeTipView> timelineTip)
        {
            _axis = axis;
            _timelineTip = timelineTip;
        }

        public void Rebuild(GameRun run, string executingNodeId = null)
        {
            if (_axis == null)
            {
                return;
            }

            _axis.SetNodeDecorator((state, go) =>
                ConfigureNodeTip(run, TimelineService.GetNode(run, state.Id), go));
            _axis.Render(TimelineAxisStateFactory.Create(
                run,
                currentDay: _presentationDay,
                executingNodeId: executingNodeId));
        }

        public void BeginAdvanceSequence(float fromDay)
        {
            _presentationDay = Mathf.Max(0f, fromDay);
        }

        public void EndAdvanceSequence()
        {
            _presentationDay = null;
        }

        public void BuildPresentation(
            GameRun run,
            IReadOnlyList<RuntimeTimelineNodeSnapshot> nodes,
            float lengthDays,
            string executingNodeId,
            bool animate)
        {
            if (_axis == null)
            {
                return;
            }

            _axis.SetNodeDecorator((state, go) =>
                ConfigureNodeTip(run, TimelineService.GetNode(run, state.Id), go));
            _axis.Render(
                TimelineAxisStateFactory.Create(
                    run,
                    nodes,
                    lengthDays,
                    run?.CurrentDay,
                    executingNodeId),
                animate);
        }

        public TimelineAxisViewState CreateState(
            GameRun run,
            IReadOnlyList<RuntimeTimelineNodeSnapshot> nodes = null,
            float? lengthDays = null,
            float? currentDay = null,
            string executingNodeId = null)
        {
            return TimelineAxisStateFactory.Create(
                run,
                nodes,
                lengthDays,
                currentDay,
                executingNodeId);
        }

        public void PlayAdvance(
            GameRun run,
            float fromDay,
            float toDay,
            string arrivingNodeId,
            Action onComplete)
        {
            _presentationDay = Mathf.Max(0f, toDay);
            TimelineAxisViewState target = CreateState(
                run,
                currentDay: toDay,
                executingNodeId: arrivingNodeId);
            _axis?.Play(
                TimelineAxisPresentationPlan.Single(
                    TimelinePresentationCue.Advance(fromDay, toDay, arrivingNodeId, target)),
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
                currentDay: _presentationDay,
                executingNodeId: kind == TimelinePresentationCueKind.TriggerStart ? nodeId : null);
            _axis?.Play(
                TimelineAxisPresentationPlan.Single(
                    TimelinePresentationCue.Node(kind, nodeId, target)),
                onComplete);
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
                _presentationDay ?? run?.CurrentDay,
                executingNodeId);
            TimelineAxisViewState after = CreateState(
                run,
                result.After,
                result.AfterLengthDays,
                _presentationDay ?? run?.CurrentDay,
                executingNodeId);
            _axis.SetNodeDecorator((state, go) =>
                ConfigureNodeTip(run, TimelineService.GetNode(run, state.Id), go));
            _axis.Render(before, animate: false);
            TimelineAxisPresentationPlan plan = TimelineAxisPresentationPlanner.BuildMutation(
                before,
                after,
                result.Cause == TimelineMutationCause.Skip,
                result.TargetNodeId);
            if (plan.IsEmpty)
            {
                _axis.Render(after, animate: false);
                onComplete?.Invoke();
                return;
            }

            _axis.Play(plan, onComplete);
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

                TimelineAxisNodeState preview = TimelineAxisStateFactory.CreatePreviewNode(
                    run,
                    item.EffectParam);
                return _axis.BeginSelection(TimelineAxisSelectionRequest.AddDay(
                    preview,
                    days,
                    day => onConfirm(new ActiveTarget(
                        day.ToString(),
                        day,
                        targetKind: cfg.ItemTargetKind.Global)),
                    onCancel));
            }

            if (item.EffectType == ItemEffectTypes.TimelineDeleteNode)
            {
                return _axis.BeginSelection(TimelineAxisSelectionRequest.Nodes(
                    TimelineAxisSelectionMode.DeleteNode,
                    TargetIds(targets),
                    nodeId => onConfirm(new ActiveTarget(
                        nodeId,
                        targetKind: cfg.ItemTargetKind.Global)),
                    onCancel));
            }

            if (item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                || item.EffectType == ItemEffectTypes.TimelineExecutePast)
            {
                return _axis.BeginSelection(TimelineAxisSelectionRequest.Nodes(
                    TimelineAxisSelectionMode.ExecuteNode,
                    TargetIds(targets),
                    nodeId => onConfirm(new ActiveTarget(
                        nodeId,
                        targetKind: cfg.ItemTargetKind.Global)),
                    onCancel));
            }

            return false;
        }

        public void PlayBossDebuffRerollTip(
            string nodeId,
            string oldTitle,
            string oldDesc,
            string newTitle,
            string newDesc)
        {
            TimelineNodeTipView tip = _timelineTip?.Invoke();
            if (tip == null
                || string.IsNullOrEmpty(nodeId)
                || _axis == null
                || !_axis.TryGetNodeBubble(nodeId, out TimelineNodeBubbleView bubble)
                || bubble == null)
            {
                return;
            }

            TipHoverTrigger trigger = bubble.GetComponent<TipHoverTrigger>();
            tip.PlayRerollTexts(
                oldTitle,
                oldDesc,
                newTitle,
                newDesc,
                () => trigger?.EndForcedShow());
            trigger?.BeginForcedShow(bubble.Rect);
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

        public bool CommitActiveItemAddPreview(string nodeId)
        {
            return _axis != null && _axis.PromotePreview(nodeId);
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
