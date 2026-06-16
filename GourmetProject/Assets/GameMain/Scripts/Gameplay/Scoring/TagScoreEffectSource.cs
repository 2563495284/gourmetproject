using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把当前棋盘上的食品标签与格子标签转换为结算效果。</summary>
    public sealed class TagScoreEffectSource : IScoreEffectSource
    {
        private readonly TagEffectRegistry _registry;

        public TagScoreEffectSource(TagEffectRegistry registry)
        {
            _registry = registry ?? TagEffectRegistry.CreateDefault();
        }

        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                CollectDishTags(snapshot, collector, dish);
                CollectCellTags(snapshot, collector, dish);
            }
        }

        private void CollectDishTags(ScoreSnapshot snapshot, ScoreEffectCollector collector, DishInstance dish)
        {
            int boardOrder = BoardOrder(snapshot, dish.Placement.Origin);
            foreach (string tagId in dish.TagIds)
            {
                TagDef tag = ResolveTag(snapshot, tagId, out IScoreEffect effect);
                if (tag == null)
                {
                    continue;
                }

                collector.Add(new ScoreEffectEntry(
                    ScorePhase.DishTags,
                    ScoreSource.DishTag(tag, dish),
                    effect,
                    dish,
                    tag,
                    null,
                    0,
                    boardOrder));
            }
        }

        private void CollectCellTags(ScoreSnapshot snapshot, ScoreEffectCollector collector, DishInstance dish)
        {
            IEnumerable<GridPos> cells = dish.OccupiedCells
                .OrderBy(c => c.Y)
                .ThenBy(c => c.X);

            foreach (GridPos cell in cells)
            {
                int boardOrder = BoardOrder(snapshot, cell);
                foreach (string tagId in snapshot.Board.TagsAt(cell))
                {
                    TagDef tag = ResolveTag(snapshot, tagId, out IScoreEffect effect);
                    if (tag == null)
                    {
                        continue;
                    }

                    collector.Add(new ScoreEffectEntry(
                        ScorePhase.CellTags,
                        ScoreSource.CellTag(tag, dish, cell),
                        effect,
                        dish,
                        tag,
                        cell,
                        0,
                        boardOrder));
                }
            }
        }

        private TagDef ResolveTag(ScoreSnapshot snapshot, string tagId, out IScoreEffect effect)
        {
            effect = null;
            if (string.IsNullOrEmpty(tagId))
            {
                return null;
            }

            TagDef tag = snapshot.Db.GetTag(tagId);
            if (tag == null)
            {
                return null;
            }

            effect = _registry.Get(tag.EffectType);
            return effect == null ? null : tag;
        }

        private static int BoardOrder(ScoreSnapshot snapshot, GridPos cell)
        {
            return cell.Y * snapshot.Board.Width + cell.X;
        }
    }
}
