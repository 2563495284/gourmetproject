using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 酸/咸「未上菜菜谱结算」来源：整体结算末尾（<see cref="ScorePhase.AfterAllDishes"/>、分数汇总前），
    /// 遍历仍未上菜的菜谱条目，若某未上菜菜带酸/咸风味，则作用于场上（已上菜）同菜谱槽的食物：
    /// 酸 → 该槽每个场上食物倍率 ×EffectValue（1.5）；咸 → 该槽每个场上食物 +EffectValue 金币（2）。
    /// 遍历顺序：菜谱从左到右（槽索引升序），同槽内从大到小（占格数降序）。
    /// </summary>
    public sealed class RecipeFlavorEffectSource : IScoreEffectSource
    {
        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            IReadOnlyList<UnservedRecipeDish> unserved = snapshot.UnservedRecipeDishes;
            if (unserved == null || unserved.Count == 0)
            {
                return;
            }

            IEnumerable<(UnservedRecipeDish Entry, DishDef Def, FlavorDef Flavor)> ordered = unserved
                .Select(u => (Entry: u, Def: snapshot.Db.GetDish(u.DishId)))
                .Where(x => x.Def != null)
                .Select(x => (x.Entry, x.Def, Flavor: snapshot.Db.GetFlavor(x.Def.FlavorId)))
                .Where(x => x.Flavor != null && IsRecipeFlavor(x.Flavor.EffectType))
                .OrderBy(x => x.Entry.SlotIndex)
                .ThenByDescending(x => x.Def.Shape.CellCount)
                .ThenBy(x => x.Entry.DishId);

            foreach ((UnservedRecipeDish entry, DishDef _, FlavorDef flavor) in ordered)
            {
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.AfterAllDishes,
                    ScoreSource.DishFlavor(flavor, null),
                    new RecipeFlavorEffect(flavor, entry.SlotIndex),
                    dish: null));
            }
        }

        private static bool IsRecipeFlavor(FlavorEffectType type)
        {
            return type == FlavorEffectType.SourRecipeMult || type == FlavorEffectType.SaltyRecipeGold;
        }
    }

    /// <summary>单条未上菜酸/咸对场上同菜谱食物的作用。</summary>
    public sealed class RecipeFlavorEffect : IScoreEffect
    {
        private readonly FlavorDef _flavor;
        private readonly int _slotIndex;

        public RecipeFlavorEffect(FlavorDef flavor, int slotIndex)
        {
            _flavor = flavor;
            _slotIndex = slotIndex;
        }

        public void Apply(ScoreContext ctx)
        {
            if (_flavor == null)
            {
                return;
            }

            List<DishInstance> served = ctx.DiningTable.Dishes
                .Where(d => !d.ExcludedFromScore && d.SourceSlotIndex == _slotIndex)
                .OrderBy(d => d.Placement.Origin.Y)
                .ThenBy(d => d.Placement.Origin.X)
                .ThenBy(d => d.Id)
                .ToList();

            foreach (DishInstance dish in served)
            {
                switch (_flavor.EffectType)
                {
                    case FlavorEffectType.SourRecipeMult:
                        ctx.MultiplyTo(dish, _flavor.EffectValue);
                        break;
                    case FlavorEffectType.SaltyRecipeGold:
                        ctx.GrantGold(_flavor.EffectValue);
                        break;
                }
            }
        }
    }
}
