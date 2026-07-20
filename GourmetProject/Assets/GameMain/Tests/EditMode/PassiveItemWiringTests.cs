using System.IO;
using System.Reflection;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassiveItemWiringTests
    {
        private cfg.Tables _tables;
        private GameRun _run;
        private GameObject _host;

        [SetUp]
        public void SetUp()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(dir, name + ".json"))));
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(_tables);
            string characterId = _tables.TbCharacter.DataList[0].Id;
            _run = new GameRun(_tables, db, characterId, "passive-item-wiring-test-seed", weekIndex: 1);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void EveryConfiguredPassiveItem_HasRegisteredModel()
        {
            foreach (cfg.PassiveItem item in _tables.TbPassiveItem.DataList)
            {
                Assert.IsTrue(PassiveItemModelRegistry.HasModel(item.Id), $"Missing passive item model registration: {item.Id}");
            }
        }

        [Test]
        public void GrantRecipeBookFromPassive_DoesNotRequireRecipeViewShown()
        {
            _host = new GameObject("BattleForm_PassiveItemWiringTests");
            BattleForm form = _host.AddComponent<BattleForm>();

            FieldInfo runField = typeof(BattleForm).GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(runField);
            runField.SetValue(form, _run);

            int before = _run.RecipeBookCount;
            Assert.Less(before, _run.RecipeBookMaxCount);

            using (RunPersistence.SuppressSave())
            {
                Assert.IsTrue(form.TryGrantRecipeBookFromPassive("Recipe Ticket"));
            }

            Assert.AreEqual(before + 1, _run.RecipeBookCount);
        }
    }
}
