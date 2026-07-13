using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把当前餐桌上的风味与材质转换为结算效果。</summary>
    public sealed class FlavorMaterialEffectSource : IScoreEffectSource
    {
        private readonly FlavorEffectRegistry _registry;

        public FlavorMaterialEffectSource(FlavorEffectRegistry registry)
        {
            _registry = registry ?? FlavorEffectRegistry.CreateDefault();
        }

        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                CollectFlavor(snapshot, collector, dish);
                CollectMaterials(snapshot, collector, dish);
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

        private sealed class MaterialAggregate
        {
            public int Count;
            public int BoardOrder;
            public GridPos Cell;
        }

        private void CollectMaterials(ScoreSnapshot snapshot, ScoreEffectCollector collector, DishInstance dish)
        {
            // 按「食物×材质」聚合该菜占据的格：材质 id -> 占格数 + 最靠上左的格（决定该材质的结算次序与明细定位）。
            var byMaterial = new Dictionary<string, MaterialAggregate>();
            foreach (GridPos cell in dish.OccupiedCells)
            {
                int boardOrder = BoardOrder(snapshot, cell);
                foreach (string materialId in snapshot.DiningTable.MaterialsAt(cell))
                {
                    if (string.IsNullOrEmpty(materialId))
                    {
                        continue;
                    }

                    if (!byMaterial.TryGetValue(materialId, out MaterialAggregate agg))
                    {
                        byMaterial[materialId] = new MaterialAggregate { Count = 1, BoardOrder = boardOrder, Cell = cell };
                    }
                    else
                    {
                        agg.Count++;
                        if (boardOrder < agg.BoardOrder)
                        {
                            agg.BoardOrder = boardOrder;
                            agg.Cell = cell;
                        }
                    }
                }
            }

            // 不同材质按从上到下、从左到右（最靠上左格）依次结算。
            foreach (KeyValuePair<string, MaterialAggregate> kv in byMaterial.OrderBy(e => e.Value.BoardOrder))
            {
                MaterialDef material = snapshot.Db.GetMaterial(kv.Key);
                if (material == null)
                {
                    continue;
                }

                MaterialAggregate agg = kv.Value;
                collector.Add(new ScoreEffectEntry(
                    ScorePhase.Materials,
                    ScoreSource.Material(material, dish, agg.Cell),
                    new MaterialEffect(material, agg.Count),
                    dish,
                    material,
                    agg.Cell,
                    0,
                    agg.BoardOrder));
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
