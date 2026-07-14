using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 主动道具子系统测试：主动使用序号可复现、targetKind→情境推导、情境无关的 Apply 派发。
    /// 主动道具表已填入种子数据（参考杀戮尖塔2 药水），覆盖无目标类与需选目标类。
    /// </summary>
    public class ItemActiveTests
    {
        private static cfg.Tables LoadTables()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Config");
            return new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(root, name + ".json"))));
        }

        private static GameRun NewRun()
        {
            cfg.Tables tables = LoadTables();
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            return new GameRun(tables, database, "glutton_dog", "item-active-test", 1);
        }

        private static ItemDefinition MakeActiveItem(
            string id,
            string effectType,
            float value = 0f,
            string effectParam = "",
            int targetKind = 0,
            int targetCount = 0)
        {
            string v = value.ToString(CultureInfo.InvariantCulture);
            string json =
                "{\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"desc\":\"\",\"quality\":0," +
                "\"specialTags\":\"\",\"effectType\":\"" + effectType + "\"," +
                "\"effectValue\":" + v + ",\"effectParam\":\"" + effectParam + "\",\"baseWeight\":1," +
                "\"targetKind\":" + targetKind + ",\"targetCount\":" + targetCount + "}";
            return ItemDefinition.From(new cfg.ActiveItem(JSON.Parse(json)));
        }

        [Test]
        public void ConfiguredItems_HaveExpectedCounts()
        {
            cfg.Tables tables = LoadTables();
            Assert.AreEqual(93, tables.TbPassiveItem.DataList.Count);
            Assert.AreEqual(17, tables.TbActiveItem.DataList.Count, "主动道具表应含当前已启用的 17 张小票。");
        }

        [Test]
        public void ActiveUseIndex_MonotonicAndPersists()
        {
            GameRun run = NewRun();
            Assert.AreEqual("active_0", run.NextActiveUseKey());
            Assert.AreEqual("active_1", run.NextActiveUseKey());
            Assert.AreEqual(2, run.ActiveUseIndex);

            RunSaveData save = run.ToSaveData();
            Assert.AreEqual(2, save.ActiveUseIndex);

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, save);
            Assert.AreEqual(2, restored.ActiveUseIndex);
            Assert.AreEqual("active_2", restored.NextActiveUseKey());
        }

        [Test]
        public void ItemActiveUsage_DerivesContextsAndTargeting()
        {
            Assert.IsFalse(ItemActiveUsage.RequiresTarget(cfg.ItemTargetKind.None));
            Assert.IsFalse(ItemActiveUsage.RequiresTarget(cfg.ItemTargetKind.Global));
            Assert.IsTrue(ItemActiveUsage.RequiresTarget(cfg.ItemTargetKind.DiningTableDish));

            // 棋盘菜只在战斗；菜谱菜任意情境。
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.DiningTableDish, ActiveUseContextKind.Battle));
            Assert.IsFalse(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.DiningTableDish, ActiveUseContextKind.Shop));
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.RecipeDish, ActiveUseContextKind.Battle));
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.RecipeDish, ActiveUseContextKind.Map));

            // 编辑类目标：奖励界面不开放。
            Assert.IsFalse(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.FlavorSlot, ActiveUseContextKind.Reward));
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.FlavorSlot, ActiveUseContextKind.Shop));
        }

        [Test]
        public void ScheduleTickets_UsableOnMapOnly()
        {
            foreach (string effectType in new[]
                     {
                         ItemEffectTypes.RerollAction,
                         ItemEffectTypes.ResetBossDebuff,
                         ItemEffectTypes.TimelineExecuteNext,
                         ItemEffectTypes.TimelineAddRewardNode,
                     })
            {
                ItemDefinition item = MakeActiveItem("sched_" + effectType, effectType, targetKind: 6);
                Assert.IsTrue(ItemActiveUsage.IsScheduleEffect(effectType));
                Assert.IsTrue(ItemActiveUsage.CanUse(item, ActiveUseContextKind.Map), $"{effectType} 应在地图可用。");
                Assert.IsFalse(ItemActiveUsage.CanUse(item, ActiveUseContextKind.Battle), $"{effectType} 不应在战斗可用。");
                Assert.IsFalse(ItemActiveUsage.CanUse(item, ActiveUseContextKind.Reward), $"{effectType} 不应在奖励界面可用。");
            }
        }

        [Test]
        public void HoldingViews_PassiveOnly()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_discount_food", 0); // 被动

            int passives = 0;
            foreach (RunItemState _ in run.PassiveItemStates)
            {
                passives++;
            }

            int actives = 0;
            foreach (RunItemState _ in run.ActiveItemStates)
            {
                actives++;
            }

            Assert.AreEqual(1, passives);
            Assert.AreEqual(0, actives, "配置全为被动，主动持有恒为 0。");
        }

        [Test]
        public void Apply_DispatchesByEffectType()
        {
            GameRun run = NewRun();
            ActiveTarget[] none = Array.Empty<ActiveTarget>();

            var ctx = new FakeContext(run);
            ActiveItemUseResult clear = ActiveItemEffectRegistry.Apply(ctx, MakeActiveItem("t_clear", "ClearBoard"), none);
            Assert.IsTrue(clear.Success);
            Assert.IsTrue(clear.BoardChanged);
            Assert.AreEqual(1, ctx.ClearCalls);

            var ctxNoClear = new FakeContext(run, canClear: false);
            ActiveItemUseResult failClear = ActiveItemEffectRegistry.Apply(ctxNoClear, MakeActiveItem("t_clear", "ClearBoard"), none);
            Assert.IsFalse(failClear.Success);

            int goldBefore = run.Gold;
            ActiveItemUseResult gold = ActiveItemEffectRegistry.Apply(new FakeContext(run), MakeActiveItem("t_gold", "GoldNow", 50f), none);
            Assert.IsTrue(gold.Success);
            Assert.AreEqual(goldBefore + 50, run.Gold);

            ActiveItemUseResult noop = ActiveItemEffectRegistry.Apply(new FakeContext(run), MakeActiveItem("t_unknown", "SomeFutureOp"), none);
            Assert.IsTrue(noop.Success);
            Assert.IsFalse(noop.BoardChanged);
        }

        [Test]
        public void Apply_DispatchesFlavorMaterialAndScheduleEffects()
        {
            GameRun run = NewRun();
            var target = new[] { new ActiveTarget("dish", 0, 0) };

            var flavorCtx = new FakeContext(run);
            ActiveItemUseResult flavor = ActiveItemEffectRegistry.Apply(
                flavorCtx, MakeActiveItem("f", ItemEffectTypes.AddFlavor, 0f, "t_sweet", 2, 1), target);
            Assert.IsTrue(flavor.Success);
            Assert.AreEqual("t_sweet", flavorCtx.LastFlavorId);

            var materialCtx = new FakeContext(run);
            ActiveItemUseResult material = ActiveItemEffectRegistry.Apply(
                materialCtx, MakeActiveItem("m", ItemEffectTypes.AddMaterial, 0f, "m_gold", 3, 1), target);
            Assert.IsTrue(material.Success);
            Assert.AreEqual("m_gold", materialCtx.LastMaterialId);

            var countCtx = new FakeContext(run);
            Assert.IsTrue(ActiveItemEffectRegistry.Apply(
                countCtx, MakeActiveItem("c", ItemEffectTypes.AddCountAs, 2f, "", 1, 1), target).Success);
            Assert.AreEqual(2, countCtx.LastCountAs);

            var convertCtx = new FakeContext(run);
            Assert.IsTrue(ActiveItemEffectRegistry.Apply(
                convertCtx, MakeActiveItem("cv", ItemEffectTypes.ConvertFlavor, 0f, "t_sour", 5, 1), target).Success);
            Assert.AreEqual("t_sour", convertCtx.LastConvertedFlavorId);

            var removeCtx = new FakeContext(run);
            Assert.IsTrue(ActiveItemEffectRegistry.Apply(
                removeCtx, MakeActiveItem("rm", ItemEffectTypes.RemoveFlavor, 0f, "t_sweet", 5, 1), target).Success);
            Assert.AreEqual("t_sweet", removeCtx.LastRemovedFlavorId);

            // 需选目标：无目标时返回失败并提示先选目标。
            ActiveItemUseResult noTarget = ActiveItemEffectRegistry.Apply(
                new FakeContext(run), MakeActiveItem("f", ItemEffectTypes.AddFlavor, 0f, "t_sweet", 2, 1), Array.Empty<ActiveTarget>());
            Assert.IsFalse(noTarget.Success);

            ActiveTarget[] none = Array.Empty<ActiveTarget>();
            var schedCtx = new FakeContext(run);
            Assert.IsTrue(ActiveItemEffectRegistry.Apply(schedCtx, MakeActiveItem("r", ItemEffectTypes.RerollAction), none).Success);
            Assert.AreEqual(1, schedCtx.RerollCalls);
            Assert.IsTrue(ActiveItemEffectRegistry.Apply(schedCtx, MakeActiveItem("b", ItemEffectTypes.ResetBossDebuff), none).Success);
            Assert.AreEqual(1, schedCtx.ResetBossCalls);
            Assert.IsTrue(ActiveItemEffectRegistry.Apply(schedCtx, MakeActiveItem("n", ItemEffectTypes.TimelineExecuteNext), none).Success);
            Assert.AreEqual(1, schedCtx.ExecuteNextCalls);
            Assert.IsTrue(ActiveItemEffectRegistry.Apply(schedCtx, MakeActiveItem("w", ItemEffectTypes.TimelineAddRewardNode, 0f, "act_event"), none).Success);
            Assert.AreEqual("act_event", schedCtx.LastRewardNodeAction);

            // 情境不支持时（返回 false）→ 使用失败。
            var deny = new FakeContext(run, scheduleResult: false);
            Assert.IsFalse(ActiveItemEffectRegistry.Apply(deny, MakeActiveItem("r", ItemEffectTypes.RerollAction), none).Success);
        }

        [Test]
        public void GameRun_RoundTrips_RecipeFlavorCellMaterialRuntimeNodes()
        {
            GameRun run = NewRun();
            Assert.IsTrue(run.AddBonusDish("cookie"), "cookie 应为有效菜品。");
            Assert.IsTrue(run.AddRecipeFlavor(0, 0, "t_sweet"));
            Assert.IsTrue(run.AddCellMaterial(new GridPos(1, 1), "m_gold"));
            run.BeginTimeline("test_timeline", 7f);
            string nodeId = run.AddRuntimeTimelineNode("act_event", new PickFirstRandomStream());
            Assert.IsFalse(string.IsNullOrEmpty(nodeId));

            GameRun restored = GameRun.FromSaveData(run.Tables, run.Database, run.ToSaveData());

            IReadOnlyList<RecipeBookSlot> entries = restored.GetRecipeBookEntries(0);
            Assert.Greater(entries.Count, 0);
            CollectionAssert.Contains(new List<string>(entries[0].ExtraFlavorIds), "t_sweet");

            bool foundMaterial = false;
            foreach (CellMaterialOverride material in restored.CellMaterialOverrides)
            {
                if (material.MaterialId == "m_gold" && material.Pos.X == 1 && material.Pos.Y == 1)
                {
                    foundMaterial = true;
                }
            }

            Assert.IsTrue(foundMaterial);

            bool foundNode = false;
            foreach (RuntimeTimelineNode node in restored.RuntimeTimelineNodes)
            {
                if (node.Id == nodeId)
                {
                    foundNode = true;
                    Assert.AreEqual(1, node.Day);
                    Assert.AreEqual("act_event", node.ActionId);
                }
            }

            Assert.IsTrue(foundNode);
        }

        [Test]
        public void RecipeExtraState_FlowsIntoServedDishInstance()
        {
            var dishes = new List<DishDef>
            {
                GameplayTestFactory.Dish("rice", new[] { "X" }, deliciousness: 5, allowRotate: false, skills: new[] { "base_skill" }),
            };
            var skills = new List<SkillDef>
            {
                new SkillDef("base_skill", "基础", string.Empty, string.Empty),
                new SkillDef("extra_skill", "额外", string.Empty, string.Empty),
            };
            var db = new GameplayDatabase(
                dishes,
                skills,
                new List<FlavorDef>(),
                new List<MaterialDef>(),
                new List<RecipeDef>());

            var rng = new RandomService();
            rng.Init("recipe-flavor-serve");
            // 菜谱条目携带玩家附加的额外风味（模拟调味小票落地后经工厂注入的 RecipeSlotEntry）。
            var entry = new RecipeSlotEntry("rice", new[] { "t_sweet" }, new[] { "extra_skill" }, 2f);
            var slots = new[] { new RecipeSlot("slot0", new[] { entry }) };
            var session = new BattleSession(new GpTable(2, 2), db, rng.Stream("battle"), slots, requiredScore: 1);

            ServeResult result = session.Serve(0);
            Assert.IsTrue(result.Success);
            CollectionAssert.Contains(new List<string>(result.Dish.FlavorIds), "t_sweet",
                "调味小票附加的额外风味应经上菜带入 DishInstance。");
            CollectionAssert.Contains(new List<string>(result.Dish.SkillIds), "extra_skill");
            Assert.AreEqual(2f, result.Dish.PermanentMultBonus, 1e-4f);
        }

        [Test]
        public void RecipeFlavorLimit_ReplacesWhenFull_AndDoubleSlotExpandsLimit()
        {
            GameRun run = NewRun();
            Assert.IsTrue(run.AddBonusDish("cookie"));
            Assert.AreEqual(1, run.FoodFlavorLimit);

            Assert.IsTrue(run.AddRecipeFlavor(0, 0, "t_sweet"));
            Assert.IsTrue(run.AddRecipeFlavor(0, 0, "t_sour"));
            IReadOnlyList<RecipeBookSlot> entries = run.GetRecipeBookEntries(0);
            Assert.AreEqual(1, entries[0].ExtraFlavorIds.Count);
            Assert.AreEqual("t_sour", entries[0].ExtraFlavorIds[0]);

            run.AcquireItem("item_flavor_double_slot", 0);
            Assert.AreEqual(2, run.FoodFlavorLimit);
            Assert.IsTrue(run.AddRecipeFlavor(0, 0, "t_spicy"));
            Assert.AreEqual(2, entries[0].ExtraFlavorIds.Count);
        }

        [Test]
        public void TimelineMutations_ModifyCurrentRuntimeNodes()
        {
            GameRun run = NewRun();
            run.WeekIndex = 3;
            TimelineService.RollWeekTimeline(run, new PickFirstRandomStream());
            Assert.AreEqual(7f, run.TimelineLengthDays);

            TimelineMutationResult delay = PassiveTimelineMutationService.DelayBoss(run, "delay", 1);
            Assert.IsTrue(delay.Changed);
            Assert.AreEqual(8f, run.TimelineLengthDays);

            TimelineMutationResult add = PassiveTimelineMutationService.AddNode(run, "reward", "act_reward", new PickFirstRandomStream());
            Assert.IsTrue(add.Changed);

            TimelineMutationResult week = PassiveTimelineMutationService.DecreaseWeek(run, "week", 99);
            Assert.IsTrue(week.Changed);
            Assert.AreEqual(1, run.WeekIndex);
        }

        private sealed class FakeContext : IActiveUseContext
        {
            private readonly bool _canClear;
            private readonly bool _canServe;
            private readonly bool _scheduleResult;

            public FakeContext(GameRun run, bool canClear = true, bool canServe = true, bool scheduleResult = true)
            {
                Run = run;
                _canClear = canClear;
                _canServe = canServe;
                _scheduleResult = scheduleResult;
            }

            public int ClearCalls { get; private set; }

            public int ServeCalls { get; private set; }

            public int RerollCalls { get; private set; }

            public int ResetBossCalls { get; private set; }

            public int ExecuteNextCalls { get; private set; }

            public string LastFlavorId { get; private set; }

            public string LastMaterialId { get; private set; }

            public string LastRewardNodeAction { get; private set; }

            public int LastCountAs { get; private set; }

            public string LastConvertedFlavorId { get; private set; }

            public string LastRemovedFlavorId { get; private set; }

            public ActiveUseContextKind ContextKind => ActiveUseContextKind.Shop;

            public GameRun Run { get; }

            public IReadOnlyList<ActiveTarget> EnumerateTargets(cfg.ItemTargetKind targetKind) => Array.Empty<ActiveTarget>();

            public bool ClearBoard()
            {
                ClearCalls++;
                return _canClear;
            }

            public bool ExtraServe()
            {
                ServeCalls++;
                return _canServe;
            }

            public bool AddPermanentScore(ActiveTarget target, float amount) => true;

            public bool DuplicateDish(ActiveTarget target, string randomKey) => true;

            public bool DestroyDish(ActiveTarget target) => true;

            public bool MultiplyScore(ActiveTarget target, float multiplier) => true;

            public bool AddCountAs(ActiveTarget target, int amount)
            {
                LastCountAs = amount;
                return true;
            }

            public bool AddFlavorToDish(ActiveTarget target, string flavorId)
            {
                LastFlavorId = flavorId;
                return true;
            }

            public bool RemoveFlavorFromDish(ActiveTarget target, string flavorId)
            {
                LastRemovedFlavorId = flavorId;
                return true;
            }

            public bool ConvertFlavorOnDish(ActiveTarget target, string toFlavorId)
            {
                LastConvertedFlavorId = toFlavorId;
                return true;
            }

            public bool ConvertDishCategory(ActiveTarget target, string category) => false;

            public bool AddMaterialToCell(ActiveTarget target, string materialId)
            {
                LastMaterialId = materialId;
                return true;
            }

            public bool GenerateDish(ActiveTarget target, string dishId, string randomKey) => true;

            public bool RerollCurrentAction()
            {
                RerollCalls++;
                return _scheduleResult;
            }

            public bool ResetWeekBoss()
            {
                ResetBossCalls++;
                return _scheduleResult;
            }

            public bool ExecuteNextTimelineNode()
            {
                ExecuteNextCalls++;
                return _scheduleResult;
            }

            public bool AddRewardNodeToTimeline(string actionId)
            {
                LastRewardNodeAction = actionId;
                return _scheduleResult;
            }
        }

        private sealed class PickFirstRandomStream : IRandomStream
        {
            public RngState State { get; set; }

            public uint NextUInt() => 0;

            public ulong NextULong() => 0;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => probability > 0d;

            public int WeightedPickIndex(IReadOnlyList<float> weights) => 0;

            public T Pick<T>(IReadOnlyList<T> items) => items[0];

            public void Shuffle<T>(IList<T> items)
            {
            }
        }
    }
}
