using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 被动道具「钩子模型」框架测试：注册表完整性、per-instance 状态存读档、LuckyEventGuarantee 保底。
    /// </summary>
    public class PassiveItemModelTests
    {
        private static cfg.Tables LoadTables()
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "Config");
            return new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(root, name + ".json"))));
        }

        private static GameRun NewRun(cfg.Tables tables)
        {
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
            return new GameRun(tables, database, "glutton_dog", "passive-model-test", 1);
        }

        [Test]
        public void Registry_HasModelForEveryPassiveItem()
        {
            cfg.Tables tables = LoadTables();
            foreach (cfg.PassiveItem item in tables.TbPassiveItem.DataList)
            {
                Assert.IsTrue(
                    PassiveItemModelRegistry.HasModel(item.Id),
                    $"被动道具 '{item.Id}'(effectType={item.EffectType}) 缺少对应 PassiveItemModel。");
            }
        }

        [Test]
        public void PassiveModel_BoundOnAcquire_AndClearedOnRemove()
        {
            cfg.Tables tables = LoadTables();
            GameRun run = NewRun(tables);
            run.AcquireItem("item_discount_food", 0);

            PassiveItemModel model = FindModel(run, "item_discount_food");
            Assert.IsNotNull(model, "获得被动道具后应绑定模型。");

            Assert.IsTrue(run.RemoveItem("item_discount_food"));
            Assert.IsNull(FindModel(run, "item_discount_food"), "移除后模型应随之消失。");
        }

        [Test]
        public void GuaranteeStreak_PersistsThroughSaveLoad()
        {
            cfg.Tables tables = LoadTables();
            GameRun run = NewRun(tables);
            run.AcquireItem("item_lucky_guarantee", 0);

            PassiveItemModel model = FindModel(run, "item_lucky_guarantee");
            Assert.IsNotNull(model);
            model.IncrementEventGuaranteeStreak();
            model.IncrementEventGuaranteeStreak();
            model.IncrementEventGuaranteeStreak();
            Assert.AreEqual(3, model.EventGuaranteeStreak);

            RunSaveData save = run.ToSaveData();
            GameRun restored = GameRun.FromSaveData(tables, run.Database, save);
            PassiveItemModel restoredModel = FindModel(restored, "item_lucky_guarantee");
            Assert.IsNotNull(restoredModel);
            Assert.AreEqual(3, restoredModel.EventGuaranteeStreak, "保底计数应随档保存并恢复。");
        }

        [Test]
        public void Guarantee_ForcesRewardWhenStreakReached_ThenResets()
        {
            cfg.Tables tables = LoadTables();
            GameRun run = NewRun(tables);
            run.AcquireItem("item_lucky_guarantee", 0); // every = 4

            PassiveItemModel model = FindModel(run, "item_lucky_guarantee");
            Assert.IsNotNull(model);
            // 预置到 every-1 = 3，下一次 act_event 应强制 Reward。
            model.IncrementEventGuaranteeStreak();
            model.IncrementEventGuaranteeStreak();
            model.IncrementEventGuaranteeStreak();

            var rng = new RandomService();
            rng.Init(2026UL);
            cfg.GameEvent ev = EventService.RollActionEventWithGuarantee(run, rng.Stream("evt"));

            Assert.IsNotNull(ev);
            Assert.AreEqual(cfg.ActionBehavior.Reward, ev.EventType, "保底触发时应强制抽到奖励事件。");
            Assert.AreEqual(0, model.EventGuaranteeStreak, "保底触发后计数清零。");
        }

        [Test]
        public void Guarantee_NoModel_WhenItemNotHeld()
        {
            cfg.Tables tables = LoadTables();
            GameRun run = NewRun(tables);
            var rng = new RandomService();
            rng.Init(7UL);

            // 未持有保底道具：仍能正常抽取，不应抛异常，也没有保底模型。
            cfg.GameEvent ev = EventService.RollActionEventWithGuarantee(run, rng.Stream("evt"));
            Assert.IsNotNull(ev);
            Assert.IsNull(FindModel(run, "item_lucky_guarantee"));
        }

        private static PassiveItemModel FindModel(GameRun run, string itemId)
        {
            foreach (PassiveItemModel m in run.PassiveModels)
            {
                if (m.ItemId == itemId)
                {
                    return m;
                }
            }

            return null;
        }
    }
}
