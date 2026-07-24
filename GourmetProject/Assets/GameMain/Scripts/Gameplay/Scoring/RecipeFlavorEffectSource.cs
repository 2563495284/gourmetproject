using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 酸/咸「未上菜菜谱结算」来源：结算开始时（<see cref="ScorePhase.BeforeAll"/>），
    /// 遍历仍未上菜的菜谱条目，若某未上菜菜带酸/咸风味，则作用于场上全部参与结算的食物：
    /// 酸 → 每个场上食物倍率 ×EffectValue（1.5）；咸 → 每个场上食物基础分 +EffectValue（20）。
    /// 多个未上菜酸/咸条目各触发一次；遍历顺序为菜谱从左到右（槽索引升序），
    /// 同槽内从大到小（占格数降序）。
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

            foreach ((UnservedRecipeDish _, DishDef _, FlavorDef flavor) in ordered)
            {
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.BeforeAll,
                    ScoreSource.DishFlavor(flavor, null),
                    new RecipeFlavorEffect(flavor),
                    dish: null));
            }
        }

        private static bool IsRecipeFlavor(FlavorEffectType type)
        {
            return type == FlavorEffectType.SourRecipeMult || type == FlavorEffectType.SaltyRecipeFlat;
        }
    }

    /// <summary>单条未上菜酸/咸对场上全部参与结算食物的作用。</summary>
    public sealed class RecipeFlavorEffect : IScoreEffect
    {
        private readonly FlavorDef _flavor;

        public RecipeFlavorEffect(FlavorDef flavor)
        {
            _flavor = flavor;
        }

        public void Apply(ScoreContext ctx)
        {
            if (_flavor == null)
            {
                return;
            }

            List<DishInstance> served = ctx.Snapshot.DishesInDefaultOrder
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
                    case FlavorEffectType.SaltyRecipeFlat:
                        ctx.AddFlatTo(dish, _flavor.EffectValue);
                        break;
                }
            }
        }
    }
}
