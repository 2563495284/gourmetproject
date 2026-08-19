using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把当前餐桌上的风味转换为结算效果。</summary>
    public sealed class FlavorEffectSource : IScoreEffectSource
    {
        private readonly FlavorEffectRegistry _registry;

        public FlavorEffectSource(FlavorEffectRegistry registry)
        {
            _registry = registry ?? FlavorEffectRegistry.CreateDefault();
        }

        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                CollectFlavor(snapshot, collector, dish);
            }
        }

        private void CollectFlavor(ScoreSnapshot snapshot, ScoreEffectCollector collector, DishInstance dish)
        {
            if (!dish.HasFlavor)
            {
                return;
            }

            int boardOrder = BoardOrder(snapshot, dish.Placement.Origin);
            foreach (string flavorId in dish.FlavorIds)
            {
                IEffectDef flavor = ResolveFlavor(snapshot, flavorId, out IScoreEffect effect);
                if (flavor == null)
                {
                    continue;
                }

                collector.Add(new ScoreEffectEntry(
                    ScorePhase.DishFlavor,
                    ScoreSource.DishFlavor(flavor, dish),
                    effect,
                    dish,
                    flavor,
                    null,
                    0,
                    boardOrder));
            }
        }

        private IEffectDef ResolveFlavor(ScoreSnapshot snapshot, string id, out IScoreEffect effect)
        {
            return Resolve(snapshot.Db.GetFlavor(id), out effect);
        }

        private IEffectDef Resolve(IEffectDef def, out IScoreEffect effect)
        {
            effect = null;
            if (def == null)
            {
                return null;
            }

            effect = _registry.Get(def.EffectType);
            return effect == null ? null : def;
        }

        private static int BoardOrder(ScoreSnapshot snapshot, GridPos cell)
        {
            return cell.Y * snapshot.DiningTable.Width + cell.X;
        }
    }
}
