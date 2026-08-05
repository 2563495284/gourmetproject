using System.Reflection;
using System.Runtime.Serialization;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Run;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class GameplayStaticStateLifecycleTests
    {
        [TearDown]
        public void TearDown()
        {
            InvokeReset(typeof(GameRunContext));
            InvokeReset(typeof(GameplayEntryRequest));
            InvokeReset(typeof(GameplayFlowSignal));
        }

        [Test]
        public void SubsystemRegistrationReset_ClearsStateRetainedWithoutDomainReload()
        {
#pragma warning disable SYSLIB0050
            var staleRun = (GameRun)FormatterServices.GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050
            GameRunContext.Set(staleRun);
            GameplayEntryRequest.RequestNewRun("stale-character");
            GameplayFlowSignal.RequestReturnToMenu();

            InvokeReset(typeof(GameRunContext));
            InvokeReset(typeof(GameplayEntryRequest));
            InvokeReset(typeof(GameplayFlowSignal));

            Assert.That(GameRunContext.HasRun, Is.False);
            Assert.That(GameplayEntryRequest.Pending, Is.False);
            Assert.That(GameplayEntryRequest.CharacterId, Is.Null);
            Assert.That(GameplayFlowSignal.ReturnToMenuRequested, Is.False);
        }

        private static void InvokeReset(System.Type type)
        {
            MethodInfo method = type.GetMethod(
                "ResetStatics",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, $"{type.Name} must reset its state at subsystem registration.");
            method.Invoke(null, null);
        }
    }
}
