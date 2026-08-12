using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        public void ScopeRegion_ChannelsUseDistinctFillFlowAndRevealProfiles()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/Battle/ScopeRegionOutline.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                BattleScopeRegionOutlineView view = instance.GetComponent<BattleScopeRegionOutlineView>();
                Assert.That(view, Is.Not.Null);

                BattleScopeRegionOutlineView.ScopeVisualProfile persistent = view.ResolveProfile(
                    BattleScopeHighlightChannel.Persistent,
                    BattleScopeRegionRole.Action);
                BattleScopeRegionOutlineView.ScopeVisualProfile flash = view.ResolveProfile(
                    BattleScopeHighlightChannel.Flash,
                    BattleScopeRegionRole.Action);
                BattleScopeRegionOutlineView.ScopeVisualProfile settlement = view.ResolveProfile(
                    BattleScopeHighlightChannel.Settlement,
                    BattleScopeRegionRole.Action);
                BattleScopeRegionOutlineView.ScopeVisualProfile condition = view.ResolveProfile(
                    BattleScopeHighlightChannel.Settlement,
                    BattleScopeRegionRole.Condition);

                Assert.That(persistent.FillAlpha, Is.EqualTo(0.06f).Within(0.001f));
                Assert.That(flash.FillAlpha, Is.EqualTo(0.10f).Within(0.001f));
                Assert.That(settlement.FillAlpha, Is.EqualTo(0.12f).Within(0.001f));
                Assert.That(flash.FlowSpeed, Is.GreaterThan(settlement.FlowSpeed));
                Assert.That(settlement.FlowSpeed, Is.GreaterThan(persistent.FlowSpeed));
                Assert.That(settlement.GlowAlpha, Is.GreaterThan(flash.GlowAlpha));
                Assert.That(flash.GlowAlpha, Is.GreaterThan(persistent.GlowAlpha));
                Assert.That(persistent.RevealDuration, Is.InRange(0.12f, 0.16f));
                Assert.That(flash.RevealDuration, Is.InRange(0.10f, 0.16f));
                Assert.That(settlement.RevealDuration, Is.InRange(0.12f, 0.16f));
                Assert.That(persistent.FadeOutDuration, Is.InRange(0.10f, 0.16f));
                Assert.That(flash.FadeOutDuration, Is.InRange(0.10f, 0.16f));
                Assert.That(settlement.FadeOutDuration, Is.InRange(0.10f, 0.16f));
                Assert.That(condition.FillAlpha, Is.Zero);
                Assert.That(condition.OutlineAlpha, Is.LessThan(settlement.OutlineAlpha));
                Assert.That(condition.GlowAlpha, Is.LessThan(settlement.GlowAlpha));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ScopeRegion_ShowDefaultsToActionAndWritesAnimatedGridProperties()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/Battle/ScopeRegionOutline.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                BattleScopeRegionOutlineView view = instance.GetComponent<BattleScopeRegionOutlineView>();
                SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
                Assert.That(view, Is.Not.Null);
                Assert.That(renderer, Is.Not.Null);
                IReadOnlyList<GridPos> cells = new[] { new GridPos(0, 0) };
                view.Show(
                    BattleScopeHighlightChannel.Persistent,
                    0,
                    cells,
                    0,
                    0,
                    0,
                    0,
                    Vector3.zero,
                    Vector2.one,
                    Color.cyan,
                    0.05f,
                    null);

                Assert.That(view.ActiveChannel, Is.EqualTo(BattleScopeHighlightChannel.Persistent));
                Assert.That(view.ActiveRole, Is.EqualTo(BattleScopeRegionRole.Action));
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat(Shader.PropertyToID("_UseGridMask")), Is.EqualTo(1f));
                Assert.That(block.GetFloat(Shader.PropertyToID("_FillAlpha")), Is.EqualTo(0.06f).Within(0.001f));
                Assert.That(block.GetFloat(Shader.PropertyToID("_GridGlowAlpha")), Is.GreaterThan(0f));
                Assert.That(block.GetFloat(Shader.PropertyToID("_GridFlowSpeed")), Is.GreaterThan(0f));
                Assert.That(block.GetFloat(Shader.PropertyToID("_GridRevealDuration")), Is.InRange(0.12f, 0.16f));
                Assert.That(block.GetFloat(Shader.PropertyToID("_GridVisibility")), Is.EqualTo(1f));

                view.Show(
                    BattleScopeHighlightChannel.Settlement,
                    BattleScopeRegionRole.Condition,
                    0,
                    cells,
                    0,
                    0,
                    0,
                    0,
                    Vector3.zero,
                    Vector2.one,
                    Color.yellow,
                    0.05f,
                    null);
                renderer.GetPropertyBlock(block);
                Assert.That(view.ActiveRole, Is.EqualTo(BattleScopeRegionRole.Condition));
                Assert.That(block.GetFloat(Shader.PropertyToID("_FillAlpha")), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ScopeRegion_FadeInAndOutKeepObjectAliveUntilVisibilityReachesZero()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameMain/Content/Prefabs/Battle/ScopeRegionOutline.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                BattleScopeRegionOutlineView view = instance.GetComponent<BattleScopeRegionOutlineView>();
                SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
                Assert.That(view, Is.Not.Null);
                Assert.That(renderer, Is.Not.Null);
                IReadOnlyList<GridPos> cells = new[] { new GridPos(0, 0) };
                view.Show(
                    BattleScopeHighlightChannel.Persistent,
                    0,
                    cells,
                    0,
                    0,
                    0,
                    0,
                    Vector3.zero,
                    Vector2.one,
                    Color.cyan,
                    0.05f,
                    null);

                BattleScopeRegionOutlineView.ScopeVisualProfile profile = view.ResolveProfile(
                    BattleScopeHighlightChannel.Persistent,
                    BattleScopeRegionRole.Action);
                view.StartFadeIn(profile.RevealDuration);
                Assert.That(view.ActiveVisibility, Is.Zero.Within(0.001f));
                Assert.That(view.IsVisibilityAnimating, Is.True);
                Assert.That(instance.activeSelf, Is.True);

                view.AdvanceVisibility(profile.RevealDuration * 0.5f);
                Assert.That(view.ActiveVisibility, Is.EqualTo(0.5f).Within(0.02f));
                Assert.That(instance.activeSelf, Is.True);
                view.AdvanceVisibility(profile.RevealDuration * 0.5f);
                Assert.That(view.ActiveVisibility, Is.EqualTo(1f).Within(0.001f));
                Assert.That(view.IsVisibilityAnimating, Is.False);

                view.StartFadeOut();
                Assert.That(view.IsVisibilityAnimating, Is.True);
                Assert.That(instance.activeSelf, Is.True);

                view.AdvanceVisibility(profile.FadeOutDuration * 0.5f);
                Assert.That(view.ActiveVisibility, Is.EqualTo(0.5f).Within(0.02f));
                Assert.That(instance.activeSelf, Is.True);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Assert.That(
                    block.GetFloat(Shader.PropertyToID("_GridVisibility")),
                    Is.EqualTo(view.ActiveVisibility).Within(0.001f));

                view.AdvanceVisibility(profile.FadeOutDuration * 0.5f);
                Assert.That(view.ActiveVisibility, Is.Zero.Within(0.001f));
                Assert.That(view.IsVisibilityAnimating, Is.False);
                Assert.That(instance.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ScopeGridMask_SingleRectangleLShapeDisconnectedAndEdgeCellsStayExact()
        {
            IReadOnlyList<IReadOnlyList<GridPos>> cases = new IReadOnlyList<GridPos>[]
            {
                new[] { new GridPos(0, 0) },
                new[]
                {
                    new GridPos(0, 0), new GridPos(1, 0),
                    new GridPos(0, 1), new GridPos(1, 1),
                },
                new[]
                {
                    new GridPos(0, 0), new GridPos(0, 1), new GridPos(1, 1),
                },
                new[]
                {
                    new GridPos(0, 0), new GridPos(2, 0), new GridPos(2, 2),
                },
                new[]
                {
                    new GridPos(-1, 0), new GridPos(0, 0), new GridPos(0, 1),
                },
            };

            foreach (IReadOnlyList<GridPos> cells in cases)
            {
                int minX = cells.Min(cell => cell.X);
                int minY = cells.Min(cell => cell.Y);
                int maxX = cells.Max(cell => cell.X);
                int maxY = cells.Max(cell => cell.Y);
                Color32[] pixels = BattleScopeRegionOutlineView.BuildMaskPixels(
                    cells,
                    minX,
                    minY,
                    maxX,
                    maxY,
                    out int width,
                    out int height);
                var expected = new HashSet<GridPos>(cells);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        GridPos cell = new GridPos(x, y);
                        Assert.That(
                            MaskCellAlpha(pixels, width, maxY, minX, cell),
                            Is.EqualTo(expected.Contains(cell) ? 255 : 0),
                            $"Mask mismatch at {cell} for case [{string.Join(", ", cells)}].");
                    }
                }

                Assert.That(pixels[0].a, Is.Zero, "Mask padding must stay transparent.");
                Assert.That(pixels[width * height - 1].a, Is.Zero, "Mask padding must stay transparent.");
            }
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

                SpriteRenderer glowRenderer = instance.transform.Find("VisualPivot/Sprite/ScopeTargetGlow")
                    ?.GetComponent<SpriteRenderer>();
                Assert.That(glowRenderer, Is.Not.Null);
                view.SetSettlementFocus(0.72f);
                view.SetScopeTargetGlow(BattleScopeHighlightChannel.Persistent, Color.cyan, 0);
                view.SetSettlementFocus(0.86f);
                Assert.That(view.ActiveScopeTargetGlowChannel, Is.EqualTo(BattleScopeHighlightChannel.Persistent));
                Assert.That(glowRenderer.color, Is.EqualTo(Color.white));

                var glowBlock = new MaterialPropertyBlock();
                glowRenderer.GetPropertyBlock(glowBlock);
                Assert.That(glowBlock.GetFloat(Shader.PropertyToID("_InnerAlpha")), Is.Zero);
                Assert.That(glowBlock.GetFloat(Shader.PropertyToID("_FillAlpha")), Is.Zero);
                Assert.That(
                    glowBlock.GetFloat(Shader.PropertyToID("_OutlineWidth")),
                    Is.EqualTo(0.045f).Within(0.0001f));

                _ = view.PlaySettlementFeedbackAsync(
                    SettlementDishFeedbackKind.PassiveFlatBonus,
                    CancellationToken.None,
                    durationScale: 10f);
                SpriteRenderer settlementGlowRenderer = instance.transform.Find("VisualPivot/Sprite/PlacementGlow")
                    ?.GetComponent<SpriteRenderer>();
                Assert.That(settlementGlowRenderer, Is.Not.Null);
                var settlementGlowBlock = new MaterialPropertyBlock();
                settlementGlowRenderer.GetPropertyBlock(settlementGlowBlock);
                Assert.That(settlementGlowBlock.GetFloat(Shader.PropertyToID("_InnerAlpha")), Is.Zero);
                Assert.That(settlementGlowBlock.GetFloat(Shader.PropertyToID("_FillAlpha")), Is.Zero);
                _ = view.PlaySettlementFeedbackAsync(
                    SettlementDishFeedbackKind.None,
                    CancellationToken.None);

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

        private static byte MaskCellAlpha(
            IReadOnlyList<Color32> pixels,
            int width,
            int maxY,
            int minX,
            GridPos cell)
        {
            int column = cell.X - minX;
            int rowFromBottom = maxY - cell.Y;
            int x = BattleScopeRegionOutlineView.MaskPadding
                + column * BattleScopeRegionOutlineView.PixelsPerCell
                + BattleScopeRegionOutlineView.PixelsPerCell / 2;
            int y = BattleScopeRegionOutlineView.MaskPadding
                + rowFromBottom * BattleScopeRegionOutlineView.PixelsPerCell
                + BattleScopeRegionOutlineView.PixelsPerCell / 2;
            return pixels[y * width + x].a;
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
