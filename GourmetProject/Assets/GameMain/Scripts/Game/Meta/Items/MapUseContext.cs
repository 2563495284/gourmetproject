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
    /// 地图（局外核心循环）情境下的主动道具使用上下文：
    /// 调味/铺台永久落到 <see cref="GameRun"/>；排程操作行动轴/Boss（重掷/重置/执行下一节点/加奖励节点）。
    /// 战斗专属能力（清盘/额外上菜/目标菜加减）在此不支持，返回 false。
    /// </summary>
    public sealed class MapUseContext : IActiveUseContext
    {
        private readonly WeekLoopController _weekLoop;

        public MapUseContext(GameRun run, WeekLoopController weekLoop)
        {
            Run = run;
            _weekLoop = weekLoop;
        }

        public ActiveUseContextKind ContextKind => ActiveUseContextKind.Map;

        public GameRun Run { get; }

        public IReadOnlyList<ActiveTarget> EnumerateTargets(cfg.ItemTargetKind targetKind)
        {
            switch (targetKind)
            {
                case cfg.ItemTargetKind.RecipeDish:
                    return BattleUseContext.EnumerateRecipeDishes(Run);
                case cfg.ItemTargetKind.DiningTableCell:
                    // 局外没有战斗餐桌，用预览餐桌（与实战同构）枚举格子。
                    DiningTable preview = Run != null ? BattleSessionFactory.BuildTablePreview(Run) : null;
                    return BattleUseContext.EnumerateTableCells(preview);
                default:
                    return Array.Empty<ActiveTarget>();
            }
        }

        // —— 战斗专属能力：地图不支持 ——

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
            return Run != null && Run.AddRecipeFlavor(target.X, target.Y, flavorId);
        }

        public bool AddMaterialToCell(ActiveTarget target, string materialId)
        {
            return Run != null && Run.AddCellMaterial(new GridPos(target.X, target.Y), materialId);
        }

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

        public bool ResetWeekBoss()
        {
            if (Run == null)
            {
                return false;
            }

            Run.ResetBossDebuffRollHistory();
            return true;
        }

        public bool ExecuteNextTimelineNode()
        {
            return _weekLoop != null && _weekLoop.ForceExecuteNextTimelineNode();
        }

        public bool AddRewardNodeToTimeline(string actionId)
        {
            if (Run == null || string.IsNullOrEmpty(actionId))
            {
                return false;
            }

            // 放在当前天数游标之后最近的整天，随天数推进被 CollectPassedNodes 触发。
            int day = (int)Math.Ceiling(Run.CurrentDay + 0.001f);
            int maxDay = (int)Math.Floor(Run.TimelineLengthDays);
            if (day > maxDay)
            {
                return false;
            }

            return !string.IsNullOrEmpty(Run.AddRuntimeTimelineNode(day, actionId));
        }
    }
}
