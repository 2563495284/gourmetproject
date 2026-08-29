using System;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.UI.Hud;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ServingOutletShortcutTests
    {
        private const string PrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/Hud/ServingOutlet.prefab";
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly MethodInfo HandleKeyboardShortcutMethod =
            typeof(ServingOutletView).GetMethod("HandleKeyboardShortcut", InstanceFlags);
        private static readonly MethodInfo CurrentDishAlphaMethod =
            typeof(ServingOutletView).GetMethod("CurrentDishAlpha", InstanceFlags);
        private static readonly FieldInfo StateField =
            typeof(ServingOutletView).GetField("<State>k__BackingField", InstanceFlags);
        private static readonly FieldInfo PreparedDishIdField =
            typeof(ServingOutletView).GetField("_preparedDishId", InstanceFlags);
        private static readonly FieldInfo KeyboardDraggingField =
            typeof(ServingOutletView).GetField("_keyboardDragging", InstanceFlags);

        private GameObject _instance;
        private ServingOutletView _view;

        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            _instance = UnityEngine.Object.Instantiate(prefab);
            _view = _instance.GetComponent<ServingOutletView>();
            Assert.That(_view, Is.Not.Null);
            Assert.That(HandleKeyboardShortcutMethod, Is.Not.Null);
            Assert.That(CurrentDishAlphaMethod, Is.Not.Null);
            Assert.That(StateField, Is.Not.Null);
            Assert.That(PreparedDishIdField, Is.Not.Null);
            Assert.That(KeyboardDraggingField, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
            }
        }

        [Test]
        public void Shortcut_FirstPressPicksUp_TracksPointer_SecondPressDrops()
        {
            bool active = false;
            int beginCount = 0;
            int endCount = 0;
            Vector2 beginPoint = default;
            Vector2 endPoint = default;
            var updates = new List<Vector2>();
            Bind(
                point =>
                {
                    beginCount++;
                    beginPoint = point;
                    active = true;
                    return true;
                },
                updates.Add,
                point =>
                {
                    endCount++;
                    endPoint = point;
                    active = false;
                    return true;
                },
                () => active);
            SetReadyForShortcut();

            Vector2 pickup = new Vector2(120f, 240f);
            Vector2 follow = new Vector2(360f, 420f);
            Vector2 drop = new Vector2(520f, 310f);
            HandleShortcut(pressed: true, pickup);
            HandleShortcut(pressed: false, follow);
            HandleShortcut(pressed: true, drop);

            Assert.That(beginCount, Is.EqualTo(1));
            Assert.That(beginPoint, Is.EqualTo(pickup));
            Assert.That(updates, Is.EqualTo(new[] { follow, drop }));
            Assert.That(endCount, Is.EqualTo(1));
            Assert.That(endPoint, Is.EqualTo(drop));
            Assert.That(IsKeyboardDragging(), Is.False);
        }

        [Test]
        public void Shortcut_LeftClickAfterPickup_DropsAtPointer()
        {
            bool active = false;
            int endCount = 0;
            Vector2 endPoint = default;
            var updates = new List<Vector2>();
            Bind(
                _ =>
                {
                    active = true;
                    return true;
                },
                updates.Add,
                point =>
                {
                    endCount++;
                    endPoint = point;
                    active = false;
                    return true;
                },
                () => active);
            SetReadyForShortcut();

            Vector2 pickup = new Vector2(120f, 240f);
            Vector2 drop = new Vector2(520f, 310f);
            HandleShortcut(pressed: true, pickup);
            HandleShortcut(pressed: false, drop, primaryPressed: true);

            Assert.That(updates, Is.EqualTo(new[] { drop }));
            Assert.That(endCount, Is.EqualTo(1));
            Assert.That(endPoint, Is.EqualTo(drop));
            Assert.That(IsKeyboardDragging(), Is.False);
        }

        [Test]
        public void Shortcut_RejectedPickup_RemainsIdle()
        {
            int updateCount = 0;
            int endCount = 0;
            Bind(
                _ => false,
                _ => updateCount++,
                _ =>
                {
                    endCount++;
                    return false;
                },
                () => false);
            SetReadyForShortcut();

            HandleShortcut(pressed: true, new Vector2(100f, 100f));
            HandleShortcut(pressed: false, new Vector2(200f, 200f));

            Assert.That(updateCount, Is.Zero);
            Assert.That(endCount, Is.Zero);
            Assert.That(IsKeyboardDragging(), Is.False);
        }

        [Test]
        public void Shortcut_InvalidDrop_ReturnsToOutletAndCanBePickedUpAgain()
        {
            bool active = false;
            int beginCount = 0;
            Bind(
                _ =>
                {
                    beginCount++;
                    active = true;
                    return true;
                },
                _ => { },
                _ =>
                {
                    active = false;
                    return false;
                },
                () => active);
            SetReadyForShortcut();

            HandleShortcut(pressed: true, new Vector2(100f, 100f));
            Assert.That(CurrentDishAlpha(), Is.EqualTo(0.35f).Within(0.001f));

            HandleShortcut(pressed: true, new Vector2(200f, 200f));
            Assert.That(IsKeyboardDragging(), Is.False);
            Assert.That(CurrentDishAlpha(), Is.EqualTo(1f).Within(0.001f));

            HandleShortcut(pressed: true, new Vector2(300f, 300f));
            Assert.That(beginCount, Is.EqualTo(2));
            Assert.That(IsKeyboardDragging(), Is.True);
        }

        [Test]
        public void Shortcut_ExternalCancellation_RestoresOutletPresentation()
        {
            bool active = false;
            Bind(
                _ =>
                {
                    active = true;
                    return true;
                },
                _ => { },
                _ => true,
                () => active);
            SetReadyForShortcut();

            HandleShortcut(pressed: true, new Vector2(100f, 100f));
            active = false;
            HandleShortcut(pressed: false, new Vector2(200f, 200f));

            Assert.That(IsKeyboardDragging(), Is.False);
            Assert.That(CurrentDishAlpha(), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Shortcut_RebindWhileCarrying_PreservesDragLifecycle()
        {
            bool active = false;
            int beginCount = 0;
            int endCount = 0;
            var updates = new List<Vector2>();
            Func<Vector2, bool> begin = _ =>
            {
                beginCount++;
                active = true;
                return true;
            };
            Action<Vector2> update = updates.Add;
            Func<Vector2, bool> end = _ =>
            {
                endCount++;
                active = false;
                return true;
            };
            Func<bool> isActive = () => active;
            Bind(begin, update, end, isActive);
            SetReadyForShortcut();

            HandleShortcut(pressed: true, new Vector2(100f, 100f));
            Bind(begin, update, end, isActive);

            Vector2 follow = new Vector2(220f, 260f);
            Vector2 drop = new Vector2(340f, 380f);
            HandleShortcut(pressed: false, follow);
            HandleShortcut(pressed: true, drop);

            Assert.That(beginCount, Is.EqualTo(1));
            Assert.That(updates, Is.EqualTo(new[] { follow, drop }));
            Assert.That(endCount, Is.EqualTo(1));
            Assert.That(IsKeyboardDragging(), Is.False);
        }

        private void Bind(
            Func<Vector2, bool> begin,
            Action<Vector2> update,
            Func<Vector2, bool> end,
            Func<bool> isActive)
        {
            _view.Bind(
                null,
                () => true,
                () => { },
                begin,
                update,
                end,
                isActive);
        }

        private void SetReadyForShortcut()
        {
            StateField.SetValue(_view, ServingOutletState.WaitingForDishDrag);
            PreparedDishIdField.SetValue(_view, (int?)42);
        }

        private void HandleShortcut(
            bool pressed,
            Vector2 screenPoint,
            bool primaryPressed = false)
        {
            HandleKeyboardShortcutMethod.Invoke(
                _view,
                new object[] { pressed, primaryPressed, screenPoint });
        }

        private bool IsKeyboardDragging()
        {
            return (bool)KeyboardDraggingField.GetValue(_view);
        }

        private float CurrentDishAlpha()
        {
            return (float)CurrentDishAlphaMethod.Invoke(_view, null);
        }
    }
}
