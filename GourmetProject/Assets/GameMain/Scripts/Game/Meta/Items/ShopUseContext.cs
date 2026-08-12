using System;
using System.Collections.Generic;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 商店/编辑界面的消耗品使用上下文。永久编辑落到 Run；排程小票操作局外核心循环。
    /// </summary>
    public sealed class ShopUseContext : IActiveUseContext
    {
        private readonly WeekLoopController _weekLoop;
        private readonly ActiveUseContextKind _contextKind;

        public ShopUseContext(
            GameRun run,
            WeekLoopController weekLoop = null,
            ActiveUseContextKind contextKind = ActiveUseContextKind.Shop)
        {
            Run = run;
            _weekLoop = weekLoop;
            _contextKind = contextKind;
        }

        public ActiveUseContextKind ContextKind => _contextKind;

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
                if (!CanCloneToCurrentOrNextIntegerDay())
                {
                    return Array.Empty<ActiveTarget>();
                }

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
                return EnumerateCurrentAndFutureDays();
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

        public bool ClearBoard() => false;

        public bool ExtraServe() => false;

        public bool AddPermanentScore(ActiveTarget target, float amount) => false;

        public bool DuplicateDish(ActiveTarget target, string randomKey) => false;

        public bool DestroyDish(ActiveTarget target) => false;

        public bool MultiplyScore(ActiveTarget target, float multiplier) => false;

        public bool AddCountAs(ActiveTarget target, int amount) => false;

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

        public bool RerollCurrentAction() => false;

        public bool ResetLastBossDebuff()
        {
            cfg.TimelineNode node = TimelineService.GetNearestUntriggeredBossNode(Run);
            if (node == null)
            {
                return false;
            }

            return Run.RerollBossDebuffForNode(node.Id);
        }

        public string CloneTimelineNodeToCurrentOrNextIntegerDay(string nodeId, string sourceItemId)
        {
            return Run?.CloneRuntimeTimelineNodeToCurrentOrNextIntegerDay(nodeId, sourceItemId)
                ?? string.Empty;
        }

        public bool AddTimelineNode(string actionId, int day)
        {
            return !string.IsNullOrEmpty(AddTimelineNodeWithId(actionId, day, string.Empty));
        }

        public string AddTimelineNodeWithId(string actionId, int day, string sourceItemId)
            => Run?.AddRuntimeTimelineNodeAtDay(actionId, day, sourceItemId) ?? string.Empty;

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

        public bool AddNextBusinessRewardDoubleStack()
        {
            if (Run == null)
            {
                return false;
            }

            Run.AddNextBusinessRewardDoubleStack();
            return true;
        }

        private IReadOnlyList<ActiveTarget> EnumerateCurrentAndFutureDays()
        {
            var targets = new List<ActiveTarget>();
            if (Run == null)
            {
                return targets;
            }

            int start = TimelineMath.CurrentOrNextIntegerDay(Run.CurrentDay);
            int end = (int)System.Math.Floor(Run.TimelineLengthDays + TimelineMath.Epsilon);
            for (int day = start; day <= end; day++)
            {
                targets.Add(new ActiveTarget(day.ToString(), day, targetKind: cfg.ItemTargetKind.Global));
            }

            return targets;
        }

        private bool CanCloneToCurrentOrNextIntegerDay()
        {
            return Run != null
                && TimelineMath.CurrentOrNextIntegerDay(Run.CurrentDay)
                    <= (int)System.Math.Floor(Run.TimelineLengthDays + TimelineMath.Epsilon);
        }
    }
}
