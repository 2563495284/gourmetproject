using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using Luban.SimpleJSON;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class GoldOnTransferCountModelTests
    {
        [Test]
        public void EveryTenTransfers_GrantsGoldAndStartsNextCycle()
        {
            (GameRun run, GoldOnTransferCountModel model) = CreateBoundModel();

            Trigger(model, 9);
            Assert.That(run.Gold, Is.Zero);
            Assert.That(model.InfoText, Is.EqualTo("9"));

            Trigger(model, 1);
            Assert.That(run.Gold, Is.EqualTo(10));
            Assert.That(model.InfoText, Is.EqualTo("0"));

            Trigger(model, 20);
            Assert.That(run.Gold, Is.EqualTo(30));
            Assert.That(model.InfoText, Is.EqualTo("0"));
            Assert.That(model.CaptureState(), Is.EqualTo("count:0"));
        }

        [Test]
        public void Restore_OldRewardedState_DoesNotPaySameCycleTwice()
        {
            (GameRun run, GoldOnTransferCountModel model) = CreateBoundModel();
            model.RestoreState("used:1;count:10;rewarded:1");

            Assert.That(model.InfoText, Is.EqualTo("0"));
            Trigger(model, 9);
            Assert.That(run.Gold, Is.Zero);

            Trigger(model, 1);
            Assert.That(run.Gold, Is.EqualTo(10));
        }

        private static void Trigger(GoldOnTransferCountModel model, int count)
        {
            for (int i = 0; i < count; i++)
            {
                model.OnSweetTransferTriggered(new SweetTransferOccurrence(1, 2));
            }
        }

        private static (GameRun Run, GoldOnTransferCountModel Model) CreateBoundModel()
        {
#pragma warning disable SYSLIB0050
            var run = (GameRun)FormatterServices.GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050
            var state = new RunItemState("item_gold_on_transfer", 1);
            SetPrivateField(run, "_items", new List<RunItemState> { state });

            const string json = "{"
                + "\"id\":\"item_gold_on_transfer\","
                + "\"name\":\"黄铜传菜铃\","
                + "\"desc\":\"累计甜蜜传递10次后\\n获得10金币\","
                + "\"quality\":1,"
                + "\"specialTags\":0,"
                + "\"effectValue\":10,"
                + "\"effectParam\":\"count:10\","
                + "\"baseWeight\":100,"
                + "\"hiddenRange\":{\"min\":20,\"max\":80},"
                + "\"targetScoreHiddenOffset\":0,"
                + "\"dishHiddenOffset\":0,"
                + "\"passiveItemHiddenOffset\":0,"
                + "\"fragmentHiddenOffset\":0,"
                + "\"termId\":\"term_sweet_transfer\","
                + "\"price\":80} ";
            ItemDefinition definition = ItemDefinition.From(new cfg.PassiveItem(JSON.Parse(json)));
            var model = new GoldOnTransferCountModel();
            state.Model = model;
            model.Bind(run, definition, state);
            return (run, model);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}
