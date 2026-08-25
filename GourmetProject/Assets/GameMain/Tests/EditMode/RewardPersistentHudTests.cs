#if UNITY_EDITOR
using GourmetProject.Game.UI.Battle;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardPersistentHudTests
    {
        [Test]
        public void OpeningReward_RestoresHudFrameAndBothSideColumns()
        {
            var hudFrame = new GameObject("HudFrame");
            var leftColumn = new GameObject("LeftColumn");
            var rightColumn = new GameObject("RightColumn");
            leftColumn.transform.SetParent(hudFrame.transform);
            rightColumn.transform.SetParent(hudFrame.transform);
            leftColumn.SetActive(false);
            rightColumn.SetActive(false);
            hudFrame.SetActive(false);

            try
            {
                BattleForm.EnsurePersistentRewardHudVisible(hudFrame, leftColumn, rightColumn);

                Assert.That(hudFrame.activeSelf, Is.True);
                Assert.That(leftColumn.activeSelf, Is.True);
                Assert.That(rightColumn.activeSelf, Is.True);
                Assert.That(leftColumn.activeInHierarchy, Is.True);
                Assert.That(rightColumn.activeInHierarchy, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(hudFrame);
            }
        }
    }
}
#endif
