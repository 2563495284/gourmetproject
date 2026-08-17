using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleFoodActionBarDoodleExitTests
    {
        [Test]
        public void ShouldExit_IgnoresNullClick()
        {
            var tools = new GameObject("DoodleTools");
            try
            {
                Assert.That(
                    BattleFoodActionBar.ShouldExitDoodleToolOnUiClick(null, tools.transform),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(tools);
            }
        }

        [Test]
        public void ShouldExit_IgnoresDrawEraseClearAndHide()
        {
            var tools = new GameObject("DoodleTools");
            var draw = new GameObject("DoodleDrawButton");
            var erase = new GameObject("DoodleEraseButton");
            var clear = new GameObject("DoodleClearButton");
            var hide = new GameObject("DoodleToggleButton");
            draw.transform.SetParent(tools.transform, false);
            erase.transform.SetParent(tools.transform, false);
            clear.transform.SetParent(tools.transform, false);
            hide.transform.SetParent(tools.transform, false);

            try
            {
                Assert.That(
                    BattleFoodActionBar.ShouldExitDoodleToolOnUiClick(draw, tools.transform),
                    Is.False);
                Assert.That(
                    BattleFoodActionBar.ShouldExitDoodleToolOnUiClick(erase, tools.transform),
                    Is.False);
                Assert.That(
                    BattleFoodActionBar.ShouldExitDoodleToolOnUiClick(clear, tools.transform),
                    Is.False);
                Assert.That(
                    BattleFoodActionBar.ShouldExitDoodleToolOnUiClick(hide, tools.transform),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(tools);
            }
        }

        [Test]
        public void ShouldExit_WhenOtherButtonClicked()
        {
            var tools = new GameObject("DoodleTools");
            var eat = new GameObject("EatButton");
            try
            {
                Assert.That(
                    BattleFoodActionBar.ShouldExitDoodleToolOnUiClick(eat, tools.transform),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(tools);
                Object.DestroyImmediate(eat);
            }
        }
    }
}
