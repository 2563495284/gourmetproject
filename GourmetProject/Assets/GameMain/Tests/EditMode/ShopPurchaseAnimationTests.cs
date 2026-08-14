using GourmetProject.Game.UI.Battle.Pages;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopPurchaseAnimationTests
    {
        [Test]
        public void Play_ConsumesPreparedAnimationExactlyOnce()
        {
            int playCount = 0;
            int cancelCount = 0;
            var animation = new PreparedShopPurchaseAnimation(
                () => playCount++,
                () => cancelCount++);

            animation.Play();
            animation.Play();
            animation.Cancel();

            Assert.That(playCount, Is.EqualTo(1));
            Assert.That(cancelCount, Is.Zero);
        }

        [Test]
        public void Cancel_ReleasesPreparedAnimationExactlyOnce()
        {
            int playCount = 0;
            int cancelCount = 0;
            var animation = new PreparedShopPurchaseAnimation(
                () => playCount++,
                () => cancelCount++);

            animation.Cancel();
            animation.Cancel();
            animation.Play();

            Assert.That(playCount, Is.Zero);
            Assert.That(cancelCount, Is.EqualTo(1));
        }
    }
}
