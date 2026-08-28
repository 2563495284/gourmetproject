#if UNITY_EDITOR
using System.Reflection;
using GourmetProject.Game.DevConsole;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DevConsoleBehaviourTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;

        private static readonly FieldInfo ConsoleField =
            typeof(DevConsoleBehaviour).GetField("_console", InstanceFlags);

        private static readonly FieldInfo InstanceField =
            typeof(DevConsoleBehaviour).GetField("_instance", StaticFlags);

        private static readonly MethodInfo OnEnableMethod =
            typeof(DevConsoleBehaviour).GetMethod("OnEnable", InstanceFlags);

        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            InstanceField.SetValue(null, null);
            _gameObject = new GameObject("DevConsoleBehaviourTests");
        }

        [TearDown]
        public void TearDown()
        {
            InstanceField.SetValue(null, null);
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void OnEnable_AfterDomainReload_RestoresRuntimeState()
        {
            var behaviour = _gameObject.AddComponent<DevConsoleBehaviour>();
            ConsoleField.SetValue(behaviour, null);
            InstanceField.SetValue(null, null);

            OnEnableMethod.Invoke(behaviour, null);

            Assert.That(ConsoleField.GetValue(behaviour), Is.Not.Null);
            Assert.That(InstanceField.GetValue(null), Is.SameAs(behaviour));
        }
    }
}
#endif
