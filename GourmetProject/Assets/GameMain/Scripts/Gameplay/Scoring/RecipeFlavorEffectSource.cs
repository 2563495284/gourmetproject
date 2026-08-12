using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 酸「未上菜食谱结算」来源：结算开始时遍历仍未上菜的食谱条目；
    /// 每一层酸都使场上全部参与结算的食物倍率 +EffectValue。
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

            IEnumerable<(UnservedRecipeDish Entry, DishDef Def)> orderedEntries = unserved
                .Select(u => (Entry: u, Def: snapshot.Db.GetDish(u.DishId)))
                .Where(x => x.Def != null)
                .OrderBy(x => x.Entry.SlotIndex)
                .ThenByDescending(x => x.Def.Shape.CellCount)
                .ThenBy(x => x.Entry.DishId);

            foreach ((UnservedRecipeDish entry, DishDef _) in orderedEntries)
            {
                foreach (string flavorId in entry.FlavorIds)
                {
                    FlavorDef flavor = snapshot.Db.GetFlavor(flavorId);
                    if (flavor?.EffectType != FlavorEffectType.RecipeAddMultFlat)
                    {
                        continue;
                    }

                    collector.Add(new ScoreEffectEntry(
                        ScorePhase.BeforeAll,
                        ScoreSource.DishFlavor(flavor, null),
                        new RecipeAddMultFlatEffect(flavor),
                        dish: null));
                }
            }
        }
    }

    public sealed class RecipeAddMultFlatEffect : IScoreEffect
    {
        private readonly FlavorDef _flavor;

        public RecipeAddMultFlatEffect(FlavorDef flavor)
        {
            _flavor = flavor;
        }

        public void Apply(ScoreContext ctx)
        {
            if (_flavor == null)
            {
                return;
            }

            foreach (DishInstance dish in ctx.Snapshot.DishesInDefaultOrder)
            {
                ctx.AddMultFlatTo(dish, _flavor.EffectValue);
            }
        }
    }
}
