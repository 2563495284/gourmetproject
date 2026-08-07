using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class ServeTriggerCueQueuePlayModeTests
    {
        [UnityTest]
        public IEnumerator BossAndPassiveCues_PlayInFifoOrderWithoutOverlappingWindows()
        {
            var root = new GameObject("ServeTriggerCueQueueTest");
            BattleWorldController controller = root.AddComponent<BattleWorldController>();
            var sources = new List<string>();
            var startTimes = new List<float>();
            bool completed = false;
            Exception failure = null;

            SetPrivateField(
                controller,
                "_serveTriggerCueSink",
                new Action<ServeTriggerCue>(cue =>
                {
                    sources.Add(cue.SourceId);
                    startTimes.Add(Time.unscaledTime);
                }));

            Enqueue(controller, Cue(ServeCueSourceKind.BossDebuff, "boss_first"));
            Enqueue(controller, Cue(ServeCueSourceKind.PassiveItem, "passive_second"));

            Assert.That(sources, Is.Empty,
                "Cue 入队时不应提前触发来源脉冲，所有表现必须由串行播放器启动。");
            _ = RunAsync();

            float timeoutAt = Time.realtimeSinceStartup + 4f;
            while (!completed && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            try
            {
                Assert.That(failure, Is.Null);
                Assert.That(completed, Is.True, "Cue 队列未在预期时间内播放完成。");
                CollectionAssert.AreEqual(
                    new[] { "boss_first", "passive_second" },
                    sources);
                Assert.That(startTimes, Has.Count.EqualTo(2));
                Assert.That(startTimes[1] - startTimes[0], Is.GreaterThanOrEqualTo(0.84f),
                    "下一条 Cue 必须等待上一条浮字与食物反馈的完整播放窗口结束。");
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }

            async Awaitable RunAsync()
            {
                try
                {
                    await controller.PlayPendingServeTriggerCuesAsync(
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    completed = true;
                }
            }
        }

        private static ServeTriggerCue Cue(
            ServeCueSourceKind sourceKind,
            string sourceId)
        {
            return new ServeTriggerCue(
                sourceKind,
                sourceId,
                sourceId,
                dishId: 1,
                ServeCueEffectKind.MultiplierFactor,
                1.5f,
                "×1.5",
                ServeCuePresentationKind.Gain);
        }

        private static void Enqueue(
            BattleWorldController controller,
            ServeTriggerCue cue)
        {
            MethodInfo method = typeof(BattleWorldController).GetMethod(
                "OnServeTriggerCueRaised",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, new object[] { cue });
        }

        private static void SetPrivateField<T>(
            object target,
            string name,
            T value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
