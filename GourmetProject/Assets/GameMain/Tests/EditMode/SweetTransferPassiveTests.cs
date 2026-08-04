using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SweetTransferPassiveTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void SettlementTransfers_AddMultiplierZonePerTarget_AndDriveGoldCounter()
        {
            GameRun run = CreateRun();
            PassiveItemModel targetModel = AttachPassive(run, "item_transfer_target_mult", 0.1f);
            PassiveItemModel sourceModel = AttachPassive(run, "item_transfer_source_mult", 0.1f);
            PassiveItemModel goldModel = AttachPassive(run, "item_gold_on_transfer", 10f, "count:10");
            int goldBefore = run.Gold;

            for (int settlement = 0; settlement < 5; settlement++)
            {
                TransferFixture fixture = CreateTransferFixture(run, settlement);
                ScoreResult result = fixture.Session.Settle();

                Assert.That(result.SkillTransfers, Has.Count.EqualTo(2));
                Assert.That(
                    result.SkillTransfers.Select(transfer => transfer.TargetInstanceId),
                    Is.EquivalentTo(new[] { fixture.FirstTarget.Id, fixture.SecondTarget.Id }));
                Assert.That(fixture.FirstTarget.PermanentMultBonus, Is.EqualTo(1.1f).Within(0.0001f));
                Assert.That(fixture.SecondTarget.PermanentMultBonus, Is.EqualTo(1.1f).Within(0.0001f));
                Assert.That(
                    fixture.Source.PermanentMultBonus,
                    Is.EqualTo(1.2f).Within(0.0001f),
                    "同一请求传给两个目标时，来源应按两次实际传递各加0.1倍率。");
            }

            Assert.That(targetModel.InfoText, Is.EqualTo("10"));
            Assert.That(sourceModel.InfoText, Is.EqualTo("10"));
            Assert.That(goldModel.InfoText, Is.EqualTo("10"));
            Assert.That(goldModel.IsIconUsed, Is.True);
            Assert.That(run.Gold, Is.EqualTo(goldBefore + 10));

            CreateTransferFixture(run, settlement: 5).Session.Settle();
            Assert.That(run.Gold, Is.EqualTo(goldBefore + 10), "传递分红只能领取一次。");
            Assert.That(goldModel.InfoText, Is.EqualTo("10"), "领取后不再累计无效进度。");
        }

        [Test]
        public void RemovedTransferCounter_DoesNotKeepListeningToExistingBattleSession()
        {
            GameRun run = CreateRun();
            PassiveItemModel goldModel = AttachPassive(run, "item_gold_on_transfer", 10f, "count:1");
            TransferFixture fixture = CreateTransferFixture(run, settlement: 0);
            int goldBefore = run.Gold;

            Assert.That(run.RemoveItem("item_gold_on_transfer"), Is.True);
            fixture.Session.Settle();

            Assert.That(run.Gold, Is.EqualTo(goldBefore));
            Assert.That(goldModel.InfoText, Is.EqualTo("0"));
        }

        private TransferFixture CreateTransferFixture(GameRun run, int settlement)
        {
            var table = new DiningTable(1, 6);
            DishInstance firstTarget = Place(table, "mantou", 1, 0, 0);
            DishInstance secondTarget = Place(table, "mantou", 2, 0, 2);
            DishInstance source = Place(table, "fruit_candy", 3, 0, 4);

            var random = new RandomService();
            random.Init("sweet-transfer-passive-tests");
            IRandomStream rng = random.DomainStream(SeedDomains.Combat, $"settlement_{settlement}");
            var session = new BattleSession(
                table,
                _database,
                rng,
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);
            var itemRuntime = new ItemRuntime(run);
            session.SweetTransferTargetMultiplier = itemRuntime.SweetTransferTargetMultiplier();
            session.SweetTransferSourceMultiplier = itemRuntime.SweetTransferSourceMultiplier();
            foreach (PassiveItemModel model in run.PassiveModels)
            {
                model.ApplyToBattle(session);
            }

            return new TransferFixture(session, source, firstTarget, secondTarget);
        }

        private DishInstance Place(
            DiningTable table,
            string dishId,
            int instanceId,
            int x,
            int y)
        {
            DishDef definition = _database.GetDish(dishId);
            Assert.That(definition, Is.Not.Null, dishId);
            var placement = new Placement(definition.Shape, 0, new GridPos(x, y));
            Assert.That(table.CanPlace(placement.Orientation, placement.Origin), Is.True, dishId);
            var instance = new DishInstance(
                instanceId,
                definition,
                placement,
                definition.SkillIds,
                Array.Empty<string>());
            table.Place(instance);
            return instance;
        }

        private GameRun CreateRun()
        {
            return new GameRun(
                _tables,
                _database,
                _tables.TbCharacter.DataList.First().Id,
                "sweet-transfer-passive-tests");
        }

        private static PassiveItemModel AttachPassive(
            GameRun run,
            string itemId,
            float effectValue,
            string effectParam = "")
        {
            cfg.PassiveItem configured = CreatePassiveItem(itemId, effectValue, effectParam);
            var state = new RunItemState(itemId, 1);
            PassiveItemModel model = PassiveItemModelRegistry.Create(itemId);
            model.Bind(run, ItemDefinition.From(configured), state);
            state.Model = model;
            ((List<RunItemState>)run.Items).Add(state);
            return model;
        }

        private static cfg.PassiveItem CreatePassiveItem(
            string itemId,
            float effectValue,
            string effectParam)
        {
            string value = effectValue.ToString(CultureInfo.InvariantCulture);
            string json = $@"{{
                ""id"":""{itemId}"",
                ""name"":""测试装饰品和消耗品"",
                ""desc"":"""",
                ""quality"":0,
                ""specialTags"":0,
                ""effectValue"":{value},
                ""effectParam"":""{effectParam}"",
                ""baseWeight"":1,
                ""hiddenRange"":{{""min"":0,""max"":0}},
                ""targetScoreHiddenOffset"":0,
                ""dishHiddenOffset"":0,
                ""passiveItemHiddenOffset"":0,
                ""fragmentHiddenOffset"":0,
                ""termId"":"""",
                ""price"":1
            }}";
            return cfg.PassiveItem.DeserializePassiveItem(JSON.Parse(json));
        }

        private readonly struct TransferFixture
        {
            public TransferFixture(
                BattleSession session,
                DishInstance source,
                DishInstance firstTarget,
                DishInstance secondTarget)
            {
                Session = session;
                Source = source;
                FirstTarget = firstTarget;
                SecondTarget = secondTarget;
            }

            public BattleSession Session { get; }

            public DishInstance Source { get; }

            public DishInstance FirstTarget { get; }

            public DishInstance SecondTarget { get; }
        }
    }
}
