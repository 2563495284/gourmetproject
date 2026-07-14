using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 商店/编辑界面的主动道具使用上下文。只落地永久 Run 变更；不操作战斗餐桌与行动轴。
    /// </summary>
    public sealed class ShopUseContext : IActiveUseContext
    {
        public ShopUseContext(GameRun run)
        {
            Run = run;
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

        public bool ResetWeekBoss() => false;

        public bool ExecuteNextTimelineNode() => false;

        public bool AddRewardNodeToTimeline(string actionId) => false;
    }
}
