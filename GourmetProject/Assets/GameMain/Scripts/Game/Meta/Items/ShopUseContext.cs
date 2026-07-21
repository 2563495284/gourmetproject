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
    /// 商店/编辑界面的主动道具使用上下文。永久编辑落到 Run；排程小票操作局外核心循环。
    /// </summary>
    public sealed class ShopUseContext : IActiveUseContext
    {
        private readonly WeekLoopController _weekLoop;

        public ShopUseContext(GameRun run, WeekLoopController weekLoop = null)
        {
            Run = run;
            _weekLoop = weekLoop;
        }

        public ActiveUseContextKind ContextKind => ActiveUseContextKind.Shop;

        public GameRun Run { get; }

        public IReadOnlyList<ActiveTarget> EnumerateTargets(cfg.ItemTargetKind targetKind)
        {
            switch (targetKind)
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
            return Run != null && Run.AddRecipeFlavor(target.X, target.Y, flavorId);
        }

        public bool RemoveFlavorFromDish(ActiveTarget target, string flavorId)
        {
            return Run != null && Run.RemoveRecipeFlavor(target.X, target.Y, flavorId);
        }

        public bool ConvertFlavorOnDish(ActiveTarget target, string toFlavorId)
        {
            return Run != null && Run.ReplaceRecipeFlavor(target.X, target.Y, toFlavorId);
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

        public bool ResetWeekBoss()
        {
            if (Run == null)
            {
                return false;
            }

            Run.RerollBossDebuffForCurrentWeek();
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

            IRandomStream rng = GameApp.Random?.DomainStream(SeedDomains.Item, $"timeline_add_{actionId}_{Run.NextActiveUseKey()}");
            return !string.IsNullOrEmpty(Run.AddRuntimeTimelineNode(actionId, rng));
        }
    }
}
