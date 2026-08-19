using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TableFragmentAttachRotationTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private string _characterId;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
            _characterId = _tables.TbCharacter.DataList.First().Id;
        }

        [Test]
        public void CollectAttachableRotations_UsesRotatedShape_WhenUnrotatedBarCannotFit()
        {
            DiningTable board = new DiningTable(8, 8, new[]
            {
                new GridPos(0, 0), new GridPos(1, 0),
                new GridPos(0, 1), new GridPos(1, 1),
                new GridPos(0, 2), new GridPos(1, 2),
            });
            TableFragmentDef bar = Fragment("XXX");

            Assert.That(
                TableFragmentBuilder.CanAttachAnywhereLocalBounds(board, bar, 3, 3),
                Is.False,
                "未旋转的 3x1 无法贴上已满高的 2x3 餐桌");
            Assert.That(
                TableFragmentBuilder.CanAttachAnywhereLocalBounds(board, bar.Rotated(1), 3, 3),
                Is.True,
                "顺时针 90° 后的 1x3 应能填入剩余一列");

            List<int> rotations = TableFragmentBuilder.CollectAttachableRotations(board, bar, 3, 3);
            Assert.That(rotations, Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void RewardFragmentChoices_OnlyRollAttachableRotations()
        {
            GameRun run = CreateRun("fragment-attach-rotation-reward");
            cfg.RewardSlot slot = _tables.TbRewardSlot.GetOrDefault("fragment_choice_3");
            Assert.That(slot, Is.Not.Null);

            List<RewardChoice> choices = RewardPoolService.RollChoices(
                new RewardContext(_tables, run, null, null, new Xoshiro256SS(4242UL)),
                slot);

            Assert.That(choices, Is.Not.Empty);
            foreach (RewardChoice choice in choices)
            {
                Assert.That(choice.Kind, Is.EqualTo(cfg.RewardKind.FragmentChoice));
                TableFragmentDef fragment = _database.GetFragment(choice.Id);
                Assert.That(fragment, Is.Not.Null);
                List<int> attachable = run.GetAttachableFragmentRotations(fragment);
                Assert.That(attachable, Is.Not.Empty);
                Assert.That(
                    attachable,
                    Does.Contain(choice.FragmentRotation),
                    $"{choice.Id} rolled rotation {choice.FragmentRotation}, attachable={string.Join(",", attachable)}");
            }
        }

        [Test]
        public void ShopPendingPack_OnlyRollsAttachableRotations()
        {
            GameRun run = CreateRun("fragment-attach-rotation-shop");
            List<string> pack = ShopService.RollFragmentPack(run);
            Assert.That(pack, Is.Not.Empty);

            run.SetPendingFragmentPack(pack);

            Assert.That(run.PendingFragmentPackRotations.Count, Is.EqualTo(run.PendingFragmentPack.Count));
            for (int i = 0; i < run.PendingFragmentPack.Count; i++)
            {
                TableFragmentDef fragment = _database.GetFragment(run.PendingFragmentPack[i]);
                Assert.That(fragment, Is.Not.Null);
                List<int> attachable = run.GetAttachableFragmentRotations(fragment);
                Assert.That(attachable, Is.Not.Empty);
                Assert.That(attachable, Does.Contain(run.PendingFragmentPackRotations[i]));
            }
        }

        private GameRun CreateRun(string seed)
        {
            return new GameRun(
                _tables,
                _database,
                _characterId,
                seed,
                execution: RunExecutionEnvironment.CreateIsolated(_tables, seed));
        }

        private static TableFragmentDef Fragment(params string[] rows)
        {
            return new TableFragmentDef("test_bar", rows, 10, 100, 1f);
        }
    }
}
