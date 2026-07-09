using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 主动道具子系统测试：全局消耗槽（基础 2 + ExtraActiveSlot）、槽满折金币、
    /// 主动使用序号可复现、targetKind→情境推导、以及情境无关的 Apply 派发。
    /// </summary>
    public class ItemActiveTests
    {
        private const string ActiveItemId = "item_family_pack";

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

        private static cfg.Item MakeItem(string id, string effectType, float value = 0f)
        {
            string v = value.ToString(CultureInfo.InvariantCulture);
            string json =
                "{\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"desc\":\"\",\"kind\":1,\"quality\":0," +
                "\"specialTags\":\"\",\"triggerTiming\":3,\"effectType\":\"" + effectType + "\"," +
                "\"effectValue\":" + v + ",\"effectParam\":\"\",\"baseWeight\":1," +
                "\"hiddenRange\":{\"min\":0,\"max\":0},\"targetKind\":0,\"targetCount\":0}";
            return new cfg.Item(JSON.Parse(json));
        }

        [Test]
        public void ActiveSlots_FullConvertsToGold()
        {
            GameRun run = NewRun();
            Assert.AreEqual(GameRun.BaseActiveSlots, run.ActiveSlotCapacity);

            Assert.AreEqual(ItemAcquireOutcome.Stacked, run.AcquireItem(ActiveItemId, 50).Outcome);
            Assert.AreEqual(ItemAcquireOutcome.Stacked, run.AcquireItem(ActiveItemId, 50).Outcome);
            Assert.AreEqual(2, run.ActiveItemCount);

            int goldBefore = run.Gold;
            ItemAcquireResult full = run.AcquireItem(ActiveItemId, 50);
            Assert.AreEqual(ItemAcquireOutcome.ConvertedToGold, full.Outcome);
            Assert.AreEqual(2, run.ActiveItemCount, "槽满后不再新增实例");
            Assert.AreEqual(goldBefore + 50, run.Gold, "槽满折算兜底金币");
        }

        [Test]
        public void ExtraActiveSlot_IncreasesCapacity()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_extra_active_slots", 0); // 被动 +2 槽
            Assert.AreEqual(GameRun.BaseActiveSlots + 2, run.ActiveSlotCapacity);

            for (int i = 0; i < GameRun.BaseActiveSlots + 2; i++)
            {
                Assert.AreEqual(ItemAcquireOutcome.Stacked, run.AcquireItem(ActiveItemId, 0).Outcome);
            }

            Assert.AreEqual(ItemAcquireOutcome.ConvertedToGold, run.AcquireItem(ActiveItemId, 10).Outcome);
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
            Assert.IsTrue(ItemActiveUsage.RequiresTarget(cfg.ItemTargetKind.BoardDish));

            // 棋盘菜只在战斗；菜谱菜任意情境。
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.BoardDish, ActiveUseContextKind.Battle));
            Assert.IsFalse(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.BoardDish, ActiveUseContextKind.Shop));
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.RecipeDish, ActiveUseContextKind.Battle));
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.RecipeDish, ActiveUseContextKind.Map));

            // 编辑类目标：奖励界面不开放。
            Assert.IsFalse(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.FlavorSlot, ActiveUseContextKind.Reward));
            Assert.IsTrue(ItemActiveUsage.IsUsableIn(cfg.ItemTargetKind.FlavorSlot, ActiveUseContextKind.Shop));
        }

        [Test]
        public void HoldingViews_SplitByKind()
        {
            GameRun run = NewRun();
            run.AcquireItem("item_discount_food", 0); // 被动
            run.AcquireItem(ActiveItemId, 0);         // 主动

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
            Assert.AreEqual(1, actives);
        }

        [Test]
        public void Apply_DispatchesByEffectType()
        {
            GameRun run = NewRun();
            ActiveTarget[] none = Array.Empty<ActiveTarget>();

            var ctx = new FakeContext(run);
            ActiveItemUseResult clear = ActiveItemEffectRegistry.Apply(ctx, MakeItem("t_clear", "ClearBoard"), none);
            Assert.IsTrue(clear.Success);
            Assert.IsTrue(clear.BoardChanged);
            Assert.AreEqual(1, ctx.ClearCalls);

            var ctxNoClear = new FakeContext(run, canClear: false);
            ActiveItemUseResult failClear = ActiveItemEffectRegistry.Apply(ctxNoClear, MakeItem("t_clear", "ClearBoard"), none);
            Assert.IsFalse(failClear.Success);

            int goldBefore = run.Gold;
            ActiveItemUseResult gold = ActiveItemEffectRegistry.Apply(new FakeContext(run), MakeItem("t_gold", "GoldNow", 50f), none);
            Assert.IsTrue(gold.Success);
            Assert.AreEqual(goldBefore + 50, run.Gold);

            ActiveItemUseResult noop = ActiveItemEffectRegistry.Apply(new FakeContext(run), MakeItem("t_unknown", "SomeFutureOp"), none);
            Assert.IsTrue(noop.Success);
            Assert.IsFalse(noop.BoardChanged);
        }

        private sealed class FakeContext : IActiveUseContext
        {
            private readonly bool _canClear;
            private readonly bool _canServe;

            public FakeContext(GameRun run, bool canClear = true, bool canServe = true)
            {
                Run = run;
                _canClear = canClear;
                _canServe = canServe;
            }

            public int ClearCalls { get; private set; }

            public int ServeCalls { get; private set; }

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
        }
    }
}
