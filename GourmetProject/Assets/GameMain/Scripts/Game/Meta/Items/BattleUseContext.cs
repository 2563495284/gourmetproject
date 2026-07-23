using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Game.Meta
{
    /// <summary>战斗情境下的主动道具使用上下文：能力落到 <see cref="BattleSession"/>。</summary>
    public sealed class BattleUseContext : IActiveUseContext
    {
        private readonly BattleSession _session;

        public BattleUseContext(BattleSession session, GameRun run)
        {
            _session = session;
            Run = run;
        }

        public ActiveUseContextKind ContextKind => ActiveUseContextKind.Battle;

        public GameRun Run { get; }

        public IReadOnlyList<ActiveTarget> EnumerateTargets(cfg.ItemTargetKind targetKind)
        {
            switch (targetKind)
            {
                case cfg.ItemTargetKind.RecipeDish:
                    return EnumerateRecipeDishes(Run);
                case cfg.ItemTargetKind.DiningTableCell:
                    return EnumerateTableCells(_session?.DiningTable);
                case cfg.ItemTargetKind.DiningTableDish:
                    return EnumerateTableDishes(_session?.DiningTable);
                case cfg.ItemTargetKind.Material:
                    return EnumerateMaterials(Run);
                case cfg.ItemTargetKind.FlavorSlot:
                    return EnumerateFlavorSlots(Run);
                default:
                    return Array.Empty<ActiveTarget>();
            }
        }

        public bool ClearBoard()
        {
            if (_session == null || _session.IsSettled || _session.DiningTable.DishCount <= 0)
            {
                return false;
            }

            _session.ClearBoard();
            return true;
        }

        public bool ExtraServe()
        {
            if (_session == null || _session.IsSettled)
            {
                return false;
            }

            for (int i = 0; i < _session.Slots.Count; i++)
            {
                if (_session.Serve(i).Success)
                {
                    return true;
                }
            }

            return false;
        }

        public bool AddPermanentScore(ActiveTarget target, float amount)
        {
            return _session != null && TryGetDishId(target, out int dishId)
                && _session.AddPermanentScoreToDish(dishId, amount);
        }

        public bool DuplicateDish(ActiveTarget target, string randomKey)
        {
            // 战斗内复制沿用战斗随机流选空位；randomKey 供局外情境派生随机，这里无需使用。
            return _session != null && TryGetDishId(target, out int dishId)
                && _session.DuplicateDishById(dishId);
        }

        public bool DestroyDish(ActiveTarget target)
        {
            return _session != null && TryGetDishId(target, out int dishId)
                && _session.DestroyDishById(dishId);
        }

        public bool MultiplyScore(ActiveTarget target, float multiplier)
        {
            return _session != null && TryGetDishId(target, out int dishId)
                && _session.MultiplyScoreOnDish(dishId, multiplier);
        }

        public bool AddCountAs(ActiveTarget target, int amount)
        {
            return _session != null && TryGetDishId(target, out int dishId)
                && _session.AddCountAsToDish(dishId, amount);
        }

        // —— 调味/铺台：永久落到 Run（战斗内使用也是永久生效，下一局起随菜谱/餐桌带入）——

        public bool AddFlavorToDish(ActiveTarget target, string flavorId)
        {
            if (target.TargetKind == cfg.ItemTargetKind.DiningTableDish)
            {
                return _session != null && TryGetDishId(target, out int dishId)
                    && _session.AddFlavorToDishById(dishId, flavorId);
            }

            return Run != null && Run.AddRecipeFlavor(target.Y, flavorId);
        }

        public bool RemoveFlavorFromDish(ActiveTarget target, string flavorId)
        {
            if (target.TargetKind == cfg.ItemTargetKind.DiningTableDish)
            {
                return _session != null && TryGetDishId(target, out int dishId)
                    && _session.RemoveFlavorFromDishById(dishId, flavorId);
            }

            return Run != null && Run.RemoveRecipeFlavor(target.Y, flavorId);
        }

        public bool ConvertFlavorOnDish(ActiveTarget target, string toFlavorId)
        {
            if (target.TargetKind == cfg.ItemTargetKind.DiningTableDish)
            {
                return _session != null && TryGetDishId(target, out int dishId)
                    && _session.ReplaceFlavorOnDishById(dishId, toFlavorId);
            }

            return Run != null && Run.ReplaceRecipeFlavor(target.Y, toFlavorId);
        }

        public bool ConvertDishCategory(ActiveTarget target, string category)
        {
            // 当前 DishDef 分类是静态只读数据；分类转换需要引入运行时菜品覆盖后才能可靠落地。
            return false;
        }

        public bool AddMaterialToCell(ActiveTarget target, string materialId)
        {
            return Run != null && Run.AddCellMaterial(new GridPos(target.X, target.Y), materialId);
        }

        public bool GenerateDish(ActiveTarget target, string dishId, string randomKey)
        {
            return _session != null && _session.GenerateDishAt(dishId, new GridPos(target.X, target.Y));
        }

        // —— 排程：战斗内不支持操作行动轴/Boss ——

        public bool RerollCurrentAction() => false;

        public bool ResetWeekBoss() => false;

        public bool ExecuteNextTimelineNode() => false;

        public bool AddRewardNodeToTimeline(string actionId) => false;

        /// <summary>餐桌菜目标的 <see cref="ActiveTarget.Id"/> 为 <see cref="Gameplay.Board.DishInstance.Id"/> 的字符串形式。</summary>
        private static bool TryGetDishId(ActiveTarget target, out int dishId)
        {
            return int.TryParse(target.Id, out dishId);
        }

        /// <summary>枚举菜谱所有条目为候选：Id=dishId，X 固定为 0，Y=菜序（供选目标 UI 与效果定位）。</summary>
        internal static IReadOnlyList<ActiveTarget> EnumerateRecipeDishes(GameRun run)
        {
            var targets = new List<ActiveTarget>();
            if (run == null)
            {
                return targets;
            }

            IReadOnlyList<string> dishes = run.RecipeDishes;
            for (int dish = 0; dish < dishes.Count; dish++)
            {
                targets.Add(new ActiveTarget(dishes[dish], 0, dish, cfg.ItemTargetKind.RecipeDish));
            }

            return targets;
        }

        /// <summary>枚举餐桌所有存在格为候选：X/Y=格坐标（供选目标 UI 与效果定位）。</summary>
        internal static IReadOnlyList<ActiveTarget> EnumerateTableCells(DiningTable table)
        {
            var targets = new List<ActiveTarget>();
            if (table == null)
            {
                return targets;
            }

            foreach (GridPos cell in table.ExistingCells())
            {
                targets.Add(new ActiveTarget(string.Empty, cell.X, cell.Y, cfg.ItemTargetKind.DiningTableCell));
            }

            return targets;
        }

        internal static IReadOnlyList<ActiveTarget> EnumerateTableDishes(DiningTable table)
        {
            var targets = new List<ActiveTarget>();
            if (table == null)
            {
                return targets;
            }

            foreach (var dish in table.Dishes)
            {
                GridPos origin = dish.Placement.Origin;
                targets.Add(new ActiveTarget(dish.Id.ToString(), origin.X, origin.Y, cfg.ItemTargetKind.DiningTableDish));
            }

            return targets;
        }

        internal static IReadOnlyList<ActiveTarget> EnumerateMaterials(GameRun run)
        {
            var targets = new List<ActiveTarget>();
            if (run?.Database?.AllMaterials == null)
            {
                return targets;
            }

            foreach (var material in run.Database.AllMaterials)
            {
                if (material != null)
                {
                    targets.Add(new ActiveTarget(material.Id, targetKind: cfg.ItemTargetKind.Material));
                }
            }

            return targets;
        }

        internal static IReadOnlyList<ActiveTarget> EnumerateFlavorSlots(GameRun run)
        {
            var targets = new List<ActiveTarget>();
            if (run == null)
            {
                return targets;
            }

            IReadOnlyList<RecipeBookSlot> dishes = run.RecipeEntries;
            for (int dish = 0; dish < dishes.Count; dish++)
            {
                RecipeBookSlot slot = dishes[dish];
                if (slot == null)
                {
                    continue;
                }

                IReadOnlyList<string> flavors = slot.ExtraFlavorIds;
                if (flavors.Count == 0)
                {
                    targets.Add(new ActiveTarget(string.Empty, 0, dish, cfg.ItemTargetKind.FlavorSlot));
                    continue;
                }

                for (int i = 0; i < flavors.Count; i++)
                {
                    targets.Add(new ActiveTarget(flavors[i], 0, dish, cfg.ItemTargetKind.FlavorSlot));
                }
            }

            return targets;
        }
    }
}
