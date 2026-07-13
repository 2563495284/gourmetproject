using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 「吃」的结算器：把菜品技能、风味、材质以及额外来源收集为阶段化效果队列，
    /// 再按确定性顺序执行并输出可解释明细。
    /// </summary>
    public sealed class ScoreCalculator
    {
        private readonly FlavorEffectRegistry _registry;
        private readonly IScoreEffectSource[] _effectSources;

        public ScoreCalculator(FlavorEffectRegistry registry = null, IEnumerable<IScoreEffectSource> effectSources = null)
        {
            _registry = registry ?? FlavorEffectRegistry.CreateDefault();
            _effectSources = (effectSources ?? Array.Empty<IScoreEffectSource>()).ToArray();
        }

        public ScoreResult Calculate(
            GpTable board,
            GameplayDatabase db,
            float finalFlat = 0f,
            float finalMultiplier = 1f,
            IEnumerable<IScoreEffectSource> extraSources = null,
            IScoreHistory history = null,
            int initialHappyCakeLayers = 0,
            int extraCountAsPerDish = 0,
            int cakeLayerThresholdReduction = 0,
            bool reverseDishOrder = false,
            IReadOnlyList<UnservedRecipeDish> unservedRecipeDishes = null)
        {
            IScoreEffectSource[] sources = MergeSources(extraSources);
            return Calculate(new ScoreSnapshot(board, db, finalFlat, finalMultiplier, sources, history, initialHappyCakeLayers, extraCountAsPerDish, cakeLayerThresholdReduction, reverseDishOrder, unservedRecipeDishes));
        }

        public ScoreResult Calculate(ScoreSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            List<ScoreEffectEntry> entries = CollectEntries(snapshot);
            var ctx = new ScoreContext(snapshot);
            ctx.EmitEvent(ScoreEventType.CalculationStarted, "开始分数结算");

            RunGlobalPhase(ctx, entries, ScorePhase.BeforeAll);

            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                ctx.BeginDish(dish);
                RunDishPhase(ctx, entries, ScorePhase.BeforeDish, dish);
                ctx.RecordDishBase();
                RunDishPhase(ctx, entries, ScorePhase.DishBase, dish);
                RunDishPhase(ctx, entries, ScorePhase.DishSkills, dish);
                RunDishPhase(ctx, entries, ScorePhase.DishFlavor, dish);
                RunDishPhase(ctx, entries, ScorePhase.Materials, dish);
                RunDishPhase(ctx, entries, ScorePhase.AfterDish, dish);
                ctx.CompleteDish();
            }

            RunGlobalPhase(ctx, entries, ScorePhase.AfterAllDishes);
            ctx.FinalizeDishes();
            ctx.RecordInitialFinalModifiers();
            RunGlobalPhase(ctx, entries, ScorePhase.Final);
            ctx.EmitEvent(ScoreEventType.CalculationFinished, "结束分数结算");
            return ctx.ToResult();
        }

        private List<ScoreEffectEntry> CollectEntries(ScoreSnapshot snapshot)
        {
            var collector = new ScoreEffectCollector();
            new FlavorMaterialEffectSource(_registry).CollectEffects(snapshot, collector);
            new SkillRuleEffectSource().CollectEffects(snapshot, collector);
            new CakeLayerBuffSource().CollectEffects(snapshot, collector);
            new RecipeFlavorEffectSource().CollectEffects(snapshot, collector);

            foreach (IScoreEffectSource source in snapshot.EffectSources)
            {
                source?.CollectEffects(snapshot, collector);
            }

            return collector.Entries
                .OrderBy(e => e.Phase)
                .ThenBy(e => e.BoardOrder)
                .ThenBy(e => e.Source.Type)
                .ThenBy(e => e.Priority)
                .ThenBy(e => e.Sequence)
                .ToList();
        }

        private static void RunGlobalPhase(ScoreContext ctx, IEnumerable<ScoreEffectEntry> entries, ScorePhase phase)
        {
            foreach (ScoreEffectEntry entry in entries)
            {
                if (entry.Phase == phase && entry.Dish == null)
                {
                    ctx.Apply(entry);
                }
            }
        }

        private static void RunDishPhase(
            ScoreContext ctx,
            IEnumerable<ScoreEffectEntry> entries,
            ScorePhase phase,
            DishInstance dish)
        {
            foreach (ScoreEffectEntry entry in entries)
            {
                if (entry.Phase == phase && (entry.Dish == null || entry.Dish.Id == dish.Id))
                {
                    ctx.Apply(entry);
                }
            }
        }

        private IScoreEffectSource[] MergeSources(IEnumerable<IScoreEffectSource> extraSources)
        {
            if (extraSources == null)
            {
                return _effectSources;
            }

            return _effectSources.Concat(extraSources).ToArray();
        }
    }
}
