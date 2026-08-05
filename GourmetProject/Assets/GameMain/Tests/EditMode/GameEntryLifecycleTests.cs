using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace GourmetProject.Tests.EditMode
{
    public sealed class GameEntryLifecycleTests
    {
        private static readonly MethodInfo ResetComponentsMethod = typeof(GameEntry).GetMethod(
            "ResetComponents",
            BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject _firstObject;
        private GameObject _secondObject;

        [SetUp]
        public void SetUp()
        {
            Assert.That(ResetComponentsMethod, Is.Not.Null);
            ResetComponentsMethod.Invoke(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_firstObject != null)
            {
                Object.DestroyImmediate(_firstObject);
            }

            if (_secondObject != null)
            {
                Object.DestroyImmediate(_secondObject);
            }

            ResetComponentsMethod.Invoke(null, null);
        }

        [Test]
        public void SubsystemRegistrationReset_RemovesDestroyedComponentReference()
        {
            _firstObject = new GameObject("First Game Framework Component");
            var first = _firstObject.AddComponent<TestGameFrameworkComponent>();
            first.RegisterForTest();

            Object.DestroyImmediate(_firstObject);
            _firstObject = null;

            TestGameFrameworkComponent retained = GameEntry.GetComponent<TestGameFrameworkComponent>();
            Assert.That(object.ReferenceEquals(retained, null), Is.False);
            Assert.That(retained == null, Is.True);

            ResetComponentsMethod.Invoke(null, null);

            _secondObject = new GameObject("Second Game Framework Component");
            var second = _secondObject.AddComponent<TestGameFrameworkComponent>();
            second.RegisterForTest();

            Assert.That(GameEntry.GetComponent<TestGameFrameworkComponent>(), Is.SameAs(second));
        }

        private sealed class TestGameFrameworkComponent : GameFrameworkComponent
        {
            public void RegisterForTest()
            {
                base.Awake();
            }
        }
    }
}
