using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>把当前棋盘上的菜品技能、风味与格子标签转换为结算效果。</summary>
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
                CollectSkills(snapshot, collector, dish);
                CollectFlavor(snapshot, collector, dish);
                CollectCellTags(snapshot, collector, dish);
            }
        }

        private void CollectSkills(ScoreSnapshot snapshot, ScoreEffectCollector collector, DishInstance dish)
        {
            int boardOrder = BoardOrder(snapshot, dish.Placement.Origin);
            foreach (string skillId in dish.SkillIds)
            {
                IEffectDef skill = ResolveSkill(snapshot, skillId, out IScoreEffect effect);
                if (skill == null)
                {
                    continue;
                }

                collector.Add(new ScoreEffectEntry(
                    ScorePhase.DishSkills,
                    ScoreSource.DishSkill(skill, dish),
                    effect,
                    dish,
                    skill,
                    null,
                    0,
                    boardOrder));
            }
        }

        private void CollectFlavor(ScoreSnapshot snapshot, ScoreEffectCollector collector, DishInstance dish)
        {
            if (!dish.HasFlavor)
            {
                return;
            }

            IEffectDef flavor = ResolveFlavor(snapshot, dish.FlavorId, out IScoreEffect effect);
            if (flavor == null)
            {
                return;
            }

            int boardOrder = BoardOrder(snapshot, dish.Placement.Origin);
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
                    IEffectDef cellTag = ResolveCell(snapshot, tagId, out IScoreEffect effect);
                    if (cellTag == null)
                    {
                        continue;
                    }

                    collector.Add(new ScoreEffectEntry(
                        ScorePhase.CellTags,
                        ScoreSource.CellTag(cellTag, dish, cell),
                        effect,
                        dish,
                        cellTag,
                        cell,
                        0,
                        boardOrder));
                }
            }
        }

        private IEffectDef ResolveSkill(ScoreSnapshot snapshot, string id, out IScoreEffect effect)
        {
            return Resolve(snapshot.Db.GetSkill(id), out effect);
        }

        private IEffectDef ResolveFlavor(ScoreSnapshot snapshot, string id, out IScoreEffect effect)
        {
            return Resolve(snapshot.Db.GetFlavor(id), out effect);
        }

        private IEffectDef ResolveCell(ScoreSnapshot snapshot, string id, out IScoreEffect effect)
        {
            return Resolve(snapshot.Db.GetCellTag(id), out effect);
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
            return cell.Y * snapshot.Board.Width + cell.X;
        }
    }
}
