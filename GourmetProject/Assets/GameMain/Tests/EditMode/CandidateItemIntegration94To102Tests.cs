using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CandidateItemIntegration94To102Tests
    {
        private cfg.Tables _tables;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
        }

        [Test]
        public void TransferExtraTargets_OnServe_AddsConfiguredTargetsToTransferRequest()
        {
            TransferFixture fixture = CreateTransferFixture(SkillTrigger.OnServe);
            GameRun run = CreateRun(fixture.Database);
            BindCandidate<SweetTransferExtraTargetsModel>(
                run,
                "item_transfer_extra_targets",
                value: 2f);
            int itemExtraTargets = new ItemRuntime(run).SweetTransferExtraTargetCount();

            ServeRuleResolver.ServeResolveResult baseline = ServeRuleResolver.ResolveOnServe(
                fixture.Table,
                fixture.Database,
                EmptyScoreHistory.Instance,
                fixture.Source,
                currentHappyCakeLayers: 0,
                itemExtraTargetCount: 0);
            ServeRuleResolver.ServeResolveResult enhanced = ServeRuleResolver.ResolveOnServe(
                fixture.Table,
                fixture.Database,
                EmptyScoreHistory.Instance,
                fixture.Source,
                currentHappyCakeLayers: 0,
                itemExtraTargetCount: itemExtraTargets);

            Assert.That(itemExtraTargets, Is.EqualTo(2));
            Assert.That(baseline.TransferRequests, Has.Count.EqualTo(1));
            Assert.That(baseline.TransferRequests[0].Count, Is.EqualTo(1));
            Assert.That(enhanced.TransferRequests, Has.Count.EqualTo(1));
            Assert.That(enhanced.TransferRequests[0].CandidateTargetIds, Has.Count.EqualTo(4));
            Assert.That(enhanced.TransferRequests[0].Count, Is.EqualTo(3));
        }

        [Test]
        public void TransferExtraTargets_OnSettle_ResolvesPayloadForConfiguredAdditionalDishes()
        {
            TransferFixture fixture = CreateTransferFixture(SkillTrigger.OnSettle);
            GameRun run = CreateRun(fixture.Database);
            BindCandidate<SweetTransferExtraTargetsModel>(
                run,
                "item_transfer_extra_targets",
                value: 2f);
            int itemExtraTargets = new ItemRuntime(run).SweetTransferExtraTargetCount();

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Table,
                fixture.Database,
                sweetTransferExtraTargetCount: itemExtraTargets);
            DishScore[] targets = result.DishScores
                .Where(score => score.DishInstanceId != fixture.Source.Id)
                .ToArray();

            Assert.That(itemExtraTargets, Is.EqualTo(2));
            Assert.That(targets, Has.Length.EqualTo(4));
            Assert.That(targets.Count(score => Math.Abs(score.FlatBonus.ToDouble() - 7d) < 0.0001d),
                Is.EqualTo(3));
            Assert.That(targets.Count(score => Math.Abs(score.FlatBonus.ToDouble()) < 0.0001d),
                Is.EqualTo(1));
        }

        [Test]
        public void ActiveItemCountFlatAll_RuntimeSourceReadsLatestActiveItemCount()
        {
            GameRun run = CreateRun(EmptyDatabase());
            BindCandidate<ActiveItemCountFlatAllModel>(
                run,
                "item_active_count_flat_all",
                value: 10f,
                param: "basis:active-items");
            var calculator = new ScoreCalculator(
                effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));
            int beforeCount = run.ActiveItemCount;

            ScoreResult before = calculator.Calculate(OneDishTable(), EmptyDatabase());
            AddActiveItemState(run);
            ScoreResult after = calculator.Calculate(OneDishTable(), EmptyDatabase());

            Assert.That(before.DishScores.Single().FlatBonus.ToDouble(), Is.EqualTo(beforeCount * 10d));
            Assert.That(run.ActiveItemCount, Is.EqualTo(beforeCount + 1));
            Assert.That(after.DishScores.Single().FlatBonus.ToDouble(), Is.EqualTo((beforeCount + 1) * 10d));
        }

        [Test]
        public void EmptyActiveSlotMultAll_RuntimeSourceReadsLatestEmptySlotCount()
        {
            GameRun run = CreateRun(EmptyDatabase());
            BindCandidate<EmptyActiveSlotMultAllModel>(
                run,
                "item_empty_active_slot_mult_all",
                value: 0.1f,
                param: "basis:empty-active-slots");
            var calculator = new ScoreCalculator(
                effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));
            int emptyBefore = run.ActiveSlotCapacity - run.ActiveItemCount;
            Assert.That(emptyBefore, Is.GreaterThan(0));

            ScoreResult before = calculator.Calculate(OneDishTable(), EmptyDatabase());
            AddActiveItemState(run);
            int emptyAfter = run.ActiveSlotCapacity - run.ActiveItemCount;
            ScoreResult after = calculator.Calculate(OneDishTable(), EmptyDatabase());

            Assert.That(emptyAfter, Is.EqualTo(emptyBefore - 1));
            Assert.That(before.DishScores.Single().Multiplier.ToDouble(),
                Is.EqualTo(1d + emptyBefore * 0.1d).Within(0.0001d));
            Assert.That(after.DishScores.Single().Multiplier.ToDouble(),
                Is.EqualTo(1d + emptyAfter * 0.1d).Within(0.0001d));
        }

        [Test]
        public void GoldStepFlatAll_RuntimeSourceReadsLatestGoldInCompleteSteps()
        {
            GameRun run = CreateRun(EmptyDatabase());
            BindCandidate<GoldStepFlatAllModel>(
                run,
                "item_gold_per5_flat_all",
                value: 1f,
                param: "basis:gold;every:5");
            var calculator = new ScoreCalculator(
                effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));

            run.Gold = 4;
            ScoreResult belowStep = calculator.Calculate(OneDishTable(), EmptyDatabase());
            run.Gold = 15;
            ScoreResult threeSteps = calculator.Calculate(OneDishTable(), EmptyDatabase());

            Assert.That(belowStep.DishScores.Single().FlatBonus.ToDouble(), Is.Zero);
            Assert.That(threeSteps.DishScores.Single().FlatBonus.ToDouble(), Is.EqualTo(3d));
        }

        private GameRun CreateRun(GameplayDatabase database)
        {
            var run = new GameRun(_tables, database, "glutton_dog", "candidate-item-94-102", 1);
            MutableItems(run).Clear();
            return run;
        }

        private void AddActiveItemState(GameRun run)
        {
            string activeItemId = _tables.TbActiveItem.DataList.First().Id;
            MutableItems(run).Add(new RunItemState(activeItemId, 1));
        }

        private static T BindCandidate<T>(
            GameRun run,
            string itemId,
            float value,
            string param = "")
            where T : PassiveItemModel, new()
        {
            var state = new RunItemState(itemId, 1);
            var model = new T();
            state.Model = model;
            MutableItems(run).Add(state);

            string json = "{"
                + $"\"id\":\"{itemId}\","
                + "\"name\":\"候选装饰品\","
                + "\"desc\":\"测试\","
                + "\"quality\":0,"
                + "\"specialTags\":0,"
                + $"\"effectValue\":{value.ToString(CultureInfo.InvariantCulture)},"
                + $"\"effectParam\":\"{param}\","
                + "\"baseWeight\":100,"
                + "\"hiddenRange\":{\"min\":10,\"max\":80},"
                + "\"targetScoreHiddenOffset\":0,"
                + "\"dishHiddenOffset\":0,"
                + "\"passiveItemHiddenOffset\":0,"
                + "\"fragmentHiddenOffset\":0,"
                + "\"termId\":\"\","
                + "\"price\":40}";
            ItemDefinition definition = ItemDefinition.From(new cfg.PassiveItem(JSON.Parse(json)));
            model.Bind(run, definition, state);
            return model;
        }

        private static List<RunItemState> MutableItems(GameRun run)
        {
            Assert.That(run.Items, Is.InstanceOf<List<RunItemState>>());
            return (List<RunItemState>)run.Items;
        }

        private static DiningTable OneDishTable()
        {
            var table = new DiningTable(1, 1);
            table.Place(CreateDish(1, "dish", 0, 0));
            return table;
        }

        private static TransferFixture CreateTransferFixture(SkillTrigger trigger)
        {
            const string skillId = "skill_candidate_transfer";
            var payload = new SkillRuleDef(
                "candidate_payload",
                skillId,
                0,
                trigger,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                new[] { 7f },
                Array.Empty<string>());
            var transfer = new SkillRuleDef(
                "candidate_transfer",
                skillId,
                1,
                trigger,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.Other,
                1,
                new[] { 0f },
                Array.Empty<string>());
            var skill = new SkillDef(
                skillId,
                "测试甜蜜传递",
                string.Empty,
                Array.Empty<string>(),
                new[] { payload, transfer },
                new[] { "美味值 +7", "甜蜜传递" });

            var table = new DiningTable(5, 1);
            DishInstance source = CreateDish(1, "source", 0, 0, new[] { skillId });
            DishInstance first = CreateDish(2, "first", 1, 0);
            DishInstance second = CreateDish(3, "second", 2, 0);
            DishInstance third = CreateDish(4, "third", 3, 0);
            DishInstance fourth = CreateDish(5, "fourth", 4, 0);
            table.Place(source);
            table.Place(first);
            table.Place(second);
            table.Place(third);
            table.Place(fourth);
            var database = new GameplayDatabase(
                new[] { source.Def, first.Def, second.Def, third.Def, fourth.Def },
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new TransferFixture(table, database, source);
        }

        private static DishInstance CreateDish(
            int instanceId,
            string id,
            int x,
            int y,
            string[] skills = null)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                id,
                id,
                10,
                shape,
                0,
                0,
                1f,
                skills ?? Array.Empty<string>(),
                string.Empty,
                allowRotate: false,
                baseId: id);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                skills ?? Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static GameplayDatabase EmptyDatabase()
            => new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());

        private sealed class TransferFixture
        {
            public TransferFixture(DiningTable table, GameplayDatabase database, DishInstance source)
            {
                Table = table;
                Database = database;
                Source = source;
            }

            public DiningTable Table { get; }

            public GameplayDatabase Database { get; }

            public DishInstance Source { get; }
        }
    }
}
