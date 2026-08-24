using System;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BossDebuffPreviewTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
        }

        [Test]
        public void PreviewedBossDebuff_IsReusedWhenNodeExecutes()
        {
            GameRun run = CreateRun("boss-preview-lock");
            cfg.TimelineNode node = BossNodes(run).First();
            cfg.BossDebuff preview = BossService.PreviewBossDebuff(run, node);
            cfg.GameAction action = TimelineService.NodeAction(run, node);
            var context = new ActionExecutionContext(action) { SourceKey = node.Id };

            ActionOutcome outcome = ActionExecutor.Execute(
                run,
                context,
                run.Random.DomainStream(SeedDomains.Effect, "boss-preview-lock-test"));

            Assert.That(preview, Is.Not.Null);
            Assert.That(outcome.BossDebuffId, Is.EqualTo(preview.Id));
        }

        [Test]
        public void PreviewedBossDebuffs_AreReservedAndSurviveSaveRestore()
        {
            GameRun run = CreateRun("boss-preview-save");
            cfg.TimelineNode[] nodes = BossNodes(run);
            cfg.BossDebuff first = BossService.PreviewBossDebuff(run, nodes[0]);
            cfg.BossDebuff second = BossService.PreviewBossDebuff(run, nodes[1]);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));

            RunSaveData save = run.ToSaveData();
            save.RandomSnapshot = run.Random.Capture();
            GameRun restored = GameRun.FromSaveData(_tables, _database, save);
            cfg.TimelineNode[] restoredNodes = BossNodes(restored);

            Assert.That(BossService.PreviewBossDebuff(restored, restoredNodes[0]).Id, Is.EqualTo(first.Id));
            Assert.That(BossService.PreviewBossDebuff(restored, restoredNodes[1]).Id, Is.EqualTo(second.Id));
        }

        [Test]
        public void Reroll_ReplacesOnlyTargetNodeLock()
        {
            GameRun run = CreateRun("boss-preview-reroll");
            cfg.TimelineNode[] nodes = BossNodes(run);
            cfg.BossDebuff first = BossService.PreviewBossDebuff(run, nodes[0]);
            cfg.BossDebuff second = BossService.PreviewBossDebuff(run, nodes[1]);

            Assert.That(run.RerollBossDebuffForNode(nodes[0].Id), Is.True);
            cfg.BossDebuff rerolled = BossService.PreviewBossDebuff(run, nodes[0]);

            Assert.That(rerolled.Id, Is.Not.EqualTo(first.Id));
            Assert.That(BossService.PreviewBossDebuff(run, nodes[1]).Id, Is.EqualTo(second.Id));
        }

        private GameRun CreateRun(string seed)
        {
            var run = new GameRun(_tables, _database, string.Empty, seed);
            cfg.Timeline timeline = _tables.TbTimeline.Get("tl_1");
            run.BeginTimeline(timeline.Id, timeline.BaseLengthDays, TimelineService.BuildTimelineNodes(timeline));
            return run;
        }

        private static cfg.TimelineNode[] BossNodes(GameRun run)
        {
            return TimelineService.GetNodes(run)
                .Where(node => FoodService.IsBossAction(run.Tables, TimelineService.NodeAction(run, node)))
                .ToArray();
        }
    }
}
