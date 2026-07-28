using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动选择情境下的主动道具使用上下文：
    /// 调味/铺台永久落到 <see cref="GameRun"/>；排程操作行动轴/Boss（重掷/重置/执行下一节点/加奖励节点）。
    /// 战斗专属能力（清盘/额外上菜/目标菜加减）在此不支持，返回 false。
    /// </summary>
    public sealed class ActionSelectUseContext : IActiveUseContext
    {
        private readonly WeekLoopController _weekLoop;

        public ActionSelectUseContext(GameRun run, WeekLoopController weekLoop)
        {
            Run = run;
            _weekLoop = weekLoop;
        }

        public ActiveUseContextKind ContextKind => ActiveUseContextKind.ActionSelect;

        public GameRun Run { get; }

        public IReadOnlyList<ActiveTarget> EnumerateTargets(ItemDefinition item)
        {
            if (item == null)
            {
                return Array.Empty<ActiveTarget>();
            }

            if (item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                || item.EffectType == ItemEffectTypes.TimelineExecutePast)
            {
                IReadOnlyList<cfg.TimelineNode> nodes = item.EffectType == ItemEffectTypes.TimelineExecuteFuture
                    ? TimelineService.GetFutureUntriggeredNodes(Run)
                    : TimelineService.GetPastTriggeredNodes(Run);
                var targets = new List<ActiveTarget>(nodes.Count);
                foreach (cfg.TimelineNode node in nodes)
                {
                    targets.Add(new ActiveTarget(node.Id, node.Day, targetKind: cfg.ItemTargetKind.Global));
                }

                return targets;
            }

            if (ItemActiveUsage.IsTimelineAddEffect(item.EffectType))
            {
                return EnumerateFutureDays();
            }

            if (item.EffectType == ItemEffectTypes.TimelineDeleteNode)
            {
                IReadOnlyList<cfg.TimelineNode> nodes = TimelineService.GetDeletableUnsettledNodes(Run);
                var targets = new List<ActiveTarget>(nodes.Count);
                foreach (cfg.TimelineNode node in nodes)
                {
                    targets.Add(new ActiveTarget(node.Id, node.Day, targetKind: cfg.ItemTargetKind.Global));
                }

                return targets;
            }

            switch (item.TargetKind)
            {
                case cfg.ItemTargetKind.RecipeDish:
                    return BattleUseContext.EnumerateRecipeDishes(Run);
                case cfg.ItemTargetKind.DiningTableCell:
                    // 局外没有战斗餐桌，用预览餐桌（与实战同构）枚举格子。
                    DiningTable preview = Run != null ? BattleSessionFactory.BuildTablePreview(Run) : null;
                    return BattleUseContext.EnumerateTableCells(preview);
                case cfg.ItemTargetKind.Material:
                    return BattleUseContext.EnumerateMaterials(Run);
                case cfg.ItemTargetKind.FlavorSlot:
                    return BattleUseContext.EnumerateFlavorSlots(Run);
                default:
                    return Array.Empty<ActiveTarget>();
            }
        }

        // —— 战斗专属能力：行动选择不支持 ——

        public bool ClearBoard() => false;

        public bool ExtraServe() => false;

        public bool AddPermanentScore(ActiveTarget target, float amount) => false;

        public bool DuplicateDish(ActiveTarget target, string randomKey) => false;

        public bool DestroyDish(ActiveTarget target) => false;

        public bool MultiplyScore(ActiveTarget target, float multiplier) => false;

        public bool AddCountAs(ActiveTarget target, int amount) => false;

        // —— 调味/铺台：永久落到 Run ——

        public bool AddFlavorToDish(ActiveTarget target, string flavorId)
        {
            return Run != null && Run.AddRecipeFlavor(target.Y, flavorId);
        }

        public bool RemoveFlavorFromDish(ActiveTarget target, string flavorId)
        {
            return Run != null && Run.RemoveRecipeFlavor(target.Y, flavorId);
        }

        public bool ConvertFlavorOnDish(ActiveTarget target, string toFlavorId)
        {
            return Run != null && Run.ReplaceRecipeFlavor(target.Y, toFlavorId);
        }

        public bool ConvertDishCategory(ActiveTarget target, string category)
        {
            return false;
        }

        public bool AddMaterialToCell(ActiveTarget target, string materialId)
        {
            return Run != null && Run.AddCellMaterial(new GridPos(target.X, target.Y), materialId);
        }

        public bool GenerateDish(ActiveTarget target, string dishId, string randomKey) => false;

        // —— 排程：操作行动轴/Boss ——

        public bool RerollCurrentAction()
        {
            if (Run == null)
            {
                return false;
            }

            string key = GameRun.BuildActionChoiceKey(Run.RunActionStepIndex, Run.WeekIndex, Run.CurrentDay, Run.ActionStepIndex);
            if (!Run.HasPendingActionChoices(key))
            {
                // 仅在行动选择态可重掷。
                return false;
            }

            IReadOnlyList<ActionChoice> previous = Run.GetPendingActionChoices(key);
            IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Action, key + "_reroll_" + Run.NextActiveUseKey());
            List<ActionChoice> rerolled = ActionScheduleService.RerollChoices(Run, rng, previous);
            Run.SetPendingActionChoices(key, rerolled);
            return true;
        }

        public bool ResetLastBossDebuff()
        {
            cfg.TimelineNode node = TimelineService.GetLastUntriggeredBossNode(Run);
            if (node == null)
            {
                return false;
            }

            return Run.RerollBossDebuffForNode(node.Id);
        }

        public bool ExecuteExtraTimelineNode(string nodeId)
        {
            return _weekLoop != null && _weekLoop.QueueExtraTimelineNode(nodeId);
        }

        public bool AddTimelineNode(string actionId, int day)
        {
            return Run != null && !string.IsNullOrEmpty(Run.AddRuntimeTimelineNodeAtDay(actionId, day));
        }

        public bool DeleteTimelineNode(string nodeId)
        {
            return _weekLoop != null
                ? _weekLoop.RemoveTimelineNode(nodeId)
                : Run != null && Run.RemoveRuntimeTimelineNode(nodeId);
        }

        public bool AddNextActionHalfCostStack()
        {
            if (Run == null)
            {
                return false;
            }

            Run.AddNextDailyActionHalfCostStack();
            return true;
        }

        private IReadOnlyList<ActiveTarget> EnumerateFutureDays()
        {
            var targets = new List<ActiveTarget>();
            if (Run == null)
            {
                return targets;
            }

            int start = System.Math.Max(1, (int)System.Math.Floor(Run.CurrentDay) + 1);
            int end = (int)System.Math.Floor(Run.TimelineLengthDays + TimelineMath.Epsilon);
            for (int day = start; day <= end; day++)
            {
                targets.Add(new ActiveTarget(day.ToString(), day, targetKind: cfg.ItemTargetKind.Global));
            }

            return targets;
        }
    }
}
