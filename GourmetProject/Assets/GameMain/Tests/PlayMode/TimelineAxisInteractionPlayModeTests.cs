using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Orchestration;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class TimelineAxisInteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator AddPreview_LeavesWithPointerAndClickConfirmsImmediately()
        {
            CreateRun(out GameRun run, out cfg.Tables tables);
            cfg.GameAction action = tables.TbAction.DataList.First();
            run.BeginTimeline("test", 7f);

            GameObject root = BuildAxis(out ActionAxisBar axis, out RectTransform container);
            axis.Build(run);
            int confirmedDay = -1;
            Assert.That(
                axis.BeginAddDaySelection(
                    run,
                    action.Id,
                    new[] { 3 },
                    day => confirmedDay = day,
                    null),
                Is.True);
            yield return null;

            TimelineAxisPointerTarget pointer =
                container.Find("DayHit_3").GetComponent<TimelineAxisPointerTarget>();
            pointer.OnPointerEnter(new PointerEventData(EventSystem.current));
            Assert.That(axis.HasPreview, Is.True);
            Assert.That(axis.PreviewDay, Is.EqualTo(3));

            pointer.OnPointerExit(new PointerEventData(EventSystem.current));
            Assert.That(axis.HasPreview, Is.False, "移出整数日期后预览状态必须立即清除。");
            Assert.That(axis.PreviewDay, Is.EqualTo(-1));
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(
                root.GetComponentsInChildren<TimelineNodeBubbleView>(true)
                    .Any(view => view.gameObject.name == "NodeBubble_Preview"),
                Is.False);

            pointer = container.Find("DayHit_3").GetComponent<TimelineAxisPointerTarget>();
            pointer.OnPointerEnter(new PointerEventData(EventSystem.current));
            pointer.OnPointerClick(new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
            });
            Assert.That(confirmedDay, Is.EqualTo(3), "单击日期必须直接调用提交回调。");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AddRewardItem_RealBeginUsePathAddsNodeConsumesItemAndClosesArrow()
        {
            CreateRun(out GameRun run, out cfg.Tables tables);
            cfg.GameAction action = tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("existing", "test", 5, action.Id) });

            ItemDefinition item = ItemDefinition.Get(
                tables,
                "item_active_add_reward_node",
                cfg.ItemKind.Active);
            Assert.That(item, Is.Not.Null);
            run.AcquireItem(item.Id, fallbackGold: 0);
            Assert.That(run.HasItem(item.Id), Is.True);

            GameObject root = BuildAxis(out ActionAxisBar axis, out RectTransform container);
            var hostObject = new GameObject(
                "BattleForm",
                typeof(RectTransform),
                typeof(BattleForm));
            hostObject.transform.SetParent(root.transform, false);
            BattleForm host = hostObject.GetComponent<BattleForm>();
            SetPrivate(host, "_run", run);
            SetPrivate(host, "_actionAxisBar", axis);
            SetPrivate(host, "_current", GameplayView.ActionSelect);
            string choiceKey = GameRun.BuildActionChoiceKey(
                run.RunActionStepIndex,
                run.WeekIndex,
                run.CurrentDay,
                run.ActionStepIndex);
            run.SetPendingActionChoices(choiceKey, new ActionChoice[0]);
            ActionCardDeck deck = BuildDeckWithSentinel(
                root.transform,
                out WeekEventCardView existingActionCard);
            SetPrivate(host, "_deck", deck);

            AttachAxisBinder(host, axis);

            TargetArrowView arrowTemplate = BuildArrowTemplate();
            SetPrivate(host, "_activeItemTargetArrowPrefab", arrowTemplate);
            RunItemSlotView slot = BuildItemSlot(root.transform);

            BeginUseActiveItem(host, item, slot);
            yield return null;

            Assert.That(axis.SelectionMode, Is.EqualTo(TimelineAxisSelectionMode.AddDay));
            Assert.That(
                root.GetComponentsInChildren<TargetArrowView>(true)
                    .Any(view => view.gameObject.name.Contains("(Clone)")),
                Is.True,
                "真实使用入口必须创建鼠标跟随箭头。");

            TimelineAxisPointerTarget pointer =
                container.Find("DayHit_3").GetComponent<TimelineAxisPointerTarget>();
            pointer.OnPointerEnter(new PointerEventData(EventSystem.current));
            using (RunPersistence.SuppressSave())
            {
                pointer.OnPointerClick(new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                });
            }

            yield return new WaitForSecondsRealtime(0.25f);

            Assert.That(run.HasItem(item.Id), Is.False, "添加成功后必须消耗主动道具。");
            Assert.That(
                TimelineService.GetNodes(run)
                    .Any(node => node.Day == 3 && node.ActionId == item.EffectParam),
                Is.True,
                "应在玩家点击的整数日期创建配置指定的节点行动。");
            Assert.That(
                GetDeckCards(deck),
                Has.Member(existingActionCard),
                "只修改行动轴时不得重建当前日常/节点行动卡或播放其刷新效果。");
            Assert.That(axis.SelectionMode, Is.EqualTo(TimelineAxisSelectionMode.None));
            Assert.That(
                root.GetComponentsInChildren<TargetArrowView>(true)
                    .Any(view => view.gameObject.name.Contains("(Clone)")),
                Is.False,
                "提交后必须关闭鼠标跟随箭头。");

            Object.Destroy(root);
            Object.Destroy(arrowTemplate.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AddPreview_OnLastDayKeepsNodesDistinctAndCenteredOnDay()
        {
            CreateRun(out GameRun run, out cfg.Tables tables);
            cfg.GameAction action = tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("last_day_existing", "test", 7, action.Id) });

            GameObject root = BuildAxis(out ActionAxisBar axis, out RectTransform container);
            axis.Build(run);
            Assert.That(
                axis.BeginAddDaySelection(run, action.Id, new[] { 7 }, _ => { }, null),
                Is.True);
            yield return null;

            TimelineAxisPointerTarget pointer =
                container.Find("DayHit_7").GetComponent<TimelineAxisPointerTarget>();
            pointer.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(0.25f);

            TimelineDayNodeGroupView group = axis.GetDayGroup(7);
            RectTransform existing = (RectTransform)group.transform
                .Find("NodeBubble_last_day_existing");
            RectTransform preview = (RectTransform)group.transform
                .Find("NodeBubble_Preview");
            Assert.That(existing, Is.Not.Null);
            Assert.That(preview, Is.Not.Null);
            Assert.That(
                Mathf.Abs(preview.anchoredPosition.x - existing.anchoredPosition.x),
                Is.GreaterThan(10f),
                "最后一天的现有节点和预览节点不能被边缘 Clamp 到同一位置。");
            Assert.That(
                existing.anchoredPosition.x + preview.anchoredPosition.x,
                Is.EqualTo(0f).Within(0.1f),
                "最后一天的气泡应以日期点为中心展开，不向行动轴内部偏移。");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HalfDayItem_OnTimelineNodeCardKeepsCardAndBuffStack()
        {
            CreateRun(out GameRun run, out cfg.Tables tables);
            cfg.GameAction action = tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[] { new RuntimeTimelineNode("node_card", "test", 3, action.Id) });
            cfg.TimelineNode node = TimelineService.GetNodes(run).Single();

            ItemDefinition item = ItemDefinition.Get(
                tables,
                "item_active_half_next_action_cost",
                cfg.ItemKind.Active);
            Assert.That(item, Is.Not.Null);
            run.AcquireItem(item.Id, fallbackGold: 0);

            var root = new GameObject("HalfDayNodeCardTestRoot", typeof(RectTransform));
            var hostObject = new GameObject(
                "BattleForm",
                typeof(RectTransform),
                typeof(BattleForm));
            hostObject.transform.SetParent(root.transform, false);
            BattleForm host = hostObject.GetComponent<BattleForm>();
            SetPrivate(host, "_run", run);
            SetPrivate(host, "_current", GameplayView.ActionSelect);
            SetPrivate(host, "_currentTimelineNodeCard", node);
            SetPrivate(host, "_currentTimelineNodePick", (System.Action)(() => { }));

            ActionCardDeck deck = BuildDeckWithSentinel(
                root.transform,
                out WeekEventCardView existingNodeCard);
            SetPrivate(host, "_deck", deck);
            RunItemSlotView slot = BuildItemSlot(root.transform);

            BeginUseActiveItem(host, item, slot);
            yield return new WaitForSecondsRealtime(0.25f);

            Assert.That(run.HasItem(item.Id), Is.False);
            Assert.That(run.NextDailyActionHalfCostStacks, Is.EqualTo(1));
            Assert.That(
                GetDeckCards(deck),
                Has.Member(existingNodeCard),
                "节点行动单卡状态使用半日券不得重建或播放卡片刷新。");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExecuteFutureAndPastItems_UseAxisArrowWithoutPreviewAndCommitOnClick()
        {
            CreateRun(out GameRun run, out cfg.Tables tables);
            cfg.GameAction action = tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("past_node", "test", 1, action.Id),
                    new RuntimeTimelineNode("future_node", "test", 4, action.Id),
                });
            TimelineService.AdvanceDays(run, 2f);
            run.MarkNodeTriggered("past_node");

            ItemDefinition futureItem = ItemDefinition.Get(
                tables,
                "item_active_execute_future_node",
                cfg.ItemKind.Active);
            ItemDefinition pastItem = ItemDefinition.Get(
                tables,
                "item_active_execute_past_node",
                cfg.ItemKind.Active);
            Assert.That(futureItem, Is.Not.Null);
            Assert.That(pastItem, Is.Not.Null);
            run.AcquireItem(futureItem.Id, fallbackGold: 0);
            run.AcquireItem(pastItem.Id, fallbackGold: 0);

            GameObject root = BuildAxis(out ActionAxisBar axis, out _);
            var hostObject = new GameObject(
                "BattleForm",
                typeof(RectTransform),
                typeof(BattleForm));
            hostObject.transform.SetParent(root.transform, false);
            BattleForm host = hostObject.GetComponent<BattleForm>();
            SetPrivate(host, "_run", run);
            SetPrivate(host, "_actionAxisBar", axis);
            SetPrivate(host, "_current", GameplayView.Shop);
            SetPrivate(host, "_loop", new WeekLoopController(run, null));
            AttachAxisBinder(host, axis);

            TargetArrowView arrowTemplate = BuildArrowTemplate();
            SetPrivate(host, "_activeItemTargetArrowPrefab", arrowTemplate);
            RunItemSlotView slot = BuildItemSlot(root.transform);

            yield return UseExecuteItemAndAssert(
                root,
                host,
                run,
                axis,
                futureItem,
                slot,
                targetDay: 4,
                targetNodeId: "future_node");
            yield return UseExecuteItemAndAssert(
                root,
                host,
                run,
                axis,
                pastItem,
                slot,
                targetDay: 1,
                targetNodeId: "past_node");

            Object.Destroy(root);
            Object.Destroy(arrowTemplate.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NodeCompletion_RescansDueNodesAndRunsNewCurrentDayNodeNext()
        {
            CreateRun(out GameRun run, out cfg.Tables tables);
            cfg.GameAction effect = tables.TbAction.DataList.First(
                action => action.Id == "act_gold_clear");
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("current_static", "test", 2, effect.Id),
                    new RuntimeTimelineNode("future_static", "test", 3, effect.Id),
                });
            run.CurrentDay = 2f;

            var view = new AutoNodeLoopView(run, effect.Id);
            var loop = new WeekLoopController(run, view);
            using (RunPersistence.SuppressSave())
            {
                loop.PromptNextAction();
            }

            Assert.That(view.AddedNodeId, Is.Not.Empty);
            Assert.That(
                view.ShownNodeIds,
                Is.EqualTo(new[] { "current_static", view.AddedNodeId }),
                "当前节点结束后必须重扫行动轴，并优先执行新加入的同日节点。");
            Assert.That(run.IsNodeTriggered("current_static"), Is.True);
            Assert.That(run.IsNodeTriggered(view.AddedNodeId), Is.True);
            Assert.That(run.IsNodeTriggered("future_static"), Is.False);
            Assert.That(view.OpenWeekMapCount, Is.EqualTo(1));

            yield return null;
        }

        [UnityTest]
        public IEnumerator DeleteSelection_OverlappedGroupResolvesPointedNodeAndConfirmsImmediately()
        {
            CreateRun(out GameRun run, out cfg.Tables tables);
            cfg.GameAction action = tables.TbAction.DataList.First();
            run.BeginTimeline(
                "test",
                7f,
                new[]
                {
                    new RuntimeTimelineNode("same_0", "test", 4, action.Id),
                    new RuntimeTimelineNode("same_1", "test", 4, action.Id),
                    new RuntimeTimelineNode("same_2", "test", 4, action.Id),
                    new RuntimeTimelineNode("same_3", "test", 4, action.Id),
                });

            GameObject root = BuildAxis(out ActionAxisBar axis, out _);
            axis.Build(run);
            string confirmedNode = null;
            Assert.That(
                axis.BeginDeleteNodeSelection(
                    run,
                    new[] { "same_0", "same_1", "same_2", "same_3" },
                    nodeId => confirmedNode = nodeId,
                    null),
                Is.True);
            yield return null;

            TimelineDayNodeGroupView group = axis.GetDayGroup(4);
            Assert.That(group, Is.Not.Null);
            Transform target = group.transform.Find("NodeBubble_same_1");
            Assert.That(target, Is.Not.Null);
            TimelineNodeBubbleView bubble = target.GetComponent<TimelineNodeBubbleView>();
            TimelineNodeBubbleView prefab =
                Resources.Load<TimelineNodeBubbleView>("Prefabs/UI/Hud/TimelineNodeBubbleView");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(
                bubble.Rect.sizeDelta,
                Is.EqualTo(prefab.Rect.sizeDelta),
                "容器布局不得覆盖用户在 Prefab 中制作的气泡尺寸。");
            TimelineNodeTailGraphic tail =
                target.GetComponentInChildren<TimelineNodeTailGraphic>(true);
            Assert.That(tail, Is.Not.Null);
            Vector2 expectedTip = tail.rectTransform.InverseTransformPoint(
                group.transform.TransformPoint(Vector3.zero));
            Assert.That(tail.Tip.x, Is.EqualTo(expectedTip.x).Within(0.1f));
            Assert.That(tail.Tip.y, Is.EqualTo(expectedTip.y).Within(0.1f));
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, target.position),
                button = PointerEventData.InputButton.Left,
            };
            group.OnPointerMove(pointer);
            group.OnPointerClick(pointer);
            Assert.That(
                confirmedNode,
                Is.EqualTo("same_1"),
                "重叠删除应由容器解析指针最近的节点，并在单击时立即提交。");

            Object.Destroy(root);
            yield return null;
        }

        private static void CreateRun(out GameRun run, out cfg.Tables tables)
        {
            var config = new ConfigService();
            config.LoadAll();
            tables = config.Tables;
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            string characterId = tables.TbCharacter.DataList.First().Id;
            run = new GameRun(tables, database, characterId, "timeline-axis-interaction");
        }

        private static GameObject BuildAxis(
            out ActionAxisBar axis,
            out RectTransform container)
        {
            var root = new GameObject(
                "AxisTestRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(1200f, 300f);

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            var axisObject = new GameObject(
                "ActionAxis",
                typeof(RectTransform),
                typeof(ActionAxisBar));
            axisObject.transform.SetParent(root.transform, false);
            RectTransform axisRect = (RectTransform)axisObject.transform;
            axisRect.sizeDelta = new Vector2(1000f, 160f);
            axis = axisObject.GetComponent<ActionAxisBar>();

            var containerObject = new GameObject("Container", typeof(RectTransform));
            containerObject.transform.SetParent(axisObject.transform, false);
            container = (RectTransform)containerObject.transform;
            container.anchorMin = new Vector2(0.06f, 0.15f);
            container.anchorMax = new Vector2(0.94f, 0.90f);
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;

            var markerObject = new GameObject(
                "Marker",
                typeof(RectTransform),
                typeof(Image));
            markerObject.transform.SetParent(axisObject.transform, false);
            RectTransform marker = (RectTransform)markerObject.transform;
            marker.anchorMin = new Vector2(0f, 0.05f);
            marker.anchorMax = new Vector2(0.03f, 0.18f);

            var remainingObject = new GameObject(
                "Remaining",
                typeof(RectTransform),
                typeof(Text));
            remainingObject.transform.SetParent(axisObject.transform, false);
            Text remaining = remainingObject.GetComponent<Text>();
            remaining.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            SetPrivate(axis, "_container", container);
            SetPrivate(axis, "_positionMarker", marker);
            SetPrivate(axis, "_remainingDaysText", remaining);
            return root;
        }

        private static TargetArrowView BuildArrowTemplate()
        {
            var root = new GameObject("TargetArrowTemplate", typeof(RectTransform));
            root.SetActive(false);
            var line = new GameObject(
                "Line",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            line.transform.SetParent(root.transform, false);
            line.GetComponent<Image>().raycastTarget = false;
            var head = new GameObject(
                "Head",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            head.transform.SetParent(root.transform, false);
            head.GetComponent<Image>().raycastTarget = false;
            TargetArrowView view = root.AddComponent<TargetArrowView>();
            root.SetActive(true);
            return view;
        }

        private static RunItemSlotView BuildItemSlot(Transform parent)
        {
            var root = new GameObject("ActiveItemSlot", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var iconObject = new GameObject(
                "Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconObject.transform.SetParent(root.transform, false);
            Image icon = iconObject.GetComponent<Image>();
            var slot = root.AddComponent<RunItemSlotView>();
            SetPrivate(slot, "_icon", icon);
            return slot;
        }

        private static ActionCardDeck BuildDeckWithSentinel(
            Transform parent,
            out WeekEventCardView sentinel)
        {
            var deckObject = new GameObject(
                "ActionCardDeck",
                typeof(RectTransform),
                typeof(ActionCardDeck));
            deckObject.transform.SetParent(parent, false);
            ActionCardDeck deck = deckObject.GetComponent<ActionCardDeck>();

            var cardObject = new GameObject(
                "ExistingActionCard",
                typeof(RectTransform));
            cardObject.SetActive(false);
            cardObject.transform.SetParent(deckObject.transform, false);
            sentinel = cardObject.AddComponent<WeekEventCardView>();
            GetDeckCards(deck).Add(sentinel);
            return deck;
        }

        private static System.Collections.Generic.List<WeekEventCardView> GetDeckCards(
            ActionCardDeck deck)
        {
            FieldInfo field = typeof(ActionCardDeck).GetField(
                "_cards",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (System.Collections.Generic.List<WeekEventCardView>)field.GetValue(deck);
        }

        private static IEnumerator UseExecuteItemAndAssert(
            GameObject root,
            BattleForm host,
            GameRun run,
            ActionAxisBar axis,
            ItemDefinition item,
            RunItemSlotView slot,
            int targetDay,
            string targetNodeId)
        {
            RuntimeTimelineNode sourceNode =
                run.RuntimeTimelineNodes.Single(node => node.Id == targetNodeId);
            var existingNodeIds = new System.Collections.Generic.HashSet<string>(
                run.RuntimeTimelineNodes.Select(node => node.Id));

            BeginUseActiveItem(host, item, slot);
            yield return null;

            Assert.That(axis.SelectionMode, Is.EqualTo(TimelineAxisSelectionMode.ExecuteNode));
            Assert.That(axis.HasPreview, Is.False, "加急单选择节点时不应创建预览气泡。");
            Assert.That(
                root.GetComponentsInChildren<TargetArrowView>(true)
                    .Any(view => view.gameObject.name.Contains("(Clone)")),
                Is.True,
                "加急单必须创建鼠标跟随箭头。");

            TimelineDayNodeGroupView group = axis.GetDayGroup(targetDay);
            Assert.That(group, Is.Not.Null);
            Transform target = group.transform.Find($"NodeBubble_{targetNodeId}");
            Assert.That(target, Is.Not.Null);
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, target.position),
                button = PointerEventData.InputButton.Left,
            };
            group.OnPointerMove(pointer);
            group.OnPointerClick(pointer);
            yield return null;

            Assert.That(run.HasItem(item.Id), Is.False, "单击目标后必须立即消耗加急单。");
            Assert.That(axis.SelectionMode, Is.EqualTo(TimelineAxisSelectionMode.None));
            Assert.That(axis.HasPreview, Is.False);
            Assert.That(
                root.GetComponentsInChildren<TargetArrowView>(true)
                    .Any(view => view.gameObject.name.Contains("(Clone)")),
                Is.False,
                "提交后必须关闭鼠标跟随箭头。");

            RuntimeTimelineNode clonedNode = run.RuntimeTimelineNodes.Single(
                node => !existingNodeIds.Contains(node.Id));
            Assert.That(
                clonedNode.Day,
                Is.EqualTo(TimelineMath.CurrentOrNextIntegerDay(run.CurrentDay)),
                "加急单应把节点行动复制到当前或下一个整数日。");
            Assert.That(clonedNode.ActionId, Is.EqualTo(sourceNode.ActionId));
            Assert.That(clonedNode.SourceItemId, Is.EqualTo(item.Id));
            Assert.That(
                run.RuntimeTimelineNodes.Any(node => node.Id == targetNodeId),
                Is.True,
                "复制后原节点必须保留。");
            Assert.That(
                run.TryDequeueExtraTimelineNode(out _),
                Is.False,
                "新版本加急单不得再写入旧的额外执行队列。");
        }

        private sealed class AutoNodeLoopView : IWeekLoopView
        {
            private readonly GameRun _run;
            private readonly string _actionId;
            private bool _addedCurrentDayNode;

            public AutoNodeLoopView(GameRun run, string actionId)
            {
                _run = run;
                _actionId = actionId;
            }

            public int LastBattleTotal => 0;

            public List<string> ShownNodeIds { get; } = new List<string>();

            public string AddedNodeId { get; private set; } = string.Empty;

            public int OpenWeekMapCount { get; private set; }

            public void HideBattleWorld()
            {
            }

            public void SavePendingRewardBattleView()
            {
            }

            public void RestorePendingRewardBattleView()
            {
            }

            public void HideResultPanel()
            {
            }

            public void OpenWeekMap()
            {
                OpenWeekMapCount++;
            }

            public void OpenShop()
            {
            }

            public void ShowTimelineNodeCard(
                cfg.TimelineNode node,
                int? interestMaxGain,
                System.Action onPick)
            {
                ShownNodeIds.Add(node.Id);
                if (!_addedCurrentDayNode)
                {
                    _addedCurrentDayNode = true;
                    AddedNodeId = _run.AddRuntimeTimelineNodeAtDay(
                        _actionId,
                        TimelineMath.CurrentOrNextIntegerDay(_run.CurrentDay));
                }

                onPick?.Invoke();
            }

            public void ShowTimelineNodeSkipped(cfg.TimelineNode node, System.Action onDone)
            {
                onDone?.Invoke();
            }

            public void StartBattle(
                int requiredScore,
                string modifier,
                string key,
                ActionExecutionContext actionContext)
            {
                Assert.Fail("Effect node should not start a battle.");
            }

            public void ShowNotice(string title, string message, System.Action onContinue)
            {
                onContinue?.Invoke();
            }

            public void OpenEventRecipeDishDelete(
                GameRun run,
                string title,
                System.Action onCancel,
                System.Action<ActiveTarget> onTargetConfirmed,
                System.Action onChanged)
            {
                Assert.Fail("Effect node should not open an event target picker.");
            }

            public void ShowEventPage(
                string title,
                string desc,
                string resultButtonText,
                string bgSprite,
                IReadOnlyList<string> options,
                IReadOnlyList<bool> optionEnabled,
                System.Action<int> onPick,
                System.Action onEnd)
            {
                Assert.Fail("Effect node should not open an event page.");
            }

            public void ShowRunResult(bool win, int total)
            {
                Assert.Fail("This test should return to the action map.");
            }
        }

        private static void AttachAxisBinder(BattleForm host, ActionAxisBar axis)
        {
            System.Type binderType = typeof(BattleForm).Assembly.GetType(
                "GourmetProject.Game.UI.Battle.View.TimelineAxisBinder");
            Assert.That(binderType, Is.Not.Null);
            ConstructorInfo binderConstructor = binderType.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single();
            object binder = binderConstructor.Invoke(new object[] { axis, null, null, null });
            SetPrivate(host, "_axisBinder", binder);
        }

        private static void BeginUseActiveItem(
            BattleForm host,
            ItemDefinition item,
            RunItemSlotView slot)
        {
            System.Type coordinatorType = typeof(BattleForm).Assembly.GetType(
                "GourmetProject.Game.UI.Battle.ActiveItemUseCoordinator");
            Assert.That(coordinatorType, Is.Not.Null);
            object coordinator = coordinatorType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single()
                .Invoke(new object[] { host });
            MethodInfo beginUse = coordinatorType.GetMethod(
                "BeginUse",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(beginUse, Is.Not.Null);
            beginUse.Invoke(coordinator, new object[] { item, slot });
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
            field.SetValue(target, value);
        }
    }
}
