using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SkillScopePresentationTests
    {
        [Test]
        public void ResolvedTargets_KeepSemanticScopeSeparateFromMultiCellTargetFootprint()
        {
            DishShape jellyShape = DishShape.FromRows(new[] { "XX" });
            DishShape arrowShape = DishShape.FromRows(new[] { "X", "X" });
            DishInstance jelly = Dish(1, "jelly", jellyShape, new GridPos(1, 2));
            DishInstance arrow = Dish(2, "arrow", arrowShape, new GridPos(1, 0));
            var board = new DiningTable(5, 5);
            board.Place(jelly);
            board.Place(arrow);

            SkillRuleDef rule = Rule(SkillScope.RoundAndSelf, SkillScope.RoundAndSelf);
            SkillScopeVisual visual = SkillScopeResolver.Resolve(
                null,
                board,
                jelly,
                rule,
                SkillScopeVisualMode.ResolvedTargets);

            Assert.That(visual.VisualTargetDishInstanceIds.Contains(arrow.Id), Is.True);
            Assert.That(visual.ActionScopeCells.Contains(new GridPos(1, 1)), Is.True);
            Assert.That(visual.ActionScopeCells.Contains(new GridPos(1, 0)), Is.False);
            Assert.That(visual.VisualTargetCells.Contains(new GridPos(1, 0)), Is.True);
        }

        [Test]
        public void ScopeHighlight_EqualEffectiveScopesUseOneMainRegion()
        {
            IReadOnlyList<GridPos> cells = new[]
            {
                new GridPos(1, 1),
                new GridPos(2, 1),
            };

            SkillExecutionTrace trace = Trace(cells, cells);

            Assert.That(BattleScopeHighlightController.ShouldRenderConditionRegion(trace), Is.False);
        }

        [Test]
        public void ScopeHighlight_DifferentEffectiveScopesKeepConditionRegion()
        {
            SkillExecutionTrace trace = Trace(
                new[] { new GridPos(1, 1) },
                new[] { new GridPos(2, 1) });

            Assert.That(BattleScopeHighlightController.ShouldRenderConditionRegion(trace), Is.True);
        }

        [Test]
        public void ScopeTargetGlow_HigherChannelOverridesAndClearingRestoresLowerChannel()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/Battle/DishPiece.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                DishPieceView view = instance.GetComponent<DishPieceView>();
                Assert.That(view, Is.Not.Null);

                view.SetScopeTargetGlow(BattleScopeHighlightChannel.Persistent, Color.cyan, 0);
                Assert.That(view.ActiveScopeTargetGlowChannel, Is.EqualTo(BattleScopeHighlightChannel.Persistent));

                SpriteRenderer glowRenderer = instance.transform.Find("VisualPivot/Sprite/ScopeTargetGlow")
                    ?.GetComponent<SpriteRenderer>();
                Assert.That(glowRenderer, Is.Not.Null);
                var glowBlock = new MaterialPropertyBlock();
                glowRenderer.GetPropertyBlock(glowBlock);
                Assert.That(glowBlock.GetFloat(Shader.PropertyToID("_InnerAlpha")), Is.Zero);
                Assert.That(glowBlock.GetFloat(Shader.PropertyToID("_FillAlpha")), Is.Zero);
                Assert.That(
                    glowBlock.GetFloat(Shader.PropertyToID("_OutlineWidth")),
                    Is.EqualTo(0.045f).Within(0.0001f));

                view.SetScopeTargetGlow(BattleScopeHighlightChannel.Flash, Color.yellow, 0);
                Assert.That(view.ActiveScopeTargetGlowChannel, Is.EqualTo(BattleScopeHighlightChannel.Flash));

                view.SetScopeTargetGlow(BattleScopeHighlightChannel.Settlement, Color.magenta, 0);
                Assert.That(view.ActiveScopeTargetGlowChannel, Is.EqualTo(BattleScopeHighlightChannel.Settlement));

                view.ClearScopeTargetGlow(BattleScopeHighlightChannel.Flash);
                Assert.That(view.ActiveScopeTargetGlowChannel, Is.EqualTo(BattleScopeHighlightChannel.Settlement));

                view.ClearScopeTargetGlow(BattleScopeHighlightChannel.Settlement);
                Assert.That(view.ActiveScopeTargetGlowChannel, Is.EqualTo(BattleScopeHighlightChannel.Persistent));

                view.ClearScopeTargetGlow(BattleScopeHighlightChannel.Persistent);
                Assert.That(view.ActiveScopeTargetGlowChannel, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static DishInstance Dish(int id, string dishId, DishShape shape, GridPos origin)
        {
            var def = new DishDef(
                dishId,
                dishId,
                20,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, origin),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static SkillRuleDef Rule(SkillScope conditionScope, SkillScope actionScope)
        {
            return new SkillRuleDef(
                "scope_rule",
                "scope_skill",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.PositionFilled,
                conditionScope,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddMultFlat,
                actionScope,
                0,
                new[] { 1.5f },
                Array.Empty<string>());
        }

        private static SkillExecutionTrace Trace(
            IReadOnlyList<GridPos> conditionCells,
            IReadOnlyList<GridPos> actionScopeCells)
        {
            return new SkillExecutionTrace(
                SkillExecutionKind.NativeSkill,
                1,
                "jelly",
                "果冻",
                1,
                "jelly",
                "果冻",
                "scope_skill",
                "scope_skill",
                "scope_rule",
                0,
                SkillTrigger.OnSettle,
                SkillActionType.AddMultFlat,
                SkillConditionType.PositionFilled,
                SkillScope.RoundAndSelf,
                SkillScope.RoundAndSelf,
                string.Empty,
                visualTargetDishInstanceIds: new[] { 1 },
                visualTargetCells: actionScopeCells.ToArray(),
                conditionCells: conditionCells,
                actionScopeCells: actionScopeCells);
        }
    }
}
