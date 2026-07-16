using System.Collections.Generic;
using System.IO;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ItemPoolServiceTests
    {
        private cfg.Tables _tables;
        private GameRun _run;

        [SetUp]
        public void SetUp()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(dir, name + ".json"))));
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(_tables);
            string characterId = _tables.TbCharacter.DataList[0].Id;
            _run = new GameRun(_tables, db, characterId, "item-pool-test-seed", weekIndex: 1);
        }

        private static IRandomStream Rng(ulong seed = 0xDEADBEEFCAFEBABEUL) => new Xoshiro256SS(seed);

        private RewardContext Context(ulong seed = 0xDEADBEEFCAFEBABEUL)
        {
            return new RewardContext(_tables, _run, week: null, package: null, Rng(seed), progress: null);
        }

        [Test]
        public void DefaultPassiveRoll_ExcludesNegativeTaggedItems()
        {
            List<string> rolled = ItemPoolService.Roll(_tables, _run, cfg.ItemKind.Passive, Rng(), count: 20, hidden: 15, distanceFloor: 5);
            foreach (string itemId in rolled)
            {
                ItemDefinition item = ItemDefinition.Get(_tables, itemId, cfg.ItemKind.Passive);
                Assert.NotNull(item, itemId);
                Assert.IsFalse(item.IsNegative, $"默认被动池不应抽到负面道具：{itemId}");
            }
        }

        [Test]
        public void NegativePassiveRoll_OnlyIncludesNegativeTaggedItems()
        {
            List<string> rolled = ItemPoolService.Roll(
                _tables, _run, cfg.ItemKind.Passive, Rng(0x1234), count: 5, hidden: 15, distanceFloor: 5,
                requiredTag: cfg.ItemSpecialTag.Negative);
            Assert.IsNotEmpty(rolled, "负面被动池在隐藏分 15 下应有候选");
            foreach (string itemId in rolled)
            {
                ItemDefinition item = ItemDefinition.Get(_tables, itemId, cfg.ItemKind.Passive);
                Assert.NotNull(item, itemId);
                Assert.IsTrue(item.IsNegative, $"显式负面池只应抽到负面道具：{itemId}");
            }
        }

        [Test]
        public void RewardPool_DefaultPassive_ExcludesNegativeTaggedItems()
        {
            cfg.RewardSlot slot = _tables.TbRewardSlot.Get("slot_specific_passive");
            Assert.NotNull(slot);

            List<RewardChoice> choices = RewardPoolService.RollChoices(Context(), slot);
            Assert.IsNotEmpty(choices);
            foreach (RewardChoice choice in choices)
            {
                ItemDefinition item = ItemDefinition.Get(_tables, choice.Id, cfg.ItemKind.Passive);
                Assert.NotNull(item, choice.Id);
                Assert.IsFalse(item.IsNegative, $"奖励池 pool_passive 不应出现负面道具：{choice.Id}");
            }
        }
    }
}
