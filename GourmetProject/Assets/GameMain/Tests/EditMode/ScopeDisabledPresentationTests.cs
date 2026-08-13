using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ScopeDisabledPresentationTests
    {
        [Test]
        public void CanDisplayScopeForDish_NormalDish_ReturnsTrue()
        {
            Assert.That(
                BattleScopeHighlightController.CanDisplayScopeForDish(CreateDish()),
                Is.True);
        }

        [Test]
        public void CanDisplayScopeForDish_SkillsDisabled_ReturnsFalse()
        {
            DishInstance dish = CreateDish();
            dish.DisableSkills();

            Assert.That(
                BattleScopeHighlightController.CanDisplayScopeForDish(dish),
                Is.False);
        }

        [Test]
        public void CanDisplayScopeForDish_ExcludedFromScore_ReturnsFalse()
        {
            DishInstance dish = CreateDish();
            dish.ExcludeFromScore();

            Assert.That(
                BattleScopeHighlightController.CanDisplayScopeForDish(dish),
                Is.False);
        }

        [Test]
        public void CanDisplayScopeForDish_Null_ReturnsFalse()
        {
            Assert.That(
                BattleScopeHighlightController.CanDisplayScopeForDish(null),
                Is.False);
        }

        private static DishInstance CreateDish()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var definition = new DishDef(
                "scope_test_dish",
                "Scope Test Dish",
                1,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            return new DishInstance(
                1,
                definition,
                new Placement(shape, 0, new GridPos(0, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}
