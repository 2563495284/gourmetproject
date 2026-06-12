using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 「吃」的结算器：先按棋盘行优先（左上→右下）顺序逐菜结算标签（技能），
    /// 再把各菜贡献汇总，最后施加局级修正（道具/Buff）。
    /// </summary>
    public sealed class ScoreCalculator
    {
        private readonly TagEffectRegistry _registry;

        public ScoreCalculator(TagEffectRegistry registry = null)
        {
            _registry = registry ?? TagEffectRegistry.CreateDefault();
        }

        public ScoreResult Calculate(GpBoard board, GameplayDatabase db, float finalFlat = 0f, float finalMultiplier = 1f)
        {
            var dishScores = new List<DishScore>();
            float rawSum = 0f;

            foreach (DishInstance dish in OrderedDishes(board))
            {
                var ctx = new EffectContext(board, db, dish);
                foreach (string tagId in dish.TagIds)
                {
                    TagDef tag = db.GetTag(tagId);
                    if (tag == null)
                    {
                        continue;
                    }

                    ITagEffect effect = _registry.Get(tag.EffectType);
                    if (effect == null)
                    {
                        continue;
                    }

                    ctx.Tag = tag;
                    effect.Apply(ctx);
                }

                var score = new DishScore(dish.Id, dish.Def.Id, dish.Def.Deliciousness, ctx.FlatBonus, ctx.Multiplier);
                dishScores.Add(score);
                rawSum += score.Contribution;
            }

            return new ScoreResult(dishScores, rawSum, finalFlat, finalMultiplier);
        }

        /// <summary>结算顺序：行优先（先 Y 后 X），并以实例 Id 兜底保证稳定。</summary>
        private static IEnumerable<DishInstance> OrderedDishes(GpBoard board)
        {
            return board.Dishes
                .OrderBy(d => d.Placement.Origin.Y)
                .ThenBy(d => d.Placement.Origin.X)
                .ThenBy(d => d.Id);
        }
    }
}
