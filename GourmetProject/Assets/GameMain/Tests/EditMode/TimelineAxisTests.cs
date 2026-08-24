using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TimelineAxisTests
    {
        private const string SpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/TimelineFresh/";
        private const string PrefabRoot = "Assets/GameMain/Content/Resources/Prefabs/UI/Hud/";

        [Test]
        public void NodeClone_PreservesIconKey()
        {
            var source = new TimelineAxisNodeState
            {
                Id = "boss",
                Day = 7,
                ActionId = "act_boss",
                Kind = ActionDisplayKind.Boss,
                IconKey = TimelineAxisIconKeys.Boss("debuff_buffet"),
                Executing = true,
            };

            TimelineAxisNodeState clone = source.Clone();

            Assert.That(clone, Is.Not.SameAs(source));
            Assert.That(clone.IconKey, Is.EqualTo("boss:buffet"));
            Assert.That(clone.Executing, Is.True);
        }

        [Test]
        public void MutationPlan_OrdersResizeRemoveAddMoveReplace_AndKeepsFinalState()
        {
            TimelineAxisViewState before = State(7f,
                Node("remove", 1, "old", ActionDisplayKind.Event),
                Node("move", 2, "same", ActionDisplayKind.Shop),
                Node("replace", 3, "old", ActionDisplayKind.Event));
            TimelineAxisViewState after = State(8f,
                Node("move", 4, "same", ActionDisplayKind.Shop),
                Node("replace", 3, "new", ActionDisplayKind.Reward),
                Node("add", 5, "new", ActionDisplayKind.Interest));

            TimelineAxisPresentationPlan plan = TimelineAxisPresentationPlanner.BuildMutation(
                before,
                after,
                skipped: false,
                targetNodeId: null);

            CollectionAssert.AreEqual(
                new[]
                {
                    TimelinePresentationCueKind.Resize,
                    TimelinePresentationCueKind.Remove,
                    TimelinePresentationCueKind.Move,
                    TimelinePresentationCueKind.Replace,
                    TimelinePresentationCueKind.Add,
                },
                plan.Cues.Select(cue => cue.Kind).ToArray());
            Assert.That(plan.FinalState.LengthDays, Is.EqualTo(8f));
            Assert.That(plan.FinalState.FindNode("replace").ActionId, Is.EqualTo("new"));
        }

        [Test]
        public void SelectionController_BlocksReentrantConfirmation()
        {
            var controller = new TimelineAxisSelectionController();
            int callbacks = 0;
            bool nestedAccepted = true;
            TimelineAxisSelectionRequest request = null;
            request = TimelineAxisSelectionRequest.AddDay(
                Node(TimelineAxisSelectionController.PreviewId, 0, "preview", ActionDisplayKind.Reward),
                new[] { 2, 4 },
                day =>
                {
                    callbacks++;
                    nestedAccepted = controller.TryConfirmDay(day);
                },
                null);

            Assert.That(controller.Begin(request), Is.True);
            Assert.That(controller.TryConfirmDay(2), Is.True);
            Assert.That(nestedAccepted, Is.False);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(controller.TryConfirmDay(3), Is.False);
        }

        [Test]
        public void DueStops_PreservesSameDayOrder_AndHonorsBoundaries()
        {
            TimelineAxisViewState state = State(7f,
                Node("at_start", 1, "a", ActionDisplayKind.Event),
                Node("same_a", 3, "b", ActionDisplayKind.Shop),
                Node("same_b", 3, "c", ActionDisplayKind.Reward),
                Node("after", 4, "d", ActionDisplayKind.Event));

            IReadOnlyList<TimelineAxisNodeState> stops =
                TimelineAxisPresentationPlanner.DueStops(state, 1f, 3f);

            CollectionAssert.AreEqual(new[] { "same_a", "same_b" }, stops.Select(node => node.Id));
        }

        [Test]
        public void PresentationPlayer_RendersAuthorityAndCallsBackOnce()
        {
            TimelineAxisViewState final = State(7f, Node("a", 2, "a", ActionDisplayKind.Event));
            var cues = new[]
            {
                TimelinePresentationCue.Node(TimelinePresentationCueKind.TriggerStart, "a", final),
                TimelinePresentationCue.Node(TimelinePresentationCueKind.TriggerComplete, "a", final),
            };
            int callbacks = 0;
            int rendered = 0;
            var player = new TimelineAxisPresentationPlayer(
                (cue, complete, speed) => complete?.Invoke(),
                state => rendered++,
                () => { },
                () => { });

            player.Play(new TimelineAxisPresentationPlan(cues, final), () => callbacks++);

            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(rendered, Is.EqualTo(1));
        }

        [Test]
        public void FreshPrefabs_HaveCompleteReferences_AndNoVisibleWhiteSprite()
        {
            string[] prefabs =
            {
                "TimelineAxisView.prefab",
                "TimelineDayPointView.prefab",
                "TimelineDayNodeGroupView.prefab",
                "TimelineNodeBubbleView.prefab",
            };
            string whitePath = AssetDatabase.GUIDToAssetPath("7295a5360413845b8b3c9d468aa1e4e8");
            Sprite white = AssetDatabase.LoadAssetAtPath<Sprite>(whitePath);

            foreach (string file in prefabs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + file);
                Assert.That(prefab, Is.Not.Null, file);
                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                {
                    Assert.That(image.sprite, Is.Not.SameAs(white), $"{file}/{image.name}");
                    if (image.sprite == null && image.gameObject == prefab)
                    {
                        Assert.That(image.color.a, Is.LessThanOrEqualTo(0.0011f), $"{file}/{image.name}");
                    }
                }
            }

            GameObject main = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + "TimelineAxisView.prefab");
            TimelineAxisView view = main.GetComponent<TimelineAxisView>();
            Assert.That(view, Is.Not.Null);
            var serialized = new SerializedObject(view);
            string[] required =
            {
                "_theme", "_axisContent", "_track", "_progress",
                "_dayLayer", "_nodeLayer", "_cursorLayer", "_cursor", "_dayBadge",
                "_currentDayText", "_dayPointPrefab", "_dayNodeGroupPrefab", "_nodeBubblePrefab",
            };
            foreach (string propertyName in required)
            {
                Assert.That(
                    serialized.FindProperty(propertyName).objectReferenceValue,
                    Is.Not.Null,
                    propertyName);
            }
        }

        [Test]
        public void FreshSprites_UseRequiredImportPolicy()
        {
            string[] names =
            {
                "timeline_panel_fresh.png", "timeline_track_fresh.png", "timeline_progress_fresh.png",
                "timeline_tick_fresh.png", "timeline_cursor_fresh.png", "timeline_day_badge_fresh.png",
                "timeline_node_bubble_fresh.png",
            };

            foreach (string name in names)
            {
                var importer = AssetImporter.GetAtPath(SpriteRoot + name) as TextureImporter;
                Assert.That(importer, Is.Not.Null, name);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), name);
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), name);
                Assert.That(importer.alphaIsTransparency, Is.True, name);
                Assert.That(importer.mipmapEnabled, Is.False, name);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), name);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), name);
            }
        }

        [Test]
        public void NodeBubbleSprite_HasCenteredBoundsAndAntialiasedAlpha()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.Combine(
                projectRoot,
                SpriteRoot + "timeline_node_bubble_fresh.png");
            Sprite imported = AssetDatabase.LoadAssetAtPath<Sprite>(
                SpriteRoot + "timeline_node_bubble_fresh.png");
            Assert.That(imported, Is.Not.Null);
            byte[] bytes = File.ReadAllBytes(absolutePath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, bytes, false), Is.True);
                Assert.That(texture.width, Is.EqualTo(texture.height));
                Assert.That(imported.rect.width, Is.EqualTo(texture.width));
                Assert.That(imported.rect.height, Is.EqualTo(texture.height));
                Assert.That(imported.pivot.x, Is.EqualTo(texture.width * 0.5f).Within(0.5f));
                Assert.That(imported.pivot.y, Is.EqualTo(texture.height * 0.5f).Within(0.5f));

                Color32[] pixels = texture.GetPixels32();
                int minX = texture.width;
                int minY = texture.height;
                int maxX = -1;
                int maxY = -1;
                bool hasFractionalAlpha = false;
                for (int y = 0; y < texture.height; y++)
                {
                    for (int x = 0; x < texture.width; x++)
                    {
                        byte alpha = pixels[y * texture.width + x].a;
                        if (alpha == 0)
                        {
                            continue;
                        }

                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                        hasFractionalAlpha |= alpha < byte.MaxValue;
                    }
                }

                Assert.That(maxX, Is.GreaterThan(minX));
                int left = minX;
                int right = texture.width - 1 - maxX;
                int bottom = minY;
                int top = texture.height - 1 - maxY;
                Assert.That(left, Is.EqualTo(right).Within(1), "节点壳水平留白必须居中。");
                Assert.That(bottom, Is.EqualTo(top).Within(1), "节点壳垂直留白必须居中。");
                Assert.That(left, Is.GreaterThan(0), "节点壳不能贴到画布边缘。");
                Assert.That(hasFractionalAlpha, Is.True, "节点壳轮廓需要抗锯齿 Alpha。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void FreshPrefab_SlicedChromeUsesDisplayScale_AndTailHasVisibleOutline()
        {
            GameObject main = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + "TimelineAxisView.prefab");
            Image track = main.transform.Find("AxisContent/Track/TrackBackground").GetComponent<Image>();
            Image progress = main.transform.Find("AxisContent/Track/TrackProgress").GetComponent<Image>();
            Image badge = main.transform.Find("AxisContent/CursorLayer/Cursor/DayBadge").GetComponent<Image>();

            Assert.That(track.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(track.pixelsPerUnitMultiplier, Is.EqualTo(12f).Within(0.001f));
            Assert.That(progress.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(progress.pixelsPerUnitMultiplier, Is.EqualTo(7.2f).Within(0.001f));
            Assert.That(badge.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(badge.pixelsPerUnitMultiplier, Is.EqualTo(16f).Within(0.001f));

            GameObject bubble = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + "TimelineNodeBubbleView.prefab");
            TimelineNodeTailGraphic tail = bubble.GetComponentInChildren<TimelineNodeTailGraphic>(true);
            Assert.That(tail, Is.Not.Null);
            var serializedTail = new SerializedObject(tail);
            Assert.That(serializedTail.FindProperty("_baseWidth").floatValue, Is.GreaterThanOrEqualTo(16f));
            Assert.That(serializedTail.FindProperty("_outlineWidth").floatValue, Is.GreaterThanOrEqualTo(2f));
            Assert.That(serializedTail.FindProperty("_outlineColor").colorValue.a, Is.GreaterThan(0.99f));
        }

        [Test]
        public void DayPointTick_IsVerticallyCenteredOnTrackBackground()
        {
            GameObject main = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + "TimelineAxisView.prefab");
            RectTransform track = main.transform
                .Find("AxisContent/Track")
                .GetComponent<RectTransform>();
            GameObject dayPoint = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + "TimelineDayPointView.prefab");
            RectTransform pointRect = dayPoint.GetComponent<RectTransform>();
            RectTransform tick = dayPoint.transform.Find("Tick").GetComponent<RectTransform>();

            Assert.That(pointRect.anchorMin.y, Is.EqualTo(track.anchorMin.y).Within(0.0001f));
            Assert.That(pointRect.anchorMax.y, Is.EqualTo(track.anchorMax.y).Within(0.0001f));
            Assert.That(pointRect.anchoredPosition.y, Is.Zero.Within(0.0001f));
            Assert.That(tick.anchorMin.y, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(tick.anchorMax.y, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(tick.anchoredPosition.y, Is.Zero.Within(0.0001f));
        }

        private static TimelineAxisViewState State(float length, params TimelineAxisNodeState[] nodes)
        {
            var state = new TimelineAxisViewState { LengthDays = length };
            state.Nodes.AddRange(nodes);
            return state;
        }

        private static TimelineAxisNodeState Node(
            string id,
            int day,
            string actionId,
            ActionDisplayKind kind)
        {
            return new TimelineAxisNodeState
            {
                Id = id,
                Day = day,
                ActionId = actionId,
                Kind = kind,
                IconKey = TimelineAxisIconKeys.ForKind(kind),
            };
        }
    }
}
